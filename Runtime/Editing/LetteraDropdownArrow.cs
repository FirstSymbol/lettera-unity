using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>A dropdown's arrow, drawn: as a character it needed a font with that character, and the web's default has none.</summary>
    [AddComponentMenu("")]
    public class LetteraDropdownArrow : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 center = rectTransform.rect.center;
            const float Half = 5, Height = 3.5f;
            vh.AddVert(center + new Vector2(-Half, Height), color, Vector2.zero);
            vh.AddVert(center + new Vector2(Half, Height), color, Vector2.zero);
            vh.AddVert(center + new Vector2(0, -Height), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
