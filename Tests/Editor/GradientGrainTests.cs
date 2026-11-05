using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>F7: a gradient with grain, chosen per paint, drawn by a variant of the styled shader that only such texts use.</summary>
    public class GradientGrainTests : TextFixture
    {
        static readonly Color32 Low = new Color32(200, 200, 200, 255), High = new Color32(216, 216, 216, 255);

        // Sixteen steps of 8 bits over the whole ramp: at 8 bits each step is a band 16 texels wide.
        static LetteraGradient Slow(bool grain)
        {
            LetteraGradient gradient = LetteraGradient.Of(Low, High);
            gradient.Grain = grain;
            return gradient;
        }

        LetteraWorldText Painted(string markup, LetteraPaint paint)
        {
            var fill = new FillEffect();
            fill.SetPaint(paint);
            LetteraWorldText text = Make("");
            text.Styles.Add(LetteraStyle.ForTag(fill, "p"));
            text.MarkStylesDirty();
            text.Text = markup;
            text.ForceRebuild();
            return text;
        }

        [Test]
        public void AGradientWithGrainTakesAFineRow()
        {
            ResolvedPaint plain = LetteraPaintCatalogs.Resolve(LetteraPaint.Of(Slow(false)), null, default, 0, default, false);
            ResolvedPaint grained = LetteraPaintCatalogs.Resolve(LetteraPaint.Of(Slow(true)), null, default, 0, default, false);
            Assert.IsFalse(plain.Grain);
            Assert.IsTrue(grained.Grain);
            Assert.AreEqual(plain.Code, grained.Code, "the same shape and edge");
            Assert.IsFalse(LetteraPaintCatalogs.Resolve(LetteraPaint.Of(SlowOne(true)), null, default, 0, default, false).Grain, "one stop is a solid color");

            Color[] row = GradientAtlas.FineRowPixels(grained.RampV);
            Assert.IsNotNull(row);
            var exact = new System.Collections.Generic.HashSet<float>();
            var bytes = new System.Collections.Generic.HashSet<int>();
            float worst = 0;
            for (int x = 0; x < row.Length; x++)
            {
                float ideal = (200 + 16f * x / (row.Length - 1)) / 255f;
                worst = Mathf.Max(worst, Mathf.Abs(row[x].r - ideal));
                exact.Add(row[x].r);
                bytes.Add(Mathf.RoundToInt(row[x].r * 255));
            }
            TestContext.WriteLine($"fine row: {exact.Count} values against {bytes.Count} at 8 bits, worst error {worst * 255:0.###} of a step");
            Assert.Less(worst * 255, 0.2f, "within a fraction of an 8-bit step");
            Assert.Greater(exact.Count, bytes.Count * 4, "steps between the 8-bit values");
        }

        static LetteraGradient SlowOne(bool grain)
        {
            LetteraGradient gradient = LetteraGradient.Of(Low);
            gradient.Grain = grain;
            return gradient;
        }

        [Test]
        public void OnlyTheGrainedQuadsCarryTheMark()
        {
            LetteraWorldText grained = Painted("<p>ab</p>c", LetteraPaint.Of(Slow(true)));
            TextEngine e = grained.Engine;
            Assert.IsTrue(e.Styled);
            Assert.IsTrue(e.Grained);
            Assert.AreEqual((float)(1 + ResolvedPaint.CodeGrain), e.Uv2[0].w, "a linear gradient that clamps, with grain");
            Assert.AreEqual((float)(1 + ResolvedPaint.CodeGrain), e.Uv2[4].w);
            Assert.AreEqual(0f, e.Uv2[8].w, "the unpainted letter");

            TextEngine plain = Painted("<p>ab</p>c", LetteraPaint.Of(Slow(false))).Engine;
            Assert.IsTrue(plain.Styled);
            Assert.IsFalse(plain.Grained, "a gradient without grain keeps the styled variant");
            Assert.AreEqual(1f, plain.Uv2[0].w);
            TextEngine bare = Make("abc").Engine;
            Assert.IsFalse(bare.Styled || bare.Grained);

            // A swatch's copy keeps its grain.
            Assert.IsTrue(LetteraPaint.Of(Slow(true)).Copy().Gradient.Grain);
        }

        [Test]
        public void TheGrainedVariantIsItsOwnAndSharedMaterial()
        {
            foreach (Material source in new[] { LetteraMaterials.World, LetteraMaterials.WorldLit, LetteraMaterials.Ui })
            {
                Material styled = LetteraMaterials.Variant(source, true, LetteraBlend.Normal, null);
                Material grained = LetteraMaterials.Variant(source, true, LetteraBlend.Normal, null, grained: true);
                Assert.AreNotSame(styled, grained, source.name);
                Assert.AreSame(grained, LetteraMaterials.Variant(source, true, LetteraBlend.Normal, null, grained: true));
                Assert.IsTrue(grained.IsKeywordEnabled("LETTERA_GRAINED"), source.name);
                Assert.IsFalse(grained.IsKeywordEnabled("LETTERA_STYLED"), "one keyword of the set");
                Assert.IsFalse(styled.IsKeywordEnabled("LETTERA_GRAINED"));
                Assert.IsTrue(LetteraMaterials.Variant(source, true, LetteraBlend.Multiply, null, grained: true).IsKeywordEnabled("LETTERA_GRAINED"), "with a blend too");
                Assert.AreSame(source, LetteraMaterials.Variant(source, false, LetteraBlend.Normal, null, grained: true), "no styled tier, no grain");
            }
            Material merged = LetteraMaterials.Variant(LetteraMaterials.Ui, true, LetteraBlend.Normal, null, true, false, true);
            Assert.IsTrue(merged.IsKeywordEnabled("LETTERA_MERGED"), "merged layers draw no gradient");
            Assert.IsFalse(merged.IsKeywordEnabled("LETTERA_GRAINED"));

            // A material of the user's own whose shader has no grained variant keeps its styled one.
            var own = new Material(Shader.Find("Unlit/Color"));
            Created.Add(own);
            Assert.IsFalse(LetteraMaterials.Variant(own, true, LetteraBlend.Normal, null, grained: true).IsKeywordEnabled("LETTERA_GRAINED"));
        }

        [Test]
        public void AWorldTextWithGrainDrawsWithTheGrainedVariant()
        {
            Painted("<p>abc</p>", LetteraPaint.Of(Slow(true)));
            LetteraWorldBatcher.Update();
            bool found = false;
            foreach (MeshRenderer renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
                foreach (Material material in renderer.sharedMaterials)
                    found |= material != null && material.IsKeywordEnabled("LETTERA_GRAINED");
            Assert.IsTrue(found);
        }

        [Test]
        public void WarmUpReadiesTheGrainedVariantsWhenAsked()
        {
            Assert.AreEqual(8, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.All).Count, "not unless asked");
            var grain = new LetteraWarmUpOptions { Grain = true };
            Assert.AreEqual(11, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.All, grain).Count, "one for each styled tier");
            Assert.AreEqual(1, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.UiPlain, grain).Count);
            Assert.AreEqual(6, LetteraShaderWarmUp.AllVariantsOf(LetteraSurfaceTier.UiStyled, new LetteraWarmUpOptions { Grain = true, Clip = true, AlphaClip = true }).Count);
            LetteraWarmUp warmUp = LetteraShaderWarmUp.Run(LetteraSurfaceTier.UiStyled | LetteraSurfaceTier.WorldStyled | LetteraSurfaceTier.WorldLitStyled, grain);
            Assert.IsTrue(warmUp.IsDone);
            Assert.AreEqual(6, warmUp.VariantCount);
        }

        // One quad over the whole target, painted with a horizontal ramp, drawn with a material's first pass.
        Texture2D DrawRamp(Material material, float rampV, float code, int width, int height)
        {
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            Created.Add(mesh);
            mesh.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            var white = new Color32(255, 255, 255, 255);
            mesh.SetColors(new[] { white, white, white, white });
            var solid = new Vector4(0, 0, 0, 3);
            mesh.SetUVs(0, new[] { solid, solid, solid, solid });
            mesh.SetUVs(1, new[] { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero });
            mesh.SetUVs(2, new[] { new Vector4(0, 0, rampV, code), new Vector4(0, 0, rampV, code), new Vector4(1, 0, rampV, code), new Vector4(1, 0, rampV, code) });
            mesh.SetUVs(3, new[] { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);

            ShaderUtil.allowAsyncCompilation = false;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            // The first draw of a new variant can come out empty.
            for (int i = 0; i < 2; i++)
            {
                var buffer = new CommandBuffer { name = "Lettera Grain Test" };
                buffer.SetRenderTarget(target);
                buffer.ClearRenderTarget(true, true, Color.black);
                buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                buffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                Graphics.ExecuteCommandBuffer(buffer);
                buffer.Dispose();
            }
            RenderTexture.active = target;
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Created.Add(picture);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(target);
            ShaderUtil.allowAsyncCompilation = true;
            return picture;
        }

        // Per column, the mean of a channel over the rows against the ramp's true value, and how much the rows vary there.
        static void Measure(Texture2D picture, out float worst, out float leastSpread)
        {
            Color32[] pixels = picture.GetPixels32();
            int width = picture.width, height = picture.height;
            worst = 0;
            leastSpread = float.MaxValue;
            for (int x = 8; x < width - 8; x++)
            {
                float sum = 0, squares = 0;
                for (int y = 0; y < height; y++)
                {
                    float r = pixels[y * width + x].r;
                    sum += r;
                    squares += r * r;
                }
                float mean = sum / height;
                float ideal = 200 + 16f * (x + 0.5f) / width;
                worst = Mathf.Max(worst, Mathf.Abs(mean - ideal));
                leastSpread = Mathf.Min(leastSpread, squares / height - mean * mean);
            }
        }

        [Test]
        public void GrainHidesTheBandsOfASlowGradient()
        {
            const int width = 512, height = 64;
            Material styled = LetteraMaterials.Variant(LetteraMaterials.World, true, LetteraBlend.Normal, null);
            Material grained = LetteraMaterials.Variant(LetteraMaterials.World, true, LetteraBlend.Normal, null, grained: true);
            float row = GradientAtlas.RowOf(Slow(false));
            float fineRow = GradientAtlas.FineRowOf(Slow(true));

            Texture2D plain = DrawRamp(styled, row, 1, width, height);
            Texture2D fine = DrawRamp(grained, fineRow, 1 + ResolvedPaint.CodeGrain, width, height);
            Texture2D plainInGrained = DrawRamp(grained, row, 1, width, height);
            Measure(plain, out float plainWorst, out float plainSpread);
            Measure(fine, out float fineWorst, out float fineSpread);
            TestContext.WriteLine($"{QualitySettings.activeColorSpace} space. Without grain: columns off the true ramp by up to {plainWorst:0.###} of a step, least variance {plainSpread:0.###}. " +
                                  $"With grain: {fineWorst:0.###}, least variance {fineSpread:0.###}.");
            Assert.Greater(plainWorst, 0.35f, "the 8-bit ramp draws in bands");
            Assert.Less(fineWorst, 0.25f, "with grain each column averages to the true ramp");
            Assert.Greater(fineSpread, 0.1f, "and every column carries grain: none is a flat band");

            // A gradient without grain in a text that has some draws as the styled variant draws it.
            Color32[] a = plain.GetPixels32(), b = plainInGrained.GetPixels32();
            int largest = 0;
            for (int i = 0; i < a.Length; i++) largest = Mathf.Max(largest, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
            Assert.LessOrEqual(largest, 1);
        }
    }
}
