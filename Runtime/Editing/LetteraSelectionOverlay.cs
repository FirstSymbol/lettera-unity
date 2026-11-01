using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Solid rectangles in the text's own local space.</summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class LetteraSelectionOverlay : MaskableGraphic
    {
        readonly List<Rect> m_Rects = new List<Rect>();
        readonly List<Color> m_Colors = new List<Color>();

        public void Begin()
        {
            m_Rects.Clear();
            m_Colors.Clear();
        }

        public void Add(Rect rect, Color tint)
        {
            m_Rects.Add(rect);
            m_Colors.Add(tint);
        }

        public void End() => SetVerticesDirty();

        internal int RectCount => m_Rects.Count;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i < m_Rects.Count; i++)
            {
                Rect r = m_Rects[i];
                Color32 c = m_Colors[i];
                int start = vh.currentVertCount;
                vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector4.zero);
                vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector4.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector4.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector4.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }
    }
}
