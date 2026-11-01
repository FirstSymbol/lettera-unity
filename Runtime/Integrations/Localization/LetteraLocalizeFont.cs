using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

namespace FirstSymbol.Lettera.Localization
{
    /// <summary>
    /// A font per locale, from an asset table: a font or a font stack, for a text on this object.
    /// It applies in play mode only, so that a scene keeps the font it was saved with. An entry with nothing in it goes back to that font.
    /// </summary>
    [AddComponentMenu("Lettera/Localize Font")]
    [DisallowMultipleComponent]
    public class LetteraLocalizeFont : LocalizedAssetBehaviour<Object, LocalizedObject>
    {
        LetteraFontAsset m_SavedFont;
        LetteraFontStack m_SavedStack;
        bool m_Kept;

        protected override void UpdateAsset(Object asset)
        {
            if (!Application.isPlaying) return;
            var host = GetComponent<ITextHost>();
            if (host == null) return;
            LetteraTextData data = host.Data;
            if (!m_Kept)
            {
                m_SavedFont = data.Font;
                m_SavedStack = data.FontStack;
                m_Kept = true;
            }
            switch (asset)
            {
                case LetteraFontAsset font:
                    data.Font = font;
                    data.FontStack = null;
                    break;
                case LetteraFontStack stack:
                    data.Font = null;
                    data.FontStack = stack;
                    break;
                default:
                    data.Font = m_SavedFont;
                    data.FontStack = m_SavedStack;
                    break;
            }
            host.RequestRebuild(DirtyLevel.Shape);
        }
    }
}
