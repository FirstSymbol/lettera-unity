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
