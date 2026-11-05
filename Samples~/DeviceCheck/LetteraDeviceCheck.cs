using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Samples
{
    /// <summary>
    /// One guided session per device, for what no machine here can answer: one screen a step, Pass, Fail or Skip, and one text block at the end to send back.
    /// Add it to an empty scene, or use Lettera > Device Check. Everything is built from code.
    /// </summary>
    public class LetteraDeviceCheck : MonoBehaviour
    {
        /// <summary>One step: what to do, what it answers, and whether a person can only do it on a phone.</summary>
        public readonly struct Step
        {
            public readonly string Id, Title, Instructions;
            public readonly string[] Covers;

            /// <summary>The same step in Russian, shown on a device whose language is Russian; null shows the English.</summary>
            public readonly string TitleRu, InstructionsRu;

            public Step(string id, string title, string instructions, params string[] covers)
            {
                Id = id;
                Title = title;
                Instructions = instructions;
                Covers = covers;
                TitleRu = InstructionsRu = null;
            }

            Step(Step english, string titleRu, string instructionsRu)
            {
                this = english;
                TitleRu = titleRu;
                InstructionsRu = instructionsRu;
            }

            /// <summary>This step with its Russian words.</summary>
            public Step Ru(string title, string instructions) => new Step(this, title, instructions);

            public string ShownTitle => Russian && TitleRu != null ? TitleRu : Title;
            public string ShownInstructions => Russian && InstructionsRu != null ? InstructionsRu : Instructions;
        }

        /// <summary>Russian on a device set to Russian, or when started with <c>-lettera-kit-lang ru</c>; <c>-lettera-kit-lang en</c> keeps English.</summary>
        public static bool Russian
        {
            get
            {
                if (s_Russian == null)
                {
                    string[] arguments = Environment.GetCommandLineArgs();
                    int at = Array.IndexOf(arguments, "-lettera-kit-lang");
                    s_Russian = at >= 0 && at + 1 < arguments.Length ? arguments[at + 1] == "ru" : Application.systemLanguage == SystemLanguage.Russian;
                }
                return s_Russian.Value;
            }
            set => s_Russian = value;
        }

        static bool? s_Russian;

        static string T(string english, string russian) => Russian ? russian : english;

        /// <summary>The steps in order. The first ones answer whether the rest can run at all.</summary>
        public static readonly Step[] Steps =
        {
            new Step("basics", "Text and emoji", "Below should be words in several scripts and an emoji. Does it all show?", "player")
                .Ru("Текст и эмодзи", "Ниже должны быть слова на нескольких письменностях и эмодзи. Всё ли видно и выглядит правильно?"),
            new Step("fallback", "Keyboard, plain route", "Tap the field and type. With the native gate off this is the route every phone had before.", "touch keyboard")
                .Ru("Клавиатура, простой путь", "Коснитесь поля и напечатайте что-нибудь. Это путь ввода, который работает на любом телефоне без нативных маршрутов."),
            new Step("rise", "Keyboard rises", "Tap the field once, quickly. Does the keyboard rise on the first tap? In Safari on an iPhone this is the top risk: also try a tap on the very edge.", "real keyboard rise", "page focus")
                .Ru("Клавиатура появляется", "Коснитесь поля один раз, коротко. Появляется ли клавиатура с первого касания? Попробуйте коснуться и у самого края поля."),
            new Step("mirror", "Typing", "Type, select by holding, take an autocorrect suggestion on a word, hold Delete, take an emoji suggestion, dictate, swipe type.", "host", "mirror")
                .Ru("Набор текста", "Напечатайте текст, выделите слово удержанием, примите подсказку автозамены, зажмите «Удалить», выберите эмодзи из подсказок, надиктуйте фразу голосом, наберите слово свайпом."),
            new Step("composition", "Composition", "With a Japanese keyboard type a word: does it show inline and commit? Then a Korean one. On Windows: the Japanese Microsoft IME, candidates beside the caret, Escape cancels.", "composition", "real ime")
                .Ru("Составной ввод", "Если есть японская клавиатура, наберите слово: видно ли его прямо в поле с подчёркиванием, и подтверждается ли оно? Затем то же с корейской. Если таких клавиатур нет, нажмите «Пропустить»."),
            new Step("traits", "Keyboards", "Tap each small field: number, e-mail, send key, one-time code, dark. Does each keyboard match its label?", "traits")
                .Ru("Виды клавиатуры", "Коснитесь каждого маленького поля: число, e-mail, кнопка «Отправить», одноразовый код, тёмная. Совпадает ли клавиатура с подписью поля?"),
            new Step("replica", "Replica", "Tap the replica field. The system's own control should stand over it, and the character limit should still hold.", "replica")
                .Ru("Системное поле поверх", "Коснитесь поля. Поверх него должно встать системное поле ввода Android, а ограничение в 10 символов должно соблюдаться."),
            new Step("clipboard", "Clipboard", "Copy from the field and paste into Notes. Copy from Notes and paste here. Paste a screenshot. Count how many times the system says something was pasted: it should be only when you paste. On Windows: to and from Word and Chrome.", "clipboard text", "clipboard images", "paste control")
                .Ru("Буфер обмена", "Скопируйте текст из поля и вставьте в Заметки. Скопируйте из Заметок и вставьте сюда. Попробуйте вставить скриншот. Система показывает уведомление о вставке: оно должно появляться только когда вы сами вставляете."),
            new Step("menu", "Menu and loupe", "Hold on a word. Does the system's menu show clear of the selection, and do Cut, Copy, Paste and Select All work? A text app on Android, Look Up on iOS. Hold and drag: does the loupe follow and show what is under it?", "system menu", "text apps", "loupe")
                .Ru("Меню и лупа", "Удерживайте палец на слове. Появляется ли системное меню, не закрывая выделение, и работают ли «Вырезать», «Копировать», «Вставить», «Выбрать всё»? Есть ли в меню пункты других приложений (например, «Перевести»)? Удерживайте и ведите палец: следует ли лупа за ним и показывает ли, что под пальцем?"),
            new Step("fonts", "Fonts", "Close the app and open it again. Does text show at once? The figures below should say mapped, not memory.", "font mapping")
                .Ru("Шрифты", "Закройте приложение и откройте снова. Появляется ли текст сразу? Цифры ниже должны показывать, что шрифты отображены с диска (mapped), а не держатся в памяти."),
            new Step("color", "Color glyphs", "Are the color glyphs below drawn with gradients? In Safari: do many emoji appear in one go?", "colrv1", "web emoji batching")
                .Ru("Цветные глифы", "Нарисованы ли эмодзи ниже цветными, с плавными градиентами, без пустых квадратов?"),
            new Step("math", "Formulas", "Below are fractions, roots, tall brackets, a matrix and accents. Are they sharp, joined and in place? Is the tall bracket one smooth shape at half alpha?", "math layout", "native v3", "bundled font")
                .Ru("Формулы", "Ниже дроби, корни, высокие скобки, матрица и акценты. Они чёткие, соединённые и на своих местах? Высокая полупрозрачная скобка выглядит одной гладкой фигурой, без более тёмных стыков?"),
            new Step("math-system", "System math font", "This line asks the system for a math font, and says which it found. Does the formula look right, or does it say none?", "system math font")
                .Ru("Системный математический шрифт", "Эта строка берёт математический шрифт у системы. Формула выглядит правильно?"),
            new Step("math-select", "Formula as one unit", "Tap beside a formula. Hold to select it. Copy and paste to Notes: you should see the source. On a Windows machine: paste into Word and Chrome.", "selection", "copy", "clipboard HTML")
                .Ru("Формула как одно целое", "Коснитесь рядом с формулой. Удержанием выделите её: она должна выделяться целиком. Скопируйте и вставьте в Заметки: должен появиться её исходный текст."),
            new Step("solid", "3D text", "These letters are solid and lit, and turn slowly. Do they look closed and smooth? Do shadows fall on the ground? Do they fade by dots, and does the frame rate below stay smooth?", "solid shader", "budgets")
                .Ru("Объёмный текст", "Эти буквы объёмные, освещённые и медленно поворачиваются. Они выглядят замкнутыми и гладкими? Падают ли тени на пол? Плавно ли всё движется?"),
            new Step("solid-many", "Many solids", "500 labels. Note the frame time.", "instancing", "batching", "budgets")
                .Ru("Много объёмных надписей", "500 надписей. Плавно ли? Если нет, напишите в заметке, насколько заметно."),
            new Step("motion", "Motion", "Does the moving text run smoothly?", "os preferences", "motion")
                .Ru("Анимация", "Движется ли текст плавно?"),
            new Step("scroll", "Scrolling", "Swipe this long text up and down, fast and slow. Does it follow the finger, coast, and stop at the ends? Note the frame time below.", "documents", "swipe", "frame rate", "memory")
                .Ru("Прокрутка", "Листайте этот длинный текст вверх и вниз, быстро и медленно. Следует ли он за пальцем, катится ли по инерции и останавливается ли на концах? Время кадра показано ниже."),
            new Step("touch", "Handles", "Do selection handles, the loupe and the context menu look right, and sit where they should?", "handles")
                .Ru("Маркеры выделения", "Выделите слово. Правильно ли выглядят маркеры выделения, лупа и меню, и стоят ли они там, где должны? Можно ли тянуть маркеры?"),
            new Step("compare", "Comparison build", "If you were also given the comparison build, run it now: press Start, leave the device alone until it is done, and send its block too. If not, Skip.", "comparison")
                .Ru("Сборка для сравнения", "Если вам дали ещё и сборку для сравнения, запустите её: нажмите Start и не трогайте устройство до конца. Если нет, нажмите «Пропустить»."),
        };

#if !LETTERA_NO_3D
        readonly List<GameObject> m_Solids = new List<GameObject>();
        Material m_SolidMaterial;

        // Solids draw in the scene behind the page, under a light of their own, and go when the page does.
        void SolidPage(int count)
        {
            ClearSolids();
            if (m_SolidMaterial == null) m_SolidMaterial = Resources.Load<Material>("LetteraSolidTest");
            if (m_SolidMaterial == null)
            {
                Shader shader = Shader.Find("Lettera/Solid Lit");
                if (shader != null) m_SolidMaterial = new Material(shader);
            }
            if (m_SolidMaterial == null)
            {
                Label(T("The solid shader is not in this build.", "Шейдера объёмного текста нет в этой сборке."), 28, new Vector2(0, 0.45f), new Vector2(1, 0.6f));
                return;
            }
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Kit Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
                m_Solids.Add(camera.gameObject);
            }
            camera.transform.SetPositionAndRotation(new Vector3(0, 2, -24), Quaternion.Euler(4, 0, 0));
            var light = new GameObject("Kit Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            m_Solids.Add(light.gameObject);
            m_Solids.Add(MakeGround(new Vector3(0, -4, 0), 1, m_SolidMaterial));
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Solid", typeof(RectTransform));
                ((RectTransform)go.transform).sizeDelta = new Vector2(30, 6);
                LetteraText3D solid = go.AddComponent<LetteraText3D>();
                solid.FaceMaterial = m_SolidMaterial;
                solid.WordWrap = false;
                solid.HorizontalAlignment = LetteraHorizontalAlignment.Center;
                if (count == 1)
                {
                    solid.FontSize = 5;
                    solid.Depth = 0.25f;
                    solid.Text = "Solid <wave>Text</wave>";
                    go.AddComponent<KitTurn>();
                }
                else
                {
                    solid.FontSize = 0.8f;
                    go.transform.position = new Vector3(-12 + i % 25, -3 + i / 25 * 0.8f, 0);
                    solid.Text = "Label";
                }
                m_Solids.Add(go);
            }
        }

        // A plane of 10 by 10 like the built-in one, but with no collider: a project with no Physics module has none to give it.
        static GameObject MakeGround(Vector3 position, float scale, Material material)
        {
            var go = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer));
            var mesh = new Mesh { name = "Ground" };
            mesh.vertices = new[] { new Vector3(-5, 0, -5), new Vector3(-5, 0, 5), new Vector3(5, 0, 5), new Vector3(5, 0, -5) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.transform.position = position;
            go.transform.localScale = new Vector3(scale, 1, scale);
            return go;
        }

        void ClearSolids()
        {
            foreach (GameObject go in m_Solids)
                if (go != null)
                    Destroy(go);
            m_Solids.Clear();
        }

        sealed class KitTurn : MonoBehaviour
        {
            void Update() => transform.rotation = Quaternion.Euler(0, Mathf.Sin(Time.time) * 35, 0);
        }
#endif

        readonly List<string> m_Results = new List<string>();
        RectTransform m_Root;
        LetteraText m_Title, m_Body, m_Facts;
        int m_Index;
        string m_Note = "";
        LetteraEditable m_NoteField;
        LetteraText m_Frame;

        void Start()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                Type module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) events.AddComponent(module);
                else events.AddComponent<StandaloneInputModule>();
            }
            var canvasObject = new GameObject("Device Check", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.matchWidthOrHeight = 0.5f;
            m_Root = (RectTransform)canvasObject.transform;
            ShowStep();
            string[] arguments = Environment.GetCommandLineArgs();
            if (Array.IndexOf(arguments, "-lettera-kit-auto") >= 0 || Application.absoluteURL.Contains("kit=auto")) AutoRun();
        }

        // By script, to prove the kit itself: every step gets an answer, and the report comes out whole.
        void AutoRun()
        {
            while (m_Index < Steps.Length) Answer("Skip");
            Debug.Log("LETTERA_KIT_RESULT " + Result().Replace("\n", "|"));
#if !UNITY_WEBGL
            if (Application.isBatchMode) Application.Quit();
#endif
        }

        LetteraText Label(string text, float size, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(m_Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(24, 8);
            rect.offsetMax = new Vector2(-24, -8);
            var label = go.AddComponent<LetteraText>();
            label.Text = text;
            label.FontSize = size;
            label.WordWrap = true;
            label.raycastTarget = false;
            return label;
        }

        void Button(string text, Vector2 anchorMin, Vector2 anchorMax, Color color, Action action)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(m_Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = new Vector2(0, 0);
            rect.offsetMin = new Vector2(8, 8);
            rect.offsetMax = new Vector2(-8, -8);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Button>().onClick.AddListener(() => action());
            var label = new GameObject("Text", typeof(RectTransform));
            label.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var shown = label.AddComponent<LetteraText>();
            shown.Text = text;
            shown.FontSize = 32;
            shown.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            shown.VerticalAlignment = LetteraVerticalAlignment.Middle;
            shown.raycastTarget = false;
        }

        void Clear()
        {
            for (int i = m_Root.childCount - 1; i >= 0; i--) Destroy(m_Root.GetChild(i).gameObject);
        }

        void ShowStep()
        {
            Clear();
#if !LETTERA_NO_3D
            ClearSolids();
#endif
            if (m_Index >= Steps.Length)
            {
                ShowResult();
                return;
            }
            Step step = Steps[m_Index];
            Label($"{m_Index + 1} {T("of", "из")} {Steps.Length}: {step.ShownTitle}", 40, new Vector2(0, 0.88f), new Vector2(1, 1));
            Label(step.ShownInstructions, 26, new Vector2(0, 0.62f), new Vector2(1, 0.88f));
            Sample(step.Id);
            GameObject note = LetteraFieldFactory.CreateInputField(m_Root, T("A note, if there is anything to say", "Заметка, если есть что сказать"));
            var noteRect = (RectTransform)note.transform;
            noteRect.anchorMin = new Vector2(0, 0.2f);
            noteRect.anchorMax = new Vector2(1, 0.27f);
            noteRect.offsetMin = new Vector2(24, 0);
            noteRect.offsetMax = new Vector2(-24, 0);
            m_NoteField = note.GetComponentInChildren<LetteraEditable>();
            Button(T("Pass", "Работает"), new Vector2(0, 0), new Vector2(0.34f, 0.18f), new Color(0.15f, 0.55f, 0.3f), () => Answer("Pass"));
            Button(T("Fail", "Не работает"), new Vector2(0.34f, 0), new Vector2(0.67f, 0.18f), new Color(0.7f, 0.2f, 0.2f), () => Answer("Fail"));
            Button(T("Skip", "Пропустить"), new Vector2(0.67f, 0), new Vector2(1, 0.18f), new Color(0.35f, 0.35f, 0.4f), () => Answer("Skip"));
        }

        // What a step needs on screen.
        void Sample(string id)
        {
            switch (id)
            {
                case "basics":
                    // A browser exposes no fonts, and the web's default has Latin, Cyrillic and Greek. That is no failure of the device.
                    Label(Application.platform == RuntimePlatform.WebGLPlayer ? "Hello, Мир, Γειά 🎉" : "Hello, Мир, مرحبا, שלום, नमस्ते 🎉", 40, new Vector2(0, 0.4f), new Vector2(1, 0.6f));
                    break;
                case "fonts":
                    LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
                    Label(T($"{sums.Faces} faces: {sums.RetainedBytes / 1024} KB held in memory, {sums.MappedBytes / 1024} KB mapped from disk, {sums.CacheFolderBytes / 1024} KB in the cache folder.",
                        $"Начертаний: {sums.Faces}. В памяти: {sums.RetainedBytes / 1024} КБ, отображено с диска (mapped): {sums.MappedBytes / 1024} КБ, в папке кэша: {sums.CacheFolderBytes / 1024} КБ."), 26, new Vector2(0, 0.4f), new Vector2(1, 0.6f));
                    break;
                case "color":
                    Label("🎉😀🚀🌈🍕🐱🎸🌍🔥⭐🦄🎈🍩🌻🐙🎨", 48, new Vector2(0, 0.4f), new Vector2(1, 0.6f));
                    break;
                case "fallback":
                case "rise":
                case "mirror":
                case "composition":
                case "clipboard":
                case "menu":
                case "touch":
                    Field(new Vector2(0, 0.3f), new Vector2(1, 0.6f));
                    break;
                case "traits":
                    Traits();
                    break;
                case "replica":
                    GameObject replica = Field(new Vector2(0, 0.42f), new Vector2(1, 0.5f));
                    LetteraEditable field = replica.GetComponentInChildren<LetteraEditable>();
                    field.AddRule(new ReplicaFieldRule());
                    field.AddRule(new MaxLengthRule { Limit = 10 });
                    break;
                case "math":
                    Label("<math=display>\\frac{a}{\\sqrt{b+1}} + \\sum_{i=0}^n \\hat{x}_i^2</math>", 40, new Vector2(0, 0.55f), new Vector2(1, 0.75f));
                    Label("<alpha=#80><math=display>\\left(\\begin{pmatrix}1&0\\\\0&1\\end{pmatrix} \\left\\{\\frac{a}{\\frac{b}{\\frac{c}{d}}}\\right.\\right)</math></alpha>", 34, new Vector2(0, 0.3f), new Vector2(1, 0.55f));
                    break;
                case "math-system":
                    Label("<math>\\int_0^1 \\frac{dx}{\\sqrt{x}}</math>", 40, new Vector2(0, 0.45f), new Vector2(1, 0.65f));
                    break;
                case "math-select":
                    GameObject mathField = Field(new Vector2(0, 0.42f), new Vector2(1, 0.6f));
                    mathField.GetComponentInChildren<LetteraEditable>().Source = "a <math>x^2</math> b";
                    break;
#if !LETTERA_NO_3D
                case "solid":
                    SolidPage(1);
                    break;
                case "solid-many":
                    SolidPage(500);
                    break;
#else
                case "solid":
                case "solid-many":
                    Label(T("The 3D module is compiled out of this build.", "Модуль 3D исключён из этой сборки."), 28, new Vector2(0, 0.45f), new Vector2(1, 0.6f));
                    break;
#endif
                case "motion":
                    Label(T("<wave>This text should move smoothly.</wave>", "<wave>Этот текст должен двигаться плавно.</wave>"), 32, new Vector2(0, 0.4f), new Vector2(1, 0.6f));
                    break;
                case "scroll":
                    ScrollPage();
                    break;
            }
        }

        void ScrollPage()
        {
            var view = new GameObject("View", typeof(RectTransform), typeof(Image));
            view.transform.SetParent(m_Root, false);
            view.GetComponent<Image>().color = new Color(1, 1, 1, 0.05f);
            var viewRect = (RectTransform)view.transform;
            viewRect.anchorMin = new Vector2(0, 0.33f);
            viewRect.anchorMax = new Vector2(1, 0.62f);
            viewRect.offsetMin = new Vector2(24, 0);
            viewRect.offsetMax = new Vector2(-24, 0);
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(view.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            var text = go.AddComponent<LetteraText>();
            text.FontSize = 24;
            var body = new StringBuilder();
            string line = T(": a long document scrolls with only what is near the view laid out.\n", ": длинный документ прокручивается, а свёрстано только то, что рядом с окном.\n");
            string word = T("Line ", "Строка ");
            for (int i = 0; i < 20000; i++) body.Append(word).Append(i).Append(line);
            text.OpenDocument(LetteraRope.FromString(body.ToString()), takeOwnership: true);
            view.AddComponent<LetteraDocumentScroller>().Text = text;
            m_Frame = Label("", 24, new Vector2(0, 0.27f), new Vector2(1, 0.33f));
        }

        void Update()
        {
            if (m_Frame == null || Time.frameCount % 15 != 0) return;
            m_Frame.SetText(Russian ? $"{Time.smoothDeltaTime * 1000:0.0} мс на кадр" : $"{Time.smoothDeltaTime * 1000:0.0} ms a frame");
        }

        GameObject Field(Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject area = LetteraFieldFactory.CreateTextArea(m_Root, T("Tap here", "Коснитесь здесь"));
            var rect = (RectTransform)area.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(24, 0);
            rect.offsetMax = new Vector2(-24, 0);
            area.GetComponentInChildren<LetteraEditable>().InsertText(T("The quick brown fox jumps over the lazy dog.", "Съешь же ещё этих мягких французских булок, да выпей чаю."), "code.set");
            return area;
        }

        void Traits()
        {
            var kinds = new (string label, LetteraKeyboardTraits traits)[]
            {
                (T("Number", "Число"), new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Number }),
                ("E-mail", new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Email, AutofillHint = LetteraAutofill.Email }),
                (T("Send key", "Кнопка «Отправить»"), new LetteraKeyboardTraits { ReturnKey = LetteraReturnKey.Send }),
                (T("One-time code", "Одноразовый код"), new LetteraKeyboardTraits { Kind = LetteraKeyboardKind.Number, AutofillHint = LetteraAutofill.OneTimeCode }),
                (T("Dark keyboard", "Тёмная клавиатура (iOS)"), new LetteraKeyboardTraits { IosAppearance = LetteraIosAppearance.Dark }),
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                GameObject input = LetteraFieldFactory.CreateInputField(m_Root, kinds[i].label);
                var rect = (RectTransform)input.transform;
                float top = 0.6f - i * 0.065f;
                rect.anchorMin = new Vector2(0, top - 0.055f);
                rect.anchorMax = new Vector2(1, top);
                rect.offsetMin = new Vector2(24, 0);
                rect.offsetMax = new Vector2(-24, 0);
                input.GetComponentInChildren<LetteraEditable>().AddRule(new KeyboardTraitsRule { Traits = kinds[i].traits });
            }
        }

        void Answer(string answer)
        {
            m_Note = m_NoteField != null ? m_NoteField.PlainText : "";
            Step step = Steps[m_Index];
            m_Results.Add($"{step.Id}: {answer}" + (m_Note.Length > 0 ? " (" + m_Note + ")" : ""));
            m_Index++;
            ShowStep();
        }

        /// <summary>Everything to send back, as one block.</summary>
        public string Result()
        {
            var text = new StringBuilder();
            text.Append("Lettera device check\n");
            text.Append("Platform: ").Append(Application.platform).Append(" / ").Append(SystemInfo.operatingSystem).Append('\n');
            text.Append("Device: ").Append(SystemInfo.deviceModel).Append('\n');
            text.Append("Unity: ").Append(Application.unityVersion).Append(", graphics ").Append(SystemInfo.graphicsDeviceType).Append('\n');
            text.Append("Frame: ").Append((Time.smoothDeltaTime * 1000).ToString("0.0")).Append(" ms\n");
            text.Append("Memory: ").Append(SystemInfo.systemMemorySize).Append(" MB in the device, ").Append(GC.GetTotalMemory(false) / (1024 * 1024)).Append(" MB managed now\n");
            foreach (LetteraNativeFeature feature in Enum.GetValues(typeof(LetteraNativeFeature)))
                text.Append(feature).Append(": ").Append(LetteraPlatform.IsActive(feature) ? "on" : "off").Append(" - ").Append(LetteraPlatform.Why(feature)).Append('\n');
            foreach (string report in LetteraPlatform.Reports) text.Append("Report: ").Append(report).Append('\n');
            foreach (string result in m_Results) text.Append(result).Append('\n');
            return text.ToString();
        }

        // With no camera and no second device: the phone's own way of sending text, or a mail with it in the body.
        static void Share(string result)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var intentClass = new AndroidJavaClass("android.content.Intent"))
            using (var intent = new AndroidJavaObject("android.content.Intent"))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND")).Dispose();
                intent.Call<AndroidJavaObject>("setType", "text/plain").Dispose();
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), result).Dispose();
                using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, T("Send the result", "Отправить результат")))
                    activity.Call("startActivity", chooser);
            }
#else
            Application.OpenURL(MailAddress(result));
#endif
        }

        /// <summary>A mail with the result in it. Mail programs cut long addresses, so a long result says to use Copy.</summary>
        public static string MailAddress(string result)
        {
            const int Longest = 1800;
            string body = result.Length > Longest ? result.Substring(0, Longest) + "\n[Cut here. Copy has all of it.]" : result;
            return "mailto:?subject=" + Uri.EscapeDataString("Lettera device check") + "&body=" + Uri.EscapeDataString(body);
        }

        void ShowResult()
        {
            string result = Result();
            Label(T("Done. Please send this back.", "Готово. Пожалуйста, отправьте это обратно."), 36, new Vector2(0, 0.9f), new Vector2(1, 1));
            GameObject block = LetteraFieldFactory.CreateTextArea(m_Root);
            var rect = (RectTransform)block.transform;
            rect.anchorMin = new Vector2(0, 0.2f);
            rect.anchorMax = new Vector2(1, 0.9f);
            rect.offsetMin = new Vector2(24, 0);
            rect.offsetMax = new Vector2(-24, 0);
            LetteraEditable shown = block.GetComponentInChildren<LetteraEditable>();
            shown.TypedMarkup = LetteraTypedMarkup.AsText;
            shown.InsertText(result, "code.set");
            shown.GetComponent<LetteraText>().FontSize = 16;
            Button(T("Copy", "Копировать"), new Vector2(0, 0), new Vector2(0.5f, 0.18f), new Color(0.2f, 0.4f, 0.8f), () =>
            {
                shown.Selectable.Activate();
                shown.Selectable.SelectAll();
                shown.Selectable.Copy();
            });
            Button(T("Share", "Поделиться"), new Vector2(0.5f, 0), new Vector2(1, 0.18f), new Color(0.15f, 0.55f, 0.3f), () => Share(result));
#if UNITY_ANDROID || UNITY_IOS
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "lettera_device_check.txt"), result);
            }
            catch (IOException e)
            {
                Debug.LogWarning("The result could not be saved: " + e.Message);
            }
#endif
            Debug.Log("LETTERA_KIT_RESULT " + result.Replace("\n", "|"));
        }
    }
}
