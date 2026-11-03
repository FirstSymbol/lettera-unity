# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **The text inspector is finished as one page.** The header says which font a text really uses and why, how much the last rebuild did, and how many texts are selected. A warning has its way out on a button. A closed card says what it holds, as `Shadow, 2 more`. Math and Document cards come when a text has a formula or a document.
- **The source reads in colour.** Tags go dim, parameters and formulas are tinted, and a tag this text does not know is marked, in the field you type in. The eye turns it off. Written, Resolved and Runtime show what a resolver or `SetText` made of the text and where it came from, with Clear for runtime text. A slider sizes this field's letters only.
- **One row of tools does both jobs.** B, I, U, S, Color, Size, Stroke, Shadow, Highlight and Link wrap a selection in the tag this text really binds, or with none selected turn a whole-text style over. The tooltip names the tag, and Alt opens the style's row.
- **New controls:** `LtUnit` (px, em, %), `LtObject` (icon, name, a list with a search, a go-to arrow), `LtEnum`, `LtColor` with hex. `LtUI.Field` picks them by type and no longer falls back on Unity's list. `LtBox` opens as a box, with Shift and Alt linking sides. Each shows a prefab override and reverts it from the right click.
- **Style rows** select, drag and copy like the rest of the house: Shift, Ctrl, Delete, Ctrl+D, Alt with an arrow, one undo each. A row goes through text and back. Alt+R hides row controls on all lists. Problems are listed with their row and a fix.
- **The picker** has a line in words for every entry, a colour per family, recently used, keys, the wrong keyboard layout, and a live look at a style under the pointer that writes nothing. An entry already on the text says so, and a click takes it away.
- **The palette** offers by where it was opened: Shift and a right click in the Hierarchy, the Scene view or the Project window, with Preferences > Lettera to turn that off. It says where a new object lands, and Tab goes into a group.
- **GameObject > Lettera:** Button and Document. **Assets > Create > Lettera:** a font stack from selected fonts, two ways. **A project's own defaults:** slots for Text, World Text, Text 3D, Button and Document; a filled one is what creating instantiates, and Add Component or Reset seeds from it. Save As Default makes a prefab and fills the slot. A text's right click also has Copy Text, Paste Text, Edit In Scene, Inspect In Scene, and converting between Canvas and world text with its styles.
- **Our own icons** on components and assets, in Hierarchy, Inspector, Project and Add Component.
- `LtUI`, the `Lt` controls and `LetteraTextEditorBase` are public, for a text subclass's or a custom effect's drawer. `[LetteraEssential]` marks what a new style shows first, and `[LetteraPicker]` gives a custom effect its place in the picker.
- **Lettera from the command line.** With `com.unity.pipeline` installed, `unity command lettera ...` creates, edits, checks and renders text, and answers in JSON. `export_png` draws a text to a picture for an agent to look at, on Canvas, world and 3D text, and leaves the scene as it was. Without that package none of it compiles. The guide for agents is `Documentation~/AgentGuide.md`.
- **A line of a cut scene can come from a clip.** With Timeline installed, `LetteraTextTrack` makes a text say what the clip under the playhead says, by the resolver's route, with its own field untouched, and gives it back in a gap and on leaving. A clip can type its line over its length. Scrubbing inside a clip does no rebuild.
- **Tracks are in Timeline's Add Track menu, under Lettera,** each with its colour and icon. A clip's label says what it does, as `Types: Run!` or `25% to 75%`, and clips and tracks have inspectors of our own. A track's inspector lists only objects that fit, and one with a text but no sequencer says that binding adds one.
- **Tools > Lettera > Sequencer,** also a button on the sequencer's inspector: cues on rows. They drag and resize, with snapping to other cues, the playhead and ticks, and Ctrl to go free. Zoom and pan as in other views, a ruler that scrubs, marquee, split at the playhead, duplicate, copy, paste, mute, delete, each one undo step. Several selected share a card of fields, and a double click opens a cue's full editor in the window. Space plays.
- **A Timeline sample.**
- **Type into a text where it stands.** F2 with the Scene view focused, Alt and a double click on a text, the inspector's Edit In Scene, or Tools > Lettera > Edit Text In Scene. A plain double click stays the editor's. Inside it is the runtime's field: caret, drag by unit, word and paragraph clicks, keys, clipboard, composition, its undo. W types and takes no tool. A bar in the Scene view, listed in its overlay menu, has B, I, U, S, a colour, the tags this text can write and markup shown or hidden; the right click has the field's commands with their titles from a player, custom ones included, then formatting.
- **It leaves nothing behind.** Esc, a click outside, another selection, Play Mode or a domain reload end it, and what was typed goes back as one undo step. Nothing is added to the object: the field lives on a hidden object of its own, so a duplicate has no helper, a prefab instance sees only its text as an override, and no scene file can hold one. Starting and leaving without a change leaves the scene clean. It declines several selected, a document, and a text whose value comes from a resolver or `SetText`, and says why. A field's rules on the same object stay out of it.
- **Every inspector is ours now.** Selectable, Editable, Dropdown, Document Scroller, Source Loader, Sequencer and Parameter Link are cards with their settings together, a switch at the end of the row it belongs to, and a fix on a warning. The smaller components each get a card and a line on what they are for.
- **A paint is one row:** a swatch of its colour, gradient or texture and a word, opening to kind, stops and how it lies on the text. A named paint lists its library with swatches. Every list in the house is the same list now, with names in place of Element 0.
- **Editable has Try It:** type, and see what its rules make of it, through a real field that lives for the trial. Rules are picked by group, with a line on each.
- **Range rules read as sentences:** chips for hover, press, focus and disabled, an effect picker that offers the effects beside the rule, and a parameter picker that says number or colour. Sequencer cues and Parameter Link choose a parameter the same way. In play mode a text with ranges shows each range's state.
- **Font pages:** where the bytes live per target, what the file says about itself, metrics by hand with a Reset, axes with their ranges and a slider on a variant, a grid of this font's glyphs that opens in the Glyphs window, Clear, Find Uses. A stack lists its families with what each covers, and a test line says which family answers each run, with missing marked. A system font has platform tabs, a switch per platform and Re-Resolve.
- **Project Settings > Lettera** closes card by card and has a filter. New: Defaults, Atlases, Tools and **In Your Build,** which says what this project carries, why, and what it weighs, with a rescan of the scenes in the build. The settings asset opens one button to here.
- **In the Scene view:** the pointer area and the ink join the padding as outlines, world text with nothing to click it says so beside itself, Text 3D has a box and a handle for its depth, and the Hierarchy's eye now hides batched world text. Tools > Lettera > Scene turns these off, per user.
- **Unity Localization, when it is installed.** A text's right click and its Interaction card have **Localize**: a Localize String Event wired to `SetText`, so the saved text stays what it is. **Localize Font**, or **+ Font By Locale**, takes a font or a font stack per locale from an asset table, in play mode. A Game Object Localizer tracks Canvas and world text, with a text's own rebuild after it writes: text, font, stack, size, colour, alignment, padding. With no such package, none of it compiles or shows.
- **Tools > Lettera > Getting Started,** also in the palette: the guides, the samples with Import, the migration tool, and every window. A new project is told once, by a line in the console and no popup.
- **Saved objects say which format they are in.** Texts, styles and assets hold `LetteraSerializedVersion`, 0 for a file from before it. `LetteraUpgrades.Register(from, to, action)` is where a later version says what to do with an older file, in the editor and never on a build. No field has needed it so far, so opening an old scene or asset changes nothing and dirties nothing: the stamp goes out with its next save.
- **New samples:** Emoji, Layout, Fonts, Styles and Rich Editor. Fonts carries a subset of Inter under the OFL, and an icon font and a pixel font drawn for it; Emoji a small COLRv1 font drawn for it.
- **The Device Check** has a page for scrolling a long document with its frame time, one for a comparison build, and memory in its report. **Share** sends the result with no second device: the share sheet on Android, a mail elsewhere.

### Fixed
- **Fifteen public classes came back from disk as missing scripts,** the same bug as the timeline's: five assets (`LetteraFieldPreset`, `LetteraInlineLibrary`, `LetteraEntranceLibrary`, `LetteraEffectPreset`, `LetteraPaintLibrary`) and ten components, among them the row of a dropdown and a password's toggle, which the GameObject menu puts in a scene. In memory they worked, so nothing showed it until a reload. Each has a file of its name now, and a test saves, reloads and checks every saved class in the package. Six internal components and the diagnostics windows moved the same way.
- **What you see from an older version:** an asset or component saved before this holds an empty script reference, and it stays a missing script in the Inspector, with its values still in the file. **Tools > Lettera > Repair Saved Objects** writes the reference back from the class name Unity 6 saves beside it, in `.asset`, `.prefab` and `.unity` files under Assets. A file saved by an older Unity has no such name, and must be remade. `LetteraRangeTopic` alone always worked, as the first class of its file: its new file took that file's guid, so saved topics open unchanged.
- Tests no longer write to EditorPrefs, which all editors of a version share: a run deleted the picker's recents in the editor you were working in.
- **A saved timeline lost Lettera's tracks and clips.** They lived in one file, and Unity finds a saved object's script by a file of its name, so they came back as missing scripts. Each now has its own file. In memory they had worked, which is why nothing showed it. Present since 0.4.0.
- **Previewing a sequencer wrote its serialized `Progress`, and took it to 0 on the way out.** A preview now moves only what it shows, so a save during one keeps the field, and leaving returns to it. The same for a sequencer's clock under a track, and a linked parameter goes back to its value when a preview ends.
- **Changing a text under a live cue could overflow the stack.** A query before the frame's rebuild parsed, and a hold, refreshing during that parse, asked for it again. Moving a sequencer right after setting its text was enough. The sample's first run in a player found this.
- **A cue removed while its hold was live, by an edit or an undo, kept holding** the value on the text until the object was disabled.
- A switch in a row of an inspector stretched across the row with its knob at the far left.
- **A document that jumped before its first layout** logged "Trying to add Text for graphic rebuild" from inside the canvas's rebuild. The Documents sample did it, on the search result it opens at.
- **Samples, the first time they ran in a player.** Fields, Motion, Documents and Math told you to add an EventSystem, and now bring one. Motion's typewriter never typed. Documents was dark blue on blue. Math's formula in world space was behind its own page. 3D Text turned far away and edge-on, and was hard to read. Basic asked the web for scripts it has no font for.
- **A one-line field cut its own text.** Its 40 pixels left 24 for a line that wants about 26, so the line sat low and scrolled inside the box, and a little less height cut it in half. The Fields sample also squeezed its column below what its fields ask.
- A dropdown's arrow was a character the web's default font does not have, and showed as a box. It is drawn now.
- **On the web, a click into a field left the selection following the mouse.** With no button held, moving the pointer selected text, and a press on a toolbar selected from the caret to there. The click's own focus change makes Unity forget that button without a release, so the event system went on dragging. The field now asks the page.
- **On the web, Shift with an arrow key undid itself:** the second press shrank what the first had selected. The browser reported the selection it had just been given, without its direction, and the field took that for a new one. A person holding the keys down would see the selection stutter around one character.
- The 3D Text and Device Check samples asked for a primitive with a collider, which a project with no Physics module has not got on the web. They make their ground from a mesh.

### Changed
- **A GameObject going off keeps what its text built.** Coming back shows the same mesh with no parse, shaping or layout, unless something changed meanwhile: its text, font, size, colour, canvas scale, or a glyph that left the atlas, which rebuilds the mesh only. Disabling the component still gives everything up, as before. 1000 labels off and the frame: 53.4 ms and 25.7 MB to 4.8 ms and nothing; back on: 78.1 ms and 25.8 MB to 30.3 ms and nothing.
- **Texts share one list of what they listen to.** Six delegates per text copied a list of every text on each enable and disable, which grew with the square of their number. 5000 world texts made and drawn: 1124 ms to 186 ms.
- **A label is cheaper at birth.** Its buffers start empty and its first mesh has room for sixteen glyphs; a text with no markup and no style shares one set of attributes and one match context, asks none of the built-in tags, and gathers its styles only when a tool reads them; what few texts use, such as holds, copies and word segmenting, comes with first use. The world batcher's 1.7 MB of merge buffers come with the first world text, and not with the first frame after any text, world or not. 1000 labels made and drawn: 312 ms to 182 ms.
- **The paragraph cache lives in native memory,** one block per paragraph in a table of its own. Storing a new paragraph makes no garbage, even while the cache grows, and a forgotten one is given back at once. The budget counts what the blocks really take. 64 score labels changing: 2.2 KB a frame to none, and the largest frame 150 KB to 12 KB; 100 multilingual labels edited: 420 KB to 37 KB a frame.
- **A distance field measures each edge only at the pixels within its reach,** and counts the fill once per row. Past the spread a pixel is fully in or out, so the bytes are the same as before, and a test holds them to the old way. 3000 Han characters, first frame: 1241 ms to 271 ms.
- **A line break no longer wakes the system font search.** No font has a glyph for one, so the first text with a second line ran the whole fallback search. First 10 KB text and its frame: 59.4 ms to 18.3 ms.
- **A first colour change of a plain text rewrites colours only.** When every quad takes the text's colour, there is nothing to note per quad.
- **`LetteraTextWarmUp.Prepare(font, characters, size)`** readies a font and a set of characters at a moment you choose, such as a loading screen: Lettera's tables, its material, the atlas page and the glyphs. Nothing is added to a build. With it the first text of a session costs 7.6 ms and its two frames 12.5 ms, against 40.2 ms and 45.8 ms without.
- **A long text places and draws only the lines near what shows.** Every line is still broken, so `Lines`, the preferred height, carets, selection and links stay exact anywhere in it; glyphs go down only on the lines within half a screen of what the root canvas and any `RectMask2D` let show, and a scroll past them places the new lines before that frame draws. A caret, hit or selection on a line far away places that line for itself, where the mesh never reads it. It starts at 4096 glyphs; `LetteraLongText` turns it off or moves that.
- **An edit of a long plain text redoes only the paragraphs from the one it touched.** The parse splices the new characters in and looks for markup only among them, shaping keeps every paragraph before the edit as it stands, and line breaking keeps their lines. A test holds every line and glyph of an edited text to the same text built from nothing, for typing at the end and in the middle, joining and splitting paragraphs, a surrogate pair and right-to-left letters.
- A layout group that asks a text's preferred height at the width it has gets the layout the text runs anyway, not a second line breaking.
- A plain text's runs continue on the face a codepoint took before, with no font search per character; line breaking sizes a run once, not per character; a text's per-character spacing buffers start as fresh zeros instead of being cleared.
- **Short paragraphs are cached too,** so a counter's digits shape once across all texts.
- **A document's window and its block measures reuse their buffers and one measuring engine,** so a scroll that moves the window and the measuring behind it make no garbage.
- **A solid glyph is held before the 3D cache trims.** A full cache gave back the solid it had just built, and the text read freed memory; the Text 3D tests hit it once the cache filled.
- **A long text finishes over the frames after it is set.** Its first paragraphs and those near what shows are shaped and broken at once; every other paragraph stands as one line of an estimated height, learned from those done, and is finished in reading order within `LetteraLongText.FinishMillisecondsPerFrame` (2 ms) a frame, across the workers where a platform has them and on the main thread on the web, starting with the frame after the one it was set in. Runs, glyphs and lines stay in reading order throughout, and a test holds a text finished by slices, by a query, deep in its middle, while typed into and across workers to the same text built whole: lines, places, heights and glyphs. `Lines`, carets, selection, hit tests, links and measures finish everything first; a layout asking the preferred height at the text's width gets the estimate. `IsFinishing` and `FinishNow()` are on `LetteraText`; `LetteraLongText.FinishLater` turns it off.
- **A plain long text holds one set of attributes,** not about 60 bytes of them per character, and keeps none of the per-character extras that only layout effects write. Plain text goes to codepoints in one tight pass and is looked through for markup once.
- **A document's window places only the lines near its viewport,** a quarter of a viewport past each edge, and places them again when a scroll inside the window goes past them. Its buffers take the room of the largest window when it opens, so no scroll grows them; measuring blocks in the background waits for a frame that builds no new window.
- A long text that only grows taller, as a content fitter makes it while it finishes, is placed again and not broken into lines again.
- **A document scrolled faster than its margins reach builds a lean window.** When a scroll steps further than a margin reaches, or leaves the whole window behind, the next frame would leave a window with margins behind too. The window then takes the paragraphs its heights point at, an eighth of a viewport past each edge, as long as the last laid out window's characters per pixel say, with a quarter to spare; it places only the lines that show and bakes its offset into the vertices, with no rest copy and no pose pass. Two frames after the scroll stops it takes its margins again, with the same line at the top; a slow scroll gets margins at once. A window's end is now where its laid out text ends, not where its last block ends.
- **An unstyled Canvas text's mesh goes up as four streams,** one copy of each array, with no vertex layout made again and no check of indices the engine wrote itself. A styled text and a part of a very long one go up as before.
- **Plain faces draw in one loop.** A text with no fill over its faces, no filter, warp, paint, fade or motion writes each quad there; a formula's part, a colour glyph and a turned glyph take the usual way. The text's colour is read once a mesh, not once a glyph, and a segment's texture and material are compared by reference. The atlas keeps a small memo of the entries it handed out before its dictionary, made stale by any change of it.
- **A field's edit in play makes no string.** The edit is spliced in a buffer of characters and waits in the engine; `Text`, `SourceText` and a save make the string when they read it. In edit mode a field writes the string as before. Setting `Text` while an edit waits replaces it, and runtime text still shows over it.
- **A plain text with no surrogate pair keeps no source index,** since every codepoint stands at its own place; the index is made when a surrogate or a tag comes in. A source that is a string is remembered by reference for the next edit to splice against, not copied. A long text's shaping buffers grow a quarter past what is asked, not to double, and never far past the text.
- A document reserves the window it expects from its viewport and font size when it opens, not the largest window. The tag walk reuses one buffer for every block, per-frame clocks are timestamps rather than objects, and a window with no ranges makes no set to look for them. The world batcher keeps each text's transform and object by reference.
- **A long text's places are sized to what shows.** A banded text took a place for every glyph shaped so far, so each slice of finishing made a new array of all of them: a 100 KB text left 6.5 MB of such arrays behind as it finished, and now none. A text of up to 32 768 codepoints takes its finishing room at its first slice, rather than growing through every size, and a long text's lines take their room at once.
- **A document scrolled to its end grows nothing.** Its last window is short and takes its margins again when the scroll stops at the end: the first wanted the rest state a window with margins keeps, larger than the window it opened with, and the second grew the attributes every short plain text shares. A document reserves the rest state with its window when it opens, and a window keeps its own attributes. The largest frame of a scroll through 1 MB: 754 KB to 6 KB.
- **A long plain text is looked through for markup as it is copied,** in one pass rather than two; markup found on the way is parsed as before. Setting 1 MB and its frame: 21.2 to 16.0 ms.

- **Lettera gives back what it keeps for speed when the game is quiet or short of memory.** After `LetteraMemorySettings.IdleReleaseSeconds` (10 s) in which no text was rebuilt, and on `Application.lowMemory`, the paragraph cache, the atlas's upload textures and outline buffers go back, and the world batcher's merge buffers and the atlas pages too once no text is left. Texts keep what they show: they draw, edit and type right after a release, and the next one that needs a paragraph or a glyph makes it again. Glyphs readied by a warm-up with no text yet stay. Project Settings > Lettera > Memory has both settings and Release Now; `LetteraMemory.Release()` and `LetteraMemory.Released` are there for a script.
- **A font can be named by its file.** `LetteraFont.CreateFromFile(path)` reads the file where it lies, mapped where the platform allows, so its bytes cost no heap. A font made from an array (`CreateFromBytes`) and a font asset's bytes are now read in place by the native library instead of copied into it, so a font's bytes are held once. Native library 4, rebuilt for every platform.
- **Glyph-major text with a stroke and a shadow draws one quad a glyph on a Canvas,** in a shader variant of its own: the shader composes face over stroke over shadow as the three quads blended in that order would, within blend rounding, and a tint the canvas multiplies in, as a button's transition does, reaches every part. Where merging cannot keep the picture (a texture or gradient paint, another blend, a stroke not wholly outside, a shadow farther than the glyph's margin, more layers, filters, warps, motion, a material of your own), the separate quads stay. Text in the usual layer order does not merge: there a glyph's face cannot join its layers, which its neighbours' layers lie between, and joining only its stroke and shadow, tried, saved a third of the quads while two more vertex streams on every quad cost the GPU more (R27, 0.80 to 1.07 ms). It shares its corners instead, below.
- **A mask culls a text whose ink lies outside it,** though its rect reaches in: a label scrolled to the edge of a `RectMask2D` with only its empty margin inside draws nothing there.
- **Atlas pages of 1024 as a project choice,** per kind, in Project Settings > Lettera > Atlases: a page costs a quarter, and the limit on pages counts in pages of 2048, so as many glyphs fit before recycling. Distance-field pages were already 8-bit.
- **An IL2CPP player is 6.7 MB smaller with one text in it** (Windows, release, at Unity's default stripping): 27.97 MB above an empty project to 21.30 MB. At that stripping level a build copies every package assembly it references whole. The core assembly no longer references Mathematics or Collections: the distance-field rasterizer's four vector structs and one list are Lettera's own, with the packages' formulas, so Burst compiles the same arithmetic and the fields come out byte for byte the same (4.75 MB). 3D text keeps both packages in its own assembly, which a build leaves out when nothing uses it. Lists of structs inside the engine are a list of Lettera's own with the members a list needs: IL2CPP writes some 130 functions for each struct a `List<T>` holds, its interfaces, comparers, sorting and enumerators, and about a dozen for this one (1.33 MB). Lists a public member, a Unity API or an `IReadOnlyList<T>` hands out stay `List<T>`.
- **The web font stays out of Windows, macOS, Android and iOS builds** (0.30 MB), where a text reaches it only when the system offers no font at all. Linux and the web keep it, and so does a project that names it as its web fallback or default font. The asset keeps its name, without its bytes.
- **The native library leaves out the HarfBuzz code Lettera never calls:** the shared libraries are linked with function sections and garbage collection, a fifth of the Windows library (1.62 to 1.32 MB). The code that stays is compiled the same.
- **A Windows player carries only its architecture's native library.** Unity copies a plugin of every CPU into each build; an Intel 64-bit player carried the ARM64 library too, 1.4 MB it never loads.
- Lettera compiles in a project without the Physics module; world text's blockers then ignore physics.
- A new atlas page is bound to the shaders as it is made, not at the next flush, so a canvas that draws first does not show the empty placeholder for a frame.
- **A text's engine is a small object again.** It had grown to 2072 bytes, past the 2 KB beyond which the collector of IL2CPP and Mono makes each object a block of its own, slower to make and to sweep. What only some texts use (motion, reveals, ranges, documents, finishing a long text, carets) lives in a side object: the engine is 1216 bytes and the side 872, and a test holds the engine under 1800.
- Lists only some texts fill come with first use.
- **The world batcher merges a text with array copies and one read of its matrix,** the one the update just read, and compares materials and textures by reference; a mirrored text's normal is told from the matrix's own numbers.
- **A Canvas text keeps its vertex buffer's size and its indices while they fit.** The same count writes vertices only, and a text of 2048 vertices or more keeps an eighth to spare, so a scroll that changes its count a little each frame no longer sizes the buffer each frame. What lies past the count is no quad's.
- **A player sets up what the first engine needs on a thread of its own as it starts:** some fifty generic types and the native library, 10 ms of the first text on IL2CPP. None of it touches Unity; it runs before the first scene where a player has more than two cores, and not on the web, in the editor, or with `LetteraWorkers.Mode` set to `Inline` before then.
- **What a text holds per codepoint lives in native memory,** three blocks a text: its codepoints, what shaping writes per codepoint, and what layout effects add. A long text's megabytes neither start collections nor are marked by them; a long text left for later takes its shaping block unzeroed, so the pages no paragraph reaches are never touched, and an engine made for one measure gives its blocks back at once. A long text's band takes room for the places of its lines at once, rather than doubling up to them. Setting 1 MB: 15.1 to 8.1 ms, and with its frame 15.9 to 8.9 ms.
- **A world label's first mesh is sized to its glyphs,** four at least: thousands made at once no longer leave room for sixteen each as garbage. A Canvas label keeps room for sixteen, so one made again with more glyphs does not grow.
- **A session's first text pays for less.** A plain text no longer makes the 78 built-in styles only to find it needs none of them; a text with markup makes them as before. The start-up thread also sets up the Unicode tables and analyzers, the comparers IL2CPP makes by reflection for keys that are enums, tuples or structs, and the collections of the atlas and of font chains. The settings asset and the UI and world shaders start loading on Unity's loading thread as a player starts. An atlas texture is made without Unity clearing and uploading it first, since every page's pixels are set and uploaded right after. First text of a session, call and two frames: 25.9 and 34.5 ms to 15.5 and 21.0 ms; a 19 MB font's load 10.2 to 6.8 ms.
- **Short labels that change in the same frame go through one batch across the threads.** The first Canvas geometry update of a frame takes every queued text under 2048 characters: the main thread parses them, while the threads already shape from the paragraph cache, break lines, place and, for plain faces whose glyphs the atlas holds, build the mesh; each text then hands its mesh to Unity as before. A paragraph to shape anew, a glyph to make, or a change to the text during its batch is done on the main thread, so the result and the cache counts are the same, and tests hold every vertex to a text built alone. `LetteraWorkers.BatchShortTexts` turns it off, `BatchFromTexts` (8) says how many texts make a batch; it is off on the web, in edit mode and on two cores or fewer. IL2CPP, before at `c50c876`, two runs of each build in turns: 1000 labels marked dirty and their frame 42.4 and 39.1 to 32.8 and 33.2 ms, 64 score labels 1.60 and 1.60 to 1.56 and 1.54 ms with no allocation; 64 changing labels 1.70 and 1.53 to 1.69 and 1.53 ms, no change. The Mono player: 64 changing labels 1.76 to 1.42 ms, 64 score labels 1.68 to 1.47 ms, 1000 marked dirty and their frame 41.0 to 27.7 ms.
- **The system's font files are tried in one pass, and a player lists them on its start-up thread.** The first character no font of a project has walked some 340 file names once per priority stem, 21 times, to order them, and a font by name walked them all again; now each name is split once, the order comes from one pass (the same order, which a test holds to the old way), and a name is a table lookup. A player with more than two cores lists the folders on the thread that already prepares the first engine, unless system fallbacks are off. 100 Arabic and Latin labels made and their frame: IL2CPP 20.3 and 17.9 to 13.2 and 13.3 ms, Mono 39.9 to 18.8 ms.
- **Canvas text with a stroke and a shadow in the usual layer order stores each glyph once.** Every shadow still lies under every stroke and every stroke under every face, and the picture is the separate quads' within the last bits of an interpolated coordinate. Each glyph has four vertices, which the index buffer names three times: the shadows' pass, the strokes' pass, the faces' pass. A triangle reads its colour, edge, softness and the shadow's shift flat from the vertex that leads it, so each pass leads with a corner of its own; the quad covers all three layers, and the shadow pass reads the field where the shadow's own quad read it. A still frame moves a third of the vertices, and on the comparison's machine a Canvas's GPU time follows the bytes of vertices it draws: 200 labels with a stroke and a shadow (R27), Windows IL2CPP, 10 runs: GPU 0.80 to 0.43 ms a frame and the frame 2.47 to 1.64 ms. Its GPU time has two levels from run to run, 0.32 in four runs and 0.43 to 0.44 in six, and a probe with the rows' order alone reads 0.28 to 0.30; the separate quads read 0.80 in every run. The same 200 plain are unchanged, GPU 0.27. Direct3D, Vulkan and Metal lead with a triangle's first vertex, OpenGL ES and WebGL with its last, and the indices follow; desktop OpenGL, which can be set either way, keeps the separate quads, as do texts with anything else on them (a paint, a fill, a filter, a warp, a line, a fade, motion, a stroke not wholly outside, a shadow farther than the glyph's margin, a colour glyph, a material of your own). A variant of the UI shader of its own, `LETTERA_LAYERED`.
- **A distance-field quad on a screen-space Canvas is cut to what it shows:** its edge, softness, layer reach and two pixels for anti-aliasing, instead of the whole tile, whose margin is there for the widest layer. Each pixel left reads the field where it did. The test's labels shade at least a quarter fewer pixels; the comparison's GPU, bound by vertex bytes, does not show it, and a phone's, bound by what it fills, has not been measured. World space keeps whole tiles, since a camera can come as near or go as far as it likes.
- `LetteraShaderWarmUp` has `LetteraSurfaceTier.UiMerged` and `LetteraSurfaceTier.UiShared`, the variants glyph-major and layer-order Canvas text with a stroke and a shadow draw with, and `All` includes them; `LetteraTextWarmUp.Prepare` makes their materials.

### Figures
Windows IL2CPP release player, Lettera alone in the comparison harness, median of 5 cold runs, before at `3fea08b` and after this batch; allocation from development players:

| Row | Before | After |
|---|---|---|
| Deactivate 1000 and the frame | 53.4 ms, 25.7 MB | 4.8 ms, 0 |
| Reactivate 1000 and the frame | 78.1 ms, 25.8 MB | 30.3 ms, 0 |
| Set 10 KB, first long text, and the frame | 59.4 ms | 18.3 ms |
| Set 10 KB again | 9.3 ms | 7.8 ms |
| First text of a session, call; two frames | 42.5 ms; 51.4 ms | 38.8 ms; 45.8 ms |
| Same after `LetteraTextWarmUp.Prepare` | | 7.6 ms; 12.5 ms |
| Create 1000 and the frame | 312 ms | 182 ms |
| 5000 world, create and first frame | 1124 ms | 186 ms |
| 64 score labels by SetText; allocation; largest frame | 2.21 ms; 2.2 KB; 150 KB | 1.69 ms; 0; 12 KB |
| Keystroke in 2000 characters; allocation | 1.94 ms; 16.2 KB | 1.85 ms; 8.5 KB |
| 100 multilingual all edited; allocation | 8.37 ms; 420 KB | 7.24 ms; 37 KB |
| 64 labels change a frame; largest frame | 1.96 ms; 8.7 KB | 1.50 ms; 8.7 KB |
| Rebuild 1000 | 30.3 ms | 22.7 ms |
| Multilingual 100, cold; recreated | 114 ms; 19.5 ms | 78.6 ms; 12.2 ms |
| Set 100 KB; 1 MB, and the frame | 175 ms; 1507 ms | 98 ms; 806 ms |
| 3000 Han, first frame | 1241 ms | 271 ms |
| Heavy scene, private memory | 992 MB | 841 MB |

The Mono player, the same way: deactivate and reactivate 1000, 58.7 and 85.2 ms to 4.0 and 28.2 ms; first 10 KB text 99.4 to 32.3 ms, and again 12.7 to 13.4 ms; first text, call 182.8 to 174.4 ms; create 1000, 459 to 329 ms; 5000 world, 1098 to 242 ms; 64 score labels 2.53 to 2.02 ms; keystroke 2.54 to 2.43 ms; multilingual edited 10.7 to 8.7 ms; 3000 Han 1346 to 306 ms. 100 Arabic and Latin labels made went from 21.3 to 39.9 ms: the system font search that a line break used to start in the first long text now runs at the first character no font has, here their Arabic, and costs less where it lands than it did there. A 19 MB font loads in 57.4 ms against 55.5 ms.

Open a 1 MB document and the first frame reads 56.7 ms against 42.7 ms. That is the previous row's 1 MB text, which now builds in half the time, still being uploaded by the render thread when this row starts: with 30 idle frames before it, the row is 43.1 ms, the same as before.

### Figures, second batch
The same harness, IL2CPP, median of 5, before at `71db03c` and after this batch:

| Row | Before | After |
|---|---|---|
| Set 10 KB, first long text, and the frame | 17.6 ms | 11.8 ms |
| Set 10 KB again | 7.58 ms | 1.76 ms |
| Set 100 KB and the frame | 95.7 ms | 29.8 ms |
| Set 1 MB and the frame | 804 ms | 196 ms |
| 100 KB, a character a frame; GPU | 50.9 ms; 3.37 ms | 1.52 ms; 0.049 ms |
| Scroll a 1 MB document, a frame; GPU; allocation a frame | 4.63 ms; 0.77 ms; 17 KB | 4.36 ms; 0.32 ms; 152 B |
| Keystroke in 2000 characters | 1.79 ms | 1.63 ms |
| 100 multilingual all edited | 7.08 ms | 6.90 ms |
| 64 labels change a frame | 1.49 ms | 1.45 ms |
| Heavy scene, private memory | 842 MB | 793 MB |

The 100 KB row's 205 KB a frame is the harness's own: it writes a new 100 KB string each frame.

Worse: opening a 1 MB document and its first frame, 55.7 to 107 ms. The 1 MB text before it now leaves its garbage to this row, where it used to be collected inside its own 800 ms: with a collection forced before the row, the row is 40 ms. 5000 world texts made and their first frame, 185 to 250 ms, with no pass of Lettera's slower by that much; a collection landing there is likely and not shown. A 19 MB font's first text, 59.5 to 64.1 ms, cause not found. A second text's two frames (2.4 to 3.1 ms) and a masked column standing still (1.31 to 1.57 ms) read the same with this batch's code left out, so they are the machine's.
The Mono player, the same way, against batch one's Mono figures: set 10 KB and the frame 32.3 to 20.2 ms, again 13.4 to 2.4 ms; 100 KB 132 to 34 ms; 1 MB 1109 to 240 ms; a character a frame into 100 KB 84.4 to 1.49 ms; scrolling 1 MB 7.58 to 6.95 ms and 17 KB to 144 B a frame; keystroke 2.43 to 2.07 ms; 64 labels 1.62 to 1.49 ms. Opening the 1 MB document went from 114 to 156 ms and 5000 world texts from 242 to 271 ms, as on IL2CPP; a 19 MB font's first text 193 to 203 ms.

### Figures, third batch
The same harness, IL2CPP, median of 5, before at `40027dd` and after this batch. The harness now pauses for a collection and ten frames before the rows after the long texts, for every engine (see `Documentation~/Comparison.md`), so the before figures here were measured again with it:

| Row | Before | After |
|---|---|---|
| Set 10 KB, first long text, and the frame | 11.4 ms | 1.32 ms |
| Set 10 KB again | 1.79 ms | 0.36 ms |
| Set 100 KB and the frame | 29.1 ms | 1.80 ms |
| Set 1 MB and the frame | 196 ms | 31.5 ms |
| 100 KB, a character a frame; largest frame | 1.46 ms; 38 MB | 1.48 ms; 8.2 MB |
| Scroll a 1 MB document, a frame; GPU; largest frame | 4.49 ms; 0.319 ms; 9.5 MB | 3.11 ms; 0.216 ms; 757 KB |
| 5000 world texts made and their first frame | 251 ms | 225 ms |
| Peak memory of the main run | 770 MB | 685 MB |

Without the pause, opening a 1 MB document and its first frame was 115 ms before and 46 ms after: the long texts before it leave far less garbage.

Worse: opening the 1 MB document, 42.5 to 46.4 ms with the pause, since the document now takes its window's whole room when it opens. A frame of 5000 world texts standing still, 2.64 to 2.84 ms, with its GPU time 0.238 to 0.222 ms; no pass of the world texts changed, and the cause was not found. A keystroke in 2000 characters, 1.63 to 1.66 ms, near its spread. A 19 MB font's first text read 64.1 ms before the pause and 57.6 ms with it, on the same code, and 63.9 ms after: its own spread is 7 ms. The first text of a session and its two frames, 45.6 to 49.7 ms, inside a spread of 7 ms.
The Mono player, 3 runs, against the second batch's Mono figures: set 10 KB and the frame 20.2 to 6.5 ms, again 2.4 to 0.38 ms; 100 KB 33.8 to 1.9 ms; 1 MB 240 to 27.7 ms; scrolling 1 MB 6.95 to 4.68 ms a frame; opening it 156 to 96 ms, the pause included. Worse on Mono: one label changing a frame 1.23 to 1.32 ms, and 5000 world texts standing still 3.66 to 3.83 ms, as on IL2CPP.

### Figures, fourth batch
The same harness, IL2CPP, median of 5, before at `6e3a2df` and after this batch:

| Row | Before | After |
|---|---|---|
| Scroll a 1 MB document, a frame; GPU; allocation a frame | 3.11 ms; 0.216 ms; 152 B | 1.79 ms; 0.172 ms; 0 |
| Open a 1 MB document and its first frame | 46.4 ms | 38.2 ms |
| Set 1 MB and the frame | 31.5 ms | 21.9 ms |
| Keystroke in 2000 characters; allocation a frame | 1.66 ms; 8.5 KB | 1.56 ms; 0 |
| A frame of 5000 world texts standing still | 2.84 ms | 2.04 ms |
| 5000 world texts made and their first frame | 225 ms | 214 ms |
| Rebuild 1000; the same in a frame | 23.1 ms; 43.0 ms | 18.4 ms; 38.6 ms |
| 64 score labels by SetText | 1.73 ms | 1.62 ms |
| 100 multilingual all edited; allocation | 6.92 ms; 37 KB | 6.45 ms; 31 KB |
| Set 10 KB again | 0.36 ms | 0.32 ms |

Worse: setting 100 KB and its frame, 1.80 to 7.17 ms in four runs of five, and 1.69 ms in the fifth. Its code path is the same as in builds that read 1.15 to 1.2 ms: the commit that turned it removed only timing lines, and a build with one unrelated script added reads 1.14 ms again. Per frame, those builds show collections landing on the frames of the long-text rows, and a collection costs 2 to 3 ms on this heap; a text that leaves less garbage keeps the heap smaller, so collections come more often, and where one lands moves with any change of the build. Reactivating 1000 labels, 29.4 to 32.7 ms, likewise splits by run, at 29.3 or 32.8 ms. Near the frame floor, inside their spreads: a focused field standing still 1.38 to 1.45 ms, one label changing a frame 1.30 to 1.37 ms, a character a frame into 100 KB 1.48 to 1.59 ms. Private memory after 15 s idle, 789 to 795 MB, inside a spread of 20 MB.
The Mono player, 3 runs, against the third batch's Mono figures: scrolling 1 MB 4.68 to 2.34 ms a frame and 144 B to none; opening it 95.9 to 87.7 ms; set 1 MB and the frame 27.7 to 20.9 ms, 100 KB 1.94 to 1.48 ms; a keystroke 2.07 to 1.67 ms and 8.5 KB to none; 5000 world texts standing still 3.83 to 3.45 ms; rebuild 1000 31.0 to 23.1 ms; 64 score labels 1.90 to 1.66 ms; 100 multilingual edited 8.37 to 7.36 ms. One label changing a frame stays where the third batch left it, 1.32 to 1.35 ms inside its spread; a 19 MB font's first text 216 to 232 ms and the first 10 KB text's frame 6.5 to 7.9 ms, each inside a spread of 18 and 1.4 ms.

### Figures, fifth batch
The same harness, IL2CPP, median of 5, before at `5bf7c8c` and after this batch. Each long text of R8 now starts after a collection and ten idle frames, for every engine (see `Documentation~/Comparison.md`), so the before figures were measured again with it:

| Row | Before | After |
|---|---|---|
| Set 1 MB and the frame | 21.2 ms | 15.95 ms |
| Set 100 KB, the call alone | 1.12 ms | 0.79 ms |
| Scroll a 1 MB document, largest frame | 754 KB | 6.1 KB |
| A character a frame into 100 KB, largest frame | 6.9 MB | 1.0 MB |
| Heavy scene, private memory | 792 MB | 774 MB |

**Setting 100 KB and its frame.** Without the pause, at `5bf7c8c`, it read 1.73, 7.13, 2.90, 7.05 and 2.90 ms in five runs. A probe player that counts collections per frame shows why: the players keep Unity's default, the incremental collector, and a cycle started by what the rows before had allocated, 1.7 MB of it by the 100 labels of the colour rows, about 1.4 MB by the two 10 KB texts and their finishing, and the comparison's own strings, ran its last slices in this row's two frames in some runs and before them in others. The same probe built with the incremental collector off read 1.64 to 2.19 ms in five runs of five. This batch's cuts moved where that cycle starts rather than removing it: without the pause, this batch read 6.68 ms in four runs of five. With the pause the row reads 1.76 to 2.02 ms before and 1.84 to 2.31 ms after, in every run: the call is a third faster, and the frame after it pays for sweeping what the pause's collection freed, as every engine's does. The incremental collector, on in these players, also costs: in the probe, 1000 labels made and drawn read 171 ms with it and 157 ms without, and 5000 world texts 217 and 208 ms; setting 1 MB read 16.3 ms with it and 19.4 ms without. Nothing here changes it for users.

Worse: setting 100 KB with its frame, 1.95 to 2.10 ms, inside a spread of 0.48 ms, with the call itself 1.12 to 0.79 ms. Inside their spreads: 1000 labels made and drawn 169.7 to 172.6 ms (spread 117 with the first, cold run, 4.5 without it), a frame of 1000 labels standing still 3.27 to 3.35 ms, the first 10 KB text and its frame 1.82 to 1.90 ms, 64 labels changing a frame 1.42 to 1.44 ms. Reactivating 1000 labels, 29.33 to 29.32 ms: the split of the fourth batch, 29.3 or 32.8 ms by run, shows in runs of the same build before and after, and the probe counts no collection in that row in either kind of run, so it is not this code's.
The Mono player, 3 runs, with the pause, against the fourth batch's Mono figures, which had none: the largest frame of scrolling 1 MB 754 KB to 4 KB, and of a character a frame into 100 KB 5.1 MB to 1.0 MB; set 1 MB and the frame 20.9 to 20.5 ms; the first 10 KB text and its frame 7.9 to 6.4 ms; reactivating 1000 labels 28.3 to 25.8 ms. Worse on Mono: setting 100 KB and its frame 1.47 to 1.93 ms, where Mono had not split by run and the pause's sweeping lands; Mono with the pause and without this batch's code was not measured. A focused field standing still 1.34 to 1.46 ms and a masked column standing still 1.53 to 1.65 ms, near the frame floor, with no code of theirs changed.
The web lab passes in Chromium, WebKit and WebKit as an iPhone: 100 KB set in 3.0 to 4.0 ms and finished over 11 to 13 frames, exact once finished.

### Figures, sixth batch
The same harness, IL2CPP, median of 5, before at `96dd770` and after this batch. R16 now opens its font by path for Lettera, as TextMesh Pro does; R16b keeps the array (see `Documentation~/Comparison.md`), and the before figures of both are the array route, the only one `96dd770` had:

| Row | Before | After |
|---|---|---|
| 19 MB font by its path: load; first text; private memory added | 30.4 ms; 59.2 ms; 63.6 MB | 21.9 ms; 47.8 ms; 26.8 MB |
| The same from an array: first text; private memory added | 59.4 ms; 64.3 MB | 55.9 ms; 45.5 MB |
| 200 masked labels, triangles drawn | 590 | 564 |
| Heavy scene, private memory; after 15 s idle; text destroyed and 15 s idle | 777 MB; 776 MB; 776 MB | 772 MB; 772 MB; 767 MB |
| 200 labels with a stroke and a shadow, GPU | 0.802 ms | 0.804 ms |

Glyph-major text with a stroke and a shadow, 200 labels in the same scene, in a probe build that is not a row of the harness: GPU 1.04 ms with the quads apart, 0.45 ms merged. Joining a glyph's stroke and shadow in layer order, tried and left out: 0.80 to 1.07 ms, since the two more vertex streams every quad then carries cost more than the third of the quads saved.

**Build size,** a project of its own: an empty uGUI project (uGUI, UI, IMGUI, JSON modules) against the same with Lettera and one text in a scene, Windows IL2CPP release, files shipped, after at `c03201a`; the web build is gzip-compressed:

| | Before | After |
|---|---|---|
| Windows, added in all | 15.78 MB | 14.40 MB |
| GameAssembly, added (of 20.52 and 20.56 MB) | 10.12 MB | 10.18 MB |
| global-metadata, added | 2.09 MB | 2.10 MB |
| Native libraries | 3.05 MB (x64 and ARM64) | 1.62 MB (x64) |
| Web, added (empty: 4.77 MB) | 4.38 MB | 3.29 MB |

What each cut costs, after: `LETTERA_NO_MATH` takes 0.44 MB off (13.96 MB); `LETTERA_NO_3D` nothing, since a build leaves out the 3D assembly when no scene uses it; both 13.96 MB. Unity's IL2CPP Code Generation set to Faster (smaller) builds, a project setting, takes Lettera's part to 10.64 MB (GameAssembly 6.42 MB). Of Lettera's added code, about half is generic code made for its value types (lists, dictionaries, sorts, comparers), an eighth `Unity.Mathematics`, which Burst and Collections bring, and an eighth Lettera's own methods. The `96dd770` build needed the Physics module to compile, so its base project had it too; the web build before carried its code, likely most of the 1.1 MB its WebAssembly lost, which was not built apart. The 29.4 MB of the targets came from another method and is not reproduced here.

Worse: 5000 world texts made and their first frame, 212 to 230 ms, in every run. Built commit by commit it appears with the commit that added merged layers, where no path of this row changed; an atlas entry four bytes larger cost Mono the same row and a rebuild of 1000 labels 2 ms, and taking it back restored both on Mono (270 and 23.0 ms), not this row on IL2CPP. No cause was found. Rebuilding 1000 labels, 18.3 to 18.6 ms, and 100 multilingual labels again, 11.7 to 12.4 ms, near their spreads.
The Mono player, 3 runs, before and after: the 19 MB font by path loads in 51.3 ms against 58.6 ms, its first text 187.6 against 196.2 ms, and adds 13.0 MB against 51.2 MB; from an array it adds 32.0 MB against 51.3 MB; 200 masked labels draw 564 triangles against 590; a frame of 200 labels with a stroke and a shadow keeps its GPU time, 0.80 ms. Worse on Mono: 3000 Han characters' first frame 304 to 311 ms, inside its spread of 11 ms.

### Figures, seventh batch
The same harness, IL2CPP, median of 5, before at `ce3c041` measured again and after this batch:

| Row | Before | After |
|---|---|---|
| 5000 world texts made and their first frame | 232.1 ms | 214.1 ms |
| 19 MB font by its path: load; first text | 22.3 ms; 48.0 ms | 9.9 ms; 35.4 ms |
| The same from an array: load; first text | 31.9 ms; 54.4 ms | 19.6 ms; 44.7 ms |
| First text of a session, call; two frames | 37.9 ms; 45.8 ms | 25.6 ms; 34.0 ms |
| `LetteraTextWarmUp.Prepare`, the call | 32.5 ms | 21.0 ms |
| Create 1000 and the frame | 176.7 ms | 165.3 ms |
| 100 multilingual, cold | 80.3 ms | 63.2 ms |
| Open a 1 MB document and its first frame | 43.3 ms | 40.2 ms |
| 19 MB font, private memory added | 26.8 MB | 25.2 MB |

**5000 world texts' first frame** ran three or four collections, and the row had moved with commits that changed none of its paths, both ways: 219 ms at one of the sixth batch's commits, 253 ms at the next. That is collections landing in its frame or not. The engine had also grown to 2072 bytes, past where the collector makes a large block of its own. Taken in steps, 3 or 4 runs each: the engine under 2 KB 232 to 226 ms, the batcher's merge 224 ms, then its matrix and comparisons, the lists made on first use and a first mesh sized to its glyphs 206 ms. That last was left out, 6 ms of this row in alternated runs: it cost 100 multilingual labels made again 0.6 to 0.8 ms, and that row is held. **The first text's start-up** was most of the 19 MB font's load: the row makes the process's first Lettera component, and the font is opened at its first rebuild. Of its 22 ms, the first engine took 11.6 ms, 7 of them some fifty generic types it sets up whatever its text, and the font 0.02 ms.

Worse, inside their spreads: 3000 Han characters' first frame 277.7 to 284.7 ms (in alternated runs of both builds 312.3 and 311.9 ms; a profile puts 220 to 240 ms of it in making distance fields, which this batch did not touch), 100 multilingual labels made again 11.9 to 12.5 ms, a mask swiped away 1.14 to 1.29 ms, 200 plain labels a frame 1.53 to 1.67 ms, 1000 labels standing still 2.98 to 3.11 ms, 64 labels changing a frame 1.52 to 1.56 ms. A measurement taken while the machine was busy with other programs read every row 15 to 25% slower, these untouched ones too, and was thrown away; alternated runs of the two builds then agreed with the table.
The Mono player, 3 runs of each build alternated: the 19 MB font's load 55.1 to 42.2 ms and its first text 198.6 to 190.5 ms, the first text of a session 188.0 to 174.6 ms, 100 multilingual cold 266.7 to 245.0 ms, create 1000 and the frame 334.6 to 320.7 ms, 64 labels changing a frame 1.65 to 1.57 ms; 5000 world texts 270.3 to 268.5 ms, no gain on Mono. Worse on Mono, in 3 runs: the first 10 KB text 5.58 to 6.04 ms, one label changing a frame 1.17 to 1.36 ms and 1000 labels standing still 2.93 to 3.17 ms, near the frame floor.
The web lab passes in Chromium, WebKit and WebKit as an iPhone, 139 checks; the web player sets up its types on the main thread, as before.

### Figures, build size
A new project (`-createProject`) with uGUI and the UI, UI Toolkit, IMGUI, JSON, image, asset bundle, screen capture, web request and Android JNI modules, a script that makes a Screen Space Overlay canvas at start, and with Lettera one `LetteraText` saying `Hello` under it; Windows, IL2CPP release, Unity's default stripping (Minimal), files shipped (the output without its `DoNotShip` folders), in MB of 2^20 bytes:

| | Shipped | Above empty | GameAssembly | Metadata | Native |
|---|---|---|---|---|---|
| Empty | 65.05 | | 9.51 | 2.29 | |
| One uGUI `Image`, no Lettera | 78.79 | 13.74 | 21.62 | 3.91 | |
| Lettera, before (`c50c876`) | 93.02 | 27.97 | 31.70 | 5.99 | 1.62 + 0.06 Burst |
| Lettera, after | 86.35 | 21.30 | 26.54 | 5.11 | 1.32 + 0.02 Burst |

13.74 MB of what this project counts above empty is uGUI's own, which any `Graphic` brings: the empty project's canvas uses no code of `UnityEngine.UI`, and a project with the UI Toolkit module compiles uGUI's bridge to UI Toolkit, which keeps much of that module (3.2 MB of its object code, and its generic types). Lettera's part over one `Image` went from 14.23 to 7.56 MB: 4.92 MB of code, 1.19 MB of metadata, the native library. Before, the 14.23 MB were 10.07 MB of code (Mathematics and Collections whole, about 4 MB with their generic types; some 130 functions for each of 85 structs in a list; Lettera's own methods, 1.9 MB of objects), 2.08 MB of metadata, 1.62 MB of native library, 0.30 MB of web font and 0.06 MB of Burst code. The batch three figure of 14.40 MB came from a project whose base scene already held uGUI and no UI Toolkit module.

The web, the same probe, the build's default gzip output in all: empty 5.14 MB, before 11.76 MB (wasm 9.24, data 2.39), after 10.77 MB (wasm 8.49, data 2.15); the web keeps its font. The web player of this batch draws the text in WebKit.

### Limits
- **Performance rows still short of their targets:** the first text of a session with no warm-up (25.6 ms, target 12) pays for its first component (Unity's own part of a Graphic's first enable, Lettera's player-loop hooks and settings, 6.7 ms), the engine's own statics, its first atlas page and glyphs and its material, which only `LetteraTextWarmUp.Prepare` or a glyph set baked ahead could move, and no baked set exists; 64 labels changing a frame (1.56 ms, target 1.4): per label the upload now writes vertices only, and a frame of one label changing reads 1.26 ms, so the 64 labels' own part is about 0.3 ms, near the spread. 1000 labels made and drawn meets its target now. The largest single frame of 64 changing labels or of a masked column is 9 to 19 KB in a player, against 4 KB; the same frames replayed in the editor allocate nothing, so what remains comes from a part of the player's frame not yet found.
- **A long text's sizes are exact once it is finished, and its glyphs are not all there.** `GlyphCount`, `GetGlyph`, `InkBounds` and the mesh cover the placed lines only; `Lines`, carets, selection rects and links cover all of it, and finish whatever waits first. A selection's whole lines far from the screen are their line boxes, not their glyphs. What decides the band is the root canvas on screen and `RectMask2D`: under a stencil `Mask` the band is the canvas, and a world canvas with no `RectMask2D` places everything. A text with a curve, a marquee, a reveal, an inline object or a stage hook places everything.
- **What a long text's estimates mean for callers.** Until the paragraphs left for later are done, a few frames for 100 KB, the text's height, `LayoutBounds` and a layout's preferred height are estimates from the paragraphs already done, and a scrollbar driven by them may move; lines already placed do not move unless paragraphs above them were estimated, as when a text is set with its view far down, which moves them once to their exact place when all above is done. A text that waits only for paragraphs that do not show costs about 2 ms a frame until done. Only a top-aligned text with no layout effect, no autosize and no eliding leaves paragraphs for later; any other is done whole as before.
- **Big texts against their targets:** setting 1 MB and its frame costs 16.4 ms (target 15). Profiled in the seventh batch: 7.5 ms is placing, nearly all of it shaping the paragraphs in the band with its buffers grown to the band's place, 3.5 ms parsing, 2.5 ms drawing, and two or three collections run inside the call. What is left is allocation: every character still becomes a codepoint in one array of four bytes each (4 MB), and the shaping buffers reach to the band's place in the text, about 17 bytes a codepoint up to a third of the way in under the harness's rect (6.9 MB); the incremental collector charges its marking to the code that allocates, so a pass that takes megabytes pays for some of a collection. Buffers in native memory would take both off the managed heap; that touches some 280 places and was not done in this batch. Scrolling a 1 MB document costs 1.77 ms a frame (target 1.6), and its largest frame is now 6 KB. Sizing the vertex buffer, 0.19 ms of the main thread a frame, now happens in 3 frames of 237, and the frame stayed within its spread: what it saved is not where this frame waits. at the harness's speed every frame shows a new viewport of text, and a lean window still parses, shapes, breaks, places and draws that viewport's characters on the main thread. A band built ahead in the background would cost the same work a frame at that speed, since nothing built is seen twice; only threads would take it off the frame. Per frame on IL2CPP the passes take 0.09 ms parsing, 0.27 ms shaping, which already goes across the workers past 4096 codepoints, 0.18 ms breaking lines, 0.15 ms placing and 0.32 ms drawing; breaking and drawing write the engine's own state on the main thread, and taking them across threads means splitting that state first, which this batch did not do. Measuring the document behind it waits while windows move, so the scrollbar's size refines when scrolling stops. Opening it costs 38 ms (target 17).
- **A lean window is placed by the document's estimates**, so in a fling the line at the top is where the heights of blocks not yet measured put it, as with any window opened by a scroll; the window that takes its margins back two frames after keeps that line.
- **A field's `Text` in play is made when read.** A script that reads it each frame gets a new string each frame after an edit, as before; one that does not read it pays nothing for the edits.
- **Memory and fonts against their targets:** a 19 MB font's first use loads in 9.9 ms (target 8). The row makes the process's first Lettera component and opens no font, which comes at the first rebuild (2.2 ms there): what is left is that component's start-up, 6.7 ms of it Unity's first enable of a Graphic with Lettera's hooks, which must run on the main thread. Destroying the heavy scene gives back 4.6 MB after the idle release: most of what a destroyed text held is likely managed memory, which Unity's collector keeps for the process; this was not measured apart. A text's own buffers are not given back after it stays unused for a while, only the shared ones.
- **Not done in these batches:** words cached below the paragraph (P4) beyond caching short paragraphs, and filling new glyphs off the frame (P28). The paragraph cache already holds every row's short labels, so P4 would move no row of the harness: a profile of 64 changing labels puts their shaping at a cache restore of about 3.6 µs a label; the batch of short labels (P5) helps a frame that rebuilds many labels, and not the harness's create and forced rebuild rows, which force each label alone and, on creation, shape paragraphs no cache holds yet, which only the main thread does; on IL2CPP 64 changing labels cost the same with or without it; 3000 Han characters' first frame stands at 273 ms against a target of 400, so P28 was not taken up.
- A selectable has no menu or loupe of its own per platform: Android Menu, iOS Menu and Loupe stay project-wide, and the page says what they are. It would need new fields and a runtime path.
- Per-glyph overrides of scale and offset are not built. Metrics are.
- A UnityEvent's list wears Unity's own look: who is called with what has a world of pickers behind it. Two object slots in the 3D pages too (a solid material's texture, a text's look).
- The Hierarchy's eye reaches batched world text until Play, and hides it in the Game view too in edit mode. Canvas text, standalone world text and Text 3D are left to the editor, and were not looked at.
- Atlas depth stays fixed: distance-field pages are 8-bit, colour pages RGBA. A page's size is a project choice, 1024 or 2048, per kind.
- Localization: **Localize Font** does not preview in edit mode, so that a scene keeps its saved font. The table entry is picked in Unity's own control. A tracked colour goes through Unity's `Convert.ToString`, so on a machine whose culture writes a decimal comma a fraction reads as 0; whole numbers are safe. Text 3D has Localize and Localize Font, and no tracked object.
- On Direct3D 12 a Windows player can crash as it quits, inside Unity's `D3D12Core` with no Lettera frame on the stack, when a run adds shader states to the cache file left by an earlier run. Seen with the Math sample; never with a fresh cache or on Direct3D 11. The cause is not known. Nothing is lost, since it happens after the player has shut down.

## [0.7.3] - 2026-11-01

### Fixed
- **A long text on a Canvas could run the whole machine out of memory.** Past about 21 KB of Latin, a text gave one canvas renderer more than 65 000 vertices, and Unity's canvas batching then grew without end: past 10 GB within seconds at 100 KB, with other programs on the machine failing too. Present since 0.1.0. A long text now draws through several renderers. 100 KB and 1 MB both hold about 2 GB for the whole player, and 1 MB sets in 1.7 s on the development machine.

### Added
These are early parts of 1.0, carried along with the fix.
- **The text inspector is one page of cards,** with a row to jump between them and a filter for settings, in place of tabs. B, I, U and S wrap a selection, and with none turn a style on or off for the whole text.
- **Tools > Lettera > Commands** (Ctrl+Alt+L): everything Lettera makes or opens, by typing, in either keyboard layout.
- **Tools > Lettera:** Glyphs, Ranges, Atlases, Clipboard, Font Tools, and an overlay of glyph boxes and baselines in the Scene view (Ctrl+Shift+I).
- **Tools > Lettera > Migrate From TextMesh Pro.** Tested on written cases only, not yet on a real project. Input fields and dropdowns stay on TextMesh Pro for now, with their texts.

### Known
- With many parts, a text's alpha through `CrossFadeAlpha` reaches the first part only until its next rebuild. A part's motion rewrites its whole mesh.

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
