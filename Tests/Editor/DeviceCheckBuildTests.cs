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
    }
}
