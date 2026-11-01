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
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

        /// <summary>The formulas this text draws, with where they stand and whether they could be read.</summary>
        public void GetFormulas(System.Collections.Generic.List<LetteraFormulaInfo> output)
        {
            if (output == null) throw new System.ArgumentNullException(nameof(output));
            output.Clear();
            ForceRebuild();
            Engine.CollectFormulas(output);
        }

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
        bool ITextHost.SolidsOnly => false;
        System.Collections.Generic.IReadOnlyList<LetteraStyle> ITextHost.HostStyles => null;
        float ITextHost.RasterScale => canvas != null ? canvas.scaleFactor : 1f;
        bool ITextHost.ClipsOverflow => m_ClipsOverflow;
        Component ITextHost.Owner => this;
        void ITextHost.RequestRebuild(DirtyLevel level) => MarkDirty(level);
        // A document's first layout places its window inside the rebuild, whose upload then carries that pose. Asking again from there is an error of the canvas.
        void ITextHost.PoseChanged()
        {
            SetVerticesDirty();
        }

        bool ITextHost.IsDrawn => !m_Hidden && IsActive() && !canvasRenderer.cull;
        bool ITextHost.SupportsMarquee => true;

        void ITextHost.ActiveRangesChanged(bool any)
        {
            if (any && Application.isPlaying) LetteraRangeRouter.EnsureInput(this);
        }

        [SerializeField] LetteraRoute m_Route;

        /// <summary>How this text takes a press.</summary>
        public LetteraRoute Route { get => m_Route; set => m_Route = value; }

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
                MarkDirty(alphaOnly ? DirtyLevel.Alpha : Engine.LevelForColorChange());
            }
        }

        partial void MarkDirty(DirtyLevel level)
        {
            Engine.SetDirty(level);
            if (level >= DirtyLevel.Layout && IsActive()) SetLayoutDirty();
            if (IsActive()) SetVerticesDirty();
        }

        Color m_AnimatedColor;

        // A curve wrote fields with no setter. Graphic would dirty everything here; this asks only for what differs, so an alpha curve stays an alpha pass.
        protected override void OnDidApplyAnimationProperties()
        {
            Engine.SyncSerializedFields();
            Color now = color;
            if (now != m_AnimatedColor)
            {
                bool alphaOnly = now.r == m_AnimatedColor.r && now.g == m_AnimatedColor.g && now.b == m_AnimatedColor.b;
                m_AnimatedColor = now;
                Engine.SetDirty(alphaOnly ? DirtyLevel.Alpha : Engine.LevelForColorChange());
            }
            if (Engine.Dirty == DirtyLevel.None || !IsActive()) return;
            if (Engine.Dirty >= DirtyLevel.Layout) SetLayoutDirty();
            SetVerticesDirty();
        }

        protected override void OnDestroy()
        {
            ClearParts();
            if (m_Engine != null)
            {
                LetteraRangeRouter.Forget(m_Engine);
                LetteraInlineObjects.Forget(m_Engine);
                m_Engine.AbandonDocument();
            }
            base.OnDestroy();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (m_Parts.Count == 0) ForgetOldParts();
            GlyphAtlasRegistration.EnsureRegistered();
            Engine.SetDirty(DirtyLevel.Parse);
            m_LastRectSize = rectTransform.rect.size;
            m_HasAnimator = GetComponentInParent<Animator>() != null;
            m_AnimatedColor = color;
            RefreshClipping();
            EnableCanvasChannels();
            AddListeners();
            Engine.SetEnabledForMotion(true);
            LetteraInlineObjects.Attach(Engine, this);
            SetAllDirty();
        }

        protected override void OnDisable()
        {
            RemoveListeners();
            Engine.SetEnabledForMotion(false);
            LetteraRangeRouter.NotifyDisabled(Engine);
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
            m_HasAnimator = GetComponentInParent<Animator>() != null;
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
                if (Engine.PoseStreams != 0)
                {
                    if (Engine.VertexCount > MaxPartVertices)
                    {
                        Engine.ClearPoseStreams();
                        UploadParts();
                    }
                    else
                    {
                        UploadPose();
                    }
                }
                return;
            }
            if (Engine.Dirty == DirtyLevel.None) Engine.SetDirty(DirtyLevel.Mesh);
            if (!Engine.Rebuild()) return;
            bool parts = Engine.VertexCount > MaxPartVertices;
            if (parts) UploadParts();
            else
            {
                ClearParts();
                UploadMesh();
            }
            Engine.ClearPoseStreams();
            ApplyMarqueeClip();
            if (MaterialsChanged())
            {
                EnableCanvasChannels();
                UpdateMaterial();
            }
            else if (parts)
            {
                PartMaterials();
            }
            canvasRenderer.SetMesh(m_Mesh);
            RaiseRebuilt(Engine.LastChanges);
        }

        // ------------------------------------------------------------- parts of a long text

        // A canvas renderer holds 16-bit indices: past this, Unity's canvas batching eats memory without end. So a long text draws through several.
        internal const int MaxPartVertices = 65000;
        const int QuadsPerPart = MaxPartVertices / 4;
        const string PartName = "Lettera Text Part";

        readonly System.Collections.Generic.List<(GameObject go, CanvasRenderer renderer, Mesh mesh)> m_Parts = new System.Collections.Generic.List<(GameObject, CanvasRenderer, Mesh)>();
        static readonly System.Collections.Generic.List<Vector3> s_PartVertices = new System.Collections.Generic.List<Vector3>();
        static readonly System.Collections.Generic.List<Color32> s_PartColors = new System.Collections.Generic.List<Color32>();
        static readonly System.Collections.Generic.List<Vector4> s_PartUv = new System.Collections.Generic.List<Vector4>();
        static readonly System.Collections.Generic.List<int> s_PartIndices = new System.Collections.Generic.List<int>();

        internal int PartCount => m_Parts.Count;

        void FillPart(Mesh mesh, int firstQuad, int quads)
        {
            TextEngine e = Engine;
            int v = firstQuad * 4, count = quads * 4;
            mesh.Clear(false);
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(e.Vertices, v, count);
            mesh.SetColors(e.Colors, v, count);
            mesh.SetUVs(0, e.Uv0, v, count);
            mesh.SetUVs(1, e.Uv1, v, count);
            if (e.Styled)
            {
                mesh.SetUVs(2, e.Uv2, v, count);
                mesh.SetUVs(3, e.Uv3, v, count);
            }
            // Every quad's six indices stand in the order of its vertices, so a part's are a run of them, moved down. A stretch of another material is its own submesh here.
            int start = firstQuad * 6, end = start + quads * 6;
            int submesh = 0;
            mesh.subMeshCount = Mathf.Max(SegmentsIn(start, end), 1);
            if (e.Segments.Count == 0)
            {
                WriteIndices(mesh, start, end, v, 0);
            }
            else
            {
                for (int i = 0; i < e.Segments.Count; i++)
                {
                    int a = Mathf.Max(e.Segments[i].IndexStart, start), b = Mathf.Min(e.Segments[i].IndexStart + e.Segments[i].IndexCount, end);
                    if (a < b) WriteIndices(mesh, a, b, v, submesh++);
                }
            }
            mesh.bounds = e.MeshBounds;
        }

        int SegmentsIn(int start, int end)
        {
            int count = 0;
            TextEngine e = Engine;
            for (int i = 0; i < e.Segments.Count; i++)
                if (Mathf.Max(e.Segments[i].IndexStart, start) < Mathf.Min(e.Segments[i].IndexStart + e.Segments[i].IndexCount, end))
                    count++;
            return count;
        }

        void WriteIndices(Mesh mesh, int from, int to, int down, int submesh)
        {
            int[] source = Engine.Indices;
            s_PartIndices.Clear();
            for (int i = from; i < to; i++) s_PartIndices.Add(source[i] - down);
            mesh.SetIndices(s_PartIndices, MeshTopology.Triangles, submesh, false);
        }

        void UploadParts()
        {
            if (m_Mesh == null) m_Mesh = new Mesh { name = "Lettera Text", hideFlags = HideFlags.HideAndDontSave };
            TextEngine e = Engine;
            int quads = e.VertexCount / 4;
            int needed = Mathf.CeilToInt((float)quads / QuadsPerPart) - 1;
            FillPart(m_Mesh, 0, Mathf.Min(quads, QuadsPerPart));
            canvasRenderer.SetMesh(m_Mesh);
            while (m_Parts.Count > needed)
            {
                DestroyPart(m_Parts[m_Parts.Count - 1]);
                m_Parts.RemoveAt(m_Parts.Count - 1);
            }
            Vector2 pivot = rectTransform.pivot;
            for (int p = 0; p < needed; p++)
            {
                if (p >= m_Parts.Count)
                {
                    var go = new GameObject(PartName, typeof(RectTransform), typeof(CanvasRenderer)) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(transform, false);
                    m_Parts.Add((go, go.GetComponent<CanvasRenderer>(), new Mesh { name = PartName, hideFlags = HideFlags.HideAndDontSave }));
                }
                (GameObject part, CanvasRenderer renderer, Mesh mesh) = m_Parts[p];
                // Its origin stands where the text's does, since the vertices are measured from there.
                var rect = (RectTransform)part.transform;
                rect.anchorMin = rect.anchorMax = pivot;
                rect.pivot = pivot;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                FillPart(mesh, (p + 1) * QuadsPerPart, Mathf.Min(quads - (p + 1) * QuadsPerPart, QuadsPerPart));
                renderer.SetMesh(mesh);
                renderer.SetAlpha(canvasRenderer.GetAlpha());
            }
            PartMaterials();
            if (m_MarqueeClipOn || m_ParentClipValid) PartClip();
        }

        // The materials of the stretches each part holds, and of the first part too, so that each submesh draws as it should.
        void PartMaterials()
        {
            if (Engine.VertexCount <= MaxPartVertices) return;
            TextEngine e = Engine;
            int quads = e.VertexCount / 4;
            Material source = m_Material != null ? m_Material : LetteraMaterials.Ui;
            for (int part = 0; part <= m_Parts.Count; part++)
            {
                CanvasRenderer renderer = part == 0 ? canvasRenderer : m_Parts[part - 1].renderer;
                int start = part * QuadsPerPart * 6, end = Mathf.Min(quads, (part + 1) * QuadsPerPart) * 6;
                int count = Mathf.Max(SegmentsIn(start, end), 1);
                renderer.materialCount = count;
                int slot = 0;
                for (int i = 0; i < e.Segments.Count; i++)
                {
                    MeshSegment segment = e.Segments[i];
                    if (Mathf.Max(segment.IndexStart, start) >= Mathf.Min(segment.IndexStart + segment.IndexCount, end)) continue;
                    Material material = segment.Custom != null ? segment.Custom : LetteraMaterials.Variant(source, e.Styled, segment.Blend, segment.Texture);
                    if (maskable && m_StencilValue > 0)
                    {
                        int bits = (1 << m_StencilValue) - 1;
                        material = StencilMaterial.Add(material, bits, UnityEngine.Rendering.StencilOp.Keep, UnityEngine.Rendering.CompareFunction.Equal, UnityEngine.Rendering.ColorWriteMask.All, bits, 0);
                        m_SegmentMasks.Add(material);
                    }
                    renderer.SetMaterial(material, slot++);
                }
                if (slot == 0) renderer.SetMaterial(source, 0);
            }
        }

        void PartClip()
        {
            foreach ((GameObject _, CanvasRenderer renderer, Mesh _) in m_Parts)
            {
                if (m_MarqueeClipOn) renderer.EnableRectClipping(m_MarqueeClip);
                else if (m_ParentClipValid) renderer.EnableRectClipping(m_ParentClip);
                else renderer.DisableRectClipping();
                renderer.clippingSoftness = canvasRenderer.clippingSoftness;
            }
        }

        void DestroyPart((GameObject go, CanvasRenderer renderer, Mesh mesh) part)
        {
            if (Application.isPlaying)
            {
                Destroy(part.mesh);
                Destroy(part.go);
            }
            else
            {
                DestroyImmediate(part.mesh);
                DestroyImmediate(part.go);
            }
        }

        void ClearParts()
        {
            if (m_Parts.Count == 0) return;
            foreach ((GameObject go, CanvasRenderer renderer, Mesh mesh) part in m_Parts) DestroyPart(part);
            m_Parts.Clear();
        }

        // After a domain reload the list is gone, and what it held is still there, never saved.
        void ForgetOldParts()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != PartName) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
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

        Rect m_ParentClip, m_MarqueeClip;
        bool m_ParentClipValid, m_MarqueeClipOn;

        // The clip keeps the sides only, so overflow upward and downward stays visible. A mask around the text still counts.
        void ApplyMarqueeClip()
        {
            TextEngine e = Engine;
            if (!e.IsMarqueeScrolling && !e.ClipsViewport)
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
            if (e.ClipsViewport)
            {
                // A viewport clips all around, with no fade.
                Vector3 c = root.transform.InverseTransformPoint(rectTransform.TransformPoint(new Vector3(own.xMin, own.yMin)));
                Vector3 d = root.transform.InverseTransformPoint(rectTransform.TransformPoint(new Vector3(own.xMax, own.yMax)));
                clip = Rect.MinMaxRect(Mathf.Min(c.x, d.x), Mathf.Min(c.y, d.y), Mathf.Max(c.x, d.x), Mathf.Max(c.y, d.y));
            }
            if (m_ParentClipValid)
            {
                clip = Rect.MinMaxRect(Mathf.Max(clip.xMin, m_ParentClip.xMin), Mathf.Max(clip.yMin, m_ParentClip.yMin), Mathf.Min(clip.xMax, m_ParentClip.xMax), Mathf.Min(clip.yMax, m_ParentClip.yMax));
            }
            m_MarqueeClip = clip;
            canvasRenderer.EnableRectClipping(clip);
            canvasRenderer.clippingSoftness = e.ClipsViewport ? Vector2.zero : new Vector2(e.MarqueeFadeUnits, 0);
        }

        public override void SetClipRect(Rect clipRect, bool validRect)
        {
            m_ParentClip = clipRect;
            m_ParentClipValid = validRect;
            if (m_MarqueeClipOn) ApplyMarqueeClip();
            else base.SetClipRect(clipRect, validRect);
            if (m_Parts.Count > 0) PartClip();
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
            if (e.VertexCount > MaxPartVertices)
            {
                PartMaterials();
                return;
            }
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
            LetteraSettings.SeedFromDefault?.Invoke(this);
        }
#endif

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (m_Hidden || m_Route == LetteraRoute.None || !base.Raycast(sp, eventCamera)) return false;
            return m_Route != LetteraRoute.Ranges || LetteraRangeRouter.Of((ITextComponent)this).HitTest(sp, eventCamera) != 0;
        }

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
            foreach ((GameObject _, CanvasRenderer renderer, Mesh _) in m_Parts) renderer.cull = true;
        }

        /// <summary>Draws again without a rebuild, unless something changed meanwhile.</summary>
        public void Show()
        {
            if (!m_Hidden) return;
            m_Hidden = false;
            canvasRenderer.cull = false;
            foreach ((GameObject _, CanvasRenderer renderer, Mesh _) in m_Parts) renderer.cull = false;
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
