using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>One glyph of a font set apart: its advance, where it is drawn, its size and how finely it is rasterized.</summary>
    [Serializable]
    public struct LetteraGlyphOverride
    {
        [Tooltip("The character this override is for. A font whose file changes keeps the override on the same character.")]
        public int Codepoint;

        [Tooltip("Font units added to the glyph's advance.")]
        public int Advance;

        [Tooltip("Font units the glyph is drawn moved by, right and up. Its advance stays.")]
        public Vector2Int Offset;

        [Tooltip("The size the glyph is drawn at, from its origin. 1 is as designed; its advance stays.")]
        [Min(0)] public float Scale;

        [Tooltip("Atlas pixels per em this glyph is rasterized at, for a detailed glyph that needs more or a plain one that needs fewer. 0 takes what the text asks.")]
        [Min(0)] public int PixelsPerEm;

        public static LetteraGlyphOverride For(int codepoint) => new LetteraGlyphOverride { Codepoint = codepoint, Scale = 1 };
    }

    /// <summary>What an asset changes in its face's glyphs, read while shaping and when the atlas makes a glyph. Built only when something is set.</summary>
    internal sealed class FaceTuning
    {
        internal struct Glyph
        {
            public int Advance, OffsetX, OffsetY;
            public float Scale;
            public int PixelsPerEm;

            public bool Shapes => Advance != 0 || OffsetX != 0 || OffsetY != 0;
            public bool Draws => Scale != 1 || PixelsPerEm > 0;
        }

        public int SpacingOffset;
        public int SpaceWidth;
        public Dictionary<uint, Glyph> Glyphs;

        /// <summary>Some glyph is drawn larger, smaller or at its own density.</summary>
        public bool Draws;

        /// <summary>What the atlas holds for this face depends on these values, so a face whose value changes makes its glyphs again.</summary>
        public int DrawKey;

        public static FaceTuning Build(FontFace face, int spacingOffset, int spaceWidth, List<LetteraGlyphOverride> overrides)
        {
            bool any = spacingOffset != 0 || spaceWidth > 0;
            Dictionary<uint, Glyph> glyphs = null;
            int key = 17;
            if (overrides != null && face.Regular != null && !face.IsVirtual)
            {
                foreach (LetteraGlyphOverride o in overrides)
                {
                    if (o.Codepoint <= 0) continue;
                    uint id = LetteraNative.lt_font_glyph(face.Regular.Font, (uint)o.Codepoint, 0);
                    if (id == 0) continue;
                    var glyph = new Glyph { Advance = o.Advance, OffsetX = o.Offset.x, OffsetY = o.Offset.y, Scale = o.Scale > 0 ? o.Scale : 1, PixelsPerEm = o.PixelsPerEm };
                    if (!glyph.Shapes && !glyph.Draws) continue;
                    glyphs = glyphs ?? new Dictionary<uint, Glyph>();
                    glyphs[id] = glyph;
                    if (glyph.Draws) key = key * 31 + (int)id * 7 + glyph.Scale.GetHashCode() * 3 + glyph.PixelsPerEm;
                }
            }
            if (!any && glyphs == null) return null;
            var tuning = new FaceTuning { SpacingOffset = spacingOffset, SpaceWidth = spaceWidth, Glyphs = glyphs, DrawKey = key };
            if (glyphs != null)
                foreach (Glyph glyph in glyphs.Values)
                    tuning.Draws |= glyph.Draws;
            return tuning;
        }

        public bool TryDraw(uint glyph, out Glyph found)
        {
            found = default;
            return Draws && Glyphs.TryGetValue(glyph, out found) && found.Draws;
        }
    }
}
