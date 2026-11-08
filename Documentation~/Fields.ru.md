# Поля

[English version](Fields.md) · [Все руководства](Lettera.ru.md)

Любой текст Lettera можно выделять, и любой текст может быть полем. Отдельного компонента ввода, который рисует свой собственный текст, нет: поле — это обычный `LetteraText` или `LetteraWorldText` с двумя компонентами рядом, поэтому всё, что умеет текст (шрифты, стили, движение, формулы, документы), работает и во время редактирования.

- [Выделение](#выделение)
- [Готовые поля](#готовые-поля)
- [Значение](#значение)
- [Правка из кода](#правка-из-кода)
- [Правила](#правила)
- [Своё правило](#своё-правило)
- [Форматирование](#форматирование)
- [Команды, меню и панели](#команды-меню-и-панели)
- [Буфер обмена](#буфер-обмена)
- [Композиция и методы ввода](#композиция-и-методы-ввода)
- [Касания](#касания)
- [Собственные элементы платформы](#собственные-элементы-платформы)
- [Сохранение состояния поля](#сохранение-состояния-поля)
- [Ограничения](#ограничения)

## Выделение

`LetteraSelectable` делает текст выделяемым и копируемым:

- щелчок ставит каретку, двойной щелчок выделяет слово, тройной — абзац, а перетаскивание расширяет выделение теми же единицами, с которых началось;
- Shift расширяет выделение; клавиши следуют и соглашениям ПК, и соглашениям Apple;
- Ctrl+C или Cmd+C копирует, правый щелчок открывает меню.

Изменение выделения ничего не перестраивает. Индексы — это кодовые точки показанного текста, а у выделения есть `Anchor`, `Head` и `Bias` — для двух мест, где может стоять индекс на мягком переносе или на смене направления.

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

        // Keep the first five characters out of every selection.
        selectable.AddSelectionFilter((ref LetteraSelectionProposal proposal) =>
        {
            if (proposal.Wanted.Start < 5)
                proposal.Wanted = new LetteraSelectionRange(5, Mathf.Max(5, proposal.Wanted.End));
        });
    }
}
```

`Select`, `SelectAll`, `SelectWordAt`, `PlaceCaret` и `Copy` делают то же из кода. `SelectionOnScreen`, `CaretOnScreen`, `HandlesOnScreen` и `LineOnScreen` дают прямоугольники на экране для собственных всплывающих окон.

**Откуда берутся нажатия.** Любому тексту нужен `EventSystem` с модулем ввода; меню GameObject добавляет его само. Текст в мире получает нажатия только через `LetteraWorldRaycaster` на своей камере. Под Canvas Group с выключенным interactable выделяемый текст не принимает нажатий вовсе: ни каретки, ни выделения, ни меню, а перетаскивание уходит в окружающий scroll view. `IsInteractable` говорит, дойдёт ли нажатие до текста сейчас.

## Готовые поля

**GameObject > Lettera** создаёт каждое из них с рамкой, подсказкой и кареткой:

| Меню | Что это |
|---|---|
| Input Field | Одна строка, Enter отправляет. |
| Text Area | Несколько строк в прокручиваемой области. |
| Password Field | Одна строка, скрытая маской, с переключателем показа. |
| Dropdown | `LetteraDropdown`: пункты, в которых может быть разметка, и список для выбора. |
| Selectable Text, Editable Text | Голый текст с компонентами, без рамки. |
| World Selectable Text | Выделяемый текст в мире; если на главной камере нет `LetteraWorldRaycaster`, он добавляется. |

`LetteraFieldFactory` строит то же из кода: `CreateInputField`, `CreateTextArea`, `CreatePasswordField`, `CreateSelectableText`, `CreateEditableText` и `CreateDropdown`. Каждый метод возвращает корневой объект; поле висит на тексте внутри. **Project Settings > Lettera > Selection & Editing** принимает для каждого вида ваши собственные префабы.

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

## Значение

Значение поля — его **исходный текст вместе с разметкой**:

| Член | Что это |
|---|---|
| `Source` | Значение с разметкой: `Hello <b>you</b>`. Сохраняется со сценой. |
| `PlainText` | Значение без разметки: `Hello you`. |
| `ShownText` | То, что видно; под маской пароля или шаблоном отличается от значения. |
| `Length`, `CopyTo`, `Equals`, `CodepointAt` | Читают значение, не создавая строки. |
| `Version` | Счётчик изменений, для собственных кешей. |

`MarkupView` решает, как разметка выглядит при наборе: `Hidden` (по умолчанию: вы видите жирный текст, а не `<b>`), `RevealAtCaret` (теги вокруг каретки видны) или `Source`. `TypedMarkup` решает, начинает ли набранный `<` тег или это просто символ, а `PlainPaste` — то же для вставленного простого текста.

**События:** `ValueChanged` после любого изменения, `Patched` — что именно изменилось и почему, `Submitted` и `Cancelled`, `FocusGained` и `FocusLost` с причиной, `EditRejected`, когда правило что-то отклонило, `CheckChanged`, когда проверка сообщает результат, `ComposingChanged`, пока метод ввода составляет текст.

## Правка из кода

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

`InsertText` и `Replace` проходят через правила, как набор с клавиатуры; `SetSource` — нет. `Undo`, `Redo`, `CanUndo` и `ClearHistory` работают с собственной историей поля, которая склеивает набор по словам и по времени. `ReadOnly` сохраняет значение, но разрешает выделять и копировать; выключенный `Interactable` забирает клавиатуру.

## Правила

Правила хранят политику поля: что в него пускать, что делают клавиши и что поле показывает вокруг себя. Они выполняются по порядку списка, и каждое видит то, что оставило предыдущее. Добавляйте их в карточке **Rules** инспектора, где они разложены по группам, или через `AddRule`:

| Группа | Правила |
|---|---|
| Ввод | `SingleLineRule`, `PasswordRule`, `MaxLengthRule` (счёт в графемах, UTF-16, UTF-8 или кодовых точках, по исходному или простому тексту), `PatternMaskRule` (`(###) ###-####`), `LetterCaseRule` |
| Фильтры | `AlphaNumericCharFilter`, `WholeNumberCharFilter`, `DecimalCharFilter`, `EmailCharFilter`, `PersonNameCharFilter` |
| Проверка | `ValidationRule` со своими проверками — при изменении, при уходе из поля, при отправке или всегда |
| Оформление | `HintDecorator`, `RaisedLabelDecorator`, `HelperTextDecorator`, `LengthCounterDecorator` |
| Форматирование | `FormattingRule` (сочетания клавиш, которые пишут теги), `CaretWatchRule` |
| Вставка | `PastePlainRule`, `PasteLinkRule` |
| Клавиатура | `EnterKeyRule`, `TabKeyRule`, `SelectAllOnFocusRule`, `ReleaseOnCancelRule`, `KeyboardTraitsRule`, `KeyboardLiftRule` |

Правила трёх видов делают разную работу:

- **Фильтр** оценивает, что останется после правки, и отклоняет её или оставляет допустимое. Удаление всегда свободно, а композиция оценивается только при фиксации.
- **Проверка** только сообщает: из-за проверки ничего не отклоняется. `ValidationRule.BlockSubmitWhenInvalid` может задержать отправку.
- **Оформление** показывает состояние поля на назначенном вами UI: подсказку, всплывающую метку, строку под полем, счётчик. Оно ничего не стоит в кадре, если не анимируется.

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

**Пресет поля** (`LetteraFieldPreset`, **Assets > Create > Lettera > Field Preset**) хранит список правил, общий для многих полей; `AddPreset` добавляет его. `GetRule<T>()` находит правило на поле.

Кнопка **Try It** в инспекторе набирает текст в настоящее поле, созданное для пробы, — видно, что правила сделают с вводом, ещё до Play.

## Своё правило

Унаследуйтесь от `LetteraFieldRule` и переопределите нужное: `FilterEdit` видит каждую правку на входе, `LookupKey` может поменять действие клавиши, `RequestKeyboard` задаёт свойства экранной клавиатуры, `OnSnapshot` узнаёт об изменениях состояния.

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

// Refuses any edit that would bring in a digit.
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

У предложенной правки можно поменять и `Start`, `RemovedLength` и `Text`, чтобы переписать её, а не отклонить. Правило со списком или другим вложенным объектом переопределяет `Clone`.

## Форматирование

Поле пишет форматирование в собственном синтаксисе, через стили, которые у него уже есть:

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

`ApplyEffect`, `RemoveEffect`, `ToggleEffect` и `ClearStyles` пишут тот тег, который стиль этого текста связывает с эффектом. **Стилей они никогда не добавляют:** если у поля нет стиля для эффекта, записать его нельзя, и об этом скажет `CanFormat`. Когда ничего не выделено, команда задаёт отложенный стиль, который получат следующие набранные символы. `CaretStylesChanged` сообщает, что покрывает каретку, — для панели, где подсвечиваются кнопки; ровно так делает пример Rich Editor.

HTML, вставленный из браузеров и текстовых редакторов, сохраняет жирный, курсив, цвета, подсветку и ссылки, если у поля есть для них стили.

## Команды, меню и панели

Всё, что пользователь может запустить на тексте из меню, кнопкой или клавишей, — это `LetteraEditCommand`: `CutCommand`, `CopyCommand`, `PasteCommand`, `PastePlainCommand`, `SelectCommand`, `SelectAllCommand`, `UndoCommand`, `RedoCommand`, `ToggleEffectCommand`, `ClearStylesCommand`, `WebLookupCommand`, `ShareTextCommand` и `PlatformActionsCommand`.

- Контекстное меню показывает команды проекта из **Project Settings > Lettera > Selection & Editing > Commands**. `LetteraContextMenu` — поставляемое меню, собранное из ваших UI-объектов; префаб в **Menu Prefab** его заменяет.
- `LetteraCommandControl` привязывает `Button` или `Toggle` к команде для панели инструментов. Без цели он действует на поле, у которого клавиатура.
- `LetteraKeepFocus` на кнопке панели не даёт нажатию на неё завершить редактирование.

Своя команда:

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

Добавьте её в список проекта (`LetteraSettings.Instance.Editing.Commands`) или привяжите к элементу управления. `LetteraEditCommand.TitleSource` может брать названия всех команд из другого места, например из таблицы локализации.

## Буфер обмена

Одно копирование пишет сразу несколько форматов: простой текст, HTML, Markdown и собственный формат Lettera. Вставка берёт самый богатый формат, который поле может использовать, и пишет его в синтаксисе поля, поэтому оформленный текст сохраняет стили между полями Lettera, а там, где платформа позволяет, — и при обмене с другими приложениями. Поле пароля не отдаёт значение в буфер, если `CopyAllowed` его правила не говорит обратного.

- В Windows и в вебе буфер обмена со всеми форматами включён по умолчанию. На Android, iOS и macOS он ждёт своего переключателя; до тех пор за пределы приложения уходит только простой текст, а богатые форматы остаются внутри.
- В браузере буфер обмена можно прочитать только позже. `Paste()` там возвращает false с предупреждением; используйте `PasteAsync()` или `PasteCommand`, которая на каждой платформе выбирает правильный путь:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FieldsPasteButton : MonoBehaviour
{
    public LetteraEditable Field;

    public void PasteHere() => new PasteCommand().Execute(new LetteraCommandContext(Field.Selectable));
}
```

- `LetteraClipboard.Backend` можно заменить: реализуйте `ILetteraClipboardBackend` для своей платформы или для теста.
- **Tools > Lettera > Clipboard** показывает все форматы, которые записало последнее копирование.

## Композиция и методы ввода

Методы ввода для китайского, японского и корейского составляют текст, прежде чем зафиксировать его. Поле показывает композицию на месте, с подчёркнутыми сегментами (их вид задаётся в **Selection & Editing > Ime Look**), не включает её в значение до фиксации и делает из неё один шаг отмены. Правила оценивают только зафиксированный текст.

- **Плееры Windows** читают собственные сообщения метода ввода: несколько сегментов, каретку внутри них, окно кандидатов рядом с кареткой.
- **Веб** ведёт сеанс через одно скрытое текстовое поле, а композиция показывается прямо в поле Lettera.
- **В остальных случаях** композиция идёт через собственный путь ввода Unity.

Весь ввод доходит до поля через `ILetteraInputSource`; `LetteraInput.Register` ставит ваш источник впереди поставляемых.

## Касания

На сенсорном экране долгое нажатие выделяет слово, двойное касание выделяет, маркеры перетаскивают концы выделения и не теряют захвата, свайп, начатый на тексте, уходит в окружающий `ScrollRect`, а лупа увеличивает текст под пальцем. Поля поднимают экранную клавиатуру; `KeyboardTraitsRule` запрашивает её тип, клавишу ввода, автозаполнение и заглавные буквы, а `KeyboardLiftRule` поднимает UI-объект, чтобы клавиатура не закрывала каретку.

## Собственные элементы платформы

Поле может работать через собственное текстовое поле платформы, её буфер обмена и меню. Всё, что выдаёт такой элемент — клавишами, голосом, автозаполнением или вставкой, — всё равно проходит через правила поля, а отклонённая правка возвращает элемент в верное состояние.

У каждой части есть **переключатель** в **Project Settings > Lettera > Native Platform**: `Auto`, `On` или `Off`, со строкой, которая объясняет, почему он включён или выключен на платформе текущего билда. **То, что не запускалось на настоящем устройстве, поставляется выключенным**:

| | Windows | Веб | Android, iOS | macOS |
|---|---|---|---|---|
| Ввод текста | композиция через метод ввода | одно скрытое текстовое поле, композиция на месте | выключено, пока не включат | ввод Unity |
| Буфер обмена с форматами | включён | включён | выключен, пока не включат | выключен, пока не включат |
| Реплика (`ReplicaFieldRule`) | ничего не делает | включена, когда поле просит | выключена, пока не включат | ничего не делает |

Меню и лупа на телефонах — выбор проекта: **Android Menu**, **iOS Menu** и **Loupe** выбирают собственные элементы Lettera или системные, а системным нужен включённый переключатель.

`ReplicaFieldRule` ставит на место поля, пока оно редактируется, собственный элемент платформы — через тот же сеанс и те же правила: в вебе это обычный элемент страницы или поверхность, которую страница регистрирует в `window.LetteraSurfaces`. Где реплики нет, правило ничего не делает, поэтому один префаб подходит для всех платформ.

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

Пример **Device Check** проводит по телефону экран за экраном и выдаёт один блок текста, который нужно прислать обратно. **Lettera > Device Check > Build for...** включает эти пути только для этого билда. Что где запускалось — в [Платформах](Platforms.ru.md).

## Сохранение состояния поля

`CaptureState` возвращает исходный текст, выделение и прокрутку в виде простых сериализуемых данных; `RestoreState` возвращает их на место. Этого хватает, чтобы сохранить черновик, если система закроет приложение в фоне:

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

Незаконченная композиция фиксируется до того, как снимается состояние.

## Ограничения

- **Собственные пути платформы не проверялись на настоящих устройствах.** В 0.6.0 записано, что на телефонах ничего не запускалось: поднимает ли Safari на настоящем iPhone клавиатуру, его копирование и вставка, весь Android и iOS, буфер обмена macOS и настоящий японский и корейский ввод где бы то ни было. Метод ввода Windows проверялся с поддельным источником, веб — через CDP. Отсюда и переключатели; что запускалось с тех пор, записано в [Платформах](Platforms.ru.md).
- На macOS нет собственного ввода текста. Android пока не передаёт картинки через буфер обмена ни в одну сторону.
- У выделяемого текста нет своего меню или лупы для отдельной платформы: Android Menu, iOS Menu и Loupe действуют на весь проект.
- `LetteraPasteButton` и кнопки вставки iOS нет: вставка идёт через меню или клавишу. В меню Lettera в вебе нет пункта Share.
- Подсветка выделения — сплошной цвет с прямыми углами. Каретка остаётся вертикальной на курсиве, а на изогнутом тексте каретка и прямоугольники не следуют наклону глифа.
- Над простым текстом поля Lettera сама не ставит курсор-«балку»: где он нужен, говорит `WantsIBeam`. Над участком ставится собственный `Cursor` участка. Меню, открытое во время смены языка, сохраняет старые названия, пока его не откроют снова. Сенсорное меню не прячется, пока текст движется.
- Лупа из меша показывает текст и его подложку, но не сцену за ними, и только на Canvas.
- Нет перетаскивания текста, визуального движения каретки, нескольких кареток, интерфейса поиска и проверки орфографии.
- В [документе](Documents.ru.md) правка выделения, которое выходит за пределы окна, целиком отклоняется с предупреждением, так что «выделить всё и удалить» там пока не работает; `Source`, `Length` и `PlainText` отвечают за окно.
- `Text` поля в Play mode создаётся при чтении: скрипт, который читает его каждый кадр, после правки получает новую строку в каждом кадре.
