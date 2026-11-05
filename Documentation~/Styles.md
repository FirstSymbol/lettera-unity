# Styles

[Русская версия](Styles.ru.md) · [All guides](Lettera.md)

Everything that changes how a range of text looks or lays out is a **style**. This guide shows how a tag becomes a look, how to build looks of your own, and how to change them at run time without parsing the text again.

- [Matchers and effects](#matchers-and-effects)
- [Where styles live](#where-styles-live)
- [Parameters](#parameters)
- [The effects](#the-effects)
- [Paints](#paints)
- [Layers and their order](#layers-and-their-order)
- [Changing a look at run time](#changing-a-look-at-run-time)
- [Composite effects and presets](#composite-effects-and-presets)
- [Ranges without tags](#ranges-without-tags)
- [Your own effect](#your-own-effect)
- [Your own matcher](#your-own-matcher)
- [Your own shader](#your-own-shader)
- [Limits](#limits)

## Matchers and effects

A `LetteraStyle` joins two things:

- a **matcher**, which finds ranges in the source: a tag such as `<glow>`, a Markdown marker, a URL, or the whole text;
- an **effect**, which changes those ranges: their colour, a stroke, their spacing, their motion.

`<b>` is bold only because the default vocabulary holds a style that binds the tag `b` to a `BoldEffect`. A tag no style claims stays as literal text. The same effect can sit behind several tags (`<b>` and `<strong>`), and the same tag name can mean something else in another project.

`LetteraStyle` has four ways in:

| Factory | Ranges it finds |
|---|---|
| `LetteraStyle.ForTag(effect, "name", defaultParameter)` | Every range wrapped in the tag called `name`, with or without a value |
| `LetteraStyle.ForWholeText(effect, parameter)` | The whole text, with no tag |
| `LetteraStyle.ForSpan(effect, start, length, parameter)` | Fixed codepoints of the rendered text |
| `LetteraStyle.ForMatcher(matcher, effect)` | Whatever a matcher finds |

A style also has `Enabled`, so it can be turned off without being removed.

## Where styles live

A text reads its styles from three places, in this order:

1. **The vocabulary.** The project's global style preset, set in **Project Settings > Lettera > Defaults > Global Style Preset**; without one, the built-in default vocabulary (`LetteraDefaultStyles`). **Use Global Style Preset** off on a text leaves it out for that text.
2. **Style presets** in the text's `StylePresets`: `LetteraStylePreset` assets shared by reference, for a look many texts use.
3. **The text's own** `Styles`.

After changing a text's lists from code, call `MarkStylesDirty()`. After changing a preset asset from code, call its `NotifyChanged()`.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesSharedPreset : MonoBehaviour
{
    public LetteraText[] Labels;

    void Start()
    {
        var preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
        preset.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "accent", "#3DA5FF"));
        preset.Styles.Add(LetteraStyle.ForTag(new StrokeEffect { Width = 0.06f }, "heavy"));

        foreach (LetteraText label in Labels)
        {
            label.StylePresets.Add(preset);
            label.MarkStylesDirty();
        }
    }
}
```

To start a project vocabulary from the built-in one, fill a preset with `LetteraDefaultStyles.Create()`, a fresh copy, and add your own styles to it.

In the inspector, the **Styles** card lists a text's styles. **+ Add Style** opens the picker, which offers every effect and matcher by what it does, finds them by words such as "gradient" or "typewriter", and shows a style live under the pointer before you choose it. Rows drag, copy, duplicate and disable like the rest of Lettera's lists; a row with a problem says what it is and offers a fix.

## Parameters

A tag's value is a list of **slots** separated by commas: `<shadow=#000C,0.08,-0.08,0.2>` gives a shadow its colour, x, y and softness. A slot left empty keeps the effect's own value: `<shadow=,,-0.2>` moves only y. Values may be quoted, and a comma inside balanced parentheses stays in its slot.

- **Lengths** of layers are in em, a share of the font size; `2px` is in local units.
- **Sizes and spacing** take `24`, `150%`, `+10`, `-5` or `1.2em`, as each tag's table row in the [README](../README.md#markup) says.
- **Colours** are `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA` or a name.
- **Choices** such as a paint's span take their name, or its first letter when no other choice starts with it: `glyph` or `g`.

A style's **default parameter** fills the slots a bare tag leaves out, so `ForTag(new ColorEffect(), "warning", "#FF8800")` makes a bare `warning` tag orange.

**Labels** name a range for code to find later: `<color #price=#0C0>12</color>` is a colour range labelled `price`. See [holds](#changing-a-look-at-run-time).

## The effects

Every effect below is in the default vocabulary under the tag shown. The README's [markup table](../README.md#markup) lists each tag's slots.

| Group | Effects and tags |
|---|---|
| Text | `BoldEffect` `<b>` `<strong>`, `ItalicEffect` `<i>` `<em>`, `SizeEffect` `<size>`, `ColorEffect` `<color>`, `AlphaEffect` `<alpha>`, `FontEffect` `<font>`, `LanguageEffect` `<lang>` |
| Lines | `UnderlineEffect` `<u>`, `StrikethroughEffect` `<s>`: solid, double, dotted, dashed or wavy, with skip-ink and overlay. `HighlightEffect` `<mark>`: by glyph, line, range or block, with rounded corners, behind or above |
| Layers | `FillEffect` `<fill>`, `StrokeEffect` `<stroke>`, `OutlineEffect` `<outline>`, `ShadowEffect` `<shadow>`, `GlowEffect` `<glow>`, `InnerShadowEffect` `<inner-shadow>`, `ExtrudeEffect` `<extrude>` |
| Colour and shape | `FilterEffect` `<filter>` (grayscale, saturation, hue, brightness, contrast, exposure, invert, sepia, tint, any matrix), `UnionEffect` `<union>` (overlaps merge into one shape), `WarpEffect` `<warp>` (a wavy outline) |
| Typography | `UppercaseEffect` `<uppercase>`, `LowercaseEffect` `<lowercase>`, `SmallCapsEffect` `<smallcaps>`, `SuperSubEffect` `<sup>` `<sub>`, `OpenTypeEffect` `<otf>`, `AxesEffect` `<axes>`, `SizeMatchEffect` `<size-match>`, `RubyEffect` `<ruby>` |
| Layout | `TrackingEffect` `<tracking>`, `WordSpacingEffect` `<word-spacing>`, `CellWidthEffect` `<cell>`, `LineHeightEffect` `<line-height>`, `ParagraphSpacingEffect` `<para-spacing>`, `LineGapEffect` `<line-gap>`, `NoBreakEffect` `<nobr>`, `ListEffect` `<li>`, `IndentEffect` `<indent>`, `AlignEffect` `<align>`, `BoxTrimEffect` `<trim>`, `CurveEffect` `<curve>`, `DividerEffect` `<divider>` |
| Eliding and direction | `EllipsisEffect` `<ellipsis>`, `TruncateEffect` `<truncate>`, `DirectionEffect` `<dir>` |
| Motion, links, inline | See [Motion](Motion.md): `<wave>` and the other motions, `<reveal>`, `<a>`, `<link>`, `<spoiler>`, `<sprite>`, `<obj>` |
| Formulas and tables | See [Math](Math.md): `<math>` and `\begin{tabular}` |

Effects with no tag of their own, added from the picker or from code: `MaterialEffect` (a material of your own), `PaintOrderEffect` (glyph-major or layer-major drawing), `ResolutionEffect` (a denser atlas for large headings), `SemanticEffect` (words for a screen reader), `NoEffect`, and the motion and interaction effects described in [Motion](Motion.md).

Auto size can save room before it shrinks the font: a text's `FitBudgets` take `FitByTracking`, `FitByGlyphWidth` and `FitByLineHeight`, spent in order and only as far as needed.

## Paints

Every coloured surface of a layer takes a **paint**: a solid colour, a gradient or a texture.

- **Gradients** are linear, radial or conic; mix in sRGB, linear light, Oklab, or in steps; and clamp, repeat or mirror past their ends.
- **Span** says what a paint is measured on: `Glyph` restarts it on each glyph, `Line` on each line, `Range` runs it across the whole range, `Block` across the text's box.
- **Blend** draws a layer normally or by multiply, screen, additive or exclusion.
- **Grain** on a gradient (`LetteraGradient.Grain`) takes away the bands of a slow gradient: it draws from a finer ramp and adds a grain of one 8-bit step, through a slightly heavier shader variant that only texts with it use.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesGradients : MonoBehaviour
{
    public LetteraText Title;
    public Texture2D Marble;

    void Start()
    {
        LetteraGradient dusk = LetteraGradient.Of(
            new Color32(255, 170, 40, 255), new Color32(240, 60, 130, 255), new Color32(70, 110, 255, 255));
        dusk.Mode = LetteraGradientMode.Oklab;

        var across = new FillEffect();
        across.SetPaint(LetteraPaint.Of(dusk, LetteraPaintSpan.Range, angle: 90));

        var rim = new StrokeEffect { Width = 0.05f };
        rim.SetPaint(LetteraPaint.Solid(new Color32(20, 20, 30, 255)));

        var stone = new FillEffect();
        stone.SetPaint(LetteraPaint.Of(Marble));

        Title.Styles.Add(LetteraStyle.ForTag(across, "dusk"));
        Title.Styles.Add(LetteraStyle.ForTag(rim, "rim"));
        Title.Styles.Add(LetteraStyle.ForTag(stone, "stone"));
        Title.MarkStylesDirty();
        Title.Text = "<rim><dusk>Evening</dusk></rim> and <stone>marble</stone>";
    }
}
```

**Named paints.** A `LetteraPaintLibrary` asset holds named swatches. Set one as the project's **Paints** in Project Settings, and a layer tag takes a swatch's name in place of a colour: `<fill=sunset>`, `<stroke=brand,0.05>`. A layer effect can also read its own library or its own swatches (`Catalog`). An unknown name warns once and uses the default paint.

After a layer's paint slot come the paint settings, in order: span, shape, fit, edge, blend, angle, scale, offset x, offset y. So `<fill=sunset,0,0,glyph>` restarts the swatch on every glyph.

A solid or gradient paint never splits a batch. A texture paint, or a blend other than normal, gets a material of its own, and the drawing order is kept.

## Layers and their order

A glyph can carry any number of layers. They draw in bands, back to front:

1. **Back:** shadows and glows.
2. **Rim:** strokes and outlines.
3. **Face:** the glyph's own face, and fills over it.
4. **Front:** inner shadows.
5. **Overlay:** what an effect asks to draw on top.

Within a band, a later style draws over an earlier one. A layer effect's `Band` can move it to another band. Filters reach what lies below them in the style list and the face, so a filter last in the list recolours everything. `PaintOrderEffect` decides, for the whole text, whether one layer finishes on every glyph before the next (`LayerMajor`, the default) or every layer of a glyph finishes before the next glyph (`GlyphMajor`), for script fonts whose neighbours overlap.

Layers reach up to 1 em from the glyph. When a layer asks for more room around a glyph than the atlas keeps, the atlas widens that glyph's margin.

## Changing a look at run time

**Change a field and say how far it reaches.** An effect's properties can be set at any time. `MarkEffectsDirty` then applies them with no parsing, at the level you give, from `Mesh` (the default) down to `Pose` for motion parameters:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesPulseStroke : MonoBehaviour
{
    public LetteraText Label;
    StrokeEffect m_Stroke;

    void Start()
    {
        m_Stroke = new StrokeEffect { Width = 0.02f };
        Label.Styles.Add(LetteraStyle.ForWholeText(m_Stroke));
        Label.MarkStylesDirty();
    }

    void Update()
    {
        m_Stroke.Width = 0.02f + 0.03f * Mathf.PingPong(Time.time, 1);
        Label.MarkEffectsDirty();
    }
}
```

**Hold a parameter of some ranges.** A hold takes over one slot of the ranges a query finds, without writing the effect's field and without parsing. Release it and the markup's value comes back:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesPriceHold : MonoBehaviour
{
    public LetteraText Label;
    LetteraHold<Color32> m_Hold;

    void Start()
    {
        Label.Text = "Apples <color #price=#FFFFFF>1.20</color>, pears <color #price=#FFFFFF>0.95</color>";
        ColorEffect color = Label.GetEffect<ColorEffect>();
        m_Hold = Label.Ranges(color).WithLabel("price").Hold<Color32>(0);
    }

    public void ShowSale(bool on)
    {
        m_Hold.Value = new Color32(255, 80, 60, 255);
        m_Hold.Weight = on ? 1 : 0;   // 0 is the markup's value, 1 the hold's, between them a blend.
    }

    void OnDestroy() => m_Hold?.Release();
}
```

A query narrows with `WithLabel`, `WithPrimary`, `WithToken`, `Bare`, `Overlapping`, `Inside`, `Skip`, `Take` and `Matching`; `Collect` lists what it finds. A hold composes by `Replace`, `Add` or `Multiply`, and several holds on one slot stack by priority. Sequencer cues and range rules ([Motion](Motion.md)) are built on holds.

## Composite effects and presets

**`CompositeEffect`** puts several effects behind one tag. Its tag value has one segment per child, separated by semicolons, and each segment holds that child's slots:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesBadge : MonoBehaviour
{
    void Start()
    {
        var badge = new CompositeEffect(
            new HighlightEffect(),
            new ColorEffect { Color = new Color32(255, 255, 255, 255) },
            new BoldEffect());

        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForTag(badge, "badge", "#2A7;;"));
        label.MarkStylesDirty();
        label.Text = "Status: <badge>online</badge>, <badge=#C33;#FFE;>offline</badge>";
    }
}
```

**Effect presets.** A `LetteraEffectPreset` asset (**Assets > Create > Lettera > Effect Preset**) saves one effect, usually a composite, under a name. A `PresetEffect` uses it; each user gets its own copy through `Instance`, so changing one text leaves the others alone. In the picker it stands under **Utility > Effect Preset**.

**Style presets** share whole lists of styles, as shown [above](#where-styles-live).

## Ranges without tags

Some matchers find ranges in plain text. They are not in the default vocabulary, because they change what plain text means; add them from the picker or from code:

| Matcher | Finds |
|---|---|
| `MarkdownMarkerMatcher` | `**bold**`, `*italic*`, `~~strike~~`, by CommonMark's rules for a marker you choose |
| `MarkdownListMatcher`, `MarkdownLinkMatcher`, `CodeSpanMatcher` | `- lists`, `[links](url)`, `` `code` `` |
| `BareUrlMatcher` | `https://...` and `www....` in running text |
| `TriggerWordMatcher` | `@mentions`, `#hashtags`: a trigger character and a word |
| `LiteralMatcher` | Fixed strings, with an optional replacement shown in their place |
| `MatcherGroup` | Several matchers under one effect, so one style answers `<i>`, `<em>` and `*...*` |
| `VoidTagMatcher` | A tag that stands alone, with or without a value, and has no closing tag |

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesChatLine : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new BoldEffect()));
        label.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), new ColorEffect { Color = new Color32(80, 170, 255, 255) }));
        label.Styles.Add(LetteraStyle.ForMatcher(new BareUrlMatcher(), new LinkEffect()));
        label.MarkStylesDirty();
        label.Text = "**@ana** see https://example.com";
    }
}
```

## Your own effect

Derive from `LetteraEffect` and write what a range gets in `Apply`. `LetteraAttributes` has a setter for each built-in attribute (`SetColor`, `SetWeight`, `SetItalic`, `MultiplySize`, `SetAlpha`, `SetFamily`, `SetLanguage`, `SetUnderline`, `SetOutline`, `SetShadow` and more), and `LetteraSlots` reads the tag's values with a fallback:

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

[Serializable]
[LetteraPicker("Look", "alert danger loud")]
public class StylesAlertEffect : LetteraEffect
{
    [SerializeField, LetteraEssential] Color32 m_Color = new Color32(255, 70, 50, 255);
    [SerializeField] float m_Grow = 1.15f;

    public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
    {
        attributes.SetColor(start, end, slots.Color(0, m_Color));
        attributes.MultiplySize(start, end, slots.Float(1, m_Grow));
        attributes.SetWeight(start, end, 700, 0);
    }
}

public class StylesUseAlert : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForTag(new StylesAlertEffect(), "alert"));
        label.MarkStylesDirty();
        label.Text = "Saved. <alert>Disk almost full.</alert> <alert=#FC0,1.0>Check soon.</alert>";
    }
}
```

- Effects are serialized by reference, so mark them `[Serializable]` and keep their state in fields.
- An effect holding a list or another nested object overrides `Clone`, so each text gets its own copy.
- `[LetteraEssential]` marks the fields a new style shows first; `[LetteraPicker(group, words)]` puts the effect in the picker's group (`Basics`, `Type`, `Lines`, `Look`, `Layout`, `Links`, `Utility` or `Custom`) and gives it words to be found by.
- `Parameters` publishes named parameters for the sequencer, rules and holds; `Stages` and the `On...` hooks let an effect join a stage of the rebuild. See [Motion](Motion.md#stage-hooks).

## Your own matcher

Derive from `LetteraMatcher` and claim ranges in `Find`. The context gives the source as a span; `IsFree` says whether another matcher already holds a stretch; `AddRawRange` adds a range over text that stays visible, and `ReplaceAndRange` shows something else in its place:

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

// Every run of digits, such as prices and counts.
[Serializable]
public class StylesDigitsMatcher : LetteraMatcher
{
    public override void Find(LetteraMatchContext context)
    {
        ReadOnlySpan<char> source = context.Text;
        for (int i = 0; i < source.Length; i++)
        {
            if (!char.IsDigit(source[i])) continue;
            int end = i;
            while (end < source.Length && char.IsDigit(source[end])) end++;
            if (context.IsFree(i, end - i)) context.AddRawRange(i, end - i, null, null);
            i = end - 1;
        }
    }
}

public class StylesTabularDigits : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        LetteraStyle figures = LetteraStyle.ForMatcher(new StylesDigitsMatcher(), new OpenTypeEffect());
        figures.DefaultParameter = "tnum";   // Tabular figures, so the times line up.
        label.Styles.Add(figures);
        label.MarkStylesDirty();
        label.Text = "Lap 1: 1:11.01, lap 2: 1:09.88";
    }
}
```

`Priority` orders matchers: higher claims first. A matcher that can write its own syntax (`CanWrap`, `WriteOpen`, `WriteClose`) lets field commands such as **Bold** write it into a field's source.

## Your own shader

**Assets > Create > Lettera > Custom Effect** writes a shader and a material beside it. The shader has one function to edit, `LetteraCustom`, which gets the glyph's distance (0.5 at the outline), the colour the style asked for, a colour glyph's own colour and the position in the text, and returns a premultiplied colour. The same pass runs on Canvas and world text, under the Built-in and Universal pipelines.

Use it through a **Custom Material** style, which is a `MaterialEffect`:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesStripes : MonoBehaviour
{
    public Material Stripes;   // The material Custom Effect made.

    void Start()
    {
        var label = GetComponent<LetteraText>();
        var custom = new MaterialEffect { Material = Stripes, Order = LetteraMaterialOrder.Replace };
        label.Styles.Add(LetteraStyle.ForTag(custom, "stripes"));
        label.MarkStylesDirty();
        label.Text = "Plain and <stripes>striped</stripes>";
    }
}
```

`Replace` draws the material in place of the face; `Keep` draws it as a layer of its own. The tag's first slot is a tint, passed as the vertex colour. A range with a material of its own gets its own draw, so it splits the batch.

## Limits

- Union is done in the atlas and warp in the styled shader tier; inner shadows are not warped.
- Strokes have no miter limit: sharp corners reach as far as the distance field does.
- Curved text draws no underlines or strikethroughs.
- Block highlights round convex corners only.
- Colour filters work in the shader's colour space, which is linear in a linear project.
- A Custom Effect's shader is written for the Built-in and Universal pipelines only, not for HDRP.
- Styling the visible delimiters of markup (showing tags in a field in their own look) is not implemented.
