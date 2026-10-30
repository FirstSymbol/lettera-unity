using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class StyleFoundationTests : TextFixture
    {
        sealed class Recorder : LetteraEffect
        {
            public readonly List<string> Raw = new List<string>();
            public readonly List<string> First = new List<string>();
            public readonly List<string> Second = new List<string>();

            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
            {
                Raw.Add(slots.Raw);
                First.Add(slots[0]);
                Second.Add(slots[1]);
            }
        }

        LetteraWorldText Recording(string markup, string defaultParameter, out Recorder recorder)
        {
            LetteraWorldText text = Make("");
            recorder = new Recorder();
            text.Styles.Add(LetteraStyle.ForTag(recorder, "k", defaultParameter));
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void CommaInsideParenthesesStaysInTheSlot()
        {
            Recording("<k=rgb(9,8,7),x>a</k>", null, out Recorder r);
            Assert.AreEqual("rgb(9,8,7)", r.First[0]);
            Assert.AreEqual("x", r.Second[0]);
        }

        [Test]
        public void UnbalancedParenthesisIsOrdinaryText()
        {
            Recording("<k=(a,b>c</k>", null, out Recorder r);
            Assert.AreEqual("(a", r.First[0]);
            Assert.AreEqual("b", r.Second[0]);
        }

        [Test]
        public void RawValueIsKept()
        {
            Recording("<k=(p;(r;s));9,2>a</k>", null, out Recorder r);
            Assert.AreEqual("(p;(r;s));9,2", r.Raw[0]);
        }

        [Test]
        public void QuotedDefaultIsOneSlot()
        {
            Recording("<k>a</k>", "\"a,b\",c", out Recorder r);
            Assert.AreEqual("a,b", r.First[0]);
            Assert.AreEqual("c", r.Second[0]);
        }

        [Test]
        public void EmptyDefaultSlotFallsThrough()
        {
            Recording("<k>a</k>", ",c", out Recorder r);
            Assert.IsNull(r.First[0]);
            Assert.AreEqual("c", r.Second[0]);
        }

        [TestCase("12", 12, LetteraLength.Unit.Bare, false)]
        [TestCase("12px", 12, LetteraLength.Unit.Pixels, false)]
        [TestCase("0.5em", 0.5f, LetteraLength.Unit.Em, false)]
        [TestCase("40%", 40, LetteraLength.Unit.Percent, false)]
        [TestCase("+2", 2, LetteraLength.Unit.Bare, true)]
        [TestCase("-0.1em", -0.1f, LetteraLength.Unit.Em, true)]
        public void LengthTokens(string token, float value, LetteraLength.Unit kind, bool delta)
        {
            Assert.IsTrue(LetteraLength.TryParse(token, out LetteraLength length));
            Assert.AreEqual(value, length.Value, 1e-5f);
            Assert.AreEqual(kind, length.Kind);
            Assert.AreEqual(delta, length.IsDelta);
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("NaNpx")]
        [TestCase("Infinity")]
        [TestCase("1e30")]
        public void BadLengthsAreAbsent(string token) => Assert.IsFalse(LetteraLength.TryParse(token, out _));

        [Test]
        public void LengthResolvesBareNumbersByTheParametersRule()
        {
            var bare = new LetteraLength(2);
            Assert.AreEqual(80f, bare.ToUnits(40, 0, true));
            Assert.AreEqual(2f, bare.ToUnits(40, 0, false));
            Assert.AreEqual(30f, new LetteraLength(10, LetteraLength.Unit.Percent).ToUnits(40, 300, false));
        }

        enum Sample { Alpha, Beta, Bravo }

        [Test]
        public void SlotsReadTypedValues()
        {
            var slots = new LetteraSlots(new[] { "b", "alpha", "on", "nope", null }, new[] { null, null, null, null, "7" });
            Assert.AreEqual(Sample.Alpha, slots.Enum(0, Sample.Alpha), "An initial two members share is not a match.");
            Assert.AreEqual(Sample.Alpha, slots.Enum(1, Sample.Beta));
            Assert.IsTrue(slots.Bool(2, false));
            Assert.IsTrue(slots.Bool(3, true));
            Assert.AreEqual(7, slots.Int(4, 0));
            Assert.AreEqual(3f, slots.Float(5, 3f));
        }

        [Test]
        public void EffectsReapplyWithoutMatchersOrShaping()
        {
            LetteraWorldText text = Make("");
            var color = new ColorEffect { Color = new Color32(255, 0, 0, 255) };
            text.Styles.Add(LetteraStyle.ForWholeText(color));
            text.MarkStylesDirty();
            text.Text = "abc";
            text.ForceRebuild();
            int parses = text.ParseCount, shapes = text.ShapeCount;

            color.Color = new Color32(0, 255, 0, 255);
            text.MarkEffectsDirty();
            text.ForceRebuild();

            Assert.AreEqual(parses, text.ParseCount);
            Assert.AreEqual(shapes, text.ShapeCount);
            Assert.AreEqual(new Color32(0, 255, 0, 255), text.Engine.Colors[0]);
        }

        [Test]
        public void ReapplyAtShapeLevelShapesOnce()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForWholeText(new ColorEffect()));
            text.MarkStylesDirty();
            text.Text = "abc";
            text.ForceRebuild();
            int parses = text.ParseCount, shapes = text.ShapeCount;
            text.MarkEffectsDirty(DirtyLevel.Shape);
            text.ForceRebuild();
            Assert.AreEqual(parses, text.ParseCount);
            Assert.AreEqual(shapes + 1, text.ShapeCount);
        }

        [Test]
        public void CloneKeepsFieldsAndIsIndependent()
        {
            var original = new LetteraStyle { Matcher = new SpanMatcher(1, 2), Effect = new OutlineEffect { Width = 0.12f }, DefaultParameter = "#FFF" };
            LetteraStyle copy = original.Clone();
            ((OutlineEffect)copy.Effect).Width = 0.05f;
            ((SpanMatcher)copy.Matcher).Spans.Add(new Vector2Int(5, 1));
            Assert.AreEqual(0.12f, ((OutlineEffect)original.Effect).Width);
            Assert.AreEqual(1, ((SpanMatcher)original.Matcher).Spans.Count);
            Assert.AreEqual("#FFF", copy.DefaultParameter);
        }

        sealed class AssetHolder : LetteraEffect
        {
            public Texture2D Texture;
            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) { }
        }

        [Test]
        public void CloneKeepsAssetReferences()
        {
            var texture = new Texture2D(1, 1);
            var copy = (AssetHolder)new AssetHolder { Texture = texture }.Clone();
            Assert.AreSame(texture, copy.Texture);
            Object.DestroyImmediate(texture);
        }
    }
}
