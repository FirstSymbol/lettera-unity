using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What Lettera adds to the Scene view, by this user's choice: outlines on a selected text, and text that goes when the Hierarchy's eye hides its object.</summary>
    [InitializeOnLoad]
    internal static class LetteraSceneOptions
    {
        const string OutlinesKey = "Lettera.Scene.Outlines", VisibilityKey = "Lettera.Scene.Visibility";
        const string OutlinesMenu = "Tools/FirstSymbol/Lettera/Scene/Outlines", VisibilityMenu = "Tools/FirstSymbol/Lettera/Scene/Follow The Hierarchy's Eye";

        static LetteraSceneOptions()
        {
            SceneVisibilityManager.visibilityChanged += Refresh;
            EditorApplication.delayCall += Refresh;
        }

        internal static bool Outlines
        {
            get => LtPrefs.GetBool(OutlinesKey, true);
            set => LtPrefs.SetBool(OutlinesKey, value);
        }

        internal static bool FollowVisibility
        {
            get => LtPrefs.GetBool(VisibilityKey, true);
            set
            {
                LtPrefs.SetBool(VisibilityKey, value);
                Refresh();
            }
        }

        [MenuItem(OutlinesMenu, false, 70)]
        static void ToggleOutlines()
        {
            Outlines = !Outlines;
            SceneView.RepaintAll();
        }

        [MenuItem(OutlinesMenu, true)]
        static bool CheckOutlines()
        {
            Menu.SetChecked(OutlinesMenu, Outlines);
            return true;
        }

        [MenuItem(VisibilityMenu, false, 71)]
        static void ToggleVisibility() => FollowVisibility = !FollowVisibility;

        [MenuItem(VisibilityMenu, true)]
        static bool CheckVisibility()
        {
            Menu.SetChecked(VisibilityMenu, FollowVisibility);
            return true;
        }

        // Batched world text draws through renderers of its own, which the eye knows nothing of. Standalone and Canvas text is the editor's own business.
        internal static void Refresh()
        {
            var hidden = new HashSet<LetteraWorldText>();
            if (FollowVisibility && !Application.isPlaying)
            {
                foreach (LetteraWorldText text in LetteraWorldBatcher.Texts)
                    if (text != null && SceneVisibilityManager.instance.IsHidden(text.gameObject))
                        hidden.Add(text);
            }
            LetteraWorldText.SetHiddenInScene(hidden);
        }
    }
}
