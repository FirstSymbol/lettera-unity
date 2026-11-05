#if !LETTERA_NO_MATH
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Formulas in a font with no MATH table, as a phone with no math font shows them: rules drawn whole on a canvas, accents over their letters, limits clear of their operators.</summary>
    public class MathFallbackTests : TextFixture
    {
        const int Width = 640, Height = 360;
        readonly List<GameObject> m_Roots = new List<GameObject>();
        Camera m_Camera;
        RenderTexture m_Target;
        LetteraFontAsset m_SavedMathFont;

        [SetUp]
        public void Fresh()
        {
            m_SavedMathFont = LetteraSettings.Instance.Math.Font;
            LetteraSettings.Instance.Math.Font = Latin;
            LetteraMathFont.Override = null;
            LetteraMathCache.Clear();
        }

        [TearDown]
        public void Back()
        {
            LetteraSettings.Instance.Math.Font = m_SavedMathFont;
            LetteraMathCache.Clear();
            foreach (GameObject root in m_Roots)
                if (root != null)
                    Object.DestroyImmediate(root);
            m_Roots.Clear();
            if (m_Target != null) Object.DestroyImmediate(m_Target);
        }

        static FontFace Face => Latin.LoadFace();

        static LetteraMathLayout Lay(string source, bool display = false) => LetteraMathCache.Get(source, display, Face, Face);

        [Test]
        public void TheTestFontHasNoTable()
        {
            Assert.IsFalse(LetteraMathFont.For(Face).HasMathTable);
        }

        // ---------------------------------------------------------------- accents and limits

        static (float left, float bottom, float right, float top) Ink(in LetteraMathItem item)
        {
            (float minX, float minY, float maxX, float maxY, float _) = LetteraMathFont.For(Face).Extents(item.Glyph);
            return (item.X + minX * item.Scale, item.Y + minY * item.Scale, item.X + maxX * item.Scale, item.Y + maxY * item.Scale);
        }

        [TestCase("\\hat{x}")]
        [TestCase("\\tilde{n}")]
        [TestCase("\\dot{a}")]
        public void AnAccentStandsOverItsLetter(string source)
        {
            LetteraMathLayout layout = Lay(source);
            Assert.AreEqual(2, layout.Items.Length, source);
            LetteraMathItem body = layout.Items[0], mark = layout.Items[1];
            float advance = LetteraMathFont.For(Face).Extents(body.Glyph).advance * body.Scale;
            var markInk = Ink(mark);
            var bodyInk = Ink(body);
            float middle = (markInk.left + markInk.right) / 2;
            Assert.AreEqual(body.X + advance / 2, middle, 0.03f, source + ": the mark's ink centres on the letter");
            Assert.Greater(markInk.bottom, bodyInk.top - 0.02f, source + ": the mark is above the letter");
        }

        [Test]
        public void AWideAccentCentresOverItsBody()
        {
            LetteraMathLayout layout = Lay("\\widehat{abc}");
            LetteraMathItem mark = layout.Items[layout.Items.Length - 1];
            var ink = Ink(mark);
            Assert.AreEqual(layout.Width / 2, (ink.left + ink.right) / 2, 0.05f);
        }

        // The test font has no n-ary signs, so named operators stand in: their limits take the same path.
        [TestCase("\\lim^{n}_{i=0}", 3)]
        [TestCase("\\max^{k}_{j}", 3)]
        public void LimitsClearTheirOperator(string source, int operatorItems)
        {
            LetteraMathLayout layout = Lay(source, true);
            float opTop = float.MinValue, opBottom = float.MaxValue;
            for (int i = 0; i < operatorItems; i++)
            {
                Assert.AreNotEqual(0u, layout.Items[i].Glyph, "the font draws the operator");
                var ink = Ink(layout.Items[i]);
                opTop = Mathf.Max(opTop, ink.top);
                opBottom = Mathf.Min(opBottom, ink.bottom);
            }
            var op = (top: opTop, bottom: opBottom);
            // The upper limit comes right after the operator, then the lower one.
            var upper = Ink(layout.Items[operatorItems]);
            Assert.GreaterOrEqual(upper.bottom - op.top, 1f / 9 - 0.005f, source + ": the upper limit clears the operator's top");
            float lowestTop = float.MinValue;
            for (int i = operatorItems + 1; i < layout.Items.Length; i++) lowestTop = Mathf.Max(lowestTop, Ink(layout.Items[i]).top);
            Assert.GreaterOrEqual(op.bottom - lowestTop, 1f / 6 - 0.005f, source + ": the lower limit clears the operator's foot");
        }

        // ---------------------------------------------------------------- rules on a canvas

        LetteraText Label(string body, float size, float scaleFactor)
        {
            var cameraObject = new GameObject("Camera");
            m_Roots.Add(cameraObject);
            m_Camera = cameraObject.AddComponent<Camera>();
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.black;
            m_Target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            m_Camera.targetTexture = m_Target;
            var canvasObject = new GameObject("Canvas", typeof(Canvas));
            m_Roots.Add(canvasObject);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = m_Camera;
            canvas.planeDistance = 10;
            canvas.scaleFactor = scaleFactor;
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(300, size * 3);
            var text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = size;
            text.Text = body;
            text.color = Color.white;
            text.ForceRebuild();
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

        // Formulas that draw nothing but one rule, so every quad of the text is a piece of it.
        static readonly string[] OnlyRules =
        {
            "\\frac{\\hphantom{ab}}{\\hphantom{ab}}",
            "\\frac{\\hphantom{abcdefgh}}{}",
            "\\overline{\\hphantom{i}}",
            "{}^{\\frac{\\hphantom{b}}{\\hphantom{c}}}",
        };

        static IEnumerable<TestCaseData> RuleCases()
        {
            foreach (string source in OnlyRules)
            foreach (float size in new[] { 20f, 40f })
            foreach (float scale in new[] { 1f, 1.9f })
                yield return new TestCaseData(source, size, scale);
        }

        [TestCaseSource(nameof(RuleCases))]
        public void ARulesPiecesMeetAndStayNearItsLength(string source, float size, float scale)
        {
            LetteraText text = Label("<math>" + source + "</math>", size, scale);
            TextEngine engine = text.Engine;
            Assert.AreEqual(1, engine.InlineItems.Count);
            TextEngine.InlineItem item = engine.InlineItems[0];
            float length = 0;
            int rules = 0;
            foreach (LetteraMathItem part in item.Math.Items)
                if (part.Kind == MathItemKind.Rule)
                {
                    length = part.Length;
                    rules++;
                }
            Assert.AreEqual(1, rules, source);
            float em = size * item.MathScale;
            length *= em;

            var pieces = new List<(float low, float high)>();
            for (int v = 0; v + 3 < engine.VertexCount; v += 4)
                pieces.Add((engine.Vertices[v].x, engine.Vertices[v + 2].x));
            Assert.GreaterOrEqual(pieces.Count, 2, source);
            pieces.Sort((a, b) => a.low.CompareTo(b.low));
            float reach = pieces[0].high;
            for (int i = 1; i < pieces.Count; i++)
            {
                Assert.LessOrEqual(pieces[i].low, reach + 0.001f, $"{source} at {size}, x{scale}: a gap between pieces {i - 1} and {i}");
                reach = Mathf.Max(reach, pieces[i].high);
            }
            float drawn = reach - pieces[0].low;
            Assert.GreaterOrEqual(drawn, length, source + ": the pieces cover the rule");
            Assert.LessOrEqual(drawn, length + 2 * (GlyphAtlas.SpreadEm * em) + 0.001f, source + ": no piece stands beyond the rule's ends by more than its field");
        }

        [TestCase(20f, 1f)]
        [TestCase(40f, 1f)]
        [TestCase(40f, 1.9f)]
        [TestCase(14f, 1.9f)]
        public void ARuleDrawsAsOneUnbrokenLine(float size, float scale)
        {
            foreach (string source in OnlyRules)
            {
                foreach (GameObject root in m_Roots)
                    if (root != null)
                        Object.DestroyImmediate(root);
                m_Roots.Clear();
                if (m_Target != null) Object.DestroyImmediate(m_Target);
                Label("<math>" + source + "</math>", size, scale);
                Texture2D image = Render();
                // Each column's brightest pixel: inside the rule every column holds its full ink.
                var columns = new float[Width];
                float peak = 0;
                for (int x = 0; x < Width; x++)
                {
                    float most = 0;
                    for (int y = 0; y < Height; y++) most = Mathf.Max(most, image.GetPixel(x, y).r);
                    columns[x] = most;
                    peak = Mathf.Max(peak, most);
                }
                Assert.Greater(peak, 0.5f, $"{source} at {size}, x{scale}: the rule drew");
                int first = -1, last = -1;
                for (int x = 0; x < Width; x++)
                    if (columns[x] >= peak * 0.5f)
                    {
                        if (first < 0) first = x;
                        last = x;
                    }
                // A column or two at each end is the rule's own soft edge.
                int faint = 0;
                for (int x = first + 2; x <= last - 2; x++)
                    if (columns[x] < peak * 0.7f)
                        faint++;
                Assert.AreEqual(0, faint, $"{source} at {size}, x{scale}: {faint} faint columns between {first} and {last}");
            }
        }
    }
}
#endif
