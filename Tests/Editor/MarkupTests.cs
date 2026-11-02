using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class MarkupTests : TextFixture
    {
        string Clean(string source) => Make(source).CleanText;

        [TestCase("<b>x</b>", "x")]
        [TestCase("<B>x</B>", "x")]
        [TestCase("<b>unclosed", "unclosed")]
        [TestCase("<b>a<i>b</b>c</i>", "abc")]
        [TestCase("<color=#F00>r</color>", "r")]
        [TestCase("<color=\"#F00\">r</color>", "r")]
        [TestCase("<b #first>x</b>", "x")]
        public void TagsAreStripped(string source, string expected) => Assert.AreEqual(expected, Clean(source));

        [TestCase("<unknown>x</unknown>")]
        [TestCase("<b")]
        [TestCase("< b>")]
        [TestCase("<>")]
        [TestCase("a < b > c")]
        [TestCase("</b>")]
        public void WhatNoStyleClaimsStaysLiteral(string source) => Assert.AreEqual(source, Clean(source));

        [Test]
        public void EscapeProtectsMarkup()
        {
            Assert.AreEqual("<b>x", Clean(@"\<b>x"));
            Assert.AreEqual(@"\", Clean(@"\\"));
            Assert.AreEqual(@"\a", Clean(@"\a"), "a backslash before a letter is literal");
        }

        [Test]
        public void NoParseKeepsContentLiteral()
        {
            Assert.AreEqual("<b>x</b>", Clean("<noparse><b>x</b></noparse>"));
            Assert.AreEqual("<b>rest", Clean("<noparse><b>rest"), "a missing closer protects the rest");
        }

        [Test]
        public void TurningOffTheGlobalPresetShowsTagsLiterally()
        {
            LetteraWorldText text = Make("<b>x</b>");
            text.UseGlobalStylePreset = false;
            text.ForceRebuild();
            Assert.AreEqual("<b>x</b>", text.CleanText);
        }

        [Test]
        public void ComponentStylesWinOverThePreset()
        {
            LetteraWorldText text = Make("<b>x</b>");
            int weight = text.GetGlyph(0).Size > 0 ? 1 : 0;
            Assert.AreEqual(1, weight);
            text.Styles.Add(LetteraStyle.ForTag(new SizeEffect(), "b", "200%"));
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.AreEqual(40f, text.GetGlyph(0).Size, 0.01f, "the component's own <b> claims the tag first");
        }

        [Test]
        public void SizesResolveAgainstTheEnclosingSize()
        {
            LetteraWorldText text = Make("<size=40>a<size=50%>b</size></size><size=+10>c</size><size=2em>d</size>", size: 20);
            Assert.AreEqual(40f, text.GetGlyph(0).Size, 0.01f);
            Assert.AreEqual(20f, text.GetGlyph(1).Size, 0.01f);
            Assert.AreEqual(30f, text.GetGlyph(2).Size, 0.01f);
            Assert.AreEqual(40f, text.GetGlyph(3).Size, 0.01f);
        }

        [Test]
        public void UnparsableParametersAreIgnored()
        {
            LetteraWorldText text = Make("<size=abc>a</size>", size: 20);
            Assert.AreEqual(20f, text.GetGlyph(0).Size, 0.01f);
            Assert.DoesNotThrow(() => Make("<color=nonsense>a</color><shadow=,,,,>b</shadow><outline=,>c</outline>"));
        }

        [Test]
        public void StyleDefaultsFillMissingSlots()
        {
            LetteraWorldText text = Make("<big>a</big><big=300%>b</big>", size: 10);
            text.Styles.Add(LetteraStyle.ForTag(new SizeEffect(), "big", "200%"));
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.AreEqual(20f, text.GetGlyph(0).Size, 0.01f);
            Assert.AreEqual(30f, text.GetGlyph(1).Size, 0.01f);
        }

        [Test]
        public void WholeTextAndSpanMatchersApply()
        {
            LetteraWorldText text = Make("abc", size: 10);
            text.Styles.Add(LetteraStyle.ForWholeText(new SizeEffect(), "200%"));
            text.Styles.Add(LetteraStyle.ForSpan(new SizeEffect(), 1, 1, "300%"));
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.AreEqual(20f, text.GetGlyph(0).Size, 0.01f);
            Assert.AreEqual(60f, text.GetGlyph(1).Size, 0.01f, "the span lies inside the whole-text range");
            Assert.AreEqual(20f, text.GetGlyph(2).Size, 0.01f);
        }

        [Test]
        public void MalformedLanguageIsIgnored()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not a well-formed"));
            // Each distinct tag warns only once per session, so this one must be new.
            string tag = "not a tag " + System.Guid.NewGuid().ToString("N");
            Assert.DoesNotThrow(() => Make($"<lang={tag}>x</lang>"));
        }

        [Test]
        public void IndexMappingWalksThroughMarkup()
        {
            LetteraWorldText text = Make("<b>ab</b>c");
            Assert.AreEqual(3, text.SourceIndexFromRendered(0));
            Assert.AreEqual(8 + 1, text.SourceIndexFromRendered(2));
            Assert.AreEqual(0, text.RenderedIndexFromSource(3));
            Assert.AreEqual(2, text.RenderedIndexFromSource(9));
        }

        [Test]
        public void GetEffectFindsThisTextsEffects()
        {
            LetteraWorldText text = Make("x");
            Assert.IsTrue(text.TryGetEffect(out BoldEffect bold));
            Assert.IsNotNull(bold);
        }
    }

    public class RebuildTests : TextFixture
    {
        [Test]
        public void SameTextDoesNothing()
        {
            LetteraWorldText text = Make("x");
            text.SetText("Score: 10");
            text.ForceRebuild();
            int rebuilds = text.RebuildCount;
            for (int i = 0; i < 5; i++)
            {
                text.SetText("Score: 10");
                text.ForceRebuild();
            }
            Assert.AreEqual(rebuilds, text.RebuildCount);
        }

        [Test]
        public void RuntimeTextLeavesTheSerializedFieldAlone()
        {
            LetteraWorldText text = Make("serialized");
            text.SetText("runtime");
            text.ForceRebuild();
            Assert.AreEqual("serialized", text.Text);
            Assert.AreEqual("runtime", text.CleanText);
            Assert.AreEqual(LetteraTextOverride.RuntimeText, text.TextOverrideReason);
            text.Text = "again";
            text.ForceRebuild();
            Assert.AreEqual("again", text.CleanText);
            Assert.AreEqual(LetteraTextOverride.None, text.TextOverrideReason);
        }

        [Test]
        public void AlphaChangeTouchesOnlyColors()
        {
            LetteraWorldText text = Make("x");
            LetteraChanges changes = LetteraChanges.None;
            text.Rebuilt += c => changes = c;
            Vector3 before = text.Engine.Vertices[0];
            text.Color = new Color(1, 1, 1, 0.5f);
            Assert.AreEqual(DirtyLevel.Alpha, text.Engine.Dirty);
            text.ForceRebuild();
            Assert.AreEqual(before, text.Engine.Vertices[0]);
            Assert.AreEqual(127, text.Engine.Colors[0].a, 1);
        }

        [Test]
        public void AFirstColorChangeByTintDrawsWhatAFullPassDraws()
        {
            LetteraWorldText tinted = Make("Plain <color=#00FF00>green</color> words");
            LetteraWorldText plain = Make("Plain words, all in the text's color");
            foreach (LetteraWorldText text in new[] { tinted, plain })
            {
                text.Color = new Color(0.2f, 0.4f, 0.8f, 0.5f);
                DirtyLevel level = text.Engine.Dirty;
                text.ForceRebuild();
                var colors = new Color32[text.Engine.VertexCount];
                System.Array.Copy(text.Engine.Colors, colors, colors.Length);
                text.SetDirty(DirtyLevel.Mesh);
                text.ForceRebuild();
                for (int i = 0; i < colors.Length; i++) Assert.AreEqual(text.Engine.Colors[i], colors[i], level + " vertex " + i);
                Assert.AreEqual(text == plain ? DirtyLevel.Tint : DirtyLevel.Mesh, level);
            }
        }

        [Test]
        public void ChangesRequestTheExpectedLevel()
        {
            LetteraWorldText text = Make("x");
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            Assert.AreEqual(DirtyLevel.Positions, text.Engine.Dirty);
            text.ForceRebuild();
            text.FontSize = 30;
            Assert.AreEqual(DirtyLevel.Layout, text.Engine.Dirty);
            text.ForceRebuild();
            // Every quad takes the text's color, so even the first change rewrites colors only.
            text.Color = Color.red;
            Assert.AreEqual(DirtyLevel.Tint, text.Engine.Dirty);
            text.ForceRebuild();
            text.Language = "ar";
            Assert.AreEqual(DirtyLevel.Shape, text.Engine.Dirty);
            text.ForceRebuild();
            text.Text = "y";
            Assert.AreEqual(DirtyLevel.Parse, text.Engine.Dirty);
        }

        [Test]
        public void SerializedWritesRaiseTheRightLevel()
        {
            LetteraWorldText text = Make("x");
            var serialized = new UnityEditor.SerializedObject(text);
            serialized.FindProperty("m_Data.FontSize").floatValue = 44;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            text.Engine.SyncSerializedFields();
            Assert.GreaterOrEqual(text.Engine.Dirty, DirtyLevel.Layout);
            text.ForceRebuild();
            Assert.AreEqual(44f, text.GetGlyph(0).Size, 0.01f);
        }

        [Test]
        public void HideAndShowDoNotRebuild()
        {
            LetteraWorldText text = Make("x");
            int rebuilds = text.RebuildCount;
            text.Hide();
            Assert.IsFalse(text.IsVisible);
            text.Show();
            text.ForceRebuild();
            Assert.AreEqual(rebuilds, text.RebuildCount);
        }

        [Test]
        public void ResolverOutputIsParsed()
        {
            LetteraWorldText text = Make("key");
            text.TextResolver = new UpperResolver();
            text.ForceRebuild();
            Assert.AreEqual("KEY", text.CleanText);
            Assert.AreEqual("key", text.Text);
            Assert.AreEqual(LetteraTextOverride.Resolver, text.TextOverrideReason);
        }

        sealed class UpperResolver : ILetteraTextResolver
        {
            public string Resolve(Component owner, string raw) => raw.ToUpperInvariant();
        }
    }
}
