using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera
{
    /// <summary>A name for a group of ranges to listen to together.</summary>
    [CreateAssetMenu(fileName = "Range Topic", menuName = "Lettera/Range Topic", order = 94)]
    public partial class LetteraRangeTopic : ScriptableObject
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;
    }
}
