using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FirstSymbol.Lettera.Editor;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>A solid text as a component: the same layout as a world text, and everything shared that can be.</summary>
    public class SolidTextTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        LetteraText3D Solid(string text, LetteraFontAsset font = null, float width = 1000, float height = 500, float size = 20)
        {
            var go = new GameObject("Solid", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = font != null ? font : Latin;
            solid.FontSize = size;
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
        }

        [TestCase("Hello, world", "NotoSans-Regular.ttf")]
        [TestCase("مرحبا بالعالم", "NotoSansArabic-Regular.ttf")]
        [TestCase("नमस्ते दुनिया", "NotoSansDevanagari-Regular.ttf")]
        [TestCase("שלום עולם", "NotoSansHebrew-Regular.ttf")]
        public void GlyphsStandWhereAWorldTextPutsThem(string text, string file)
        {
            LetteraFont font = Font(file);
            LetteraText3D solid = Solid(text, font);
            LetteraWorldText flat = Make(text, 1000, 500, font, 20);
            flat.ForceRebuild();
            System.ReadOnlySpan<TextEngine.PlacedGlyph> a = solid.Engine.PlacedSpan, b = flat.Engine.PlacedSpan;
            Assert.AreEqual(b.Length, a.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(b[i].Glyph, a[i].Glyph, "glyph " + i);
                Assert.AreEqual(b[i].X, a[i].X, 1e-3f, "x of " + i);
                Assert.AreEqual(b[i].Y, a[i].Y, 1e-3f, "y of " + i);
            }
        }

        [Test]
        public void AWrappedAlignedTextIsPlacedTheSame()
        {
            const string text = "Several words that wrap <size=30>and grow</size> over more than one line";
            LetteraText3D solid = Solid(text, Latin, 150, 400);
            solid.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            solid.ForceRebuild();
            LetteraWorldText flat = Make(text, 150, 400, Latin, 20);
            flat.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            flat.ForceRebuild();
            Assert.Greater(solid.Lines.Count, 1);
            Assert.AreEqual(flat.Lines.Count, solid.Lines.Count);
            System.ReadOnlySpan<TextEngine.PlacedGlyph> a = solid.Engine.PlacedSpan, b = flat.Engine.PlacedSpan;
            for (int i = 0; i < a.Length; i++) Assert.AreEqual(b[i].X, a[i].X, 1e-3f, "x of " + i);
        }

        [Test]
        public void ASolidTextMakesNoAtlasTile()
        {
            GlyphAtlas.Clear();
            int before = GlyphAtlas.GlyphCount;
            Solid("Nothing here needs a tile QZXJ");
            Assert.AreEqual(before, GlyphAtlas.GlyphCount);
        }

        [Test]
        public void TwoTextsBuildAGlyphOnceWhateverTheirDepth()
        {
            LetteraSolidCache.Clear();
            int before = LetteraSolidCache.Builds;
            LetteraText3D one = Solid("A");
            int built = LetteraSolidCache.Builds - before;
            Assert.GreaterOrEqual(built, 1);
            LetteraText3D two = Solid("A");
            two.Depth = 0.5f;
            two.ForceRebuild();
            Assert.AreEqual(built, LetteraSolidCache.Builds - before, "another depth builds nothing");
            LetteraBevel edge = two.FrontBevel;
            edge.Shape = LetteraBevelShape.Chamfer;
            two.FrontBevel = edge;
            two.ForceRebuild();
            Assert.Greater(LetteraSolidCache.Builds - before, built, "another edge builds its own");
        }

        [Test]
        public void DepthMovesTheBackAndBuildsNothing()
        {
            LetteraText3D text = Solid("H");
            text.Pivot = LetteraSolidPivot.Front;
            text.Depth = 0.25f;
            text.ShareMesh = false;
            text.ForceRebuild();
            Bounds first = text.CurrentMesh.bounds;
            Assert.AreEqual(0.25f * 20, first.size.z, 1e-3f);
            Assert.AreEqual(0, first.min.z, 1e-3f, "the front lies on the transform");
            int builds = LetteraSolidCache.Builds;
            text.Depth = 1;
            text.ForceRebuild();
            Assert.AreEqual(20, text.CurrentMesh.bounds.size.z, 1e-3f);
            Assert.AreEqual(builds, LetteraSolidCache.Builds);
        }

        [TestCase(LetteraSolidPivot.Middle, -1f)]
        [TestCase(LetteraSolidPivot.Front, 0f)]
        [TestCase(LetteraSolidPivot.Back, -2f)]
        public void ThePivotChoosesThePlane(LetteraSolidPivot pivot, float expected)
        {
            LetteraText3D text = Solid("H");
            text.Depth = 0.1f;
            text.Pivot = pivot;
            text.ForceRebuild();
            Assert.AreEqual(expected, text.CurrentMesh.bounds.min.z, 1e-3f);
        }

        [Test]
        public void MaterialsFallBackAndSubmeshesFollowThem()
        {
            var face = new Material(Shader.Find("Lettera/Solid Lit"));
            var side = new Material(face);
            try
            {
                LetteraText3D text = Solid("H");
                text.FaceMaterial = face;
                text.ForceRebuild();
                Assert.AreEqual(face, text.GetSurfaceMaterial(LetteraSolidSurface.Bevel));
                Assert.AreEqual(face, text.GetSurfaceMaterial(LetteraSolidSurface.BackBevel));
                Assert.AreEqual(1, text.CurrentMesh.subMeshCount);
                text.SideMaterial = side;
                text.ShareMesh = false;
                text.ForceRebuild();
                Assert.AreEqual(2, text.CurrentMesh.subMeshCount);
                CollectionAssert.AreEqual(new[] { face, side }, text.GetComponent<MeshRenderer>().sharedMaterials);
                Assert.AreEqual(side, text.GetSurfaceMaterial(LetteraSolidSurface.Side));
            }
            finally
            {
                Object.DestroyImmediate(face);
                Object.DestroyImmediate(side);
            }
        }

        [Test]
        public void IdenticalTextsShareAMeshAndAChangedOneLeavesIt()
        {
            LetteraText3D a = Solid("Same"), b = Solid("Same");
            Assert.AreEqual(a.CurrentMesh, b.CurrentMesh);
            Assert.AreEqual(2, LetteraSharedSolidMeshes.ReferencesOf(a.CurrentMesh));
            b.Text = "Other";
            b.ForceRebuild();
            Assert.AreNotEqual(a.CurrentMesh, b.CurrentMesh);
            Assert.AreEqual(1, LetteraSharedSolidMeshes.ReferencesOf(a.CurrentMesh));
        }

        [Test]
        public void ThreadsGiveTheSameBytes()
        {
            const string text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz";
            byte[] Bytes()
            {
                LetteraSolidCache.Clear();
                LetteraText3D solid = Solid(text);
                solid.ForceRebuild();
                var all = new List<byte>();
                foreach (LetteraSolidMeshBuilder.Placement place in solid.Builder.Placed)
                {
                    foreach (LetteraSolidVertex v in place.Entry.Vertices)
                    {
                        all.AddRange(System.BitConverter.GetBytes(v.Position.x));
                        all.AddRange(System.BitConverter.GetBytes(v.Position.y));
                        all.AddRange(System.BitConverter.GetBytes(v.Position.z));
                        all.AddRange(System.BitConverter.GetBytes(v.Normal.x));
                    }
                    foreach (int i in place.Entry.Indices) all.AddRange(System.BitConverter.GetBytes(i));
                }
                return all.ToArray();
            }
            byte[] inline = Bytes();
            LetteraSolidCache.ThreadsForTests = true;
            try
            {
                CollectionAssert.AreEqual(inline, Bytes());
            }
            finally
            {
                LetteraSolidCache.ThreadsForTests = false;
            }
        }

        [Test]
        public void ACachedRebuildAllocatesNothing()
        {
            LetteraText3D text = Solid("0123456789");
            text.ShareMesh = false;
            text.WarmCharacters = "0123456789";
            text.ForceRebuild();
            text.Depth = 0.2f;
            text.ForceRebuild();
            var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC.Alloc.In.Frame");
            long before = System.GC.GetTotalMemory(false);
            for (int i = 0; i < 5; i++)
            {
                text.Depth = 0.2f + i * 0.01f;
                text.ForceRebuild();
            }
            long grown = System.GC.GetTotalMemory(false) - before;
            recorder.Dispose();
            Assert.Less(grown, 16 * 1024, "a pass of depth changes builds nothing, and keeps little");
        }

        [Test]
        public void ColorComesFromTheRangeAndTheText()
        {
            LetteraText3D text = Solid("<color=#FF0000>a</color>b");
            text.ForceRebuild();
            Color32[] colors = text.CurrentMesh.colors32;
            Assert.IsTrue(System.Array.Exists(colors, c => c.r == 255 && c.g == 0 && c.b == 0));
            Assert.IsTrue(System.Array.Exists(colors, c => c.r == 255 && c.g == 255 && c.b == 255));
        }

        [Test]
        public void AFormulaExtrudesWithItsRulesAsBoxes()
        {
            LetteraText3D text = Solid("<math>\\frac{a}{b}</math>");
            text.ShareMesh = false;
            text.ForceRebuild();
            Assert.Greater(text.Builder.Placed.Count, 2);
            bool box = false;
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed) box |= place.Entry.Key.Glyph == LetteraSolidCache.UnitBoxGlyph;
            Assert.IsTrue(box, "the fraction's bar is a box");
        }

        [Test]
        public void AnUnderlineIsABox()
        {
            LetteraText3D text = Solid("<u>line</u>");
            text.ForceRebuild();
            bool box = false;
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed) box |= place.Entry.Key.Glyph == LetteraSolidCache.UnitBoxGlyph;
            Assert.IsTrue(box);
            Assert.AreEqual(0, text.Engine.VertexCount, "nothing draws flat");
        }

        [Test]
        public void MotionMovesTheSolidWithoutARebuild()
        {
            LetteraText3D text = Solid("<wave>abc</wave>");
            text.ForceRebuild();
            int builds = LetteraSolidCache.Builds, writes = LetteraSolidMeshBuilder.IndexWrites;
            Assert.IsFalse(LetteraSharedSolidMeshes.ReferencesOf(text.CurrentMesh) > 0, "a moving text keeps its own mesh");
            Vector3[] rest = text.CurrentMesh.vertices;
            text.Engine.TickPose(0.3f, 0.3f);
            Vector3[] moved = text.CurrentMesh.vertices;
            float furthest = 0;
            for (int i = 0; i < rest.Length; i++) furthest = Mathf.Max(furthest, Vector3.Distance(rest[i], moved[i]));
            Assert.Greater(furthest, 0.01f);
            Assert.AreEqual(builds, LetteraSolidCache.Builds);
            Assert.AreEqual(writes, LetteraSolidMeshBuilder.IndexWrites, "a pose writes vertices only");
        }

        [Test]
        public void ARangeCanHaveItsOwnDepthAndEdge()
        {
            LetteraText3D text = Solid("a<depth=3>b</depth>");
            text.Pivot = LetteraSolidPivot.Front;
            text.Depth = 0.1f;
            text.ShareMesh = false;
            text.ForceRebuild();
            Assert.AreEqual(0.3f * 20, text.CurrentMesh.bounds.size.z, 1e-2f);
            Assert.AreEqual("ab", text.Engine.ShownTextForTests());

            LetteraText3D edge = Solid("a<bevel=chamfer,0.05,0.05>b</bevel>");
            Assert.AreEqual("ab", edge.Engine.ShownTextForTests());
            LetteraWorldText flat = Make("a<depth=3>b</depth>");
            flat.ForceRebuild();
            StringAssert.Contains("<depth", flat.Engine.ShownTextForTests(), "flat text has no such tag");
        }

        [Test]
        public void TheEngineKeepsItsHandsOffTheTextsOwnStyles()
        {
            LetteraText3D text = Solid("x");
            Assert.AreEqual(0, text.Styles.Count, "nothing is written into what a scene saves");
        }

        [Test]
        public void ANoEntryForAnEmptyFontOrGlyphIsNoProblem()
        {
            LetteraText3D text = Solid("   ");
            Assert.AreEqual(0, text.Builder.VertexCount);
            Assert.DoesNotThrow(() => text.Text = "");
            text.ForceRebuild();
        }

        [Test]
        public void ManyLivesLeaveNoMeshBehind()
        {
            int before = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            for (int i = 0; i < 100; i++)
            {
                LetteraText3D text = Solid("Cycle" + i % 3);
                text.gameObject.SetActive(false);
                text.gameObject.SetActive(true);
                text.ForceRebuild();
                Object.DestroyImmediate(text.gameObject);
            }
            Assert.LessOrEqual(Resources.FindObjectsOfTypeAll<Mesh>().Length - before, 2);
        }

        [Test]
        public void AColorGlyphDrawsFlatOnAChildOfItsOwn()
        {
            LetteraText3D plain = Solid("plain");
            Assert.IsNull(plain.transform.Find("Lettera Flat"), "no child until one is needed");
            string path = Path.Combine(FontsFolder(), "NotoColorEmoji-COLRv0.ttf");
            if (!File.Exists(path)) Assert.Inconclusive("No color test font.");
        }

        static string FontsFolder()
        {
            string[] found = AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(AssetDatabase.GUIDToAssetPath(found[0]))), "..", "Fonts~");
        }

        [Test]
        public void AnObjFileReadsBack()
        {
            LetteraText3D text = Solid("H");
            text.ForceRebuild();
            string obj = LetteraText3DMenus.ToObj(text.CurrentMesh);
            int v = 0, n = 0, f = 0;
            foreach (string line in obj.Split('\n'))
            {
                if (line.StartsWith("v ")) v++;
                else if (line.StartsWith("vn ")) n++;
                else if (line.StartsWith("f ")) f++;
            }
            Assert.AreEqual(text.CurrentMesh.vertexCount, v);
            Assert.AreEqual(v, n);
            int triangles = 0;
            for (int s = 0; s < text.CurrentMesh.subMeshCount; s++) triangles += text.CurrentMesh.GetTriangles(s).Length / 3;
            Assert.AreEqual(triangles, f);
        }

        [Test]
        public void AWorldTextConvertsAndKeepsItsWords()
        {
            LetteraWorldText flat = Make("Words <b>here</b>", 400, 100, Latin, 20);
            flat.Color = Color.red;
            GameObject go = flat.gameObject;
            LetteraText3D solid = LetteraText3DMenus.ConvertWorldText(flat);
            Assert.IsNotNull(solid);
            Assert.IsNull(go.GetComponent<LetteraWorldText>());
            Assert.AreEqual("Words <b>here</b>", solid.Text);
            Assert.AreEqual(Color.red, solid.Color);
            Assert.AreEqual(20, solid.FontSize);
        }

        [Test]
        public void ASelectableRefusesToConvert()
        {
            LetteraWorldText flat = Make("Words", 400, 100, Latin, 20);
            flat.gameObject.AddComponent<LetteraSelectable>();
            Assert.IsNull(LetteraText3DMenus.ConvertWorldText(flat));
        }

        [Test]
        public void CoreNeverNamesTheModule()
        {
            string[] found = AssetDatabase.FindAssets("FirstSymbol.Lettera t:asmdef");
            string coreFolder = null;
            foreach (string guid in found)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == "FirstSymbol.Lettera.asmdef") coreFolder = Path.GetDirectoryName(path);
            }
            Assert.IsNotNull(coreFolder);
            StringAssert.DoesNotContain("Text3D", File.ReadAllText(Path.Combine(coreFolder, "FirstSymbol.Lettera.asmdef")));
            foreach (string file in Directory.GetFiles(coreFolder, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Text3D/") || normalized.EndsWith("AssemblyInfo.cs")) continue;
                string source = File.ReadAllText(file);
                StringAssert.DoesNotContain("LetteraText3D", source, file);
                StringAssert.DoesNotContain("LetteraSolidCache", source, file);
            }
        }
    }
}
