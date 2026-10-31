# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

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
