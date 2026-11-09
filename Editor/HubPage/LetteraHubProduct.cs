using System;
using System.Collections.Generic;
using FirstSymbol.Hub;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.HubPage
{
    /// <summary>How Lettera appears in the FirstSymbol Hub. This assembly compiles only when the hub is installed; Lettera does not need it.</summary>
    internal sealed class LetteraHubProduct : HubProduct
    {
        public override string PackageName => "com.firstsymbol.lettera";
        public override string DisplayName => "Lettera";
        public override string Description => "A text engine for Unity UI and world space: OpenType shaping, bidirectional text, color emoji, one shared SDF atlas, markup styles, formulas and solid 3D text.";
        public override string AccentHex => "#9b6dff";
        public override string SettingsPath => "Project/FirstSymbol/Lettera";

        public override IEnumerable<HubStatusRow> StatusRows()
        {
            var settings = Resources.Load<LetteraSettings>(LetteraSettings.ResourceName);
            if (settings == null)
            {
                yield return new HubStatusRow("Settings asset", "none yet: Lettera uses its defaults", HubTone.Normal);
                yield break;
            }
            int active = 0, total = 0;
            var off = new List<string>();
            foreach (LetteraNativeFeature feature in Enum.GetValues(typeof(LetteraNativeFeature)))
            {
                total++;
                if (LetteraPlatform.IsActive(feature)) active++;
                else off.Add(feature.ToString());
            }
            yield return new HubStatusRow("Native routes", active + " of " + total + " on for this platform", active > 0 ? HubTone.Good : HubTone.Normal);
            if (off.Count > 0) yield return new HubStatusRow("Routes off", string.Join(", ", off));
            LetteraAtlasSettings atlas = settings.Atlas;
            yield return new HubStatusRow("Atlas page size", "distance field " + Size(atlas.SdfPageSize) + ", color " + Size(atlas.ColorPageSize) + ", multi-channel " + Size(atlas.MsdfPageSize));
        }

        static string Size(LetteraAtlasPageSize size) => size == LetteraAtlasPageSize.Size1024 ? "1024" : "2048";

        public override void ApplyTheme(VisualElement pageRoot, bool light)
        {
            foreach (VisualElement root in pageRoot.Query<VisualElement>(className: "lt-root").ToList())
                root.EnableInClassList("lt--light", light);
        }
    }
}
