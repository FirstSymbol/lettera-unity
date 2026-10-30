using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The checks the style constructor has to pass as a whole.</summary>
    public class Phase2ExitTests : TextFixture
    {
        static readonly string[] s_Vocabulary =
        {
            "b", "strong", "i", "em", "size=20", "color=#F00", "alpha=0.5", "font=x", "lang=ja", "u", "s", "strikethrough", "outline", "shadow",
            "stroke", "fill=#0F0", "glow", "inner-shadow", "mark", "extrude", "filter=1,sepia", "union", "warp", "curve=300",
            "uppercase", "lowercase", "smallcaps", "sup", "sub", "otf=onum", "axes=500", "size-match",
            "tracking=1", "word-spacing=2", "cell", "line-height=2", "para-spacing=3", "line-gap=4", "nobr", "indent=5", "align=center", "trim=capheight,baseline",
            "ellipsis", "truncate", "dir=rtl", "link=https://a.io", "a=https://a.io",
        };

        [Test]
        public void EveryDefaultTagIsClaimed()
        {
            foreach (string tag in s_Vocabulary)
            {
                string name = tag.Split('=')[0];
                LetteraWorldText text = Make($"<{tag}>ab</{name}>");
                StringAssert.DoesNotContain("<", text.CleanText, tag);
                Assert.AreEqual(2, text.CleanText.Length, tag);
            }
        }

        [Test]
        public void VoidAndStructuralDefaultsAreClaimed()
        {
            Assert.AreEqual("a\u2028b", Make("a<br>b").CleanText);
            Assert.AreEqual("<b>", Make("<noparse><b></noparse>").CleanText);
            Assert.AreEqual("a | b", Make("a<divider>b").CleanText);
            Assert.AreEqual("a", Make("<li>a</li>").CleanText);
            Assert.AreEqual("a", Make("<ruby=b>a</ruby>").CleanText);
        }

        [Test]
        public void OneOfEveryEffectOnMixedScriptsRendersInBothModes()
        {
            var markup = new StringBuilder("Latin مرحبا ");
            foreach (string tag in s_Vocabulary)
            {
                if (tag.StartsWith("dir", StringComparison.Ordinal) || tag.StartsWith("curve", StringComparison.Ordinal)) continue;
                markup.Append('<').Append(tag).Append('>');
            }
            markup.Append("Text مرحبا");
            foreach (LetteraRenderMode mode in new[] { LetteraRenderMode.SDF, LetteraRenderMode.MSDF })
            {
                LetteraWorldText text = Make("");
                text.Font = null;
                text.FontStack = Stack(Latin, Arabic);
                text.RenderMode = mode;
                text.Text = markup.ToString();
                text.ForceRebuild();
                Assert.Greater(text.Engine.VertexCount, 0, mode.ToString());
                Assert.IsNotNull(Render(200, 200, Color.black));
            }
        }

        [Test]
        public void AGradientRunsAcrossRestartsPerGlyphAndPerLineFromOneSwatch()
        {
            var library = ScriptableObject.CreateInstance<LetteraPaintLibrary>();
            library.Swatches.Add(new LetteraSwatch("sun", LetteraPaint.Of(LetteraGradient.Of(new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255)))));
            var fill = new FillEffect { Library = library, Catalog = LetteraPaintCatalog.Library };
            LetteraWorldText text = Make("", 100000);
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.MarkStylesDirty();
            text.Text = "<k=sun,0,0,range>HH</k>";
            text.ForceRebuild();
            Assert.Greater(text.Engine.Uv2[4].x, text.Engine.Uv2[0].x + 0.1f);
            text.Text = "<k=sun,0,0,glyph>HH</k>";
            text.ForceRebuild();
            Assert.AreEqual(text.Engine.Uv2[4].x, text.Engine.Uv2[0].x, 1e-4f);
            text.Text = "<k=sun,0,0,line>HH\nHH</k>";
            text.ForceRebuild();
            Assert.AreEqual(text.Engine.Uv2[8].x, text.Engine.Uv2[0].x, 1e-4f);
            UnityEngine.Object.DestroyImmediate(library);
        }

        [Test]
        public void ProtectionBeatsMarkdownUrlsAndTriggers()
        {
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new BoldEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new BareUrlMatcher(), new LinkEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), new ColorEffect()));
            text.MarkStylesDirty();
            text.Text = "<noparse>**a** https://a.io @b</noparse> **c** https://d.io @e";
            text.ForceRebuild();
            Assert.AreEqual("**a** https://a.io @b c https://d.io @e", text.CleanText);
            var links = new List<(int, int, string)>();
            text.Engine.GetPrimaryValues<LinkEffect>(links);
            Assert.AreEqual(1, links.Count);
            Assert.AreEqual("https://d.io", links[0].Item3);
        }

        // ------------------------------------------------------------- levels

        [TestCase(DirtyLevel.Mesh, 0, 0)]
        [TestCase(DirtyLevel.Positions, 0, 0)]
        [TestCase(DirtyLevel.Layout, 0, 0)]
        [TestCase(DirtyLevel.Shape, 0, 1)]
        [TestCase(DirtyLevel.Parse, 1, 1)]
        public void AParameterChangeRunsOnlyItsPasses(DirtyLevel level, int parses, int shapes)
        {
            LetteraWorldText text = Make("<stroke>abc</stroke>");
            int p = text.ParseCount, s = text.ShapeCount;
            text.MarkEffectsDirty(level);
            text.ForceRebuild();
            Assert.AreEqual(p + parses, text.ParseCount);
            Assert.AreEqual(s + shapes, text.ShapeCount);
        }

        // ------------------------------------------------------------- allocations

        [Test]
        public void AMeshRebuildWithLayersAllocatesNothingAfterWarmUp()
        {
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(Color.red, Color.blue), LetteraPaintSpan.Range));
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(fill, "k"));
            text.MarkStylesDirty();
            text.Text = "<k><stroke=#F00,0.05><shadow><glow=#0F0,0.1>Some text</glow></shadow></stroke></k>";
            text.ForceRebuild();
            for (int i = 0; i < 3; i++)
            {
                text.Engine.SetDirty(DirtyLevel.Mesh);
                text.ForceRebuild();
            }
            // GC.GetAllocatedBytesForCurrentThread reads 0 on Unity's Mono, so this uses the live profiler counter, with a control.
            using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long control = recorder.CurrentValue;
            GC.KeepAlive(new byte[32]);
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");
            // Reading the counter costs a few bytes, and the rest of the editor allocates at times too, so take the quietest of a few
            // windows. Even one small allocation per rebuild would show as at least 24 000 bytes.
            long quietest = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue;
                for (int i = 0; i < 1000; i++)
                {
                    text.Engine.SetDirty(DirtyLevel.Mesh);
                    text.Engine.Rebuild();
                }
                quietest = System.Math.Min(quietest, recorder.CurrentValue - before);
            }
            Assert.Less(quietest, 200);
        }

        // ------------------------------------------------------------- bad input

        static readonly string[] s_Pieces =
        {
            "<", ">", "</", "=", ",", ";", "(", ")", "\"", "'", "#", "/", "\\", " ", "\n", "a", "fi", "مر", "א", "🎉", "*", "**", "_", "`", "``", "[", "]", "](", "- ", "1. ", "@", "http://", "www.",
            "<ruby>", "</ruby>", "<rt>", "</rt>", "<rp>", "<li>", "</li>", "<divider>", "<divider=", "<mark>", "<fill=", "<stroke=#F00,9999>", "<glow=,,,>", "<curve=0>", "<curve=-1>",
            "<tracking=NaN>", "<tracking=1e30>", "<size=-5>", "<cell=0>", "<axes=~,~,~>", "<otf=toolongtag>", "<indent=-999>", "<align=nope>", "<trim=ink,ink>", "<extrude>", "<filter=2,sepia>", "<union>", "<warp=9,0>",
            "<u=,wavy,0>", "<u=#F00,dashed,1000>", "<link=", "<a=(;)>", "</b>", "<b>",
        };

        [Test]
        public void RandomMarkupNeverThrowsOrHangs()
        {
            // Odd names warn, which is right. Errors and exceptions, also those caught around an effect, are not.
            string failure = null;
            void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = failure ?? message;
            }
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            var random = new System.Random(12345);
            LetteraWorldText text = Make("", 200, 100);
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new BoldEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("*"), new ItalicEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("_"), new ItalicEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownListMatcher(), new ListEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new MarkdownLinkMatcher(), new LinkEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new CodeSpanMatcher(), new NoEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new BareUrlMatcher(), new LinkEffect()));
            text.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), new ColorEffect()));
            text.MarkStylesDirty();
            var markup = new StringBuilder();
            for (int round = 0; round < 400; round++)
            {
                markup.Clear();
                int count = random.Next(1, 30);
                for (int i = 0; i < count; i++) markup.Append(s_Pieces[random.Next(s_Pieces.Length)]);
                string value = markup.ToString();
                text.Text = value;
                try
                {
                    text.ForceRebuild();
                }
                catch (Exception e)
                {
                    Assert.Fail($"'{value}' threw {e}");
                }
            }
            Application.logMessageReceived -= OnLog;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            Assert.IsNull(failure, failure);
        }
    }
}
