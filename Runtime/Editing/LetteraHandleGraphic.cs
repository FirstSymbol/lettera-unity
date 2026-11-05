using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>A droplet at the top of an invisible target as wide as a finger.</summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class LetteraHandleGraphic : MaskableGraphic
    {
        internal enum Shape { Caret, Start, End }

        /// <summary>A selection's start points up and right at its end of the text, its end up and left, and a caret's straight up, as Android
        /// draws them: so the two of a short selection stand apart instead of on top of each other.</summary>
        internal Shape Kind;

        /// <summary>Where on the rect the point is, which is where the handle is placed.</summary>
        internal Vector2 Pivot => Kind == Shape.Start ? new Vector2(1, 1) : Kind == Shape.End ? new Vector2(0, 1) : new Vector2(0.5f, 1);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color32 c = new Color(0.2f, 0.565f, 1f, 1f);
            const int Sides = 20;
            float radius = Mathf.Min(r.width, r.height) * 0.22f;
            Vector2 center, tip;
            switch (Kind)
            {
                case Shape.Start:
                    tip = new Vector2(r.xMax, r.yMax);
                    center = tip - new Vector2(radius, radius);
                    break;
                case Shape.End:
                    tip = new Vector2(r.xMin, r.yMax);
                    center = tip + new Vector2(radius, -radius);
                    break;
                default:
                    tip = new Vector2(r.center.x, r.yMax);
                    center = tip - new Vector2(0, radius * Mathf.Sqrt(2));
                    break;
            }
            vh.AddVert(center, c, Vector4.zero);
            for (int i = 0; i <= Sides; i++)
            {
                float angle = i * Mathf.PI * 2 / Sides;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, c, Vector4.zero);
            }
            for (int i = 1; i <= Sides; i++) vh.AddTriangle(0, i, i + 1);
            // The point: the corner of the square around the circle that faces the text. Its two sides leave the circle 45 degrees either side.
            int at = vh.currentVertCount;
            Vector2 u = (tip - center).normalized;
            const float Cos45 = 0.70710678f;
            var left = new Vector2(u.x * Cos45 - u.y * Cos45, u.x * Cos45 + u.y * Cos45);
            var right = new Vector2(u.x * Cos45 + u.y * Cos45, -u.x * Cos45 + u.y * Cos45);
            vh.AddVert(center, c, Vector4.zero);
            vh.AddVert(center + left * radius, c, Vector4.zero);
            vh.AddVert(tip, c, Vector4.zero);
            vh.AddVert(center + right * radius, c, Vector4.zero);
            vh.AddTriangle(at, at + 1, at + 2);
            vh.AddTriangle(at, at + 2, at + 3);
        }

        // The whole rect takes touches, though the drop is smaller.
        public override bool Raycast(Vector2 sp, Camera eventCamera) => true;
    }
}
