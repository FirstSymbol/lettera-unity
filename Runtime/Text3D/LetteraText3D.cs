using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Lettera
{
    /// <summary>Which plane of a solid text lies on its transform.</summary>
    public enum LetteraSolidPivot
    {
        Middle,
        Front,
        Back,
    }

    /// <summary>What a solid text does with a color glyph.</summary>
    public enum LetteraSolidColorGlyphs
    {
        /// <summary>Flat on the front plane.</summary>
        Flat,
        Hide,
    }

    /// <summary>
    /// Text with depth: solid, lit, shadowed, drawn by its own renderer. Markup, fonts, layout and motion are the same as a world text's,
    /// and each glyph is built once whatever the depth.
    /// </summary>
    [AddComponentMenu("Lettera/Text 3D")]
    [RequireComponent(typeof(RectTransform), typeof(MeshFilter), typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public partial class LetteraText3D : MonoBehaviour, ITextHost
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

        internal const int MaxVerticesHard = 1000000;

        [SerializeField] LetteraTextData m_Data = new LetteraTextData();
        [SerializeField] Color m_Color = Color.white;

        [SerializeField, Tooltip("How deep the text is, in em.")] float m_Depth = 0.15f;
        [SerializeField, Tooltip("Which plane lies on the transform.")] LetteraSolidPivot m_Pivot = LetteraSolidPivot.Middle;
        [SerializeField] bool m_FrontFace = true, m_BackFace = true, m_Sides = true;
        [SerializeField] LetteraBevel m_FrontBevel = LetteraBevel.Default;
        [SerializeField] LetteraBevel m_BackBevel = LetteraBevel.Default;
        [SerializeField, Tooltip("The back takes the front's edge.")] bool m_MirrorBevel = true;
        [SerializeField, Range(0, 180), Tooltip("Corners within this angle shade smooth.")] float m_SmoothingAngle = 30;
        [SerializeField, Min(0.0001f), Tooltip("How far a curve may stray from its true line, in em.")] float m_CurveTolerance = 0.001f;
        [SerializeField] LetteraSolidColorGlyphs m_ColorGlyphs = LetteraSolidColorGlyphs.Flat;
        [SerializeField, Tooltip("Built with the text and kept referenced, so that a counter's digits never build in a frame that shows.")] string m_WarmCharacters = "";
        [SerializeField, Tooltip("Identical texts draw one mesh, which is what lets them instance.")] bool m_ShareMesh = true;
        [SerializeField] bool m_RaycastTarget = true;

        [SerializeField] Material m_FaceMaterial, m_BevelMaterial, m_SideMaterial, m_BackBevelMaterial, m_BackMaterial;
        [SerializeField, Tooltip("Extra solids around the glyphs: outlines, plates and tubes.")] List<LetteraSolidLayer> m_Layers = new List<LetteraSolidLayer>();
        [SerializeField, Tooltip("Bends, twists, tapers, waves, a ball or a path, applied in order.")] List<LetteraDeformer> m_Deformers = new List<LetteraDeformer>();
        [SerializeField, Min(0.005f), Tooltip("With a deformer, no edge across the text is longer than this, in em. Less is smoother and heavier.")] float m_DeformDetail = 0.05f;

        TextEngine m_Engine;
        RectTransform m_Rect;
        MeshFilter m_Filter;
        MeshRenderer m_Renderer;
        Mesh m_Mesh;
        bool m_MeshIsShared;
        int m_ChangeCount;
        bool m_SolidsDirty = true;
        bool m_ColorsDirty;
        bool m_WarnedMaterial;
        int m_SubmeshCount;
        LetteraSolidMeshBuilder m_Builder;
        readonly List<LetteraSolidEntry> m_Held = new List<LetteraSolidEntry>();
        readonly List<LetteraSolidEntry> m_HeldBefore = new List<LetteraSolidEntry>();
        readonly List<LetteraSolidEntry> m_Warm = new List<LetteraSolidEntry>();
        readonly List<Material> m_Materials = new List<Material>();

        LetteraTextData ITextHost.Data => m_Data;
        Rect ITextHost.LayoutRect => Rect.rect;
        Color32 ITextHost.BaseColor => m_Color;
        float ITextHost.RasterScale => 1f;
        bool ITextHost.ClipsOverflow => false;
        Component ITextHost.Owner => this;
        bool ITextHost.SolidsOnly => true;
        IReadOnlyList<LetteraStyle> ITextHost.HostStyles => LetteraSolidEffects.Styles;
        bool ITextHost.IsDrawn => isActiveAndEnabled && !m_Hidden;
        bool ITextHost.SupportsMarquee => false;

        void ITextHost.ActiveRangesChanged(bool any)
        {
            if (any && Application.isPlaying) LetteraRangeRouter.EnsureInput(this);
        }

        void ITextHost.RequestRebuild(DirtyLevel level) => MarkDirty(level);

        void ITextHost.PoseChanged()
        {
            if (m_Builder != null) ApplyPose();
        }

        RectTransform Rect => m_Rect != null ? m_Rect : m_Rect = (RectTransform)transform;
        internal TextEngine Engine => m_Engine ?? (m_Engine = new TextEngine(this));
        internal LetteraSolidMeshBuilder Builder => m_Builder;
        internal Mesh CurrentMesh => m_Mesh;

        partial void MarkDirty(DirtyLevel level)
        {
            Engine.SetDirty(level);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void MarkSolidsDirty()
        {
            m_SolidsDirty = true;
            MarkDirty(DirtyLevel.Mesh);
        }

        public Color Color
        {
            get => m_Color;
            set
            {
                if (m_Color == value) return;
                m_Color = value;
                m_ColorsDirty = true;
                MarkDirty(DirtyLevel.Mesh);
            }
        }

        /// <summary>How deep the text is, in em. Changing it builds no solid.</summary>
        public float Depth { get => m_Depth; set { if (m_Depth == value) return; m_Depth = Mathf.Max(value, 0); m_ColorsDirty = true; MarkDirty(DirtyLevel.Mesh); } }
        public LetteraSolidPivot Pivot { get => m_Pivot; set { if (m_Pivot == value) return; m_Pivot = value; m_ColorsDirty = true; MarkDirty(DirtyLevel.Mesh); } }
        public bool FrontFace { get => m_FrontFace; set { if (m_FrontFace == value) return; m_FrontFace = value; MarkSolidsDirty(); } }
        public bool BackFace { get => m_BackFace; set { if (m_BackFace == value) return; m_BackFace = value; MarkSolidsDirty(); } }
        public bool Sides { get => m_Sides; set { if (m_Sides == value) return; m_Sides = value; MarkSolidsDirty(); } }
        public LetteraBevel FrontBevel { get => m_FrontBevel; set { m_FrontBevel = value; MarkSolidsDirty(); } }
        public LetteraBevel BackBevel { get => m_BackBevel; set { m_BackBevel = value; MarkSolidsDirty(); } }
        public bool MirrorBevel { get => m_MirrorBevel; set { if (m_MirrorBevel == value) return; m_MirrorBevel = value; MarkSolidsDirty(); } }
        public float SmoothingAngle { get => m_SmoothingAngle; set { if (m_SmoothingAngle == value) return; m_SmoothingAngle = value; MarkSolidsDirty(); } }
        public float CurveTolerance { get => m_CurveTolerance; set { if (m_CurveTolerance == value) return; m_CurveTolerance = Mathf.Max(value, 0.0001f); MarkSolidsDirty(); } }
        public LetteraSolidColorGlyphs ColorGlyphs { get => m_ColorGlyphs; set { if (m_ColorGlyphs == value) return; m_ColorGlyphs = value; MarkSolidsDirty(); } }
        public bool ShareMesh { get => m_ShareMesh; set { if (m_ShareMesh == value) return; m_ShareMesh = value; MarkSolidsDirty(); } }
        public bool RaycastTarget { get => m_RaycastTarget; set => m_RaycastTarget = value; }
        public string WarmCharacters { get => m_WarmCharacters; set { m_WarmCharacters = value ?? ""; MarkSolidsDirty(); } }

        public Material FaceMaterial { get => m_FaceMaterial; set { m_FaceMaterial = value; MarkSolidsDirty(); } }
        public Material BevelMaterial { get => m_BevelMaterial; set { m_BevelMaterial = value; MarkSolidsDirty(); } }
        public Material SideMaterial { get => m_SideMaterial; set { m_SideMaterial = value; MarkSolidsDirty(); } }
        public Material BackBevelMaterial { get => m_BackBevelMaterial; set { m_BackBevelMaterial = value; MarkSolidsDirty(); } }
        public Material BackMaterial { get => m_BackMaterial; set { m_BackMaterial = value; MarkSolidsDirty(); } }

        /// <summary>The extra solids built around the glyphs, in order. After changing one in place, call RefreshSolids.</summary>
        public IReadOnlyList<LetteraSolidLayer> Layers => m_Layers;

        public void AddLayer(LetteraSolidLayer layer)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            m_Layers.Add(layer);
            MarkSolidsDirty();
        }

        public void InsertLayer(int index, LetteraSolidLayer layer)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            m_Layers.Insert(index, layer);
            MarkSolidsDirty();
        }

        public void RemoveLayerAt(int index)
        {
            m_Layers.RemoveAt(index);
            MarkSolidsDirty();
        }

        public void ClearLayers()
        {
            if (m_Layers.Count == 0) return;
            m_Layers.Clear();
            MarkSolidsDirty();
        }

        /// <summary>The chain of deformers the whole text goes through, in order. A range's own come from its tags, first.</summary>
        public IReadOnlyList<LetteraDeformer> Deformers => m_Deformers;

        public void AddDeformer(LetteraDeformer deformer)
        {
            m_Deformers.Add(deformer);
            MarkSolidsDirty();
        }

        public void SetDeformer(int index, LetteraDeformer deformer)
        {
            m_Deformers[index] = deformer;
            MarkSolidsDirty();
        }

        public void RemoveDeformerAt(int index)
        {
            m_Deformers.RemoveAt(index);
            MarkSolidsDirty();
        }

        public void ClearDeformers()
        {
            if (m_Deformers.Count == 0) return;
            m_Deformers.Clear();
            MarkSolidsDirty();
        }

        /// <summary>With a deformer, no edge across the text is longer than this, in em.</summary>
        public float DeformDetail { get => m_DeformDetail; set { value = Mathf.Max(value, 0.005f); if (m_DeformDetail == value) return; m_DeformDetail = value; MarkSolidsDirty(); } }

        /// <summary>Builds the solids again, after a layer was changed in place.</summary>
        public void RefreshSolids() => MarkSolidsDirty();

        // Whether anything deforms this text: its own chain, or a range's tag.
        internal bool Deforms
        {
            get
            {
                for (int i = 0; i < m_Deformers.Count; i++)
                    if (!m_Deformers[i].Off)
                        return true;
                return m_Engine != null && LetteraSolidEffects.HasDeform(m_Engine);
            }
        }

        /// <summary>The material a surface ends up with, after falling back through the slots.</summary>
        public Material GetSurfaceMaterial(LetteraSolidSurface surface)
        {
            switch (surface)
            {
                case LetteraSolidSurface.Bevel: return m_BevelMaterial != null ? m_BevelMaterial : GetSurfaceMaterial(LetteraSolidSurface.Face);
                case LetteraSolidSurface.Side: return m_SideMaterial != null ? m_SideMaterial : GetSurfaceMaterial(LetteraSolidSurface.Face);
                case LetteraSolidSurface.BackBevel: return m_BackBevelMaterial != null ? m_BackBevelMaterial : GetSurfaceMaterial(LetteraSolidSurface.Bevel);
                case LetteraSolidSurface.Back: return m_BackMaterial != null ? m_BackMaterial : GetSurfaceMaterial(LetteraSolidSurface.Face);
                default: return m_FaceMaterial != null ? m_FaceMaterial : LetteraSolidDefaults.Material(ref m_WarnedMaterial);
            }
        }

        internal LetteraSolidRecipe Recipe => new LetteraSolidRecipe
        {
            Front = m_FrontBevel,
            Back = m_MirrorBevel ? m_FrontBevel : m_BackBevel,
            Tolerance = m_CurveTolerance,
            SmoothingCos = Mathf.Cos(m_SmoothingAngle * Mathf.Deg2Rad),
            Faces = (m_FrontFace ? LetteraSolidFaces.Front : 0) | (m_BackFace ? LetteraSolidFaces.Back : 0) | (m_Sides ? LetteraSolidFaces.Sides : 0),
            MaxEdge = Deforms ? m_DeformDetail : 0,
        };

        // ------------------------------------------------------------------ life

        void OnEnable()
        {
            GlyphAtlasRegistration.EnsureRegistered();
            m_Filter = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();
            if (m_Builder == null) m_Builder = new LetteraSolidMeshBuilder();
            m_Builder.SetOwner(this);
            m_SolidsDirty = true;
            m_ColorsDirty = false;
            AddListeners();
            Engine.SetDirty(DirtyLevel.Parse);
            Engine.SetEnabledForMotion(true);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void OnDisable()
        {
            RemoveListeners();
            Engine.SetEnabledForMotion(false);
            LetteraRangeRouter.NotifyDisabled(Engine);
            ReleaseAll();
            m_Builder?.Dispose();
            m_Builder = null;
            DropMesh();
            LetteraSolidFlatChild.Remove(this);
        }

        void OnDestroy()
        {
            if (m_Engine != null)
            {
                LetteraRangeRouter.Forget(m_Engine);
                LetteraInlineObjects.Forget(m_Engine);
                m_Engine.AbandonDocument();
            }
        }

        void LateUpdate()
        {
            RebuildIfDirty();
            // A wave with a speed moves every frame, with or without any motion of the text's own.
            if (m_Builder != null && (m_Builder.DeformAnimated || m_SolidPose != null) && Application.isPlaying && !m_Hidden) ApplyPose();
        }

        internal void RebuildIfDirty()
        {
            if (m_Builder == null) return;
            bool engineDirty = Engine.Dirty != DirtyLevel.None;
            if (engineDirty) Engine.Rebuild();
            if (engineDirty || m_SolidsDirty || m_ColorsDirty) RebuildMesh();
            if (engineDirty) RaiseRebuilt(Engine.LastChanges);
        }

        /// <summary>The formulas this text draws, with where they stand and whether they could be read.</summary>
        public void GetFormulas(List<LetteraFormulaInfo> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            ForceRebuild();
            Engine.CollectFormulas(output);
        }

        bool m_Hidden;

        /// <summary>False while hidden with Hide().</summary>
        public bool IsVisible => !m_Hidden;

        /// <summary>Stops drawing, keeping all rebuilt state. A hidden text does no per-frame work.</summary>
        public void Hide()
        {
            if (m_Hidden) return;
            m_Hidden = true;
            if (m_Renderer != null) m_Renderer.enabled = false;
            SolidsWritten?.Invoke(false);
        }

        /// <summary>Draws again without a rebuild, unless something changed meanwhile.</summary>
        public void Show()
        {
            if (!m_Hidden) return;
            m_Hidden = false;
            if (m_Renderer != null) m_Renderer.enabled = true;
            SolidsWritten?.Invoke(false);
        }

        /// <summary>Rebuilds now, e.g. before reading results in the same frame.</summary>
        public void ForceRebuild()
        {
            if (m_Builder == null) return;
            if (Engine.Dirty != DirtyLevel.None || m_SolidsDirty || m_ColorsDirty) RebuildIfDirty();
        }

        /// <summary>The characters to build ahead, with their outlines, whatever is shown now.</summary>
        public void Warm(string characters)
        {
            if (string.IsNullOrEmpty(characters) || m_Builder == null) return;
            FontFace face = Engine.PrimaryFaceForMath;
            if (face == null) return;
            LetteraSolidRecipe recipe = Recipe;
            for (int i = 0; i < characters.Length; i++)
            {
                int cp = char.ConvertToUtf32(characters, i);
                if (char.IsHighSurrogate(characters[i])) i++;
                uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, (uint)cp, 0);
                if (glyph != 0) m_Warm.Add(LetteraSolidCache.Acquire(face.Regular, glyph, recipe));
            }
        }

        void ReleaseAll()
        {
            foreach (LetteraSolidEntry entry in m_Held) LetteraSolidCache.Release(entry);
            m_Held.Clear();
            foreach (LetteraSolidEntry entry in m_Warm) LetteraSolidCache.Release(entry);
            m_Warm.Clear();
        }

        void DropMesh()
        {
            if (m_Filter != null) m_Filter.sharedMesh = null;
            if (m_Mesh == null) return;
            if (m_MeshIsShared)
            {
                LetteraSharedSolidMeshes.Release(m_Mesh);
            }
            else
            {
                if (Application.isPlaying) Destroy(m_Mesh);
                else DestroyImmediate(m_Mesh);
            }
            m_Mesh = null;
            m_MeshIsShared = false;
        }

        // ------------------------------------------------------------------ mesh

        void RebuildMesh()
        {
            bool rebuildSolids = m_SolidsDirty;
            m_SolidsDirty = false;
            m_ColorsDirty = false;
            LetteraSolidRecipe recipe = Recipe;
            // Those held before go only after this pass has taken its own, so that nothing is dropped and built again.
            List<LetteraSolidEntry> previous = m_HeldBefore;
            previous.Clear();
            previous.AddRange(m_Held);
            m_Held.Clear();
            if (m_Warm.Count == 0 && !string.IsNullOrEmpty(m_WarmCharacters)) Warm(m_WarmCharacters);
            m_Builder.Begin(Engine, recipe, m_Depth, m_Pivot, m_ColorGlyphs == LetteraSolidColorGlyphs.Flat, this, m_Held);
            for (int i = 0; i < previous.Count; i++) LetteraSolidCache.Release(previous[i]);
            previous.Clear();
            ResolveMaterials();
            WriteMesh();
            // What moves in depth starts where it is now, not at rest for a frame.
            if (m_SolidPose != null || (m_Builder.HasMotion && LetteraSolidPoser.Wanted(Engine, null))) ApplyPose();
            LetteraSolidFlatChild.Update(this);
            SolidsWritten?.Invoke(true);
        }

        /// <summary>After the solids were built (true) or only moved (false): what follows the drawn text listens here.</summary>
        internal event Action<bool> SolidsWritten;

        int[] m_SurfaceToSubmesh = new int[5];
        Material[] m_Given;

        /// <summary>The material a layer's surface ends up with: its own, then the text's.</summary>
        public Material GetLayerMaterial(LetteraSolidLayer layer, LetteraSolidSurface surface)
        {
            if (layer == null) return GetSurfaceMaterial(surface);
            if (surface == LetteraSolidSurface.Side && layer.SideMaterial != null) return layer.SideMaterial;
            return layer.Material != null ? layer.Material : GetSurfaceMaterial(surface);
        }

        int Submesh(Material material)
        {
            int index = m_Materials.IndexOf(material);
            if (index >= 0) return index;
            m_Materials.Add(material);
            return m_Materials.Count - 1;
        }

        void ResolveMaterials()
        {
            m_Materials.Clear();
            int maps = 5 * (1 + m_Layers.Count), all = maps + RangeMapSlots;
            if (m_SurfaceToSubmesh.Length != all) m_SurfaceToSubmesh = new int[all];
            for (int s = 0; s < 5; s++) m_SurfaceToSubmesh[s] = Submesh(GetSurfaceMaterial((LetteraSolidSurface)s));
            for (int l = 0; l < m_Layers.Count; l++)
            {
                LetteraSolidLayer layer = m_Layers[l];
                bool used = layer != null && layer.Enabled;
                for (int s = 0; s < 5; s++) m_SurfaceToSubmesh[5 + l * 5 + s] = used ? Submesh(GetLayerMaterial(layer, (LetteraSolidSurface)s)) : 0;
            }
            ResolveRangeMaterials(maps);
            m_SubmeshCount = m_Materials.Count;
            // A renderer hands out a new array each time it is asked, so what it was given is remembered here.
            bool same = m_Given != null && m_Given.Length == m_Materials.Count;
            for (int i = 0; same && i < m_Given.Length; i++) same = m_Given[i] == m_Materials[i];
            if (!same && m_Renderer != null)
            {
                m_Given = m_Materials.ToArray();
                m_Renderer.sharedMaterials = m_Given;
            }
        }

        void WriteMesh()
        {
            bool share = m_ShareMesh && !Engine.PosesObjects && !m_Builder.HasMotion && !m_Builder.HasGroupLayers && !m_Builder.DeformAnimated && m_SolidPose == null;
            if (share)
            {
                ulong hash = m_Builder.ContentHash(m_SubmeshCount, m_SurfaceToSubmesh);
                Mesh found = LetteraSharedSolidMeshes.Find(hash);
                if (found != null)
                {
                    if (found != m_Mesh)
                    {
                        DropMesh();
                        m_Mesh = found;
                        m_MeshIsShared = true;
                        LetteraSharedSolidMeshes.Retain(found);
                        if (m_Filter != null) m_Filter.sharedMesh = found;
                    }
                    return;
                }
                if (m_MeshIsShared) DropMesh();
                m_Mesh = new Mesh { name = "Lettera Solid", hideFlags = HideFlags.HideAndDontSave };
                m_MeshIsShared = true;
                LetteraSharedSolidMeshes.Add(hash, m_Mesh);
            }
            else if (m_Mesh == null || m_MeshIsShared)
            {
                if (m_MeshIsShared) DropMesh();
                m_Mesh = new Mesh { name = "Lettera Solid", hideFlags = HideFlags.HideAndDontSave };
                m_MeshIsShared = false;
            }
            if (++m_ChangeCount == 2 && !m_MeshIsShared) m_Mesh.MarkDynamic();
            m_Builder.Write(m_Mesh, m_SubmeshCount, m_SurfaceToSubmesh);
            if (m_Filter != null && m_Filter.sharedMesh != m_Mesh) m_Filter.sharedMesh = m_Mesh;
        }

        void ApplyPose()
        {
            if (m_Mesh == null || m_MeshIsShared || m_SolidsDirty) return;
            m_Builder.ApplyPose(Engine, m_Mesh);
            LetteraSolidFlatChild.Update(this);
            SolidsWritten?.Invoke(false);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (m_Data == null) return;
            Engine.SyncSerializedFields();
            m_SolidsDirty = true;
            Engine.SetDirty(DirtyLevel.Parse);
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        void Reset()
        {
            if (string.IsNullOrEmpty(m_Data.Text)) m_Data.Text = "3D Text";
            if (Rect.sizeDelta == new Vector2(100, 100))
            {
                Rect.sizeDelta = new Vector2(20, 5);
                m_Data.FontSize = 2f;
            }
            if (m_FaceMaterial == null) m_FaceMaterial = LetteraSolidDefaults.EditorDefault();
            LetteraSettings.SeedFromDefault?.Invoke(this);
        }
#endif
    }

    /// <summary>A vertex as the mesh holds it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LetteraSolidMeshVertex
    {
        public float3 Position;
        public float3 Normal;
        public Color32 Color;
        public float2 Uv0;
        public float2 Uv1;
    }

    /// <summary>Where a text's solids come from, and how they fall back when nothing was assigned.</summary>
    internal static class LetteraSolidDefaults
    {
        const string ShaderName = "Lettera/Solid Lit";
        static Material s_Runtime;

        internal static Material Material(ref bool warned)
        {
            Material set = LetteraSettings.Instance.Solid3D.DefaultMaterial;
            if (set != null) return set;
            if (s_Runtime != null) return s_Runtime;
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                s_Runtime = new Material(shader) { name = "Lettera Solid (runtime)", hideFlags = HideFlags.HideAndDontSave };
                return s_Runtime;
            }
            if (!warned)
            {
                warned = true;
                Debug.LogError("Lettera: a Text 3D has no material, and its shader is not in this build. Assign a material on the component, or use Lettera > 3D > Allow Creating From Scripts.");
            }
            return null;
        }

#if UNITY_EDITOR
        internal const string DefaultMaterialName = "LetteraSolidDefault";

        internal static Material EditorDefault()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets(DefaultMaterialName + " t:Material"))
            {
                var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (material != null) return material;
            }
            return null;
        }
#endif
    }

    /// <summary>Identical texts draw one mesh. A text that moves leaves it.</summary>
    internal static class LetteraSharedSolidMeshes
    {
        sealed class Shared
        {
            public Mesh Mesh;
            public ulong Hash;
            public int References;
        }

        static readonly Dictionary<ulong, Shared> s_ByHash = new Dictionary<ulong, Shared>();
        static readonly Dictionary<Mesh, Shared> s_ByMesh = new Dictionary<Mesh, Shared>();

        internal static int Count => s_ByHash.Count;

        internal static Mesh Find(ulong hash) => s_ByHash.TryGetValue(hash, out Shared shared) && shared.Mesh != null ? shared.Mesh : null;

        internal static void Add(ulong hash, Mesh mesh)
        {
            var shared = new Shared { Mesh = mesh, Hash = hash, References = 1 };
            s_ByHash[hash] = shared;
            s_ByMesh[mesh] = shared;
        }

        internal static void Retain(Mesh mesh)
        {
            if (s_ByMesh.TryGetValue(mesh, out Shared shared)) shared.References++;
        }

        internal static void Release(Mesh mesh)
        {
            if (mesh == null || !s_ByMesh.TryGetValue(mesh, out Shared shared)) return;
            if (--shared.References > 0) return;
            s_ByMesh.Remove(mesh);
            s_ByHash.Remove(shared.Hash);
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh);
        }

        internal static int ReferencesOf(Mesh mesh) => mesh != null && s_ByMesh.TryGetValue(mesh, out Shared shared) ? shared.References : 0;
    }

    /// <summary>Copies solids into one text's vertices. A cached rebuild allocates nothing managed.</summary>
    internal sealed unsafe class LetteraSolidMeshBuilder : IDisposable
    {
        internal struct Placement
        {
            public LetteraSolidEntry Entry;
            public int Cluster, GlyphIndex;
            public float X, Y, SizeX, SizeY, SizeZ, Cos, Sin, Depth, BevelDepth, ZOffset, Unit;
            public Color32 Color;
            public int VertexStart, VertexCount;
            /// <summary>Which surface map it takes: 0 the text's, 1 and on its layers'.</summary>
            public int Map;
        }

        readonly List<Placement> m_Placed = new List<Placement>();
        NativeArray<LetteraSolidMeshVertex> m_Rest, m_Posed;
        NativeArray<CopyItem> m_Items;
        NativeArray<IndexItem> m_IndexItems;
        NativeArray<float3> m_ItemEnds;
        int m_VertexCount, m_IndexCount;
        bool m_Over;
        float m_Scale;
        Bounds m_RestBounds;
        readonly LetteraDeformChain m_Chain = new LetteraDeformChain();
        readonly LetteraSolidRanges m_Ranges = new LetteraSolidRanges();
        readonly LetteraSolidPoser m_Poser = new LetteraSolidPoser();
        internal LetteraSolidRanges Ranges => m_Ranges;
        internal LetteraSolidPoser Poser => m_Poser;
        int m_GlyphPlacements;
        readonly List<LetteraDeformer> m_TextDeformers = new List<LetteraDeformer>();
        // Layers made per word, line or text belong to this text alone, and are kept while their glyphs are the same.
        Dictionary<ulong, LetteraSolidEntry> m_Groups = new Dictionary<ulong, LetteraSolidEntry>(), m_GroupsBefore = new Dictionary<ulong, LetteraSolidEntry>();
        internal bool HasMotion { get; private set; }
        internal int VertexCount => m_VertexCount;
        internal int TriangleCount => m_IndexCount / 3;
        internal bool OverLimit => m_Over;
        internal Bounds Bounds { get; private set; }
        internal int FailedGlyphs { get; private set; }
        internal IReadOnlyList<Placement> Placed => m_Placed;
        /// <summary>Where each vertex stands before motion and deformers, and where it is drawn now.</summary>
        internal NativeArray<LetteraSolidMeshVertex> RestVertices => m_Rest;
        internal NativeArray<LetteraSolidMeshVertex> PosedVertices => m_Posed;
        internal static int IndexWrites { get; private set; }
        /// <summary>Whether a deformer moves with time, which makes every frame a pose frame.</summary>
        internal bool DeformAnimated => m_Chain.Any && m_Chain.Animated;
        internal bool Deformed => m_Chain.Any;
        /// <summary>Layers made per word, line or text are this text's own, so its mesh is too.</summary>
        internal bool HasGroupLayers => m_Groups.Count > 0;
        internal int GroupLayerCount => m_Groups.Count;

        public void Dispose()
        {
            if (m_Rest.IsCreated) m_Rest.Dispose();
            if (m_Posed.IsCreated) m_Posed.Dispose();
            if (m_Items.IsCreated) m_Items.Dispose();
            if (m_IndexItems.IsCreated) m_IndexItems.Dispose();
            if (m_ItemEnds.IsCreated) m_ItemEnds.Dispose();
            foreach (LetteraSolidEntry entry in m_Groups.Values) entry.Release();
            m_Groups.Clear();
            m_Chain.Dispose();
            m_Ranges.Dispose();
            if (m_Maps.IsCreated) m_Maps.Dispose();
        }

        internal void Begin(TextEngine engine, in LetteraSolidRecipe recipe, float depth, LetteraSolidPivot pivot, bool flatColor, LetteraText3D owner, List<LetteraSolidEntry> held)
        {
            m_Placed.Clear();
            FailedGlyphs = 0;
            HasMotion = engine.HasMotionForSolids;
            ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
            int vertices = 0, limit = LetteraSettings.Instance.Solid3D.MaxVertices;
            float scale = engine.CurrentFontSize > 0 ? engine.CurrentFontSize : owner.FontSize;
            m_Scale = scale;
            LetteraSolidCache.BuildAhead(engine, recipe);
            m_Over = false;
            for (int i = 0; i < glyphs.Length; i++)
            {
                ref readonly TextEngine.PlacedGlyph g = ref glyphs[i];
                if (engine.IsDrawnFlatForSolids(i)) continue;
                LetteraSolidEntry entry;
                bool rule = g.Special == TextEngine.SpecialRuleForSolids;
                float length = 1, thickness = 1;
                LetteraSolidRecipe own = recipe;
                float depthEm = depth;
                LetteraSolidEffects.Read(engine, g.Cluster, ref own, ref depthEm);
                if (rule)
                {
                    engine.RuleOfForSolids(g.SpecialIndex, out length, out thickness, out bool vertical);
                    if (vertical)
                    {
                        float swap = length;
                        length = thickness;
                        thickness = swap;
                    }
                    entry = LetteraSolidCache.Acquire(null, LetteraSolidCache.UnitBoxGlyph, own);
                }
                else
                {
                    entry = LetteraSolidCache.Acquire(g.Instance, g.Glyph, own);
                }
                held.Add(entry);
                if (entry.Failed)
                {
                    FailedGlyphs++;
                    continue;
                }
                if (entry.Vertices.Length == 0) continue;
                if (vertices + entry.Vertices.Length > limit)
                {
                    m_Over = true;
                    continue;
                }
                float sizeX = g.Size * (1 + g.XScale), size = g.Size;
                if (rule)
                {
                    sizeX = g.Unit * length;
                    size = g.Unit * thickness;
                }
                float bevels = entry.FrontHeight + entry.BackHeight;
                float zOffset = pivot == LetteraSolidPivot.Front ? 0 : pivot == LetteraSolidPivot.Back ? -depthEm * g.Size : -depthEm * g.Size * 0.5f;
                m_Placed.Add(new Placement
                {
                    Entry = entry,
                    Cluster = g.Cluster,
                    GlyphIndex = i,
                    X = g.X,
                    Y = g.Y,
                    SizeX = sizeX,
                    SizeY = size,
                    SizeZ = g.Size,
                    Cos = g.Angle != 0 ? Mathf.Cos(g.Angle) : 1,
                    Sin = g.Angle != 0 ? Mathf.Sin(g.Angle) : 0,
                    Depth = depthEm,
                    BevelDepth = bevels,
                    ZOffset = zOffset,
                    Color = engine.SolidColorOf(i),
                    VertexStart = vertices,
                    VertexCount = entry.Vertices.Length,
                    Unit = g.Unit,
                });
                vertices += entry.Vertices.Length;
            }
            int glyphPlacements = m_Placed.Count;
            m_GlyphPlacements = glyphPlacements;
            m_Ranges.Assign(engine, m_Placed, glyphPlacements, 1 + (owner.Layers?.Count ?? 0));
            ValueList<TextEngine.SolidBox> boxes = engine.SolidBoxes;
            for (int b = 0; b < boxes.Count; b++)
            {
                TextEngine.SolidBox box = boxes[b];
                LetteraSolidEntry unit = LetteraSolidCache.Acquire(null, LetteraSolidCache.UnitBoxGlyph, recipe);
                held.Add(unit);
                // A deformer curves a line only where it has corners, so a long one goes in short pieces.
                int pieces = recipe.MaxEdge > 0 ? Mathf.Clamp(Mathf.CeilToInt(box.Rect.width / Mathf.Max(recipe.MaxEdge * box.Em, 1e-4f)), 1, 512) : 1;
                float zOffset = pivot == LetteraSolidPivot.Front ? 0 : pivot == LetteraSolidPivot.Back ? -depth * box.Em : -depth * box.Em * 0.5f;
                for (int piece = 0; piece < pieces; piece++)
                {
                    if (vertices + unit.Vertices.Length > limit)
                    {
                        m_Over = true;
                        break;
                    }
                    float w = box.Rect.width / pieces;
                    m_Placed.Add(new Placement
                    {
                        Entry = unit, Cluster = -1, GlyphIndex = box.Glyph,
                        X = box.Rect.x + w * piece, Y = box.Rect.y, SizeX = w, SizeY = box.Rect.height, SizeZ = box.Em,
                        Cos = 1, Sin = 0, Depth = depth, BevelDepth = 0, ZOffset = zOffset, Color = box.Color,
                        VertexStart = vertices, VertexCount = unit.Vertices.Length,
                    });
                    vertices += unit.Vertices.Length;
                }
            }
            PlaceLayers(engine, recipe, depth, pivot, owner.Layers, glyphPlacements, scale, held, ref vertices, limit);

            m_VertexCount = vertices;
            if (m_Rest.Length < vertices)
            {
                if (m_Rest.IsCreated) m_Rest.Dispose();
                if (m_Posed.IsCreated) m_Posed.Dispose();
                int capacity = Mathf.Max(vertices, m_Rest.Length * 2);
                m_Rest = new NativeArray<LetteraSolidMeshVertex>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_Posed = new NativeArray<LetteraSolidMeshVertex>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            else if (!m_Rest.IsCreated)
            {
                m_Rest = new NativeArray<LetteraSolidMeshVertex>(Mathf.Max(vertices, 16), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_Posed = new NativeArray<LetteraSolidMeshVertex>(Mathf.Max(vertices, 16), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            int indices = 0;
            if (!m_Items.IsCreated || m_Items.Length < m_Placed.Count)
            {
                if (m_Items.IsCreated) m_Items.Dispose();
                if (m_ItemEnds.IsCreated) m_ItemEnds.Dispose();
                m_Items = new NativeArray<CopyItem>(Mathf.Max(m_Placed.Count, 64), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m_ItemEnds = new NativeArray<float3>(Mathf.Max(m_Placed.Count, 64) * 2, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            for (int p = 0; p < m_Placed.Count; p++)
            {
                Placement place = m_Placed[p];
                m_Items[p] = new CopyItem
                {
                    Source = (LetteraSolidVertex*)place.Entry.Vertices.GetUnsafeReadOnlyPtr(),
                    Start = place.VertexStart, Count = place.VertexCount,
                    X = place.X, Y = place.Y, SizeX = place.SizeX, SizeY = place.SizeY, SizeZ = place.SizeZ,
                    Cos = place.Cos, Sin = place.Sin, Extra = place.Depth - place.BevelDepth, BevelDepth = place.BevelDepth, Depth = place.Depth, ZOffset = place.ZOffset,
                    Color = place.Color,
                };
                indices += place.Entry.Indices.Length;
            }
            var ends = new NativeArray<float3>(2, Allocator.TempJob);
            new CopyJob { Items = m_Items, Count = m_Placed.Count, Target = m_Rest, Ends = ends, ItemEnds = m_ItemEnds }.Run();
            float3 min = ends[0], max = ends[1];
            ends.Dispose();
            m_Ranges.PoseRest(engine, m_Placed, m_Rest, m_ItemEnds, ref min, ref max);
            m_IndexCount = indices;
            if (m_VertexCount > 0) m_Posed.GetSubArray(0, m_VertexCount).CopyFrom(m_Rest.GetSubArray(0, m_VertexCount));
            m_RestBounds = m_VertexCount == 0 ? new Bounds(Vector3.zero, Vector3.zero) : new Bounds((min + max) * 0.5f, max - min);
            Bounds = m_RestBounds;
            PrepareDeform(engine, owner.Deformers, Application.isPlaying ? Time.time : 0);
            if (m_Chain.Any) Bounds = m_Chain.Run(m_Posed, m_VertexCount, m_Scale);
        }

        // ------------------------------------------------------------------ layers

        void PlaceLayers(TextEngine engine, in LetteraSolidRecipe recipe, float depth, LetteraSolidPivot pivot, IReadOnlyList<LetteraSolidLayer> layers, int glyphPlacements, float scale, List<LetteraSolidEntry> held, ref int vertices, int limit)
        {
            Dictionary<ulong, LetteraSolidEntry> swap = m_GroupsBefore;
            m_GroupsBefore = m_Groups;
            m_Groups = swap;
            m_Groups.Clear();
            if (layers != null)
            {
                for (int l = 0; l < layers.Count; l++)
                {
                    LetteraSolidLayer layer = layers[l];
                    if (layer == null || !layer.Enabled) continue;
                    LetteraSolidRecipe own = LetteraSolidLayerBuilder.RecipeOf(layer, recipe);
                    int parts = layer.Cut == LetteraSolidLayerCut.Engraved && !layer.IsTube ? 2 : 1;
                    if (layer.Scope == LetteraSolidLayerScope.Glyph)
                    {
                        for (int p = 0; p < glyphPlacements; p++)
                        {
                            Placement glyph = m_Placed[p];
                            ref readonly TextEngine.PlacedGlyph g = ref engine.PlacedSpan[glyph.GlyphIndex];
                            if (g.Special == TextEngine.SpecialRuleForSolids || g.Instance == null) continue;
                            for (int part = 0; part < parts; part++)
                            {
                                LetteraSolidEntry entry = LetteraSolidCache.AcquireLayer(g.Instance, g.Glyph, layer, part, own);
                                held.Add(entry);
                                if (!entry.Vertices.IsCreated || entry.Vertices.Length == 0) continue;
                                if (vertices + entry.Vertices.Length > limit)
                                {
                                    m_Over = true;
                                    continue;
                                }
                                Color32 color = layer.Color;
                                color.a = (byte)(color.a * glyph.Color.a / 255);
                                AddLayer(layer, l, part, entry, glyph.Cluster, glyph.GlyphIndex, glyph.X + layer.Shift.x * g.Size, glyph.Y + layer.Shift.y * g.Size, glyph.SizeX, glyph.SizeY, g.Size, glyph.Cos, glyph.Sin, depth, pivot, color, ref vertices);
                            }
                        }
                        continue;
                    }
                    PlaceGroups(engine, layer, l, parts, own, depth, pivot, scale, ref vertices, limit);
                }
            }
            foreach (KeyValuePair<ulong, LetteraSolidEntry> old in m_GroupsBefore)
                if (!m_Groups.ContainsKey(old.Key))
                    old.Value.Release();
            m_GroupsBefore.Clear();
        }

        void AddLayer(LetteraSolidLayer layer, int index, int part, LetteraSolidEntry entry, int cluster, int glyph, float x, float y, float sizeX, float sizeY, float sizeZ, float cos, float sin, float textDepth, LetteraSolidPivot pivot, Color32 color, ref int vertices)
        {
            float front = pivot == LetteraSolidPivot.Front ? 0 : pivot == LetteraSolidPivot.Back ? -textDepth * sizeZ : -textDepth * sizeZ * 0.5f;
            float anchor = layer.Anchor == LetteraSolidLayerAnchor.Front ? front : front + textDepth * sizeZ;
            float z = anchor + layer.Z * sizeZ;
            float own = layer.SolidDepth;
            if (layer.Cut == LetteraSolidLayerCut.Engraved && !layer.IsTube)
            {
                float cut = Mathf.Clamp(layer.CutDepth, 0.001f, Mathf.Max(own - 0.001f, 0.001f));
                if (part == 0) own = cut;
                else
                {
                    z += cut * sizeZ;
                    own = Mathf.Max(own - cut, 0.001f);
                }
            }
            m_Placed.Add(new Placement
            {
                Entry = entry, Cluster = cluster, GlyphIndex = glyph,
                X = x, Y = y, SizeX = sizeX, SizeY = sizeY, SizeZ = sizeZ, Cos = cos, Sin = sin,
                Depth = own, BevelDepth = entry.FrontHeight + entry.BackHeight, ZOffset = z, Color = color,
                VertexStart = vertices, VertexCount = entry.Vertices.Length, Map = index + 1,
            });
            vertices += entry.Vertices.Length;
        }

        readonly List<int> m_GroupGlyphs = new List<int>();

        // Glyphs in reading order, cut where a space, a line or nothing ends a word or a line.
        void PlaceGroups(TextEngine engine, LetteraSolidLayer layer, int index, int parts, in LetteraSolidRecipe recipe, float depth, LetteraSolidPivot pivot, float scale, ref int vertices, int limit)
        {
            ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
            ReadOnlySpan<int> text = engine.ShownCodepoints;
            m_GroupGlyphs.Clear();
            int line = int.MinValue;
            for (int i = 0; i <= glyphs.Length; i++)
            {
                bool cut = i == glyphs.Length;
                bool skip = false;
                if (!cut)
                {
                    ref readonly TextEngine.PlacedGlyph g = ref glyphs[i];
                    bool space = g.Cluster >= 0 && g.Cluster < text.Length && FirstSymbol.Lettera.Unicode.LetteraUnicode.IsWhiteSpace(text[g.Cluster]);
                    skip = space || g.Instance == null || g.Special == TextEngine.SpecialRuleForSolids || engine.IsDrawnFlatForSolids(i);
                    if (layer.Scope == LetteraSolidLayerScope.Word && space) cut = true;
                    if (layer.Scope != LetteraSolidLayerScope.Text && line != int.MinValue && g.Line != line && m_GroupGlyphs.Count > 0) cut = true;
                }
                if (cut && m_GroupGlyphs.Count > 0)
                {
                    BuildGroup(engine, layer, index, parts, recipe, depth, pivot, scale, ref vertices, limit);
                    m_GroupGlyphs.Clear();
                }
                if (i < glyphs.Length && !skip)
                {
                    m_GroupGlyphs.Add(i);
                    line = glyphs[i].Line;
                }
            }
        }

        void BuildGroup(TextEngine engine, LetteraSolidLayer layer, int index, int parts, in LetteraSolidRecipe recipe, float depth, LetteraSolidPivot pivot, float scale, ref int vertices, int limit)
        {
            ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
            ref readonly TextEngine.PlacedGlyph first = ref glyphs[m_GroupGlyphs[0]];
            float2 origin = new float2(first.X, first.Y);
            float inverse = 1f / Mathf.Max(scale, 1e-4f);
            for (int part = 0; part < parts; part++)
            {
                ulong hash = 14695981039346656037UL;
                unchecked
                {
                    void Mix(ulong v) => hash = (hash ^ v) * 1099511628211UL;
                    Mix((ulong)(uint)layer.ShapeHash(part));
                    Mix((ulong)(uint)recipe.HashOf());
                    Mix((ulong)(uint)scale.GetHashCode());
                    foreach (int gi in m_GroupGlyphs)
                    {
                        ref readonly TextEngine.PlacedGlyph g = ref glyphs[gi];
                        Mix((ulong)(uint)g.Instance.Id);
                        Mix(g.Glyph);
                        Mix((ulong)(uint)(g.X - origin.x).GetHashCode());
                        Mix((ulong)(uint)(g.Y - origin.y).GetHashCode());
                        Mix((ulong)(uint)(g.Size * (1 + g.XScale)).GetHashCode());
                        Mix((ulong)(uint)g.Angle.GetHashCode());
                    }
                }
                if (!m_Groups.TryGetValue(hash, out LetteraSolidEntry entry))
                {
                    if (!m_GroupsBefore.TryGetValue(hash, out entry))
                    {
                        var segments = new NativeList<float4>(1024, Allocator.TempJob);
                        float2 low = new float2(float.MaxValue), high = new float2(float.MinValue);
                        foreach (int gi in m_GroupGlyphs)
                        {
                            ref readonly TextEngine.PlacedGlyph g = ref glyphs[gi];
                            float cos = g.Angle != 0 ? Mathf.Cos(g.Angle) : 1, sin = g.Angle != 0 ? Mathf.Sin(g.Angle) : 0;
                            LetteraSolidLayerBuilder.AddGlyph(g.Instance, g.Glyph, (new float2(g.X, g.Y) - origin) * inverse, g.Size * (1 + g.XScale) * inverse, g.Size * inverse, cos, sin, ref segments, ref low, ref high);
                        }
                        var key = new LetteraSolidKey { Instance = -1, Glyph = (uint)index, Edges = recipe.HashOf(), Layer = layer.ShapeHash(part) };
                        entry = LetteraSolidLayerBuilder.Build(key, segments, low, high, layer, part, recipe) ?? new LetteraSolidEntry { Key = key };
                        segments.Dispose();
                    }
                    m_Groups[hash] = entry;
                }
                if (!entry.Vertices.IsCreated || entry.Vertices.Length == 0) continue;
                if (vertices + entry.Vertices.Length > limit)
                {
                    m_Over = true;
                    continue;
                }
                AddLayer(layer, index, part, entry, -1, -1, origin.x + layer.Shift.x * scale, origin.y + layer.Shift.y * scale, scale, scale, scale, 1, 0, depth, pivot, layer.Color, ref vertices);
            }
        }

        // ------------------------------------------------------------------ deform

        readonly Dictionary<ulong, int2> m_RangeSets = new Dictionary<ulong, int2>();
        readonly List<int> m_RangeOps = new List<int>();

        void PrepareDeform(TextEngine engine, IReadOnlyList<LetteraDeformer> deformers, float time)
        {
            m_Chain.Begin();
            if (m_VertexCount == 0) return;
            float3 min = m_RestBounds.min, max = m_RestBounds.max;
            if (deformers != null)
                for (int d = 0; d < deformers.Count; d++)
                    m_Chain.AddText(deformers[d], min, max, m_Scale, time);
            ValueList<LetteraAttributes.ExtraRange> extras = engine.ExtrasForSolids;
            if (extras.Count == 0) return;
            // Each range measures by its own glyphs, and a glyph in several takes them in order.
            m_RangeOps.Clear();
            int ranges = 0;
            for (int e = 0; e < extras.Count && ranges < 64; e++)
            {
                LetteraAttributes.ExtraRange extra = extras[e];
                if (extra.Key != LetteraSolidEffects.DeformKey || !(extra.Payload is LetteraDeformer deformer)) continue;
                float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
                for (int p = 0; p < m_Placed.Count; p++)
                {
                    int cluster = m_Placed[p].Cluster;
                    if (cluster < extra.Start || cluster >= extra.End || m_Placed[p].VertexCount == 0) continue;
                    low = math.min(low, m_ItemEnds[p * 2]);
                    high = math.max(high, m_ItemEnds[p * 2 + 1]);
                }
                if (low.x > high.x) continue;
                m_RangeOps.Add(e);
                m_RangeOps.Add(m_Chain.AddRange(deformer, low, high, m_Scale, time));
                ranges++;
            }
            if (m_RangeOps.Count == 0) return;
            m_RangeSets.Clear();
            for (int p = 0; p < m_Placed.Count; p++)
            {
                int cluster = m_Placed[p].Cluster;
                if (cluster < 0) continue;
                ulong set = 0;
                for (int r = 0; r < m_RangeOps.Count; r += 2)
                {
                    LetteraAttributes.ExtraRange extra = extras[m_RangeOps[r]];
                    if (cluster >= extra.Start && cluster < extra.End && m_RangeOps[r + 1] >= 0) set |= 1UL << (r / 2);
                }
                if (set == 0) continue;
                if (!m_RangeSets.TryGetValue(set, out int2 ops))
                {
                    // Ops a glyph takes lie side by side; a glyph in one range uses the range's own.
                    int count = math.countbits(set);
                    if (count == 1)
                    {
                        ops = new int2(m_RangeOps[math.tzcnt(set) * 2 + 1], 1);
                    }
                    else
                    {
                        int start = -1;
                        for (int r = 0; r < m_RangeOps.Count / 2; r++)
                        {
                            if ((set & (1UL << r)) == 0) continue;
                            int extraIndex = m_RangeOps[r * 2];
                            LetteraAttributes.ExtraRange extra = extras[extraIndex];
                            float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
                            for (int q = 0; q < m_Placed.Count; q++)
                            {
                                int c = m_Placed[q].Cluster;
                                if (c < extra.Start || c >= extra.End || m_Placed[q].VertexCount == 0) continue;
                                low = math.min(low, m_ItemEnds[q * 2]);
                                high = math.max(high, m_ItemEnds[q * 2 + 1]);
                            }
                            int op = m_Chain.AddRange((LetteraDeformer)extra.Payload, low, high, m_Scale, time);
                            if (start < 0) start = op;
                        }
                        ops = new int2(start, count);
                    }
                    m_RangeSets[set] = ops;
                }
                m_Chain.AddSpan(m_Placed[p].VertexStart, m_Placed[p].VertexCount, ops.x, ops.y);
            }
        }

        // ------------------------------------------------------------------ copy

        unsafe struct CopyItem
        {
            [NativeDisableUnsafePtrRestriction] public LetteraSolidVertex* Source;
            public int Start, Count;
            public float X, Y, SizeX, SizeY, SizeZ, Cos, Sin, Extra, BevelDepth, Depth, ZOffset;
            public Color32 Color;
        }

        [BurstCompile]
        unsafe struct CopyJob : IJob
        {
            [ReadOnly] public NativeArray<CopyItem> Items;
            public int Count;
            [NativeDisableParallelForRestriction] public NativeArray<LetteraSolidMeshVertex> Target;
            public NativeArray<float3> Ends;
            public NativeArray<float3> ItemEnds;

            public void Execute()
            {
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int p = 0; p < Count; p++)
                {
                    CopyItem item = Items[p];
                    float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
                    // Bevels deeper than the text fold flat rather than through each other.
                    float squeeze = item.BevelDepth > 0 && item.Depth < item.BevelDepth ? math.max(item.Depth, 0) / item.BevelDepth : 1;
                    float extra = math.max(item.Extra, 0);
                    for (int i = 0; i < item.Count; i++)
                    {
                        LetteraSolidVertex v = item.Source[i];
                        float lx = v.Position.x * item.SizeX, ly = v.Position.y * item.SizeY;
                        float3 normal = v.Normal;
                        float3 position = new float3(item.X + lx * item.Cos - ly * item.Sin, item.Y + lx * item.Sin + ly * item.Cos, (v.Position.z * squeeze + v.Weight * extra) * item.SizeZ + item.ZOffset);
                        low = math.min(low, position);
                        high = math.max(high, position);
                        Target[item.Start + i] = new LetteraSolidMeshVertex
                        {
                            Position = position,
                            Normal = new float3(normal.x * item.Cos - normal.y * item.Sin, normal.x * item.Sin + normal.y * item.Cos, normal.z),
                            Color = item.Color,
                            Uv0 = v.Uv,
                            Uv1 = new float2(item.X + lx, item.Y + ly),
                        };
                    }
                    ItemEnds[p * 2] = low;
                    ItemEnds[p * 2 + 1] = high;
                    min = math.min(min, low);
                    max = math.max(max, high);
                }
                Ends[0] = min;
                Ends[1] = max;
            }
        }

        /// <summary>A hash of everything that shows, so identical texts find each other.</summary>
        internal ulong ContentHash(int submeshes, int[] maps)
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                void Mix(ulong value) => h = (h ^ value) * 1099511628211UL;
                Mix((ulong)m_VertexCount);
                Mix((ulong)submeshes);
                for (int s = 0; s < maps.Length; s++) Mix((ulong)maps[s]);
                if (m_Chain.Any) Mix(m_Chain.HashOf());
                Mix(m_Ranges.HashOf());
                for (int p = 0; p < m_Placed.Count; p++)
                {
                    Placement place = m_Placed[p];
                    Mix((ulong)(uint)place.Entry.Key.GetHashCode());
                    Mix((ulong)place.Map);
                    Mix(BitConverter.DoubleToInt64Bits(place.X) == 0 ? 0 : (ulong)BitConverter.DoubleToInt64Bits(place.X));
                    Mix((ulong)BitConverter.DoubleToInt64Bits(place.Y));
                    Mix((ulong)BitConverter.DoubleToInt64Bits(place.SizeX));
                    Mix((ulong)BitConverter.DoubleToInt64Bits(place.SizeY));
                    Mix((ulong)BitConverter.DoubleToInt64Bits(place.Depth));
                    Mix((ulong)BitConverter.DoubleToInt64Bits(place.ZOffset));
                    Mix((ulong)BitConverter.DoubleToInt64Bits(place.Cos));
                    Mix(place.Color.r | (ulong)place.Color.g << 8 | (ulong)place.Color.b << 16 | (ulong)place.Color.a << 24);
                }
            }
            return h;
        }

        NativeArray<int> m_Maps;

        /// <summary>Writes the mesh. Maps hold five submeshes a surface map, the text's first, then each layer's.</summary>
        internal void Write(Mesh mesh, int submeshes, int[] maps)
        {
            IndexWrites++;
            Mesh.MeshDataArray array = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData data = array[0];
            bool small = m_VertexCount < 65000;
            data.SetVertexBufferParams(m_VertexCount,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2));
            data.SetIndexBufferParams(m_IndexCount, small ? IndexFormat.UInt16 : IndexFormat.UInt32);
            if (m_VertexCount > 0) data.GetVertexData<LetteraSolidMeshVertex>().CopyFrom(m_Posed.GetSubArray(0, m_VertexCount));

            if (!m_IndexItems.IsCreated || m_IndexItems.Length < m_Placed.Count)
            {
                if (m_IndexItems.IsCreated) m_IndexItems.Dispose();
                m_IndexItems = new NativeArray<IndexItem>(Mathf.Max(m_Placed.Count, 64), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            for (int p = 0; p < m_Placed.Count; p++)
            {
                Placement place = m_Placed[p];
                int[] starts = place.Entry.SurfaceStarts;
                m_IndexItems[p] = new IndexItem
                {
                    Source = (int*)place.Entry.Indices.GetUnsafeReadOnlyPtr(),
                    VertexStart = place.VertexStart,
                    Map = place.Map * 5 < maps.Length ? place.Map : 0,
                    S0 = starts[0], S1 = starts[1], S2 = starts[2], S3 = starts[3], S4 = starts[4], S5 = starts[5],
                };
            }
            data.subMeshCount = submeshes;
            var ranges = new NativeArray<int>(submeshes + 1, Allocator.TempJob);
            if (!m_Maps.IsCreated || m_Maps.Length < maps.Length)
            {
                if (m_Maps.IsCreated) m_Maps.Dispose();
                m_Maps = new NativeArray<int>(Mathf.Max(maps.Length, 5), Allocator.Persistent);
            }
            for (int i = 0; i < maps.Length; i++) m_Maps[i] = maps[i];
            var job = new IndexJob { Items = m_IndexItems, Count = m_Placed.Count, Map = m_Maps, Submeshes = submeshes, Ranges = ranges, Small = small };
            if (small) job.Target16 = data.GetIndexData<ushort>();
            else job.Target32 = data.GetIndexData<int>();
            job.Run();
            for (int submesh = 0; submesh < submeshes; submesh++)
                data.SetSubMesh(submesh, new SubMeshDescriptor(ranges[submesh], ranges[submesh + 1] - ranges[submesh]), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            ranges.Dispose();
            Mesh.ApplyAndDisposeWritableMeshData(array, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = Bounds;
        }

        unsafe struct IndexItem
        {
            [NativeDisableUnsafePtrRestriction] public int* Source;
            public int VertexStart, Map, S0, S1, S2, S3, S4, S5;
        }

        [BurstCompile]
        unsafe struct IndexJob : IJob
        {
            [ReadOnly] public NativeArray<IndexItem> Items;
            public int Count, Submeshes;
            public bool Small;
            [ReadOnly] public NativeArray<int> Map;
            public NativeArray<int> Ranges;
            [NativeDisableContainerSafetyRestriction] public NativeArray<ushort> Target16;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Target32;

            public void Execute()
            {
                int cursor = 0;
                int* edges = stackalloc int[6];
                for (int submesh = 0; submesh < Submeshes; submesh++)
                {
                    Ranges[submesh] = cursor;
                    for (int p = 0; p < Count; p++)
                    {
                        IndexItem item = Items[p];
                        edges[0] = item.S0; edges[1] = item.S1; edges[2] = item.S2; edges[3] = item.S3; edges[4] = item.S4; edges[5] = item.S5;
                        int map = item.Map * 5;
                        for (int s = 0; s < 5; s++)
                        {
                            if (Map[map + s] != submesh) continue;
                            for (int i = edges[s]; i < edges[s + 1]; i++)
                            {
                                int index = item.Source[i] + item.VertexStart;
                                if (Small) Target16[cursor] = (ushort)index;
                                else Target32[cursor] = index;
                                cursor++;
                            }
                        }
                    }
                }
                Ranges[Submeshes] = cursor;
            }
        }

        internal void ApplyPose(TextEngine engine, Mesh mesh)
        {
            if (m_VertexCount == 0) return;
            var ranges = new NativeArray<PoseRange>(m_Placed.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            bool deep = m_Poser.Compute(engine, m_Placed, m_GlyphPlacements, m_ItemEnds, m_Owner != null ? m_Owner.SolidPose : null);
            for (int p = 0; p < m_Placed.Count; p++)
            {
                Placement place = m_Placed[p];
                LetteraGlyphPose pose = engine.PoseOfForSolids(place.GlyphIndex);
                var range = new PoseRange { Start = place.VertexStart, Count = place.VertexCount, Pose = pose, Alpha = Mathf.Clamp01(pose.Alpha * pose.Tint.a) };
                if (deep && place.Cluster >= 0 && m_Poser.Has(place.GlyphIndex))
                {
                    range.Deep = true;
                    range.Matrix = m_Poser.Matrix(place.GlyphIndex);
                    range.Normal = m_Poser.Normal(place.GlyphIndex);
                    range.Alpha *= m_Poser.Alpha(place.GlyphIndex);
                }
                ranges[p] = range;
            }
            var reach = new NativeArray<float3>(2, Allocator.TempJob);
            new PoseJob { Rest = m_Rest, Posed = m_Posed, Ranges = ranges, Reach = reach }.Run();
            // Depth can carry a glyph out of the box it rests in, and what is outside a renderer's bounds is culled.
            if (deep && !m_Chain.Any)
            {
                Bounds = new Bounds((Vector3)((reach[0] + reach[1]) * 0.5f), (Vector3)(reach[1] - reach[0]));
                mesh.bounds = Bounds;
            }
            reach.Dispose();
            ranges.Dispose();
            if (m_Chain.Any)
            {
                if (m_Chain.Animated) PrepareDeform(engine, m_Owner != null ? m_Owner.Deformers : null, Time.time);
                Bounds = m_Chain.Run(m_Posed, m_VertexCount, m_Scale);
                mesh.bounds = Bounds;
            }
            mesh.SetVertexBufferData(m_Posed, 0, 0, m_VertexCount, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        LetteraText3D m_Owner;

        internal void SetOwner(LetteraText3D owner) => m_Owner = owner;

        struct PoseRange
        {
            public int Start, Count;
            public LetteraGlyphPose Pose;
            public float Alpha;
            public bool Deep;
            public float4x4 Matrix;
            public float3x3 Normal;
        }

        [BurstCompile]
        struct PoseJob : IJob
        {
            [ReadOnly] public NativeArray<LetteraSolidMeshVertex> Rest;
            public NativeArray<LetteraSolidMeshVertex> Posed;
            [ReadOnly] public NativeArray<PoseRange> Ranges;
            public NativeArray<float3> Reach;

            public void Execute()
            {
                float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
                for (int r = 0; r < Ranges.Length; r++)
                {
                    PoseRange range = Ranges[r];
                    LetteraGlyphPose pose = range.Pose;
                    float a = pose.A, b = pose.B, c = pose.C, d = pose.D, tx = pose.Tx, ty = pose.Ty;
                    // The nearest rotation of what may be a scale and a shear, for the normals.
                    float2 x = math.normalizesafe(new float2(a, b), new float2(1, 0));
                    for (int i = range.Start; i < range.Start + range.Count; i++)
                    {
                        LetteraSolidMeshVertex v = Rest[i];
                        // Depth first, about where the glyph rests, then the flat motion over it.
                        if (range.Deep)
                        {
                            v.Position = math.transform(range.Matrix, v.Position);
                            v.Normal = math.normalizesafe(math.mul(range.Normal, v.Normal), v.Normal);
                        }
                        v.Position = new float3(a * v.Position.x + c * v.Position.y + tx, b * v.Position.x + d * v.Position.y + ty, v.Position.z);
                        v.Normal = new float3(x.x * v.Normal.x - x.y * v.Normal.y, x.y * v.Normal.x + x.x * v.Normal.y, v.Normal.z);
                        v.Color.a = (byte)(v.Color.a * range.Alpha);
                        Posed[i] = v;
                        low = math.min(low, v.Position);
                        high = math.max(high, v.Position);
                    }
                }
                Reach[0] = low;
                Reach[1] = high;
            }
        }
    }
}
