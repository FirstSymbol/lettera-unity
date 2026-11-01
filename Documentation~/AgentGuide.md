---
name: lettera
description: Create, inspect and fix Lettera text in the open Unity Editor through "unity command lettera ...": Canvas, world and solid 3D text, input fields, styles, gradients, fonts, formulas, TextMesh Pro migration. Use when the user asks to build or change text, titles, labels, fields or typography in a Unity project that has the Lettera package.
---

# Driving Lettera from the command line

Lettera registers CLI commands in the `lettera` group. They need the `com.unity.pipeline` package and an open editor; without that package the commands do not exist, and nothing else changes.

Every command answers JSON. A failure is `{"error": "...", "kind": "..."}`, and changes nothing. Edits are undoable in the editor and mark the scene dirty. **No command saves a scene**: use `unity command scene save` when the user wants the result kept. If more than one editor is open, say which with `unity command --project-path <project> lettera ...`.

## The first rule: look at what you made

You cannot tell from JSON whether text looks right. After every visual change:

```bash
unity command lettera export_png --target Title --output preview.png
```

Then open `preview.png` and look at it. Check that the words are all there, that nothing is cut off or wrapped by surprise, that the colours are the ones meant and the text stands out from the background. If something is off, fix it and export again. Do not call visual work done before you have seen the picture.

`export_png` works on Canvas, world and 3D text, and on an object holding several texts, such as a card. It draws a copy in a separate preview scene: the scene stays as it was and clean, and nothing is left behind. The picture also shows text that overflows its rect. A 3D text is seen from a slight angle, so that its depth shows. The answer gives `path`, `width`, `height`, and `drawnPixels`; 0 there, with a `warning`, means nothing showed. `--scale` multiplies the resolution (2 by default). `--background "#ffffff"` or `--background transparent` replaces the default, which is dark or light to stand apart from the text's colour. Motion is drawn at rest.

## Workflow

1. `unity command lettera help`: commands, JSON conventions, examples.
2. `unity command lettera types`: every effect, matcher and field rule, with fields and defaults. `unity command lettera tags`: the markup vocabulary in force.
3. `unity command lettera list`: what exists, with paths and ids.
4. Create or edit. Keep the `id` (`"#1234"`) from the answer as `--target`; names can be ambiguous.
5. **`export_png`, and look.** Fix, and look again.
6. `unity command lettera validate --target <t>`: tags nothing is bound to, broken formulas, missing glyphs. `ok` answers for those. `otherPlatforms` lists what would be missing on the web and on phones, such as a font, for you to pass on; it fails nothing unless you name that platform with `--platform`.

Targets are hierarchy paths (`/Canvas/Title`), names or ids (`#1234`). On a field, the root and the text inside both work.

**Where things go.** A new object's rect is centred on its parent, and `--position x,y` moves it from there: `0,-40` is 40 below the centre, in local units, y up. Inside its rect a text starts at the top unless `alignment` says otherwise: `Center` alone centres it across and leaves it at the top; `'Center,Middle'` centres both ways.

**Lettera makes text.** A panel behind a card is a plain uGUI `Image`, made with Unity's own `gameobject` and `component` command groups. Give a text as its child and export the panel: `export_png` draws it too. `--background` only colours the picture, not the scene.

## Commands

| Command | Purpose |
|---|---|
| `help`, `types`, `tags [--target]`, `list` | What exists |
| `get --target <t> [--full]` | Description; only non-default values without `--full`. What it prints, `set` accepts |
| `create --kind text\|world\|text3d\|input\|area\|password\|dropdown [--text] [--name] [--parent] [--size w,h] [--position x,y] [--data patch]` | New object. Canvas kinds use the scene's canvas, or make one |
| `set --target <t> --data <patch>` | Patch, see below |
| `style_add --target <t> [--effect <type>] [--tag <name>] [--matcher <type>] [--parameter <text>] [--data <effect fields>]` | Adds a style. No tag and no matcher: the whole text |
| `style_list --target <t>`, `style_remove --target <t> --index <i>`, `style_set --target <t> --index <i> --data {effect,matcher,tag,enabled,parameter}` | Styles by index |
| `preset_save --target <t> --path Assets/...asset`, `preset_apply --target <t> --path ...` | Style presets |
| `font_create --path <file> [--output Assets/...asset]` | Font asset from a `.ttf`, `.otf`, `.ttc` or `.otc` |
| `font_stack --fonts a.asset,b.asset [--path ...] [--target <t>]` | Font stack, in fallback order |
| `font_info --path <asset or file>` | Family, style, metrics, glyph count, color, variable |
| `font_subset --path <asset> --keep <text or U+0020-007E> [--output ...]` | A smaller copy, as a new asset |
| `validate --target <t> [--platform web\|android\|ios\|windows\|mac\|linux]` | Problems here, and what is missing on other platforms |
| `measure --target <t> [--width <w>]` | Size, bounds, lines |
| `glyphs --target <t>`, `glyphs --text <text> [--font <asset>]` | Shaping result, glyph by glyph |
| `formula --source <tex> [--display] [--size <n>]` | Checks a formula and measures it |
| `export_png --target <t> --output <file.png> [--scale <n>] [--background <colour>]` | **Renders a picture to look at** |
| `atlas`, `atlas_reset`, `atlas_export --output <file.png> [--set sdf\|msdf\|color] [--page <n>]` | Shared glyph atlases |
| `caches`, `memory` | Text caches; what loaded fonts cost |
| `select --target <t>`, `edit_in_scene --target <t>` | For the person watching |
| `migrate_scan [--root]`, `migrate_status`, `migrate_report --output <file>`, `migrate_fonts`, `migrate_components [--simple]`, `migrate_scripts [--preview]` | TextMesh Pro migration. Refuses on a dirty open scene rather than prompt |
| `settings_get [--key math.scale]`, `settings_set --data {...} [--create]` | `LetteraSettings` |
| `device_check_build --target android\|ios\|web\|windows\|mac` | Builds the device check sample. Refuses on a dirty open scene |

## JSON (`--data`)

Relaxed JSON: unquoted keys and words, and single quotes: `{text:'Hi',wrap:false}`.

- **`set` keys:** `text`, `font`, `stack`, `size` (a number is the font size, `[w,h]` the rect), `fontSize`, `rect`, `color`, `alignment` (`"Center"` or `"Center,Middle"`), `wrap`, `overflow`, `padding` (one number or `[left,bottom,right,top]`), `lineHeight`, `direction`, `language`, `renderMode`, `name`, `active`, `position`, `rotation`, `scale`, `styles`, `presets`, `rules`, and any other property of the component (`autoSize`, `depth`, `lit`...). An unknown key answers with the keys there are. All of a patch is read before any of it applies, so a bad one changes nothing.
- **In `create`,** `--size` is always the rect, and `size` inside `--data` follows the rule above.
- **Colours:** `"#RRGGBB"`, `"#RRGGBBAA"`, names (`"white"`) or `[r,g,b,a]`.
- **Vectors:** `[x,y]` or `"x,y"`. **Enums:** by name (`"RightToLeft"`). **Assets:** by path; `null` clears.
- **Polymorphic values:** effects, matchers and rules take `"$type"`: `{$type:Stroke,width:0.05}`, or just the name: `"Bold"`. `Fill` means `FillEffect`, `MaxLength` means `MaxLengthRule`.
- **Paints:** `"#f00"`; `"#f00,#00f"` is a gradient; `{gradient:'#f00,#00f',angle:90,span:Range}` says how it lies. `span` is `Glyph`, `Line`, `Range` or `Block`: a gradient measured on the range runs across a sentence, on the glyph it restarts.
- **Lists:** an array replaces a list; an object `{"0":{...}}` patches by index.

## Markup

A tag means something only because a style binds it. The vocabulary in force is in `tags`: `<b>`, `<i>`, `<color=#F00>`, `<size=150%>`, `<fill=...>`, `<stroke=...>`, `<shadow=...>`, `<a=https://...>`, `<wave>`, `<reveal=...>`, `<math>`, and many more. A tag nothing claims shows as written, and `validate` reports it. `\<` escapes markup. A bold title is `{text:'<b>Sunset</b>'}`.

## Recipes

```bash
# A gradient title
unity command lettera create --kind text --name Title --text "Sunset" --size 600,120 --data "{size:72,alignment:'Center,Middle'}"
unity command lettera style_add --target Title --effect Fill --data "{paint:{gradient:'#FF8A00,#E52E71',angle:0,span:Range}}"
unity command lettera export_png --target Title --output preview.png   # and look
# Change the gradient: style_set takes the whole effect
unity command lettera style_set --target Title --index 0 --data "{effect:{\$type:Fill,paint:{gradient:'#34D399,#38BDF8,#A855F7',angle:0,span:Range}}}"

# A card: a text holding a body text, exported whole. Body's rect is centred on the card's, so -45 puts its top a little under the title
unity command lettera create --kind text --name Card --text "<b>Sunset</b>" --size 600,200 --data "{size:64,alignment:'Center,Top'}"
unity command lettera create --kind text --name Body --parent Card --text "Warm colours at the end of the day." --size 560,90 --position 0,-45 --data "{size:26,alignment:Center,color:'#C7CEDB'}"
unity command lettera export_png --target Card --output preview.png

# An input field with validation: one line, at most 40 characters, an email's characters only
unity command lettera create --kind input --name Email
unity command lettera set --target Email --data '{rules:[{$type:SingleLine},{$type:MaxLength,limit:40},{$type:Email}]}'

# A right-to-left paragraph
unity command lettera create --kind text --name Arabic --size 500,200
unity command lettera set --target Arabic --data "{text:'مرحبا بالعالم',direction:RightToLeft,language:ar,size:36}"

# A link
unity command lettera set --target Title --data "{text:'Read the <a=https://unity.com>docs</a>'}"

# A typewriter: <reveal=entrance,reach> shows up to its reach. Moving the reach from 0% to 100% is for code or a LetteraSequencer
unity command lettera set --target Title --data "{text:'<reveal=,100%>Line by line</reveal>'}"

# A formula: check it, then use it
unity command lettera formula --source "\frac{a}{b}"
unity command lettera set --target Title --data "{text:'Half is <math>\\\\frac{1}{2}</math>'}"

# Solid text
unity command lettera create --kind text3d --name Logo --text "Solid" --data "{depth:0.25,frontBevel:{shape:Round,width:0.03,height:0.03,roundness:0.5,segments:4}}"
unity command lettera export_png --target Logo --output logo.png

# A migration from TextMesh Pro. Commit first: version control is the undo
unity command scene save
unity command lettera migrate_scan
unity command lettera migrate_fonts
unity command lettera migrate_components --simple
unity command lettera migrate_scripts --preview
unity command lettera migrate_scripts
unity command lettera migrate_report --output migration.md
```

Quoting differs between shells. Bash expands `$type` inside double quotes: write `\$type` there, as in `style_set` above, or put single quotes around a patch with no single quotes in it, as in the input field above.
