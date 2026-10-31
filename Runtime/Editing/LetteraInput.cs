using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraClauseKind { Raw, ActiveRaw, Converted, ActiveConverted, Invalid }

    /// <summary>A part of a composition.</summary>
    public struct LetteraClause
    {
        public int Length;
        public LetteraClauseKind Kind;
    }

    /// <summary>Where an input source delivers what the user did.</summary>
    public interface ILetteraInputSink
    {
        void Key(LetteraKeyCode key, LetteraKeyModifiers modifiers, bool repeat = false);
        void Text(string text);

        /// <summary>The running composition. Clauses can be null, for one raw clause.</summary>
        void Composition(string text, int caret, LetteraClause[] clauses);

        /// <summary>The composition ends: commit puts it into the text, otherwise it goes away.</summary>
        void CompositionEnd(bool commit);

        /// <summary>A soft keyboard's action: submit, next, previous, newline, done, cancel.</summary>
        void Action(string verb);
    }

    /// <summary>What an input source can ask the text, to place a candidate window or to fill an OS text box.</summary>
    public interface ILetteraInputHost
    {
        LetteraSelectionRange Selection { get; }
        string CommittedPlainText { get; }

        /// <summary>The caret in screen pixels, from the bottom left.</summary>
        Rect CaretScreenRect { get; }
        LetteraKeyboardTraits Traits { get; }
    }

    /// <summary>A way for keys and text to reach a field. Later platforms add theirs through <see cref="LetteraInput.Register"/>.</summary>
    public interface ILetteraInputSource
    {
        void Attach(ILetteraInputSink sink, ILetteraInputHost host);
        void Detach();

        /// <summary>Called each frame while the text has the keyboard.</summary>
        void Pump();
    }

    /// <summary>Which input source a text gets when it takes the keyboard.</summary>
    public static class LetteraInput
    {
        static readonly List<(Func<ILetteraInputSource> create, int priority)> s_Sources = new List<(Func<ILetteraInputSource>, int)>();

        /// <summary>Higher priorities are asked first. A factory that returns null passes.</summary>
        public static void Register(Func<ILetteraInputSource> create, int priority)
        {
            s_Sources.Add((create, priority));
            s_Sources.Sort((a, b) => b.priority.CompareTo(a.priority));
        }

        public static void Unregister(Func<ILetteraInputSource> create) => s_Sources.RemoveAll(s => s.create == create);

        /// <summary>Tests set this so no OS input interferes.</summary>
        internal static bool NoDefaultSource;

        internal static ILetteraInputSource CreateSource()
        {
            foreach ((Func<ILetteraInputSource> create, int _) in s_Sources)
            {
                ILetteraInputSource source = create();
                if (source != null) return source;
            }
            if (NoDefaultSource) return null;
            ILetteraInputSource native = LetteraNativeHosts.Create();
            if (native != null) return native;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Application.isMobilePlatform || LetteraWebInputSource.LetteraInput_HasCoarsePointer() != 0) return new LetteraWebInputSource();
#endif
            if (Application.isMobilePlatform && TouchScreenKeyboard.isSupported) return new LetteraTouchKeyboardSource();
            return new LetteraUnityInputSource();
        }
    }

    /// <summary>Keys and composition as Unity queues them for UI, which works under either input backend.</summary>
    public sealed class LetteraUnityInputSource : ILetteraInputSource
    {
        readonly Event m_Event = new Event();
        readonly StringBuilder m_Typed = new StringBuilder();
        ILetteraInputSink m_Sink;
        ILetteraInputHost m_Host;
        string m_Composition = "";
        static bool s_LegacyCompositionBroken;

        public void Attach(ILetteraInputSink sink, ILetteraInputHost host)
        {
            m_Sink = sink;
            m_Host = host;
            m_Composition = "";
            TrySetComposition(true);
        }

        public void Detach()
        {
            TrySetComposition(false);
            m_Sink = null;
            m_Host = null;
        }

        // A project using only the Input System package has no legacy input class to ask.
        static void TrySetComposition(bool on)
        {
            if (s_LegacyCompositionBroken) return;
            try
            {
                UnityEngine.Input.imeCompositionMode = on ? IMECompositionMode.On : IMECompositionMode.Auto;
            }
            catch (InvalidOperationException)
            {
                s_LegacyCompositionBroken = true;
            }
        }

        static string ReadComposition()
        {
            if (s_LegacyCompositionBroken) return "";
            try
            {
                return UnityEngine.Input.compositionString ?? "";
            }
            catch (InvalidOperationException)
            {
                s_LegacyCompositionBroken = true;
                return "";
            }
        }

        public void Pump()
        {
            m_Typed.Clear();
            while (m_Sink != null && Event.PopEvent(m_Event))
            {
                if (m_Event.rawType != EventType.KeyDown) continue;
                LetteraKeyModifiers modifiers = Modifiers(m_Event);
                LetteraKeyCode key = LetteraKeyTable.FromUnity(m_Event.keyCode);
                char character = m_Event.character;
                if (key != LetteraKeyCode.None && key != LetteraKeyCode.Space)
                {
                    bool plainKey = character == 0 || char.IsControl(character);
                    Flush();
                    bool letterChord = !plainKey && (modifiers & (LetteraKeyModifiers.Control | LetteraKeyModifiers.Alt | LetteraKeyModifiers.Command)) == 0;
                    if (!letterChord) m_Sink.Key(key, modifiers);
                    if (plainKey) continue;
                }
                if (character == 0 || char.IsControl(character)) continue;
                // Control alone never types. Control with Alt is AltGr and does.
                bool control = (modifiers & LetteraKeyModifiers.Control) != 0, alt = (modifiers & LetteraKeyModifiers.Alt) != 0, command = (modifiers & LetteraKeyModifiers.Command) != 0;
                if (command || (control && !alt)) continue;
                m_Typed.Append(character);
            }
            if (m_Sink == null) return;

            string composition = ReadComposition();
            if (composition != m_Composition)
            {
                if (composition.Length == 0)
                {
                    // Unity delivers what the IME commits as ordinary characters.
                    if (m_Typed.Length > 0)
                    {
                        m_Sink.Composition(m_Typed.ToString(), m_Typed.Length, null);
                        m_Sink.CompositionEnd(true);
                        m_Typed.Clear();
                    }
                    else
                    {
                        m_Sink.CompositionEnd(false);
                    }
                }
                else
                {
                    Flush();
                    m_Sink.Composition(composition, composition.Length, null);
                }
                m_Composition = composition;
            }
            Flush();
            if (m_Composition.Length > 0 && m_Sink != null && !s_LegacyCompositionBroken && m_Host != null)
            {
                Rect caret = m_Host.CaretScreenRect;
                UnityEngine.Input.compositionCursorPos = new Vector2(caret.x, Screen.height - caret.y);
            }
        }

        void Flush()
        {
            if (m_Typed.Length == 0) return;
            string text = m_Typed.ToString();
            m_Typed.Clear();
            m_Sink.Text(text);
        }

        static LetteraKeyModifiers Modifiers(Event e)
        {
            LetteraKeyModifiers modifiers = LetteraKeyModifiers.None;
            if (e.shift) modifiers |= LetteraKeyModifiers.Shift;
            if (e.control) modifiers |= LetteraKeyModifiers.Control;
            if (e.alt) modifiers |= LetteraKeyModifiers.Alt;
            if (e.command) modifiers |= LetteraKeyModifiers.Command;
            return modifiers;
        }
    }

    /// <summary>Text from the system's on-screen keyboard. Its text box holds what the user types; each change comes back as one edit.</summary>
    public sealed class LetteraTouchKeyboardSource : ILetteraInputSource, ILetteraKeyboardWriter
    {
        TouchScreenKeyboard m_Keyboard;
        ILetteraInputSink m_Sink;
        ILetteraInputHost m_Host;
        string m_Known = "";

        public void Attach(ILetteraInputSink sink, ILetteraInputHost host)
        {
            m_Sink = sink;
            m_Host = host;
            m_Known = host.CommittedPlainText ?? "";
            LetteraKeyboardTraits traits = host.Traits;
            TouchScreenKeyboard.hideInput = true;
            m_Keyboard = TouchScreenKeyboard.Open(m_Known, traits.ToUnityType(), traits.Autocorrect, traits.Multiline, traits.Secure);
            if (m_Keyboard != null && host.Selection.End <= m_Known.Length) SendSelection();
        }

        void SendSelection()
        {
            if (m_Keyboard == null || !m_Keyboard.canSetSelection) return;
            LetteraSelectionRange selection = m_Host.Selection;
            m_Keyboard.selection = new RangeInt(selection.Start, selection.Length);
        }

        public void Detach()
        {
            if (m_Keyboard != null) m_Keyboard.active = false;
            m_Keyboard = null;
            m_Sink = null;
            m_Host = null;
        }

        public void Pump()
        {
            if (m_Keyboard == null || m_Sink == null) return;
            string text = m_Keyboard.text ?? "";
            if (text != m_Known)
            {
                if (m_Sink is ILetteraKeyboardSink keyboard) keyboard.Reconcile(text);
                m_Known = m_Host?.CommittedPlainText ?? text;
                // A rule refused or rewrote it: the OS box has to show what the field holds.
                if (m_Known != text) m_Keyboard.text = m_Known;
            }
            switch (m_Keyboard.status)
            {
                case TouchScreenKeyboard.Status.Done:
                    m_Sink.Action("done");
                    break;
                case TouchScreenKeyboard.Status.Canceled:
                    m_Sink.Action("cancel");
                    break;
            }
        }

        void ILetteraKeyboardWriter.TextChanged(string text)
        {
            m_Known = text;
            if (m_Keyboard != null && m_Keyboard.text != text) m_Keyboard.text = text;
            if (m_Keyboard != null) SendSelection();
        }
    }

    /// <summary>A sink that can take the whole text of an OS text box and find the edit in it.</summary>
    public interface ILetteraKeyboardSink
    {
        /// <summary>The caret, in UTF-16 units of the text, tells where repeated characters went. -1 when unknown.</summary>
        void Reconcile(string text, int caret = -1);
    }

    internal interface ILetteraKeyboardWriter
    {
        void TextChanged(string text);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// A touch browser's keyboard, through a hidden text box on the page, so the field itself shows what is typed.
    /// A press on a field arms it first, because only a touch handler can raise that keyboard.
    /// </summary>
    public sealed class LetteraWebInputSource : ILetteraInputSource, ILetteraKeyboardWriter
    {
        const string Lib = "__Internal";
        [System.Runtime.InteropServices.DllImport(Lib)] internal static extern int LetteraInput_HasCoarsePointer();
        [System.Runtime.InteropServices.DllImport(Lib)] internal static extern void LetteraInput_Arm();
        [System.Runtime.InteropServices.DllImport(Lib)] static extern void LetteraInput_Open(string text, int start, int end, int multiline, int secure, int kind, int returnKey);
        [System.Runtime.InteropServices.DllImport(Lib)] static extern void LetteraInput_Close();
        [System.Runtime.InteropServices.DllImport(Lib)] static extern int LetteraInput_TakeFlags();
        [System.Runtime.InteropServices.DllImport(Lib)] static extern int LetteraInput_IsComposing();
        [System.Runtime.InteropServices.DllImport(Lib)] static extern int LetteraInput_GetText(byte[] buffer, int size);
        [System.Runtime.InteropServices.DllImport(Lib)] static extern int LetteraInput_GetComposition(byte[] buffer, int size);
        [System.Runtime.InteropServices.DllImport(Lib)] static extern int LetteraInput_GetCaret();
        [System.Runtime.InteropServices.DllImport(Lib)] static extern void LetteraInput_Set(string text, int start, int end);
        [System.Runtime.InteropServices.DllImport(Lib)] static extern void LetteraInput_Place(float x, float y);

        ILetteraInputSink m_Sink;
        ILetteraInputHost m_Host;
        byte[] m_Buffer = new byte[1024];
        bool m_Composing, m_CapturedKeys;
        LetteraSelectionRange m_SentSelection;
        Rect m_LastCaret;

        delegate int Reader(byte[] buffer, int size);

        string Read(Reader reader)
        {
            int size = reader(null, 0) + 1;
            if (m_Buffer.Length < size) m_Buffer = new byte[size * 2];
            int length = reader(m_Buffer, m_Buffer.Length);
            return Encoding.UTF8.GetString(m_Buffer, 0, length);
        }

        static int Utf16(string text, int codepoints) => LetteraEditProposal.Utf16Of(text, codepoints);

        public void Attach(ILetteraInputSink sink, ILetteraInputHost host)
        {
            m_Sink = sink;
            m_Host = host;
            // Unity otherwise cancels every key on the page, which would keep them out of the box.
            m_CapturedKeys = WebGLInput.captureAllKeyboardInput;
            WebGLInput.captureAllKeyboardInput = false;
            string text = host.CommittedPlainText ?? "";
            LetteraKeyboardTraits traits = host.Traits;
            m_SentSelection = host.Selection;
            LetteraInput_Open(text, Utf16(text, m_SentSelection.Start), Utf16(text, m_SentSelection.End), traits.Multiline ? 1 : 0, traits.Secure ? 1 : 0, (int)traits.Kind, (int)traits.ReturnKey);
        }

        public void Detach()
        {
            LetteraInput_Close();
            WebGLInput.captureAllKeyboardInput = m_CapturedKeys;
            m_Sink = null;
            m_Host = null;
        }

        public void Pump()
        {
            if (m_Sink == null) return;
            int flags = LetteraInput_TakeFlags();
            bool composing = LetteraInput_IsComposing() != 0;
            if ((flags & 2) != 0 || composing != m_Composing)
            {
                if (composing)
                {
                    string composition = Read(LetteraInput_GetComposition);
                    m_Sink.Composition(composition, composition.Length, null);
                }
                else if (m_Composing)
                {
                    // What it became arrives with the box's text, below.
                    m_Sink.CompositionEnd(false);
                }
                m_Composing = composing;
            }
            if ((flags & 1) != 0 && !composing && m_Sink is ILetteraKeyboardSink keyboard)
            {
                string text = Read(LetteraInput_GetText);
                keyboard.Reconcile(text, LetteraInput_GetCaret());
                if (m_Host == null) return;
                SendState();
            }
            if ((flags & 4) != 0) m_Sink.Key(LetteraKeyCode.Enter, LetteraKeyModifiers.None);
            if (m_Sink == null) return;
            if ((flags & 8) != 0)
            {
                m_Sink.Action("done");
                return;
            }
            if (m_Host.Selection != m_SentSelection && !composing) SendState();
            Rect caret = m_Host.CaretScreenRect;
            if (caret != m_LastCaret)
            {
                m_LastCaret = caret;
                LetteraInput_Place(caret.x / Screen.width, caret.yMin / Screen.height);
            }
        }

        void SendState()
        {
            string text = m_Host.CommittedPlainText ?? "";
            m_SentSelection = m_Host.Selection;
            LetteraInput_Set(text, Utf16(text, m_SentSelection.Start), Utf16(text, m_SentSelection.End));
        }

        void ILetteraKeyboardWriter.TextChanged(string text)
        {
            if (m_Host != null && !m_Composing) SendState();
        }
    }
#endif

    public enum LetteraKeyboardKind { Default, Ascii, Url, Number, Decimal, Phone, Name, Email, Search }
    public enum LetteraReturnKey { Default, Done, Go, Next, Search, Send }

    /// <summary>What a soft keyboard should offer. What the current route cannot pass on does nothing.</summary>
    [Serializable]
    public enum LetteraAutoCapitalize { Default, None, Sentences, Words, All }

    public enum LetteraTraitToggle { Default, On, Off }

    public enum LetteraIosAppearance { Default, Light, Dark }

    [Flags]
    public enum LetteraAndroidImeFlags { None = 0, NoFullscreen = 1, NoExtractUi = 2, NoAccessoryAction = 4, NoPersonalizedLearning = 8 }

    /// <summary>iOS's own typing helps.</summary>
    [Serializable]
    public struct LetteraIosSmart
    {
        public LetteraTraitToggle Quotes, Dashes, InsertDelete;
    }

    /// <summary>The portable names of what an autofill can offer, which follow the web's autocomplete tokens. A string not listed here goes to the web unchanged.</summary>
    public static class LetteraAutofill
    {
        public const string Username = "username", CurrentPassword = "current-password", NewPassword = "new-password", OneTimeCode = "one-time-code", Email = "email", Name = "name", GivenName = "given-name", FamilyName = "family-name", Tel = "tel", StreetAddress = "street-address", PostalCode = "postal-code", Country = "country", CreditCardNumber = "cc-number";
    }

    /// <summary>What a platform's keyboard may report, as one vocabulary.</summary>
    public static class LetteraVerbs
    {
        public const string Submit = "submit", Return = "return", Done = "done", Next = "next", Previous = "previous", Cancel = "cancel", Copy = "copy", Cut = "cut", Paste = "paste", PastePlain = "pastePlain", Undo = "undo", Redo = "redo", SelectAll = "selectAll", Newline = "newline", CutCopied = "cutCopied";

        /// <summary>Whether a session's boundary should take this.</summary>
        public static bool IsKnown(string verb)
        {
            switch (verb)
            {
                case Submit: case Return: case Done: case Next: case Previous: case Cancel: case Copy: case Cut: case Paste: case PastePlain: case Undo: case Redo: case SelectAll: case Newline: case CutCopied:
                    return true;
                default:
                    return false;
            }
        }
    }

    public struct LetteraKeyboardTraits
    {
        public LetteraKeyboardKind Kind;
        public LetteraReturnKey ReturnKey;
        public bool Autocorrect;
        public bool Multiline;
        public bool Secure;
        public string AutofillHint;

        [Tooltip("Web, Android, iOS.")] public LetteraAutoCapitalize AutoCapitalize;
        [Tooltip("Web, iOS. Android follows suggestions.")] public LetteraTraitToggle SpellCheck;
        [Tooltip("iOS only.")] public LetteraIosSmart IosSmart;
        [Tooltip("iOS only.")] public LetteraIosAppearance IosAppearance;
        [Tooltip("iOS only: the return key stays off while the field is empty.")] public LetteraTraitToggle IosReturnKeyWhenEmpty;
        [Tooltip("Android only.")] public LetteraAndroidImeFlags AndroidImeFlags;

        public TouchScreenKeyboardType ToUnityType()
        {
            switch (Kind)
            {
                case LetteraKeyboardKind.Ascii: return TouchScreenKeyboardType.ASCIICapable;
                case LetteraKeyboardKind.Url: return TouchScreenKeyboardType.URL;
                case LetteraKeyboardKind.Number: return TouchScreenKeyboardType.NumberPad;
                case LetteraKeyboardKind.Decimal: return TouchScreenKeyboardType.DecimalPad;
                case LetteraKeyboardKind.Phone: return TouchScreenKeyboardType.PhonePad;
                case LetteraKeyboardKind.Name: return TouchScreenKeyboardType.NamePhonePad;
                case LetteraKeyboardKind.Email: return TouchScreenKeyboardType.EmailAddress;
                case LetteraKeyboardKind.Search: return TouchScreenKeyboardType.Search;
                default: return TouchScreenKeyboardType.Default;
            }
        }
    }
}
