using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A card for a text's formulas, which shows only when the text has one.</summary>
    internal static class LetteraMathInspector
    {
        internal static VisualElement Card(Action<List<LetteraFormulaInfo>> collect)
        {
            VisualElement list = LtUI.El("");
            VisualElement card = LtUI.Card("Math", list);
            var formulas = new List<LetteraFormulaInfo>();
            string shown = null;
            void Refresh()
            {
                formulas.Clear();
                collect(formulas);
                var state = new System.Text.StringBuilder();
                foreach (LetteraFormulaInfo f in formulas) state.Append(f.Source).Append(f.Failed).Append('|');
                string now = state.ToString();
                card.style.display = formulas.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (now == shown) return;
                shown = now;
                list.Clear();
                foreach (LetteraFormulaInfo f in formulas)
                {
                    string source = f.Source.Length > 40 ? f.Source.Substring(0, 40) + "…" : f.Source;
                    string state2 = f.Failed ? "Cannot be read at " + f.ErrorPosition + ": " + f.Message : (f.Display ? "Display, " : "Inline, ") + f.Box.width.ToString("0.#") + " x " + f.Box.height.ToString("0.#");
                    Label source2 = LtUI.Text(source, "lt-subtitle");
                    Label detail = LtUI.Text(state2, "lt-subtitle");
                    detail.style.whiteSpace = WhiteSpace.Normal;
                    if (f.Failed) detail.style.color = new Color(1f, 0.55f, 0.45f);
                    list.Add(source2);
                    list.Add(detail);
                }
            }
            card.schedule.Execute(Refresh).Every(500);
            Refresh();
            return card;
        }

#if !LETTERA_NO_MATH
        /// <summary>What a font holds for formulas, for its own inspector.</summary>
        internal static string DescribeFont(LetteraFontAsset font)
        {
            if (font == null) return null;
            FontFace face = font.LoadFace();
            if (face == null) return "Not loaded.";
            if (LetteraNative.Version < 3) return "This editor holds an older native library: restart it to read the math table.";
            if (!NativeMathData.Available(face)) return "No math table. Formulas in this font use fallback layout.";
            LetteraMathFont math = LetteraMathFont.For(face);
            return "Has a math table: 56 constants, scripts at " + math.C(MathConstant.ScriptPercentScaleDown) + "% and " + math.C(MathConstant.ScriptScriptPercentScaleDown) + "%.";
        }
#endif
    }
}
