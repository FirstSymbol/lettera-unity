using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // The pose pass rewrites vertex data from what the last mesh left, without running any pass of the rebuild.
    // Only a text with something moving keeps the arrays this needs.
    internal sealed partial class TextEngine
    {
        internal struct MotionSpan
        {
            public LetteraMotionEffect Effect;
            public int Start, End, Range, LinkStart, LinkCount, BodyStart, BodyCount;
            public int PhaseIndex, SeenVersion;
            public LetteraSlots Slots;
            public LetteraMotionValues Base, Effective;
        }

        struct PoseLink
        {
            public int Glyph, Body;
        }

        struct PoseBody
        {
            public LetteraBodyBox Box;
            public float Em;
        }

        // Each span's phase stays while the effect stays, across parses.
        struct MotionPhase
        {
            public LetteraMotionEffect Effect;
            public double Time;
        }

        readonly List<MotionSpan> m_MotionSpans = new List<MotionSpan>();
        readonly List<MotionPhase> m_Phases = new List<MotionPhase>();
        readonly List<PoseLink> m_PoseLinks = new List<PoseLink>();
        readonly List<PoseBody> m_PoseBodies = new List<PoseBody>();
        LetteraGlyphPose[] m_GlyphPoses = new LetteraGlyphPose[0];
        LetteraGlyphPose[] m_BodyPoses = new LetteraGlyphPose[0];
        Vector3[] m_RestVertices = new Vector3[0];
        Color32[] m_RestColors = new Color32[0];
        int[] m_QuadOwner = new int[0];
        int[] m_CpBody = new int[0];
        int m_CurrentOwner = -1;
        bool m_WantsPose, m_InHub, m_PoseDirty;

        /// <summary>A pass wrote vertex data since the last upload. The bits say which streams.</summary>
        public int PoseStreams { get; private set; }
        public const int StreamPositions = 1, StreamColors = 2, StreamUv0 = 4, StreamUv1 = 8, StreamUv3 = 16;

        /// <summary>How many pose passes ran.</summary>
        public int PoseCount { get; private set; }

        public bool HasPoseConsumers => HasMoving || HasSpoilers;

        // What needs a tick. A spoiler at rest does not: it joins only while a cover fades.
        bool HasMoving => m_MotionSpans.Count > 0 || HasRevealOrMarquee || m_PoseHooks.Count > 0 || m_CoverFading;

        public void MarkPoseDirty() => m_PoseDirty = true;

        /// <summary>Takes what the text's effects asked for, after they ran.</summary>
        void CollectMotions()
        {
            m_RevealsCollected = false;
            m_MotionSpans.Clear();
            List<LetteraAttributes.MotionRequest> requests = m_Attributes.MotionRequests;
            for (int r = 0; r < requests.Count; r++)
            {
                LetteraAttributes.MotionRequest request = requests[r];
                var span = new MotionSpan { Effect = request.Effect, Start = request.Start, End = request.End, Range = request.Range, Slots = request.Slots, SeenVersion = request.Effect.Version };
                request.Effect.Resolve(request.Slots, ref span.Base);
                span.Effective = span.Base;
                span.PhaseIndex = PhaseOf(request.Effect);
                m_MotionSpans.Add(span);
            }
            RemoveUnusedPhases();
            UpdateHubMembership();
        }

        int PhaseOf(LetteraMotionEffect effect)
        {
            for (int i = 0; i < m_Phases.Count; i++)
                if (m_Phases[i].Effect == effect)
                    return i;
            m_Phases.Add(new MotionPhase { Effect = effect, Time = effect.Phase });
            return m_Phases.Count - 1;
        }

        void RemoveUnusedPhases()
        {
            if (m_Phases.Count <= m_MotionSpans.Count) return;
            // Dropping one means renumbering what uses them, which is rare enough to do by hand.
            for (int i = m_Phases.Count - 1; i >= 0; i--)
            {
                bool used = false;
                for (int s = 0; s < m_MotionSpans.Count; s++) used |= m_MotionSpans[s].Effect == m_Phases[i].Effect;
                if (used) continue;
                m_Phases.RemoveAt(i);
                for (int s = 0; s < m_MotionSpans.Count; s++)
                {
                    MotionSpan span = m_MotionSpans[s];
                    if (span.PhaseIndex > i)
                    {
                        span.PhaseIndex--;
                        m_MotionSpans[s] = span;
                    }
                }
            }
        }

        void UpdateHubMembership()
        {
            bool wants = HasMoving && !m_Disabled;
            if (wants == m_InHub) return;
            m_InHub = wants;
            if (wants) LetteraMotionHub.Add(this);
            else LetteraMotionHub.Remove(this);
        }

        bool m_Disabled;

        /// <summary>The component went away or came back.</summary>
        public void SetEnabledForMotion(bool enabled)
        {
            m_Disabled = !enabled;
            UpdateHubMembership();
        }

        // After a mesh: the rest state, and who owns what.
        void PrepareRestState()
        {
            m_WantsPose = true;
            EnsureExact(ref m_RestVertices, VertexCount);
            EnsureExact(ref m_RestColors, VertexCount);
            Array.Copy(Vertices, m_RestVertices, VertexCount);
            Array.Copy(Colors, m_RestColors, VertexCount);
            CopyRestStreams();
            EnsureExact(ref m_GlyphPoses, m_PlacedCount);
            BuildBodies();
        }

        bool m_RevealsCollected;

        // Reveals number units by line, so they wait for what the shaping pass found.
        void CollectRevealsOnce()
        {
            if (m_RevealsCollected) return;
            m_RevealsCollected = true;
            CollectReveals();
        }

        void CopyRestStreams() => CollapseExtraQuads();

        static void EnsureExact<T>(ref T[] array, int size)
        {
            if (array.Length < size) Array.Resize(ref array, Math.Max(size, array.Length * 2));
        }

        void EnsureOwnerSize(int quads)
        {
            if (m_QuadOwner.Length < quads) Array.Resize(ref m_QuadOwner, Math.Max(quads, Math.Max(m_QuadOwner.Length * 2, 64)));
        }

        // Which body each glyph of each span belongs to, in reading order.
        void BuildBodies()
        {
            m_PoseLinks.Clear();
            m_PoseBodies.Clear();
            EnsureExact(ref m_CpBody, m_TextLength + 1);
            for (int s = 0; s < m_MotionSpans.Count; s++)
            {
                MotionSpan span = m_MotionSpans[s];
                span.BodyStart = m_PoseBodies.Count;
                span.LinkStart = m_PoseLinks.Count;
                span.BodyCount = NumberBodies(span);
                for (int b = 0; b < span.BodyCount; b++) m_PoseBodies.Add(new PoseBody { Box = LetteraBodyBox.Empty });
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    ref PlacedGlyph g = ref m_Placed[i];
                    if (g.Cluster < span.Start || g.Cluster >= span.End) continue;
                    int body = m_CpBody[g.Cluster];
                    if (body < 0) continue;
                    m_PoseLinks.Add(new PoseLink { Glyph = i, Body = span.BodyStart + body });
                    PoseBody box = m_PoseBodies[span.BodyStart + body];
                    LetteraLine line = m_Lines[Math.Min(g.Line, m_Lines.Count - 1)];
                    float bottom = g.Y - line.Descent * 0.8f, top = g.Y + line.Ascent * 0.8f;
                    box.Box.Add(g.X, bottom, g.X + g.Advance, top);
                    if (g.Size > box.Em) box.Em = g.Size;
                    m_PoseBodies[span.BodyStart + body] = box;
                }
                span.LinkCount = m_PoseLinks.Count - span.LinkStart;
                m_MotionSpans[s] = span;
            }
            EnsureExact(ref m_BodyPoses, m_PoseBodies.Count);
        }

        // Fills the body number of each codepoint, -1 for what does not count. Returns how many bodies there are.
        int NumberBodies(in MotionSpan span)
        {
            int count = 0;
            int end = Math.Min(span.End, m_TextLength);
            switch (span.Effect.Unit)
            {
                case LetteraMotionUnit.Range:
                    for (int c = span.Start; c < end; c++) m_CpBody[c] = 0;
                    return end > span.Start ? 1 : 0;
                case LetteraMotionUnit.Line:
                {
                    int lastLine = -1;
                    for (int c = span.Start; c < end; c++)
                    {
                        int line = LineOf(c, LetteraCaretBias.Forward);
                        if (line != lastLine)
                        {
                            lastLine = line;
                            count++;
                        }
                        m_CpBody[c] = count - 1;
                    }
                    return count;
                }
                case LetteraMotionUnit.Word:
                {
                    var text = new ReadOnlySpan<int>(m_Text, 0, m_TextLength);
                    int c = span.Start;
                    while (c < end)
                    {
                        LetteraTextNavigator.WordAt(text, c, out _, out int wordEnd);
                        wordEnd = Math.Min(Math.Max(wordEnd, c + 1), end);
                        bool space = LetteraUnicode.IsWhiteSpace(text[c]) || LetteraTextNavigator.IsParagraphSeparator(text[c]);
                        for (int k = c; k < wordEnd; k++) m_CpBody[k] = space ? -1 : count;
                        if (!space) count++;
                        c = wordEnd;
                    }
                    return count;
                }
                default:
                {
                    int current = -1;
                    for (int c = span.Start; c < end; c++)
                    {
                        if (m_Boundaries[c] || current < 0)
                        {
                            bool space = LetteraUnicode.IsWhiteSpace(m_Text[c]);
                            if (!space) count++;
                            current = space ? -1 : count - 1;
                            m_CpBody[c] = current;
                            if (space) current = -2;
                            continue;
                        }
                        m_CpBody[c] = current < 0 ? -1 : current;
                    }
                    return count;
                }
            }
        }

        /// <summary>Advances the clocks, and poses what changed. A text with a pending rebuild only advances: the mesh poses itself when built.</summary>
        public void TickPose(float scaled, float unscaled)
        {
            if (!m_Host.IsDrawn) return;
            bool moved = false;
            bool reduced = LetteraAccessibility.ReducedMotion;
            for (int p = 0; p < m_Phases.Count; p++)
            {
                MotionPhase phase = m_Phases[p];
                LetteraMotionEffect effect = phase.Effect;
                if (effect.Clock == LetteraClock.Manual)
                {
                    if (phase.Time != effect.Phase)
                    {
                        phase.Time = effect.Phase;
                        m_Phases[p] = phase;
                        moved = true;
                    }
                    continue;
                }
                if (reduced && effect.ReducedMotionPolicy == LetteraReducedMotion.Rest) continue;
                float step = effect.Clock == LetteraClock.Scaled ? scaled : unscaled;
                if (step == 0) continue;
                phase.Time += step;
                m_Phases[p] = phase;
                moved = true;
            }
            moved |= TickReveal(unscaled, scaled);
            moved |= TickMarquee(scaled, unscaled);
            moved |= TickCovers(unscaled);
            moved |= m_PoseHooks.Count > 0;
            for (int s = 0; s < m_MotionSpans.Count && !moved; s++) moved = m_MotionSpans[s].Effect.Version != m_MotionSpans[s].SeenVersion;
            if (!moved && !m_PoseDirty) return;
            m_PoseDirty = false;
            if (m_Dirty >= DirtyLevel.Mesh || !m_WantsPose || VertexCount == 0) return;
            RunPosePass();
            m_Host.PoseChanged();
        }

        // Pose-level holds compose over the values the cascade gave, with no reapply.
        void ResolveEffective(ref MotionSpan span)
        {
            if (span.Effect.Version != span.SeenVersion)
            {
                span.SeenVersion = span.Effect.Version;
                span.Base = default;
                span.Effect.Resolve(span.Slots, ref span.Base);
            }
            span.Effective = span.Base;
            if (m_Holds.Count == 0) return;
            for (int h = 0; h < m_Holds.Count; h++)
            {
                IHoldSource hold = m_Holds[h];
                for (int slot = 0; slot < 8; slot++)
                {
                    if (!hold.TryPoseNumber(span.Effect, span.Range, slot, out float number, out float weight, out LetteraHoldComposition composition)) continue;
                    float value = span.Effective[slot];
                    span.Effective[slot] = composition == LetteraHoldComposition.Add ? value + number * weight
                        : composition == LetteraHoldComposition.Multiply ? value * Mathf.LerpUnclamped(1, number, weight) : Mathf.LerpUnclamped(value, number, weight);
                }
            }
        }

        void RunPosePass()
        {
            PoseCount++;
            int glyphs = m_PlacedCount;
            for (int i = 0; i < glyphs; i++) m_GlyphPoses[i] = LetteraGlyphPose.Identity;
            int streams = 0;
            bool reduced = LetteraAccessibility.ReducedMotion;

            PoseReveal();

            for (int s = 0; s < m_MotionSpans.Count; s++)
            {
                MotionSpan span = m_MotionSpans[s];
                ResolveEffective(ref span);
                m_MotionSpans[s] = span;
                LetteraMotionEffect effect = span.Effect;
                if (reduced && effect.ReducedMotionPolicy == LetteraReducedMotion.Rest) continue;
                float t = (float)Wrap(m_Phases[span.PhaseIndex].Time, effect, span.Effective);
                for (int b = 0; b < span.BodyCount; b++)
                {
                    PoseBody body = m_PoseBodies[span.BodyStart + b];
                    LetteraGlyphPose pose = LetteraGlyphPose.Identity;
                    float phi = effect.PhiOf(span.Effective, t, b);
                    effect.PoseBody(new LetteraMotionContext(t, phi, b, span.BodyCount, effect.Seed, body.Box, body.Em, span.Effective), ref pose);
                    m_BodyPoses[span.BodyStart + b] = effect.Strengthen(pose, body.Box);
                }
                for (int l = span.LinkStart; l < span.LinkStart + span.LinkCount; l++)
                {
                    PoseLink link = m_PoseLinks[l];
                    m_GlyphPoses[link.Glyph] = m_BodyPoses[link.Body].After(m_GlyphPoses[link.Glyph]);
                }
            }

            PoseSpoilers();
            RunPoseHooks();

            // Every quad of a glyph takes its glyph's pose, so a stroke never drifts from its face.
            int quads = VertexCount / 4;
            bool colors = false, moved = false;
            for (int q = 0; q < quads; q++)
            {
                int owner = m_QuadOwner[q];
                int v = q * 4;
                if (owner < 0 || owner >= glyphs)
                {
                    if (m_MarqueeOffset != 0 || m_MarqueeWasMoved)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 r = m_RestVertices[v + i];
                            Vertices[v + i] = new Vector3(r.x + m_MarqueeOffset, r.y);
                        }
                        moved = true;
                    }
                    continue;
                }
                ref LetteraGlyphPose pose = ref m_GlyphPoses[owner];
                for (int i = 0; i < 4; i++)
                {
                    Vector3 r = m_RestVertices[v + i];
                    Vertices[v + i] = new Vector3(pose.A * r.x + pose.C * r.y + pose.Tx + m_MarqueeOffset, pose.B * r.x + pose.D * r.y + pose.Ty);
                    Color32 rest = m_RestColors[v + i];
                    if (pose.Alpha != 1 || pose.Tint != Color.white)
                    {
                        Color tint = pose.Tint;
                        rest.r = (byte)(rest.r * tint.r);
                        rest.g = (byte)(rest.g * tint.g);
                        rest.b = (byte)(rest.b * tint.b);
                        rest.a = (byte)(rest.a * tint.a * Mathf.Clamp01(pose.Alpha));
                        colors = true;
                    }
                    Colors[v + i] = rest;
                }
                moved = true;
            }
            m_MarqueeWasMoved = m_MarqueeOffset != 0;
            ApplyCoverAlpha();
            if (moved) streams |= StreamPositions;
            // Colors stay in step each pass: a glyph whose fade ended has to go back as well.
            if (colors || m_PoseHadColors) streams |= StreamColors;
            m_PoseHadColors = colors;

            for (int s = 0; s < m_MotionSpans.Count; s++)
            {
                MotionSpan span = m_MotionSpans[s];
                if (reduced && span.Effect.ReducedMotionPolicy == LetteraReducedMotion.Rest && !(span.Effect is DeformMotion)) continue;
                span.Effect.WriteStreams(this, span, (float)Wrap(m_Phases[span.PhaseIndex].Time, span.Effect, span.Effective));
            }
            streams |= m_ExtraStreams;
            m_ExtraStreams = 0;
            PoseStreams |= streams;
        }

        bool m_PoseHadColors, m_MarqueeWasMoved;
        int m_ExtraStreams;

        static double Wrap(double time, LetteraMotionEffect effect, in LetteraMotionValues values)
        {
            double period = effect.PeriodOf(values);
            return period > 0 ? time % period : time % 3600.0;
        }

        /// <summary>Called after an upload that took the streams.</summary>
        public void ClearPoseStreams() => PoseStreams = 0;

        internal void WriteDeform(in MotionSpan span, float phase)
        {
            if (!Styled) return;
            int quads = VertexCount / 4;
            bool any = false;
            for (int q = 0; q < quads; q++)
            {
                int owner = m_QuadOwner[q];
                if (owner < 0 || owner >= m_PlacedCount) continue;
                int cluster = m_Placed[owner].Cluster;
                if (cluster < span.Start || cluster >= span.End) continue;
                int v = q * 4;
                if (Uv3[v].w == 0) continue;
                for (int i = 0; i < 4; i++) Uv3[v + i].z = phase;
                any = true;
            }
            if (any) m_ExtraStreams |= StreamUv3;
        }

        /// <summary>Where a vertex rests, for tests and what wants to compare.</summary>
        internal Vector3 RestVertex(int v) => m_WantsPose ? m_RestVertices[v] : Vertices[v];

        /// <summary>The glyph's pose after the last pass. Tests and inline objects read this.</summary>
        internal LetteraGlyphPose PoseOfGlyph(int glyph) => glyph < m_GlyphPoses.Length && m_WantsPose ? m_GlyphPoses[glyph] : LetteraGlyphPose.Identity;

        internal int FirstGlyphOf(int cluster)
        {
            for (int i = 0; i < m_PlacedCount; i++)
                if (m_Placed[i].Cluster == cluster)
                    return i;
            return -1;
        }
    }
}
