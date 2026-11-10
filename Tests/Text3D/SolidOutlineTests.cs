using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>A Text 3D gives the flat outlines of its glyphs, where its solids stand.</summary>
    public class SolidOutlineTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_Made)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Made.Clear();
            LetteraSolidCache.Clear();
        }

        [Test]
        public void TheOutlinesLieWhereTheSolidsStand()
        {
            var go = new GameObject("Solid", typeof(RectTransform));
            m_Made.Add(go);
            ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 500);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = Latin;
            solid.FontSize = 40;
            solid.WordWrap = false;
            solid.ShareMesh = false;
            solid.Depth = 0.3f;
            solid.Text = "HOx";
            solid.ForceRebuild();

            var buffer = new LetteraOutlineBuffer();
            Assert.AreEqual(3, solid.GetGlyphOutlines(buffer));
            for (int i = 0; i < 3; i++)
            {
                Bounds body = SolidAnchorTests.GlyphBounds(solid, i, false);
                Rect flat = buffer.Paths[i].Bounds;
                Assert.AreEqual(body.center.x, flat.center.x, 0.5f, "glyph " + i);
                Assert.AreEqual(body.center.y, flat.center.y, 0.5f, "glyph " + i);
                Assert.AreEqual(body.size.x, flat.width, 1f, "glyph " + i);
                Assert.AreEqual(body.size.y, flat.height, 1f, "glyph " + i);
            }
        }
    }
}
