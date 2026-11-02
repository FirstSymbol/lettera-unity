// Lettera against TextMesh Pro: one workload, one font file, the same scene. Run a player with -compare <engine> -out <file.json>.
// Engines: lettera, tmp-static (an atlas prepared ahead with the characters used), tmp-dynamic (an atlas filled as it goes),
// and for pictures only tmp-fallback (the dynamic setup with system fonts as fallbacks, and right-to-left on for the right-to-left sheet).
// The rows are in Documentation~/Comparison.md. No number in the report is typed by hand: tools turn this file's JSON into its tables.
// -scenario picks what one cold start runs: main (default), extra, first, multilang, font, memory, draw. Anything that must start cold has its own process.
// Another engine joins from a script of its own beside this one, by adding to External before the scene loads. This file does not need to know it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FirstSymbol.Lettera;
using TMPro;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class LetteraCompare : MonoBehaviour
{
    public interface IEngine
    {
        // The clock runs around this: reading the file and making what the engine needs of it.
        void LoadFont(string path);
        // A second font, from a large file, for the row on first use of one. Null when the file cannot be read.
        Component LargeLabel(Transform parent, string path, string text, float size, Vector2 box);
        Component Label(Transform parent, string text, float size, Vector2 box);
        Component World(string text, float size);
        void Text(Component c, string text);
        // Text by the call the engine's documentation gives for text that changes at runtime.
        void SetText(Component c, string text);
        void Color(Component c, Color color);
        void Force(Component c);
        void ForceAll(IReadOnlyList<Component> all);
        void Dirty(Component c);
        // A viewport over a long text, and how to move it, from 0 at the top to 1 at the end.
        Action<float> Scroll(Transform parent, string text);
        // What the engine needs to draw any script, outside any clock.
        void Fallbacks();
        // An editable field, focused, with the caret at the end of its text.
        Component Field(Transform parent, string text, float size, Vector2 box);
        void Key(Component field, string character);
        int FieldLength(Component field);
        // How many glyphs the text holds now, so that a row can say what it built.
        int Glyphs(Component c);
    }

    public static readonly Dictionary<string, Func<IEngine>> External = new Dictionary<string, Func<IEngine>>();

    sealed class LetteraEngine : IEngine
    {
        public void LoadFont(string path) => s_LetteraFont = LetteraFont.CreateFromBytes(File.ReadAllBytes(path));

        public Component LargeLabel(Transform parent, string path, string text, float size, Vector2 box)
        {
            var label = (LetteraText)Label(parent, "", size, box);
            label.Font = LetteraFont.CreateFromBytes(File.ReadAllBytes(path));
            label.Text = text;
            return label;
        }

        public void SetText(Component c, string text) => ((LetteraText)c).SetText(text);
        public void ForceAll(IReadOnlyList<Component> all) { foreach (Component c in all) Force(c); }
        public void Fallbacks() { }

        public Component Field(Transform parent, string text, float size, Vector2 box)
        {
            var label = (LetteraText)Label(parent, text, size, box);
            label.raycastTarget = true;
            var editable = label.gameObject.AddComponent<LetteraEditable>();
            editable.Selectable.Activate();
            label.ForceRebuild();
            int end = editable.Length;
            editable.Selectable.Select(end, end);
            return editable;
        }

        public void Key(Component field, string character) => ((LetteraEditable)field).InsertText(character, "user.type");
        public int FieldLength(Component field) => ((LetteraEditable)field).Length;
        public int Glyphs(Component c) => c is LetteraText a ? a.GlyphCount : -1;

        public Component Label(Transform parent, string text, float size, Vector2 box)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            var label = go.AddComponent<LetteraText>();
            label.raycastTarget = false;
            label.Font = s_LetteraFont;
            label.FontSize = size;
            label.Text = text;
            return label;
        }

        public Component World(string text, float size)
        {
            var go = new GameObject("World", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(40, 4);
            var label = go.AddComponent<LetteraWorldText>();
            label.Font = s_LetteraFont;
            label.FontSize = size;
            label.Text = text;
            return label;
        }

        public void Text(Component c, string text)
        {
            if (c is LetteraText a) a.Text = text;
            else ((LetteraWorldText)c).Text = text;
        }

        public void Color(Component c, Color color)
        {
            if (c is LetteraText a) a.color = color;
            else ((LetteraWorldText)c).Color = color;
        }

        public void Force(Component c)
        {
            if (c is LetteraText a) a.ForceRebuild();
            else ((LetteraWorldText)c).ForceRebuild();
        }

        public void Dirty(Component c)
        {
            if (c is LetteraText a) a.MarkStylesDirty();
            else ((LetteraWorldText)c).MarkStylesDirty();
        }

        public Action<float> Scroll(Transform parent, string text)
        {
            var view = new GameObject("View", typeof(RectTransform));
            view.transform.SetParent(parent, false);
            ((RectTransform)view.transform).sizeDelta = new Vector2(800, 600);
            var label = (LetteraText)Label(view.transform, "", 14, new Vector2(800, 600));
            label.OpenDocument(LetteraRope.FromString(text), takeOwnership: true);
            var scroller = view.AddComponent<LetteraDocumentScroller>();
            scroller.Text = label;
            label.ForceRebuild();
            scroller.ScrollToEnd();
            double end = scroller.Position;
            scroller.ScrollToStart();
            return at => scroller.Position = at * end;
        }
    }

    sealed class TmpEngine : IEngine
    {
        readonly string m_Name;
        readonly Event m_Key = new Event { type = EventType.KeyDown };

        public TmpEngine(string name) => m_Name = name;

        public void LoadFont(string path)
        {
            // The same file, imported as a font: TextMesh Pro reads a Font asset, and a plain path gives it nothing to read.
            Font source = Resources.Load<Font>("NotoSans-Regular");
            if (source == null) throw new InvalidOperationException("Assets/Compare/Resources must hold NotoSans-Regular.ttf.");
            s_TmpFont = TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (m_Name != "tmp-static") return;
            s_TmpFont.TryAddCharacters(Latin, out string _);
            s_TmpFont.atlasPopulationMode = AtlasPopulationMode.Static;
        }

        public Component LargeLabel(Transform parent, string path, string text, float size, Vector2 box)
        {
            var label = (TextMeshProUGUI)Label(parent, "", size, box);
            label.font = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048);
            label.text = text;
            return label;
        }

        public void SetText(Component c, string text) => ((TMP_Text)c).SetText(text);
        public void ForceAll(IReadOnlyList<Component> all) { foreach (Component c in all) Force(c); }

        public void Fallbacks()
        {
            s_TmpFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
            foreach ((string family, string style) in new[] { ("Segoe UI", "Regular"), ("Nirmala UI", "Regular"), ("Leelawadee UI", "Regular"), ("Microsoft YaHei", "Regular"), ("Yu Gothic", "Regular"), ("Malgun Gothic", "Regular"), ("Segoe UI Emoji", "Regular") })
            {
                TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(family, style);
                if (font != null) s_TmpFont.fallbackFontAssetTable.Add(font);
                else Debug.Log("LETTERA_COMPARE no system font " + family);
            }
        }

        public Component Field(Transform parent, string text, float size, Vector2 box)
        {
            var go = new GameObject("Field", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            ((RectTransform)viewport.transform).sizeDelta = box;
            var label = (TextMeshProUGUI)Label(viewport.transform, "", size, box);
            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = (RectTransform)viewport.transform;
            field.textComponent = label;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.onFocusSelectAll = false;
            field.text = text;
            field.ActivateInputField();
            return field;
        }

        public void Key(Component field, string character)
        {
            var input = (TMP_InputField)field;
            input.stringPosition = input.text.Length;
            m_Key.character = character[0];
            input.ProcessEvent(m_Key);
            // Its own input loop draws the label after handling a key, and this call stands in for that loop.
            input.ForceLabelUpdate();
        }

        public int FieldLength(Component field) => ((TMP_InputField)field).text.Length;
        public int Glyphs(Component c) => ((TMP_Text)c).textInfo.characterCount;

        public Component Label(Transform parent, string text, float size, Vector2 box)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.font = s_TmpFont;
            label.fontSize = size;
            label.text = text;
            return label;
        }

        public Component World(string text, float size)
        {
            var go = new GameObject("World");
            var label = go.AddComponent<TextMeshPro>();
            label.font = s_TmpFont;
            label.fontSize = size;
            label.text = text;
            return label;
        }

        public void Text(Component c, string text) => ((TMP_Text)c).text = text;
        public void Color(Component c, Color color) => ((TMP_Text)c).color = color;
        public void Force(Component c) => ((TMP_Text)c).ForceMeshUpdate();
        public void Dirty(Component c) => ((TMP_Text)c).ForceMeshUpdate(true, true);

        public Action<float> Scroll(Transform parent, string text)
        {
            var view = new GameObject("View", typeof(RectTransform), typeof(RectMask2D));
            view.transform.SetParent(parent, false);
            var viewport = (RectTransform)view.transform;
            viewport.sizeDelta = new Vector2(800, 600);
            var label = (TextMeshProUGUI)Label(view.transform, text, 14, new Vector2(800, 600));
            label.ForceMeshUpdate();
            var content = label.rectTransform;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, label.preferredHeight);
            var scroll = view.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return at => scroll.verticalNormalizedPosition = 1 - at;
        }
    }

    static LetteraFont s_LetteraFont;
    static TMP_FontAsset s_TmpFont;
    static readonly ProfilerRecorder s_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
    static readonly FrameTiming[] s_Timing = new FrameTiming[1];

    IEngine m_Engine;
    string m_Name;
    Canvas m_Canvas;
    readonly List<(string row, double ms, long bytes, string note)> m_Rows = new List<(string, double, long, string)>();
    readonly List<GameObject> m_Made = new List<GameObject>();

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Arg("-compare") != null || Arg("-sheets") != null) new GameObject("Compare").AddComponent<LetteraCompare>();
    }

    const string Latin = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    // A row that throws must not leave a player running with nothing to do.
    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Exception) return;
        Debug.Log("LETTERA_COMPARE stopped: " + message);
        if (Arg("-out") != null) Write(Arg("-out"));
        Application.Quit(3);
    }

    IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;
        m_Name = Arg("-compare") ?? "lettera";
        string fonts = Arg("-fonts");
        Prepare(fonts);
        m_Canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        if (m_Engine is TmpEngine) m_Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        Camera camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -60);
        camera.clearFlags = CameraClearFlags.SolidColor;
        // Grey, since a black shadow on black would show nothing.
        camera.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
        yield return null;
        if (Arg("-sheets") != null)
        {
            yield return Sheets(Arg("-sheets"));
            Application.Quit();
            yield break;
        }
        switch (Arg("-scenario") ?? "main")
        {
            case "extra": yield return ExtraRows(); break;
            case "first": yield return FirstRows(); break;
            case "prepared": yield return PreparedRows(); break;
            case "multilang": yield return MultilingualRows(); break;
            case "font": yield return FontRows(); break;
            case "memory": yield return MemoryRows(); break;
            case "draw": yield return DrawRows(); break;
            default: yield return Rows(); break;
        }
        Say("control_gc_counter", 0, ControlBytes(), s_Gc.Valid ? "valid" : "not valid in this player: bytes columns say nothing");
        Say("control_frame_timing", 0, FrameTimingManager.IsFeatureEnabled() ? 1 : 0, "");
        Write(Arg("-out"));
        Debug.Log("LETTERA_COMPARE done " + m_Name);
        Application.Quit();
    }

    void Prepare(string fonts)
    {
        string path = Path.Combine(fonts, "NotoSans-Regular.ttf");
        if (m_Name == "lettera") m_Engine = new LetteraEngine();
        else if (External.TryGetValue(m_Name, out Func<IEngine> make)) m_Engine = make();
        else m_Engine = new TmpEngine(m_Name);
        var clock = Stopwatch.StartNew();
        m_Engine.LoadFont(path);
        Say("prepare_font", clock.Elapsed.TotalMilliseconds);
        if (m_Name == "tmp-fallback") m_Engine.Fallbacks();
    }

    void Say(string row, double ms, long bytes = -1, string note = "")
    {
        m_Rows.Add((row, ms, bytes, note));
        Debug.Log("LETTERA_COMPARE_ROW " + row + " " + ms.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms " + note + " heap=" + (Profiler.GetTotalAllocatedMemoryLong() / 1048576) + "MB");
    }

    static double Median(List<double> values)
    {
        values.Sort();
        return values.Count == 0 ? 0 : values[values.Count / 2];
    }

    static long GcNow() => s_Gc.Valid ? s_Gc.LastValue : -1;

    void Clear()
    {
        foreach (Transform child in m_Canvas.transform) Destroy(child.gameObject);
        foreach (GameObject go in m_Made) Destroy(go);
        m_Made.Clear();
    }

    static string Sentence(int i)
    {
        const string Words = "The quick brown fox jumps over a lazy dog and then runs back home again ";
        int length = 12 + i * 7 % 29;
        return Words.Substring(i % 20, length);
    }

    // Between rows that each leave much garbage, the same pause for every engine: a collection and a few frames, so that a row does not pay for the one before it.
    static IEnumerator Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        for (int i = 0; i < 10; i++) yield return null;
    }

    // A frame's whole time, since each engine does its work at a different moment, and a row must not be won by moving work out of the clock.
    IEnumerator FrameTime(string row, int frames, Action each, string note = "")
    {
        var times = new List<double>();
        var allocations = new List<double>();
        var gpu = new List<double>();
        long largest = -1;
        for (int i = 0; i < 5; i++) yield return null;
        var clock = new Stopwatch();
        for (int i = 0; i < frames; i++)
        {
            clock.Restart();
            each?.Invoke();
            yield return null;
            times.Add(clock.Elapsed.TotalMilliseconds);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, s_Timing) > 0 && s_Timing[0].gpuFrameTime > 0) gpu.Add(s_Timing[0].gpuFrameTime);
            long gc = GcNow();
            if (gc < 0) continue;
            allocations.Add(gc);
            largest = Math.Max(largest, gc);
        }
        // A usual frame, and apart from it the largest, since one frame's growth of a buffer is not what every frame costs.
        string timing = gpu.Count == 0 ? "" : " gpu=" + Median(gpu).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        Say(row, Median(times), allocations.Count == 0 ? -1 : (long)Median(allocations), (note + " largest=" + largest + timing).Trim());
    }

    IEnumerator Rows()
    {
        Transform parent = m_Canvas.transform;
        // A development player is run for what only it can count, and the rows that take longest add nothing to that.
        bool light = Arg("-light") != null;

        // R1, R2, R7
        var labels = new List<Component>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) labels.Add(m_Engine.Label(parent, Sentence(i), 18, new Vector2(400, 30)));
        m_Engine.ForceAll(labels);
        double created = clock.Elapsed.TotalMilliseconds;
        Say("R1_create_1000", created);
        clock.Restart();
        yield return null;
        Say("R1_first_frame_after", clock.Elapsed.TotalMilliseconds);
        Say("R1_create_and_frame", created + clock.Elapsed.TotalMilliseconds);
        var runs = new List<double>();
        for (int r = 0; r < 5; r++)
        {
            foreach (Component c in labels) m_Engine.Dirty(c);
            clock.Restart();
            m_Engine.ForceAll(labels);
            runs.Add(clock.Elapsed.TotalMilliseconds);
            yield return null;
        }
        Say("R2_rebuild_1000", Median(runs));
        // The frame as a whole, with no forcing: an engine that gathers its work into the canvas update shows here what it gains.
        runs.Clear();
        for (int r = 0; r < 5; r++)
        {
            clock.Restart();
            foreach (Component c in labels) m_Engine.Dirty(c);
            yield return null;
            runs.Add(clock.Elapsed.TotalMilliseconds);
            yield return null;
        }
        Say("R2_rebuild_1000_frame", Median(runs));
        yield return FrameTime("R7_1000_unchanging_frame", 120, null);
        yield return R15("R15_atlas_after_latin");
        Clear();
        yield return null;

        // R3, R4
        var numbers = new List<Component>();
        for (int i = 0; i < 64; i++) numbers.Add(m_Engine.Label(parent, "0", 18, new Vector2(100, 30)));
        foreach (Component c in numbers) m_Engine.Force(c);
        int tick = 0;
        var strings = new string[1000];
        for (int i = 0; i < strings.Length; i++) strings[i] = i.ToString();
        yield return FrameTime("R3_64_change_a_frame", 240, () =>
        {
            tick++;
            for (int i = 0; i < numbers.Count; i++) m_Engine.Text(numbers[i], strings[(tick + i) % strings.Length]);
        });
        Component one = numbers[0];
        yield return FrameTime("R4_one_changes_a_frame", 240, () => m_Engine.Text(one, strings[++tick % strings.Length]));
        Clear();
        yield return null;

        // R5, R6
        var colored = new List<Component>();
        for (int i = 0; i < 100; i++) colored.Add(m_Engine.Label(parent, Sentence(i), 18, new Vector2(400, 30)));
        yield return null;
        yield return FrameTime("R5_100_color_a_frame", 240, () =>
        {
            tick++;
            Color color = new Color((tick & 31) / 31f, 1, 1, 1);
            foreach (Component c in colored) m_Engine.Color(c, color);
        });
        yield return FrameTime("R6_100_alpha_a_frame", 240, () =>
        {
            tick++;
            Color color = new Color(1, 1, 1, (tick & 31) / 31f);
            foreach (Component c in colored) m_Engine.Color(c, color);
        });
        Clear();
        yield return null;

        // R8, R9. Each long text starts after a collection, so that none pays for what the labels before it or the text before it left.
        var page = new StringBuilder();
        foreach (int size in light ? new[] { 10, 100 } : new[] { 10, 100, 1000 })
        {
            yield return Settle();
            page.Clear();
            int i = 0;
            while (page.Length < size * 1024) page.Append(Sentence(i++)).Append(i % 8 == 0 ? "\n" : " ");
            Component big = m_Engine.Label(parent, "", 14, new Vector2(800, 100000));
            yield return null;
            clock.Restart();
            string note = "";
            try
            {
                m_Engine.Text(big, page.ToString());
                m_Engine.Force(big);
            }
            catch (Exception e)
            {
                note = "failed: " + e.GetType().Name;
            }
            double set = clock.Elapsed.TotalMilliseconds;
            yield return null;
            Say("R8_set_" + size + "KB", set, -1, note + " frame=" + clock.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            Say("R8_set_" + size + "KB_and_frame", clock.Elapsed.TotalMilliseconds, -1, (note + " glyphs=" + m_Engine.Glyphs(big)).Trim());
            if (size == 100)
            {
                string body = page.ToString();
                yield return FrameTime("R9_100KB_append_a_frame", 30, () =>
                {
                    body += "x";
                    m_Engine.Text(big, body);
                });
            }
            Destroy(big.gameObject);
            yield return null;
            // The same work again, since the first long text of a run also pays for what the run has not touched yet.
            if (size == 10)
            {
                Component again = m_Engine.Label(parent, "", 14, new Vector2(800, 100000));
                yield return null;
                clock.Restart();
                m_Engine.Text(again, page.ToString());
                m_Engine.Force(again);
                Say("R8_set_10KB_again", clock.Elapsed.TotalMilliseconds);
                Destroy(again.gameObject);
                yield return null;
            }
        }

        // R10, on the 1 MB page the last turn left
        if (!light)
        {
            yield return Settle();
            clock.Restart();
            Action<float> move = m_Engine.Scroll(parent, page.ToString());
            yield return null;
            Say("R10_scroll_1MB_first_frame", clock.Elapsed.TotalMilliseconds);
            int step = 0;
            yield return FrameTime("R10_scroll_1MB_frame", 240, () => move(++step / 240f));
            Clear();
            yield return null;
        }

        // R12
        yield return Settle();
        var mixed = new List<Component>();
        clock.Restart();
        for (int i = 0; i < 100; i++) mixed.Add(m_Engine.Label(parent, "مرحبا بالعالم Hello " + i, 18, new Vector2(400, 30)));
        m_Engine.ForceAll(mixed);
        Say("R12_mixed_100_create", clock.Elapsed.TotalMilliseconds);
        yield return null;
        Say("R12_mixed_100_and_frame", clock.Elapsed.TotalMilliseconds);
        Clear();
        yield return null;

        // R11
        if (!light)
        {
            yield return Settle();
            clock.Restart();
            for (int i = 0; i < 5000; i++)
            {
                Component world = m_Engine.World("Label " + (i % 1000), 1);
                world.transform.position = new Vector3(-40 + i % 100 * 0.8f, -20 + i / 100 * 0.8f, 0);
                m_Made.Add(world.gameObject);
            }
            yield return null;
            Say("R11_5000_world_first_frame", clock.Elapsed.TotalMilliseconds);
            yield return FrameTime("R11_5000_world_frame", 60, null);
            Clear();
            yield return null;
        }

        // R15, past Latin. The same system font on each; a prepared Latin atlas has no place here.
        if (m_Name != "tmp-static")
        {
            if (m_Engine is TmpEngine) s_TmpFont.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_FontAsset.CreateFontAsset("Microsoft YaHei", "Regular") };
            var cjk = new StringBuilder();
            for (int i = 0; i < 3000; i++) cjk.Append((char)(0x4E00 + i));
            yield return Settle();
            clock.Restart();
            Component han = m_Engine.Label(parent, cjk.ToString(), 18, new Vector2(1160, 680));
            m_Engine.Force(han);
            yield return null;
            Say("R15_3000_cjk_first_frame", clock.Elapsed.TotalMilliseconds);
            for (int i = 0; i < 30; i++) yield return null;
            yield return R15("R15_atlas_after_3000_cjk");
            Clear();
            yield return null;
        }

    }

    // ---------------------------------------------------------------- rows that need a scene of their own

    IEnumerator ExtraRows()
    {
        Transform parent = m_Canvas.transform;

        // R20, R21: 200 labels in a column, under a mask 400 high, and the same column with no mask, to see what the mask costs.
        var view = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
        view.transform.SetParent(parent, false);
        ((RectTransform)view.transform).sizeDelta = new Vector2(800, 400);
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(view.transform, false);
        var rect = (RectTransform)content.transform;
        for (int i = 0; i < 200; i++)
        {
            Component label = m_Engine.Label(content.transform, Sentence(i), 18, new Vector2(760, 26));
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0, 180 - i * 30);
        }
        yield return null;
        yield return FrameTime("R20_mask_still_frame", 240, null);
        int step = 0;
        // Far enough down that labels go out of the mask and others come in, every frame.
        yield return FrameTime("R21_mask_scroll_frame", 240, () => rect.anchoredPosition = new Vector2(0, Mathf.PingPong(++step * 12f, 5600)));
        rect.anchoredPosition = new Vector2(3000, 0);
        yield return FrameTime("R21_mask_swiped_away_frame", 240, null);
        rect.anchoredPosition = Vector2.zero;
        Destroy(view.GetComponent<RectMask2D>());
        yield return null;
        yield return FrameTime("R20_no_mask_still_frame", 240, null);
        Clear();
        yield return null;

        // R22: a keystroke in a field of 2 000 characters, focused, with its caret at the end.
        var system = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        m_Made.Add(system);
        yield return null;
        var body = new StringBuilder();
        for (int i = 0; body.Length < 2000; i++) body.Append(Sentence(i)).Append(i % 8 == 7 ? "\n" : " ");
        body.Length = 2000;
        Component field = m_Engine.Field(parent, body.ToString(), 18, new Vector2(1100, 640));
        for (int i = 0; i < 10; i++) yield return null;
        yield return FrameTime("R22_field_idle_frame", 120, null);
        var keys = new string[26];
        for (int i = 0; i < 26; i++) keys[i] = ((char)('a' + i)).ToString();
        int before = m_Engine.FieldLength(field), typed = 0;
        yield return FrameTime("R22_keystroke_2000", 120, () => m_Engine.Key(field, keys[typed++ % 26]));
        Say("R22_keystroke_check", m_Engine.FieldLength(field) - before, -1, "characters added of " + typed);
        Clear();
        yield return null;

        // R24: many texts leave and come back by their GameObjects.
        var labels = new List<Component>();
        for (int i = 0; i < 1000; i++) labels.Add(m_Engine.Label(parent, Sentence(i), 18, new Vector2(400, 30)));
        for (int i = 0; i < 5; i++) yield return null;
        var off = new List<double>();
        var on = new List<double>();
        var offBytes = new List<double>();
        var onBytes = new List<double>();
        var clock = new Stopwatch();
        for (int r = 0; r < 7; r++)
        {
            clock.Restart();
            foreach (Component c in labels) c.gameObject.SetActive(false);
            yield return null;
            off.Add(clock.Elapsed.TotalMilliseconds);
            offBytes.Add(GcNow());
            for (int i = 0; i < 3; i++) yield return null;
            clock.Restart();
            foreach (Component c in labels) c.gameObject.SetActive(true);
            yield return null;
            on.Add(clock.Elapsed.TotalMilliseconds);
            onBytes.Add(GcNow());
            for (int i = 0; i < 3; i++) yield return null;
        }
        Say("R24_deactivate_1000", Median(off), s_Gc.Valid ? (long)Median(offBytes) : -1);
        Say("R24_reactivate_1000", Median(on), s_Gc.Valid ? (long)Median(onBytes) : -1);
        Clear();
        yield return null;

        // R25: 64 score labels, by the call each engine gives for text that changes at runtime. The strings exist ahead, so that what allocates is the engine.
        var scores = new List<Component>();
        for (int i = 0; i < 64; i++) scores.Add(m_Engine.Label(parent, "Score: 00000", 18, new Vector2(200, 30)));
        var strings = new string[1000];
        for (int i = 0; i < strings.Length; i++) strings[i] = "Score: " + (i * 37).ToString("00000");
        yield return null;
        int tick = 0;
        yield return FrameTime("R25_64_score_labels", 240, () =>
        {
            tick++;
            for (int i = 0; i < scores.Count; i++) m_Engine.SetText(scores[i], strings[(tick + i) % strings.Length]);
        });
        Clear();
        yield return null;
    }

    // Asks nothing of any earlier text: this is the first a process draws.
    IEnumerator FirstRows()
    {
        for (int i = 0; i < 10; i++) yield return null;
        var clock = Stopwatch.StartNew();
        Component first = m_Engine.Label(m_Canvas.transform, "Hello, world", 18, new Vector2(400, 30));
        m_Engine.Force(first);
        Say("R23_first_text_call", clock.Elapsed.TotalMilliseconds);
        yield return null;
        Say("R23_first_text_frame", clock.Elapsed.TotalMilliseconds);
        // The render thread's part of the first draw ends in the frame after.
        yield return null;
        Say("R23_first_text_two_frames", clock.Elapsed.TotalMilliseconds);
        for (int i = 0; i < 5; i++) yield return null;
        clock.Restart();
        Component second = m_Engine.Label(m_Canvas.transform, "Hello, world", 18, new Vector2(400, 30));
        ((RectTransform)second.transform).anchoredPosition = new Vector2(0, 40);
        m_Engine.Force(second);
        yield return null;
        yield return null;
        Say("R23_second_text_two_frames", clock.Elapsed.TotalMilliseconds);
    }

    // The same first text, after the call Lettera offers for a loading screen. Other engines have their own ways, so this row is Lettera's only.
    IEnumerator PreparedRows()
    {
        if (m_Name != "lettera") yield break;
        for (int i = 0; i < 10; i++) yield return null;
        var clock = Stopwatch.StartNew();
        LetteraTextWarmUp.Prepare(s_LetteraFont);
        Say("R23p_prepare_call", clock.Elapsed.TotalMilliseconds);
        for (int i = 0; i < 10; i++) yield return null;
        clock.Restart();
        Component first = m_Engine.Label(m_Canvas.transform, "Hello, world", 18, new Vector2(400, 30));
        m_Engine.Force(first);
        Say("R23p_first_text_call", clock.Elapsed.TotalMilliseconds);
        yield return null;
        Say("R23p_first_text_frame", clock.Elapsed.TotalMilliseconds);
        yield return null;
        Say("R23p_first_text_two_frames", clock.Elapsed.TotalMilliseconds);
    }

    static readonly string[] Languages =
    {
        "The quick brown fox jumps over the lazy dog", "Съешь ещё этих мягких французских булок", "Ξεσκεπάζω την ψυχοφθόρα βδελυγμία",
        "مرحبا بالعالم، هذا نص عربي موصول", "שלום עולם, זה טקסט בעברית", "नमस्ते दुनिया, यह देवनागरी है", "สวัสดีชาวโลก นี่คือภาษาไทย",
        "你好世界,这是中文。", "こんにちは世界、これは日本語です。", "안녕하세요 세계, 이것은 한국어입니다",
    };

    static string Multilingual(int i, string tail = "") => Languages[i % Languages.Length] + " " + i + tail;

    // R26: 100 labels in ten languages, in a process that has shaped nothing. TextMesh Pro draws but does not shape these.
    IEnumerator MultilingualRows()
    {
        // A prepared Latin atlas does not hold these, so its row would say nothing.
        if (m_Name == "tmp-static") yield break;
        m_Engine.Fallbacks();
        for (int i = 0; i < 10; i++) yield return null;
        Transform parent = m_Canvas.transform;
        var labels = new List<Component>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) labels.Add(m_Engine.Label(parent, Multilingual(i), 18, new Vector2(800, 30)));
        yield return null;
        Say("R26_multilang_100_cold", clock.Elapsed.TotalMilliseconds);
        for (int i = 0; i < 5; i++) yield return null;
        var runs = new List<double>();
        var bytes = new List<double>();
        for (int r = 0; r < 7; r++)
        {
            string tail = new string('!', r + 1);
            clock.Restart();
            for (int i = 0; i < labels.Count; i++) m_Engine.SetText(labels[i], Multilingual(i, tail));
            yield return null;
            runs.Add(clock.Elapsed.TotalMilliseconds);
            bytes.Add(GcNow());
            yield return null;
        }
        Say("R26_multilang_100_edit", Median(runs), s_Gc.Valid ? (long)Median(bytes) : -1);
        Clear();
        for (int i = 0; i < 5; i++) yield return null;
        labels.Clear();
        clock.Restart();
        for (int i = 0; i < 100; i++) labels.Add(m_Engine.Label(parent, Multilingual(i), 18, new Vector2(800, 30)));
        yield return null;
        Say("R26_multilang_100_again", clock.Elapsed.TotalMilliseconds);
        Clear();
        yield return null;
    }

    // R16: a large font's first use.
    IEnumerator FontRows()
    {
        string path = Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "Fonts", "msyh.ttc");
        if (!File.Exists(path)) yield break;
        for (int i = 0; i < 10; i++) yield return null;
        Memory(out long privateBefore, out _);
        var clock = Stopwatch.StartNew();
        Component label = m_Engine.LargeLabel(m_Canvas.transform, path, "Hello 你好世界", 24, new Vector2(800, 60));
        Say("R16_large_font_load", clock.Elapsed.TotalMilliseconds);
        m_Engine.Force(label);
        yield return null;
        yield return null;
        Say("R16_large_font_first_text", clock.Elapsed.TotalMilliseconds);
        Memory(out long privateAfter, out _);
        Say("M_large_font_private", (privateAfter - privateBefore) / 1048576.0, -1, "file=" + new FileInfo(path).Length / 1048576 + "MB");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryCounters
    {
        public uint Size, PageFaults;
        public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, Private;
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")] static extern bool GetMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

    static void Memory(out long privateBytes, out long workingSet)
    {
        GetMemoryInfo(GetCurrentProcess(), out MemoryCounters counters, (uint)Marshal.SizeOf<MemoryCounters>());
        privateBytes = (long)counters.Private;
        workingSet = (long)counters.WorkingSet;
    }

    void SayMemory(string row)
    {
        Memory(out long privateBytes, out long workingSet);
        Say(row, privateBytes / 1048576.0, -1, "workingset=" + workingSet / 1048576 + "MB managed=" + Profiler.GetMonoUsedSizeLong() / 1048576 + "MB");
    }

    // Whole process, by the operating system: what the engines keep and give back. 15 seconds is longer than any engine here claims to wait.
    IEnumerator MemoryRows()
    {
        Transform parent = m_Canvas.transform;
        if (m_Name != "tmp-static") m_Engine.Fallbacks();
        for (int i = 0; i < 30; i++) yield return null;
        SayMemory("M0_before_text");
        var labels = new List<Component>();
        for (int i = 0; i < 100; i++) labels.Add(m_Engine.Label(parent, m_Name == "tmp-static" ? Sentence(i) : Multilingual(i), 18, new Vector2(800, 30)));
        for (int i = 0; i < 30; i++) yield return null;
        SayMemory("M1_100_labels");
        for (int r = 0; r < 60; r++)
        {
            for (int i = 0; i < labels.Count; i++) m_Engine.SetText(labels[i], m_Name == "tmp-static" ? Sentence(i + r) : Multilingual(i, new string('!', r % 7 + 1)));
            yield return null;
        }
        var page = new StringBuilder();
        for (int i = 0; page.Length < 100 * 1024; i++) page.Append(Sentence(i)).Append(i % 8 == 0 ? "\n" : " ");
        Component big = m_Engine.Label(parent, page.ToString(), 14, new Vector2(800, 100000));
        for (int i = 0; i < 1000; i++) labels.Add(m_Engine.Label(parent, Sentence(i), 18, new Vector2(400, 30)));
        for (int i = 0; i < 30; i++) yield return null;
        SayMemory("M2_heavy_scene");
        // The text stays, but nothing touches it.
        yield return new WaitForSecondsRealtime(15);
        SayMemory("M3_idle_15s_text_kept");
        Destroy(big.gameObject);
        foreach (Component c in labels) Destroy(c.gameObject);
        for (int i = 0; i < 5; i++) yield return null;
        SayMemory("M4_destroyed");
        yield return new WaitForSecondsRealtime(15);
        SayMemory("M5_idle_15s_after_destroy");
    }

    // ---------------------------------------------------------------- draw calls

    // Unity 6.4 has no recorder named Batches Count or Draw Calls Count in a player, which is why the earlier row was invalid. These are the ones it has.
    static readonly (string row, string name)[] Counters =
    {
        ("setpass", "SetPass Calls Count"), ("vertices", "Vertices Count"), ("triangles", "Triangles Count"),
        ("draw_calls", "Standard Draw Calls Count"), ("instanced_draw_calls", "Standard Instanced Draw Calls Count"),
        ("dynamic_batches", "Dynamic Batches Count"), ("dynamic_batched_draws", "Dynamic Batched Draw Calls Count"),
    };

    IEnumerator DrawCount(string row)
    {
        for (int i = 0; i < 5; i++) yield return null;
        ProfilerRecorder[] counters = Counters.Select(c => ProfilerRecorder.StartNew(ProfilerCategory.Render, c.name, 16)).ToArray();
        for (int i = 0; i < 14; i++) yield return null;
        for (int i = 0; i < Counters.Length; i++)
        {
            // The scene stands still, but a counter can read back a frame's start: the largest of the frames is the frame.
            long value = -1;
            if (counters[i].Valid)
                for (int j = 0; j < counters[i].Count; j++)
                    value = Math.Max(value, counters[i].GetSample(j).Value);
            if (value >= 0) Say("D_" + row + "_" + Counters[i].row, value);
            counters[i].Dispose();
        }
    }

    // The same scenes as the time rows, with the numbers counted while they stand still.
    IEnumerator DrawRows()
    {
        Transform parent = m_Canvas.transform;
        for (int i = 0; i < 10; i++) yield return null;
        yield return DrawCount("empty");
        Component one = m_Engine.Label(parent, "One label", 18, new Vector2(400, 30));
        yield return DrawCount("1_label");
        Clear();
        yield return null;
        for (int i = 0; i < 1000; i++) m_Engine.Label(parent, Sentence(i), 18, new Vector2(400, 30));
        yield return DrawCount("1000_labels");
        Clear();
        yield return null;
        if (m_Name != "tmp-static") m_Engine.Fallbacks();
        for (int i = 0; i < 100; i++) m_Engine.Label(parent, m_Name == "tmp-static" ? Sentence(i) : Multilingual(i), 18, new Vector2(800, 30));
        yield return DrawCount("100_multilingual");
        Clear();
        yield return null;
        var view = new GameObject("Mask", typeof(RectTransform), typeof(RectMask2D));
        view.transform.SetParent(parent, false);
        ((RectTransform)view.transform).sizeDelta = new Vector2(800, 400);
        for (int i = 0; i < 200; i++)
        {
            Component label = m_Engine.Label(view.transform, Sentence(i), 18, new Vector2(760, 26));
            ((RectTransform)label.transform).anchoredPosition = new Vector2(0, 180 - i * 30);
        }
        yield return DrawCount("200_masked");
        Clear();
        yield return null;
        for (int i = 0; i < 5000; i++)
        {
            Component world = m_Engine.World("Label " + (i % 1000), 1);
            world.transform.position = new Vector3(-40 + i % 100 * 0.8f, -20 + i / 100 * 0.8f, 0);
            m_Made.Add(world.gameObject);
        }
        yield return DrawCount("5000_world");
        Clear();
        yield return null;
        // Which counters this player has at all, for whoever finds a row above to be invalid.
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        var found = new StringBuilder();
        foreach (ProfilerRecorderHandle handle in handles)
        {
            ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.Category == ProfilerCategory.Render && description.Name.EndsWith("Count")) found.Append(description.Name).Append("; ");
        }
        Debug.Log("LETTERA_COMPARE counters " + found);
    }

    static object s_Hold;

    long ControlBytes()
    {
        s_Hold = new byte[4096];
        return GcNow();
    }

    IEnumerator R15(string row)
    {
        yield return null;
        long bytes = 0;
        int count = 0;
        // Asked of each engine, since a font made at runtime leaves its atlas without a name to look for.
        var textures = new HashSet<Texture>();
        if (!(m_Engine is TmpEngine))
        {
            foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>())
                if (t is Texture2DArray || (t.name ?? "").Contains("Atlas"))
                    textures.Add(t);
        }
        else
        {
            foreach (Texture2D t in s_TmpFont.atlasTextures) textures.Add(t);
            if (s_TmpFont.fallbackFontAssetTable != null)
                foreach (TMP_FontAsset font in s_TmpFont.fallbackFontAssetTable)
                    foreach (Texture2D t in font.atlasTextures)
                        textures.Add(t);
        }
        foreach (Texture t in textures)
        {
            if (t == null) continue;
            bytes += Profiler.GetRuntimeMemorySizeLong(t);
            count++;
        }
        Say(row, 0, bytes, "textures=" + count);
    }

    void Write(string path)
    {
        var json = new StringBuilder("{\"engine\":\"" + m_Name + "\",\"unity\":\"" + Application.unityVersion + "\",\"backend\":\"");
#if ENABLE_IL2CPP
        json.Append("IL2CPP");
#else
        json.Append("Mono");
#endif
        json.Append("\",\"development\":").Append(Debug.isDebugBuild ? "true" : "false").Append(",\"gpu\":\"").Append(SystemInfo.graphicsDeviceType).Append("\",\"device\":\"").Append(SystemInfo.processorType.Replace("\"", "")).Append("\",\"rows\":[");
        for (int i = 0; i < m_Rows.Count; i++)
        {
            (string row, double ms, long bytes, string note) = m_Rows[i];
            if (i > 0) json.Append(',');
            json.Append("{\"row\":\"").Append(row).Append("\",\"ms\":").Append(ms.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(",\"bytes\":").Append(bytes).Append(",\"note\":\"").Append(note.Replace("\"", "'")).Append("\"}");
        }
        json.Append("]}");
        File.WriteAllText(path, json.ToString());
    }

    // ---------------------------------------------------------------- sheets

    static readonly (string name, float size, string text)[][] Pages =
    {
        new[]
        {
            ("S1_latin", 16f, "A paragraph of plain text in the sizes a screen uses, with kerning such as AVATAR To Ty and ligatures such as office and fluffy."),
            ("S1_latin", 24f, "Heading With Kerning: AVATAR, To, Ty, Wa"),
            ("S1_latin", 48f, "Button Label"),
            ("S1_latin", 120f, "Rage fi"),
        },
        new[]
        {
            ("S2_rtl", 32f, "مرحبا بالعالم، هذا نص عربي موصول"),
            ("S2_rtl", 32f, "שלום עולם, זה טקסט בעברית"),
            ("S2_rtl", 32f, "العدد 123 مع English داخل الجملة"),
        },
        new[]
        {
            ("S3_complex", 32f, "नमस्ते दुनिया, यह देवनागरी है"),
            ("S3_complex", 32f, "নমস্কার পৃথিবী"),
            ("S3_complex", 32f, "வணக்கம் உலகம்"),
            ("S3_complex", 32f, "สวัสดีชาวโลก นี่คือภาษาไทย"),
        },
        new[]
        {
            ("S4_cjk", 32f, "你好世界,这是中文。こんにちは世界、これは日本語です。안녕하세요 세계"),
        },
        new[]
        {
            ("S5_emoji", 48f, "😀 🎉 👨‍👩‍👧‍👦 👍🏽 🇯🇵 ❤️"),
        },
        new[]
        {
            ("S6_fallback", 32f, "Latin Кириллица Ελληνικά עברית العربية 中文 हिन्दी"),
        },
        new[]
        {
            ("S7_markup", 28f, "<b>bold</b> <i>italic</i> <u>under</u> <s>strike</s> <color=#FF8040>color</color> <size=150%>size</size> <alpha=#80>alpha<alpha=#FF>"),
            ("S7_markup", 28f, "H<sub>2</sub>O x<sup>2</sup> <smallcaps>Small Caps</smallcaps> <uppercase>upper</uppercase> <mark=#FFFF0060>mark</mark>"),
            ("S7_markup", 28f, "<link=x>link</link> <nobr>no break</nobr> <noparse><b>literal</b></noparse>"),
            ("S7_markup_lettera", 28f, "<stroke=#3399FF,0.08>stroke</stroke> <shadow>shadow</shadow> <glow=#FF0,0.2>glow</glow> <tracking=0.1em>tracking</tracking> <otf=onum>0123</otf> <math>\\frac{a}{b}</math>"),
        },
        new[]
        {
            ("S9_sharp", 16f, "Small AKWXvwxz 0123"),
            ("S9_sharp", 200f, "AKWX"),
        },
        new[]
        {
            ("S10_motion", 48f, "<wave>Wave moves by tag</wave>"),
            ("S10_motion", 48f, "<shake>Shake moves by tag</shake>"),
            ("S10_motion", 48f, "<roll>Roll moves by tag</roll>"),
        },
    };

    IEnumerator Sheets(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach ((string name, float size, string text)[] page in Pages)
        {
            string sheet = page[0].name.Split('_')[0] + "_" + page[0].name.Split('_')[1];
            float y = -10;
            var made = new List<Component>();
            foreach ((string name, float size, string text) in page)
            {
                Component label = m_Engine.Label(m_Canvas.transform, text, size, new Vector2(1160, size * 3));
                var rect = (RectTransform)label.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(20, y);
                m_Engine.Color(label, UnityEngine.Color.white);
                if (m_Name == "tmp-fallback" && sheet == "S2_rtl")
                {
                    ((TMP_Text)label).isRightToLeftText = true;
                    ((TMP_Text)label).alignment = TextAlignmentOptions.Right;
                }
                y -= (size > 100 ? 1.4f : 3) * size + 6;
                made.Add(label);
            }
            yield return new WaitForSeconds(1.5f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, sheet + "_" + m_Name + ".png"));
            yield return new WaitForSeconds(0.5f);
            foreach (Component c in made) Destroy(c.gameObject);
            yield return null;
        }
        yield return EffectsSheet(folder);
        yield return Msdf(folder);
    }

    // S8: what each engine has for these, by its own route. Lettera's by a tag or style, TextMesh Pro's by a material.
    IEnumerator EffectsSheet(string folder)
    {
        var made = new List<Component>();
        (string word, string tag, string material)[] rows =
        {
            ("Outline", "<stroke=#3399FF,0.08>Outline</stroke>", "Outline"),
            ("Shadow", "<shadow=#000000,0.06,-0.06>Shadow</shadow>", "Underlay"),
            ("Glow", "<glow=#FFFF00,0.2>Glow</glow>", "Glow"),
            ("Gradient", "Gradient", null),
            ("Inner shadow", "<inner-shadow=#000000,0.05,-0.05>Inner shadow</inner-shadow>", "Inner"),
            ("Layers", "<shadow=#000000,0.08,-0.08><stroke=#3399FF,0.08><glow=#FFFF00,0.2>Layers</glow></stroke></shadow>", "All"),
        };
        float y = -10;
        foreach ((string word, string tag, string material) in rows)
        {
            bool ours = m_Name == "lettera";
            Component label = m_Engine.Label(m_Canvas.transform, ours ? tag : word, 64, new Vector2(1160, 100));
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, y);
            y -= 110;
            m_Engine.Color(label, UnityEngine.Color.white);
            if (ours && material == null)
            {
                var fill = new FillEffect();
                fill.SetPaint(LetteraPaint.Of(LetteraGradient.Of(new Color(1, 0.3f, 0), new Color(1, 1, 0))));
                ((LetteraText)label).Styles.Add(LetteraStyle.ForWholeText(fill));
                ((LetteraText)label).MarkStylesDirty();
            }
            else if (m_Engine is TmpEngine)
            {
                var text = (TMP_Text)label;
                if (material == null)
                {
                    text.enableVertexGradient = true;
                    text.colorGradient = new VertexGradient(new Color(1, 0.3f, 0), new Color(1, 1, 0), new Color(1, 0.3f, 0), new Color(1, 1, 0));
                }
                else TmpMaterial(text, material);
            }
            made.Add(label);
        }
        yield return new WaitForSeconds(1.5f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "S8_effects_" + m_Name + ".png"));
        yield return new WaitForSeconds(0.5f);
        foreach (Component c in made) Destroy(c.gameObject);
        yield return null;
    }

    static void TmpMaterial(TMP_Text text, string name)
    {
        // The build keeps a material per set of keywords, or a player would have stripped those shader variants.
        Material keep = Resources.Load<Material>("Tmp_" + name);
        var material = new Material(text.fontSharedMaterial) { shader = keep.shader };
        foreach (string keyword in keep.shaderKeywords) material.EnableKeyword(keyword);
        material.SetFloat(ShaderUtilities.ID_FaceDilate, 0);
        if (name == "Outline" || name == "All")
        {
            // Outside the face, as a project would set it: a width alone also eats into the letter.
            material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.3f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.2f, 0.6f, 1));
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.3f);
        }
        if (name == "Underlay" || name == "Inner" || name == "All")
        {
            material.SetColor(ShaderUtilities.ID_UnderlayColor, UnityEngine.Color.black);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.5f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
            if (name == "Inner")
            {
                material.SetFloat(ShaderUtilities.ID_UnderlayDilate, -0.2f);
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.2f);
            }
        }
        if (name == "Glow" || name == "All")
        {
            material.SetColor(ShaderUtilities.ID_GlowColor, new Color(1, 1, 0));
            material.SetFloat(ShaderUtilities.ID_GlowOuter, 1f);
            material.SetFloat(ShaderUtilities.ID_GlowInner, 0f);
            material.SetFloat(ShaderUtilities.ID_GlowPower, 0.5f);
        }
        ShaderUtilities.UpdateShaderRatios(material);
        text.extraPadding = true;
        text.fontSharedMaterial = material;
    }

    // S9's second half: Lettera's sharper mode. TextMesh Pro has no such mode.
    IEnumerator Msdf(string folder)
    {
        if (m_Name != "lettera") yield break;
        Component label = m_Engine.Label(m_Canvas.transform, "AKWX", 200, new Vector2(1160, 300));
        ((LetteraText)label).RenderMode = LetteraRenderMode.MSDF;
        m_Engine.Color(label, UnityEngine.Color.white);
        yield return new WaitForSeconds(1.5f);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder, "S9_sharp_msdf_lettera.png"));
        yield return new WaitForSeconds(0.5f);
        Destroy(label.gameObject);
        yield return null;
    }
}
