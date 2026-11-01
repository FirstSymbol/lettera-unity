using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>One line of a dropdown's list. Works in a list of your own too.</summary>
    [AddComponentMenu("Lettera/Dropdown Row")]
    public class LetteraDropdownRow : MonoBehaviour
    {
        [SerializeField] LetteraText m_Label;
        [SerializeField] Image m_Icon;
        [SerializeField] GameObject m_Check;
        [SerializeField] Toggle m_Toggle;

        public LetteraText Label { get => m_Label; set => m_Label = value; }
        public Image Icon { get => m_Icon; set => m_Icon = value; }
        public GameObject Check { get => m_Check; set => m_Check = value; }
        public Toggle Toggle { get => m_Toggle; set => m_Toggle = value; }

        public void Bind(LetteraDropdownOption option, bool isCurrent)
        {
            if (m_Label != null) m_Label.Text = option.Text;
            if (m_Icon != null)
            {
                m_Icon.sprite = option.Image;
                m_Icon.enabled = option.Image != null;
            }
            if (m_Check != null) m_Check.SetActive(isCurrent);
            if (m_Toggle != null)
            {
                m_Toggle.SetIsOnWithoutNotify(isCurrent);
                m_Toggle.interactable = !option.Disabled;
            }
        }
    }
}
