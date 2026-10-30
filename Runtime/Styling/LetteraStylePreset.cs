using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A reusable list of styles shared between texts.</summary>
    [CreateAssetMenu(menuName = "Lettera/Style Preset", fileName = "New Lettera Style Preset", order = 110)]
    public class LetteraStylePreset : ScriptableObject
    {
        /// <summary>Raised when a preset is edited; texts using it parse again.</summary>
        public static event Action<LetteraStylePreset> PresetChanged;

        [SerializeField] List<LetteraStyle> m_Styles = new List<LetteraStyle>();

        public List<LetteraStyle> Styles => m_Styles;

        /// <summary>Tells texts the list changed. Call after editing <see cref="Styles"/> from code.</summary>
        public void NotifyChanged() => PresetChanged?.Invoke(this);

        void OnValidate() => NotifyChanged();
    }
}
