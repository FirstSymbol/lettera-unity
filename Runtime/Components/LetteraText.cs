using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Text for a Canvas: a uGUI graphic with shaping, bidi, emoji, styles and layout.</summary>
    [AddComponentMenu("Lettera/Text")]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public partial class LetteraText : MaskableGraphic, ILayoutElement, ITextHost
    {
        [SerializeField] LetteraTextData m_Data = new LetteraTextData();

        TextEngine m_Engine;
        Mesh m_Mesh;
        bool m_Hidden;
        bool m_ClipsOverflow;
        bool m_HasAnimator;
        Vector2 m_LastRectSize;

        LetteraTextData ITextHost.Data => m_Data;
        Rect ITextHost.LayoutRect => rectTransform.rect;
        Color32 ITextHost.BaseColor => color;
        float ITextHost.RasterScale => canvas != null ? canvas.scaleFactor : 1f;
        bool ITextHost.ClipsOverflow => m_ClipsOverflow;
        Component ITextHost.Owner => this;

        TextEngine Engine => m_Engine ?? (m_Engine = new TextEngine(this));

        public override Material defaultMaterial => LetteraMaterials.Ui;

        public override Color color
        {
            get => base.color;
            set
            {
                Color old = base.color;
                if (old == value) return;
                bool alphaOnly = old.r == value.r && old.g == value.g && old.b == value.b;
                base.color = value;
                MarkDirty(alphaOnly ? DirtyLevel.Alpha : DirtyLevel.Mesh);
            }
        }

        partial void MarkDirty(DirtyLevel level)
        {
            Engine.SetDirty(level);
            if (level >= DirtyLevel.Layout && IsActive()) SetLayoutDirty();
            if (IsActive()) SetVerticesDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            GlyphAtlasRegistration.EnsureRegistered();
            Engine.SetDirty(DirtyLevel.Parse);
            m_LastRectSize = rectTransform.rect.size;
            m_HasAnimator = GetComponent<Animator>() != null;
            RefreshClipping();
            EnableCanvasChannels();
            AddListeners();
            SetAllDirty();
        }

        protected override void OnDisable()
        {
            RemoveListeners();
            base.OnDisable();
            if (m_Mesh != null)
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
                m_Mesh = null;
            }
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnableCanvasChannels();
            RefreshClipping();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            RefreshClipping();
        }

        void EnableCanvasChannels()
        {
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            if (root == null) return;
            const AdditionalCanvasShaderChannels needed = AdditionalCanvasShaderChannels.TexCoord1;
            if ((root.additionalShaderChannels & needed) != needed) root.additionalShaderChannels |= needed;
        }

        void RefreshClipping()
        {
            bool clips = GetComponentInParent<RectMask2D>() != null || GetComponentInParent<Mask>() != null;
            if (clips == m_ClipsOverflow) return;
            m_ClipsOverflow = clips;
            Engine.SetDirty(DirtyLevel.Positions);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Vector2 size = rectTransform.rect.size;
            if (size == m_LastRectSize) return;
            bool widthChanged = !Mathf.Approximately(size.x, m_LastRectSize.x);
            m_LastRectSize = size;
            bool heightMatters = m_Data.AutoSize || m_Data.Overflow != LetteraOverflow.Visible || m_ClipsOverflow || Engine.HasElideRequests;
            MarkDirty(widthChanged || heightMatters ? DirtyLevel.Layout : DirtyLevel.Positions);
        }

        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        protected override void UpdateGeometry()
        {
            if (m_HasAnimator) Engine.SyncSerializedFields();
            if (Engine.Dirty == DirtyLevel.None && m_Mesh != null) return;
            if (Engine.Dirty == DirtyLevel.None) Engine.SetDirty(DirtyLevel.Mesh);
            if (!Engine.Rebuild()) return;
            UploadMesh();
            canvasRenderer.SetMesh(m_Mesh);
            RaiseRebuilt(Engine.LastChanges);
        }

        void UploadMesh()
        {
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Lettera Text", hideFlags = HideFlags.HideAndDontSave };
            TextEngine e = Engine;
            m_Mesh.Clear(false);
            m_Mesh.SetVertices(e.Vertices, 0, e.VertexCount);
            m_Mesh.SetColors(e.Colors, 0, e.VertexCount);
            m_Mesh.SetUVs(0, e.Uv0, 0, e.VertexCount);
            m_Mesh.SetUVs(1, e.Uv1, 0, e.VertexCount);
            m_Mesh.SetIndices(e.Indices, 0, e.IndexCount, MeshTopology.Triangles, 0, false);
            m_Mesh.bounds = e.MeshBounds;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (m_Data == null) return;
            DirtyLevel before = Engine.Dirty;
            Engine.SyncSerializedFields();
            // Style lists have no cheap comparison, so any Inspector edit parses again.
            Engine.SetDirty(DirtyLevel.Parse);
            if (Engine.Dirty != before && isActiveAndEnabled) SetLayoutDirty();
        }

        protected override void Reset()
        {
            base.Reset();
            if (string.IsNullOrEmpty(m_Data.Text)) m_Data.Text = "New Text";
        }
#endif

        public override bool Raycast(Vector2 sp, Camera eventCamera) => !m_Hidden && base.Raycast(sp, eventCamera);

        // ------------------------------------------------------------- ILayoutElement

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth => 0;
        public float preferredWidth => Engine.GetMaxContentWidth() + m_Data.Padding.x + m_Data.Padding.z;
        public float flexibleWidth => -1;
        public float minHeight => 0;
        public float preferredHeight =>
            Engine.GetHeightForWidth(Mathf.Max(rectTransform.rect.width - m_Data.Padding.x - m_Data.Padding.z, 0)) + m_Data.Padding.y + m_Data.Padding.w;
        public float flexibleHeight => -1;
        public int layoutPriority => 0;

        // ------------------------------------------------------------- visibility

        /// <summary>False while hidden with Hide().</summary>
        public bool IsVisible => !m_Hidden;

        /// <summary>Stops drawing and pointer events, keeping all rebuilt state.</summary>
        public void Hide()
        {
            if (m_Hidden) return;
            m_Hidden = true;
            canvasRenderer.cull = true;
        }

        /// <summary>Draws again without a rebuild, unless something changed meanwhile.</summary>
        public void Show()
        {
            if (!m_Hidden) return;
            m_Hidden = false;
            canvasRenderer.cull = false;
        }

        /// <summary>Rebuilds now, e.g. before reading results in the same frame.</summary>
        public void ForceRebuild()
        {
            if (Engine.Dirty == DirtyLevel.None) return;
            UpdateGeometry();
            GlyphAtlas.Flush();
        }
    }
}
