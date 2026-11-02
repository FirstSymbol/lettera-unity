using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A GameObject going off keeps what was built; the component going off still gives it up.</summary>
    public class DeactivationTests : TextFixture
    {
        readonly List<GameObject> m_Roots = new List<GameObject>();

        [TearDown]
        public void DestroyRoots()
        {
            foreach (GameObject root in m_Roots)
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            m_Roots.Clear();
        }

        Canvas MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas));
            m_Roots.Add(go);
            return go.GetComponent<Canvas>();
        }

        LetteraText Label(Transform parent, string body = "Kept while away")
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(400, 40);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 18;
            text.Text = body;
            return text;
        }

        static int DrawnVertices(LetteraText text) => text.canvasRenderer.GetMesh() != null ? text.canvasRenderer.GetMesh().vertexCount : 0;

        LetteraText Built()
        {
            LetteraText text = Label(MakeCanvas().transform);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(DrawnVertices(text), 0);
            return text;
        }

        [Test]
        public void ComingBackShowsTheSameMeshWithNoRebuild()
        {
            LetteraText text = Built();
            int rebuilds = text.RebuildCount, vertices = DrawnVertices(text);
            Mesh mesh = text.MeshForTests;
            text.gameObject.SetActive(false);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(0, DrawnVertices(text), "nothing is drawn while away");
            Assert.IsTrue(text.MeshForTests != null, "it is kept");
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.AreEqual(vertices, DrawnVertices(text));
            Assert.AreSame(mesh, text.MeshForTests);
        }

        [Test]
        public void AParentGoingOffCountsTooAndKeepsLayout()
        {
            Canvas canvas = MakeCanvas();
            var group = new GameObject("Group", typeof(RectTransform));
            group.transform.SetParent(canvas.transform, false);
            LetteraText text = Label(group.transform);
            Canvas.ForceUpdateCanvases();
            int rebuilds = text.RebuildCount;
            float width = text.Lines[0].Width;
            group.SetActive(false);
            Assert.AreEqual(width, text.Lines[0].Width, "the layout is there to read while away");
            group.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.Greater(DrawnVertices(text), 0);
        }

        [Test]
        public void TextChangedWhileAwayShowsWhenBack()
        {
            LetteraText text = Built();
            int parses = text.ParseCount;
            text.gameObject.SetActive(false);
            text.Text = "New words here";
            Assert.AreEqual(parses, text.ParseCount, "nothing runs while away");
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(parses + 1, text.ParseCount);
            Assert.AreEqual("New words here", text.CleanText);
            Assert.Greater(DrawnVertices(text), 0);
        }

        [Test]
        public void FontChangedWhileAwayShapesAgainButDoesNotParse()
        {
            LetteraText text = Built();
            int parses = text.ParseCount, shapes = text.ShapeCount;
            text.gameObject.SetActive(false);
            text.Font = Arabic;
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(parses, text.ParseCount);
            Assert.AreEqual(shapes + 1, text.ShapeCount);
        }

        [Test]
        public void SharedFontTouchedWhileAwayIsHeard()
        {
            LetteraText text = Built();
            int shapes = text.ShapeCount;
            text.gameObject.SetActive(false);
            var stack = Stack(Latin);
            stack.NotifyChanged();
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(shapes + 1, text.ShapeCount);
        }

        [Test]
        public void AGlyphLeavingTheAtlasRebuildsTheMeshOnly()
        {
            LetteraText text = Built();
            int rebuilds = text.RebuildCount, parses = text.ParseCount, shapes = text.ShapeCount;
            text.gameObject.SetActive(false);
            GlyphAtlas.Clear();
            GlyphAtlas.Flush();
            Assert.AreEqual(rebuilds, text.RebuildCount, "an absent text costs nothing when the atlas clears");
            int glyphsNow = GlyphAtlas.GlyphCount;
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rebuilds + 1, text.RebuildCount);
            Assert.AreEqual(parses, text.ParseCount);
            Assert.AreEqual(shapes, text.ShapeCount);
            Assert.Greater(GlyphAtlas.GlyphCount, glyphsNow, "its glyphs came back");
            Assert.Greater(DrawnVertices(text), 0);
        }

        [Test]
        public void ColorChangedWhileAwayIsKept()
        {
            LetteraText text = Built();
            text.gameObject.SetActive(false);
            text.color = new Color(1, 0, 0, 0.5f);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Color32 drawn = ((ITextComponent)text).SharedEngine.Colors[0];
            Assert.AreEqual(255, drawn.r);
            Assert.AreEqual(128, drawn.a, 1);
        }

        [Test]
        public void ASizeChangedWhileAwayLaysOutAgain()
        {
            LetteraText text = Label(MakeCanvas().transform, "Words enough to wrap");
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(1, text.Lines.Count);
            text.gameObject.SetActive(false);
            ((RectTransform)text.transform).sizeDelta = new Vector2(60, 40);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(text.Lines.Count, 1);
        }

        [Test]
        public void AMovedTextTakesItsNewCanvasScale()
        {
            LetteraText text = Built();
            int rebuilds = text.RebuildCount;
            Canvas other = MakeCanvas();
            other.scaleFactor = 5;
            text.gameObject.SetActive(false);
            text.transform.SetParent(other.transform, false);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rebuilds + 1, text.RebuildCount);
        }

        [Test]
        public void DisablingTheComponentStillReleasesEverything()
        {
            LetteraText text = Built();
            int listening = LetteraTextListeners.Count, parses = text.ParseCount;
            text.enabled = false;
            Assert.AreEqual(listening - 1, LetteraTextListeners.Count);
            Assert.AreEqual(0, DrawnVertices(text));
            Canvas.ForceUpdateCanvases();
            text.enabled = true;
            Assert.AreEqual(listening, LetteraTextListeners.Count);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(parses + 1, text.ParseCount, "back from a disable it starts over");
            Assert.Greater(DrawnVertices(text), 0);
        }

        [Test]
        public void DisablingKeepsNoMesh()
        {
            LetteraText text = Built();
            Mesh mesh = text.MeshForTests;
            Assert.IsTrue(mesh != null);
            text.enabled = false;
            Assert.IsTrue(mesh == null, "the mesh is destroyed");
        }

        [Test]
        public void DisablingALongTextTakesItsPartsAway()
        {
            var body = new System.Text.StringBuilder();
            while (body.Length < 100 * 1024) body.Append("Long enough to need several canvas renderers ").Append(body.Length % 7 == 0 ? "\n" : " ");
            Canvas canvas = MakeCanvas();
            LetteraText text = Label(canvas.transform, body.ToString());
            ((RectTransform)text.transform).sizeDelta = new Vector2(800, 100000);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(text.PartCount, 0);
            text.gameObject.SetActive(false);
            Assert.Greater(text.PartCount, 0, "kept while its object is away");
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(text.PartCount, 0);
            text.enabled = false;
            Assert.AreEqual(0, text.PartCount);
        }

        [Test]
        public void DestroyedWhileAwayLeavesNothingListening()
        {
            LetteraText text = Built();
            int listening = LetteraTextListeners.Count;
            Mesh mesh = text.MeshForTests;
            text.gameObject.SetActive(false);
            Assert.AreEqual(listening, LetteraTextListeners.Count);
            UnityEngine.Object.DestroyImmediate(text.gameObject);
            Assert.AreEqual(listening - 1, LetteraTextListeners.Count);
            Assert.IsTrue(mesh == null, "the mesh goes with it");
        }

        [Test]
        public void UnderAMaskItComesBackClippedAndDrawn()
        {
            Canvas canvas = MakeCanvas();
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            mask.transform.SetParent(canvas.transform, false);
            ((RectTransform)mask.transform).sizeDelta = new Vector2(300, 100);
            LetteraText text = Label(mask.transform);
            Canvas.ForceUpdateCanvases();
            int rebuilds = text.RebuildCount;
            text.gameObject.SetActive(false);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.IsTrue(text.canvasRenderer.hasRectClipping);
            Assert.Greater(DrawnVertices(text), 0);
        }

        [Test]
        public void UnderAStencilMaskItsSegmentsGetMaterialsAgain()
        {
            Canvas canvas = MakeCanvas();
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Mask));
            mask.transform.SetParent(canvas.transform, false);
            ((RectTransform)mask.transform).sizeDelta = new Vector2(400, 100);
            LetteraText text = Label(mask.transform, "<shadow>Masked</shadow> and <mark=#FF0>marked</mark>");
            Canvas.ForceUpdateCanvases();
            int count = text.canvasRenderer.materialCount;
            Shader first = text.canvasRenderer.GetMaterial(0).shader;
            text.gameObject.SetActive(false);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(count, text.canvasRenderer.materialCount);
            Assert.IsTrue(text.canvasRenderer.GetMaterial(0) != null);
            Assert.AreEqual(first, text.canvasRenderer.GetMaterial(0).shader);
        }

        [Test]
        public void HiddenWhenItGoesAndShownAfterComesBackDrawn()
        {
            LetteraText text = Built();
            int rebuilds = text.RebuildCount;
            text.Hide();
            text.gameObject.SetActive(false);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            text.Show();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(rebuilds, text.RebuildCount);
            Assert.Greater(DrawnVertices(text), 0);
        }

        [Test]
        public void ReadingWhileAwayStillWorks()
        {
            LetteraText text = Built();
            text.gameObject.SetActive(false);
            Assert.AreEqual("Kept while away", text.CleanText);
            Assert.AreEqual(15, text.GlyphCount);
            text.Text = "Longer now and asked for";
            text.ForceRebuild();
            Assert.AreEqual("Longer now and asked for", text.CleanText);
            text.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Assert.Greater(DrawnVertices(text), 0);
        }

        // ------------------------------------------------------------- world text

        [Test]
        public void WorldTextComesBackWithNoRebuildAndInItsBatch()
        {
            LetteraWorldText text = Make("Kept while away");
            LetteraWorldBatcher.Update();
            int rebuilds = text.RebuildCount;
            int listening = LetteraTextListeners.Count;
            text.gameObject.SetActive(false);
            Assert.IsFalse(LetteraWorldBatcher.Texts.Contains(text));
            Assert.AreEqual(listening, LetteraTextListeners.Count);
            LetteraWorldBatcher.Update();
            text.gameObject.SetActive(true);
            Assert.IsTrue(LetteraWorldBatcher.Texts.Contains(text));
            LetteraWorldBatcher.Update();
            Assert.AreEqual(rebuilds, text.RebuildCount);
        }

        [Test]
        public void WorldTextHearsWhileAwayAndDisablingReleases()
        {
            LetteraWorldText text = Make("Kept while away");
            LetteraWorldBatcher.Update();
            int parses = text.ParseCount, rebuilds = text.RebuildCount;
            text.gameObject.SetActive(false);
            text.Text = "Changed";
            text.gameObject.SetActive(true);
            LetteraWorldBatcher.Update();
            Assert.AreEqual(parses + 1, text.ParseCount);
            Assert.AreEqual("Changed", text.CleanText);
            int listening = LetteraTextListeners.Count;
            text.enabled = false;
            Assert.AreEqual(listening - 1, LetteraTextListeners.Count);
            text.enabled = true;
            LetteraWorldBatcher.Update();
            Assert.AreEqual(parses + 2, text.ParseCount);
            Assert.Greater(text.RebuildCount, rebuilds);
        }

        [Test]
        public void ManyWorldTextsLeavingKeepTheRestDrawn()
        {
            var texts = new List<LetteraWorldText>();
            for (int i = 0; i < 40; i++) texts.Add(Make("Label " + i));
            LetteraWorldBatcher.Update();
            for (int i = 0; i < texts.Count; i += 2) texts[i].gameObject.SetActive(false);
            LetteraWorldBatcher.Update();
            for (int i = 1; i < texts.Count; i += 2) Assert.IsTrue(LetteraWorldBatcher.Texts.Contains(texts[i]));
            for (int i = 0; i < texts.Count; i += 2) texts[i].gameObject.SetActive(true);
            for (int i = 0; i < texts.Count; i += 3) UnityEngine.Object.DestroyImmediate(texts[i].gameObject);
            LetteraWorldBatcher.Update();
            for (int i = 1; i < texts.Count; i += 3) Assert.IsTrue(LetteraWorldBatcher.Texts.Contains(texts[i]));
        }

        // ------------------------------------------------------------- what it costs

        static long Allocated(Action body)
        {
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            body();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [Test]
        public void EnablingManyDoesNotCostMoreAsTheyShareMore()
        {
            Canvas canvas = MakeCanvas();
            var labels = new List<LetteraText>();
            for (int i = 0; i < 300; i++) labels.Add(Label(canvas.transform));
            Canvas.ForceUpdateCanvases();

            long Pass(int count)
            {
                return Allocated(() =>
                {
                    for (int i = 0; i < count; i++) labels[i].gameObject.SetActive(false);
                    for (int i = 0; i < count; i++) labels[i].gameObject.SetActive(true);
                });
            }

            Pass(300);
            Canvas.ForceUpdateCanvases();
            long fifty = Pass(50);
            Canvas.ForceUpdateCanvases();
            long all = Pass(300);
            Canvas.ForceUpdateCanvases();
            // One delegate list per text grew with the square of the count: 26 MB for 1000.
            Assert.Less(all, 300 * 2048, "no more than a little per text, whoever shares the font");
            Assert.Less(all, fifty * 6 + 64 * 1024, "six times the texts costs about six times as much");
        }

        [Test]
        public void ListenersAreOneListNotADelegatePerText()
        {
            int before = LetteraTextListeners.Count;
            Canvas canvas = MakeCanvas();
            LetteraText a = Label(canvas.transform), b = Label(canvas.transform), c = Label(canvas.transform);
            Assert.AreEqual(before + 3, LetteraTextListeners.Count);
            int shapesA = 0;
            Canvas.ForceUpdateCanvases();
            shapesA = c.ShapeCount;
            UnityEngine.Object.DestroyImmediate(b.gameObject);
            Assert.AreEqual(before + 2, LetteraTextListeners.Count);
            Stack(Latin).NotifyChanged();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(shapesA + 1, c.ShapeCount, "the one moved into the gap still hears");
            Assert.Greater(a.ShapeCount, 0);
        }

        [Test]
        public void AShortLabelAtBirthHoldsNoBigBuffers()
        {
            Canvas canvas = MakeCanvas();
            long bytes = Allocated(() => Label(canvas.transform, "Short"));
            LetteraText text = Label(canvas.transform, "Short");
            Canvas.ForceUpdateCanvases();
            TextEngine engine = ((ITextComponent)text).SharedEngine;
            Assert.AreEqual(0, engine.Uv2.Length, "only styled text has these");
            Assert.AreEqual(0, engine.Uv3.Length);
            Assert.LessOrEqual(engine.Vertices.Length, 64);
            // Was 42 KB of buffers before any text was set; what stands here is objects, not room for text not yet there.
            Assert.Less(bytes, 24 * 1024);
        }

        [Test]
        public void ACounterGrowingFromOneDigitToFiveGrowsNoBuffer()
        {
            Canvas canvas = MakeCanvas();
            LetteraText text = Label(canvas.transform, "0");
            Canvas.ForceUpdateCanvases();
            string[] values = { "12", "345", "6789", "10234", "7" };
            long bytes = Allocated(() =>
            {
                foreach (string value in values)
                {
                    text.SetText(value);
                    text.ForceRebuild();
                }
            });
            // Buffers that started empty grew in the frame a counter first changed: 43 KB for 64 counters.
            Assert.Less(bytes, 256);
        }

        [Test]
        public void AStyledTextStillGetsItsStreams()
        {
            Canvas canvas = MakeCanvas();
            LetteraText text = Label(canvas.transform, "<shadow>Long enough to grow past the first buffers of a text, and more</shadow>");
            Canvas.ForceUpdateCanvases();
            TextEngine engine = ((ITextComponent)text).SharedEngine;
            if (engine.Styled)
            {
                Assert.GreaterOrEqual(engine.Uv2.Length, engine.VertexCount);
                Assert.GreaterOrEqual(engine.Uv3.Length, engine.VertexCount);
            }
            text.Text = "Plain now";
            Canvas.ForceUpdateCanvases();
            text.Text = "<mark=#FF0>Styled again, and a good deal longer than it was before, to grow</mark>";
            Canvas.ForceUpdateCanvases();
            Assert.Greater(engine.VertexCount, 0);
            if (engine.Styled) Assert.GreaterOrEqual(engine.Uv2.Length, engine.VertexCount);
        }
    }
}
