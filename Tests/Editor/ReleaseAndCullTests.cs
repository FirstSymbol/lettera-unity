using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>What Lettera gives back when quiet or short of memory, a font named by its file, and a mask culling by ink.</summary>
    public class ReleaseAndCullTests : EditingFixture
    {
        readonly List<GameObject> m_Roots = new List<GameObject>();

        [TearDown]
        public void DestroyRoots()
        {
            foreach (GameObject root in m_Roots)
                if (root != null)
                    Object.DestroyImmediate(root);
            m_Roots.Clear();
        }

        Transform MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas));
            m_Roots.Add(go);
            return go.transform;
        }

        LetteraText Label(Transform parent, string body, Vector2 size, LetteraFontAsset font = null)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            var text = go.AddComponent<LetteraText>();
            text.Font = font != null ? font : Latin;
            text.FontSize = 18;
            text.Text = body;
            return text;
        }

        static int Drawn(LetteraText text) => text.canvasRenderer.GetMesh() != null ? text.canvasRenderer.GetMesh().vertexCount : 0;

        static string FontFile(string file)
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(asmdef), "..", "Fonts~", file));
        }

        [Test]
        public void ATextDrawsAndEditsRightAfterARelease()
        {
            LetteraText text = Label(MakeCanvas(), "Before the release", new Vector2(400, 40));
            Canvas.ForceUpdateCanvases();
            Assert.Greater(Drawn(text), 0);
            int releases = LetteraMemory.ReleaseCount;
            LetteraMemory.Release();
            Assert.AreEqual(releases + 1, LetteraMemory.ReleaseCount);
            Assert.AreEqual(0, LetteraCaches.Stats.Bytes, "the paragraph cache went back");
            text.Text = "After the release, with new words";
            text.ForceRebuild();
            Canvas.ForceUpdateCanvases();
            LetteraText reference = Label(MakeCanvas(), "After the release, with new words", new Vector2(400, 40));
            Canvas.ForceUpdateCanvases();
            Assert.Greater(Drawn(text), 0);
            Assert.AreEqual(reference.GlyphCount, text.GlyphCount);
            Assert.AreEqual(Drawn(reference), Drawn(text));

            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            text.ForceRebuild();
            field.Selectable.Select(field.Length, field.Length);
            LetteraMemory.Release();
            field.InsertText("!", "user.type");
            text.ForceRebuild();
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual("After the release, with new words!", text.Text);
            Assert.AreEqual(reference.GlyphCount + 1, text.GlyphCount);
        }

        [Test]
        public void WithNoTextLeftThePagesGoAndComeBackForTheNextText()
        {
            LetteraText first = Label(MakeCanvas(), "Glyphs on a page", new Vector2(400, 40));
            Canvas.ForceUpdateCanvases();
            Assert.Greater(GlyphAtlas.SdfPageCount, 0);
            Object.DestroyImmediate(first.gameObject);
            GlyphAtlas.Release(true);
            Assert.AreEqual(0, GlyphAtlas.SdfPageCount, "no page is kept for no text");
            Assert.AreEqual(0, GlyphAtlas.GlyphCount);
            GlyphAtlas.Flush();
            Assert.AreEqual(0, GlyphAtlas.SdfPageCount, "a frame with no glyph makes no page");

            LetteraText next = Label(MakeCanvas(), "A new text after", new Vector2(400, 40));
            Canvas.ForceUpdateCanvases();
            Assert.Greater(GlyphAtlas.SdfPageCount, 0);
            Assert.Greater(Drawn(next), 0);
            Assert.AreSame(GlyphAtlas.TextureOf(0), Shader.GetGlobalTexture("_LetteraSdfAtlas"), "the new page is the one bound");
        }

        [Test]
        public void AFontNamedByItsFileIsReadFromThatFile()
        {
            LetteraFont font = LetteraFont.CreateFromFile(FontFile("NotoSans-Regular.ttf"));
            Created.Add(font);
            Assert.IsTrue(font.HasData);
            LetteraText text = Label(MakeCanvas(), "From a file", new Vector2(400, 40), font);
            LetteraText reference = Label(MakeCanvas(), "From a file", new Vector2(400, 40));
            Canvas.ForceUpdateCanvases();
            Assert.Greater(text.GlyphCount, 0);
            Assert.AreEqual(reference.GlyphCount, text.GlyphCount);
            Assert.AreEqual(LetteraFontSource.RuntimeFile, font.LoadedFace.Source);
            Assert.AreEqual(0, font.LoadedFace.RetainedBytes, "its bytes stay in the file");
            Assert.Greater(font.LoadedFace.MappedBytes, 0);
        }

        [Test]
        public void AFontFromBytesIsReadWhereTheArrayIs()
        {
            byte[] bytes = File.ReadAllBytes(FontFile("NotoSans-Regular.ttf"));
            LetteraFont font = LetteraFont.CreateFromBytes(bytes);
            Created.Add(font);
            LetteraText text = Label(MakeCanvas(), "From bytes", new Vector2(400, 40), font);
            Canvas.ForceUpdateCanvases();
            int glyphs = text.GlyphCount;
            Assert.Greater(glyphs, 0);
            Assert.AreEqual(bytes.Length, font.LoadedFace.RetainedBytes);
            font.ReleaseFace();
            text.Text = "From bytes";
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.AreEqual(glyphs, text.GlyphCount, "a face let go and loaded again reads the same array");
            Assert.AreEqual(LetteraFontSource.RuntimeBytes, font.LoadedFace.Source);
        }

        // P23: glyph-major text with a stroke and a shadow draws one quad a glyph, and the picture is the three quads' within blend rounding.
        [Test]
        public void MergedLayersDrawOneQuadAGlyphAndTheSamePicture()
        {
            LetteraText text = LayeredLabel("<stroke=#3399FF,0.08><shadow=#000000,0.06,-0.06>Kerning AV</shadow></stroke>");
            text.Styles.Add(LetteraStyle.ForWholeText(new PaintOrderEffect { Order = LetteraPaintOrder.GlyphMajor }));
            text.MarkStylesDirty();
            text.ForceRebuild();
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(text.Engine.LayersMerged);
            int glyphs = Drawn(text) / 4;
            int separate = SamePictureMergedAndApart(text);
            Assert.AreEqual(glyphs * 3 * 4, separate, "one quad a glyph against three");

            // A layer order the shader does not compose, or a paint it does not hold, keeps the quads apart.
            text.Text = "<glow=#FFFF00,0.2><stroke=#3399FF,0.08>Glow</stroke></glow>";
            text.ForceRebuild();
            Assert.IsFalse(text.Engine.LayersMerged);
        }

        // Layer-major, the usual order: a glyph's stroke and shadow join in one quad under every face, and a shadow that reaches an earlier stroke goes first.
        [Test]
        public void MergedLayersInLayerOrderKeepThePicture()
        {
            foreach (string body in new[]
            {
                "<stroke=#3399FF,0.08><shadow=#000000,0.06,-0.06>Kerning AV wavy fox</shadow></stroke>",
                "<stroke=#3399FF,0.08><shadow=#200000,-0.08,-0.04>To AVA, rt ff\nsecond line</shadow></stroke>",
            })
            {
                LetteraText text = LayeredLabel(body);
                Assert.IsTrue(text.Engine.LayersMerged, body);
                int hoisted = text.Engine.LayersHoisted;
                int merged = Drawn(text);
                int separate = SamePictureMergedAndApart(text);
                Assert.AreEqual(0, separate % 12, body);
                Assert.AreEqual(separate / 3 * 2 + hoisted * 4, merged, body);
                if (body.Contains("-0.08")) Assert.Greater(hoisted, 0, "a shadow to the left reaches the stroke before it");
                foreach (GameObject root in m_Roots) Object.DestroyImmediate(root);
                m_Roots.Clear();
            }

            // A shadow alone has nothing to join in this order, and keeps its quads.
            LetteraText alone = LayeredLabel("<shadow=#000000C0,0.05,-0.05>Shadow alone</shadow>");
            Assert.IsFalse(alone.Engine.LayersMerged);
        }

        // A tint a canvas multiplies into the vertices, as a button's transition does, reaches the merged layers as it reaches separate ones.
        [Test]
        public void MergedLayersTakeTheCanvasTint()
        {
            LetteraText text = LayeredLabel("<stroke=#3399FF,0.08><shadow=#000000,0.06,-0.06>Tinted label</shadow></stroke>");
            text.canvasRenderer.SetColor(new Color(1f, 0.4f, 0.6f, 0.8f));
            Assert.IsTrue(text.Engine.LayersMerged);
            SamePictureMergedAndApart(text);
        }

        LetteraText LayeredLabel(string body)
        {
            GlyphAtlas.Clear();
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            m_Roots.Add(canvasObject);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(300, 120);
            LetteraText text = Label(canvas.transform, body, new Vector2(280, 100));
            text.FontSize = 40;
            text.ForceRebuild();
            Canvas.ForceUpdateCanvases();
            return text;
        }

        // Renders the text merged, then apart, and holds the two pictures together. Returns the vertices drawn apart.
        int SamePictureMergedAndApart(LetteraText text)
        {
            var background = new Color(0.2f, 0.22f, 0.26f, 1);
            Texture2D merged = Render(300, 120, background);
            TextEngine.NoMergedLayersForTests = true;
            try
            {
                text.MarkStylesDirty();
                text.ForceRebuild();
                Canvas.ForceUpdateCanvases();
                Assert.IsFalse(text.Engine.LayersMerged);
                int separate = Drawn(text);
                Texture2D apart = Render(300, 120, background);
                int differing = 0, inked = 0;
                float worst = 0;
                for (int y = 0; y < 120; y++)
                    for (int x = 0; x < 300; x++)
                    {
                        Color a = merged.GetPixel(x, y), b = apart.GetPixel(x, y);
                        float d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                        worst = Mathf.Max(worst, d);
                        if (d > 3 / 255f) differing++;
                        if (Mathf.Abs(b.b - background.b) > 0.05f) inked++;
                    }
                Assert.Greater(inked, 1500, "the text drew");
                Assert.LessOrEqual(differing, inked / 200, "worst " + worst * 255 + " in " + differing + " of " + inked);
                return separate;
            }
            finally
            {
                TextEngine.NoMergedLayersForTests = false;
                text.MarkStylesDirty();
                text.ForceRebuild();
            }
        }

        // The row I3 of the targets: a label that reaches into a mask with only its empty margin draws nothing there.
        [Test]
        public void AMaskCullsALabelWhoseInkLiesOutsideIt()
        {
            Transform canvas = MakeCanvas();
            var view = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
            view.transform.SetParent(canvas, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 400);
            // Its rect reaches 3 units into the mask; its letters start well below that.
            LetteraText below = Label(view.transform, "wn fox jumps ove", new Vector2(760, 26));
            ((RectTransform)below.transform).anchoredPosition = new Vector2(0, -210);
            below.VerticalAlignment = LetteraVerticalAlignment.Bottom;
            LetteraText inside = Label(view.transform, "fully inside", new Vector2(760, 26));
            LetteraText edge = Label(view.transform, "half inside", new Vector2(760, 26));
            ((RectTransform)edge.transform).anchoredPosition = new Vector2(0, -200);
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(below.canvasRenderer.cull, "only its margin reached in");
            Assert.IsFalse(inside.canvasRenderer.cull);
            Assert.IsFalse(edge.canvasRenderer.cull, "ink across the edge draws");

            // New text with ink inside the mask must come back without anything moving.
            below.Text = "a line above\nwn fox jumps ove";
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            Assert.IsFalse(below.canvasRenderer.cull, "its ink reaches in now: " + below.Engine.InkKnown + " " + below.Engine.InkRect + " " + below.Engine.MeshBounds + " lines " + below.Lines.Count + " drawn " + Drawn(below) + " dirty " + below.Engine.Dirty);
            Assert.Greater(Drawn(below), 0);

            // With the mask gone, nothing stays culled.
            below.Text = "wn fox jumps ove";
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(below.canvasRenderer.cull);
            Object.DestroyImmediate(view.GetComponent<RectMask2D>());
            Canvas.ForceUpdateCanvases();
            Assert.IsFalse(below.canvasRenderer.cull);
        }
    }
}
