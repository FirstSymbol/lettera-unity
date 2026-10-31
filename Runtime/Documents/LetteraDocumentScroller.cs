using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    public enum LetteraScrollMovement { Elastic, Clamped }
    public enum LetteraScrollStart { Top, End }

    /// <summary>
    /// A viewport for a text: drag, wheel, inertia and elastic ends, as uGUI's Scroll Rect moves. It works on a document and on any plain Canvas text.
    /// Neither the text's rect nor this one grows to the content's size: the content moves by an offset, with no rebuild.
    /// </summary>
    [AddComponentMenu("Lettera/Document Scroller")]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class LetteraDocumentScroller : UIBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IMotionClient
    {
        [Tooltip("The text it moves. Empty looks among the children.")]
        [SerializeField] LetteraText m_Text;
        [SerializeField] LetteraScrollMovement m_MovementType;
        [SerializeField, Min(0)] float m_Elasticity = 0.1f;
        [SerializeField] bool m_Inertia = true;
        [SerializeField, Range(0, 1)] float m_DecelerationRate = 0.135f;
        [Tooltip("How many lines one wheel notch moves.")]
        [SerializeField, Min(0)] float m_ScrollSensitivity = 3;
        [SerializeField] Scrollbar m_VerticalScrollbar;
        [SerializeField] bool m_HideScrollbarsWhenUnused = true;
        [SerializeField] LetteraScrollStart m_StartAt;
        [Tooltip("At the end, stay at the end as the document grows.")]
        [SerializeField] bool m_StickToEnd = true;
        [Tooltip("A wheel this view cannot use goes on to an enclosing scroll view.")]
        [SerializeField] bool m_PassWheelAtEdges = true;

        double m_Position;
        float m_Velocity;
        bool m_Dragging, m_InHub, m_Bound, m_HandleDrag;
        Vector2 m_DragStartPoint;
        double m_DragStartPosition;
        LetteraDocumentView m_BoundView;

        public LetteraText Text { get => m_Text; set { Unbind(); m_Text = value; Bind(); } }
        public LetteraScrollMovement MovementType { get => m_MovementType; set => m_MovementType = value; }
        public float Elasticity { get => m_Elasticity; set => m_Elasticity = value; }
        public bool Inertia { get => m_Inertia; set => m_Inertia = value; }
        public float DecelerationRate { get => m_DecelerationRate; set => m_DecelerationRate = value; }
        public float ScrollSensitivity { get => m_ScrollSensitivity; set => m_ScrollSensitivity = value; }
        public Scrollbar VerticalScrollbar { get => m_VerticalScrollbar; set { m_VerticalScrollbar = value; RefreshBar(); } }
        public bool HideScrollbarsWhenUnused { get => m_HideScrollbarsWhenUnused; set { m_HideScrollbarsWhenUnused = value; RefreshBar(); } }
        public LetteraScrollStart StartAt { get => m_StartAt; set => m_StartAt = value; }
        public bool StickToEnd { get => m_StickToEnd; set => m_StickToEnd = value; }
        public bool PassWheelAtEdges { get => m_PassWheelAtEdges; set => m_PassWheelAtEdges = value; }

        public float Velocity => m_Velocity;

        /// <summary>The position, from the content's top, in local units.</summary>
        public double Position
        {
            get => m_Position;
            set => MoveTo(value);
        }

        public event Action PositionChanged;

        TextEngine Engine => ((ITextComponent)m_Text).SharedEngine;

        float Viewport => Engine.WindowViewportHeight;

        double Extent
        {
            get
            {
                TextEngine engine = Engine;
                return engine.IsWindowed ? engine.View.Rope.TotalHeight : engine.PlainExtent;
            }
        }

        double MaxPosition => Math.Max(0, Extent - Viewport);

        protected override void OnEnable()
        {
            base.OnEnable();
            Bind();
        }

        protected override void OnDisable()
        {
            Unbind();
            if (m_InHub) LetteraMotionHub.Remove(this);
            m_InHub = false;
            base.OnDisable();
        }

        protected override void OnRectTransformDimensionsChange() => RefreshBar();

        void Bind()
        {
            if (m_Text == null) m_Text = GetComponentInChildren<LetteraText>(true);
            if (m_Text == null || m_Bound) return;
            m_Bound = true;
            m_Text.Rebuilt += OnTextRebuilt;
            if (m_VerticalScrollbar != null) m_VerticalScrollbar.onValueChanged.AddListener(OnBarMoved);
            SyncMode();
            if (m_StartAt == LetteraScrollStart.End) MoveTo(MaxPosition);
            RefreshBar();
        }

        void Unbind()
        {
            if (!m_Bound || m_Text == null) return;
            m_Bound = false;
            m_Text.Rebuilt -= OnTextRebuilt;
            if (m_VerticalScrollbar != null) m_VerticalScrollbar.onValueChanged.RemoveListener(OnBarMoved);
            if (m_BoundView != null) m_BoundView.GeometryChanged -= RefreshBar;
            m_BoundView = null;
            if (!Engine.IsWindowed) Engine.SetPlainScroll(false);
        }

        void SyncMode()
        {
            TextEngine engine = Engine;
            if (engine.IsWindowed)
            {
                if (m_BoundView != engine.View)
                {
                    if (m_BoundView != null) m_BoundView.GeometryChanged -= RefreshBar;
                    m_BoundView = engine.View;
                    m_BoundView.GeometryChanged += RefreshBar;
                }
                engine.SetPlainScroll(false);
                return;
            }
            if (m_BoundView != null) m_BoundView.GeometryChanged -= RefreshBar;
            m_BoundView = null;
            engine.SetPlainScroll(true);
        }

        void OnTextRebuilt(LetteraChanges changes)
        {
            bool atEnd = m_StickToEnd && m_Position >= m_LastMax - 0.5 && m_LastMax > 0;
            SyncMode();
            if (atEnd) MoveTo(MaxPosition);
            else if (m_Position > MaxPosition) MoveTo(MaxPosition);
            RefreshBar();
        }

        double m_LastMax;

        void MoveTo(double position)
        {
            TextEngine engine = Engine;
            position = Math.Max(position, m_MovementType == LetteraScrollMovement.Elastic && (m_Dragging || m_InHub) ? position : 0);
            if (m_MovementType == LetteraScrollMovement.Clamped || !(m_Dragging || m_InHub)) position = Math.Max(0, Math.Min(position, MaxPosition));
            if (position == m_Position && !engine.IsWindowed) return;
            m_Position = position;
            if (engine.IsWindowed)
            {
                engine.View.ScrollTo(Math.Max(0, Math.Min(position, MaxPosition)));
                // Past an end, the rest of an elastic pull shows through the same offset.
                double past = position - Math.Max(0, Math.Min(position, MaxPosition));
                if (past != 0) engine.SetWindowOffset(engine.WindowOffset + (float)past);
            }
            else engine.SetPlainOffset((float)position);
            RefreshBar();
            PositionChanged?.Invoke();
        }

        void RefreshBar()
        {
            if (m_Text == null) return;
            m_LastMax = MaxPosition;
            if (m_VerticalScrollbar == null) return;
            double extent = Extent;
            float viewport = Viewport;
            bool unused = extent <= viewport + 0.5;
            if (m_HideScrollbarsWhenUnused && m_VerticalScrollbar.gameObject.activeSelf == unused) m_VerticalScrollbar.gameObject.SetActive(!unused);
            // The handle says how much shows, and stays large enough to take hold of.
            float minimum = Mathf.Max(LetteraEditingSettings.Dp(24), 1) / Mathf.Max(viewport, 1);
            m_VerticalScrollbar.size = Mathf.Clamp((float)(viewport / Math.Max(extent, 1)), Mathf.Min(minimum, 1), 1);
            if (m_HandleDrag) return;
            float value = MaxPosition > 0 ? (float)(1 - m_Position / MaxPosition) : 1;
            m_VerticalScrollbar.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        void OnBarMoved(float value)
        {
            StopMovement();
            MoveTo((1 - value) * MaxPosition);
        }

        public void ScrollToStart() => MoveTo(0);
        public void ScrollToEnd() => MoveTo(MaxPosition);

        public void StopMovement()
        {
            m_Velocity = 0;
            LeaveHub();
        }

        void EnterHub()
        {
            if (m_InHub) return;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        void LeaveHub()
        {
            if (!m_InHub) return;
            m_InHub = false;
            LetteraMotionHub.Remove(this);
        }

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            StopMovement();
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || m_Text == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out m_DragStartPoint)) return;
            m_Dragging = true;
            m_DragStartPosition = m_Position;
            m_Velocity = 0;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!m_Dragging || !RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, data.position, data.pressEventCamera, out Vector2 point)) return;
            double wanted = m_DragStartPosition + (point.y - m_DragStartPoint.y);
            double max = MaxPosition;
            // Past an end, a pull moves less and less, as a Scroll Rect's does.
            if (m_MovementType == LetteraScrollMovement.Elastic)
            {
                float view = Math.Max(Viewport, 1);
                if (wanted < 0) wanted = -RubberDelta((float)-wanted, view);
                else if (wanted > max) wanted = max + RubberDelta((float)(wanted - max), view);
            }
            double before = m_Position;
            MoveTo(wanted);
            float delta = Time.unscaledDeltaTime;
            if (delta > 0)
            {
                float speed = (float)((m_Position - before) / delta);
                m_Velocity = Mathf.Lerp(m_Velocity, speed, delta * 10);
            }
        }

        static float RubberDelta(float over, float view) => (1 - 1 / (over * 0.55f / view + 1)) * view;

        public void OnEndDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            m_Dragging = false;
            if (!m_Inertia) m_Velocity = 0;
            EnterHub();
        }

        public void OnScroll(PointerEventData data)
        {
            if (m_Text == null) return;
            float notches = data.scrollDelta.y;
            if (notches == 0) return;
            double max = MaxPosition;
            bool cannot = notches > 0 ? m_Position <= 0 : m_Position >= max;
            if (cannot)
            {
                if (m_PassWheelAtEdges && transform.parent != null) ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, data, ExecuteEvents.scrollHandler);
                return;
            }
            StopMovement();
            MoveTo(m_Position - notches * m_ScrollSensitivity * Engine.EstimatedLineHeight);
        }

        // Inertia and the return from an elastic end: the same curves as a Scroll Rect.
        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            if (this == null || m_Text == null || !isActiveAndEnabled) return (m_InHub = false);
            if (m_Dragging) return true;
            float delta = unscaled;
            if (delta <= 0) return true;
            double max = MaxPosition;
            double over = m_Position < 0 ? m_Position : m_Position > max ? m_Position - max : 0;
            if (m_MovementType == LetteraScrollMovement.Elastic && over != 0)
            {
                float position = (float)over, speed = m_Velocity;
                float next = Mathf.SmoothDamp(position, 0, ref speed, m_Elasticity, Mathf.Infinity, delta);
                if (Mathf.Abs(speed) < 1) speed = 0;
                m_Velocity = speed;
                MoveTo(m_Position - position + next);
                if (Mathf.Abs(next) < 0.5f && speed == 0)
                {
                    m_InHub = false;
                    MoveTo(Math.Max(0, Math.Min(m_Position, max)));
                    return false;
                }
                return true;
            }
            if (!m_Inertia || m_Velocity == 0) return (m_InHub = false);
            m_Velocity *= Mathf.Pow(m_DecelerationRate, delta);
            if (Mathf.Abs(m_Velocity) < 1)
            {
                m_Velocity = 0;
                return (m_InHub = false);
            }
            MoveTo(m_Position + m_Velocity * delta);
            return true;
        }
    }
}
