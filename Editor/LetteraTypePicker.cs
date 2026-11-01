using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Lists types to add, with a search box and groups.</summary>
    internal sealed class LetteraTypePicker : EditorWindow
    {
        Action<Type> m_Chosen;
        List<(string group, string title, Type type)> m_All;
        VisualElement m_List;

        public static void Open(Rect anchor, List<(string group, string title, Type type)> items, Action<Type> chosen)
        {
            var window = CreateInstance<LetteraTypePicker>();
            window.m_All = items;
            window.m_Chosen = chosen;
            window.ShowAsDropDown(GUIUtility.GUIToScreenRect(anchor), new Vector2(280, 360));
        }

        void CreateGUI()
        {
            VisualElement root = LtUI.Root();
            root.style.flexGrow = 1;
            rootVisualElement.Add(root);
            var search = new TextField();
            search.AddToClassList("lt-row__field");
            root.Add(search);
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            m_List = scroll.contentContainer;
            search.RegisterValueChangedCallback(e => Fill(e.newValue));
            Fill("");
            search.schedule.Execute(() => search.Focus());
        }

        void Fill(string query)
        {
            m_List.Clear();
            foreach ((string group, string title, Type type) in Matching(m_All, query))
            {
                Type chosen = type;
                m_List.Add(LtUI.Clickable(group + "  /  " + title, "lt-tool", chosen.FullName, () =>
                {
                    m_Chosen?.Invoke(chosen);
                    Close();
                }));
            }
        }

        public static IEnumerable<(string group, string title, Type type)> Matching(IEnumerable<(string group, string title, Type type)> items, string query) =>
            items.Where(i => string.IsNullOrEmpty(query) || i.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || i.group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
