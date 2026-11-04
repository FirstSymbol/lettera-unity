using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>What a range takes for itself (materials by surface, a fixed pose), motion and reveals in depth, and the looks that ship.</summary>
    public class SolidPoseMotionTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<Object> m_Made = new List<Object>();

        LetteraText3D Solid(string text, float size = 20, System.Action<LetteraText3D> before = null)
        {
            var go = new GameObject("Solid", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 500);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = Latin;
            solid.FontSize = size;
            solid.WordWrap = false;
            solid.ShareMesh = false;
            before?.Invoke(solid);
            solid.Text = text;
            solid.ForceRebuild();
            m_Made.Add(go);
            return solid;
        }

        Material Colored(Color color)
        {
            var material = new Material(Shader.Find("Lettera/Solid Lit")) { name = "Test " + color };
            material.SetColor("_BaseColor", color);
            m_Made.Add(material);
            return material;
        }

        [TearDown]
        public void Clean()
        {
            foreach (Object o in m_Made)
                if (o != null)
                    Object.DestroyImmediate(o);
            m_Made.Clear();
        }

        static LetteraSolidMeshBuilder.Placement GlyphOf(LetteraText3D text, int cluster)
        {
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed)
                if (place.Cluster == cluster)
                    return place;
            Assert.Fail("no glyph at " + cluster);
            return default;
        }

        static Bounds BoundsOf(Vector3[] vertices, LetteraSolidMeshBuilder.Placement place)
        {
            var bounds = new Bounds(vertices[place.VertexStart], Vector3.zero);
            for (int i = place.VertexStart; i < place.VertexStart + place.VertexCount; i++) bounds.Encapsulate(vertices[i]);
            return bounds;
        }

        static Bounds GlyphBounds(LetteraText3D text, int cluster) => BoundsOf(text.CurrentMesh.vertices, GlyphOf(text, cluster));

        static void NormalsWhole(Mesh mesh)
        {
            foreach (Vector3 n in mesh.normals)
            {
                Assert.IsFalse(float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z));
                Assert.AreEqual(1, n.magnitude, 1e-3f);
            }
        }

        // ------------------------------------------------------------------ D3: materials by surface

        [Test]
        public void ARangeTakesASetsMaterialsSurfaceBySurface()
        {
            Material red = Colored(Color.red), blue = Colored(Color.blue);
            LetteraText3D all = Solid("a<surface=paint>b</surface>c", before: t => t.AddSurfaceSet(new LetteraSolidSurfaceSet("Paint", red) { Side = blue }));
            Assert.AreEqual("abc", all.Engine.ShownTextForTests());
            Material[] materials = all.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.AreEqual(3, all.CurrentMesh.subMeshCount, "the text's own, the set's face and the set's side");
            CollectionAssert.Contains(materials, red);
            CollectionAssert.Contains(materials, blue);
            LetteraSolidMeshBuilder.Placement b = GlyphOf(all, 1);
            int[] starts = b.Entry.SurfaceStarts;
            int side = starts[(int)LetteraSolidSurface.Side + 1] - starts[(int)LetteraSolidSurface.Side];
            Assert.AreEqual(side, (int)all.CurrentMesh.GetIndexCount(System.Array.IndexOf(materials, blue)), "only b's sides are blue");
            Assert.AreEqual(b.Entry.Indices.Length - side, (int)all.CurrentMesh.GetIndexCount(System.Array.IndexOf(materials, red)), "the rest of b falls back to the set's face");

            LetteraText3D face = Solid("a<surface=paint,face>b</surface>c", before: t => t.AddSurfaceSet(new LetteraSolidSurfaceSet("paint", red)));
            materials = face.GetComponent<MeshRenderer>().sharedMaterials;
            Assert.AreEqual(2, face.CurrentMesh.subMeshCount);
            starts = GlyphOf(face, 1).Entry.SurfaceStarts;
            Assert.AreEqual(starts[1] - starts[0], (int)face.CurrentMesh.GetIndexCount(System.Array.IndexOf(materials, red)), "only b's face is red");
            Assert.AreEqual(red, face.GetRangeMaterial("PAINT", LetteraSolidSurface.Side), "a set falls back within itself");
        }

        [Test]
        public void AnUnknownSetKeepsTheTextsMaterialsAndFlatTextIgnoresTheTags()
        {
            LetteraText3D text = Solid("a<surface=nothing>b</surface>c");
            Assert.AreEqual(1, text.CurrentMesh.subMeshCount);
            LetteraWorldText flat = Make("a<surface=x>b</surface><pose=0,30>c</pose><revolve>d</revolve>");
            flat.ForceRebuild();
            string shown = flat.Engine.ShownTextForTests();
            StringAssert.Contains("<surface", shown);
            StringAssert.Contains("<pose", shown);
            StringAssert.Contains("<revolve", shown, "flat text has no such tags");
        }

        // ------------------------------------------------------------------ D3: a fixed pose

        [Test]
        public void ARangeTurnsLiftsAndScalesWhereItRests()
        {
            LetteraText3D plain = Solid("abc");
            Bounds rest = GlyphBounds(plain, 1);

            LetteraText3D yawed = Solid("a<pose=0,90>b</pose>c");
            Bounds turned = GlyphBounds(yawed, 1);
            Assert.AreEqual(rest.size.z, turned.size.x, 0.05f, "a quarter turn about Y puts the depth across");
            Assert.AreEqual(rest.size.x, turned.size.z, 0.05f);
            Assert.AreEqual(rest.center.x, turned.center.x, 0.05f, "about its own middle");
            Assert.AreEqual(GlyphBounds(plain, 0).center, GlyphBounds(yawed, 0).center, "the rest of the text stays");

            LetteraText3D lifted = Solid("a<pose=0,0,0,1>b</pose>c");
            Assert.AreEqual(rest.center.z - 20, GlyphBounds(lifted, 1).center.z, 0.01f, "one em towards the viewer");

            LetteraText3D scaled = Solid("a<pose=0,0,0,0,2>b</pose>c");
            Assert.AreEqual(rest.size.x * 2, GlyphBounds(scaled, 1).size.x, 0.05f);
            Assert.AreEqual(rest.size.z * 2, GlyphBounds(scaled, 1).size.z, 0.05f);

            LetteraText3D rolled = Solid("a<pose=0,0,180>b</pose>c");
            Assert.AreEqual(rest.size.x, GlyphBounds(rolled, 1).size.x, 0.05f);
            Assert.AreEqual(rest.center.y, GlyphBounds(rolled, 1).center.y, 0.05f, "a half turn in the plane, about its middle");
            NormalsWhole(rolled.CurrentMesh);
            NormalsWhole(yawed.CurrentMesh);
        }

        [Test]
        public void AWholeRangeTurnsAsOneBody()
        {
            LetteraText3D plain = Solid("ab");
            LetteraText3D whole = Solid("<pose=0,0,90,0,1,whole>ab</pose>");
            LetteraText3D each = Solid("<pose=0,0,90>ab</pose>");
            Bounds a = GlyphBounds(whole, 0), b = GlyphBounds(whole, 1);
            Assert.AreEqual(a.center.x, b.center.x, 4f, "a quarter turn of the pair stands one glyph over the other");
            Assert.Greater(Mathf.Abs(a.center.y - b.center.y), 5);
            Assert.AreEqual(GlyphBounds(plain, 0).center.y, GlyphBounds(each, 0).center.y, 0.5f, "each about its own middle stays in its line");
        }

        [Test]
        public void APoseIsPartOfWhatSharesAMesh()
        {
            LetteraText3D a = Solid("ab", before: t => t.ShareMesh = true), b = Solid("a<pose=0,30>b</pose>", before: t => t.ShareMesh = true), c = Solid("a<pose=0,30>b</pose>", before: t => t.ShareMesh = true);
            Assert.AreNotEqual(a.CurrentMesh, b.CurrentMesh);
            Assert.AreEqual(b.CurrentMesh, c.CurrentMesh);
        }

        [Test]
        public void APoseMotionAndADeformerWorkTogether()
        {
            LetteraText3D text = Solid("<pose=20,30><wave>abc</wave></pose>");
            text.AddDeformer(LetteraDeformer.Bend(90));
            text.ForceRebuild();
            Vector3[] rest = text.CurrentMesh.vertices;
            text.Engine.TickPose(0.3f, 0.3f);
            Vector3[] moved = text.CurrentMesh.vertices;
            float furthest = 0;
            for (int i = 0; i < rest.Length; i++) furthest = Mathf.Max(furthest, Vector3.Distance(rest[i], moved[i]));
            Assert.Greater(furthest, 0.01f);
            NormalsWhole(text.CurrentMesh);
        }

        // ------------------------------------------------------------------ D4: motion in depth

        [TestCase("revolve")]
        [TestCase("swing")]
        [TestCase("surge")]
        [TestCase("orbit")]
        [TestCase("tumble")]
        public void EachDepthMotionMovesGlyphsInDepthWithoutARebuild(string tag)
        {
            LetteraText3D plain = Solid("abc");
            LetteraText3D text = Solid("<" + tag + ">abc</" + tag + ">");
            Assert.AreEqual("abc", text.Engine.ShownTextForTests());
            int builds = LetteraSolidCache.Builds, writes = LetteraSolidMeshBuilder.IndexWrites;
            float moved = 0;
            for (int frame = 0; frame < 6; frame++)
            {
                text.Engine.TickPose(0.37f, 0.37f);
                for (int c = 0; c < 3; c++)
                {
                    Bounds now = GlyphBounds(text, c), rest = GlyphBounds(plain, c);
                    moved = Mathf.Max(moved, Mathf.Abs(now.center.z - rest.center.z), Mathf.Abs(now.size.z - rest.size.z));
                }
            }
            Assert.Greater(moved, 0.5f, "something went into depth");
            Assert.AreEqual(builds, LetteraSolidCache.Builds);
            Assert.AreEqual(writes, LetteraSolidMeshBuilder.IndexWrites, "a pose writes vertices only");
            NormalsWhole(text.CurrentMesh);
            Assert.IsTrue(text.CurrentMesh.bounds.Contains(text.CurrentMesh.vertices[GlyphOf(text, 1).VertexStart]), "bounds follow what moves");
        }

        [Test]
        public void ARevolveAtAQuarterTurnStandsEdgeOn()
        {
            LetteraText3D plain = Solid("a");
            LetteraText3D text = Solid("<revolve=1,0>a</revolve>");
            text.Engine.TickPose(0.25f, 0.25f);
            Bounds rest = GlyphBounds(plain, 0), turned = GlyphBounds(text, 0);
            Assert.AreEqual(rest.size.z, turned.size.x, 0.1f);
            Assert.AreEqual(rest.size.x, turned.size.z, 0.1f);
        }

        [Test]
        public void ASurgeAtItsCrestComesOutByItsAmplitude()
        {
            LetteraText3D plain = Solid("a");
            LetteraText3D text = Solid("<surge=0.5,1,0>a</surge>");
            text.Engine.TickPose(0.25f, 0.25f);
            Assert.AreEqual(GlyphBounds(plain, 0).center.z - 0.5f * 20, GlyphBounds(text, 0).center.z, 0.05f);
        }

        [Test]
        public void SwingHangsFromTheTopEdge()
        {
            var swing = new SwingMotion();
            var box = new LetteraBodyBox { XMin = 0, YMin = 0, XMax = 10, YMax = 20 };
            LetteraSolidPose pose = swing.SolidPoseAt(new LetteraMotionValues { V0 = 30, V1 = 1, V2 = 0, V3 = 0 }, 0.25f, 0, 1, box, 20);
            Assert.AreEqual(20, pose.Pivot.y, 1e-4f);
            Assert.AreEqual(30, Quaternion.Angle(Quaternion.identity, pose.Rotation), 0.01f);
        }

        sealed class Rise : LetteraSolidMotion
        {
            public override void Resolve(in LetteraSlots slots, ref LetteraMotionValues values) => values.V0 = slots.Float(0, 1);
            public override void PoseSolid(in LetteraMotionContext context, ref LetteraSolidPose pose) => pose.Lift(context.Values.V0 * context.Em);
        }

        sealed class TurnSecond : ILetteraSolidPose
        {
            public Vector3 Centre;
            public void Pose(int glyph, Vector3 centre, ref Matrix4x4 transform)
            {
                if (glyph != 1) return;
                Centre = centre;
                transform = Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0));
            }
        }

        [Test]
        public void ACustomEffectAndAHookPoseGlyphsInDepth()
        {
            LetteraText3D plain = Solid("abc");
            LetteraText3D text = Solid("", before: t =>
            {
                t.Styles.Add(LetteraStyle.ForTag(new Rise(), "rise"));
                t.MarkStylesDirty();
            });
            text.Text = "a<rise=2>b</rise>c";
            text.ForceRebuild();
            text.Engine.TickPose(0.1f, 0.1f);
            Assert.AreEqual(GlyphBounds(plain, 1).center.z - 40, GlyphBounds(text, 1).center.z, 0.05f, "two em out");
            Assert.AreEqual(GlyphBounds(plain, 0).center.z, GlyphBounds(text, 0).center.z, 0.01f);

            var hook = new TurnSecond();
            LetteraText3D hooked = Solid("abc", before: t => t.ShareMesh = true);
            hooked.SolidPose = hook;
            hooked.ForceRebuild();
            Assert.AreEqual(0, LetteraSharedSolidMeshes.ReferencesOf(hooked.CurrentMesh), "a hooked text keeps its own mesh");
            Bounds rest = GlyphBounds(plain, 1), turned = GlyphBounds(hooked, 1);
            Assert.AreEqual(rest.size.z, turned.size.x, 0.1f);
            Assert.AreEqual(rest.center.x, hook.Centre.x, 0.1f, "the hook is given the glyph's middle");
            Assert.AreEqual(GlyphBounds(plain, 0).size.x, GlyphBounds(hooked, 0).size.x, 0.01f, "other glyphs as they were");
        }

        [Test]
        public void DepthMotionAllocatesNothingAFrame()
        {
            LetteraText3D text = Solid("<revolve>ab</revolve><surge>cd</surge><orbit>ef</orbit><tumble>gh</tumble><swing>ij</swing>");
            text.AddDeformer(LetteraDeformer.Bend(40));
            text.ForceRebuild();
            for (int i = 0; i < 3; i++) text.Engine.TickPose(0.02f, 0.02f);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) text.Engine.TickPose(0.02f, 0.02f);
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // ------------------------------------------------------------------ D5: reveal in depth

        static LetteraText3D Revealing(SolidPoseMotionTests fixture, string kind) => fixture.Solid("<reveal=" + kind + ">abc</reveal>");

        [Test]
        public void TurnInStartsEdgeOnAndEndsAtRest()
        {
            LetteraText3D plain = Solid("abc");
            LetteraText3D text = Revealing(this, "turnin");
            text.Engine.TickPose(0.005f, 0.005f);
            Bounds rest = GlyphBounds(plain, 0), early = GlyphBounds(text, 0);
            Assert.Less(early.size.x, rest.size.x * 0.6f, "nearly edge-on");
            Assert.Greater(early.size.z, rest.size.z * 2);
            Assert.AreEqual(255, text.CurrentMesh.colors32[GlyphOf(text, 0).VertexStart].a, "a solid turns in, it does not fade");
            text.Engine.TickPose(1, 1);
            AssertAtRest(plain, text);
        }

        [Test]
        public void FlyInComesFromTheViewer()
        {
            LetteraText3D plain = Solid("abc");
            LetteraText3D text = Revealing(this, "flyin");
            text.Engine.TickPose(0.05f, 0.05f);
            Assert.Less(GlyphBounds(text, 0).center.z, GlyphBounds(plain, 0).center.z - 20, "well in front of where it lands");
            Assert.Less(text.CurrentMesh.colors32[GlyphOf(text, 0).VertexStart].a, 255);
            text.Engine.TickPose(1, 1);
            AssertAtRest(plain, text);
        }

        [Test]
        public void DeepenGrowsTheBackOutOfTheFace()
        {
            LetteraText3D plain = Solid("abc");
            LetteraText3D text = Revealing(this, "deepen");
            text.Engine.TickPose(0.02f, 0.02f);
            Bounds rest = GlyphBounds(plain, 0), early = GlyphBounds(text, 0);
            Assert.Less(early.size.z, rest.size.z * 0.6f);
            Assert.AreEqual(rest.min.z, early.min.z, 0.01f, "the face stands still");
            Assert.AreEqual(rest.size.x, early.size.x, 0.01f);
            NormalsWhole(text.CurrentMesh);
            text.Engine.TickPose(1, 1);
            AssertAtRest(plain, text);
        }

        [Test]
        public void TumbleInFallsFromBehindTurning()
        {
            LetteraText3D plain = Solid("abc");
            LetteraText3D text = Revealing(this, "tumblein");
            text.Engine.TickPose(0.05f, 0.05f);
            Bounds rest = GlyphBounds(plain, 1), early = GlyphBounds(text, 1);
            Assert.Greater(early.center.z, rest.center.z + 5, "behind where it lands");
            Assert.Greater(early.center.y, rest.center.y + 1, "above");
            text.Engine.TickPose(1, 1);
            AssertAtRest(plain, text);
        }

        void AssertAtRest(LetteraText3D plain, LetteraText3D text)
        {
            for (int c = 0; c < 3; c++)
            {
                Bounds a = GlyphBounds(plain, c), b = GlyphBounds(text, c);
                Assert.AreEqual(a.center.x, b.center.x, 0.01f);
                Assert.AreEqual(a.center.z, b.center.z, 0.01f);
                Assert.AreEqual(a.size.z, b.size.z, 0.01f);
            }
        }

        [TestCase("turnin")]
        [TestCase("flyin")]
        [TestCase("deepen")]
        [TestCase("tumblein")]
        public void OnFlatTextADepthRevealIsItsFade(string kind)
        {
            LetteraWorldText depth = Make("<reveal=" + kind + ">abc</reveal>"), fade = Make("<reveal=fade>abc</reveal>");
            depth.ForceRebuild();
            fade.ForceRebuild();
            depth.Engine.TickPose(0.08f, 0.08f);
            fade.Engine.TickPose(0.08f, 0.08f);
            for (int g = 0; g < 3; g++)
            {
                LetteraGlyphPose a = depth.Engine.PoseOfGlyph(g), b = fade.Engine.PoseOfGlyph(g);
                Assert.AreEqual((b.A, b.B, b.C, b.D, b.Tx, b.Ty), (a.A, a.B, a.C, a.D, a.Tx, a.Ty), "glyph " + g + " stays where it is");
                Assert.AreEqual(b.Alpha, a.Alpha, 1e-5f, "and fades the same");
                Assert.Less(a.Alpha, 1);
            }
            for (int v = 0; v < depth.Engine.VertexCount; v++) Assert.AreEqual(fade.Engine.Colors[v].a, depth.Engine.Colors[v].a);
        }

        [Test]
        public void OnACanvasADepthRevealIsItsFade()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            m_Made.Add(canvas);
            LetteraText Label(string body)
            {
                var go = new GameObject("Label", typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(400, 40);
                var label = go.AddComponent<LetteraText>();
                label.Font = Latin;
                label.FontSize = 18;
                label.Text = body;
                label.ForceRebuild();
                return label;
            }
            LetteraText depth = Label("<reveal=turnin>abc</reveal>"), fade = Label("<reveal=fade>abc</reveal>");
            depth.Engine.TickPose(0.1f, 0.1f);
            fade.Engine.TickPose(0.1f, 0.1f);
            Assert.AreEqual(fade.Engine.VertexCount, depth.Engine.VertexCount);
            for (int v = 0; v < depth.Engine.VertexCount; v++)
            {
                Assert.AreEqual(fade.Engine.Vertices[v], depth.Engine.Vertices[v]);
                Assert.AreEqual(fade.Engine.Colors[v].a, depth.Engine.Colors[v].a);
            }
        }

        // ------------------------------------------------------------------ D13: looks that ship

        const string LooksFolder = "Packages/com.firstsymbol.lettera/Runtime/Text3D/Looks";

        [Test]
        public void SevenLooksShipReadyToApply()
        {
            var looks = new List<LetteraSolidLook>();
            foreach (string guid in AssetDatabase.FindAssets("t:LetteraSolidLook", new[] { LooksFolder }))
                looks.Add(AssetDatabase.LoadAssetAtPath<LetteraSolidLook>(AssetDatabase.GUIDToAssetPath(guid)));
            Assert.AreEqual(7, looks.Count);
            bool wholeTextLayer = false;
            foreach (LetteraSolidLook look in looks)
            {
                Assert.IsNotNull(look.FaceMaterial, look.name);
                Assert.AreEqual("Lettera/Solid Lit", look.FaceMaterial.shader.name, look.name);
                LetteraText3D text = Solid("Look");
                look.ApplyTo(text);
                text.ForceRebuild();
                Assert.Greater(text.Builder.VertexCount, 0, look.name);
                Assert.AreEqual(look.Layers.Count, text.Layers.Count, look.name);
                foreach (Material material in text.GetComponent<MeshRenderer>().sharedMaterials) Assert.IsNotNull(material, look.name);
                foreach (LetteraSolidLayer layer in look.Layers) wholeTextLayer |= layer.Scope == LetteraSolidLayerScope.Text;
            }
            Assert.IsTrue(wholeTextLayer, "a look can hold a layer around the whole text");
        }
    }
}
