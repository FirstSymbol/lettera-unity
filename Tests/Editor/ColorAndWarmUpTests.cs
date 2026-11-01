using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A change of the text's color with no mesh pass, and warming shaders up ahead of time.</summary>
    public class ColorAndWarmUpTests : TextFixture
    {
        static long MeshPassesDuring(LetteraWorldText text, Color color)
        {
            System.Array.Clear(TextEngine.PassTicks, 0, TextEngine.PassTicks.Length);
            TextEngine.TimePasses = true;
            text.Color = color;
            text.ForceRebuild();
            TextEngine.TimePasses = false;
            return TextEngine.PassTicks[4];
        }

        [Test]
        public void TheSecondColorChangeRunsNoMeshPassAndGivesTheSameVertices()
        {
            LetteraWorldText text = Make("Plain words");
            text.ForceRebuild();
            Assert.AreEqual(0, MeshPassesDuring(text, Color.red), "a plain text's first change needs no pass either");
            Assert.AreEqual(0, MeshPassesDuring(text, Color.green), "a later change runs a mesh pass");
            Color32 shown = text.Engine.Colors[0];
            Assert.AreEqual(0, shown.r);
            Assert.AreEqual(255, shown.g);

            // What a full pass gives is what this gave.
            LetteraWorldText fresh = Make("Plain words");
            fresh.Color = Color.green;
            fresh.ForceRebuild();
            for (int i = 0; i < text.Engine.VertexCount; i++) Assert.AreEqual(fresh.Engine.Colors[i], text.Engine.Colors[i]);
        }

        [Test]
        public void AColorTagKeepsItsColor()
        {
            LetteraWorldText text = Make("<color=#0000ff>blue</color> rest");
            text.ForceRebuild();
            MeshPassesDuring(text, Color.red);
            Assert.AreEqual(0, MeshPassesDuring(text, Color.green));
            Assert.AreEqual(255, text.Engine.Colors[0].b, "the tag's glyph");
            Assert.AreEqual(0, text.Engine.Colors[0].g);
            Assert.AreEqual(255, text.Engine.Colors[(text.Engine.VertexCount - 1)].g, "the rest");
        }

        [Test]
        public void ALineInTheTextsColorStillFollowsIt()
        {
            LetteraWorldText text = Make("<u>underlined</u>");
            text.ForceRebuild();
            Assert.Greater(MeshPassesDuring(text, Color.red), 0);
            Assert.Greater(MeshPassesDuring(text, Color.green), 0, "a line takes the color too, so this has to draw");
        }

        [Test]
        public void AWavingTextKeepsTheNewColorAcrossPoses()
        {
            LetteraWorldText text = Make("<wave>moving</wave>");
            text.ForceRebuild();
            MeshPassesDuring(text, Color.red);
            MeshPassesDuring(text, Color.green);
            LetteraMotionHub.Tick(0.1f, 0.1f);
            Assert.AreEqual(255, text.Engine.Colors[0].g);
            Assert.AreEqual(0, text.Engine.Colors[0].r);
        }

        [Test]
        public void AlphaAndColorTogetherStayRight()
        {
            LetteraWorldText text = Make("Plain");
            text.ForceRebuild();
            MeshPassesDuring(text, new Color(1, 0, 0, 1));
            MeshPassesDuring(text, new Color(0, 1, 0, 0.5f));
            Assert.AreEqual(255, text.Engine.Colors[0].g);
            Assert.AreEqual(127, text.Engine.Colors[0].a, 1);
        }

        // ------------------------------------------------------------- warm-up

        [Test]
        public void TiersMapToExactlyTheirVariants()
        {
            Assert.AreEqual(2, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.UiPlain | LetteraSurfaceTier.UiStyled).Count);
            Assert.AreEqual(6, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.All).Count);
            Assert.AreEqual(4, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.UiPlain | LetteraSurfaceTier.UiStyled, new LetteraWarmUpOptions { Clip = true }).Count);
            Assert.AreEqual(1, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.WorldPlain, new LetteraWarmUpOptions { Clip = true, AlphaClip = true }).Count, "only the UI shaders clip by keyword");
        }

        [Test]
        public void NoneDoesNothing()
        {
            LetteraWarmUp warmUp = LetteraShaderWarmUp.Run(LetteraSurfaceTier.None);
            Assert.IsTrue(warmUp.IsDone);
            Assert.AreEqual(0, warmUp.VariantCount);
        }

        [Test]
        public void ARunTouchesEveryVariantWithNoErrorAndLeavesNoTarget()
        {
            RenderTexture before = RenderTexture.active;
            LetteraWarmUp warmUp = LetteraShaderWarmUp.Run(LetteraSurfaceTier.All, new LetteraWarmUpOptions { Clip = true, AlphaClip = true });
            Assert.IsTrue(warmUp.IsDone);
            Assert.AreEqual(10, warmUp.VariantsDone);
            Assert.AreEqual(before, RenderTexture.active);
        }

        [Test]
        public void AProgressiveRunAdvancesByItsCountAndEndsOnce()
        {
            LetteraWarmUp warmUp = LetteraShaderWarmUp.RunProgressively(LetteraSurfaceTier.All, 2);
            int done = 0;
            warmUp.Done += () => done++;
            Assert.AreEqual(0, warmUp.VariantsDone);
            LetteraMotionHub.Tick(0.01f, 0.01f);
            Assert.AreEqual(2, warmUp.VariantsDone);
            for (int i = 0; i < 5; i++) LetteraMotionHub.Tick(0.01f, 0.01f);
            Assert.IsTrue(warmUp.IsDone);
            Assert.AreEqual(1, done);
        }
    }
}
