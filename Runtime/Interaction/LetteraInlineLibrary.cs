using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A set of inline items to share between texts.</summary>
    [CreateAssetMenu(fileName = "Inline Library", menuName = "Lettera/Inline Library", order = 95)]
    public partial class LetteraInlineLibrary : ScriptableObject, ILetteraInlineSource
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

        [SerializeField] List<LetteraInlineEntry> m_Entries = new List<LetteraInlineEntry>();

        public List<LetteraInlineEntry> Entries => m_Entries;

        public bool TryGet(string key, out LetteraInlineEntry entry)
        {
            foreach (LetteraInlineEntry candidate in m_Entries)
            {
                if (candidate == null || candidate.Key != key) continue;
                entry = candidate;
                return true;
            }
            entry = null;
            return false;
        }
    }
}
