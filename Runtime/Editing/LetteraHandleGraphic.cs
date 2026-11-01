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
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color32 c = new Color(0.2f, 0.565f, 1f, 1f);
            const int Sides = 16;
            float radius = Mathf.Min(r.width, r.height) * 0.22f;
            var center = new Vector2(r.center.x, r.yMax - radius * 1.2f);
            vh.AddVert(center, c, Vector4.zero);
            for (int i = 0; i <= Sides; i++)
            {
                float angle = i * Mathf.PI * 2 / Sides;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, c, Vector4.zero);
            }
            for (int i = 1; i <= Sides; i++) vh.AddTriangle(0, i, i + 1);
        }

        // The whole rect takes touches, though the drop is smaller.
        public override bool Raycast(Vector2 sp, Camera eventCamera) => true;
    }
}
