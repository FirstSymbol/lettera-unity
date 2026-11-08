using System;

namespace FirstSymbol.Lettera
{
    // Descriptors for the slots motions share. The ids and levels are what rules, cues and holds know them by; the rest is for the tag panel.
    internal static class LetteraMotionSlots
    {
        public const string Degrees = "°";

        public static LetteraParamInfo Rate(int slot, string field, string id = "Rate", string hint = null, string example = "1.5", bool essential = true) =>
            LetteraParamInfo.ForMotion(id, slot, LetteraParamKind.Float, DirtyLevel.Pose, hint ?? "Speed, in cycles a second: 1 repeats every second, 0.5 every two. Negative runs backwards.",
                step: 0.1f, decimals: 2, defaultField: field, essential: essential, example: example, unitLabel: "Hz");

        public static LetteraParamInfo Step(int slot, string field, bool essential = true) =>
            LetteraParamInfo.ForMotion("Step", slot, LetteraParamKind.Float, DirtyLevel.Pose, "Delay from one letter to the next, in cycles. 0 moves all together; 0.1 spreads one wave over ten letters.",
                step: 0.01f, decimals: 3, defaultField: field, essential: essential, example: "0.1", unitLabel: "cycles");

        /// <summary>A distance in em, as the motion's numbers are.</summary>
        public static LetteraParamInfo Em(string id, int slot, string hint, string field, string example, bool essential = false, string display = null) =>
            LetteraParamInfo.ForMotion(id, slot, LetteraParamKind.Float, DirtyLevel.Pose, hint, step: 0.01f, decimals: 3, defaultField: field, essential: essential, example: example, unitLabel: "em", display: display);

        public static LetteraParamInfo Angle(string id, int slot, string hint, string field, string example, bool essential = false) =>
            LetteraParamInfo.ForMotion(id, slot, LetteraParamKind.Float, DirtyLevel.Pose, hint, step: 1, decimals: 1, defaultField: field, essential: essential, example: example, unitLabel: Degrees);

        public static LetteraParamInfo Number(string id, int slot, string hint, string field, string example, bool essential = false, float min = float.NegativeInfinity, float max = float.PositiveInfinity,
            float step = 0.01f, int decimals = 3, string unit = null, DirtyLevel level = DirtyLevel.Pose) =>
            LetteraParamInfo.ForMotion(id, slot, LetteraParamKind.Float, level, hint, min, max, step, decimals, defaultField: field, essential: essential, example: example, unitLabel: unit);
    }
}
