using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // Ranges that take input, and an identity for ranges that survives a parse.
    internal sealed partial class TextEngine
    {
        internal struct ActiveRange
        {
            public LetteraRangeEffect Effect;
            public int Id, Start, End, RecordIndex, StyleIndex;
            public string Primary, Label;
            public int SegmentStart, SegmentCount;
        }

        struct OldRange
        {
            public int Style, Start, End, Id;
            public string Label;
        }

        List<ActiveRange> m_Active = Rare<ActiveRange>.List;
        readonly List<Rect> m_ActiveRects = new List<Rect>();
        List<OldRange> m_OldRanges = Rare<OldRange>.List;
        List<int> m_RangeIds = Rare<int>.List;
        int[] m_OldText = System.Array.Empty<int>();
        int m_OldLength = -1, m_NextRangeId = 1;
        bool m_ActiveRectsValid;

        public IReadOnlyList<ActiveRange> ActiveRanges => m_Active;
        public int RangeIdOf(int recordIndex) => recordIndex >= 0 && recordIndex < m_RangeIds.Count ? m_RangeIds[recordIndex] : 0;

        /// <summary>Raised when ids retire, so what is keyed by them can let go.</summary>
        public event Action<int> RangeRetired;

        /// <summary>The ranges with input changed.</summary>
        public event Action ActiveRangesChanged;

        // After the rendered text is known, before effects run: carries ids across, by what the edit left alone.
        void AssignRangeIds(LetteraMatchContext context)
        {
            List<LetteraMatchContext.RangeRecord> ranges = context.Ranges;
            if (IsWindowed)
            {
                AssignDocumentRangeIds(context);
                return;
            }
            if (ranges.Count == 0 && m_OldRanges.Count == 0)
            {
                // Nothing has an identity to keep, and a plain text pays nothing for this.
                m_RangeIds.Clear();
                m_OldLength = -1;
                return;
            }
            int prefix = 0, suffix = 0;
            int oldLength = Math.Max(m_OldLength, 0);
            while (prefix < oldLength && prefix < m_TextLength && m_OldText[prefix] == m_Text[prefix]) prefix++;
            while (suffix < oldLength - prefix && suffix < m_TextLength - prefix && m_OldText[oldLength - 1 - suffix] == m_Text[m_TextLength - 1 - suffix]) suffix++;
            int delta = m_TextLength - oldLength;
            MapRevealValues(prefix, suffix, oldLength);

            m_RangeIds.Clear();
            for (int r = 0; r < ranges.Count; r++) Own(ref m_RangeIds).Add(0);
            bool same = delta == 0 && prefix == oldLength;
            for (int r = 0; r < ranges.Count; r++)
            {
                LetteraMatchContext.RangeRecord range = ranges[r];
                for (int o = 0; o < m_OldRanges.Count; o++)
                {
                    OldRange old = m_OldRanges[o];
                    if (old.Id == 0 || old.Style != range.StyleIndex) continue;
                    bool match;
                    if (!string.IsNullOrEmpty(range.Label) && old.Label == range.Label) match = true;
                    else if (same) match = old.Start == range.RenderedStart && old.End == range.RenderedEnd;
                    else if (old.End <= prefix && old.Start == range.RenderedStart && old.End == range.RenderedEnd) match = true;
                    else if (old.Start >= oldLength - suffix && old.Start + delta == range.RenderedStart && old.End + delta == range.RenderedEnd) match = true;
                    else
                    {
                        // An edit inside keeps the range only when its effect grows with what is typed; a link or a spoiler starts over.
                        LetteraEffect effect = m_EffectiveStyles[range.StyleIndex]?.Effect;
                        match = old.Start == range.RenderedStart && old.End + delta == range.RenderedEnd && old.Start <= prefix && old.End >= oldLength - suffix && effect != null && effect.ExtendsOnType;
                    }
                    if (!match) continue;
                    m_RangeIds[r] = old.Id;
                    old.Id = 0;
                    m_OldRanges[o] = old;
                    break;
                }
                if (m_RangeIds[r] == 0) m_RangeIds[r] = m_NextRangeId++;
            }
            for (int o = 0; o < m_OldRanges.Count; o++)
                if (m_OldRanges[o].Id != 0)
                {
                    m_CoverValue.Remove(m_OldRanges[o].Id);
                    m_CoverTarget.Remove(m_OldRanges[o].Id);
                    RangeRetired?.Invoke(m_OldRanges[o].Id);
                }

            m_OldRanges.Clear();
            for (int r = 0; r < ranges.Count; r++)
                Own(ref m_OldRanges).Add(new OldRange { Style = ranges[r].StyleIndex, Start = ranges[r].RenderedStart, End = ranges[r].RenderedEnd, Id = m_RangeIds[r], Label = ranges[r].Label });
            EnsureExact(ref m_OldText, m_TextLength);
            Array.Copy(m_Text, m_OldText, m_TextLength);
            m_OldLength = m_TextLength;
        }

        // Reveal values follow the text they belong to.
        void MapRevealValues(int prefix, int suffix, int oldLength)
        {
            if (m_CpVisible.Length == 0 || oldLength == m_TextLength && prefix == oldLength) return;
            int size = m_TextLength + 1;
            EnsureRevealArrays(size);
            int delta = m_TextLength - oldLength;
            if (delta > 0)
            {
                for (int i = m_TextLength - 1; i >= m_TextLength - suffix; i--)
                {
                    m_CpVisible[i] = m_CpVisible[i - delta];
                    m_CpShownEvent[i] = m_CpShownEvent[i - delta];
                }
            }
            else if (delta < 0)
            {
                for (int i = m_TextLength - suffix; i < m_TextLength; i++)
                {
                    m_CpVisible[i] = m_CpVisible[i - delta];
                    m_CpShownEvent[i] = m_CpShownEvent[i - delta];
                }
            }
            for (int i = prefix; i < m_TextLength - suffix; i++)
            {
                m_CpVisible[i] = 0;
                m_CpShownEvent[i] = false;
            }
        }

        void CollectActiveRanges()
        {
            m_ActiveRectsValid = false;
            bool had = m_Active.Count > 0;
            m_Active.Clear();
            List<LetteraAttributes.ActiveRequest> requests = m_Attributes.ActiveRequests;
            List<LetteraMatchContext.RangeRecord> ranges = m_MatchContext.Ranges;
            for (int a = 0; a < requests.Count; a++)
            {
                LetteraAttributes.ActiveRequest request = requests[a];
                if (request.End <= request.Start) continue;
                LetteraMatchContext.RangeRecord record = ranges[request.Range];
                string primary = record.Primary ?? request.Value;
                Own(ref m_Active).Add(new ActiveRange { Effect = request.Effect, Id = RangeIdOf(request.Range), Start = request.Start, End = request.End, RecordIndex = request.Range, StyleIndex = record.StyleIndex, Primary = primary, Label = record.Label });
            }
            if (m_Active.Count > 0 || had)
            {
                ActiveRangesChanged?.Invoke();
                m_Host.ActiveRangesChanged(m_Active.Count > 0);
            }
        }

        // Each range's box, kept apart from the range so a hit test rejects most of them with four comparisons and no copy of anything.
        float[] m_ActiveBoxes = System.Array.Empty<float>();
        LetteraRangeEffect[] m_ActiveEffects = System.Array.Empty<LetteraRangeEffect>();

        void EnsureActiveRects()
        {
            if (m_ActiveRectsValid) return;
            m_ActiveRectsValid = true;
            m_ActiveRects.Clear();
            EnsureExact(ref m_ActiveBoxes, m_Active.Count * 4);
            EnsureExact(ref m_ActiveEffects, m_Active.Count);
            for (int a = 0; a < m_Active.Count; a++)
            {
                ActiveRange range = m_Active[a];
                range.SegmentStart = m_ActiveRects.Count;
                ContentRangeRects(range.Start, range.End, m_ActiveRects);
                range.SegmentCount = m_ActiveRects.Count - range.SegmentStart;
                m_Active[a] = range;
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                for (int s = range.SegmentStart; s < m_ActiveRects.Count; s++)
                {
                    Rect rect = m_ActiveRects[s];
                    xMin = Mathf.Min(xMin, rect.xMin);
                    yMin = Mathf.Min(yMin, rect.yMin);
                    xMax = Mathf.Max(xMax, rect.xMax);
                    yMax = Mathf.Max(yMax, rect.yMax);
                }
                m_ActiveBoxes[a * 4] = xMin;
                m_ActiveBoxes[a * 4 + 1] = yMin;
                m_ActiveBoxes[a * 4 + 2] = xMax;
                m_ActiveBoxes[a * 4 + 3] = yMax;
                m_ActiveEffects[a] = range.Effect;
            }
        }

        /// <summary>The rects of a range's fragments, in local space.</summary>
        public void ActiveRectsOf(int id, List<Rect> rects)
        {
            Exact();
            EnsurePositioned();
            EnsureActiveRects();
            foreach (ActiveRange range in m_Active)
            {
                if (range.Id != id) continue;
                for (int s = 0; s < range.SegmentCount; s++) rects.Add(ScrolledRect(m_ActiveRects[range.SegmentStart + s]));
                return;
            }
        }

        /// <summary>The range at a local point: highest priority, then the earlier style, then the shorter range, then the order they came in. 0 for none.</summary>
        public int HitTestActive(Vector2 point, float touchPadding)
        {
            Exact();
            if (m_Active.Count == 0 || ShowsBars) return 0;
            EnsurePositioned();
            EnsureActiveRects();
            point = MapToContent(point);
            if (InMarqueeGap(point)) return 0;
            int best = -1;
            float bestDistance = float.MaxValue;
            float[] boxes = m_ActiveBoxes;
            LetteraRangeEffect[] effects = m_ActiveEffects;
            int count = m_Active.Count;
            for (int a = 0; a < count; a++)
            {
                LetteraRangeEffect effect = effects[a];
                float padding = Math.Max(effect.HitPadding * CurrentFontSize, touchPadding);
                if (point.x < boxes[a * 4] - padding || point.y < boxes[a * 4 + 1] - padding || point.x > boxes[a * 4 + 2] + padding || point.y > boxes[a * 4 + 3] + padding) continue;
                if (!effect.Enabled) continue;
                ActiveRange range = m_Active[a];
                float distance = float.MaxValue;
                for (int s = 0; s < range.SegmentCount; s++)
                {
                    Rect rect = m_ActiveRects[range.SegmentStart + s];
                    float dx = Mathf.Max(Mathf.Max(rect.xMin - point.x, 0), point.x - rect.xMax);
                    float dy = Mathf.Max(Mathf.Max(rect.yMin - point.y, 0), point.y - rect.yMax);
                    if (dx > padding || dy > padding) continue;
                    distance = Mathf.Min(distance, Mathf.Sqrt(dx * dx + dy * dy));
                }
                if (distance == float.MaxValue) continue;
                if (!InlineAllows(range, point)) continue;
                if (IsHiddenAt(range, point)) continue;
                if (best < 0 || Beats(range, distance, m_Active[best], bestDistance))
                {
                    best = a;
                    bestDistance = distance;
                }
            }
            return best >= 0 ? m_Active[best].Id : 0;
        }

        bool Beats(in ActiveRange a, float aDistance, in ActiveRange b, float bDistance)
        {
            // Only touch padding lets neighbours overlap: the nearer rect goes first there.
            if (aDistance != bDistance && (aDistance > 0 || bDistance > 0)) return aDistance < bDistance;
            if (a.Effect.Priority != b.Effect.Priority) return a.Effect.Priority > b.Effect.Priority;
            if (a.StyleIndex != b.StyleIndex && a.End - a.Start == b.End - b.Start) return a.StyleIndex < b.StyleIndex;
            return a.End - a.Start < b.End - b.Start;
        }

        // A reveal hides what it hides from the pointer too.
        bool IsHiddenAt(in ActiveRange range, Vector2 point)
        {
            if (m_RevealSpans.Count == 0) return false;
            int index = HitTestCaretNoMap(point);
            index = Math.Min(Math.Max(index, range.Start), range.End - 1);
            return IsRevealHidden(index);
        }

        int HitTestCaretNoMap(Vector2 point)
        {
            bool scrolling = m_MarqueeScrolling;
            m_MarqueeScrolling = false;
            int index = HitTestCaret(point, out _);
            m_MarqueeScrolling = scrolling;
            return index;
        }

        bool InlineAllows(in ActiveRange range, Vector2 point)
        {
            if (range.Effect.InlineHits == LetteraInlineHits.Include) return true;
            int index = Math.Min(Math.Max(HitTestCaretNoMap(point), range.Start), range.End - 1);
            int cluster = HitTestCaretNoMap(point);
            bool inline = false;
            if (index < m_TextLength) inline = m_Text[index] == 0xFFFC;
            if (!inline && cluster > range.Start) inline = m_Text[cluster - 1] == 0xFFFC;
            return range.Effect.InlineHits == LetteraInlineHits.Only ? inline : !inline;
        }

        public bool TryGetActive(int id, out ActiveRange range)
        {
            foreach (ActiveRange candidate in m_Active)
            {
                if (candidate.Id != id) continue;
                range = candidate;
                return true;
            }
            range = default;
            return false;
        }

        /// <summary>The ranges that hold a target, from the outermost in.</summary>
        internal void NestOf(in ActiveRange target, List<int> outerToInner)
        {
            outerToInner.Clear();
            foreach (ActiveRange candidate in m_Active)
                if (candidate.Id != target.Id && candidate.Start <= target.Start && candidate.End >= target.End)
                    outerToInner.Add(candidate.Id);
            outerToInner.Sort((a, b) =>
            {
                TryGetActive(a, out ActiveRange ra);
                TryGetActive(b, out ActiveRange rb);
                return (rb.End - rb.Start).CompareTo(ra.End - ra.Start);
            });
        }

        /// <summary>The ranges that can take the keyboard, in reading order.</summary>
        internal void FocusableRanges(List<int> ids, LetteraRangeTopic topic)
        {
            ids.Clear();
            for (int a = 0; a < m_Active.Count; a++)
            {
                ActiveRange range = m_Active[a];
                if (!range.Effect.Enabled || !range.Effect.Focusable) continue;
                if (topic != null && range.Effect.Topic != topic) continue;
                if (IsRevealHidden(range.Start)) continue;
                ids.Add(range.Id);
            }
            ids.Sort((a, b) =>
            {
                TryGetActive(a, out ActiveRange ra);
                TryGetActive(b, out ActiveRange rb);
                return ra.Start.CompareTo(rb.Start);
            });
        }

        // ------------------------------------------------------------- spoilers

        struct CoverQuad
        {
            public int Quad, Id;
            public Color32 Color;
        }

        List<CoverQuad> m_CoverQuads = Rare<CoverQuad>.List;
        Dictionary<int, float> m_CoverValue = Rare<int, float>.Map;
        Dictionary<int, bool> m_CoverTarget = Rare<int, bool>.Map;
        List<int> m_CoverKeys = Rare<int>.List;
        bool m_CoverFading;

        bool HasSpoilers
        {
            get
            {
                for (int a = 0; a < m_Active.Count; a++)
                    if (m_Active[a].Effect is SpoilerEffect)
                        return true;
                return false;
            }
        }

        /// <summary>Whether a codepoint lies under a cover now, for what must not read it.</summary>
        public bool IsConcealed(int cp)
        {
            for (int a = 0; a < m_Active.Count; a++)
            {
                ActiveRange range = m_Active[a];
                if (cp < range.Start || cp >= range.End || !(range.Effect is SpoilerEffect)) continue;
                if (!m_CoverTarget.TryGetValue(range.Id, out bool uncovered) || !uncovered) return !SuspendElide;
            }
            return false;
        }

        /// <summary>The effect of one of the text's styles, in the order they are collected.</summary>
        internal LetteraEffect EffectAt(int style)
        {
            EnsureParsed();
            return style >= 0 && style < m_EffectiveStyles.Count ? m_EffectiveStyles[style]?.Effect : null;
        }

        /// <summary>What a screen reader gets for an inline object at a codepoint.</summary>
        internal string InlineAltAt(int cp) => m_InlineByCp.TryGetValue(cp, out int formula) && m_InlineItems[formula].Math != null ? (m_InlineItems[formula].MathDisplay ? "$$" + m_InlineItems[formula].MathSource + "$$" : "$" + m_InlineItems[formula].MathSource + "$") : m_InlineByCp.TryGetValue(cp, out int item) ? m_InlineItems[item].Entry?.Alt : null;

        /// <summary>The language a codepoint is tagged with, or null.</summary>
        public string LanguageAt(int cp)
        {
            if (cp < 0 || cp >= m_Attributes.Length) return null;
            int index = m_Attributes.At(cp).Language;
            return index >= 0 ? m_Attributes.Languages[index] : null;
        }

        internal IReadOnlyList<LetteraAttributes.SemanticRequest> SemanticRequests => m_Attributes.SemanticRequests;

        /// <summary>Whether a span crosses a cover that does not let its text out.</summary>
        internal bool SpanConceals(int start, int end)
        {
            for (int a = 0; a < m_Active.Count; a++)
            {
                ActiveRange range = m_Active[a];
                if (range.Effect is SpoilerEffect spoiler && spoiler.CopyPolicy != LetteraSpoilerCopy.Keep && !IsUncovered(range.Id) && range.Start < end && start < range.End) return true;
            }
            return false;
        }

        public bool IsUncovered(int id) => m_CoverTarget.TryGetValue(id, out bool uncovered) && uncovered;

        /// <summary>Starts a spoiler's fade toward covered or uncovered.</summary>
        public void SetUncovered(int id, bool uncovered, float seconds)
        {
            Own(ref m_CoverTarget)[id] = uncovered;
            if (LetteraAccessibility.ReducedMotion || seconds <= 0) Own(ref m_CoverValue)[id] = uncovered ? 0 : 1;
            m_CoverFading = true;
            m_CoverSeconds = Mathf.Max(seconds, 0.0001f);
            UpdateHubMembership();
            MarkPoseDirty();
            if (m_WantsPose && m_Dirty < DirtyLevel.Mesh && VertexCount > 0)
            {
                RunPosePass();
                m_Host.PoseChanged();
            }
        }

        float m_CoverSeconds = 0.2f;

        float CoverOf(int id) => m_CoverValue.TryGetValue(id, out float value) ? value : 1;

        bool TickCovers(float unscaled)
        {
            if (!m_CoverFading) return false;
            bool any = false;
            m_CoverKeys.Clear();
            foreach (KeyValuePair<int, bool> pair in m_CoverTarget) Own(ref m_CoverKeys).Add(pair.Key);
            foreach (int id in m_CoverKeys)
            {
                float target = m_CoverTarget[id] ? 0 : 1, value = CoverOf(id);
                if (value == target) continue;
                Own(ref m_CoverValue)[id] = Mathf.MoveTowards(value, target, unscaled / m_CoverSeconds);
                any = true;
            }
            if (!any)
            {
                m_CoverFading = false;
                UpdateHubMembership();
            }
            return any;
        }

        // A cover draws above the text, and the glyphs under it are not drawn at all: nothing can show through or reach a screenshot.
        void DrawSpoilerCovers()
        {
            m_CoverQuads.Clear();
            if (!HasSpoilers) return;
            EnsureActiveRects();
            m_CurrentOwner = -1;
            for (int a = 0; a < m_Active.Count; a++)
            {
                ActiveRange range = m_Active[a];
                if (!(range.Effect is SpoilerEffect spoiler)) continue;
                UseSegment(s_NoPaint);
                m_FilterRow = 0;
                for (int s = 0; s < range.SegmentCount; s++)
                {
                    Rect rect = m_ActiveRects[range.SegmentStart + s];
                    int quad = VertexCount / 4;
                    Color32 color = spoiler.CoverColor;
                    AddSolid(rect.xMin, rect.yMin, rect.xMax, rect.yMax, color, s_NoPaint, default, 0, 0);
                    Own(ref m_CoverQuads).Add(new CoverQuad { Quad = quad, Id = range.Id, Color = color });
                }
            }
        }

        void PoseSpoilers()
        {
            if (m_CoverQuads.Count == 0 && !HasSpoilers) return;
            bool lifted = SuspendElide;
            for (int a = 0; a < m_Active.Count; a++)
            {
                ActiveRange range = m_Active[a];
                if (!(range.Effect is SpoilerEffect)) continue;
                float cover = lifted ? 0 : CoverOf(range.Id);
                if (cover <= 0) continue;
                // The cover goes first and the text comes up under it, and nothing shows while it is whole.
                for (int i = 0; i < m_PlacedCount; i++)
                {
                    int cluster = m_Placed[i].Cluster;
                    if (cluster < range.Start || cluster >= range.End) continue;
                    m_GlyphPoses[i].Alpha *= 1 - cover;
                }
            }
        }

        void ApplyCoverAlpha()
        {
            if (m_CoverQuads.Count == 0) return;
            bool lifted = SuspendElide;
            int copies = Math.Max(m_MarqueeCopies, 1), unit = VertexCount / copies;
            for (int c = 0; c < m_CoverQuads.Count; c++)
            {
                CoverQuad cover = m_CoverQuads[c];
                float value = lifted ? 0 : CoverOf(cover.Id);
                for (int copy = 0; copy < copies; copy++)
                {
                    int v = cover.Quad * 4 + copy * unit;
                    for (int i = 0; i < 4; i++)
                    {
                        Color32 color = m_RestColors[v + i];
                        color.a = (byte)(color.a * value);
                        Colors[v + i] = color;
                    }
                }
            }
            m_ExtraStreams |= StreamColors;
        }

        /// <summary>What plain copy writes for a selection that crosses covered text.</summary>
        internal void MaskConcealed(System.Text.StringBuilder plain, int start, int end)
        {
            for (int a = m_Active.Count - 1; a >= 0; a--)
            {
                ActiveRange range = m_Active[a];
                if (!(range.Effect is SpoilerEffect spoiler) || IsUncovered(range.Id) || spoiler.CopyPolicy == LetteraSpoilerCopy.Keep) continue;
                int from = Math.Max(range.Start, start), to = Math.Min(range.End, end);
                if (from >= to) continue;
                int a16 = Utf16Between(start, from), b16 = a16 + Utf16Between(from, to);
                plain.Remove(a16, b16 - a16);
                if (spoiler.CopyPolicy == LetteraSpoilerCopy.Mask)
                {
                    var text = new ReadOnlySpan<int>(m_Text, 0, m_TextLength);
                    int clusters = 0;
                    for (int c = from; c < to; c = LetteraTextNavigator.NextStop(text, c)) clusters++;
                    plain.Insert(a16, new string('█', clusters));
                }
            }
        }

        int Utf16Between(int from, int to)
        {
            int count = 0;
            for (int i = from; i < to; i++) count += m_Text[i] > 0xFFFF ? 2 : 1;
            return count;
        }
    }
}
