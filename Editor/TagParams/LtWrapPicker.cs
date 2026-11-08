using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>One tag the wrap list offers: what it is called, what it does, and where in the list it belongs.</summary>
    internal sealed class LetteraWrapEntry
    {
        public string Tag, Effect, Kind, Words, Default, Group;
        public bool Bound;
        public Type EffectType;
        internal int Order;
    }

    /// <summary>The tags a text can wrap a selection in: those that have a style for it, with its project's own first, and those it cannot use dimmed at the end.</summary>
    internal static class LetteraWrapList
    {
        public const string Used = "Used here", Project = "Project", All = "All", NotBound = "Not in this text's vocabulary";
        public static readonly string[] Groups = { Used, Project, All, NotBound };

        static Dictionary<Type, LetteraStyleTemplate> s_ByEffect;
        static Dictionary<Type, string> s_TagOf;
        static List<(string tag, LetteraStyleTemplate template)> s_TagTemplates;

        static void Prepare()
        {
            if (s_ByEffect != null) return;
            s_ByEffect = new Dictionary<Type, LetteraStyleTemplate>();
            s_TagOf = new Dictionary<Type, string>();
            s_TagTemplates = new List<(string, LetteraStyleTemplate)>();
            foreach (LetteraStyleTemplate template in LetteraStyleTemplates.All)
            {
                if (template.Driven != LetteraStyleTemplates.Tags) continue;
                try
                {
                    var matcher = template.Matcher?.Invoke() as TagMatcher;
                    LetteraEffect effect = template.Effect?.Invoke();
                    if (matcher == null || effect == null) continue;
                    if (!s_ByEffect.ContainsKey(effect.GetType()))
                    {
                        s_ByEffect[effect.GetType()] = template;
                        s_TagOf[effect.GetType()] = matcher.Name;
                    }
                    s_TagTemplates.Add((matcher.Name, template));
                }
                catch (Exception)
                {
                    // A template that cannot be made here is no help to a search.
                }
            }
        }

        /// <summary>The tag the picker writes for an effect, or null when it has none.</summary>
        public static string TagOf(Type effect)
        {
            Prepare();
            return s_TagOf.TryGetValue(effect, out string tag) ? tag : null;
        }

        static bool IsBuiltIn(string tag, Type effect, string parameter)
        {
            foreach (LetteraStyle style in LetteraDefaultStyles.Styles)
                if (style.Matcher is TagMatcher match && string.Equals(match.Name, tag, StringComparison.OrdinalIgnoreCase))
                    return style.Effect != null && style.Effect.GetType() == effect && (style.DefaultParameter ?? "") == (parameter ?? "");
            return false;
        }

        public static List<LetteraWrapEntry> Build(IReadOnlyList<LetteraStyle> styles, string source)
        {
            Prepare();
            var entries = new List<LetteraWrapEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LetteraTagSpan tag in LetteraTagScanner.Scan(source ?? "")) inText.Add(tag.Name);
            if (styles != null)
                foreach (LetteraStyle style in styles)
                {
                    if (style == null || !style.Enabled || style.Effect == null || !(style.Matcher is TagMatcher match) || !match.CanWrap) continue;
                    if (!seen.Add(match.Name)) continue;
                    Type type = style.Effect.GetType();
                    s_ByEffect.TryGetValue(type, out LetteraStyleTemplate template);
                    entries.Add(new LetteraWrapEntry
                    {
                        Tag = match.Name,
                        EffectType = type,
                        Effect = template?.Name ?? LetteraStyleList.Pretty(type.Name, "Effect"),
                        Kind = template?.Kind ?? LetteraStyleTemplates.Custom,
                        Words = (template != null ? LetteraStyleTemplates.WordsOf(template, false) : "Your own effect.") + " " + template?.Keywords,
                        Default = style.DefaultParameter,
                        Bound = true,
                        Group = inText.Contains(match.Name) ? Used : IsBuiltIn(match.Name, type, style.DefaultParameter) ? All : Project,
                        Order = entries.Count,
                    });
                }
            HashSet<string> known = LetteraVocabulary.Tags(styles ?? new List<LetteraStyle>());
            foreach ((string tag, LetteraStyleTemplate template) in s_TagTemplates)
            {
                if (known.Contains(tag) || !seen.Add(tag)) continue;
                entries.Add(new LetteraWrapEntry
                {
                    Tag = tag,
                    Effect = template.Name,
                    Kind = template.Kind,
                    Words = LetteraStyleTemplates.WordsOf(template, false) + " " + template.Keywords,
                    Bound = false,
                    Group = NotBound,
                    Order = entries.Count,
                });
            }
            return entries;
        }

        /// <summary>How well an entry fits a search: its tag and name count most, its kind and words less.</summary>
        public static int Rank(string query, LetteraWrapEntry entry)
        {
            if (string.IsNullOrWhiteSpace(query)) return 1;
            int best = Math.Max(LtSearchText.Score(query, entry.Tag) * 3, LtSearchText.Score(query, entry.Effect) * 2);
            best = Math.Max(best, LtSearchText.Score(query, entry.Kind));
            if (!string.IsNullOrEmpty(entry.Words) && LtSearchText.Score(query, entry.Words) >= 30) best = Math.Max(best, 30);
            return best;
        }

        /// <summary>The entries a search keeps, group by group in the list's order, best fit first within a group.</summary>
        public static List<LetteraWrapEntry> Filter(IEnumerable<LetteraWrapEntry> entries, string query)
        {
            var kept = new List<(LetteraWrapEntry entry, int score)>();
            foreach (LetteraWrapEntry entry in entries)
            {
                int score = Rank(query, entry);
                if (score > 0) kept.Add((entry, score));
            }
            return kept.OrderBy(k => Array.IndexOf(Groups, k.entry.Group)).ThenByDescending(k => k.score).ThenBy(k => k.entry.Order).Select(k => k.entry).ToList();
        }
    }

    /// <summary>
    /// The list a selection is wrapped from. It sits in the inspector under the text field, takes the keys while it is open and goes with Esc,
    /// a click outside or a choice. A popup window would take the focus from the editor the caret is in.
    /// </summary>
    internal sealed class LtWrapPicker : VisualElement
    {
        readonly List<LetteraWrapEntry> m_All;
        readonly LtSearch m_Search;
        readonly VisualElement m_List;
        readonly ScrollView m_Scroll;
        readonly Label m_Info;
        readonly List<(LetteraWrapEntry entry, VisualElement row)> m_Rows = new List<(LetteraWrapEntry, VisualElement)>();
        int m_At;

        /// <summary>An entry was picked; true when it was picked to be edited at once.</summary>
        public event Action<LetteraWrapEntry, bool> Chosen;
        public event Action Closed;

        public LtSearch Search => m_Search;
        public IReadOnlyList<(LetteraWrapEntry entry, VisualElement row)> Shown => m_Rows;

        public LtWrapPicker(List<LetteraWrapEntry> entries, string note = null)
        {
            m_All = entries;
            AddToClassList("lt-wrap");
            m_Search = new LtSearch("Search tags");
            m_Search.Changed += Refresh;
            m_Search.Field.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Add(m_Search);
            m_List = LtUI.El("lt-wrap__list");
            m_Scroll = new ScrollView { mode = ScrollViewMode.Vertical };
            m_Scroll.AddToClassList("lt-wrap__scroll");
            m_Scroll.Add(m_List);
            Add(m_Scroll);
            m_Info = LtUI.Text(note ?? "", "lt-hint lt-wrap__info");
            m_Info.style.display = string.IsNullOrEmpty(note) ? DisplayStyle.None : DisplayStyle.Flex;
            Add(m_Info);
            Add(LtUI.Text("Enter: wrap    Ctrl+Enter: wrap and edit    Esc: close", "lt-hint lt-wrap__keys"));
            Refresh("");
            m_Search.Field.schedule.Execute(() => m_Search.Field.Focus());
        }

        /// <summary>Types into the search box.</summary>
        public void Query(string text)
        {
            m_Search.Field.SetValueWithoutNotify(text);
            Refresh(text);
        }

        public void Say(string message)
        {
            m_Info.text = message;
            m_Info.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void Refresh(string query)
        {
            m_List.Clear();
            m_Rows.Clear();
            string group = null;
            List<LetteraWrapEntry> shown = LetteraWrapList.Filter(m_All, query);
            foreach (LetteraWrapEntry entry in shown)
            {
                if (entry.Group != group)
                {
                    group = entry.Group;
                    m_List.Add(LtUI.Text(group, "lt-wrap__group"));
                }
                VisualElement row = LtUI.El("lt-wrap__row",
                    LtUI.Text("<" + entry.Tag + ">", "lt-wrap__tag"),
                    LtUI.Text(entry.Effect, "lt-wrap__name"),
                    LtUI.Text(string.IsNullOrEmpty(entry.Default) ? "" : "default " + entry.Default, "lt-wrap__default"),
                    LtUI.Text(entry.Kind ?? "", "lt-wrap__kind"));
                row.EnableInClassList("lt-wrap__row--dim", !entry.Bound);
                LetteraWrapEntry picked = entry;
                row.RegisterCallback<ClickEvent>(e => Pick(picked, e.ctrlKey || e.commandKey));
                m_List.Add(row);
                m_Rows.Add((entry, row));
            }
            if (shown.Count == 0)
            {
                m_List.Add(LtUI.Text("No tag matches \"" + query + "\".", "lt-hint lt-wrap__empty"));
                m_List.Add(LtUI.Clickable("Type it as plain markup", "lt-fix__button lt-wrap__plain", "Closes this list and leaves the text as it is.", Close));
            }
            m_At = 0;
            Paint();
        }

        void Paint()
        {
            for (int i = 0; i < m_Rows.Count; i++) m_Rows[i].row.EnableInClassList("lt-wrap__row--on", i == m_At);
            if (m_Scroll.panel != null && m_At >= 0 && m_At < m_Rows.Count) m_Scroll.ScrollTo(m_Rows[m_At].row);
        }

        void OnKey(KeyDownEvent e)
        {
            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    if (m_Rows.Count > 0) m_At = (m_At + 1) % m_Rows.Count;
                    Paint();
                    break;
                case KeyCode.UpArrow:
                    if (m_Rows.Count > 0) m_At = (m_At - 1 + m_Rows.Count) % m_Rows.Count;
                    Paint();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (m_At >= 0 && m_At < m_Rows.Count) Pick(m_Rows[m_At].entry, e.ctrlKey || e.commandKey);
                    break;
                case KeyCode.Escape:
                    Close();
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        /// <summary>What a click or Enter does.</summary>
        public void Pick(LetteraWrapEntry entry, bool edit)
        {
            if (!entry.Bound)
            {
                Say("<" + entry.Tag + "> is not in this text's vocabulary, so it would show as plain text. Add a style that binds it in the Styles list, or turn on the global style preset.");
                return;
            }
            Chosen?.Invoke(entry, edit);
        }

        public void Close()
        {
            if (parent != null) RemoveFromHierarchy();
            Closed?.Invoke();
        }
    }
}
