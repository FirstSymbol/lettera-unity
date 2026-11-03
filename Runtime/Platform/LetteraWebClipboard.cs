using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;
using AOT;
#endif

namespace FirstSymbol.Lettera
{
    /// <summary>Lets a control that is not one of Lettera's menus run a clipboard command in a browser, which allows these calls only inside the user's own gesture.</summary>
    public static class LetteraWebGestures
    {
        /// <summary>Puts a control's place before the page, so its release can reach the clipboard. Elsewhere this does nothing.</summary>
        public static void Arm(LetteraEditCommand command, RectTransform control)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LetteraWebClipboard.ArmGesture(command, control);
#endif
        }

        /// <summary>Takes it away again.</summary>
        public static void Disarm(RectTransform control)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LetteraWebClipboard.DisarmGesture(control);
#endif
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // In a browser everything happens in the page's own handlers: Unity hears a key or a release a frame later, by which time the browser no longer allows it.
    internal sealed class LetteraWebClipboard : LetteraPlatformClipboard, ILetteraClipboardAsyncOnly, IMotionClient
    {
        sealed class WebHost : ILetteraClipboardHost
        {
            public bool TryWrite(IReadOnlyList<LetteraClipItem> items)
            {
                SplitForPage(items, out string plain, out string html);
                if (s_Capturing) LetteraClip_Answer(plain, html);
                else LetteraClip_WriteNow(plain, html);
                return true;
            }

            public bool TryRead(List<LetteraClipItem> items) => false;
            public LetteraClipPresence Probe() => LetteraClipPresence.Unknown;
        }

        [DllImport("__Internal")] static extern void LetteraClip_SetCallback(Action<int> callback, int maxImage);
        [DllImport("__Internal")] static extern void LetteraClip_Answer(string plain, string html);
        [DllImport("__Internal")] static extern void LetteraClip_WriteNow(string plain, string html);
        [DllImport("__Internal")] static extern int LetteraClip_Read();
        [DllImport("__Internal")] static extern int LetteraClip_NextResult();
        [DllImport("__Internal")] static extern int LetteraClip_ItemCount();
        [DllImport("__Internal")] static extern int LetteraClip_ItemFormat(int index, byte[] buffer, int size);
        [DllImport("__Internal")] static extern int LetteraClip_ItemSize(int index);
        [DllImport("__Internal")] static extern void LetteraClip_ItemBytes(int index, byte[] target);
        [DllImport("__Internal")] static extern int LetteraClip_WritesFail();
        [DllImport("__Internal")] static extern void LetteraGesture_Arm(int verb, string plain, string html, float[] rects, int count);
        [DllImport("__Internal")] static extern void LetteraGesture_Disarm(int verb);

        const int VerbCopy = 1, VerbCut = 2, VerbPaste = 3, VerbCount = 4;

        static bool s_Capturing, s_CallbackSet;
        static LetteraWebClipboard s_Instance;

        readonly Dictionary<int, Action<List<LetteraClipItem>>> m_Waiting = new Dictionary<int, Action<List<LetteraClipItem>>>();
        bool m_InHub;

        internal LetteraWebClipboard() : base(new WebHost())
        {
            s_Instance = this;
        }

        /// <summary>Done before any session, since a page's copy event needs somewhere to go.</summary>
        internal static void EnsureCallback()
        {
            if (s_CallbackSet) return;
            s_CallbackSet = true;
            LetteraClip_SetCallback(OnCopyEvent, LetteraSettings.Instance.Platform.PasteImageMaxBytes);
        }

        // Lettera's own format has no type of its own here, and comments do not survive a browser, so it rides on an attribute.
        static void SplitForPage(IReadOnlyList<LetteraClipItem> items, out string plain, out string html)
        {
            plain = "";
            html = "";
            string own = null;
            foreach (LetteraClipItem item in items)
            {
                if (item.Format == LetteraClipFormat.PlainText) plain = item.Text;
                else if (item.Format == LetteraClipFormat.Html) html = item.Text;
                else if (item.Format == LetteraClipFormat.Lettera) own = item.Text;
            }
            // Past this size a page's handler would hold the frame up: plain text only.
            if (Encoding.UTF8.GetByteCount(plain) > LetteraSettings.Instance.Platform.WebCopySyncLimit) html = "";
            else if (own != null && html.Length > 0) html = LetteraHtmlCodec.CarryOwnFormat(html, own);
        }

        // The page's copy and cut events arrive here at once, in the handler, and the clipboard's content goes back before it returns.
        [MonoPInvokeCallback(typeof(Action<int>))]
        static void OnCopyEvent(int cut)
        {
            LetteraSelectable selectable = LetteraSelectable.Current;
            if (selectable == null) return;
            s_Capturing = true;
            try
            {
                if (cut != 0 && selectable.Editable != null && selectable.Editable.ReadOnly) return;
                selectable.Copy();
            }
            catch (Exception e)
            {
                LetteraPlatform.Report(LetteraNativeFeature.Clipboard, "Building a copy failed: " + e.Message);
            }
            finally
            {
                s_Capturing = false;
            }
        }

        // ---------------------------------------------------------- reading

        // Reading here always comes back later, so only the shadow answers at once.
        public override void Read(List<LetteraClipItem> items) => items.AddRange(Shadow);

        public override void ReadAsync(Action<List<LetteraClipItem>> done)
        {
            int id = LetteraClip_Read();
            m_Waiting[id] = done;
            if (m_InHub) return;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        public bool TickMotion(float scaled, float unscaled)
        {
            TickGestures();
            int result;
            while ((result = LetteraClip_NextResult()) != 0)
            {
                int id = Math.Abs(result);
                if (!m_Waiting.TryGetValue(id, out Action<List<LetteraClipItem>> done)) continue;
                m_Waiting.Remove(id);
                var items = new List<LetteraClipItem>();
                if (result > 0)
                {
                    TakeItems(items);
                    PreferShadow(items, 0);
                }
                // A refused read is no error and changes nothing, unless the browser never let our copies out either: then a copy stays within the page.
                else if (LetteraClip_WritesFail() != 0) items.AddRange(Shadow);
                done(items);
            }
            m_InHub = m_Waiting.Count > 0 || s_Armed.Count > 0;
            return m_InHub;
        }

        /// <summary>What the page handed over, as items.</summary>
        internal static void TakeItems(List<LetteraClipItem> items)
        {
            int count = LetteraClip_ItemCount();
            var name = new byte[128];
            for (int i = 0; i < count; i++)
            {
                int length = LetteraClip_ItemFormat(i, name, name.Length);
                var data = new byte[LetteraClip_ItemSize(i)];
                LetteraClip_ItemBytes(i, data);
                items.Add(new LetteraClipItem(Encoding.UTF8.GetString(name, 0, length), data));
            }
        }

        // ---------------------------------------------------------- gestures

        static readonly ValueList<(int verb, RectTransform control)> s_Armed = new ValueList<(int, RectTransform)>();
        static readonly float[][] s_SentRects = new float[VerbCount][];
        static readonly string[] s_SentPlain = new string[VerbCount];
        static readonly Vector3[] s_Corners = new Vector3[4];

        static int VerbOf(LetteraEditCommand command)
        {
            if (command is CopyCommand) return VerbCopy;
            if (command is CutCommand) return VerbCut;
            if (command is PasteCommand) return VerbPaste;
            return 0;
        }

        internal static void ArmGesture(LetteraEditCommand command, RectTransform control)
        {
            int verb = VerbOf(command);
            if (verb == 0 || control == null) return;
            if (!(LetteraClipboard.Backend is LetteraWebClipboard clipboard)) return;
            for (int i = 0; i < s_Armed.Count; i++)
                if (s_Armed[i].control == control)
                {
                    s_Armed[i] = (verb, control);
                    return;
                }
            s_Armed.Add((verb, control));
            if (clipboard.m_InHub) return;
            clipboard.m_InHub = true;
            LetteraMotionHub.Add(clipboard);
        }

        internal static void DisarmGesture(RectTransform control)
        {
            for (int i = s_Armed.Count - 1; i >= 0; i--)
                if (s_Armed[i].control == control)
                    s_Armed.RemoveAt(i);
        }

        // A control moves and a selection changes, so what the page holds follows. Sent only on a change.
        static void TickGestures()
        {
            var flat = new List<float>[VerbCount];
            for (int i = s_Armed.Count - 1; i >= 0; i--)
            {
                (int verb, RectTransform control) = s_Armed[i];
                if (control == null)
                {
                    s_Armed.RemoveAt(i);
                    continue;
                }
                if (!control.gameObject.activeInHierarchy) continue;
                control.GetWorldCorners(s_Corners);
                Canvas canvas = control.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, s_Corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, s_Corners[2]);
                (flat[verb] ?? (flat[verb] = new List<float>())).AddRange(new[] { Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y) });
            }
            for (int verb = 1; verb < VerbCount; verb++) SendGesture(verb, flat[verb]);
        }

        static void SendGesture(int verb, List<float> rects)
        {
            if (rects == null)
            {
                if (s_SentRects[verb] == null) return;
                s_SentRects[verb] = null;
                s_SentPlain[verb] = null;
                LetteraGesture_Disarm(verb);
                return;
            }
            string plain = "", html = "";
            if (verb != VerbPaste)
            {
                LetteraSelectable selectable = LetteraSelectable.Current;
                if (selectable != null && !selectable.Selection.IsCaret && (selectable.Editable == null || selectable.Editable.CanCopy))
                    SplitForPage(LetteraClipboard.SelectionItems(selectable), out plain, out html);
            }
            float[] sent = s_SentRects[verb];
            bool same = sent != null && sent.Length == rects.Count && s_SentPlain[verb] == plain + "\0" + html;
            for (int i = 0; same && i < rects.Count; i++) same = sent[i] == rects[i];
            if (same) return;
            s_SentRects[verb] = rects.ToArray();
            s_SentPlain[verb] = plain + "\0" + html;
            LetteraGesture_Arm(verb, plain, html, s_SentRects[verb], rects.Count);
        }
    }
#endif
}
