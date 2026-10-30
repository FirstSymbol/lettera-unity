# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.2.0] - Unreleased

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
