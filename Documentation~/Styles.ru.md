# Стили

[English version](Styles.md) · [Все руководства](Lettera.ru.md)

Всё, что меняет вид или вёрстку участка текста, — это **стиль**. Здесь показано, как тег становится внешним видом, как собрать свои стили и как менять их во время игры, не разбирая текст заново.

- [Матчеры и эффекты](#матчеры-и-эффекты)
- [Где живут стили](#где-живут-стили)
- [Параметры](#параметры)
- [Эффекты](#эффекты)
- [Заливки](#заливки)
- [Слои и их порядок](#слои-и-их-порядок)
- [Изменение вида во время игры](#изменение-вида-во-время-игры)
- [Составные эффекты и пресеты](#составные-эффекты-и-пресеты)
- [Участки без тегов](#участки-без-тегов)
- [Свой эффект](#свой-эффект)
- [Свой матчер](#свой-матчер)
- [Свой шейдер](#свой-шейдер)
- [Ограничения](#ограничения)

## Матчеры и эффекты

`LetteraStyle` соединяет две вещи:

- **матчер**, который находит участки в исходном тексте: тег вроде `<glow>`, маркер Markdown, URL или весь текст;
- **эффект**, который эти участки меняет: цвет, обводку, интервалы, движение.

`<b>` делает текст жирным только потому, что в словаре по умолчанию есть стиль, связывающий тег `b` с `BoldEffect`. Тег, который не забрал ни один стиль, остаётся буквальным текстом. Один эффект может стоять за несколькими тегами (`<b>` и `<strong>`), а одно и то же имя тега в другом проекте может значить что-то другое.

Создать `LetteraStyle` можно четырьмя способами:

| Фабрика | Какие участки находит |
|---|---|
| `LetteraStyle.ForTag(effect, "name", defaultParameter)` | Каждый участок, обёрнутый в тег с именем `name`, со значением или без |
| `LetteraStyle.ForWholeText(effect, parameter)` | Весь текст, без тега |
| `LetteraStyle.ForSpan(effect, start, length, parameter)` | Заданные кодовые точки выводимого текста |
| `LetteraStyle.ForMatcher(matcher, effect)` | Всё, что находит матчер |

У стиля есть ещё `Enabled`, чтобы выключить его, не удаляя.

## Где живут стили

Текст берёт стили из трёх мест, в таком порядке:

1. **Словарь.** Глобальный пресет стилей проекта из **Project Settings > Lettera > Defaults > Global Style Preset**; если его нет — встроенный словарь по умолчанию (`LetteraDefaultStyles`). Выключенный флажок **Use Global Style Preset** у текста убирает словарь для этого текста.
2. **Пресеты стилей** в `StylePresets` текста: ассеты `LetteraStylePreset`, общие по ссылке, — для вида, который нужен многим текстам.
3. **Собственные** `Styles` текста.

Изменив списки текста из кода, вызовите `MarkStylesDirty()`. Изменив ассет пресета из кода, вызовите его `NotifyChanged()`.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesSharedPreset : MonoBehaviour
{
    public LetteraText[] Labels;

    void Start()
    {
        var preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
        preset.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "accent", "#3DA5FF"));
        preset.Styles.Add(LetteraStyle.ForTag(new StrokeEffect { Width = 0.06f }, "heavy"));

        foreach (LetteraText label in Labels)
        {
            label.StylePresets.Add(preset);
            label.MarkStylesDirty();
        }
    }
}
```

Чтобы начать словарь проекта со встроенного, заполните пресет свежей копией `LetteraDefaultStyles.Create()` и добавьте к ней свои стили.

В инспекторе стили текста перечислены в карточке **Styles**. **+ Add Style** открывает выбор: в нём все эффекты и матчеры разложены по тому, что они делают, ищутся по словам вроде «gradient» или «typewriter», а стиль под указателем показывается вживую ещё до выбора. Строки перетаскиваются, копируются, дублируются и выключаются, как во всех списках Lettera; строка с проблемой говорит, в чём дело, и предлагает исправление.

## Параметры

Значение тега — список **слотов** через запятую: `<shadow=#000C,0.08,-0.08,0.2>` задаёт тени цвет, x, y и мягкость. Пустой слот оставляет значение самого эффекта: `<shadow=,,-0.2>` сдвигает только y. Значения можно брать в кавычки, а запятая внутри парных скобок остаётся в своём слоте.

- **Длины** слоёв задаются в em — долях размера шрифта; `2px` — в локальных единицах.
- **Размеры и интервалы** принимают `24`, `150%`, `+10`, `-5` или `1.2em` — как указано в строке тега в таблице [README](../README.ru.md).
- **Цвета** — `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA` или имя.
- **Варианты выбора**, например охват заливки, пишутся названием или его первой буквой, если с неё не начинается другой вариант: `glyph` или `g`.

**Параметр по умолчанию** стиля заполняет слоты, которые голый тег не задал, поэтому `ForTag(new ColorEffect(), "warning", "#FF8800")` делает голый тег `warning` оранжевым.

**Метки** дают участку имя, по которому код найдёт его позже: `<color #price=#0C0>12</color>` — участок цвета с меткой `price`. См. [удержания](#изменение-вида-во-время-игры).

## Правка параметров тегов в инспекторе

Помнить порядок слотов не нужно. В инспекторе текста поставьте курсор внутрь тега, и **панель тега** под полем текста покажет его слоты, по строке на слот: номер, имя и элемент для того, что слот принимает: образец с альфой для цвета, ползунок для числа с границами, кнопки `em` / `px` / `%` для длины, кнопки или список для выбора, переключатель для да и нет.

- **Задан или унаследован.** Слот, который тег оставил пустым, показывает, что будет применено и откуда оно: `inherited: -0.05 em (effect)` — собственное значение эффекта, `(style default)` — параметр по умолчанию стиля. Щёлкните по нему или по **Set**, чтобы задать слоту это значение; кнопка сброса (или `Alt+Backspace`) возвращает его.
- **Минимальная разметка.** Правка пишет только этот слот. Остальные токены остаются как были, пустые слоты перед заданным пишутся как `,`, а хвостовые пустые слоты и `=` уходят, когда ничего не осталось: сброс последнего заданного слота в `<shadow=,,,0.2>` даёт `<shadow>`. Значению, которому нужны кавычки, они ставятся.
- **Отмена.** Перетаскивание, сеанс выбора цвета или серия набора — один шаг `Ctrl+Z`; щелчок или сброс — отдельный шаг.
- **Вложенные теги.** Заголовок перечисляет все теги вокруг курсора; щёлкните по одному, чтобы править его. **Copy** копирует разметку открывающего тега.
- **Не читается.** Значение, которое эффект не может прочитать, помечено *Not read: the effect's value is used* и остаётся как написано, пока вы его не измените. Значение вне границ слота помечено *Clamped*. Значения за описанными слотами показаны плашками *Extra*.
- **Что делает тег и что писать.** Под разметкой панель одним предложением говорит, что делает тег, и предлагает от одной до четырёх разметок **Try**: `<curve=180>`, `<curve=90,0.5>`. Щелчок записывает это значение в тег. Под полем каждого слота всегда видна строка: что слот меняет, что принимает (границы, варианты, единицы) и что попробовать. Слоты в градусах показывают `°`, скорости — `Hz`, расстояния в em — `em`; записываемый токен остаётся обычным числом.
- **Настройки краски.** Девять настроек краски слоя (охват, форма, подгонка, край, смешивание, угол, масштаб, смещение) собраны под одним заголовком, закрытым, пока одну из них не зададут.
- **Параметров нет.** Тег вроде `<uppercase>` или `<nobr>` не читает значения, и панель так и говорит: *This tag takes no parameters.* Значение после `=` игнорируется, а кнопка его убирает.
- **Составные теги.** `<a>` — это ссылка, цвет и подчёркивание вместе. Его значение — по части на каждый эффект, через `;`, и панель даёт каждой части поле с именем её эффекта: первая — URL.
- **Теги, которые читает сопоставитель.** Формы есть и у `<li>`, `<ruby>`, `<divider>`, `<sprite>`, `<obj>` и `<math>`. Формула `<math>` — это текст между тегами, поэтому у неё нет слота; таблицы `\begin{tabular}` вообще не теги.
- **Без формы.** Тег, который не берёт ни один стиль, — обычный текст, и панель так и говорит. Незакрытый тег или кавычка показывают свой текст и кнопку **Fix**. Ваш эффект, не объявивший слоты, показывает *No slot form for this effect yet*, сырое значение как текст и ссылку на раздел этого руководства [Свой эффект](#свой-эффект).

**Обернуть в тег.** `Ctrl+.` (переназначается в *Edit, Shortcuts, Lettera*), кнопка `<>` на панели инструментов или кнопка **Wrap in tag...** на панели открывают список под полем текста. В нём теги, у которых есть стиль для этого текста: уже использованные здесь, затем собственные теги проекта, затем встроенные. Теги, которых этот текст не знает, затемнены с причиной. Набирайте для поиска по имени, группе или действию (`gradient` находит `<fill>`); `Enter` оборачивает выделение, `Ctrl+Enter` оборачивает и переходит в форму нового тега, `Esc` закрывает. Без выделения вставляется пустая пара в позиции курсора. Выделение, которое разрезает тег, расширяется, чтобы теги остались парными.

**Defaults на карточке стиля.** Для стиля с тегом, чей эффект имеет форму, **Defaults** показывает те же строки для параметра по умолчанию, со строкой *Bare `<warning>` = `<color=#FF8800>`* и числом слотов, которые заполняет голый тег. Он пишет ту же строку, что и сырое поле, поэтому пресеты и значения из кода видны в нём. **Raw** переключает на текстовое поле; оно же показывается, когда строка не разбирается.

У каждого встроенного тега есть описание и либо форма, либо прямое заявление, что параметров нет; тест падает, если встроенный эффект остался неописанным. Так же и ваши эффекты, если переопределят `Description`, `Examples` и `Parameters` (или `TakesNoParameters`). Панель пока не дополняет имена тегов при наборе, а UI Toolkit редактора не даёт ей поддержки скринридера.

## Эффекты

Каждый эффект ниже есть в словаре по умолчанию под указанным тегом. Слоты каждого тега перечислены в [таблице разметки README](../README.ru.md).

| Группа | Эффекты и теги |
|---|---|
| Текст | `BoldEffect` `<b>` `<strong>`, `ItalicEffect` `<i>` `<em>`, `SizeEffect` `<size>`, `ColorEffect` `<color>`, `AlphaEffect` `<alpha>`, `FontEffect` `<font>`, `LanguageEffect` `<lang>` |
| Линии | `UnderlineEffect` `<u>`, `StrikethroughEffect` `<s>`: сплошные, двойные, точечные, штриховые или волнистые, с пропуском у выносных элементов и поверх текста. `HighlightEffect` `<mark>`: по глифу, строке, участку или блоку, со скруглёнными углами, под текстом или над ним |
| Слои | `FillEffect` `<fill>`, `StrokeEffect` `<stroke>`, `OutlineEffect` `<outline>`, `ShadowEffect` `<shadow>`, `GlowEffect` `<glow>`, `InnerShadowEffect` `<inner-shadow>`, `ExtrudeEffect` `<extrude>` |
| Цвет и форма | `FilterEffect` `<filter>` (оттенки серого, насыщенность, тон, яркость, контраст, экспозиция, инверсия, сепия, тонирование, любая матрица), `UnionEffect` `<union>` (перекрытия сливаются в одну фигуру), `WarpEffect` `<warp>` (волнистый контур) |
| Типографика | `UppercaseEffect` `<uppercase>`, `LowercaseEffect` `<lowercase>`, `SmallCapsEffect` `<smallcaps>`, `SuperSubEffect` `<sup>` `<sub>`, `OpenTypeEffect` `<otf>`, `AxesEffect` `<axes>`, `SizeMatchEffect` `<size-match>`, `RubyEffect` `<ruby>` |
| Вёрстка | `TrackingEffect` `<tracking>`, `WordSpacingEffect` `<word-spacing>`, `CellWidthEffect` `<cell>`, `LineHeightEffect` `<line-height>`, `ParagraphSpacingEffect` `<para-spacing>`, `LineGapEffect` `<line-gap>`, `NoBreakEffect` `<nobr>`, `ListEffect` `<li>`, `IndentEffect` `<indent>`, `AlignEffect` `<align>`, `BoxTrimEffect` `<trim>`, `CurveEffect` `<curve>`, `DividerEffect` `<divider>` |
| Сокращение и направление | `EllipsisEffect` `<ellipsis>`, `TruncateEffect` `<truncate>`, `DirectionEffect` `<dir>` |
| Движение, ссылки, встраивание | См. [Движение](Motion.ru.md): `<wave>` и другие анимации, `<reveal>`, `<a>`, `<link>`, `<spoiler>`, `<sprite>`, `<obj>` |
| Формулы и таблицы | См. [Формулы](Math.ru.md): `<math>` и `\begin{tabular}` |

Эффекты без собственного тега, которые добавляют из выбора стилей или из кода: `MaterialEffect` (свой материал), `PaintOrderEffect` (рисование по глифам или по слоям), `ResolutionEffect` (более плотный атлас для крупных заголовков), `SemanticEffect` (слова для экранного диктора), `NoEffect`, а также эффекты движения и отклика из [Движения](Motion.ru.md).

Автоподбор размера может сэкономить место, прежде чем уменьшать шрифт: `FitBudgets` текста принимают `FitByTracking`, `FitByGlyphWidth` и `FitByLineHeight`, которые расходуются по порядку и ровно настолько, насколько нужно.

## Заливки

Каждая цветная поверхность слоя принимает **заливку** (paint): сплошной цвет, градиент или текстуру.

- **Градиенты** бывают линейными, радиальными и коническими; смешивают цвета в sRGB, в линейном свете, в Oklab или ступенями; за концами обрезаются, повторяются или отражаются.
- **Grain** у градиента (`LetteraGradient.Grain`) убирает полосы медленного градиента: он берёт цвета из более точной шкалы и добавляет зерно в одну 8-битную ступень, через чуть более тяжёлый вариант шейдера, которым пользуются только тексты с ним.
- **Охват** (span) говорит, к чему привязана заливка: `Glyph` начинает её заново на каждом глифе, `Line` — на каждой строке, `Range` растягивает на весь участок, `Block` — на всю рамку текста.
- **Наложение** (blend) рисует слой обычно или умножением, экраном, сложением или исключением.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesGradients : MonoBehaviour
{
    public LetteraText Title;
    public Texture2D Marble;

    void Start()
    {
        LetteraGradient dusk = LetteraGradient.Of(
            new Color32(255, 170, 40, 255), new Color32(240, 60, 130, 255), new Color32(70, 110, 255, 255));
        dusk.Mode = LetteraGradientMode.Oklab;

        var across = new FillEffect();
        across.SetPaint(LetteraPaint.Of(dusk, LetteraPaintSpan.Range, angle: 90));

        var rim = new StrokeEffect { Width = 0.05f };
        rim.SetPaint(LetteraPaint.Solid(new Color32(20, 20, 30, 255)));

        var stone = new FillEffect();
        stone.SetPaint(LetteraPaint.Of(Marble));

        Title.Styles.Add(LetteraStyle.ForTag(across, "dusk"));
        Title.Styles.Add(LetteraStyle.ForTag(rim, "rim"));
        Title.Styles.Add(LetteraStyle.ForTag(stone, "stone"));
        Title.MarkStylesDirty();
        Title.Text = "<rim><dusk>Evening</dusk></rim> and <stone>marble</stone>";
    }
}
```

**Именованные заливки.** Ассет `LetteraPaintLibrary` хранит именованные образцы. Назначьте его проекту в поле **Paints** в Project Settings, и тег слоя примет имя образца вместо цвета: `<fill=sunset>`, `<stroke=brand,0.05>`. Эффект слоя может также брать образцы из собственной библиотеки или собственного списка (`Catalog`). Неизвестное имя один раз предупреждает и использует заливку по умолчанию.

После собственных слотов слоя идут настройки заливки, по порядку: охват, форма, вписывание, края, наложение, угол, масштаб, смещение x, смещение y. Так `<fill=sunset,0,0,glyph>` начинает образец заново на каждом глифе.

Сплошная и градиентная заливки никогда не разбивают батч. Текстурная заливка или наложение, отличное от обычного, получает собственный материал, а порядок отрисовки сохраняется.

## Слои и их порядок

На глифе может быть сколько угодно слоёв. Они рисуются полосами, от дальних к ближним:

1. **Back:** тени и свечение.
2. **Rim:** обводки и контуры.
3. **Face:** собственная поверхность глифа и заливки поверх неё.
4. **Front:** внутренние тени.
5. **Overlay:** то, что эффект просит нарисовать сверху.

Внутри полосы более поздний стиль рисуется поверх более раннего. Свойство `Band` эффекта слоя переносит его в другую полосу. Фильтры действуют на то, что лежит под ними в списке стилей, и на поверхность глифа, поэтому фильтр в конце списка перекрашивает всё. `PaintOrderEffect` решает для всего текста, заканчивается ли один слой на всех глифах перед следующим (`LayerMajor`, по умолчанию) или все слои одного глифа перед следующим глифом (`GlyphMajor`) — для рукописных шрифтов, у которых соседи перекрываются.

Слои отходят от глифа не дальше чем на 1 em. Если слою нужно вокруг глифа больше места, чем хранит атлас, атлас расширяет поле этого глифа.

## Изменение вида во время игры

**Поменяйте поле и скажите, как далеко это заходит.** Свойства эффекта можно менять в любой момент. Затем `MarkEffectsDirty` применяет их без разбора текста, на указанном уровне: от `Mesh` (по умолчанию) до `Pose` для параметров движения:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesPulseStroke : MonoBehaviour
{
    public LetteraText Label;
    StrokeEffect m_Stroke;

    void Start()
    {
        m_Stroke = new StrokeEffect { Width = 0.02f };
        Label.Styles.Add(LetteraStyle.ForWholeText(m_Stroke));
        Label.MarkStylesDirty();
    }

    void Update()
    {
        m_Stroke.Width = 0.02f + 0.03f * Mathf.PingPong(Time.time, 1);
        Label.MarkEffectsDirty();
    }
}
```

**Удержите параметр на части участков.** Удержание (hold) берёт на себя один слот у участков, найденных запросом, не записывая поле эффекта и не разбирая текст. Отпустите его — и вернётся значение из разметки:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesPriceHold : MonoBehaviour
{
    public LetteraText Label;
    LetteraHold<Color32> m_Hold;

    void Start()
    {
        Label.Text = "Apples <color #price=#FFFFFF>1.20</color>, pears <color #price=#FFFFFF>0.95</color>";
        ColorEffect color = Label.GetEffect<ColorEffect>();
        m_Hold = Label.Ranges(color).WithLabel("price").Hold<Color32>(0);
    }

    public void ShowSale(bool on)
    {
        m_Hold.Value = new Color32(255, 80, 60, 255);
        m_Hold.Weight = on ? 1 : 0;   // 0 is the markup's value, 1 the hold's, between them a blend.
    }

    void OnDestroy() => m_Hold?.Release();
}
```

Запрос сужается через `WithLabel`, `WithPrimary`, `WithToken`, `Bare`, `Overlapping`, `Inside`, `Skip`, `Take` и `Matching`; `Collect` перечисляет найденное. Удержание сочетается со значением через `Replace`, `Add` или `Multiply`, а несколько удержаний одного слота складываются по приоритету. Реплики секвенсора и правила участков ([Движение](Motion.ru.md)) построены на удержаниях.

## Составные эффекты и пресеты

**`CompositeEffect`** ставит за одним тегом несколько эффектов. Значение его тега состоит из сегментов через точку с запятой, по одному на дочерний эффект, и в каждом сегменте — слоты этого эффекта:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesBadge : MonoBehaviour
{
    void Start()
    {
        var badge = new CompositeEffect(
            new HighlightEffect(),
            new ColorEffect { Color = new Color32(255, 255, 255, 255) },
            new BoldEffect());

        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForTag(badge, "badge", "#2A7;;"));
        label.MarkStylesDirty();
        label.Text = "Status: <badge>online</badge>, <badge=#C33;#FFE;>offline</badge>";
    }
}
```

**Пресеты эффектов.** Ассет `LetteraEffectPreset` (**Assets > Create > Lettera > Effect Preset**) хранит под именем один эффект, обычно составной. Его применяет `PresetEffect`; каждый пользователь получает собственную копию через `Instance`, так что изменение одного текста не задевает остальные. В выборе стилей он стоит в **Utility > Effect Preset**.

**Пресеты стилей** делятся целыми списками стилей, как показано [выше](#где-живут-стили).

## Участки без тегов

Некоторые матчеры находят участки в обычном тексте. В словаре по умолчанию их нет, потому что они меняют смысл обычного текста; добавляйте их из выбора стилей или из кода:

| Матчер | Что находит |
|---|---|
| `MarkdownMarkerMatcher` | `**bold**`, `*italic*`, `~~strike~~` — по правилам CommonMark для выбранного маркера |
| `MarkdownListMatcher`, `MarkdownLinkMatcher`, `CodeSpanMatcher` | `- списки`, `[ссылки](url)`, `` `код` `` |
| `BareUrlMatcher` | `https://...` и `www....` в тексте |
| `TriggerWordMatcher` | `@упоминания`, `#хештеги`: символ-триггер и слово |
| `LiteralMatcher` | Заданные строки, с необязательной заменой на их месте |
| `MatcherGroup` | Несколько матчеров под одним эффектом, чтобы один стиль отвечал и на `<i>`, и на `<em>`, и на `*...*` |
| `VoidTagMatcher` | Одиночный тег, со значением или без, у которого нет закрывающего |

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesChatLine : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForMatcher(new MarkdownMarkerMatcher("**"), new BoldEffect()));
        label.Styles.Add(LetteraStyle.ForMatcher(new TriggerWordMatcher("@"), new ColorEffect { Color = new Color32(80, 170, 255, 255) }));
        label.Styles.Add(LetteraStyle.ForMatcher(new BareUrlMatcher(), new LinkEffect()));
        label.MarkStylesDirty();
        label.Text = "**@ana** see https://example.com";
    }
}
```

## Свой эффект

Унаследуйтесь от `LetteraEffect` и опишите в `Apply`, что получает участок. У `LetteraAttributes` есть метод для каждого встроенного атрибута (`SetColor`, `SetWeight`, `SetItalic`, `MultiplySize`, `SetAlpha`, `SetFamily`, `SetLanguage`, `SetUnderline`, `SetOutline`, `SetShadow` и другие), а `LetteraSlots` читает значения тега с запасным значением:

```csharp
using System;
using System.Collections.Generic;
using FirstSymbol.Lettera;
using UnityEngine;

[Serializable]
[LetteraPicker("Look", "alert danger loud")]
public class StylesAlertEffect : LetteraEffect
{
    [SerializeField, LetteraEssential] Color32 m_Color = new Color32(255, 70, 50, 255);
    [SerializeField] float m_Grow = 1.15f;

    // Names the slots Apply reads, so the inspector can edit <alert=#FC0,1.0> with a form.
    public override IReadOnlyList<LetteraParamInfo> Parameters => s_Slots;

    static readonly LetteraParamInfo[] s_Slots =
    {
        LetteraParamInfo.ForSlot(0, "Color", LetteraParamKind.Color, "Text colour", defaultField: "m_Color"),
        LetteraParamInfo.ForSlot(1, "Grow", LetteraParamKind.Float, "How much bigger the text gets", 1, 2, 0.05f, defaultField: "m_Grow"),
    };

    public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
    {
        attributes.SetColor(start, end, slots.Color(0, m_Color));
        attributes.MultiplySize(start, end, slots.Float(1, m_Grow));
        attributes.SetWeight(start, end, 700, 0);
    }
}

public class StylesUseAlert : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForTag(new StylesAlertEffect(), "alert"));
        label.MarkStylesDirty();
        label.Text = "Saved. <alert>Disk almost full.</alert> <alert=#FC0,1.0>Check soon.</alert>";
    }
}
```

- Эффекты сериализуются по ссылке, поэтому помечайте их `[Serializable]` и храните состояние в полях.
- Эффект со списком или другим вложенным объектом переопределяет `Clone`, чтобы каждый текст получил свою копию.
- `[LetteraEssential]` отмечает поля, которые новый стиль показывает первыми; `[LetteraPicker(group, words)]` ставит эффект в группу выбора стилей (`Basics`, `Type`, `Lines`, `Look`, `Layout`, `Links`, `Utility` или `Custom`) и даёт слова, по которым его найти.
- **Слоты для инспектора.** Переопределите `Parameters` и опишите каждую позицию, которую читает `Apply`, через `LetteraParamInfo.ForSlot(позиция, имя, вид, подсказка, ...)`; позиции считаются с 0, как индекс, который вы передаёте в `slots`. Тогда панель тега, список обёртывания и форма **Defaults** на карточке стиля работают для вашего тега без кода редактора: цвет получает образец, число с `min` и `max` получает ползунок, длина принимает `units` (`LetteraSlotUnits`) и значение голого числа, а `ForEnum<T>` или `choices` дают выпадающий список. `defaultField` называет поле, которое даёт значение пустому слоту (`"m_Offset.x"` берёт компонент вектора), чтобы строка показывала, что будет применено; `defaultText` говорит то же словами. Эффект без описаний оставляет строку с сырым значением. Массив создавайте один раз, в статическом поле.
- **Скажите, что делает тег.** Переопределите `Description` (одно-два простых предложения) и `Examples` (`new LetteraTagExample("180", "Полукруг")`: значение после знака равенства и что оно даёт), а в `ForSlot` передайте `hint`, `example` и `unitLabel` (`"\u00b0"` для градусов). Они показываются рядом со слотами, в панели и в `lettera tags`. Эффект, не читающий значений, переопределяет `TakesNoParameters` и возвращает `true`, чтобы «параметров нет» отличалось от «ещё не описан».
- `Parameters` также публикует именованные параметры для секвенсора, правил и удержаний; `Stages` и методы `On...` позволяют эффекту вмешаться в этап перестройки. См. [Движение](Motion.ru.md#обработчики-этапов).

## Свой матчер

Унаследуйтесь от `LetteraMatcher` и забирайте участки в `Find`. Контекст отдаёт исходный текст как span; `IsFree` говорит, не держит ли отрезок уже другой матчер; `AddRawRange` добавляет участок над текстом, который остаётся видимым, а `ReplaceAndRange` показывает на его месте что-то другое:

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

// Every run of digits, such as prices and counts.
[Serializable]
public class StylesDigitsMatcher : LetteraMatcher
{
    public override void Find(LetteraMatchContext context)
    {
        ReadOnlySpan<char> source = context.Text;
        for (int i = 0; i < source.Length; i++)
        {
            if (!char.IsDigit(source[i])) continue;
            int end = i;
            while (end < source.Length && char.IsDigit(source[end])) end++;
            if (context.IsFree(i, end - i)) context.AddRawRange(i, end - i, null, null);
            i = end - 1;
        }
    }
}

public class StylesTabularDigits : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        LetteraStyle figures = LetteraStyle.ForMatcher(new StylesDigitsMatcher(), new OpenTypeEffect());
        figures.DefaultParameter = "tnum";   // Tabular figures, so the times line up.
        label.Styles.Add(figures);
        label.MarkStylesDirty();
        label.Text = "Lap 1: 1:11.01, lap 2: 1:09.88";
    }
}
```

`Priority` упорядочивает матчеры: у кого больше, тот забирает первым. Матчер, который умеет писать свой синтаксис (`CanWrap`, `WriteOpen`, `WriteClose`), позволяет командам поля вроде **Bold** вписывать его в исходный текст поля.

## Свой шейдер

**Assets > Create > Lettera > Custom Effect** создаёт шейдер и рядом с ним материал. В шейдере одна функция для правки, `LetteraCustom`: она получает расстояние глифа (0.5 на контуре), цвет, который запросил стиль, собственный цвет цветного глифа и положение в тексте, а возвращает цвет с предумноженной альфой. Один и тот же проход работает на Canvas и в мире, в конвейерах Built-in и Universal.

Используйте его через стиль **Custom Material**, то есть `MaterialEffect`:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class StylesStripes : MonoBehaviour
{
    public Material Stripes;   // The material Custom Effect made.

    void Start()
    {
        var label = GetComponent<LetteraText>();
        var custom = new MaterialEffect { Material = Stripes, Order = LetteraMaterialOrder.Replace };
        label.Styles.Add(LetteraStyle.ForTag(custom, "stripes"));
        label.MarkStylesDirty();
        label.Text = "Plain and <stripes>striped</stripes>";
    }
}
```

`Replace` рисует материал вместо поверхности глифа; `Keep` — отдельным слоем. Первый слот тега — оттенок, он передаётся как цвет вершины. Участок со своим материалом рисуется отдельным вызовом, поэтому разбивает батч.

## Ограничения

- Слияние (union) делается в атласе, а деформация контура — в шейдере стилизованного уровня; внутренние тени не деформируются.
- У обводок нет ограничения на острые углы: острый угол тянется так далеко, как достаёт поле расстояний.
- Изогнутый текст не рисует подчёркиваний и зачёркиваний.
- Блочная подсветка скругляет только выпуклые углы.
- Цветовые фильтры работают в цветовом пространстве шейдера, а в линейном проекте оно линейное.
- Шейдер Custom Effect написан только для конвейеров Built-in и Universal, не для HDRP.
- Отдельный вид для видимых ограничителей разметки (тегов в поле в своём оформлении) не реализован.
