# Getting Started

[English version](GettingStarted.md) · [Все руководства](Lettera.ru.md)

Это руководство проходит Lettera по одной задаче за раз, в том порядке, в каком проект обычно с ними сталкивается. Каждая часть заканчивается ссылкой на руководство, где та же тема разобрана глубже.

1. [Текст на экране](#1-текст-на-экране)
2. [Шрифты](#2-шрифты)
3. [Разметка](#3-разметка)
4. [Стили](#4-стили)
5. [Поля](#5-поля)
6. [Движение](#6-движение)
7. [Документы](#7-документы)
8. [Формулы](#8-формулы)
9. [3D-текст](#9-3d-текст)

## 1. Текст на экране

Компонентов текста три:

| Компонент | Где рисует | Меню |
|---|---|---|
| `LetteraText` | На Canvas, как `Graphic` из uGUI: с ним работают маски, layout-группы и Canvas Group. | **GameObject > Lettera > Text** |
| `LetteraWorldText` | В мировом пространстве, без Canvas. Тексты с одним материалом сливаются в один батч. | **GameObject > Lettera > World Text** |
| `LetteraText3D` | Объёмной освещённой геометрией. См. [3D-текст](#9-3d-текст). | **GameObject > Lettera > Text 3D** |

У первых двух один API: что работает на одном, работает и на другом. Текст на Canvas из кода:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartFirstLabel : MonoBehaviour
{
    public RectTransform Parent;   // Something under a Canvas.

    void Start()
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(Parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(400, 120);

        var label = go.AddComponent<LetteraText>();
        label.FontSize = 32;
        label.color = Color.white;
        label.HorizontalAlignment = LetteraHorizontalAlignment.Center;
        label.VerticalAlignment = LetteraVerticalAlignment.Middle;
        label.Text = "Hello from code";
    }
}
```

**Рамка.** Текст раскладывается внутри своего rect за вычетом `Padding`. Что происходит у краёв, решают эти свойства:

| Свойство | Что делает |
|---|---|
| `WordWrap` | Переносит строки по ширине рамки, по правилам переноса Unicode. |
| `HorizontalAlignment` | `Start`, `Center`, `End`, `Left`, `Right` или `Justify`. `Start` и `End` следуют направлению абзаца, поэтому для арабского и иврита меняются местами. |
| `VerticalAlignment` | `Top`, `Middle` или `Bottom`. |
| `Overflow` | `Visible`, `Ellipsis` (значок на месте обрезки) или `Truncate` (затухание). `OverflowPosition` ставит обрезку в начало (0), середину (0.5) или конец (1). |
| `AutoSize` | Подбирает наибольший размер между `MinFontSize` и `MaxFontSize`, при котором текст помещается; какой выбран, говорит `CurrentFontSize`. |
| `LineHeightMode`, `LineHeightValue` | Расстояние между строками. |
| `BaseDirection`, `Language` | Направление абзаца (по умолчанию `Auto`) и язык в формате BCP 47 для форм, которые зависят от языка. |

**Текст во время игры.** Значение, которое нужно сохранить, задавайте через `Text`. Значение, которое меняется каждый кадр, — через `SetText`: он принимает строку, `StringBuilder`, `char[]` или span и не меняет того, что сохраняет сцена. Простой текст, обновляемый так, не выделяет памяти, как только его символы уже встречались.

**Измерения.** Lettera отвечает на вопросы о тексте, не трогая его rect:

```csharp
using System.Collections.Generic;
using FirstSymbol.Lettera;
using UnityEngine;

public class StartMeasure : MonoBehaviour
{
    public LetteraText Label;
    readonly List<LetteraSearchHit> m_Hits = new List<LetteraSearchHit>();

    void Start()
    {
        // How big this text would be at most 300 units wide.
        Vector2 size = Label.MeasureText(new LetteraMeasureOptions { MaxWidth = 300 });

        // How many words it shows, by Unicode's word rules. Neither call allocates.
        int words = Label.CountUnits(LetteraTextUnit.Word);
        foreach ((int start, int end) in Label.Units(LetteraTextUnit.Line))
            Debug.Log("A line from " + start + " to " + end);

        // Every place a string stands, ignoring case.
        int found = Label.FindAll("lettera", m_Hits, ignoreCase: true);
        Debug.Log(size + ", " + words + " words, " + found + " hits");
    }
}
```

`Lines`, `GlyphCount`, `GetGlyph`, `InkBounds` и `LayoutBounds` описывают то, что текст нарисовал; `HitTestCaret`, `TryGetCaretRect` и `GetRangeRects` переводят точки в символы и обратно.

**Текст в мире и указатель.** Текст в мировом пространстве получает нажатия только через `LetteraWorldRaycaster` на камере, а любому тексту нужен `EventSystem` с модулем ввода. Меню GameObject добавляет EventSystem само.

## 2. Шрифты

Если шрифт не назначен, текст рисуется шрифтом операционной системы по умолчанию, а всё, чего в нём нет, берёт из других шрифтов системы, включая эмодзи. В вебе системных шрифтов нет: Lettera приносит с собой Noto Sans для латиницы, кириллицы и греческого, а для остальных письменностей нужен назначенный шрифт.

Чтобы использовать свой шрифт, выделите файл и выберите **Assets > Create > Lettera > Font Asset From Selection**. Затем назначьте его в поле **Font** текста или в **Project Settings > Lettera > Defaults** — для всех текстов сразу. Несколько начертаний с запасными между ними образуют **стек шрифтов**; шрифт, который уже есть в системе игрока, описывает **системный ассет шрифта**, в нём хранятся только имена.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartFonts : MonoBehaviour
{
    public LetteraText Label;
    public LetteraFontAsset Brand;      // A font asset made from a file.
    public LetteraFontAsset Icons;

    void Start()
    {
        var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
        stack.Families.Add(new LetteraFontFamily { Primary = Brand });
        stack.Families.Add(new LetteraFontFamily { Primary = Icons });
        Label.FontStack = stack;
    }
}
```

Подробнее — в [Шрифтах](Fonts.ru.md): стеки с начертаниями, системные шрифты по платформам, вариативные шрифты, память, подмножества, пиксельные и цветные шрифты.

## 3. Разметка

Теги похожи на HTML: `<b>bold</b>`, `<color=#F80>orange</color>`, `<size=150%>big</size>`. Значение тега — список параметров через запятую, значение можно взять в кавычки: `<shadow=#000,0.1,-0.1>`. `<br>` — перенос строки.

Тег что-то значит только потому, что **стиль** связывает его с **эффектом**. Словарь по умолчанию действует, пока проект не назначит свой глобальный пресет стилей, и включает:

| Что | Теги |
|---|---|
| Насыщенность, наклон, размер, цвет | `<b>` `<strong>` `<i>` `<em>` `<size>` `<color>` `<alpha>` |
| Шрифты и язык | `<font>` `<lang>` `<otf>` `<axes>` `<size-match>` |
| Линии и слои | `<u>` `<s>` `<outline>` `<shadow>` `<fill>` `<stroke>` `<glow>` `<inner-shadow>` `<extrude>` `<filter>` `<union>` `<warp>` `<mark>` |
| Регистр и положение | `<uppercase>` `<lowercase>` `<smallcaps>` `<sup>` `<sub>` `<ruby>` |
| Вёрстка | `<tracking>` `<word-spacing>` `<cell>` `<line-height>` `<para-spacing>` `<line-gap>` `<nobr>` `<li>` `<indent>` `<align>` `<trim>` `<curve>` `<divider>` `<ellipsis>` `<truncate>` `<dir>` |
| Движение и отклик | `<wave>` `<shake>` `<reveal>` и другие анимации, `<a>` `<link>` `<spoiler>` `<sprite>` `<obj>` |
| Формулы | `<math>` и таблицы LaTeX, начинающиеся с `\begin{tabular}` |

Параметры каждого тега перечислены в [таблице разметки README](../README.ru.md).

Тег, который никто не забрал, остаётся буквальным текстом, поэтому `a <b` и `<unknown>` показываются как написаны. Чтобы показать тег буквально, экранируйте его: `\<b>`, или оберните в `<noparse>...</noparse>`. Флажок **Use Global Style Preset** у текста отключает для него словарь проекта.

## 4. Стили

Стиль — это пара: то, что находит участки (матчер, обычно имя тега), и то, что их меняет (эффект). Добавив стиль тексту, вы даёте ему новый тег:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartOwnTag : MonoBehaviour
{
    public LetteraText Label;

    void Start()
    {
        var sunset = new FillEffect();
        sunset.SetPaint(LetteraPaint.Of(LetteraGradient.Of(
            new Color32(255, 170, 40, 255), new Color32(240, 60, 130, 255)), LetteraPaintSpan.Range));

        Label.Styles.Add(LetteraStyle.ForTag(sunset, "sunset"));
        Label.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "warning", "#FF8800"));
        Label.MarkStylesDirty();
        Label.Text = "<sunset>Evening</sunset> and a <warning>warning</warning>";
    }
}
```

В инспекторе кнопка **+ Add Style** в карточке **Styles** предлагает все эффекты и матчеры по тому, что они делают. Стили, общие для многих текстов, кладут в ассет **пресета стилей**; пресет на весь проект в **Project Settings > Lettera** заменяет словарь по умолчанию.

Подробнее — в [Стилях](Styles.ru.md): заливки и градиенты, порядок слоёв, удержания, меняющие параметр без разбора текста, составные эффекты, свой эффект и шейдер.

## 5. Поля

`LetteraSelectable` делает текст выделяемым и копируемым, `LetteraEditable` — полем. **GameObject > Lettera** создаёт Input Field, Text Area, Password Field, Dropdown, Selectable Text и Editable Text — каждое с рамкой и подсказкой.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartChatBox : MonoBehaviour
{
    public RectTransform Parent;

    void Start()
    {
        GameObject root = LetteraFieldFactory.CreateInputField(Parent, "Say something...");
        LetteraEditable field = root.GetComponentInChildren<LetteraEditable>();
        field.AddRule(new MaxLengthRule { Limit = 80 });
        field.Submitted += value =>
        {
            Debug.Log("Sent: " + field.PlainText);
            field.SetSource("");
        };
    }
}
```

Значение поля — его исходный текст вместе с разметкой (`Source`); `PlainText` — то же без разметки. Правила решают, что может попасть в поле, что делают клавиши и что поле показывает вокруг себя.

Подробнее — в [Полях](Fields.ru.md): выделение, все правила, команды форматирования, буфер обмена, композиция и собственные элементы платформы.

## 6. Движение

Теги движения работают без всякой настройки:

```text
<wave>waves</wave> <shake>shakes</shake> <bounce>bounces</bounce> <roll>1234</roll>
<a=https://unity.com>a link</a> and a <spoiler>secret</spoiler>
```

Движение сдвигает вершины, оставшиеся от последней перестройки, поэтому колышущийся текст никогда не раскладывается заново и не выделяет памяти в кадре. Печатная машинка — это `RevealEffect`, бегущая строка — `MarqueeEffect`, а `LetteraSequencer` плавно меняет любой параметр по временной шкале.

Подробнее — в [Движении](Motion.ru.md): все анимации и их параметры, появление, секвенсор, дорожки Timeline, участки, отвечающие на указатель, встроенные спрайты и ослабленное движение.

## 7. Документы

Текст, который слишком длинный, чтобы разложить его целиком — журнал, книгу, файл на 100 МБ, — откройте как документ. Раскладывается только то, что рядом с видимой областью, а прокрутка в этих пределах ничего не перестраивает.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StartBook : MonoBehaviour
{
    public LetteraText Page;
    public TextAsset Book;

    void Start()
    {
        Page.OpenDocument(LetteraRope.FromString(Book.text), takeOwnership: true);
        Page.gameObject.AddComponent<LetteraDocumentScroller>().Text = Page;
    }
}
```

Длинный текст, заданный обычным способом, тоже рисует только строки рядом с видимой областью. Подробнее — в [Документах](Documents.ru.md).

## 8. Формулы

Формула — это TeX внутри `<math>`, в потоке предложения, или на отдельной строке с `<math=display>`:

```text
Energy is <math>E = mc^2</math>, and <math=display>\sum_{i=1}^n i = \frac{n(n+1)}{2}</math>
```

В Windows используется системный Cambria Math. В вебе, на Android и iOS системного математического шрифта нет: один раз выполните **Lettera > Math > Use Bundled Font**. Подробнее — в [Формулах](Math.ru.md), вместе с химией и таблицами LaTeX.

## 9. 3D-текст

**GameObject > Lettera > Text 3D** создаёт `LetteraText3D`: замкнутые освещённые тела, построенные по контурам глифов, с глубиной, фасками и пятью слотами материалов. Разметка, шрифты, запасные шрифты и движение работают так же, как у текста в мире, а `<depth=2>` или `<bevel=chamfer,0.03,0.03>` действуют на участок. В комплекте семь готовых образов, от Gold до Neon. Модуль живёт в своей сборке `FirstSymbol.Lettera.Text3D`, и билд оставляет её за бортом, если ни одна сцена её не использует.

Подробнее — в [3D-тексте](Text3D.ru.md).

## Куда дальше

- **Tools > Lettera > Getting Started** перечисляет эти руководства, импортирует примеры и открывает любое окно.
- **Tools > Lettera > Commands** (Ctrl+Alt+L) находит по набранным словам всё, что Lettera создаёт или открывает.
- [Инструменты](Tools.ru.md) описывают окна: Glyphs, Ranges, Atlases, Clipboard, Font Tools, Sequencer и оверлей в окне Scene.
- Переходите с TextMesh Pro? Прочитайте [Переход](Migration.ru.md).
