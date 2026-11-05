# Шрифты

[English version](Fonts.md) · [Все руководства](Lettera.ru.md)

Lettera сама читает файлы шрифтов через HarfBuzz и рисует все глифы из одного общего атласа. Нет ни отдельного материала на каждый шрифт, ни атласа, который нужно запекать заранее: глиф растеризуется, когда он впервые понадобился тексту, а шрифты никогда не разбивают батч.

- [Совсем без шрифта](#совсем-без-шрифта)
- [Ассет шрифта](#ассет-шрифта)
- [Стеки шрифтов](#стеки-шрифтов)
- [Системные ассеты шрифтов](#системные-ассеты-шрифтов)
- [Насыщенность, курсив и вариативные шрифты](#насыщенность-курсив-и-вариативные-шрифты)
- [Возможности OpenType](#возможности-opentype)
- [Настройка начертания](#настройка-начертания)
- [Память](#память)
- [Подмножества](#подмножества)
- [Атлас: SDF и MSDF](#атлас-sdf-и-msdf)
- [Пиксельные шрифты](#пиксельные-шрифты)
- [Цветные шрифты и эмодзи](#цветные-шрифты-и-эмодзи)
- [Веб](#веб)
- [Ограничения](#ограничения)

## Совсем без шрифта

Текст без **Font** и без **Font Stack** рисуется шрифтом операционной системы по умолчанию. Символ, которого в нём нет, ищется в других шрифтах, установленных в системе: арабский, деванагари, иероглифы и эмодзи рисуются даже в пустом проекте. На Android Lettera читает собственную конфигурацию шрифтов устройства, поэтому китайский и японский телефон рисуют иероглифы каждый в своих формах.

**Project Settings > Lettera > Defaults** меняет это для всего проекта:

| Настройка | Что делает |
|---|---|
| **Default Font** | Начертание для текста без собственного шрифта — вместо системного. |
| **No System Fallback** | Больше не искать в шрифтах системы. Символ, которого нет ни в одном назначенном шрифте, покажется пустым прямоугольником. |
| **No Emoji** | Не брать эмодзи из цветного начертания: они идут по обычной цепочке шрифтов. |
| **Web Fallback Font** | Заменяет встроенный веб-шрифт. См. [Веб](#веб). |

## Ассет шрифта

Выделите один или несколько файлов `.ttf`, `.otf`, `.ttc` или `.otc` и выберите **Assets > Create > Lettera > Font Asset From Selection**. Ассет хранит байты файла; шага импорта нет, настраивать больше нечего. Коллекция `.ttc` содержит несколько начертаний, нужное выбирает **Face Index** ассета.

Назначьте ассет в поле **Font** текста. Из кода подойдёт любой `LetteraFontAsset`:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsAssign : MonoBehaviour
{
    public LetteraFontAsset Heading;

    void Start()
    {
        var label = GetComponent<LetteraText>();
        label.Font = Heading;
        Debug.Log("Family: " + Heading.FamilyName);
    }
}
```

**Во время игры** шрифт можно получить из байтов — скачанных или из asset bundle — или из файла на диске. Файл читается там, где лежит, и отображается в память, где платформа это позволяет, так что его байты не занимают управляемую память:

```csharp
using System.IO;
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsAtRunTime : MonoBehaviour
{
    public LetteraText Label;
    public TextAsset FontBytes;   // A .ttf renamed to .bytes.

    void Start()
    {
        // From an array: the array is kept and must not change afterwards.
        LetteraFont fromBytes = LetteraFont.CreateFromBytes(FontBytes.bytes);

        // From a file: nothing is copied into memory.
        string path = Path.Combine(Application.persistentDataPath, "Downloaded.otf");
        LetteraFont fromFile = File.Exists(path) ? LetteraFont.CreateFromFile(path) : null;

        Label.Font = fromFile != null ? fromFile : fromBytes;
    }
}
```

Если в шрифте текста нет какого-то символа, он всё равно придёт из шрифтов системы, если проект этого не отключил.

## Стеки шрифтов

Стек — это список **семейств**, которые перебираются по порядку для каждого символа: рисует первое семейство, у которого есть нужный глиф. Стек пригодится для фирменного шрифта со шрифтом иконок рядом, для отдельного шрифта под каждую письменность или чтобы дать семейству его жирное и курсивное начертания.

Создайте стек через **Assets > Create > Lettera > Font Stack** или выделите несколько ассетов шрифтов и выберите **Font Stack From Selection, Each A Fallback** (каждый — отдельное семейство) либо **Font Stack From Selection, One Family** (одно семейство, в котором все они — начертания).

| Часть | Что это |
|---|---|
| **Primary** | Обычное начертание семейства. |
| **Faces** | Остальные насыщенности и курсивы. `<b>` и `<i>` выбирают из них, прежде чем что-то синтезировать. |
| **Name** | Что выбирает `<font=Name>`. Если пусто, берётся имя семейства из основного начертания. |
| **Preferred Language** | Тег BCP 47, например `ja` или `zh-Hans`: для текста на этом языке семейство пробуется первым. |
| **Fallback Stack** | Другой стек, который пробуется после семейств этого. |

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsStack : MonoBehaviour
{
    public LetteraFontAsset Regular, Bold, Italic, Icons, Japanese;

    void Start()
    {
        var stack = ScriptableObject.CreateInstance<LetteraFontStack>();

        var main = new LetteraFontFamily { Primary = Regular, Name = "Body" };
        main.Faces.Add(Bold);
        main.Faces.Add(Italic);
        stack.Families.Add(main);

        stack.Families.Add(new LetteraFontFamily { Primary = Icons, Name = "Icons" });
        stack.Families.Add(new LetteraFontFamily { Primary = Japanese, PreferredLanguage = "ja" });

        var label = GetComponent<LetteraText>();
        label.FontStack = stack;
        label.Text = "<b>Saved</b> <font=Icons>\uE002</font> <lang=ja>保存しました</lang>";
    }
}
```

Если у текста заданы и **Font**, и **Font Stack**, сначала идёт шрифт, а за ним стек.

Страница стека в инспекторе показывает, что покрывает каждое семейство, а тестовая строка — какое семейство отвечает за каждый отрезок образца, с пометкой недостающих символов.

## Системные ассеты шрифтов

`LetteraSystemFont` называет шрифт, который есть в системе игрока, отдельно для каждой платформы, и не несёт байтов. Создаётся через **Assets > Create > Lettera > System Font Asset**. Ничего не загружается, пока текст его не использует.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsSystem : MonoBehaviour
{
    LetteraSystemFont m_Serif;

    void Start()
    {
        m_Serif = ScriptableObject.CreateInstance<LetteraSystemFont>();
        m_Serif.Windows = new[] { "Georgia", "Times New Roman" };
        m_Serif.MacOS = new[] { "Georgia", "Times" };
        m_Serif.IOS = new[] { "Georgia" };
        m_Serif.Android = new[] { "Noto Serif", "serif" };
        m_Serif.Linux = new[] { "DejaVu Serif", "Liberation Serif" };
        m_Serif.Common = new[] { "Noto Serif" };   // Tried on every platform after its own list.
        m_Serif.Weight = 300;                      // A light cut where the family has one.
        GetComponent<LetteraText>().Font = m_Serif;
    }

    void Update()
    {
        // Null until a text has used it, or when no name in the lists is installed.
        if (m_Serif.ResolvedFamily != null) Debug.Log("Found " + m_Serif.ResolvedFamily + " at " + m_Serif.ResolvedPath);
    }
}
```

Имена могут быть именами семейств или файлов. На Android общие имена `sans-serif`, `serif` и `monospace`, псевдонимы из конфигурации и Roboto находят собственные семейства системы.

**Weight** принимает число CSS от 100 до 900: текст без собственной насыщенности рисуется ближайшим установленным начертанием или этой точкой вариативного шрифта, и шрифты, к которым система обращается за недостающими символами, рисуются с той же насыщенностью. Настройки каждой платформы (`WindowsSettings`, `AndroidSettings` и остальные) могут задать свой **Weight**, вписать свои верхний и нижний выносные элементы и межстрочный зазор, если у системного шрифта они там неверны, и задать пиксельную сетку для шрифта, нарисованного пикселями только в этой системе. Изменив настройки платформы из кода, вызовите `PlatformSettingsChanged()`.

На странице ассета в инспекторе есть вкладка для каждой платформы и кнопка **Re-Resolve**.

## Насыщенность, курсив и вариативные шрифты

`<b>` просит насыщенность 700, `<i>` — курсив. Сначала Lettera ищет настоящее начертание: в **Faces** семейства стека, среди начертаний системного семейства или по оси вариативного шрифта. Только если его нет, она синтезирует: утолщает контур (`SyntheticBoldStrength` в ассете шрифта) или наклоняет (`SyntheticItalicSlant`, в процентах высоты глифа).

`<b>` принимает насыщенность и режим: `<b=600>`, `<b=700,f>` — принудительно синтетический жирный, `<b=700,r>` — только настоящие начертания. `<i=12>` задаёт наклон синтетического курсива.

**Вариативные шрифты** двигаются по своим осям на участке с помощью `<axes>`. Значения идут по порядку — `wght`, `wdth`, `ital`, `slnt` и `opsz` — или парами с именем для любой другой оси. Значение бывает абсолютным (`640`), в процентах от значения оси по умолчанию (`120%`), смещением от него (`+80`) или `~`, чтобы ось не трогать:

```text
<axes=100>Thin</axes> <axes=900>Black</axes> <axes=400,75%>Condensed</axes> <axes=GRAD 50>Graded</axes>
```

Значения ограничиваются пределами оси, а ось, которой у шрифта нет, пропускается.

**Вариант шрифта** (`LetteraFontVariant`, **Assets > Create > Lettera > Font Variant**) — закреплённая точка на осях вариативного шрифта, сохранённая ассетом. Его можно положить в **Faces** семейства стека как отдельную насыщенность, чтобы его выбирал `<b>`:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsVariant : MonoBehaviour
{
    public LetteraFont Variable;   // A font asset with a wght axis.

    void Start()
    {
        var semiBold = ScriptableObject.CreateInstance<LetteraFontVariant>();
        semiBold.Source = Variable;
        semiBold.Axes.Add(new LetteraFontVariant.AxisValue { Tag = "wght", Value = 600 });
        GetComponent<LetteraText>().Font = semiBold;
    }
}
```

## Возможности OpenType

`<otf>` включает и выключает возможности шрифта по их четырёхбуквенным тегам, с необязательным значением: `<otf=tnum>` — моноширинные цифры, `<otf=smcp,onum>`, `<otf=-liga>` — без лигатур, `<otf=cv02 3>` — третья форма символьного варианта. Возможности, которых у шрифта нет, ничего не делают.

Некоторые теги работают через возможности, если они есть у шрифта, и синтезируют результат иначе: `<smallcaps>`, `<sup>`, `<sub>`.

`<size-match=XHeight>` или `<size-match=CapHeight>` масштабирует запасные шрифты, чтобы их высота строчных или прописных совпадала с основным шрифтом, как `font-size-adjust` в CSS. Метрику задаёт **Project Settings > Lettera > Defaults > Size Match**; флажок **Normalize Size** в ассете шрифта делает то же для этого шрифта всякий раз, когда он оказывается запасным.

## Настройка начертания

В ассете шрифта есть настройки, которые меняют набор, не трогая файл:

| Настройка | Что делает |
|---|---|
| `FontScale` | Умножает размер начертания — для шрифта, который выглядит мелким рядом с соседями. |
| `NormalizeSize` | В роли запасного подгоняет шрифт под высоту строчных или прописных основного. |
| `SpacingOffset` | Единицы шрифта, добавленные к ширине каждого глифа, — для шрифта, который набирается слишком плотно или слишком разреженно. Связные письменности вроде арабской сохраняют свою ширину, а диакритика не получает ничего. |
| `SpaceWidth` | Ширина пробела и неразрывного пробела в единицах шрифта. 0 оставляет ширину из файла. |
| Метрики | Карточка **Metrics** задаёт верхний и нижний выносные элементы и межстрочный зазор вручную, с кнопкой **Reset**. |
| `GlyphOverrides` | Отдельный глиф, выбранный по символу: добавка к ширине, смещение при рисовании, размер при рисовании и число пикселей на em при растеризации. |

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsTuning : MonoBehaviour
{
    public LetteraFontAsset Display;

    void Start()
    {
        Display.SpacingOffset = -20;    // Set 20 font units tighter.
        Display.SpaceWidth = 220;

        // The question mark is drawn a little higher and 10% larger.
        LetteraGlyphOverride question = LetteraGlyphOverride.For('?');
        question.Offset = new Vector2Int(0, 40);
        question.Scale = 1.1f;
        Display.GlyphOverrides.Add(question);
        Display.GlyphOverridesChanged();
    }
}
```

Размер в переопределении глифа рисует те же пиксели крупнее, поэтому для сильно увеличенного глифа поднимите вместе с ним и **Pixels Per Em**. Изменения ассета в редакторе меняют сам ассет; если он должен остаться прежним, во время игры меняйте копию.

## Память

Пока шрифт не используется, он ничего не стоит:

- В плеере ни один шрифт не держит в памяти полную копию своего файла. Шрифт распаковывается в папку кеша, когда он впервые нужен тексту, а дальше **отображается** с диска.
- На настольных платформах билд кладёт каждый шрифт отдельным файлом рядом с собой, так что шрифт, до которого не дотягивается ни одна сцена, не стоит совсем ничего. На Android, iOS и в вебе байты остаются внутри ассета. **Project Settings > Lettera > Font Memory** выбирает для каждой платформы: `Auto`, `Inline` или `Packed`. Флажок **Keep Inline** в ассете оставляет именно этот шрифт внутри ассета везде.
- Там же задаётся предел папки кеша (`FontCacheBytes`, по умолчанию 256 МБ).

Что шрифты занимают прямо сейчас:

```csharp
using System.Collections.Generic;
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsMemoryReport : MonoBehaviour
{
    readonly List<LetteraFontMemoryEntry> m_Faces = new List<LetteraFontMemoryEntry>();

    void Start()
    {
        LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
        Debug.Log(sums.Faces + " faces, " + sums.RetainedBytes / 1024 + " KB held, " + sums.MappedBytes / 1024 + " KB mapped");

        LetteraFontMemory.Snapshot(m_Faces);
        foreach (LetteraFontMemoryEntry face in m_Faces)
            Debug.Log(face.Family + " " + face.Style + ": " + face.Source);
    }
}
```

**Первый текст сеанса** один раз платит за таблицы Lettera, первую страницу атласа, её материал и глифы, которые он рисует. Чтобы заплатить за это за экраном загрузки, вызовите `LetteraTextWarmUp.Prepare` со шрифтом и ожидаемыми символами. В билд ничего не добавляется:

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsWarmUp : MonoBehaviour
{
    public LetteraFontAsset Body;

    void Awake()
    {
        LetteraTextWarmUp.Prepare(Body, LetteraTextWarmUp.Latin + "0123456789", 24);
        LetteraShaderWarmUp.Run(LetteraSurfaceTier.UiPlain | LetteraSurfaceTier.UiStyled);
    }
}
```

Текст меньше 72 единиц и текст крупнее используют разные глифы, поэтому готовьте тот размер, который будете показывать.

Если 10 секунд ни один текст не перестраивался или система сообщает о нехватке памяти, Lettera отдаёт кеши, которые держит только ради скорости. То, что тексты показывают, остаётся при них. Задержку задаёт **Project Settings > Lettera > Memory**, а `LetteraMemory.Release()` отдаёт всё сразу.

## Подмножества

Подмножество — копия шрифта, в которой остались только выбранные символы: меньше скачивать и меньше байтов везти ради символов, которых проект никогда не покажет. Карточка **Subset** на странице ассета шрифта, а для многих шрифтов сразу — **Tools > Lettera > Font Tools**, оставляют или убирают символы по тексту, по диапазонам Unicode вроде `U+0400-04FF`, по пресетам (Basic Latin, Latin-1, Cyrillic, Greek, Digits) или по тому, что используют тексты проекта. По умолчанию сохраняется то, что обычный инструмент для подмножеств молча выбрасывает: возможности OpenType, имена, имена глифов и контур пустого глифа.

То же из скрипта редактора:

```csharp
#if UNITY_EDITOR
using FirstSymbol.Lettera;
using FirstSymbol.Lettera.Editor;
using UnityEngine;

public static class FontsSubsetTool
{
    public static LetteraFont KeepLatinAndCyrillic(LetteraFont font)
    {
        var options = new LetteraSubsetOptions
        {
            Mode = LetteraSubsetMode.Keep,
            Characters = LetteraFontSubset.Presets["Latin-1"] + " " + LetteraFontSubset.Presets["Cyrillic"],
            FromProject = true,   // Also every character the project's texts hold.
        };
        LetteraSubsetResult result = LetteraFontSubset.Run(font, options);
        if (!result.Succeeded)
        {
            Debug.LogWarning(result.Refused);
            return null;
        }
        Debug.Log(result.BytesBefore + " bytes to " + result.BytesAfter);
        return LetteraFontSubset.SaveAs(font, result.Bytes, "Assets/Fonts/Subset.asset");
    }
}
#endif
```

`LetteraFontSubset.Replace` вместо этого заменяет данные самого шрифта, одним шагом отмены. Инструмент живёт только в редакторе; ни один плеер его не содержит.

## Атлас: SDF и MSDF

Глифы хранятся как поля расстояний со знаком, поэтому один глиф годится для любого размера, а обводки, свечение и тени берутся из того же поля. **Render Mode** текста выбирает:

- **SDF** — по умолчанию: углы скругляются на крупных размерах, памяти меньше всего.
- **MSDF** — острые углы на любом размере и острые углы обводки, в собственном атласе и с большим расходом памяти на глиф.

**Project Settings > Lettera > Atlases** задаёт для каждого вида размер страницы (2048 или 1024; страница 1024 стоит четверть) и для полей расстояний — 8 или 16 бит на канал. 16 бит удваивают страницу и сохраняют поле гладким вдали от края, где его читают широкие обводки, свечение и тени. Изменение вступает в силу, когда страницы этого вида в следующий раз создаются с нуля. **Tools > Lettera > Atlases** показывает, что лежит на страницах. Когда страниц становится больше предела, дольше всех не использованная переиспользуется.

## Пиксельные шрифты

Шрифт, нарисованный по пиксельной сетке, может выводить каждый свой пиксель точно. В ассете шрифта задайте **Pixels Per Em** равным сетке (8 для восьмипиксельного шрифта) или нажмите **Find Pixel Grid**; **Snap To Font Pixels** держит глифы и смещения эффектов на целых пикселях шрифта. На размерах, кратных сетке, — для восьмипиксельного шрифта это 16, 24 и 32 — каждый край ложится на пиксель экрана, а обводки и тени получаются в целых пикселях шрифта.

```csharp
using FirstSymbol.Lettera;
using UnityEngine;

public class FontsPixel : MonoBehaviour
{
    public LetteraFontAsset Pixel;

    void Start()
    {
        int grid = Pixel.FindPixelGrid();     // 0 for a font not drawn in pixels.
        Pixel.FontPixelsPerEm = grid > 0 ? grid : 8;
        Pixel.SnapToFontPixels = true;

        var label = GetComponent<LetteraText>();
        label.Font = Pixel;
        label.FontSize = Pixel.FontPixelsPerEm * 3;
    }
}
```

## Цветные шрифты и эмодзи

Эмодзи и цветные глифы рисуются в цвете из шрифтов COLRv0, COLRv1 (с градиентами и вариативными осями), `sbix` и `CBDT`, в цветной атлас рядом с атласом полей расстояний. Флаги, составные последовательности, оттенки кожи и клавиши — каждый одним кластером: каретка и Backspace берут их целиком.

Если своего цветного шрифта нет, эмодзи приходят из системного шрифта эмодзи и выглядят так, как на этом устройстве. **Assets > Create > Lettera > Color Font Asset From Selection** создаёт `LetteraColorFont`; назначьте его шрифтом текста или положите в стек, и его глифы пойдут раньше системных. Из байтов: `LetteraFont.CreateFromBytes<LetteraColorFont>(bytes)`.

`<color>` красит слова вокруг эмодзи, а сам эмодзи оставляет как нарисован; `<alpha>` и прозрачность текста делают его прозрачнее. Тени и свечение рисуются вокруг эмодзи по его альфе; остальные слои пропускают цветные глифы, если в эффекте **On Color Glyphs** не выставлено **Apply**.

## Веб

Браузеры не дают доступа к файлам шрифтов:

- Текст без шрифта использует встроенный **Noto Sans** — латиница, кириллица и греческий. Остальные письменности показываются пустыми прямоугольниками, пока вы не назначите шрифт, который их покрывает, — через **Font**, **Font Stack** или **Default Font** проекта. Шейпинг дальше работает как везде.
- **Web Fallback Font** в Project Settings заменяет встроенный шрифт.
- Эмодзи рисует собственный canvas браузера, поэтому они выглядят как эмодзи этого устройства. Цвет и прозрачность применяются как обычно.
- Память под глифы ограничена сильнее, чем на других платформах: давно не использованная страница переиспользуется раньше.
- Встроенный веб-шрифт не попадает в билды для Windows, macOS, Android и iOS, если проект не назвал его своим веб- или основным шрифтом.

`unity command lettera validate` (см. [руководство для агентов](AgentGuide.md)) перечисляет, чего тексту не хватит в вебе и на телефонах.

## Ограничения

- **Шрифты Android читались только из тестовых файлов.** Устройство не использовалось. Не проверялись темы шрифтов в прошивке, которые подменяют системные файлы во время работы приложения, шрифты, которые производитель держит вне `/system/fonts` и `/product/fonts`, и прошивки, меняющие шрифты прямо во время отрисовки. Языки устройства, его настройка жирного текста и высокие варианты Android 15 читаются один раз, при первом тексте; изменение во время работы приложения вступит в силу при следующем запуске.
- Системный ассет шрифта находит другие начертания семейства по имени файла и имени семейства, открывая каждого кандидата один раз; на Android — по конфигурации. Насыщенность между двумя начертаниями семейства без вариативного шрифта берёт ближайшее начертание и не синтезирует более светлое.
- Размер в переопределении глифа рисует те же пиксели крупнее или мельче, поэтому увеличенный глиф мягче, если вместе с ним не поднять пиксели на em. Размер не проверялся на слоях, слитых формах глифов и объёмных телах Text 3D.
- Цветные страницы остаются 8-битными RGBA. Глубина полей расстояний и размер страниц любого вида — выбор проекта.
- COLRv1: 10 глифов из 200 в тестовом шрифте не рисуются (развёрточные градиенты с совпадающими углами, которые растеризатор HarfBuzz пропускает). Градиент через прозрачность немного отличается от Chromium. Цветной глиф, использующий собственный цвет текста, за ним не следует.
- Шрифты, в которых есть только растровые наборы и нет контуров, не поддерживаются как пиксельные.
- На macOS шрифт в asset bundle, собранном Scriptable Build Pipeline, остаётся внутри ассета.
- Собственные буферы текста не отдаются, даже если он долго не используется, — отдаются только общие кеши.
- Межбуквенные настройки, переопределения глифов, глубина атласа, насыщенность системных шрифтов и конфигурация Android покрыты тестами; замер времени до и после не был доведён до конца.
