namespace FirstSymbol.Lettera
{
    /// <summary>What a solid text reads to pose glyphs in depth: its motions' bodies and phases, and its reveals' values. Nothing else calls these.</summary>
    internal sealed partial class TextEngine
    {
        internal int MotionSpanCountForSolids => m_Aside.MotionSpans.Count;

        /// <summary>A motion over a range, as the last pose pass resolved it, or null when it rests now.</summary>
        internal LetteraMotionEffect MotionForSolids(int span, out LetteraMotionValues values, out float time, out int linkStart, out int linkCount, out int bodyCount)
        {
            MotionSpan s = m_Aside.MotionSpans[span];
            values = s.Effective;
            linkStart = s.LinkStart;
            linkCount = s.LinkCount;
            bodyCount = s.BodyCount;
            time = 0;
            if (LetteraAccessibility.ReducedMotion && s.Effect.ReducedMotionPolicy == LetteraReducedMotion.Rest) return null;
            if (s.PhaseIndex < 0 || s.PhaseIndex >= m_Aside.Phases.Count) return null;
            time = (float)Wrap(m_Aside.Phases[s.PhaseIndex].Time, s.Effect, s.Effective);
            return s.Effect;
        }

        /// <summary>One glyph a motion moves, with the number of its body within its span and the body's box.</summary>
        internal void MotionLinkForSolids(int span, int link, out int glyph, out int body, out LetteraBodyBox box, out float em)
        {
            PoseLink l = m_Aside.PoseLinks[link];
            glyph = l.Glyph;
            body = l.Body - m_Aside.MotionSpans[span].BodyStart;
            PoseBody b = m_Aside.PoseBodies[l.Body];
            box = b.Box;
            em = b.Em;
        }

        internal bool RevealsForSolids => m_Aside.RevealSpans.Count > 0;

        /// <summary>A glyph on its way in or out by an entrance that moves in depth, with how far along it is.</summary>
        internal bool RevealInDepthForSolids(int glyph, out LetteraEntrance entrance, out float value, out LetteraBodyBox box, out float em, out int unit, out int seed)
        {
            entrance = null;
            value = 1;
            box = default;
            em = 0;
            unit = 0;
            seed = 0;
            if (m_Aside.RevealSpans.Count == 0 || glyph < 0 || glyph >= m_PlacedCount) return false;
            ref PlacedGlyph g = ref m_Placed[glyph];
            if (g.Cluster < 0 || g.Cluster >= m_Aside.CpReveal.Length) return false;
            int span = m_Aside.CpReveal[g.Cluster];
            if (span < 0) return false;
            float v = m_Aside.CpVisible[g.Cluster];
            if (v >= 1 || v <= 0) return false;
            RevealSpan s = m_Aside.RevealSpans[span];
            s.Effect.Resolve(s.Entrance, out LetteraEntrance enter, out LetteraEntrance leave);
            entrance = leave != null && m_Aside.CpTarget[g.Cluster] < v ? leave : enter;
            if (entrance == null || !entrance.InDepth) return false;
            int key = BodyKey(g.Cluster);
            if (key >= m_Aside.RevealBodies.Count) return false;
            RevealBody body = m_Aside.RevealBodies[key];
            value = v;
            box = body.Box;
            em = body.Em;
            unit = m_Aside.CpUnit[g.Cluster];
            seed = s.Effect.Seed;
            return true;
        }
    }
}
