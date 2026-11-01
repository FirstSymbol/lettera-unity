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
    /// <summary>What shaping made of a text: its glyphs, their fonts and their tiles, and one glyph's outline.</summary>
    internal sealed class LetteraGlyphsWindow : LetteraToolWindow
    {
        [MenuItem("Tools/Lettera/Glyphs", false, 20)]
        static void Open() => GetWindow<LetteraGlyphsWindow>("Lettera Glyphs").minSize = new Vector2(320, 300);

        /// <summary>Opens on a font and some letters of it, as a font's own glyph grid asks.</summary>
        internal static void OpenFor(LetteraFontAsset font, string text)
        {
            var window = GetWindow<LetteraGlyphsWindow>("Lettera Glyphs");
            window.minSize = new Vector2(320, 300);
            window.m_Font.value = font;
            window.m_Text.value = text;
        }

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
                painter.lineJoin = LineJoin.Round;
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
}
