# Fonts

[Русская версия](Fonts.ru.md) · [All guides](Lettera.md)

Lettera reads font files itself, through HarfBuzz, and draws every glyph from one shared atlas. There is no per-font material and no atlas to bake ahead: a glyph is rasterized the first time a text needs it, and fonts never split a batch.

- [No font at all](#no-font-at-all)
- [A font asset](#a-font-asset)
- [Font stacks](#font-stacks)
- [System font assets](#system-font-assets)
- [Weights, italics and variable fonts](#weights-italics-and-variable-fonts)
- [OpenType features](#opentype-features)
- [Tuning a face](#tuning-a-face)
- [Memory](#memory)
- [Subsets](#subsets)
- [The atlas: SDF and MSDF](#the-atlas-sdf-and-msdf)
- [Pixel fonts](#pixel-fonts)
- [Colour fonts and emoji](#colour-fonts-and-emoji)
- [The web](#the-web)
- [Limits](#limits)

## No font at all

A text with no **Font** and no **Font Stack** draws in the operating system's default face. A character that face lacks is looked up in the other fonts installed on the system: Arabic, Devanagari, Han and emoji all draw in an empty project. On Android Lettera reads the device's own font configuration, so a Chinese phone and a Japanese phone each draw Han characters in their own forms.

**Project Settings > Lettera > Defaults** changes this for the whole project:

| Setting | What it does |
|---|---|
| **Default Font** | The face a text with no font of its own uses, in place of the system's. |
| **No System Fallback** | Stops looking in the system's fonts. A character no assigned font has shows a missing-glyph box. |
| **No Emoji** | Stops taking emoji from a colour face: they go through the ordinary font chain instead. |
| **Web Fallback Font** | Replaces the bundled web font. See [The web](#the-web). |

## A font asset

Select one or more `.ttf`, `.otf`, `.ttc` or `.otc` files and choose **Assets > Create > Lettera > Font Asset From Selection**. The asset holds the file's bytes; there is no import step and nothing else to set. A `.ttc` collection holds several faces, and the asset's **Face Index** picks one.

Set it on a text's **Font**. From code, any `LetteraFontAsset` works:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsAssign : MonoBehaviour
{
    public LetteraFontAsset Heading;

    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Font = Heading;
        Debug.Log("Family: " + Heading.FamilyName);
    }
}
```

**At run time** a font can come from bytes, such as a download or an asset bundle, or from a file on disk. A file is read where it lies and mapped where the platform allows, so its bytes cost no managed memory:

```csharp
using System.IO;
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsAtRunTime : MonoBehaviour
{
    public LetteraText Label;
    public TextAsset FontBytes;   // A .ttf renamed to .bytes.

    void Start()
    {
        // From an array: the array is kept and must not change afterwards.
        LetteraFont fromBytes = LetteraFont.CreateFromBytes(FontBytes.bytes);

        // From a file: nothing is copied into memory.
        string path = Path.Combine(Application.persistentDataPath, "Downloaded.otf");
        LetteraFont fromFile = File.Exists(path) ? LetteraFont.CreateFromFile(path) : null;

        Label.Font = fromFile != null ? fromFile : fromBytes;
    }
}
```

A text whose font is missing a character still falls back to the system's fonts, unless the project turned that off.

## Font stacks

A stack is a list of **families** tried in order for each character: the first family that has a glyph for it draws it. Use one for a brand font with an icon font beside it, for one font per script, or to give a family its bold and italic faces.

Make one with **Assets > Create > Lettera > Font Stack**, or select several font assets and use **Font Stack From Selection, Each A Fallback** (one family each) or **Font Stack From Selection, One Family** (one family holding them all as its cuts).

| Part | What it is |
|---|---|
| **Primary** | The family's regular face. |
| **Faces** | Its other weights and italics. `<b>` and `<i>` pick among these before they synthesize anything. |
| **Name** | What `<font=Name>` selects. Empty takes the primary face's family name. |
| **Preferred Language** | A BCP 47 tag such as `ja` or `zh-Hans`: for text in that language this family is tried first. |
| **Fallback Stack** | Another stack tried after this one's families. |

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsStack : MonoBehaviour
{
    public LetteraFontAsset Regular, Bold, Italic, Icons, Japanese;

    void Start()
    {
        var stack = ScriptableObject.CreateInstance<LetteraFontStack>();

        var main = new LetteraFontFamily { Primary = Regular, Name = "Body" };
        main.Faces.Add(Bold);
        main.Faces.Add(Italic);
        stack.Families.Add(main);

        stack.Families.Add(new LetteraFontFamily { Primary = Icons, Name = "Icons" });
        stack.Families.Add(new LetteraFontFamily { Primary = Japanese, PreferredLanguage = "ja" });

        var label = GetComponent<LetteraText>();
        label.FontStack = stack;
        label.Text = "<b>Saved</b> <font=Icons>\uE002</font> <lang=ja>保存しました</lang>";
    }
}
```

When a text has both a **Font** and a **Font Stack**, the font comes first and the stack after it.

The font page of a stack lists each family with what it covers, and its test line says which family answers each run of a sample text, with missing characters marked.

## System font assets

A `LetteraSystemFont` names a font the player's own system has, per platform, and carries no bytes. Make one with **Assets > Create > Lettera > System Font Asset**. Nothing is loaded until a text uses it.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsSystem : MonoBehaviour
{
    LetteraSystemFont m_Serif;

    void Start()
    {
        m_Serif = ScriptableObject.CreateInstance<LetteraSystemFont>();
        m_Serif.Windows = new[] { "Georgia", "Times New Roman" };
        m_Serif.MacOS = new[] { "Georgia", "Times" };
        m_Serif.IOS = new[] { "Georgia" };
        m_Serif.Android = new[] { "Noto Serif", "serif" };
        m_Serif.Linux = new[] { "DejaVu Serif", "Liberation Serif" };
        m_Serif.Common = new[] { "Noto Serif" };   // Tried on every platform after its own list.
        m_Serif.Weight = 300;                      // A light cut where the family has one.
        GetComponent<LetteraText>().Font = m_Serif;
    }

    void Update()
    {
        // Null until a text has used it, or when no name in the lists is installed.
        if (m_Serif.ResolvedFamily != null) Debug.Log("Found " + m_Serif.ResolvedFamily + " at " + m_Serif.ResolvedPath);
    }
}
```

Names can be family names or file names. On Android the generic names `sans-serif`, `serif` and `monospace`, the configuration's aliases and Roboto find the system's own families.

**Weight** takes a CSS number from 100 to 900: a text with no weight of its own draws at the nearest installed cut, or at that point of a variable face, and faces the system falls back to for missing characters draw at the same weight. Each platform's settings (`WindowsSettings`, `AndroidSettings` and the others) can set its own **Weight**, write its own ascender, descender and line gap when the system's face has wrong ones there, and set a pixel grid for a face drawn in pixels on that system only. After changing a platform's settings from code, call `PlatformSettingsChanged()`.

The asset's page in the inspector has a tab per platform and **Re-Resolve**.

## Weights, italics and variable fonts

`<b>` asks for weight 700 and `<i>` for italic. Lettera first looks for a real face: a cut in the stack family's **Faces**, a face of a system family, or a variable font's axis. Only when there is none does it synthesize one: a bolder outline (`SyntheticBoldStrength` on the font asset) or a shear (`SyntheticItalicSlant`, in percent of the glyph height).

`<b>` takes a weight and a mode: `<b=600>`, `<b=700,f>` to force the synthetic bold, `<b=700,r>` to use real faces only. `<i=12>` sets the slant of a synthetic italic.

**Variable fonts** move along their axes per range with `<axes>`. Values are positional, for `wght`, `wdth`, `ital`, `slnt` and `opsz`, or named pairs for any other axis. A value is absolute (`640`), a percent of the axis default (`120%`), a change from it (`+80`), or `~` to leave the axis alone:

```text
<axes=100>Thin</axes> <axes=900>Black</axes> <axes=400,75%>Condensed</axes> <axes=GRAD 50>Graded</axes>
```

Values clamp to the axis, and an axis the face lacks is ignored.

A **font variant** (`LetteraFontVariant`, **Assets > Create > Lettera > Font Variant**) is a fixed place on a variable font's axes, saved as an asset. It can stand in a stack family's **Faces** as a weight of its own, so that `<b>` picks it:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsVariant : MonoBehaviour
{
    public LetteraFont Variable;   // A font asset with a wght axis.

    void Start()
    {
        var semiBold = ScriptableObject.CreateInstance<LetteraFontVariant>();
        semiBold.Source = Variable;
        semiBold.Axes.Add(new LetteraFontVariant.AxisValue { Tag = "wght", Value = 600 });
        GetComponent<LetteraText>().Font = semiBold;
    }
}
```

## OpenType features

`<otf>` turns features on and off by their four-letter tags, with an optional value: `<otf=tnum>` for tabular figures, `<otf=smcp,onum>`, `<otf=-liga>` to turn ligatures off, `<otf=cv02 3>` for the third form of a character variant. Features a face lacks do nothing.

Other tags work through features when the face has them and synthesize otherwise: `<smallcaps>`, `<sup>`, `<sub>`.

`<size-match=XHeight>` or `<size-match=CapHeight>` scales fallback faces so their x-height or cap height matches the primary face, as CSS's `font-size-adjust` does. **Project Settings > Lettera > Defaults > Size Match** sets the metric; a font asset's **Normalize Size** does the same for that face whenever it is a fallback.

## Tuning a face

A font asset carries settings that change how its face sets, without editing the file:

| Setting | What it does |
|---|---|
| `FontScale` | Multiplies the face's size, for a face that looks small beside its neighbours. |
| `NormalizeSize` | As a fallback, scales the face to the primary face's x-height or cap height. |
| `SpacingOffset` | Font units added to every glyph's advance, for a face that sets too tight or too loose. Joined scripts such as Arabic keep their advances, and marks take none. |
| `SpaceWidth` | The advance of a space and a no-break space, in font units. 0 keeps the file's. |
| Metrics | The **Metrics** card writes an ascender, descender and line gap by hand, with **Reset**. |
| `GlyphOverrides` | One glyph set apart by its character: an advance added, an offset it is drawn at, a size it is drawn at, and the pixels per em it is rasterized at. |
| `RasterDetail` | How finely the face's glyphs are drawn: `Standard` as the text asks (64 pixels per em, 128 for large text), `Fine` at least 128, `Finest` at least 256, for a hairline or calligraphic face. Each step takes four times the atlas room per glyph; layout does not move. A glyph override's own pixels per em still wins. |

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsTuning : MonoBehaviour
{
    public LetteraFontAsset Display;

    void Start()
    {
        Display.SpacingOffset = -20;    // Set 20 font units tighter.
        Display.SpaceWidth = 220;

        // The question mark is drawn a little higher and 10% larger.
        LetteraGlyphOverride question = LetteraGlyphOverride.For('?');
        question.Offset = new Vector2Int(0, 40);
        question.Scale = 1.1f;
        Display.GlyphOverrides.Add(question);
        Display.GlyphOverridesChanged();
    }
}
```

A glyph override's size draws the same pixels larger, so raise its **Pixels Per Em** with it for a glyph made much larger. Changing these on an asset in the editor changes the asset; do it on a copy at run time if the asset must stay as it is.

## Memory

Fonts cost nothing until they are used:

- In a player no font keeps a whole copy of its file in memory. A font is unpacked the first time a text needs it, into a cache folder, and **mapped** from disk after that.
- On desktop a build carries each font in a file of its own beside the build, so a font no scene reaches costs nothing at all. On Android, iOS and the web the bytes stay inside the asset. **Project Settings > Lettera > Font Memory** chooses per platform: `Auto`, `Inline` or `Packed`. A font asset's **Keep Inline** keeps that one font inside its asset everywhere.
- The same section bounds the cache folder (`FontCacheBytes`, 256 MB by default).

What fonts hold right now:

```csharp
using System.Collections.Generic;
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsMemoryReport : MonoBehaviour
{
    readonly List<LetteraFontMemoryEntry> m_Faces = new List<LetteraFontMemoryEntry>();

    void Start()
    {
        LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
        Debug.Log(sums.Faces + " faces, " + sums.RetainedBytes / 1024 + " KB held, " + sums.MappedBytes / 1024 + " KB mapped");

        LetteraFontMemory.Snapshot(m_Faces);
        foreach (LetteraFontMemoryEntry face in m_Faces)
            Debug.Log(face.Family + " " + face.Style + ": " + face.Source);
    }
}
```

**The first text of a session** pays once for Lettera's tables, the atlas's first page, its material and the glyphs it draws. To pay that behind a loading screen instead, call `LetteraTextWarmUp.Prepare` with the font and the characters you expect. Nothing is added to the build:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsWarmUp : MonoBehaviour
{
    public LetteraFontAsset Body;

    void Awake()
    {
        LetteraTextWarmUp.Prepare(Body, LetteraTextWarmUp.Latin + "0123456789", 24);
        LetteraShaderWarmUp.Run(LetteraSurfaceTier.UiPlain | LetteraSurfaceTier.UiStyled);
    }
}
```

Text under 72 units and text over it use different glyphs, so prepare at the size you will show.

After 10 seconds in which no text was rebuilt, and when the system reports low memory, Lettera gives back the caches it keeps only for speed. Texts keep what they show. **Project Settings > Lettera > Memory** sets the delay, and `LetteraMemory.Release()` does it at once.

## Subsets

A subset is a copy of a font that keeps only the characters you choose: a smaller download, and fewer bytes carried for characters the project never shows. The **Subset** card of a font asset's page, and **Tools > Lettera > Font Tools** for many at once, keep or remove characters by text, by Unicode ranges such as `U+0400-04FF`, by presets (Basic Latin, Latin-1, Cyrillic, Greek, Digits), or by what the project's texts use. By default it keeps what an ordinary subset tool quietly drops: OpenType features, names, glyph names and the missing-glyph outline.

The same from an editor script:

```csharp
#if UNITY_EDITOR
using FirstSymbol.Lettera;
using FirstSymbol.Lettera.Editor;
using UnityEngine;

public static class FontsSubsetTool
{
    public static LetteraFont KeepLatinAndCyrillic(LetteraFont font)
    {
        var options = new LetteraSubsetOptions
        {
            Mode = LetteraSubsetMode.Keep,
            Characters = LetteraFontSubset.Presets["Latin-1"] + " " + LetteraFontSubset.Presets["Cyrillic"],
            FromProject = true,   // Also every character the project's texts hold.
        };
        LetteraSubsetResult result = LetteraFontSubset.Run(font, options);
        if (!result.Succeeded)
        {
            Debug.LogWarning(result.Refused);
            return null;
        }
        Debug.Log(result.BytesBefore + " bytes to " + result.BytesAfter);
        return LetteraFontSubset.SaveAs(font, result.Bytes, "Assets/Fonts/Subset.asset");
    }
}
#endif
```

`LetteraFontSubset.Replace` swaps the font's own data instead, as one undo step. The subsetter lives in the editor only; no player holds it.

## The atlas: SDF and MSDF

Glyphs are stored as signed distance fields, so one glyph serves every size, and outlines, glows and shadows come from the same field. **Render Mode** on a text chooses:

- **SDF**, the default: round corners at large sizes, the least memory.
- **MSDF**: sharp corners at any size, and sharp stroke corners, in an atlas of its own at more memory per glyph.

**Project Settings > Lettera > Atlases** sets, per kind, the page size (2048 or 1024; a page of 1024 costs a quarter) and, for the distance-field kinds, 8 or 16 bits per channel. 16 bits doubles a page and keeps the field smooth far from the edge, where wide outlines, glows and shadows read it. A change takes effect when that kind's pages are next made from nothing. **Tools > Lettera > Atlases** shows what the pages hold. When the page limit is reached, the least recently used page is recycled.

## Pixel fonts

A font drawn on a pixel grid can draw every font pixel exactly. On the font asset, set **Pixels Per Em** to the grid (8 for an 8-pixel font), or press **Find Pixel Grid**; **Snap To Font Pixels** keeps glyphs and the offsets of effects on whole font pixels. At sizes that are multiples of the grid, such as 16, 24 and 32 for an 8-pixel font, every edge lands on a screen pixel, and outlines and shadows come in whole font pixels.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsPixel : MonoBehaviour
{
    public LetteraFontAsset Pixel;

    void Start()
    {
        int grid = Pixel.FindPixelGrid();     // 0 for a font not drawn in pixels.
        Pixel.FontPixelsPerEm = grid > 0 ? grid : 8;
        Pixel.SnapToFontPixels = true;

        var label = GetComponent<LetteraText>();
        label.Font = Pixel;
        label.FontSize = Pixel.FontPixelsPerEm * 3;
    }
}
```

## Colour fonts and emoji

Emoji and colour glyphs draw in colour from COLRv0, COLRv1 (with gradients and variable axes), `sbix` and `CBDT` fonts, into a colour atlas beside the distance-field one. Flags, joined sequences, skin tones and keycaps are one cluster each: a caret and Backspace take them whole.

With no colour font of your own, emoji come from the system's emoji font, so they look like that device's. **Assets > Create > Lettera > Color Font Asset From Selection** makes a `LetteraColorFont`; set it as a text's font or put it in a stack, and its glyphs come ahead of the system's. From bytes: `LetteraFont.CreateFromBytes<LetteraColorFont>(bytes)`.

`<color>` tints the words around an emoji and leaves the emoji as drawn; `<alpha>` and a text's alpha fade it. Shadows and glows draw around an emoji from its alpha; other layers skip colour glyphs unless an effect's **On Color Glyphs** says **Apply**.

## The web

Browsers expose no font files:

- A text with no font uses a bundled **Noto Sans**, which covers Latin, Cyrillic and Greek. Other scripts show missing-glyph boxes until you assign a font that covers them, through **Font**, **Font Stack** or the project's **Default Font**. Shaping then works as everywhere else.
- **Web Fallback Font** in Project Settings replaces the bundled font.
- Emoji are drawn by the browser's own canvas, so they look like that device's emoji. Colours and alpha apply as usual.
- Glyph memory is capped lower than elsewhere: the least recently used page is recycled sooner.
- The bundled web font stays out of Windows, macOS, Android and iOS builds unless a project names it as its web or default font.

`unity command lettera validate` (see the [Agent Guide](AgentGuide.md)) lists what a text would be missing on the web and on phones.

## Limits

- **Android fonts were read from fixture files only.** No device was used. Firmware font themes that replace the system's files while the app runs, fonts a vendor keeps outside `/system/fonts` and `/product/fonts`, and firmware that swaps fonts while drawing were not checked. The device's languages, its bold text setting and Android 15's tall variants are read once, at the first text; a change while the app runs takes effect at its next start.
- A system font asset finds a family's other cuts by file name and family name, opening each candidate once; on Android by the configuration. A weight between two cuts of a family with no variable face takes the nearest cut and does not synthesize a lighter one.
- A glyph override's size draws the same pixels larger or smaller, so a glyph made larger is softer unless its pixels per em are raised with it. Its size was not checked on layers, merged glyph shapes or Text 3D solids.
- Colour pages stay 8-bit RGBA. A distance-field kind's depth and every kind's page size are project choices.
- COLRv1: 10 glyphs of the 200 in the test font draw nothing (sweep gradients with coincident angles, which HarfBuzz's raster code skips). A gradient through alpha differs slightly from Chromium's. A colour glyph that uses the text's own colour does not follow it.
- Fonts holding only bitmap strikes and no outlines are not supported as pixel fonts.
- On macOS, a font in an asset bundle built with the Scriptable Build Pipeline stays inline.
- A text's own buffers are not given back after it stays unused for a while, only the shared caches.
- Spacing, glyph overrides, atlas depth, system font weights and Android's configuration are covered by the test suite; a timing run of them against the version before was not finished.
