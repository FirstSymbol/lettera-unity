using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraRevealUnit { Cluster, Word, Line, Paragraph }
    public enum LetteraEntranceKind { Cut, Fade, Slide, Scale, Spin, Flip, Skew, Stretch, Spiral, Pop, Drop, Rain, Burst, Domino, Swing, Wave, Shake, Glitch, Chaos, Tint }
    public enum LetteraEase { Linear, InQuad, OutQuad, InOutQuad, OutCubic, InOutCubic, OutBack, OutBounce }

    /// <summary>How a unit arrives. Whatever it does, at 1 it is at rest, which lets one entrance serve both directions.</summary>
    [Serializable]
    public class LetteraEntrance
    {
        public LetteraEntranceKind Kind = LetteraEntranceKind.Fade;
        [Min(0)] public float Duration = 0.25f;
        public LetteraEase Ease = LetteraEase.OutCubic;
        [Tooltip("Where in the unit's box it turns and scales.")]
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
        [Tooltip("In em, where the kind moves anything.")]
        public float Distance = 0.5f;
        [Tooltip("Turns or degrees, for the kinds that turn.")]
        public float Amount = 1;
        public Color From = Color.white;

        public static float Eased(LetteraEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case LetteraEase.InQuad: return t * t;
                case LetteraEase.OutQuad: return 1 - (1 - t) * (1 - t);
                case LetteraEase.InOutQuad: return t < 0.5f ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
                case LetteraEase.OutCubic: return 1 - (1 - t) * (1 - t) * (1 - t);
                case LetteraEase.InOutCubic: return t < 0.5f ? 4 * t * t * t : 1 - 4 * (1 - t) * (1 - t) * (1 - t);
                case LetteraEase.OutBack:
                    {
                        float u = t - 1;
                        return 1 + 2.70158f * u * u * u + 1.70158f * u * u;
                    }
                case LetteraEase.OutBounce:
                    {
                        float x = t;
                        if (x < 1 / 2.75f) return 7.5625f * x * x;
                        if (x < 2 / 2.75f) { x -= 1.5f / 2.75f; return 7.5625f * x * x + 0.75f; }
                        if (x < 2.5f / 2.75f) { x -= 2.25f / 2.75f; return 7.5625f * x * x + 0.9375f; }
                        x -= 2.625f / 2.75f;
                        return 7.5625f * x * x + 0.984375f;
                    }
                default: return t;
            }
        }

        /// <summary>The pose at a share of the entrance. Must be the identity at 1.</summary>
        public virtual void Pose(float value, in LetteraBodyBox box, float em, int index, int seed, ref LetteraGlyphPose pose)
        {
            float e = Eased(Ease, value), o = 1 - e;
            Vector2 pivot = box.At(Pivot.x, Pivot.y);
            if (value >= 1) return;
            switch (Kind)
            {
                case LetteraEntranceKind.Cut:
                    pose.Alpha *= value >= 1 ? 1 : 0;
                    break;
                case LetteraEntranceKind.Fade:
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Slide:
                    pose.Move(0, -o * Distance * em);
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Scale:
                    pose.Scale(e, e, pivot);
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Spin:
                    pose.Turn(o * Amount * 360, pivot);
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Flip:
                    pose.Scale(1, Mathf.Abs(Mathf.Cos(o * Mathf.PI * 0.5f)), pivot);
                    break;
                case LetteraEntranceKind.Skew:
                    pose.Skew(o * Mathf.Tan(30 * Mathf.Deg2Rad), 0, box.At(0.5f, 0));
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Stretch:
                    pose.Scale(1 + o * Amount, e, pivot);
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Spiral:
                    {
                        float angle = o * Mathf.PI * 2, radius = o * Distance * em * 2;
                        pose.Move(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                        pose.Alpha *= e;
                    }
                    break;
                case LetteraEntranceKind.Pop:
                    {
                        float pop = Eased(LetteraEase.OutBack, value);
                        pose.Scale(pop, pop, pivot);
                    }
                    break;
                case LetteraEntranceKind.Drop:
                    pose.Move(0, (1 - Eased(LetteraEase.OutBounce, value)) * Distance * 2.4f * em);
                    break;
                case LetteraEntranceKind.Rain:
                    {
                        float delay = LetteraMotionNoise.Unit(seed, index, 7) * 0.3f;
                        float rained = Mathf.Clamp01((value - delay) / (1 - delay));
                        pose.Move(0, (1 - Eased(LetteraEase.OutCubic, rained)) * Distance * 2.4f * em);
                        pose.Alpha *= rained;
                    }
                    break;
                case LetteraEntranceKind.Burst:
                    {
                        float angle = LetteraMotionNoise.Unit(seed, index, 11) * Mathf.PI * 2;
                        pose.Move(Mathf.Cos(angle) * o * Distance * 4 * em, Mathf.Sin(angle) * o * Distance * 4 * em);
                        pose.Alpha *= e;
                    }
                    break;
                case LetteraEntranceKind.Domino:
                    pose.Turn(o * 90, box.At(0, 0));
                    break;
                case LetteraEntranceKind.Swing:
                    pose.Turn(o * 40 * Mathf.Cos(e * Mathf.PI * 3), box.TopCenter);
                    break;
                case LetteraEntranceKind.Wave:
                    pose.Move(0, Mathf.Sin(e * Mathf.PI) * Distance * em);
                    pose.Alpha *= e;
                    break;
                case LetteraEntranceKind.Shake:
                    {
                        int k = Mathf.FloorToInt(value * 20);
                        pose.Move(o * 0.15f * em * LetteraMotionNoise.Signed(seed, index, k), o * 0.15f * em * LetteraMotionNoise.Signed(seed, index, k + 100));
                        pose.Alpha *= e;
                    }
                    break;
                case LetteraEntranceKind.Glitch:
                    {
                        int k = Mathf.FloorToInt(value * 12);
                        pose.Move(o * 0.1f * em * LetteraMotionNoise.Signed(seed, index, k), 0);
                        pose.Alpha *= LetteraMotionNoise.Unit(seed, index, k + 50) < 0.15f ? 0 : e;
                    }
                    break;
                case LetteraEntranceKind.Chaos:
                    {
                        pose.Move(o * em * LetteraMotionNoise.Signed(seed, index, 1), o * em * LetteraMotionNoise.Signed(seed, index, 2));
                        pose.Turn(o * 90 * LetteraMotionNoise.Signed(seed, index, 3), pivot);
                        float s = 1 + o * 0.5f * LetteraMotionNoise.Signed(seed, index, 4);
                        pose.Scale(s, s, pivot);
                        pose.Alpha *= e;
                    }
                    break;
                case LetteraEntranceKind.Tint:
                    pose.Tint *= Color.LerpUnclamped(From, Color.white, e);
                    break;
            }
        }

        public LetteraEntrance Clone() => (LetteraEntrance)MemberwiseClone();
    }

    /// <summary>A named way in, and an optional other way out.</summary>
    [Serializable]
    public class LetteraEntranceEntry
    {
        public string Name = "";
        [SerializeReference] public LetteraEntrance Enter = new LetteraEntrance();
        [SerializeReference] public LetteraEntrance Leave;
    }

    /// <summary>Named entrances to share between texts.</summary>
    [CreateAssetMenu(fileName = "Entrance Library", menuName = "Lettera/Entrance Library", order = 93)]
    public class LetteraEntranceLibrary : ScriptableObject
    {
        [SerializeField] List<LetteraEntranceEntry> m_Entries = new List<LetteraEntranceEntry>();
        public List<LetteraEntranceEntry> Entries => m_Entries;

        public LetteraEntranceEntry Find(string name)
        {
            foreach (LetteraEntranceEntry entry in m_Entries)
                if (entry != null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }
    }

    /// <summary>Shows the leading part of what it covers: typewriter text and its reverse. Tag slots: entrance, reach, unit.</summary>
    [Serializable]
    public class RevealEffect : LetteraEffect
    {
        [SerializeField] string m_Entrance = "";
        [Tooltip("The frontier: 100% is everything; a bare number counts units.")]
        [SerializeField] string m_Reach = "100%";
        [SerializeField] LetteraRevealUnit m_Unit;
        [Tooltip("False keeps the space of hidden text. True closes it up, as if it were not there.")]
        [SerializeField] bool m_Reflow;
        [Tooltip("False runs one frontier through all ranges, one after another. True fills each on its own.")]
        [SerializeField] bool m_Independent;
        [SerializeField] LetteraClock m_Clock = LetteraClock.Unscaled;
        [SerializeField] LetteraEntranceLibrary m_Library;
        [SerializeField] List<LetteraEntranceEntry> m_Entries = new List<LetteraEntranceEntry>();
        [SerializeField, Min(0)] float m_SoftUnits = 1;
        [SerializeField] int m_Seed;

        static readonly LetteraParamInfo[] s_Params =
        {
            new LetteraParamInfo("Entrance", 0, LetteraParamKind.String, DirtyLevel.Pose, true),
            new LetteraParamInfo("Reach", 1, LetteraParamKind.Length, DirtyLevel.Pose, true),
            new LetteraParamInfo("Unit", 2, LetteraParamKind.Enum, DirtyLevel.Parse, true),
        };

        [NonSerialized] internal int Version;
        [NonSerialized] LetteraEntrance m_Scratch;

        internal event Action Touched;
        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Params;

        /// <summary>Fires once for each unit going forward, with its logical span, for something like a typing sound.</summary>
        public event Action<int, int> UnitShown;

        /// <summary>Fires when every unit has reached where it goes.</summary>
        public event Action Settled;

        void Touch()
        {
            Version++;
            Touched?.Invoke();
        }

        public string Entrance { get => m_Entrance; set { m_Entrance = value; Touch(); } }
        public string Reach { get => m_Reach; set { m_Reach = value; Touch(); } }
        public LetteraRevealUnit Unit { get => m_Unit; set { m_Unit = value; Touch(); } }
        public bool Reflow { get => m_Reflow; set { m_Reflow = value; Touch(); } }
        public bool Independent { get => m_Independent; set { m_Independent = value; Touch(); } }
        public LetteraClock Clock { get => m_Clock; set { m_Clock = value; Touch(); } }
        public LetteraEntranceLibrary Library { get => m_Library; set { m_Library = value; Touch(); } }
        public List<LetteraEntranceEntry> Entries => m_Entries;
        public float SoftUnits { get => m_SoftUnits; set { m_SoftUnits = value; Touch(); } }
        public int Seed { get => m_Seed; set => m_Seed = value; }

        /// <summary>Sets the frontier: a share of all units, or a number of them.</summary>
        public void RevealTo(string reach) => Reach = reach;

        /// <summary>Every unit jumps to where it goes.</summary>
        public void SnapToTarget()
        {
            SnapRequested = true;
            Touch();
        }

        internal bool SnapRequested;

        /// <summary>Moves units by this much under a manual clock.</summary>
        public void Tick(float seconds)
        {
            ManualSeconds += seconds;
            Touch();
        }

        internal float ManualSeconds;

        internal void RaiseUnitShown(int start, int end) => UnitShown?.Invoke(start, end);
        internal void RaiseSettled() => Settled?.Invoke();

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            attributes.AddReveal(this, start, end, slots[0], slots[1], slots.Enum(2, m_Unit));
        }

        public override LetteraEffect Clone()
        {
            var copy = (RevealEffect)base.Clone();
            copy.m_Entries = new List<LetteraEntranceEntry>(m_Entries);
            return copy;
        }

        // A name in the effect's list, then in its library, then one of the built-in kinds.
        internal void Resolve(string name, out LetteraEntrance enter, out LetteraEntrance leave)
        {
            name = name ?? "";
            foreach (LetteraEntranceEntry entry in m_Entries)
                if (entry != null && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    enter = entry.Enter;
                    leave = entry.Leave;
                    return;
                }
            LetteraEntranceEntry library = m_Library != null ? m_Library.Find(name) : null;
            if (library != null)
            {
                enter = library.Enter;
                leave = library.Leave;
                return;
            }
            leave = null;
            if (m_Scratch == null || m_ScratchName != name)
            {
                m_ScratchName = name;
                m_Scratch = new LetteraEntrance();
                if (name.Length == 0) m_Scratch.Duration = 0;
                else if (Enum.TryParse(name, true, out LetteraEntranceKind kind)) m_Scratch.Kind = kind;
                else m_Scratch.Duration = 0;
                if (name.Length == 0) m_Scratch.Kind = LetteraEntranceKind.Cut;
            }
            enter = m_Scratch;
        }

        [NonSerialized] string m_ScratchName;
    }

    public enum LetteraMarqueeMode { Loop, BackAndForth }

    /// <summary>Scrolls a Canvas text sideways through its box, when it does not fit or when told to. Whole-text only; world text has no clip to scroll within.</summary>
    [Serializable]
    public class MarqueeEffect : LetteraEffect
    {
        [SerializeField] LetteraMarqueeMode m_Mode;
        [Tooltip("Between copies, in em unless written with px or %.")]
        [SerializeField] string m_Gap = "2em";
        [Tooltip("Per second, in em unless written with px. Negative runs the other way.")]
        [SerializeField] string m_Speed = "2em";
        [SerializeField, Min(0)] float m_Pause = 1;
        [SerializeField] bool m_EvenIfFits;
        [Tooltip("A soft edge at left and right, in em. 0 cuts hard.")]
        [SerializeField, Min(0)] float m_Fade = 1;
        [SerializeField] float m_Phase;
        [SerializeField] LetteraClock m_Clock;

        [NonSerialized] internal int Version;

        public LetteraMarqueeMode Mode { get => m_Mode; set { m_Mode = value; Version++; } }
        public string Gap { get => m_Gap; set { m_Gap = value; Version++; } }
        public string Speed { get => m_Speed; set { m_Speed = value; Version++; } }
        public float Pause { get => m_Pause; set { m_Pause = value; Version++; } }
        public bool EvenIfFits { get => m_EvenIfFits; set { m_EvenIfFits = value; Version++; } }
        public float Fade { get => m_Fade; set { m_Fade = value; Version++; } }
        public float Phase { get => m_Phase; set { m_Phase = value; Version++; } }
        public LetteraClock Clock { get => m_Clock; set { m_Clock = value; Version++; } }

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.SetMarquee(this);
    }
}
