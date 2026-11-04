using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Where on a glyph copies of a prefab go.</summary>
    public enum LetteraGlyphAttachPlace
    {
        /// <summary>One on the glyph's highest point.</summary>
        Top,
        /// <summary>One on its lowest point.</summary>
        Bottom,
        /// <summary>One on its front face, as near its middle as the face reaches.</summary>
        Front,
        /// <summary>Along its outline, evenly spaced, standing out from its sides.</summary>
        Strokes,
        /// <summary>Scattered over the surfaces that face up, by a seed.</summary>
        Scatter,
    }

    /// <summary>One kind of copy a solid text puts on its glyphs.</summary>
    [Serializable]
    public sealed class LetteraGlyphAttachment
    {
        public bool Enabled = true;
        public GameObject Prefab;
        public LetteraGlyphAttachPlace Place = LetteraGlyphAttachPlace.Top;
        [Tooltip("Which characters get copies. Empty: every glyph.")] public string Characters = "";
        [Min(0), Tooltip("How big a copy is, in em: the prefab at scale one is one em.")] public float Size = 0.25f;
        [Tooltip("How far out of the surface a copy stands, in em. Negative sinks it in.")] public float Lift;
        [Range(0, 1), Tooltip("Each copy's size varies by up to this share, either way.")] public float ScaleVariation;
        [Tooltip("Degrees each copy is turned about its up.")] public float Spin;
        [Range(0, 180), Tooltip("Each copy's turn varies by up to this, either way, in degrees.")] public float SpinVariation;
        [Tooltip("Another seed gives other sizes, turns and scattered places.")] public int Seed;
        [Min(0.01f), Tooltip("Strokes: room between copies along the outline, in em.")] public float Spacing = 0.15f;
        [Min(0), Tooltip("Scatter: copies per square em of surface facing up.")] public float Density = 40;
        [Range(0, 89), Tooltip("Scatter: how far from straight up a surface may face, in degrees.")] public float MaxSlope = 30;
        [Min(1), Tooltip("At most this many on one glyph.")] public int MaxPerGlyph = 24;
        [Tooltip("On: a copy's up is the surface's outward normal. Off: the way its place faces (up for Top and Scatter, down for Bottom, out for Front and Strokes) at rest, turned with the glyph.")] public bool AlignToSurface = true;
        [Tooltip("A copy grows and shrinks with its glyph's motion and deformers.")] public bool ScaleWithGlyph = true;
    }

    /// <summary>
    /// Copies of prefabs on a solid text's glyphs: on top, bottom or front, along the strokes, or scattered over what faces up.
    /// Each sits on a triangle of its glyph, so motion and deformers carry it, and a glyph faded or revealed to nothing hides its copies.
    /// </summary>
    [AddComponentMenu("Lettera/Text 3D Attachments")]
    [RequireComponent(typeof(LetteraText3D))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class LetteraGlyphAttachments : MonoBehaviour
    {
        [SerializeField] List<LetteraGlyphAttachment> m_Attachments = new List<LetteraGlyphAttachment>();

        const string RootName = "Lettera Attachments";

        struct Copy
        {
            public GameObject Instance;
            public GameObject Prefab;
            public Vector3 PrefabScale;
            public LetteraSolidAnchor Anchor;
            public int Rule;
            public float Size, Lift;
            public quaternion Spin;
            public bool ScaleWithGlyph;
        }

        struct Wanted
        {
            public LetteraSolidAnchor Anchor;
            public int Rule;
            public float Size, Lift;
            public quaternion Spin;
        }

        LetteraText3D m_Text;
        Transform m_Root;
        readonly List<Copy> m_Copies = new List<Copy>();
        readonly List<Wanted> m_Wanted = new List<Wanted>();
        readonly List<Copy> m_Spare = new List<Copy>();
        readonly List<float3> m_Kept = new List<float3>();

        /// <summary>The kinds of copies, in order. After changing one in place, call Rebuild.</summary>
        public List<LetteraGlyphAttachment> Attachments => m_Attachments;

        /// <summary>How many copies stand now.</summary>
        public int CopyCount => m_Copies.Count;

        /// <summary>The copies standing now, with the character each sits on.</summary>
        public void GetCopies(List<GameObject> copies, List<int> characters = null)
        {
            if (copies == null) throw new ArgumentNullException(nameof(copies));
            copies.Clear();
            characters?.Clear();
            foreach (Copy copy in m_Copies)
            {
                copies.Add(copy.Instance);
                characters?.Add(copy.Anchor.Character);
            }
        }

        /// <summary>The anchor a copy follows, as for any place found by a ray.</summary>
        public LetteraSolidAnchor AnchorOf(int copy) => m_Copies[copy].Anchor;

        void OnEnable()
        {
            m_Text = GetComponent<LetteraText3D>();
            m_Text.SolidsWritten += OnSolids;
            m_Text.ForceRebuild();
            Rebuild();
        }

        void OnDisable()
        {
            if (m_Text != null) m_Text.SolidsWritten -= OnSolids;
            Clear();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (isActiveAndEnabled && m_Text != null) UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Rebuild(); };
        }
#endif

        void OnSolids(bool built)
        {
            if (built) Rebuild();
            else Follow();
        }

        /// <summary>Places the copies again from the text as it is built now.</summary>
        public void Rebuild()
        {
            if (m_Text == null) return;
            LetteraSolidMeshBuilder builder = m_Text.Builder;
            if (builder == null) return;
            m_Wanted.Clear();
            for (int r = 0; r < m_Attachments.Count; r++)
            {
                LetteraGlyphAttachment rule = m_Attachments[r];
                if (rule == null || !rule.Enabled || rule.Prefab == null) continue;
                Gather(builder, rule, r);
            }
            Sync();
            Follow();
        }

        // ------------------------------------------------------------------ places

        void Gather(LetteraSolidMeshBuilder builder, LetteraGlyphAttachment rule, int ruleIndex)
        {
            IReadOnlyList<LetteraSolidMeshBuilder.Placement> placed = builder.Placed;
            ReadOnlySpan<int> shown = m_Text.Engine.ShownCodepoints;
            NativeArray<LetteraSolidMeshVertex> rest = builder.RestVertices;
            for (int p = 0; p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.Map != 0 || place.Cluster < 0 || place.VertexCount < 3) continue;
                if (!string.IsNullOrEmpty(rule.Characters) && (place.Cluster >= shown.Length || !Contains(rule.Characters, shown[place.Cluster]))) continue;
                float em = place.SizeZ;
                var random = new Unity.Mathematics.Random(Hash((uint)rule.Seed, (uint)place.Cluster, (uint)ruleIndex));
                int before = m_Wanted.Count;
                switch (rule.Place)
                {
                    case LetteraGlyphAttachPlace.Top:
                        Extreme(builder, rest, p, place, new float3(0, -1, 0), rule, ruleIndex, ref random);
                        break;
                    case LetteraGlyphAttachPlace.Bottom:
                        Extreme(builder, rest, p, place, new float3(0, 1, 0), rule, ruleIndex, ref random);
                        break;
                    case LetteraGlyphAttachPlace.Front:
                        Front(builder, rest, p, place, rule, ruleIndex, ref random);
                        break;
                    case LetteraGlyphAttachPlace.Strokes:
                        Strokes(builder, rest, p, place, rule, ruleIndex, em, ref random);
                        break;
                    case LetteraGlyphAttachPlace.Scatter:
                        Scatter(builder, rest, p, place, rule, ruleIndex, em, ref random);
                        break;
                }
                for (int w = before; w < m_Wanted.Count; w++)
                {
                    Wanted wanted = m_Wanted[w];
                    wanted.Size *= em;
                    wanted.Lift *= em;
                    m_Wanted[w] = wanted;
                }
            }
        }

        static bool Contains(string characters, int codepoint)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                int cp = char.IsHighSurrogate(characters[i]) && i + 1 < characters.Length ? char.ConvertToUtf32(characters[i], characters[++i]) : characters[i];
                if (cp == codepoint) return true;
            }
            return false;
        }

        static uint Hash(uint a, uint b, uint c)
        {
            uint h = math.hash(new uint3(a, b, c));
            return h == 0 ? 1u : h;
        }

        void Add(LetteraSolidMeshBuilder builder, int place, int triangle, float3 weights, float3 facing, LetteraGlyphAttachment rule, int ruleIndex, ref Unity.Mathematics.Random random)
        {
            LetteraSolidAnchor anchor = LetteraSolidRay.MakeAnchor(builder, place, triangle, weights, rule.AlignToSurface ? (float3?)null : facing);
            float size = rule.Size * (1 + rule.ScaleVariation * random.NextFloat(-1, 1));
            float spin = rule.Spin + rule.SpinVariation * random.NextFloat(-1, 1);
            m_Wanted.Add(new Wanted { Anchor = anchor, Rule = ruleIndex, Size = math.max(size, 0), Lift = rule.Lift, Spin = quaternion.AxisAngle(new float3(0, 1, 0), math.radians(spin)) });
        }

        static void Bounds(NativeArray<LetteraSolidMeshVertex> rest, in LetteraSolidMeshBuilder.Placement place, out float3 low, out float3 high)
        {
            low = new float3(float.MaxValue);
            high = new float3(float.MinValue);
            for (int i = place.VertexStart; i < place.VertexStart + place.VertexCount; i++)
            {
                low = math.min(low, rest[i].Position);
                high = math.max(high, rest[i].Position);
            }
        }

        // The highest or lowest point, by rays down or up at several places across the glyph's width.
        void Extreme(LetteraSolidMeshBuilder builder, NativeArray<LetteraSolidMeshVertex> rest, int p, in LetteraSolidMeshBuilder.Placement place, float3 down, LetteraGlyphAttachment rule, int ruleIndex, ref Unity.Mathematics.Random random)
        {
            Bounds(rest, place, out float3 low, out float3 high);
            float3 size = high - low;
            float z = (low.z + high.z) * 0.5f;
            float startY = down.y < 0 ? high.y + size.y + 1 : low.y - size.y - 1;
            float best = float.MinValue;
            int bestTriangle = -1;
            float3 bestWeights = default;
            const int Samples = 9;
            for (int s = 0; s < Samples; s++)
            {
                // From the middle outward, so that a tie goes to the middle.
                int k = (s + 1) / 2 * (s % 2 == 0 ? 1 : -1);
                float x = (low.x + high.x) * 0.5f + k * size.x * 0.48f / (Samples / 2);
                if (!LetteraSolidRay.CastPlacement(rest, place, new float3(x, startY, z), down, out int triangle, out float3 weights, out float t)) continue;
                float height = -down.y * (startY + down.y * t);
                if (height > best + 1e-4f * size.y)
                {
                    best = height;
                    bestTriangle = triangle;
                    bestWeights = weights;
                }
            }
            if (bestTriangle >= 0) Add(builder, p, bestTriangle, bestWeights, -down, rule, ruleIndex, ref random);
        }

        // The front face's point nearest the glyph's middle, by rays at a grid of places.
        void Front(LetteraSolidMeshBuilder builder, NativeArray<LetteraSolidMeshVertex> rest, int p, in LetteraSolidMeshBuilder.Placement place, LetteraGlyphAttachment rule, int ruleIndex, ref Unity.Mathematics.Random random)
        {
            Bounds(rest, place, out float3 low, out float3 high);
            float3 middle = (low + high) * 0.5f;
            int faceFrom = place.Entry.SurfaceStarts[(int)LetteraSolidSurface.Face], faceTo = place.Entry.SurfaceStarts[(int)LetteraSolidSurface.Face + 1];
            int bestTriangle = -1;
            float3 bestWeights = default;
            const int Grid = 7;
            // The flat face first; a bevel only where a glyph is too thin to show any.
            for (int pass = 0; pass < 2 && bestTriangle < 0; pass++)
            {
                float near = float.MaxValue;
                for (int gx = 0; gx < Grid; gx++)
                    for (int gy = 0; gy < Grid; gy++)
                    {
                        float2 at = math.lerp(low.xy, high.xy, (new float2(gx, gy) + 0.5f) / Grid);
                        float d = math.distancesq(at, middle.xy);
                        if (d >= near) continue;
                        if (!LetteraSolidRay.CastPlacement(rest, place, new float3(at, low.z - 1), new float3(0, 0, 1), out int triangle, out float3 weights, out float _)) continue;
                        if (pass == 0 && (triangle < faceFrom || triangle >= faceTo)) continue;
                        near = d;
                        bestTriangle = triangle;
                        bestWeights = weights;
                    }
            }
            if (bestTriangle >= 0) Add(builder, p, bestTriangle, bestWeights, new float3(0, 0, -1), rule, ruleIndex, ref random);
        }

        // Points along the sides at mid depth, a spacing apart.
        void Strokes(LetteraSolidMeshBuilder builder, NativeArray<LetteraSolidMeshVertex> rest, int p, in LetteraSolidMeshBuilder.Placement place, LetteraGlyphAttachment rule, int ruleIndex, float em, ref Unity.Mathematics.Random random)
        {
            LetteraSolidEntry entry = place.Entry;
            NativeArray<int> indices = entry.Indices;
            int from = entry.SurfaceStarts[(int)LetteraSolidSurface.Side], to = entry.SurfaceStarts[(int)LetteraSolidSurface.Side + 1];
            float spacing = rule.Spacing * em, step = spacing * 0.25f;
            m_Kept.Clear();
            for (int t = from; t + 2 < to && m_Kept.Count < rule.MaxPerGlyph; t += 3)
            {
                int ia = place.VertexStart + indices[t], ib = place.VertexStart + indices[t + 1], ic = place.VertexStart + indices[t + 2];
                float3 a = rest[ia].Position, b = rest[ib].Position, c = rest[ic].Position;
                if (math.lengthsq(math.cross(b - a, c - a)) < 1e-12f) continue;
                float mid = (math.min(a.z, math.min(b.z, c.z)) + math.max(a.z, math.max(b.z, c.z))) * 0.5f;
                // Where the plane at mid depth crosses the triangle: two of its edges, as weights.
                float3 w0 = default, w1 = default;
                int crossings = 0;
                for (int e = 0; e < 3 && crossings < 2; e++)
                {
                    int i = e, j = (e + 1) % 3;
                    float zi = i == 0 ? a.z : i == 1 ? b.z : c.z, zj = j == 0 ? a.z : j == 1 ? b.z : c.z;
                    if ((zi - mid) * (zj - mid) > 0 || zi == zj) continue;
                    float s = (mid - zi) / (zj - zi);
                    float3 w = default;
                    w[i] = 1 - s;
                    w[j] = s;
                    if (crossings == 0) w0 = w;
                    else w1 = w;
                    crossings++;
                }
                if (crossings < 2) continue;
                float3 p0 = w0.x * a + w0.y * b + w0.z * c, p1 = w1.x * a + w1.y * b + w1.z * c;
                int samples = math.max(1, (int)math.ceil(math.distance(p0, p1) / step));
                for (int k = 0; k <= samples && m_Kept.Count < rule.MaxPerGlyph; k++)
                {
                    float f = (float)k / samples;
                    float3 w = math.lerp(w0, w1, f);
                    float3 point = w.x * a + w.y * b + w.z * c;
                    bool clear = true;
                    for (int q = 0; q < m_Kept.Count && clear; q++) clear = math.distancesq(point, m_Kept[q]) >= spacing * spacing;
                    if (!clear) continue;
                    m_Kept.Add(point);
                    float3 facing = math.normalizesafe(w.x * rest[ia].Normal + w.y * rest[ib].Normal + w.z * rest[ic].Normal);
                    Add(builder, p, t, w, facing, rule, ruleIndex, ref random);
                }
            }
        }

        readonly List<float> m_Areas = new List<float>();
        readonly List<int> m_Triangles = new List<int>();

        // Points over the triangles that face up, as many as their area and the density ask for.
        void Scatter(LetteraSolidMeshBuilder builder, NativeArray<LetteraSolidMeshVertex> rest, int p, in LetteraSolidMeshBuilder.Placement place, LetteraGlyphAttachment rule, int ruleIndex, float em, ref Unity.Mathematics.Random random)
        {
            NativeArray<int> indices = place.Entry.Indices;
            float cos = math.cos(math.radians(rule.MaxSlope));
            m_Areas.Clear();
            m_Triangles.Clear();
            float total = 0;
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                LetteraSolidMeshVertex a = rest[place.VertexStart + indices[t]], b = rest[place.VertexStart + indices[t + 1]], c = rest[place.VertexStart + indices[t + 2]];
                float3 normal = math.normalizesafe(a.Normal + b.Normal + c.Normal);
                if (normal.y < cos) continue;
                float area = math.length(math.cross(b.Position - a.Position, c.Position - a.Position)) * 0.5f;
                if (area <= 0) continue;
                total += area;
                m_Areas.Add(total);
                m_Triangles.Add(t);
            }
            if (total <= 0) return;
            float wanted = total / (em * em) * rule.Density;
            int count = math.min(rule.MaxPerGlyph, (int)math.floor(wanted + random.NextFloat()));
            for (int n = 0; n < count; n++)
            {
                float pick = random.NextFloat() * total;
                int lo = 0, hi = m_Areas.Count - 1;
                while (lo < hi)
                {
                    int m = (lo + hi) / 2;
                    if (m_Areas[m] < pick) lo = m + 1;
                    else hi = m;
                }
                float r1 = math.sqrt(random.NextFloat()), r2 = random.NextFloat();
                Add(builder, p, m_Triangles[lo], new float3(1 - r1, r1 * (1 - r2), r1 * r2), new float3(0, 1, 0), rule, ruleIndex, ref random);
            }
        }

        // ------------------------------------------------------------------ copies

        // Keeps the copies there are where the same prefab is wanted again, and makes or drops the rest.
        void Sync()
        {
            m_Spare.Clear();
            m_Spare.AddRange(m_Copies);
            m_Copies.Clear();
            foreach (Wanted wanted in m_Wanted)
            {
                LetteraGlyphAttachment rule = m_Attachments[wanted.Rule];
                int reuse = -1;
                for (int s = 0; s < m_Spare.Count; s++)
                    if (m_Spare[s].Prefab == rule.Prefab && m_Spare[s].Instance != null)
                    {
                        reuse = s;
                        break;
                    }
                Copy copy;
                if (reuse >= 0)
                {
                    copy = m_Spare[reuse];
                    m_Spare[reuse] = m_Spare[m_Spare.Count - 1];
                    m_Spare.RemoveAt(m_Spare.Count - 1);
                }
                else
                {
                    EnsureRoot();
                    GameObject instance = Instantiate(rule.Prefab, m_Root, false);
                    foreach (Transform part in instance.GetComponentsInChildren<Transform>(true)) part.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    copy = new Copy { Instance = instance, Prefab = rule.Prefab, PrefabScale = rule.Prefab.transform.localScale };
                }
                copy.Anchor = wanted.Anchor;
                copy.Rule = wanted.Rule;
                copy.Size = wanted.Size;
                copy.Lift = wanted.Lift;
                copy.Spin = wanted.Spin;
                copy.ScaleWithGlyph = rule.ScaleWithGlyph;
                m_Copies.Add(copy);
            }
            foreach (Copy spare in m_Spare) DestroyNow(spare.Instance);
            m_Spare.Clear();
        }

        // Each copy where its triangle is drawn now.
        void Follow()
        {
            if (m_Text == null || m_Copies.Count == 0) return;
            LetteraSolidMeshBuilder builder = m_Text.Builder;
            if (builder == null) return;
            for (int i = 0; i < m_Copies.Count; i++)
            {
                Copy copy = m_Copies[i];
                if (copy.Instance == null) continue;
                bool found = m_Text.TryGetPoseLocal(ref copy.Anchor, out Pose pose, out float scale);
                bool shown = found && m_Text.IsVisible && LetteraSolidRay.AlphaAt(builder, copy.Anchor) > 0 && (!copy.ScaleWithGlyph || scale > 1e-4f);
                if (copy.Instance.activeSelf != shown) copy.Instance.SetActive(shown);
                m_Copies[i] = copy;
                if (!shown) continue;
                float grow = copy.ScaleWithGlyph ? scale : 1;
                quaternion rotation = math.mul(pose.rotation, copy.Spin);
                float3 up = math.mul(rotation, new float3(0, 1, 0));
                Transform t = copy.Instance.transform;
                t.SetLocalPositionAndRotation((float3)pose.position + up * (copy.Lift * grow), rotation);
                t.localScale = copy.PrefabScale * (copy.Size * grow);
            }
        }

        void EnsureRoot()
        {
            if (m_Root != null) return;
            Transform found = transform.Find(RootName);
            if (found != null) DestroyNow(found.gameObject);
            var go = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
            m_Root = go.transform;
            m_Root.SetParent(transform, false);
        }

        void Clear()
        {
            m_Copies.Clear();
            if (m_Root != null) DestroyNow(m_Root.gameObject);
            m_Root = null;
        }

        static void DestroyNow(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
