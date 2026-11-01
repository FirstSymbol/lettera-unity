using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Lists types to add, in groups, with a line in words each, and a search.</summary>
    internal static class LetteraTypePicker
    {
        public static void Open(VisualElement anchor, List<(string group, string title, Type type)> items, Func<Type, string> words, Action<Type> chosen)
        {
            var rows = new List<(string, string, object)>();
            var groups = new Dictionary<object, string>();
            foreach ((string group, string title, Type type) in items)
            {
                rows.Add((title, words?.Invoke(type), type));
                groups[type] = group;
            }
            LtPopup.Show(anchor, new Vector2(Mathf.Max(anchor.worldBound.width, 320), 380), () => new LtChoiceList(rows, value =>
            {
                chosen((Type)value);
                LtPopup.CloseCurrent();
            }, -1, true, value => groups[value]));
        }

        public static IEnumerable<(string group, string title, Type type)> Matching(IEnumerable<(string group, string title, Type type)> items, string query) =>
            items.Where(i => string.IsNullOrEmpty(query) || i.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || i.group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
