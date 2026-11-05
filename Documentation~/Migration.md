# From TextMesh Pro

**Tools > Lettera > Migrate From TextMesh Pro** moves a project's texts, fonts and scripts from TextMesh Pro to Lettera. It scans when asked, changes only what a button says, deletes nothing, and leaves TextMesh Pro installed.

[Русская версия](Migration.ru.md) · Other guides: [Getting Started](GettingStarted.md), [Fonts](Fonts.md), [Styles](Styles.md), [Fields](Fields.md), [Tools](Tools.md)

## Before you start

1. **Commit everything.** Version control is the real undo: the tool rewrites scene, prefab and script files on disk.
2. **Set Asset Serialization to Force Text** (**Project Settings > Editor**). The tool reads scenes and prefabs as text, and says so with a button to the setting until it is set.
3. **Save your open scenes.** A scene open with unsaved changes is asked about before stage 2: Save, Don't Save or Cancel, and Cancel stops the stage.

## The order

The window has one button per stage. Press them in order; each one scans first if nothing has been scanned yet.

1. **Scan** reads every scene, prefab, script, assembly definition and TextMesh Pro font asset under Assets, and changes nothing. It skips `Library`, `Temp`, `Logs`, `obj`, folders whose names start with a dot or end in `~`, and Lettera's own folder. TextMesh Pro's own folder is read only for its fonts. A file it cannot read is a finding, marked **Unreadable**, not silence.
2. **1. Create Fonts.** Each TextMesh Pro font asset gets a Lettera font made from its source font file, saved beside that file as `<name> Lettera.asset` (or in `Assets/Lettera/Fonts` when the file lives in a package). An asset whose source file cannot be found is marked **Needs You**, and the texts that use it take the project's default font.
3. **2. Convert Scenes And Prefabs.** Each `TextMeshProUGUI` becomes a `LetteraText` and each `TextMeshPro` a `LetteraWorldText`, **in place**: the component keeps its identity in the file, so every field, event target and prefab override that pointed at it still does. TextMesh Pro's sub-mesh helpers are removed. Open scenes that changed are opened again.
4. **3. Rewrite Scripts.** Assembly definitions first: those that reference `Unity.TextMeshPro` gain `FirstSymbol.Lettera`. Then each C# file that uses TextMesh Pro is rewritten, with the original kept in `Library/Lettera/ScriptBackups`, outside the Project window.
5. **Copy The Report** puts everything found, done and not carried on the clipboard as Markdown, for a review or an issue.

Then let Unity compile, and work through what the report lists under **Not carried**.

## What converts

### Components

| TextMesh Pro | Lettera |
|---|---|
| Text | `Text`, with its markup converted (below) |
| Font asset | The Lettera font made in stage 1 |
| Font size, auto size, min and max | `FontSize`, `AutoSize`, `MinFontSize`, `MaxFontSize` |
| Vertex colour | The component's colour |
| Horizontal alignment Left, Center, Right | `Start`, `Center`, `End`, which follow the paragraph's direction |
| Geometry | `Center` |
| Justified and Flush | `Justify` |
| Vertical alignment Top, Middle, Bottom | the same |
| Overflow, Ellipsis, Truncate | `Visible`, `Ellipsis`, `Truncate` |
| Word wrapping | `WordWrap` |
| Margins | `Padding`, in Lettera's order |
| Right to left | `BaseDirection` right to left |
| Rich text off | `UseGlobalStylePreset` off, so tags show as written |
| Font style bold, italic, underline, strikethrough, lowercase, uppercase, small caps | The text wrapped in `<b>`, `<i>`, `<u>`, `<s>`, `<lowercase>`, `<uppercase>`, `<smallcaps>` |
| Raycast target and padding, maskable | the same |
| World text | `LetteraWorldText` with **Standalone** on, so it sorts against other transparent objects as before |
| `set_text`, `set_fontSize` in a UnityEvent | `set_Text`, `set_FontSize` |
| A prefab instance's override of text, size, auto size, colour or margins | The same override on Lettera's property |

### Markup

Lettera's default vocabulary already reads `b i u s size color alpha font align indent line-height nobr br noparse mark sub sup smallcaps lowercase uppercase sprite`, so those stay as written. The rest:

| TextMesh Pro | Becomes |
|---|---|
| `<sprite name="x">`, `<sprite index=3>`, `<sprite="asset" ...>` | `<sprite=x>`, `<sprite=3>`; an inline library you make supplies the pictures |
| `<link="id">` | `<link=id>`, which marks a range for your code, as before. It opens nothing on its own |
| `<page>` | Removed |
| `<quad>`, `<material>` | Left as literal text inside `<noparse>`: they need a choice only you can make |
| `<alpha>` | Kept. Note that here it holds until its own closing tag, where TextMesh Pro held it until the next colour tag |

### Scripts

A member is renamed only on a variable this file shows to hold a TextMesh Pro type: a declaration, `var` with a visible initializer, `GetComponent<T>`, a cast, `as`, `new`, an array or list element, or a `foreach` variable. Strings, comments and preprocessor lines are never touched.

| TextMesh Pro | Lettera |
|---|---|
| `TextMeshProUGUI`, `TMP_Text` | `LetteraText` |
| `TextMeshPro` | `LetteraWorldText` |
| `TMP_FontAsset` | `LetteraFontAsset` |
| `TMP_InputField`, `TMP_Dropdown` | `LetteraEditable`, `LetteraDropdown` |
| `using TMPro;` | `using FirstSymbol.Lettera;`, and both while a TextMesh Pro type remains |
| `.text`, `.fontSize`, `.fontSizeMin`, `.fontSizeMax`, `.enableAutoSizing`, `.font` | `.Text`, `.FontSize`, `.MinFontSize`, `.MaxFontSize`, `.AutoSize`, `.Font` |
| `.enableWordWrapping`, `.overflowMode` | `.WordWrap`, `.Overflow` |
| `.ForceMeshUpdate()` | `.ForceRebuild()` |
| `.bounds`, `.textBounds` | `.InkBounds` |
| `.alignment = TextAlignmentOptions.TopLeft;` | Two assignments, `HorizontalAlignment` and `VerticalAlignment` |
| `.onValueChanged.AddListener(h)`, `RemoveListener(h)` | `.ValueChanged += h`, `-= h` |

What it reports instead, with what to use:

| TextMesh Pro | Use |
|---|---|
| `preferredWidth`, `preferredHeight`, `GetPreferredValues` | `GetMaxContentWidth()`, `GetHeightForWidth(width)`, `MeasureText(...)` |
| `textInfo` | `Lines` and `GetGlyph` |
| `maxVisibleCharacters` | A `RevealEffect` |
| `fontStyle`, `fontWeight`, `characterSpacing` | A whole-text style, `<b=weight>`, `<tracking>` |
| `richText` | `UseGlobalStylePreset` |
| `margin` | `Padding`, whose order is left, bottom, right, top |
| `isRightToLeftText`, `textWrappingMode` | `BaseDirection`, `WordWrap` |
| `onEndEdit`, `onSelect`, `onDeselect` | `Submitted`, `FocusGained`, `FocusLost` on the field |
| Any other `TMP_` type | Stays, for you to change |

A converted script reads like this:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class Scoreboard : MonoBehaviour
{
    public LetteraText score;           // Was TextMeshProUGUI: the scene's reference still holds.
    public LetteraEditable playerName;  // Was TMP_InputField.

    void Start()
    {
        score.Text = "0";
        score.FontSize = 48;
        score.HorizontalAlignment = LetteraHorizontalAlignment.End;    // Was alignment = TextAlignmentOptions.Right.
        score.VerticalAlignment = LetteraVerticalAlignment.Middle;
        playerName.ValueChanged += value => Debug.Log(value);         // Was onValueChanged.AddListener.
    }
}
```

## What does not convert

Each of these is a line under **Not carried**, naming the file and what to do:

- **Input fields and dropdowns** stay on TextMesh Pro, with the texts inside them. Rebuild them with **GameObject > Lettera > Input Field** or **Dropdown**; see [Fields](Fields.md).
- **A material's look:** outline, underlay and glow. Give the text a stroke, shadow or glow style instead; see [Styles](Styles.md).
- Character spacing and line spacing (use `<tracking>` and `<line-height>`), a vertex gradient (use `<fill>`), and the Highlight font style, which keeps no colour.
- Baseline, Geometry and Capline alignment become Middle; Masking, Scroll Rect, Page and Linked overflow become Visible; preserved whitespace is not kept.
- A prefab instance's override of alignment, font style, wrapping, overflow, font, material or spacing: set it again on the instance.
- A sprite's tint and animation.

Not converted at all: font fallback tables, `TMP_Settings`, sprite assets, style sheets and colour gradient assets. Build a font's fallbacks as a font stack and pictures as an inline library (see [Fonts](Fonts.md)), and a style sheet's styles as styles of your own (see [Styles](Styles.md)).

## Troubleshooting

- **Scan does nothing.** Asset Serialization is not Force Text; the window says so, with a button to the setting. A scene or prefab still saved in binary is listed as **Unreadable**.
- **Texts lost their font.** Stage 2 ran before stage 1, or a font was marked **Needs You**. Run **1. Create Fonts**, then run stage 2 again on a fresh checkout of those files, or set the font on the texts.
- **A script still fails to compile.** The report lists each line left for you, with what to use. Most are `textInfo` and material code.
- **A field in a script lost its reference to an input field.** The script's `TMP_InputField` became `LetteraEditable`, while the input field in the scene is still TextMesh Pro's. Rebuild the field with Lettera and assign it, or restore that script from `Library/Lettera/ScriptBackups` until you do.
- **Something looks different.** Compare the converted scene with the old one from version control. Alignment now follows the paragraph's direction, so a right-to-left text aligned Left now starts at the right.
- **The window lost its findings.** It holds them while it is open; closing it or a script reload starts afresh. Scan again: converted scenes and prefabs are found no more. TextMesh Pro's font assets always are, since nothing is deleted.

## When you are done

When Scan finds nothing left but TextMesh Pro's fonts and what you chose to keep, TextMesh Pro can be removed from the project. The Lettera fonts of stage 1 stay where they are, and the backups in `Library/Lettera/ScriptBackups` can be deleted.

## From the command line

With `com.unity.pipeline` installed, `unity command lettera migrate_scan`, `migrate_status`, `migrate_fonts`, `migrate_components`, `migrate_scripts --preview` and `migrate_report --output <file>` run the same stages. They refuse on an open scene with unsaved changes rather than ask. See the [Agent guide](AgentGuide.md).

## Limits

- The tool has been run on TextMesh Pro's own *Examples & Extras*: every text swapped, and scenes and prefabs opened with no new missing scripts. Scripts there kept many compile errors, nearly all `textInfo` and material code, which the report names. It has not yet been run on a real project, nor have converted scenes been compared picture for picture.
- The scan reads the whole project each time.
