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
        void ITextHost.RequestRebuild(DirtyLevel level) => MarkDirty(level);
        void ITextHost.PoseChanged() => SetVerticesDirty();
        bool ITextHost.IsDrawn => !m_Hidden && IsActive() && !canvasRenderer.cull;
        bool ITextHost.SupportsMarquee => true;

        TextEngine Engine => m_Engine ?? (m_Engine = new TextEngine(this));

        public override Material defaultMaterial => LetteraMaterials.Variant(LetteraMaterials.Ui, Engine.Styled, LetteraBlend.Normal, null);

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
            Engine.SetEnabledForMotion(true);
            SetAllDirty();
        }

        protected override void OnDisable()
        {
            RemoveListeners();
            Engine.SetEnabledForMotion(false);
            foreach (Material mask in m_SegmentMasks) StencilMaterial.Remove(mask);
            m_SegmentMasks.Clear();
            m_LastSegments.Clear();
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
            AdditionalCanvasShaderChannels needed = AdditionalCanvasShaderChannels.TexCoord1;
            if (Engine.Styled) needed |= AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
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
            if (Engine.Dirty == DirtyLevel.None && m_Mesh != null)
            {
                if (Engine.PoseStreams != 0) UploadPose();
                return;
            }
            if (Engine.Dirty == DirtyLevel.None) Engine.SetDirty(DirtyLevel.Mesh);
            if (!Engine.Rebuild()) return;
            UploadMesh();
            Engine.ClearPoseStreams();
            ApplyMarqueeClip();
            if (MaterialsChanged())
            {
                EnableCanvasChannels();
                UpdateMaterial();
            }
            canvasRenderer.SetMesh(m_Mesh);
            RaiseRebuilt(Engine.LastChanges);
        }

        // A pose frame changes vertex data and nothing else: no index write, no material, no layout.
        void UploadPose()
        {
            TextEngine e = Engine;
            int streams = e.PoseStreams;
            e.ClearPoseStreams();
            if ((streams & TextEngine.StreamPositions) != 0) m_Mesh.SetVertices(e.Vertices, 0, e.VertexCount, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            if ((streams & TextEngine.StreamColors) != 0) m_Mesh.SetColors(e.Colors, 0, e.VertexCount);
            if ((streams & TextEngine.StreamUv0) != 0) m_Mesh.SetUVs(0, e.Uv0, 0, e.VertexCount);
            if ((streams & TextEngine.StreamUv1) != 0) m_Mesh.SetUVs(1, e.Uv1, 0, e.VertexCount);
            if ((streams & TextEngine.StreamUv3) != 0 && e.Styled) m_Mesh.SetUVs(3, e.Uv3, 0, e.VertexCount);
            canvasRenderer.SetMesh(m_Mesh);
        }

        // ------------------------------------------------------------- marquee clip

        Rect m_ParentClip;
        bool m_ParentClipValid, m_MarqueeClipOn;

        // The clip keeps the sides only, so overflow upward and downward stays visible. A mask around the text still counts.
        void ApplyMarqueeClip()
        {
            TextEngine e = Engine;
            if (!e.IsMarqueeScrolling)
            {
                if (!m_MarqueeClipOn) return;
                m_MarqueeClipOn = false;
                if (m_ParentClipValid) canvasRenderer.EnableRectClipping(m_ParentClip);
                else canvasRenderer.DisableRectClipping();
                canvasRenderer.clippingSoftness = Vector2.zero;
                return;
            }
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            if (root == null) return;
            m_MarqueeClipOn = true;
            Rect own = rectTransform.rect;
            Vector3 a = root.transform.InverseTransformPoint(rectTransform.TransformPoint(new Vector3(own.xMin + m_Data.Padding.x, own.yMin)));
            Vector3 b = root.transform.InverseTransformPoint(rectTransform.TransformPoint(new Vector3(own.xMax - m_Data.Padding.z, own.yMax)));
            Rect clip = Rect.MinMaxRect(Mathf.Min(a.x, b.x), -100000, Mathf.Max(a.x, b.x), 100000);
            if (m_ParentClipValid)
            {
                clip = Rect.MinMaxRect(Mathf.Max(clip.xMin, m_ParentClip.xMin), Mathf.Max(clip.yMin, m_ParentClip.yMin), Mathf.Min(clip.xMax, m_ParentClip.xMax), Mathf.Min(clip.yMax, m_ParentClip.yMax));
            }
            canvasRenderer.EnableRectClipping(clip);
            canvasRenderer.clippingSoftness = new Vector2(e.MarqueeFadeUnits, 0);
        }

        public override void SetClipRect(Rect clipRect, bool validRect)
        {
            m_ParentClip = clipRect;
            m_ParentClipValid = validRect;
            if (m_MarqueeClipOn) ApplyMarqueeClip();
            else base.SetClipRect(clipRect, validRect);
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
            if (e.Styled)
            {
                m_Mesh.SetUVs(2, e.Uv2, 0, e.VertexCount);
                m_Mesh.SetUVs(3, e.Uv3, 0, e.VertexCount);
            }
            int segments = e.Segments.Count;
            if (segments <= 1)
            {
                m_Mesh.subMeshCount = 1;
                m_Mesh.SetIndices(e.Indices, 0, e.IndexCount, MeshTopology.Triangles, 0, false);
            }
            else
            {
                m_Mesh.subMeshCount = segments;
                for (int i = 0; i < segments; i++)
                    m_Mesh.SetIndices(e.Indices, e.Segments[i].IndexStart, e.Segments[i].IndexCount, MeshTopology.Triangles, i, false);
            }
            m_Mesh.bounds = e.MeshBounds;
        }

        // The materials of the last rebuild's stretches, to see when the renderer needs new ones.
        readonly System.Collections.Generic.List<MeshSegment> m_LastSegments = new System.Collections.Generic.List<MeshSegment>();
        readonly System.Collections.Generic.List<Material> m_SegmentMasks = new System.Collections.Generic.List<Material>();
        bool m_LastStyled;

        bool MaterialsChanged()
        {
            TextEngine e = Engine;
            bool changed = m_LastStyled != e.Styled || m_LastSegments.Count != e.Segments.Count;
            for (int i = 0; !changed && i < e.Segments.Count; i++)
                changed = m_LastSegments[i].Blend != e.Segments[i].Blend || m_LastSegments[i].Texture != e.Segments[i].Texture || m_LastSegments[i].Custom != e.Segments[i].Custom;
            if (!changed) return false;
            m_LastStyled = e.Styled;
            m_LastSegments.Clear();
            m_LastSegments.AddRange(e.Segments);
            return true;
        }

        protected override void UpdateMaterial()
        {
            base.UpdateMaterial();
            if (!IsActive()) return;
            TextEngine e = Engine;
            foreach (Material mask in m_SegmentMasks) StencilMaterial.Remove(mask);
            m_SegmentMasks.Clear();
            int count = e.Segments.Count;
            if (count == 0 || (count == 1 && e.Segments[0].IsDefault)) return;

            // A material of the user's own stays; only the variant of the default one changes per stretch.
            Material source = m_Material != null ? m_Material : LetteraMaterials.Ui;
            canvasRenderer.materialCount = count;
            for (int i = 0; i < count; i++)
            {
                MeshSegment segment = e.Segments[i];
                Material material = segment.Custom != null ? segment.Custom : LetteraMaterials.Variant(source, e.Styled, segment.Blend, segment.Texture);
                if (maskable && m_StencilValue > 0)
                {
                    int bits = (1 << m_StencilValue) - 1;
                    material = StencilMaterial.Add(material, bits, UnityEngine.Rendering.StencilOp.Keep, UnityEngine.Rendering.CompareFunction.Equal, UnityEngine.Rendering.ColorWriteMask.All, bits, 0);
                    m_SegmentMasks.Add(material);
                }
                canvasRenderer.SetMaterial(material, i);
            }
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
            // A mask may have wanted it culled meanwhile.
            RecalculateClipping();
        }

        // A mask decides about culling on its own, which must not bring a hidden text back.
        public override void Cull(Rect clipRect, bool validRect)
        {
            if (m_Hidden)
            {
                canvasRenderer.cull = true;
                return;
            }
            base.Cull(clipRect, validRect);
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
