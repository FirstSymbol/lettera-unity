using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraDictionaryScript { Thai, Lao, Khmer, Myanmar }

    /// <summary>A word list for a script written without spaces, which is where its lines then break. It takes effect only once listed in the settings or registered.</summary>
    [CreateAssetMenu(fileName = "Word Dictionary", menuName = "Lettera/Word Dictionary", order = 96)]
    public class LetteraWordDictionary : ScriptableObject
    {
        [SerializeField] LetteraDictionaryScript m_WritingSystem;
        [Tooltip("The built automaton.")]
        [SerializeField] TextAsset m_Data;
        [Tooltip("What the source of the words asks to be kept with them.")]
        [SerializeField, TextArea] string m_SourceNotice = "";
        [SerializeField] int m_WordCount;

        public LetteraDictionaryScript Script { get => m_WritingSystem; set => m_WritingSystem = value; }
        public TextAsset Data { get => m_Data; set => m_Data = value; }
        public string SourceNotice { get => m_SourceNotice; set => m_SourceNotice = value; }
        public int WordCount { get => m_WordCount; set => m_WordCount = value; }
    }

    /// <summary>What word dictionaries are in force. A project with none pays nothing: its lines break as before.</summary>
    public static class LetteraWordDictionaries
    {
        static readonly Dictionary<LetteraDictionaryScript, LetteraWordAutomaton> s_Registered = new Dictionary<LetteraDictionaryScript, LetteraWordAutomaton>();
        static bool s_SettingsRead;

        internal static UnicodeScript ScriptOf(LetteraDictionaryScript script)
        {
            switch (script)
            {
                case LetteraDictionaryScript.Lao: return UnicodeScript.Lao;
                case LetteraDictionaryScript.Khmer: return UnicodeScript.Khmer;
                case LetteraDictionaryScript.Myanmar: return UnicodeScript.Myanmar;
                default: return UnicodeScript.Thai;
            }
        }

        /// <summary>Reads the settings' list, once and after each change of them.</summary>
        internal static void EnsureFromSettings()
        {
            if (s_SettingsRead) return;
            s_SettingsRead = true;
            LetteraSettings.Changed += () => s_SettingsRead = false;
            LetteraWordSegmenters.Clear();
            foreach (KeyValuePair<LetteraDictionaryScript, LetteraWordAutomaton> pair in s_Registered) LetteraWordSegmenters.Set(ScriptOf(pair.Key), pair.Value);
            foreach (LetteraWordDictionary dictionary in LetteraSettings.Instance.Dictionaries)
                if (dictionary != null && dictionary.Data != null && !s_Registered.ContainsKey(dictionary.Script))
                    LetteraWordSegmenters.Set(ScriptOf(dictionary.Script), new LetteraWordAutomaton(dictionary.Data.bytes));
        }

        /// <summary>Puts a dictionary in force now, and showing text reflows.</summary>
        public static void Register(LetteraDictionaryScript script, byte[] data)
        {
            s_Registered[script] = new LetteraWordAutomaton(data);
            Reflow();
        }

        public static void Register(LetteraWordDictionary dictionary) => Register(dictionary.Script, dictionary.Data.bytes);

        public static void Unregister(LetteraDictionaryScript script)
        {
            if (!s_Registered.Remove(script)) return;
            Reflow();
        }

        // Text shaped under the old words cannot be found again, and every text is told.
        static void Reflow()
        {
            s_SettingsRead = false;
            EnsureFromSettings();
            LetteraSettings.RaiseChanged();
        }
    }
}
