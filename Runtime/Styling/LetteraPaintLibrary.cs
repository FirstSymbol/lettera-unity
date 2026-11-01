using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A shared list of named paints.</summary>
    [CreateAssetMenu(menuName = "Lettera/Paint Library", fileName = "Lettera Paint Library")]
    public class LetteraPaintLibrary : ScriptableObject
    {
        [SerializeField] List<LetteraSwatch> m_Swatches = new List<LetteraSwatch>();

        public List<LetteraSwatch> Swatches => m_Swatches;

        public LetteraSwatch Find(string name) => LetteraPaintCatalogs.Find(m_Swatches, name);
    }
}
