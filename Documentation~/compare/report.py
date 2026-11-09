"""Writes Comparison.md and Comparison.ru.md from what compare.py left. usage: python report.py <work folder>
No figure in the report is typed by hand: each comes from compare.json or run.out."""
import json, os, re, shutil, statistics, sys

WORK = sys.argv[1]
HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.dirname(HERE)
PICTURES = os.path.join(DOCS, "comparison")
data = json.load(open(os.path.join(WORK, "compare.json"), encoding="utf-8"))
rows = data["rows"]
meta = data["meta"]
ENGINES = ["lettera", "tmp-static", "tmp-dynamic"]
run_out = open(os.path.join(WORK, "run.out"), encoding="utf-8", errors="replace").read()


def peak(engine):
    values = [int(m) for m in re.findall(rf"{engine}_main_\d: peak (\d+) MB", run_out)]
    return statistics.median(values) if values else None


def size(name):
    m = re.search(re.escape(name) + r" (\d+) KB", run_out)
    return int(m.group(1)) if m else 0


def cell(row, engine, kind):
    c = rows.get(row, {}).get(engine)
    if not c:
        return "—"
    if kind == "ms":
        mark = " †" if c.get("spread", 0) > 0.25 * abs(c["ms"]) else ""
        return (f"{c['ms']:.2f} ms" if c["ms"] < 100 else f"{c['ms']:.0f} ms") + mark
    if kind == "mb":
        return f"{c['bytes'] / 1048576:.1f} MB"
    if kind == "bytes":
        return (f"{c['bytes']:,} B / {c.get('largest', -1):,} B").replace(",", " ")
    return "—"


def table(head, lines, kind="ms"):
    out = ["| " + head[0] + " | Lettera | " + head[1] + " | " + head[2] + " |", "|---|---:|---:|---:|"]
    for row, label in lines:
        out.append(f"| {label} | " + " | ".join(cell(row, e, kind) for e in ENGINES) + " |")
    return "\n".join(out)


TIME = {
    "en": [("prepare_font", "Prepare a font (R0)"), ("R1_create_1000", "Create 1 000 labels (R1)"), ("R1_first_frame_after", "The frame after (R1)"), ("R2_rebuild_1000", "Rebuild 1 000 labels (R2)"),
           ("R3_64_change_a_frame", "64 labels change a frame (R3)"), ("R4_one_changes_a_frame", "One label changes a frame (R4)"), ("R5_100_color_a_frame", "Colour of 100 labels a frame (R5)"), ("R6_100_alpha_a_frame", "Alpha of 100 labels a frame, by property (R6)"),
           ("R7_1000_unchanging_frame", "A frame of 1 000 unchanging labels (R7)"), ("R8_set_10KB", "Set 10 KB, first long text of a run (R8)"), ("R8_set_10KB_again", "Set 10 KB again (R8)"), ("R8_set_100KB", "Set 100 KB (R8)"), ("R8_set_1000KB", "Set 1 MB (R8)"),
           ("R9_100KB_append_a_frame", "100 KB, one character appended a frame (R9)"), ("R10_scroll_1MB_first_frame", "Scroll 1 MB: open and first frame (R10)"), ("R10_scroll_1MB_frame", "Scroll 1 MB: a frame (R10)"),
           ("R11_5000_world_first_frame", "5 000 world texts: create and first frame (R11)"), ("R11_5000_world_frame", "5 000 world texts: a frame (R11)"), ("R12_mixed_100_create", "100 Arabic and Latin labels (R12)"), ("R15_3000_cjk_first_frame", "3 000 Han characters: first frame (R15)")],
    "ru": [("prepare_font", "Подготовка шрифта (R0)"), ("R1_create_1000", "Создать 1 000 подписей (R1)"), ("R1_first_frame_after", "Кадр после этого (R1)"), ("R2_rebuild_1000", "Перестроить 1 000 подписей (R2)"),
           ("R3_64_change_a_frame", "64 подписи меняются за кадр (R3)"), ("R4_one_changes_a_frame", "Одна подпись меняется за кадр (R4)"), ("R5_100_color_a_frame", "Цвет 100 подписей за кадр (R5)"), ("R6_100_alpha_a_frame", "Альфа 100 подписей за кадр, свойством (R6)"),
           ("R7_1000_unchanging_frame", "Кадр с 1 000 неизменных подписей (R7)"), ("R8_set_10KB", "Задать 10 КБ, первый длинный текст запуска (R8)"), ("R8_set_10KB_again", "Задать 10 КБ ещё раз (R8)"), ("R8_set_100KB", "Задать 100 КБ (R8)"), ("R8_set_1000KB", "Задать 1 МБ (R8)"),
           ("R9_100KB_append_a_frame", "100 КБ, один символ дописывается за кадр (R9)"), ("R10_scroll_1MB_first_frame", "Прокрутка 1 МБ: открытие и первый кадр (R10)"), ("R10_scroll_1MB_frame", "Прокрутка 1 МБ: кадр (R10)"),
           ("R11_5000_world_first_frame", "5 000 текстов в мире: создание и первый кадр (R11)"), ("R11_5000_world_frame", "5 000 текстов в мире: кадр (R11)"), ("R12_mixed_100_create", "100 подписей, арабский с латиницей (R12)"), ("R15_3000_cjk_first_frame", "3 000 иероглифов: первый кадр (R15)")],
}
GC = {
    "en": [("R3_64_change_a_frame", "R3"), ("R4_one_changes_a_frame", "R4"), ("R5_100_color_a_frame", "R5"), ("R6_100_alpha_a_frame", "R6"), ("R7_1000_unchanging_frame", "R7"), ("R9_100KB_append_a_frame", "R9")],
}
MEMORY = {
    "en": [("R15_atlas_after_latin", "Atlas textures after Latin"), ("R15_atlas_after_3000_cjk", "Atlas textures after 3 000 Han characters")],
    "ru": [("R15_atlas_after_latin", "Текстуры атласа после латиницы"), ("R15_atlas_after_3000_cjk", "Текстуры атласа после 3 000 иероглифов")],
}

HEAD = {"en": ("Row", "TextMesh Pro, static atlas", "TextMesh Pro, dynamic atlas"), "ru": ("Строка", "TextMesh Pro, статический атлас", "TextMesh Pro, динамический атлас")}


def pics(sheet, *engines):
    return " ".join(f"![{sheet} {e}](comparison/{sheet}_{e}.png)" for e in engines)


lettera_kb = size("Managed/FirstSymbol.Lettera.dll") + size("Managed/FirstSymbol.Lettera.Unicode.dll") + size("Managed/FirstSymbol.Lettera.Text3D.dll")
native_kb = size("lettera_native.dll")
tmp_kb = size("Managed/Unity.TextMeshPro.dll")
peaks = {e: peak(e) for e in ENGINES}
info = dict(unity=meta["unity"], backend=meta["backend"], gpu=meta["gpu"], device=meta["device"].strip(), rounds=data["rounds"],
            lettera_kb=lettera_kb, native_kb=native_kb, tmp_kb=tmp_kb, peak_l=peaks["lettera"], peak_s=peaks["tmp-static"], peak_d=peaks["tmp-dynamic"],
            r1=cell("R1_create_1000", "lettera", "ms"), r1t=cell("R1_create_1000", "tmp-static", "ms"), r11=cell("R11_5000_world_first_frame", "lettera", "ms"), r11t=cell("R11_5000_world_first_frame", "tmp-static", "ms"),
            r3=cell("R3_64_change_a_frame", "lettera", "ms"), r3t=cell("R3_64_change_a_frame", "tmp-static", "ms"), r10=cell("R10_scroll_1MB_frame", "lettera", "ms"), r10t=cell("R10_scroll_1MB_frame", "tmp-static", "ms"),
            r5=cell("R5_100_color_a_frame", "lettera", "bytes"), r5t=cell("R5_100_color_a_frame", "tmp-static", "bytes"))

Y, S, P, N = {"en": ("Yes", "With setup", "Partly", "No"), "ru": ("Да", "С настройкой", "Частично", "Нет")}["en"], None, None, None

MATRIX_EN = """| Feature | Lettera | TextMesh Pro | Behind it |
|---|---|---|---|
| Latin, Cyrillic, Greek | Yes | Yes | S1, S6 |
| Arabic, joined | Yes | No | S2 |
| Hebrew | Yes | Partly: letters reversed by a flag, some marks misplaced | S2 |
| Numbers and Latin inside right-to-left text | Yes | No: reversed with the line | S2 |
| Devanagari, Bengali | Yes | No: conjuncts and vowel signs not formed | S3 |
| Tamil | Yes | Partly: letters drawn, signs not reordered by a shaper | S3 |
| Thai | Yes | Partly: marks not positioned | S3 |
| Khmer, Myanmar, Lao word breaks | Yes | No | `SegmentationTests` |
| Chinese, Japanese, Korean | Yes | With setup: fallback fonts | S4 |
| Ruby | Yes | No | `ListRubyTests` |
| Unicode line breaking (UAX #14) | Yes | Partly: its own rules | `UnicodeConformanceTests` |
| Bidirectional text (UAX #9) | Yes | No | `UnicodeConformanceTests` |
| Colour emoji, ZWJ and skin tone | Yes | With setup: a sprite asset of a project's own; a font's own colours are not drawn | S5 |
| Colour font kinds: COLRv0, COLRv1, sbix, CBDT | Yes (COLRv1 not yet seen on a device) | No | `ColorPaintTests` |
| System fonts, no font assigned | Yes | With setup: font assets by family name | S4, S6 |
| Fallback chosen by coverage | Yes | With setup: a list per font | S6 |
| Variable fonts | Yes | No | `TypographyTests` |
| OpenType features by tag | Yes | Partly: kerning, some by a font setting | `TypographyTests` |
| Tags both have | Yes | Yes | S7 |
| Custom tags and effects | Yes | With setup: code over `textInfo` | `MatcherTests` |
| Markdown, bare links, trigger words | Yes | No | `MatcherTests` |
| Outline, shadow, glow | Yes, per range by tag | With setup: one material per look | S8 |
| Inner shadow | Yes | Partly: a material keyword; no visible result in the settings tried | S8 |
| Several layers on one range | Yes | Partly: what one material holds | S8 |
| Gradient | Yes, along a range | Partly: per glyph corners | S8 |
| Sharp corners (MSDF) | Yes | No | S9 |
| Motion by tag | Yes | With setup: code | S10 |
| Selection and editing | Yes | Yes, through `TMP_InputField`; selection of a label needs code | `EditingTests` |
| Rich clipboard | Yes | No | `ClipboardTests` |
| IME | Yes | Yes | `ImmTests` |
| Native mobile input | Yes (Android, iOS, Web) | Partly: the platform's keyboard | not seen on a device |
| Math | Yes | No | `MathTests` |
| Extruded 3D text | Yes | No | samples |
| Long documents, windowed | Yes | No | R10, `DocumentTests` |
| Timeline | Tracks exist, not played end to end yet | With setup | — |
| Migration from the other | Yes, Lettera from TextMesh Pro | — | `MigrationTests` |
| Built-in, URP | Yes | Yes | builds |
| HDRP | Yes: solid, world and lit world text | Unity documents it; not run here | a Windows player |
| Native plugin needed | Yes | No | build |
| Years in the field | New | Long | — |
"""

MATRIX_RU = """| Возможность | Lettera | TextMesh Pro | Чем подтверждено |
|---|---|---|---|
| Латиница, кириллица, греческий | Да | Да | S1, S6 |
| Арабский со связями | Да | Нет | S2 |
| Иврит | Да | Частично: буквы переворачиваются флагом, часть знаков не на месте | S2 |
| Числа и латиница внутри текста справа налево | Да | Нет: переворачиваются вместе со строкой | S2 |
| Деванагари, бенгальский | Да | Нет: лигатуры и знаки гласных не собираются | S3 |
| Тамильский | Да | Частично: буквы есть, знаки не переставлены | S3 |
| Тайский | Да | Частично: знаки не позиционируются | S3 |
| Границы слов кхмерского, бирманского, лаосского | Да | Нет | `SegmentationTests` |
| Китайский, японский, корейский | Да | С настройкой: резервные шрифты | S4 |
| Руби | Да | Нет | `ListRubyTests` |
| Перенос строк Unicode (UAX #14) | Да | Частично: свои правила | `UnicodeConformanceTests` |
| Двунаправленный текст (UAX #9) | Да | Нет | `UnicodeConformanceTests` |
| Цветные эмодзи, ZWJ и тон кожи | Да | С настройкой: свой спрайт-ассет проекта; цвета самого шрифта не рисуются | S5 |
| Виды цветных шрифтов: COLRv0, COLRv1, sbix, CBDT | Да (COLRv1 ещё не видели на устройстве) | Нет | `ColorPaintTests` |
| Системные шрифты, шрифт не задан | Да | С настройкой: ассеты шрифтов по имени семейства | S4, S6 |
| Резервный шрифт по покрытию | Да | С настройкой: список на шрифт | S6 |
| Вариативные шрифты | Да | Нет | `TypographyTests` |
| Функции OpenType тегом | Да | Частично: кернинг, часть настройкой шрифта | `TypographyTests` |
| Общие теги | Да | Да | S7 |
| Свои теги и эффекты | Да | С настройкой: код поверх `textInfo` | `MatcherTests` |
| Markdown, голые ссылки, слова-триггеры | Да | Нет | `MatcherTests` |
| Контур, тень, свечение | Да, на диапазон тегом | С настройкой: материал на каждый вид | S8 |
| Внутренняя тень | Да | Частично: ключевое слово материала; в проверенных настройках результат не виден | S8 |
| Несколько слоёв на диапазоне | Да | Частично: что вмещает один материал | S8 |
| Градиент | Да, вдоль диапазона | Частично: по углам глифа | S8 |
| Острые углы (MSDF) | Да | Нет | S9 |
| Движение тегом | Да | С настройкой: код | S10 |
| Выделение и редактирование | Да | Да, через `TMP_InputField`; выделение в подписи требует кода | `EditingTests` |
| Богатый буфер обмена | Да | Нет | `ClipboardTests` |
| IME | Да | Да | `ImmTests` |
| Нативный ввод на мобильных | Да (Android, iOS, Web) | Частично: клавиатура платформы | на устройстве не проверено |
| Формулы | Да | Нет | `MathTests` |
| Объёмный 3D-текст | Да | Нет | сэмплы |
| Длинные документы с окном | Да | Нет | R10, `DocumentTests` |
| Timeline | Дорожки есть, целиком ещё не проигрывались | С настройкой | — |
| Переход с другого движка | Да, на Lettera с TextMesh Pro | — | `MigrationTests` |
| Built-in, URP | Да | Да | сборки |
| HDRP | Да: объёмный, мировой и освещённый мировой текст | Заявлено Unity; здесь не запускалось | плеер Windows |
| Нужен нативный плагин | Да | Нет | сборка |
| Годы в работе | Новый | Долго | — |
"""

SHEETS = [("S1_latin", "lettera", "tmp-static"), ("S2_rtl", "lettera", "tmp-fallback"), ("S3_complex", "lettera", "tmp-fallback"), ("S4_cjk", "lettera", "tmp-fallback"), ("S5_emoji", "lettera", "tmp-fallback"), ("S6_fallback", "lettera", "tmp-fallback"), ("S7_markup", "lettera", "tmp-static"), ("S8_effects", "lettera", "tmp-static"), ("S9_sharp", "lettera", "tmp-static"), ("S10_motion", "lettera", "tmp-static")]
TITLES = {
    "en": {"S1_latin": "S1 Latin", "S2_rtl": "S2 Right to left. TextMesh Pro with fallback fonts and `isRightToLeftText` on", "S3_complex": "S3 Complex scripts. TextMesh Pro with fallback fonts", "S4_cjk": "S4 Chinese, Japanese, Korean", "S5_emoji": "S5 Emoji. TextMesh Pro with the emoji font as a fallback, and no sprite asset. A flag shows as JP in both: Windows' emoji font has no flags", "S6_fallback": "S6 Five scripts in a line", "S7_markup": "S7 Markup. The last line is Lettera's own tags, shown as text by the other", "S8_effects": "S8 Effects. Lettera by tag and style, TextMesh Pro by material and vertex gradient", "S9_sharp": "S9 At 200 px. Lettera's SDF, TextMesh Pro's SDF", "S10_motion": "S10 A still of motion by tag. TextMesh Pro needs code for this, so its picture shows plain text"},
    "ru": {"S1_latin": "S1 Латиница", "S2_rtl": "S2 Справа налево. TextMesh Pro с резервными шрифтами и включённым `isRightToLeftText`", "S3_complex": "S3 Сложные письменности. TextMesh Pro с резервными шрифтами", "S4_cjk": "S4 Китайский, японский, корейский", "S5_emoji": "S5 Эмодзи. TextMesh Pro со шрифтом эмодзи как резервным, без спрайт-ассета. Флаг в обоих показан как JP: в шрифте эмодзи Windows нет флагов", "S6_fallback": "S6 Пять письменностей в строке", "S7_markup": "S7 Разметка. Последняя строка — собственные теги Lettera, у второго движка показаны как текст", "S8_effects": "S8 Эффекты. Lettera тегом и стилем, TextMesh Pro материалом и градиентом вершин", "S9_sharp": "S9 При 200 px. SDF Lettera, SDF TextMesh Pro", "S10_motion": "S10 Кадр движения тегом. TextMesh Pro нужен для этого код, поэтому на его картинке обычный текст"},
}


def sheets(lang):
    out = []
    for sheet, a, b in SHEETS:
        out.append("**" + TITLES[lang][sheet] + "**\n\n" + pics(sheet, a, b))
    out.append(("**S9 MSDF, Lettera**" if lang == "en" else "**S9 MSDF, Lettera**") + "\n\n![S9 MSDF](comparison/S9_sharp_msdf_lettera.png)")
    return "\n\n".join(out)


EN = """# Lettera and TextMesh Pro

[Русская версия](Comparison.ru.md)

Every figure below comes from a script: [compare/](compare/) runs both engines and writes the tables, and you can rerun it. A row TextMesh Pro wins is shown as it came out.

## Method

- Unity {unity}, Windows player, {backend}, {gpu}, {device}. Median of {rounds} runs, a cold start each. Nothing else heavy running.
- The same font file on both: Noto Sans. TextMesh Pro in two setups, each its best case: a static atlas prepared ahead with the characters used, and a dynamic atlas. Lettera has one.
- The work is forced inside the clock (`ForceMeshUpdate`, `ForceRebuild`), and rows named "a frame" time the whole frame, so that no row is won by moving work out of it.
- Allocation comes from a development player, with a deliberate allocation showing that the counter counts.
- The players ran on a desktop of their own, off the screen of the machine, so the timing is not disturbed by a window; Direct3D drew as usual.
- Since 1.0's third optimization batch, the rows that follow the long texts (scrolling a document, mixed labels, 5000 world texts, 3000 Han) start after a collection and ten idle frames, the same for every engine, so that none pays for the garbage of the row before it. Earlier figures had no such pause.
- Since the fifth, each long text of R8 (10 KB, 100 KB, 1 MB) starts the same way. The players keep Unity's default, the incremental collector, and a cycle started by the labels or the text before a long text ran its slices in that row's frames in some runs and not in others: the 100 KB row moved between 1.7 and 7 ms on a Mono build, on code that had not changed. A row after the pause pays, as it allocates, for sweeping what the collection freed, on every engine.
- Since the sixth, R27 draws 200 labels at 28 px with a stroke and a shadow, and the same 200 plain, standing still, and gives the GPU time of a frame of each, on every engine that has both layers: Lettera by tags, TextMesh Pro by one shared material with an outline and an underlay. Another engine joins it through `LetteraCompare.ILayers`.
- Since the sixth, too, Lettera opens R16's 19 MB font by its path, with `LetteraFont.CreateFromFile`, as TextMesh Pro opens it by its path; before, the script read the file into an array and gave Lettera that. R16b keeps the array route, Lettera's only, in a process of its own.
- A text whose paragraphs are partly left for later frames (Lettera's long texts) is timed on the frame it is set; the finishing in the frames after is the frame time of whatever row comes next.

## Time

{time}

{spread_note}

R12 times the work only: TextMesh Pro's Arabic comes out unjoined, as S2 shows, so its row is not the same result for less. R15's first frame is one run of each engine's own atlas filling.

## Allocation per frame

A usual frame (the median) / the largest single frame of the row.

{gc}

## Memory and size

{memory}

The figure is what `Profiler.GetRuntimeMemorySizeLong` says of each engine's textures. It counts a readable texture's copy in memory too, which TextMesh Pro's dynamic atlases keep.

| | Lettera | TextMesh Pro, static | TextMesh Pro, dynamic |
|---|---:|---:|---:|
| Whole player's peak over a run | {peak_l} MB | {peak_s} MB | {peak_d} MB |
{managed_row}| Native library in a Windows build | {native_kb} KB | none | none |

## Features

Each cell is Yes, With setup, Partly or No. A sheet (S) or a named test stands behind each. TextMesh Pro's column was seen on these sheets where a sheet is named. Elsewhere it is what is known of that engine, and was not run here.

{matrix}

## Sheets

The same scene in each engine, at 1200 × 700. Lettera on the left, TextMesh Pro on the right. Every picture is drawn with Noto fonts only, on both engines and the same on both: Noto Sans, Noto Sans Arabic, Hebrew, Devanagari, Bengali, Tamil and Thai, Noto Sans CJK SC, and for emoji Noto Color Emoji on Lettera and Noto Emoji, its single-colour face, on TextMesh Pro, which draws no colour fonts. All are under the SIL Open Font License 1.1; no operating-system font is used, and none of these fonts ships in the package. Noto Color Emoji draws the family emoji as black figures on a light square in this version; a browser draws the same from the same file.

{sheets}

## When to stay on TextMesh Pro

- **Scrolling one very long text, once it is open.** {r10} a frame here, {r10t} there. Opening it is the other way round, by a long way.
- **A build that must stay small, or no native plugin.** {small_en}
- **You rely on `textInfo`,** or on years of field use. Lettera is new, and parts of it have not been seen on a real phone.
- **Latin only, and a static atlas you already have.** Then most of what Lettera adds, you will not use.

## What was not measured, and why

- **Draw calls and batches (R14).** The `Batches Count` recorder gave no valid value in this player, even in a development build. The other route, the Frame Debugger's count in the editor, has not been done.
- **Scene load, and first use of a 16 MB font (R16); inspector typing (R18); alpha through `CanvasGroup` (R6, by property only); atlas after 200 emoji (R15).** Not written yet.
- **Build size per platform (R17).** Windows only, above.
- **Managed code size under IL2CPP.** It is compiled into `GameAssembly.dll`, so the per-assembly row is left out of the table above.
- **Web and Android.** Not yet; Android needs the phone of whoever runs it.
- **iOS.** Exported only. No Mac was at hand.
- **TextMesh Pro's emoji by sprite asset,** which a project would prepare itself.
- **Fields (S11) and variable fonts (S12) have no sheet.** Tests stand behind those cells.

## Run it yourself

```
python compare.py <Unity.exe> <path to this package> <work folder> 5 --sheet-fonts <folder of the Noto fonts>
python report.py <work folder>
```
"""

RU = """# Lettera и TextMesh Pro

[English version](Comparison.md)

Все числа ниже получены скриптом: [compare/](compare/) запускает оба движка и пишет таблицы, повторить можно самостоятельно. Строки, где выигрывает TextMesh Pro, показаны как есть.

## Как мерили

- Unity {unity}, плеер Windows, {backend}, {gpu}, {device}. Медиана {rounds} запусков, каждый с холодного старта. Ничего тяжёлого рядом не работало.
- Один и тот же файл шрифта: Noto Sans. TextMesh Pro в двух настройках, каждая в лучшем для него случае: статический атлас, заранее заполненный нужными символами, и динамический. У Lettera настройка одна.
- Работа принудительно выполняется внутри замера (`ForceMeshUpdate`, `ForceRebuild`), а строки «за кадр» замеряют весь кадр, чтобы ни одна строка не выигрывала за счёт переноса работы за пределы замера.
- Выделения памяти взяты из development-плеера, а контрольное выделение показывает, что счётчик работает.
- Плееры работали на отдельном рабочем столе, вне экрана машины, так что окно замер не сбивает; Direct3D рисовал как обычно.
- С третьей серии оптимизаций 1.0 строки после длинных текстов (прокрутка документа, смешанные подписи, 5000 текстов в мире, 3000 иероглифов) начинаются после сборки мусора и десяти пустых кадров, одинаково для всех движков, чтобы ни одна не платила за мусор строки перед ней. В более ранних числах такой паузы не было.
- С пятой серии так же начинается каждый длинный текст R8 (10 КБ, 100 КБ, 1 МБ). Плееры оставляют сборщик Unity по умолчанию, инкрементальный, и цикл, начатый подписями или текстом перед длинным текстом, в одних запусках отрабатывал свои отрезки в кадрах этой строки, а в других нет: строка 100 КБ гуляла между 1,7 и 7 мс на сборке Mono, на неизменном коде. Строка после паузы, выделяя память, платит за подметание того, что освободила сборка, на любом движке.
- С шестой серии R27 рисует 200 подписей в 28 px с обводкой и тенью и те же 200 без них, неподвижно, и даёт время GPU на кадр того и другого, на каждом движке, где есть оба слоя: у Lettera тегами, у TextMesh Pro одним общим материалом с обводкой и подложкой. Другой движок подключается через `LetteraCompare.ILayers`.
- С шестой серии также Lettera открывает шрифт R16 в 19 МБ по пути, через `LetteraFont.CreateFromFile`, как TextMesh Pro открывает его по пути; раньше скрипт читал файл в массив и отдавал Lettera его. R16b оставляет путь через массив, только у Lettera, в отдельном процессе.

## Время

{time}

{spread_note}

R12 замеряет только работу: арабский у TextMesh Pro выходит без связей, как видно на S2, так что это не тот же результат за меньшее время. Первый кадр R15 — заполнение собственного атласа каждого движка.

## Выделение памяти за кадр

Обычный кадр (медиана) / самый большой кадр в строке.

{gc}

## Память и размер

{memory}

Это значение `Profiler.GetRuntimeMemorySizeLong` для текстур каждого движка. В него входит и копия читаемой текстуры в памяти, которую держат динамические атласы TextMesh Pro.

| | Lettera | TextMesh Pro, статический | TextMesh Pro, динамический |
|---|---:|---:|---:|
| Пик всего плеера за запуск | {peak_l} МБ | {peak_s} МБ | {peak_d} МБ |
{managed_row_ru}| Нативная библиотека в билде Windows | {native_kb} КБ | нет | нет |

## Возможности

В каждой ячейке: Да, С настройкой, Частично или Нет. За каждой стоит лист (S) или названный тест. Столбец TextMesh Pro проверен на этих листах там, где лист указан. В остальных случаях это то, что известно об этом движке, и здесь это не запускалось.

{matrix}

## Листы

Одна и та же сцена в каждом движке, 1200 × 700. Слева Lettera, справа TextMesh Pro. Все картинки нарисованы только шрифтами Noto, на обоих движках одними и теми же: Noto Sans, Noto Sans Arabic, Hebrew, Devanagari, Bengali, Tamil и Thai, Noto Sans CJK SC, а для эмодзи Noto Color Emoji у Lettera и Noto Emoji, его одноцветная гарнитура, у TextMesh Pro, который цветные шрифты не рисует. Все они под SIL Open Font License 1.1; системные шрифты не используются, и ни один из этих шрифтов не входит в пакет. Noto Color Emoji в этой версии рисует эмодзи семьи чёрными фигурами на светлом квадрате; браузер рисует то же из того же файла.

{sheets}

## Когда остаться на TextMesh Pro

- **Прокрутка одного очень длинного текста, когда он уже открыт.** {r10} за кадр здесь, {r10t} там. Само открытие наоборот, причём с большим отрывом.
- **Билд должен оставаться маленьким или без нативного плагина.** {small_ru}
- **Вы опираетесь на `textInfo`** или на годы работы в реальных проектах. Lettera новый, и часть его ещё не видели на настоящем телефоне.
- **Только латиница и уже готовый статический атлас.** Тогда большая часть того, что добавляет Lettera, вам не пригодится.

## Что не мерили и почему

- **Вызовы отрисовки и батчи (R14).** Счётчик `Batches Count` не дал допустимого значения в этом плеере, даже в development-сборке. Другой путь, счёт Frame Debugger в редакторе, пока не пройден.
- **Загрузка сцены и первое использование шрифта на 16 МБ (R16); ввод в инспекторе (R18); альфа через `CanvasGroup` (R6, только свойством); атлас после 200 эмодзи (R15).** Пока не написаны.
- **Размер билда по платформам (R17).** Только Windows, выше.
- **Размер управляемого кода под IL2CPP.** Он компилируется в `GameAssembly.dll`, поэтому строки по сборкам в таблице выше нет.
- **Web и Android.** Пока нет; для Android нужен телефон того, кто запускает.
- **iOS.** Только экспорт. Mac под рукой не было.
- **Эмодзи TextMesh Pro через спрайт-ассет,** который проект готовит сам.
- **У полей ввода (S11) и вариативных шрифтов (S12) нет листа.** За этими ячейками стоят тесты.

## Запустить самому

```
python compare.py <Unity.exe> <путь к этому пакету> <рабочая папка> 5 --sheet-fonts <папка со шрифтами Noto>
python report.py <рабочая папка>
```
"""

os.makedirs(PICTURES, exist_ok=True)
for sheet, a, b in SHEETS:
    for engine in (a, b):
        shutil.copyfile(os.path.join(WORK, "sheets", f"{sheet}_{engine}.png"), os.path.join(PICTURES, f"{sheet}_{engine}.png"))
shutil.copyfile(os.path.join(WORK, "sheets", "S9_sharp_msdf_lettera.png"), os.path.join(PICTURES, "S9_sharp_msdf_lettera.png"))

SPREAD = {"en": "† The five runs of this row differ by more than a quarter of the median (largest minus smallest over the median), so its figure is rough; the table shows it as it came out.",
          "ru": "† Пять запусков этой строки различаются больше чем на четверть медианы (наибольший минус наименьший, делённый на медиану), поэтому её число грубое; таблица показывает его как есть."}
for key in ("r1", "r1t", "r11", "r11t", "r3", "r3t", "r10", "r10t"):
    info[key] = info[key].replace(" †", "")
readable = info["lettera_kb"] > 0 and info["tmp_kb"] > 0
info["managed_row"] = f"| Managed assemblies in a Windows build | {info['lettera_kb']} KB | {info['tmp_kb']} KB | {info['tmp_kb']} KB |\n" if readable else ""
info["managed_row_ru"] = f"| Управляемые сборки в билде Windows | {info['lettera_kb']} КБ | {info['tmp_kb']} КБ | {info['tmp_kb']} КБ |\n" if readable else ""
info["small_en"] = f"TextMesh Pro is {info['tmp_kb']} KB of managed code and nothing else." if readable else f"TextMesh Pro brings no native library; Lettera's is {info['native_kb']} KB."
info["small_ru"] = f"TextMesh Pro — это {info['tmp_kb']} КБ управляемого кода, и больше ничего." if readable else f"У TextMesh Pro нет нативной библиотеки; у Lettera она {info['native_kb']} КБ."
wide = any(c.get("spread", 0) > 0.25 * abs(c["ms"]) for row, _ in TIME["en"] for c in [rows.get(row, {}).get("lettera"), rows.get(row, {}).get("tmp-static"), rows.get(row, {}).get("tmp-dynamic")] if c)
for lang, template, matrix, name in (("en", EN, MATRIX_EN, "Comparison.md"), ("ru", RU, MATRIX_RU, "Comparison.ru.md")):
    text = template.format(time=table(HEAD[lang], TIME[lang]), gc=table(HEAD[lang], GC["en"], "bytes"), memory=table(HEAD[lang], MEMORY[lang], "mb"), matrix=matrix, sheets=sheets(lang), spread_note=SPREAD[lang] if wide else "", **info)
    open(os.path.join(DOCS, name), "w", encoding="utf-8", newline="\n").write(text)
total = sum(os.path.getsize(os.path.join(PICTURES, f)) for f in os.listdir(PICTURES))
print("pictures", total // 1024, "KB")
print(json.dumps(peaks))
