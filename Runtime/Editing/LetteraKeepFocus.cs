using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Pressing this object does not end editing: a toolbar, a menu or a handle.</summary>
    [AddComponentMenu("Lettera/Keep Focus")]
    public class LetteraKeepFocus : MonoBehaviour, IPointerDownHandler
    {
        static double s_LastPress = -1000;

        // A press deselects first and reaches its target later in the same frame; the check runs after both.
        internal static bool PressedRecently => LetteraEditClock.Now - s_LastPress < 0.05;

        public void OnPointerDown(PointerEventData eventData) => s_LastPress = LetteraEditClock.Now;

        /// <summary>Tells the field to stay active as if this object had been pressed.</summary>
        public static void MarkPressed() => s_LastPress = LetteraEditClock.Now;
    }
}
