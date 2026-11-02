namespace FirstSymbol.Lettera
{
    /// <summary>
    /// How a long text on a Canvas spends its work. Glyphs are placed and drawn only on the lines near what can show, and a query on any other line
    /// places that line for itself. When the text is set, only its first paragraphs and those near what shows are shaped and broken into lines; the
    /// rest are estimated and finished over the next frames, so the text's height and its scrollbar may move until then.
    /// </summary>
    public static class LetteraLongText
    {
        /// <summary>Whether a long text places and draws only the lines near what shows. Off, every glyph is placed and drawn, as before 1.0.</summary>
        public static bool VisibleLinesOnly = true;

        /// <summary>How many shaped glyphs a text needs before only its lines near what shows are placed.</summary>
        public static int FromGlyphs = 4096;

        /// <summary>How far past what shows lines are placed, on each side, in heights of what shows. Moving further places them again.</summary>
        public static float Margin = 0.5f;

        /// <summary>
        /// Whether a long text leaves the paragraphs far from what shows for later frames. Until they are done, its height is an estimate; Lines, carets,
        /// selection, hit tests and measures finish them first and are exact. Off, every paragraph is shaped and broken when the text is set.
        /// </summary>
        public static bool FinishLater = true;

        /// <summary>How long finishing may take in a frame, for all texts together, in milliseconds. At least one step of about 2000 characters runs.</summary>
        public static float FinishMillisecondsPerFrame = 2f;
    }
}
