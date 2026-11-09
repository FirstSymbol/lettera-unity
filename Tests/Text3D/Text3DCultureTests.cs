using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>A solid's tags with fractions build the same solids on a machine whose culture writes a decimal comma.</summary>
    public class Text3DCultureTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        static readonly string[] Cultures = { "ru-RU", "de-DE" };

        static readonly string[] Markups =
        {
            "<bevel=round,0.03,0.03>OB</bevel>",
            "<bevel=chamfer,0.05,-0.04>OB</bevel>",
            "<taper=0.5>OB</taper>",
            "<depth=0.4>OB</depth>",
        };

        byte[] Bytes(string markup, string culture)
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                LetteraSolidCache.Clear();
                var go = new GameObject("Solid", typeof(RectTransform));
                Created.Add(go);
                ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 500);
                LetteraText3D solid = go.AddComponent<LetteraText3D>();
                solid.Font = Latin;
                solid.FontSize = 20;
                solid.WordWrap = false;
                solid.ShareMesh = false;
                solid.Text = markup;
                solid.ForceRebuild();
                var all = new List<byte>();
                foreach (LetteraSolidMeshBuilder.Placement place in solid.Builder.Placed)
                {
                    foreach (LetteraSolidVertex v in place.Entry.Vertices)
                    {
                        all.AddRange(BitConverter.GetBytes(v.Position.x));
                        all.AddRange(BitConverter.GetBytes(v.Position.y));
                        all.AddRange(BitConverter.GetBytes(v.Position.z));
                    }
                    foreach (int i in place.Entry.Indices) all.AddRange(BitConverter.GetBytes(i));
                }
                return all.ToArray();
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        [Test]
        public void SolidTagsBuildTheSameInEveryCulture([ValueSource(nameof(Markups))] string markup)
        {
            byte[] expected = Bytes(markup, "en-US");
            Assert.Greater(expected.Length, 0);
            foreach (string name in Cultures)
                CollectionAssert.AreEqual(expected, Bytes(markup, name), $"{markup} in {name}");
        }

        [Test]
        public void AFractionChangesTheSolid()
        {
            CollectionAssert.AreNotEqual(Bytes("<bevel=round,0.03,0.03>OB</bevel>", "ru-RU"), Bytes("<bevel=round,0,0>OB</bevel>", "ru-RU"), "0.03 was read as 0");
        }
    }
}
