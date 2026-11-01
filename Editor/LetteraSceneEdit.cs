using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Why a session ended.</summary>
    internal enum LetteraSceneEditEnd { Done, Escape, ClickOutside, SelectionChanged, PlayMode, DomainReload, TextGone, ChangedElsewhere }

    /// <summary>
    /// One text being typed into where it stands. The runtime's field does the work from an object of its own, bound to the text, so nothing is
    /// added to what is edited: a duplicate has no helper, a prefab sees no added component, and a scene file cannot hold one.
    /// While it runs the value lives in the text's field, and only at the end does it go back through Undo, as one step.
    /// </summary>
    internal sealed class LetteraSceneEdit
    {
        internal const string HelperName = "Lettera Scene Edit";
        internal const string UndoName = "Edit Text In Scene";
        const int SourcePriority = int.MaxValue;

        internal static LetteraSceneEdit Current { get; private set; }

        /// <summary>A session started or ended, or a try at one was refused.</summary>
        internal static event Action StateChanged;
        internal static void RaiseStateChanged() => StateChanged?.Invoke();

        /// <summary>Replaces how the right click's menu shows. Tests use this.</summary>
        internal static Action<IReadOnlyList<MenuLine>> MenuOverride;

        /// <summary>One line of the right click's menu.</summary>
        internal struct MenuLine
        {
            public string Title;
            public bool Enabled, On, Separator;
            public Action Run;
        }

        readonly MonoBehaviour m_Text;
        readonly ITextComponent m_Host;
        readonly string m_Original;
        readonly bool m_Literal;
        readonly int m_Mask;
        readonly bool m_ToolsHidden;
        readonly List<LetteraSelectable> m_Silenced = new List<LetteraSelectable>();
        readonly Func<ILetteraInputSource> m_SourceFactory;
        GameObject m_Helper;
        string m_Expected;
        bool m_Dirtied, m_EndWanted, m_Ended;

        internal MonoBehaviour Text => m_Text;
        internal LetteraSelectable Selectable { get; private set; }
        internal LetteraEditable Field { get; private set; }
        internal LetteraSceneEditInput Input { get; } = new LetteraSceneEditInput();
        internal LetteraSceneEditVisual Visual { get; } = new LetteraSceneEditVisual();
        internal GameObject Helper => m_Helper;
        internal LetteraSceneEditEnd? EndedBy { get; private set; }
        TextEngine Engine => m_Host.SharedEngine;

        // ------------------------------------------------------------- whether it can start

        /// <summary>The text of an object, or null.</summary>
        internal static MonoBehaviour TextOf(GameObject go)
        {
            if (go == null) return null;
            foreach (MonoBehaviour component in go.GetComponents<MonoBehaviour>())
                if (component is ITextComponent)
                    return component;
            return null;
        }

        /// <summary>Why what is picked cannot be typed into, in words for a chip. Null when it can.</summary>
        internal static string WhyNot(IReadOnlyList<UnityEngine.Object> targets)
        {
            if (targets == null || targets.Count == 0) return "Select a text to edit it here.";
            if (targets.Count > 1) return "Several objects are selected. Editing in the scene takes one text.";
            MonoBehaviour text = targets[0] as MonoBehaviour;
            if (text == null) text = TextOf(targets[0] as GameObject);
            return text == null ? "This object has no Lettera text." : WhyNot(text);
        }

        internal static string WhyNot(MonoBehaviour text)
        {
            if (text == null || !(text is ITextComponent host)) return "This object has no Lettera text.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "In Play Mode a text runs as in a player. Leave Play Mode to edit it here.";
            if (EditorUtility.IsPersistent(text)) return "This is a prefab on disk. Open it in Prefab Mode to edit its text here.";
            if (!text.isActiveAndEnabled) return "This text is inactive, so it shows nothing to type into.";
            if (host.TextRect == null) return "This text has no rect to type into.";
            TextEngine engine = host.SharedEngine;
            if (engine.IsWindowed) return "This text shows a document, and what it shows is not what it stores.";
            switch (engine.TextOverride)
            {
                case LetteraTextOverride.RuntimeText: return "A script set what this text shows with SetText, so its written value is not what shows.";
                case LetteraTextOverride.Resolver: return "A resolver makes what this text shows, so its written value is not what shows.";
                case LetteraTextOverride.Both: return "A script and a resolver make what this text shows, so its written value is not what shows.";
            }
            return null;
        }

        // ------------------------------------------------------------- start

        /// <summary>Starts on a text, ending any session before it. Null when it cannot, and why says the reason.</summary>
        internal static LetteraSceneEdit Start(MonoBehaviour text, out string why)
        {
            if (Current != null && Current.m_Text == text)
            {
                why = null;
                return Current;
            }
            why = WhyNot(text);
            if (why != null) return null;
            Current?.End(LetteraSceneEditEnd.Done);
            Current = new LetteraSceneEdit(text);
            RaiseStateChanged();
            return Current;
        }

        LetteraSceneEdit(MonoBehaviour text)
        {
            m_Text = text;
            m_Host = (ITextComponent)text;
            Selection.objects = new UnityEngine.Object[] { text.gameObject };
            m_Original = m_Expected = m_Host.SourceText ?? "";
            m_ToolsHidden = Tools.hidden;
            Tools.hidden = true;

            // A password field's rules are its own business: here the written value shows.
            m_Literal = Engine.Literal;
            m_Mask = Engine.MaskGlyph;
            Engine.Literal = false;
            Engine.MaskGlyph = 0;
            m_Host.Dirty(DirtyLevel.Parse);

            LetteraSelectable existing = text.GetComponent<LetteraSelectable>();
            if (existing != null)
            {
                existing.SourceWritesIgnored = true;
                m_Silenced.Add(existing);
            }

            m_Helper = EditorUtility.CreateGameObjectWithHideFlags(HelperName, HideFlags.HideAndDontSave);
            m_Helper.SetActive(false);
            Selectable = m_Helper.AddComponent<LetteraSelectable>();
            Selectable.hideFlags = HideFlags.HideAndDontSave;
            Selectable.BindHost(m_Host);
            Selectable.Authoring = true;
            Selectable.VisualSource = () => Visual;
            if (existing != null && existing.Commands.Count > 0)
            {
                Selectable.Commands.Clear();
                Selectable.Commands.AddRange(existing.Commands);
            }
            Field = m_Helper.AddComponent<LetteraEditable>();
            Field.hideFlags = HideFlags.HideAndDontSave;
            m_Helper.SetActive(true);

            Field.ValueChanged += OnValueChanged;
            Field.Cancelled += OnCancelled;
            Selectable.MenuRequested += OnMenuRequested;

            m_SourceFactory = () => Input;
            LetteraInput.Register(m_SourceFactory, SourcePriority);
            Selectable.Activate();
            LetteraInput.Unregister(m_SourceFactory);
            Selectable.PlaceCaret(Field.Length);
            Selectable.Tick();
            Repaint();
        }

        // ------------------------------------------------------------- running

        void OnValueChanged(string source)
        {
            m_Expected = source;
            if (m_Dirtied || source == m_Original) return;
            // From the first change on the scene has something to save, even if the session never ends before a quit or a save.
            m_Dirtied = true;
            MarkSceneDirty();
        }

        void OnCancelled() => m_EndWanted = true;

        void MarkSceneDirty()
        {
            Scene scene = m_Text.gameObject.scene;
            if (scene.IsValid() && !Application.isPlaying) EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>A frame of the session: what ends it from outside, the composition, and the caret's blink.</summary>
        internal void Tick()
        {
            if (m_Ended) return;
            if (m_Text == null || !m_Text.isActiveAndEnabled)
            {
                End(LetteraSceneEditEnd.TextGone);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                End(LetteraSceneEditEnd.PlayMode);
                return;
            }
            GameObject[] selected = Selection.gameObjects;
            if (selected.Length != 1 || selected[0] != m_Text.gameObject)
            {
                End(LetteraSceneEditEnd.SelectionChanged);
                return;
            }
            if (!Field.IsComposing && (m_Host.SourceText ?? "") != m_Expected)
            {
                End(LetteraSceneEditEnd.ChangedElsewhere);
                return;
            }
            Input.Pump();
            if (m_EndWanted)
            {
                End(LetteraSceneEditEnd.Escape);
                return;
            }
            bool drew = Visual.Version;
            Selectable.Tick();
            if (Visual.Version != drew) Repaint();
        }

        /// <summary>A key from the Scene view. True when the session took it, so that it reaches no tool or shortcut.</summary>
        internal bool Key(Event e)
        {
            if (m_Ended) return false;
            bool taken = Input.Feed(e);
            Changed();
            if (m_EndWanted) End(LetteraSceneEditEnd.Escape);
            return taken;
        }

        /// <summary>A command the editor names, from its menus. True when the session took it.</summary>
        internal bool Command(string name, bool run)
        {
            if (m_Ended) return false;
            switch (name)
            {
                case "Copy": if (run) Selectable.Copy(); break;
                case "Cut": if (run) Field.Cut(); break;
                case "Paste": if (run) Field.RunPaste(); break;
                case "SelectAll": if (run) Selectable.SelectAll(); break;
                case "Delete":
                case "SoftDelete": if (run) Field.DeleteForward(); break;
                // These would act on the object under the typing.
                case "Duplicate":
                case "FrameSelected":
                case "FrameSelectedWithLock":
                    break;
                default:
                    return false;
            }
            if (run) Changed();
            return true;
        }

        PointerEventData Pointer(Vector2 screen, PointerEventData.InputButton button) =>
            new PointerEventData(null) { position = screen, pressPosition = screen, button = button, pointerId = -1 };

        /// <summary>Whether a point in the view's pixels falls on the text: on its rect, or on what overflows it.</summary>
        internal bool Contains(Vector2 screen, Camera camera) => HitsText(m_Host, screen, camera);

        internal static bool HitsText(ITextComponent host, Vector2 screen, Camera camera)
        {
            RectTransform rect = host.TextRect;
            if (rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out Vector2 local)) return false;
            if (rect.rect.Contains(local)) return true;
            host.SharedEngine.EnsurePositioned();
            return host.SharedEngine.LayoutBounds.Contains(local);
        }

        /// <summary>A press of the left button on the text: a caret, a word or a paragraph by how many clicks it is.</summary>
        internal void PointerDown(Vector2 screen, Camera camera)
        {
            if (m_Ended) return;
            Selectable.PointerCamera = camera;
            Selectable.OnPointerDown(Pointer(screen, PointerEventData.InputButton.Left));
            Changed();
        }

        internal void PointerDrag(Vector2 screen)
        {
            if (m_Ended) return;
            Selectable.OnDrag(Pointer(screen, PointerEventData.InputButton.Left));
            Changed();
        }

        internal void PointerUp(Vector2 screen)
        {
            if (m_Ended) return;
            Selectable.OnPointerUp(Pointer(screen, PointerEventData.InputButton.Left));
            Changed();
        }

        /// <summary>A right click on the text: what a player does, then this session's menu.</summary>
        internal void RightClick(Vector2 screen, Camera camera)
        {
            if (m_Ended) return;
            Selectable.PointerCamera = camera;
            Selectable.OnPointerDown(Pointer(screen, PointerEventData.InputButton.Right));
            Changed();
        }

        void Changed()
        {
            if (m_Ended) return;
            Selectable.Tick();
            Repaint();
        }

        void Repaint()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------- the bar and the menu

        internal bool ShowsMarkup
        {
            get => Field.MarkupView == LetteraMarkupView.Source;
            set
            {
                Field.MarkupView = value ? LetteraMarkupView.Source : LetteraMarkupView.Hidden;
                Changed();
            }
        }

        internal bool CanFormat(Type effect) => !m_Ended && Field.CanFormat(effect);
        internal bool IsFormatOn(Type effect) => !m_Ended && Field.CanFormat(effect) && Field.IsEffectActive(effect);

        internal void ToggleFormat(Type effect)
        {
            if (m_Ended) return;
            Field.ToggleEffect(effect, null, "user.format");
            Changed();
        }

        internal void ApplyColor(Color color)
        {
            if (m_Ended) return;
            Field.ApplyEffect(typeof(ColorEffect), "#" + ColorUtility.ToHtmlStringRGB(color), "user.format");
            Changed();
        }

        /// <summary>The tags this text can write that the bar has no button of its own for and that need nothing filled in.</summary>
        internal List<(string tag, Type effect)> CommonTags(int limit = 6)
        {
            var tags = new List<(string, Type)>();
            if (m_Ended) return tags;
            Engine.EnsureParsed();
            var seen = new HashSet<Type> { typeof(BoldEffect), typeof(ItalicEffect), typeof(UnderlineEffect), typeof(StrikethroughEffect), typeof(ColorEffect) };
            IReadOnlyList<LetteraStyle> styles = Engine.EffectiveStyles;
            for (int i = 0; i < styles.Count && tags.Count < limit; i++)
            {
                LetteraStyle style = styles[i];
                if (style == null || !style.Enabled || !(style.Matcher is TagMatcher tag) || !tag.CanWrap || style.Effect == null) continue;
                if (style.Effect is SizeEffect || style.Effect is AlphaEffect || style.Effect is FontEffect || style.Effect is LanguageEffect) continue;
                if (string.IsNullOrEmpty(tag.Name) || !seen.Add(style.Effect.GetType())) continue;
                tags.Add((tag.Name, style.Effect.GetType()));
            }
            return tags;
        }

        /// <summary>The field's commands as a player offers them, then formatting.</summary>
        internal List<MenuLine> MenuLines()
        {
            var lines = new List<MenuLine>();
            Selectable.BuildMenuEntries();
            IReadOnlyList<LetteraMenuEntry> entries = Selectable.MenuEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                lines.Add(new MenuLine { Title = entries[i].Title, Enabled = entries[i].IsEnabled, On = entries[i].IsOn, Run = () => RunEntry(index) });
            }
            if (lines.Count > 0) lines.Add(new MenuLine { Separator = true });
            foreach ((string title, Type effect) in new[] { ("Bold", typeof(BoldEffect)), ("Italic", typeof(ItalicEffect)), ("Underline", typeof(UnderlineEffect)), ("Strikethrough", typeof(StrikethroughEffect)) })
            {
                Type chosen = effect;
                lines.Add(new MenuLine { Title = "Format/" + title, Enabled = CanFormat(chosen), On = IsFormatOn(chosen), Run = () => ToggleFormat(chosen) });
            }
            foreach ((string tag, Type effect) in CommonTags())
            {
                Type chosen = effect;
                lines.Add(new MenuLine { Title = "Format/<" + tag + ">", Enabled = CanFormat(chosen), On = IsFormatOn(chosen), Run = () => ToggleFormat(chosen) });
            }
            lines.Add(new MenuLine { Title = "Show Markup", Enabled = true, On = ShowsMarkup, Run = () => ShowsMarkup = !ShowsMarkup });
            return lines;
        }

        void RunEntry(int index)
        {
            if (m_Ended) return;
            Selectable.RunMenuEntry(index);
            Changed();
        }

        // The runtime's menu is objects on a canvas of the game's: here it is a menu of the editor's.
        void OnMenuRequested(in LetteraMenuPlacement placement, ref bool handled)
        {
            handled = true;
            List<MenuLine> lines = MenuLines();
            if (MenuOverride != null)
            {
                MenuOverride(lines);
                return;
            }
            var menu = new GenericMenu();
            foreach (MenuLine line in lines)
            {
                if (line.Separator) menu.AddSeparator("");
                else if (line.Enabled) menu.AddItem(new GUIContent(line.Title), line.On, () => line.Run());
                else menu.AddDisabledItem(new GUIContent(line.Title), line.On);
            }
            menu.ShowAsContext();
        }

        // ------------------------------------------------------------- end

        /// <summary>Writes a prefab instance's value as an override now, for a save during a session.</summary>
        internal void RecordOverride()
        {
            if (!m_Ended && m_Text != null) PrefabUtility.RecordPrefabInstancePropertyModifications(m_Text);
        }

        /// <summary>Ends the session. What was typed stays, as one undo step. Every helper goes.</summary>
        internal void End(LetteraSceneEditEnd reason)
        {
            if (m_Ended) return;
            m_Ended = true;
            EndedBy = reason;
            if (Current == this) Current = null;
            Tools.hidden = m_ToolsHidden;

            if (m_Text == null)
            {
                // Whoever took the component away took what was typed with it: an undo of that brings both back.
                Selectable.ForgetHost();
                DestroyHelper();
                Repaint();
                RaiseStateChanged();
                return;
            }

            Field.ValueChanged -= OnValueChanged;
            Field.Cancelled -= OnCancelled;
            Selectable.MenuRequested -= OnMenuRequested;
            Selectable.Deactivate();
            string final = m_Host.SourceText ?? "";
            DestroyHelper();

            Engine.Literal = m_Literal;
            Engine.MaskGlyph = m_Mask;
            foreach (LetteraSelectable silenced in m_Silenced)
                if (silenced != null)
                    silenced.SourceWritesIgnored = false;

            // Someone else wrote the value during the session, and their write has its own undo.
            if (reason != LetteraSceneEditEnd.ChangedElsewhere && final != m_Original) Commit(final);
            m_Host.Dirty(DirtyLevel.Parse);
            Repaint();
            RaiseStateChanged();
        }

        void Commit(string final)
        {
            // What the undo system compares against has to be what the text held before all of this.
            ((ITextHost)m_Text).Data.Text = m_Original;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RecordObject(m_Text, UndoName);
            m_Host.SourceText = final;
            Undo.FlushUndoRecordObjects();
            PrefabUtility.RecordPrefabInstancePropertyModifications(m_Text);
            MarkSceneDirty();
            Undo.IncrementCurrentGroup();
        }

        void DestroyHelper()
        {
            if (m_Helper != null) UnityEngine.Object.DestroyImmediate(m_Helper);
            m_Helper = null;
        }

        /// <summary>Takes away what a session left behind, if it could not end: such objects outlive a domain reload.</summary>
        internal static int Sweep()
        {
            int swept = 0;
            foreach (LetteraSelectable selectable in Resources.FindObjectsOfTypeAll<LetteraSelectable>())
            {
                if (selectable == null || selectable.gameObject.name != HelperName || (Current != null && Current.m_Helper == selectable.gameObject)) continue;
                UnityEngine.Object.DestroyImmediate(selectable.gameObject);
                swept++;
            }
            return swept;
        }
    }

    /// <summary>What a session draws, kept as rectangles in the text's own space for the Scene view to draw.</summary>
    internal sealed class LetteraSceneEditVisual : ISelectionVisual
    {
        internal readonly List<Rect> Rects = new List<Rect>();
        internal readonly List<Rect> Underlines = new List<Rect>();
        internal Color SelectionColor, CaretColor, UnderlineColor;
        internal Rect Caret;
        internal bool CaretOn;

        /// <summary>Turns over when what shows differs, which tells the view to paint.</summary>
        internal bool Version { get; private set; }

        public void Show(List<Rect> rects, Color color)
        {
            Rects.Clear();
            Rects.AddRange(rects);
            SelectionColor = color;
            Version = !Version;
        }

        public void ShowCaret(Rect rect, Color color, bool visible)
        {
            Caret = rect;
            CaretColor = color;
            CaretOn = visible;
        }

        public void ShowUnderlines(List<Rect> rects, Color color)
        {
            Underlines.Clear();
            Underlines.AddRange(rects);
            UnderlineColor = color;
        }

        public void Follow() { }
        public void Release() { }
    }

    /// <summary>Keys and composition from the Scene view's events. The session is what feeds it, and a test can feed it the same way.</summary>
    internal sealed class LetteraSceneEditInput : ILetteraInputSource
    {
        /// <summary>Replaces where the running composition is read from. Tests use this.</summary>
        internal static Func<string> CompositionOverride;

        ILetteraInputSink m_Sink;
        string m_Composition = "";
        string m_Held = "";
        char m_High;
        static bool s_LegacyBroken;

        public void Attach(ILetteraInputSink sink, ILetteraInputHost host)
        {
            m_Sink = sink;
            m_Composition = "";
            m_Held = "";
            SetIme(true);
        }

        public void Detach()
        {
            SetIme(false);
            m_Sink = null;
        }

        static void SetIme(bool on)
        {
            if (s_LegacyBroken) return;
            try
            {
                UnityEngine.Input.imeCompositionMode = on ? IMECompositionMode.On : IMECompositionMode.Auto;
            }
            catch (InvalidOperationException)
            {
                s_LegacyBroken = true;
            }
        }

        static string ReadComposition()
        {
            if (CompositionOverride != null) return CompositionOverride() ?? "";
            if (s_LegacyBroken) return "";
            try
            {
                return UnityEngine.Input.compositionString ?? "";
            }
            catch (InvalidOperationException)
            {
                s_LegacyBroken = true;
                return "";
            }
        }

        /// <summary>Asks the system to put its candidates here, in pixels of the window.</summary>
        internal static void PlaceCandidates(Vector2 windowPixels)
        {
            if (s_LegacyBroken) return;
            try
            {
                UnityEngine.Input.compositionCursorPos = windowPixels;
            }
            catch (InvalidOperationException)
            {
                s_LegacyBroken = true;
            }
        }

        internal bool Composing => m_Composition.Length > 0;

        public void Pump()
        {
            if (m_Sink == null) return;
            string composition = ReadComposition();
            if (composition == m_Composition) return;
            bool ended = composition.Length == 0;
            m_Composition = composition;
            if (!ended)
            {
                m_Sink.Composition(composition, composition.Length, null);
                return;
            }
            // What the system commits arrives as ordinary characters, on either side of the composition's end.
            if (m_Held.Length > 0)
            {
                m_Sink.Composition(m_Held, m_Held.Length, null);
                m_Sink.CompositionEnd(true);
                m_Held = "";
            }
            else
            {
                m_Sink.CompositionEnd(false);
            }
        }

        /// <summary>One key event. True when it belongs to the text.</summary>
        internal bool Feed(Event e)
        {
            if (m_Sink == null || e.type != EventType.KeyDown) return false;
            Pump();
            LetteraKeyModifiers modifiers = Modifiers(e);
            bool control = e.control, alt = e.alt, command = e.command;
            LetteraKeyCode key = LetteraKeyTable.FromUnity(e.keyCode);
            char character = e.character;
            bool plain = character == 0 || char.IsControl(character);

            if (key != LetteraKeyCode.None && key != LetteraKeyCode.Space)
            {
                bool chord = control || alt || command;
                if (plain || chord) m_Sink.Key(key, modifiers);
                if (plain) return Belongs(e, key, modifiers);
            }
            if (plain) return Belongs(e, key, modifiers);

            // Control alone never types. Control with Alt is AltGr and does.
            if (command || (control && !alt)) return Belongs(e, key, modifiers);
            if (char.IsHighSurrogate(character))
            {
                m_High = character;
                return true;
            }
            string text = m_High != 0 && char.IsLowSurrogate(character) ? new string(new[] { m_High, character }) : character.ToString();
            m_High = '\0';
            if (m_Composition.Length > 0) m_Held += text;
            else m_Sink.Text(text);
            return true;
        }

        // A plain key is the text's, so that W types and does not take the Move tool. A chord only when it means something to a field, so that Control+S still saves.
        static bool Belongs(Event e, LetteraKeyCode key, LetteraKeyModifiers modifiers)
        {
            if (e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15 && key == LetteraKeyCode.None) return false;
            if (!e.control && !e.alt && !e.command) return e.keyCode != KeyCode.None;
            LetteraEditVerb verb = LetteraKeyTable.Resolve(key, modifiers, LetteraKeyTable.Family);
            return verb != LetteraEditVerb.None && verb != LetteraEditVerb.Pass;
        }

        static LetteraKeyModifiers Modifiers(Event e)
        {
            LetteraKeyModifiers modifiers = LetteraKeyModifiers.None;
            if (e.shift) modifiers |= LetteraKeyModifiers.Shift;
            if (e.control) modifiers |= LetteraKeyModifiers.Control;
            if (e.alt) modifiers |= LetteraKeyModifiers.Alt;
            if (e.command) modifiers |= LetteraKeyModifiers.Command;
            return modifiers;
        }
    }

    /// <summary>The Scene view's side of a session: its ways in, its events, and what ends it from outside.</summary>
    [InitializeOnLoad]
    internal static class LetteraSceneEditView
    {
        const string Menu = "Tools/Lettera/Edit Text In Scene";
        static readonly int s_Control = "LetteraSceneEdit".GetHashCode();
        static bool s_Pressed, s_BarIsOurs;

        /// <summary>What the last try at starting said, for the bar's chip.</summary>
        internal static string LastWhy { get; private set; }

        static LetteraSceneEditView()
        {
            SceneView.beforeSceneGui += Before;
            SceneView.duringSceneGui += During;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorSceneManager.sceneSaving += (_, _) => LetteraSceneEdit.Current?.RecordOverride();
            EditorSceneManager.sceneClosing += SceneClosing;
            PrefabStage.prefabSaving += _ => LetteraSceneEdit.Current?.RecordOverride();
            PrefabStage.prefabStageClosing += _ => LetteraSceneEdit.Current?.End(LetteraSceneEditEnd.Done);
            EditorApplication.delayCall += () => LetteraSceneEdit.Sweep();
            LetteraSceneEdit.StateChanged += PutBarAway;
            Selection.selectionChanged += SelectionChanged;
        }

        internal static void BeforeReload() => LetteraSceneEdit.Current?.End(LetteraSceneEditEnd.DomainReload);

        internal static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) LetteraSceneEdit.Current?.End(LetteraSceneEditEnd.PlayMode);
        }

        static void SceneClosing(Scene scene, bool removing)
        {
            LetteraSceneEdit session = LetteraSceneEdit.Current;
            if (session != null && session.Text != null && session.Text.gameObject.scene == scene) session.End(LetteraSceneEditEnd.Done);
        }

        static void Update()
        {
            LetteraSceneEdit session = LetteraSceneEdit.Current;
            if (session == null) return;
            session.Tick();
        }

        // A refusal's reason stands until the selection it was about is gone.
        static void SelectionChanged()
        {
            if (LetteraSceneEdit.Current != null || LastWhy == null) return;
            LastWhy = null;
            LetteraSceneEdit.RaiseStateChanged();
        }

        // What a session opened goes when it ends. A bar someone opened from the overlay menu stays.
        static void PutBarAway()
        {
            if (!s_BarIsOurs || LetteraSceneEdit.Current != null || LastWhy != null) return;
            s_BarIsOurs = false;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && view.TryGetOverlay(LetteraSceneEditBar.Id, out Overlay bar)) bar.displayed = false;
        }

        // ------------------------------------------------------------- ways in

        [Shortcut("Lettera/Edit Text In Scene", typeof(SceneView), KeyCode.F2)]
        [MenuItem(Menu, false, 26)]
        static void BeginFromSelection() => Begin(Selection.objects);

        /// <summary>From what is selected, or what an inspector shows. Says why when it cannot.</summary>
        internal static bool Begin(IReadOnlyList<UnityEngine.Object> targets)
        {
            string why = LetteraSceneEdit.WhyNot(targets);
            if (why != null)
            {
                Tell(why);
                return false;
            }
            MonoBehaviour text = targets[0] as MonoBehaviour;
            if (text == null) text = LetteraSceneEdit.TextOf(targets[0] as GameObject);
            return Begin(text, out _);
        }

        /// <summary>On one text: it becomes the selection, and the Scene view takes the keyboard.</summary>
        internal static bool Begin(MonoBehaviour text, out string why)
        {
            why = SceneView.lastActiveSceneView == null ? "Open a Scene view: that is where a text is edited." : LetteraSceneEdit.WhyNot(text);
            if (why != null)
            {
                Tell(why);
                return false;
            }
            SceneView view = SceneView.lastActiveSceneView;
            LetteraSceneEdit.Start(text, out why);
            if (why != null)
            {
                Tell(why);
                return false;
            }
            LastWhy = null;
            view.Focus();
            ShowBar(view);
            return true;
        }

        static void Tell(string why)
        {
            LastWhy = why;
            LetteraSceneEdit.RaiseStateChanged();
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            ShowBar(view);
            view.ShowNotification(new GUIContent(why));
        }

        static void ShowBar(SceneView view)
        {
            if (!view.TryGetOverlay(LetteraSceneEditBar.Id, out Overlay bar) || bar.displayed) return;
            bar.displayed = true;
            s_BarIsOurs = true;
        }

        /// <summary>The text under a point in the view's pixels, nearest first. A world text has no renderer of its own for the editor's picking to find.</summary>
        internal static MonoBehaviour TextAt(IEnumerable<MonoBehaviour> candidates, Vector2 screen, Camera camera)
        {
            MonoBehaviour best = null;
            float bestDistance = float.MaxValue;
            Ray ray = camera.ScreenPointToRay(screen);
            foreach (MonoBehaviour candidate in candidates)
            {
                if (!(candidate is ITextComponent host) || !candidate.isActiveAndEnabled) continue;
                RectTransform rect = host.TextRect;
                if (rect == null || !LetteraSceneEdit.HitsText(host, screen, camera)) continue;
                float distance = Vector3.Dot(rect.position - ray.origin, ray.direction);
                if (distance < 0 || distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
            return best;
        }

        static IEnumerable<MonoBehaviour> TextsOnStage()
        {
            foreach (MonoBehaviour behaviour in StageUtility.GetCurrentStageHandle().FindComponentsOfType<MonoBehaviour>())
                if (behaviour is ITextComponent)
                    yield return behaviour;
        }

        // ------------------------------------------------------------- events

        static Vector2 Pixels(Event e) => HandleUtility.GUIPointToScreenPixelCoordinate(e.mousePosition);

        // Before the view's own tools see anything: the press that starts a marquee or a flight must not reach them.
        static void Before(SceneView view)
        {
            Event e = Event.current;
            if (e == null) return;
            LetteraSceneEdit session = LetteraSceneEdit.Current;
            if (session == null)
            {
                // A plain double click stays the editor's.
                if (e.type == EventType.MouseDown && e.button == 0 && e.alt && e.clickCount == 2)
                {
                    MonoBehaviour text = TextAt(TextsOnStage(), Pixels(e), view.camera);
                    if (text != null && Begin(text, out _)) e.Use();
                }
                return;
            }

            int control = GUIUtility.GetControlID(s_Control, FocusType.Passive);
            switch (e.type)
            {
                case EventType.KeyDown:
                    if (session.Key(e)) e.Use();
                    break;
                case EventType.ValidateCommand:
                    if (session.Command(e.commandName, false)) e.Use();
                    break;
                case EventType.ExecuteCommand:
                    if (session.Command(e.commandName, true)) e.Use();
                    break;
                case EventType.MouseDown:
                    if (e.button != 0 || e.alt) break;
                    if (session.Contains(Pixels(e), view.camera))
                    {
                        GUIUtility.hotControl = control;
                        s_Pressed = true;
                        session.PointerDown(Pixels(e), view.camera);
                        e.Use();
                    }
                    else
                    {
                        // The click goes on to whatever is under it.
                        session.End(LetteraSceneEditEnd.ClickOutside);
                    }
                    break;
                case EventType.MouseDrag:
                    if (!s_Pressed || e.button != 0) break;
                    session.PointerDrag(Pixels(e));
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!s_Pressed || e.button != 0) break;
                    s_Pressed = false;
                    if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
                    session.PointerUp(Pixels(e));
                    e.Use();
                    break;
                case EventType.ContextClick:
                    if (!session.Contains(Pixels(e), view.camera)) break;
                    session.RightClick(Pixels(e), view.camera);
                    e.Use();
                    break;
                case EventType.Repaint:
                    PlaceCandidates(session, view);
                    break;
            }
        }

        static void PlaceCandidates(LetteraSceneEdit session, SceneView view)
        {
            if (!session.Field.IsComposing) return;
            RectTransform rect = ((ITextComponent)session.Text).TextRect;
            Vector2 gui = HandleUtility.WorldToGUIPoint(rect.TransformPoint(new Vector3(session.Visual.Caret.xMin, session.Visual.Caret.yMin)));
            // From the view's corner to the window's: what the system measures from.
            Vector2 inWindow = GUIUtility.GUIToScreenPoint(gui) - view.position.position;
            LetteraSceneEditInput.PlaceCandidates(EditorGUIUtility.PointsToPixels(inWindow));
        }

        static void During(SceneView view)
        {
            LetteraSceneEdit session = LetteraSceneEdit.Current;
            Event e = Event.current;
            if (session == null || e == null) return;
            RectTransform rect = ((ITextComponent)session.Text).TextRect;
            if (rect == null) return;
            if (session.Contains(Pixels(e), view.camera)) EditorGUIUtility.AddCursorRect(new Rect(0, 0, view.position.width, view.position.height), MouseCursor.Text);
            if (e.type == EventType.Repaint) Draw(session, rect);
        }

        static void Draw(LetteraSceneEdit session, RectTransform rect)
        {
            Matrix4x4 before = Handles.matrix;
            UnityEngine.Rendering.CompareFunction zTest = Handles.zTest;
            Color color = Handles.color;
            Handles.matrix = rect.localToWorldMatrix;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            LetteraSceneEditVisual visual = session.Visual;
            Rect frame = rect.rect;
            Fill(frame, Color.clear, new Color(0.4f, 0.65f, 1f, 0.9f));
            // Over the glyphs, not under them as in a player, so the color has to leave them readable.
            Color selection = visual.SelectionColor;
            selection.a = Mathf.Min(selection.a, 0.4f);
            foreach (Rect selected in visual.Rects) Fill(selected, selection, Color.clear);
            foreach (Rect line in visual.Underlines) Fill(line, visual.UnderlineColor, Color.clear);
            if (visual.CaretOn)
            {
                // A caret as thin as the type can vanish at a distance, and on a background of its own color.
                float least = HandleUtility.GetHandleSize(rect.TransformPoint(visual.Caret.center)) * 0.012f / Mathf.Max(rect.lossyScale.x, 0.0001f);
                Rect caret = visual.Caret;
                caret.width = Mathf.Max(caret.width, least);
                Fill(caret, new Color(0.25f, 0.6f, 1f, 1f), Color.clear);
            }

            Handles.matrix = before;
            Handles.zTest = zTest;
            Handles.color = color;
        }

        static readonly Vector3[] s_Corners = new Vector3[4];

        static void Fill(Rect box, Color face, Color outline)
        {
            s_Corners[0] = new Vector3(box.xMin, box.yMin);
            s_Corners[1] = new Vector3(box.xMax, box.yMin);
            s_Corners[2] = new Vector3(box.xMax, box.yMax);
            s_Corners[3] = new Vector3(box.xMin, box.yMax);
            Handles.DrawSolidRectangleWithOutline(s_Corners, face, outline);
        }
    }

    /// <summary>The floating bar of a session: formatting, the text's tags, markup shown or hidden. Listed in the Scene view's overlay menu.</summary>
    [Overlay(typeof(SceneView), Id, "Lettera Text", defaultDisplay: false)]
    internal sealed class LetteraSceneEditBar : Overlay
    {
        internal const string Id = "lettera-scene-edit";

        VisualElement m_Root, m_Tools, m_Tags;
        Label m_Chip, m_Markup;
        ColorField m_Color;
        readonly List<(Label tool, Type effect)> m_Formats = new List<(Label, Type)>();
        readonly List<(string, Type)> m_ShownTags = new List<(string, Type)>();

        public override VisualElement CreatePanelContent()
        {
            m_Root = LtUI.Root();
            m_Root.style.width = 340;
            m_Root.style.paddingLeft = m_Root.style.paddingRight = 4;
            m_Root.style.paddingTop = m_Root.style.paddingBottom = 4;
            m_Chip = LtUI.Text("", "lt-chip");
            m_Chip.style.whiteSpace = WhiteSpace.Normal;
            m_Chip.name = "lt-scene-chip";
            m_Root.Add(m_Chip);

            m_Tools = LtUI.El("lt-toolbar");
            m_Tools.style.flexWrap = Wrap.Wrap;
            m_Tools.Add(Format("B", "lt-tool lt-tool--bold", "Bold", typeof(BoldEffect)));
            m_Tools.Add(Format("I", "lt-tool lt-tool--italic", "Italic", typeof(ItalicEffect)));
            m_Tools.Add(Format("U", "lt-tool", "Underline", typeof(UnderlineEffect)));
            m_Tools.Add(Format("S", "lt-tool", "Strikethrough", typeof(StrikethroughEffect)));
            m_Tools.Add(LtUI.El("lt-tool-gap"));
            m_Color = new ColorField { value = new Color(1, 0.8f, 0), showAlpha = false, showEyeDropper = false, tooltip = "Color: wraps the selection, or colors what is typed next." };
            m_Color.AddToClassList("lt-scene-color");
            m_Color.style.width = 44;
            m_Color.RegisterValueChangedCallback(e => LetteraSceneEdit.Current?.ApplyColor(e.newValue));
            m_Tools.Add(m_Color);
            m_Tools.Add(LtUI.El("lt-tool-gap"));
            m_Markup = LtUI.Clickable("Markup", "lt-tool", "Shows the tags in the text, or hides them.", () =>
            {
                LetteraSceneEdit session = LetteraSceneEdit.Current;
                if (session != null) session.ShowsMarkup = !session.ShowsMarkup;
                Refresh();
            });
            m_Tools.Add(m_Markup);
            m_Tools.Add(LtUI.Clickable("Done", "lt-tool", "Ends editing, as Esc does. What was typed stays, as one undo step.", () =>
            {
                LetteraSceneEdit.Current?.End(LetteraSceneEditEnd.Done);
                Refresh();
            }));
            m_Root.Add(m_Tools);

            m_Tags = LtUI.El("lt-toolbar");
            m_Tags.style.flexWrap = Wrap.Wrap;
            m_Root.Add(m_Tags);

            m_Root.schedule.Execute(Refresh).Every(200);
            LetteraSceneEdit.StateChanged += Refresh;
            Refresh();
            return m_Root;
        }

        public override void OnWillBeDestroyed()
        {
            LetteraSceneEdit.StateChanged -= Refresh;
            base.OnWillBeDestroyed();
        }

        Label Format(string text, string classes, string name, Type effect)
        {
            Label tool = LtUI.Clickable(text, classes, name + ": the selection, or what is typed next.", () =>
            {
                LetteraSceneEdit.Current?.ToggleFormat(effect);
                Refresh();
            });
            tool.name = "lt-scene-" + text.ToLowerInvariant();
            m_Formats.Add((tool, effect));
            return tool;
        }

        void Refresh()
        {
            LetteraSceneEdit session = LetteraSceneEdit.Current;
            bool running = session != null;
            m_Tools.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            m_Tags.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            string why = LetteraSceneEditView.LastWhy ?? "Select one text and press F2, or Alt and double click it.";
            string chip = running ? "Editing " + session.Text.name + ". Esc ends." : why;
            if (m_Chip.text != chip) m_Chip.text = chip;
            m_Chip.EnableInClassList("lt-chip--orange", !running && LetteraSceneEditView.LastWhy != null);
            m_Chip.EnableInClassList("lt-chip--accent", running);
            if (!running) return;
            foreach ((Label tool, Type effect) in m_Formats)
            {
                tool.EnableInClassList("lt-tool--on", session.IsFormatOn(effect));
                tool.SetEnabled(session.CanFormat(effect));
            }
            m_Markup.EnableInClassList("lt-tool--on", session.ShowsMarkup);
            RefreshTags(session);
        }

        void RefreshTags(LetteraSceneEdit session)
        {
            List<(string tag, Type effect)> tags = session.CommonTags();
            bool same = tags.Count == m_ShownTags.Count;
            for (int i = 0; same && i < tags.Count; i++) same = tags[i] == m_ShownTags[i];
            if (same) return;
            m_ShownTags.Clear();
            m_Tags.Clear();
            foreach ((string tag, Type effect) in tags)
            {
                m_ShownTags.Add((tag, effect));
                Label tool = LtUI.Clickable("<" + tag + ">", "lt-tool", "Wraps the selection in this tag.", () =>
                {
                    LetteraSceneEdit.Current?.ToggleFormat(effect);
                    Refresh();
                });
                m_Tags.Add(tool);
            }
        }
    }
}
