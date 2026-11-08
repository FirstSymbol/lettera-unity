using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>
    /// The default parameter of a style, as a form: the slots a bare tag fills when it leaves them empty. It writes the same string the raw
    /// field wrote, so presets, prefab overrides and values set from code all show up here.
    /// </summary>
    internal sealed class LtDefaultsForm : VisualElement
    {
        readonly SerializedProperty m_Parameter;
        readonly Func<LetteraEffect> m_GetEffect;
        LetteraEffect m_Effect;
        readonly Func<string> m_GetTag;
        string m_Tag;
        readonly LtTagForm m_Form;
        readonly Label m_Summary, m_Filled, m_Error, m_RawToggle;
        readonly VisualElement m_FormBox, m_RawBox;
        readonly LtGesture m_Gesture;
        bool m_Raw, m_ShowingRaw;

        public bool RawShown => m_ShowingRaw;
        public LtTagForm Form => m_Form;
        public string SummaryText => m_Summary.text;
        public string ErrorText => m_Error.text;
        public string FilledText => m_Filled.text;

        /// <summary>Whether an effect has a form to show here. The others keep the raw field.</summary>
        public static bool Wants(object effect) =>
            effect is LetteraEffect described && !(effect is CompositeEffect) && LetteraTagModel.HasSchema(described.Parameters);

        public LtDefaultsForm(SerializedProperty parameter, Func<LetteraEffect> effect, Func<string> tagName, Func<float> emPixels = null)
        {
            m_Parameter = parameter.Copy();
            m_GetEffect = effect;
            m_Effect = effect();
            m_GetTag = tagName;
            m_Tag = tagName();
            AddToClassList("lt-defaults");
            m_Gesture = new LtGesture(this);
            Add(LtUI.El("lt-defaults__head", LtUI.Text("Defaults", "lt-row__label lt-defaults__title"), LtUI.Text("Filled in when a tag leaves a slot empty", "lt-hint lt-defaults__sub")));
            m_Form = new LtTagForm(emPixels ?? (() => 0f));
            m_Form.SlotChanged += Write;
            m_Form.Holding += m_Gesture.Hold;
            m_Error = LtUI.Text("", "lt-note lt-defaults__error");
            m_FormBox = LtUI.El("lt-defaults__form", m_Form);
            m_RawBox = LtUI.El("lt-defaults__raw", m_Error, LtUI.Field("Raw", m_Parameter, "Parameters used when a tag gives none, in the tag's syntax."));
            m_Summary = LtUI.Text("", "lt-defaults__summary");
            m_Filled = LtUI.Text("", "lt-hint lt-defaults__filled");
            m_RawToggle = LtUI.Clickable("Raw", "lt-chip lt-chip--click lt-defaults__rawtoggle", "Edit the default as text.", () =>
            {
                m_Raw = !m_Raw;
                Refresh();
            });
            Add(m_FormBox);
            Add(m_RawBox);
            Add(LtUI.El("lt-defaults__foot", m_Summary, LtUI.El("lt-tagpanel__space"), m_RawToggle));
            Add(m_Filled);
            this.TrackPropertyValue(m_Parameter, _ => Refresh());
            // The card's effect fields supply what an empty slot shows; they are read again when the pointer comes back to the form.
            RegisterCallback<PointerEnterEvent>(_ => Refresh());
            Refresh();
        }

        static string Synthetic(string value) => "<x" + (string.IsNullOrEmpty(value) ? "" : "=" + value) + ">";

        // The value of the whole default is read as the value of a tag, with the tag's own reader.
        static LetteraTagSpan Read(string value)
        {
            string text = Synthetic(value);
            List<LetteraTagSpan> tags = LetteraTagScanner.Scan(text);
            if (tags.Count != 1 || tags[0].Start != 0 || tags[0].End != text.Length || tags[0].SelfClosing) return null;
            return tags[0];
        }

        static string ValueOf(string synthetic) => synthetic.Length <= 3 ? "" : synthetic.Substring(3, synthetic.Length - 4);

        public void Refresh()
        {
            m_Effect = m_GetEffect() ?? m_Effect;
            m_Tag = m_GetTag();
            string value = m_Parameter.hasMultipleDifferentValues ? "" : m_Parameter.stringValue ?? "";
            LetteraTagSpan tag = Read(value);
            bool valid = tag != null;
            bool showRaw = m_Raw || !valid;
            m_ShowingRaw = showRaw;
            m_FormBox.style.display = showRaw ? DisplayStyle.None : DisplayStyle.Flex;
            m_RawBox.style.display = showRaw ? DisplayStyle.Flex : DisplayStyle.None;
            m_Error.style.display = valid ? DisplayStyle.None : DisplayStyle.Flex;
            m_Error.text = valid ? "" : value.IndexOf('"') >= 0 || value.IndexOf('\'') >= 0 ? "Quote is not closed." : "This is not a valid tag value.";
            m_RawToggle.EnableInClassList("lt-chip--accent", showRaw);
            m_RawToggle.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;

            IReadOnlyList<LetteraParamInfo> described = m_Effect.Parameters;
            if (valid && !showRaw)
            {
                List<LetteraSlotRow> rows = LetteraTagModel.Rows(tag, described, m_Effect, null);
                int count = LetteraTagModel.SlotCount(described);
                List<string> extras = null;
                for (int i = count; i < tag.Slots.Count; i++)
                    (extras ?? (extras = new List<string>())).Add("Extra " + (i + 1) + ": " + (string.IsNullOrEmpty(tag.Slots[i].Value) ? "empty" : tag.Slots[i].Value));
                m_Form.Show(described, rows, extras);
            }
            // A bare tag means what the effect's own tag would say with these values.
            string filled = LetteraTagModel.BareMeans(LetteraWrapList.TagOf(m_Effect.GetType()) ?? m_Tag ?? "tag", value);
            m_Summary.text = valid ? (m_Tag != null ? "Bare " + "<" + m_Tag + ">" : "Whole text") + " = " + filled : "";
            m_Summary.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;
            int set = 0;
            if (tag != null)
                foreach (LetteraParamInfo info in described)
                    if (info.Slot >= 0 && !string.IsNullOrEmpty(tag.Slot(info.Slot)))
                        set++;
            m_Filled.text = valid ? "Slots a bare tag fills: " + set + " of " + LetteraTagModel.SlotCount(described) : "";
            m_Filled.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;
        }

        internal void Write(int slot, string token)
        {
            string value = m_Parameter.stringValue ?? "";
            string synthetic = Synthetic(value);
            LetteraTagSpan tag = Read(value);
            if (tag == null) return;
            LetteraTextEdit edit = LetteraTagWriter.SetSlot(synthetic, tag, slot, token);
            if (!edit.Ok || edit.Text == synthetic) return;
            string next = ValueOf(edit.Text);
            bool typing = m_Form.Editing && !string.IsNullOrEmpty(token);
            m_Gesture.Run(() =>
            {
                m_Parameter.serializedObject.Update();
                m_Parameter.stringValue = next;
                m_Parameter.serializedObject.ApplyModifiedProperties();
            }, !typing);
            Refresh();
        }
    }
}
