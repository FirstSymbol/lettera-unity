# Documents

A document is a text too long to lay out whole: a log, a book, a file of 200 000 lines. Lettera keeps its source in a **rope** and lays out only a **window** of it near what shows. Scrolling inside the window moves the glyphs without a rebuild; scrolling past it moves the window.

[Русская версия](Documents.ru.md) · Other guides: [Getting Started](GettingStarted.md), [Fields](Fields.md), [Motion](Motion.md), [Tools](Tools.md)

## Do you need one?

A plain `LetteraText` already copes with long text: it shapes the paragraphs near what shows first, finishes the rest over the next frames, and places glyphs only on the lines near the screen (see [Long text without a document](#long-text-without-a-document)). Take a document when:

- the text is megabytes, or comes from a file you would rather not read into a string;
- it has to scroll smoothly at any length, with a scrollbar;
- you want to search it, jump through it or edit it without holding it all in a `string`.

## Open a document

**GameObject > Lettera > Document** makes a Canvas text inside a scroller, ready to fill. From code:

```csharp
// A file: mapped where the platform allows, and decoded only as it is read.
text.OpenDocument(LetteraFileSource.Load(path), takeOwnership: true);

// Text you already hold.
text.OpenDocument(LetteraRope.FromString("First line\nSecond line"), takeOwnership: true);
```

- `takeOwnership: true` lets the text dispose of the rope when the document closes. `text.CloseDocument()` goes back to the text's own value.
- `text.Document` is the rope, `text.View` the window's position.
- A file may be UTF-8, UTF-16 or UTF-32, with or without a byte order mark; `LetteraFileOptions` sets the encoding and how the bytes are read (`Mapped`, `Chunked` or `InMemory`). The web reads the file into memory.
- Markup works as in any text. A tag opened on the first line stays in force thousands of lines later, wherever the window stands.
- Selection, ranges, links and motion work inside the window as in any text.

### From the inspector: Source Loader

A **Source Loader** on the text's object opens a document from a file, a `TextAsset` or a web address, when the object is enabled. A load that fails or is cancelled leaves the text as it was.

```csharp
var loader = text.gameObject.AddComponent<LetteraSourceLoader>();
loader.Source = new LetteraPathSource { Root = LetteraPathRoot.StreamingAssets, Path = "Books/whale.txt" };
loader.Loaded += () => Debug.Log("The book is open");
loader.Failed += error => Debug.LogWarning(error);
loader.Reload();
```

`LetteraTextAssetSource` reads a `TextAsset`, and `LetteraUrlTextSource` an address. `Progress` and `IsLoading` say how far it is; `Cancel()` stops it.

## Scroll

`LetteraDocumentScroller` is the viewport: put it on a `RectTransform` and let the text fill it. Neither grows to the document's size.

```csharp
var scroller = scrollerObject.AddComponent<LetteraDocumentScroller>();
scroller.Text = text;
scroller.StickToEnd = true;     // A log: stays at the end while lines arrive.
scroller.ScrollToEnd();
```

It takes a drag, the wheel, inertia and elastic ends, and drives a `Scrollbar` (`VerticalScrollbar`). `PassWheelAtEdges` hands the wheel to the scroll view around it once the document is at an end. It also works on a plain Canvas text that is not a document.

Jump from code through the view. Positions are characters of the source, markup included:

```csharp
text.View.ScrollToParagraph(1000);
text.View.ScrollToChar(52000, LetteraRevealAlign.Center);
text.View.PositionChanged += () => Debug.Log(text.View.DocumentY + " of " + text.View.MaxDocumentY);
```

Heights start as estimates and are measured in the background. A height that turns out different above the view moves nothing under it: the view is held to a character, not to a pixel. While the scrollbar's handle is dragged, `FreezeExtent` keeps what it reads still.

## Find text

`LetteraDocumentSearch` reads the document a slice at a time, so no frame waits for it. Run it in a coroutine:

```csharp
using System.Collections;

IEnumerator Find(string query)
{
    var search = new LetteraDocumentSearch(text.Document) { Query = query, Comparison = StringComparison.OrdinalIgnoreCase };
    var hits = new List<LetteraSearchHit>();
    search.Start();
    while (search.Step(2, hits)) yield return null;    // About 2 ms a frame.
    if (hits.Count > 0) text.View.ScrollToSpan(hits[0].Start, hits[0].End, LetteraRevealAlign.Center);
}
StartCoroutine(Find("whale"));
```

- `Scope = LetteraSearchScope.Shown` searches what a reader sees, so a word split by a tag is one word; `Source`, the default, searches the source as written.
- Hits are characters of the source. `Progress` says how far it has read; `Cancel()` stops it.
- `text.FindAll(query, hits, ignoreCase)` on a document reads the whole document through the same search, and reports characters of its source.

## Edit

A `LetteraEditable` on the same text edits the document: typing, deleting, the clipboard and undo, which goes back to where its edit was. The selection is held in characters of the source, so it stays while the window moves and follows edits. See [Fields](Fields.md) for the field itself.

## Text too small to read

A document drawn below 4 screen pixels to an em draws bars in place of its glyphs, as a minimap does. `text.View.BarsEnabled` turns that off, and `MinLegiblePixelsPerEm` moves the line. `IsShownAsBars` says which is showing.

## Thai, Lao, Khmer and Myanmar

These scripts write words without spaces, so lines break and double clicks select by a word list. Four lists ship with the package, built from ICU's: **Thai Words**, **Lao Words**, **Khmer Words** and **Myanmar Words**.

- Nothing is listed by default, so nothing is added to a build. List the ones you need in **Project Settings > Lettera > Defaults > Dictionaries**.
- Or register one from a script: `LetteraWordDictionaries.Register(dictionary)`.
- To make your own, **Assets > Create > Lettera > Word Dictionary**, then **Build From A Word List…** on its inspector: one word per line, `#` for comments.
- Word motions and `text.Units(LetteraTextUnit.Word)` count the same words, in any text, document or not.

## Settings

**Project Settings > Lettera > Documents**, read when a document opens:

| Setting | Default | What it does |
|---|---|---|
| Margin Viewports | 0.5 | How far past the viewport the window reaches, in viewports |
| Edge Viewports | 0.25 | The window moves when what shows comes this near its edge |
| Max Window Codepoints | 14 000 | The most a window holds, so that its mesh stays within what a Canvas can draw |
| Scan Budget Ms | 1 | Time a frame may spend finding open tags and measuring |
| Measure In Background | on | Measures heights between frames rather than on demand |

## Long text without a document

A plain text that is long, from about 4096 glyphs, also works only where it shows:

- It places and draws glyphs only on the lines within half a screen of what its root canvas and any `RectMask2D` let show. A scroll past them places the new lines before that frame draws.
- When it is set, only its first paragraphs and those near what shows are shaped and broken into lines. The rest stand at an estimated height and are finished over the next frames, about 2 ms a frame, in reading order.
- Until then its height, a layout's preferred height and so a scrollbar are estimates and may move. `IsFinishing` says so, and `FinishNow()` ends it.
- `Lines`, carets, selection, hit tests, links and measures finish everything first, so they are exact anywhere. `GlyphCount`, `GetGlyph` and `InkBounds` cover the placed lines.
- An edit of a plain text redoes only the paragraphs from the one it touched.

`LetteraLongText` sets all of it:

```csharp
LetteraLongText.FromGlyphs = 8192;                 // Where placing only what shows starts.
LetteraLongText.FinishMillisecondsPerFrame = 1;    // A smaller share of each frame for finishing.
LetteraLongText.FinishLater = false;               // Shape and break everything at once.
```

## Limits

- An edit of a selection that reaches past what the window holds is refused whole, with one warning, so nothing is half deleted: select all and delete does not work on a document yet. Copying such a selection works.
- A rule of a field that needs the whole value reads the window. `Source` on a document returns the window's source, and `Length` and `PlainText` answer for the window.
- `AutoSize` shows a document at the smallest size, and eliding is ignored.
- A world text's window follows `View`, and nothing clips it: there is no camera viewport for world text. A windowed text inside a `ScrollRect` is not refused, though it should be. List numbers do not carry across a window's edge. Rounded ends of ranges cut by a window are not drawn.
- Every keystroke in a document parses the window again. One long paragraph with a keystroke reshapes all of it.
- A Chinese and Japanese word list is not shipped. A project can build its own. The shipped lists were checked against a few sentences per script, not against ICU's results on a corpus.
- Equation numbers in a document count within the window, so the same equation can show another number after a scroll. A `longtable` is one object and does not split across windows. A text's walk by units counts the window.
- A lean window, which a fling builds when it outruns the window's margins, is placed by the document's estimates: the line at the top is where the heights of blocks not yet measured put it. The scrollbar's size refines when scrolling stops.
- A long text's glyphs are not all there until it is finished: `GlyphCount`, `GetGlyph`, `InkBounds` and the mesh cover the placed lines only. Under a stencil `Mask` the band is the whole canvas, and a world canvas with no `RectMask2D` places everything. A text with a curve, a marquee, a reveal, an inline object or a stage hook places everything.
- Only a top-aligned long text with no layout effect, no auto size and no eliding leaves paragraphs for later. A text waiting only for paragraphs that do not show costs about 2 ms a frame until done.
