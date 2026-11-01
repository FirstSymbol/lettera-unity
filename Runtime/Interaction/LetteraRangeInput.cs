using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera
{
    /// <summary>The input of one text's ranges. It exists only for a text that has some, and lets through everything else.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class LetteraRangeInput : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        LetteraRangeRouter m_Router;
        GameObject m_Forwarded;

        static readonly List<RaycastResult> s_Results = new List<RaycastResult>();

        internal LetteraRangeRouter Router => m_Router;
        internal void Bind(LetteraRangeRouter router) => m_Router = router;

        // What lies behind this text under the pointer, for a route that shares its press.
        GameObject Behind(PointerEventData data)
        {
            if (EventSystem.current == null) return null;
            s_Results.Clear();
            EventSystem.current.RaycastAll(data, s_Results);
            bool past = false;
            foreach (RaycastResult result in s_Results)
            {
                if (result.gameObject == gameObject) past = true;
                else if (past && !result.gameObject.transform.IsChildOf(transform)) return result.gameObject;
            }
            return null;
        }

        bool HasSelectable => GetComponent<LetteraSelectable>() != null;

        public void OnPointerEnter(PointerEventData data) => m_Router?.PointerInside(data, true);
        public void OnPointerExit(PointerEventData data) => m_Router?.PointerInside(data, false);
        public void OnPointerMove(PointerEventData data) => m_Router?.PointerMoved(data);

        public void OnPointerDown(PointerEventData data)
        {
            if (m_Router == null || HasSelectable) return;
            bool taken = m_Router.Press(data);
            if (m_Router.Route == LetteraRoute.Observe)
            {
                GameObject behind = Behind(data);
                m_Forwarded = behind != null ? ExecuteEvents.ExecuteHierarchy(behind, data, ExecuteEvents.pointerDownHandler) : null;
                return;
            }
            if (taken || m_Router.Route == LetteraRoute.Normal) return;
            // Nothing here took it, so it goes where it would have gone without this text.
            Transform parent = transform.parent;
            m_Forwarded = parent != null ? ExecuteEvents.ExecuteHierarchy(parent.gameObject, data, ExecuteEvents.pointerDownHandler) : null;
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (m_Router == null || HasSelectable) return;
            m_Router.Release(data);
            if (m_Forwarded != null) ExecuteEvents.Execute(m_Forwarded, data, ExecuteEvents.pointerUpHandler);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (m_Router == null || HasSelectable) return;
            if (m_Forwarded != null) ExecuteEvents.Execute(m_Forwarded, data, ExecuteEvents.pointerClickHandler);
            m_Forwarded = null;
        }
    }
}
