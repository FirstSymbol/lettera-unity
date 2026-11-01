using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Text in world space, without a Canvas. Texts sharing a sorting context merge into one mesh.</summary>
    [AddComponentMenu("Lettera/World Text")]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public partial class LetteraWorldText : MonoBehaviour, ITextHost
    {
        /// <summary>The formulas this text draws, with where they stand and whether they could be read.</summary>
        public void GetFormulas(System.Collections.Generic.List<LetteraFormulaInfo> output)
        {
            if (output == null) throw new System.ArgumentNullException(nameof(output));
            output.Clear();
            ForceRebuild();
            Engine.CollectFormulas(output);
        }

        [SerializeField] LetteraTextData m_Data = new LetteraTextData();
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] int m_SortingLayerID;
        [SerializeField] int m_SortingOrder;
        [SerializeField] int m_BatchGroup;
        [SerializeField] bool m_Standalone;
        [SerializeField] bool m_Lit;
        [SerializeField] bool m_CastShadows;
        [SerializeField] bool m_RaycastTarget = true;

        TextEngine m_Engine;
        RectTransform m_Rect;
        bool m_Hidden;
        bool m_HasAnimator;
        bool m_Registered;

        LetteraTextData ITextHost.Data => m_Data;
        Rect ITextHost.LayoutRect => Rect.rect;
        Color32 ITextHost.BaseColor => m_Color;
        float ITextHost.RasterScale => 1f;
        bool ITextHost.ClipsOverflow => false;
        Component ITextHost.Owner => this;
        void ITextHost.RequestRebuild(DirtyLevel level) => MarkDirty(level);
        void ITextHost.PoseChanged() => LetteraWorldBatcher.NotifyPosed(this);
        bool ITextHost.IsDrawn => !m_Hidden && isActiveAndEnabled;
        bool ITextHost.SupportsMarquee => false;

        void ITextHost.ActiveRangesChanged(bool any)
        {
            if (any && Application.isPlaying) LetteraRangeRouter.EnsureInput(this);
        }

        [SerializeField] LetteraRoute m_Route;

        /// <summary>How this text takes a press.</summary>
        public LetteraRoute Route { get => m_Route; set => m_Route = value; }

        RectTransform Rect => m_Rect != null ? m_Rect : m_Rect = (RectTransform)transform;
        internal TextEngine Engine => m_Engine ?? (m_Engine = new TextEngine(this));
        internal bool Hidden => m_Hidden;
        internal Vector2 LastRectSize;

        public Color Color
        {
            get => m_Color;
            set
            {
                if (m_Color == value) return;
                bool alphaOnly = m_Color.r == value.r && m_Color.g == value.g && m_Color.b == value.b;
                m_Color = value;
                MarkDirty(alphaOnly ? DirtyLevel.Alpha : Engine.LevelForColorChange());
            }
        }

        public int SortingLayerID { get => m_SortingLayerID; set => m_SortingLayerID = value; }
        public int SortingOrder { get => m_SortingOrder; set => m_SortingOrder = value; }
        public int BatchGroup { get => m_BatchGroup; set => m_BatchGroup = value; }

        /// <summary>Draw through an own renderer, which sorts by its own distance to the camera.</summary>
        public bool Standalone { get => m_Standalone; set => m_Standalone = value; }

        /// <summary>Lights the text with the ambient and main light. Uses the lit material, in a batch of its own.</summary>
        public bool Lit { get => m_Lit; set => m_Lit = value; }
        public bool CastShadows { get => m_CastShadows; set => m_CastShadows = value; }
        public bool RaycastTarget { get => m_RaycastTarget; set => m_RaycastTarget = value; }

        partial void MarkDirty(DirtyLevel level) => Engine.SetDirty(level);

        void OnEnable()
        {
            GlyphAtlasRegistration.EnsureRegistered();
            Engine.SetDirty(DirtyLevel.Parse);
            LastRectSize = Rect.rect.size;
            m_HasAnimator = GetComponentInParent<Animator>() != null;
            m_AnimatedColor = m_Color;
            AddListeners();
            LetteraWorldBatcher.Register(this);
            m_Registered = true;
            Engine.SetEnabledForMotion(true);
            LetteraInlineObjects.Attach(Engine, this);
        }

        Color m_AnimatedColor;

        void OnDidApplyAnimationProperties()
        {
            Engine.SyncSerializedFields();
            if (m_Color == m_AnimatedColor) return;
            bool alphaOnly = m_Color.r == m_AnimatedColor.r && m_Color.g == m_AnimatedColor.g && m_Color.b == m_AnimatedColor.b;
            m_AnimatedColor = m_Color;
            Engine.SetDirty(alphaOnly ? DirtyLevel.Alpha : Engine.LevelForColorChange());
        }

        void OnTransformParentChanged() => m_HasAnimator = GetComponentInParent<Animator>() != null;

        void OnDestroy()
        {
            if (m_Engine != null)
            {
                LetteraRangeRouter.Forget(m_Engine);
                LetteraInlineObjects.Forget(m_Engine);
                m_Engine.AbandonDocument();
            }
        }

        void OnDisable()
        {
            RemoveListeners();
            Engine.SetEnabledForMotion(false);
            LetteraRangeRouter.NotifyDisabled(Engine);
            if (m_Registered) LetteraWorldBatcher.Unregister(this);
            m_Registered = false;
        }

        internal bool HasAnimator => m_HasAnimator;
        internal void NotifyRebuilt() => RaiseRebuilt(Engine.LastChanges);

        internal void OnRectChanged(Vector2 size)
        {
            bool widthChanged = !Mathf.Approximately(size.x, LastRectSize.x);
            LastRectSize = size;
            bool heightMatters = m_Data.AutoSize || m_Data.Overflow != LetteraOverflow.Visible || Engine.HasElideRequests;
            Engine.SetDirty(widthChanged || heightMatters ? DirtyLevel.Layout : DirtyLevel.Positions);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (m_Data == null) return;
            Engine.SyncSerializedFields();
            Engine.SetDirty(DirtyLevel.Parse);
        }

        void Reset()
        {
            if (string.IsNullOrEmpty(m_Data.Text)) m_Data.Text = "New Text";
            // A fresh RectTransform is 100 x 100 units, far too large for a world object.
            if (Rect.sizeDelta == new Vector2(100, 100))
            {
                Rect.sizeDelta = new Vector2(20, 5);
                m_Data.FontSize = 2f;
            }
        }
#endif

        /// <summary>False while hidden with Hide().</summary>
        public bool IsVisible => !m_Hidden;

        /// <summary>Stops drawing, keeping all rebuilt state. A hidden text does no per-frame work.</summary>
        public void Hide()
        {
            if (m_Hidden) return;
            m_Hidden = true;
            LetteraWorldBatcher.NotifyChanged(this);
        }

        /// <summary>Draws again without a rebuild, unless something changed meanwhile.</summary>
        public void Show()
        {
            if (!m_Hidden) return;
            m_Hidden = false;
            LetteraWorldBatcher.NotifyChanged(this);
        }

        /// <summary>Rebuilds now, e.g. before reading results in the same frame.</summary>
        public void ForceRebuild()
        {
            if (Engine.Dirty == DirtyLevel.None) return;
            Engine.Rebuild();
            LetteraWorldBatcher.NotifyChanged(this);
            GlyphAtlas.Flush();
        }
    }
}
