using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera
{
    public enum LetteraInlineHits { Include, Exclude, Only }
    public enum LetteraRangeState { Idle, Hovered, Pressed, Disabled }
    public enum LetteraRangeEventKind { Entered, Exited, Pressed, Released, Activated, MenuRequested, PressHoldProgress, StateChanged, FocusChanged, Gesture }
    public enum LetteraRangeCursor { Default, Hand, IBeam, Custom }

    /// <summary>What decides whether a press goes to this text at all.</summary>
    public enum LetteraRoute
    {
        /// <summary>The whole rect takes a press, and what no range takes goes on to the parents.</summary>
        Auto,
        /// <summary>The whole rect takes a press and keeps it.</summary>
        Normal,
        /// <summary>Only the ranges hit: elsewhere the text is not there, and what lies behind it gets the press.</summary>
        Ranges,
        /// <summary>The ranges get a press, and so does what lies behind.</summary>
        Observe,
        /// <summary>Nothing hits.</summary>
        None,
    }

    /// <summary>A name for a group of ranges to listen to together.</summary>
    [CreateAssetMenu(fileName = "Range Topic", menuName = "Lettera/Range Topic", order = 94)]
    public class LetteraRangeTopic : ScriptableObject { }

    /// <summary>An event on a range. The router reuses it: copy what you need before returning.</summary>
    public sealed class LetteraRangeEvent
    {
        public LetteraRangeEventKind Kind { get; internal set; }
        public int Id { get; internal set; }
        public int Start { get; internal set; }
        public int End { get; internal set; }
        public string Primary { get; internal set; }
        public string Label { get; internal set; }
        public LetteraRangeTopic Topic { get; internal set; }
        public LetteraRangeEffect Effect { get; internal set; }
        public LetteraPointerKind PointerKind { get; internal set; }
        public bool ViaKeyboard { get; internal set; }
        public bool ViaAssistive { get; internal set; }
        public PointerEventData.InputButton Button { get; internal set; }
        public Vector2 ScreenPoint { get; internal set; }
        public float Progress { get; internal set; }

        /// <summary>A press ended by losing it, not by letting go.</summary>
        public bool Cancelled { get; internal set; }

        internal bool Stopped, DefaultPrevented;

        /// <summary>Ends delivery of this event.</summary>
        public void Stop() => Stopped = true;

        /// <summary>Skips what the effect does by default, such as opening a link.</summary>
        public void PreventDefault() => DefaultPrevented = true;
    }

    /// <summary>The events of a set of ranges.</summary>
    public sealed class LetteraRangeEvents
    {
        public event Action<LetteraRangeEvent> Entered, Exited, Pressed, Released, Activated, MenuRequested, PressHoldProgress, StateChanged, FocusChanged, Any;

        internal void Raise(LetteraRangeEvent e)
        {
            Any?.Invoke(e);
            if (e.Stopped) return;
            switch (e.Kind)
            {
                case LetteraRangeEventKind.Entered: Entered?.Invoke(e); break;
                case LetteraRangeEventKind.Exited: Exited?.Invoke(e); break;
                case LetteraRangeEventKind.Pressed: Pressed?.Invoke(e); break;
                case LetteraRangeEventKind.Released: Released?.Invoke(e); break;
                case LetteraRangeEventKind.Activated: Activated?.Invoke(e); break;
                case LetteraRangeEventKind.MenuRequested: MenuRequested?.Invoke(e); break;
                case LetteraRangeEventKind.PressHoldProgress: PressHoldProgress?.Invoke(e); break;
                case LetteraRangeEventKind.StateChanged: StateChanged?.Invoke(e); break;
                case LetteraRangeEventKind.FocusChanged: FocusChanged?.Invoke(e); break;
            }
        }

        internal bool HasActivated => Activated != null || Any != null;
    }

    /// <summary>
    /// A range that takes pointer and keyboard input. It has no look of its own: a look comes from composing it with color, underline or highlight,
    /// as the default a tag does. Slots after a type's own: priority, hit padding in em, focusable.
    /// </summary>
    [Serializable]
    public abstract class LetteraRangeEffect : LetteraEffect
    {
        [SerializeField] bool m_Enabled = true;
        [SerializeField] int m_Priority;
        [Tooltip("Extra room around the text that still hits, in em.")]
        [SerializeField, Min(0)] float m_HitPadding;
        [SerializeField] bool m_Focusable = true;
        [SerializeField] LetteraRangeTopic m_Topic;
        [SerializeField] LetteraRangeCursor m_Cursor = LetteraRangeCursor.Hand;
        [SerializeField] LetteraInlineHits m_InlineHits;
        [SerializeReference] List<LetteraRangeRule> m_Rules = new List<LetteraRangeRule>();

        readonly LetteraRangeEvents m_Events = new LetteraRangeEvents();

        public bool Enabled { get => m_Enabled; set => m_Enabled = value; }
        public int Priority { get => m_Priority; set => m_Priority = value; }
        public float HitPadding { get => m_HitPadding; set => m_HitPadding = value; }
        public bool Focusable { get => m_Focusable; set => m_Focusable = value; }
        public LetteraRangeTopic Topic { get => m_Topic; set => m_Topic = value; }
        public LetteraRangeCursor Cursor { get => m_Cursor; set => m_Cursor = value; }
        public LetteraInlineHits InlineHits { get => m_InlineHits; set => m_InlineHits = value; }
        public List<LetteraRangeRule> Rules => m_Rules;

        /// <summary>Events of every range of this effect. Handlers of a topic run first.</summary>
        public LetteraRangeEvents Events => m_Events;

        /// <summary>Called for events no handler stopped.</summary>
        public virtual void OnEvent(LetteraRangeEvent e) { }

        /// <summary>The cancelable default action, which runs last.</summary>
        public virtual void OnDefault(LetteraRangeEvent e) { }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.AddActive(this, start, end, slots[0]);

        public override LetteraEffect Clone()
        {
            var copy = (LetteraRangeEffect)base.Clone();
            copy.m_Rules = new List<LetteraRangeRule>(m_Rules);
            return copy;
        }
    }

    /// <summary>Raises events and does nothing else: mentions, hashtags and your own actions. The range's first slot is its value.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class ActionEffect : LetteraRangeEffect
    {
        public override bool ExtendsOnType => false;
    }

    public enum LetteraSpoilerCopy { Mask, Skip, Keep }

    /// <summary>Text under a cover until someone taps it. Covered glyphs do not draw, so nothing shows through.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SpoilerEffect : LetteraRangeEffect
    {
        [SerializeField] Color32 m_CoverColor = new Color32(70, 72, 80, 255);
        [SerializeField, Min(0)] float m_FadeSeconds = 0.2f;
        [SerializeField] LetteraSpoilerCopy m_CopyPolicy;

        public Color32 CoverColor { get => m_CoverColor; set => m_CoverColor = value; }
        public float FadeSeconds { get => m_FadeSeconds; set => m_FadeSeconds = value; }
        public LetteraSpoilerCopy CopyPolicy { get => m_CopyPolicy; set => m_CopyPolicy = value; }

        public override bool ExtendsOnType => false;

        /// <summary>A cover changed: range id, and whether it is off now.</summary>
        public event Action<int, bool> CoverChanged;

        internal void RaiseCover(int id, bool uncovered) => CoverChanged?.Invoke(id, uncovered);

        public override void OnDefault(LetteraRangeEvent e)
        {
            if (e.Kind == LetteraRangeEventKind.Activated) LetteraRangeRouter.Toggle(this, e.Id);
        }
    }

    /// <summary>A pose-level tint for feedback such as hover, which costs no mesh pass. Slot: weight, 0 none to 1 the full color.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class RangeTintEffect : LetteraMotionEffect
    {
        [SerializeField] Color m_Color = new Color(0.8f, 0.9f, 1, 1);
        [SerializeField, Range(0, 1)] float m_Weight;

        static readonly LetteraParamInfo[] s_Params = { new LetteraParamInfo("Weight", 0, LetteraParamKind.Float, DirtyLevel.Pose, true, 0, 1) };

        public RangeTintEffect()
        {
            Clock = LetteraClock.Manual;
            Unit = LetteraMotionUnit.Range;
            // Feedback, not motion: it stays under a reduced-motion preference.
            ReducedMotionPolicy = LetteraReducedMotion.Keep;
        }

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public Color Color { get => m_Color; set { m_Color = value; Version++; } }
        public float Weight { get => m_Weight; set { m_Weight = value; Version++; } }

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v) => v.V0 = Slot(s, 0, m_Weight);
        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p) => p.Tint *= Color.LerpUnclamped(Color.white, m_Color, c.Values.V0);
        public override float MaxReachEm(in LetteraMotionValues v) => 0;
    }

    /// <summary>The input of one text's ranges. It exists only for a text that has some, and lets through everything else.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class LetteraRangeInput : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        LetteraRangeRouter m_Router;
        GameObject m_Forwarded;

        static readonly List<RaycastResult> s_Results = new List<RaycastResult>();

        internal LetteraRangeRouter Router => m_Router;
        internal void Bind(LetteraRangeRouter router) => m_Router = router;

        // What lies behind this text under the pointer, for a route that shares its press.
        GameObject Behind(PointerEventData data)
        {
            if (EventSystem.current == null) return null;
            s_Results.Clear();
            EventSystem.current.RaycastAll(data, s_Results);
            bool past = false;
            foreach (RaycastResult result in s_Results)
            {
                if (result.gameObject == gameObject) past = true;
                else if (past && !result.gameObject.transform.IsChildOf(transform)) return result.gameObject;
            }
            return null;
        }

        bool HasSelectable => GetComponent<LetteraSelectable>() != null;

        public void OnPointerEnter(PointerEventData data) => m_Router?.PointerInside(data, true);
        public void OnPointerExit(PointerEventData data) => m_Router?.PointerInside(data, false);
        public void OnPointerMove(PointerEventData data) => m_Router?.PointerMoved(data);

        public void OnPointerDown(PointerEventData data)
        {
            if (m_Router == null || HasSelectable) return;
            bool taken = m_Router.Press(data);
            if (m_Router.Route == LetteraRoute.Observe)
            {
                GameObject behind = Behind(data);
                m_Forwarded = behind != null ? ExecuteEvents.ExecuteHierarchy(behind, data, ExecuteEvents.pointerDownHandler) : null;
                return;
            }
            if (taken || m_Router.Route == LetteraRoute.Normal) return;
            // Nothing here took it, so it goes where it would have gone without this text.
            Transform parent = transform.parent;
            m_Forwarded = parent != null ? ExecuteEvents.ExecuteHierarchy(parent.gameObject, data, ExecuteEvents.pointerDownHandler) : null;
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (m_Router == null || HasSelectable) return;
            m_Router.Release(data);
            if (m_Forwarded != null) ExecuteEvents.Execute(m_Forwarded, data, ExecuteEvents.pointerUpHandler);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (m_Router == null || HasSelectable) return;
            if (m_Forwarded != null) ExecuteEvents.Execute(m_Forwarded, data, ExecuteEvents.pointerClickHandler);
            m_Forwarded = null;
        }
    }

    /// <summary>The pointer and keyboard of a text's ranges: one per text with any.</summary>
    public sealed class LetteraRangeRouter : IMotionClient
    {
        static readonly Dictionary<TextEngine, LetteraRangeRouter> s_Routers = new Dictionary<TextEngine, LetteraRangeRouter>();

        readonly ITextComponent m_Text;
        readonly TextEngine m_Engine;
        readonly LetteraRangeEvents m_All = new LetteraRangeEvents();
        readonly Dictionary<LetteraRangeTopic, LetteraRangeEvents> m_Topics = new Dictionary<LetteraRangeTopic, LetteraRangeEvents>();
        readonly Dictionary<int, LetteraRangeState> m_States = new Dictionary<int, LetteraRangeState>();
        readonly HashSet<int> m_Disabled = new HashSet<int>();
        readonly LetteraRangeEvent m_Event = new LetteraRangeEvent();
        readonly List<int> m_Nest = new List<int>(), m_Focusable = new List<int>();
        readonly RulesRuntime m_Rules;
        int m_Hovered, m_Pressed, m_Focused;
        bool m_FocusFromKey, m_Inside, m_InClientList, m_Claimed, m_Dragged;
        Vector2 m_LastPointer, m_PressPoint;
        Camera m_Camera;
        LetteraPointerKind m_Kind;
        double m_PressTime;
        bool m_HoldEnded;
        int m_PressPointerId = int.MinValue;

        internal LetteraRangeRouter(ITextComponent text)
        {
            m_Text = text;
            m_Engine = text.SharedEngine;
            m_Engine.RangeRetired += OnRetired;
            m_Rules = new RulesRuntime(this, text);
        }

        /// <summary>The router of a text, made on first need.</summary>
        public static LetteraRangeRouter Of(Component text)
        {
            var component = text as ITextComponent;
            if (component == null) throw new ArgumentException("Expected a LetteraText or a LetteraWorldText.", nameof(text));
            return Of(component);
        }

        internal static LetteraRangeRouter Of(ITextComponent text)
        {
            TextEngine engine = text.SharedEngine;
            if (s_Routers.TryGetValue(engine, out LetteraRangeRouter router)) return router;
            router = new LetteraRangeRouter(text);
            s_Routers[engine] = router;
            return router;
        }

        internal static void Forget(TextEngine engine)
        {
            if (s_Routers.TryGetValue(engine, out LetteraRangeRouter router))
            {
                LetteraMotionHub.Remove(router);
                if (router.m_Hovered != 0) LetteraCursors.Set(LetteraRangeCursor.Default);
                s_Routers.Remove(engine);
            }
            LetteraSemanticTree.Forget(engine);
            LetteraInlineObjects.Forget(engine);
        }

        internal TextEngine Engine => m_Engine;
        internal ITextComponent Text => m_Text;

        /// <summary>How the text takes a press.</summary>
        internal LetteraRoute Route => m_Text is LetteraText canvas ? canvas.Route : m_Text is LetteraWorldText world ? world.Route : LetteraRoute.Auto;

        /// <summary>Puts the hidden input component on a text with ranges. Play mode only.</summary>
        internal static void EnsureInput(Component owner)
        {
            var input = owner.GetComponent<LetteraRangeInput>();
            if (input == null)
            {
                input = owner.gameObject.AddComponent<LetteraRangeInput>();
                input.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
            }
            input.Bind(Of((ITextComponent)owner));
        }

        /// <summary>The text went away for now: no press survives it.</summary>
        internal static void NotifyDisabled(TextEngine engine)
        {
            if (!s_Routers.TryGetValue(engine, out LetteraRangeRouter router)) return;
            router.CancelPress();
            router.m_Inside = false;
            router.UpdateHover();
        }

        /// <summary>Events of every range of this text.</summary>
        public LetteraRangeEvents All => m_All;

        /// <summary>Events of the ranges of one topic.</summary>
        public LetteraRangeEvents Get(LetteraRangeTopic topic)
        {
            if (!m_Topics.TryGetValue(topic, out LetteraRangeEvents events))
            {
                events = new LetteraRangeEvents();
                m_Topics[topic] = events;
            }
            return events;
        }

        public int Hovered => m_Hovered;
        public int Focused => m_Focused;
        internal bool Claimed => m_Claimed;
        internal bool FocusIsFromKey => m_FocusFromKey;

        public LetteraRangeState GetState(int id)
        {
            if (m_Disabled.Contains(id)) return LetteraRangeState.Disabled;
            return m_States.TryGetValue(id, out LetteraRangeState state) ? state : LetteraRangeState.Idle;
        }

        public void SetDisabled(int id, bool disabled)
        {
            if (disabled) m_Disabled.Add(id);
            else m_Disabled.Remove(id);
            m_Rules.SignalChanged(id);
        }

        internal bool IsDisabled(int id) => m_Disabled.Contains(id);

        // Whoever heard a range was entered or pressed hears it end too, also when it ended by leaving the text.
        void OnRetired(int id)
        {
            if (m_Pressed == id)
            {
                m_Event.Cancelled = true;
                Dispatch(id, LetteraRangeEventKind.Released);
                m_Event.Cancelled = false;
                m_Pressed = 0;
                m_Claimed = false;
                LetteraLinks.DisarmPress();
            }
            if (m_Hovered == id)
            {
                Dispatch(id, LetteraRangeEventKind.Exited);
                m_Hovered = 0;
                UpdateCursor();
            }
            if (m_Focused == id)
            {
                m_Focused = 0;
                DrawRing();
            }
            m_States.Remove(id);
            m_Disabled.Remove(id);
            m_Rules.Retire(id);
        }

        static float TouchPadding(LetteraPointerKind kind) => kind == LetteraPointerKind.Touch ? LetteraEditingSettings.Dp(LetteraSettings.Instance.Motion.TouchHitPadding) : 0;

        int HitAt(Vector2 screen, Camera camera, LetteraPointerKind kind)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Text.TextRect, screen, camera, out Vector2 local)) return 0;
            int id = m_Engine.HitTestActive(local, TouchPadding(kind));
            return m_Disabled.Contains(id) ? 0 : id;
        }

        /// <summary>The range at a screen point, or 0.</summary>
        public int HitTest(Vector2 screenPoint, Camera camera) => HitAt(screenPoint, camera, LetteraPointerKind.Mouse);

        // ------------------------------------------------------------- pointer

        internal void PointerInside(PointerEventData data, bool inside)
        {
            m_Kind = LetteraSelectable.Classify(data);
            if (m_Kind == LetteraPointerKind.Touch) return;
            m_Inside = inside;
            m_LastPointer = data.position;
            m_Camera = data.enterEventCamera;
            if (inside && !m_InClientList)
            {
                m_InClientList = true;
                LetteraMotionHub.Add(this);
            }
            UpdateHover();
        }

        internal void PointerMoved(PointerEventData data)
        {
            if (LetteraSelectable.Classify(data) == LetteraPointerKind.Touch) return;
            m_LastPointer = data.position;
            if (data.enterEventCamera != null) m_Camera = data.enterEventCamera;
        }

        // Hover follows every frame, so a range moving under a still pointer updates too.
        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            if (m_Inside) UpdateHover();
            TickHold();
            m_Rules.Tick(unscaled, scaled);
            bool keep = m_Inside || m_Pressed != 0 || m_Rules.Busy;
            if (!keep) m_InClientList = false;
            return keep;
        }

        void EnsureTicking()
        {
            if (m_InClientList) return;
            m_InClientList = true;
            LetteraMotionHub.Add(this);
        }

        internal void Tick(float unscaled, float scaled) => ((IMotionClient)this).TickMotion(scaled, unscaled);

        void UpdateHover()
        {
            int id = m_Inside ? HitAt(m_LastPointer, m_Camera, m_Kind) : 0;
            if (id == m_Hovered) return;
            int old = m_Hovered;
            m_Hovered = id;
            if (old != 0)
            {
                SetState(old, old == m_Pressed ? LetteraRangeState.Pressed : LetteraRangeState.Idle);
                Dispatch(old, LetteraRangeEventKind.Exited);
            }
            if (id != 0)
            {
                SetState(id, LetteraRangeState.Hovered);
                Dispatch(id, LetteraRangeEventKind.Entered);
            }
            UpdateCursor();
        }

        void SetState(int id, LetteraRangeState state)
        {
            LetteraRangeState old = GetState(id);
            if (state == LetteraRangeState.Idle) m_States.Remove(id);
            else m_States[id] = state;
            if (old == state) return;
            m_Rules.SignalChanged(id);
            EnsureTicking();
            if (m_Engine.TryGetActive(id, out _)) Dispatch(id, LetteraRangeEventKind.StateChanged);
            if (LetteraSemanticTree.TryGetFor(m_Engine, out LetteraSemanticTree tree)) tree.RefreshStates();
        }

        void UpdateCursor()
        {
            LetteraRangeCursor cursor = LetteraRangeCursor.Default;
            if (m_Hovered != 0 && m_Engine.TryGetActive(m_Hovered, out TextEngine.ActiveRange range)) cursor = range.Effect.Cursor;
            // A focused field keeps its own, until the modifier that would follow the link is down.
            if (cursor != LetteraRangeCursor.Default && m_Text is Component component)
            {
                var editable = component.GetComponent<LetteraEditable>();
                if (editable != null && !editable.ReadOnly && !LetteraSelectable.IsLinkModifierHeld) cursor = LetteraRangeCursor.IBeam;
            }
            LetteraCursors.Set(cursor);
        }

        /// <summary>True when a range took this press.</summary>
        internal bool Press(PointerEventData data, bool primaryHeld = false)
        {
            if (m_Pressed != 0 && data.pointerId != m_PressPointerId)
            {
                // A second finger ends the first press, with no activation.
                CancelPress();
                return true;
            }
            m_Kind = LetteraSelectable.Classify(data);
            Camera camera = data.pressEventCamera != null ? data.pressEventCamera : data.enterEventCamera;
            int id = HitAt(data.position, camera, m_Kind);
            if (id == 0) return false;
            if (data.button == PointerEventData.InputButton.Middle) return false;
            FillPointer(data);
            ClearFocusRing();
            if (data.button == PointerEventData.InputButton.Right)
            {
                Dispatch(id, LetteraRangeEventKind.MenuRequested);
                return m_Event.Stopped;
            }
            m_Pressed = id;
            m_Claimed = true;
            m_Dragged = false;
            m_HoldEnded = false;
            m_PressPoint = data.position;
            m_PressPointerId = data.pointerId;
            m_PressTime = LetteraEditClock.Now;
            m_Camera = camera;
            SetState(id, LetteraRangeState.Pressed);
            m_HoldBySelectable = primaryHeld;
            Dispatch(id, LetteraRangeEventKind.Pressed);
            if (m_Pressed == id && m_Engine.TryGetActive(id, out TextEngine.ActiveRange range)) LetteraLinks.ArmPress(range, this, camera);
            EnsureTicking();
            return true;
        }

        bool m_HoldBySelectable;

        // A selectable keeps the clock of a long press itself, since what follows it is partly its own.
        internal void HoldProgress(float progress)
        {
            if (m_Pressed == 0) return;
            m_Event.Progress = progress;
            Dispatch(m_Pressed, LetteraRangeEventKind.PressHoldProgress);
        }

        /// <summary>A long press came to its end. True when a listener kept the menu to itself.</summary>
        internal bool HoldEnded()
        {
            if (m_Pressed == 0) return false;
            m_HoldEnded = true;
            Dispatch(m_Pressed, LetteraRangeEventKind.MenuRequested);
            return m_Event.Stopped;
        }

        void TickHold()
        {
            if (m_Pressed == 0 || m_Kind != LetteraPointerKind.Touch || m_HoldEnded || m_HoldBySelectable) return;
            float seconds = LetteraSettings.Instance.Editing.LongPressSeconds;
            float progress = seconds > 0 ? (float)((LetteraEditClock.Now - m_PressTime) / seconds) : 1;
            if (progress < 1)
            {
                m_Event.Progress = progress;
                Dispatch(m_Pressed, LetteraRangeEventKind.PressHoldProgress);
                return;
            }
            m_HoldEnded = true;
            Dispatch(m_Pressed, LetteraRangeEventKind.MenuRequested);
        }

        /// <summary>A drag past the slop: the press is off, and what scrolls or selects goes on.</summary>
        internal bool DragTo(PointerEventData data)
        {
            if (m_Pressed == 0) return false;
            float slop = LetteraEditingSettings.Dp(LetteraSettings.Instance.Editing.DragSlop);
            if ((data.position - m_PressPoint).magnitude <= slop) return false;
            CancelPress();
            return true;
        }

        internal void CancelPress()
        {
            if (m_Pressed == 0) return;
            int id = m_Pressed;
            m_Pressed = 0;
            m_Claimed = false;
            LetteraLinks.DisarmPress();
            SetState(id, id == m_Hovered ? LetteraRangeState.Hovered : LetteraRangeState.Idle);
            m_Event.Cancelled = true;
            Dispatch(id, LetteraRangeEventKind.Released);
            m_Event.Cancelled = false;
        }

        internal void Release(PointerEventData data)
        {
            if (m_Pressed == 0 || data.pointerId != m_PressPointerId) return;
            int id = m_Pressed;
            m_Pressed = 0;
            m_Claimed = false;
            FillPointer(data);
            bool over = HitAt(data.position, m_Camera, m_Kind) == id;
            SetState(id, id == m_Hovered ? LetteraRangeState.Hovered : LetteraRangeState.Idle);
            Dispatch(id, LetteraRangeEventKind.Released);
            if (over && !m_HoldEnded) Activate(id, false);
            else LetteraLinks.DisarmPress();
        }

        internal void Activate(int id, bool keyboard)
        {
            m_Event.ViaKeyboard = keyboard;
            Dispatch(id, LetteraRangeEventKind.Activated);
            m_Event.ViaKeyboard = false;
            m_Rules.Trigger(id, LetteraRangeEventKind.Activated);
        }

        internal bool RaiseMenu(int id)
        {
            Dispatch(id, LetteraRangeEventKind.MenuRequested);
            return m_Event.Stopped;
        }

        internal void RaiseActivate(int id, bool assistive)
        {
            m_Event.ViaAssistive = assistive;
            Activate(id, false);
            m_Event.ViaAssistive = false;
        }

        void FillPointer(PointerEventData data)
        {
            m_Event.PointerKind = LetteraSelectable.Classify(data);
            m_Event.Button = data.button;
            m_Event.ScreenPoint = data.position;
        }

        // Outer ranges capture, the target takes it, and they hear it again on its way back.
        void Dispatch(int id, LetteraRangeEventKind kind)
        {
            if (!m_Engine.TryGetActive(id, out TextEngine.ActiveRange target)) return;
            LetteraRangeEvent e = m_Event;
            e.Kind = kind;
            e.Id = id;
            e.Start = target.Start;
            e.End = target.End;
            e.Primary = target.Primary;
            e.Label = target.Label;
            e.Topic = target.Effect.Topic;
            e.Effect = target.Effect;
            e.Stopped = false;
            e.DefaultPrevented = false;

            m_Engine.NestOf(target, m_Nest);
            for (int n = 0; n < m_Nest.Count && !e.Stopped; n++) DeliverTo(m_Nest[n]);
            if (!e.Stopped) DeliverTo(id);
            for (int n = m_Nest.Count - 1; n >= 0 && !e.Stopped; n--) DeliverTo(m_Nest[n]);
            if (!e.Stopped) m_All.Raise(e);
            if (!e.DefaultPrevented) target.Effect.OnDefault(e);
        }

        void DeliverTo(int id)
        {
            if (!m_Engine.TryGetActive(id, out TextEngine.ActiveRange range)) return;
            if (range.Effect.Topic != null && m_Topics.TryGetValue(range.Effect.Topic, out LetteraRangeEvents events)) events.Raise(m_Event);
            if (m_Event.Stopped) return;
            range.Effect.Events.Raise(m_Event);
            if (m_Event.Stopped) return;
            range.Effect.OnEvent(m_Event);
        }

        // ------------------------------------------------------------- keyboard

        /// <summary>Moves keyboard focus to the next or previous range. False at the end, which clears it.</summary>
        public bool StepFocus(bool forward, LetteraRangeTopic topic = null, bool wrap = false)
        {
            m_Engine.EnsurePositioned();
            m_Engine.FocusableRanges(m_Focusable, topic);
            if (m_Focusable.Count == 0) return false;
            int at = m_Focusable.IndexOf(m_Focused);
            int next = at < 0 ? (forward ? 0 : m_Focusable.Count - 1) : at + (forward ? 1 : -1);
            if (next < 0 || next >= m_Focusable.Count)
            {
                if (!wrap)
                {
                    ClearFocus();
                    return false;
                }
                next = (next + m_Focusable.Count) % m_Focusable.Count;
            }
            SetFocus(m_Focusable[next], true);
            return true;
        }

        void SetFocus(int id, bool fromKey)
        {
            int old = m_Focused;
            m_Focused = id;
            m_FocusFromKey = fromKey && id != 0;
            if (old != 0) Dispatch(old, LetteraRangeEventKind.FocusChanged);
            if (id != 0) Dispatch(id, LetteraRangeEventKind.FocusChanged);
            m_Rules.SignalChanged(old);
            m_Rules.SignalChanged(id);
            DrawRing();
            FocusRingChanged?.Invoke();
            if (LetteraSemanticTree.TryGetFor(m_Engine, out LetteraSemanticTree tree)) tree.RefreshStates();
        }

        internal event Action FocusRingChanged;

        GameObject m_RingObject;
        LetteraSelectionOverlay m_RingOverlay;
        WorldSelectionVisual m_RingWorld;
        readonly List<Rect> m_RingRects = new List<Rect>(), m_RingBands = new List<Rect>();

        /// <summary>The bands drawn around the focused range. Tests read this.</summary>
        internal int FocusRingBandCount => m_RingBands.Count;

        // Only focus that came from a key shows, as a band around the range. A press clears it.
        void DrawRing()
        {
            m_RingRects.Clear();
            m_RingBands.Clear();
            if (m_FocusFromKey && m_Focused != 0) m_Engine.ActiveRectsOf(m_Focused, m_RingRects);
            const float width = 2;
            foreach (Rect r in m_RingRects)
            {
                m_RingBands.Add(Rect.MinMaxRect(r.xMin - width, r.yMin - width, r.xMax + width, r.yMin));
                m_RingBands.Add(Rect.MinMaxRect(r.xMin - width, r.yMax, r.xMax + width, r.yMax + width));
                m_RingBands.Add(Rect.MinMaxRect(r.xMin - width, r.yMin, r.xMin, r.yMax));
                m_RingBands.Add(Rect.MinMaxRect(r.xMax, r.yMin, r.xMax + width, r.yMax));
            }
            if (!Application.isPlaying) return;
            Color color = LetteraSettings.Instance.Motion.FocusColor;
            if (m_Text is LetteraWorldText world)
            {
                if (m_RingWorld == null)
                {
                    if (m_RingBands.Count == 0) return;
                    m_RingWorld = new WorldSelectionVisual(world, LetteraHighlightOrder.Above);
                }
                m_RingWorld.Show(m_RingBands, color);
                return;
            }
            if (m_RingOverlay == null)
            {
                if (m_RingBands.Count == 0) return;
                m_RingObject = new GameObject("Lettera Focus", typeof(RectTransform)) { hideFlags = SelectionVisuals.Flags };
                m_RingObject.layer = m_Text.TextRect.gameObject.layer;
                m_RingObject.transform.SetParent(m_Text.TextRect, false);
                m_RingObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
                m_RingOverlay = m_RingObject.AddComponent<LetteraSelectionOverlay>();
                m_RingOverlay.raycastTarget = false;
                CanvasSelectionVisual.FitInside(m_RingOverlay.rectTransform, m_Text.TextRect);
            }
            m_RingOverlay.Begin();
            foreach (Rect band in m_RingBands) m_RingOverlay.Add(band, color);
            m_RingOverlay.End();
        }

        public void ClearFocus() => SetFocus(0, false);

        internal void FocusRange(int id) => SetFocus(id, false);

        void ClearFocusRing()
        {
            if (!m_FocusFromKey) return;
            m_FocusFromKey = false;
            DrawRing();
            FocusRingChanged?.Invoke();
        }

        /// <summary>Runs the focused range, as Enter does.</summary>
        public bool ActivateFocused()
        {
            if (m_Focused == 0) return false;
            Activate(m_Focused, true);
            return true;
        }

        public bool MenuOnFocused()
        {
            if (m_Focused == 0) return false;
            Dispatch(m_Focused, LetteraRangeEventKind.MenuRequested);
            return m_Event.Stopped;
        }

        // ------------------------------------------------------------- signals

        internal bool Signal(int id, string signal)
        {
            switch (signal)
            {
                case LetteraSignals.Hovered: return GetState(id) == LetteraRangeState.Hovered || m_Hovered == id;
                case LetteraSignals.Pressed: return GetState(id) == LetteraRangeState.Pressed;
                case LetteraSignals.Focused: return m_Focused == id;
                case LetteraSignals.Disabled: return m_Disabled.Contains(id);
                default: return m_Rules.Custom(id, signal) != 0;
            }
        }

        internal float Scalar(int id, string signal) => Signal(id, signal) && !IsCustom(signal) ? 1 : m_Rules.Custom(id, signal);
        static bool IsCustom(string signal) => signal != LetteraSignals.Hovered && signal != LetteraSignals.Pressed && signal != LetteraSignals.Focused && signal != LetteraSignals.Disabled;

        /// <summary>Publishes a value of your own that rules can read. The same value again does nothing.</summary>
        public void Publish(int id, string signal, float value)
        {
            m_Rules.Publish(id, signal, value);
            EnsureTicking();
        }

        internal void RulesBusy() => EnsureTicking();

        // ------------------------------------------------------------- spoilers

        static LetteraRangeRouter FindOwner(LetteraRangeEffect effect, int id)
        {
            foreach (KeyValuePair<TextEngine, LetteraRangeRouter> pair in s_Routers)
                if (pair.Key.TryGetActive(id, out TextEngine.ActiveRange range) && range.Effect == effect)
                    return pair.Value;
            return null;
        }

        internal static void Toggle(SpoilerEffect effect, int id)
        {
            LetteraRangeRouter router = FindOwner(effect, id);
            if (router == null) return;
            router.SetUncovered(effect, id, !router.m_Engine.IsUncovered(id));
        }

        /// <summary>Whether a spoiler is off.</summary>
        public bool IsUncovered(int id) => m_Engine.IsUncovered(id);

        public void SetUncovered(int id, bool uncovered)
        {
            if (m_Engine.TryGetActive(id, out TextEngine.ActiveRange range) && range.Effect is SpoilerEffect spoiler) SetUncovered(spoiler, id, uncovered);
        }

        void SetUncovered(SpoilerEffect effect, int id, bool uncovered)
        {
            if (m_Engine.IsUncovered(id) == uncovered) return;
            m_Engine.SetUncovered(id, uncovered, effect.FadeSeconds);
            effect.RaiseCover(id, uncovered);
        }

        /// <summary>Puts every cover of this text back.</summary>
        public void CoverAll()
        {
            foreach (TextEngine.ActiveRange range in m_Engine.ActiveRanges)
                if (range.Effect is SpoilerEffect spoiler)
                    SetUncovered(spoiler, range.Id, false);
        }

        /// <summary>A debugger's view: live ranges and their state.</summary>
        public void Snapshot(List<LetteraRangeInfoEntry> results)
        {
            foreach (TextEngine.ActiveRange range in m_Engine.ActiveRanges)
                results.Add(new LetteraRangeInfoEntry { Id = range.Id, Start = range.Start, End = range.End, Primary = range.Primary, State = GetState(range.Id), Effect = range.Effect });
        }
    }

    /// <summary>One line of a router's snapshot.</summary>
    public struct LetteraRangeInfoEntry
    {
        public int Id, Start, End;
        public string Primary;
        public LetteraRangeState State;
        public LetteraRangeEffect Effect;
    }

    /// <summary>What a range's own signals are called, for rules.</summary>
    public static class LetteraSignals
    {
        public const string Hovered = "lettera.hovered", Pressed = "lettera.pressed", Focused = "lettera.focused", Disabled = "lettera.disabled";
    }

    // ------------------------------------------------------------- rules

    public enum LetteraEnvelopeKind { Instant, Transition, FollowSignal }
    public enum LetteraMix { Replace, Add, Multiply }

    /// <summary>When a rule is on.</summary>
    [Serializable]
    public abstract class LetteraRangeCondition
    {
        internal abstract bool Test(LetteraRangeRouter router, int id);
        internal virtual float Scalar(LetteraRangeRouter router, int id) => Test(router, id) ? 1 : 0;
    }

    [Serializable, UnityEngine.Scripting.Preserve]
    public class SignalIs : LetteraRangeCondition
    {
        public string Signal = LetteraSignals.Hovered;
        public SignalIs() { }
        public SignalIs(string signal) => Signal = signal;
        internal override bool Test(LetteraRangeRouter router, int id) => router.Signal(id, Signal);
    }

    [Serializable, UnityEngine.Scripting.Preserve]
    public class ScalarAtLeast : LetteraRangeCondition
    {
        public string Signal = "";
        public float Threshold = 0.5f;
        internal override bool Test(LetteraRangeRouter router, int id) => router.Scalar(id, Signal) >= Threshold;
        internal override float Scalar(LetteraRangeRouter router, int id) => router.Scalar(id, Signal);
    }

    [Serializable, UnityEngine.Scripting.Preserve]
    public class AllOf : LetteraRangeCondition
    {
        [SerializeReference] public List<LetteraRangeCondition> Items = new List<LetteraRangeCondition>();
        internal override bool Test(LetteraRangeRouter router, int id)
        {
            foreach (LetteraRangeCondition item in Items)
                if (item != null && !item.Test(router, id))
                    return false;
            return true;
        }
    }

    [Serializable, UnityEngine.Scripting.Preserve]
    public class AnyOf : LetteraRangeCondition
    {
        [SerializeReference] public List<LetteraRangeCondition> Items = new List<LetteraRangeCondition>();
        internal override bool Test(LetteraRangeRouter router, int id)
        {
            foreach (LetteraRangeCondition item in Items)
                if (item != null && item.Test(router, id))
                    return true;
            return false;
        }
    }

    [Serializable, UnityEngine.Scripting.Preserve]
    public class NotOf : LetteraRangeCondition
    {
        [SerializeReference] public LetteraRangeCondition Item;
        internal override bool Test(LetteraRangeRouter router, int id) => Item == null || !Item.Test(router, id);
    }

    /// <summary>One value a rule changes: a parameter of an effect in the same list, by its id.</summary>
    [Serializable]
    public class LetteraRangeChange
    {
        [SerializeReference] public LetteraEffect Target;
        public string Parameter = "";
        public float ToNumber;
        public Color32 ToColor = new Color32(255, 255, 255, 255);
        public bool IsColor;
        public LetteraMix Mix;
    }

    /// <summary>Changes parameters of a range while a condition holds, through holds: fields are never written, and at weight 0 nothing is left.</summary>
    [Serializable]
    public class LetteraRangeRule
    {
        [SerializeReference] public LetteraRangeCondition When = new SignalIs();
        public LetteraEnvelopeKind Envelope;
        [Min(0)] public float Enter = 0.15f, Exit = 0.15f;
        public LetteraEase Ease = LetteraEase.OutCubic;
        public LetteraClock Clock = LetteraClock.Unscaled;
        [SerializeReference] public List<LetteraRangeChange> Changes = new List<LetteraRangeChange>();
        public bool TriggerOnActivate;
        public int Priority;

        /// <summary>A rule started or ended on a range.</summary>
        public event Action<int, bool> Stepped;
        internal void RaiseStepped(int id, bool on) => Stepped?.Invoke(id, on);
    }

    // The weights of a text's rules, with the holds they write through.
    internal sealed class RulesRuntime
    {
        struct Key : IEquatable<Key>
        {
            public LetteraRangeRule Rule;
            public int Id;
            public bool Equals(Key o) => Rule == o.Rule && Id == o.Id;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => (Rule != null ? Rule.GetHashCode() : 0) * 397 ^ Id;
        }

        sealed class Live
        {
            public float Weight, Goal;
            public bool Pulse;
            public IDisposable[] Holds;
        }

        readonly LetteraRangeRouter m_Router;
        readonly ITextComponent m_Text;
        readonly Dictionary<Key, Live> m_Live = new Dictionary<Key, Live>();
        readonly Dictionary<(int, string), float> m_Custom = new Dictionary<(int, string), float>();
        readonly List<Key> m_Keys = new List<Key>();

        public RulesRuntime(LetteraRangeRouter router, ITextComponent text)
        {
            m_Router = router;
            m_Text = text;
        }

        public bool Busy
        {
            get
            {
                foreach (Live live in m_Live.Values)
                    if (live.Weight != live.Goal)
                        return true;
                return false;
            }
        }

        public float Custom(int id, string signal) => m_Custom.TryGetValue((id, signal), out float value) ? value : 0;

        public void Publish(int id, string signal, float value)
        {
            if (Custom(id, signal) == value) return;
            m_Custom[(id, signal)] = value;
            SignalChanged(id);
        }

        public void Retire(int id)
        {
            m_Keys.Clear();
            foreach (Key key in m_Live.Keys)
                if (key.Id == id)
                    m_Keys.Add(key);
            foreach (Key key in m_Keys)
            {
                Dispose(m_Live[key]);
                m_Live.Remove(key);
            }
        }

        static void Dispose(Live live)
        {
            if (live.Holds == null) return;
            foreach (IDisposable hold in live.Holds) hold?.Dispose();
            live.Holds = null;
        }

        public void SignalChanged(int id)
        {
            if (id == 0 || !m_Router.Engine.TryGetActive(id, out TextEngine.ActiveRange range)) return;
            foreach (LetteraRangeRule rule in range.Effect.Rules)
            {
                if (rule == null) continue;
                bool on = rule.When != null && rule.When.Test(m_Router, id);
                var key = new Key { Rule = rule, Id = id };
                if (!m_Live.TryGetValue(key, out Live live))
                {
                    if (!on) continue;
                    live = new Live();
                    m_Live[key] = live;
                }
                float goal = on ? 1 : 0;
                if (rule.Envelope == LetteraEnvelopeKind.FollowSignal) goal = rule.When.Scalar(m_Router, id);
                if (live.Goal == goal && live.Weight == goal) continue;
                live.Goal = goal;
                rule.RaiseStepped(id, on);
                if (rule.Envelope == LetteraEnvelopeKind.Instant || rule.Envelope == LetteraEnvelopeKind.FollowSignal || LetteraAccessibility.ReducedMotion)
                {
                    live.Weight = goal;
                    Write(key, live, range);
                }
                else
                {
                    m_Router.RulesBusy();
                }
            }
        }

        public void Trigger(int id, LetteraRangeEventKind kind)
        {
            if (!m_Router.Engine.TryGetActive(id, out TextEngine.ActiveRange range)) return;
            foreach (LetteraRangeRule rule in range.Effect.Rules)
            {
                if (rule == null || !rule.TriggerOnActivate) continue;
                var key = new Key { Rule = rule, Id = id };
                if (!m_Live.TryGetValue(key, out Live live))
                {
                    live = new Live();
                    m_Live[key] = live;
                }
                live.Goal = 1;
                live.Pulse = true;
                m_Router.RulesBusy();
            }
        }

        public void Tick(float unscaled, float scaled)
        {
            if (m_Live.Count == 0) return;
            m_Keys.Clear();
            foreach (Key key in m_Live.Keys) m_Keys.Add(key);
            foreach (Key key in m_Keys)
            {
                if (!m_Live.TryGetValue(key, out Live live)) continue;
                if (live.Weight == live.Goal)
                {
                    if (live.Pulse && live.Goal >= 1)
                    {
                        live.Goal = 0;
                        live.Pulse = false;
                    }
                    else
                    {
                        if (live.Weight <= 0) Finish(key, live);
                        continue;
                    }
                }
                LetteraRangeRule rule = key.Rule;
                float seconds = live.Goal > live.Weight ? rule.Enter : rule.Exit;
                float step = rule.Clock == LetteraClock.Scaled ? scaled : unscaled;
                live.Weight = seconds <= 0 ? live.Goal : Mathf.MoveTowards(live.Weight, live.Goal, step / seconds);
                if (m_Router.Engine.TryGetActive(key.Id, out TextEngine.ActiveRange range)) Write(key, live, range);
            }
        }

        void Finish(in Key key, Live live)
        {
            Dispose(live);
            m_Live.Remove(key);
        }

        void Write(in Key key, Live live, in TextEngine.ActiveRange range)
        {
            LetteraRangeRule rule = key.Rule;
            float weight = rule.Envelope == LetteraEnvelopeKind.Transition ? LetteraEntrance.Eased(rule.Ease, live.Weight) : live.Weight;
            if (live.Holds == null) live.Holds = new IDisposable[rule.Changes.Count];
            int id = key.Id;
            for (int c = 0; c < rule.Changes.Count; c++)
            {
                LetteraRangeChange change = rule.Changes[c];
                if (change?.Target == null) continue;
                LetteraParamInfo info = Find(change.Target, change.Parameter);
                if (info == null || info.Slot < 0) continue;
                LetteraHoldComposition composition = change.Mix == LetteraMix.Add ? LetteraHoldComposition.Add : change.Mix == LetteraMix.Multiply ? LetteraHoldComposition.Multiply : LetteraHoldComposition.Replace;
                if (change.IsColor)
                {
                    if (live.Holds[c] == null)
                    {
                        LetteraHold<Color32> made = new LetteraRangeQuery(m_Router.Engine, change.Target).Matching(r => r.Id == id).Hold<Color32>(info.Slot, composition, info.Level, rule.Priority);
                        made.Weight = weight;
                        made.Value = change.ToColor;
                        live.Holds[c] = made;
                    }
                    else ((LetteraHold<Color32>)live.Holds[c]).Weight = weight;
                }
                else
                {
                    if (live.Holds[c] == null)
                    {
                        LetteraHold<float> made = new LetteraRangeQuery(m_Router.Engine, change.Target).Matching(r => r.Id == id).Hold<float>(info.Slot, composition, info.Level, rule.Priority);
                        made.Weight = weight;
                        made.Value = change.ToNumber;
                        live.Holds[c] = made;
                    }
                    else ((LetteraHold<float>)live.Holds[c]).Weight = weight;
                }
            }
            if (live.Weight <= 0 && live.Goal <= 0) Finish(key, live);
        }

        static LetteraParamInfo Find(LetteraEffect effect, string id)
        {
            foreach (LetteraParamInfo info in effect.Parameters)
                if (info.Id == id)
                    return info;
            return null;
        }
    }

    /// <summary>What sets the pointer's look. On the web it is the canvas's CSS; elsewhere textures you provide in the settings.</summary>
    public interface ILetteraCursorBackend
    {
        void Set(LetteraRangeCursor cursor);
    }

    public static class LetteraCursors
    {
        /// <summary>Replaces the backend. Tests use this.</summary>
        public static ILetteraCursorBackend Backend;

        static LetteraRangeCursor s_Current;
        internal static LetteraRangeCursor Current => s_Current;

        public static void Set(LetteraRangeCursor cursor)
        {
            if (s_Current == cursor) return;
            s_Current = cursor;
            if (Backend != null)
            {
                Backend.Set(cursor);
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            LetteraCursor_Set(cursor == LetteraRangeCursor.Hand ? "pointer" : cursor == LetteraRangeCursor.IBeam ? "text" : "");
#else
            LetteraMotionSettings settings = LetteraSettings.Instance.Motion;
            Texture2D texture = cursor == LetteraRangeCursor.Hand ? settings.HandCursor : cursor == LetteraRangeCursor.IBeam ? settings.IBeamCursor : null;
            if (settings.CursorBackendEnabled && (texture != null || cursor == LetteraRangeCursor.Default)) Cursor.SetCursor(texture, Vector2.zero, CursorMode.Auto);
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void LetteraCursor_Set(string css);
#endif
    }

    /// <summary>Animation and interaction defaults of the project.</summary>
    [Serializable]
    public class LetteraMotionSettings
    {
        [Tooltip("Lowers how often a moving text uploads, on weak devices. 0 means every frame.")]
        [Min(0)] public int MaxFramesPerSecond;
        [Min(0.001f)] public float MaxStepSeconds = 0.25f;
        [Min(0)] public float TouchHitPadding = 8;
        public List<string> LinkSchemes = new List<string> { "http", "https", "mailto", "tel" };
        public LetteraInlineLibrary InlineLibrary;
        [Min(0)] public int MarqueeMaxVertices = 40000;
        public Color FocusColor = new Color(0.3f, 0.6f, 1f, 0.55f);
        public LetteraEntranceLibrary DefaultEntranceLibrary;
        public bool CursorBackendEnabled = true;
        public Texture2D HandCursor, IBeamCursor;
    }
}
