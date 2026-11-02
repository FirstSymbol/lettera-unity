namespace FirstSymbol.Lettera
{
    /// <summary>
    /// How a long text on a Canvas spends its work. Every line is still broken and measured, so its height, its lines and every index stay exact;
    /// glyphs are placed and drawn only on the lines near what can show, and a query on any other line places that line for itself.
    /// </summary>
    public static class LetteraLongText
    {
        /// <summary>Whether a long text places and draws only the lines near what shows. Off, every glyph is placed and drawn, as before 1.0.</summary>
        public static bool VisibleLinesOnly = true;

        /// <summary>How many shaped glyphs a text needs before only its lines near what shows are placed.</summary>
        public static int FromGlyphs = 4096;

        /// <summary>How far past what shows lines are placed, on each side, in heights of what shows. Moving further places them again.</summary>
        public static float Margin = 0.5f;
    }
}
