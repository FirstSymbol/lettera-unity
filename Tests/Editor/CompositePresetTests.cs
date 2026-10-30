using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class CompositePresetTests : TextFixture
    {
        sealed class Recorder : LetteraEffect
        {
            public readonly List<string> First = new List<string>();
            public readonly List<string> Raw = new List<string>();

            public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
            {
                First.Add(slots[0]);
                Raw.Add(slots.Raw);
            }
        }

        LetteraWorldText With(string markup, LetteraEffect effect, string tag = "k", string defaults = null)
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(effect, tag, defaults));
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        // ------------------------------------------------------------- the grammar

        [Test]
        public void EachChildGetsItsSegment()
        {
            var a = new Recorder();
            var b = new Recorder();
            var c = new Recorder();
            With("<k=(mailto:a@b.io);teal;wavy>x</k>", new CompositeEffect(a, b, c));
            Assert.AreEqual("mailto:a@b.io", a.First[0]);
            Assert.AreEqual("teal", b.First[0]);
            Assert.AreEqual("wavy", c.First[0]);
        }

        [Test]
        public void BareValueReachesTheFirstChild()
        {
            var a = new Recorder();
            var b = new Recorder();
            With("<k=mailto:a@b.io>x</k>", new CompositeEffect(a, b));
            Assert.AreEqual("mailto:a@b.io", a.First[0]);
            Assert.IsNull(b.First[0]);
        }

        [Test]
        public void NestedCompositeTakesItsListInParentheses()
        {
            var p = new Recorder();
            var r = new Recorder();
            var s = new Recorder();
            var nine = new Recorder();
            With("<k=(p;(r;s));9>x</k>", new CompositeEffect(new CompositeEffect(p, new CompositeEffect(r, s)), nine));
            Assert.AreEqual("p", p.First[0]);
            Assert.AreEqual("r", r.First[0]);
            Assert.AreEqual("s", s.First[0]);
            Assert.AreEqual("9", nine.First[0]);
        }

        [Test]
        public void QuotedGroupIsLiteral()
        {
            var a = new Recorder();
            With("<k=\"(p;r)\">x</k>", new CompositeEffect(a));
            Assert.AreEqual("(p;r)", a.First[0]);
        }

        [Test]
        public void UnbalancedParenthesisIsOrdinary()
        {
            var a = new Recorder();
            With("<k=(a>x</k>", new CompositeEffect(a));
            Assert.AreEqual("(a", a.First[0]);
        }

        [Test]
        public void CommaInsideParenthesesStaysInTheSegment()
        {
            var a = new Recorder();
            With("<k=rgb(9,8,7)>x</k>", new CompositeEffect(a));
            Assert.AreEqual("rgb(9,8,7)", a.First[0]);
        }

        [Test]
        public void NonCompositeTreatsSemicolonAsOrdinary()
        {
            LetteraWorldText text = Make("<color=#F00;x>a</color>");
            Assert.AreEqual(new Color32(255, 255, 255, 255), text.Engine.Colors[0], "An unreadable token falls back to the field.");
        }

        [Test]
        public void DefaultParameterAddressesChildren()
        {
            var a = new Recorder();
            var b = new Recorder();
            With("<k>x</k>", new CompositeEffect(a, b), "k", "one;two");
            Assert.AreEqual("one", a.First[0]);
            Assert.AreEqual("two", b.First[0]);
        }

        [Test]
        public void TagSegmentBeatsDefaultSegment()
        {
            var a = new Recorder();
            var b = new Recorder();
            With("<k=;tag>x</k>", new CompositeEffect(a, b), "k", "one;two");
            Assert.AreEqual("one", a.First[0]);
            Assert.AreEqual("tag", b.First[0]);
        }

        [Test]
        public void ChildrenDrawAsTheirOwnLayers()
        {
            LetteraWorldText text = With("<k>x</k>", new CompositeEffect(new StrokeEffect(), new StrokeEffect { Width = 0.05f }, new ColorEffect { Color = new Color32(255, 0, 0, 255) }));
            Assert.AreEqual(3, text.Engine.VertexCount / 4);
            Assert.AreEqual(new Color32(255, 0, 0, 255), text.Engine.Colors[8]);
        }

        [Test]
        public void DefaultAnchorTagIsALink()
        {
            LetteraWorldText text = Make("<a=https://a.io>x</a>");
            Assert.AreEqual(new Color32(77, 163, 255, 255), text.Engine.Colors[0]);
            Assert.Greater(text.Engine.VertexCount / 4, 1, "It has an underline.");
            var values = new List<(int, int, string)>();
            text.Engine.GetPrimaryValues<CompositeEffect>(values);
            Assert.AreEqual("https://a.io", values[0].Item3);
        }

        // ------------------------------------------------------------- clone

        [Test]
        public void CloneKeepsNestedChildrenAndIsIndependent()
        {
            var stroke = new StrokeEffect { Width = 0.2f };
            var composite = new CompositeEffect(new CompositeEffect(stroke));
            var copy = (CompositeEffect)composite.Clone();
            var inner = (CompositeEffect)copy.Children[0];
            ((StrokeEffect)inner.Children[0]).Width = 0.5f;
            Assert.AreEqual(0.2f, stroke.Width);
        }

        [Test]
        public void CloneKeepsTextureAndLibraryReferences()
        {
            var texture = new Texture2D(1, 1);
            var library = ScriptableObject.CreateInstance<LetteraPaintLibrary>();
            var fill = new FillEffect { Library = library, Catalog = LetteraPaintCatalog.Library };
            fill.SetPaint(LetteraPaint.Of(texture));
            var copy = (FillEffect)new CompositeEffect(fill).Clone().ToChild();
            Assert.AreSame(library, copy.Library);
            Assert.AreSame(texture, copy.Paint.Inline.Texture);
            copy.Paint.Inline.Color = new Color32(1, 2, 3, 4);
            Assert.AreNotEqual(copy.Paint.Inline.Color, fill.Paint.Inline.Color);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(library);
        }

        // ------------------------------------------------------------- presets

        LetteraEffectPreset Preset(LetteraEffect root)
        {
            var preset = ScriptableObject.CreateInstance<LetteraEffectPreset>();
            preset.Root = root;
            return preset;
        }

        [Test]
        public void TwoTextsGetIndependentInstances()
        {
            LetteraEffectPreset preset = Preset(new StrokeEffect { Width = 0.1f });
            var a = new PresetEffect(preset);
            var b = new PresetEffect(preset);
            ((StrokeEffect)a.Instance).Width = 0.3f;
            Assert.AreEqual(0.1f, ((StrokeEffect)b.Instance).Width);
            Assert.AreEqual(0.1f, ((StrokeEffect)preset.Root).Width, "Nothing leaks back to the asset.");
            UnityEngine.Object.DestroyImmediate(preset);
        }

        [Test]
        public void EditingTheAssetReachesItsUsers()
        {
            LetteraEffectPreset preset = Preset(new ColorEffect { Color = new Color32(255, 0, 0, 255) });
            LetteraWorldText text = With("<k>x</k>", new PresetEffect(preset));
            Assert.AreEqual(new Color32(255, 0, 0, 255), text.Engine.Colors[0]);
            ((ColorEffect)preset.Root).Color = new Color32(0, 255, 0, 255);
            preset.NotifyChanged();
            text.ForceRebuild();
            Assert.AreEqual(new Color32(0, 255, 0, 255), text.Engine.Colors[0]);
            UnityEngine.Object.DestroyImmediate(preset);
        }

        [Test]
        public void EmptyPresetDoesNothingAndWarnsOnce()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no preset"));
            LetteraWorldText text = With("<k>x</k>", new PresetEffect());
            Assert.AreEqual(1, text.Engine.VertexCount / 4);
            text.MarkStylesDirty();
            text.ForceRebuild();
        }

        [Test]
        public void SelfReferenceDoesNotHang()
        {
            var preset = ScriptableObject.CreateInstance<LetteraEffectPreset>();
            preset.name = "Loop" + Guid.NewGuid().ToString("N");
            preset.Root = new CompositeEffect(new PresetEffect(preset));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("reaches itself"));
            Assert.DoesNotThrow(() => With("<k>x</k>", new PresetEffect(preset)));
            UnityEngine.Object.DestroyImmediate(preset);
        }

        [Test]
        public void GetEffectFindsAChildInAPreset()
        {
            var color = new ColorEffect();
            LetteraEffectPreset preset = Preset(new CompositeEffect(color));
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(new PresetEffect(preset), "k"));
            text.MarkStylesDirty();
            Assert.IsNotNull(text.GetEffect<ColorEffect>(), "Before the first rebuild.");
            text.Text = "<k>x</k>";
            text.ForceRebuild();
            Assert.IsNotNull(text.GetEffect<ColorEffect>(), "After it.");
            UnityEngine.Object.DestroyImmediate(preset);
        }

        // ------------------------------------------------------------- queries and holds

        LetteraWorldText Warned(out ColorEffect effect)
        {
            effect = new ColorEffect { Color = new Color32(9, 9, 9, 255) };
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(effect, "w"));
            text.MarkStylesDirty();
            text.Text = "<w #warn=#F00>a</w> <w #warn=#0F0>b</w> <w=#00F>c</w> <w>d</w>";
            text.ForceRebuild();
            return text;
        }

        static readonly Color32 Blue = new Color32(0, 0, 255, 255), Red = new Color32(255, 0, 0, 255), Green = new Color32(0, 255, 0, 255);

        [Test]
        public void QueryFiltersAndKeepsTextOrder()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            var found = new List<LetteraRangeInfo>();
            text.Ranges(effect).WithLabel("warn").Collect(found);
            Assert.AreEqual(2, found.Count);
            Assert.Less(found[0].Start, found[1].Start);
            found.Clear();
            text.Ranges(effect).Bare().Collect(found);
            Assert.AreEqual(1, found.Count);
            found.Clear();
            text.Ranges(effect).WithToken(0, "#00F").Collect(found);
            Assert.AreEqual(1, found.Count);
            found.Clear();
            text.Ranges(effect).Skip(1).Take(2).Collect(found);
            Assert.AreEqual(2, found.Count);
            found.Clear();
            text.Ranges(effect).Overlapping(0, 1).Collect(found);
            Assert.AreEqual(1, found.Count);
            found.Clear();
            text.Ranges(effect).Inside(0, 3).Collect(found);
            Assert.AreEqual(2, found.Count);
            found.Clear();
            text.Ranges(effect).Matching(r => r.End - r.Start == 1).Collect(found);
            Assert.AreEqual(4, found.Count);
        }

        [Test]
        public void HoldReplacesAtMeshLevelAndReleaseRestores()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            TextEngine e = text.Engine;
            int parses = text.ParseCount, shapes = text.ShapeCount;
            LetteraHold<Color32> hold = text.Ranges(effect).WithLabel("warn").Hold<Color32>(0);
            hold.Value = Blue;
            text.ForceRebuild();
            Assert.AreEqual(Blue, e.Colors[0]);
            Assert.AreEqual(Blue, e.Colors[4]);
            Assert.AreEqual(parses, text.ParseCount);
            Assert.AreEqual(shapes, text.ShapeCount);
            Assert.AreEqual(new Color32(9, 9, 9, 255), effect.Color, "The serialized field never changes.");

            hold.Release();
            text.ForceRebuild();
            Assert.AreEqual(Red, e.Colors[0]);
            Assert.AreEqual(Green, e.Colors[4]);
        }

        [Test]
        public void NewMatchesTakeTheHeldValue()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            LetteraHold<Color32> hold = text.Ranges(effect).WithLabel("warn").Hold<Color32>(0);
            hold.Value = Blue;
            int changes = 0;
            hold.Changed += () => changes++;
            text.Text = "<w #warn=#F00>a</w> <w #warn=#0F0>b</w> <w #warn=#F0F>c</w>";
            text.ForceRebuild();
            Assert.AreEqual(3, hold.Count);
            Assert.AreEqual(Blue, text.Engine.Colors[8]);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void OneMemberCanDiffer()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            LetteraHold<Color32> hold = text.Ranges(effect).WithLabel("warn").Hold<Color32>(0);
            hold.Value = Blue;
            hold.SetValue(1, Green);
            text.ForceRebuild();
            Assert.AreEqual(Blue, text.Engine.Colors[0]);
            Assert.AreEqual(Green, text.Engine.Colors[4]);
        }

        [Test]
        public void MultiplyMultipliesAColor()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            LetteraHold<Color32> hold = text.Ranges(effect).WithLabel("warn").Hold<Color32>(0, LetteraHoldComposition.Multiply);
            hold.Value = new Color32(128, 255, 255, 255);
            text.ForceRebuild();
            Assert.AreEqual(128, text.Engine.Colors[0].r, 1);
            Assert.AreEqual(0, text.Engine.Colors[0].g);
        }

        [Test]
        public void SuspendKeepsMembershipButDropsValues()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            LetteraHold<Color32> hold = text.Ranges(effect).WithLabel("warn").Hold<Color32>(0);
            hold.Value = Blue;
            hold.Suspended = true;
            text.ForceRebuild();
            Assert.AreEqual(Red, text.Engine.Colors[0]);
            Assert.AreEqual(2, hold.Count);
        }

        [Test]
        public void DisposedHoldThrows()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            LetteraHold<Color32> hold = text.Ranges(effect).Hold<Color32>(0);
            hold.Dispose();
            Assert.Throws<ObjectDisposedException>(() => hold.Value = Blue);
            Assert.Throws<ObjectDisposedException>(() => _ = hold.Count);
        }

        [Test]
        public void EnumsCannotAdd()
        {
            LetteraWorldText text = Warned(out ColorEffect effect);
            Assert.Throws<ArgumentException>(() => text.Ranges(effect).Hold<string>(0, LetteraHoldComposition.Add));
        }

        [Test]
        public void HoldOnAFloatComposesOverTheField()
        {
            var stroke = new StrokeEffect { Width = 0.1f };
            LetteraWorldText text = With("<k>x</k>", stroke);
            LetteraHold<float> hold = text.Ranges(stroke).Hold<float>(3, LetteraHoldComposition.Add);
            hold.Value = 0.5f;
            text.ForceRebuild();
            Assert.Greater(text.Engine.Uv1[0].y, 0f, "The field's softness of 0 plus the held amount.");
        }
    }

    static class CompositeTestExtensions
    {
        public static LetteraEffect ToChild(this LetteraEffect composite) => ((CompositeEffect)composite).Children[0];
    }
}
