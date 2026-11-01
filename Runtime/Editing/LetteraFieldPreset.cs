using System;
using System.Collections.Generic;
using System.Text;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>One rule list for many fields, such as chat, password or form. Each field works on its own copy.</summary>
    [CreateAssetMenu(fileName = "Field Preset", menuName = "Lettera/Field Preset", order = 92)]
    public class LetteraFieldPreset : ScriptableObject
    {
        [SerializeReference] List<LetteraFieldRule> m_Rules = new List<LetteraFieldRule>();

        internal static event Action<LetteraFieldPreset> PresetChanged;

        public List<LetteraFieldRule> Rules => m_Rules;

        void OnValidate() => PresetChanged?.Invoke(this);
        public void NotifyChanged() => PresetChanged?.Invoke(this);
    }
}
