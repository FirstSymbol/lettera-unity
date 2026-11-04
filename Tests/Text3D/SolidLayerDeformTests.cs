using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>Extra solids around a text, and deformers over it: their shapes, their places, and that nothing they make is open.</summary>
    public class SolidLayerDeformTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        LetteraText3D Solid(string text, float size = 20, float width = 2000)
        {
            var go = new GameObject("Solid", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 500);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = Latin;
            solid.FontSize = size;
            solid.WordWrap = false;
            solid.ShareMesh = false;
            solid.Text = text;
            solid.ForceRebuild();
            m_Made.Add(go);
            return solid;
        }

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_Made)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Made.Clear();
            LetteraSolidCache.Clear();
        }

        static IEnumerable<LetteraSolidMeshBuilder.Placement> LayerPlacements(LetteraText3D text)
        {
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed)
                if (place.Map > 0)
                    yield return place;
        }

        static float FaceArea(LetteraSolidEntry entry)
        {
            float area = 0;
            for (int t = entry.SurfaceStarts[(int)LetteraSolidSurface.Face]; t + 2 < entry.SurfaceStarts[(int)LetteraSolidSurface.Face + 1]; t += 3)
            {
                float3 a = entry.Vertices[entry.Indices[t]].Position, b = entry.Vertices[entry.Indices[t + 1]].Position, c = entry.Vertices[entry.Indices[t + 2]].Position;
                area += math.length(math.cross(b - a, c - a)) * 0.5f;
            }
            return area;
        }

        static void Extent(LetteraSolidEntry entry, out float2 low, out float2 high)
        {
            low = new float2(float.MaxValue);
            high = new float2(float.MinValue);
            foreach (LetteraSolidVertex v in entry.Vertices)
            {
                low = math.min(low, v.Position.xy);
                high = math.max(high, v.Position.xy);
            }
        }

        // ------------------------------------------------------------------ layers

        [Test]
        public void AnOutlineLayerIsOneClosedSolidAWord()
        {
            LetteraText3D text = Solid("Hello there");
            int plain = text.Builder.VertexCount;
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Outline));
            text.ForceRebuild();
            Assert.AreEqual(2, text.Builder.GroupLayerCount, "one per word");
            Assert.Greater(text.Builder.VertexCount, plain);
            int found = 0;
            foreach (LetteraSolidMeshBuilder.Placement place in LayerPlacements(text))
            {
                found++;
                Assert.IsNull(SolidGeometry.ClosedProblem(place.Entry), "word " + found);
            }
            Assert.AreEqual(2, found);
        }

        [TestCase(LetteraSolidLayerScope.Line, 2)]
        [TestCase(LetteraSolidLayerScope.Text, 1)]
        public void ScopesGroupByLineOrText(LetteraSolidLayerScope scope, int groups)
        {
            LetteraText3D text = Solid("one two\nthree four");
            LetteraSolidLayer layer = LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Outline);
            layer.Scope = scope;
            text.AddLayer(layer);
            text.ForceRebuild();
            Assert.AreEqual(groups, text.Builder.GroupLayerCount);
        }

        [TestCase(0.1f)]
        [TestCase(-0.02f)]
        public void AnOutlineGrowsOrShrinksByItsOffset(float offset)
        {
            LetteraText3D text = Solid("O");
            var layer = new LetteraSolidLayer { Shape = LetteraSolidLayerShape.Outline, Scope = LetteraSolidLayerScope.Glyph, Offset = offset, Detail = 64 };
            layer.Bevel.Shape = LetteraBevelShape.None;
            text.AddLayer(layer);
            text.ForceRebuild();
            LetteraSolidEntry glyph = text.Builder.Placed[0].Entry, grown = null;
            foreach (LetteraSolidMeshBuilder.Placement place in LayerPlacements(text)) grown = place.Entry;
            Assert.IsNotNull(grown);
            Assert.IsNull(SolidGeometry.ClosedProblem(grown));
            Extent(glyph, out float2 low, out float2 high);
            Extent(grown, out float2 lowLayer, out float2 highLayer);
            float2 expected = high - low + 2 * offset;
            Assert.AreEqual(expected.x, highLayer.x - lowLayer.x, 0.02f, "width");
            Assert.AreEqual(expected.y, highLayer.y - lowLayer.y, 0.02f, "height");
        }

        [Test]
        public void APlateWithTheLettersCutThroughLosesTheirArea()
        {
            LetteraText3D text = Solid("Ab");
            var whole = new LetteraSolidLayer { Shape = LetteraSolidLayerShape.Plate, Scope = LetteraSolidLayerScope.Word, Offset = 0.1f, Radius = 0.05f, Detail = 64 };
            whole.Bevel.Shape = LetteraBevelShape.None;
            LetteraSolidLayer cut = whole.Clone();
            cut.Cut = LetteraSolidLayerCut.Through;
            text.AddLayer(whole);
            text.AddLayer(cut);
            text.ForceRebuild();
            float glyphs = 0;
            LetteraSolidEntry plate = null, holed = null;
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed)
            {
                if (place.Map == 0) glyphs += FaceArea(place.Entry);
                else if (place.Map == 1) plate = place.Entry;
                else holed = place.Entry;
            }
            Assert.IsNull(SolidGeometry.ClosedProblem(holed));
            Assert.AreEqual(FaceArea(plate) - glyphs, FaceArea(holed), glyphs * 0.08f, "the plate minus the letters");
        }

        [Test]
        public void AnEngravedPlateIsAHoledFrontOnAWholeBack()
        {
            LetteraText3D text = Solid("Ab");
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.EngravedPlate));
            text.ForceRebuild();
            var parts = new List<LetteraSolidMeshBuilder.Placement>(LayerPlacements(text));
            Assert.AreEqual(2, parts.Count);
            Assert.Less(parts[0].ZOffset, parts[1].ZOffset, "the holed part stands in front");
            Assert.Less(FaceArea(parts[0].Entry), FaceArea(parts[1].Entry));
        }

        [Test]
        public void ATubeIsAsDeepAsItIsWideAndClosed()
        {
            LetteraText3D text = Solid("O");
            text.Pivot = LetteraSolidPivot.Front;
            LetteraSolidLayer neon = LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Neon);
            text.AddLayer(neon);
            text.ForceRebuild();
            LetteraSolidMeshBuilder.Placement tube = default;
            foreach (LetteraSolidMeshBuilder.Placement place in LayerPlacements(text)) tube = place;
            Assert.IsNotNull(tube.Entry);
            Assert.IsNull(SolidGeometry.ClosedProblem(tube.Entry, 2 * neon.Radius));
            Assert.AreEqual(2 * neon.Radius, tube.Depth, 1e-5f);
            Assert.AreEqual(neon.Z * 20, tube.ZOffset, 1e-3f, "its front stands where asked");
        }

        [Test]
        public void EveryPresetBuilds([Values] LetteraSolidLayerPreset preset)
        {
            LetteraText3D text = Solid("Ab cd");
            text.AddLayer(LetteraSolidLayer.Preset(preset));
            text.ForceRebuild();
            int count = 0;
            foreach (LetteraSolidMeshBuilder.Placement place in LayerPlacements(text))
            {
                count++;
                Assert.Greater(place.Entry.Vertices.Length, 0);
            }
            Assert.Greater(count, 0);
        }

        [Test]
        public void ALayersMaterialIsItsOwnSubmesh()
        {
            var face = new Material(Shader.Find("Lettera/Solid Lit"));
            var own = new Material(face);
            try
            {
                LetteraText3D text = Solid("Ab");
                text.FaceMaterial = face;
                LetteraSolidLayer layer = LetteraSolidLayer.Preset(LetteraSolidLayerPreset.BackPlate);
                layer.Material = own;
                text.AddLayer(layer);
                text.ForceRebuild();
                Assert.AreEqual(2, text.CurrentMesh.subMeshCount);
                CollectionAssert.AreEqual(new[] { face, own }, text.GetComponent<MeshRenderer>().sharedMaterials);
                Assert.AreEqual(own, text.GetLayerMaterial(layer, LetteraSolidSurface.Side));
                layer.Material = null;
                text.RefreshSolids();
                text.ForceRebuild();
                Assert.AreEqual(1, text.CurrentMesh.subMeshCount, "an empty slot takes the text's");
            }
            finally
            {
                Object.DestroyImmediate(face);
                Object.DestroyImmediate(own);
            }
        }

        [Test]
        public void AGlyphsLayerMovesWithItsGlyph()
        {
            LetteraText3D text = Solid("<wave>ab</wave>");
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Slab));
            text.ForceRebuild();
            var glyphOf = new Dictionary<int, int>();
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed)
                if (place.Map == 0)
                    glyphOf[place.Cluster] = place.GlyphIndex;
            int layers = 0;
            foreach (LetteraSolidMeshBuilder.Placement place in LayerPlacements(text))
            {
                layers++;
                Assert.AreEqual(glyphOf[place.Cluster], place.GlyphIndex, "a layer takes its glyph's pose");
            }
            Assert.AreEqual(2, layers);
        }

        [Test]
        public void AWordsLayerIsKeptWhileItsGlyphsStay()
        {
            LetteraText3D text = Solid("Same words");
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Badge));
            text.ForceRebuild();
            int builds = LetteraSolidCache.Builds;
            text.Color = Color.red;
            text.Depth = 0.3f;
            text.ForceRebuild();
            Assert.AreEqual(builds, LetteraSolidCache.Builds, "colour and depth build nothing");
            text.Text = "Same other";
            text.ForceRebuild();
            Assert.AreEqual(builds + 1, LetteraSolidCache.Builds, "only the changed word");
        }

        [Test]
        public void ALookCarriesItsLayers()
        {
            var look = ScriptableObject.CreateInstance<LetteraSolidLook>();
            try
            {
                look.Layers.Add(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.BackPlate));
                LetteraText3D text = Solid("Ab");
                look.ApplyTo(text);
                Assert.AreEqual(1, text.Layers.Count);
                Assert.AreNotSame(look.Layers[0], text.Layers[0], "a copy, so the asset stays as it was");
            }
            finally
            {
                Object.DestroyImmediate(look);
            }
        }

        // ------------------------------------------------------------------ deformers

        static Vector3 Centre(Vector3[] vertices, LetteraSolidMeshBuilder.Placement place)
        {
            Vector3 sum = Vector3.zero;
            for (int i = place.VertexStart; i < place.VertexStart + place.VertexCount; i++) sum += vertices[i];
            return sum / Mathf.Max(place.VertexCount, 1);
        }

        [Test]
        public void CutFinerASolidStaysClosedAndNoEdgeIsLong()
        {
            var recipe = new LetteraSolidRecipe { Tolerance = 0.001f, SmoothingCos = 0.866f, Faces = LetteraSolidFaces.All, MaxEdge = 0.04f, Front = LetteraBevel.Default, Back = LetteraBevel.Default };
            LetteraText3D text = Solid("O");
            LetteraSolidEntry coarse = text.Builder.Placed[0].Entry;
            LetteraSolidEntry fine = LetteraSolidCache.Acquire(text.Engine.PlacedSpan[0].Instance, text.Engine.PlacedSpan[0].Glyph, recipe);
            Assert.Greater(fine.Indices.Length, coarse.Indices.Length);
            Assert.IsNull(SolidGeometry.ClosedProblem(fine));
            for (int t = 0; t + 2 < fine.Indices.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    float2 a = fine.Vertices[fine.Indices[t + e]].Position.xy, b = fine.Vertices[fine.Indices[t + (e + 1) % 3]].Position.xy;
                    Assert.LessOrEqual(math.distance(a, b), 0.04f + 1e-4f);
                }
            LetteraSolidCache.Release(fine);
        }

        [Test]
        public void ABendOf360ClosesARing()
        {
            LetteraText3D text = Solid("ABCDEFGHIJKL");
            text.FrontBevel = new LetteraBevel { Shape = LetteraBevelShape.None };
            text.AddDeformer(LetteraDeformer.Bend(360));
            text.ForceRebuild();
            Vector3[] vertices = text.CurrentMesh.vertices;
            Bounds bounds = text.CurrentMesh.bounds;
            // A ring stands as deep as it is wide.
            Assert.AreEqual(bounds.size.x, bounds.size.z, bounds.size.x * 0.12f);
            float width = bounds.size.x;
            Assert.Greater(width, 0);
            Vector3 first = Centre(vertices, text.Builder.Placed[0]), last = Centre(vertices, text.Builder.Placed[text.Builder.Placed.Count - 1]);
            Assert.Less(Vector3.Distance(first, last), width * 0.45f, "the ends come round to meet");
            foreach (Vector3 n in text.CurrentMesh.normals) Assert.AreEqual(1, n.magnitude, 1e-3f);
        }

        [Test]
        public void ABendKeepsItsLookWhenResized()
        {
            LetteraText3D small = Solid("Bend me", 20), large = Solid("Bend me", 40);
            small.AddDeformer(LetteraDeformer.Bend(120));
            large.AddDeformer(LetteraDeformer.Bend(120));
            small.ForceRebuild();
            large.ForceRebuild();
            Bounds a = small.CurrentMesh.bounds, b = large.CurrentMesh.bounds;
            Assert.AreEqual(a.size.z / a.size.x, b.size.z / b.size.x, 0.01f);
            Assert.AreEqual(2 * a.size.x, b.size.x, a.size.x * 0.02f);
        }

        [Test]
        public void ATwistOf180TurnsTheFarEndAround()
        {
            LetteraText3D text = Solid("IIIIIIII");
            LetteraDeformer twist = LetteraDeformer.Twist(180);
            twist.Center = 0;
            text.AddDeformer(twist);
            text.ForceRebuild();
            Vector3[] normals = text.CurrentMesh.normals;
            LetteraSolidMeshBuilder.Placement first = text.Builder.Placed[0], last = text.Builder.Placed[text.Builder.Placed.Count - 1];
            int faceFirst = first.Entry.Indices[first.Entry.SurfaceStarts[0]] + first.VertexStart;
            int faceLast = last.Entry.Indices[last.Entry.SurfaceStarts[0]] + last.VertexStart;
            Assert.Less(normals[faceFirst].z, -0.9f, "the start still faces the viewer");
            Assert.Greater(normals[faceLast].z, 0.6f, "the end faces away");
        }

        [Test]
        public void DeformersChainInOrder()
        {
            LetteraText3D a = Solid("Order"), b = Solid("Order");
            a.AddDeformer(LetteraDeformer.Bend(90));
            a.AddDeformer(LetteraDeformer.Twist(90));
            b.AddDeformer(LetteraDeformer.Twist(90));
            b.AddDeformer(LetteraDeformer.Bend(90));
            a.ForceRebuild();
            b.ForceRebuild();
            Assert.AreNotEqual(a.CurrentMesh.bounds.size, b.CurrentMesh.bounds.size);
        }

        [Test]
        public void ARangeTagDeformsOnlyItsRange()
        {
            LetteraText3D plain = Solid("ab cd"), tagged = Solid("<bend=90>ab</bend> cd");
            Assert.AreEqual("ab cd", tagged.Engine.ShownTextForTests());
            Vector3[] p = plain.CurrentMesh.vertices, t = tagged.CurrentMesh.vertices;
            var placed = tagged.Builder.Placed;
            bool moved = false;
            for (int k = 0; k < placed.Count; k++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[k];
                LetteraSolidMeshBuilder.Placement same = default;
                foreach (LetteraSolidMeshBuilder.Placement q in plain.Builder.Placed)
                    if (q.Cluster == place.Cluster)
                        same = q;
                Vector3 a = Centre(t, place), b = Centre(p, same);
                if (place.Cluster >= 3) Assert.Less(Vector3.Distance(a, b), 1e-3f, "outside the range nothing moves");
                else moved |= Vector3.Distance(a, b) > 0.05f;
            }
            Assert.IsTrue(moved);
        }

        [Test]
        public void AnUnderlineBendsWithTheText()
        {
            LetteraText3D text = Solid("<u>a long underline</u>");
            text.AddDeformer(LetteraDeformer.Bend(90, LetteraDeformAxis.Up));
            text.ForceRebuild();
            int pieces = 0;
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed) pieces += place.Entry.Key.Glyph == LetteraSolidCache.UnitBoxGlyph ? 1 : 0;
            Assert.Greater(pieces, 8, "a line goes in short pieces, so it curves");
        }

        [Test]
        public void APathCarriesTheTextRoundACorner()
        {
            LetteraText3D text = Solid("abcdefghij");
            text.AddDeformer(LetteraDeformer.Path(new Vector3(0, 0, 0), new Vector3(40, 0, 0), new Vector3(40, 0, 200)));
            text.ForceRebuild();
            Vector3[] vertices = text.CurrentMesh.vertices;
            Vector3 last = Centre(vertices, text.Builder.Placed[text.Builder.Placed.Count - 1]);
            Assert.Greater(last.z, 20, "past the corner the text runs into depth");
        }

        [Test]
        public void AWaveKeepsEveryNormalWhole()
        {
            LetteraText3D text = Solid("Waves");
            text.AddDeformer(LetteraDeformer.Wave(0.2f, 1));
            text.AddDeformer(LetteraDeformer.Taper(0.5f));
            text.AddDeformer(LetteraDeformer.Sphere(90));
            text.ForceRebuild();
            foreach (Vector3 n in text.CurrentMesh.normals)
            {
                Assert.IsFalse(float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z));
                Assert.AreEqual(1, n.magnitude, 1e-3f);
            }
            Assert.IsTrue(text.CurrentMesh.bounds.size.z > 0.15f * 20 + 1, "the wave and ball reach into depth");
        }

        [Test]
        public void ADeformedCachedRebuildAllocatesLittle()
        {
            LetteraText3D text = Solid("0123456789");
            text.AddDeformer(LetteraDeformer.Bend(60));
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Slab));
            text.ForceRebuild();
            text.Depth = 0.2f;
            text.ForceRebuild();
            long before = System.GC.GetTotalMemory(false);
            for (int i = 0; i < 5; i++)
            {
                text.Depth = 0.2f + i * 0.01f;
                text.ForceRebuild();
            }
            Assert.Less(System.GC.GetTotalMemory(false) - before, 16 * 1024);
        }

        [Test]
        public void MotionStillMovesADeformedText()
        {
            LetteraText3D text = Solid("<wave>abc</wave>");
            text.AddDeformer(LetteraDeformer.Bend(90));
            text.ForceRebuild();
            Vector3[] rest = text.CurrentMesh.vertices;
            text.Engine.TickPose(0.3f, 0.3f);
            Vector3[] moved = text.CurrentMesh.vertices;
            float furthest = 0;
            for (int i = 0; i < rest.Length; i++) furthest = Mathf.Max(furthest, Vector3.Distance(rest[i], moved[i]));
            Assert.Greater(furthest, 0.01f);
        }
    }
}
