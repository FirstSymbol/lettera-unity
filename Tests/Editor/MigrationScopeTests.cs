using System.IO;
using System.Linq;
using FirstSymbol.Lettera.Editor.Migration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>What the migration leaves alone, kept per project, and the folder tree that narrows its findings.</summary>
    public class MigrationScopeTests
    {
        string m_Root, m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "LetteraMigrationScope").Replace('\\', '/');
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
            Directory.CreateDirectory(m_Root + "/UI/Menus");
            Directory.CreateDirectory(m_Root + "/Game");
            File.WriteAllText(m_Root + "/UI/Menus/Title.cs", "using TMPro;\nclass Title { TextMeshProUGUI label; }\n");
            File.WriteAllText(m_Root + "/UI/Score.cs", "using TMPro;\nclass Score { TextMeshProUGUI label; }\n");
            File.WriteAllText(m_Root + "/Game/Hud.cs", "using TMPro;\nclass Hud { TextMeshProUGUI label; }\n");
            File.WriteAllText(m_Root + "/Game/Plain.cs", "class Plain { }\n");
            m_Settings = m_Root + "/Settings/Migration.json";
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        LetteraMigrationRun NewRun() => new LetteraMigrationRun(new LetteraMigrationExclusions(m_Settings));

        [Test]
        public void AnExcludedFileIsRememberedAndSkipped()
        {
            LetteraMigrationRun run = NewRun();
            run.Scan(m_Root);
            Assert.AreEqual(3, run.Findings.Count);
            LetteraTmpFinding score = run.Findings.Single(f => f.Path.EndsWith("/UI/Score.cs"));
            run.Exclude(score);
            Assert.IsTrue(score.Excluded);
            Assert.IsTrue(File.Exists(m_Settings), "kept in a file of the project");
            StringAssert.Contains("Score.cs", File.ReadAllText(m_Settings));

            // A later run, as after a restart, reads it back.
            LetteraMigrationRun later = NewRun();
            Assert.IsTrue(later.Exclusions.Covers(score.Path));
            later.Scan(m_Root);
            Assert.AreEqual(3, later.Findings.Count, "still listed, so that it can be taken back");
            Assert.IsTrue(later.Findings.Single(f => f.Path == score.Path).Excluded);
            var previews = later.PreviewScripts();
            Assert.AreEqual(2, previews.Count);
            Assert.IsFalse(previews.Any(p => p.path == score.Path), "no stage touches it");

            later.Include(score.Path);
            Assert.IsFalse(later.Findings.Single(f => f.Path == score.Path).Excluded);
            Assert.AreEqual(3, later.PreviewScripts().Count);
            Assert.AreEqual(0, NewRun().Exclusions.Paths.Count);
        }

        [Test]
        public void AnExcludedFolderIsNotScanned()
        {
            LetteraMigrationRun run = NewRun();
            run.Scan(m_Root);
            run.ExcludeFolder(m_Root + "/UI");
            Assert.AreEqual(2, run.Findings.Count(f => f.Excluded), "what it holds is excluded at once");
            Assert.AreEqual(1, run.PreviewScripts().Count);
            run.Scan(m_Root);
            Assert.AreEqual(1, run.Findings.Count);
            StringAssert.EndsWith("/Game/Hud.cs", run.Findings[0].Path);
            run.Include(m_Root + "/UI");
            run.Scan(m_Root);
            Assert.AreEqual(3, run.Findings.Count);
        }

        [Test]
        public void ExclusionsCoverWhatIsUnderThem()
        {
            var exclusions = new LetteraMigrationExclusions(null);
            exclusions.Add("Assets/UI/");
            Assert.IsTrue(exclusions.Covers("Assets/UI"));
            Assert.IsTrue(exclusions.Covers("Assets/UI/Menus/A.prefab"));
            Assert.IsTrue(exclusions.Covers("Assets\\UI\\B.unity"));
            Assert.IsFalse(exclusions.Covers("Assets/UIKit/A.prefab"), "a name that only starts the same");
            Assert.IsFalse(exclusions.Covers("Assets/Game/A.prefab"));
            Assert.IsTrue(exclusions.Contains("Assets/UI"));
            Assert.IsFalse(exclusions.Contains("Assets/UI/Menus/A.prefab"));
        }

        [Test]
        public void TheTreeCountsAndNarrows()
        {
            string[] files = { "Assets/UI/Menus/Title.prefab", "Assets/UI/Score.prefab", "Assets/Game/Hud.unity", "Assets/Game.unity" };
            LetteraFolderTree tree = LetteraFolderTree.Build(files);
            Assert.AreEqual(1, tree.Roots.Count);
            LetteraFolderTree.Node assets = tree.Roots[0];
            Assert.AreEqual(4, assets.Count);
            CollectionAssert.AreEqual(new[] { "Game", "UI" }, assets.Children.Select(c => c.Name).ToArray());
            LetteraFolderTree.Node ui = assets.Children[1];
            Assert.AreEqual(2, ui.Count);
            Assert.AreEqual("Assets/UI/Menus", ui.Children[0].Path);
            Assert.AreEqual(1, ui.Children[0].Count);
            Assert.AreEqual(2, files.Count(f => LetteraFolderTree.InFolder(f, "Assets/UI")));
            Assert.AreEqual(4, files.Count(f => LetteraFolderTree.InFolder(f, "")));
            Assert.IsFalse(LetteraFolderTree.InFolder("Assets/UIKit/A.prefab", "Assets/UI"));
        }

        [Test]
        public void TheWindowNarrowsByFolderAndExcludesFromARow()
        {
            LetteraMigrationRun run = NewRun();
            run.Scan(m_Root);
            var window = ScriptableObject.CreateInstance<LetteraMigrationWindow>();
            try
            {
                window.UseRun(run);
                typeof(LetteraMigrationWindow).GetMethod("CreateGUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(window, null);
                VisualElement root = window.rootVisualElement;
                Assert.IsNotNull(root.Q(className: "lt-root"));
                Assert.IsNull(root.Q<IMGUIContainer>());
                Assert.IsNotNull(root.Q("lt-tree-" + m_Root + "/UI"), "the tree shows the folder");
                Assert.AreEqual(3, root.Query(className: "lt-finding").ToList().Count);

                window.Folder = m_Root + "/UI";
                Assert.AreEqual(2, root.Query(className: "lt-finding").ToList().Count, "narrowed to a folder");
                Assert.IsTrue(root.Q("lt-tree-" + m_Root + "/UI").ClassListContains("lt-tree__row--on"));

                Assert.AreEqual(2, root.Query<Label>(className: "lt-icon-btn").ToList().Count(l => l.text == "Exclude"), "each row offers it");
                run.Exclude(run.Findings.First(f => LetteraFolderTree.InFolder(f.Path, m_Root + "/UI")));
                window.UseRun(run);
                Assert.AreEqual(1, run.Exclusions.Paths.Count);
                Assert.AreEqual(1, root.Query(className: "lt-finding--excluded").ToList().Count);
                Assert.AreEqual(1, root.Q(className: "lt-findings").Query<Label>().ToList().Count(l => l.text == "Include"), "the row offers the way back");
                Assert.IsTrue(root.Q(className: "lt-excluded").Query<Label>().ToList().Any(l => l.text == run.Exclusions.Paths[0]), "and listed with a way back");

                window.Folder = "";
                Assert.AreEqual(3, root.Query(className: "lt-finding").ToList().Count);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
