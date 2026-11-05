using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a system's own menu and loupe take from managed code. Below the versions they need, they say so, and Lettera's own show.</summary>
    internal interface ILetteraSystemUi
    {
        bool MenuSupported { get; }
        bool LoupeSupported { get; }
        void ShowMenu(Rect avoid, string[] titles, bool[] enabled);
        void HideMenu();

        /// <summary>An entry, -1 when the menu went away, -2 for nothing yet.</summary>
        int NextMenuChoice();
        void ShowLoupe(Vector2 point);
        void HideLoupe();
    }

    /// <summary>The system's loupe in place of Lettera's mesh one.</summary>
    internal sealed class LetteraSystemLoupe : ILetteraLoupe
    {
        readonly ILetteraSystemUi m_Ui;

        internal LetteraSystemLoupe(ILetteraSystemUi ui) => m_Ui = ui;

        public void Show(LetteraSelectable owner) { }
        public void Move(Vector2 screenPoint, Rect lineOnScreen) => m_Ui.ShowLoupe(screenPoint);
        public void Hide() => m_Ui.HideLoupe();
    }

    /// <summary>This platform's menu and loupe, when it has them and they are on.</summary>
    internal static class LetteraSystemUi
    {
        /// <summary>Replaces the platform's for tests.</summary>
        internal static ILetteraSystemUi Override;

        internal static ILetteraSystemUi Current
        {
            get
            {
                if (Override != null) return Override;
#if UNITY_ANDROID && !UNITY_EDITOR
                return LetteraAndroidUi.Instance;
#elif UNITY_IOS && !UNITY_EDITOR
                return LetteraIosUi.Instance;
#else
                return null;
#endif
            }
        }

        /// <summary>The menu to use before the project's own and the built-in one, or null.</summary>
        internal static LetteraMenuPresenter Menu(LetteraMenuOverride android = LetteraMenuOverride.Project, LetteraMenuOverride ios = LetteraMenuOverride.Project)
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.SystemMenu)) return null;
            return WantsSystemMenu(android, ios) ? LetteraSystemMenu.Resolve(Current) : null;
        }

        /// <summary>Whether a text's choice, or the project's where it has none, is the system's menu on this platform.</summary>
        internal static bool WantsSystemMenu(LetteraMenuOverride android, LetteraMenuOverride ios)
        {
            LetteraEditingSettings settings = LetteraSettings.Instance.Editing;
            bool apple = LetteraPlatform.Kind == LetteraPlatformKind.IOS;
            LetteraMenuOverride own = apple ? ios : android;
            if (own != LetteraMenuOverride.Project) return own == LetteraMenuOverride.System;
            return (apple ? settings.IosMenu : settings.AndroidMenu) == LetteraMenuChoice.System;
        }

        /// <summary>Whether a text's choice, or the project's where it has none, is the system's loupe.</summary>
        internal static bool WantsSystemLoupe(LetteraLoupeOverride own) =>
            own != LetteraLoupeOverride.Project ? own == LetteraLoupeOverride.System : LetteraSettings.Instance.Editing.Loupe == LetteraLoupeChoice.System;

        /// <summary>The loupe to use before Lettera's, or null.</summary>
        internal static ILetteraLoupe Loupe(LetteraLoupeOverride own = LetteraLoupeOverride.Project)
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.SystemLoupe) || !WantsSystemLoupe(own)) return null;
            ILetteraSystemUi ui = Current;
            return ui != null && ui.LoupeSupported ? new LetteraSystemLoupe(ui) : null;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal static class LetteraAndroid
    {
        static AndroidJavaClass s_Bridge;
        internal static AndroidJavaClass Bridge => s_Bridge ?? (s_Bridge = new AndroidJavaClass("com.firstsymbol.lettera.LetteraBridge"));
        internal static AndroidJavaObject Activity => UnityEngine.Android.AndroidApplication.currentActivity;
    }

    internal sealed class LetteraAndroidHost : ILetteraNativeHost
    {
        public bool IsAvailable
        {
            get
            {
                try
                {
                    return LetteraAndroid.Bridge != null && LetteraAndroid.Activity != null;
                }
                catch (AndroidJavaException)
                {
                    return false;
                }
            }
        }

        public void Open(in LetteraMirrorState state, in LetteraKeyboardTraits traits, LetteraHostMode mode) =>
            LetteraAndroid.Bridge.CallStatic("open", LetteraAndroid.Activity, state.Text, state.SelectionStart, state.SelectionEnd, state.Revision, LetteraHostLines.AndroidInputType(traits), LetteraHostLines.AndroidImeOptions(traits), traits.AutofillHint ?? "");

        public void Push(in LetteraMirrorState state) =>
            LetteraAndroid.Bridge.CallStatic("push", LetteraAndroid.Activity, state.Text, state.SelectionStart, state.SelectionEnd, state.Revision);

        public void Place(Rect caret, Rect field) =>
            LetteraAndroid.Bridge.CallStatic("place", LetteraAndroid.Activity, Mathf.RoundToInt(caret.x), Mathf.RoundToInt(Screen.height - caret.yMax));

        public void Reconfigure(in LetteraKeyboardTraits traits) { }
        public void Close() => LetteraAndroid.Bridge.CallStatic("close", LetteraAndroid.Activity);

        public int Drain(LetteraHostEvent[] into)
        {
            int count = 0;
            while (count < into.Length)
            {
                string line = LetteraAndroid.Bridge.CallStatic<string>("next");
                if (line == null) break;
                if (LetteraHostLines.TryParse(line, out LetteraHostEvent e)) into[count++] = e;
            }
            return count;
        }
    }

    internal sealed class LetteraAndroidClipboard : ILetteraClipboardHost
    {
        public bool TryWrite(IReadOnlyList<LetteraClipItem> items)
        {
            string plain = "", html = "";
            foreach (LetteraClipItem item in items)
            {
                if (item.Format == LetteraClipFormat.PlainText) plain = item.Text;
                else if (item.Format == LetteraClipFormat.Html) html = item.Text;
            }
            return LetteraAndroid.Bridge.CallStatic<bool>("clipWrite", LetteraAndroid.Activity, plain, html);
        }

        public bool TryRead(List<LetteraClipItem> items)
        {
            string html = LetteraAndroid.Bridge.CallStatic<string>("clipRead", LetteraAndroid.Activity, true);
            string plain = LetteraAndroid.Bridge.CallStatic<string>("clipRead", LetteraAndroid.Activity, false);
            if (!string.IsNullOrEmpty(html)) items.Add(new LetteraClipItem(LetteraClipFormat.Html, html));
            if (!string.IsNullOrEmpty(plain)) items.Add(new LetteraClipItem(LetteraClipFormat.PlainText, plain));
#if LETTERA_TRACE_MIRROR
            Debug.Log($"LETTERA_CLIP read html={html?.Length ?? -1} plain=[{plain}]");
#endif
            return true;
        }

        public LetteraClipPresence Probe() => (LetteraClipPresence)LetteraAndroid.Bridge.CallStatic<int>("clipProbe", LetteraAndroid.Activity);
    }

    internal sealed class LetteraAndroidUi : ILetteraSystemUi
    {
        internal static readonly LetteraAndroidUi Instance = new LetteraAndroidUi();
        static int Sdk => new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

        public bool MenuSupported => Sdk >= 23;
        public bool LoupeSupported => Sdk >= 28;

        public void ShowMenu(Rect avoid, string[] titles, bool[] enabled) =>
            LetteraAndroid.Bridge.CallStatic<bool>("showMenu", LetteraAndroid.Activity, Mathf.RoundToInt(avoid.xMin), Mathf.RoundToInt(Screen.height - avoid.yMax), Mathf.RoundToInt(avoid.xMax), Mathf.RoundToInt(Screen.height - avoid.yMin), titles, enabled);

        public void HideMenu() => LetteraAndroid.Bridge.CallStatic("hideMenu", LetteraAndroid.Activity);
        public int NextMenuChoice() => LetteraAndroid.Bridge.CallStatic<int>("nextMenuChoice");
        public void ShowLoupe(Vector2 point) => LetteraAndroid.Bridge.CallStatic<bool>("loupeShow", LetteraAndroid.Activity, point.x, Screen.height - point.y);
        public void HideLoupe() => LetteraAndroid.Bridge.CallStatic("loupeHide", LetteraAndroid.Activity);
    }
#endif

#if UNITY_IOS && !UNITY_EDITOR
    internal static class LetteraIos
    {
        [DllImport("__Internal")] internal static extern int LetteraIos_Available();
        [DllImport("__Internal")] internal static extern void LetteraIos_Open(string text, int start, int end, int revision, int multiline, int secure, int kind, int returnKey, int autocapitalize, int autocorrect, int spellCheck, int appearance, int smartQuotes, int smartDashes, int smartInsert, int returnWhenEmpty, string autofill);
        [DllImport("__Internal")] internal static extern void LetteraIos_Push(string text, int start, int end, int revision);
        [DllImport("__Internal")] internal static extern void LetteraIos_Place(float x, float y, float width, float height);
        [DllImport("__Internal")] internal static extern void LetteraIos_Close();
        [DllImport("__Internal")] internal static extern int LetteraIos_Next(byte[] buffer, int size);
        [DllImport("__Internal")] internal static extern int LetteraIos_ClipWrite(string plain, string html, string own);
        [DllImport("__Internal")] internal static extern int LetteraIos_ClipProbe();
        [DllImport("__Internal")] internal static extern int LetteraIos_ClipRead(int which, byte[] buffer, int size);
        [DllImport("__Internal")] internal static extern int LetteraIos_MenuSupported();
        [DllImport("__Internal")] internal static extern void LetteraIos_ShowMenu(float x, float y, float width, float height, string titles, string enabled);
        [DllImport("__Internal")] internal static extern void LetteraIos_HideMenu();
        [DllImport("__Internal")] internal static extern int LetteraIos_NextMenuChoice();
        [DllImport("__Internal")] internal static extern int LetteraIos_LoupeSupported();
        [DllImport("__Internal")] internal static extern void LetteraIos_ShowLoupe(float x, float y);
        [DllImport("__Internal")] internal static extern void LetteraIos_HideLoupe();
    }

    internal sealed class LetteraIosHost : ILetteraNativeHost
    {
        byte[] m_Buffer = new byte[4096];

        public bool IsAvailable => LetteraIos.LetteraIos_Available() != 0;

        public void Open(in LetteraMirrorState state, in LetteraKeyboardTraits traits, LetteraHostMode mode) =>
            LetteraIos.LetteraIos_Open(state.Text, state.SelectionStart, state.SelectionEnd, state.Revision, traits.Multiline ? 1 : 0, traits.Secure ? 1 : 0, (int)traits.Kind, (int)traits.ReturnKey, (int)traits.AutoCapitalize, traits.Autocorrect ? 1 : 0, (int)traits.SpellCheck, (int)traits.IosAppearance, (int)traits.IosSmart.Quotes, (int)traits.IosSmart.Dashes, (int)traits.IosSmart.InsertDelete, (int)traits.IosReturnKeyWhenEmpty, traits.AutofillHint ?? "");

        public void Push(in LetteraMirrorState state) => LetteraIos.LetteraIos_Push(state.Text, state.SelectionStart, state.SelectionEnd, state.Revision);
        public void Place(Rect caret, Rect field) => LetteraIos.LetteraIos_Place(caret.x, Screen.height - caret.yMax, caret.width, caret.height);
        public void Reconfigure(in LetteraKeyboardTraits traits) { }
        public void Close() => LetteraIos.LetteraIos_Close();

        public int Drain(LetteraHostEvent[] into)
        {
            int count = 0;
            while (count < into.Length)
            {
                int length = LetteraIos.LetteraIos_Next(m_Buffer, m_Buffer.Length);
                if (length == 0) break;
                if (length < 0)
                {
                    // Too long for the buffer: it grows, and the line waits.
                    m_Buffer = new byte[-length * 2];
                    continue;
                }
                if (LetteraHostLines.TryParse(System.Text.Encoding.UTF8.GetString(m_Buffer, 0, length), out LetteraHostEvent e)) into[count++] = e;
            }
            return count;
        }
    }

    internal sealed class LetteraIosClipboard : ILetteraClipboardHost
    {
        public bool TryWrite(IReadOnlyList<LetteraClipItem> items)
        {
            string plain = "", html = "", own = "";
            foreach (LetteraClipItem item in items)
            {
                if (item.Format == LetteraClipFormat.PlainText) plain = item.Text;
                else if (item.Format == LetteraClipFormat.Html) html = item.Text;
                else if (item.Format == LetteraClipFormat.Lettera) own = item.Text;
            }
            return LetteraIos.LetteraIos_ClipWrite(plain, html, own) != 0;
        }

        public bool TryRead(List<LetteraClipItem> items)
        {
            Add(items, 2, LetteraClipFormat.Lettera);
            Add(items, 1, LetteraClipFormat.Html);
            Add(items, 0, LetteraClipFormat.PlainText);
            return true;
        }

        static void Add(List<LetteraClipItem> items, int which, string format)
        {
            var buffer = new byte[64 * 1024];
            int length = LetteraIos.LetteraIos_ClipRead(which, buffer, buffer.Length);
            if (length < 0)
            {
                buffer = new byte[-length];
                length = LetteraIos.LetteraIos_ClipRead(which, buffer, buffer.Length);
            }
            if (length > 0) items.Add(new LetteraClipItem(format, System.Text.Encoding.UTF8.GetString(buffer, 0, length)));
        }

        public LetteraClipPresence Probe() => (LetteraClipPresence)LetteraIos.LetteraIos_ClipProbe();
    }

    internal sealed class LetteraIosUi : ILetteraSystemUi
    {
        internal static readonly LetteraIosUi Instance = new LetteraIosUi();

        public bool MenuSupported => LetteraIos.LetteraIos_MenuSupported() != 0;
        public bool LoupeSupported => LetteraIos.LetteraIos_LoupeSupported() != 0;

        public void ShowMenu(Rect avoid, string[] titles, bool[] enabled)
        {
            var flags = new System.Text.StringBuilder(enabled.Length);
            foreach (bool on in enabled) flags.Append(on ? '1' : '0');
            LetteraIos.LetteraIos_ShowMenu(avoid.x, Screen.height - avoid.yMax, avoid.width, avoid.height, string.Join("\t", titles), flags.ToString());
        }

        public void HideMenu() => LetteraIos.LetteraIos_HideMenu();
        public int NextMenuChoice() => LetteraIos.LetteraIos_NextMenuChoice();
        public void ShowLoupe(Vector2 point) => LetteraIos.LetteraIos_ShowLoupe(point.x, Screen.height - point.y);
        public void HideLoupe() => LetteraIos.LetteraIos_HideLoupe();
    }
#endif

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
    // The system's pasteboard through the Objective-C runtime, with no library of ours. Every selector is checked first, and anything missing means plain text instead.
    internal sealed class LetteraMacClipboard : ILetteraClipboardHost
    {
        const string Objc = "/usr/lib/libobjc.A.dylib";
        [DllImport(Objc)] static extern IntPtr objc_getClass(string name);
        [DllImport(Objc)] static extern IntPtr sel_registerName(string name);
        [DllImport(Objc)] static extern bool class_respondsToSelector(IntPtr cls, IntPtr selector);
        [DllImport(Objc)] static extern IntPtr object_getClass(IntPtr instance);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr target, IntPtr selector);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr target, IntPtr selector, IntPtr a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern bool SendPtrPtr(IntPtr target, IntPtr selector, IntPtr a, IntPtr b);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendBytes(IntPtr target, IntPtr selector, byte[] bytes, UIntPtr length);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendCString(IntPtr target, IntPtr selector, byte[] utf8);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern UIntPtr SendLength(IntPtr target, IntPtr selector);

        static IntPtr Sel(string name) => sel_registerName(name);

        static bool Responds(IntPtr instanceOrClass, string selector) => instanceOrClass != IntPtr.Zero && class_respondsToSelector(object_getClass(instanceOrClass), Sel(selector));

        static IntPtr Text(string text) => SendCString(objc_getClass("NSString"), Sel("stringWithUTF8String:"), FontFace.NullTerminated(text));

        static IntPtr Pasteboard()
        {
            IntPtr cls = objc_getClass("NSPasteboard");
            return Responds(cls, "generalPasteboard") ? Send(cls, Sel("generalPasteboard")) : IntPtr.Zero;
        }

        static T InPool<T>(Func<T> body)
        {
            IntPtr pool = Send(Send(objc_getClass("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
            try
            {
                return body();
            }
            finally
            {
                Send(pool, Sel("drain"));
            }
        }

        static string TypeOf(string format)
        {
            switch (format)
            {
                case LetteraClipFormat.PlainText: return "public.utf8-plain-text";
                case LetteraClipFormat.Html: return "public.html";
                case LetteraClipFormat.Markdown: return "net.daringfireball.markdown";
                case LetteraClipFormat.Lettera: return "com.firstsymbol.lettera.text+json";
                case LetteraClipFormat.Url: return "public.url";
                case LetteraClipFormat.Png: return "public.png";
                default: return null;
            }
        }

        public bool TryWrite(IReadOnlyList<LetteraClipItem> items) => InPool(() =>
        {
            IntPtr board = Pasteboard();
            if (!Responds(board, "clearContents") || !Responds(board, "setData:forType:")) return false;
            Send(board, Sel("clearContents"));
            IntPtr dataClass = objc_getClass("NSData");
            foreach (LetteraClipItem item in items)
            {
                string type = TypeOf(item.Format);
                if (type == null) continue;
                IntPtr data = SendBytes(dataClass, Sel("dataWithBytes:length:"), item.Bytes, (UIntPtr)item.Bytes.Length);
                if (!SendPtrPtr(board, Sel("setData:forType:"), data, Text(type))) return false;
            }
            return true;
        });

        public bool TryRead(List<LetteraClipItem> items) => InPool(() =>
        {
            IntPtr board = Pasteboard();
            if (!Responds(board, "dataForType:")) return false;
            foreach (string format in new[] { LetteraClipFormat.Lettera, LetteraClipFormat.Html, LetteraClipFormat.Markdown, LetteraClipFormat.PlainText, LetteraClipFormat.Png })
            {
                IntPtr data = SendPtr(board, Sel("dataForType:"), Text(TypeOf(format)));
                if (data == IntPtr.Zero) continue;
                int length = (int)SendLength(data, Sel("length"));
                if (length <= 0 || (format == LetteraClipFormat.Png && length > LetteraSettings.Instance.Platform.PasteImageMaxBytes)) continue;
                var bytes = new byte[length];
                Marshal.Copy(Send(data, Sel("bytes")), bytes, 0, length);
                items.Add(new LetteraClipItem(format, bytes));
            }
            return true;
        });

        public LetteraClipPresence Probe() => LetteraClipPresence.Unknown;
    }
#endif
}
