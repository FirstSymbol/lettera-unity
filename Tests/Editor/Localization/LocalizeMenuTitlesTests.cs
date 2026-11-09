using System.Collections.Generic;
using FirstSymbol.Lettera.Localization.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Localization.Editor.Tests
{
    /// <summary>Menu titles from a string table, following the table of the language selected.</summary>
    public sealed class LocalizeMenuTitlesTests
    {
        readonly List<Object> m_Made = new List<Object>();
        GameObject m_Object;
        LetteraLocalizeMenuTitles m_Titles;

        [SetUp]
        public void SetUp()
        {
            // A project with no localization settings: Unity's own code says so in the log.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            LetteraEditCommand.TitleSource = null;
            m_Object = new GameObject("Menu Titles");
            m_Titles = m_Object.AddComponent<LetteraLocalizeMenuTitles>();
        }

        [TearDown]
        public void TearDown()
        {
            m_Titles.Leave();
            Object.DestroyImmediate(m_Object);
            foreach (Object made in m_Made) Object.DestroyImmediate(made);
            m_Made.Clear();
            LetteraEditCommand.TitleSource = null;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        StringTable Table(string locale, params string[] keysAndTitles)
        {
            var shared = ScriptableObject.CreateInstance<SharedTableData>();
            shared.TableCollectionName = "Menu";
            var table = ScriptableObject.CreateInstance<StringTable>();
            table.SharedData = shared;
            table.LocaleIdentifier = new LocaleIdentifier(locale);
            for (int i = 0; i + 1 < keysAndTitles.Length; i += 2) table.AddEntry(keysAndTitles[i], keysAndTitles[i + 1]);
            m_Made.Add(shared);
            m_Made.Add(table);
            return table;
        }

        [Test]
        public void TitlesFollowTheTableOfTheSelectedLanguage()
        {
            var copy = new CopyCommand();
            var plain = new PastePlainCommand();
            var bold = new ToggleEffectCommand();
            m_Titles.Join();
            Assert.AreEqual("Copy", copy.Title, "nothing loaded yet: the title it had");

            m_Titles.Use(Table("de", "Copy", "Kopieren", "PastePlain", "Als reinen Text einfügen", "Bold", "Fett"));
            Assert.AreEqual("Kopieren", copy.Title);
            Assert.AreEqual("Als reinen Text einfügen", plain.Title);
            Assert.AreEqual("Fett", bold.Title, "a style's command by the style's name");

            m_Titles.Use(Table("fr", "Copy", "Copier"));
            Assert.AreEqual("Copier", copy.Title, "a new language's table");
            Assert.AreEqual("Paste as plain text", plain.Title, "a key the table lacks keeps its title");
        }

        [Test]
        public void ATitleWrittenOnACommandIsItsKey()
        {
            var command = new CopyCommand { Title = "Copy link" };
            Assert.AreEqual("Copy link", command.TitleKey);
            m_Titles.Join();
            m_Titles.Use(Table("de", "Copy", "Kopieren", "Copy link", "Link kopieren"));
            Assert.AreEqual("Link kopieren", command.Title);
        }

        [Test]
        public void APrefixGoesBeforeEachKey()
        {
            m_Titles.KeyPrefix = "Menu/";
            m_Titles.Join();
            m_Titles.Use(Table("de", "Copy", "Falsch", "Menu/Copy", "Kopieren"));
            Assert.AreEqual("Kopieren", new CopyCommand().Title);
        }

        [Test]
        public void LeavingGivesBackWhatAnsweredBefore()
        {
            LetteraEditCommand.TitleSource = c => c is CutCommand ? "Before" : null;
            m_Titles.Join();
            m_Titles.Use(Table("de", "Copy", "Kopieren"));
            Assert.AreEqual("Kopieren", new CopyCommand().Title);
            Assert.AreEqual("Before", new CutCommand().Title, "what this table lacks, the earlier source still answers");
            m_Titles.Leave();
            Assert.AreEqual("Copy", new CopyCommand().Title);
            Assert.AreEqual("Before", new CutCommand().Title);
        }

        [Test]
        public void TheInspectorNamesTheKeys()
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(m_Titles);
            try
            {
                var labels = new List<string>();
                editor.CreateInspectorGUI().Query<Label>().ForEach(l => labels.Add(l.text));
                Assert.IsTrue(labels.Exists(l => l.Contains("Cut, Copy, Paste, PastePlain, Select, SelectAll")), string.Join(" | ", labels));
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
