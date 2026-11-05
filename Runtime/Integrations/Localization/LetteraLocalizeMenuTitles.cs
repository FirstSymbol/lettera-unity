using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace FirstSymbol.Lettera.Localization
{
    /// <summary>
    /// Menu command titles from a string table, in the selected language, for every text's menu while this is enabled.
    /// A command is looked up by its TitleKey: the title written on it, or its kind's name, such as Cut, PastePlain or Bold.
    /// A key the table lacks keeps the title it had. It applies in play mode.
    /// </summary>
    [AddComponentMenu("Lettera/Localize Menu Titles")]
    [DisallowMultipleComponent]
    public class LetteraLocalizeMenuTitles : MonoBehaviour
    {
        [SerializeField] LocalizedStringTable m_Table = new LocalizedStringTable();
        [Tooltip("Put before each key, as in Menu/ for Menu/Copy. Empty looks keys up as they are.")]
        [SerializeField] string m_KeyPrefix = "";

        StringTable m_Loaded;
        readonly Dictionary<string, string> m_Titles = new Dictionary<string, string>();
        Func<LetteraEditCommand, string> m_Source, m_Before;
        bool m_Joined;

        public LocalizedStringTable Table
        {
            get => m_Table;
            set
            {
                bool listening = isActiveAndEnabled;
                if (listening) m_Table.TableChanged -= Use;
                m_Table = value ?? new LocalizedStringTable();
                Use(null);
                if (listening) m_Table.TableChanged += Use;
            }
        }

        public string KeyPrefix
        {
            get => m_KeyPrefix;
            set
            {
                m_KeyPrefix = value ?? "";
                m_Titles.Clear();
                LetteraEditCommand.RefreshTitles();
            }
        }

        void OnEnable()
        {
            Join();
            // Unity's table answers once loaded, and again with each new language.
            m_Table.TableChanged += Use;
        }

        void OnDisable()
        {
            m_Table.TableChanged -= Use;
            Leave();
        }

        internal void Join()
        {
            if (m_Joined) return;
            m_Joined = true;
            m_Source = Title;
            m_Before = LetteraEditCommand.TitleSource;
            LetteraEditCommand.TitleSource = m_Source;
        }

        internal void Leave()
        {
            if (!m_Joined) return;
            m_Joined = false;
            if (LetteraEditCommand.TitleSource == m_Source) LetteraEditCommand.TitleSource = m_Before;
            m_Before = null;
            m_Loaded = null;
            m_Titles.Clear();
            LetteraEditCommand.RefreshTitles();
        }

        /// <summary>The table of the language now selected.</summary>
        internal void Use(StringTable table)
        {
            m_Loaded = table;
            m_Titles.Clear();
            LetteraEditCommand.RefreshTitles();
        }

        string Title(LetteraEditCommand command)
        {
            string title = null;
            if (m_Loaded != null)
            {
                string key = command.TitleKey;
                if (!m_Titles.TryGetValue(key, out title))
                {
                    StringTableEntry entry = m_Loaded.GetEntry(m_KeyPrefix + key);
                    title = entry != null ? entry.GetLocalizedString() : null;
                    m_Titles[key] = title;
                }
            }
            return !string.IsNullOrEmpty(title) ? title : m_Before?.Invoke(command);
        }
    }
}
