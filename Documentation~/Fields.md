# Fields

[Русская версия](Fields.ru.md) · [All guides](Lettera.md)

Any Lettera text can be selected, and any text can be a field. There is no separate input component that draws its own text: a field is an ordinary `LetteraText` or `LetteraWorldText` with two components beside it, so everything the text can do (fonts, styles, motion, formulas, documents) works while it is edited.

- [Selection](#selection)
- [Ready-made fields](#ready-made-fields)
- [The value](#the-value)
- [Editing from code](#editing-from-code)
- [Rules](#rules)
- [Your own rule](#your-own-rule)
- [Formatting](#formatting)
- [Commands, menus and toolbars](#commands-menus-and-toolbars)
- [The clipboard](#the-clipboard)
- [Composition and input methods](#composition-and-input-methods)
- [Touch](#touch)
- [The platform's own controls](#the-platforms-own-controls)
- [Saving a field's state](#saving-a-fields-state)
- [Limits](#limits)

## Selection

`LetteraSelectable` makes a text selectable and copyable:

- a click places the caret, a double click selects a word, a triple click a paragraph, and a drag extends by the unit it started with;
- Shift extends; the keys follow both PC and Apple conventions;
- Ctrl+C or Cmd+C copies, and a right click opens a menu.

A selection change rebuilds nothing. Indices are codepoints of the shown text, and a selection has an `Anchor`, a `Head` and a `Bias` for the two places an index can stand at a soft wrap or a change of direction.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsSelectable : MonoBehaviour
{
    void Start()
    {
        var selectable = gameObject.AddComponent<LetteraSelectable>();
        selectable.HighlightColor = new Color(1f, 0.8f, 0.2f, 0.45f);
        selectable.InactiveHighlight = LetteraInactiveHighlight.Dim;

        selectable.SelectionChanged += change =>
            Debug.Log("Selected: " + selectable.SelectedPlainText());

        // Never let a selection start inside the first word.
        selectable.AddSelectionFilter((ref LetteraSelectionProposal proposal) =>
        {
            if (proposal.Wanted.Start < 5)
                proposal.Wanted = new LetteraSelectionRange(5, Mathf.Max(5, proposal.Wanted.End));
        });
    }
}
```

`Select`, `SelectAll`, `SelectWordAt`, `PlaceCaret` and `Copy` do the same from code. `SelectionOnScreen`, `CaretOnScreen`, `HandlesOnScreen` and `LineOnScreen` give screen rectangles for your own popups.

**Where presses come from.** Every text needs an `EventSystem` with an input module; the GameObject menu adds one. A world text takes presses only through a `LetteraWorldRaycaster` on its camera. Under a Canvas Group that is not interactable, a selectable text takes no press at all: no caret, no selection, no menu, and a drag goes on to the scroll view around it. `IsInteractable` says whether a press would reach the text now.

## Ready-made fields

**GameObject > Lettera** makes each of these, with its frame, hint and caret:

| Menu | What it is |
|---|---|
| Input Field | One line, Enter submits. |
| Text Area | Several lines in a scrolling viewport. |
| Password Field | One line, masked, with a show/hide toggle. |
| Dropdown | `LetteraDropdown`: options that may hold markup, and a list to pick from. |
| Selectable Text, Editable Text | A bare text with the components, no frame. |
| World Selectable Text | Selectable world text; a `LetteraWorldRaycaster` goes on the main camera if it has none. |

`LetteraFieldFactory` builds the same from code: `CreateInputField`, `CreateTextArea`, `CreatePasswordField`, `CreateSelectableText`, `CreateEditableText` and `CreateDropdown`. Each returns the root object; the field is on the text inside. **Project Settings > Lettera > Selection & Editing** takes prefabs of your own for each kind.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsSignUp : MonoBehaviour
{
    public RectTransform Column;
    public LetteraText Status;

    void Start()
    {
        GameObject nameRoot = LetteraFieldFactory.CreateInputField(Column, "Your name");
        LetteraEditable name = nameRoot.GetComponentInChildren<LetteraEditable>();
        name.AddRule(new PersonNameCharFilter());
        name.AddRule(new MaxLengthRule { Limit = 40 });

        LetteraFieldFactory.CreatePasswordField(Column);

        GameObject countryRoot = LetteraFieldFactory.CreateDropdown(Column);
        var country = countryRoot.GetComponent<LetteraDropdown>();
        country.AddOptions(new[] { "Deutschland", "Россия", "<b>日本</b>" });
        country.OnValueChanged.AddListener(index => Status.Text = "Picked option " + index);
    }
}
```

## The value

A field's value is its **source text, markup included**:

| Member | What it is |
|---|---|
| `Source` | The value with markup: `Hello <b>you</b>`. Saved with the scene. |
| `PlainText` | The value without markup: `Hello you`. |
| `ShownText` | What shows, which differs from the value under a password mask or a pattern. |
| `Length`, `CopyTo`, `Equals`, `CodepointAt` | Read the value without making a string. |
| `Version` | Counts changes, for caches of your own. |

`MarkupView` decides how markup shows while typing: `Hidden` (the default: you see bold, not `<b>`), `RevealAtCaret` (the tags around the caret show) or `Source`. `TypedMarkup` decides whether a typed `<` starts a tag or is a literal character, and `PlainPaste` the same for pasted plain text.

**Events:** `ValueChanged` after any change, `Patched` with exactly what changed and why, `Submitted` and `Cancelled`, `FocusGained` and `FocusLost` with a reason, `EditRejected` when a rule refused something, `CheckChanged` when validation reports, `ComposingChanged` while an input method composes.

## Editing from code

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsFromCode : MonoBehaviour
{
    public LetteraEditable Field;

    void Start()
    {
        Field.SetSource("Dear <b>reader</b>,");      // Replaces everything; clears the history.
        Field.Selectable.PlaceCaret(Field.Length);
        Field.InsertText(" welcome.");               // Through the rules, like typing.

        using (Field.BeginHistoryGroup())            // One undo step for both.
        {
            Field.Replace(0, 4, "Hello");
            Field.InsertText("!");
        }

        Field.Submitted += value => Debug.Log("Sent " + Field.PlainText);
        Field.ValueChanged += value => Debug.Log(Field.Length + " characters");
    }
}
```

`InsertText` and `Replace` go through the rules, as typing does; `SetSource` does not. `Undo`, `Redo`, `CanUndo` and `ClearHistory` work on the field's own history, which merges typing by word and by time. `ReadOnly` keeps the value but allows selection and copy; `Interactable` off takes the keyboard away.

## Rules

Rules hold a field's policy: what it lets in, what keys do, and what it shows around itself. They run in list order, and each sees what the one before it left. Add them in the inspector's **Rules** card, which offers them by group, or with `AddRule`:

| Group | Rules |
|---|---|
| Input | `SingleLineRule`, `PasswordRule`, `MaxLengthRule` (counted in graphemes, UTF-16, UTF-8 or codepoints, of the source or the plain text), `PatternMaskRule` (`(###) ###-####`), `LetterCaseRule` |
| Filters | `AlphaNumericCharFilter`, `WholeNumberCharFilter`, `DecimalCharFilter`, `EmailCharFilter`, `PersonNameCharFilter` |
| Validation | `ValidationRule` with checks of your own, run on change, on leaving, on submit or always |
| Decoration | `HintDecorator`, `RaisedLabelDecorator`, `HelperTextDecorator`, `LengthCounterDecorator` |
| Formatting | `FormattingRule` (shortcuts that write tags), `CaretWatchRule` |
| Paste | `PastePlainRule`, `PasteLinkRule` |
| Keyboard | `EnterKeyRule`, `TabKeyRule`, `SelectAllOnFocusRule`, `ReleaseOnCancelRule`, `KeyboardTraitsRule`, `KeyboardLiftRule` |

Three kinds of rule do different jobs:

- A **filter** judges what an edit would leave behind, and refuses it or keeps what is legal. Deleting is always free, and a composition is judged only when it commits.
- A **validator** only reports: nothing is ever refused because of a check. `ValidationRule.BlockSubmitWhenInvalid` can hold a submit back.
- A **decorator** shows the field's state on UI you assign: a hint, a floating label, a line under the field, a counter. A decorator costs nothing per frame unless it animates.

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

[Serializable]
public class FieldsHasDomain : LetteraValueCheck
{
    public override LetteraCheckResult Check(ILetteraTextView value)
    {
        string text = value.PlainText;
        return text.Contains("@") && text.Contains(".")
            ? new LetteraCheckResult(null)
            : new LetteraCheckResult(LetteraCheckResult.Invalid, "That does not look like an address");
    }
}

public class FieldsEmail : MonoBehaviour
{
    public LetteraEditable Field;
    public LetteraText Helper, Counter;

    void Start()
    {
        Field.AddRule(new SingleLineRule());
        Field.AddRule(new EmailCharFilter());
        Field.AddRule(new MaxLengthRule { Limit = 64, CountIn = LetteraCountSpace.Plain });
        Field.AddRule(new KeyboardTraitsRule { Traits = new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Email, AutofillHint = LetteraAutofill.Email } });

        var validation = new ValidationRule { When = LetteraCheckTime.OnBlur, BlockSubmitWhenInvalid = true };
        validation.Checks.Add(new FieldsHasDomain());
        Field.AddRule(validation);

        Field.AddRule(new HelperTextDecorator { Target = Helper, HelperText = "We send the code here" });
        Field.AddRule(new LengthCounterDecorator { Target = Counter });
    }
}
```

A **field preset** (`LetteraFieldPreset`, **Assets > Create > Lettera > Field Preset**) holds a list of rules many fields share; `AddPreset` adds one. `GetRule<T>()` finds a rule on a field.

The inspector's **Try It** button types into a real field built for the trial, so you see what the rules make of an input before you press Play.

## Your own rule

Derive from `LetteraFieldRule` and override what you need: `FilterEdit` sees every edit on its way in, `LookupKey` can change what a key does, `RequestKeyboard` sets a soft keyboard's traits, `OnSnapshot` hears of state changes.

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

// No digits, and a refused edit says why.
[Serializable]
public class FieldsNoDigitsRule : LetteraFieldRule
{
    public override void FilterEdit(ref LetteraEditProposal edit)
    {
        if (edit.IsPureDeletion) return;
        foreach (char c in edit.Text)
        {
            if (!char.IsDigit(c)) continue;
            edit.Reject();
            return;
        }
    }
}

public class FieldsUseNoDigits : MonoBehaviour
{
    void Start()
    {
        var field = GetComponent<LetteraEditable>();
        field.AddRule(new FieldsNoDigitsRule());
        field.EditRejected += reason => Debug.Log("No digits here");
    }
}
```

A proposal's `Start`, `RemovedLength` and `Text` can also be changed, to rewrite an edit rather than refuse it. A rule holding a list or another nested object overrides `Clone`.

## Formatting

A field writes formatting in its own syntax, through the styles it already has:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsToolbar : MonoBehaviour
{
    public LetteraEditable Field;

    void Start() => Field.AddRule(new FormattingRule());   // Ctrl or Cmd with B, I and U.

    public void Bold() => Field.ToggleEffect<BoldEffect>();
    public void Red() => Field.ApplyEffect<ColorEffect>("#C0392B");
    public void Plain() => Field.ClearStyles();
    public bool IsBold => Field.IsEffectActive<BoldEffect>();
}
```

`ApplyEffect`, `RemoveEffect`, `ToggleEffect` and `ClearStyles` write the tag that a style of this text binds to that effect. **They never add a style:** a field whose styles have nothing for an effect cannot write it, and `CanFormat` says so. With nothing selected, a command sets a pending style that the next typed characters take. `CaretStylesChanged` reports what covers the caret, for a toolbar whose buttons light up; the Rich Editor sample does exactly this.

Pasted HTML from browsers and word processors keeps its bold, italics, colours, highlights and links when the field has styles for them.

## Commands, menus and toolbars

Everything a user can run on a text, from a menu, a button or a key, is a `LetteraEditCommand`: `CutCommand`, `CopyCommand`, `PasteCommand`, `PastePlainCommand`, `SelectAllCommand`, `UndoCommand`, `RedoCommand`, `ToggleEffectCommand`, `ClearStylesCommand`, `WebLookupCommand`, `ShareTextCommand` and `PlatformActionsCommand`.

- The context menu lists the project's commands from **Project Settings > Lettera > Selection & Editing > Commands**. `LetteraContextMenu` is the shipped menu, built from UI objects you own; a prefab in **Menu Prefab** replaces it.
- `LetteraCommandControl` binds a `Button` or `Toggle` to a command for a toolbar. With no target it acts on the field that has the keyboard.
- `LetteraKeepFocus` on a toolbar button keeps a press there from ending the edit.

A command of your own:

```csharp
using System;
using FirstSymbol.Lettera;

[Serializable]
public class FieldsShoutCommand : LetteraEditCommand
{
    public override string FallbackTitle => "Shout";

    public override bool CanRun(in LetteraCommandContext context) => context.CanEdit && context.HasSelection;

    public override void Execute(in LetteraCommandContext context) =>
        context.Editable.InsertText(context.Selectable.SelectedPlainText().ToUpperInvariant());
}
```

Add it to the project's list (`LetteraSettings.Instance.Editing.Commands`) or bind it to a control. `LetteraEditCommand.TitleSource` can supply every command's title from elsewhere, such as a localization table.

## The clipboard

One copy writes several formats at once: plain text, HTML, Markdown and Lettera's own format. A paste takes the richest format the field can use and writes it in the field's own syntax, so styled text keeps its styles between Lettera fields and to and from other applications where the platform allows. A password field keeps its value off the clipboard unless its rule's `CopyAllowed` says otherwise.

- On Windows and the web, the clipboard with all its formats is on by default. On Android, iOS and macOS it waits for its gate; until then only plain text leaves the app, and richer formats stay within it.
- In a browser the clipboard can only be read later. `Paste()` there returns false with a warning; use `PasteAsync()`, or `PasteCommand`, which picks the right way on each platform:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsPasteButton : MonoBehaviour
{
    public LetteraEditable Field;

    public void PasteHere() => new PasteCommand().Execute(new LetteraCommandContext(Field.Selectable));
}
```

- `LetteraClipboard.Backend` is replaceable: implement `ILetteraClipboardBackend` for a platform of your own or a test.
- **Tools > Lettera > Clipboard** shows every format the last copy wrote.

## Composition and input methods

Chinese, Japanese and Korean input methods compose text before it commits. A field shows the composition in place, with its clauses underlined (the look is in **Selection & Editing > Ime Look**), keeps it out of the value until it commits, and makes one undo step of it. Rules judge only the committed text.

- **Windows players** read the input method's own messages: several clauses, the caret inside them, the candidate window beside the caret.
- **The web** runs a session through one hidden text box, with composition shown inline in the field.
- **Elsewhere** composition goes through Unity's own input path.

All input reaches a field through `ILetteraInputSource`; `LetteraInput.Register` puts a source of your own ahead of the shipped ones.

## Touch

On a touch screen a long press selects a word, a double tap selects, handles drag the ends and keep their grab, a swipe that starts on a text goes on to the `ScrollRect` around it, and a loupe magnifies the text under the finger. Fields raise the soft keyboard; `KeyboardTraitsRule` asks for its kind, return key, autofill and capitalization, and `KeyboardLiftRule` moves a UI object up so the caret clears the keyboard.

## The platform's own controls

A field can run through the platform's own text control, clipboard and menu. Whatever the control produces, by keys, voice, autofill or paste, still goes through the field's rules, and a refused edit puts the control right.

Each piece has a **gate** in **Project Settings > Lettera > Native Platform**: `Auto`, `On` or `Off`, with a line saying why it is on or off for the platform you are building. **What has not been seen to run on a real device ships off**:

| | Windows | Web | Android, iOS | macOS |
|---|---|---|---|---|
| Text input | composition through the input method | one hidden text box, inline composition | off until turned on | Unity's |
| Clipboard with formats | on | on | off until turned on | off until turned on |
| Replica (`ReplicaFieldRule`) | does nothing | on, when a field asks | off until turned on | does nothing |

The menu and loupe on phones are a project choice: **Android Menu**, **iOS Menu** and **Loupe** pick Lettera's own or the system's, and the system's needs its gate on.

`ReplicaFieldRule` puts the platform's own control in a field's place while it is edited, through the same session and the same rules: on the web a plain element, or a surface a page registers on `window.LetteraSurfaces`. Where there is no replica it does nothing, so one prefab fits every target.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsGates : MonoBehaviour
{
    void Start()
    {
        foreach (LetteraNativeFeature feature in System.Enum.GetValues(typeof(LetteraNativeFeature)))
            Debug.Log(feature + ": " + (LetteraPlatform.IsActive(feature) ? "on" : "off") + ", " + LetteraPlatform.Why(feature));
    }
}
```

The **Device Check** sample walks through a phone one screen at a time and gives one text block to send back. **Lettera > Device Check > Build for...** turns the routes on for that build only. See [Platforms](Platforms.md) for what has run where.

## Saving a field's state

`CaptureState` returns the source, the selection and the scroll as plain, serializable data; `RestoreState` puts them back. That is enough to keep a draft across an app that the system closes in the background:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsKeepDraft : MonoBehaviour
{
    const string Key = "chat.draft";
    public LetteraEditable Field;

    void Start()
    {
        string saved = PlayerPrefs.GetString(Key, "");
        if (saved.Length > 0) Field.RestoreState(JsonUtility.FromJson<LetteraEditable.State>(saved));
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(Field.CaptureState()));
        PlayerPrefs.Save();
    }
}
```

A composition in progress is committed before the state is taken.

## Limits

- **The native routes have not been seen on real devices.** 0.6.0 recorded that nothing on a phone had run: whether Safari on a real iPhone raises a keyboard, its copy and paste, all of Android and iOS, the macOS clipboard, and real Japanese and Korean input anywhere. Windows' input method was driven with a fake reader and the web through CDP. Hence the gates; [Platforms](Platforms.md) keeps what has run since.
- macOS has no native text input. Android carries images on the clipboard in neither direction yet.
- A selectable text has no menu or loupe of its own per platform: Android Menu, iOS Menu and Loupe are project-wide.
- `LetteraPasteButton` and iOS's paste control are not there: paste goes through a menu or a key. A Lettera menu has no Share on the web.
- The selection highlight is a solid colour with square corners. The caret stays upright on italic text, and on curved text the caret and rectangles do not follow the glyph's angle.
- The I-beam cursor is not set by Lettera: `WantsIBeam` tells where it belongs. A menu open during a change of language keeps its titles until it opens again. A touch menu does not hide while the text moves.
- The mesh loupe shows the text and its plate, not the scene behind it, and only on a Canvas.
- No text drag and drop, visual caret movement, several carets, or find and spelling UI.
- In a [document](Documents.md), an edit of a selection that reaches past the window is refused whole, with a warning, so select all and delete does not work there yet; `Source`, `Length` and `PlainText` answer for the window.
- A field's `Text` in Play mode is made when it is read: a script that reads it every frame gets a new string every frame after an edit.
