# Tools

Everything Lettera adds to the editor: where to make things, the text inspector, typing into a text in the Scene view, the windows that show what a text is made of, the settings, and the keys.

[Русская версия](Tools.ru.md) · Other guides: [Getting Started](GettingStarted.md), [Styles](Styles.md), [Motion](Motion.md), [Migration](Migration.md), [Agent guide](AgentGuide.md)

## Start here

**Tools > Lettera > Getting Started** lists the guides (each opens in English or Russian), the samples with an **Import** button for each, the migration tool, every window and the two settings pages. A new project is told once, by a line in the console, that it is there.

## The palette

**Tools > Lettera > Commands...**, or **Ctrl+Alt+L** (Cmd+Alt+L on a Mac): everything Lettera makes or opens, by typing.

- It reads the keyboard in either layout: typing `ищдв` with a Russian layout on finds **Bold** as `bold` does.
- What you used last comes first when nothing is typed.
- **Shift and a right click** in the Hierarchy, the Scene view or the Project window opens it there, offering first what fits: objects to create in the Hierarchy and the Scene view, assets in the Project window. Its top line says where a new object will land. **Preferences > Lettera** turns the Shift and right click off; Ctrl+Alt+L works either way.
- **Up** and **Down** move, **Enter** runs, **Tab** goes into the selected entry's group (Create, Assets, Selected text, Window, Settings and so on), **Backspace** on an empty search leaves it, **Esc** closes.

## Making things

**GameObject > Lettera:** Text, World Text, Text 3D, Button, Document, Selectable Text, Editable Text, Input Field, Text Area, Password Field, Dropdown and World Selectable Text. A Canvas object finds or makes a Canvas.

**Assets > Create > Lettera:** a font asset, a colour font asset, a font variant, a system font, a font stack (empty, or from the selected font files, each a fallback or one family), a style preset, an effect preset, a paint library, an inline library, an entrance library, a range topic, a field preset, a word dictionary, a solid look and a custom effect with its shader.

**A project's own defaults.** **Project Settings > Lettera > Defaults** has slots for Text, World Text, Text 3D, Button and Document. A filled slot is what the GameObject menu makes; Add Component and Reset seed from it. A text's right click has **Save As Default**, which makes a prefab of it and fills its slot.

## The text inspector

One page of cards: **Text**, **Font**, **Layout**, **Styles**, **Render** and **Interaction**, with **Math**, **Document** and **Live** when a text has a formula, a document or, in Play Mode, ranges with a state. A row at the top jumps between cards, and a filter finds a setting. A closed card says what it holds, as `Shadow, 2 more`.

- **The header** says which font the text really uses and why, how much its last rebuild did, and how many texts are selected. A warning carries its fix on a button.
- **The source reads in colour.** Tags go dim, parameters and formulas are tinted, and a tag this text does not know is marked. The eye turns colour off. **Written**, **Resolved** and **Runtime** show what a resolver or `SetText` made of the text and where it came from; **Clear** drops runtime text.
- **The tool row:** B, I, U, S, Color, Size, Stroke, Shadow, Highlight and Link wrap a selection in the tag this text really binds, or with nothing selected turn a whole-text style on or off. The tooltip names the tag, and Alt with a click opens the style's row. **↵** inserts a line break, **More** lists every style with a search.
- **Styles** select, drag and copy like files: Shift and Ctrl to select several, **Delete**, **Ctrl+D** to duplicate, **Alt** with an arrow to move, one undo each. A row can go through text and back. Problems are listed with their row and a fix. **Alt+R** over a list hides the row controls on every list; **Preferences > Lettera > Row Controls** does the same.
- **+ Add Style** opens the picker: every effect and matcher with a line in words, a colour per family, recently used, keys, and a live look at a style under the pointer that writes nothing. An entry already on the text says so, and a click takes it away. See [Styles](Styles.md).

Every other component and asset has a card page of the same kind: Selectable, Editable (with **Try It**, a real field for trying its rules), Dropdown, Document Scroller, Source Loader, Sequencer, Parameter Link, fonts, stacks, presets and libraries.

### A text's right click

**Copy Text** and **Paste Text** (the source, markup included), **Edit In Scene**, **Inspect In Scene**, **Save As Default**, **Convert To World Text** or **Convert To Canvas Text** with its styles, and on a world text **Convert To Text 3D**. With Unity Localization installed: **Localize** and **Localize Font**. A Text 3D has **Bake...**, **Bake To Mesh Asset...** and **Export OBJ...**.

## Typing into a text in the Scene view

Start with **F2** while the Scene view has focus and one text is selected, **Alt and a double click** on a text, the inspector's **Edit In Scene**, or **Tools > Lettera > Edit Text In Scene**. A plain double click stays Unity's.

- Inside is the runtime's own field: caret, drag by unit, word and paragraph clicks, keys, the clipboard, composition and its undo. **W** types and does not switch tools.
- The **Lettera Text** bar, listed in the Scene view's overlay menu, has B, I, U, S, a colour, the tags this text can write, and markup shown or hidden. The right click has the field's commands, custom ones included, then formatting.
- **Esc**, a click outside, another selection, entering Play Mode or a domain reload end it. What was typed goes back as one undo step.
- Nothing is added to the object: the field lives on a hidden object of its own, so a duplicate has no helper, a prefab instance sees only its text as an override, and no scene file can hold one. Starting and leaving without a change leaves the scene clean.
- It declines several selected texts, a document, and a text whose value comes from a resolver or `SetText`, and says why. A field's rules on the same object stay out of it.

## In the Scene view

- **Inspect Text In Scene**, **Ctrl+Shift+I** with the Scene view focused, or **Tools > Lettera > Inspect Text In Scene**: draws each selected text's glyph advances and baselines, for up to 2 000 glyphs.
- **Outlines:** a selected text shows its padding, the area that takes the pointer and its ink as outlines. World text with nothing to click it says so beside itself.
- **Text 3D** has a box and a handle that drags its depth.
- **The Hierarchy's eye** hides batched world text.
- **Tools > Lettera > Scene > Outlines** and **Follow The Hierarchy's Eye** turn these off, for you alone.

## Windows

Each window follows the selection; the chip at its top, **Follows the selection**, pins it to what it shows. Each says what to do when it has nothing to show.

| Window | Menu | What it shows |
|---|---|---|
| Glyphs | Tools > Lettera > Glyphs | What shaping made of a text. **From A Text** reads the selected text's own runs (**Use Selected Text**); **Free** shapes any string with a font you pick, or the system's. The table lists each glyph's id, character, advance and font; a click draws its outline with the points on and off the curve. |
| Ranges | Tools > Lettera > Ranges | What the markup became: every range with its tag, effect, span and parameters, and **Problems** with a fix where one exists. **Refresh** reads the text again. |
| Atlases | Tools > Lettera > Atlases | Every text's glyph pages: glyphs, SDF, MSDF and colour pages, the page limits and the bytes. Pages one by one, **Export PNG**, **Clear And Rebuild**, and **Warm**, which renders the characters you type with the selected text's font. |
| Clipboard | Tools > Lettera > Clipboard | Every format a copy wrote, decoded. It reads the clipboard only when you press **Read The Clipboard**. |
| Font Tools | Tools > Lettera > Font Tools | Font files into assets, many at once: drop them, **Browse...** or **Use Selected**, then **Create Assets**, each beside its file; a file outside the project asks for a folder. Subsets are on a font's own inspector, in its Subset card. |
| Sequencer | Tools > Lettera > Sequencer, or the button on a sequencer's inspector | A sequencer's cues on rows. See below. |
| Migration | Tools > Lettera > Migrate From TextMesh Pro | See [Migration](Migration.md). |
| Repair Saved Objects | Tools > Lettera > Repair Saved Objects | Writes back the script reference of Lettera objects that an older version saved and that came back as missing scripts, in `.asset`, `.prefab` and `.unity` files under Assets. Files change on disk, so version control is the undo. |
| Bake Text 3D | A Text 3D's right click > Bake... | A mesh asset, a static object, lightmap UVs, levels of detail and FBX. See [3D text](Text3D.md). |
| Lettera Solid Material | **Edit In Lettera** on a `Lettera/Solid Lit` material | The material's settings as a card page. |

### The Sequencer window

Cues of the selected `LetteraSequencer` stand on rows. They drag and resize by their edges, snapping to other cues, the playhead and ticks; **Ctrl** while dragging goes free. Several selected share a card of fields, and a double click opens a cue's full editor in the window. Every edit is one undo step.

| Key or gesture | Does |
|---|---|
| Space | Play or stop |
| S | Split at the playhead |
| Ctrl+D, Ctrl+C, Ctrl+V | Duplicate, copy, paste |
| Ctrl+A, Esc | Select all, select none |
| M | Mute |
| F | Fit everything in view |
| Delete, Backspace | Delete |
| Drag on empty space | Marquee; with Ctrl or Shift, add to the selection |
| Wheel, Shift+wheel, Ctrl+wheel | Scroll, scroll sideways, zoom at the pointer |
| Middle drag, Alt+drag | Pan |
| Drag on the ruler | Scrub, in edit mode, with no change to the scene |

### Timeline

With the Timeline package installed, **Add Track > Lettera** offers a text track (a line of a cut scene from a clip, which can type itself over the clip's length), a sequence track and a parameter track, each with its colour and icon. A clip's label says what it does, as `Types: Run!` or `25% to 75%`. A track's inspector lists only objects that fit. Scrubbing inside a clip does no rebuild. See [Motion](Motion.md).

## Settings

**Project Settings > Lettera**, one card per area, with a filter at the top: Defaults (fonts, the global style preset, paints, dictionaries, the prefab slots), Selection & Editing, Animation & Interaction, Documents, Native Platform, Font Memory, Memory, Atlases, Math, 3D, Commands, **In Your Build** (what this project carries into a build, why, and what it weighs, with **Rescan Scenes**), and Tools. With no settings asset yet, the page offers to make one.

**Preferences > Lettera** holds what is yours alone: Shift and a right click opening the palette, and the row controls of lists.

## Keys

| Keys | Where | Does |
|---|---|---|
| Ctrl+Alt+L | Anywhere | The palette |
| Shift + right click | Hierarchy, Scene view, Project window | The palette, offering what fits there |
| F2 | Scene view | Type into the selected text |
| Alt + double click | Scene view, on a text | Type into it |
| Esc | While typing in the Scene view | End, keeping what was typed |
| Ctrl+Shift+I | Scene view | Glyph boxes and baselines on and off |
| Alt+R | Over a list | Hide or show row controls |
| Ctrl+D, Delete, Alt+arrow | Style rows | Duplicate, remove, move |

On a Mac, Ctrl is Cmd. **Edit > Shortcuts** lists Commands, Edit Text In Scene and Inspect Text In Scene under **Lettera**, to change.

## From the command line

With the `com.unity.pipeline` package installed, `unity command lettera ...` makes, edits, checks and renders text from a terminal or a coding agent, and answers in JSON. See the [Agent guide](AgentGuide.md), in English.

## Limits

- A UnityEvent's list wears Unity's own look, and so do two object slots on the 3D pages (a solid material's texture, a text's look).
- The Hierarchy's eye reaches batched world text until Play, and hides it in the Game view too in edit mode. Canvas text, standalone world text and Text 3D are left to the editor.
- **Localize Font** does not preview in edit mode, so that a scene keeps its saved font.
- Typing into a text and undoing all of it inside one session leaves the scene marked changed.
