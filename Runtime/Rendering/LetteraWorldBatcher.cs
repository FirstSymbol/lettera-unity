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
                Layer = text.gameObject.layer;
                SceneHandle = text.gameObject.scene.GetHashCode();
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
        }

        sealed class Batch
        {
            public BatchKey Key;
            public readonly List<LetteraWorldText> Texts = new List<LetteraWorldText>();
            public readonly List<Shard> Shards = new List<Shard>();
            public bool Dirty = true;
        }

        sealed class Entry
        {
            public LetteraWorldText Text;
            public Batch Batch;
            public Matrix4x4 Matrix;
        }

        static readonly List<Entry> s_Entries = new List<Entry>();
        static readonly Dictionary<LetteraWorldText, Entry> s_ByText = new Dictionary<LetteraWorldText, Entry>();
        static readonly Dictionary<BatchKey, Batch> s_Batches = new Dictionary<BatchKey, Batch>();

        // Merged geometry of one shard.
        static Vector3[] s_Vertices = new Vector3[ShardVertices + 1024];
        static Vector3[] s_Normals = new Vector3[ShardVertices + 1024];
        static Color32[] s_Colors = new Color32[ShardVertices + 1024];
        static Vector4[] s_Uv0 = new Vector4[ShardVertices + 1024];
        static Vector4[] s_Uv1 = new Vector4[ShardVertices + 1024];
        static int[] s_Indices = new int[(ShardVertices + 1024) * 3 / 2];

        public static void Register(LetteraWorldText text)
        {
            if (s_ByText.ContainsKey(text)) return;
            var entry = new Entry { Text = text, Matrix = text.transform.localToWorldMatrix };
            s_ByText[text] = entry;
            s_Entries.Add(entry);
            Assign(entry);
        }

        public static void Unregister(LetteraWorldText text)
        {
            if (!s_ByText.TryGetValue(text, out Entry entry)) return;
            s_ByText.Remove(text);
            s_Entries.Remove(entry);
            if (entry.Batch != null)
            {
                entry.Batch.Texts.Remove(text);
                entry.Batch.Dirty = true;
            }
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
                entry.Batch.Texts.Remove(entry.Text);
                entry.Batch.Dirty = true;
            }
            if (!s_Batches.TryGetValue(key, out Batch batch))
            {
                batch = new Batch { Key = key };
                s_Batches[key] = batch;
            }
            batch.Texts.Add(entry.Text);
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

                Vector2 size = ((RectTransform)text.transform).rect.size;
                if (size != text.LastRectSize) text.OnRectChanged(size);
                if (text.HasAnimator) text.Engine.SyncSerializedFields();
                if (text.Engine.Dirty != DirtyLevel.None && text.Engine.Rebuild())
                {
                    entry.Batch.Dirty = true;
                    text.NotifyRebuilt();
                }
                Matrix4x4 matrix = text.transform.localToWorldMatrix;
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
        }

        static void Upload(Batch batch)
        {
            int shardIndex = 0, vertexCount = 0, indexCount = 0;
            batch.Texts.Sort((a, b) => a.SortingOrder != b.SortingOrder ? a.SortingOrder.CompareTo(b.SortingOrder) : a.GetHashCode().CompareTo(b.GetHashCode()));
            foreach (LetteraWorldText text in batch.Texts)
            {
                if (text == null || text.Hidden) continue;
                TextEngine e = text.Engine;
                if (e.VertexCount == 0) continue;
                if (vertexCount > 0 && vertexCount + e.VertexCount > ShardVertices)
                {
                    Commit(batch, shardIndex++, vertexCount, indexCount);
                    vertexCount = indexCount = 0;
                }
                EnsureCapacity(vertexCount + e.VertexCount, indexCount + e.IndexCount);
                Matrix4x4 m = text.transform.localToWorldMatrix;
                // The face looks along -Z; a mirrored text still has its normal towards the viewer.
                Vector3 normal = m.MultiplyVector(Vector3.back).normalized;
                if (m.determinant < 0) normal = -normal;
                for (int i = 0; i < e.VertexCount; i++)
                {
                    s_Vertices[vertexCount + i] = m.MultiplyPoint3x4(e.Vertices[i]);
                    s_Normals[vertexCount + i] = normal;
                    s_Colors[vertexCount + i] = e.Colors[i];
                    s_Uv0[vertexCount + i] = e.Uv0[i];
                    s_Uv1[vertexCount + i] = e.Uv1[i];
                }
                for (int i = 0; i < e.IndexCount; i++) s_Indices[indexCount + i] = e.Indices[i] + vertexCount;
                vertexCount += e.VertexCount;
                indexCount += e.IndexCount;
            }
            if (vertexCount > 0) Commit(batch, shardIndex++, vertexCount, indexCount);
            for (int i = shardIndex; i < batch.Shards.Count; i++)
            {
                if (batch.Shards[i].Mesh != null) batch.Shards[i].Mesh.Clear(false);
            }
        }

        static void EnsureCapacity(int vertices, int indices)
        {
            if (vertices > s_Vertices.Length)
            {
                int capacity = Math.Max(vertices, s_Vertices.Length * 2);
                Array.Resize(ref s_Vertices, capacity);
                Array.Resize(ref s_Normals, capacity);
                Array.Resize(ref s_Colors, capacity);
                Array.Resize(ref s_Uv0, capacity);
                Array.Resize(ref s_Uv1, capacity);
            }
            if (indices > s_Indices.Length) Array.Resize(ref s_Indices, Math.Max(indices, s_Indices.Length * 2));
        }

        static void Commit(Batch batch, int index, int vertexCount, int indexCount)
        {
            Shard shard = GetShard(batch, index);
            Mesh mesh = shard.Mesh;
            mesh.Clear(false);
            mesh.indexFormat = vertexCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(s_Vertices, 0, vertexCount);
            mesh.SetNormals(s_Normals, 0, vertexCount);
            mesh.SetColors(s_Colors, 0, vertexCount);
            mesh.SetUVs(0, s_Uv0, 0, vertexCount);
            mesh.SetUVs(1, s_Uv1, 0, vertexCount);
            mesh.SetIndices(s_Indices, 0, indexCount, MeshTopology.Triangles, 0, true);

            BatchKey key = batch.Key;
            MeshRenderer renderer = shard.Renderer;
            renderer.sortingLayerID = key.SortingLayer;
            renderer.sortingOrder = key.SortingOrder;
            renderer.shadowCastingMode = key.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            shard.Object.layer = key.Layer;
        }

        static Shard GetShard(Batch batch, int index)
        {
            while (batch.Shards.Count <= index) batch.Shards.Add(new Shard());
            Shard shard = batch.Shards[index];
            if (shard.Object != null) return shard;

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
            foreach (LetteraWorldText text in batch.Texts)
            {
                if (text == null || !text.gameObject.scene.IsValid()) continue;
                SceneManager.MoveGameObjectToScene(shard.Object, text.gameObject.scene);
                break;
            }
            return shard;
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
