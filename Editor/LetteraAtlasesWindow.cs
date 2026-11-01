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
}
