using System;

namespace FirstSymbol.Lettera
{
    /// <summary>The glyph outlines of a Text 3D, as <see cref="LetteraGlyphOutlines"/> gives them for a flat text.</summary>
    public static class LetteraText3DOutlines
    {
        /// <summary>
        /// Reads the outlines of a Text 3D into a buffer, which is cleared first: the flat shapes of its glyphs, before depth and bevels, in
        /// its local space. Rebuilds the text if it is out of date. Returns the number of paths.
        /// </summary>
        public static int GetGlyphOutlines(this LetteraText3D text, LetteraOutlineBuffer into, LetteraOutlineOptions options = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (into == null) throw new ArgumentNullException(nameof(into));
            text.ForceRebuild();
            return text.Engine.CollectOutlines(into, options);
        }
    }
}
