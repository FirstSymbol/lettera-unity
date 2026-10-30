# Lettera

A text engine for Unity UI and world space: full OpenType shaping, bidirectional text, Unicode line breaking, system fonts with automatic fallback, color emoji, one shared SDF atlas, and styles driven by extensible markup.

[Русская версия](README.ru.md)

```csharp
label.Text = "Hello · مرحبا بالعالم · שלום · 👨‍👩‍👧‍👦";
```

No font files are needed: with nothing assigned, a text uses the operating system's default face and fills gaps from the OS fonts.

## Installation

Unity Package Manager → **+** → **Add package from git URL...**:

```
https://github.com/FirstSymbol/lettera-unity.git
```

A specific version:

```
https://github.com/FirstSymbol/lettera-unity.git#0.1.0
```

Requires Unity 6 (6000.0) or newer.

## Quick start

- **GameObject → Lettera → Text** creates a Canvas text.
- **GameObject → Lettera → World Text** creates text in world space, without a Canvas.
- To use your own font, select a `.ttf`, `.otf` or `.ttc` file and choose **Assets → Create → Lettera → Font Asset From Selection**.

```csharp
using FirstSymbol.Lettera;

label.Text = "<b>Score</b>: <color=#FC0>10</color>";
label.SetText("Score: 11");     // Runtime text; the same characters again cost nothing.
label.FontSize = 24;
label.AutoSize = true;
label.Overflow = LetteraOverflow.Ellipsis;
Vector2 size = label.MeasureText(new LetteraMeasureOptions { MaxWidth = 300 });
```

## Markup

A tag means something only because a style binds it to an effect; a tag nothing claims stays as literal text. The built-in vocabulary applies until a project assigns its own global style preset, and **Use Global Style Preset** turns it off per text.

| Tag | Effect | Parameters |
|---|---|---|
| `<b>`, `<strong>` | Weight | weight 100-900; `f` forces synthetic, `r` real faces only |
| `<i>`, `<em>` | Italic | slant in percent of the glyph height |
| `<size>` | Size | `24`, `150%`, `+10`, `-5`, `1.2em` |
| `<color>` | Color | `#RGB`, `#RRGGBB`, `#RRGGBBAA`, or a name |
| `<alpha>` | Alpha | `0.5`, `50%`, `#80` |
| `<font>` | Family | a family name of the font stack |
| `<lang>` | Language | a BCP 47 tag |
| `<u>`, `<s>` | Underline, strikethrough | optional color |
| `<outline>` | Outline | color, width in em or `2px` |
| `<shadow>` | Shadow | color, x, y, softness, dilate |
| `<ellipsis>`, `<truncate>` | Elide a range | position: 0 start, 0.5 middle, 1 end |
| `<dir>` | Base direction | `ltr`, `rtl`, `auto` |
| `<br>` | Line break | |
| `<noparse>` | Literal content | |

`\<` escapes markup. Parameters are comma-separated, and values may be quoted: `<shadow=#000,0.1,-0.1>`.

To define your own tag, add a style to a text or preset:

```csharp
label.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "warning", "#FF8800"));
label.MarkStylesDirty();
```

Custom behavior derives from `LetteraEffect` (what a range looks like) and `LetteraMatcher` (how ranges are found).

## Platforms

| Platform | Native plugin | System fonts and emoji |
|---|---|---|
| Windows x64, ARM64 | prebuilt | yes |
| macOS universal | prebuilt | yes |
| Linux x64, ARM64 | prebuilt | yes, read from the font folders |
| iOS device and simulator | prebuilt XCFramework | yes, through CoreText |
| Android ARM64, ARMv7, x86-64 | prebuilt | yes |
| Web (WebGL 2), including Safari on iOS | prebuilt | emoji through the browser; text fonts must be assigned |

See [the changelog](CHANGELOG.md) for what each platform has been verified on.

### The web

Browsers expose no font files, so on the web:

- A text with no font uses a bundled Noto Sans, which covers Latin, Cyrillic and Greek. **Other scripts show missing-glyph boxes until you assign a font** that covers them, through **Font** or **Font Stack**. Shaping then works as everywhere else.
- Emoji are drawn by the browser's own canvas, so they look like that device's emoji. Colors and alpha apply as usual.
- `LetteraSettings.WebGLFallbackFont` replaces the bundled font.
- Glyph memory is capped lower than on other platforms: the least recently used atlas page is recycled when the limit is reached.

## Rebuilding the native plugin

`Native~/build.py` builds HarfBuzz and the C wrapper for every platform with [zig](https://ziglang.org), and Android with the NDK from Unity's Android module. Shaping comes from [HarfBuzz](https://harfbuzz.github.io), compiled from source in `Native~/third_party`; see [third-party notices](THIRD-PARTY-NOTICES.md). The iOS and web libraries link statically, so they rename HarfBuzz's symbols to avoid the copy Unity's web player links.

## Tests

**Window → General → Test Runner**. The Unicode suite runs the official conformance files for UAX #9, #14 and #29.
