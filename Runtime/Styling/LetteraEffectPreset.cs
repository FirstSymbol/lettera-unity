using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A reusable effect, usually a composite, as an asset. Each user runs its own copy; editing the asset reaches all of them.</summary>
    [CreateAssetMenu(menuName = "Lettera/Effect Preset", fileName = "Lettera Effect Preset")]
    public partial class LetteraEffectPreset : ScriptableObject
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

        /// <summary>Raised when a preset changes, so texts using it rebuild.</summary>
        public static event Action<LetteraEffectPreset> PresetChanged;

        [SerializeReference] LetteraEffect m_Root;

        internal int Version { get; private set; }

        public LetteraEffect Root { get => m_Root; set { m_Root = value; NotifyChanged(); } }

        /// <summary>Call after editing the root from a script.</summary>
        public void NotifyChanged()
        {
            Version++;
            PresetChanged?.Invoke(this);
        }

        void OnValidate() => NotifyChanged();
    }
}
