using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class RemainingEffectTests : TextFixture
    {
        const int W = 300, H = 300;
        static readonly Color Background = new Color(0, 0, 0, 1);

        [SetUp]
        public void ClearAtlas() => GlyphAtlas.Clear();

        LetteraWorldText m_Last;

        LetteraWorldText Show(string markup, float size = 120, LetteraFontAsset font = null, params LetteraStyle[] styles)
        {
            if (m_Last != null) Object.DestroyImmediate(m_Last.gameObject);
            LetteraWorldText text = Make("", 280, 280, font, size);
            m_Last = text;
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Styles.AddRange(styles);
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        // ------------------------------------------------------------- extrude

        [Test]
        public void ExtrudeAddsOneQuadPerStep()
        {
            var extrude = new ExtrudeEffect { Steps = 8 };
            Assert.AreEqual(9, Show("<k>x</k>", 40, null, LetteraStyle.ForTag(extrude, "k")).Engine.VertexCount / 4);
            extrude.Bevel = true;
            Assert.AreEqual(17, Show("<k>x</k>", 40, null, LetteraStyle.ForTag(extrude, "k")).Engine.VertexCount / 4);
        }

        [Test]
        public void ExtrudeRunsFromFarToNear()
        {
            var extrude = new ExtrudeEffect { Steps = 4, Offset = new Vector2(0.1f, 0), NearColor = new Color32(0, 0, 0, 255), FarColor = new Color32(255, 0, 0, 255) };
            TextEngine e = Show("<k>x</k>", 40, null, LetteraStyle.ForTag(extrude, "k")).Engine;
            Assert.AreEqual(255, e.Colors[0].r, "The farthest step comes first.");
            Assert.Greater(e.Colors[0].r, e.Colors[4].r);
            Assert.Greater(e.Colors[4].r, e.Colors[8].r);
            Assert.AreEqual(e.Vertices[16].x + 0.1f * 40, e.Vertices[0].x, 1e-3f, "The farthest is at the full offset.");
            Assert.AreEqual(e.Vertices[16].x + 0.025f * 40, e.Vertices[12].x, 1e-3f, "The nearest is one step from the face.");
        }

        // ------------------------------------------------------------- filters

        static bool IsGray(Color c) => Mathf.Abs(c.r - c.g) < 0.02f && Mathf.Abs(c.g - c.b) < 0.02f;

        [Test]
        public void GrayscaleMakesEveryPixelGray()
        {
            Show("<k><color=#F44>H</color></k>", 120, null, LetteraStyle.ForTag(new FilterEffect(new GrayscaleFilter()), "k"));
            Texture2D image = Render(W, H, Background);
            int lit = 0;
            foreach (Color c in image.GetPixels())
            {
                Assert.IsTrue(IsGray(c), c.ToString());
                if (c.r > 0.2f) lit++;
            }
            Assert.Greater(lit, 100);
        }

        [Test]
        public void IntensityZeroChangesNothing()
        {
            Show("<color=#F44>H</color>");
            var before = Render(W, H, Background).GetPixels();
            Show("<k=0><color=#F44>H</color></k>", 120, null, LetteraStyle.ForTag(new FilterEffect(new GrayscaleFilter()), "k"));
            var after = Render(W, H, Background).GetPixels();
            for (int i = 0; i < before.Length; i++) Assert.AreEqual(before[i].g, after[i].g, 0.01f);
        }

        [Test]
        public void FiltersComposeInStyleOrder()
        {
            var a = new float[20];
            var b = new float[20];
            var c = new float[20];
            new BrightnessFilter { Amount = 0.5f }.GetMatrix(a);
            new InvertFilter().GetMatrix(b);
            FilterAtlas.Compose(a, b, c);
            // Half brightness then invert: red 1 becomes 0.5, then 0.5.
            Assert.AreEqual(-0.5f, c[0], 1e-5f);
            Assert.AreEqual(1f, c[4], 1e-5f);

            Show("<k1><k2><color=#F00>H</color></k2></k1>", 120, null,
                LetteraStyle.ForTag(new FilterEffect(new BrightnessFilter { Amount = 0.5f }), "k1"),
                LetteraStyle.ForTag(new FilterEffect(new InvertFilter()), "k2"));
            Texture2D image = Render(W, H, Background);
            float brightest = 0;
            foreach (Color p in image.GetPixels()) brightest = Mathf.Max(brightest, p.g);
            Assert.Greater(brightest, 0.9f, "Red with half brightness then inverted has its green at 1.");
        }

        [Test]
        public void EqualFiltersShareARow()
        {
            var matrix = new float[20];
            new SepiaFilter().GetMatrix(matrix);
            Assert.AreEqual(FilterAtlas.RowOf(matrix), FilterAtlas.RowOf((float[])matrix.Clone()));
            Assert.Greater(FilterAtlas.RowOf(matrix), 0f);
        }

        [Test]
        public void FilterRespectsStyleOrder()
        {
            // The layer of a later style than the filter is out of its reach; the earlier one is inside it.
            var filter = new FilterEffect(new GrayscaleFilter());
            TextEngine e = Show("<f><early><late>H</late></early></f>", 40, null,
                LetteraStyle.ForTag(new StrokeEffect { Width = 0.02f }, "early", "#F00"),
                LetteraStyle.ForTag(filter, "f"),
                LetteraStyle.ForTag(new StrokeEffect { Width = 0.04f }, "late", "#00F")).Engine;
            Assert.AreEqual(3, e.VertexCount / 4);
            Assert.Greater(e.Uv1[0].w, 0f, "Earlier than the filter.");
            Assert.AreEqual(0f, e.Uv1[4].w, "Later than the filter.");
            Assert.Greater(e.Uv1[8].w, 0f, "The face.");
        }

        [Test]
        public void EditingFromAScriptStaysAtMeshLevel()
        {
            var effect = new FilterEffect(new BrightnessFilter { Amount = 0.5f });
            LetteraWorldText text = Show("<k>H</k>", 40, null, LetteraStyle.ForTag(effect, "k"));
            int parses = text.ParseCount, shapes = text.ShapeCount;
            ((BrightnessFilter)effect.Filter).Amount = 0.25f;
            float before = text.Engine.Uv1[0].w;
            text.MarkEffectsDirty();
            text.ForceRebuild();
            Assert.AreNotEqual(before, text.Engine.Uv1[0].w);
            Assert.AreEqual(parses, text.ParseCount);
            Assert.AreEqual(shapes, text.ShapeCount);
        }

        [Test]
        public void NamedFilterInTheTag()
        {
            Assert.Greater(Show("<filter=1,sepia>H</filter>", 40).Engine.Uv1[0].w, 0f);
        }

        // ------------------------------------------------------------- union

        const string Word = "سلم";

        [Test]
        public void UnionMergesAConnectedWordIntoOneQuad()
        {
            int separate = Show(Word, 40, Arabic).Engine.VertexCount / 4;
            int merged = Show("<union>" + Word + "</union>", 40, Arabic).Engine.VertexCount / 4;
            Assert.Greater(separate, 1);
            Assert.AreEqual(1, merged);
        }

        [Test]
        public void UnionHasNoDarkerOverlapUnderTranslucentPaint()
        {
            // Without union the fill of overlapping glyphs adds up; with it, every inside pixel has one level.
            LevelsOf("<k>" + Word + "</k>", out int withoutLevels);
            LevelsOf("<union><k>" + Word + "</k></union>", out int withLevels);
            Assert.AreEqual(0, withLevels, "No pixel is brighter than the flat inside of the merged shape.");
            Assert.Greater(withoutLevels, 0, "Where glyphs overlap, translucent paint adds up.");
        }

        void LevelsOf(string markup, out int levels)
        {
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Solid(new Color32(255, 0, 0, 128)));
            Show(markup, 100, Arabic, LetteraStyle.ForTag(fill, "k"));
            Texture2D image = Render(W, H, Background);
            // The commonest bright color is the flat inside; anything clearly brighter is where two layers of paint add up.
            var counts = new int[256];
            foreach (Color32 c in image.GetPixels32()) counts[c.r]++;
            int flat = 100;
            for (int value = 100; value < 256; value++)
                if (counts[value] > counts[flat])
                    flat = value;
            levels = 0;
            for (int value = flat + 4; value < 256; value++) levels += counts[value];
        }

        [Test]
        public void LatinWithoutOverlapStaysSeparate()
        {
            Assert.AreEqual(3, Show("<union>HHH</union>", 40).Engine.VertexCount / 4);
        }

        [Test]
        public void UnionEntryIsReused()
        {
            Show("<union>" + Word + "</union>", 40, Arabic);
            int glyphs = GlyphAtlas.GlyphCount;
            Show("<union>" + Word + "</union>", 40, Arabic);
            Assert.AreEqual(glyphs, GlyphAtlas.GlyphCount);
        }

        // ------------------------------------------------------------- warp

        [Test]
        public void WarpIsStableForAPhaseAndChangesWithIt()
        {
            var warp = new WarpEffect { Amplitude = 0.03f, Scale = 0.2f };
            Show("<k>H</k>", 120, null, LetteraStyle.ForTag(warp, "k"));
            var first = Render(W, H, Background).GetPixels();
            var again = Render(W, H, Background).GetPixels();
            CollectionAssert.AreEqual(first, again);
            warp.Phase = 2;
            m_Last.MarkEffectsDirty();
            m_Last.ForceRebuild();
            var moved = Render(W, H, Background).GetPixels();
            CollectionAssert.AreNotEqual(first, moved);
            Show("H");
            var flat = Render(W, H, Background).GetPixels();
            CollectionAssert.AreNotEqual(first, flat);
        }

        [Test]
        public void ColorGlyphsAndPlainTextStayUnwarped()
        {
            TextEngine e = Show("<warp=0.02,0.2>H</warp>", 40).Engine;
            Assert.AreEqual(1f, e.Uv3[0].w);
            e = Show("H", 40).Engine;
            Assert.IsFalse(e.Styled);
        }

        // ------------------------------------------------------------- paint order

        [Test]
        public void GlyphMajorFinishesEachGlyphFirst()
        {
            TextEngine layer = Show("<stroke=#F00,0.05>AB</stroke>", 40).Engine;
            Assert.AreEqual(new Color32(255, 0, 0, 255), layer.Colors[4], "Layer major: both strokes come first.");

            var order = new PaintOrderEffect { Order = LetteraPaintOrder.GlyphMajor };
            TextEngine glyph = Show("<o><stroke=#F00,0.05>AB</stroke></o>", 40, null, LetteraStyle.ForTag(order, "o")).Engine;
            Assert.AreEqual(new Color32(255, 0, 0, 255), glyph.Colors[0]);
            Assert.AreEqual(new Color32(255, 255, 255, 255), glyph.Colors[4], "Glyph major: the first glyph's face comes before the second's stroke.");
            Assert.AreEqual(new Color32(255, 0, 0, 255), glyph.Colors[8]);
        }

        [Test]
        public void ReverseEndsWithTheFirstGlyph()
        {
            var order = new PaintOrderEffect { Order = LetteraPaintOrder.GlyphMajor, Reverse = true };
            LetteraWorldText text = Show("<o>AB</o>", 40, null, LetteraStyle.ForTag(order, "o"));
            TextEngine e = text.Engine;
            Assert.Greater(e.Vertices[0].x, e.Vertices[4].x, "The last glyph draws first.");
        }

        // ------------------------------------------------------------- custom material

        [Test]
        public void ReplaceDrawsTheMaterialInPlaceOfTheFace()
        {
            var material = new Material(LetteraMaterials.World);
            var effect = new MaterialEffect { Material = material };
            TextEngine e = Show("a<k>b</k>c", 40, null, LetteraStyle.ForTag(effect, "k")).Engine;
            Assert.AreEqual(3, e.VertexCount / 4, "No extra quad.");
            Assert.AreEqual(3, e.Segments.Count);
            Assert.AreSame(material, e.Segments[1].Custom);
            Object.DestroyImmediate(material);
        }

        [Test]
        public void KeepDrawsBoth()
        {
            var material = new Material(LetteraMaterials.World);
            var effect = new MaterialEffect { Material = material, Order = LetteraMaterialOrder.Keep };
            TextEngine e = Show("<k>b</k>", 40, null, LetteraStyle.ForTag(effect, "k")).Engine;
            Assert.AreEqual(2, e.VertexCount / 4);
            Assert.IsNull(e.Segments[0].Custom);
            Assert.AreSame(material, e.Segments[1].Custom);
            Object.DestroyImmediate(material);
        }

        [Test]
        public void NullMaterialLeavesTheTextAlone()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no material"));
            TextEngine e = Show("<k>b</k>", 40, null, LetteraStyle.ForTag(new MaterialEffect(), "k")).Engine;
            Assert.AreEqual(1, e.VertexCount / 4);
        }

        // ------------------------------------------------------------- curve

        [Test]
        public void GlyphsLieOnTheCircle()
        {
            LetteraWorldText text = Show("<curve=200>ABCDEFGHIJ</curve>", 20);
            Vector2 center = new Vector2(0, text.Lines[0].Baseline - 200);
            for (int i = 0; i < text.GlyphCount; i++)
            {
                LetteraGlyphInfo g = text.GetGlyph(i);
                float angle = text.Engine.AngleOf(i);
                // The middle of the glyph's baseline.
                Vector2 middle = new Vector2(g.X, g.Y) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * g.Advance * 0.5f;
                Assert.AreEqual(200f, Vector2.Distance(middle, center), 1f, "glyph " + i);
                Vector2 outward = (middle - center).normalized;
                Vector2 tangent = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Assert.AreEqual(0f, Vector2.Dot(outward, tangent), 0.02f, "Turned to the tangent, glyph " + i);
            }
        }

        [Test]
        public void TheSignFlipsTheBend()
        {
            LetteraWorldText up = Show("<curve=200>ABCDE</curve>", 20);
            float upEnd = up.GetGlyph(0).Y - up.GetGlyph(2).Y;
            LetteraWorldText down = Show("<curve=-200>ABCDE</curve>", 20);
            float downEnd = down.GetGlyph(0).Y - down.GetGlyph(2).Y;
            Assert.Less(upEnd, 0f, "A positive radius bends the ends down.");
            Assert.Greater(downEnd, 0f);
        }

        [Test]
        public void ZeroRadiusIsNoCurve()
        {
            LetteraWorldText plain = Show("ABC", 20);
            float x = plain.GetGlyph(1).X;
            LetteraWorldText text = Show("<curve=0>ABC</curve>", 20);
            Assert.AreEqual(x, text.GetGlyph(1).X, 1e-3f);
            Assert.AreEqual(0f, text.Engine.AngleOf(1));
        }

        [Test]
        public void HitTestFindsARotatedGlyph()
        {
            LetteraWorldText text = Show("<curve=100>ABCDEFG</curve>", 20);
            LetteraGlyphInfo g = text.GetGlyph(6);
            float angle = text.Engine.AngleOf(6);
            Vector2 middle = new Vector2(g.X, g.Y) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * g.Advance * 0.5f;
            Assert.IsTrue(text.Engine.HitTestGlyph(middle, 0.1f, out LetteraHit hit));
            Assert.AreEqual(6, hit.GlyphIndex);
        }

        [Test]
        public void StrokeTurnsWithItsGlyph()
        {
            TextEngine e = Show("<curve=100><stroke=#F00,0.05>AB</stroke></curve>", 20).Engine;
            Vector3 strokeEdge = e.Vertices[2] - e.Vertices[1], faceEdge = e.Vertices[10] - e.Vertices[9];
            Assert.AreEqual(Mathf.Atan2(faceEdge.y, faceEdge.x), Mathf.Atan2(strokeEdge.y, strokeEdge.x), 1e-3f);
        }
    }
}
