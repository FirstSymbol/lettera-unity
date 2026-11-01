using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Time to pixels and rows to pixels, with zoom and pan as in every other view that zooms.</summary>
    internal sealed class SequencerView
    {
        internal const float MinScale = 4, MaxScale = 4000, RowHeight = 26;

        public float PixelsPerSecond = 80;
        public float ScrollSeconds;
        public float ScrollY;

        public float X(float seconds) => (seconds - ScrollSeconds) * PixelsPerSecond;
        public float T(float x) => x / PixelsPerSecond + ScrollSeconds;
        public float Y(int row) => row * RowHeight - ScrollY;
        public int RowAt(float y) => Mathf.Max(0, Mathf.FloorToInt((y + ScrollY) / RowHeight));

        /// <summary>The moment under the pointer stays under it.</summary>
        public void ZoomAt(float x, float factor)
        {
            float moment = T(x);
            PixelsPerSecond = Mathf.Clamp(PixelsPerSecond * factor, MinScale, MaxScale);
            ScrollSeconds = Mathf.Max(0, moment - x / PixelsPerSecond);
        }

        public void Pan(float dx, float dy)
        {
            ScrollSeconds = Mathf.Max(0, ScrollSeconds - dx / PixelsPerSecond);
            ScrollY = Mathf.Max(0, ScrollY - dy);
        }

        public void Fit(float seconds, float width)
        {
            if (seconds <= 0 || width <= 0) return;
            PixelsPerSecond = Mathf.Clamp(width * 0.95f / seconds, MinScale, MaxScale);
            ScrollSeconds = 0;
        }

        /// <summary>A round step that leaves at least this much room between ticks: 1, 2 or 5 times a power of ten.</summary>
        public float TickStep(float minPixels = 64)
        {
            float wanted = minPixels / PixelsPerSecond;
            float power = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(wanted)));
            foreach (float multiple in new[] { 1f, 2f, 5f, 10f })
                if (power * multiple >= wanted)
                    return power * multiple;
            return power * 10;
        }
    }

    internal enum SequencerZone { None, Body, Start, End }

    /// <summary>What the window's gestures and commands do to a sequencer's cues, with no panel and no undo. The window gives each change its undo step.</summary>
    internal sealed class SequencerModel
    {
        const float SnapPixels = 8, EdgePixels = 6;
        internal const float MinUnit = 0.01f;
        const string ClipPrefix = "LetteraCues:";

        [Serializable]
        sealed class Bundle
        {
            public List<LetteraCue> Cues = new List<LetteraCue>();
        }

        struct Grip
        {
            public int Index, Lane;
            public float Start, Unit;
        }

        readonly Func<List<LetteraCue>> m_Cues;
        readonly Func<LetteraCue, float> m_Span;
        readonly List<Grip> m_Grips = new List<Grip>();
        readonly List<float> m_Targets = new List<float>();
        SequencerZone m_DragZone;
        int m_DragPrimary = -1;
        float m_PressTime;
        int m_PressRow;

        public readonly SequencerView View = new SequencerView();
        public readonly List<int> Selected = new List<int>();
        public float Playhead;
        public bool SnapOn = true;

        /// <summary>Where the last drag step snapped, or below 0 where it did not.</summary>
        public float SnapLine { get; private set; } = -1;

        public SequencerModel(Func<List<LetteraCue>> cues, Func<LetteraCue, float> span = null)
        {
            m_Cues = cues;
            m_Span = span ?? (cue => cue.UnitSeconds);
        }

        List<LetteraCue> Cues => m_Cues();

        public float SpanOf(LetteraCue cue) => Mathf.Max(m_Span(cue), MinUnit);

        public bool IsSelected(int index) => Selected.Contains(index);

        // ------------------------------------------------------------- finding and selecting

        /// <summary>The cue under a point, with the later drawn winning, and which part of it.</summary>
        public int Hit(float x, float y, out SequencerZone zone)
        {
            zone = SequencerZone.None;
            int row = View.RowAt(y);
            List<LetteraCue> cues = Cues;
            for (int i = cues.Count - 1; i >= 0; i--)
            {
                LetteraCue cue = cues[i];
                if (cue.Lane != row) continue;
                float left = View.X(cue.Start), right = View.X(cue.Start + SpanOf(cue));
                if (x < left - 1 || x > right + 1) continue;
                // A very short cue is still a thing to move.
                float edge = Mathf.Min(EdgePixels, (right - left) / 3);
                zone = x <= left + edge ? SequencerZone.Start : x >= right - edge ? SequencerZone.End : SequencerZone.Body;
                return i;
            }
            return -1;
        }

        public void Click(int index, bool add)
        {
            if (add)
            {
                if (!Selected.Remove(index)) Selected.Add(index);
            }
            else if (!Selected.Contains(index))
            {
                Selected.Clear();
                Selected.Add(index);
            }
            Selected.Sort();
        }

        public void ClearSelection() => Selected.Clear();

        public void SelectAll()
        {
            Selected.Clear();
            for (int i = 0; i < Cues.Count; i++) Selected.Add(i);
        }

        /// <summary>Everything a rectangle touches.</summary>
        public void Marquee(float x0, float y0, float x1, float y1, bool add)
        {
            float left = Mathf.Min(x0, x1), right = Mathf.Max(x0, x1), top = Mathf.Min(y0, y1), bottom = Mathf.Max(y0, y1);
            if (!add) Selected.Clear();
            List<LetteraCue> cues = Cues;
            for (int i = 0; i < cues.Count; i++)
            {
                LetteraCue cue = cues[i];
                float cueLeft = View.X(cue.Start), cueRight = View.X(cue.Start + SpanOf(cue));
                float cueTop = View.Y(cue.Lane), cueBottom = cueTop + SequencerView.RowHeight;
                if (cueRight < left || cueLeft > right || cueBottom < top || cueTop > bottom) continue;
                if (!Selected.Contains(i)) Selected.Add(i);
            }
            Selected.Sort();
        }

        /// <summary>After an undo the list may be shorter.</summary>
        public void ClampSelection() => Selected.RemoveAll(i => i >= Cues.Count);

        // ------------------------------------------------------------- dragging

        public void BeginDrag(int index, SequencerZone zone, float pointerX, int row)
        {
            m_DragZone = zone;
            m_DragPrimary = index;
            m_PressTime = View.T(pointerX);
            m_PressRow = row;
            m_Grips.Clear();
            SnapLine = -1;
            List<LetteraCue> cues = Cues;
            if (zone == SequencerZone.Body)
                foreach (int i in Selected)
                    m_Grips.Add(new Grip { Index = i, Start = cues[i].Start, Lane = cues[i].Lane, Unit = cues[i].UnitSeconds });
            else
                m_Grips.Add(new Grip { Index = index, Start = cues[index].Start, Lane = cues[index].Lane, Unit = cues[index].UnitSeconds });

            // What it can snap to: the start, the playhead, and the ends of whatever is not moving.
            m_Targets.Clear();
            m_Targets.Add(0);
            m_Targets.Add(Playhead);
            for (int i = 0; i < cues.Count; i++)
            {
                if (IsGripped(i)) continue;
                m_Targets.Add(cues[i].Start);
                m_Targets.Add(cues[i].Start + SpanOf(cues[i]));
            }
        }

        bool IsGripped(int index)
        {
            foreach (Grip grip in m_Grips)
                if (grip.Index == index)
                    return true;
            return false;
        }

        public bool Dragging => m_DragPrimary >= 0;

        float NearestTarget(float moment, out float distance)
        {
            distance = float.MaxValue;
            float best = moment;
            foreach (float target in m_Targets)
            {
                float d = Mathf.Abs(target - moment);
                if (d >= distance) continue;
                distance = d;
                best = target;
            }
            return best;
        }

        float NearestTick(float moment, out float distance)
        {
            float step = View.TickStep();
            float tick = Mathf.Round(moment / step) * step;
            distance = Mathf.Abs(tick - moment);
            return tick;
        }

        /// <summary>One step of a drag. True if a cue moved. A held Ctrl lets it go free of the snap.</summary>
        public bool UpdateDrag(float pointerX, int row, bool free = false)
        {
            if (!Dragging) return false;
            bool snap = SnapOn && !free;
            float threshold = SnapPixels / View.PixelsPerSecond;
            float delta = View.T(pointerX) - m_PressTime;
            SnapLine = -1;
            List<LetteraCue> cues = Cues;
            bool moved = false;
            switch (m_DragZone)
            {
                case SequencerZone.Body:
                    {
                        float least = float.MaxValue;
                        int lowest = int.MaxValue;
                        Grip primary = m_Grips[0];
                        foreach (Grip grip in m_Grips)
                        {
                            least = Mathf.Min(least, grip.Start);
                            lowest = Mathf.Min(lowest, grip.Lane);
                            if (grip.Index == m_DragPrimary) primary = grip;
                        }
                        if (snap)
                        {
                            float span = SpanOf(cues[primary.Index]);
                            float head = primary.Start + delta, tail = head + span;
                            // Something real on the rows beats a mere tick.
                            float atHead = NearestTarget(head, out float headDistance), atTail = NearestTarget(tail, out float tailDistance);
                            if (Mathf.Min(headDistance, tailDistance) > threshold)
                            {
                                atHead = NearestTick(head, out headDistance);
                                atTail = NearestTick(tail, out tailDistance);
                            }
                            if (headDistance <= tailDistance && headDistance <= threshold)
                            {
                                delta = atHead - primary.Start;
                                SnapLine = atHead;
                            }
                            else if (tailDistance <= threshold)
                            {
                                delta = atTail - span - primary.Start;
                                SnapLine = atTail;
                            }
                        }
                        delta = Mathf.Max(delta, -least);
                        int rows = Mathf.Max(row - m_PressRow, -lowest);
                        foreach (Grip grip in m_Grips)
                        {
                            LetteraCue cue = cues[grip.Index];
                            float start = grip.Start + delta;
                            int lane = grip.Lane + rows;
                            moved |= cue.Start != start || cue.Lane != lane;
                            cue.Start = start;
                            cue.Lane = lane;
                        }
                        break;
                    }
                case SequencerZone.Start:
                    {
                        Grip grip = m_Grips[0];
                        LetteraCue cue = cues[grip.Index];
                        float start = grip.Start + delta;
                        if (snap && SnapTo(start, threshold, out float at)) start = at;
                        start = Mathf.Clamp(start, 0, grip.Start + grip.Unit - MinUnit);
                        moved = cue.Start != start;
                        cue.Start = start;
                        cue.UnitSeconds = grip.Start + grip.Unit - start;
                        break;
                    }
                case SequencerZone.End:
                    {
                        Grip grip = m_Grips[0];
                        LetteraCue cue = cues[grip.Index];
                        // The tail is what its members stagger by. The drag sets the last one's end, and the ramp gives way.
                        float tail = SpanOf(cue) - cue.UnitSeconds;
                        float end = grip.Start + grip.Unit + tail + delta;
                        if (snap && SnapTo(end, threshold, out float at)) end = at;
                        float unit = Mathf.Max(end - grip.Start - tail, MinUnit);
                        moved = cue.UnitSeconds != unit;
                        cue.UnitSeconds = unit;
                        break;
                    }
            }
            return moved;
        }

        bool SnapTo(float moment, float threshold, out float at)
        {
            at = NearestTarget(moment, out float distance);
            if (distance > threshold) at = NearestTick(moment, out distance);
            if (distance > threshold) return false;
            SnapLine = at;
            return true;
        }

        public void EndDrag()
        {
            m_DragPrimary = -1;
            m_DragZone = SequencerZone.None;
            m_Grips.Clear();
            SnapLine = -1;
        }

        // ------------------------------------------------------------- commands

        static LetteraCue Clone(LetteraCue cue) => JsonUtility.FromJson<LetteraCue>(JsonUtility.ToJson(cue));

        /// <summary>Splits at a moment what the moment falls inside, or else nothing. Why it could not is said in words; null is success.</summary>
        public string SplitSelected(float at, Func<LetteraCue, LetteraParamKind> kindOf)
        {
            List<LetteraCue> cues = Cues;
            var chosen = new List<int>(Selected.Count > 0 ? Selected : AllAt(at));
            chosen.Sort();
            string why = "The playhead is not inside a cue.";
            var made = new List<int>();
            for (int n = chosen.Count - 1; n >= 0; n--)
            {
                int index = chosen[n];
                string problem = Split(cues[index], at, kindOf(cues[index]), out LetteraCue second);
                if (problem != null)
                {
                    why = problem;
                    continue;
                }
                cues.Insert(index + 1, second);
                for (int m = 0; m < made.Count; m++) made[m]++;
                made.Add(index + 1);
            }
            if (made.Count == 0) return why;
            Selected.Clear();
            Selected.AddRange(made);
            Selected.Sort();
            return null;
        }

        List<int> AllAt(float at)
        {
            var found = new List<int>();
            List<LetteraCue> cues = Cues;
            for (int i = 0; i < cues.Count; i++)
                if (at > cues[i].Start && at < cues[i].Start + cues[i].UnitSeconds)
                    found.Add(i);
            return found;
        }

        // The two parts meet at the value it had there, so nothing jumps. A curve restarts in each part, so an eased ramp keeps its value at the cut and not its exact shape.
        internal static string Split(LetteraCue cue, float at, LetteraParamKind kind, out LetteraCue second)
        {
            second = null;
            if (at <= cue.Start + MinUnit || at >= cue.Start + cue.UnitSeconds - MinUnit) return "The playhead is not inside that cue.";
            if (cue.StepSeconds > 0) return "A staggered cue cannot be split: its ranges overlap the cut.";
            float eased = LetteraEntrance.Eased(cue.Curve, (at - cue.Start) / cue.UnitSeconds);
            LetteraCue after = Clone(cue);
            switch (kind)
            {
                case LetteraParamKind.Float:
                case LetteraParamKind.Int:
                    {
                        float there = Mathf.LerpUnclamped(cue.From.Number, cue.To.Number, eased), end = cue.To.Number;
                        if (cue.Mix == LetteraMix.Multiply && Mathf.Approximately(there, 0)) return "A multiplying cue cannot be split where it stands at 0.";
                        cue.To.Number = there;
                        // An adding cue goes on holding its end, so the second part only adds what is left.
                        after.From.Number = cue.Mix == LetteraMix.Replace ? there : cue.Mix == LetteraMix.Add ? 0 : 1;
                        after.To.Number = cue.Mix == LetteraMix.Replace ? end : cue.Mix == LetteraMix.Add ? end - there : end / there;
                        break;
                    }
                case LetteraParamKind.Color:
                    {
                        if (cue.Mix != LetteraMix.Replace) return "Only a replacing color cue can be split.";
                        Color32 there = Color32.LerpUnclamped(cue.From.Color, cue.To.Color, eased);
                        cue.To.Color = there;
                        after.From.Color = there;
                        break;
                    }
                default:
                    if (eased < 0.5f)
                    {
                        // The change is still to come, and it stays in the second part.
                        cue.To = cue.From;
                    }
                    else after.From = after.To;
                    break;
            }
            float unit = cue.UnitSeconds;
            cue.UnitSeconds = at - cue.Start;
            after.Start = at;
            after.UnitSeconds = unit - cue.UnitSeconds;
            second = after;
            return null;
        }

        /// <summary>Copies stand right after their originals and become the selection.</summary>
        public void Duplicate()
        {
            List<LetteraCue> cues = Cues;
            var made = new List<int>();
            foreach (int index in Selected)
            {
                LetteraCue copy = Clone(cues[index]);
                copy.Start = cues[index].Start + SpanOf(cues[index]);
                cues.Add(copy);
                made.Add(cues.Count - 1);
            }
            Selected.Clear();
            Selected.AddRange(made);
        }

        public string CopyText()
        {
            var bundle = new Bundle();
            foreach (int index in Selected) bundle.Cues.Add(Cues[index]);
            return ClipPrefix + JsonUtility.ToJson(bundle);
        }

        internal static bool CanPaste(string text) => text != null && text.StartsWith(ClipPrefix, StringComparison.Ordinal);

        /// <summary>The earliest lands on this moment, and the rest keep their distances. False when the text holds no cues.</summary>
        public bool Paste(string text, float at)
        {
            if (!CanPaste(text)) return false;
            Bundle bundle;
            try
            {
                bundle = JsonUtility.FromJson<Bundle>(text.Substring(ClipPrefix.Length));
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (bundle?.Cues == null || bundle.Cues.Count == 0) return false;
            float earliest = float.MaxValue;
            foreach (LetteraCue cue in bundle.Cues) earliest = Mathf.Min(earliest, cue.Start);
            Selected.Clear();
            List<LetteraCue> cues = Cues;
            foreach (LetteraCue cue in bundle.Cues)
            {
                cue.Start += at - earliest;
                cues.Add(cue);
                Selected.Add(cues.Count - 1);
            }
            return true;
        }

        /// <summary>All of them go silent unless all of them already are.</summary>
        public void ToggleMute()
        {
            bool anyLoud = false;
            foreach (int index in Selected) anyLoud |= !Cues[index].Muted;
            foreach (int index in Selected) Cues[index].Muted = anyLoud;
        }

        public void Delete()
        {
            var order = new List<int>(Selected);
            order.Sort();
            for (int n = order.Count - 1; n >= 0; n--) Cues.RemoveAt(order[n]);
            Selected.Clear();
        }

        /// <summary>A row nothing stands on at this moment, for a new cue.</summary>
        public int FreeRow(float at)
        {
            for (int row = 0; ; row++)
            {
                bool taken = false;
                foreach (LetteraCue cue in Cues)
                    taken |= cue.Lane == row && at < cue.Start + SpanOf(cue) && at + 1 > cue.Start;
                if (!taken) return row;
            }
        }

        public int RowCount
        {
            get
            {
                int rows = 1;
                foreach (LetteraCue cue in Cues) rows = Mathf.Max(rows, cue.Lane + 1);
                return rows;
            }
        }

        /// <summary>The end of the last cue, muted ones too, since they are drawn.</summary>
        public float ContentEnd
        {
            get
            {
                float end = 0;
                foreach (LetteraCue cue in Cues) end = Mathf.Max(end, cue.Start + SpanOf(cue));
                return end;
            }
        }
    }
}
