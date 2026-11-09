# Переход с TextMesh Pro

**Tools > FirstSymbol > Lettera > Migrate From TextMesh Pro** переводит тексты, шрифты и скрипты проекта с TextMesh Pro на Lettera. Он сканирует, только когда попросят, меняет только то, что написано на кнопке, ничего не удаляет и оставляет TextMesh Pro установленным.

[English version](Migration.md) · Другие руководства: [Getting Started](GettingStarted.ru.md), [Fonts](Fonts.ru.md), [Styles](Styles.ru.md), [Fields](Fields.ru.md), [Инструменты](Tools.ru.md)

## Перед началом

1. **Закоммитьте всё.** Настоящая отмена — это система контроля версий: инструмент переписывает файлы сцен, префабов и скриптов прямо на диске.
2. **Поставьте Asset Serialization в Force Text** (**Project Settings > Editor**). Инструмент читает сцены и префабы как текст и, пока настройка не стоит, говорит об этом и даёт кнопку к ней.
3. **Сохраните открытые сцены.** Об открытой сцене с несохранёнными изменениями перед этапом 2 спросят: Save, Don't Save или Cancel, и Cancel останавливает этап.

## Порядок

В окне по кнопке на этап. Нажимайте их по порядку; каждая сначала сканирует, если сканирования ещё не было.

1. **Scan** читает все сцены, префабы, скрипты, определения сборок и ассеты шрифтов TextMesh Pro под Assets и ничего не меняет. Он пропускает `Library`, `Temp`, `Logs`, `obj`, папки, чьё имя начинается с точки или кончается на `~`, и папку самой Lettera. Из собственной папки TextMesh Pro читаются только шрифты. Файл, который не удалось прочитать, становится находкой с пометкой **Unreadable**, а не пропадает молча.
2. **1. Create Fonts.** Каждый ассет шрифта TextMesh Pro получает шрифт Lettera из своего исходного файла шрифта, сохранённый рядом с этим файлом как `<имя> Lettera.asset` (или в `Assets/Lettera/Fonts`, если файл лежит в пакете). Ассет, чей исходный файл не найден, помечается **Needs You**, и тексты с ним берут шрифт проекта по умолчанию.
3. **2. Convert Scenes And Prefabs.** Каждый `TextMeshProUGUI` становится `LetteraText`, а каждый `TextMeshPro` — `LetteraWorldText`, **на месте**: компонент сохраняет свою идентичность в файле, поэтому каждое поле, цель события и переопределение префаба, что указывали на него, указывают и дальше. Служебные под-меши TextMesh Pro удаляются. Изменённые открытые сцены открываются заново.
4. **3. Rewrite Scripts.** Сначала определения сборок: те, что ссылаются на `Unity.TextMeshPro`, получают ещё `FirstSymbol.Lettera`. Затем переписывается каждый файл C#, где используется TextMesh Pro, а оригинал сохраняется в `Library/Lettera/ScriptBackups`, вне окна Project.
5. **Copy The Report** кладёт в буфер обмена всё найденное, сделанное и не перенесённое, в Markdown, для ревью или задачи.

Потом дайте Unity скомпилировать проект и пройдите по тому, что отчёт перечисляет под **Not carried**.

**Что-то не трогать.** У каждой находки есть **Exclude**, а в контекстном меню папки — **Exclude This Folder**; исключённая строка остаётся в списке, приглушённая, с кнопкой **Include**, чтобы вернуть её, и ни один этап её не трогает. Дерево папок рядом с находками считает их по папкам: щелчок показывает находки одной папки, второй щелчок — снова все. Исключения хранятся в `ProjectSettings/Lettera/Migration.json`, отсортированными, так что команда может их закоммитить.

## Что переносится

### Компоненты

| TextMesh Pro | Lettera |
|---|---|
| Текст | `Text`, с переведённой разметкой (см. ниже) |
| Ассет шрифта | Шрифт Lettera, созданный на этапе 1 |
| Размер шрифта, авторазмер, минимум и максимум | `FontSize`, `AutoSize`, `MinFontSize`, `MaxFontSize` |
| Цвет вершин | Цвет компонента |
| Выравнивание по горизонтали Left, Center, Right | `Start`, `Center`, `End`, которые следуют направлению абзаца |
| Geometry | `Center` |
| Justified и Flush | `Justify` |
| Выравнивание по вертикали Top, Middle, Bottom | так же |
| Overflow, Ellipsis, Truncate | `Visible`, `Ellipsis`, `Truncate` |
| Перенос слов | `WordWrap` |
| Поля | `Padding`, в порядке Lettera |
| Справа налево | `BaseDirection` справа налево |
| Rich text выключен | `UseGlobalStylePreset` выключен, и теги видны как написаны |
| Начертание: жирный, курсив, подчёркнутый, зачёркнутый, строчные, прописные, капитель | Текст обёрнут в `<b>`, `<i>`, `<u>`, `<s>`, `<lowercase>`, `<uppercase>`, `<smallcaps>` |
| Raycast target, его отступ, maskable | так же |
| Текст в мире | `LetteraWorldText` с включённым **Standalone**, чтобы сортироваться среди прозрачных объектов как раньше |
| `set_text`, `set_fontSize` в UnityEvent | `set_Text`, `set_FontSize` |
| Переопределение текста, размера, авторазмера, цвета или полей у экземпляра префаба | То же переопределение на свойстве Lettera |

### Разметка

Словарь Lettera по умолчанию уже читает `b i u s size color alpha font align indent line-height nobr br noparse mark sub sup smallcaps lowercase uppercase sprite`, поэтому эти теги остаются как есть. Остальные:

| TextMesh Pro | Становится |
|---|---|
| `<sprite name="x">`, `<sprite index=3>`, `<sprite="asset" ...>` | `<sprite=x>`, `<sprite=3>`; картинки даёт библиотека инлайн-элементов, которую вы создадите |
| `<link="id">` | `<link=id>`: помечает диапазон для вашего кода, как и раньше. Сам по себе ничего не открывает |
| `<page>` | Удаляется |
| `<quad>`, `<material>` | Остаются буквальным текстом внутри `<noparse>`: выбор тут можете сделать только вы |
| `<alpha>` | Остаётся. Учтите, что здесь он действует до своего закрывающего тега, а в TextMesh Pro — до следующего тега цвета |

### Скрипты

Член переименовывается только у переменной, про которую сам файл показывает, что в ней тип TextMesh Pro: объявление, `var` с видимым инициализатором, `GetComponent<T>`, приведение, `as`, `new`, элемент массива или списка, переменная `foreach`. Строки, комментарии и строки препроцессора не трогаются никогда.

| TextMesh Pro | Lettera |
|---|---|
| `TextMeshProUGUI`, `TMP_Text` | `LetteraText` |
| `TextMeshPro` | `LetteraWorldText` |
| `TMP_FontAsset` | `LetteraFontAsset` |
| `TMP_InputField`, `TMP_Dropdown` | `LetteraEditable`, `LetteraDropdown` |
| `using TMPro;` | `using FirstSymbol.Lettera;`, и оба, пока остаётся тип TextMesh Pro |
| `.text`, `.fontSize`, `.fontSizeMin`, `.fontSizeMax`, `.enableAutoSizing`, `.font` | `.Text`, `.FontSize`, `.MinFontSize`, `.MaxFontSize`, `.AutoSize`, `.Font` |
| `.enableWordWrapping`, `.overflowMode` | `.WordWrap`, `.Overflow` |
| `.ForceMeshUpdate()` | `.ForceRebuild()` |
| `.bounds`, `.textBounds` | `.InkBounds` |
| `.alignment = TextAlignmentOptions.TopLeft;` | Два присваивания: `HorizontalAlignment` и `VerticalAlignment` |
| `.onValueChanged.AddListener(h)`, `RemoveListener(h)` | `.ValueChanged += h`, `-= h` |

О чём он вместо этого сообщает и что взять:

| TextMesh Pro | Что взять |
|---|---|
| `preferredWidth`, `preferredHeight`, `GetPreferredValues` | `GetMaxContentWidth()`, `GetHeightForWidth(width)`, `MeasureText(...)` |
| `textInfo` | `Lines` и `GetGlyph` |
| `maxVisibleCharacters` | `RevealEffect` |
| `fontStyle`, `fontWeight`, `characterSpacing` | Стиль на весь текст, `<b=вес>`, `<tracking>` |
| `richText` | `UseGlobalStylePreset` |
| `margin` | `Padding`, порядок: слева, снизу, справа, сверху |
| `isRightToLeftText`, `textWrappingMode` | `BaseDirection`, `WordWrap` |
| `onEndEdit`, `onSelect`, `onDeselect` | `Submitted`, `FocusGained`, `FocusLost` у поля |
| Любой другой тип `TMP_` | Остаётся, менять его вам |

Переписанный скрипт выглядит так:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class Scoreboard : MonoBehaviour
{
    public LetteraText score;           // Был TextMeshProUGUI: ссылка из сцены сохранилась.
    public LetteraEditable playerName;  // Был TMP_InputField.

    void Start()
    {
        score.Text = "0";
        score.FontSize = 48;
        score.HorizontalAlignment = LetteraHorizontalAlignment.End;    // Было alignment = TextAlignmentOptions.Right.
        score.VerticalAlignment = LetteraVerticalAlignment.Middle;
        playerName.ValueChanged += value => Debug.Log(value);         // Было onValueChanged.AddListener.
    }
}
```

## Что не переносится

Каждый из этих пунктов — строка под **Not carried** с именем файла и тем, что сделать:

- **Поля ввода и выпадающие списки** остаются на TextMesh Pro вместе с текстами внутри. Пересоздайте их через **GameObject > Lettera > Input Field** или **Dropdown**; см. [Fields](Fields.ru.md).
- **Вид материала:** обводка, подложка и свечение. Дайте тексту стиль обводки, тени или свечения; см. [Styles](Styles.ru.md).
- Межбуквенный и межстрочный интервалы (берите `<tracking>` и `<line-height>`), градиент по вершинам (берите `<fill>`) и начертание Highlight, которое не хранит цвета.
- Выравнивание Baseline, Geometry и Capline становится Middle; переполнение Masking, Scroll Rect, Page и Linked становится Visible; сохранение пробелов не переносится.
- Переопределение у экземпляра префаба для выравнивания, начертания, переноса, переполнения, шрифта, материала или интервалов: задайте его на экземпляре заново.
- Тонировка и анимация спрайта.

Не переносятся вовсе: таблицы запасных шрифтов, `TMP_Settings`, ассеты спрайтов, таблицы стилей и ассеты градиентов. Запасные шрифты соберите стеком шрифтов, картинки — библиотекой инлайн-элементов (см. [Fonts](Fonts.ru.md)), а стили из таблицы стилей — своими стилями (см. [Styles](Styles.ru.md)).

## Если что-то не так

- **Scan ничего не делает.** Asset Serialization не в режиме Force Text; окно говорит об этом и даёт кнопку к настройке. Сцена или префаб, всё ещё сохранённые в двоичном виде, помечаются **Unreadable**.
- **Тексты потеряли шрифт.** Этап 2 прошёл раньше этапа 1, или шрифт помечен **Needs You**. Запустите **1. Create Fonts**, а затем этап 2 заново на свежей копии этих файлов из контроля версий, или задайте шрифт текстам вручную.
- **Скрипт всё ещё не компилируется.** Отчёт перечисляет каждую оставленную вам строку и что взять. Чаще всего это `textInfo` и код материалов.
- **Поле в скрипте потеряло ссылку на поле ввода.** `TMP_InputField` в скрипте стал `LetteraEditable`, а поле ввода в сцене всё ещё от TextMesh Pro. Пересоздайте поле на Lettera и назначьте его или пока верните этот скрипт из `Library/Lettera/ScriptBackups`.
- **Что-то выглядит иначе.** Сравните сцену со старой из контроля версий. Выравнивание теперь следует направлению абзаца, поэтому текст справа налево с выравниванием Left теперь начинается справа.
- **Окно потеряло находки.** Оно держит их, пока открыто; закрытие или перезагрузка скриптов начинают заново. Просканируйте ещё раз: переведённые сцены и префабы больше не находятся. Ассеты шрифтов TextMesh Pro находятся всегда, ведь ничего не удаляется.

## Когда всё готово

Когда Scan не находит ничего, кроме шрифтов TextMesh Pro и того, что вы решили оставить, TextMesh Pro можно удалить из проекта. Шрифты Lettera с этапа 1 остаются на месте, а копии в `Library/Lettera/ScriptBackups` можно удалить.

## Из командной строки

С установленным `com.unity.pipeline` команды `unity command lettera migrate_scan`, `migrate_status`, `migrate_fonts`, `migrate_components`, `migrate_scripts --preview` и `migrate_report --output <файл>` выполняют те же этапы. При открытой сцене с несохранёнными изменениями они отказываются работать, а не спрашивают. См. [Agent guide](AgentGuide.md).

## Ограничения

- Инструмент прогоняли на собственных *Examples & Extras* TextMesh Pro: все тексты заменены, сцены и префабы открываются без новых пропавших скриптов. Скрипты там сохранили много ошибок компиляции, почти все про `textInfo` и материалы, и отчёт их называет. На настоящем проекте его ещё не прогоняли, а переведённые сцены не сравнивали с исходными по картинке.
- Сканирование каждый раз читает весь проект.
