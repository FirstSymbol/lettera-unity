using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEditor;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>With LETTERA_TEST_CULTURE set (for example ru-RU or de-DE), the editor and every test run in that culture, so a whole run shows what a decimal comma changes.</summary>
    public class TestCulture
    {
        [InitializeOnLoadMethod]
        static void Apply()
        {
            string name = Environment.GetEnvironmentVariable("LETTERA_TEST_CULTURE");
            if (string.IsNullOrEmpty(name)) return;
            var culture = CultureInfo.GetCultureInfo(name);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
        }

        [Test]
        public void TheRunUsesTheCultureAskedFor()
        {
            string name = Environment.GetEnvironmentVariable("LETTERA_TEST_CULTURE");
            if (string.IsNullOrEmpty(name)) Assert.Pass("no culture asked for");
            Assert.AreEqual(name, CultureInfo.CurrentCulture.Name);
            Assert.AreEqual(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        }
    }
}
