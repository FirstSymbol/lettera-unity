# Formulas and LaTeX tables

A formula sits in a line of text like a word, or stands as a paragraph of its own. It is written in a linear subset of TeX and laid out from the font's own OpenType MATH table. LaTeX tables work the same way, in ordinary text.

[Русская версия](Math.ru.md) · Other guides: [Getting Started](GettingStarted.md), [Fonts](Fonts.md), [Styles](Styles.md), [3D Text](Text3D.md)

## Write a formula

```csharp
label.Text = "Energy is <math>E = mc^2</math>, and <math=display>\\sum_{i=0}^n i = \\frac{n(n+1)}{2}</math> stands by itself.";
```

- `<math>` keeps the formula in its line. `<math=display>` makes it a paragraph of its own, centred, in display style.
- A verbatim string spares the doubled backslashes: `label.Text = @"<math>\sqrt{x^2+1}</math>";`.
- `<math=inline,x squared>x^2</math>` gives the formula words for a screen reader.
- `<math>` is in the default vocabulary. A text that should show those characters literally needs `<noparse>` or `\<math>`.
- A display environment needs no tag at all: `\begin{equation}`, `align`, `gather`, `multline` and `displaymath`, starred or not, written in ordinary text become a display formula on a line of its own, numbered on the same count as `<math=display>`. An environment with no matching `\end` stays text.

## What you can write

- Fractions and binomials, roots, scripts and limits, large operators, `\left \right` and sized delimiters, accents, bars, braces, arrows.
- Matrices, `cases`, `aligned`, `array`, and the display environments `align`, `gather`, `equation` and `multline`, with their starred forms.
- `\text`, spacing, `\color`, alphabets such as `\mathbb`, `\mathcal` and `\mathfrak`, and about 475 symbols.
- No macros and no `\def`. A command the parser does not know fails the formula, unless an [extension](#commands-of-your-own) answers for it.

### Chemistry and quantities

`\ce` and `\pu` read chemistry and units as mhchem writes them:

```
\ce{2H2 + O2 -> 2H2O}     \ce{SO4^2-}     \ce{^{227}_{90}Th}     \ce{C6H5-CHO}
\ce{A <=>[H2O][\Delta] B}     \ce{BaSO4 v}     \ce{NH3 ^}     \pu{1.2e3 kJ//mol}
```

Numbers after a symbol become subscripts and a trailing sign a charge. Amounts such as `2`, `0.5` and `1/2` stand before with a thin space. States such as `(aq)` stay upright. Arrows of every kind (`->`, `<-`, `<->`, `<=>`, `<=>>`, `<<=>`) carry text above and below, and keep a reaction's length of at least 1.6 em. Bonds are `-`, `=`, `#` and `\bond{...}`. Units take thin spaces and the multiplication dot; `//` is a fraction, `e` a power of ten, and digits from five up group in threes.

### Equation numbers

In `<math=display>`, `align` and `gather` number each row, and `equation` and `multline` the whole once: (1), (2) and on, counted per text in reading order.

- `\tag{x}` puts "(x)" in a number's place, and `\tag*{x}` draws "x". Both work on a display formula with no environment too.
- `\notag` or `\nonumber` leaves a row without a number. The starred environments number only what `\tag` labels.
- `\setcounter{equation}{n}` in any formula of a text makes the next number n + 1.
- A number stands at the right edge of the text's box, at the left in a right-to-left text, centred on its row. It belongs to its formula, so selection, colour and motion reach it.

## Which font

A formula takes the first of these that is set:

1. the style's `LetteraMathEffect.Font`;
2. the project's font, **Project Settings > FirstSymbol > Lettera > Math > Font** (`LetteraSettings.Math.Font`);
3. the text's own font, if it holds a MATH table;
4. a system math font: Cambria Math on Windows. The web, Android and iOS have none;
5. the text's font with fallback layout, which is readable but plain.

**The bundled font.** Noto Sans Math ships in the package and enters a build only when something refers to it. **Lettera > Math > Use Bundled Font** sets it for the project, at 343 KB in every build. On a style it costs only where that style is used. Any font with a MATH table works: STIX Two Math and Latin Modern Math look more classic.

A build for the web, Android or iOS warns when a scene in it holds a formula and no math font is set.

## Style a formula

A formula takes colour, size, alpha, stroke, shadow, glow and motion like text, and moves as one piece:

```csharp
label.Text = "<color=#4DA3FF><size=150%><math>\\int_0^1 x\\,dx</math></size></color>";
```

A tag of your own, with its own font, is a style with the math matcher and effect:

```csharp
label.Styles.Add(LetteraStyle.ForMatcher(new LetteraMathTagMatcher("tex"), new LetteraMathEffect { Font = fontAsset, Scale = 1.1f }));
label.MarkStylesDirty();
label.Text = "The area is <tex>\\pi r^2</tex>.";
```

See [Styles](Styles.md) for styles in general.

## A formula in a text

A formula is one object in its line: it does not break, and selects, copies and deletes whole.

- **Copying** gives `$source$` as plain text, MathML with its source as HTML, and the tag itself to a Lettera field.
- **A formula that cannot be read** draws what was written in a frame, and the text around it renders. **Project Settings > FirstSymbol > Lettera > Math > Errors** shows the source without the frame, or nothing.
- **Formulas from code:**

```csharp
if (!LetteraMath.Validate("\\frac{1}{", out string message, out int position))
    Debug.Log($"At {position}: {message}");

LetteraMath.Failed += (source, at, why) => Debug.LogWarning($"{why} in {source}");

// Width, height above the baseline and depth below it, with no text around.
Vector3 box = LetteraMath.Measure("\\sqrt{x^2+1}", display: false, font: null, size: 32);

var formulas = new List<LetteraFormulaInfo>();
label.GetFormulas(formulas);
foreach (LetteraFormulaInfo formula in formulas)
    Debug.Log($"{formula.Source} at {formula.Box}, numbers: {(formula.Tags != null ? string.Join(" ", formula.Tags) : "none")}");
```

`Validate` only reads, so it is fit for a field's validator: a field that shows a formula as it is typed is in the Math sample.

## Commands of your own

An extension answers for a command the parser does not know. Its answer is drawn upright, as text, in the command's place:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class SpeedUnits : ILetteraMathExtension
{
    public bool TryParse(string command, out string replacementSource)
    {
        replacementSource = command == "kmh" ? "km/h" : null;
        return replacementSource != null;
    }

    [RuntimeInitializeOnLoadMethod]
    static void Install() => LetteraMath.Register(new SpeedUnits());
}
```

Now `<math>v = 90\,\kmh</math>` reads.

## LaTeX tables

```csharp
label.Text = "Prices: \\begin{tabular}{|l|r|}\\hline Apples & 1.20 \\\\ Pears & 0.95 \\\\ \\hline\\end{tabular} today.";
```

- `tabular`, `tabular*`, `tabularx`, `tabulary` and `longtable` in ordinary text, with no tag around them, and a `table` float around one with `\caption` (numbered through the text, "Table 1:"), `\centering` and `\label`.
- A `tabular` stands in its line, centred on it or by `[t]` and `[b]`. `longtable` and `table` stand on a line of their own.
- **Columns:** `l c r`, `p{} m{} b{}` with a width, `X`, `L C R J`, `|` and `||` rules, `@{}`, `!{}`, `>{\bfseries}`, `>{\centering}` and `*{n}{...}`.
- **Rows:** `\hline`, `\cline`, `\toprule \midrule \bottomrule \cmidrule` and `\\[space]`.
- **Cells:** `\multicolumn` with its own rules, `\multirow`, `\textbf \textit \emph \texttt`, `\bfseries`, formulas in `$...$`, `\(...\)` or `<math>`, `--`, `~`, escapes, `\newline`, and tables of their own. A longtable shows its first head and its last foot.
- **Widths** follow CSS's automatic table layout: every column at its widest when that fits, else each between its narrowest and widest in proportion to its spare, else narrower still with words broken between glyphs. `p{}` keeps its width, `tabularx` shares what is left among its `X` columns, `tabulary` gives in proportion to content, and `tabular*` with `\extracolsep{\fill}` spreads to its width.
- **A table never runs past its text's width.** It lays out again when the text resizes, a set width past the text's is held to it, and a table that cannot fit even at its narrowest is drawn smaller.
- Copying a table gives its source. `LetteraTableEffect.CaptionLabel` sets the word before a caption's number.
- Source past 65 536 characters, more than 1 000 rows, 64 columns, 20 000 cells or 4 levels of nesting is cut back to `LetteraTableLimits`, with one warning per table.

## Settings

**Project Settings > FirstSymbol > Lettera > Math:**

| Setting | What it does |
|---|---|
| Font | The project's math font. It costs its size in every build once set here |
| Scale | A formula against the text around it, for a math font visibly larger or smaller |
| Errors | What a formula that cannot be read shows: a frame, its source, or nothing |
| Delimiter Coverage | How much a stretched delimiter covers of what it wraps |
| Least Rule Width | The least a fraction bar draws, in device pixels, so a faint bar does not read as missing |
| Cache | The most laid-out formulas kept, in bytes |
| Compile Out Formulas | Sets `LETTERA_NO_MATH`: no formula code in builds, and `<math>x</math>` shows `x` |

## Limits

- An inline formula never breaks: a long one overflows a narrow line. Right-to-left math is not done, so a formula always runs left to right.
- There is no selection inside a formula. A paste of HTML with MathML gives its text, not a formula; rich copy works from Lettera outward, and from Lettera's own format back.
- Fallback layout, with no MATH table, is readable and plain, and stretches nothing beyond the pieces Unicode has.
- A number does not move below a formula too wide to leave it room, and overlaps it instead. Room at the line's edge is the author's to leave.
- Only a whole display formula that is one environment numbers its rows. `aligned` and `gathered` inside other material, and a `\tag` inside them, take no number; a `\tag` on the formula itself still works. Two `\tag`s on one line are an error, as in amsmath.
- In a document, numbers count within the window, so the same equation can show another number after a scroll.
- A table is drawn through the formula route: `GetFormulas` lists it, and it selects, copies and deletes whole. A longtable does not split across a document's windows.
- A cell's words are shaped in the text's primary font only, with synthetic bold and slant for `\textbf` and `\textit`: no fallback font, no bidi inside a cell, no Lettera tags inside a table, and `\color`, `\rowcolor` and `\cellcolor` are ignored. Lines in a cell break at spaces only, with no hyphenation. Other LaTeX text commands are dropped and what their groups hold shows.
- A table's fit is worked out at the text's size; one inside `<size>` scales with it. During auto size's trials a table keeps the width it was last laid at.
- `<=>>` and `<<=>` draw as two harpoons with the shorter at 60%. `\bond{~--}` and `\bond{~=}` draw as ≅, `\bond{-~-}` as ≂, `\bond{~-}` as ≃. Kröger–Vink notation gets no special reading. An isotope's numbers are set flush right by padding with a digit's width, which is exact for tabular figures.
- LaTeX text commands outside formulas and tables (`\textbf`, `itemize`, `\Huge` and the like in ordinary text) are not read; see the README's *What 1.0.0 is not*.
- Formulas have been seen on Windows and in desktop browsers, and not on a phone or a Mac. See [Platforms](Platforms.md).
