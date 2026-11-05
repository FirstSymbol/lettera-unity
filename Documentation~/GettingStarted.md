# Getting Started

[Русская версия](GettingStarted.ru.md) · [All guides](Lettera.md)

This guide goes through Lettera one task at a time, in the order a project usually meets them. Each part ends with a link to the guide that goes deeper.

1. [Text on screen](#1-text-on-screen)
2. [Fonts](#2-fonts)
3. [Markup](#3-markup)
4. [Styles](#4-styles)
5. [Fields](#5-fields)
6. [Motion](#6-motion)
7. [Documents](#7-documents)
8. [Math](#8-math)
9. [3D text](#9-3d-text)

## 1. Text on screen

There are three text components:

| Component | Where it draws | Menu |
|---|---|---|
| `LetteraText` | On a Canvas, as a uGUI `Graphic`: masks, layout groups and Canvas Groups work with it. | **GameObject > Lettera > Text** |
| `LetteraWorldText` | In world space, without a Canvas. Texts with the same material merge into one batch. | **GameObject > Lettera > World Text** |
| `LetteraText3D` | As solid lit geometry. See [3D text](#9-3d-text). | **GameObject > Lettera > Text 3D** |

The first two share one API: what works on one works on the other. A Canvas text from code:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartFirstLabel : MonoBehaviour
{
    public RectTransform Parent;   // Something under a Canvas.

    void Start()
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(Parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(400, 120);

        var label = go.AddComponent<LetteraText>();
        label.FontSize = 32;
        label.color = Color.white;
        label.HorizontalAlignment = LetteraHorizontalAlignment.Center;
        label.VerticalAlignment = LetteraVerticalAlignment.Middle;
        label.Text = "Hello from code";
    }
}
```

**The box.** A text lays out inside its rect, less its `Padding`. These settle what happens at the edges:

| Property | What it does |
|---|---|
| `WordWrap` | Breaks lines at the box's width, by Unicode's line breaking rules. |
| `HorizontalAlignment` | `Start`, `Center`, `End`, `Left`, `Right` or `Justify`. `Start` and `End` follow the paragraph's direction, so they flip for Arabic and Hebrew. |
| `VerticalAlignment` | `Top`, `Middle` or `Bottom`. |
| `Overflow` | `Visible`, `Ellipsis` (a marker where the text is cut) or `Truncate` (a fade). `OverflowPosition` puts the cut at the start (0), the middle (0.5) or the end (1). |
| `AutoSize` | Picks the largest size between `MinFontSize` and `MaxFontSize` that fits; `CurrentFontSize` says which. |
| `LineHeightMode`, `LineHeightValue` | The distance between lines. |
| `BaseDirection`, `Language` | The paragraph's direction (`Auto` by default) and a BCP 47 language for forms that differ by language. |

**Runtime text.** Set `Text` for a value you keep. For a value that changes every frame, call `SetText`: it takes a string, a `StringBuilder`, a `char[]` or a span, and it does not change what the scene saves. A plain text updated this way allocates nothing once its characters have been seen.

**Measuring.** Lettera answers questions about a text without touching its rect:

```csharp
using System.Collections.Generic;
using FirstSymbol.Lettera;
using UnityEngine;

public class StartMeasure : MonoBehaviour
{
    public LetteraText Label;
    readonly List<LetteraSearchHit> m_Hits = new List<LetteraSearchHit>();

    void Start()
    {
        // How big this text would be at most 300 units wide.
        Vector2 size = Label.MeasureText(new LetteraMeasureOptions { MaxWidth = 300 });

        // How many words it shows, by Unicode's word rules. Neither call allocates.
        int words = Label.CountUnits(LetteraTextUnit.Word);
        foreach ((int start, int end) in Label.Units(LetteraTextUnit.Line))
            Debug.Log("A line from " + start + " to " + end);

        // Every place a string stands, ignoring case.
        int found = Label.FindAll("lettera", m_Hits, ignoreCase: true);
        Debug.Log(size + ", " + words + " words, " + found + " hits");
    }
}
```

`Lines`, `GlyphCount`, `GetGlyph`, `InkBounds` and `LayoutBounds` describe what the text drew; `HitTestCaret`, `TryGetCaretRect` and `GetRangeRects` map between points and characters.

**World text and pointers.** A world text takes clicks only through a `LetteraWorldRaycaster` on the camera, and every text needs an `EventSystem` with an input module. The GameObject menu adds the EventSystem for you.

## 2. Fonts

With no font assigned, a text uses the operating system's default face and fills every gap from the other fonts the system has, emoji included. On the web there are no system fonts: Lettera brings Noto Sans for Latin, Cyrillic and Greek, and other scripts need a font you assign.

To use a font of your own, select the file and choose **Assets > Create > Lettera > Font Asset From Selection**. Then set it on a text's **Font**, or in **Project Settings > Lettera > Defaults** for every text. Several faces with fallbacks between them make a **font stack**; a font the player's system already has is a **system font asset**, which holds only names.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartFonts : MonoBehaviour
{
    public LetteraText Label;
    public LetteraFontAsset Brand;      // A font asset made from a file.
    public LetteraFontAsset Icons;

    void Start()
    {
        var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
        stack.Families.Add(new LetteraFontFamily { Primary = Brand });
        stack.Families.Add(new LetteraFontFamily { Primary = Icons });
        Label.FontStack = stack;
    }
}
```

More in [Fonts](Fonts.md): stacks with weights and italics, system fonts per platform, variable fonts, memory, subsets, pixel and colour fonts.

## 3. Markup

Tags look like HTML: `<b>bold</b>`, `<color=#F80>orange</color>`, `<size=150%>big</size>`. A tag's value is a list of parameters separated by commas, and a value may be quoted: `<shadow=#000,0.1,-0.1>`. `<br>` is a line break.

Tags mean something only because a **style** binds them to an **effect**. The default vocabulary, in force until a project assigns its own global style preset, covers:

| Kind | Tags |
|---|---|
| Weight, slant, size, colour | `<b>` `<strong>` `<i>` `<em>` `<size>` `<color>` `<alpha>` |
| Fonts and language | `<font>` `<lang>` `<otf>` `<axes>` `<size-match>` |
| Lines and layers | `<u>` `<s>` `<outline>` `<shadow>` `<fill>` `<stroke>` `<glow>` `<inner-shadow>` `<extrude>` `<filter>` `<union>` `<warp>` `<mark>` |
| Case and position | `<uppercase>` `<lowercase>` `<smallcaps>` `<sup>` `<sub>` `<ruby>` |
| Layout | `<tracking>` `<word-spacing>` `<cell>` `<line-height>` `<para-spacing>` `<line-gap>` `<nobr>` `<li>` `<indent>` `<align>` `<trim>` `<curve>` `<divider>` `<ellipsis>` `<truncate>` `<dir>` |
| Motion and interaction | `<wave>` `<shake>` `<reveal>` and the other motions, `<a>` `<link>` `<spoiler>` `<sprite>` `<obj>` |
| Formulas | `<math>`, and LaTeX tables starting with `\begin{tabular}` |

The README's [markup table](../README.md#markup) gives each tag's parameters.

A tag that nothing claims stays as literal text, so `a <b` or a tag name no style knows shows as written. To show a tag literally, escape it as `\<b>` or wrap it in `<noparse>...</noparse>`. **Use Global Style Preset** on a text turns the project's vocabulary off for that text.

## 4. Styles

A style is a pair: something that finds ranges (a matcher, usually a tag name) and something that changes them (an effect). Adding one to a text gives it a new tag:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartOwnTag : MonoBehaviour
{
    public LetteraText Label;

    void Start()
    {
        var sunset = new FillEffect();
        sunset.SetPaint(LetteraPaint.Of(LetteraGradient.Of(
            new Color32(255, 170, 40, 255), new Color32(240, 60, 130, 255)), LetteraPaintSpan.Range));

        Label.Styles.Add(LetteraStyle.ForTag(sunset, "sunset"));
        Label.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "warning", "#FF8800"));
        Label.MarkStylesDirty();
        Label.Text = "<sunset>Evening</sunset> and a <warning>warning</warning>";
    }
}
```

In the inspector, the **Styles** card's **+ Add Style** offers every effect and matcher by what it does. Styles shared by many texts go in a **style preset** asset; a project-wide one in **Project Settings > Lettera** replaces the default vocabulary.

More in [Styles](Styles.md): paints and gradients, the layer order, holds that change a parameter without parsing, composite effects, your own effect and shader.

## 5. Fields

`LetteraSelectable` makes a text selectable and copyable; `LetteraEditable` makes it a field. **GameObject > Lettera** builds an Input Field, a Text Area, a Password Field, a Dropdown, a Selectable Text or an Editable Text, each with its frame and hint.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartChatBox : MonoBehaviour
{
    public RectTransform Parent;

    void Start()
    {
        GameObject root = LetteraFieldFactory.CreateInputField(Parent, "Say something...");
        LetteraEditable field = root.GetComponentInChildren<LetteraEditable>();
        field.AddRule(new MaxLengthRule { Limit = 80 });
        field.Submitted += value =>
        {
            Debug.Log("Sent: " + field.PlainText);
            field.SetSource("");
        };
    }
}
```

A field's value is its source text, markup included (`Source`); `PlainText` is the same without markup. Rules decide what may go in, what keys do and what the field shows around itself.

More in [Fields](Fields.md): selection, every rule, formatting commands, the clipboard, composition and the platform's own controls.

## 6. Motion

Motion tags work with nothing set up:

```text
<wave>waves</wave> <shake>shakes</shake> <bounce>bounces</bounce> <roll>1234</roll>
<a=https://unity.com>a link</a> and a <spoiler>secret</spoiler>
```

Motion moves the vertices the last rebuild left, so a waving text is never laid out again and allocates nothing per frame. A typewriter is `RevealEffect`, a ticker is `MarqueeEffect`, and `LetteraSequencer` ramps any parameter over a timeline.

More in [Motion](Motion.md): every motion and its parameters, reveals, the sequencer, Timeline tracks, ranges that answer a pointer, inline sprites and reduced motion.

## 7. Documents

For text too long to lay out at once, such as a log, a book or a 100 MB file, open it as a document. Only what is near the view is laid out, and scrolling inside that costs no rebuild.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartBook : MonoBehaviour
{
    public LetteraText Page;
    public TextAsset Book;

    void Start()
    {
        Page.OpenDocument(LetteraRope.FromString(Book.text), takeOwnership: true);
        Page.gameObject.AddComponent<LetteraDocumentScroller>().Text = Page;
    }
}
```

A long text set the ordinary way also draws only the lines near what shows. More in [Documents](Documents.md).

## 8. Math

A formula is TeX inside `<math>`, in the flow of a sentence, or on a line of its own with `<math=display>`:

```text
Energy is <math>E = mc^2</math>, and <math=display>\sum_{i=1}^n i = \frac{n(n+1)}{2}</math>
```

On Windows the system's Cambria Math is used. On the web, Android and iOS there is no system math font: run **Lettera > Math > Use Bundled Font** once. More in [Math](Math.md), with chemistry and LaTeX tables.

## 9. 3D text

**GameObject > Lettera > Text 3D** makes `LetteraText3D`: closed, lit solids built from the glyph outlines, with a depth, bevels and five material slots. Markup, fonts, fallback and motion work as on world text, and `<depth=2>` or `<bevel=chamfer,0.03,0.03>` act on a range. Seven looks ship ready to apply, from Gold to Neon. It lives in its own assembly, `FirstSymbol.Lettera.Text3D`, which a build leaves out when no scene uses it.

More in [3D Text](Text3D.md).

## Where to look next

- **Tools > Lettera > Getting Started** lists these guides, imports the samples and opens every window.
- **Tools > Lettera > Commands** (Ctrl+Alt+L) finds anything Lettera makes or opens by typing.
- [Tools](Tools.md) describes the windows: Glyphs, Ranges, Atlases, Clipboard, Font Tools, the Sequencer and the Scene view overlay.
- Coming from TextMesh Pro? Read [Migration](Migration.md).
