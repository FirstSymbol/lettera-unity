#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A page's own text box, as what a field's session runs through on the web. It holds no rule: everything typed comes back as events.</summary>
    internal sealed class LetteraWebHost : ILetteraNativeHost, ILetteraReplicaHost
    {
        const string Lib = "__Internal";
        [DllImport(Lib)] static extern void LetteraHost_Open(string text, int start, int end, int revision, int multiline, int secure, int kind, int returnKey, int autocapitalize, int autocorrect, int spellCheck, string autofill);
        [DllImport(Lib)] static extern int LetteraHost_OpenReplica(string text, int start, int end, int revision, int multilineControl, int secure, int kind, int returnKey, int autocapitalize, int autocorrect, int spellCheck, string autofill, string placeholder, string surface, string controlId, string surfaceData, int readOnly);
        [DllImport(Lib)] static extern void LetteraHost_Push(string text, int start, int end, int revision);
        [DllImport(Lib)] static extern void LetteraHost_Close();
        [DllImport(Lib)] static extern int LetteraHost_Next(int[] ints, byte[] buffer, int size);
        [DllImport(Lib)] static extern void LetteraHost_Place(float x, float y, float width, float height);
        [DllImport(Lib)] static extern int LetteraHost_UsesOverlay();

        readonly int[] m_Ints = new int[7];
        byte[] m_Buffer = new byte[4096];
        bool m_CapturedKeys, m_Open;

        public bool IsAvailable => true;

        public void Open(in LetteraMirrorState state, in LetteraKeyboardTraits traits, LetteraHostMode mode)
        {
            // Unity otherwise cancels every key on the page, which would keep them out of the box.
            LetteraWebClipboard.EnsureCallback();
            m_CapturedKeys = WebGLInput.captureAllKeyboardInput;
            WebGLInput.captureAllKeyboardInput = false;
            m_Open = true;
            IsReplicaOpen = false;
            if (mode == LetteraHostMode.Replica && traits.Replica != null && TryReplica(state, traits)) return;
            LetteraHost_Open(state.Text, state.SelectionStart, state.SelectionEnd, state.Revision, traits.Multiline ? 1 : 0, traits.Secure ? 1 : 0, (int)traits.Kind, (int)traits.ReturnKey, (int)traits.AutoCapitalize, traits.Autocorrect ? 1 : 0, (int)traits.SpellCheck, traits.AutofillHint ?? "");
        }

        public bool IsReplicaOpen { get; private set; }

        bool TryReplica(in LetteraMirrorState state, in LetteraKeyboardTraits traits)
        {
            LetteraReplicaRequest request = traits.Replica;
            // Browsers have no secure multi-line control, and a window of a document cannot honestly show as a whole text.
            bool secureWrap = traits.Secure && request.WantsMultilineControl;
            if (secureWrap || state.Offset > 0 || state.Text.Length >= LetteraMirrorSource.WholeTextUnits)
            {
                LetteraPlatform.Report(LetteraNativeFeature.Replica, secureWrap ? "A secure field that wraps has no control of its own in a browser, so Lettera draws it." : "A long text has no control that could hold it, so Lettera draws it.");
                return false;
            }
            int done = LetteraHost_OpenReplica(state.Text, state.SelectionStart, state.SelectionEnd, state.Revision, request.WantsMultilineControl ? 1 : 0, traits.Secure ? 1 : 0, (int)traits.Kind, (int)traits.ReturnKey, (int)traits.AutoCapitalize, traits.Autocorrect ? 1 : 0, (int)traits.SpellCheck, traits.AutofillHint ?? "", request.Placeholder ?? "", request.Surface ?? "", request.ControlId ?? "", request.SurfaceData ?? "", request.ReadOnly ? 1 : 0);
            if (done == 0)
            {
                LetteraPlatform.Report(LetteraNativeFeature.Replica, "Surface '" + request.Surface + "' could not be built, so Lettera draws this field.");
                return false;
            }
            IsReplicaOpen = true;
            return true;
        }

        public void Push(in LetteraMirrorState state) => LetteraHost_Push(state.Text, state.SelectionStart, state.SelectionEnd, state.Revision);

        public void Place(Rect caret, Rect field) => LetteraHost_Place(caret.x, caret.y, Mathf.Max(field.width, 1), Mathf.Max(field.height, 1));

        public void Reconfigure(in LetteraKeyboardTraits traits) { }

        public void Close()
        {
            if (!m_Open) return;
            m_Open = false;
            if (LetteraHost_UsesOverlay() != 0) LetteraPlatform.Report(LetteraNativeFeature.TextInput, "Script focus raised no keyboard twice, so this page's field uses a real text box under the finger.");
            LetteraHost_Close();
            WebGLInput.captureAllKeyboardInput = m_CapturedKeys;
        }

        public int Drain(LetteraHostEvent[] into)
        {
            int count = 0;
            while (count < into.Length)
            {
                int kind = LetteraHost_Next(m_Ints, m_Buffer, m_Buffer.Length);
                if (kind == 0) break;
                if (kind < 0)
                {
                    // Too long for the buffer: it grows, and the event waits.
                    m_Buffer = new byte[-kind * 2];
                    continue;
                }
                int length = 0;
                while (length < m_Buffer.Length && m_Buffer[length] != 0) length++;
                string text = Encoding.UTF8.GetString(m_Buffer, 0, length);
                var e = new LetteraHostEvent { BaseRevision = m_Ints[0], Start = m_Ints[1], End = m_Ints[2], Caret = m_Ints[3], Text = text };
                switch (kind)
                {
                    case 1:
                        e.Kind = LetteraHostEventKind.WholeText;
                        break;
                    case 2:
                        e.Kind = LetteraHostEventKind.Compose;
                        e.Start = e.End = 0;
                        break;
                    case 3:
                        e.Kind = LetteraHostEventKind.ComposeEnd;
                        e.Commit = m_Ints[6] != 0;
                        break;
                    case 4:
                        e.Kind = LetteraHostEventKind.Key;
                        e.KeyCode = (LetteraKeyCode)m_Ints[4];
                        e.Modifiers = (LetteraKeyModifiers)m_Ints[5];
                        e.Repeat = m_Ints[6] != 0;
                        break;
                    case 5:
                        e.Kind = LetteraHostEventKind.Select;
                        break;
                    case 6:
                        e.Kind = LetteraHostEventKind.Verb;
                        break;
                    case 7:
                        e.Kind = LetteraHostEventKind.TypedAtSelection;
                        break;
                    case 8:
                        var pasted = new List<LetteraClipItem>();
                        LetteraWebClipboard.TakeItems(pasted);
                        e.Kind = LetteraHostEventKind.Paste;
                        e.Items = pasted;
                        break;
                    case 9:
                        e.Kind = LetteraHostEventKind.Tap;
                        e.Frame = new Rect(m_Ints[1], m_Ints[2], 0, 0);
                        break;
                    default:
                        continue;
                }
                into[count++] = e;
            }
            return count;
        }
    }

    // A touch browser raises its keyboard only inside a touch handler, and Unity hears of a touch a frame later. So where fields stand goes to the page ahead of time.
    internal sealed class LetteraWebFields : IMotionClient
    {
        [DllImport("__Internal")] static extern void LetteraHost_SetFields(float[] rects, int count);
        [DllImport("__Internal")] static extern int LetteraInput_HasCoarsePointer();

        static readonly List<LetteraEditable> s_Fields = new List<LetteraEditable>();
        static LetteraWebFields s_Instance;
        float[] m_Sent = new float[0];
        float m_Wait;

        internal static void Add(LetteraEditable field)
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.TextInput) || LetteraInput_HasCoarsePointer() == 0) return;
            if (!s_Fields.Contains(field)) s_Fields.Add(field);
            if (s_Instance == null)
            {
                s_Instance = new LetteraWebFields();
                LetteraMotionHub.Add(s_Instance);
            }
        }

        internal static void Remove(LetteraEditable field) => s_Fields.Remove(field);

        public bool TickMotion(float scaled, float unscaled)
        {
            m_Wait -= unscaled;
            if (m_Wait > 0) return true;
            m_Wait = 0.1f;
            var flat = new List<float>(s_Fields.Count * 4);
            var corners = new Vector3[4];
            for (int i = s_Fields.Count - 1; i >= 0; i--)
            {
                LetteraEditable field = s_Fields[i];
                if (field == null)
                {
                    s_Fields.RemoveAt(i);
                    continue;
                }
                if (!field.isActiveAndEnabled || field.ReadOnly || !field.Interactable) continue;
                LetteraFieldPressArea area = field.GetComponentInParent<LetteraFieldPressArea>();
                ((RectTransform)(area != null ? area.transform : field.transform)).GetWorldCorners(corners);
                Canvas canvas = field.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                flat.Add(Mathf.Min(a.x, b.x));
                flat.Add(Mathf.Min(a.y, b.y));
                flat.Add(Mathf.Max(a.x, b.x));
                flat.Add(Mathf.Max(a.y, b.y));
            }
            bool same = flat.Count == m_Sent.Length;
            for (int i = 0; same && i < flat.Count; i++) same = flat[i] == m_Sent[i];
            if (same) return true;
            m_Sent = flat.ToArray();
            LetteraHost_SetFields(m_Sent, m_Sent.Length);
            return true;
        }
    }
}
#endif
