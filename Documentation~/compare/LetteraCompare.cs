// Lettera against TextMesh Pro: one workload, one font file, the same scene. Run a player with -compare <engine> -out <file.json>.
// Engines: lettera, tmp-static (an atlas prepared ahead with the characters used), tmp-dynamic (an atlas filled as it goes).
// The rows are in Documentation~/Comparison.md. No number in the report is typed by hand: tools turn this file's JSON into its tables.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FirstSymbol.Lettera;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class LetteraCompare : MonoBehaviour
{
    interface IEngine
    {
        Component Label(Transform parent, string text, float size, Vector2 box);
        Component World(string text, float size);
        void Text(Component c, string text);
        void Color(Component c, Color color);
        void Force(Component c);
        void Dirty(Component c);
    }

    sealed class LetteraEngine : IEngine
    {
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
    }

    sealed class TmpEngine : IEngine
    {
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
    }

    static LetteraFont s_LetteraFont;
    static TMP_FontAsset s_TmpFont;
    static readonly ProfilerRecorder s_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

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
        if (m_Name != "lettera") m_Canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        Camera camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -60);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = UnityEngine.Color.black;
        yield return null;
        if (Arg("-sheets") != null)
        {
            yield return Sheets(Arg("-sheets"));
            Application.Quit();
            yield break;
        }
        yield return Rows();
        Write(Arg("-out"));
        Debug.Log("LETTERA_COMPARE done " + m_Name);
        Application.Quit();
    }

    void Prepare(string fonts)
    {
        string path = Path.Combine(fonts, "NotoSans-Regular.ttf");
        if (m_Name == "lettera")
        {
            s_LetteraFont = LetteraFont.CreateFromBytes(File.ReadAllBytes(path));
            m_Engine = new LetteraEngine();
            return;
        }
        // The same file, imported as a font: TextMesh Pro reads a Font asset, and a plain path gives it nothing to read.
        Font source = Resources.Load<Font>("NotoSans-Regular");
        if (source == null) throw new InvalidOperationException("Assets/Compare/Resources must hold NotoSans-Regular.ttf.");
        var clock = Stopwatch.StartNew();
        s_TmpFont = TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        if (m_Name == "tmp-static")
        {
            s_TmpFont.TryAddCharacters(Latin, out string _);
            s_TmpFont.atlasPopulationMode = AtlasPopulationMode.Static;
        }
        Say("prepare_font", clock.Elapsed.TotalMilliseconds);
        m_Engine = new TmpEngine();
    }

    void Say(string row, double ms, long bytes = -1, string note = "") => m_Rows.Add((row, ms, bytes, note));

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

    // A frame's whole time, since each engine does its work at a different moment, and a row must not be won by moving work out of the clock.
    IEnumerator FrameTime(string row, int frames, Action each, string note = "")
    {
        var times = new List<double>();
        long allocated = -1;
        for (int i = 0; i < 5; i++) yield return null;
        var clock = new Stopwatch();
        for (int i = 0; i < frames; i++)
        {
            clock.Restart();
            each?.Invoke();
            yield return null;
            times.Add(clock.Elapsed.TotalMilliseconds);
            long gc = GcNow();
            if (gc >= 0) allocated = Math.Max(allocated, gc);
        }
        Say(row, Median(times), allocated, note);
    }

    IEnumerator Rows()
    {
        Transform parent = m_Canvas.transform;

        // R1, R2, R7
        var labels = new List<Component>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) labels.Add(m_Engine.Label(parent, Sentence(i), 18, new Vector2(400, 30)));
        foreach (Component c in labels) m_Engine.Force(c);
        Say("R1_create_1000", clock.Elapsed.TotalMilliseconds);
        clock.Restart();
        yield return null;
        Say("R1_first_frame_after", clock.Elapsed.TotalMilliseconds);
        var runs = new List<double>();
        for (int r = 0; r < 5; r++)
        {
            foreach (Component c in labels) m_Engine.Dirty(c);
            clock.Restart();
            foreach (Component c in labels) m_Engine.Force(c);
            runs.Add(clock.Elapsed.TotalMilliseconds);
            yield return null;
        }
        Say("R2_rebuild_1000", Median(runs));
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

        // R8, R9
        var page = new StringBuilder();
        foreach (int size in new[] { 10, 100, 1000 })
        {
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
        }

        // R12
        var mixed = new List<Component>();
        clock.Restart();
        for (int i = 0; i < 100; i++) mixed.Add(m_Engine.Label(parent, "مرحبا بالعالم Hello " + i, 18, new Vector2(400, 30)));
        foreach (Component c in mixed) m_Engine.Force(c);
        Say("R12_mixed_100_create", clock.Elapsed.TotalMilliseconds);
        Clear();
        yield return null;

        // R11
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

        Say("control_gc_counter", 0, ControlBytes(), s_Gc.Valid ? "valid" : "not valid in this player: bytes columns say nothing");
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
        foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>())
        {
            string name = t.name ?? "";
            bool ours = t is Texture2DArray || name.Contains("Atlas") || name.Contains("NotoSans");
            if (!ours) continue;
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
            ("S7_markup", 28f, "<b>bold</b> <i>italic</i> <u>under</u> <s>strike</s> <color=#FF8040>color</color> <size=150%>size</size> <alpha=#80>alpha</alpha>"),
            ("S7_markup", 28f, "H<sub>2</sub>O x<sup>2</sup> <smallcaps>Small Caps</smallcaps> <uppercase>upper</uppercase> <mark=#FFFF0060>mark</mark>"),
            ("S7_markup", 28f, "<link=x>link</link> <nobr>no break</nobr> <noparse><b>literal</b></noparse>"),
            ("S7_markup_lettera", 28f, "<stroke=#3399FF,0.08>stroke</stroke> <shadow>shadow</shadow> <glow=#FF0,0.2>glow</glow> <fill=linear(#F40,#FF0)>gradient</fill> <wave>wave</wave> <math>\\frac{a}{b}</math>"),
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
                y -= size * 3 + 6;
                made.Add(label);
            }
            yield return new WaitForSeconds(1.5f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, sheet + "_" + m_Name + ".png"));
            yield return new WaitForSeconds(0.5f);
            foreach (Component c in made) Destroy(c.gameObject);
            yield return null;
        }
    }
}
