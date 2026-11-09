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
    /// <summary>Making font assets in a batch, and the way to the tools that make a font smaller or teach it words.</summary>
    internal sealed class LetteraFontToolsWindow : LetteraToolWindow
    {
        [MenuItem("Tools/FirstSymbol/Lettera/Font Tools", false, 24)]
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
}
