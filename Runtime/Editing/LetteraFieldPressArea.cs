using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera
{
    /// <summary>Makes a field's whole frame count as the field: a press on the empty part of a text area places the caret, as people expect.</summary>
    [AddComponentMenu("Lettera/Field Press Area")]
    public class LetteraFieldPressArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] LetteraEditable m_Field;
        bool m_Forwarding;
        GameObject m_PassedTo;

        /// <summary>The field presses go to.</summary>
        public LetteraEditable Field { get => m_Field; set => m_Field = value; }

        void Forward<T>(PointerEventData data, ExecuteEvents.EventFunction<T> function) where T : IEventSystemHandler
        {
            if (m_Field == null) return;
            m_Forwarding = true;
            try
            {
                ExecuteEvents.Execute(m_Field.gameObject, data, function);
            }
            finally
            {
                m_Forwarding = false;
            }
        }

        // A field hands what is not its own to whatever is above it, which comes here first: it goes on up, or it would come straight back.
        GameObject Above<T>() where T : IEventSystemHandler
        {
            Transform parent = transform.parent;
            return parent != null ? ExecuteEvents.GetEventHandler<T>(parent.gameObject) : null;
        }

        public void OnPointerDown(PointerEventData data)
        {
            // The event system deselects whatever is not under the pointer, and this frame is the field's own.
            LetteraKeepFocus.MarkPressed();
            Forward(data, ExecuteEvents.pointerDownHandler);
        }

        public void OnPointerUp(PointerEventData data) => Forward(data, ExecuteEvents.pointerUpHandler);
        public void OnPointerClick(PointerEventData data) => Forward(data, ExecuteEvents.pointerClickHandler);

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (!m_Forwarding)
            {
                Forward(data, ExecuteEvents.initializePotentialDrag);
                return;
            }
            GameObject above = Above<IInitializePotentialDragHandler>();
            if (above != null) ExecuteEvents.Execute(above, data, ExecuteEvents.initializePotentialDrag);
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (!m_Forwarding)
            {
                m_PassedTo = null;
                Forward(data, ExecuteEvents.beginDragHandler);
                return;
            }
            m_PassedTo = Above<IDragHandler>();
            if (m_PassedTo != null) ExecuteEvents.Execute(m_PassedTo, data, ExecuteEvents.beginDragHandler);
        }

        public void OnDrag(PointerEventData data)
        {
            if (m_PassedTo != null) ExecuteEvents.Execute(m_PassedTo, data, ExecuteEvents.dragHandler);
            else Forward(data, ExecuteEvents.dragHandler);
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (m_PassedTo != null) ExecuteEvents.Execute(m_PassedTo, data, ExecuteEvents.endDragHandler);
            else Forward(data, ExecuteEvents.endDragHandler);
            m_PassedTo = null;
        }
    }
}
