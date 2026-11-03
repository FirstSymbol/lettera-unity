using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A distance quad on a screen canvas is cut to what it shows, and the picture stays the one its whole tile drew.</summary>
    public class QuadReachTests : TextFixture
    {
        const int Width = 640, Height = 360;
        readonly List<GameObject> m_Roots = new List<GameObject>();
        Camera m_Camera;
        RenderTexture m_Target;

        [TearDown]
        public void DestroyRoots()
        {
            TextEngine.WholeTilesForTests = false;
            foreach (GameObject root in m_Roots)
                if (root != null)
                    Object.DestroyImmediate(root);
            m_Roots.Clear();
            if (m_Target != null) Object.DestroyImmediate(m_Target);
        }

        Transform MakeCanvas()
        {
            var cameraObject = new GameObject("Camera");
            m_Roots.Add(cameraObject);
            m_Camera = cameraObject.AddComponent<Camera>();
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f, 1);
            m_Target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            m_Camera.targetTexture = m_Target;
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            m_Roots.Add(canvasObject);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = m_Camera;
            canvas.planeDistance = 10;
            return canvas.transform;
        }

        LetteraText Label(Transform parent, string body, float size, Vector2 position, float scale = 1)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(600, size * 1.6f);
            rect.anchoredPosition = position;
            rect.localScale = new Vector3(scale, scale, 1);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = size;
            text.Text = body;
            text.color = Color.white;
            return text;
        }

        Texture2D Render()
        {
            ShaderUtil.allowAsyncCompilation = false;
            GlyphAtlas.Flush();
            Canvas.ForceUpdateCanvases();
            m_Camera.Render();
            m_Camera.Render();
            RenderTexture.active = m_Target;
            var result = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            Created.Add(result);
            result.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            result.Apply();
            RenderTexture.active = null;
            ShaderUtil.allowAsyncCompilation = true;
            return result;
        }

        static float QuadArea(LetteraText text)
        {
            TextEngine engine = text.Engine;
            float area = 0;
            for (int v = 0; v + 3 < engine.VertexCount; v += 4)
                area += Mathf.Abs((engine.Vertices[v + 2].x - engine.Vertices[v].x) * (engine.Vertices[v + 1].y - engine.Vertices[v].y));
            return area;
        }

        static void Rebuild(List<LetteraText> texts)
        {
            foreach (LetteraText text in texts)
            {
                text.MarkStylesDirty();
                text.ForceRebuild();
            }
        }

        [Test]
        public void CutQuadsDrawTheSamePicture()
        {
            GlyphAtlas.Clear();
            Transform canvas = MakeCanvas();
            const string layered = "<stroke=#3399FF,0.08><shadow=#000000,0.06,-0.06>";
            var texts = new List<LetteraText>
            {
                Label(canvas, layered + "The quick brown fox jumps</shadow></stroke>", 28, new Vector2(0, 140)),
                Label(canvas, layered + "over a lazy dog, Wavy AV</shadow></stroke>", 28, new Vector2(0, 128)),
                Label(canvas, "Plain words at fourteen, il1|", 14, new Vector2(-120, 95)),
                Label(canvas, layered + "Small and layered text</shadow></stroke>", 12, new Vector2(150, 95)),
                Label(canvas, "<shadow=#000000,0.1,-0.1,0.6>Soft shadow</shadow> <glow=#FFFF00,0.15>glow</glow>", 40, new Vector2(0, 50)),
                Label(canvas, "<stroke=#FF3300,0.05,center>Centred stroke</stroke> <stroke=#00FF66,0.04,inside>inside</stroke>", 36, new Vector2(0, 0)),
                Label(canvas, layered + "Scaled down a little</shadow></stroke>", 30, new Vector2(0, -50), 0.7f),
                Label(canvas, layered + "Large glyphs Qg</shadow></stroke>", 72, new Vector2(0, -110)),
            };
            LetteraText glyphMajor = Label(canvas, layered + "Glyph major merged</shadow></stroke>", 32, new Vector2(0, -160));
            glyphMajor.Styles.Add(LetteraStyle.ForWholeText(new PaintOrderEffect { Order = LetteraPaintOrder.GlyphMajor }));
            texts.Add(glyphMajor);
            Rebuild(texts);
            Texture2D cut = Render();
            Assert.IsTrue(glyphMajor.Engine.LayersMerged);
            Assert.IsTrue(texts[0].Engine.LayersShared, "layer order shares its corners on a screen canvas too");
            var cutAreas = new float[texts.Count];
            for (int i = 0; i < texts.Count; i++) cutAreas[i] = QuadArea(texts[i]);

            TextEngine.WholeTilesForTests = true;
            Rebuild(texts);
            Texture2D whole = Render();
            float cutTotal = 0, wholeTotal = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                float wholeArea = QuadArea(texts[i]);
                // Small text keeps more of its tile: two pixels of margin are a larger part of its em.
                Assert.LessOrEqual(cutAreas[i], wholeArea, "text " + i + " was not grown");
                cutTotal += cutAreas[i];
                wholeTotal += wholeArea;
            }
            Assert.Less(cutTotal, wholeTotal * 0.75f, "the quads shade a quarter less at least");

            int differing = 0, inked = 0;
            float worst = 0;
            Color background = m_Camera.backgroundColor;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    Color a = cut.GetPixel(x, y), b = whole.GetPixel(x, y);
                    float d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                    worst = Mathf.Max(worst, d);
                    if (d > 1.5f / 255f) differing++;
                    if (Mathf.Abs(b.b - background.b) > 0.05f) inked++;
                }
            Assert.Greater(inked, 20000, "the texts drew");
            // The same pixels read the field at the same places, give or take the last bits of an interpolated uv, which the filter's sub-texel steps can turn into a level or two on a steep edge.
            Assert.LessOrEqual(worst, 6 / 255f, differing + " pixels of " + inked + " differ");
            Assert.LessOrEqual(differing, inked / 500, "worst " + worst * 255 + " of 255");
        }
    }
}
