using System;
using System.Collections.Generic;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>The names a text slot can offer: the keys of the inline items a style knows, the ways a reveal brings text in.</summary>
    internal static class LetteraSlotSuggestions
    {
        public static IReadOnlyList<string> For(LetteraParamInfo info, LetteraEffect effect)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(string name)
            {
                if (!string.IsNullOrEmpty(name) && seen.Add(name)) names.Add(name);
            }

            switch (info.Suggests)
            {
                case "inline":
                    if (effect is InlineEffect inline)
                    {
                        foreach (LetteraInlineEntry entry in inline.Items) Add(entry?.Key);
                        if (inline.Library != null)
                            foreach (LetteraInlineEntry entry in inline.Library.Entries) Add(entry?.Key);
                    }
                    LetteraInlineLibrary shared = LetteraSettings.Instance.Motion.InlineLibrary;
                    if (shared != null)
                        foreach (LetteraInlineEntry entry in shared.Entries) Add(entry?.Key);
                    break;
                case "entrance":
                    if (effect is RevealEffect reveal)
                        foreach (LetteraEntranceEntry entry in reveal.Entries) Add(entry?.Name);
                    foreach (string name in Enum.GetNames(typeof(LetteraEntranceKind))) Add(name.ToLowerInvariant());
                    break;
            }
            return names;
        }
    }
}
