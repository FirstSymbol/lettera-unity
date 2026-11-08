using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Glyphs jerk and split into color fringes in short bursts. Slots: split, rate, chance, jerk.</summary>
    [Serializable]
    public class GlitchMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Split = 0.04f, m_Rate = 10;
        [SerializeField, Range(0, 1)] float m_Chance = 0.15f;
        [SerializeField] float m_Jerk = 0.03f;
        [SerializeField] Color32 m_FringeA = new Color32(255, 0, 85, 255), m_FringeB = new Color32(0, 229, 255, 255);

        static readonly LetteraParamInfo[] s_Params =
        {
            LetteraMotionSlots.Em("Split",0,"How far the colour fringes split apart, in em.","m_Split","0.06",true),
            LetteraMotionSlots.Rate(1,"m_Rate",hint:"How many times a second a new burst may start.",example:"12"),
            LetteraMotionSlots.Number("Chance",2,"How likely a letter glitches at each burst, 0 never to 1 always.","m_Chance","0.3",true,0,1,0.05f,2),
            LetteraMotionSlots.Em("Jerk",3,"How far a glitching letter jumps sideways, in em.","m_Jerk","0.05"),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Split { get => m_Split; set { m_Split = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public float Chance { get => m_Chance; set { m_Chance = value; Version++; } }
        public float Jerk { get => m_Jerk; set { m_Jerk = value; Version++; } }
        public Color32 FringeA { get => m_FringeA; set => m_FringeA = value; }
        public Color32 FringeB { get => m_FringeB; set => m_FringeB = value; }

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Split);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Chance);
            v.V3 = Slot(s, 3, m_Jerk);
        }

        internal static bool Burst(int seed, int index, int k, float chance) => LetteraMotionNoise.Unit(seed, index, k * 4) < chance;

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p)
        {
            int k = Mathf.FloorToInt(c.T * c.Values.V1);
            if (!Burst(c.Seed, c.Index, k, c.Values.V2)) return;
            p.Move(c.Values.V3 * c.Em * LetteraMotionNoise.Signed(c.Seed, c.Index, k * 4 + 1), 0);
            if (LetteraMotionNoise.Unit(c.Seed, c.Index, k * 4 + 2) < 0.1f) p.Alpha = 0;
        }

        public override float MaxReachEm(in LetteraMotionValues v) => Mathf.Abs(v.V0) + Mathf.Abs(v.V3);

        internal override void WriteStreams(TextEngine engine, in TextEngine.MotionSpan span, float t) => engine.WriteFringes(this, span, t);
    }

    /// <summary>Symbols that settle into the real text, in reading order. Layout, indices and copied text stay the real ones. Slots: seconds, rate.</summary>
    [Serializable]
    public class ScrambleMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Seconds = 1, m_Rate = 16;
        [SerializeField] string m_Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789#%&*+=?";
        [Tooltip("Under a manual clock, or with Seconds at 0: how much has settled, 0 to 1.")]
        [SerializeField, Range(0, 1)] float m_Progress;

        static readonly LetteraParamInfo[] s_Params =
        {
            LetteraMotionSlots.Number("Seconds",0,"How long the text takes to settle.","m_Seconds","2",true,0,step:0.1f,decimals:2,unit:"s"),
            LetteraMotionSlots.Number("Rate",1,"How many times a second the random symbols change.","m_Rate","16",false,0,step:1,decimals:1,unit:"Hz"),
            LetteraMotionSlots.Number("Progress",2,"How much has settled, 0 to 1. Used under a manual clock, or when Seconds is 0.","m_Progress","0.5",true,0,1,0.05f,2),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Seconds { get => m_Seconds; set { m_Seconds = value; Version++; } }
        public float Rate { get => m_Rate; set { m_Rate = value; Version++; } }
        public string Alphabet { get => m_Alphabet; set { m_Alphabet = value; Version++; } }
        public float Progress { get => m_Progress; set { m_Progress = value; Version++; } }

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Seconds);
            v.V1 = Slot(s, 1, m_Rate);
            v.V2 = Slot(s, 2, m_Progress);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p) { }
        public override float MaxReachEm(in LetteraMotionValues v) => 0;

        internal float SettledAt(in LetteraMotionValues v, float t) => Clock == LetteraClock.Manual || v.V0 <= 0 ? v.V2 : Mathf.Clamp01(t / v.V0);

        internal override void ReserveQuads(TextEngine engine, in TextEngine.MotionSpan span) => engine.PrepareSymbols(span, m_Alphabet);

        internal override void WriteStreams(TextEngine engine, in TextEngine.MotionSpan span, float t) => engine.WriteScramble(this, span, t);
    }

    /// <summary>Characters turn on a wheel and settle on the real value, which suits counters. Slots: seconds, start, lag.</summary>
    [Serializable]
    public class RollMotion : LetteraMotionEffect
    {
        [SerializeField] float m_Seconds = 1.2f, m_Start = 20, m_Lag = 2;
        [SerializeField] string m_Wheel = "0123456789";
        [Tooltip("Under a manual clock: how many places the wheels have left to turn.")]
        [SerializeField] float m_Roll;

        static readonly LetteraParamInfo[] s_Params =
        {
            LetteraMotionSlots.Number("Seconds",0,"How long the wheels take to stop.","m_Seconds","2",true,0,step:0.1f,decimals:2,unit:"s"),
            LetteraMotionSlots.Number("Start",1,"How many places each wheel starts away from its final symbol.","m_Start","30",true,step:1,decimals:1,unit:"places"),
            LetteraMotionSlots.Number("Lag",2,"How many fewer places each character along the order has to turn, so the wheels stop one after another.","m_Lag","2",false,step:0.5f,decimals:1,unit:"places"),
            LetteraMotionSlots.Number("Roll",3,"Places the wheels still have to turn. Used under a manual clock, or when Seconds is 0.","m_Roll","10",step:1,decimals:1,unit:"places"),
        };

        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;
        public float Seconds { get => m_Seconds; set { m_Seconds = value; Version++; } }
        public float Start { get => m_Start; set { m_Start = value; Version++; } }
        public float Lag { get => m_Lag; set { m_Lag = value; Version++; } }
        public string Wheel { get => m_Wheel; set { m_Wheel = value; Version++; } }
        public float Roll { get => m_Roll; set { m_Roll = value; Version++; } }

        public override void Resolve(in LetteraSlots s, ref LetteraMotionValues v)
        {
            v.V0 = Slot(s, 0, m_Seconds);
            v.V1 = Slot(s, 1, m_Start);
            v.V2 = Slot(s, 2, m_Lag);
            v.V3 = Slot(s, 3, m_Roll);
        }

        public override void PoseBody(in LetteraMotionContext c, ref LetteraGlyphPose p) { }
        public override float MaxReachEm(in LetteraMotionValues v) => 0;

        internal float RollAt(in LetteraMotionValues v, float t)
        {
            if (Clock == LetteraClock.Manual || v.V0 <= 0) return v.V3;
            float x = Mathf.Clamp01(t / v.V0);
            return v.V1 * (1 - (1 - (1 - x) * (1 - x) * (1 - x)));
        }

        internal override void ReserveQuads(TextEngine engine, in TextEngine.MotionSpan span) => engine.PrepareSymbols(span, m_Wheel);

        internal override void WriteStreams(TextEngine engine, in TextEngine.MotionSpan span, float t) => engine.WriteRoll(this, span, t);
    }
}
