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
            if (root.renderMode != RenderMode.WorldSpace)
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
            bool banded = Engine.IsBanded && isActiveAndEnabled;
            if (banded == m_InBanded) return;
            m_InBanded = banded;
            if (banded)
            {
                s_Banded.Add(this);
                if (!s_BandHooked)
                {
                    s_BandHooked = true;
                    Canvas.preWillRenderCanvases += CheckBands;
                }
            }
            else
            {
                s_Banded.Remove(this);
            }
        }

        void LeaveBanded()
        {
            if (!m_InBanded) return;
            m_InBanded = false;
            s_Banded.Remove(this);
        }

        internal static void CheckBandsNow() => CheckBands();

        // Before the canvases rebuild, so a text scrolled past its band draws the new lines in the same frame.
        static void CheckBands()
        {
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
