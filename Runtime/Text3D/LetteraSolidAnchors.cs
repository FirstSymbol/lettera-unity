using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// A place on a solid text's surface. It is held by the triangle it lies in, so it goes wherever motion and deformers take
    /// that triangle. Read it again any frame with <see cref="LetteraText3D.TryGetPose(in LetteraSolidAnchor, out Pose, Space)"/>.
    /// </summary>
    public struct LetteraSolidAnchor
    {
        internal int Placement, Glyph, Cluster, Map, Triangle;
        internal LetteraSolidKey Key;
        internal float3 Weights, RestPoint;
        // The turn from the rest triangle's frame to the anchor's own, so that a turned triangle turns the anchor alike.
        internal quaternion Turn, RestRotation;
        internal float RestEdge;
        internal LetteraSolidSurface SurfaceOf;
        internal bool Set;

        /// <summary>False for an anchor that was never found.</summary>
        public bool IsSet => Set;
        /// <summary>The character of the shown text it lies on, or -1 on a layer made around a word, a line or the whole text.</summary>
        public int Character => Cluster;
        /// <summary>Which surface of the solid it lies on.</summary>
        public LetteraSolidSurface Surface => SurfaceOf;
        /// <summary>The text's layer it lies on, or -1 on the glyphs themselves.</summary>
        public int Layer => Map - 1;
    }

    /// <summary>What a ray found on a solid text.</summary>
    public struct LetteraSolidHit
    {
        public LetteraSolidAnchor Anchor;
        /// <summary>In world space.</summary>
        public Vector3 Point, Normal;
        /// <summary>From the ray's origin, in world units.</summary>
        public float Distance;
        public int Character => Anchor.Character;
    }

    public partial class LetteraText3D
    {
        /// <summary>
        /// The nearest place the ray meets the drawn solids, glyphs and layers alike, as they stand this frame. Needs no collider.
        /// Keep <see cref="LetteraSolidHit.Anchor"/> to follow that place later through motion and deformers.
        /// </summary>
        public bool Raycast(Ray ray, out LetteraSolidHit hit, float maxDistance = float.PositiveInfinity)
        {
            hit = default;
            ForceRebuild();
            if (m_Builder == null || m_Builder.VertexCount == 0) return false;
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            float3 origin = toLocal.MultiplyPoint3x4(ray.origin);
            float3 direction = toLocal.MultiplyVector(ray.direction);
            if (math.lengthsq(direction) < 1e-20f) return false;
            if (!LetteraSolidRay.Cast(m_Builder, origin, direction, out int place, out int triangle, out float3 weights, out float t)) return false;
            LetteraSolidAnchor anchor = LetteraSolidRay.MakeAnchor(m_Builder, place, triangle, weights, null);
            Vector3 local = origin + direction * t;
            Vector3 point = transform.TransformPoint(local);
            if (float.IsFinite(maxDistance) && Vector3.Distance(ray.origin, point) > maxDistance) return false;
            LetteraSolidRay.Read(m_Builder, anchor, out float3 _, out float3 normal, out quaternion _, out float _);
            hit = new LetteraSolidHit
            {
                Anchor = anchor,
                Point = point,
                Normal = transform.TransformDirection(normal).normalized,
                Distance = Vector3.Distance(ray.origin, point),
            };
            return true;
        }

        /// <summary>
        /// Where an anchor stands now: its position on the surface, and a rotation whose up is the surface's outward normal and whose
        /// forward leans toward the viewer of the text's front. False once the glyph it lay on is gone or built again with another shape.
        /// </summary>
        public bool TryGetPose(in LetteraSolidAnchor anchor, out Pose pose, Space space = Space.World) => TryGetPose(anchor, out pose, out float _, space);

        /// <summary>As the other overload, with how much the surface there was scaled since rest, by motion or by deformers.</summary>
        public bool TryGetPose(in LetteraSolidAnchor anchor, out Pose pose, out float scale, Space space = Space.World)
        {
            LetteraSolidAnchor copy = anchor;
            bool found = TryGetPoseLocal(ref copy, out pose, out scale);
            if (found && space == Space.World)
            {
                Transform t = transform;
                pose = new Pose(t.TransformPoint(pose.position), t.rotation * pose.rotation);
            }
            return found;
        }

        /// <summary>
        /// An anchor on a character's glyph, from a point in the text's own space near its surface, for a script that places things
        /// itself. The nearest point of the glyph's solid is taken.
        /// </summary>
        public bool TryAnchorCharacter(int character, Vector3 localPoint, out LetteraSolidAnchor anchor)
        {
            anchor = default;
            ForceRebuild();
            if (m_Builder == null) return false;
            IReadOnlyList<LetteraSolidMeshBuilder.Placement> placed = m_Builder.Placed;
            float best = float.MaxValue;
            int bestPlace = -1, bestTriangle = -1;
            float3 bestWeights = default;
            NativeArray<LetteraSolidMeshVertex> rest = m_Builder.RestVertices;
            for (int p = 0; p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.Cluster != character || place.Map != 0) continue;
                NativeArray<int> indices = place.Entry.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    float3 a = rest[place.VertexStart + indices[i]].Position, b = rest[place.VertexStart + indices[i + 1]].Position, c = rest[place.VertexStart + indices[i + 2]].Position;
                    float3 w = LetteraSolidRay.Closest(localPoint, a, b, c);
                    float d = math.distancesq(w.x * a + w.y * b + w.z * c, localPoint);
                    if (d < best && math.lengthsq(math.cross(b - a, c - a)) > 1e-12f)
                    {
                        best = d;
                        bestPlace = p;
                        bestTriangle = i;
                        bestWeights = w;
                    }
                }
            }
            if (bestPlace < 0) return false;
            anchor = LetteraSolidRay.MakeAnchor(m_Builder, bestPlace, bestTriangle, bestWeights, null);
            return true;
        }

        internal bool TryGetPoseLocal(ref LetteraSolidAnchor anchor, out Pose pose, out float scale)
        {
            pose = Pose.identity;
            scale = 0;
            if (!anchor.Set || m_Builder == null || !LetteraSolidRay.Resolve(m_Builder, ref anchor)) return false;
            LetteraSolidRay.Read(m_Builder, anchor, out float3 position, out float3 _, out quaternion rotation, out scale);
            pose = new Pose(position, rotation);
            return true;
        }
    }

    /// <summary>Rays against a text's drawn triangles, and anchors read back from them.</summary>
    internal static unsafe class LetteraSolidRay
    {
        struct Item
        {
            [NativeDisableUnsafePtrRestriction] public int* Indices;
            public int Count, VertexStart;
        }

        [BurstCompile]
        struct CastJob : IJob
        {
            [ReadOnly] public NativeArray<LetteraSolidMeshVertex> Vertices;
            [ReadOnly] public NativeArray<Item> Items;
            public float3 Origin, Direction;
            public NativeArray<float4> Result;

            public void Execute()
            {
                float best = float.MaxValue;
                float4 found = new float4(-1, -1, 0, 0);
                float2 weights = default;
                for (int p = 0; p < Items.Length; p++)
                {
                    Item item = Items[p];
                    for (int i = 0; i + 2 < item.Count; i += 3)
                    {
                        float3 a = Vertices[item.VertexStart + item.Indices[i]].Position;
                        float3 b = Vertices[item.VertexStart + item.Indices[i + 1]].Position;
                        float3 c = Vertices[item.VertexStart + item.Indices[i + 2]].Position;
                        float3 e1 = b - a, e2 = c - a;
                        float3 h = math.cross(Direction, e2);
                        float det = math.dot(e1, h);
                        if (math.abs(det) < 1e-20f) continue;
                        float inv = 1f / det;
                        float3 s = Origin - a;
                        float u = math.dot(s, h) * inv;
                        if (u < 0 || u > 1) continue;
                        float3 q = math.cross(s, e1);
                        float v = math.dot(Direction, q) * inv;
                        if (v < 0 || u + v > 1) continue;
                        float t = math.dot(e2, q) * inv;
                        if (t <= 0 || t >= best) continue;
                        best = t;
                        found = new float4(p, i, t, 0);
                        weights = new float2(u, v);
                    }
                }
                Result[0] = found;
                Result[1] = new float4(weights, 0, 0);
            }
        }

        internal static bool Cast(LetteraSolidMeshBuilder builder, float3 origin, float3 direction, out int place, out int triangle, out float3 weights, out float t)
        {
            IReadOnlyList<LetteraSolidMeshBuilder.Placement> placed = builder.Placed;
            var items = new NativeArray<Item>(placed.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int p = 0; p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement placement = placed[p];
                NativeArray<int> indices = placement.Entry.Indices;
                items[p] = new Item
                {
                    Indices = indices.IsCreated ? (int*)indices.GetUnsafeReadOnlyPtr() : null,
                    Count = indices.IsCreated ? indices.Length : 0,
                    VertexStart = placement.VertexStart,
                };
            }
            var result = new NativeArray<float4>(2, Allocator.TempJob);
            new CastJob { Vertices = builder.PosedVertices, Items = items, Origin = origin, Direction = direction, Result = result }.Run();
            float4 found = result[0], uv = result[1];
            result.Dispose();
            items.Dispose();
            place = (int)found.x;
            triangle = (int)found.y;
            t = found.z;
            weights = new float3(1 - uv.x - uv.y, uv.x, uv.y);
            return place >= 0;
        }

        /// <summary>A ray against one placement's triangles, in the given vertices: the rest ones to find where something goes.</summary>
        internal static bool CastPlacement(NativeArray<LetteraSolidMeshVertex> vertices, in LetteraSolidMeshBuilder.Placement placement, float3 origin, float3 direction, out int triangle, out float3 weights, out float t)
        {
            NativeArray<int> indices = placement.Entry.Indices;
            triangle = -1;
            weights = default;
            t = 0;
            if (!indices.IsCreated || indices.Length < 3) return false;
            var items = new NativeArray<Item>(1, Allocator.TempJob);
            items[0] = new Item { Indices = (int*)indices.GetUnsafeReadOnlyPtr(), Count = indices.Length, VertexStart = placement.VertexStart };
            var result = new NativeArray<float4>(2, Allocator.TempJob);
            new CastJob { Vertices = vertices, Items = items, Origin = origin, Direction = direction, Result = result }.Run();
            float4 found = result[0], uv = result[1];
            result.Dispose();
            items.Dispose();
            triangle = (int)found.y;
            t = found.z;
            weights = new float3(1 - uv.x - uv.y, uv.x, uv.y);
            return found.x >= 0;
        }

        static void Corners(NativeArray<LetteraSolidMeshVertex> vertices, in LetteraSolidMeshBuilder.Placement place, int triangle, out int a, out int b, out int c)
        {
            NativeArray<int> indices = place.Entry.Indices;
            a = place.VertexStart + indices[triangle];
            b = place.VertexStart + indices[triangle + 1];
            c = place.VertexStart + indices[triangle + 2];
        }

        // A frame a triangle carries: forward along its normal, up along its first edge.
        static quaternion FrameOf(float3 a, float3 b, float3 c, out float edge)
        {
            float3 e = b - a;
            edge = math.length(e);
            float3 n = math.cross(e, c - a);
            if (edge < 1e-12f || math.lengthsq(n) < 1e-24f) return quaternion.identity;
            return quaternion.LookRotationSafe(math.normalize(n), e / edge);
        }

        static LetteraSolidSurface SurfaceAt(LetteraSolidEntry entry, int triangle)
        {
            int[] starts = entry.SurfaceStarts;
            for (int s = 0; s < 5; s++)
                if (triangle >= starts[s] && triangle < starts[s + 1])
                    return (LetteraSolidSurface)s;
            return LetteraSolidSurface.Face;
        }

        /// <summary>
        /// An anchor on a triangle of the text at rest. Its rotation at rest has the surface's normal as up, and the given forward,
        /// or else toward the viewer of the front, or else the text's up, laid onto the surface.
        /// </summary>
        internal static LetteraSolidAnchor MakeAnchor(LetteraSolidMeshBuilder builder, int place, int triangle, float3 weights, float3? up)
        {
            LetteraSolidMeshBuilder.Placement placement = builder.Placed[place];
            NativeArray<LetteraSolidMeshVertex> rest = builder.RestVertices;
            Corners(rest, placement, triangle, out int ia, out int ib, out int ic);
            float3 a = rest[ia].Position, b = rest[ib].Position, c = rest[ic].Position;
            float3 normal = math.normalizesafe(weights.x * rest[ia].Normal + weights.y * rest[ib].Normal + weights.z * rest[ic].Normal, new float3(0, 0, -1));
            float3 surfaceUp = up ?? normal;
            float3 forward = new float3(0, 0, -1);
            forward -= surfaceUp * math.dot(forward, surfaceUp);
            if (math.lengthsq(forward) < 1e-6f)
            {
                forward = new float3(0, 1, 0);
                forward -= surfaceUp * math.dot(forward, surfaceUp);
            }
            quaternion own = quaternion.LookRotationSafe(math.normalizesafe(forward), surfaceUp);
            quaternion frame = FrameOf(a, b, c, out float edge);
            return new LetteraSolidAnchor
            {
                Placement = place,
                Glyph = placement.GlyphIndex,
                Cluster = placement.Cluster,
                Map = placement.Map,
                Triangle = triangle,
                Key = placement.Entry.Key,
                Weights = weights,
                RestPoint = weights.x * a + weights.y * b + weights.z * c,
                Turn = math.mul(math.inverse(frame), own),
                RestRotation = own,
                RestEdge = edge,
                SurfaceOf = SurfaceAt(placement.Entry, triangle),
                Set = true,
            };
        }

        static bool Same(in LetteraSolidMeshBuilder.Placement place, in LetteraSolidAnchor anchor, NativeArray<LetteraSolidMeshVertex> rest, bool exact)
        {
            if (place.Map != anchor.Map || place.Cluster != anchor.Cluster || !place.Entry.Key.Equals(anchor.Key)) return false;
            NativeArray<int> indices = place.Entry.Indices;
            if (!indices.IsCreated || anchor.Triangle + 2 >= indices.Length) return false;
            if (!exact && anchor.Cluster >= 0) return true;
            // Layers made around a word or a line share one key, so only where they stand tells them apart.
            Corners(rest, place, anchor.Triangle, out int a, out int b, out int c);
            float3 point = anchor.Weights.x * rest[a].Position + anchor.Weights.y * rest[b].Position + anchor.Weights.z * rest[c].Position;
            return math.distancesq(point, anchor.RestPoint) < 1e-6f * math.max(1, math.lengthsq(anchor.RestPoint));
        }

        /// <summary>Finds the anchor's placement in the text as built now, and remembers it for next time.</summary>
        internal static bool Resolve(LetteraSolidMeshBuilder builder, ref LetteraSolidAnchor anchor)
        {
            IReadOnlyList<LetteraSolidMeshBuilder.Placement> placed = builder.Placed;
            NativeArray<LetteraSolidMeshVertex> rest = builder.RestVertices;
            if (anchor.Placement < placed.Count && Same(placed[anchor.Placement], anchor, rest, true)) return true;
            // The text was built again: the same glyph at the same place first, then the same glyph moved.
            for (int pass = 0; pass < 2; pass++)
                for (int p = 0; p < placed.Count; p++)
                {
                    if (!Same(placed[p], anchor, rest, pass == 0)) continue;
                    anchor.Placement = p;
                    if (pass == 1)
                    {
                        Corners(rest, placed[p], anchor.Triangle, out int a, out int b, out int c);
                        anchor.RestPoint = anchor.Weights.x * rest[a].Position + anchor.Weights.y * rest[b].Position + anchor.Weights.z * rest[c].Position;
                    }
                    return true;
                }
            // Built with another shape, by a new edge or a deformer's finer cut: the nearest point of the same glyph at rest.
            if (anchor.Cluster < 0) return false;
            for (int p = 0; p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.Cluster != anchor.Cluster || place.Map != anchor.Map || !place.Entry.Indices.IsCreated) continue;
                NativeArray<int> indices = place.Entry.Indices;
                float best = float.MaxValue;
                int triangle = -1;
                float3 weights = default;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    float3 a = rest[place.VertexStart + indices[i]].Position, b = rest[place.VertexStart + indices[i + 1]].Position, c = rest[place.VertexStart + indices[i + 2]].Position;
                    if (math.lengthsq(math.cross(b - a, c - a)) < 1e-12f) continue;
                    float3 w = Closest(anchor.RestPoint, a, b, c);
                    float d = math.distancesq(w.x * a + w.y * b + w.z * c, anchor.RestPoint);
                    if (d < best)
                    {
                        best = d;
                        triangle = i;
                        weights = w;
                    }
                }
                float reach = 0.05f * place.SizeZ;
                if (triangle < 0 || best > reach * reach) continue;
                Corners(rest, place, triangle, out int ia, out int ib, out int ic);
                quaternion frame = FrameOf(rest[ia].Position, rest[ib].Position, rest[ic].Position, out float edge);
                anchor.Placement = p;
                anchor.Glyph = place.GlyphIndex;
                anchor.Key = place.Entry.Key;
                anchor.Triangle = triangle;
                anchor.Weights = weights;
                anchor.RestPoint = weights.x * rest[ia].Position + weights.y * rest[ib].Position + weights.z * rest[ic].Position;
                anchor.Turn = math.mul(math.inverse(frame), anchor.RestRotation);
                anchor.RestEdge = edge;
                anchor.SurfaceOf = SurfaceAt(place.Entry, triangle);
                return true;
            }
            return false;
        }

        internal static void Read(LetteraSolidMeshBuilder builder, in LetteraSolidAnchor anchor, out float3 position, out float3 normal, out quaternion rotation, out float scale)
        {
            LetteraSolidMeshBuilder.Placement placement = builder.Placed[anchor.Placement];
            NativeArray<LetteraSolidMeshVertex> posed = builder.PosedVertices;
            Corners(posed, placement, anchor.Triangle, out int ia, out int ib, out int ic);
            LetteraSolidMeshVertex a = posed[ia], b = posed[ib], c = posed[ic];
            float3 w = anchor.Weights;
            position = w.x * a.Position + w.y * b.Position + w.z * c.Position;
            normal = math.normalizesafe(w.x * a.Normal + w.y * b.Normal + w.z * c.Normal, new float3(0, 0, -1));
            quaternion frame = FrameOf(a.Position, b.Position, c.Position, out float edge);
            rotation = math.normalize(math.mul(frame, anchor.Turn));
            scale = anchor.RestEdge > 1e-12f ? edge / anchor.RestEdge : 1;
        }

        /// <summary>How opaque the surface under an anchor is drawn now, 0 to 255: a reveal or a fade hides what sits there.</summary>
        internal static int AlphaAt(LetteraSolidMeshBuilder builder, in LetteraSolidAnchor anchor)
        {
            LetteraSolidMeshBuilder.Placement placement = builder.Placed[anchor.Placement];
            return builder.PosedVertices[placement.VertexStart + placement.Entry.Indices[anchor.Triangle]].Color.a;
        }

        /// <summary>Weights of the point of a triangle nearest to p.</summary>
        internal static float3 Closest(float3 p, float3 a, float3 b, float3 c)
        {
            float3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = math.dot(ab, ap), d2 = math.dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return new float3(1, 0, 0);
            float3 bp = p - b;
            float d3 = math.dot(ab, bp), d4 = math.dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return new float3(0, 1, 0);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                float v = d1 / (d1 - d3);
                return new float3(1 - v, v, 0);
            }
            float3 cp = p - c;
            float d5 = math.dot(ab, cp), d6 = math.dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return new float3(0, 0, 1);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                float w = d2 / (d2 - d6);
                return new float3(1 - w, 0, w);
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            {
                float w = (d4 - d3) / (d4 - d3 + (d5 - d6));
                return new float3(0, 1 - w, w);
            }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            return new float3(1 - vv - ww, vv, ww);
        }
    }
}
