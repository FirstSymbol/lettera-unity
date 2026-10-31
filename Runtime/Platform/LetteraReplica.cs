using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a field asks of a platform's control when it stands in its place.</summary>
    public sealed class LetteraReplicaRequest
    {
        public string Surface = "", ControlId = "", SurfaceData = "", Placeholder = "";
        public bool Wraps, AcceptsLineBreaks, ReadOnly;

        /// <summary>A multi-line control when the text wraps or takes line breaks, a single-line one when it does neither.</summary>
        public bool WantsMultilineControl => Wraps || AcceptsLineBreaks;
    }

    /// <summary>A host that may show its control. It says whether it did, since a surface can refuse.</summary>
    internal interface ILetteraReplicaHost
    {
        bool IsReplicaOpen { get; }
    }

    /// <summary>
    /// While a field has the keyboard, the platform's own control stands in its place: the browser's, or on a phone the system's. It runs through the same session, so every rule of the field still decides.
    /// Elsewhere this does nothing, so one prefab fits every target.
    /// </summary>
    [Serializable, LetteraRuleInfo(LetteraRuleGroup.Keyboard, "Replica Field")]
    public class ReplicaFieldRule : LetteraFieldRule
    {
        [SerializeField, Tooltip("The name of a registered surface. Empty is the plain control.")] string m_Surface = "";
        [SerializeField, Tooltip("The control's id, for a UI test tool: a DOM id on the web, a tag on Android, an accessibility identifier on iOS.")] string m_ControlId = "";
        [SerializeField, Tooltip("Goes to the surface as it is.")] string m_SurfaceData = "";

        public string Surface { get => m_Surface; set => m_Surface = value; }
        public string ControlId { get => m_ControlId; set => m_ControlId = value; }
        public string SurfaceData { get => m_SurfaceData; set => m_SurfaceData = value; }

        public override void RequestKeyboard(ref LetteraKeyboardTraits traits)
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.Replica)) return;
            LetteraText text = Field.GetComponent<LetteraText>();
            bool singleLine = false;
            string placeholder = "";
            foreach (LetteraFieldRule rule in Field.ActiveRulesForReplica)
            {
                if (!rule.Enabled) continue;
                singleLine |= rule is SingleLineRule;
                if (rule is HintDecorator hint && hint.Target != null) placeholder = hint.Target.Text;
            }
            traits.Replica = new LetteraReplicaRequest
            {
                Surface = m_Surface,
                ControlId = m_ControlId,
                SurfaceData = m_SurfaceData,
                Placeholder = placeholder,
                Wraps = text != null && text.WordWrap,
                AcceptsLineBreaks = !singleLine,
                ReadOnly = Field.ReadOnly,
            };
        }
    }
}
