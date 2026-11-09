# Движение

[English version](Motion.md) · [Все руководства](Lettera.ru.md)

Текст в Lettera может двигаться, появляться по частям и отвечать на указатель и клавиатуру. Всё это работает в **проходе позы**: вершины, оставшиеся от последней перестройки, каждый кадр сдвигаются, тонируются и делаются прозрачнее — без разбора, шейпинга и вёрстки и без выделения памяти. Текст, в котором ничего не движется, вообще не посещается.

- [Анимации](#анимации)
- [Общие настройки анимаций](#общие-настройки-анимаций)
- [Появление: печатная машинка и не только](#появление-печатная-машинка-и-не-только)
- [Бегущая строка](#бегущая-строка)
- [Секвенсор](#секвенсор)
- [Animator и связи параметров](#animator-и-связи-параметров)
- [Timeline](#timeline)
- [Участки, которые отвечают](#участки-которые-отвечают)
- [Ссылки и спойлеры](#ссылки-и-спойлеры)
- [Правила участков](#правила-участков)
- [Встроенные спрайты и объекты](#встроенные-спрайты-и-объекты)
- [Семантика](#семантика)
- [Ослабленное движение](#ослабленное-движение)
- [Своя анимация](#своя-анимация)
- [Обработчики этапов](#обработчики-этапов)
- [Ограничения](#ограничения)

## Анимации

Эти теги есть в словаре по умолчанию, поэтому работают без всякой настройки. Слоты необязательны и идут в указанном порядке; пустой слот оставляет значение эффекта.

| Тег | Эффект | Что делает | Слоты |
|---|---|---|---|
| `<wave>` | `WaveMotion` | Глифы покачиваются вверх-вниз и вбок | амплитуда по y, амплитуда по x, частота, шаг |
| `<wobble>` | `WobbleMotion` | Глифы качаются и сплющиваются | поворот, сплющивание, частота, шаг |
| `<shake>` | `ShakeMotion` | Глифы дрожат, замирая между скачками | амплитуда, частота, поворот |
| `<bounce>` | `BounceMotion` | Глифы подпрыгивают, не опускаясь ниже места покоя | высота, частота, шаг, доля цикла в воздухе |
| `<float>` | `FloatMotion` | Глифы плавно дрейфуют по гладкому шуму | амплитуда по y, амплитуда по x, частота, шаг |
| `<spin>` | `SpinMotion` | Глифы вращаются | частота, шаг |
| `<pulse>` | `PulseMotion` | Глифы растут и сжимаются | величина, частота, шаг |
| `<pendulum>` | `PendulumMotion` | Глифы качаются, подвешенные за верхний край | поворот, частота, шаг |
| `<deform>` | `DeformMotion` | Контур течёт | амплитуда, масштаб, смещение |
| `<glitch>` | `GlitchMotion` | Глифы дёргаются и расслаиваются на цветные края вспышками | расслоение, частота, вероятность, рывок |
| `<scramble>` | `ScrambleMotion` | Случайные символы оседают в настоящий текст, по порядку чтения | секунды, частота, прогресс |
| `<roll>` | `RollMotion` | Символы прокручиваются на барабане и останавливаются на своём значении, как у счётчика | секунды, начало, отставание, прокрутка |

Длины задаются в em. **Частота** — циклов в секунду; **шаг** — на сколько циклов каждый глиф отстаёт от соседа: именно он превращает покачивание в волну.

```text
<wave=0.3,0,2>Higher and faster</wave>  <shake=0.02,12>nervous</shake>  <roll=1.5>1 250 000</roll>
```

Анимации сочетаются: `<wave><pulse>both</pulse></wave>`. Выделение, каретка, проверка попаданий и копируемый текст остаются там, куда текст поставила вёрстка; движется только то, что нарисовано. `<scramble>` и `<roll>` меняют то, что видно, а настоящие символы оставляют на месте — для копирования и экранного диктора.

## Общие настройки анимаций

Каждая анимация наследуется от `LetteraMotionEffect` и имеет:

| Свойство | Что делает |
|---|---|
| `Unit` | Что движется одним телом: `Cluster`, `Word`, `Line` или весь `Range`. |
| `Clock` | `Scaled` (следует `Time.timeScale`), `Unscaled` или `Manual`, где входом служит `Phase`. |
| `Phase` | При ручных часах — время; иначе — смещение старта. |
| `Strength` | Масштабирует всё движение; 0 — неподвижно. |
| `Seed` | Для анимаций со случайностью, чтобы два текста не двигались одинаково. |
| `ReducedMotionPolicy` | Что происходит, когда игрок просит меньше движения: `Rest` останавливает, `Keep` оставляет. |

Чтобы изменить числа идущей анимации, задайте их и вызовите `MarkEffectsDirty(DirtyLevel.Pose)` — он переделывает только проход позы:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionCalmDown : MonoBehaviour
{
    public LetteraText Title;
    WaveMotion m_Wave;

    void Start()
    {
        m_Wave = new WaveMotion { Amplitude = 0.25f, Rate = 1.5f, Unit = LetteraMotionUnit.Cluster };
        Title.Styles.Add(LetteraStyle.ForWholeText(m_Wave));
        Title.MarkStylesDirty();
    }

    void Update()
    {
        // Settles over four seconds.
        m_Wave.Strength = Mathf.Clamp01(1 - Time.timeSinceLevelLoad / 4);
        Title.MarkEffectsDirty(DirtyLevel.Pose);
    }
}
```

В карточке Interaction инспектора есть **Preview Motion**: движение запускается в режиме редактирования, и ничего не записывается в сцену.

## Появление: печатная машинка и не только

`RevealEffect` показывает начальную часть того, что покрывает. Его **reach** — граница: `100%` показывает всё, `50%` — половину, а голое число считает единицы. **Unit** — это `Cluster`, `Word`, `Line` или `Paragraph`. **Entrance** говорит, как появляется каждая единица: `Cut`, `Fade`, `Slide`, `Scale`, `Spin`, `Flip`, `Skew`, `Stretch`, `Spiral`, `Pop`, `Drop`, `Rain`, `Burst`, `Domino`, `Swing`, `Wave`, `Shake`, `Glitch`, `Chaos` или `Tint`, либо имя из библиотеки появлений.

В разметке слоты такие: появление, граница, единица — `<reveal=pop,40%,word>`. Печатная машинка двигает границу со временем:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionTypewriter : MonoBehaviour
{
    public LetteraText Line;
    public float CharactersPerSecond = 20;
    RevealEffect m_Reveal;
    float m_Shown;

    void Start()
    {
        m_Reveal = new RevealEffect { Entrance = "fade", Reach = "0", Unit = LetteraRevealUnit.Cluster };
        m_Reveal.UnitShown += (start, end) => { /* a key click sound here */ };
        m_Reveal.Settled += () => Debug.Log("All shown");
        Line.Styles.Add(LetteraStyle.ForWholeText(m_Reveal));
        Line.Text = "The door was open, and nobody was home.";
    }

    void Update()
    {
        m_Shown += CharactersPerSecond * Time.deltaTime;
        m_Reveal.Reach = m_Shown.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        Line.MarkEffectsDirty(DirtyLevel.Pose);
    }

    public void Skip() => m_Reveal.SnapToTarget();
}
```

- Выключенный `Reflow` сохраняет место скрытого текста, так что строки не сдвигаются по мере появления слов; включённый — смыкает его.
- `Independent` заполняет каждый участок отдельно, а не одной общей границей.
- Если граница движется назад, единицы уходят: с появлением **Leave** записи, если оно есть.
- Ассеты `LetteraEntranceLibrary` хранят именованные появления с их длительностью и сглаживанием — для собственных появлений проекта; **Project Settings > FirstSymbol > Lettera > Animation & Interaction > Default Entrance Library** делает библиотеку доступной всем текстам.

Границей могут управлять и секвенсор или клип Timeline, без кода.

## Бегущая строка

`MarqueeEffect` прокручивает текст на Canvas вбок через его рамку, когда он не помещается: бегущая строка. `Mode` — `Loop` или `BackAndForth`; `Speed`, `Gap` и `Pause` задают темп. Эффект действует на весь текст; выключите перенос слов, чтобы строка могла выйти за рамку.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionTicker : MonoBehaviour
{
    void Start()
    {
        var ticker = GetComponent<LetteraText>();
        ticker.WordWrap = false;
        ticker.Styles.Add(LetteraStyle.ForWholeText(new MarqueeEffect { Mode = LetteraMarqueeMode.Loop }));
        ticker.Text = "Breaking: the river is high, the bridge is closed, take the north road until Friday.";
    }
}
```

## Секвенсор

`LetteraSequencer` плавно меняет любой опубликованный параметр любого эффекта текста по одной временной шкале, без кода: **реплика** (cue) ведёт параметр от одного значения к другому за своё время, на выбранных участках, со сдвигом от единицы к единице. Всё идёт через удержания, поэтому ни одно поле эффекта не записывается, а остановка возвращает значения разметки.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionIntro : MonoBehaviour
{
    void Start()
    {
        var title = GetComponent<LetteraText>();
        title.Styles.Add(LetteraStyle.ForWholeText(new WaveMotion { Amplitude = 0 }));   // Style 0.
        title.Text = "Welcome back";

        var sequencer = gameObject.AddComponent<LetteraSequencer>();
        sequencer.Cues.Add(new LetteraCue
        {
            Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" },
            From = LetteraValue.Of(0),
            To = LetteraValue.Of(0.3f),
            UnitSeconds = 0.6f,     // How long each glyph's ramp takes.
            StepSeconds = 0.05f,    // How far each glyph starts after the one before.
            Curve = LetteraEase.OutBack,
        });
        sequencer.Repeat = LetteraRepeat.Retrace;
        sequencer.Cycles = 2;
        sequencer.Finished += () => Debug.Log("Intro done");
        sequencer.Play();
    }
}
```

- `Target` реплики называет стиль текста по индексу, путь вниз по составным эффектам (`Node`) и параметр по имени. Выбор параметра в инспекторе перечисляет их и говорит, число это или цвет.
- `Select` сужает реплику до части участков: по метке, основному значению, токену, пересечению, `Skip` и `Take`.
- `Repeat` — это `Restart`, `Retrace` (назад тем же путём), `Return` или `Accumulate`; `Cycles` задаёт число проходов, а отрицательное число повторяет без конца.
- Управляют им `Play`, `Pause`, `Stop`, `Seek` и `Progress`. Присвоенный `Progress` показывает ровно этот момент, поэтому `AnimationCurve` или ползунок могут им управлять.
- **Tools > FirstSymbol > Lettera > Sequencer** (и кнопка в его инспекторе) показывает реплики по строкам: перетаскивание и растяжение с привязкой, разрезание по указателю времени, копирование, вставка, отключение, масштаб и перемотка. Пробел запускает воспроизведение.

## Animator и связи параметров

Animator может анимировать `color` и прозрачность текста, как у любого Graphic; кривая прозрачности стоит только прохода прозрачности. Для параметра эффекта `LetteraParamLink` даёт этому одному параметру поле (`Value`, `ColorValue` или `VectorValue`), к которому можно привязать кривые анимации:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionLinkGlow : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForWholeText(new GlowEffect { Radius = 0.1f }));   // Style 0.

        var link = gameObject.AddComponent<LetteraParamLink>();
        link.Target.Style = 0;
        link.Target.Parameter = "Radius";
        link.Value = 0.3f;   // An Animator can now record and play this field.
    }
}
```

## Timeline

Если установлен пакет Timeline, **Add Track > Lettera** в окне Timeline предлагает три дорожки. Без пакета ничего из этого не компилируется.

| Дорожка | К чему привязана | Её клипы |
|---|---|---|
| Text Track | Объект текста | Пока указатель времени над клипом, текст говорит слова клипа, по желанию печатая их за длину клипа. Собственное значение текста не трогается и возвращается в промежутке. |
| Sequence Track | `LetteraSequencer` | Проигрывают секвенсор от одного положения до другого. |
| Parameter Track | `LetteraParamLink` | Ведут связанный параметр от одного значения к другому по кривой. |

Перемотка внутри текстового клипа ничего не перестраивает. Пример Timeline собирает короткую катсцену со всеми тремя дорожками из кода.

## Участки, которые отвечают

**Эффект участка** (`LetteraRangeEffect`) заставляет свои участки отвечать на указатель и клавиатуру. В комплекте `LinkEffect` (`<a>`, `<link>`), `SpoilerEffect` (`<spoiler>`) и `ActionEffect`, который только поднимает события: он подходит для упоминаний, хештегов и собственных кнопок.

События приходят через маршрутизатор текста — для всех участков или для одной темы:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionClickableWords : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        var action = new ActionEffect();
        label.Styles.Add(LetteraStyle.ForTag(action, "item"));
        label.MarkStylesDirty();
        label.Text = "You found <item=sword>a sword</item> and <item=key>a rusty key</item>.";

        action.Events.Activated += e => Debug.Log("Picked up " + e.Primary);

        LetteraRangeRouter router = LetteraRangeRouter.Of((Component)label);
        router.All.Entered += e => Debug.Log("Pointing at " + e.Primary);
        router.All.MenuRequested += e => e.Stop();
    }
}
```

- Событие говорит, какой это участок (`Id`, `Start`, `End`, `Primary`, `Label`), как оно пришло (`PointerKind`, `ViaKeyboard`, `Button`) и где. `Stop()` прекращает его доставку; `PreventDefault()` пропускает собственное действие эффекта, например открытие ссылки.
- У перекрывающихся участков одна цель: по приоритету, затем по стилю, затем самый короткий; события проходят вниз и обратно вверх через вложенные участки.
- `HitPadding` расширяет область попадания участка; на сенсорном экране добавляется свой запас. Долгое нажатие поднимает `PressHoldProgress` и `MenuRequested`.
- **Маршрут текста** решает, что делает нажатие на остальной текст: `Auto` забирает его и передаёт родителям то, что не забрал ни один участок, `Ranges` пропускает нажатия всюду, кроме участков, `Observe` отдаёт нажатие и участкам, и тому, что лежит за текстом.
- **Клавиатура:** Tab и Shift+Tab перебирают фокусируемые участки текста только для чтения, Enter активирует, а рамка фокуса видна, когда фокус пришёл с клавиатуры. `LetteraRangeNavigator` включает участки текста в навигацию UI Unity. В поле Ctrl+Enter или Cmd+Enter переходит по ссылке у каретки.
- В тексте только для чтения щелчок активирует, а перетаскивание выделяет; в поле обычное нажатие ставит каретку, а с модификатором — переходит по ссылке.
- Ассеты `LetteraRangeTopic` объединяют участки из многих текстов, чтобы один слушатель слышал их все.

## Ссылки и спойлеры

`<a=https://unity.com>Unity</a>` выглядит как ссылка и открывает адрес по щелчку; `<link=...>` открывает его без оформления. Адреса открываются через `LetteraLinks.Open`, который пропускает только схемы из **Project Settings > FirstSymbol > Lettera > Animation & Interaction > Link Schemes** (по умолчанию `http`, `https`, `mailto` и `tel`) и отклоняет остальные с предупреждением; к `www.` добавляется `https://`. В вебе адрес открывает собственный обработчик отпускания на странице, поэтому блокировщики всплывающих окон его пропускают.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionLinkPolicy : MonoBehaviour
{
    void OnEnable() => LetteraLinks.Opening += Check;
    void OnDisable() => LetteraLinks.Opening -= Check;

    // Every link passes through here first.
    static void Check(LetteraLinkRequest request)
    {
        if (request.Url.Contains("example.org")) request.Refused = true;
    }
}
```

Выключенный `LinkEffect.OpenOnActivate` не даёт ссылкам открываться, а событие `Followed` сообщает, когда ссылка была открыта. `LetteraLinks.Opener` заменяет сам способ открытия адреса.

`<spoiler>` рисует покрывало над текстом, который под ним вообще не рисуется, так что ничего не просвечивает; касание его снимает. `CoverColor`, `FadeSeconds` и `CopyPolicy` (`Mask`, `Skip` или `Keep` — что получит простое копирование) его настраивают.

## Правила участков

`LetteraRangeRule` меняет параметры участка, пока выполняется условие: наведение, нажатие, фокус или недоступность, либо ваш собственный сигнал, — с огибающей, которая плавно входит и выходит. Стоит правило столько же, сколько уровень параметра, поэтому подсветка при наведении стоит только позы:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionHoverTint : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        var tint = new RangeTintEffect { Color = new Color(1f, 0.55f, 0.2f, 1f) };
        var action = new ActionEffect();
        action.Rules.Add(new LetteraRangeRule
        {
            When = new SignalIs(LetteraSignals.Hovered),
            Envelope = LetteraEnvelopeKind.Transition,
            Enter = 0.1f,
            Exit = 0.3f,
            Changes = { new LetteraRangeChange { Target = tint, Parameter = "Weight", ToNumber = 1 } },
        });
        label.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(action, tint), "hot"));
        label.MarkStylesDirty();
        label.Text = "Point at <hot>this</hot> or <hot>that</hot>.";
    }
}
```

Условия сочетаются через `AllOf`, `AnyOf` и `NotOf`; `ScalarAtLeast` читает сигнал, который публикуете вы. В инспекторе правило читается как предложение, с переключателями состояний и выбором эффекта и параметра. В Play mode текст с участками показывает состояние каждого участка.

## Встроенные спрайты и объекты

`<sprite=name>` ставит спрайт в строку одним символом: он переносится, выделяется, копируется своим альтернативным текстом, движется, исчезает и появляется вместе с соседями. Это один quad в собственном меше текста. `<obj=name>` ставит префаб или забирает объект сцены туда, где оказался его символ.

Имена ищутся в собственных записях эффекта, затем в его библиотеке, затем в **Inline Library** проекта в **Project Settings > FirstSymbol > Lettera > Animation & Interaction**. Ассет `LetteraInlineLibrary` (**Assets > Create > Lettera > Inline Library**) хранит записи со спрайтом или объектом, альтернативным текстом, размером и подъёмом в em и оттенком.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class MotionInlineIcons : MonoBehaviour
{
    public Sprite Coin, Heart;

    void Start()
    {
        var label = GetComponent<LetteraText>();
        var icons = new InlineEffect();
        icons.Items.Add(new LetteraInlineEntry { Key = "coin", Sprite = Coin, Alt = "coins" });
        icons.Items.Add(new LetteraInlineEntry { Key = "heart", Sprite = Heart, Alt = "lives", Size = 1.2f });
        label.Styles.Add(LetteraStyle.ForMatcher(new InlineMediaMatcher("icon"), icons));
        label.MarkStylesDirty();
        label.Text = "<icon=coin> 250   <wave><icon=heart> 3</wave>";
    }
}
```

## Семантика

`SemanticEffect` даёт участку роль (ссылка, кнопка, заголовок, упоминание, код, формула и другие), подпись, значение и действия, которые он принимает. `LetteraSemanticTree.Of(text)` описывает участки текста для вашего собственного моста к средствам доступности: роли, подписи, состояния, прямоугольники на экране и действия — всё обновляется вместе с участками. Ссылки, формулы и встроенные элементы сами получают роли Link, Math и Image.

## Ослабленное движение

`LetteraAccessibility.ReducedMotion` учитывается везде: анимации с `ReducedMotionPolicy` `Rest` замирают, появления наступают сразу, а секвенсоры с `HonorReducedMotion` перескакивают в конец. Значение следует настройке системы, пока его не задаст скрипт, а в вебе — настройке браузера. `LetteraAccessibility.SteadyCaret` останавливает мигание каретки.

## Своя анимация

Унаследуйтесь от `LetteraMotionEffect`. `Resolve` один раз читает числа участка из его слотов; `PoseBody` каждый кадр двигает одно тело и должен оставаться чистым: без выделения памяти и без объектов Unity. Назовите слоты частоты и шага, и фаза `Phi` придёт уже с их учётом:

```csharp
using System;
using FirstSymbol.Lettera;
using UnityEngine;

// Glyphs lean left and right like grass in the wind. Slots: lean in degrees, rate, step.
[Serializable]
public class MotionGrassMotion : LetteraMotionEffect
{
    [SerializeField] float m_Lean = 10, m_Rate = 0.6f, m_Step = 0.12f;

    protected override int RateSlot => 1;
    protected override int StepSlot => 2;

    public override void Resolve(in LetteraSlots slots, ref LetteraMotionValues values)
    {
        values.V0 = Slot(slots, 0, m_Lean);
        values.V1 = Slot(slots, 1, m_Rate);
        values.V2 = Slot(slots, 2, m_Step);
    }

    public override void PoseBody(in LetteraMotionContext context, ref LetteraGlyphPose pose)
    {
        float lean = context.Values.V0 * Mathf.Sin(context.Phi * Mathf.PI * 2);
        pose.Turn(lean, context.Box.At(0.5f, 0));   // About the glyph's foot.
    }

    public override float MaxReachEm(in LetteraMotionValues values) => 0.3f;
}

public class MotionUseGrass : MonoBehaviour
{
    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Styles.Add(LetteraStyle.ForTag(new MotionGrassMotion(), "grass"));
        label.MarkStylesDirty();
        label.Text = "<grass>A field of tall letters</grass>";
    }
}
```

`MaxReachEm` сообщает отсечению и маскам, как далеко глиф может уйти со своего места. У `LetteraGlyphPose` есть `Move`, `Turn`, `Scale` и `Skew` относительно любой точки, а также `Alpha` и `Tint`.

## Обработчики этапов

Эффект может вмешаться в любой этап перестройки: переопределите `Stages` нужными флагами (`Analyzed`, `Shaped`, `LinesBroken`, `Placed`, `Mesh`, `Pose`) и соответствующие `OnAnalyzed`, `OnShaped`, `OnLinesBroken`, `OnPlaced`, `OnMesh` и `OnPose`. Каждый получает вид на данные этапа для своего участка, действительный только во время вызова. Эффект, который ни о чём не просит, ничего не стоит.

## Ограничения

- Проверка попадания остаётся в положении покоя: область подпрыгивающей ссылки за ней не движется. Текст в мире, у которого движется transform, заново сливает весь свой батч. Бегущая строка работает только на Canvas.
- `<scramble>` и `<roll>`, пока идут, сводят слои и заменяют кластеры целиком, поэтому связные буквы стоят раздельно, пока не осядут.
- Выделение со встроенным элементом копируется простым текстом и при вставке не превращается обратно в элемент. Слои вроде обводки и тени спрайты пропускают; повёрнутые в атласе спрайты и поворот спрайта не применяются. Объект появляется только в Play mode или во время предпросмотра. Подчёркивание спойлера рисуется под его покрывалом.
- Обработчики этапов: `OnAnalyzed` вызывается после шейпинга, поэтому скрытый кластер не меняет шейпинг соседей. `OnPlaced` не может добавить глиф. Встроенные анимации не работают через `OnPose`.
- Цель правила внутри пресета стилей не переносится в собственную копию общих стилей текста: такое правило ничего не находит.
- Не сделано: собственные огибающие, распознаватели жестов на участке и фильтры по темам в запросах.
- Маршрут Auto забирает весь прямоугольник; текст за ним получает нажатие только с `Ranges` или `Observe`. `Observe` и пути через родительские `Button` и `ScrollRect` проверялись вызовом обработчиков, а не настоящим модулем ввода.
- Если длинный текст разбит на много частей, прозрачность через `CrossFadeAlpha` доходит только до первой части до следующей перестройки, а движение части переписывает весь её меш.
- Длинный текст с бегущей строкой, появлением или встроенным объектом размещает все строки, а не только те, что рядом с видимой областью.
- Плеер с управляемым удалением кода на уровне `High` не запускался с эффектами, до которых можно дойти только через сериализованные ссылки; у этих типов стоит `[Preserve]`.
