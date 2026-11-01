using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Runs an action on a click, on an object with no Button.</summary>
    [AddComponentMenu("")]
    internal sealed class LetteraMenuClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Action;
        public void OnPointerClick(PointerEventData eventData) => Action?.Invoke();
    }
}
