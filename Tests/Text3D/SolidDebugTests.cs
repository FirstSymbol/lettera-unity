using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    public class SolidDebugTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        [Test]
        public unsafe void Dump()
        {
            FontFace face = Latin.LoadFace();
            foreach (uint cp in new uint[] { 0x2C67, 0x206B })
            {
                uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, cp, 0);
                var commands = new float[4096];
                int count;
                fixed (float* p = commands) count = LetteraNative.lt_glyph_outline(face.Regular.Font, glyph, p, commands.Length);
                var text = new StringBuilder("DUMP U+" + cp.ToString("X4") + " ");
                for (int i = 0; i < count; i++) text.Append(commands[i].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                Debug.Log(text.ToString());
                foreach (float width in new[] { 0f, 0.02f })
                {
                    LetteraBevel bevel = LetteraBevel.Default;
                    bevel.Width = width;
                    bevel.Height = width;
                    var recipe = new LetteraSolidRecipe { Front = bevel, Back = bevel, Tolerance = 0.001f, SmoothingCos = 0.866f, Faces = LetteraSolidFaces.All };
                    LetteraSolidEntry entry = LetteraSolidCache.Build(default, commands, count, 1f / face.Metrics.Upem, recipe);
                    Debug.Log($"DUMP width {width}: failed {entry.Failed}, caps dropped total {LetteraSolidCache.CapsDropped}, why {LetteraSolidCache.LastCapFailure}, face indices {entry.SurfaceStarts[1] - entry.SurfaceStarts[0]}");
                    entry.Release();
                }
            }
        }
    }
}
