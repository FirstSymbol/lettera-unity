# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.7.2] - 2026-11-01

### Fixed
- **The editor could crash in a domain reload.** Lettera releases its native fonts as a reload begins. A material inspector closing after that makes Unity rebuild canvases, and a text with work pending shaped against a released font. Present since fonts were first released this way. Nothing shapes now until the fonts are back, and the pending work stays pending.

## [0.7.1] - 2026-11-01

### Added
- The 3D sample makes its five looks in code: Plain, Gold, Chrome, Candy and Stone.
- A font's inspector has a Math card, whose button opens the font and says what its math table holds.

### Fixed
- `GlyphAtlas.Clear` left a formula's rules pointing at tiles that were gone.
- The 3D sample's tooltip named looks that did not exist, and it never released its render texture.

### Verified
- The Math and 3D samples run in a Windows player, and were looked at. Neither has run in a browser.

### Known
- **The 3D sample's player sometimes crashes on exit** on the development machine: about 1 run in 8, inside `D3D12Core.dll`, after all scripts have finished. Nothing is lost by then. Five scenes holding its parts one at a time, static, moving, counting, 500 shared and a render texture, did not do it in 40 runs, and nor did a control of Unity's cubes under the same light in 14. The cause is not found. A project's own scene has not been seen to do it.

## [0.7.0] - 2026-11-01

Formulas in the text's flow, and solid lit text with depth.

Native library 3 reads OpenType MATH. An editor still holding an older library lays formulas out by fallback until it is restarted, and everything else works.

### Added
- **Formulas.** `<math>` and `<math=display>` hold a linear, bounded subset of TeX: fractions, binomials, roots, scripts, limits, large operators, `\left \right` and sized delimiters, accents, bars, braces, arrows, matrices, `cases`, `aligned`, `array`, `\text`, spacing, `\color`, alphabets, about 475 symbols. `LetteraMathSpanMatcher` reads `\(..\)`, `\[..\]` and, when turned on, dollars.
- **Layout on OpenType MATH**, with all 56 constants, italic correction, top accents, glyph variants and assemblies. With no table, a text's own font lays formulas out with fallback spacing and Unicode's bracket pieces.
- **One object in its line.** A formula is laid out once in em and cached, so a size change, a re-wrap or a color change runs no math. It takes color, alpha, gradient, stroke, shadow, glow and motion, and moves as one piece. A formula that cannot be read draws what was written, and the text around it stands.
- **Rules that stay exact.** A bar is a distance field whose middle column stretches, so that it takes every layer, at any size. Assemblies' parts meet at the middle of their overlap, so nothing draws twice under alpha.
- **Selection and copy.** A formula selects, copies and deletes whole. Plain text gets `$source$`, Markdown the same, HTML MathML with its source, and a Lettera field the tag. `LetteraSemanticRole.Math` carries its words.
- `LetteraMath.Validate`, `Measure`, `Failed`, `Register`; `LetteraMathMarkup.ToMathML`; `GetFormulas` on each text component.
- **Noto Sans Math** in the package, 343 KB, out of any build that does not reference it. **Lettera > Math > Use Bundled Font**, and a build check that warns on the web, Android and iOS when a scene holds a formula and no font is set. `LETTERA_NO_MATH` compiles formulas out, and `<math>x</math>` then shows `x`.
- **3D text.** `LetteraText3D`, in its own assembly `FirstSymbol.Lettera.Text3D`, which core never names. Closed solids from glyph outlines, with overlapping contours cleaned by non-zero winding. Nine edge shapes and a curve of your own, narrowed where a stroke is thin so that they cannot cross. One solid serves every depth.
- **Five surfaces** that fall back to one another, `Lettera/Solid Lit` for Built-in and URP, with shadows and a fade through a screen dither. Textures per surface or across the text.
- **Everything text does.** Markup, fonts, bidi, shaping, wrapping and auto size are a world text's. Every motion moves the solids, with no vertex rebuilt but its own. Underline and strikethrough extrude, formulas extrude with their bars as boxes, and a color glyph draws flat on the front plane.
- `<depth=2>` and `<bevel=chamfer,0.03,0.03>` on a range. `LetteraSolidLook`. Identical texts share a mesh, so that they instance. **Bake To Mesh Asset**, **Export OBJ**, **Convert To Text 3D**. `LETTERA_NO_3D` removes the module.
- Inspectors for all of it, in the same custom UI: edge profiles drawn live, material fallbacks, vertex counts, chips for what a solid does not do, a Math card that appears when a text has a formula, Math and 3D cards in Project Settings. A solid material's inspector is one row that opens its card, since a `ShaderGUI` cannot build UI Toolkit.
- Math and 3D samples, and five new steps in the Device Check.

### Changed
- **`<math>` is now a tag in the default vocabulary.** A text showing those characters literally needs `<noparse>` or `\<math>`.
- `ITextHost` gained `SolidsOnly` and `HostStyles`, and `LetteraAttributes` a channel for what a module and its host agree on. Both are internal.
- `EscapeMatcher` runs just before math, so that an escaped tag stays text.

### Limits
- **No macros, `\ce`, `\pu` or LaTeX text rules.** Equation numbers: `\tag` is read and draws nothing.
- **An inline formula never breaks.** A long one overflows a narrow line. Right-to-left math is not done, so a formula always runs left to right.
- **Selection inside a formula: no.** Whether `RevealActive` in a field shows its source has not been run.
- **A paste of HTML with MathML gives its text**, not a formula. Rich copy works from Lettera outward, and from Lettera's own format back.
- **Fallback layout** with no MATH table is readable and plain, and has no stretch beyond the pieces Unicode has.
- **On solids:** `<surface=name>` is not done. A gradient paint does not reach a solid's vertices: a solid takes a color. Dashed, dotted and wavy lines extrude as plain boxes. A shear from `deform` moves the vertices by it, with normals by its rotation only.
- **Bevels narrow, and do not ridge.** A wide edge on a thin stroke leaves a thin flat strip.
- **HDRP** has no default material.
- **Canvas:** no solid `Graphic`. See the README's recipe.
- The kit's formula step has no reference picture beside it.

### Differences from Chromium
A browser is the judge for formulas, by MathML written from our own tree with our spacing written out. Where we differ on purpose or where it does:

| Row | Which | Why |
|---|---|---|
| Stretchy operators, radicals | Chromium is wider | With this font, the same glyph measures 32.8 px with `stretchy="false"` and 72.2 px with it, and the font's advance says 32.8. Heights agree within 1 px. |
| Superscript on a slanted letter | Ours sits further right, by its italic correction | TeX's rule. Chromium applies none to a letter. `x^2` differs by 0.05 em. |
| Scripts on an integral | Subscript tucked in by its correction | MathML Core's rule, kept in text style too, where Chromium drops it. |
| `\overline`, `\underline`, sized over and under braces | Ours covers the body | The MathML we write for these is an approximation. Ours were judged by eye. |
| Braces | Ours takes the first size that covers, Chromium a smaller | Ours covers. |
| Matrix rows | Ours keep a 1.2 em line | TeX's strut. Chromium has none. |
| Primes | Ours use the plain form at script size | The font's script form sits low. |

### Verified
- **Solids over whole fonts.** A sample of about 400 code points each of Noto Sans, Noto Sans Arabic and Noto Sans Devanagari, with the default edge: every edge shared by exactly two triangles going opposite ways, with no cap dropped. Every edge shape on a ring: closed, positive volume, normals outward.
- **Hand-written outlines.** Volume and area by the divergence theorem, overlapping squares as one outline, TrueType and CFF winding, a contour inside one of the same way, a bow tie, a stroke far thinner than its edge, the same bytes twice, and threads against inline byte for byte. 20 000 random command streams with NaN and infinities: no exception and no hang.
- **Placement.** Latin, Arabic, Devanagari and Hebrew, and a wrapped, aligned text with a size change: every glyph at a world text's position.
- **In a Windows player, looked at:** nine edge shapes under a shadowing light, scripts from the front and the side, a formula, an underline, a range's depth, a dithered fade, a wave.
- **Compiled out.** A Windows build with both defines passes the self-test and holds neither the shader nor the font. A build with the module but no solid text holds neither shader nor font, by a byte search of its files.
- **Formulas against Chromium 156**, in a Windows player, per pixel at 96 px, over the sheet's rows without stretched glyphs. See Figures. The contact sheet was looked at.
- A text with a formula: its box, its failure, every clipboard format, well-formed MathML for the whole sheet, a spin moving it whole, the screen reader's words, the bundled font's size, place and licence, and the build check where it warns and where it stays quiet.

### Figures
Windows Mono player, against a 0.6.0 build made the same way, in turns, median of 5, in ms:

| Row | 0.6.0 | 0.7.0 | Change |
|---|---|---|---|
| 64 labels a frame | 1.665 | 1.733 | +4.1% |
| 500 labels, first build | 112.7 | 109.9 | -2.5% |
| One level: parse, shape, layout, mesh | .0333, .0269, .0247, .0183 | .0339, .0272, .0247, .0183 | up to +1.8% |
| 200 world texts, first build | 33.3 | 33.5 | +0.5% |
| One world text changing | .0976 | .1031 | +5.6% |
| 100 alpha, 100 color a frame | .690, .715 | .678, .721 | -1.8%, +0.9% |

One world text changing is over this phase's 5% line in this run, and was +2.9% in the run before it, with the same engine but for two small savings. Nothing math or 3D runs in that path that was found. It is left as measured.

Math and solids, same player:

| Row | Target | Measured |
|---|---|---|
| `\\frac{a}{b}`, first time | 40 µs | 9 µs |
| A formula of about 100 nodes | 400 µs | 192 µs |
| 16 KB hostile formula, worst of four | 8 ms | 2.1 ms |
| Cached formula | 1 µs | under the clock's 1 µs |
| Text of 20 formulas: first build, next | | 17.8 ms, 0.51 ms |
| Solid, 64 distinct glyphs, first build | 25 ms | **36 ms: missed** |
| Same, cached rebuild | 1.5 ms | 0.77 ms |
| Depth change | as cached | 0.76 ms |
| Motion frame, 200 glyphs, 137 000 vertices | 3 ms | about 1 ms over an empty frame, by frame time |
| Vertices per Latin glyph, default edge | 1 500 | 674 over 64 glyphs here, 927 over a font's sample |
| 3D module's DLL, Mono, unused | recorded | 73 KB |
| Formula code in core's DLL, Mono | recorded | 136 KB |
| 1 000 identical texts, 500 under URP: batches | 5, 10 | **not counted:** the render counters were not valid in these players |


Formulas against Chromium at 96 px, 82 rows with no stretched glyph, including the differences listed above: ink box within 2 px on 40, within 5 px on 55; overlap at least 0.90 on 37, at least 0.80 on 45, mean 0.82. Plain rows such as `x`, `x+y`, `f(x)` and `a,b` overlap at 0.98 or more. The targets of 1.5 px and 0.90 are not met across the sheet, and the rows under them are the differences above, thin strokes offset by a fraction of a pixel, or both: **judged by eye on the contact sheet, not proven row by row.** The spec's per-node geometry was not built: boxes were compared for whole formulas, and references are not stored in the package.

`Lettera/Solid Lit` under URP 17.4 in a Windows player: lit, shadows cast and received, by a look at the pictures. Built-in the same. Web wasm at managed stripping High: 34.5 MB uncompressed with everything in; the build without either feature was not made for the web, so no web size delta is recorded.

### Not yet seen
- Phones, Safari on a real iPhone and a Mac: none has run a formula or a solid. **Lettera > Device Check** covers them.
- Word and Chrome receiving the clipboard's HTML.
- Selection and caret of a formula by real keys in a browser: run in EditMode through the input seams only. Bidi against Chromium's rects: not run.
- Plain text mesh output identical to 0.6.0's by hash: not compared. Web and Android builds of the Math and 3D samples: compiled only as part of the package's check, run neither.
- Cambria Math's `flac` and kerning: read as a system font by one test; its pixels were not compared.

## [0.6.0] - 2026-11-01

The platform's own text controls, clipboards and menus, COLRv1 color fonts, and fonts that cost no memory until they are used.

Everything that has not been seen to run on a real device ships **off**: on Android, iOS and macOS a project turns it on in Project Settings > Lettera > Native Platform, and the Device Check sample walks through what to look at.

### Added
- **One input protocol.** `ILetteraNativeHost` and `LetteraMirrorSource`: a platform's control reports what happened, and the field decides. So whatever a control produces, by keys, voice, autofill or paste, goes through the field's rules, and a refused edit puts the control right. A control only ever holds a window of a long text.
- **Web input.** A field's session runs through one hidden text box on every web device, with its rules for words and graphemes, composition shown inline, and a keyboard raised in the touch handler itself, since where fields stand reaches the page ahead of time. When the page never shrinks, twice, the next tap lands on a real text box under the finger.
- **Replicas.** `ReplicaFieldRule` puts the platform's own control in a field's place through the same session, so every rule still holds: on the web a plain element, or a surface a page registers on `window.LetteraSurfaces`. Elsewhere it does nothing, so one prefab fits every target.
- **Clipboard with its formats.** `LetteraPlatformClipboard` over Windows' own calls (plain text, HTML with its offsets, Markdown, Lettera's format, PNG, DIB, JPEG, GIF, files) and the browser's copy, cut and paste events, so styled text goes to and from other applications. Lettera's menu items reach the clipboard inside the user's own gesture through `LetteraWebGestures`. `ILetteraClipboardProbe` says what is there with no read, and `ILetteraClipboardAsyncOnly` marks a clipboard that reads later.
- **Windows composition** through the input method's own messages, in players: several clauses, the caret inside them, candidates beside the caret.
- **Android, iOS and macOS routes**, off by default: the system's text control, clipboard, menu, loupe and preferences on phones, the pasteboard on macOS. On iOS a default build compiles a stub, so code nothing here could compile cannot fail your Xcode build: `IosNativeInBuild` swaps the real file in.
- **Keyboard traits.** `AutoCapitalize`, `SpellCheck`, iOS smart typing, appearance and return key, Android flags, and `LetteraAutofill` names, with a zero value meaning the platform's default, so old assets read as they did. `LetteraVerbs` names what a control may report, and an unknown verb is refused out loud.
- **COLRv1** through HarfBuzz's own raster code, chosen glyph by glyph, with variable fonts. The native library is now version 2.
- **Fonts that cost no memory until used.** A font is unpacked once, the first time a text needs it, and mapped from disk after that. On desktop a build carries each in a file of its own, so an unused font costs nothing. No asset on disk changes in a build. `LetteraFontMemory` shows what each face holds, and Project Settings bound the cache folder.
- **Subset tool**, in the font's inspector and as `LetteraFontSubset`: keep or remove characters, by presets, ranges or what the project's texts use. By default it keeps what a default subset quietly drops. Its library is for the editor, and no player holds it.
- **Batched web emoji.** Many new emoji in a frame draw on one canvas and read it back once.
- **OS preferences.** `LetteraAccessibility.SteadyCaret`, and reduced motion, follow the system until a script sets them.
- **Public selection geometry** on `LetteraSelectable`: `SelectionOnScreen`, `HandlesOnScreen`, `CaretOnScreen`, `LineOnScreen`, `ViewportOnScreen`, `IsMenuShown`.
- **`LetteraFieldPressArea`.** A press on the empty part of a text area now counts as the field.
- **Device Check sample**, and the menu Lettera > Device Check, which builds it with every native route on for that build only.

### Changed
- On the web, `LetteraEditable.Paste()` returns false with a warning, because a browser reads its clipboard later: use `PasteAsync`, or `PasteCommand`, which picks the way. `HasText` asks the probe first, and not knowing counts as yes.
- `LetteraNative.ExpectedVersion` became `MinimumVersion` and `CurrentVersion`. An editor still holding the older library gets a warning, not an error, and everything but COLRv1 works.
- The package now depends on `com.unity.modules.unitywebrequest` and `com.unity.modules.androidjni`.
- A web emoji's pixels come at the frame's flush rather than when its glyph is asked for. Its rect is valid at once.

### Fixed
- A press on the empty part of a text area did nothing, because the text inside is only as tall as its lines.
- On the web, Unity's own handler overwrote what a page set on the clipboard further up, and with it the plain text of any copy made from a page's own elements.
- On the web, a second click in a field being edited took the keyboard away and lost keys, and made Unity forget a held right button, so a real right click opened no menu. The menu had only ever been opened from code.
- A hostile bitmap header could overflow when read.

### Verified
- Real keys, paste, cut, focus rules, menu items by real clicks, replicas and surfaces in Chromium and in WebKit, by a driver that reads what a field holds after every step. Composition through CDP in Chromium. iPhone emulation: a tap while the main loop stalls, a swipe, a tap outside, and the overlay route forced.
- COLRv1 against Chromium, per pixel at 96 px: mean error about 1.3 of 255 over 190 of the test font's 200 glyphs, and the same with a variable axis at two values, at about 0.5 ms a glyph. The other 10 are in Limits.
- A Windows player, with the real clipboard read and written by another process: plain text, HTML with valid offsets, a picture, and a clipboard held by another program, which gives up in 25 ms. What was there is put back.
- Input method messages through a Windows player's real window with a fake reader: two clauses, one undo step, no character twice, the window procedure restored after 1000 sessions.
- Fonts in a Windows player: payloads in StreamingAssets and nowhere else, mapped with nothing retained, a deleted or cut cache file written again, a blocked folder held in memory instead, and a font only a bundle holds working after `AssetBundle.Unload(false)`.
- Gradle builds for both the `Activity` and `GameActivity` entries, with the classes and the manifest's `queries` inspected in the APK. The JVM checks of the Android core. iOS export with stub and with real file, `pbxproj` inspected. Objective-C++ compiled against hand-written declarations of Apple's API, which proves syntax and not that those selectors exist.
- All tests against the old library in the owner's editor, with the COLRv1 ones inconclusive, and against the new one in a separate editor process.

### Figures
Windows Mono player, against a 0.5.0 build made the same way, in turns, median of 5, in ms:

| Row | 0.5.0 | 0.6.0 | Change |
|---|---|---|---|
| 64 labels a frame | 1.655 | 1.664 | +0.6% |
| 500 labels, first build | 109.6 | 112.1 | +2.2% |
| One level: parse, shape, layout, mesh | .0331, .0269, .0246, .0178 | .0330, .0269, .0248, .0181 | up to +1.7% |
| 200 world texts, first build | 33.3 | 31.9 | -4.2% |
| One world text changing | .0869 | .0912 | +4.9% |
| 100 alpha, 100 color a frame | .674, .733 | .677, .741 | +0.4%, +1.1% |

Plain text stays within the 5% this phase allowed itself. COLRv1 about 0.5 ms a glyph at 260 px square. Sizes: web archive 532 KB to 564 KB gzipped, so raster code stays in the web build; jslib 10 KB to 43 KB unminified; Android arm64 library 1.79 MB to 1.85 MB; Windows library 1.63 MB to 1.70 MB.

### Limits
- **Nothing on a phone has run.** Safari on a real iPhone: whether a keyboard rises, copy, paste. All of Android and iOS. macOS clipboard. Real Japanese and Korean input anywhere: Windows' input method was driven with a fake reader, and the web through CDP.
- COLRv1: 10 of the test font's 200 glyphs draw nothing, nine sweep gradients with coincident angles and one more, because HarfBuzz's raster code skips them. A gradient through alpha differs slightly from Chromium's, which interpolates premultiplied. A glyph using the text's own color does not follow it, as with COLRv0.
- The word-shape cache of 0.5.0's Limits is still not there. The library's `lt_shape_flagged` waits for it.
- Batching web emoji reads the canvas once rather than 64 times, but it was not 3 times faster as aimed for: about 1 time in headless Chromium and 2 in WebKit, whose cost here is drawing the glyphs. A real GPU browser was not measured.
- Safari's copy could not be proved: Playwright's WebKit on Windows does not keep what a page's copy handler sets, so those checks say SKIP. Paste into a field was proved there.
- macOS has no native text input. Scriptable Build Pipeline gets no callback of its own, so a font in such a bundle stays inline.
- `LetteraPasteButton` and iOS's paste control are not there: paste goes through a menu or a key. The device check shows its page's address as text, with no QR code. A Lettera menu has no Share on the web.
- Android carries images on the clipboard in neither direction yet, and its keyboards' image commits are not wired.
- The Input-System-only Windows variant and Word and Chrome paste were not run. The Project Settings cards were only built by a test, since the stand has no settings asset. The surface hooks for phones are not there: on a phone a replica is the plain control.

## [0.5.0] - 2026-10-31

Documents and performance: text of any length through one window, shared caches, word dictionaries and threads.

### Added
- **Rope.** `LetteraRope` keeps a document's source in blocks, with totals in a tree, so position queries read at most one block. `LetteraFileSource.Load` maps a UTF-8 file and decodes only what is read. UTF-16, UTF-32, byte-order marks and invalid UTF-8 are handled.
- **Documents.** `OpenDocument` shows a rope through one window near the view, through the same pipeline as any text, so selection, ranges and motion work in it. A scroll inside the window is a pose, with no rebuild. A tag opened far before the window stays in force. Heights start as estimates and are measured in the background, and a correction above the view moves nothing under it.
- **Document scroller.** `LetteraDocumentScroller`: drag, wheel, inertia, elastic ends, scrollbars and sticking to the end, on a document or any plain Canvas text.
- **Sources.** `LetteraSourceLoader` with `LetteraPathSource`, `LetteraTextAssetSource` and `LetteraUrlTextSource`. A failed or cancelled load leaves the text as it was.
- **Editable documents.** A `LetteraEditable` on a document edits the rope, keeps a selection in source positions through scrolling, and undo goes back to where its edit was.
- **Search.** `LetteraDocumentSearch` reads a slice at a time, in the source or in what is shown, and finds a word split by a tag or across a block's edge.
- **Bars.** A document too small on screen to read draws bars in place of glyphs.
- **Word dictionaries.** `LetteraWordDictionary` breaks lines and selects by words in Thai, Lao, Khmer and Myanmar, and word motions count those words. Four are shipped, built from ICU's lists. They are off until listed in the settings, so a project that lists none has no dictionary bytes. The inspector builds your own from a word list.
- **Caches.** A paragraph with the same characters and shaping attributes shapes once, across every text. Font fallback is shared per font set.
- **Threads.** `LetteraWorkers` runs analysis and shaping of big texts across threads in players, with results byte for byte the same as inline. The web has no threads, and runs the same code inline.
- **Color with no mesh pass.** After a text's first color change, later ones only rewrite its glyphs' colors.
- **Shader warm-up.** `LetteraShaderWarmUp.Run` and `RunProgressively`, by surface tier.
- **Viewports.** `ILetteraRangeViewport`, implemented by the scroller and by `ScrollRectViewport`. A keyboard focus step brings its range into view through one.
- A Documents sample, and Project Settings for documents and dictionaries.

### Fixed
- After a width change, hit areas and `OnPlaced` could stay as they were: positions run ahead of the frame's rebuild took a path that skipped them. In 0.4.0 a link could answer where it no longer stood.
- A range leaving the text now ends what its listeners heard begin: `Exited`, and `Released` as cancelled.
- The motion allocation tests of 0.4.0 used a counter that always reads 0 on Unity's Mono, so they could not fail. With a working counter and a control, the three rows measured still come to 0 bytes.

### Changed
- `LinkEffect`'s neighbors in a window keep their ids through scrolling only if they hold state: a range with no state gets a new id when it comes back. Ids are never reused.
- A typed `U+FFFC` stays dropped, and a marquee is refused on a text with a viewport.

### Limits
- **Not done, on purpose, each named by the spec as what may slip:** the word-shape cache, which needs a native change and so a restart of a running editor (one long paragraph with a keystroke still reshapes all of it); mesh building on threads; threads across different texts, so 1000 labels do not get faster from them, only from the paragraph cache; pose on threads.
- **Not done, by a measured decision:** incremental parse. Every keystroke still parses the window or field again. At 10 000 codepoints that was about 1 ms of 8.5, and mesh building 4.9.
- An edit of a selection that reaches past the window is **refused whole**, with one warning, so nothing is half deleted: select all and delete does not work on a document yet. Copy of such a selection works.
- The Chinese and Japanese word list is not shipped, because its older notices want a qualified reading. A project can build its own. Dictionaries and their word search were checked against a few sentences, not against ICU's own results on a corpus.
- `CameraViewport` for world text does not exist: a world text's window follows `View`, and nothing clips it. A windowed text is not refused inside a `ScrollRect`, though it should be. List numbers do not carry across a window's edge. Rounded ends of ranges cut by a window are not handled.
- Document rules that need the whole value are not skipped with a warning; a rule reads the window. `Source` on a document returns the window's source, not the whole. `Length` and `PlainText` answer for the window.
- `AutoSize` shows at the smallest size on a document and elide is ignored, with no inspector chip yet. Inspectors of the new components are plain field lists in the Lettera style, and there are no chips for missing dictionaries, a dictionary toggle list, or a loader's source picker beyond the list of fields.
- A rope query takes about 5 times as long on 100 MB as on 100 KB (0.24 us at the top), against the target of 2 times. The 50 MB heap target was not measured, and a block costs more than the target allows for by estimate. The rope's fuzz ran 6 000 edits, not 100 000.
- `EditingTests.AnIdleFieldRebuildsNothing` failed once, at 3740 bytes, in 1 of 3 full runs, and passes alone. The cause was not found.

### Verified
- EditMode 1031 tests (1030 pass, 1 inconclusive, since it needs a font without real small caps), PlayMode 24. Parallel shaping is byte-identical to inline on 7 kinds of text at 1, 2 and 4 threads, and through 40 random texts.
- Against the 0.4.0 tag built the same way, in turns, median of 5 runs, on a Windows Mono player, since this machine has no Windows IL2CPP module. Plain text stays within its 5%: 64 labels changing each frame 1.62 to 1.63 ms, level Layout +1.7%, level Mesh +0.6%, alpha +0.6%. Better: color on 100 labels 1.92 to 0.71 ms, one world label's change 0.131 to 0.089 ms, level Shape 0.039 to 0.027 ms, 500 labels' first build 121 to 110 ms.
- In the editor, which the targets are not set for: a keystroke at 10 000 codepoints in 100 paragraphs 8.5 ms, of which shaping 0.53 against about 5 before; in one paragraph of 10 000 still 12.8. A keystroke in a 12 MB document 2 to 3.5 ms, at either end. 123 000 codepoints shape in 76 ms inline and 44 ms on 4 threads. 2000 waving glyphs 0.66 ms with no upload, against 0.6 ms that includes one, so that row is still missed. A hit test over 200 ranges 7.7 us, from 21.8.
- Builds succeed with no errors for Windows, Linux, macOS, iOS, Android and the web, in a clean project.
- A player self-test with a document, a scroll, a jump, an edit, a search and a warm-up passes on Windows (Direct3D 12) and on the web in Chromium, WebKit and WebKit's iPhone emulation. On Windows it also reads a mapped file and shapes on 4 threads.

## [0.4.0] - 2026-10-31

Animation and interaction: text that moves with no rebuild, ranges that answer a pointer and a keyboard, and things that stand in the flow.

### Added
- **Pose pass.** Motion rewrites vertex data from what the last mesh left, with no pass of the rebuild and no allocation per frame. A text with nothing moving is not in the hub at all. A moving world text re-uploads only its own part of a batch.
- **Motions.** `<wave>`, `<wobble>`, `<shake>`, `<bounce>`, `<float>`, `<spin>`, `<pulse>`, `<pendulum>`, `<deform>`, `<glitch>`, `<scramble>`, `<roll>`, by cluster, word, line or range, with scaled, unscaled and manual clocks. Subclass `LetteraMotionEffect` for your own.
- **Reveal and marquee.** `RevealEffect` with an entrance library, by cluster, word, line or paragraph, keeping space or reflowing. `MarqueeEffect` loops or goes back and forth when a text does not fit.
- **Sequencer.** `LetteraSequencer` ramps any published parameter over one timeline with no code: staggered members, restart, retrace, return and accumulate, a `Progress` a curve can drive. Its values are holds, so no field is written. Inspector with transport, a strip of cues and edit-mode scrubbing.
- **Animator and Timeline.** An alpha curve costs an alpha pass. `LetteraParamLink` gives one effect parameter a field curves can bind. An optional assembly adds `LetteraSequenceTrack` and `LetteraParamTrack` when the Timeline package is present; nothing depends on it.
- **Ranges.** `LetteraRangeEffect`, `ActionEffect`, `LinkEffect`, `SpoilerEffect`. `LetteraRangeRouter` gives one target by priority, then style, then length; capture and bubble along a nest; `Stop` and `PreventDefault`; hover that follows a moving range; touch padding; long press. Ids stay across a parse that left a range alone. `LetteraRoute` says how a text takes a press, and a text without ranges behaves as before.
- **Keyboard.** Tab and Shift+Tab step through ranges on read-only text, Enter activates, a focus ring shows when focus came from a key. `LetteraRangeNavigator` joins Unity's UI navigation. In a field, Ctrl+Enter or Cmd+Enter follows the link at the caret.
- **Selection and ranges together.** On read-only text a click activates and a drag selects; in a field a plain press places the caret and the modifier follows the link.
- **Links.** `LetteraLinks.Open` takes only `http`, `https`, `mailto` and `tel` by default, and refuses the rest with a warning. `www.` gains `https://`. On the web, the page's own release handler opens the address, so a popup blocker lets it through.
- **Spoilers.** `<spoiler>` draws a cover above glyphs that are not drawn at all. Plain copy masks covered text, and rich formats do not carry it.
- **State rules.** `LetteraRangeRule` changes parameters by condition, with an envelope, through weighted holds. `RangeTintEffect` is hover feedback that costs only a pose. Holds gained `Weight`.
- **Inline items.** `<sprite=name>` and `<obj=name>` are one U+FFFC each. A sprite is one quad in the text's mesh, and moves, fades and reveals like a glyph. An object is a pooled prefab or an adopted scene object placed where the character landed.
- **Semantics.** `SemanticEffect` and `LetteraSemanticTree`: a platform-neutral description of roles, labels, states and actions.
- **Stage hooks.** `LetteraEffect.Stages` with `OnAnalyzed`, `OnShaped`, `OnLinesBroken`, `OnPlaced`, `OnMesh`, `OnPose`. An effect that asks for none costs nothing.
- **Reduced motion.** `LetteraAccessibility.ReducedMotion` applies on change. On the web it follows the browser's preference.
- **Tools.** Picker groups Animation, Interactive, Inline and Semantic, found also by words like "typewriter" and "ticker". Motion preview in edit mode. Project settings for animation and interaction. A Motion sample.

### Changed
- `<wave>`, `<wobble>`, `<shake>`, `<bounce>`, `<float>`, `<spin>`, `<pulse>`, `<pendulum>`, `<glitch>`, `<scramble>`, `<roll>`, `<deform>`, `<reveal>`, `<spoiler>`, `<sprite>` and `<obj>` are now default tags, so these words in text no longer show literally. A project with its own global preset is not affected.
- `<a>` and `<link>` now open their URL when clicked. `LinkEffect.OpenOnActivate` turns that off.
- `LinkEffect` derives from `LetteraRangeEffect`.
- A typed or pasted U+FFFC is dropped from fields.

### Limits
- Measured in the editor on the development machine, two budgets are missed: 2000 waving glyphs take 1.12 ms against a target of 0.6 ms, and a hit test across 200 ranges takes 21.8 us against 20 us. A sequencer with 20 cues takes 0.07 ms against 0.2 ms. All three allocate 0 bytes per frame. Figures for players and the other rows of the budget table were not measured.
- Safari on a real iPhone, a real Android device, a real popup opened by clicking a link in a browser, and frame rates in any player were not tried.
- A sequencer has its inspector strip but no separate timeline window: cues do not drag, and Split, copy and marquee selection do not exist. The rule editor is plain rows, with no condition builder or target menu. Only the sequencer's inspector has a parameter menu, and the live play-mode card is not implemented.
- `Auto` route takes the whole rect and forwards what no range took to the parents. Text behind it gets a press only with `Ranges` or `Observe`. `Observe` and the parent `Button` and `ScrollRect` paths were tried by calling handlers, not with a real input module and a real swipe.
- Hit tests stay at rest positions: a bouncing link's area does not move. A world text whose transform moves still re-merges its whole batch. World marquee is refused.
- Scramble and roll collapse layers during the effect and replace whole clusters, so cursive letters stand apart until they settle.
- A rule's target inside a preset does not follow `EnsureOwnCopies`: on a text's own copy of shared styles the rule finds nothing. Link stage: `Selected` and uncovered signals, per-segment rule scope, custom envelopes, gesture recognizers, `RouteAsked`, viewports and topic filters on queries are not implemented.
- Stage hooks: `OnAnalyzed` runs after shaping, so hiding a cluster does not reshape its neighbours, and it cannot ask for a language or a feature. `OnPlaced` cannot add a virtual glyph. Built-in motions do not run through `OnPose`.
- Inline: layers such as stroke and shadow skip sprites, and rotated atlas sprites and the `turn` slot on a sprite do not apply. An object spawns only in play mode or during a preview. A selection holding an inline item copies as plain text only, so it does not paste back as the item. A spoiler's underline draws under its cover, not hidden with the glyphs.
- A managed stripping `High` player was not run with effects reached only through serialized references; those types carry `[Preserve]`. Marquee, spoiler and rule pixels were not compared against a reference.

### Verified
- EditMode 907 tests (906 pass, 1 is inconclusive, since it needs a font without real small caps), among them 10 000 random edits with animation and ranges: no exception, no reused id, and the hub empties. PlayMode 24, among them budgets at 0 bytes per frame.
- Builds succeed for Windows, Linux, macOS, iOS, Android and the web, in a clean Built-in project with no Timeline package. The Timeline assembly compiles in a project that has the package; its tracks were never played.
- A player self-test with a wave, a reveal, a marquee, a link pressed through the text's own handlers with a fake opener, and a sprite passes on Windows (Direct3D 12) and on the web in Chromium, WebKit and WebKit's iPhone emulation. It checks that motion runs with no rebuild and that things exist, and does not compare pixels.
- The Motion sample compiles; it was not run. The new inspectors are covered by a test that they build with no default Unity look, and were not seen or clicked.

## [0.3.0] - 2026-10-31

Selection and editing: any text can be selected, and any text can be a field.

### Added
- **Selection.** `LetteraSelectable` on Canvas or world text: clicks by cluster, word and paragraph, drags by the same units, Shift to extend, keys for both PC and Apple conventions, filters that clamp or veto a change. A selection change rebuilds nothing. `LetteraWorldRaycaster` gives world text pointer events.
- **Caret geometry.** `HitTestCaret`, `TryGetCaretRect`, `GetRangeRects`, `TryGetRangeBounds` and `GetLineIndex`, with a bias for the two spots an index has at a soft wrap or a change of direction. `LetteraTextNavigator` finds stops by cluster, word and paragraph.
- **Editing.** `LetteraEditable`: the source stays the authority and an edit changes only what it touches, undo merged by word and by time, history groups, `Patched` and `ValueChanged` with reasons, zero-allocation readers, `LetteraCaret`, scrolling inside a viewport without rebuilding.
- **Rules.** Single line, password (one mask per cluster, literal value, off the clipboard), max length in graphemes, UTF-16, UTF-8 or codepoints, pattern mask, letter case, select on focus, release on cancel, Enter and Tab keys, keyboard traits and lift. Field presets. Filters for numbers, e-mail and names. Validators, also late ones. Decorators: hint, raised label, helper text, length counter.
- **Formatting.** `ApplyEffect`, `RemoveEffect`, `ToggleEffect` and `ClearStyles` write the field's own syntax, and never add a style. Pending styles at a caret, typing that inherits, markup hidden, revealed at the caret or shown whole, `FormattingRule` with shortcuts, a lamp for toolbars. `LetteraLiveSpans` for ranges outside the source.
- **Composition.** Shown in place, out of the value until it commits, as one undo step, with clause underlines.
- **Clipboard.** One copy writes plain text, HTML, Markdown and Lettera's own format. Paste takes the richest and writes it in the destination's syntax. HTML from Word and Google Docs reads clean, in linear time. Attachments.
- **Touch.** Long press, double tap, swipes that go on to a `ScrollRect`, handles that keep their grab, a mesh loupe.
- **Commands and menu.** `LetteraEditCommand` is the one path for menu, button and shortcut. `LetteraContextMenu` on UI objects you own, or built in. `LetteraCommandControl` for toolbars.
- **Web on touch devices.** `LetteraWebInputSource` raises the keyboard from a hidden text box, focused in the browser's own touch handler.
- **Ready-made.** `LetteraDropdown`, menu items for input field, text area, password field, dropdown, selectable and editable text. Project-wide settings. Inspectors for all of it, and a Project Settings page in the same look.

### Changed
- The GameObject menu now adds an input module when it creates an EventSystem. Without one, UI got no input.
- An empty paragraph under automatic direction takes the direction of the one before it, so a caret on it stands on the right side.
- A hidden `LetteraText` stays hidden under a `RectMask2D`, which used to bring it back.

### Limits
- Rich text reaches other apps as plain text, and on the web the clipboard stays inside the page: Unity offers plain text only, and native channels come in 0.6.0.
- Composition inline needs Unity's own IME path, with one clause; the web on touch devices shows it through its box. Native input comes in 0.6.0.
- The selection highlight is a solid color with square corners; `HighlightPaint` and rounded looks are not implemented. The caret stays upright on italic text, and on curved text caret and rectangles do not follow the glyph's angle.
- Styling visible delimiters (markup chrome) is not implemented.
- The I-beam cursor is not set: `WantsIBeam` tells where it belongs. A menu open during a change of language keeps its titles until it opens again. A touch menu does not hide while the text moves.
- Fields and menus are built from code, not prefab assets; the settings' slots take your own prefabs. The mesh loupe shows the text and its plate, not the scene behind, and only on a Canvas.
- Word navigation in Thai, Lao, Khmer and Myanmar takes whole runs until dictionaries come in 0.5.0. Every keystroke parses the text again until 0.5.0 too.
- No text drag and drop, visual caret movement, several carets, or find and spelling UI.

### Verified
- EditMode 788 tests (1 is skipped, since it needs a font without real small caps), Unicode conformance 14, PlayMode 19, among them 10 000 random edits, selections, undos and keys on random markup with no exception, and hostile clipboard input that stays linear.
- A keystroke with its rebuild took 1.3 ms at 1 000 codepoints and 13.6 ms at 10 000, in the editor on the development machine.
- Builds succeed for Windows, Linux, macOS, iOS, Android and the web, in a clean Built-in project.
- A player self-test with fields passes on Windows (Direct3D 12) and on the web in Chromium, WebKit and WebKit's iPhone emulation. In those three browsers a real press and real keys reached the field: mouse and keyboard on the desktop, a touch and the hidden text box in the emulation.
- A Windows player of a project using only the Input System package ran the self-test with no input exceptions.
- Real keys in a Windows player were not tried, because a test cannot take the foreground from a running desktop; desktop web runs the same input source. No real IME, iPhone or Android device was tried, and Linux, macOS, Android and iOS players were built but not run. The hidden text box relies on Safari on a real iPhone raising the keyboard from a touch handler, which emulation cannot prove.

## [0.2.0] - 2026-10-31

The style constructor: layered paint, the full set of typography and layout effects, more ways to find ranges, and an inspector to build it all in.

### Added
- **Paint.** Solid colors, gradients (linear, radial, conic; sRGB, linear, Oklab and stepped mixing; clamp, repeat and mirror) and textures, measured against a glyph, line, range or the block. Named paints in a library. Blend modes: multiply, screen, additive, exclusion.
- **Layers.** `<fill>`, `<stroke>` (inside, centered or outside; sharp corners in MSDF), `<glow>`, `<inner-shadow>`, `<extrude>`, `<filter>` (grayscale, saturation, hue, brightness, contrast, exposure, invert, sepia, tint, any matrix), `<union>`, `<warp>`, and custom materials. Any number of layers per glyph. Layers reach up to 1 em from the glyph; the atlas raises a glyph's reach when asked.
- **Typography.** `<uppercase>`, `<lowercase>`, `<smallcaps>`, `<sup>`, `<sub>`, `<otf>` for OpenType features, `<axes>` for variable fonts, font variant assets, `<size-match>`, resolution boost, `<ruby>`.
- **Layout.** `<tracking>` (with connecting strokes in Arabic), `<word-spacing>`, `<cell>`, `<line-height>`, `<para-spacing>`, `<line-gap>`, `<nobr>`, `<indent>`, `<li>` lists, `<align>` per paragraph with justification budgets, `<trim>`, `<curve>`, `<divider>`, auto-size fit budgets, several ellipsis ranges, truncate fade.
- **Decorations.** Double, dotted, dashed and wavy lines, skip ink and overlay; `<mark>` highlights by glyph, line, range or block, with rounded corners.
- **Finding ranges.** Markdown emphasis, lists, links and code spans, bare URLs, trigger words such as mentions, literal strings, matcher groups, void tags. `<link>` and `<a>`.
- **Reuse and control.** Composite effects with a parameter grammar, effect preset assets, range queries, and holds that take over a parameter at mesh cost: `text.Ranges(effect).WithLabel("warn").Hold<Color32>(0).Value = red`. `MarkEffectsDirty` re-applies effects without re-parsing.
- **MSDF.** `RenderMode = MSDF`: sharp corners at any size, in an atlas of its own, with the same material.
- **Pixel fonts.** Grid detection, exact pixels at multiples of the grid, hard-edged effects in whole font pixels, snapping.
- **Inspectors.** A searchable style picker, paint and gradient controls, essential parameters first, copy and paste, inspectors for every asset, warnings in the header, a Scene view handle for curves and padding, and a custom effect template.

### Changed
- `<outline>` and `<shadow>` take any paint, and draw as many layers as there are styles bound to them.
- New default tags mean that text such as `<fill>` is no longer literal. See the table in the README.
- A comma inside balanced parentheses stays in its parameter, and a style's default parameter is now split like a tag's value, quotes included.
- Custom effects and matchers holding a list or another nested object should override `Clone`.

### Fixed
- Overlapping contours, common in variable fonts, no longer show a seam inside the glyph.
- `RenderMode`'s setter and a serialized change now raise the same rebuild.
- An emergency line break no longer counts trailing whitespace in the line's width.

### Limits
- Two shader tiers, plain and styled. Union is done in the atlas and warp in the styled tier, so neither needs one of its own. Inner shadows are not warped.
- Effects cannot yet hook the shaping, layout or mesh stages themselves. That comes with animation in 0.4.0.
- Atlas page size is fixed at 2048, and MSDF stores 8 bits per channel.
- Strokes have no miter limit. Sharp corners reach as far as the field does.
- Fonts holding only bitmap strikes and no outlines are not supported as pixel fonts; that needs native code.
- Curved text draws no underlines or strikethroughs.
- Block highlights round convex corners only.
- Color filters work in the shader's color space, which is linear in a linear project.
- Per-glyph font overrides and a glyph picker are not implemented.

### Verified
- EditMode 476 tests (1 is skipped, since it needs a font without real small caps), Unicode conformance 14, PlayMode 14. New pixel tests read back what the shaders draw, with URP in the test project.
- Builds succeed for Windows, Linux, macOS, iOS, Android and the web, in a clean Built-in project.
- A player self-test with gradients, layers, decoration lines, MSDF, a list and a pixel font passes on Windows (Direct3D 12) and on the web in Chromium and WebKit, including WebKit's iPhone emulation.
- Linux, macOS, Android and iOS were built but not run. Safari on a real iPhone has not been tried.

## [0.1.0] - 2026-10-30

### Added
- `LetteraText` (Canvas) and `LetteraWorldText` (world space, merged batches).
- OpenType shaping through HarfBuzz, UAX #9 bidi, UAX #14 line breaking, UAX #29 graphemes and words, UAX #24 scripts.
- Font assets, font stacks, OS font discovery and fallback, color emoji (COLRv0, sbix, CBDT).
- One shared SDF atlas and one shared color atlas, bound globally so fonts never split a batch.
- Wrapping, six horizontal and three vertical alignments, justify, ellipsis and truncate at any position, auto size, line height modes, padding, layout element support.
- Markup with extensible matchers and effects, style presets, and a built-in vocabulary.
- Measuring and hit testing APIs.
- Lit world text (ambient and main light, Built-in and URP) and shadow casting.
- A custom inspector for both text components.
- The web, including Safari on iOS: a bundled default font, emoji through the browser's canvas, and a lower glyph memory cap.
- Atlas pages recycle by least recent use when the page limit is reached.
- Runtime text updates allocate nothing after warm-up when the text has no tag parameters.

### Verified
- Unicode conformance: all five official test files pass at 100%.
- Windows: player self-test passes on D3D12.
- Web: player self-test passes in Chromium and in WebKit on Windows, on WebGL 2. Safari on a real iPhone has not been tried.
- Builds without errors, not run: Linux, macOS, Android, iOS (Xcode project export; not compiled by Xcode).

### Known limits
- Lit text uses the ambient and main light only, and does not receive shadows.
- HDRP is not supported.
- iOS system fonts are found through CoreText; this has not been run on a device.
