using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>The builder on outlines written by hand and on whole fonts: every mesh closed, outward, and of the right volume.</summary>
    public class SolidBuilderTests
    {
        static readonly LetteraSolidRecipe Plain = new LetteraSolidRecipe { Tolerance = 0.001f, SmoothingCos = 0.866f, Faces = LetteraSolidFaces.All };

        static LetteraSolidRecipe WithEdge(LetteraBevelShape shape, float width = 0.02f, float height = 0.02f, int segments = 4)
        {
            LetteraBevel bevel = LetteraBevel.Default;
            bevel.Shape = shape;
            bevel.Width = width;
            bevel.Height = height;
            bevel.Segments = segments;
            LetteraSolidRecipe recipe = Plain;
            recipe.Front = bevel;
            recipe.Back = bevel;
            return recipe;
        }

        static float[] Square(float x, float y, float size, bool counterClockwise = true)
        {
            return counterClockwise
                ? new[] { 0, x, y, 1, x + size, y, 1, x + size, y + size, 1, x, y + size, 4f }
                : new[] { 0, x, y, 1, x, y + size, 1, x + size, y + size, 1, x + size, y, 4f };
        }

        static float[] Join(params float[][] parts)
        {
            var all = new List<float>();
            foreach (float[] part in parts) all.AddRange(part);
            return all.ToArray();
        }

        static LetteraSolidEntry Build(float[] commands, in LetteraSolidRecipe recipe) =>
            LetteraSolidCache.Build(default, commands, commands.Length, 1, recipe);

        static float3 Final(in LetteraSolidVertex v, float depth, float bevels) => new float3(v.Position.xy, v.Position.z + v.Weight * (depth - bevels));

        /// <summary>Every edge, by welded position, shared by exactly two triangles going opposite ways.</summary>
        static string ClosedProblem(LetteraSolidEntry entry, float depth = 0.3f)
        {
            float bevels = entry.FrontHeight + entry.BackHeight;
            var ids = new Dictionary<int3, int>();
            int IdOf(int index)
            {
                float3 p = Final(entry.Vertices[index], depth, bevels);
                int3 q = (int3)math.round(p * 20000f);
                if (!ids.TryGetValue(q, out int id))
                {
                    id = ids.Count;
                    ids[q] = id;
                }
                return id;
            }
            var edges = new Dictionary<long, int>();
            void Note(int a, int b)
            {
                if (a == b) return;
                long key = (long)a << 32 | (uint)b;
                edges.TryGetValue(key, out int count);
                edges[key] = count + 1;
            }
            for (int t = 0; t + 2 < entry.Indices.Length; t += 3)
            {
                int a = IdOf(entry.Indices[t]), b = IdOf(entry.Indices[t + 1]), c = IdOf(entry.Indices[t + 2]);
                Note(a, b);
                Note(b, c);
                Note(c, a);
            }
            int bad = 0;
            foreach (KeyValuePair<long, int> edge in edges)
            {
                long opposite = (edge.Key & 0xFFFFFFFFL) << 32 | (uint)(edge.Key >> 32);
                edges.TryGetValue(opposite, out int back);
                if (back != edge.Value) bad++;
            }
            return bad == 0 ? null : bad + " of " + edges.Count + " edges are not matched";
        }

        static float Volume(LetteraSolidEntry entry, float depth)
        {
            float bevels = entry.FrontHeight + entry.BackHeight;
            float volume = 0;
            for (int t = 0; t + 2 < entry.Indices.Length; t += 3)
            {
                float3 a = Final(entry.Vertices[entry.Indices[t]], depth, bevels), b = Final(entry.Vertices[entry.Indices[t + 1]], depth, bevels), c = Final(entry.Vertices[entry.Indices[t + 2]], depth, bevels);
                volume += math.dot(a, math.cross(b, c)) / 6;
            }
            return volume;
        }

        static float SurfaceArea(LetteraSolidEntry entry, LetteraSolidSurface surface)
        {
            float area = 0;
            for (int t = entry.SurfaceStarts[(int)surface]; t + 2 < entry.SurfaceStarts[(int)surface + 1]; t += 3)
            {
                float3 a = entry.Vertices[entry.Indices[t]].Position, b = entry.Vertices[entry.Indices[t + 1]].Position, c = entry.Vertices[entry.Indices[t + 2]].Position;
                area += math.length(math.cross(b - a, c - a)) * 0.5f;
            }
            return area;
        }

        // Each triangle's geometric normal agrees with its vertices' shading normals, where the shape is smooth enough to ask.
        static string NormalsProblem(LetteraSolidEntry entry, float depth = 0.3f)
        {
            float bevels = entry.FrontHeight + entry.BackHeight;
            int bad = 0, checkedOnes = 0;
            for (int t = 0; t + 2 < entry.Indices.Length; t += 3)
            {
                LetteraSolidVertex a = entry.Vertices[entry.Indices[t]], b = entry.Vertices[entry.Indices[t + 1]], c = entry.Vertices[entry.Indices[t + 2]];
                float3 n = math.cross(Final(b, depth, bevels) - Final(a, depth, bevels), Final(c, depth, bevels) - Final(a, depth, bevels));
                if (math.lengthsq(n) < 1e-14f) continue;
                checkedOnes++;
                float3 shade = a.Normal + b.Normal + c.Normal;
                if (math.dot(n, shade) <= 0) bad++;
            }
            return bad == 0 ? null : bad + " of " + checkedOnes + " triangles face away from their normals";
        }

        [TearDown]
        public void ClearCache() => LetteraSolidCache.Clear();

        [Test]
        public void ASquareIsABoxOfTheRightVolume()
        {
            using (var scope = new EntryScope(Build(Square(0, 0, 1), Plain)))
            {
                Assert.IsNull(ClosedProblem(scope.Entry));
                Assert.AreEqual(0.3f, Volume(scope.Entry, 0.3f), 0.0015f);
                Assert.IsNull(NormalsProblem(scope.Entry));
                Assert.AreEqual(1, SurfaceArea(scope.Entry, LetteraSolidSurface.Face), 1e-3f);
                Assert.AreEqual(1, SurfaceArea(scope.Entry, LetteraSolidSurface.Back), 1e-3f);
            }
        }

        [Test]
        public void TheFaceIsWoundTowardTheViewerOnMinusZ()
        {
            using (var scope = new EntryScope(Build(Square(0, 0, 1), Plain)))
            {
                LetteraSolidEntry e = scope.Entry;
                int s = e.SurfaceStarts[(int)LetteraSolidSurface.Face];
                float3 a = e.Vertices[e.Indices[s]].Position, b = e.Vertices[e.Indices[s + 1]].Position, c = e.Vertices[e.Indices[s + 2]].Position;
                Assert.Less(math.cross(b - a, c - a).z, 0, "Unity's outward normal, cross(b - a, c - a), points at the viewer");
                Assert.AreEqual(-1, e.Vertices[e.Indices[s]].Normal.z, 1e-4f);
            }
        }

        [Test]
        public void AClockwiseOutlineComesOutTheSame()
        {
            using (var ccw = new EntryScope(Build(Square(0, 0, 1), Plain)))
            using (var cw = new EntryScope(Build(Square(0, 0, 1, false), Plain)))
            {
                Assert.AreEqual(Volume(ccw.Entry, 0.3f), Volume(cw.Entry, 0.3f), 1e-4f);
                Assert.Greater(Volume(cw.Entry, 0.3f), 0);
            }
        }

        [Test]
        public void AHoleStaysAHoleWhicheverWayFontsWindIt()
        {
            float[] trueType = Join(Square(0, 0, 4, false), Square(1, 1, 2, true));
            float[] cff = Join(Square(0, 0, 4, true), Square(1, 1, 2, false));
            foreach (float[] commands in new[] { trueType, cff })
                using (var scope = new EntryScope(Build(commands, Plain)))
                {
                    Assert.IsNull(ClosedProblem(scope.Entry));
                    Assert.AreEqual(12 * 0.5f, Volume(scope.Entry, 0.5f), 0.03f);
                    Assert.AreEqual(12, SurfaceArea(scope.Entry, LetteraSolidSurface.Face), 0.012f);
                }
        }

        [Test]
        public void OverlappingSquaresBecomeOneOutline()
        {
            float[] commands = Join(Square(0, 0, 2), Square(1, 1, 2));
            using (var scope = new EntryScope(Build(commands, Plain)))
            {
                Assert.IsNull(ClosedProblem(scope.Entry));
                Assert.AreEqual(7 * 0.5f, Volume(scope.Entry, 0.5f), 0.02f, "two 4s minus their shared 1");
                Assert.AreEqual(7, SurfaceArea(scope.Entry, LetteraSolidSurface.Face), 0.01f);
            }
        }

        [Test]
        public void AContourInsideOneOfTheSameWayAddsNothing()
        {
            float[] commands = Join(Square(0, 0, 4), Square(1, 1, 2));
            using (var scope = new EntryScope(Build(commands, Plain)))
            {
                Assert.IsNull(ClosedProblem(scope.Entry));
                Assert.AreEqual(16 * 0.5f, Volume(scope.Entry, 0.5f), 0.04f);
            }
        }

        [Test]
        public void ABowTieIsRightOrFailsAndNeverThrows()
        {
            float[] commands = { 0, 0, 0, 1, 2, 2, 1, 2, 0, 1, 0, 2, 4 };
            using (var scope = new EntryScope(Build(commands, Plain)))
            {
                if (scope.Entry.Failed) return;
                Assert.IsNull(ClosedProblem(scope.Entry));
                Assert.Greater(Volume(scope.Entry, 0.5f), 0);
            }
        }

        [Test]
        public void DepthZeroIsFlatWithNoNaN()
        {
            using (var scope = new EntryScope(Build(Square(0, 0, 1), WithEdge(LetteraBevelShape.Round))))
            {
                float bevels = scope.Entry.FrontHeight + scope.Entry.BackHeight;
                foreach (LetteraSolidVertex v in scope.Entry.Vertices)
                {
                    Assert.IsFalse(math.any(math.isnan(v.Position)));
                    Assert.IsFalse(math.any(math.isnan(v.Normal)));
                }
            }
        }

        static readonly LetteraBevelShape[] Shapes = { LetteraBevelShape.Chamfer, LetteraBevelShape.Round, LetteraBevelShape.Cove, LetteraBevelShape.Bead, LetteraBevelShape.Step, LetteraBevelShape.TwoSteps, LetteraBevelShape.Ogee, LetteraBevelShape.Engraved };

        [Test]
        public void EveryEdgeShapeStaysClosedAndOutward([ValueSource(nameof(Shapes))] LetteraBevelShape shape)
        {
            float[] ring = Join(Square(0, 0, 1), Square(0.25f, 0.25f, 0.5f, false));
            using (var scope = new EntryScope(Build(ring, WithEdge(shape, 0.05f, 0.05f))))
            {
                Assert.IsFalse(scope.Entry.Failed);
                Assert.IsNull(ClosedProblem(scope.Entry), shape.ToString());
                Assert.Greater(Volume(scope.Entry, 0.4f), 0, shape.ToString());
                Assert.IsNull(NormalsProblem(scope.Entry), shape.ToString());
            }
        }

        [Test]
        public void ProfilesStartAndEndWhereTheySay([ValueSource(nameof(Shapes))] LetteraBevelShape shape)
        {
            var points = new Unity.Collections.NativeList<float2>(16, Unity.Collections.Allocator.Temp);
            LetteraBevelProfile.Fill(shape, 0.5f, 4, default, ref points);
            Assert.GreaterOrEqual(points.Length, 2);
            Assert.AreEqual(new float2(0, 0), points[0]);
            if (shape != LetteraBevelShape.Engraved)
            {
                Assert.AreEqual(1, points[points.Length - 1].x, 1e-4f);
                Assert.AreEqual(1, points[points.Length - 1].y, 1e-4f);
            }
        }

        [Test]
        public void AHostileCurveComesOutInRange()
        {
            var curve = new AnimationCurve(new Keyframe(0, float.NaN), new Keyframe(0.5f, 1000), new Keyframe(0.2f, -1000));
            var points = new Unity.Collections.NativeList<float2>(16, Unity.Collections.Allocator.Temp);
            LetteraBevelProfile.Fill(LetteraBevelShape.Custom, 0.5f, 4, new AnimationCurveSampler(curve), ref points);
            for (int i = 0; i < points.Length; i++)
            {
                Assert.IsFalse(math.any(math.isnan(points[i])));
                Assert.IsTrue(points[i].y >= 0 && points[i].y <= 1);
            }
        }

        [Test]
        public void AWideEdgeOnAThinStrokeNarrowsAndStaysClosed()
        {
            float[] thin = { 0, 0, 0, 1, 2, 0, 1, 2, 0.1f, 1, 0, 0.1f, 4 };
            int clamped = LetteraSolidCache.BevelsClamped;
            using (var scope = new EntryScope(Build(thin, WithEdge(LetteraBevelShape.Chamfer, 0.5f, 0.05f))))
            {
                Assert.IsNull(ClosedProblem(scope.Entry));
                Assert.Greater(Volume(scope.Entry, 0.3f), 0);
                Assert.Greater(LetteraSolidCache.BevelsClamped, clamped, "a counter says it was clamped");
            }
        }

        [Test]
        public void TheOutlineAloneDecidesTheFrontSilhouette()
        {
            using (var plain = new EntryScope(Build(Square(0, 0, 1), Plain)))
            using (var edged = new EntryScope(Build(Square(0, 0, 1), WithEdge(LetteraBevelShape.Round, 0.1f, 0.1f))))
            {
                float3 BoundsOf(LetteraSolidEntry e)
                {
                    float3 max = new float3(float.MinValue);
                    foreach (LetteraSolidVertex v in e.Vertices) max = math.max(max, v.Position);
                    return max;
                }
                Assert.AreEqual(BoundsOf(plain.Entry).x, BoundsOf(edged.Entry).x, 1e-5f, "bevels only go inward");
                Assert.AreEqual(BoundsOf(plain.Entry).y, BoundsOf(edged.Entry).y, 1e-5f);
            }
        }

        [Test]
        public void ARoundEdgesNormalsFollowACircle()
        {
            using (var scope = new EntryScope(Build(Square(0, 0, 4), WithEdge(LetteraBevelShape.Round, 0.5f, 0.5f, 16))))
            {
                LetteraSolidEntry e = scope.Entry;
                int checks = 0;
                for (int t = e.SurfaceStarts[(int)LetteraSolidSurface.Bevel]; t < e.SurfaceStarts[(int)LetteraSolidSurface.Bevel + 1]; t++)
                {
                    LetteraSolidVertex v = e.Vertices[e.Indices[t]];
                    // On the side x = 0 of the square: inset from the side, height toward the face.
                    if (v.Position.y < 1 || v.Position.y > 3 || v.Position.x >= 0.5f) continue;
                    float inward = v.Position.x, z = v.Position.z;
                    // The circle's centre is at (0.5, 0.5): inset 0.5 in, and height 0.5 from the face.
                    float2 fromCentre = math.normalize(new float2(inward - 0.5f, z - 0.5f));
                    float2 normal = math.normalize(new float2(v.Normal.x, v.Normal.z));
                    Assert.Less(math.degrees(math.acos(math.saturate(math.dot(fromCentre, normal)))), 6f);
                    checks++;
                }
                Assert.Greater(checks, 8);
            }
        }

        [Test]
        public void TheSameInputGivesTheSameBytes()
        {
            float[] commands = Join(Square(0, 0, 2), Square(1, 1, 2), new float[] { 0, 5, 0, 2, 6, 3, 7, 0, 4 });
            using (var a = new EntryScope(Build(commands, WithEdge(LetteraBevelShape.Round))))
            using (var b = new EntryScope(Build(commands, WithEdge(LetteraBevelShape.Round))))
            {
                Assert.AreEqual(a.Entry.Vertices.Length, b.Entry.Vertices.Length);
                for (int i = 0; i < a.Entry.Vertices.Length; i++)
                {
                    Assert.AreEqual(a.Entry.Vertices[i].Position, b.Entry.Vertices[i].Position);
                    Assert.AreEqual(a.Entry.Vertices[i].Normal, b.Entry.Vertices[i].Normal);
                }
                CollectionAssert.AreEqual(a.Entry.Indices.ToArray(), b.Entry.Indices.ToArray());
            }
        }

        [Test]
        public void ToleranceTradesPointsForCloseness()
        {
            float[] curve = { 0, 0, 0, 2, 1, 2, 2, 0, 1, 2, -1, 4 };
            int Vertices(float tolerance)
            {
                LetteraSolidRecipe recipe = Plain;
                recipe.Tolerance = tolerance;
                using (var scope = new EntryScope(Build(curve, recipe))) return scope.Entry.Vertices.Length;
            }
            Assert.Greater(Vertices(0.0005f), Vertices(0.001f));
            Assert.Greater(Vertices(0.001f), Vertices(0.004f));
        }

        [Test]
        public void AnOpenSolidHasOnlyWhatWasAskedFor()
        {
            LetteraSolidRecipe recipe = Plain;
            recipe.Faces = LetteraSolidFaces.Sides;
            using (var scope = new EntryScope(Build(Square(0, 0, 1), recipe)))
            {
                Assert.AreEqual(0, SurfaceArea(scope.Entry, LetteraSolidSurface.Face));
                Assert.AreEqual(0, SurfaceArea(scope.Entry, LetteraSolidSurface.Back));
                Assert.Greater(scope.Entry.Indices.Length, 0);
            }
        }

        [Test]
        public void RandomStreamsNeverThrowOrHang()
        {
            var random = new Unity.Mathematics.Random(12345);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int run = 0; run < 20000; run++)
            {
                int length = random.NextInt(0, 64);
                var commands = new float[length];
                for (int i = 0; i < length; i++)
                {
                    int kind = random.NextInt(0, 12);
                    commands[i] = kind == 0 ? float.NaN : kind == 1 ? float.PositiveInfinity : kind == 2 ? random.NextInt(-1, 6) : random.NextFloat(-3, 3);
                }
                LetteraSolidEntry entry = Build(commands, run % 2 == 0 ? Plain : WithEdge(LetteraBevelShape.Round, 0.1f, 0.1f));
                entry.Release();
                if (watch.Elapsed.TotalSeconds > 90) Assert.Fail("Over the time cap after " + run);
            }
            Debug.Log($"20 000 random streams in {watch.Elapsed.TotalSeconds:0.0} s");
        }

        sealed class EntryScope : IDisposable
        {
            public readonly LetteraSolidEntry Entry;

            public EntryScope(LetteraSolidEntry entry) => Entry = entry;
            public void Dispose() => Entry.Release();
        }
    }

    /// <summary>The builder over whole fonts.</summary>
    public class SolidFontTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        static readonly string[] Fonts = { "NotoSans-Regular.ttf", "NotoSansArabic-Regular.ttf", "NotoSansDevanagari-Regular.ttf" };

        [Test]
        public unsafe void EveryGlyphOfTheScriptsIsClosed([ValueSource(nameof(Fonts))] string file)
        {
            FontFace face = Font(file).LoadFace();
            int total = LetteraNative.lt_face_collect_unicodes(face.Face, null, 0);
            Assert.Greater(total, 0);
            var codes = new uint[total];
            fixed (uint* p = codes) LetteraNative.lt_face_collect_unicodes(face.Face, p, total);
            LetteraBevel bevel = LetteraBevel.Default;
            var recipe = new LetteraSolidRecipe { Front = bevel, Back = bevel, Tolerance = 0.001f, SmoothingCos = 0.866f, Faces = LetteraSolidFaces.All };
            int glyphs = 0, failed = 0, open = 0, vertices = 0, step = Mathf.Max(1, total / 400);
            var problems = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var seen = new HashSet<uint>();
            for (int i = 0; i < total; i += step)
            {
                uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, codes[i], 0);
                if (glyph == 0 || !seen.Add(glyph)) continue;
                LetteraSolidEntry entry = LetteraSolidCache.Acquire(face.Regular, glyph, recipe);
                LetteraSolidCache.Release(entry);
                if (entry.Failed)
                {
                    failed++;
                    problems.Add("U+" + codes[i].ToString("X4") + " failed");
                    continue;
                }
                if (entry.Vertices.Length == 0) continue;
                glyphs++;
                vertices += entry.Vertices.Length;
                string problem = SolidGeometry.ClosedProblem(entry);
                if (problem != null)
                {
                    open++;
                    if (problems.Count < 12) problems.Add("U+" + codes[i].ToString("X4") + ": " + problem);
                }
            }
            Debug.Log($"{file}: {glyphs} glyphs, {vertices / Mathf.Max(glyphs, 1)} vertices each, {watch.Elapsed.TotalMilliseconds / Mathf.Max(glyphs, 1):0.00} ms each, clamped {LetteraSolidCache.BevelsClamped}, caps dropped {LetteraSolidCache.CapsDropped}");
            LetteraSolidCache.Clear();
            Assert.AreEqual(0, failed + open, string.Join("; ", problems));
            Assert.Greater(glyphs, 20);
        }
    }

    internal static class SolidGeometry
    {
        internal static string ClosedProblem(LetteraSolidEntry entry, float depth = 0.3f)
        {
            float bevels = entry.FrontHeight + entry.BackHeight;
            var ids = new Dictionary<int3, int>();
            int IdOf(int index)
            {
                LetteraSolidVertex v = entry.Vertices[index];
                float3 p = new float3(v.Position.xy, v.Position.z + v.Weight * (depth - bevels));
                int3 q = (int3)math.round(p * 20000f);
                if (!ids.TryGetValue(q, out int id))
                {
                    id = ids.Count;
                    ids[q] = id;
                }
                return id;
            }
            var edges = new Dictionary<long, int>();
            void Note(int a, int b)
            {
                if (a == b) return;
                long key = (long)a << 32 | (uint)b;
                edges.TryGetValue(key, out int count);
                edges[key] = count + 1;
            }
            for (int t = 0; t + 2 < entry.Indices.Length; t += 3)
            {
                int a = IdOf(entry.Indices[t]), b = IdOf(entry.Indices[t + 1]), c = IdOf(entry.Indices[t + 2]);
                Note(a, b);
                Note(b, c);
                Note(c, a);
            }
            int bad = 0;
            foreach (KeyValuePair<long, int> edge in edges)
            {
                long opposite = (edge.Key & 0xFFFFFFFFL) << 32 | (uint)(edge.Key >> 32);
                edges.TryGetValue(opposite, out int back);
                if (back != edge.Value) bad++;
            }
            return bad == 0 ? null : bad + " of " + edges.Count + " edges unmatched";
        }
    }
}
