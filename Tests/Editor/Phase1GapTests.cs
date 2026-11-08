using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class AtlasRecyclingTests : TextFixture
    {
        (int sdf, int color) m_SavedLimits;

        [SetUp]
        public void SetUp() => m_SavedLimits = GlyphAtlas.PageLimits;

        [TearDown]
        public void TearDown() => GlyphAtlas.SetPageLimits(m_SavedLimits.sdf, m_SavedLimits.color);

        [Test]
        public void AFullAtlasRecyclesInsteadOfGrowing()
        {
            FontFace face = Latin.LoadFace();
            GlyphAtlas.SetPageLimits(GlyphAtlas.SdfPageCount > 0 ? GlyphAtlas.SdfPageCount : 1, 1);
            int pages = Mathf.Max(GlyphAtlas.SdfPageCount, 1);

            // Each large tile covers much of a page, so a few hundred glyphs overflow any page count.
            int failures = 0;
            for (uint glyph = 1; glyph < 900; glyph++)
            {
                AtlasGlyph entry = GlyphAtlas.Get(face.Regular, glyph, 252);
                if (entry.Kind == AtlasGlyph.Sdf && entry.U1 <= entry.U0) failures++;
                if (glyph % 40 == 0) GlyphAtlas.Flush();
            }
            GlyphAtlas.Flush();
            Assert.AreEqual(0, failures);
            Assert.LessOrEqual(GlyphAtlas.SdfPageCount, pages, "the page count stays at the limit");
        }

        [Test]
        public void RecyclingAsksTextsToRebuild()
        {
            FontFace face = Latin.LoadFace();
            GlyphAtlas.SetPageLimits(Mathf.Max(GlyphAtlas.SdfPageCount, 1), 1);
            int cleared = 0;
            void Count() => cleared++;
            GlyphAtlas.Cleared += Count;
            for (uint glyph = 1; glyph < 900; glyph++) GlyphAtlas.Get(face.Regular, glyph, 248);
            GlyphAtlas.Flush();
            GlyphAtlas.Cleared -= Count;
            Assert.Greater(cleared, 0);
        }

        [Test]
        public void AVisibleTextSurvivesRecycling()
        {
            LetteraWorldText text = Make("Stays readable");
            FontFace face = Latin.LoadFace();
            GlyphAtlas.SetPageLimits(Mathf.Max(GlyphAtlas.SdfPageCount, 1), 1);
            for (uint glyph = 1; glyph < 900; glyph++) GlyphAtlas.Get(face.Regular, glyph, 244);
            GlyphAtlas.Flush();
            Assert.AreEqual(DirtyLevel.Mesh, text.Engine.Dirty, "it was told to place its glyphs again");
            text.ForceRebuild();
            Assert.Greater(text.Engine.VertexCount, 0);
        }
    }

    public class SharedStyleCopyTests : TextFixture
    {
        [Test]
        public void ChangingAnEffectAffectsOnlyThisText()
        {
            LetteraWorldText first = Make("<shadow>a</shadow>");
            LetteraWorldText second = Make("<shadow>a</shadow>");
            int layers = second.Engine.VertexCount;

            first.GetEffect<ShadowEffect>().Dilate = 0.1f;
            Assert.AreNotSame(first.GetEffect<ShadowEffect>(), second.GetEffect<ShadowEffect>());
            Assert.AreEqual(0f, second.GetEffect<ShadowEffect>().Dilate);
            Assert.AreEqual(layers, second.Engine.VertexCount);
        }

        [Test]
        public void CopiesKeepWorking()
        {
            LetteraWorldText text = Make("<size=200%>a</size>", size: 10);
            Assert.IsNotNull(text.GetEffect<SizeEffect>());
            text.SetDirty(DirtyLevel.Parse);
            text.ForceRebuild();
            Assert.AreEqual(20f, text.GetGlyph(0).Size, 0.01f);
        }

        [Test]
        public void AnEditedPresetReachesItsUsers()
        {
            var preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
            preset.Styles.Add(LetteraStyle.ForTag(new SizeEffect(), "big", "200%"));
            LetteraWorldText text = Make("<big>a</big>", size: 10);
            text.StylePresets.Add(preset);
            text.MarkStylesDirty();
            text.ForceRebuild();
            Assert.AreEqual(20f, text.GetGlyph(0).Size, 0.01f);

            preset.Styles[0].DefaultParameter = "300%";
            preset.NotifyChanged();
            text.ForceRebuild();
            Assert.AreEqual(30f, text.GetGlyph(0).Size, 0.01f);
            Object.DestroyImmediate(preset);
        }
    }

    public class BrowserEmojiTests : TextFixture
    {
        sealed class SolidRenderer : IEmojiRenderer
        {
            public readonly List<string> Rendered = new List<string>();

            public float Measure(string cluster, int pixels) => pixels * 1.25f;

            public void Render(string cluster, int pixels, int width, int height, int baselineY, byte[] output)
            {
                Rendered.Add(cluster);
                for (int i = 0; i < width * height * 4; i += 4)
                {
                    output[i] = 255;
                    output[i + 3] = 255;
                }
            }
        }

        SolidRenderer m_Renderer;

        [SetUp]
        public void SetUp()
        {
            m_Renderer = new SolidRenderer();
            BrowserEmoji.Renderer = m_Renderer;
        }

        [TearDown]
        public void TearDown()
        {
            BrowserEmoji.Renderer = null;
            BrowserEmoji.Reset();
        }

        static FontFace Face => BrowserEmoji.Face;

        [Test]
        public void EachClusterIsOneGlyphWithItsMeasuredAdvance()
        {
            int[] family = { 0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467 };
            uint a = BrowserEmoji.GlyphFor(family, out int advance);
            uint b = BrowserEmoji.GlyphFor(family, out _);
            Assert.AreEqual(a, b, "a repeated cluster reuses its glyph");
            Assert.AreEqual(1250, advance);
            Assert.AreEqual(char.ConvertFromUtf32(0x1F468) + "‍" + char.ConvertFromUtf32(0x1F469) + "‍" + char.ConvertFromUtf32(0x1F467), BrowserEmoji.ClusterText(a));
        }

        [Test]
        public void TheAtlasGetsTheBrowsersPixelsOnce()
        {
            int[] party = { 0x1F389 };
            uint glyph = BrowserEmoji.GlyphFor(party, out _);
            AtlasGlyph first = GlyphAtlas.Get(Face.Regular, glyph, 64);
            AtlasGlyph second = GlyphAtlas.Get(Face.Regular, glyph, 64);
            Assert.AreEqual(AtlasGlyph.Color, first.Kind);
            Assert.AreEqual(first.U0, second.U0);
            Assert.AreEqual(0, m_Renderer.Rendered.Count, "the rect comes at once, the pixels with the frame");
            GlyphAtlas.Flush();
            GlyphAtlas.Flush();
            Assert.AreEqual(1, m_Renderer.Rendered.Count);
            Assert.Greater(first.Top, 0);
            Assert.Less(first.Bottom, 0);
        }

        [Test]
        public void TheVirtualFaceHasNoFontBehindIt()
        {
            Assert.IsTrue(Face.IsVirtual);
            Assert.IsTrue(Face.IsColor);
            Assert.IsTrue(Face.HasGlyph(0x1F389));
            Assert.AreSame(Face.Regular, Face.GetInstance(700, 0.1f, 0.2f), "synthesis does not apply");
        }
    }

    public class AllocationTests : TextFixture
    {
        [Test]
        public void RuntimeTextUpdatesAllocateNothingAfterWarmUp()
        {
            LetteraWorldText text = Make("0000");
            var buffer = new char[4];

            void Update(int value)
            {
                for (int i = 3; i >= 0; i--)
                {
                    buffer[i] = (char)('0' + value % 10);
                    value /= 10;
                }
                text.SetText(buffer, 0, 4);
                text.ForceRebuild();
            }

            for (int i = 0; i < 300; i++) Update(i); // Warm-up: glyphs reach the atlas, buffers grow.

            // GC.GetAllocatedBytesForCurrentThread reads 0 on Unity's Mono, so this uses the live profiler counter.
            // The counter spans all threads by default, and the editor's own threads (import, package manager,
            // the test runner) allocate right after start; only the calling thread's allocations are the text's.
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1,
                ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            using var everyThread = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            // Control: the counter must see even a small allocation. The test stays within one frame.
            long control = recorder.CurrentValue;
            System.GC.KeepAlive(new byte[32]);
            Assert.GreaterOrEqual(recorder.CurrentValue - control, 32, "the allocation counter does not work here");

            // Take the quietest of a few windows.
            long allocated = long.MaxValue, anyThread = long.MaxValue;
            for (int window = 0; window < 5; window++)
            {
                long before = recorder.CurrentValue, beforeAll = everyThread.CurrentValue;
                for (int i = 0; i < 300; i++) Update(i);
                allocated = System.Math.Min(allocated, recorder.CurrentValue - before);
                anyThread = System.Math.Min(anyThread, everyThread.CurrentValue - beforeAll);
            }
            Assert.Less(allocated, 200, $"300 runtime text updates allocated {allocated} bytes on this thread ({anyThread} on all threads)");
        }
    }

    public class LitWorldTextTests : TextFixture
    {
        [Test]
        public void TheLitMaterialCompilesAndIsSupported()
        {
            Assert.IsTrue(LetteraMaterials.WorldLit.shader.isSupported);
            Assert.IsTrue(LetteraMaterials.World.shader.isSupported);
        }

        [Test]
        public void LitTextsDrawThroughTheLitMaterial()
        {
            LetteraWorldText text = Make("Lit");
            text.Lit = true;
            LetteraWorldBatcher.NotifyChanged(text);
            LetteraWorldBatcher.Update();
            bool found = false;
            foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
                found |= renderer.gameObject.name == "Lettera World Batch" && renderer.sharedMaterial == LetteraMaterials.WorldLit;
            Assert.IsTrue(found);
        }
    }
}
