using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>A project without Unity Localization has none of Lettera's part of it: no assembly, no menu, no chip.</summary>
    public sealed class LocalizationAbsentTests
    {
        static bool Installed => UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.localization") != null;

        [Test]
        public void NothingCompilesOrAppears()
        {
            if (Installed) Assert.Ignore("This project has the package; the tests beside the assembly cover it.");
            foreach (string name in new[] { "FirstSymbol.Lettera.Localization", "FirstSymbol.Lettera.Localization.Editor" })
                Assert.IsFalse(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name), name);
            CollectionAssert.IsEmpty(FirstSymbol.Lettera.Editor.LetteraEditingMenus.ExtraChips);
            Assert.IsFalse(Unsupported.GetSubmenus("CONTEXT/LetteraText").Any(m => m.EndsWith("/Localize")));
            var go = new GameObject("Text", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<LetteraText>();
                VisualElement card = FirstSymbol.Lettera.Editor.LetteraEditingMenus.InteractionCard(new UnityEngine.Object[] { text });
                Assert.IsFalse(card.Query<Label>().ToList().Any(l => l.text.Contains("Localize")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
