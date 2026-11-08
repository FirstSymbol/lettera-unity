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
https://github.com/FirstSymbol/lettera-unity.git#1.0.0-rc.2
```

Requires Unity 6.3 LTS (6000.3) or newer.

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

## Guides

In `Documentation~`, each in English and Russian; **Tools > Lettera > Getting Started** opens them too.

- [Lettera](Documentation~/Lettera.md): what it is, installing it, five minutes.
- [Getting Started](Documentation~/GettingStarted.md): text on screen, fonts, markup, styles, fields, motion, documents, formulas and 3D, in task order.
- [Fonts](Documentation~/Fonts.md), [Styles](Documentation~/Styles.md), [Fields](Documentation~/Fields.md), [Motion](Documentation~/Motion.md), [Documents](Documentation~/Documents.md), [Formulas and LaTeX tables](Documentation~/Math.md), [3D text](Documentation~/Text3D.md).
- [Tools](Documentation~/Tools.md): every window, the Scene view, the palette and the keys.
- [Migration](Documentation~/Migration.md): from TextMesh Pro.
- [Platforms](Documentation~/Platforms.md): what has run where, plainly.
- [Comparison](Documentation~/Comparison.md) against TextMesh Pro, in English and [Russian](Documentation~/Comparison.ru.md), and the [Agent guide](Documentation~/AgentGuide.md), in English only.

## Markup

A tag means something only because a style binds it to an effect; a tag nothing claims stays as literal text. The built-in vocabulary applies until a project assigns its own global style preset, and **Use Global Style Preset** turns it off per text.

| Tag | Effect | Parameters |
|---|---|---|
| `<b>`, `<strong>` | Weight | weight 100-900; mode: `f` forces synthetic, `r` real faces only |
| `<i>`, `<em>` | Italic | slant in percent of the glyph height |
| `<size>` | Size | size: `24`, `150%`, `+10`, `-5`, `1.2em` |
| `<color>` | Color | color: `#RGB`, `#RRGGBB`, `#RRGGBBAA`, or a name |
| `<alpha>` | Alpha | alpha: `0.5`, `50%`, `#80` |
| `<font>` | Family | family: a family name of the font stack |
| `<lang>` | Language | language: a BCP 47 tag |
| `<u>`, `<s>` | Underline, strikethrough | paint, style, thickness, offset, skip ink, overlay, then paint settings |
| `<outline>` | Outline | color, width in em or `2px`, then paint settings |
| `<shadow>` | Shadow | color, x, y, softness, dilate, then paint settings |
| `<fill>` | Face paint | paint, dilate, softness, then paint settings |
| `<stroke>` | Band along the outline | paint, width (em or `2px`), align: `inside` `center` `outside` or -1 to 1, softness, corners: `round` or `sharp` |
| `<glow>` | Halo | paint, radius, softness, dilate |
| `<inner-shadow>` | Shadow cast inward | paint, x, y, softness, choke |
| `<extrude>` | Stacked copies | x, y, near color, far color, dilate, softness |
| `<filter>` | Color filter | intensity, `grayscale` `invert` `sepia` |
| `<union>`, `<warp>` | Merge overlaps; distort the outline | none; amplitude, scale, travel |
| `<mark>` | Highlight | paint, shape: `glyph` `line` `range` `block`, paint span, radius, padding, order: `behind` `above`, height |
| `<uppercase>`, `<lowercase>`, `<smallcaps>` | Case | small caps: scale of synthetic ones |
| `<sup>`, `<sub>` | Superior, inferior | placement: `super` or `sub` |
| `<otf>` | OpenType features | `onum`, `-dlig`, `cv02 3` |
| `<axes>` | Variable axes | wght, wdth, ital, slnt, opsz; `640`, `120%`, `+80`, `~`; `GRAD 50` |
| `<size-match>` | Match fallbacks on | `XHeight` or `CapHeight` |
| `<ruby>`, `<rt>`, `<rp>` | Readings above text | `<ruby=reading>` or `<rt>` |
| `<tracking>` | Letter spacing | amount in local units or `em` |
| `<word-spacing>` | Word spacing | local units or `em` |
| `<cell>` | Equal width cells | width, or `auto` |
| `<line-height>` | Line height | height (a multiplier, or px, em, %), mode, leading |
| `<para-spacing>`, `<line-gap>` | Paragraph and line spacing | after, before; amount |
| `<nobr>` | Keep together | |
| `<li>` | List item | level, number, marker text |
| `<indent>` | Margin | amount in local units, `em`, or `%` of the rect |
| `<align>` | Paragraph alignment | alignment, justify mode, last line |
| `<trim>` | Text box trim | top edge, bottom edge |
| `<curve>` | Curved baseline | radius, anchor |
| `<divider>` | Separator a line can break at | string |
| `<link>`, `<a>` | A URL; a URL with a look | URL |
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

## Styling

Every colored surface takes a paint: a color, a gradient or a texture. Build styles in the inspector's **Styles** tab, where **+ Add Style** offers everything by how it is driven and what it does, or in code:

```csharp
var fill = new FillEffect();
fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(orange, pink, blue), LetteraPaintSpan.Range));
label.Styles.Add(LetteraStyle.ForTag(fill, "sunset"));
label.MarkStylesDirty();
label.Text = "<sunset>A gradient across the whole range</sunset>";
```

A gradient measured on the **Range** runs across a sentence, on the **Glyph** or **Line** it restarts. Solid and gradient paints never split a batch; a texture or a blend other than normal gets its own material, and order is kept.

A slow gradient that shows bands takes **Grain**: finer steps and a fine grain, drawn by a slightly heavier shader that only texts with it use.

Layers draw in bands: shadow and glow, stroke, face, inner shadow. Within a band, later styles draw on top. Filters reach what lies below them in the style list and the face, so a filter last in the list recolors everything.

Change a parameter at the cost of its level, with no parsing:

```csharp
stroke.Width = 0.05f;
label.MarkEffectsDirty();          // Mesh level by default.

LetteraHold<Color32> hold = label.Ranges(color).WithLabel("warn").Hold<Color32>(0);
hold.Value = red;                  // Every <color #warn=...> range, without touching the field.
hold.Release();                    // Back to what the markup says.
```

Several effects share a style through `CompositeEffect`, whose tag value has one semicolon-separated segment per child: `<badge=(mailto:a@b.io);teal;wavy>`. Save one as an effect preset asset to reuse it.

**Ranges can come from more than tags:** Markdown (`**bold**`, `- lists`, `[links](url)`, `` `code` ``), bare URLs, trigger words such as `@mentions`, literal strings. Those are not in the default vocabulary, because they change plain text: add them from the picker.

**MSDF:** set **Render Mode** to MSDF for sharp corners at any size, at more atlas memory. **Pixel fonts:** set a font asset's **Pixels Per Em**, or press **Find Pixel Grid**; at multiples of the grid every font pixel is exact, and effects come in whole font pixels.

**Hairline faces:** set a font asset's **Raster Detail** to Fine or Finest, for tiles of at least 128 or 256 pixels per em, at four or sixteen times the atlas room per glyph.

**Your own effect:** **Assets → Create → Lettera → Custom Effect** writes a shader whose one function you edit, for a **Custom Material** style. It works on Canvas and world text, with the Built-in and Universal pipelines.

## Selection and editing

```csharp
gameObject.AddComponent<LetteraSelectable>();           // select and copy
var field = gameObject.AddComponent<LetteraEditable>(); // and edit
field.AddRule(new SingleLineRule());
field.AddRule(new MaxLengthRule { Limit = 40 });
field.Submitted += value => Debug.Log(value);
```

**GameObject > Lettera** creates an input field, text area, password field, dropdown, selectable or editable text. The Fields sample builds all of them from code.

- A field's value is its source text, markup included: `field.Source`. `field.PlainText` is the value without markup, and `CopyTo` and `Equals` read it without allocating.
- Rules hold the policy and run in list order. A filter judges what an edit leaves behind, a validator only reports, and a decorator shows the state on UI you assign.
- Commands in `field.ToggleEffect<BoldEffect>()` style write the field's own syntax and need a style that writes that effect: they never add one.
- World text needs a `LetteraWorldRaycaster` on the camera, and every text an `EventSystem` with an input module.
- Under a Canvas Group that is not interactable, a selectable text or a field takes no press: no caret, no selection, no menu, and a drag goes to the scroll view around it. `IsInteractable` says which.
- `text.Units(LetteraTextUnit.Word)` walks the clusters, words (by UAX #29 and the installed dictionaries), lines or paragraphs a text shows, all of it or a range, and `CountUnits` counts them, neither allocating. `FindAll` finds every place a string stands, with or without case.
- Everything reaches the OS through `ILetteraInputSource` and `ILetteraClipboardBackend`, which you can replace.

## Motion and interaction

```
<wave>moves with no rebuild</wave>  <shake>shakes</shake>  <roll>1234</roll>
<a=https://unity.com>a link</a>  <spoiler>a secret under a cover</spoiler>  <sprite=star>
```

- **Motion** runs on a pose pass: no rebuild and nothing allocated per frame. `RevealEffect` is a typewriter, `MarqueeEffect` a ticker.
- **`LetteraSequencer`** ramps any effect parameter over a timeline, and a curve can drive its `Progress`. **Tools > Lettera > Sequencer** shows its cues on rows, to drag, split and copy. `LetteraParamLink` gives one parameter an animatable field. Timeline tracks appear when that package is installed.
- **Ranges** answer a pointer and a keyboard: `LetteraRangeRouter.Of(text).All.Activated += e => Debug.Log(e.Primary);`. Links open through an allow-list of schemes, in `LetteraSettings.Motion.LinkSchemes`.
- **`LetteraRangeRule`** changes parameters on hover, press or focus. **Inline items** come from `InlineEffect` or a `LetteraInlineLibrary`. **`LetteraSemanticTree`** describes ranges for an assistive bridge. Effects can join any stage of a rebuild through `Stages`.
- `LetteraAccessibility.ReducedMotion` is honored everywhere, and on the web it follows the browser.

## Documents

```csharp
text.OpenDocument(LetteraFileSource.Load(path), takeOwnership: true);   // Or LetteraRope.FromString(...), or a Source Loader component.
scrollerObject.AddComponent<LetteraDocumentScroller>().Text = text;
text.View.ScrollToParagraph(1000);
```

- Only what is near the view is laid out, and a scroll inside that is no rebuild. A file is mapped and decoded only as read.
- A `LetteraEditable` on the same text edits the document. `LetteraDocumentSearch` finds text a slice at a time.
- Thai, Lao, Khmer and Myanmar break by words once a `LetteraWordDictionary` is listed in the project settings. Nothing is listed by default, so nothing is added to a build.
- **A long text without a document** places and draws glyphs only on the lines near what its canvas and masks let show; a scroll past them places the new lines before that frame draws. When it is set, only its first paragraphs and those near what shows are shaped and broken into lines; the rest stand at an estimated height and are finished over the next frames, about 2 ms a frame (`LetteraLongText.FinishMillisecondsPerFrame`), in reading order. Until then its height, a layout's preferred height and so a scrollbar are estimates and may move; `IsFinishing` says so and `FinishNow()` ends it. `Lines`, carets, selection, hit tests, links and measures finish everything first, so they are exact anywhere. An edit of a plain text redoes only the paragraphs from the one it touched. `GlyphCount`, `GetGlyph` and `InkBounds` cover the placed lines. `LetteraLongText` turns either part off or sets where it starts. A document's window, too, places only the lines near its viewport; scrolled faster than its margins reach, it holds and places only what shows, and takes its margins again two frames after the scroll stops.

## Formulas

```csharp
label.Text = "Energy is <math>E = mc^2</math>, and <math=display>\\sum_{i=0}^n i = \\frac{n(n+1)}{2}</math> stands by itself.";
```

- A formula is written in a linear subset of TeX, inside `<math>`. `<math=display>` makes a paragraph of its own. `<math=inline,x squared>x^2</math>` gives it words for a screen reader.
- **Commands:** fractions and binomials, roots, scripts and limits, large operators, `\left \right` and sized delimiters, accents, bars, braces, arrows, matrices, `cases`, `aligned`, `array`, `\text`, spacing, `\color`, alphabets such as `\mathbb`, and about 475 symbols. No macros and no `\def`.
- **Chemistry and quantities** as mhchem writes them: `\ce{2H2 + O2 -> 2H2O}`, `\ce{SO4^2-}`, `\ce{^{227}_{90}Th}`, `\ce{C6H5-CHO}`, `\ce{A <=>[H2O][\Delta] B}`, `\ce{BaSO4 v}`, and `\pu{1.2e3 kJ//mol}`. Numbers become subscripts and signs charges, states such as `(aq)` stay upright, arrows of every kind carry text above and below, and units take their thin spaces and multiplication dots.
- A formula that cannot be read draws what was written in a frame, and the text around it renders. `LetteraMath.Validate` checks one, and `LetteraMath.Failed` tells of each.
- **Equation numbers.** In `<math=display>`, `align`, `gather`, `equation` and `multline` number themselves, (1), (2) and on, counted per text. `\tag{x}` puts a label in a number's place and `\tag*{x}` drops its parentheses, `\notag` or `\nonumber` leaves a row without, and `\setcounter{equation}{n}` makes the next number n + 1. A number stands at the line's right edge, on the left in a right-to-left text, centred on its row; `LetteraFormulaInfo.Tags` lists them.
- **Environments need no tag.** `\begin{align} a &= b \\ c &= d \end{align}` written in ordinary text is a display formula, and so are `equation`, `gather`, `multline` and `displaymath`, starred or not, numbered on the same count. One inside a table is the table's, and a table inside one is the formula's.
- A formula is one object in its line: it does not break, and selects, copies and deletes whole. It takes color, size, alpha, stroke, shadow, glow and motion like text, and moves as one piece.
- Copying gives `$source$` as plain text, MathML with its source as HTML, and the tag to a Lettera field.
- **Which font.** In order: the style's `LetteraMathEffect.Font`, the project's `LetteraSettings.Math.Font`, the text's font if it holds a math table, a system math font (Cambria Math on Windows; on the web, Android and iOS there is none), and last the text's font with fallback layout, which is readable but plain.
- **The bundled font.** Noto Sans Math is in the package and enters a build only when something references it. **Lettera > Math > Use Bundled Font** sets it for the project, at 343 KB in every build. On a style it costs only where that style is used. A build for the web, Android or iOS warns when a scene holds a formula and no font is set. Any font with a MATH table works: STIX Two Math and Latin Modern Math look more classic.
- `LETTERA_NO_MATH` compiles formulas out. `<math>x</math>` then shows `x`.

## LaTeX tables

```csharp
label.Text = "Prices: \\begin{tabular}{|l|r|}\\hline Apples & 1.20 \\\\ Pears & 0.95 \\\\ \\hline\\end{tabular} today.";
```

- `tabular`, `tabular*`, `tabularx`, `tabulary` and `longtable` in ordinary text, and a `table` float around one with `\caption` and `\centering`. A `tabular` stands in its line, centred on it or by `[t]` and `[b]`; `longtable` and `table` stand on a line of their own.
- Columns `l c r`, `p{} m{} b{}` with a width, `X`, `L C R J`, `|` rules, `@{}`, `>{\bfseries}` and `*{n}{...}`. Rows take `\hline`, `\cline`, booktabs rules and `\\[space]`; cells take `\multicolumn`, `\multirow`, `\textbf` and its kin, `$...$` formulas and tables of their own. longtable shows its first head and its last foot.
- **Widths.** Columns size as CSS's automatic table layout does: each as wide as its content wants when that fits, else each between its narrowest and widest in proportion, and narrower still if it must. A table never runs past the text's width, and lays out again when the text resizes. `tabularx` and `tabular*` fill their set width, held to the text's.
- Copying gives the table's source. Huge sizes are cut back with one warning: `LetteraTableLimits`.

## 3D text

```csharp
LetteraText3D solid = go.AddComponent<LetteraText3D>();    // Or GameObject > Lettera > Text 3D.
solid.Text = "Solid <wave>Text</wave>";
solid.Depth = 0.25f;
solid.FrontBevel = new LetteraBevel { Shape = LetteraBevelShape.Round, Width = 0.03f, Height = 0.03f, Roundness = 0.5f, Segments = 4 };
```

- Real geometry, lit and shadowed, drawn by the object's own `MeshRenderer`: nine edge shapes and a curve of your own, five material slots that fall back to one another, and a `Lettera/Solid Lit` shader for Built-in, URP and HDRP, where it goes through the pipeline's own light loop. World text draws under HDRP too, lit or not.
- Markup, fonts, fallback, bidi, Arabic and Indic shaping, wrapping and auto size are the same as a world text's. So is motion: every motion moves the solids, and a fade goes through a screen dither, so nothing needs sorting.
- Each glyph is built once, whatever the text's depth, and shared by every text with the same font and edges. Identical texts share one mesh, so they can instance.
- `<depth=2>` and `<bevel=chamfer,0.03,0.03>` act on a range. `LetteraSolidLook` holds a depth, edges, materials and layers to give at once.
- **A range takes its own materials and pose.** `<surface=gold>` gives it the materials of a named set on the text, and `<surface=gold,face,side>` only those surfaces. `<pose=pitch,yaw,roll,lift,scale>` stands each glyph in a fixed pose about its middle, or the range as one with a sixth slot `whole`.
- **Motion in depth:** `<revolve>`, `<swing>`, `<surge>`, `<orbit>`, `<tumble>`, with rate, step and axis as slots. A custom motion derives from `LetteraSolidMotion`, and `solid.SolidPose` takes an `ILetteraSolidPose` that poses each glyph from a script. **Reveals in depth:** `<reveal=turnin>`, `flyin`, `deepen`, `tumblein`; on flat text they are a fade.
- **Seven looks ship** in `Runtime/Text3D/Looks`: Plain, Gold, Chrome, Candy, Stone, Neon and Sign, on buttons in the inspector's Look card. Neon brings a tube around each glyph and Sign a board behind the whole text.
- **Layers** are extra solids per glyph, word, line or text: an outline grown or shrunk, a rounded plate, or a neon tube along each outline, as a band or with the letters cut through or engraved, each with its own depth, edge, colour and materials. `solid.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.Badge))`.
- **Deformers** bend (360 degrees makes a ring), twist, taper, wave, wrap onto a ball or follow a path, in order, and take the layers and underlines along: `solid.AddDeformer(LetteraDeformer.Bend(360))`. A range takes its own: `<bend=90>`, `<twist=180>`, `<taper=0.5>`, `<ripple=0.1,1.5>`, `<sphere=120>`.
- Underline and strikethrough extrude. Formulas extrude, with their bars as boxes. A color glyph draws flat on the front plane, or not at all.
- **What it does not do:** 2D layers (stroke, shadow, glow) do not draw on a solid. No selection or editing.
- **In a Canvas.** It is not a Graphic, since a Canvas has no light: it draws as a scene object, without masks or layout, and not at all under Screen Space Overlay. For solids in a UI, point a camera at the text, with a `RenderTexture` as its target, and show that in a `RawImage`. The 3D sample does this.
- **In a build.** A scene's component brings its material and shader. A build with none holds neither. A text made from a script with no material needs **Lettera > 3D > Allow Creating From Scripts**, which puts the shader in every build. `LETTERA_NO_3D` removes the module.
- Its context menu has **Bake To Mesh Asset** and **Export OBJ**, and a world text's has **Convert To Text 3D**.
- **Colliders:** `LetteraText3DColliders` keeps a box or a convex hull per glyph, or the drawn mesh, matching the text through rebuilds, motion and deformers; a Rigidbody on the text takes them as one compound body. It compiles only with the Physics module.
- **Rays and places:** `solid.Raycast(ray, out LetteraSolidHit hit)` finds the nearest point of the drawn solids, layers included, with no collider. `solid.TryGetPose(hit.Anchor, out Pose pose)` says where that place is any later frame, through motion and deformers.
- **Copies on glyphs:** `LetteraGlyphAttachments` puts prefabs on each glyph's top, bottom or front, along its outline, or scattered over what faces up, sized in em, varied by scale, turn and seed. They ride their glyph through motion and deformers, and hide with it.
- **Bake...** in its context menu also makes a static object, with lightmap UVs if asked, a level-of-detail group, and FBX when the FBX Exporter package is in the project. Materials made only in memory are saved beside the mesh.

## Platforms

| Platform | Native plugin | System fonts and emoji |
|---|---|---|
| Windows x64, ARM64 | prebuilt | yes |
| macOS universal | prebuilt | yes |
| Linux x64, ARM64 | prebuilt | yes, read from the font folders |
| iOS device and simulator | prebuilt XCFramework | yes, through CoreText |
| tvOS and visionOS, device and simulator | prebuilt XCFramework, not yet run on either | yes, through CoreText |
| UWP x64, ARM64 | the Windows library, not yet built or run | the Windows font folder, where the app may read it |
| Android ARM64, ARMv7, x86-64 | prebuilt | yes |
| Web (WebGL 2), including Safari on iOS | prebuilt | emoji through the browser; text fonts must be assigned |
| Web (WebGPU), alone or listed before WebGL 2 as a fallback | the same as WebGL 2 | as on WebGL 2 |

See [the changelog](CHANGELOG.md) for what each platform has been verified on.

### The web

Browsers expose no font files, so on the web:

- A text with no font uses a bundled Noto Sans, which covers Latin, Cyrillic and Greek. **Other scripts show missing-glyph boxes until you assign a font** that covers them, through **Font** or **Font Stack**. Shaping then works as everywhere else.
- Emoji are drawn by the browser's own canvas, so they look like that device's emoji. Colors and alpha apply as usual.
- `LetteraSettings.WebGLFallbackFont` replaces the bundled font.
- Glyph memory is capped lower than on other platforms: the least recently used atlas page is recycled when the limit is reached.

### Native platform

Fields, clipboards and menus can run through the platform's own controls. Each piece has a gate under Project Settings > Lettera > Native Platform, and it says why it is on or off where you are building.

| | Windows | Web | Android, iOS | macOS |
|---|---|---|---|---|
| Text input | composition through the input method | one text box, inline composition | off until turned on | Unity's |
| Clipboard with formats | on | on | off until turned on | off until turned on |
| Replica (`ReplicaFieldRule`) | inert | on, when a field asks | off until turned on | inert |
| Fonts packed beside the build | on | no | inside the asset | on |
| COLRv1 | on | on | on, not yet seen on a device | on, not yet seen on a device |

What has not been seen to run on a real device ships off. The Device Check sample walks through a device one screen at a time, and gives one text block to send back: **Lettera > Device Check > Build for...** turns the routes on for that build only.

```csharp
// Paste works everywhere through the command. In a browser the clipboard can only be read later.
new PasteCommand().Execute(new LetteraCommandContext(field.Selectable));
field.PasteAsync();

// What fonts cost right now.
LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
```

After updating, the editor keeps the native library it loaded until it restarts: COLRv1 waits for that, and everything else works.

[Platforms](Documentation~/Platforms.md) says, platform by platform, what has been built, run and seen by a person.

## What 1.0.0 is not

These are left for 1.x:

- Emoji and other colour glyphs as stickers on 3D text: the picture's outline cut out, grown by a border and extruded with the text.
- Scene volumes, such as a ball, a box or a half-space, that move, turn, scale and tint the glyphs they reach.
- Breaking a text into one object per glyph, each with its own mesh and collider.
- A bevel wider than half a stroke closing the stroke in a ridge; such an edge now narrows to a flat strip.
- Animated solid glyphs placed by the shader, so that a moving 3D text costs per glyph rather than per vertex.
- New solid glyphs built in the background and shown when ready.
- A baked asset of solid glyphs, made ahead of time.
- Detail of solid glyphs that falls as a 3D text gets smaller on screen.
- A native paste button on iOS, which pastes without the system's permission prompt.
- Gesture recognizers of your own on a range, such as a drag, weighed against selection and scrolling.
- Keyboard and gamepad focus that moves from a text to the nearest control across canvases, with the on-screen keyboard's Next and Previous in the same order.
- A world-space document that follows a camera's view of its plane.
- Scene-view picking that clicks through transparent and masked parts of a canvas.
- A noise-texture tool for shader effects.
- LaTeX text commands outside formulas and tables: `\textbf`, `itemize`, `\Huge` and their kin in ordinary text.
- Unity 2022: Lettera needs Unity 6.0 or newer.

## Samples

In **Window > Package Manager > Lettera > Samples**. Each is built from code: add its component to an empty object in an empty scene, and press Play.

- **Basic:** mixed scripts, emoji, tags, wrapping, eliding, auto size and runtime text, with no fonts in the project.
- **Fields:** a chat box, a checked e-mail field, a password, a rich text area, a dropdown and selectable text.
- **Motion:** every motion, a typewriter, a ticker, a link, a spoiler, a hover rule and an inline sprite.
- **Documents:** 200 000 lines in a scrolling view, searched and jumped through.
- **Math:** formulas in a sentence and by themselves, every construct beside its source, a field that shows what is typed. Assign a font on the web.
- **3D Text:** a line per edge shape, each look, a formula, a plate, neon, a twist and a ring, 500 shared labels, and solid text in a Canvas. Assign a material with the Lettera/Solid Lit shader.
- **Device Check:** one guided session per device, and one text block to copy or share.
- **Emoji:** flags, joined sequences, skin tones, keycaps, alpha and color on emoji, and a small color font of its own.
- **Layout:** one box of text with a live control for alignment, wrap, overflow, auto size, line height, trim, curve and padding.
- **Fonts:** system fonts, a stack, a variable axis, OpenType features, a pixel font and an icon font, made from code.
- **Styles:** a gallery beside its markup: every layer, gradients, filters, presets, a tag of your own and an effect with its own shader.
- **Rich Editor:** a word processor's page: a toolbar that follows the caret, the keys you expect, and paste that keeps formatting.
- **Timeline:** needs the Timeline package. A cut scene built from code, with a line typed by a clip, a sequencer and a linked parameter on three tracks.

## Against TextMesh Pro

[Comparison](Documentation~/Comparison.md): times, memory, features and pictures, including when to stay on TextMesh Pro.

## Agents and the command line

With `com.unity.pipeline` installed, `unity command lettera ...` creates, edits and renders text from a terminal or an agent. [Agent guide](Documentation~/AgentGuide.md), in English.

## Rebuilding the native plugin

`Native~/build.py` builds HarfBuzz and the C wrapper for every platform with [zig](https://ziglang.org), and Android with the NDK from Unity's Android module. Shaping comes from [HarfBuzz](https://harfbuzz.github.io), compiled from source in `Native~/third_party`; see [third-party notices](THIRD-PARTY-NOTICES.md). The iOS and web libraries link statically, so they rename HarfBuzz's symbols to avoid the copy Unity's web player links.

## Tests

**Window → General → Test Runner**. The Unicode suite runs the official conformance files for UAX #9, #14 and #29.

## License

MIT: free for commercial and non-commercial use. See [LICENSE](LICENSE.md). Bundled third-party components keep their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md).
