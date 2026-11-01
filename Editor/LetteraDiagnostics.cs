using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What every diagnostics window shares: a header, a pin, and following the selection until pinned.</summary>
    internal abstract class LetteraToolWindow : EditorWindow
    {
        protected abstract string Title { get; }
        protected abstract string Subtitle { get; }
        protected VisualElement Page { get; private set; }
        protected bool Pinned { get; private set; }

        protected virtual void CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.style.flexGrow = 1;
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            rootVisualElement.Add(root);
            VisualElement header = LtFrame.Header(scroll.contentContainer, Title, Subtitle);
            Page = header;
            Build();
        }

        protected abstract void Build();

        protected VisualElement PinToggle()
        {
            Label pin = null;
            pin = LtUI.Clickable("Follows the selection", "lt-chip", "Pin this window to what it shows now.", () =>
            {
                Pinned = !Pinned;
                pin.text = Pinned ? "Pinned" : "Follows the selection";
                pin.EnableInClassList("lt-chip--accent", Pinned);
            });
            return pin;
        }

        void OnSelectionChange()
        {
            if (!Pinned) SelectionChanged();
        }

        protected virtual void SelectionChanged() { }

        internal static TextEngine EngineOf(GameObject go, out Component component)
        {
            component = null;
            if (go == null) return null;
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c is ITextComponent text)
                {
                    component = c;
                    return text.SharedEngine;
                }
            }
            return null;
        }
    }

    /// <summary>What shaping made of a text: its glyphs, their fonts and their tiles, and one glyph's outline.</summary>
    internal sealed class LetteraGlyphsWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Glyphs", false, 20)]
        static void Open() => GetWindow<LetteraGlyphsWindow>("Lettera Glyphs").minSize = new Vector2(320, 300);

        protected override string Title => "Glyphs";
        protected override string Subtitle => "What shaping made of a text";

        VisualElement m_Table, m_Outline;
        Label m_Target;
        ObjectField m_Font;
        TextField m_Text;
        GameObject m_Free;

        protected override void Build()
        {
            m_Target = LtUI.Text("", "lt-hint");
            m_Font = new ObjectField { objectType = typeof(LetteraFontAsset), allowSceneObjects = false };
            m_Text = new TextField { value = "Hello مرحبا" };
            m_Text.AddToClassList("lt-source");
            m_Text.RegisterValueChangedCallback(_ => ShowFree());
            m_Font.RegisterValueChangedCallback(_ => ShowFree());
            Page.Add(LtUI.Card("From A Text", PinToggle(), m_Target, LtUI.Clickable("Use Selected Text", "lt-add lt-add--small", "Shows what the selected text is drawing.", ShowSelected)));
            Page.Add(LtUI.Card("Free", LtUI.Row("Font", m_Font, "Empty uses the system's."), LtUI.Row("Text", m_Text)));
            m_Outline = LtUI.El("lt-glyph");
            m_Outline.style.height = 200;
            Page.Add(LtUI.Card("One Glyph", m_Outline));
            m_Table = LtUI.El("lt-glyph-table");
            Page.Add(LtUI.Card("Glyphs", m_Table));
            ShowSelected();
        }

        protected override void SelectionChanged() => ShowSelected();

        void ShowSelected()
        {
            if (m_Table == null) return;
            TextEngine engine = EngineOf(Selection.activeGameObject, out Component component);
            if (engine == null)
            {
                m_Target.text = "Select a text, or type one in the Free card.";
                ShowFree();
                return;
            }
            m_Target.text = component.name;
            engine.Rebuild();
            Show(engine);
        }

        void ShowFree()
        {
            if (m_Free == null)
            {
                m_Free = new GameObject("Lettera Glyphs Bench", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                ((RectTransform)m_Free.transform).sizeDelta = new Vector2(100000, 1000);
                LetteraWorldText text = m_Free.AddComponent<LetteraWorldText>();
                text.WordWrap = false;
                text.Hide();
            }
            LetteraWorldText bench = m_Free.GetComponent<LetteraWorldText>();
            bench.Font = m_Font.value as LetteraFontAsset;
            bench.Text = m_Text.value ?? "";
            bench.ForceRebuild();
            Show(bench.Engine);
        }

        void OnDisable()
        {
            if (m_Free != null) DestroyImmediate(m_Free);
        }

        void Show(TextEngine engine)
        {
            m_Table.Clear();
            ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
            ReadOnlySpan<int> shown = engine.ShownCodepoints;
            m_Table.Add(Line("Glyph", "Char", "Advance", "Font", true));
            int count = Math.Min(glyphs.Length, 300);
            for (int i = 0; i < count; i++)
            {
                TextEngine.PlacedGlyph g = glyphs[i];
                string character = g.Cluster < shown.Length ? "U+" + shown[g.Cluster].ToString("X4") : "";
                VisualElement line = Line(g.Glyph.ToString(), character, g.Advance.ToString("0.##"), g.Instance != null ? FaceName(g.Instance.Face) : "", false);
                FontInstance instance = g.Instance;
                uint glyph = g.Glyph;
                line.RegisterCallback<ClickEvent>(_ => ShowOutline(instance, glyph));
                m_Table.Add(line);
            }
            if (glyphs.Length > count) m_Table.Add(LtUI.Text($"And {glyphs.Length - count} more.", "lt-hint"));
            if (glyphs.Length == 0) m_Table.Add(LtUI.Text("No glyphs.", "lt-hint"));
        }

        static string FaceName(FontFace face) => face.IsVirtual ? "browser" : face.Family;

        static VisualElement Line(string a, string b, string c, string d, bool head)
        {
            VisualElement row = LtUI.El("lt-glyph-row");
            row.style.flexDirection = FlexDirection.Row;
            foreach (string cell in new[] { a, b, c, d })
            {
                Label label = LtUI.Text(cell, head ? "lt-hint" : "lt-row__value");
                label.style.width = new Length(25, LengthUnit.Percent);
                label.style.overflow = Overflow.Hidden;
                row.Add(label);
            }
            return row;
        }

        // Points on and off the curve, drawn from the font's own outline.
        unsafe void ShowOutline(FontInstance instance, uint glyph)
        {
            if (instance == null) return;
            var commands = new float[8192];
            int count;
            fixed (float* p = commands) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, commands.Length);
            count = Math.Min(count, commands.Length);
            m_Outline.Clear();
            var view = new VisualElement();
            view.style.flexGrow = 1;
            float upem = Mathf.Max(instance.Face.Metrics.Upem, 1);
            view.generateVisualContent += context =>
            {
                Painter2D painter = context.painter2D;
                Rect box = view.contentRect;
                float scale = Mathf.Min(box.width, box.height) / upem * 0.7f;
                Vector2 origin = new Vector2(box.width * 0.2f, box.height * 0.8f);
                Vector2 P(float x, float y) => origin + new Vector2(x * scale, -y * scale);
                painter.strokeColor = new Color(1, 1, 1, 0.15f);
                painter.lineWidth = 1;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0, origin.y));
                painter.LineTo(new Vector2(box.width, origin.y));
                painter.Stroke();
                painter.strokeColor = new Color(0.6f, 0.45f, 1);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                int i = 0;
                while (i < count)
                {
                    int command = (int)commands[i];
                    if (command == 0 && i + 2 < count)
                    {
                        painter.MoveTo(P(commands[i + 1], commands[i + 2]));
                        i += 3;
                    }
                    else if (command == 1 && i + 2 < count)
                    {
                        painter.LineTo(P(commands[i + 1], commands[i + 2]));
                        i += 3;
                    }
                    else if (command == 2 && i + 4 < count)
                    {
                        painter.QuadraticCurveTo(P(commands[i + 1], commands[i + 2]), P(commands[i + 3], commands[i + 4]));
                        i += 5;
                    }
                    else if (command == 3 && i + 6 < count)
                    {
                        painter.BezierCurveTo(P(commands[i + 1], commands[i + 2]), P(commands[i + 3], commands[i + 4]), P(commands[i + 5], commands[i + 6]));
                        i += 7;
                    }
                    else if (command == 4)
                    {
                        painter.ClosePath();
                        i += 1;
                    }
                    else
                    {
                        break;
                    }
                }
                painter.Stroke();
            };
            m_Outline.Add(view);
        }
    }

    /// <summary>The ranges of a text, what each does, and what is wrong with them.</summary>
    internal sealed class LetteraRangesWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Ranges", false, 21)]
        static void Open() => GetWindow<LetteraRangesWindow>("Lettera Ranges").minSize = new Vector2(320, 300);

        protected override string Title => "Ranges";
        protected override string Subtitle => "What the markup became";

        VisualElement m_List, m_Problems;
        Label m_Target;

        protected override void Build()
        {
            m_Target = LtUI.Text("", "lt-hint");
            Page.Add(LtUI.Card("Target", PinToggle(), m_Target, LtUI.Clickable("Refresh", "lt-add lt-add--small", "Reads the selected text again.", Refresh)));
            m_Problems = LtUI.El("lt-problems");
            Page.Add(LtUI.Card("Problems", m_Problems));
            m_List = LtUI.El("lt-ranges");
            Page.Add(LtUI.Card("Ranges", m_List));
            Refresh();
        }

        protected override void SelectionChanged() => Refresh();

        void Refresh()
        {
            if (m_List == null) return;
            m_List.Clear();
            m_Problems.Clear();
            TextEngine engine = EngineOf(Selection.activeGameObject, out Component component);
            if (engine == null)
            {
                m_Target.text = "Select a text to see its ranges.";
                return;
            }
            m_Target.text = component.name;
            engine.EnsureParsed();
            foreach (string problem in LetteraRangeProblems.Of(engine)) m_Problems.Add(new LtFix(problem, null));
            if (m_Problems.childCount == 0) m_Problems.Add(LtUI.Text("None found.", "lt-hint"));
            IReadOnlyList<LetteraMatchContext.RangeRecord> ranges = engine.ParsedRanges;
            for (int i = 0; i < ranges.Count; i++)
            {
                LetteraMatchContext.RangeRecord range = ranges[i];
                LetteraStyle style = engine.EffectiveStyles[range.StyleIndex];
                string effect = style?.Effect != null ? style.Effect.GetType().Name.Replace("Effect", "") : "no effect";
                string tag = style?.Matcher is TagMatcher t ? "<" + t.Name + ">" : style?.Matcher?.GetType().Name ?? "";
                var row = LtUI.El("lt-range");
                row.Add(LtUI.Text($"{tag}  {effect}", "lt-row__value"));
                string slots = range.Slots != null && range.Slots.Length > 0 ? string.Join(", ", range.Slots.Where(x => x != null)) : "";
                row.Add(LtUI.Text($"{range.RenderedStart} to {range.RenderedEnd}{(range.WholeText ? ", whole text" : "")}{(slots.Length > 0 ? ", " + slots : "")}", "lt-hint"));
                m_List.Add(row);
            }
            if (ranges.Count == 0) m_List.Add(LtUI.Text("This text has no ranges.", "lt-hint"));
        }
    }

    /// <summary>Things a text's markup does that are probably not meant.</summary>
    internal static class LetteraRangeProblems
    {
        internal static IEnumerable<string> Of(TextEngine engine)
        {
            var tags = new HashSet<string>();
            foreach (LetteraStyle style in engine.HostForTools.Data.Styles)
            {
                if (style?.Matcher is TagMatcher tag && !string.IsNullOrEmpty(tag.Name) && !tags.Add(tag.Name))
                    yield return $"<{tag.Name}> is bound twice in this text's own styles: the later one wins.";
                if (style != null && style.Matcher != null && style.Effect == null && !(style.Matcher is EscapeMatcher) && !(style.Matcher is NoParseMatcher) && !(style.Matcher is LineBreakMatcher))
                    yield return $"A style matches {style.Matcher.GetType().Name} and does nothing.";
            }
            foreach (LetteraFormulaInfo formula in Formulas(engine))
                if (formula.Failed)
                    yield return $"A formula cannot be read at {formula.ErrorPosition}: {formula.Message}";
        }

        static List<LetteraFormulaInfo> Formulas(TextEngine engine)
        {
            var list = new List<LetteraFormulaInfo>();
            engine.CollectFormulas(list);
            return list;
        }
    }

    /// <summary>The shared atlases: their pages, what they hold, and clearing or warming them.</summary>
    internal sealed class LetteraAtlasesWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Atlases", false, 22)]
        static void Open() => GetWindow<LetteraAtlasesWindow>("Lettera Atlases").minSize = new Vector2(320, 300);

        protected override string Title => "Atlases";
        protected override string Subtitle => "Every text's glyph pages";

        LtStatus m_Status;
        Image m_Image;
        Label m_Info;
        GlyphAtlasView.Set m_Set = GlyphAtlasView.Set.Sdf;
        int m_Page;
        TextField m_Warm;

        protected override void Build()
        {
            m_Status = new LtStatus()
                .Line("Glyphs", () => GlyphAtlas.GlyphCount.ToString())
                .Line("SDF pages", () => GlyphAtlas.SdfPageCount.ToString())
                .Line("MSDF pages", () => GlyphAtlas.MsdfPageCount.ToString())
                .Line("Color pages", () => GlyphAtlas.ColorPageCount.ToString())
                .Line("Limits", () => $"{GlyphAtlas.PageLimits.sdf} SDF, {GlyphAtlas.PageLimits.color} color")
                .Line("Bytes", () => $"{Bytes() / (1024f * 1024f):0.0} MB, on pages");
            Page.Add(LtUI.Card("In Use", m_Status));
            VisualElement tabs = LtUI.El("lt-tabs");
            foreach ((string name, GlyphAtlasView.Set set) in new[] { ("SDF", GlyphAtlasView.Set.Sdf), ("MSDF", GlyphAtlasView.Set.Msdf), ("Color", GlyphAtlasView.Set.Color) })
            {
                GlyphAtlasView.Set chosen = set;
                tabs.Add(LtUI.Clickable(name, "lt-tab lt-tab__label", "Shows this atlas.", () =>
                {
                    m_Set = chosen;
                    m_Page = 0;
                    ShowPage();
                }));
            }
            m_Image = new Image { scaleMode = ScaleMode.ScaleToFit };
            m_Image.style.height = 320;
            m_Image.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            m_Info = LtUI.Text("", "lt-hint");
            VisualElement steps = LtUI.El("lt-chips",
                LtUI.Clickable("Previous", "lt-chip", "The page before this.", () => { m_Page = Math.Max(0, m_Page - 1); ShowPage(); }),
                LtUI.Clickable("Next", "lt-chip", "The page after this.", () => { m_Page++; ShowPage(); }),
                LtUI.Clickable("Export PNG", "lt-chip", "Saves this page where you choose.", Export));
            Page.Add(LtUI.Card("Pages", tabs, m_Image, steps, m_Info));
            m_Warm = new TextField { value = "" };
            m_Warm.AddToClassList("lt-source");
            Page.Add(LtUI.Card("Actions",
                LtUI.Clickable("Clear And Rebuild", "lt-add lt-add--small", "Empties every atlas. Each text makes what it needs again.", () =>
                {
                    GlyphAtlas.Clear();
                    Refresh();
                }),
                LtUI.Row("Warm", m_Warm, "Characters to render ahead with the selected text's font, on Enter.")));
            m_Warm.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return) return;
                EngineOf(Selection.activeGameObject, out Component component);
                if (component != null) Warm(component, m_Warm.value);
            }, TrickleDown.TrickleDown);
            Refresh();
            rootVisualElement.schedule.Execute(() => m_Status.Refresh()).Every(1000);
        }

        static void Warm(Component component, string characters)
        {
            component.GetType().GetMethod("Warm", new[] { typeof(string) })?.Invoke(component, new object[] { characters });
        }

        static long Bytes()
        {
            const long Page = 2048L * 2048;
            return GlyphAtlas.SdfPageCount * Page + GlyphAtlas.MsdfPageCount * Page * 4 + GlyphAtlas.ColorPageCount * Page * 4;
        }

        void Refresh()
        {
            m_Status.Refresh();
            ShowPage();
        }

        void ShowPage()
        {
            if (m_Image == null) return;
            if (m_Image.image != null) DestroyImmediate(m_Image.image);
            Texture2DArray array = GlyphAtlas.TextureOf((int)m_Set);
            if (array != null) m_Page = Mathf.Clamp(m_Page, 0, array.depth - 1);
            m_Image.image = GlyphAtlasView.CopyPage(m_Set, m_Page, out string info);
            m_Info.text = array != null ? $"Page {m_Page + 1}. {info}" : info + ". A text that draws fills it.";
        }

        void Export()
        {
            if (!(m_Image.image is Texture2D page)) return;
            string path = EditorUtility.SaveFilePanel("Export Page", "", $"lettera_{m_Set}_{m_Page}", "png");
            if (string.IsNullOrEmpty(path)) return;
            var target = RenderTexture.GetTemporary(page.width, page.height, 0);
            Graphics.Blit(page, target);
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = target;
            var readable = new Texture2D(page.width, page.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, page.width, page.height), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, readable.EncodeToPNG());
            DestroyImmediate(readable);
        }

        void OnDisable()
        {
            if (m_Image != null && m_Image.image != null) DestroyImmediate(m_Image.image);
        }
    }

    /// <summary>What is on the clipboard, in each format. It reads only when asked.</summary>
    internal sealed class LetteraClipboardWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Clipboard", false, 23)]
        static void Open() => GetWindow<LetteraClipboardWindow>("Lettera Clipboard").minSize = new Vector2(320, 300);

        protected override string Title => "Clipboard";
        protected override string Subtitle => "Every format a copy wrote";

        VisualElement m_Items;
        Label m_Provider;

        protected override void Build()
        {
            m_Provider = LtUI.Text("", "lt-hint");
            Page.Add(LtUI.Card("Reading", m_Provider, LtUI.Clickable("Read The Clipboard", "lt-add lt-add--small", "Reads what is there now, and only now.", Read)));
            m_Items = LtUI.El("lt-clip");
            Page.Add(LtUI.Card("Formats", m_Items));
            m_Items.Add(LtUI.Text("Press Read The Clipboard.", "lt-hint"));
            m_Provider.text = "Through " + LetteraClipboard.Backend.GetType().Name;
        }

        void Read()
        {
            m_Items.Clear();
            var items = new List<LetteraClipItem>();
            LetteraClipboard.Backend.Read(items);
            foreach (LetteraClipItem item in items)
            {
                var card = LtUI.El("lt-range");
                card.Add(LtUI.Text($"{item.Format}, {item.Bytes?.Length ?? 0} bytes", "lt-row__value"));
                string text = item.Text ?? "";
                Label body = LtUI.Text(text.Length > 400 ? text.Substring(0, 400) + "…" : text, "lt-hint");
                body.style.whiteSpace = WhiteSpace.Normal;
                card.Add(body);
                m_Items.Add(card);
            }
            if (items.Count == 0) m_Items.Add(LtUI.Text("Nothing a text could read.", "lt-hint"));
        }
    }

    /// <summary>Making font assets in a batch, and the way to the tools that make a font smaller or teach it words.</summary>
    internal sealed class LetteraFontToolsWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Font Tools", false, 24)]
        static void Open() => GetWindow<LetteraFontToolsWindow>("Lettera Font Tools").minSize = new Vector2(320, 300);

        protected override string Title => "Font Tools";
        protected override string Subtitle => "Fonts into assets";

        readonly List<string> m_Paths = new List<string>();
        VisualElement m_List;
        Label m_Done;

        protected override void Build()
        {
            VisualElement drop = LtUI.El("lt-drop");
            drop.style.height = 70;
            drop.style.alignItems = Align.Center;
            drop.style.justifyContent = Justify.Center;
            drop.style.backgroundColor = new Color(0, 0, 0, 0.18f);
            drop.Add(LtUI.Text("Drop font files here", "lt-hint"));
            drop.RegisterCallback<DragUpdatedEvent>(_ => DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            drop.RegisterCallback<DragPerformEvent>(_ =>
            {
                DragAndDrop.AcceptDrag();
                foreach (string path in DragAndDrop.paths) AddPath(path);
            });
            m_List = LtUI.El("lt-font-list");
            m_Done = LtUI.Text("", "lt-hint");
            Page.Add(LtUI.Card("Create", drop,
                LtUI.Clickable("Browse...", "lt-add lt-add--small", "Picks a font file.", () =>
                {
                    string path = EditorUtility.OpenFilePanelWithFilters("Font", "", new[] { "Fonts", "ttf,otf,ttc,otc" });
                    if (!string.IsNullOrEmpty(path)) AddPath(path);
                }),
                LtUI.Clickable("Use Selected", "lt-add lt-add--small", "Takes the font files selected in the Project window.", () =>
                {
                    foreach (string guid in Selection.assetGUIDs) AddPath(AssetDatabase.GUIDToAssetPath(guid));
                }),
                m_List,
                LtUI.Clickable("Create Assets", "lt-add", "Makes each font's asset next to it.", Create),
                m_Done));
            Page.Add(LtUI.Card("Other Tools",
                LtUI.Text("Subset: on a font's own inspector, in its Subset card.", "lt-hint"),
                LtUI.Text("Word dictionaries: Assets > Create > Lettera.", "lt-hint")));
        }

        void AddPath(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".ttf" && extension != ".otf" && extension != ".ttc" && extension != ".otc") return;
            if (m_Paths.Contains(path)) return;
            m_Paths.Add(path);
            m_List.Add(LtUI.Text(Path.GetFileName(path), "lt-row__value"));
        }

        void Create()
        {
            int made = 0;
            string lastFolder = null;
            foreach (string path in m_Paths)
            {
                string local = path;
                // A font outside the project needs a place to go.
                if (!local.StartsWith("Assets/") && !local.StartsWith("Packages/"))
                {
                    lastFolder = lastFolder ?? EditorUtility.SaveFolderPanel("Where should the asset go?", "Assets", "");
                    if (string.IsNullOrEmpty(lastFolder)) continue;
                    string relative = FileUtil.GetProjectRelativePath(lastFolder);
                    if (string.IsNullOrEmpty(relative)) continue;
                    var asset = CreateInstance<LetteraFont>();
                    asset.SetFontData(File.ReadAllBytes(path));
                    AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(relative + "/" + Path.GetFileNameWithoutExtension(path) + " Lettera.asset"));
                    made++;
                    continue;
                }
                LetteraMenus.CreateFontAsset<LetteraFont>(local);
                made++;
            }
            AssetDatabase.SaveAssets();
            m_Paths.Clear();
            m_List.Clear();
            m_Done.text = made + (made == 1 ? " asset made." : " assets made.");
        }
    }

    /// <summary>Draws what a selected text is made of in the Scene view: glyph boxes, baselines and line bounds. Ctrl+Shift+I.</summary>
    internal static class LetteraInspectOverlay
    {
        const string Key = "Lettera.Inspect.Enabled";

        internal static bool Enabled
        {
            get => EditorPrefs.GetBool(Key, false);
            set
            {
                EditorPrefs.SetBool(Key, value);
                SceneView.RepaintAll();
            }
        }

        [InitializeOnLoadMethod]
        static void Install() => SceneView.duringSceneGui += Draw;

        [Shortcut("Lettera/Inspect Text In Scene", typeof(SceneView), KeyCode.I, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        [MenuItem("Tools/Lettera/Inspect Text In Scene", false, 25)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem("Tools/Lettera/Inspect Text In Scene", true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked("Tools/Lettera/Inspect Text In Scene", Enabled);
            return true;
        }

        static void Draw(SceneView view)
        {
            if (!Enabled) return;
            foreach (GameObject go in Selection.gameObjects)
            {
                TextEngine engine = LetteraToolWindow.EngineOf(go, out Component component);
                if (engine == null) continue;
                DrawText(engine, component.transform);
            }
        }

        static void DrawText(TextEngine engine, Transform transform)
        {
            Matrix4x4 before = Handles.matrix;
            Handles.matrix = transform.localToWorldMatrix;
            ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
            Color box = new Color(0.6f, 0.45f, 1, 0.7f), baseline = new Color(0.4f, 0.9f, 0.6f, 0.9f);
            int limit = Math.Min(glyphs.Length, 2000);
            for (int i = 0; i < limit; i++)
            {
                TextEngine.PlacedGlyph g = glyphs[i];
                Handles.color = box;
                float top = g.Size * 0.8f, bottom = -g.Size * 0.2f;
                Handles.DrawLine(new Vector3(g.X, g.Y + bottom), new Vector3(g.X, g.Y + top));
                Handles.DrawLine(new Vector3(g.X + g.Advance, g.Y + bottom), new Vector3(g.X + g.Advance, g.Y + top));
            }
            Handles.color = baseline;
            foreach (LetteraLine line in engine.Lines)
                Handles.DrawLine(new Vector3(line.X, line.Baseline), new Vector3(line.X + line.Width, line.Baseline));
            Handles.matrix = before;
        }
    }
}
