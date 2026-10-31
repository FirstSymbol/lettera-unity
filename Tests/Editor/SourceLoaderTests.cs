using System;
using System.Collections;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Where documents come from, and what a failed or cancelled load leaves behind.</summary>
    public class SourceLoaderTests : TextFixture
    {
        sealed class SlowSource : LetteraTextSource
        {
            public string Text = "From a slow source";
            public int Steps = 3;
            public int Started;
            public Exception Fail;
            public override bool HasContent => true;

            public override IEnumerator Load(LetteraLoadContext context, CancellationToken cancel, Action<LetteraRope> done)
            {
                Started++;
                for (int i = 0; i < Steps; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    yield return null;
                }
                if (Fail != null) throw Fail;
                done(LetteraRope.FromString(Text));
            }
        }

        LetteraSourceLoader Loader(LetteraTextSource source, string text = "Before")
        {
            LetteraWorldText label = Make(text);
            var loader = label.gameObject.AddComponent<LetteraSourceLoader>();
            loader.LoadOnEnable = false;
            loader.Source = source;
            return loader;
        }

        static void Run(LetteraSourceLoader loader)
        {
            for (int i = 0; i < 1000 && loader.Step(); i++) { }
        }

        [Test]
        public void ALoadShowsTheDocumentAndRaisesLoaded()
        {
            LetteraSourceLoader loader = Loader(new SlowSource());
            int loaded = 0;
            loader.Loaded += () => loaded++;
            loader.Reload();
            Assert.IsTrue(loader.IsLoading);
            Run(loader);
            Assert.AreEqual(1, loaded);
            Assert.IsFalse(loader.IsLoading);
            Assert.AreEqual("From a slow source", loader.GetComponent<LetteraWorldText>().Document.ToString());
        }

        [Test]
        public void AFailureReportsAndLeavesWhatShowed()
        {
            LetteraSourceLoader loader = Loader(new LetteraPathSource { Root = LetteraPathRoot.Absolute, Path = Path.Combine(Path.GetTempPath(), "lettera-no-such-file.txt") });
            string reason = null;
            loader.Failed += r => reason = r;
            loader.Reload();
            Run(loader);
            StringAssert.Contains("no such", reason);
            Assert.AreEqual(reason, loader.LastError);
            LetteraWorldText text = loader.GetComponent<LetteraWorldText>();
            Assert.IsFalse(text.IsWindowed);
            Assert.AreEqual("Before", text.Text);
        }

        [Test]
        public void ACancelRaisesNothingAndANewLoadWorks()
        {
            var source = new SlowSource();
            LetteraSourceLoader loader = Loader(source);
            int loaded = 0;
            loader.Loaded += () => loaded++;
            loader.Reload();
            loader.Step();
            loader.Cancel();
            Run(loader);
            Assert.AreEqual(0, loaded);
            Assert.IsFalse(loader.IsLoading);
            loader.Reload();
            Run(loader);
            Assert.AreEqual(1, loaded);
        }

        [Test]
        public void OfTwoQuickReloadsOnlyTheLastReports()
        {
            var source = new SlowSource();
            LetteraSourceLoader loader = Loader(source);
            int loaded = 0;
            loader.Loaded += () => loaded++;
            loader.Reload();
            loader.Step();
            loader.Reload();
            Run(loader);
            Assert.AreEqual(1, loaded);
            Assert.AreEqual(2, source.Started);
        }

        [Test]
        public void ASourceWithNothingToLoadDoesNothing()
        {
            LetteraSourceLoader loader = Loader(new LetteraPathSource());
            int calls = 0;
            loader.Loaded += () => calls++;
            loader.Failed += _ => calls++;
            loader.Reload();
            Assert.IsFalse(loader.IsLoading);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void RootsResolveAndAddressesTakeTheUrlPath()
        {
            Assert.AreEqual(Application.persistentDataPath.TrimEnd('/') + "/a/b.txt", new LetteraPathSource { Root = LetteraPathRoot.PersistentData, Path = "a/b.txt" }.FullPath);
            Assert.AreEqual("C:/x.txt", new LetteraPathSource { Root = LetteraPathRoot.Absolute, Path = "C:/x.txt" }.FullPath);
            Assert.IsFalse(new LetteraPathSource { Root = LetteraPathRoot.Absolute, Path = "C:/x.txt" }.IsUrl);
            Assert.IsTrue(new LetteraPathSource { Root = LetteraPathRoot.Absolute, Path = "jar:file:///a.apk!/x.txt" }.IsUrl);
        }

        [Test]
        public void AFileLoads()
        {
            string path = Path.GetTempFileName();
            File.WriteAllText(path, "Hello file");
            LetteraSourceLoader loader = Loader(new LetteraPathSource { Root = LetteraPathRoot.Absolute, Path = path });
            loader.Reload();
            Run(loader);
            LetteraWorldText text = loader.GetComponent<LetteraWorldText>();
            Assert.AreEqual("Hello file", text.Document.ToString());
            text.CloseDocument();
            File.Delete(path);
        }

        [Test]
        public void ATextAssetLoadsWithNoSceneChange()
        {
            var asset = new TextAsset("Hello asset");
            LetteraSourceLoader loader = Loader(new LetteraTextAssetSource { Asset = asset });
            LetteraWorldText text = loader.GetComponent<LetteraWorldText>();
            // A new object counts as changed whatever happens to it, so only what the load itself marks can tell.
            UnityEditor.EditorUtility.ClearDirty(loader.gameObject);
            UnityEditor.EditorUtility.ClearDirty(text);
            UnityEditor.EditorUtility.ClearDirty(loader);
            loader.Reload();
            Run(loader);
            text.ForceRebuild();
            Assert.AreEqual("Hello asset", text.Document.ToString());
            Assert.IsFalse(UnityEditor.EditorUtility.IsDirty(loader.gameObject), "the object");
            Assert.IsFalse(UnityEditor.EditorUtility.IsDirty(text), "the text");
            Assert.IsFalse(UnityEditor.EditorUtility.IsDirty(loader), "the loader");
            UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void ItsInspectorIsOurOwn()
        {
            LetteraSourceLoader loader = Loader(new SlowSource());
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(loader);
            UnityEngine.UIElements.VisualElement root = editor.CreateInspectorGUI();
            Assert.IsTrue(root.ClassListContains("lt-root"));
            UnityEngine.Object.DestroyImmediate(editor);
        }
    }
}
