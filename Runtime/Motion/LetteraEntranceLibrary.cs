using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Named entrances to share between texts.</summary>
    [CreateAssetMenu(fileName = "Entrance Library", menuName = "Lettera/Entrance Library", order = 93)]
    public class LetteraEntranceLibrary : ScriptableObject
    {
        [SerializeField] List<LetteraEntranceEntry> m_Entries = new List<LetteraEntranceEntry>();
        public List<LetteraEntranceEntry> Entries => m_Entries;

        public LetteraEntranceEntry Find(string name)
        {
            foreach (LetteraEntranceEntry entry in m_Entries)
                if (entry != null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }
    }
}
