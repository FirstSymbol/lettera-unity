using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Gives one effect parameter a field that curves, tracks and tweens can reach. An Animator cannot bind into an effect's list directly.</summary>
    [AddComponentMenu("Lettera/Parameter Link")]
    [ExecuteAlways]
    public class LetteraParamLink : MonoBehaviour, IMotionClient
    {
        [Tooltip("The text. Empty looks on this object, then up.")]
        [SerializeField] Component m_Text;
        [SerializeField] LetteraParamTarget m_Target = new LetteraParamTarget();
        [SerializeField] LetteraRangeSelect m_Select = new LetteraRangeSelect();
        [SerializeField] LetteraMix m_Mix;
        [SerializeField] int m_Priority;
        [SerializeField] float m_Value;
        [SerializeField] Color m_Color = Color.white;
        [SerializeField] Vector2 m_Vector;

        TextEngine m_Engine;
        object m_Hold;
        LetteraEffect m_Bound;
        LetteraParamInfo m_Info;
        float m_LastValue = float.NaN;
        Color m_LastColor;
        Vector2 m_LastVector;
        bool m_InHub;

        public LetteraParamTarget Target => m_Target;
        public LetteraRangeSelect Select => m_Select;
        public LetteraMix Mix { get => m_Mix; set { m_Mix = value; Unbind(); } }
        public int Priority { get => m_Priority; set { m_Priority = value; Unbind(); } }
        public float Value { get => m_Value; set { m_Value = value; Apply(); } }
        public Color ColorValue { get => m_Color; set { m_Color = value; Apply(); } }
        public Vector2 VectorValue { get => m_Vector; set { m_Vector = value; Apply(); } }

        /// <summary>Why it cannot run, or null.</summary>
        public string Problem { get; private set; }

        public Component Text { get => m_Text; set { m_Text = value; m_Engine = null; Unbind(); } }

        TextEngine Engine
        {
            get
            {
                if (m_Engine != null) return m_Engine;
                if (m_Text is ITextComponent assigned) return m_Engine = assigned.SharedEngine;
                var canvas = GetComponentInParent<LetteraText>();
                if (canvas != null) return m_Engine = ((ITextComponent)canvas).SharedEngine;
                var world = GetComponentInParent<LetteraWorldText>();
                return m_Engine = world != null ? ((ITextComponent)world).SharedEngine : null;
            }
        }

        void OnEnable()
        {
            m_LastValue = float.NaN;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        void OnDisable()
        {
            m_InHub = false;
            LetteraMotionHub.Remove(this);
            Unbind();
        }

        void OnDidApplyAnimationProperties() => Apply();

        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            if (!m_InHub) return false;
            Apply();
            return true;
        }

        void Unbind()
        {
            (m_Hold as IDisposable)?.Dispose();
            m_Hold = null;
            m_Bound = null;
            m_Info = null;
            m_LastValue = float.NaN;
        }

        void Apply()
        {
            TextEngine engine = Engine;
            if (engine == null)
            {
                Problem = "No text to drive.";
                return;
            }
            LetteraEffect effect = m_Target.Resolve(engine);
            if (effect != m_Bound || m_Hold == null) Bind(engine, effect);
            if (m_Hold == null) return;
            switch (m_Hold)
            {
                case LetteraHold<float> hold:
                    if (m_Value == m_LastValue) return;
                    m_LastValue = m_Value;
                    hold.Value = m_Info.Kind == LetteraParamKind.Int ? Mathf.Round(m_Value) : m_Value;
                    break;
                case LetteraHold<Color32> hold:
                    if (m_Color == m_LastColor && !float.IsNaN(m_LastValue)) return;
                    m_LastColor = m_Color;
                    m_LastValue = 0;
                    hold.Value = m_Color;
                    break;
                case LetteraHold<string> hold:
                    if (m_Value == m_LastValue && m_Vector == m_LastVector) return;
                    m_LastValue = m_Value;
                    m_LastVector = m_Vector;
                    hold.Value = LetteraSequencer.DiscreteText(m_Info, new LetteraValue { Number = m_Value, Vector = m_Vector });
                    break;
            }
        }

        void Bind(TextEngine engine, LetteraEffect effect)
        {
            Unbind();
            LetteraParamInfo info = LetteraParamTarget.Find(effect, m_Target.Parameter);
            if (effect == null || info == null || info.Slot < 0)
            {
                Problem = effect == null ? "The effect it points at is gone." : $"The effect has no parameter '{m_Target.Parameter}' a hold can reach.";
                return;
            }
            Problem = null;
            m_Bound = effect;
            m_Info = info;
            LetteraRangeQuery query = m_Select.Build(engine, effect);
            LetteraHoldComposition composition = m_Mix == LetteraMix.Add ? LetteraHoldComposition.Add : m_Mix == LetteraMix.Multiply ? LetteraHoldComposition.Multiply : LetteraHoldComposition.Replace;
            switch (info.Kind)
            {
                case LetteraParamKind.Float:
                case LetteraParamKind.Int:
                    m_Hold = query.Hold<float>(info.Slot, composition, info.Level, m_Priority);
                    break;
                case LetteraParamKind.Color:
                    m_Hold = query.Hold<Color32>(info.Slot, composition, info.Level, m_Priority);
                    break;
                default:
                    m_Hold = query.Hold<string>(info.Slot, LetteraHoldComposition.Replace, info.Level, m_Priority);
                    break;
            }
        }
    }
}
