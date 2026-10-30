using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class PaintLayerTests : TextFixture
    {
        static readonly Color32 Red = new Color32(255, 0, 0, 255);
        static readonly Color32 Blue = new Color32(0, 0, 255, 255);

        // Reach only grows and is shared, so an earlier test's wide stroke would change these thresholds.
        [SetUp]
        public void ClearAtlas() => GlyphAtlas.Clear();

        LetteraWorldText Styled(string markup, params LetteraStyle[] styles)
        {
            LetteraWorldText text = Make("");
            text.Styles.AddRange(styles);
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        LetteraWorldText Painted(string markup, LetteraPaint paint, float width = 1000)
        {
            var fill = new FillEffect();
            fill.SetPaint(paint);
            LetteraWorldText text = Make("", width);
            text.Styles.Add(LetteraStyle.ForTag(fill, "p"));
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        static int Quads(LetteraWorldText text) => text.Engine.VertexCount / 4;

        // ------------------------------------------------------------- 0.1.0 compatibility

        [Test]
        public void ShadowOutlineAndFaceKeepTheirOrderAndValues()
        {
            LetteraWorldText text = Make("<outline=#F00,0.1><shadow=#00F,0.1,-0.1,0.2>x</shadow></outline>");
            TextEngine e = text.Engine;
            Assert.AreEqual(3, Quads(text));
            Assert.AreEqual(Blue, e.Colors[0], "The shadow draws first.");
            Assert.AreEqual(Red, e.Colors[4], "The outline draws second.");
            Assert.AreEqual(new Color32(255, 255, 255, 255), e.Colors[8], "The face draws last.");
            Assert.AreEqual(0.5f - 0.1f / (2f * 0.1875f), e.Uv1[4].x, 1e-5f, "The outline's threshold.");
            Assert.AreEqual(0.1f, e.Uv1[0].y, 1e-5f, "The shadow's softness at the base spread.");
            Assert.AreEqual(0.5f, e.Uv1[8].x, 1e-5f);
            Assert.AreEqual(e.Vertices[8].x + 0.1f * 20f, e.Vertices[0].x, 1e-3f, "The shadow moves by its offset in em.");
            Assert.IsFalse(e.Styled, "Solid outer layers stay on the plain shader.");
        }

        // ------------------------------------------------------------- layer storage

        [Test]
        public void InnermostRangeOfOneEffectWins()
        {
            LetteraWorldText text = Make("<stroke=#000><stroke=#F00>x</stroke></stroke>");
            Assert.AreEqual(2, Quads(text));
            Assert.AreEqual(Red, text.Engine.Colors[0]);
        }

        [Test]
        public void TwoEffectsOnOneGlyphBothDraw()
        {
            LetteraWorldText text = Styled("<k1><k2>x</k2></k1>",
                LetteraStyle.ForTag(new StrokeEffect(), "k1", "#F00"),
                LetteraStyle.ForTag(new StrokeEffect(), "k2", "#00F"));
            Assert.AreEqual(3, Quads(text));
            Assert.AreEqual(Red, text.Engine.Colors[0], "Earlier styles draw first.");
            Assert.AreEqual(Blue, text.Engine.Colors[4]);
        }

        [Test]
        public void ThreeHundredDistinctRangesAllRender()
        {
            var markup = new System.Text.StringBuilder();
            for (int i = 0; i < 300; i++) markup.Append($"<outline=#{i % 256:X2}0000>a</outline>");
            LetteraWorldText text = Make(markup.ToString(), 100000);
            Assert.AreEqual(600, Quads(text));
            Assert.AreEqual(299 % 256, text.Engine.Colors[299 * 4].r);
        }

        // ------------------------------------------------------------- fill

        [Test]
        public void FirstFillReplacesTheFace()
        {
            int plain = Quads(Make("x"));
            LetteraWorldText text = Make("<fill=#00F>x</fill>");
            Assert.AreEqual(plain, Quads(text));
            Assert.AreEqual(Blue, text.Engine.Colors[0]);
        }

        [Test]
        public void SecondFillAddsOneQuad()
        {
            LetteraWorldText text = Styled("<k1><k2>x</k2></k1>",
                LetteraStyle.ForTag(new FillEffect(), "k1", "#F00"),
                LetteraStyle.ForTag(new FillEffect(), "k2", "#00F"));
            Assert.AreEqual(2, Quads(text));
            Assert.AreEqual(Red, text.Engine.Colors[0]);
            Assert.AreEqual(Blue, text.Engine.Colors[4]);
        }

        [Test]
        public void DefaultFillTakesTheGlyphColor()
        {
            LetteraWorldText text = Make("<color=#F00><fill>x</fill></color>");
            Assert.AreEqual(Red, text.Engine.Colors[0]);
        }

        [Test]
        public void TransparentFillLeavesHollowText()
        {
            LetteraWorldText text = Make("<fill=#0000><stroke=#FFF,0.05>O</stroke></fill>");
            Assert.AreEqual(2, Quads(text));
            Assert.AreEqual(0, text.Engine.Colors[4].a);
        }

        // ------------------------------------------------------------- stroke

        [Test]
        public void StrokeAlignmentSetsTheBand()
        {
            TextEngine outside = Make("<stroke=#000,0.1,outside>x</stroke>").Engine;
            Assert.AreEqual(0.5f - 0.1f / 0.375f, outside.Uv1[0].x, 1e-5f);
            Assert.AreEqual(0f, outside.Uv1[0].z);
            Assert.IsFalse(outside.Styled);

            TextEngine inside = Make("<stroke=#000,0.1,inside>x</stroke>").Engine;
            Assert.AreEqual(0.5f, inside.Uv1[4].x, 1e-5f, "A stroke with a part inside draws over the face.");
            Assert.AreEqual(0.5f + 0.1f / 0.375f, inside.Uv1[4].z, 1e-5f);
            Assert.IsTrue(inside.Styled);

            TextEngine center = Make("<stroke=#000,0.1,center>x</stroke>").Engine;
            Assert.AreEqual(0.5f - 0.05f / 0.375f, center.Uv1[4].x, 1e-5f);
            Assert.AreEqual(0.5f + 0.05f / 0.375f, center.Uv1[4].z, 1e-5f);
        }

        [Test]
        public void OutlineEqualsOutsideStroke()
        {
            TextEngine a = Make("<outline=#000,0.1>x</outline>").Engine;
            TextEngine b = Make("<stroke=#000,0.1,outside>x</stroke>").Engine;
            Assert.AreEqual(a.VertexCount, b.VertexCount);
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.AreEqual(a.Uv1[i], b.Uv1[i]);
                Assert.AreEqual(a.Uv0[i], b.Uv0[i]);
                Assert.AreEqual(a.Colors[i], b.Colors[i]);
            }
        }

        [Test]
        public void ZeroWidthStrokeEmitsNoQuad()
        {
            Assert.AreEqual(1, Quads(Make("<stroke=#000,0>x</stroke>")));
        }

        [Test]
        public void WideStrokeRaisesTheReachOnce()
        {
            LetteraWorldText text = Make("<stroke=#000,0.5>O</stroke>");
            TextEngine e = text.Engine;
            float threshold = e.Uv1[0].x;
            Assert.Greater(threshold, 0f, "The stroke's edge stays inside the distance field.");
            Assert.AreEqual(0.5f - 0.5f / 1.5f, threshold, 1e-4f, "A 0.5 em reach uses the 0.75 em tier.");

            int glyphs = GlyphAtlas.GlyphCount;
            text.MarkEffectsDirty();
            text.ForceRebuild();
            Assert.AreEqual(glyphs, GlyphAtlas.GlyphCount);
        }

        // ------------------------------------------------------------- shadow, glow, inner shadow

        [Test]
        public void ShadowOffsetFollowsFixedPixelSize()
        {
            var shadow = new ShadowEffect { Offset = new Vector2(0.1f, 0), FixedPixelSize = true };
            LetteraWorldText text = Styled("<k>a<size=50%>a</size></k>", LetteraStyle.ForTag(shadow, "k"));
            TextEngine e = text.Engine;
            float first = e.Vertices[0].x - e.Vertices[8].x, second = e.Vertices[4].x - e.Vertices[12].x;
            Assert.AreEqual(first, second, 1e-3f);

            shadow = new ShadowEffect { Offset = new Vector2(0.1f, 0) };
            text = Styled("<k>a<size=50%>a</size></k>", LetteraStyle.ForTag(shadow, "k"));
            e = text.Engine;
            first = e.Vertices[0].x - e.Vertices[8].x;
            second = e.Vertices[4].x - e.Vertices[12].x;
            Assert.AreEqual(first * 0.5f, second, 1e-3f);
        }

        [Test]
        public void ShadowGoesBelowAndGlowHasNoQuadAtZeroRadius()
        {
            Assert.AreEqual(1, Quads(Make("<glow=#F00,0>x</glow>")));
            Assert.AreEqual(2, Quads(Make("<glow=#F00,0.2>x</glow>")));
        }

        [Test]
        public void GlowFadesToZeroAtItsRadius()
        {
            TextEngine e = Make("<glow=#F00,0.3,1>x</glow>").Engine;
            float toSdf = 1f / (2f * 0.375f);
            float low = e.Uv1[0].x - e.Uv1[0].y, high = e.Uv1[0].x + e.Uv1[0].y;
            Assert.AreEqual(0.5f - 0.3f * toSdf, low, 1e-4f, "Alpha reaches zero at the radius.");
            Assert.AreEqual(0.5f, high, 1e-4f, "Alpha is full at the edge.");
        }

        [Test]
        public void InnerShadowDrawsAfterTheFaceWithItsOffset()
        {
            TextEngine e = Make("<inner-shadow=#F00,0.03,-0.03>x</inner-shadow>").Engine;
            Assert.IsTrue(e.Styled);
            Assert.AreEqual(4f, e.Uv0[4].w, "The second quad is the inner shadow.");
            Assert.Greater(e.Uv3[4].x, 0f);
            Assert.Less(e.Uv3[4].y, 0f);
        }

        // ------------------------------------------------------------- paint

        static LetteraGradient TwoStops => LetteraGradient.Of(Red, Blue);

        [Test]
        public void RangeSpanRisesAcrossGlyphs()
        {
            LetteraWorldText text = Painted("<p>ABCD</p>", LetteraPaint.Of(TwoStops, LetteraPaintSpan.Range));
            TextEngine e = text.Engine;
            Assert.IsTrue(e.Styled);
            float last = float.MinValue;
            for (int q = 0; q < 4; q++)
            {
                float left = e.Uv2[q * 4].x, right = e.Uv2[q * 4 + 3].x;
                Assert.Greater(right, left);
                Assert.Greater(left, last - 0.3f, "The ramp does not reset at a glyph.");
                last = right;
            }
            Assert.Greater(e.Uv2[12].x, e.Uv2[0].x);
        }

        [Test]
        public void GlyphSpanRestartsPerGlyph()
        {
            LetteraWorldText text = Painted("<p>HH</p>", LetteraPaint.Of(TwoStops, LetteraPaintSpan.Glyph));
            TextEngine e = text.Engine;
            Assert.AreEqual(e.Uv2[0].x, e.Uv2[4].x, 1e-4f);
            Assert.AreEqual(e.Uv2[3].x, e.Uv2[7].x, 1e-4f);
        }

        [Test]
        public void VerticalRangeSpanRisesAcrossLines()
        {
            LetteraWorldText text = Painted("<p>aaaa\naaaa</p>", LetteraPaint.Of(TwoStops, LetteraPaintSpan.Range, 90));
            TextEngine e = text.Engine;
            Assert.AreEqual(8, Quads(text));
            float firstLine = e.Uv2[1].x, secondLine = e.Uv2[29].x;
            Assert.Greater(firstLine, secondLine, "Going up is going forward at 90 degrees.");
        }

        [Test]
        public void ChainPrefersTagThenDefaultThenEffectThenSwatch()
        {
            var effect = new LetteraPaintSettings();
            var swatch = new LetteraPaintSettings { Angle = 10 };
            var source = LetteraPaint.Of(TwoStops);
            source.Settings = swatch;

            ResolvedPaint paint = LetteraPaintCatalogs.Resolve(source, effect, new LetteraSlots(new[] { null, null, null, null, null, "30" }, new[] { null, null, null, null, null, "20" }), 0, default, false);
            Assert.AreEqual(30f, paint.Angle);
            paint = LetteraPaintCatalogs.Resolve(source, effect, new LetteraSlots(null, new[] { null, null, null, null, null, "20" }), 0, default, false);
            Assert.AreEqual(20f, paint.Angle);
            paint = LetteraPaintCatalogs.Resolve(source, effect, default, 0, default, false);
            Assert.AreEqual(10f, paint.Angle);
            effect.Angle = 15;
            paint = LetteraPaintCatalogs.Resolve(source, effect, default, 0, default, false);
            Assert.AreEqual(15f, paint.Angle);
            effect.Angle = float.NaN;
            swatch.Angle = float.NaN;
            paint = LetteraPaintCatalogs.Resolve(source, effect, default, 0, default, false);
            Assert.AreEqual(0f, paint.Angle, "The final fallback.");
        }

        [Test]
        public void FinalFallbacks()
        {
            ResolvedPaint paint = LetteraPaintCatalogs.Resolve(LetteraPaint.Of(TwoStops), null, default, 0, default, false);
            Assert.AreEqual(LetteraPaintSpan.Block, paint.Span);
            Assert.AreEqual(LetteraPaintShape.Linear, paint.Shape);
            Assert.AreEqual(LetteraPaintFit.Stretch, paint.Fit);
            Assert.AreEqual(LetteraBlend.Normal, paint.Blend);
            Assert.AreEqual(1f, paint.Scale);
        }

        [TestCase(1, LetteraPaintEdge.Clamp)]
        [TestCase(2, LetteraPaintEdge.Repeat)]
        [TestCase(3, LetteraPaintEdge.Mirror)]
        public void GradientCodesCarryShapeAndEdge(int code, LetteraPaintEdge edge)
        {
            var source = LetteraPaint.Of(TwoStops);
            source.Settings.Edge = edge;
            Assert.AreEqual(code, LetteraPaintCatalogs.Resolve(source, null, default, 0, default, false).Code);
        }

        [Test]
        public void ConicIgnoresEdge()
        {
            var source = LetteraPaint.Of(TwoStops);
            source.Settings.Edge = LetteraPaintEdge.Repeat;
            source.Settings.Shape = LetteraPaintShape.Conic;
            Assert.AreEqual(7, LetteraPaintCatalogs.Resolve(source, null, default, 0, default, false).Code);
        }

        [Test]
        public void DegenerateGradientsActSolid()
        {
            ResolvedPaint one = LetteraPaintCatalogs.Resolve(LetteraPaint.Of(LetteraGradient.Of(Red)), null, default, 0, default, false);
            Assert.IsTrue(one.IsSolid);
            Assert.AreEqual(Red, one.Color);
            ResolvedPaint none = LetteraPaintCatalogs.Resolve(LetteraPaint.Of(new LetteraGradient()), null, default, 0, default, false);
            Assert.IsTrue(none.IsSolid);
            Assert.AreEqual(0, none.Color.a);
        }

        [Test]
        public void IdenticalGradientsShareARow()
        {
            float a = GradientAtlas.RowOf(LetteraGradient.Of(Red, Blue));
            float b = GradientAtlas.RowOf(LetteraGradient.Of(Red, Blue));
            float c = GradientAtlas.RowOf(LetteraGradient.Of(Blue, Red));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        [TestCase(LetteraGradientMode.Srgb, 128)]
        [TestCase(LetteraGradientMode.Linear, 188)]
        public void GradientModesInterpolateDifferently(LetteraGradientMode mode, int middle)
        {
            var gradient = LetteraGradient.Of(new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255));
            gradient.Mode = mode;
            var row = new Color32[257];
            GradientAtlas.Bake(gradient, row);
            Assert.AreEqual(middle, row[128].r, 2);
        }

        [Test]
        public void SteppedGradientsHaveHardStops()
        {
            var gradient = LetteraGradient.Of(Red, Blue);
            gradient.Mode = LetteraGradientMode.Stepped;
            var row = new Color32[256];
            GradientAtlas.Bake(gradient, row);
            Assert.AreEqual(Red, row[128]);
            Assert.AreEqual(Blue, row[255]);
        }

        [Test]
        public void FadeToTransparentKeepsItsColor()
        {
            var gradient = LetteraGradient.Of(Red, new Color32(0, 0, 0, 0));
            var row = new Color32[257];
            GradientAtlas.Bake(gradient, row);
            Assert.AreEqual(255, row[128].r, 1, "Premultiplied mixing does not darken towards transparent black.");
            Assert.AreEqual(128, row[128].a, 2);
        }

        // ------------------------------------------------------------- named paints

        [Test]
        public void SwatchNameResolvesFromTheOwnList()
        {
            var fill = new FillEffect { Catalog = LetteraPaintCatalog.Own };
            fill.OwnSwatches.Add(new LetteraSwatch("sunset", LetteraPaint.Of(TwoStops)));
            LetteraWorldText text = Styled("<k=sunset>x</k>", LetteraStyle.ForTag(fill, "k"));
            Assert.AreEqual(1f, text.Engine.Uv2[0].w);
        }

        [Test]
        public void UnknownNameUsesTheDefaultPaint()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("nosuch"));
            LetteraWorldText text = Make("<fill=nosuch>x</fill>");
            Assert.AreEqual(new Color32(255, 255, 255, 255), text.Engine.Colors[0]);
        }

        // ------------------------------------------------------------- segments

        [Test]
        public void TextureAndBlendGetTheirOwnSegmentsInOrder()
        {
            var texture = new Texture2D(2, 2);
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(texture));
            LetteraWorldText text = Styled("a<k>b</k>c", LetteraStyle.ForTag(fill, "k"));
            TextEngine e = text.Engine;
            Assert.AreEqual(3, e.Segments.Count);
            Assert.IsNull(e.Segments[0].Texture);
            Assert.AreSame(texture, e.Segments[1].Texture);
            Assert.IsNull(e.Segments[2].Texture);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void SolidAndGradientPaintsKeepOneSegment()
        {
            TextEngine e = Painted("a<p>b</p>c", LetteraPaint.Of(TwoStops)).Engine;
            Assert.AreEqual(1, e.Segments.Count);
        }

        [Test]
        public void MaterialVariantsAreShared()
        {
            Material a = LetteraMaterials.Variant(LetteraMaterials.World, true, LetteraBlend.Multiply, null);
            Material b = LetteraMaterials.Variant(LetteraMaterials.World, true, LetteraBlend.Multiply, null);
            Assert.AreSame(a, b);
            Assert.AreSame(LetteraMaterials.World, LetteraMaterials.Variant(LetteraMaterials.World, false, LetteraBlend.Normal, null));
            Assert.IsTrue(a.IsKeywordEnabled("LETTERA_STYLED"));
        }
    }
}
