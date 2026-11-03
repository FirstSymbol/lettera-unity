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
        };

        // ------------------------------------------------------------------ life

        void OnEnable()
        {
            GlyphAtlasRegistration.EnsureRegistered();
            m_Filter = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();
            if (m_Builder == null) m_Builder = new LetteraSolidMeshBuilder();
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

        void LateUpdate() => RebuildIfDirty();

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
        }

        /// <summary>Draws again without a rebuild, unless something changed meanwhile.</summary>
        public void Show()
        {
            if (!m_Hidden) return;
            m_Hidden = false;
            if (m_Renderer != null) m_Renderer.enabled = true;
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
            LetteraSolidFlatChild.Update(this);
        }

        readonly Material[] m_SurfaceSlots = new Material[5];
        readonly int[] m_SurfaceToSubmesh = new int[5];
        Material[] m_Given;

        void ResolveMaterials()
        {
            m_Materials.Clear();
            for (int s = 0; s < 5; s++)
            {
                Material material = GetSurfaceMaterial((LetteraSolidSurface)s);
                m_SurfaceSlots[s] = material;
                int index = m_Materials.IndexOf(material);
                if (index < 0)
                {
                    index = m_Materials.Count;
                    m_Materials.Add(material);
                }
                m_SurfaceToSubmesh[s] = index;
            }
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
            bool share = m_ShareMesh && !Engine.PosesObjects && !m_Builder.HasMotion;
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
            public int[] OverrideSurface;
        }

        readonly List<Placement> m_Placed = new List<Placement>();
        NativeArray<LetteraSolidMeshVertex> m_Rest, m_Posed;
        NativeArray<CopyItem> m_Items;
        NativeArray<IndexItem> m_IndexItems;
        int m_VertexCount, m_IndexCount;
        bool m_Over;
        internal bool HasMotion { get; private set; }
        internal int VertexCount => m_VertexCount;
        internal int TriangleCount => m_IndexCount / 3;
        internal bool OverLimit => m_Over;
        internal Bounds Bounds { get; private set; }
        internal int FailedGlyphs { get; private set; }
        internal IReadOnlyList<Placement> Placed => m_Placed;
        internal static int IndexWrites { get; private set; }

        public void Dispose()
        {
            if (m_Rest.IsCreated) m_Rest.Dispose();
            if (m_Posed.IsCreated) m_Posed.Dispose();
            if (m_Items.IsCreated) m_Items.Dispose();
            if (m_IndexItems.IsCreated) m_IndexItems.Dispose();
        }

        internal void Begin(TextEngine engine, in LetteraSolidRecipe recipe, float depth, LetteraSolidPivot pivot, bool flatColor, LetteraText3D owner, List<LetteraSolidEntry> held)
        {
            m_Placed.Clear();
            FailedGlyphs = 0;
            HasMotion = engine.HasMotionForSolids;
            ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
            int vertices = 0, limit = LetteraSettings.Instance.Solid3D.MaxVertices;
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
                float scaleZ = g.Size;
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
                    SizeZ = scaleZ,
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
            ValueList<TextEngine.SolidBox> boxes = engine.SolidBoxes;
            for (int b = 0; b < boxes.Count; b++)
            {
                TextEngine.SolidBox box = boxes[b];
                LetteraSolidEntry unit = LetteraSolidCache.Acquire(null, LetteraSolidCache.UnitBoxGlyph, recipe);
                held.Add(unit);
                if (vertices + unit.Vertices.Length > limit)
                {
                    m_Over = true;
                    continue;
                }
                float zOffset = pivot == LetteraSolidPivot.Front ? 0 : pivot == LetteraSolidPivot.Back ? -depth * box.Em : -depth * box.Em * 0.5f;
                m_Placed.Add(new Placement
                {
                    Entry = unit, Cluster = -1, GlyphIndex = box.Glyph,
                    X = box.Rect.x, Y = box.Rect.y, SizeX = box.Rect.width, SizeY = box.Rect.height, SizeZ = box.Em,
                    Cos = 1, Sin = 0, Depth = depth, BevelDepth = 0, ZOffset = zOffset, Color = box.Color,
                    VertexStart = vertices, VertexCount = unit.Vertices.Length,
                });
                vertices += unit.Vertices.Length;
            }
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
                m_Items = new NativeArray<CopyItem>(Mathf.Max(m_Placed.Count, 64), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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
            new CopyJob { Items = m_Items, Count = m_Placed.Count, Target = m_Rest, Ends = ends }.Run();
            float3 min = ends[0], max = ends[1];
            ends.Dispose();
            m_IndexCount = indices;
            if (m_VertexCount > 0) m_Posed.GetSubArray(0, m_VertexCount).CopyFrom(m_Rest.GetSubArray(0, m_VertexCount));
            Bounds = m_VertexCount == 0 ? new Bounds(Vector3.zero, Vector3.zero) : new Bounds((min + max) * 0.5f, max - min);
        }

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

            public void Execute()
            {
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int p = 0; p < Count; p++)
                {
                    CopyItem item = Items[p];
                    // Bevels deeper than the text fold flat rather than through each other.
                    float squeeze = item.BevelDepth > 0 && item.Depth < item.BevelDepth ? math.max(item.Depth, 0) / item.BevelDepth : 1;
                    float extra = math.max(item.Extra, 0);
                    for (int i = 0; i < item.Count; i++)
                    {
                        LetteraSolidVertex v = item.Source[i];
                        float lx = v.Position.x * item.SizeX, ly = v.Position.y * item.SizeY;
                        float3 normal = v.Normal;
                        float3 position = new float3(item.X + lx * item.Cos - ly * item.Sin, item.Y + lx * item.Sin + ly * item.Cos, (v.Position.z * squeeze + v.Weight * extra) * item.SizeZ + item.ZOffset);
                        min = math.min(min, position);
                        max = math.max(max, position);
                        Target[item.Start + i] = new LetteraSolidMeshVertex
                        {
                            Position = position,
                            Normal = new float3(normal.x * item.Cos - normal.y * item.Sin, normal.x * item.Sin + normal.y * item.Cos, normal.z),
                            Color = item.Color,
                            Uv0 = v.Uv,
                            Uv1 = new float2(item.X + lx, item.Y + ly),
                        };
                    }
                }
                Ends[0] = min;
                Ends[1] = max;
            }
        }

        /// <summary>A hash of everything that shows, so identical texts find each other.</summary>
        internal ulong ContentHash(int submeshes, int[] surfaceToSubmesh)
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                void Mix(ulong value) => h = (h ^ value) * 1099511628211UL;
                Mix((ulong)m_VertexCount);
                Mix((ulong)submeshes);
                for (int s = 0; s < 5; s++) Mix((ulong)surfaceToSubmesh[s]);
                for (int p = 0; p < m_Placed.Count; p++)
                {
                    Placement place = m_Placed[p];
                    Mix((ulong)(uint)place.Entry.Key.GetHashCode());
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

        internal void Write(Mesh mesh, int submeshes, int[] surfaceToSubmesh)
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
            if (m_VertexCount > 0) data.GetVertexData<LetteraSolidMeshVertex>().CopyFrom(m_Rest.GetSubArray(0, m_VertexCount));

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
                    S0 = starts[0], S1 = starts[1], S2 = starts[2], S3 = starts[3], S4 = starts[4], S5 = starts[5],
                };
            }
            data.subMeshCount = submeshes;
            var ranges = new NativeArray<int>(submeshes + 1, Allocator.TempJob);
            var map = new NativeArray<int>(5, Allocator.TempJob);
            for (int i = 0; i < 5; i++) map[i] = surfaceToSubmesh[i];
            var job = new IndexJob { Items = m_IndexItems, Count = m_Placed.Count, Map = map, Submeshes = submeshes, Ranges = ranges, Small = small };
            if (small) job.Target16 = data.GetIndexData<ushort>();
            else job.Target32 = data.GetIndexData<int>();
            job.Run();
            for (int submesh = 0; submesh < submeshes; submesh++)
                data.SetSubMesh(submesh, new SubMeshDescriptor(ranges[submesh], ranges[submesh + 1] - ranges[submesh]), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            ranges.Dispose();
            map.Dispose();
            Mesh.ApplyAndDisposeWritableMeshData(array, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = Bounds;
        }

        unsafe struct IndexItem
        {
            [NativeDisableUnsafePtrRestriction] public int* Source;
            public int VertexStart, S0, S1, S2, S3, S4, S5;
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
                        for (int s = 0; s < 5; s++)
                        {
                            if (Map[s] != submesh) continue;
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
            for (int p = 0; p < m_Placed.Count; p++)
            {
                Placement place = m_Placed[p];
                LetteraGlyphPose pose = engine.PoseOfForSolids(place.GlyphIndex);
                ranges[p] = new PoseRange { Start = place.VertexStart, Count = place.VertexCount, Pose = pose, Alpha = Mathf.Clamp01(pose.Alpha * pose.Tint.a) };
            }
            new PoseJob { Rest = m_Rest, Posed = m_Posed, Ranges = ranges }.Run();
            ranges.Dispose();
            mesh.SetVertexBufferData(m_Posed, 0, 0, m_VertexCount, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        struct PoseRange
        {
            public int Start, Count;
            public LetteraGlyphPose Pose;
            public float Alpha;
        }

        [BurstCompile]
        struct PoseJob : IJob
        {
            [ReadOnly] public NativeArray<LetteraSolidMeshVertex> Rest;
            public NativeArray<LetteraSolidMeshVertex> Posed;
            [ReadOnly] public NativeArray<PoseRange> Ranges;

            public void Execute()
            {
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
                        v.Position = new float3(a * v.Position.x + c * v.Position.y + tx, b * v.Position.x + d * v.Position.y + ty, v.Position.z);
                        v.Normal = new float3(x.x * v.Normal.x - x.y * v.Normal.y, x.y * v.Normal.x + x.x * v.Normal.y, v.Normal.z);
                        v.Color.a = (byte)(v.Color.a * range.Alpha);
                        Posed[i] = v;
                    }
                }
            }
        }
    }
}
