using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using AOT;
using UnityEngine;
using UnityEngine.Scripting;

namespace FirstSymbol.Lettera
{
    /// <summary>What the procedure below reads from the system's input method, so that it can be tested without one.</summary>
    internal interface ILetteraImm
    {
        /// <summary>The string being composed.</summary>
        string CompositionText(IntPtr window);

        /// <summary>The caret inside it, in UTF-16 units.</summary>
        int CompositionCursor(IntPtr window);

        /// <summary>What it became.</summary>
        string ResultText(IntPtr window);

        /// <summary>One attribute per UTF-16 unit.</summary>
        byte[] Attributes(IntPtr window);

        /// <summary>Unit offsets where clauses start, ending with the length.</summary>
        int[] Clauses(IntPtr window);

        void PlaceCandidates(IntPtr window, Rect excludedInClientPixels);
        void Detach(IntPtr window, bool detached);
    }

    /// <summary>
    /// Composition on Windows through the system's input method messages, in a player. Keys and typed text stay Unity's; what this adds is several clauses, the caret inside them, and where the candidate list goes.
    /// In the editor Unity's own path stays, since a Game view shares the editor's window.
    /// </summary>
    internal sealed class LetteraImmInputSource : ILetteraInputSource
    {
        internal const int WM_IME_SETCONTEXT = 0x0281, WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F;
        internal const long ISC_SHOWUICOMPOSITIONWINDOW = 0x80000000L;
        internal const int GCS_COMPSTR = 0x0008, GCS_COMPATTR = 0x0010, GCS_COMPCLAUSE = 0x0020, GCS_CURSORPOS = 0x0080, GCS_RESULTSTR = 0x0800;

        delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        readonly ILetteraInputSource m_Keys;
        readonly ILetteraImm m_Imm;
        readonly Queue<Action<ILetteraInputSink>> m_Events = new Queue<Action<ILetteraInputSink>>();
        ILetteraInputSink m_Sink;
        ILetteraInputHost m_Host;
        Rect m_LastCaret;
        bool m_Secure, m_Detached;

        // The one session the procedure speaks to.
        static LetteraImmInputSource s_Active;

        // What the procedure does with a message, apart from the window: testable with no window at all.
        internal static Func<IntPtr, uint, IntPtr, IntPtr, IntPtr> ForwardOverride;

        static IntPtr s_Window, s_Original;
        static readonly WindowProcedure s_Procedure = Procedure;
        static IntPtr s_ProcedurePointer;

        internal LetteraImmInputSource(ILetteraInputSource keys, ILetteraImm imm)
        {
            m_Keys = keys;
            m_Imm = imm;
        }

        public void Attach(ILetteraInputSink sink, ILetteraInputHost host)
        {
            m_Sink = sink;
            m_Host = host;
            m_Keys.Attach(sink, host);
            s_Active = this;
            m_Secure = host.Traits.Secure;
            if (!Install())
            {
                LetteraPlatform.Report(LetteraNativeFeature.TextInput, "No window was found, so composition stays with Unity's own path.");
                return;
            }
            if (m_Secure)
            {
                m_Imm.Detach(s_Window, true);
                m_Detached = true;
            }
        }

        public void Detach()
        {
            if (m_Detached) m_Imm.Detach(s_Window, false);
            m_Detached = false;
            if (s_Active == this) s_Active = null;
            Uninstall();
            m_Keys.Detach();
            m_Events.Clear();
            m_Sink = null;
            m_Host = null;
        }

        public void Pump()
        {
            if (m_Sink == null) return;
            while (m_Events.Count > 0 && m_Sink != null) m_Events.Dequeue()(m_Sink);
            if (m_Sink == null) return;
            m_Keys.Pump();
            if (m_Host == null || s_Window == IntPtr.Zero) return;
            Rect caret = m_Host.CaretScreenRect;
            if (caret == m_LastCaret) return;
            m_LastCaret = caret;
            // Screen pixels from the bottom left, and the system counts client pixels from the top.
            m_Imm.PlaceCandidates(s_Window, new Rect(caret.x, Screen.height - caret.yMax, caret.width, caret.height));
        }

        // ------------------------------------------------------------- messages

        /// <summary>Handles a message the way the window's procedure does. True means it stops here.</summary>
        internal bool Handle(IntPtr window, uint message, ref IntPtr lParam)
        {
            switch (message)
            {
                case WM_IME_SETCONTEXT:
                    // The field draws the composition, so the system's own window stays away. The message still goes on.
                    lParam = (IntPtr)(lParam.ToInt64() & ~ISC_SHOWUICOMPOSITIONWINDOW);
                    return false;
                case WM_IME_STARTCOMPOSITION:
                    return true;
                case WM_IME_ENDCOMPOSITION:
                    m_Events.Enqueue(sink => sink.CompositionEnd(false));
                    return true;
                case WM_IME_COMPOSITION:
                    ReadComposition(window, lParam.ToInt64());
                    return true;
                default:
                    return false;
            }
        }

        void ReadComposition(IntPtr window, long flags)
        {
            if ((flags & GCS_RESULTSTR) != 0)
            {
                string result = m_Imm.ResultText(window) ?? "";
                // What it became takes the composition's place in one step.
                m_Events.Enqueue(sink =>
                {
                    sink.Composition(result, result.Length, null);
                    sink.CompositionEnd(true);
                });
            }
            if ((flags & GCS_COMPSTR) == 0) return;
            string text = m_Imm.CompositionText(window) ?? "";
            int cursor = (flags & GCS_CURSORPOS) != 0 ? m_Imm.CompositionCursor(window) : text.Length;
            LetteraClause[] clauses = BuildClauses(text, (flags & GCS_COMPATTR) != 0 ? m_Imm.Attributes(window) : null, (flags & GCS_COMPCLAUSE) != 0 ? m_Imm.Clauses(window) : null);
            m_Events.Enqueue(sink => sink.Composition(text, cursor, clauses));
        }

        /// <summary>The attributes per unit, as the field's clauses. A boundary list that makes no sense falls back to one clause.</summary>
        internal static LetteraClause[] BuildClauses(string text, byte[] attributes, int[] boundaries)
        {
            if (string.IsNullOrEmpty(text)) return null;
            bool usable = boundaries != null && boundaries.Length >= 2 && boundaries[0] == 0 && boundaries[boundaries.Length - 1] == text.Length;
            if (usable)
                for (int i = 1; i < boundaries.Length; i++)
                    usable &= boundaries[i] > boundaries[i - 1];
            if (!usable) boundaries = new[] { 0, text.Length };
            var clauses = new LetteraClause[boundaries.Length - 1];
            for (int i = 0; i + 1 < boundaries.Length; i++)
            {
                byte attribute = attributes != null && boundaries[i] < attributes.Length ? attributes[boundaries[i]] : (byte)0;
                clauses[i] = new LetteraClause { Length = boundaries[i + 1] - boundaries[i], Kind = KindOf(attribute) };
            }
            return clauses;
        }

        static LetteraClauseKind KindOf(byte attribute)
        {
            switch (attribute)
            {
                case 1: return LetteraClauseKind.ActiveConverted; // ATTR_TARGET_CONVERTED
                case 2: return LetteraClauseKind.Converted;       // ATTR_CONVERTED
                case 3: return LetteraClauseKind.ActiveRaw;       // ATTR_TARGET_NOTCONVERTED
                case 4: return LetteraClauseKind.Invalid;         // ATTR_INPUT_ERROR
                default: return LetteraClauseKind.Raw;            // ATTR_INPUT
            }
        }

        // ------------------------------------------------------------- the window

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        const int GWLP_WNDPROC = -4;

        static IntPtr FindPlayerWindow()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr window = IntPtr.Zero;
            while ((window = FindWindowExW(IntPtr.Zero, window, null, null)) != IntPtr.Zero)
            {
                GetWindowThreadProcessId(window, out uint process);
                if (process == self && IsWindowVisible(window) && GetWindow(window, 4) == IntPtr.Zero) return window;
            }
            return IntPtr.Zero;
        }

        static bool Install()
        {
            if (s_Original != IntPtr.Zero) return true;
            s_Window = FindPlayerWindow();
            if (s_Window == IntPtr.Zero) return false;
            if (s_ProcedurePointer == IntPtr.Zero) s_ProcedurePointer = Marshal.GetFunctionPointerForDelegate(s_Procedure);
            s_Original = SetWindowLongPtr(s_Window, GWLP_WNDPROC, s_ProcedurePointer);
            Application.quitting -= Uninstall;
            Application.quitting += Uninstall;
            return s_Original != IntPtr.Zero;
        }

        static void Uninstall()
        {
            if (s_Original == IntPtr.Zero) return;
            // Only if nothing has subclassed on top since: taking it back then would cut that one off.
            if (GetWindowLongPtr(s_Window, GWLP_WNDPROC) == s_ProcedurePointer) SetWindowLongPtr(s_Window, GWLP_WNDPROC, s_Original);
            s_Original = IntPtr.Zero;
        }

        static IntPtr Forward(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) => CallWindowProc(s_Original, window, message, wParam, lParam);

        /// <summary>The window this runs on, for a check in a player. Zero when not installed.</summary>
        internal static bool IsInstalledNow => s_Original != IntPtr.Zero && GetWindowLongPtr(s_Window, GWLP_WNDPROC) == s_ProcedurePointer;
#else
        static bool Install() => false;
        static void Uninstall() { }
        static IntPtr Forward(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) => IntPtr.Zero;
        internal static bool IsInstalledNow => false;
#endif

        [MonoPInvokeCallback(typeof(WindowProcedure)), Preserve]
        static IntPtr Procedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            LetteraImmInputSource active = s_Active;
            if (active != null)
            {
                try
                {
                    if (active.Handle(window, message, ref lParam)) return IntPtr.Zero;
                }
                catch (Exception e)
                {
                    // A throw must not cross into the system: this session goes back to Unity's path.
                    s_Active = null;
                    LetteraPlatform.MarkFailed(LetteraNativeFeature.TextInput, e.Message);
                }
            }
            return ForwardOverride != null ? ForwardOverride(window, message, wParam, lParam) : Forward(window, message, wParam, lParam);
        }

        /// <summary>The procedure itself, for a test.</summary>
        internal static IntPtr ProcedureForTests(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) => Procedure(window, message, wParam, lParam);

        internal static void MakeActiveForTests(LetteraImmInputSource source) => s_Active = source;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        sealed class ProofImm : ILetteraImm
        {
            public string Text = "\u65E5\u672C\u8A9E", Result = "\u65E5\u672C\u8A9E";
            public int Detaches, Places;
            public string CompositionText(IntPtr window) => Text;
            public int CompositionCursor(IntPtr window) => 2;
            public string ResultText(IntPtr window) => Result;
            public byte[] Attributes(IntPtr window) => new byte[] { 1, 1, 2 };
            public int[] Clauses(IntPtr window) => new[] { 0, 2, 3 };
            public void PlaceCandidates(IntPtr window, Rect excluded) => Places++;
            public void Detach(IntPtr window, bool detached) => Detaches++;
        }

        /// <summary>Runs the messages through the player's real window with a fake input method. "ok", or what went wrong. Called by a device check only.</summary>
        internal static string ProofInPlayer(LetteraEditable field)
        {
            var sink = (ILetteraInputSink)field.Selectable;
            var host = (ILetteraInputHost)field.Selectable;
            var imm = new ProofImm();
            string before = field.PlainText;
            field.Selectable.Activate();
            var keys = new LetteraUnityInputSource();
            var source = new LetteraImmInputSource(keys, imm);
            source.Attach(sink, host);
            if (!IsInstalledNow) return "not installed";
            IntPtr window = s_Window;
            IntPtr original = s_Original;
            SendMessage(window, WM_IME_STARTCOMPOSITION, IntPtr.Zero, IntPtr.Zero);
            SendMessage(window, WM_IME_COMPOSITION, IntPtr.Zero, (IntPtr)(GCS_COMPSTR | GCS_COMPATTR | GCS_COMPCLAUSE | GCS_CURSORPOS));
            source.Pump();
            if (!field.IsComposing) return "no composition";
            if (field.PlainText != before) return "composition changed the text";
            SendMessage(window, WM_IME_COMPOSITION, IntPtr.Zero, (IntPtr)GCS_RESULTSTR);
            SendMessage(window, WM_IME_ENDCOMPOSITION, IntPtr.Zero, IntPtr.Zero);
            source.Pump();
            if (field.IsComposing) return "still composing";
            if (field.PlainText != before + imm.Result) return "committed as " + field.PlainText;
            field.Undo();
            if (field.PlainText != before) return "undo left " + field.PlainText;
            source.Detach();
            if (s_Original != IntPtr.Zero || GetWindowLongPtr(window, GWLP_WNDPROC) != original) return "procedure not restored";
            for (int i = 0; i < 1000; i++)
            {
                var again = new LetteraImmInputSource(new LetteraUnityInputSource(), imm);
                again.Attach(sink, host);
                again.Detach();
            }
            if (GetWindowLongPtr(window, GWLP_WNDPROC) != original) return "drifted after 1000 sessions";
            return "ok";
        }
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    // The system's reads. Each runs inside a message, so nothing here may fail loudly.
    internal sealed class LetteraWindowsImm : ILetteraImm
    {
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct CandidateForm { public uint Index, Style; public Point Current; public NativeRect Area; }

        [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr window);
        [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode)] static extern int ImmGetCompositionStringW(IntPtr context, int index, byte[] buffer, int length);
        [DllImport("imm32.dll")] static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
        [DllImport("imm32.dll")] static extern bool ImmAssociateContextEx(IntPtr window, IntPtr context, uint flags);
        const uint CFS_EXCLUDE = 0x0080, IACE_CHILDREN = 1, IACE_DEFAULT = 16;

        static byte[] Read(IntPtr window, int index)
        {
            IntPtr context = ImmGetContext(window);
            if (context == IntPtr.Zero) return null;
            try
            {
                int length = ImmGetCompositionStringW(context, index, null, 0);
                if (length <= 0) return null;
                var buffer = new byte[length];
                ImmGetCompositionStringW(context, index, buffer, length);
                return buffer;
            }
            finally
            {
                ImmReleaseContext(window, context);
            }
        }

        public string CompositionText(IntPtr window)
        {
            byte[] data = Read(window, LetteraImmInputSource.GCS_COMPSTR);
            return data != null ? Encoding.Unicode.GetString(data) : "";
        }

        public string ResultText(IntPtr window)
        {
            byte[] data = Read(window, LetteraImmInputSource.GCS_RESULTSTR);
            return data != null ? Encoding.Unicode.GetString(data) : "";
        }

        public int CompositionCursor(IntPtr window)
        {
            IntPtr context = ImmGetContext(window);
            if (context == IntPtr.Zero) return 0;
            int cursor = ImmGetCompositionStringW(context, LetteraImmInputSource.GCS_CURSORPOS, null, 0);
            ImmReleaseContext(window, context);
            return Mathf.Max(cursor, 0);
        }

        public byte[] Attributes(IntPtr window) => Read(window, LetteraImmInputSource.GCS_COMPATTR);

        public int[] Clauses(IntPtr window)
        {
            byte[] data = Read(window, LetteraImmInputSource.GCS_COMPCLAUSE);
            if (data == null) return null;
            var clauses = new int[data.Length / 4];
            Buffer.BlockCopy(data, 0, clauses, 0, clauses.Length * 4);
            return clauses;
        }

        public void PlaceCandidates(IntPtr window, Rect excluded)
        {
            IntPtr context = ImmGetContext(window);
            if (context == IntPtr.Zero) return;
            var form = new CandidateForm
            {
                Index = 0,
                Style = CFS_EXCLUDE,
                Current = new Point { X = Mathf.RoundToInt(excluded.x), Y = Mathf.RoundToInt(excluded.yMax) },
                Area = new NativeRect { Left = Mathf.RoundToInt(excluded.x), Top = Mathf.RoundToInt(excluded.y), Right = Mathf.RoundToInt(excluded.xMax), Bottom = Mathf.RoundToInt(excluded.yMax) },
            };
            ImmSetCandidateWindow(context, ref form);
            ImmReleaseContext(window, context);
        }

        public void Detach(IntPtr window, bool detached)
        {
            ImmAssociateContextEx(window, IntPtr.Zero, detached ? IACE_CHILDREN : IACE_DEFAULT);
        }
    }
#endif
}
