using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class DeviceCheckBuildTests
    {
        // Seen on an Android phone: in a project with no settings asset the device check ran with every native route off, since what was turned
        // on lived only in the editor's memory.
        [Test]
        public void ABuildSeesEveryRouteOnEvenWithNoSettingsAsset()
        {
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(LetteraSettings.Instance))) Assert.Ignore("This project has a settings asset of its own.");
            LetteraGate before = LetteraSettings.Instance.Platform.TextInput;
            LetteraSettings seen = null;
            LetteraDeviceCheckMenu.WithNativeOn(() => seen = Resources.Load<LetteraSettings>(LetteraSettings.ResourceName));
            try
            {
                Assert.IsNotNull(seen, "a player would find settings where it looks");
                Assert.AreEqual(LetteraGate.On, seen.Platform.TextInput);
                Assert.AreEqual(LetteraGate.On, seen.Platform.SystemMenu);
                Assert.AreEqual(LetteraMenuChoice.System, seen.Editing.AndroidMenu);
            }
            finally
            {
                AssetDatabase.DeleteAsset("Assets/LetteraDeviceCheckBuild");
            }
            Assert.IsFalse(AssetDatabase.LoadAssetAtPath<LetteraSettings>(LetteraDeviceCheckMenu.TemporarySettingsPath), "nothing is left behind");
            Assert.AreEqual(before, LetteraSettings.Instance.Platform.TextInput, "the editor's own settings are as they were");
        }

        // Seen on an iPhone: with no math font set, a web build drew the root as nothing and the integral as an empty box.
        [Test]
        public void ABuildCarriesTheBundledMathFontWhenNoneIsSet()
        {
#if !LETTERA_NO_MATH
            LetteraFontAsset bundled = LetteraMathMenu.BundledFont();
            if (bundled == null) Assert.Ignore("The bundled math font is not in this package.");
            if (LetteraSettings.Instance.Math.Font != null) Assert.Ignore("This project sets a math font of its own.");
            LetteraFontAsset seen = null;
            LetteraDeviceCheckMenu.WithNativeOn(() => seen = LetteraSettings.Instance.Math.Font ?? Resources.Load<LetteraSettings>(LetteraSettings.ResourceName)?.Math.Font);
            AssetDatabase.DeleteAsset("Assets/LetteraDeviceCheckBuild");
            Assert.AreSame(bundled, seen);
            Assert.IsNull(LetteraSettings.Instance.Math.Font, "the editor's own settings are as they were");
#endif
        }
    }
}
