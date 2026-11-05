using System;
using System.Collections.Generic;
using System.Reflection;
using FirstSymbol.Lettera.Editor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Localization.Editor
{
    /// <summary>Only with Unity Localization installed: a text's right click, a chip on its Interaction card, and the palette.</summary>
    internal static class LetteraLocalizeMenus
    {
        [InitializeOnLoadMethod]
        static void Join()
        {
            LetteraEditingMenus.ExtraChips.Add(new LetteraCardChip { Title = "Localize", Type = typeof(LocalizeStringEvent), Add = Setup });
            LetteraEditingMenus.ExtraChips.Add(new LetteraCardChip { Title = "Font By Locale", Type = typeof(LetteraLocalizeFont), Add = c => Undo.AddComponent<LetteraLocalizeFont>(c.gameObject) });
        }

        [MenuItem("CONTEXT/LetteraText/Localize")]
        [MenuItem("CONTEXT/LetteraWorldText/Localize")]
        [MenuItem("CONTEXT/LetteraText3D/Localize")]
        static void Localize(MenuCommand command) => Setup((Component)command.context);

        [MenuItem("CONTEXT/LetteraText/Localize Font")]
        [MenuItem("CONTEXT/LetteraWorldText/Localize Font")]
        [MenuItem("CONTEXT/LetteraText3D/Localize Font")]
        static void LocalizeFont(MenuCommand command)
        {
            var component = (Component)command.context;
            if (component.GetComponent<LetteraLocalizeFont>() == null) Undo.AddComponent<LetteraLocalizeFont>(component.gameObject);
        }

        /// <summary>A string event wired to SetText, so that the saved text stays what it is and is what a build falls back on with no entry.</summary>
        internal static void Setup(Component text)
        {
            foreach (LocalizeStringEvent present in text.GetComponents<LocalizeStringEvent>())
                for (int i = 0; i < present.OnUpdateString.GetPersistentEventCount(); i++)
                    if (present.OnUpdateString.GetPersistentTarget(i) == text)
                        return;
            var localize = Undo.AddComponent<LocalizeStringEvent>(text.gameObject);
            MethodInfo setText = text.GetType().GetMethod("SetText", new[] { typeof(string) });
            var call = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), text, setText);
            UnityEventTools.AddPersistentListener(localize.OnUpdateString, call);
            localize.OnUpdateString.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            EditorUtility.SetDirty(localize);
        }
    }

    internal sealed class LetteraLocalizePalette : ILetteraPaletteSource
    {
        public IEnumerable<LetteraCommand> Commands()
        {
            Component Selected()
            {
                GameObject go = Selection.activeGameObject;
                return go == null ? null : go.GetComponent<LetteraText>() as Component ?? go.GetComponent<LetteraWorldText>();
            }
            yield return new LetteraCommand { Name = "Localize", Group = "Selected text", Words = "locale language translate table string", Available = () => Selected() != null, Run = () => LetteraLocalizeMenus.Setup(Selected()) };
        }
    }

    [CustomEditor(typeof(LetteraLocalizeMenuTitles))]
    internal sealed class LetteraLocalizeMenuTitlesEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            // The table is picked in Unity's own control: only it knows the tables.
            root.Add(LtUI.Card("",
                LtUI.Text("Menu titles from a string table, in the selected language, for every text's menu while this is enabled. A key the table lacks keeps its title.", "lt-hint"),
                new UnityEditor.UIElements.PropertyField(serializedObject.FindProperty("m_Table"), "Table"),
                LtUI.Field("Key Prefix", serializedObject.FindProperty("m_KeyPrefix"), "Put before each key, as in Menu/ for Menu/Copy."),
                LtUI.Text("Keys of the project's commands: " + Keys() + ". A command with a title of its own is looked up by that title.", "lt-hint")));
            return root;
        }

        internal static string Keys()
        {
            var keys = new List<string>();
            foreach (LetteraEditCommand command in LetteraSettings.Instance.Editing.Commands)
                if (command != null && !keys.Contains(command.TitleKey))
                    keys.Add(command.TitleKey);
            return keys.Count > 0 ? string.Join(", ", keys) : "none";
        }
    }

    [CustomEditor(typeof(LetteraLocalizeFont))]
    [CanEditMultipleObjects]
    internal sealed class LetteraLocalizeFontEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            // The table entry is picked in Unity's own control: only it knows the tables.
            root.Add(LtUI.Card("", LtUI.Text("A font or a font stack per locale, from an asset table. It applies in play mode, and an empty entry goes back to the saved font.", "lt-hint"), new UnityEditor.UIElements.PropertyField(serializedObject.FindProperty("m_LocalizedAssetReference"), "Font")));
            return root;
        }
    }
}
