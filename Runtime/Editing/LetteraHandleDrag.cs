using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Turns a drag on a handle into a move of a selection's end, keeping where it was grabbed.</summary>
    [AddComponentMenu("")]
    internal sealed class LetteraHandleDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        internal enum Kind { Start, End, Caret }

        public LetteraSelectable Owner;
        public Kind Which;
        Vector2 m_GrabOffset;

        public void OnPointerDown(PointerEventData data)
        {
            if (Owner == null) return;
            Camera camera = Owner.EventCameraGuess();
            Rect caret = Owner.EndpointOnScreen(Which, camera);
            m_GrabOffset = new Vector2(caret.x, caret.yMin + caret.height * 0.5f) - data.position;
        }

        public void OnBeginDrag(PointerEventData data) => Owner?.BeginHandleDrag();

        public void OnDrag(PointerEventData data)
        {
            if (Owner == null) return;
            Owner.DragHandle(Which, data.position + m_GrabOffset, data.position);
        }

        public void OnEndDrag(PointerEventData data) => Owner?.EndHandleDrag();
    }
}
