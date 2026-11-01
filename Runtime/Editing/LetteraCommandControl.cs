using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Binds a Button or a Toggle to a command, for a toolbar. Without a target it acts on the field that has the keyboard.</summary>
    [AddComponentMenu("Lettera/Command Control")]
    public class LetteraCommandControl : MonoBehaviour
    {
        [SerializeReference] LetteraEditCommand m_Command;
        [SerializeField] LetteraSelectable m_Target;

        Button m_Button;
        Toggle m_Toggle;
        LetteraSelectable m_Listening;

        public LetteraEditCommand Command { get => m_Command; set { m_Command = value; Refresh(); } }
        public LetteraSelectable Target { get => m_Target; set { m_Target = value; Refresh(); } }

        LetteraSelectable Effective => m_Target != null ? m_Target : LetteraSelectable.Current;

        void OnEnable()
        {
            m_Button = GetComponent<Button>();
            m_Toggle = GetComponent<Toggle>();
            if (m_Button != null) m_Button.onClick.AddListener(Run);
            if (m_Toggle != null) m_Toggle.onValueChanged.AddListener(OnToggle);
            if (GetComponent<LetteraKeepFocus>() == null) gameObject.AddComponent<LetteraKeepFocus>();
            LetteraClipboard.Written += Refresh;
            LetteraWebGestures.Arm(m_Command, transform as RectTransform);
            Refresh();
        }

        void OnDisable()
        {
            if (m_Button != null) m_Button.onClick.RemoveListener(Run);
            if (m_Toggle != null) m_Toggle.onValueChanged.RemoveListener(OnToggle);
            LetteraClipboard.Written -= Refresh;
            LetteraWebGestures.Disarm(transform as RectTransform);
            Listen(null);
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus) Refresh();
        }

        void LateUpdate()
        {
            // The field that has the keyboard changes without telling a toolbar.
            if (Effective != m_Listening) Refresh();
        }

        void OnToggle(bool on) => Run();

        internal void Run()
        {
            LetteraSelectable target = Effective;
            if (m_Command == null || target == null) return;
            var context = new LetteraCommandContext(target);
            if (m_Command.CanRun(context)) m_Command.Execute(context);
            Refresh();
        }

        void Listen(LetteraSelectable target)
        {
            if (m_Listening == target) return;
            if (m_Listening != null)
            {
                m_Listening.SelectionChanged -= OnSelection;
                if (m_Listening.Editable != null) m_Listening.Editable.CaretStylesChanged -= OnStyles;
            }
            m_Listening = target;
            if (target != null)
            {
                target.SelectionChanged += OnSelection;
                if (target.Editable != null) target.Editable.CaretStylesChanged += OnStyles;
            }
        }

        void OnSelection(LetteraSelectionChange change) => Refresh();
        void OnStyles(LetteraCaretStyles styles) => Refresh();

        void Refresh()
        {
            LetteraSelectable target = Effective;
            Listen(target);
            bool usable = false, on = false;
            if (m_Command != null && target != null)
            {
                var context = new LetteraCommandContext(target);
                usable = m_Command.CanRun(context);
                on = m_Command.IsOn(context);
            }
            if (m_Button != null) m_Button.interactable = usable;
            if (m_Toggle != null)
            {
                m_Toggle.interactable = usable;
                m_Toggle.SetIsOnWithoutNotify(on);
            }
        }
    }
}
