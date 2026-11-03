using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Reveal: a value per codepoint that moves toward what the frontier says. Marquee: one offset for everything, over copies of the mesh.
    internal sealed partial class TextEngine
    {
        struct RevealSpan
        {
            public RevealEffect Effect;
            public int Start, End;
            public string Entrance, Reach;
            public LetteraRevealUnit Unit;
        }

        struct RevealBody
        {
            public LetteraBodyBox Box;
            public float Em;
        }

        bool HasRevealOrMarquee => (m_Aside.RevealSpans.Count > 0 && !m_Aside.RevealSettled) || m_Aside.Marquee != null && m_Aside.MarqueeScrolling;

        /// <summary>Whether a codepoint draws nothing now, for what should not react to it.</summary>
        public bool IsRevealHidden(int cp) => cp >= 0 && cp < m_Aside.CpVisible.Length && cp < m_TextLength && m_Aside.RevealSpans.Count > 0 && m_Aside.CpReveal[cp] >= 0 && m_Aside.CpVisible[cp] <= 0;

        void CollectReveals()
        {
            foreach (RevealEffect effect in m_Aside.RevealListening) effect.Touched -= OnRevealTouched;
            Empty(m_Aside.RevealListening);
            Empty(m_Aside.RevealSpans);
            List<LetteraAttributes.RevealRequest> requests = m_Attributes.RevealRequests;
            if (requests.Count == 0) return;
            EnsureExact(ref m_Aside.CpReveal, m_TextLength + 1);
            EnsureRevealArrays(m_TextLength + 1);
            for (int c = 0; c < m_TextLength; c++) m_Aside.CpReveal[c] = -1;
            foreach (LetteraAttributes.RevealRequest request in requests)
            {
                Own(ref m_Aside.RevealSpans).Add(new RevealSpan { Effect = request.Effect, Start = request.Start, End = Math.Min(request.End, m_TextLength), Entrance = request.Entrance ?? request.Effect.Entrance, Reach = request.Reach ?? request.Effect.Reach, Unit = request.Unit });
                // Inner ranges come later, and the innermost governs a codepoint.
                for (int c = request.Start; c < request.End && c < m_TextLength; c++) m_Aside.CpReveal[c] = m_Aside.RevealSpans.Count - 1;
                if (!m_Aside.RevealListening.Contains(request.Effect))
                {
                    Own(ref m_Aside.RevealListening).Add(request.Effect);
                    request.Effect.Touched += OnRevealTouched;
                }
            }
            NumberRevealUnits();
            UpdateRevealTargets();
            if (!m_Aside.RevealInitialized)
            {
                // What is there when a text first shows starts already at its targets if no entrance runs; otherwise it plays.
                m_Aside.RevealInitialized = true;
                for (int c = 0; c < m_TextLength; c++) m_Aside.CpVisible[c] = 0;
            }
            m_Aside.RevealSettled = false;
            m_Aside.RevealRemovalsDirty = true;
        }

        void OnRevealTouched()
        {
            UpdateRevealTargets();
            m_Aside.RevealSettled = false;
            UpdateHubMembership();
            MarkPoseDirty();
        }

        void EnsureRevealArrays(int size)
        {
            EnsureExact(ref m_Aside.CpVisible, size);
            EnsureExact(ref m_Aside.CpTarget, size);
            EnsureExact(ref m_Aside.CpUnit, size);
            EnsureExact(ref m_Aside.CpShownEvent, size);
        }

        void NumberRevealUnits()
        {
            for (int s = 0; s < m_Aside.RevealSpans.Count; s++)
            {
                RevealSpan span = m_Aside.RevealSpans[s];
                int unit = -1;
                int c = span.Start;
                var text = m_Text.Slice(0, m_TextLength);
                while (c < span.End)
                {
                    int end;
                    switch (span.Unit)
                    {
                        case LetteraRevealUnit.Word:
                            LetteraTextNavigator.WordAt(text, c, out _, out end);
                            break;
                        case LetteraRevealUnit.Paragraph:
                            LetteraTextNavigator.ParagraphAt(text, c, out _, out _, out end);
                            break;
                        case LetteraRevealUnit.Line:
                            end = m_Lines.Count > 0 ? m_Lines[LineOf(c, LetteraCaretBias.Forward)].End : LetteraTextNavigator.NextStop(text, c);
                            break;
                        default:
                            end = LetteraTextNavigator.NextStop(text, c);
                            break;
                    }
                    end = Math.Min(Math.Max(end, c + 1), span.End);
                    bool space = span.Unit == LetteraRevealUnit.Word && LetteraUnicode.IsWhiteSpace(text[c]);
                    if (!space) unit++;
                    for (int k = c; k < end; k++) m_Aside.CpUnit[k] = Math.Max(unit, 0);
                    c = end;
                }
            }
        }

        static bool ReadReach(string reach, int count, out float units)
        {
            units = count;
            if (!LetteraLength.TryParse(reach, out LetteraLength length)) return string.IsNullOrEmpty(reach);
            units = length.Kind == LetteraLength.Unit.Percent ? length.Value * 0.01f * count : length.Value;
            return true;
        }

        void UpdateRevealTargets()
        {
            if (m_Aside.RevealSpans.Count == 0) return;
            // A shared frontier counts one set of units over all ranges of an effect, in text order.
            for (int s = 0; s < m_Aside.RevealSpans.Count; s++)
            {
                RevealSpan span = m_Aside.RevealSpans[s];
                int before = 0, total = UnitCount(s);
                bool shared = !span.Effect.Independent;
                if (shared)
                {
                    int union = 0;
                    for (int o = 0; o < m_Aside.RevealSpans.Count; o++)
                    {
                        if (m_Aside.RevealSpans[o].Effect != span.Effect) continue;
                        if (o < s) before += UnitCount(o);
                        union += UnitCount(o);
                    }
                    total = union;
                }
                ReadReach(span.Effect.Reach != span.Reach && m_Attributes.RevealTagged(s) ? span.Reach : span.Effect.Reach, total, out float reach);
                for (int c = span.Start; c < span.End; c++)
                {
                    if (m_Aside.CpReveal[c] != s) continue;
                    if (LetteraTextNavigator.IsParagraphSeparator(m_Text[c]) || m_Text[c] == 0x2028)
                    {
                        m_Aside.CpTarget[c] = 1;
                        continue;
                    }
                    m_Aside.CpTarget[c] = Mathf.Clamp01(reach - (before + m_Aside.CpUnit[c]));
                }
            }
        }

        int UnitCount(int span)
        {
            RevealSpan s = m_Aside.RevealSpans[span];
            if (s.End <= s.Start) return 0;
            int last = -1;
            for (int c = s.Start; c < s.End; c++)
            {
                if (m_Aside.CpReveal[c] != span) continue;
                bool space = s.Unit == LetteraRevealUnit.Word && LetteraUnicode.IsWhiteSpace(m_Text[c]);
                if (!space && m_Aside.CpUnit[c] > last) last = m_Aside.CpUnit[c];
            }
            return last + 1;
        }

        // Values move at a constant speed, so a change of direction goes on from where a unit is.
        bool TickReveal(float unscaled, float scaled)
        {
            if (m_Aside.RevealSpans.Count == 0 || m_Aside.RevealSettled) return false;
            bool reduced = LetteraAccessibility.ReducedMotion;
            bool moved = false, settled = true, setChanged = false;
            for (int s = 0; s < m_Aside.RevealSpans.Count; s++)
            {
                RevealSpan span = m_Aside.RevealSpans[s];
                RevealEffect effect = span.Effect;
                float seconds = effect.Clock == LetteraClock.Scaled ? scaled : effect.Clock == LetteraClock.Unscaled ? unscaled : effect.ManualSeconds;
                effect.Resolve(span.Entrance, out LetteraEntrance enter, out LetteraEntrance leave);
                bool snap = effect.SnapRequested || reduced;
                for (int c = span.Start; c < span.End; c++)
                {
                    if (m_Aside.CpReveal[c] != s) continue;
                    float v = m_Aside.CpVisible[c], target = m_Aside.CpTarget[c];
                    if (v == target) continue;
                    if (target > v && v <= 0 && !m_Aside.CpShownEvent[c])
                    {
                        m_Aside.CpShownEvent[c] = true;
                        int end = c + 1;
                        while (end < span.End && m_Aside.CpReveal[end] == s && m_Aside.CpUnit[end] == m_Aside.CpUnit[c]) end++;
                        effect.RaiseUnitShown(c, end);
                    }
                    float duration = target >= v || leave == null ? enter.Duration : leave.Duration;
                    float next = snap || duration <= 0 ? target : Mathf.MoveTowards(v, target, seconds / duration);
                    if ((next <= 0) != (v <= 0)) setChanged = true;
                    if (next <= 0 && target <= 0) m_Aside.CpShownEvent[c] = false;
                    m_Aside.CpVisible[c] = next;
                    moved = true;
                    if (next != target) settled = false;
                }
            }
            foreach (RevealEffect effect in m_Aside.RevealListening)
            {
                effect.SnapRequested = false;
                effect.ManualSeconds = 0;
            }
            if (setChanged && RevealReflows)
            {
                m_Aside.RevealRemovalsDirty = true;
                SetDirty(DirtyLevel.Layout);
                m_Host.RequestRebuild(DirtyLevel.Layout);
            }
            if (settled)
            {
                m_Aside.RevealSettled = true;
                foreach (RevealEffect effect in m_Aside.RevealListening) effect.RaiseSettled();
                UpdateHubMembership();
                moved = true;
            }
            return moved;
        }

        bool RevealReflows
        {
            get
            {
                foreach (RevealSpan span in m_Aside.RevealSpans)
                    if (span.Effect.Reflow)
                        return true;
                return false;
            }
        }

        // Hidden text under Reflow takes no room: to layout, it is as elided as anything.
        void ApplyRevealRemovals()
        {
            if (m_Aside.RevealSpans.Count == 0 || !RevealReflows) return;
            int c = 0;
            while (c < m_TextLength)
            {
                bool hidden = m_Aside.CpReveal[c] >= 0 && m_Aside.RevealSpans[m_Aside.CpReveal[c]].Effect.Reflow && m_Aside.CpVisible[c] <= 0 && m_Aside.CpTarget[c] <= 0 && !LetteraTextNavigator.IsParagraphSeparator(m_Text[c]);
                if (!hidden)
                {
                    c++;
                    continue;
                }
                int end = c + 1;
                while (end < m_TextLength && m_Aside.CpReveal[end] >= 0 && m_Aside.RevealSpans[m_Aside.CpReveal[end]].Effect.Reflow && m_Aside.CpVisible[end] <= 0 && m_Aside.CpTarget[end] <= 0 && !LetteraTextNavigator.IsParagraphSeparator(m_Text[end])) end++;
                Own(ref m_Aside.Removals).Add(new Removal { Start = c, End = end, Anchor = c });
                c = end;
            }
        }

        void BuildRevealBodies()
        {
            Empty(m_Aside.RevealBodies);
            if (m_Aside.RevealSpans.Count == 0) return;
            // A unit moves as one body, about its own box.
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                if (m_Aside.CpReveal[g.Cluster] < 0) continue;
                int key = BodyKey(g.Cluster);
                while (m_Aside.RevealBodies.Count <= key) Own(ref m_Aside.RevealBodies).Add(new RevealBody { Box = LetteraBodyBox.Empty });
                RevealBody body = m_Aside.RevealBodies[key];
                LetteraLine line = m_Lines[Math.Min(g.Line, m_Lines.Count - 1)];
                body.Box.Add(g.X, g.Y - line.Descent * 0.8f, g.X + g.Advance, g.Y + line.Ascent * 0.8f);
                if (g.Size > body.Em) body.Em = g.Size;
                m_Aside.RevealBodies[key] = body;
            }
        }

        // Spans are few; this gives each unit of each span its own slot.
        int BodyKey(int cp)
        {
            int span = m_Aside.CpReveal[cp], offset = 0;
            for (int s = 0; s < span; s++) offset += UnitCountCached(s);
            return offset + m_Aside.CpUnit[cp];
        }

        int UnitCountCached(int span) => m_Aside.RevealSpans[span].End - m_Aside.RevealSpans[span].Start + 1;

        void PoseReveal()
        {
            if (m_Aside.RevealSpans.Count == 0) return;
            for (int i = 0; i < m_PlacedCount; i++)
            {
                ref PlacedGlyph g = ref m_Placed[i];
                int span = m_Aside.CpReveal[g.Cluster];
                if (span < 0) continue;
                float v = m_Aside.CpVisible[g.Cluster];
                if (v >= 1) continue;
                if (v <= 0)
                {
                    // Nothing to see, with the vertex count kept.
                    m_Aside.GlyphPoses[i] = new LetteraGlyphPose { Alpha = 0, Tint = Color.white, Tx = g.X, Ty = g.Y };
                    continue;
                }
                RevealSpan s = m_Aside.RevealSpans[span];
                s.Effect.Resolve(s.Entrance, out LetteraEntrance enter, out LetteraEntrance leave);
                LetteraEntrance entrance = leave != null && m_Aside.CpTarget[g.Cluster] < v ? leave : enter;
                RevealBody body = m_Aside.RevealBodies[BodyKey(g.Cluster)];
                LetteraGlyphPose pose = LetteraGlyphPose.Identity;
                entrance.Pose(v, body.Box, body.Em, m_Aside.CpUnit[g.Cluster], s.Effect.Seed, ref pose);
                m_Aside.GlyphPoses[i] = pose;
            }
        }

        // ------------------------------------------------------------- marquee

        /// <summary>A press or a selection on the text keeps it still.</summary>
        public bool MarqueeHold { get; set; }

        /// <summary>The text is scrolling now, with the fade in local units at its sides.</summary>
        public bool IsMarqueeScrolling => m_Aside.MarqueeScrolling;
        public float MarqueeFadeUnits => m_Aside.Marquee != null ? m_Aside.Marquee.Fade * CurrentFontSize : 0;
        public float MarqueeOffset => m_Aside.MarqueeOffset;

        // Before layout: whether it will scroll decides elision and alignment.
        void DecideMarquee(float size, float width)
        {
            m_Aside.MarqueeScrolling = false;
            m_Aside.Marquee = null;
            if (m_Attributes.Marquee == null) return;
            if (!m_Host.SupportsMarquee || ClipsViewport)
            {
                if (!s_MarqueeWarned)
                {
                    s_MarqueeWarned = true;
                    Debug.LogWarning("Lettera: marquee needs a Canvas text. World text has no clip to scroll within, so it stays still.", m_Host.Owner);
                }
                m_Aside.Marquee = null;
                return;
            }
            m_Aside.Marquee = m_Attributes.Marquee;
            if (float.IsInfinity(width)) return;
            float widest = BreakLines(m_Aside.MeasureLines, size, float.PositiveInfinity, false, false).MaxWidth;
            m_Aside.MarqueeScrolling = !LetteraAccessibility.ReducedMotion && (m_Aside.Marquee.EvenIfFits || widest > width + Epsilon);
            m_Aside.MarqueeW = widest;
            m_Aside.MarqueeB = width;
        }

        static bool s_MarqueeWarned;

        static float ResolveLength(string token, float em, float percentBase, float fallback)
        {
            if (!LetteraLength.TryParse(token, out LetteraLength length)) return fallback;
            return length.ToUnits(em, percentBase, true);
        }

        // The whole mesh again for each copy, so the frame only has to move it.
        void DuplicateForMarquee()
        {
            m_Aside.MarqueeCopies = 1;
            m_Aside.MarqueeOffset = 0;
            if (!m_Aside.MarqueeScrolling || VertexCount == 0) return;
            float em = CurrentFontSize;
            float gap = ResolveLength(m_Aside.Marquee.Gap, em, m_Aside.MarqueeB, 2 * em);
            m_Aside.MarqueeD = m_Aside.MarqueeW + gap;
            m_Aside.MarqueeRtl = m_Paragraphs.Count > 0 && (m_Paragraphs[0].Level & 1) != 0;
            if (m_Aside.Marquee.Mode != LetteraMarqueeMode.Loop || m_Aside.MarqueeD <= 0) return;
            int copies = Mathf.CeilToInt((m_Aside.MarqueeB + gap) / m_Aside.MarqueeD) + 1;
            int unitVertices = VertexCount, unitIndices = IndexCount;
            if (unitVertices * copies > 40000)
            {
                Debug.LogWarning("Lettera: this marquee would need too many vertices for its copies, so it travels back and forth instead.", m_Host.Owner);
                return;
            }
            m_Aside.MarqueeCopies = copies;
            int segmentsBefore = Segments.Count;
            float sign = m_Aside.MarqueeRtl ? -1 : 1;
            for (int copy = 1; copy < copies; copy++)
            {
                float shift = sign * copy * m_Aside.MarqueeD;
                int baseVertex = VertexCount, baseIndex = IndexCount;
                for (int q = 0; q < unitVertices / 4; q++)
                {
                    int v = Reserve();
                    if (m_RecordOwners)
                    {
                        EnsureOwnerSize(v / 4 + 1);
                        m_Aside.QuadOwner[v / 4] = m_Aside.QuadOwner[q];
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = Vertices[q * 4 + i];
                        Vertices[v + i] = new Vector3(p.x + shift, p.y, p.z);
                        Colors[v + i] = Colors[q * 4 + i];
                        Uv0[v + i] = Uv0[q * 4 + i];
                        Uv1[v + i] = Uv1[q * 4 + i];
                        if (Styled)
                        {
                            Uv2[v + i] = Uv2[q * 4 + i];
                            Uv3[v + i] = Uv3[q * 4 + i];
                        }
                        m_OwnAlpha[v + i] = m_OwnAlpha[q * 4 + i];
                    }
                }
                // Reserve wrote indices in vertex order, but drawing order is the first copy's.
                for (int i = 0; i < unitIndices; i++) Indices[baseIndex + i] = Indices[i] + baseVertex;
                for (int s = 0; s < segmentsBefore; s++)
                {
                    MeshSegment segment = Segments[s];
                    segment.IndexStart += baseIndex;
                    Segments.Add(segment);
                }
            }
        }

        bool TickMarquee(float scaled, float unscaled)
        {
            if (m_Aside.Marquee == null || !m_Aside.MarqueeScrolling) return false;
            if (LetteraAccessibility.ReducedMotion)
            {
                bool wasMoved = m_Aside.MarqueeOffset != 0;
                m_Aside.MarqueeOffset = 0;
                return wasMoved;
            }
            if (!MarqueeHold)
            {
                if (m_Aside.Marquee.Clock == LetteraClock.Manual) m_Aside.MarqueeTime = m_Aside.Marquee.Phase;
                else m_Aside.MarqueeTime += m_Aside.Marquee.Clock == LetteraClock.Scaled ? scaled : unscaled;
            }
            float offset = MarqueeOffsetAt(m_Aside.MarqueeTime);
            if (offset == m_Aside.MarqueeOffset) return false;
            m_Aside.MarqueeOffset = offset;
            return true;
        }

        internal float MarqueeOffsetAt(double t)
        {
            float em = CurrentFontSize;
            float speed = ResolveLength(m_Aside.Marquee.Speed, em, m_Aside.MarqueeB, 2 * em);
            float s = Mathf.Abs(speed);
            if (s <= 0) return 0;
            float pause = m_Aside.Marquee.Pause;
            if (m_Aside.Marquee.Mode == LetteraMarqueeMode.Loop && m_Aside.MarqueeCopies > 1)
            {
                double period = pause + m_Aside.MarqueeD / s;
                double u = t % period;
                float travel = u < pause ? 0 : (float)(u - pause) * s;
                // Running the other way, a copy comes in from the far side. Copies are alike, so the jump does not show.
                if (speed >= 0) return (m_Aside.MarqueeRtl ? 1 : -1) * travel;
                return travel > 0 ? (m_Aside.MarqueeRtl ? 1 : -1) * (m_Aside.MarqueeD - travel) : 0;
            }
            float span = Mathf.Max(m_Aside.MarqueeW - m_Aside.MarqueeB, 0);
            if (span <= 0) return 0;
            double leg = pause + span / s, whole = 2 * leg, at = t % whole;
            float distance;
            if (at < pause) distance = 0;
            else if (at < leg) distance = (float)(at - pause) * s;
            else if (at < leg + pause) distance = span;
            else distance = span - (float)(at - leg - pause) * s;
            return (m_Aside.MarqueeRtl ? 1 : -1) * distance;
        }

        // A point on any copy means the same place on the first.
        Vector2 MapToContent(Vector2 point)
        {
            point.y -= m_Aside.WindowOffset;
            if (!m_Aside.MarqueeScrolling) return point;
            point.x -= m_Aside.MarqueeOffset;
            if (m_Aside.MarqueeCopies > 1 && m_Aside.MarqueeD > 0)
            {
                float start = LayoutBounds.xMin;
                if (m_Aside.MarqueeRtl)
                {
                    float end = LayoutBounds.xMax;
                    while (point.x < end - m_Aside.MarqueeD) point.x += m_Aside.MarqueeD;
                }
                else
                {
                    while (point.x > start + m_Aside.MarqueeD) point.x -= m_Aside.MarqueeD;
                }
            }
            return point;
        }

        /// <summary>A point lands in the gap between copies.</summary>
        internal bool InMarqueeGap(Vector2 mapped)
        {
            if (!m_Aside.MarqueeScrolling || m_Aside.MarqueeCopies <= 1) return false;
            return m_Aside.MarqueeRtl ? mapped.x < LayoutBounds.xMax - m_Aside.MarqueeW : mapped.x > LayoutBounds.xMin + m_Aside.MarqueeW;
        }
    }
}
