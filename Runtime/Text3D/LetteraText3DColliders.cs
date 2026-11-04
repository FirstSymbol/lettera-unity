#if LETTERA_PHYSICS
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a solid text's colliders are made of.</summary>
    public enum LetteraSolidColliderShape
    {
        /// <summary>A box around each glyph, turned with it. Cheapest, and fine on a moving body.</summary>
        Boxes,
        /// <summary>A convex hull of each glyph. Closer than a box; holes and hollows are filled.</summary>
        Hulls,
        /// <summary>The drawn mesh itself, exact. Only for static colliders and kinematic bodies.</summary>
        Mesh,
    }

    /// <summary>
    /// Colliders that keep matching a solid text as drawn: built again with its mesh, and moved with its motion and deformers.
    /// They sit on hidden children, so a Rigidbody on the text takes them all as one compound body.
    /// </summary>
    [AddComponentMenu("Lettera/Text 3D Colliders")]
    [RequireComponent(typeof(LetteraText3D))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class LetteraText3DColliders : MonoBehaviour
    {
        [SerializeField] LetteraSolidColliderShape m_Shape = LetteraSolidColliderShape.Boxes;
        [SerializeField, Tooltip("Layers around the glyphs collide too.")] bool m_IncludeLayers = true;
        [SerializeField, Tooltip("Follow motion and moving deformers every frame. A Mesh is cooked again each frame it moves, which costs.")] bool m_FollowMotion = true;
        [SerializeField] bool m_IsTrigger;
        [SerializeField] PhysicsMaterial m_Material;

        const string RootName = "Lettera Colliders";

        LetteraText3D m_Text;
        Transform m_Root;
        readonly List<Part> m_Parts = new List<Part>();
        MeshCollider m_MeshCollider;
        int m_Active;

        struct Part
        {
            public GameObject Object;
            public BoxCollider Box;
            public MeshCollider Hull;
            public Mesh HullMesh;
            public int Placement, Cluster;
            public int3 Corners;
            public float3 RestOrigin;
            public quaternion RestFrame;
            public float RestLength;
        }

        public LetteraSolidColliderShape Shape { get => m_Shape; set { if (m_Shape == value) return; m_Shape = value; Rebuild(); } }
        public bool IncludeLayers { get => m_IncludeLayers; set { if (m_IncludeLayers == value) return; m_IncludeLayers = value; Rebuild(); } }
        public bool FollowMotion { get => m_FollowMotion; set => m_FollowMotion = value; }
        public bool IsTrigger { get => m_IsTrigger; set { if (m_IsTrigger == value) return; m_IsTrigger = value; Rebuild(); } }
        public PhysicsMaterial Material { get => m_Material; set { if (m_Material == value) return; m_Material = value; Rebuild(); } }

        /// <summary>How many colliders stand now: one per glyph, or one for a Mesh.</summary>
        public int ColliderCount => m_Shape == LetteraSolidColliderShape.Mesh ? (m_MeshCollider != null && m_MeshCollider.enabled ? 1 : 0) : m_Active;

        /// <summary>The colliders standing now, in text order.</summary>
        public void GetColliders(List<Collider> output)
        {
            if (output == null) throw new System.ArgumentNullException(nameof(output));
            output.Clear();
            if (m_Shape == LetteraSolidColliderShape.Mesh)
            {
                if (m_MeshCollider != null && m_MeshCollider.enabled) output.Add(m_MeshCollider);
                return;
            }
            for (int i = 0; i < m_Active; i++) output.Add(m_Shape == LetteraSolidColliderShape.Boxes ? m_Parts[i].Box : (Collider)m_Parts[i].Hull);
        }

        /// <summary>The character of the shown text a collider stands for, or -1: for a Mesh, or a layer around more than one glyph.</summary>
        public int CharacterOf(Collider collider)
        {
            if (collider == null) return -1;
            for (int i = 0; i < m_Active; i++)
                if (m_Parts[i].Box == collider || m_Parts[i].Hull == collider)
                    return m_Parts[i].Cluster;
            return -1;
        }

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
            else if (m_FollowMotion) Follow();
        }

        /// <summary>Makes the colliders again from the text as it is drawn now.</summary>
        public void Rebuild()
        {
            if (m_Text == null) return;
            LetteraSolidMeshBuilder builder = m_Text.Builder;
            if (builder == null) return;
            EnsureRoot();
            if (m_Shape == LetteraSolidColliderShape.Mesh)
            {
                SetActiveParts(0);
                if (m_MeshCollider == null) m_MeshCollider = m_Root.gameObject.AddComponent<MeshCollider>();
                m_MeshCollider.enabled = true;
                m_MeshCollider.isTrigger = false;
                m_MeshCollider.sharedMaterial = m_Material;
                CookMesh();
                return;
            }
            if (m_MeshCollider != null)
            {
                m_MeshCollider.sharedMesh = null;
                m_MeshCollider.enabled = false;
            }
            IReadOnlyList<LetteraSolidMeshBuilder.Placement> placed = builder.Placed;
            NativeArray<LetteraSolidMeshVertex> rest = builder.RestVertices, posed = builder.PosedVertices;
            int used = 0;
            for (int p = 0; p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.VertexCount < 3 || (place.Map > 0 && !m_IncludeLayers)) continue;
                if (used == m_Parts.Count) m_Parts.Add(MakePart(used));
                Part part = m_Parts[used];
                part.Placement = p;
                part.Cluster = place.Map > 0 && place.Cluster < 0 ? -1 : place.Cluster;
                part.Corners = Corners(rest, place.VertexStart, place.VertexCount);
                Frame(rest, part.Corners, out part.RestOrigin, out part.RestFrame, out part.RestLength);
                // The shape is taken as drawn now, in the text's own axes turned as the three corners are, so that a bent glyph is
                // held as bent; motion after this only moves it, as the corners move.
                Placed(posed, part, out float3 origin, out quaternion turn, out float scale);
                NativeArray<LetteraSolidMeshVertex> source = posed;
                if (scale < 1e-3f)
                {
                    source = rest;
                    origin = part.RestOrigin;
                    turn = quaternion.identity;
                    scale = 1;
                }
                quaternion back = math.inverse(turn);
                float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
                Vector3[] hull = m_Shape == LetteraSolidColliderShape.Hulls ? Scratch(place.VertexCount) : null;
                for (int i = 0; i < place.VertexCount; i++)
                {
                    float3 local = math.mul(back, source[place.VertexStart + i].Position - origin) / scale;
                    low = math.min(low, local);
                    high = math.max(high, local);
                    if (hull != null) hull[i] = local;
                }
                bool box = m_Shape == LetteraSolidColliderShape.Boxes;
                if (part.Box == null && box) part.Box = part.Object.AddComponent<BoxCollider>();
                if (part.Hull == null && !box) part.Hull = part.Object.AddComponent<MeshCollider>();
                if (part.Box != null)
                {
                    part.Box.enabled = box;
                    part.Box.center = (low + high) * 0.5f;
                    part.Box.size = math.max(high - low, 1e-4f);
                    part.Box.isTrigger = m_IsTrigger;
                    part.Box.sharedMaterial = m_Material;
                }
                if (part.Hull != null)
                {
                    part.Hull.enabled = !box;
                    if (!box)
                    {
                        if (part.HullMesh == null) part.HullMesh = new UnityEngine.Mesh { name = "Lettera Hull", hideFlags = HideFlags.HideAndDontSave };
                        part.HullMesh.Clear();
                        part.HullMesh.indexFormat = place.VertexCount > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                        part.HullMesh.SetVertices(hull, 0, place.VertexCount);
                        part.HullMesh.SetIndices(place.Entry.Indices, MeshTopology.Triangles, 0, false);
                        part.HullMesh.bounds = new Bounds((low + high) * 0.5f, high - low);
                        part.Hull.convex = true;
                        part.Hull.sharedMesh = null;
                        part.Hull.sharedMesh = part.HullMesh;
                        part.Hull.isTrigger = m_IsTrigger;
                        part.Hull.sharedMaterial = m_Material;
                    }
                }
                part.Object.layer = gameObject.layer;
                m_Parts[used] = part;
                used++;
            }
            SetActiveParts(used);
            Follow();
        }

        // Moves each glyph's collider to where its glyph is drawn now, or cooks the mesh again.
        void Follow()
        {
            LetteraSolidMeshBuilder builder = m_Text != null ? m_Text.Builder : null;
            if (builder == null || m_Root == null) return;
            if (m_Shape == LetteraSolidColliderShape.Mesh)
            {
                CookMesh();
                return;
            }
            NativeArray<LetteraSolidMeshVertex> posed = builder.PosedVertices;
            for (int i = 0; i < m_Active; i++)
            {
                Part part = m_Parts[i];
                Placed(posed, part, out float3 origin, out quaternion frame, out float scale);
                bool shown = scale > 1e-4f;
                if (part.Object.activeSelf != shown) part.Object.SetActive(shown);
                if (!shown) continue;
                part.Object.transform.SetLocalPositionAndRotation(origin, frame);
                part.Object.transform.localScale = new Vector3(scale, scale, scale);
            }
        }

        // Where the corners put a glyph now: its first corner, the turn since rest, and the scale since rest.
        static void Placed(NativeArray<LetteraSolidMeshVertex> posed, in Part part, out float3 origin, out quaternion turn, out float scale)
        {
            Frame(posed, part.Corners, out origin, out quaternion frame, out float length);
            scale = part.RestLength > 1e-12f ? length / part.RestLength : 1;
            turn = math.mul(frame, math.inverse(part.RestFrame));
        }

        void CookMesh()
        {
            if (m_MeshCollider == null) return;
            // Setting the same mesh again is what makes a collider read its moved vertices.
            m_MeshCollider.sharedMesh = null;
            m_MeshCollider.sharedMesh = m_Text.CurrentMesh;
        }

        Part MakePart(int index)
        {
            var go = new GameObject("Glyph " + index) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(m_Root, false);
            return new Part { Object = go };
        }

        void SetActiveParts(int count)
        {
            for (int i = count; i < m_Parts.Count; i++)
                if (m_Parts[i].Object != null && m_Parts[i].Object.activeSelf)
                    m_Parts[i].Object.SetActive(false);
            for (int i = 0; i < count; i++)
                if (!m_Parts[i].Object.activeSelf)
                    m_Parts[i].Object.SetActive(true);
            m_Active = count;
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
            foreach (Part part in m_Parts)
                if (part.HullMesh != null)
                    DestroyNow(part.HullMesh);
            m_Parts.Clear();
            m_Active = 0;
            m_MeshCollider = null;
            if (m_Root != null) DestroyNow(m_Root.gameObject);
            m_Root = null;
        }

        static void DestroyNow(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        static Vector3[] s_Scratch = new Vector3[256];

        static Vector3[] Scratch(int count)
        {
            if (s_Scratch.Length < count) s_Scratch = new Vector3[Mathf.NextPowerOfTwo(count)];
            return s_Scratch;
        }

        /// <summary>Three vertices far apart that a glyph's frame is read from: the same three at rest and posed.</summary>
        internal static int3 Corners(NativeArray<LetteraSolidMeshVertex> vertices, int start, int count)
        {
            int a = start;
            for (int i = start; i < start + count; i++)
            {
                float3 p = vertices[i].Position, q = vertices[a].Position;
                if (p.x < q.x || (p.x == q.x && p.y < q.y)) a = i;
            }
            int b = a;
            float far = -1;
            for (int i = start; i < start + count; i++)
            {
                float d = math.distancesq(vertices[i].Position, vertices[a].Position);
                if (d > far)
                {
                    far = d;
                    b = i;
                }
            }
            int c = a;
            float3 axis = math.normalizesafe(vertices[b].Position - vertices[a].Position);
            far = -1;
            for (int i = start; i < start + count; i++)
            {
                float3 off = vertices[i].Position - vertices[a].Position;
                float d = math.lengthsq(off - axis * math.dot(off, axis));
                if (d > far)
                {
                    far = d;
                    c = i;
                }
            }
            return new int3(a, b, c);
        }

        internal static void Frame(NativeArray<LetteraSolidMeshVertex> vertices, int3 corners, out float3 origin, out quaternion frame, out float length)
        {
            origin = vertices[corners.x].Position;
            float3 e = vertices[corners.y].Position - origin;
            length = math.length(e);
            float3 n = math.cross(e, vertices[corners.z].Position - origin);
            frame = length < 1e-12f || math.lengthsq(n) < 1e-24f ? quaternion.identity : quaternion.LookRotationSafe(math.normalize(n), e / length);
        }
    }
}
#endif
