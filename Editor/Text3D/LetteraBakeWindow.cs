using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    internal sealed class LetteraBakeWindow : EditorWindow
    {
        [SerializeField] LetteraText3D m_Text;
        [SerializeField] bool m_Static = true, m_Lightmap, m_Disable = true, m_Fbx;
        [SerializeField] int m_Levels = 1;

        internal static void Open(LetteraText3D text)
        {
            var window = GetWindow<LetteraBakeWindow>(true, "Bake Text 3D");
            window.m_Text = text;
            window.minSize = new Vector2(360, 320);
            window.Build();
        }

        void OnEnable() => Build();

        void Build()
        {
            rootVisualElement.Clear();
            if (m_Text == null) return;
            VisualElement root = LtUI.Root();
            rootVisualElement.Add(root);
            VisualElement page = LtFrame.Header(root, "Bake " + m_Text.name, "A mesh asset, and what draws it without the text system");
            VisualElement Switch(Func<bool> get, Action<bool> set) => LtUI.El("lt-switch-holder", LtUI.Toggle(get, set));
            var levels = new SliderInt(1, 4) { value = m_Levels, showInputField = true };
            levels.RegisterValueChangedCallback(e => m_Levels = e.newValue);
            VisualElement fbx = Switch(() => m_Fbx && LetteraText3DBake.FbxAvailable, on => m_Fbx = on);
            fbx.SetEnabled(LetteraText3DBake.FbxAvailable);
            page.Add(LtUI.Card("Make",
                LtUI.Row("Static Object", Switch(() => m_Static, on => m_Static = on), "An object that draws the baked mesh, marked static for batching and occlusion."),
                LtUI.Row("Lightmap UVs", Switch(() => m_Lightmap, on => m_Lightmap = on), "Unwraps a second UV set and lets the object take baked light. A material mapped across the whole text maps per surface afterwards."),
                LtUI.Row("Levels Of Detail", levels, "Above one, coarser meshes for distance, in a level-of-detail group."),
                LtUI.Row("FBX", fbx, LetteraText3DBake.FbxAvailable ? "Also writes the baked object as FBX beside the mesh." : "Needs the FBX Exporter package."),
                LtUI.Row("Turn Text Off", Switch(() => m_Disable, on => m_Disable = on), "The text is turned off once its baked object stands in. One undo brings it back.")));
            page.Add(LtUI.Clickable("Bake", "lt-button", "Choose where the mesh goes. Materials made only in memory are saved beside it.", Run));
        }

        void Run()
        {
            if (m_Text == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Bake Text 3D", m_Text.name, "asset", "Where to save the mesh");
            if (string.IsNullOrEmpty(path)) return;
            GameObject made = LetteraText3DBake.Bake(m_Text, path, new LetteraBakeOptions
            {
                StaticObject = m_Static,
                LightmapUvs = m_Lightmap,
                LodLevels = m_Levels,
                DisableText = m_Disable,
                Fbx = m_Fbx,
            });
            if (made != null) Selection.activeGameObject = made;
            Close();
        }
    }
}
