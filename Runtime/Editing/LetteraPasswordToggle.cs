using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Flips a password field between its dots and its value. On a Button or a Toggle.</summary>
    [AddComponentMenu("Lettera/Password Toggle")]
    public class LetteraPasswordToggle : MonoBehaviour
    {
        [SerializeField] LetteraEditable m_Field;

        public LetteraEditable Field { get => m_Field; set => m_Field = value; }

        void OnEnable()
        {
            if (GetComponent<LetteraKeepFocus>() == null) gameObject.AddComponent<LetteraKeepFocus>();
            var button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(Flip);
        }

        void OnDisable()
        {
            var button = GetComponent<Button>();
            if (button != null) button.onClick.RemoveListener(Flip);
        }

        /// <summary>Shows the value if it was hidden, and hides it if it showed.</summary>
        public void Flip()
        {
            PasswordRule rule = m_Field != null ? m_Field.GetRule<PasswordRule>() : null;
            if (rule != null) rule.ShowValue = !rule.ShowValue;
        }
    }
}
