using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    // A long text places only the lines near what can show. What can show is the root canvas on screen, cut by a mask's clip;
    // each frame, before the canvases draw, a text whose placed lines no longer cover that places them again.
    public partial class LetteraText
    {
        static readonly List<LetteraText> s_Banded = new List<LetteraText>();
        static bool s_BandHooked;
        [System.NonSerialized] bool m_InBanded;
        static readonly Vector3[] s_Corners = new Vector3[4];

        // Local y of what of this text can show. False when anything may: a world canvas with no mask, or no canvas yet.
        bool VisibleBand(out float low, out float high)
        {
            low = high = 0;
            Canvas own = canvas;
            if (own == null) return false;
            Canvas root = own.rootCanvas;
            if (root == null) return false;
            bool has = false;
            Rect area = default;
            if (root.renderMode != UnityEngine.RenderMode.WorldSpace)
            {
                area = ((RectTransform)root.transform).rect;
                has = true;
            }
            if (m_ParentClipValid)
            {
                area = has ? Rect.MinMaxRect(Mathf.Max(area.xMin, m_ParentClip.xMin), Mathf.Max(area.yMin, m_ParentClip.yMin), Mathf.Min(area.xMax, m_ParentClip.xMax), Mathf.Min(area.yMax, m_ParentClip.yMax)) : m_ParentClip;
                has = true;
            }
            if (!has) return false;
            if (area.width <= 0 || area.height <= 0)
            {
                // Nothing of it shows: a band of no height places nothing.
                low = high = float.MaxValue;
                return true;
            }
            Matrix4x4 toLocal = transform.worldToLocalMatrix * root.transform.localToWorldMatrix;
            s_Corners[0] = new Vector3(area.xMin, area.yMin);
            s_Corners[1] = new Vector3(area.xMax, area.yMin);
            s_Corners[2] = new Vector3(area.xMin, area.yMax);
            s_Corners[3] = new Vector3(area.xMax, area.yMax);
            low = float.MaxValue;
            high = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float y = toLocal.MultiplyPoint3x4(s_Corners[i]).y;
                if (y < low) low = y;
                if (y > high) high = y;
            }
            return !float.IsNaN(low) && !float.IsNaN(high);
        }

        void PushVisible()
        {
            TextEngine e = Engine;
            if (!e.MayBand)
            {
                e.SetVisible(false, 0, 0);
                return;
            }
            bool has = VisibleBand(out float low, out float high);
            e.SetVisible(has, low, high);
        }

        void AfterBandRebuild()
        {
            JoinFinishing();
            bool banded = Engine.IsBanded && isActiveAndEnabled;
            if (banded == m_InBanded) return;
            m_InBanded = banded;
            if (banded)
            {
                s_Banded.Add(this);
                HookBands();
            }
            else
            {
                s_Banded.Remove(this);
            }
        }

        static void HookBands()
        {
            if (s_BandHooked) return;
            s_BandHooked = true;
            Canvas.preWillRenderCanvases += CheckBands;
        }

        void LeaveBanded()
        {
            LeaveFinishing();
            if (!m_InBanded) return;
            m_InBanded = false;
            s_Banded.Remove(this);
        }

        internal static void CheckBandsNow() => CheckBands();

        // ------------------------------------------------------------- paragraphs left for later

        static readonly List<LetteraText> s_Finishing = new List<LetteraText>();
        [System.NonSerialized] bool m_InFinishing;
        [System.NonSerialized] int m_SeenEstimate;

        void JoinFinishing()
        {
            if (m_InFinishing || Engine.PendingParagraphs == 0 || !isActiveAndEnabled) return;
            m_InFinishing = true;
            m_SeenEstimate = Engine.EstimateVersion;
            s_Finishing.Add(this);
            HookBands();
        }

        void LeaveFinishing()
        {
            if (!m_InFinishing) return;
            m_InFinishing = false;
            s_Finishing.Remove(this);
        }

        /// <summary>Whether this text still has paragraphs left for later frames, and so a height that is an estimate.</summary>
        public bool IsFinishing => Engine.PendingParagraphs > 0;

        /// <summary>Shapes and lays out every paragraph left for later now, so that its height is exact.</summary>
        public void FinishNow()
        {
            if (Engine.PendingParagraphs == 0) return;
            Engine.EnsurePositioned();
            MarkDirty(DirtyLevel.Mesh);
            if (IsActive()) SetLayoutDirty();
        }

        internal static void FinishSomeNow() => FinishSome();

        // Each frame finishes a slice of what waits, in all texts together, within the time the settings give it.
        static void FinishSome()
        {
            if (s_Finishing.Count == 0) return;
            long deadline = System.Diagnostics.Stopwatch.GetTimestamp() +
                            (long)(Mathf.Max(LetteraLongText.FinishMillisecondsPerFrame, 0) * 0.001 * System.Diagnostics.Stopwatch.Frequency);
            bool first = true;
            for (int i = 0; i < s_Finishing.Count; i++)
            {
                LetteraText text = s_Finishing[i];
                if (text == null)
                {
                    s_Finishing.RemoveAt(i--);
                    continue;
                }
                TextEngine e = text.Engine;
                if (e.PendingParagraphs > 0 && (first || System.Diagnostics.Stopwatch.GetTimestamp() < deadline))
                {
                    first = false;
                    if (e.FinishStep(deadline)) text.MarkDirty(DirtyLevel.Positions);
                }
                // A query may have finished it meanwhile, which placed it again.
                if (e.Dirty >= DirtyLevel.Mesh) text.SetVerticesDirty();
                if (e.EstimateVersion != text.m_SeenEstimate)
                {
                    text.m_SeenEstimate = e.EstimateVersion;
                    text.SetLayoutDirty();
                }
                if (e.PendingParagraphs > 0) continue;
                text.m_InFinishing = false;
                s_Finishing.RemoveAt(i--);
            }
        }

        // Before the canvases rebuild, so a text scrolled past its band draws the new lines in the same frame.
        static void CheckBands()
        {
            FinishSome();
            for (int i = s_Banded.Count - 1; i >= 0; i--)
            {
                if (i >= s_Banded.Count) continue;
                LetteraText text = s_Banded[i];
                if (text == null)
                {
                    s_Banded.RemoveAt(i);
                    continue;
                }
                TextEngine e = text.Engine;
                if (!e.IsBanded || e.Dirty >= DirtyLevel.Positions) continue;
                bool has = text.VisibleBand(out float low, out float high);
                if (has && e.BandCovers(low, high)) continue;
                text.MarkDirty(DirtyLevel.Positions);
            }
        }
    }
}
