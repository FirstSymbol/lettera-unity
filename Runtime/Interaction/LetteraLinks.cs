using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>What opens a URL.</summary>
    public interface ILetteraLinkOpener
    {
        void Open(string url);
    }

    /// <summary>A link on its way out: rewrite it, or refuse it.</summary>
    public sealed class LetteraLinkRequest
    {
        public string Url;
        public bool Refused;
    }

    /// <summary>Opening links. What may come from other people's text goes through an allow-list.</summary>
    public static class LetteraLinks
    {
        /// <summary>Replaces what opens URLs. Tests use this.</summary>
        public static ILetteraLinkOpener Opener;

        /// <summary>Raised before a link opens.</summary>
        public static event Action<LetteraLinkRequest> Opening;

        static string s_ArmedUrl;

        /// <summary>The address a text's URL means, or null when it is not allowed.</summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            int colon = url.IndexOf(':');
            if (colon <= 0) return null;
            string scheme = url.Substring(0, colon).ToLowerInvariant();
            foreach (string allowed in LetteraSettings.Instance.Motion.LinkSchemes)
                if (string.Equals(allowed, scheme, StringComparison.OrdinalIgnoreCase))
                    return url;
            return null;
        }

        /// <summary>Opens a URL through the allow-list. False when it stayed shut.</summary>
        public static bool Open(string url)
        {
            string normalized = Normalize(url);
            if (normalized == null)
            {
                Debug.LogWarning($"Lettera: refused to open the link '{url}'. Its scheme is not in the settings' list.");
                return false;
            }
            var request = new LetteraLinkRequest { Url = normalized };
            Opening?.Invoke(request);
            if (request.Refused || string.IsNullOrEmpty(request.Url)) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (Opener == null && s_ArmedUrl == request.Url)
            {
                s_ArmedUrl = null;
                // The page's release handler opened it, inside the gesture. A tap quicker than a frame gets there too late for that.
                if (LetteraLink_TakeOpened() != 0) return true;
            }
#endif
            if (Opener != null) Opener.Open(request.Url);
            else Application.OpenURL(request.Url);
            return true;
        }

        // A browser blocks a window opened outside a user's gesture, and Unity runs a frame after it. The address goes to the page at the press,
        // and the page's own release handler opens it. A subscriber who may still cancel takes the later, riskier path.
        internal static void ArmPress(in TextEngine.ActiveRange range, LetteraRangeRouter router, Camera camera)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!(range.Effect is LinkEffect link) || !link.OpenOnActivate || Opener != null) return;
            if (link.HasFollowers || router.All.HasActivated || Opening != null) return;
            string url = Normalize(range.Primary);
            if (url == null) return;
            s_ArmedUrl = url;
            // The release is judged in the page, a frame before this code hears of it, so the page gets the places where it counts.
            s_ArmRects.Clear();
            router.Engine.ActiveRectsOf(range.Id, s_ArmRects);
            RectTransform text = router.Text.TextRect;
            float[] flat = new float[s_ArmRects.Count * 4];
            for (int i = 0; i < s_ArmRects.Count; i++)
            {
                Rect r = s_ArmRects[i];
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, text.TransformPoint(new Vector3(r.xMin, r.yMin)));
                Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, text.TransformPoint(new Vector3(r.xMax, r.yMax)));
                flat[i * 4] = Mathf.Min(a.x, b.x);
                flat[i * 4 + 1] = Mathf.Min(a.y, b.y);
                flat[i * 4 + 2] = Mathf.Max(a.x, b.x);
                flat[i * 4 + 3] = Mathf.Max(a.y, b.y);
            }
            LetteraLink_Arm(url, flat, flat.Length);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        static readonly List<Rect> s_ArmRects = new List<Rect>();
#endif

        internal static void DisarmPress()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (s_ArmedUrl == null) return;
            s_ArmedUrl = null;
            LetteraLink_Disarm();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void LetteraLink_Arm(string url, float[] rects, int count);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int LetteraLink_TakeOpened();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void LetteraLink_Disarm();
#endif
    }

    /// <summary>Puts a text's ranges in Unity's UI navigation, so arrows and gamepads step through them and leave at the ends.</summary>
    [AddComponentMenu("Lettera/Range Navigator")]
    public class LetteraRangeNavigator : Selectable, ISubmitHandler
    {
        Component m_TextComponent;

        LetteraRangeRouter Router
        {
            get
            {
                if (m_TextComponent == null)
                {
                    m_TextComponent = GetComponent<LetteraText>();
                    if (m_TextComponent == null) m_TextComponent = GetComponent<LetteraWorldText>();
                }
                return m_TextComponent != null ? LetteraRangeRouter.Of(m_TextComponent) : null;
            }
        }

        public override void OnMove(AxisEventData eventData)
        {
            bool forward = eventData.moveDir == MoveDirection.Right || eventData.moveDir == MoveDirection.Down;
            if (Router != null && Router.StepFocus(forward))
            {
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (Router != null && Router.ActivateFocused()) eventData.Use();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            Router?.ClearFocus();
            base.OnDeselect(eventData);
        }
    }

    // ------------------------------------------------------------- semantics

    public enum LetteraSemanticRole { Text, Link, Button, Mention, Tag, Code, Math, Heading, Image, Comment, Error, Status }

    [Flags]
    public enum LetteraSemanticStates { None = 0, Disabled = 1, Focused = 2, Hovered = 4, Pressed = 8, Selected = 16, Concealed = 32, Visited = 64 }

    [Flags]
    public enum LetteraSemanticActions { None = 0, Activate = 1, Context = 2, Focus = 4, Dismiss = 8 }

    /// <summary>Describes a range for what cannot read pixels. Nothing enters the tree on its own: a range is described only where this covers it.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SemanticEffect : LetteraEffect
    {
        [SerializeField] LetteraSemanticRole m_Role;
        [Tooltip("Empty takes the covered text.")]
        [SerializeField] string m_Label = "";
        [SerializeField] string m_Value = "", m_Hint = "", m_Language = "";
        [SerializeField] LetteraSemanticActions m_Actions;
        [SerializeField, Range(1, 6)] int m_Level = 1;
        [SerializeField] bool m_IncludeVirtual;

        public LetteraSemanticRole Role { get => m_Role; set => m_Role = value; }
        public string Label { get => m_Label; set => m_Label = value; }
        public string Value { get => m_Value; set => m_Value = value; }
        public string Hint { get => m_Hint; set => m_Hint = value; }
        public string Language { get => m_Language; set => m_Language = value; }
        public LetteraSemanticActions Actions { get => m_Actions; set => m_Actions = value; }
        public int Level { get => m_Level; set => m_Level = value; }
        public bool IncludeVirtual { get => m_IncludeVirtual; set => m_IncludeVirtual = value; }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.AddSemantic(this, start, end);
    }

    /// <summary>One described range.</summary>
    public sealed class LetteraSemanticNode
    {
        public int Id { get; internal set; }
        public int ParentId { get; internal set; }
        public int Start { get; internal set; }
        public int End { get; internal set; }
        public LetteraSemanticRole Role { get; internal set; }
        public string Label { get; internal set; }
        public string Value { get; internal set; }
        public string Hint { get; internal set; }
        public string Language { get; internal set; }
        public int Level { get; internal set; }
        public LetteraSemanticStates States { get; internal set; }
        public LetteraSemanticActions Actions { get; internal set; }
        public readonly List<Rect> Segments = new List<Rect>();

        /// <summary>The union of the segments, in local space.</summary>
        public Rect Bounds
        {
            get
            {
                if (Segments.Count == 0) return default;
                Rect bounds = Segments[0];
                foreach (Rect r in Segments) bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin), Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
                return bounds;
            }
        }
    }

    /// <summary>A platform-neutral description of a text. Lettera ships no OS bridge yet: a bridge, or a test, is an adapter over this.</summary>
    public sealed class LetteraSemanticTree
    {
        static readonly Dictionary<TextEngine, LetteraSemanticTree> s_Trees = new Dictionary<TextEngine, LetteraSemanticTree>();

        readonly ITextComponent m_Text;
        readonly TextEngine m_Engine;
        readonly List<LetteraSemanticNode> m_Nodes = new List<LetteraSemanticNode>();
        readonly Dictionary<int, LetteraSemanticNode> m_ById = new Dictionary<int, LetteraSemanticNode>();

        LetteraSemanticTree(ITextComponent text)
        {
            m_Text = text;
            m_Engine = text.SharedEngine;
        }

        public static LetteraSemanticTree Of(Component text)
        {
            var component = (ITextComponent)text;
            TextEngine engine = component.SharedEngine;
            if (s_Trees.TryGetValue(engine, out LetteraSemanticTree tree)) return tree;
            tree = new LetteraSemanticTree(component);
            s_Trees[engine] = tree;
            engine.SemanticsChanged += tree.Refresh;
            tree.Refresh();
            return tree;
        }

        public static bool TryGet(Component text, out LetteraSemanticTree tree) => s_Trees.TryGetValue(((ITextComponent)text).SharedEngine, out tree);

        internal static bool TryGetFor(TextEngine engine, out LetteraSemanticTree tree)
        {
            if (s_Trees.Count != 0) return s_Trees.TryGetValue(engine, out tree);
            tree = null;
            return false;
        }

        internal static void Forget(TextEngine engine)
        {
            if (s_Trees.TryGetValue(engine, out LetteraSemanticTree tree)) engine.SemanticsChanged -= tree.Refresh;
            s_Trees.Remove(engine);
        }

        /// <summary>Live nodes in reading order.</summary>
        public IReadOnlyList<LetteraSemanticNode> Nodes => m_Nodes;

        /// <summary>One node was added, updated (true) or removed (false, with its id).</summary>
        public event Action<int, bool> Changed;

        /// <summary>A rebuild that changed nodes finished.</summary>
        public event Action Committed;

        /// <summary>Handlers run first and can take over an action: return true to stop the built-in one.</summary>
        public event Func<int, LetteraSemanticActions, bool> ActionAsked;

        internal void Refresh()
        {
            m_Engine.EnsurePositioned();
            var seen = new HashSet<int>();
            bool any = false;
            IReadOnlyList<LetteraAttributes.SemanticRequest> requests = m_Engine.SemanticRequests;
            var fresh = new List<LetteraSemanticNode>(requests.Count);
            ReadOnlySpan<int> text = m_Engine.ShownCodepoints;
            for (int r = 0; r < requests.Count; r++)
            {
                LetteraAttributes.SemanticRequest request = requests[r];
                int id = m_Engine.RangeIdOf(request.Range);
                bool isNew = !m_ById.TryGetValue(id, out LetteraSemanticNode node);
                if (isNew)
                {
                    node = new LetteraSemanticNode { Id = id };
                    m_ById[id] = node;
                }
                seen.Add(id);
                SemanticEffect effect = request.Effect;
                node.Start = request.Start;
                node.End = request.End;
                node.Role = effect.Role;
                node.Value = effect.Value;
                node.Hint = effect.Hint;
                node.Language = !string.IsNullOrEmpty(effect.Language) ? effect.Language : m_Engine.LanguageAt(request.Start);
                node.Level = effect.Level;
                node.Actions = effect.Actions;
                ComposeFrom(node, request);
                string label = !string.IsNullOrEmpty(effect.Label) ? effect.Label : DerivedLabel(text, request.Start, request.End, node);
                bool changed = isNew || label != node.Label;
                node.Label = label;
                node.Segments.Clear();
                m_Engine.GetRangeRects(request.Start, request.End, node.Segments);
                fresh.Add(node);
                if (changed)
                {
                    any = true;
                    Changed?.Invoke(id, true);
                }
            }
            var gone = new List<int>();
            foreach (int id in m_ById.Keys)
                if (!seen.Contains(id))
                    gone.Add(id);
            foreach (int id in gone)
            {
                m_ById.Remove(id);
                any = true;
                Changed?.Invoke(id, false);
            }
            fresh.Sort((a, b) => a.Start.CompareTo(b.Start));
            m_Nodes.Clear();
            m_Nodes.AddRange(fresh);
            foreach (LetteraSemanticNode node in m_Nodes)
            {
                node.ParentId = 0;
                int best = int.MaxValue;
                foreach (LetteraSemanticNode other in m_Nodes)
                    if (other != node && other.Start <= node.Start && other.End >= node.End && other.End - other.Start < best)
                    {
                        best = other.End - other.Start;
                        node.ParentId = other.Id;
                    }
            }
            RefreshStates();
            if (any) Committed?.Invoke();
        }

        void ComposeFrom(LetteraSemanticNode node, in LetteraAttributes.SemanticRequest request)
        {
            foreach (TextEngine.ActiveRange range in m_Engine.ActiveRanges)
            {
                if (range.Start > request.Start || range.End < request.End) continue;
                if (node.Role == LetteraSemanticRole.Text && range.Effect is LinkEffect) node.Role = LetteraSemanticRole.Link;
                if (range.Effect is LinkEffect || range.Effect is ActionEffect) node.Actions |= LetteraSemanticActions.Activate | LetteraSemanticActions.Context;
                if (range.Effect is SpoilerEffect) node.Actions |= LetteraSemanticActions.Activate;
            }
            if (node.Role == LetteraSemanticRole.Text && request.End - request.Start == 1 && m_Engine.ShownCodepoints[request.Start] == 0xFFFC) node.Role = LetteraSemanticRole.Image;
        }

        string DerivedLabel(ReadOnlySpan<int> text, int start, int end, LetteraSemanticNode node)
        {
            for (int c = start; c < end && c < text.Length; c++)
                if (m_Engine.IsConcealed(c))
                    return "Hidden text";
            var builder = new System.Text.StringBuilder(end - start);
            for (int c = start; c < end && c < text.Length; c++)
            {
                if (text[c] == 0xFFFC)
                {
                    string alt = m_Engine.InlineAltAt(c);
                    if (!string.IsNullOrEmpty(alt)) builder.Append(alt);
                    continue;
                }
                builder.Append(char.ConvertFromUtf32(text[c]));
            }
            return builder.ToString();
        }

        /// <summary>Reads hover, press, focus and covers again, with no parse.</summary>
        public void RefreshStates()
        {
            LetteraRangeRouter router = LetteraRangeRouter.Of(m_Text);
            foreach (LetteraSemanticNode node in m_Nodes)
            {
                LetteraSemanticStates states = LetteraSemanticStates.None;
                foreach (TextEngine.ActiveRange range in m_Engine.ActiveRanges)
                {
                    if (range.Start > node.Start || range.End < node.End) continue;
                    if (router.GetState(range.Id) == LetteraRangeState.Hovered) states |= LetteraSemanticStates.Hovered;
                    if (router.GetState(range.Id) == LetteraRangeState.Pressed) states |= LetteraSemanticStates.Pressed;
                    if (router.GetState(range.Id) == LetteraRangeState.Disabled) states |= LetteraSemanticStates.Disabled;
                    if (router.Focused == range.Id) states |= LetteraSemanticStates.Focused;
                    if (range.Effect is SpoilerEffect && !router.IsUncovered(range.Id)) states |= LetteraSemanticStates.Concealed;
                }
                if (states == node.States) continue;
                node.States = states;
                Changed?.Invoke(node.Id, true);
            }
        }

        /// <summary>Runs one declared action. False when the node does not declare it.</summary>
        public bool RunAction(int id, LetteraSemanticActions action)
        {
            if (!m_ById.TryGetValue(id, out LetteraSemanticNode node) || (node.Actions & action) == 0) return false;
            if (ActionAsked != null)
                foreach (Func<int, LetteraSemanticActions, bool> handler in ActionAsked.GetInvocationList())
                    if (handler(id, action))
                        return true;
            LetteraRangeRouter router = LetteraRangeRouter.Of(m_Text);
            foreach (TextEngine.ActiveRange range in m_Engine.ActiveRanges)
            {
                if (range.Start > node.Start || range.End < node.End) continue;
                if (action == LetteraSemanticActions.Activate) router.RaiseActivate(range.Id, true);
                else if (action == LetteraSemanticActions.Context) router.RaiseMenu(range.Id);
                else if (action == LetteraSemanticActions.Focus) router.FocusRange(range.Id);
                else if (action == LetteraSemanticActions.Dismiss) router.ClearFocus();
                return true;
            }
            return true;
        }
    }
}
