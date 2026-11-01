using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Puts a text's ranges in Unity's UI navigation, so arrows and gamepads step through them and leave at the ends.</summary>
    [AddComponentMenu("Lettera/Range Navigator")]
    public class LetteraRangeNavigator : Selectable, ISubmitHandler
    {
        Component m_TextComponent;

        LetteraRangeRouter Router
        {
            get
            {
                if (m_TextComponent == null)
                {
                    m_TextComponent = GetComponent<LetteraText>();
                    if (m_TextComponent == null) m_TextComponent = GetComponent<LetteraWorldText>();
                }
                return m_TextComponent != null ? LetteraRangeRouter.Of(m_TextComponent) : null;
            }
        }

        public override void OnMove(AxisEventData eventData)
        {
            bool forward = eventData.moveDir == MoveDirection.Right || eventData.moveDir == MoveDirection.Down;
            if (Router != null && Router.StepFocus(forward))
            {
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (Router != null && Router.ActivateFocused()) eventData.Use();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            Router?.ClearFocus();
            base.OnDeselect(eventData);
        }
    }
}
