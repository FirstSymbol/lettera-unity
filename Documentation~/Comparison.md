# Lettera and TextMesh Pro

[Русская версия](Comparison.ru.md)

Every figure below comes from a script: [compare/](compare/) runs both engines and writes the tables, and you can rerun it. A row TextMesh Pro wins is shown as it came out.

## Method

- Unity 6000.4.8f1, Windows player, Mono, Direct3D12, AMD Ryzen 7 4800H with Radeon Graphics. Median of 5 runs, a cold start each. Nothing else heavy running.
- The same font file on both: Noto Sans. TextMesh Pro in two setups, each its best case: a static atlas prepared ahead with the characters used, and a dynamic atlas. Lettera has one.
- The work is forced inside the clock (`ForceMeshUpdate`, `ForceRebuild`), and rows named "a frame" time the whole frame, so that no row is won by moving work out of it.
- Allocation comes from a development player, with a deliberate allocation showing that the counter counts.
- Since 1.0's third optimization batch, the rows that follow the long texts (scrolling a document, mixed labels, 5000 world texts, 3000 Han) start after a collection and ten idle frames, the same for every engine, so that none pays for the garbage of the row before it. Earlier figures had no such pause.
- A text whose paragraphs are partly left for later frames (Lettera's long texts) is timed on the frame it is set; the finishing in the frames after is the frame time of whatever row comes next.

## Time

| Row | Lettera | TextMesh Pro, static atlas | TextMesh Pro, dynamic atlas |
|---|---:|---:|---:|
| Prepare a font (R0) | 2.98 ms | 54.55 ms | 38.12 ms |
| Create 1 000 labels (R1) | 417 ms | 270 ms | 349 ms |
| The frame after (R1) | 25.47 ms | 30.67 ms | 27.63 ms |
| Rebuild 1 000 labels (R2) | 38.64 ms | 55.39 ms | 56.70 ms |
| 64 labels change a frame (R3) | 2.03 ms | 1.52 ms | 1.50 ms |
| One label changes a frame (R4) | 1.28 ms | 1.21 ms | 1.12 ms |
| Colour of 100 labels a frame (R5) | 1.62 ms | 6.25 ms | 6.40 ms |
| Alpha of 100 labels a frame, by property (R6) | 1.63 ms | 6.24 ms | 6.40 ms |
| A frame of 1 000 unchanging labels (R7) | 3.17 ms | 4.27 ms | 4.30 ms |
| Set 10 KB, first long text of a run (R8) | 94.71 ms | 25.75 ms | 26.66 ms |
| Set 10 KB again (R8) | 12.51 ms | 21.07 ms | 21.68 ms |
| Set 100 KB (R8) | 216 ms | 279 ms | 293 ms |
| Set 1 MB (R8) | 1717 ms | 4859 ms | 5246 ms |
| 100 KB, one character appended a frame (R9) | 83.10 ms | 207 ms | 216 ms |
| Scroll 1 MB: open and first frame (R10) | 147 ms | 7628 ms | 8240 ms |
| Scroll 1 MB: a frame (R10) | 7.48 ms | 1.77 ms | 1.98 ms |
| 5 000 world texts: create and first frame (R11) | 1041 ms | 412 ms | 420 ms |
| 5 000 world texts: a frame (R11) | 4.23 ms | 4.90 ms | 4.98 ms |
| 100 Arabic and Latin labels (R12) | 20.53 ms | 43.71 ms | 43.95 ms |
| 3 000 Han characters: first frame (R15) | 1266 ms | — | 12421 ms |

R12 times the work only: TextMesh Pro's Arabic comes out unjoined, as S2 shows, so its row is not the same result for less. R15's first frame is one run of each engine's own atlas filling.

## Allocation per frame

A usual frame (the median) / the largest single frame of the row.

| Row | Lettera | TextMesh Pro, static atlas | TextMesh Pro, dynamic atlas |
|---|---:|---:|---:|
| R3 | 0 B / 8 756 B | 0 B / 4 160 B | 0 B / 24 572 B |
| R4 | 0 B / 4 160 B | 0 B / 4 160 B | 0 B / 4 160 B |
| R5 | 0 B / 28 888 B | 0 B / 4 160 B | 0 B / 4 160 B |
| R6 | 0 B / 4 160 B | 0 B / 4 160 B | 0 B / 4 160 B |
| R7 | 0 B / 2 112 B | 0 B / 2 112 B | 0 B / 2 112 B |
| R9 | 209 440 B / 32 263 044 B | 38 717 812 B / 42 388 222 B | 38 717 812 B / 42 388 222 B |

## Memory and size

| Row | Lettera | TextMesh Pro, static atlas | TextMesh Pro, dynamic atlas |
|---|---:|---:|---:|
| Atlas textures after Latin | 4.0 MB | 8.0 MB | 8.0 MB |
| Atlas textures after 3 000 Han characters | 24.0 MB | — | 88.0 MB |

The figure is what `Profiler.GetRuntimeMemorySizeLong` says of each engine's textures. It counts a readable texture's copy in memory too, which TextMesh Pro's dynamic atlases keep.

| | Lettera | TextMesh Pro, static | TextMesh Pro, dynamic |
|---|---:|---:|---:|
| Whole player's peak over a run | 2067 MB | 3742 MB | 5942 MB |
| Managed assemblies in a Windows build | 1675 KB | 437 KB | 437 KB |
| Native library in a Windows build | 1656 KB | none | none |

## Features

Each cell is Yes, With setup, Partly or No. A sheet (S) or a named test stands behind each. TextMesh Pro's column was seen on these sheets where a sheet is named. Elsewhere it is what is known of that engine, and was not run here.

| Feature | Lettera | TextMesh Pro | Behind it |
|---|---|---|---|
| Latin, Cyrillic, Greek | Yes | Yes | S1, S6 |
| Arabic, joined | Yes | No | S2 |
| Hebrew | Yes | Partly: letters reversed by a flag, some marks misplaced | S2 |
| Numbers and Latin inside right-to-left text | Yes | No: reversed with the line | S2 |
| Devanagari, Bengali | Yes | No: conjuncts and vowel signs not formed | S3 |
| Tamil | Yes | Partly: letters drawn, signs not reordered by a shaper | S3 |
| Thai | Yes | Partly: marks not positioned | S3 |
| Khmer, Myanmar, Lao word breaks | Yes | No | `SegmentationTests` |
| Chinese, Japanese, Korean | Yes | With setup: fallback fonts | S4 |
| Ruby | Yes | No | `ListRubyTests` |
| Unicode line breaking (UAX #14) | Yes | Partly: its own rules | `UnicodeConformanceTests` |
| Bidirectional text (UAX #9) | Yes | No | `UnicodeConformanceTests` |
| Colour emoji, ZWJ and skin tone | Yes | With setup: a sprite asset of a project's own; a font's own colours are not drawn | S5 |
| Colour font kinds: COLRv0, COLRv1, sbix, CBDT | Yes (COLRv1 not yet seen on a device) | No | `ColorPaintTests` |
| System fonts, no font assigned | Yes | With setup: font assets by family name | S4, S6 |
| Fallback chosen by coverage | Yes | With setup: a list per font | S6 |
| Variable fonts | Yes | No | `TypographyTests` |
| OpenType features by tag | Yes | Partly: kerning, some by a font setting | `TypographyTests` |
| Tags both have | Yes | Yes | S7 |
| Custom tags and effects | Yes | With setup: code over `textInfo` | `MatcherTests` |
| Markdown, bare links, trigger words | Yes | No | `MatcherTests` |
| Outline, shadow, glow | Yes, per range by tag | With setup: one material per look | S8 |
| Inner shadow | Yes | Partly: a material keyword; no visible result in the settings tried | S8 |
| Several layers on one range | Yes | Partly: what one material holds | S8 |
| Gradient | Yes, along a range | Partly: per glyph corners | S8 |
| Sharp corners (MSDF) | Yes | No | S9 |
| Motion by tag | Yes | With setup: code | S10 |
| Selection and editing | Yes | Yes, through `TMP_InputField`; selection of a label needs code | `EditingTests` |
| Rich clipboard | Yes | No | `ClipboardTests` |
| IME | Yes | Yes | `ImmTests` |
| Native mobile input | Yes (Android, iOS, Web) | Partly: the platform's keyboard | not seen on a device |
| Math | Yes | No | `MathTests` |
| Extruded 3D text | Yes | No | samples |
| Long documents, windowed | Yes | No | R10, `DocumentTests` |
| Timeline | Tracks exist, not played end to end yet | With setup | — |
| Migration from the other | Yes, Lettera from TextMesh Pro | — | `MigrationTests` |
| Built-in, URP | Yes | Yes | builds |
| HDRP | Solid text with a material of your own | Unity documents it; not run here | — |
| Native plugin needed | Yes | No | build |
| Years in the field | New | Long | — |


## Sheets

The same scene in each engine, at 1200 × 700. Lettera on the left, TextMesh Pro on the right. System fonts on Windows drew what Noto Sans does not hold.

**S1 Latin**

![S1_latin lettera](comparison/S1_latin_lettera.png) ![S1_latin tmp-static](comparison/S1_latin_tmp-static.png)

**S2 Right to left. TextMesh Pro with fallback fonts and `isRightToLeftText` on**

![S2_rtl lettera](comparison/S2_rtl_lettera.png) ![S2_rtl tmp-fallback](comparison/S2_rtl_tmp-fallback.png)

**S3 Complex scripts. TextMesh Pro with fallback fonts**

![S3_complex lettera](comparison/S3_complex_lettera.png) ![S3_complex tmp-fallback](comparison/S3_complex_tmp-fallback.png)

**S4 Chinese, Japanese, Korean**

![S4_cjk lettera](comparison/S4_cjk_lettera.png) ![S4_cjk tmp-fallback](comparison/S4_cjk_tmp-fallback.png)

**S5 Emoji. TextMesh Pro with the emoji font as a fallback, and no sprite asset. A flag shows as JP in both: Windows' emoji font has no flags**

![S5_emoji lettera](comparison/S5_emoji_lettera.png) ![S5_emoji tmp-fallback](comparison/S5_emoji_tmp-fallback.png)

**S6 Five scripts in a line**

![S6_fallback lettera](comparison/S6_fallback_lettera.png) ![S6_fallback tmp-fallback](comparison/S6_fallback_tmp-fallback.png)

**S7 Markup. The last line is Lettera's own tags, shown as text by the other**

![S7_markup lettera](comparison/S7_markup_lettera.png) ![S7_markup tmp-static](comparison/S7_markup_tmp-static.png)

**S8 Effects. Lettera by tag and style, TextMesh Pro by material and vertex gradient**

![S8_effects lettera](comparison/S8_effects_lettera.png) ![S8_effects tmp-static](comparison/S8_effects_tmp-static.png)

**S9 At 200 px. Lettera's SDF, TextMesh Pro's SDF**

![S9_sharp lettera](comparison/S9_sharp_lettera.png) ![S9_sharp tmp-static](comparison/S9_sharp_tmp-static.png)

**S10 A still of motion by tag. TextMesh Pro needs code for this, so its picture shows plain text**

![S10_motion lettera](comparison/S10_motion_lettera.png) ![S10_motion tmp-static](comparison/S10_motion_tmp-static.png)

**S9 MSDF, Lettera**

![S9 MSDF](comparison/S9_sharp_msdf_lettera.png)

## When to stay on TextMesh Pro

- **You create many labels at once and every millisecond there counts.** 1 000 labels take 417 ms here and 270 ms there, and 5 000 world texts 1041 ms and 412 ms.
- **Numbers that change every frame on many labels.** 64 labels take 2.03 ms here and 1.52 ms there.
- **Scrolling one very long text, once it is open.** 7.48 ms a frame here, 1.77 ms there. Opening it is the other way round, by a long way.
- **A build that must stay small, or no native plugin.** TextMesh Pro is 437 KB of managed code and nothing else.
- **HDRP,** where Lettera has no materials of its own.
- **You rely on `textInfo`,** or on years of field use. Lettera is new, and parts of it have not been seen on a real phone.
- **Latin only, and a static atlas you already have.** Then most of what Lettera adds, you will not use.

## What was not measured, and why

- **Draw calls and batches (R14).** The `Batches Count` recorder gave no valid value in this player, even in a development build. The other route, the Frame Debugger's count in the editor, has not been done.
- **Scene load, and first use of a 16 MB font (R16); inspector typing (R18); alpha through `CanvasGroup` (R6, by property only); atlas after 200 emoji (R15).** Not written yet.
- **Build size per platform (R17).** Windows only, above.
- **IL2CPP on Windows.** The module is not installed on the machine used.
- **Web and Android.** Not yet; Android needs the phone of whoever runs it.
- **iOS.** Exported only. No Mac was at hand.
- **TextMesh Pro's emoji by sprite asset,** which a project would prepare itself.
- **Fields (S11) and variable fonts (S12) have no sheet.** Tests stand behind those cells.

## Run it yourself

```
python compare.py <Unity.exe> <path to this package> <work folder> 5
python report.py <work folder>
```
