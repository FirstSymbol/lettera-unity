using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Merges world texts that share a sorting context into combined meshes, split into shards of about
    /// 16 384 vertices. A merged shard has one depth-sort position; Standalone texts get a context of their own.
    /// </summary>
    internal static class LetteraWorldBatcher
    {
        const int ShardVertices = 16384;

        readonly struct BatchKey : IEquatable<BatchKey>
        {
            public readonly int SortingLayer, SortingOrder, Group, Layer, SceneHandle, StandaloneId;
            public readonly bool Shadows, Lit;

            public BatchKey(LetteraWorldText text)
            {
                SortingLayer = text.SortingLayerID;
                SortingOrder = text.SortingOrder;
                Group = text.BatchGroup;
                GameObject go = text.CachedObject;
                Layer = go.layer;
                SceneHandle = go.scene.GetHashCode();
                Shadows = text.CastShadows;
                Lit = text.Lit;
                StandaloneId = text.Standalone ? text.GetHashCode() : 0;
            }

            public bool Equals(BatchKey o) => SortingLayer == o.SortingLayer && SortingOrder == o.SortingOrder && Group == o.Group &&
                                              Layer == o.Layer && SceneHandle == o.SceneHandle && Shadows == o.Shadows && Lit == o.Lit && StandaloneId == o.StandaloneId;
            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(SortingLayer, SortingOrder, Group, Layer, SceneHandle, Shadows, Lit, StandaloneId);
        }

        sealed class Shard
        {
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Material[] Materials;
            public bool MaterialsDirty;
            public Vector3[] Vertices;
            public Color32[] Colors;
            public Vector4[] Uv3;
            public int VertexCount;
        }

        sealed class Batch
        {
            public BatchKey Key;
            public readonly List<Entry> Texts = new List<Entry>();
            public readonly List<Shard> Shards = new List<Shard>();
            public bool Dirty = true;
        }

        sealed class Entry
        {
            public LetteraWorldText Text;
            public Batch Batch;
            public Matrix4x4 Matrix;
            public Shard Shard;
            public int BaseVertex;
            public int EntrySlot, BatchSlot;
        }

        static readonly List<Entry> s_Posed = new List<Entry>();
        static readonly List<(Shard shard, int streams)> s_PosedShards = new List<(Shard, int)>();

        static readonly List<Entry> s_Entries = new List<Entry>();
        static readonly Dictionary<LetteraWorldText, Entry> s_ByText = new Dictionary<LetteraWorldText, Entry>();
        static readonly Dictionary<BatchKey, Batch> s_Batches = new Dictionary<BatchKey, Batch>();

        // Merged geometry of one shard. A project with no world text never needs them, and the update runs every frame from the first text on, so they come with the first batch.
        const int ScratchVertices = ShardVertices + 1024;
        static Vector3[] s_Vertices = Array.Empty<Vector3>();
        static Vector3[] s_Normals = Array.Empty<Vector3>();
        static Color32[] s_Colors = Array.Empty<Color32>();
        static Vector4[] s_Uv0 = Array.Empty<Vector4>();
        static Vector4[] s_Uv1 = Array.Empty<Vector4>();
        static Vector4[] s_Uv2 = Array.Empty<Vector4>();
        static Vector4[] s_Uv3 = Array.Empty<Vector4>();
        static int[] s_Indices = Array.Empty<int>();

        // The stretches of one shard that need different materials, in drawing order.
        struct MaterialRun
        {
            public int IndexStart, IndexCount;
            public bool Styled;
            public LetteraBlend Blend;
            public Texture2D Texture;
            public Material Custom;
        }

        static readonly List<MaterialRun> s_Runs = new List<MaterialRun>();
        static bool s_ShardStyled, s_ShardPosed;

        /// <summary>Every enabled world text.</summary>
        internal static IReadOnlyCollection<LetteraWorldText> Texts => s_ByText.Keys;

        public static void Register(LetteraWorldText text)
        {
            if (s_ByText.ContainsKey(text)) return;
            var entry = new Entry { Text = text, Matrix = text.transform.localToWorldMatrix };
            s_ByText[text] = entry;
            entry.EntrySlot = s_Entries.Count;
            s_Entries.Add(entry);
            Assign(entry);
        }

        // Places in these lists mean nothing, and a batch sorts its own before it merges: taking one out is a swap, whatever the number of texts.
        static void RemoveFromEntries(Entry entry)
        {
            int last = s_Entries.Count - 1;
            Entry moved = s_Entries[last];
            s_Entries[entry.EntrySlot] = moved;
            moved.EntrySlot = entry.EntrySlot;
            s_Entries.RemoveAt(last);
        }

        static void RemoveFromBatch(Entry entry)
        {
            List<Entry> texts = entry.Batch.Texts;
            int last = texts.Count - 1;
            Entry moved = texts[last];
            texts[entry.BatchSlot] = moved;
            moved.BatchSlot = entry.BatchSlot;
            texts.RemoveAt(last);
            entry.Batch.Dirty = true;
        }

        public static void Unregister(LetteraWorldText text)
        {
            if (!s_ByText.TryGetValue(text, out Entry entry)) return;
            s_ByText.Remove(text);
            RemoveFromEntries(entry);
            if (entry.Batch != null) RemoveFromBatch(entry);
        }

        /// <summary>A pose pass wrote this text's vertices: only its part of its shard has to go up again.</summary>
        public static void NotifyPosed(LetteraWorldText text)
        {
            if (s_ByText.TryGetValue(text, out Entry entry) && !s_Posed.Contains(entry)) s_Posed.Add(entry);
        }

        public static void NotifyChanged(LetteraWorldText text)
        {
            if (s_ByText.TryGetValue(text, out Entry entry) && entry.Batch != null) entry.Batch.Dirty = true;
        }

        static void Assign(Entry entry)
        {
            var key = new BatchKey(entry.Text);
            if (entry.Batch != null)
            {
                if (entry.Batch.Key.Equals(key)) return;
                RemoveFromBatch(entry);
            }
            if (!s_Batches.TryGetValue(key, out Batch batch))
            {
                batch = new Batch { Key = key };
                s_Batches[key] = batch;
            }
            entry.BatchSlot = batch.Texts.Count;
            batch.Texts.Add(entry);
            batch.Dirty = true;
            entry.Batch = batch;
        }

        public static void Update()
        {
            for (int i = 0; i < s_Entries.Count; i++)
            {
                Entry entry = s_Entries[i];
                LetteraWorldText text = entry.Text;
                if (text == null || text.Hidden) continue;
                Assign(entry);

                RectTransform rect = text.CachedRect;
                Vector2 size = rect.rect.size;
                if (size != text.LastRectSize) text.OnRectChanged(size);
                if (text.HasAnimator) text.Engine.SyncSerializedFields();
                if (text.Engine.Dirty != DirtyLevel.None && text.Engine.Rebuild())
                {
                    entry.Batch.Dirty = true;
                    text.NotifyRebuilt();
                }
                Matrix4x4 matrix = rect.localToWorldMatrix;
                if (matrix != entry.Matrix)
                {
                    entry.Matrix = matrix;
                    entry.Batch.Dirty = true;
                }
            }

            foreach (Batch batch in s_Batches.Values)
            {
                if (!batch.Dirty) continue;
                batch.Dirty = false;
                Upload(batch);
            }
            UploadPosed();
        }

        // One text's stretch of a shard's retained copy, and the streams it changed. The other texts there are not merged again.
        static void UploadPosed()
        {
            if (s_Posed.Count == 0) return;
            s_PosedShards.Clear();
            for (int p = 0; p < s_Posed.Count; p++)
            {
                Entry entry = s_Posed[p];
                Shard shard = entry.Shard;
                LetteraWorldText text = entry.Text;
                if (shard == null || text == null || shard.Vertices == null || shard.Mesh == null) continue;
                TextEngine e = text.Engine;
                int streams = e.PoseStreams;
                e.ClearPoseStreams();
                if (entry.BaseVertex + e.VertexCount > shard.VertexCount) continue;
                Matrix4x4 m = entry.Matrix;
                if ((streams & TextEngine.StreamPositions) != 0)
                    for (int i = 0; i < e.VertexCount; i++)
                        shard.Vertices[entry.BaseVertex + i] = m.MultiplyPoint3x4(e.Vertices[i]);
                if ((streams & TextEngine.StreamColors) != 0) Array.Copy(e.Colors, 0, shard.Colors, entry.BaseVertex, e.VertexCount);
                if ((streams & TextEngine.StreamUv3) != 0 && e.Styled) Array.Copy(e.Uv3, 0, shard.Uv3, entry.BaseVertex, e.VertexCount);
                int found = -1;
                for (int s = 0; s < s_PosedShards.Count; s++)
                    if (s_PosedShards[s].shard == shard)
                        found = s;
                if (found < 0) s_PosedShards.Add((shard, streams));
                else s_PosedShards[found] = (shard, s_PosedShards[found].streams | streams);
            }
            s_Posed.Clear();
            for (int s = 0; s < s_PosedShards.Count; s++)
            {
                (Shard shard, int streams) = s_PosedShards[s];
                if ((streams & TextEngine.StreamPositions) != 0) shard.Mesh.SetVertices(shard.Vertices, 0, shard.VertexCount);
                if ((streams & TextEngine.StreamColors) != 0) shard.Mesh.SetColors(shard.Colors, 0, shard.VertexCount);
                if ((streams & TextEngine.StreamUv3) != 0) shard.Mesh.SetUVs(3, shard.Uv3, 0, shard.VertexCount);
            }
        }

        static void Upload(Batch batch)
        {
            int shardIndex = 0, vertexCount = 0, indexCount = 0;
            s_Runs.Clear();
            s_ShardStyled = false;
            s_ShardPosed = false;
            batch.Texts.Sort(s_BySortingOrder);
            for (int t = 0; t < batch.Texts.Count; t++) batch.Texts[t].BatchSlot = t;
            for (int t = 0; t < batch.Texts.Count; t++)
            {
                Entry placed = batch.Texts[t];
                LetteraWorldText text = placed.Text;
                if (text == null || text.Hidden) continue;
                TextEngine e = text.Engine;
                if (e.VertexCount == 0) continue;
                if (vertexCount > 0 && vertexCount + e.VertexCount > ShardVertices)
                {
                    Commit(batch, shardIndex++, vertexCount, indexCount);
                    vertexCount = indexCount = 0;
                    s_Runs.Clear();
                    s_ShardStyled = false;
                    s_ShardPosed = false;
                }
                EnsureCapacity(vertexCount + e.VertexCount, indexCount + e.IndexCount);
                // The matrix the update loop just read for this text: asking the engine again costs a call per text.
                Matrix4x4 m = placed.Matrix;
                placed.BaseVertex = vertexCount;
                placed.Shard = GetShard(batch, shardIndex);
                s_ShardPosed |= e.HasPoseConsumers;
                // The face looks along -Z; a mirrored text still has its normal towards the viewer.
                Vector3 normal = m.MultiplyVector(Vector3.back).normalized;
                if (Determinant3(m) < 0) normal = -normal;
                PlaceVertices(e.Vertices, s_Vertices, vertexCount, e.VertexCount, m);
                Array.Fill(s_Normals, normal, vertexCount, e.VertexCount);
                Array.Copy(e.Colors, 0, s_Colors, vertexCount, e.VertexCount);
                Array.Copy(e.Uv0, 0, s_Uv0, vertexCount, e.VertexCount);
                Array.Copy(e.Uv1, 0, s_Uv1, vertexCount, e.VertexCount);
                if (e.Styled)
                {
                    Array.Copy(e.Uv2, 0, s_Uv2, vertexCount, e.VertexCount);
                    Array.Copy(e.Uv3, 0, s_Uv3, vertexCount, e.VertexCount);
                }
                else
                {
                    Array.Clear(s_Uv2, vertexCount, e.VertexCount);
                    Array.Clear(s_Uv3, vertexCount, e.VertexCount);
                }
                Offset(e.Indices, s_Indices, indexCount, e.IndexCount, vertexCount);
                s_ShardStyled |= e.Styled;
                foreach (MeshSegment segment in e.Segments) AddRun(indexCount + segment.IndexStart, segment.IndexCount, e.Styled, segment.Blend, segment.Texture, segment.Custom);
                vertexCount += e.VertexCount;
                indexCount += e.IndexCount;
            }
            if (vertexCount > 0) Commit(batch, shardIndex++, vertexCount, indexCount);
            for (int i = shardIndex; i < batch.Shards.Count; i++)
            {
                if (batch.Shards[i].Mesh != null) batch.Shards[i].Mesh.Clear(false);
            }
        }

        // A text's vertices into the shard's, with the matrix read once: thousands of texts merge in one frame when a scene starts.
        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
        static void PlaceVertices(Vector3[] from, Vector3[] to, int at, int count, in Matrix4x4 m)
        {
            float m00 = m.m00, m01 = m.m01, m02 = m.m02, m03 = m.m03;
            float m10 = m.m10, m11 = m.m11, m12 = m.m12, m13 = m.m13;
            float m20 = m.m20, m21 = m.m21, m22 = m.m22, m23 = m.m23;
            if (at + count > to.Length || count > from.Length) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count; i++)
            {
                Vector3 p = from[i];
                to[at + i] = new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
            }
        }

        [Unity.IL2CPP.CompilerServices.Il2CppSetOption(Unity.IL2CPP.CompilerServices.Option.ArrayBoundsChecks, false)]
        static void Offset(int[] from, int[] to, int at, int count, int offset)
        {
            if (at + count > to.Length || count > from.Length) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count; i++) to[at + i] = from[i] + offset;
        }

        static float Determinant3(in Matrix4x4 m) =>
            m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);

        static readonly Comparison<Entry> s_BySortingOrder = (a, b) =>
        {
            int x = a.Text.SortingOrder, y = b.Text.SortingOrder;
            return x != y ? x.CompareTo(y) : a.Text.GetHashCode().CompareTo(b.Text.GetHashCode());
        };

        static void AddRun(int start, int count, bool styled, LetteraBlend blend, Texture2D texture, Material custom)
        {
            if (s_Runs.Count > 0)
            {
                MaterialRun last = s_Runs[s_Runs.Count - 1];
                // By reference: Unity's equality asks the engine whether each object still lives.
                if (last.Styled == styled && last.Blend == blend && ReferenceEquals(last.Texture, texture) && ReferenceEquals(last.Custom, custom))
                {
                    last.IndexCount += count;
                    s_Runs[s_Runs.Count - 1] = last;
                    return;
                }
            }
            s_Runs.Add(new MaterialRun { IndexStart = start, IndexCount = count, Styled = styled, Blend = blend, Texture = texture, Custom = custom });
        }

        static void EnsureCapacity(int vertices, int indices)
        {
            if (vertices > s_Vertices.Length)
            {
                int capacity = Math.Max(Math.Max(vertices, s_Vertices.Length * 2), ScratchVertices);
                Array.Resize(ref s_Vertices, capacity);
                Array.Resize(ref s_Normals, capacity);
                Array.Resize(ref s_Colors, capacity);
                Array.Resize(ref s_Uv0, capacity);
                Array.Resize(ref s_Uv1, capacity);
                Array.Resize(ref s_Uv2, capacity);
                Array.Resize(ref s_Uv3, capacity);
            }
            if (indices > s_Indices.Length) Array.Resize(ref s_Indices, Math.Max(Math.Max(indices, s_Indices.Length * 2), ScratchVertices * 3 / 2));
        }

        static void Commit(Batch batch, int index, int vertexCount, int indexCount)
        {
            Shard shard = GetShard(batch, index);
            Mesh mesh = shard.Mesh;
            mesh.Clear(false);
            shard.VertexCount = vertexCount;
            if (s_ShardPosed)
            {
                // Static shards keep sharing the scratch arrays. One that moves keeps its own copy.
                if (shard.Vertices == null || shard.Vertices.Length < vertexCount)
                {
                    shard.Vertices = new Vector3[vertexCount * 2];
                    shard.Colors = new Color32[vertexCount * 2];
                    shard.Uv3 = new Vector4[vertexCount * 2];
                    mesh.MarkDynamic();
                }
                Array.Copy(s_Vertices, shard.Vertices, vertexCount);
                Array.Copy(s_Colors, shard.Colors, vertexCount);
                Array.Copy(s_Uv3, shard.Uv3, vertexCount);
            }
            else
            {
                shard.Vertices = null;
                shard.Colors = null;
                shard.Uv3 = null;
            }
            mesh.indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(s_Vertices, 0, vertexCount);
            mesh.SetNormals(s_Normals, 0, vertexCount);
            mesh.SetColors(s_Colors, 0, vertexCount);
            mesh.SetUVs(0, s_Uv0, 0, vertexCount);
            mesh.SetUVs(1, s_Uv1, 0, vertexCount);
            if (s_ShardStyled)
            {
                mesh.SetUVs(2, s_Uv2, 0, vertexCount);
                mesh.SetUVs(3, s_Uv3, 0, vertexCount);
            }

            BatchKey key = batch.Key;
            MeshRenderer renderer = shard.Renderer;
            Material source = key.Lit ? LetteraMaterials.WorldLit : LetteraMaterials.World;
            int runs = s_Runs.Count;
            if (runs <= 1)
            {
                mesh.subMeshCount = 1;
                mesh.SetIndices(s_Indices, 0, indexCount, MeshTopology.Triangles, 0, true);
                MaterialRun only = runs == 1 ? s_Runs[0] : default;
                SetMaterials(shard, 1, 0, only.Custom != null ? only.Custom : LetteraMaterials.Variant(source, only.Styled, only.Blend, only.Texture));
            }
            else
            {
                mesh.subMeshCount = runs;
                for (int i = 0; i < runs; i++)
                {
                    MaterialRun run = s_Runs[i];
                    mesh.SetIndices(s_Indices, run.IndexStart, run.IndexCount, MeshTopology.Triangles, i, true);
                    SetMaterials(shard, runs, i, run.Custom != null ? run.Custom : LetteraMaterials.Variant(source, run.Styled, run.Blend, run.Texture));
                }
            }
            renderer.sortingLayerID = key.SortingLayer;
            renderer.sortingOrder = key.SortingOrder;
            renderer.shadowCastingMode = key.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            shard.Object.layer = key.Layer;
        }

        // Reassigns the renderer's materials only when they changed, so a steady shard allocates nothing.
        static void SetMaterials(Shard shard, int count, int index, Material material)
        {
            if (shard.Materials == null || shard.Materials.Length != count)
            {
                shard.Materials = new Material[count];
                shard.Materials[index] = material;
                shard.MaterialsDirty = true;
            }
            else if (shard.Materials[index] != material)
            {
                shard.Materials[index] = material;
                shard.MaterialsDirty = true;
            }
            if (index == count - 1 && shard.MaterialsDirty)
            {
                shard.MaterialsDirty = false;
                shard.Renderer.sharedMaterials = shard.Materials;
            }
        }

        static Shard GetShard(Batch batch, int index)
        {
            while (batch.Shards.Count <= index) batch.Shards.Add(new Shard());
            Shard shard = batch.Shards[index];
            if (shard.Object != null) return shard;

            shard.Materials = null;
            shard.Object = new GameObject("Lettera World Batch") { hideFlags = HideFlags.HideAndDontSave };
            var filter = shard.Object.AddComponent<MeshFilter>();
            shard.Renderer = shard.Object.AddComponent<MeshRenderer>();
            shard.Renderer.sharedMaterial = batch.Key.Lit ? LetteraMaterials.WorldLit : LetteraMaterials.World;
            shard.Renderer.lightProbeUsage = LightProbeUsage.Off;
            shard.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            shard.Renderer.receiveShadows = false;
            shard.Mesh = new Mesh { name = "Lettera World Batch", hideFlags = HideFlags.HideAndDontSave };
            filter.sharedMesh = shard.Mesh;

            // The shard belongs to the text's scene, so preview and additive scene cameras see it.
            for (int t = 0; t < batch.Texts.Count; t++)
            {
                LetteraWorldText text = batch.Texts[t].Text;
                if (text == null || !text.gameObject.scene.IsValid()) continue;
                SceneManager.MoveGameObjectToScene(shard.Object, text.gameObject.scene);
                break;
            }
            return shard;
        }

        /// <summary>A scene that closes takes its shards with it, and what is left of them goes here. Its texts have unregistered by then.</summary>
        internal static void ForgetScene(Scene scene)
        {
            int handle = scene.GetHashCode();
            List<BatchKey> gone = null;
            foreach (KeyValuePair<BatchKey, Batch> pair in s_Batches)
            {
                if (pair.Key.SceneHandle != handle) continue;
                foreach (Shard shard in pair.Value.Shards)
                {
                    if (shard.Object != null) Destroy(shard.Object);
                    if (shard.Mesh != null) Destroy(shard.Mesh);
                }
                (gone = gone ?? new List<BatchKey>()).Add(pair.Key);
            }
            if (gone != null)
                foreach (BatchKey key in gone)
                    s_Batches.Remove(key);
        }

        /// <summary>Lets the merge buffers go; the next merge makes them again.</summary>
        internal static void ReleaseScratch()
        {
            s_Vertices = Array.Empty<Vector3>();
            s_Normals = Array.Empty<Vector3>();
            s_Colors = Array.Empty<Color32>();
            s_Uv0 = Array.Empty<Vector4>();
            s_Uv1 = Array.Empty<Vector4>();
            s_Uv2 = Array.Empty<Vector4>();
            s_Uv3 = Array.Empty<Vector4>();
            s_Indices = Array.Empty<int>();
        }

        public static void Shutdown()
        {
            foreach (Batch batch in s_Batches.Values)
            {
                foreach (Shard shard in batch.Shards)
                {
                    if (shard.Object != null) Destroy(shard.Object);
                    if (shard.Mesh != null) Destroy(shard.Mesh);
                }
            }
            s_Batches.Clear();
            s_Entries.Clear();
            s_ByText.Clear();
        }

        static void Destroy(UnityEngine.Object target)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
