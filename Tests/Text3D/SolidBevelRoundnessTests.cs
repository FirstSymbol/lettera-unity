using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>A round edge of any roundness keeps the face it rounds into.</summary>
    public class SolidBevelRoundnessTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        static float FaceArea(LetteraSolidEntry entry, out int indices)
        {
            int start = entry.SurfaceStarts[(int)LetteraSolidSurface.Face], end = entry.SurfaceStarts[(int)LetteraSolidSurface.Face + 1];
            indices = end - start;
            float area = 0;
            for (int t = start; t + 2 < end; t += 3)
            {
                float3 a = entry.Vertices[entry.Indices[t]].Position, b = entry.Vertices[entry.Indices[t + 1]].Position, c = entry.Vertices[entry.Indices[t + 2]].Position;
                area += math.length(math.cross(b - a, c - a)) * 0.5f;
            }
            return area;
        }

        static LetteraSolidEntry Build(string character, float roundness, out GameObject go)
        {
            go = new GameObject("Solid", typeof(RectTransform));
            LetteraText3D text = go.AddComponent<LetteraText3D>();
            text.Font = Latin;
            text.FontSize = 20;
            text.ShareMesh = false;
            text.Depth = 0.3f;
            LetteraBevel bevel = LetteraBevel.Default;
            bevel.Shape = LetteraBevelShape.Round;
            bevel.Width = 0.03f;
            bevel.Height = 0.03f;
            bevel.Roundness = roundness;
            text.FrontBevel = bevel;
            text.Text = character;
            text.ForceRebuild();
            return text.Builder.Placed[0].Entry;
        }

        [TestCase("C")]
        [TestCase("O")]
        [TestCase("l")]
        public void EveryRoundnessKeepsTheFace(string character)
        {
            LetteraSolidEntry quarter = Build(character, 0.5f, out GameObject made);
            float expected = FaceArea(quarter, out int quarterIndices);
            Object.DestroyImmediate(made);
            Assert.Greater(quarterIndices, 0);
            for (int step = 0; step <= 10; step++)
            {
                float roundness = step / 10f;
                LetteraSolidEntry entry = Build(character, roundness, out made);
                float area = FaceArea(entry, out int indices);
                foreach (LetteraSolidVertex v in entry.Vertices)
                    Assert.IsFalse(math.any(math.isnan(v.Position)) || math.any(math.isnan(v.Normal)), $"{character} at {roundness}: a vertex is not a number");
                Object.DestroyImmediate(made);
                Assert.Greater(indices, 0, $"{character} at {roundness} has a face");
                Assert.AreEqual(expected, area, expected * 0.02f, $"{character} at {roundness}: the face is as large as a quarter circle's");
            }
        }
    }
}
