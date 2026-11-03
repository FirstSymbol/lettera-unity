using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A Canvas text's mesh goes up as separate streams; what the canvas draws from it is the text.</summary>
    public class CanvasUploadTests : TextFixture
    {
        LetteraText CanvasLabel(string text, float size)
        {
            var root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Created.Add(root);
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(400, 100);
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(400, 100);
            var label = go.AddComponent<LetteraText>();
            label.raycastTarget = false;
            label.Font = Latin;
            label.FontSize = size;
            label.color = Color.white;
            label.Text = text;
            return label;
        }

        static int Ink(Texture2D picture)
        {
            int count = 0;
            foreach (Color32 pixel in picture.GetPixels32())
                if (pixel.r > 128)
                    count++;
            return count;
        }

        Texture2D Draw()
        {
            Canvas.ForceUpdateCanvases();
            return Render(400, 100, Color.black);
        }

        [Test]
        public void AnUnstyledTextDrawsFromItsStreams()
        {
            LetteraText label = CanvasLabel("Hello, streams", 40);
            int ink = Ink(Draw());
            Assert.Greater(ink, 400, "a 40 px line of text leaves ink on the canvas");
            Assert.AreEqual(((ITextComponent)label).SharedEngine.VertexCount, label.MeshForTests.vertexCount);

            // Fewer vertices, then more: the streams follow without a mesh made anew.
            label.Text = "Hi";
            int fewer = Ink(Draw());
            Assert.Greater(fewer, 50);
            Assert.Less(fewer, ink);
            Assert.AreEqual(((ITextComponent)label).SharedEngine.VertexCount, label.MeshForTests.vertexCount);
            label.Text = "Hello, streams";
            Assert.AreEqual(ink, Ink(Draw()), "the same text draws the same ink again");

            // A colour change rewrites one stream.
            label.color = new Color(0.2f, 0.2f, 0.2f);
            Assert.Less(Ink(Draw()), 10, "a dark colour leaves no bright ink");
        }

        // A long text keeps its indices for all its room and says how many draw: a shorter text in the same room draws its own quads only.
        [Test]
        public void AShorterLongTextInTheSameRoomDrawsOnlyItsQuads()
        {
            string longer = new string('x', 700), shorter = new string('x', 680);
            LetteraText label = CanvasLabel(longer, 6);
            int full = Ink(Draw());
            int room = label.MeshForTests.vertexCount;
            Assert.GreaterOrEqual(room, 2048, "a long text's room");
            label.Text = shorter;
            int drawn = Ink(Draw());
            Assert.AreEqual(room, label.MeshForTests.vertexCount, "the room stayed");
            Assert.AreEqual(1, label.MeshForTests.subMeshCount);
            Assert.AreEqual(((ITextComponent)label).SharedEngine.IndexCount, (int)label.MeshForTests.GetSubMesh(0).indexCount);
            Assert.Less(drawn, full, "the quads past its count are drawn");
            CanvasLabel(shorter, 6);
            label.gameObject.SetActive(false);
            Assert.AreEqual(drawn, Ink(Draw()), "the same as the shorter text made anew");
        }

        // Plain faces drawn across the threads keep the shading parameters the buffer holds: what the mesh has is still what the engine wrote.
        [Test]
        public void KeptShadingParametersAreTheEnginesThroughEveryChange()
        {
            LetteraWorkerMode mode = LetteraWorkers.Mode;
            int threshold = LetteraWorkers.ParallelFromCodepoints;
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            LetteraWorkers.ParallelFromCodepoints = 256;
            try
            {
                LetteraText label = CanvasLabel(new string('x', 700), 6);
                int drawn = TextEngine.SplitPasses[2];
                var held = new System.Collections.Generic.List<Vector4>();
                foreach (string text in new[] { new string('x', 690), new string('x', 700), "<color=#f00>" + new string('x', 690) + "</color>", new string('x', 760), new string('w', 300) + "<u>" + new string('x', 400) + "</u>", new string('x', 700) })
                {
                    label.Text = text;
                    Draw();
                    TextEngine e = ((ITextComponent)label).SharedEngine;
                    label.MeshForTests.GetUVs(1, held);
                    for (int v = 0; v < e.VertexCount; v++) Assert.AreEqual(e.Uv1[v], held[v], text.Substring(0, 12) + " vertex " + v);
                }
                Assert.Greater(TextEngine.SplitPasses[2], drawn, "faces were drawn across the threads");
            }
            finally
            {
                LetteraWorkers.Mode = mode;
                LetteraWorkers.ParallelFromCodepoints = threshold;
            }
        }
    }
}
