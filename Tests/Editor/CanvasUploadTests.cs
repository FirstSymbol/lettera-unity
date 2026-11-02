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
    }
}
