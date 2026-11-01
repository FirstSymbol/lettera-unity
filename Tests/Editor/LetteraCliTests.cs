using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirstSymbol.Lettera.Editor.Cli;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>The commands of the CLI, called the way the CLI calls them, with the package or without it.</summary>
    public class LetteraCliTests : TextFixture
    {
        const string Folder = "Assets/LetteraCliTest";
        static string Output(string name) => Path.GetFullPath(Path.Combine("Temp", "LetteraCliTest", name));

        Scene m_Scene;

        [SetUp]
        public void SetUp()
        {
            m_Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(Folder);
            AssetDatabase.ImportAsset(Folder);
            Directory.CreateDirectory(Path.GetDirectoryName(Output("x")));
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists(Path.GetDirectoryName(Output("x")))) Directory.Delete(Path.GetDirectoryName(Output("x")), true);
        }

        static Dictionary<string, object> Good(object answer)
        {
            var d = answer as Dictionary<string, object>;
            Assert.IsNotNull(d, "an object: " + answer);
            Assert.IsFalse(d.ContainsKey("error"), d.TryGetValue("error", out object e) ? e as string : "");
            return d;
        }

        static string Bad(object answer)
        {
            var d = answer as Dictionary<string, object>;
            Assert.IsNotNull(d, "an object: " + answer);
            Assert.IsTrue(d.ContainsKey("error"), "expected an error, got: " + LetteraCliJson.Write(answer));
            return (string)d["error"];
        }

        GameObject MakeKind(string kind, string name, string text = "Lettera")
        {
            Dictionary<string, object> made = Good(LetteraCli.Create(kind, text, name));
            GameObject go = GameObject.Find((string)made["path"]);
            Assert.IsNotNull(go, name);
            go.GetComponents<MonoBehaviour>().OfType<ITextComponent>().First();
            ((MonoBehaviour)go.GetComponents<MonoBehaviour>().OfType<ITextComponent>().First()).GetType().GetProperty("Font").SetValue(go.GetComponents<MonoBehaviour>().OfType<ITextComponent>().First(), Latin);
            return go;
        }

        // ------------------------------------------------------------------------------------- AC 2: each command

        [Test]
        public void EveryCommandRunsThroughItsStaticMethodAndAnswersJson()
        {
            GameObject title = MakeKind("text", "Title");
            string target = (string)Good(LetteraCli.Get("Title"))["path"];
            string fontFile = Folder + "/Copy.ttf";
            File.Copy(Path.Combine(FontFolder(), "NotoSans-Regular.ttf"), fontFile);
            AssetDatabase.ImportAsset(fontFile);

            var run = new Dictionary<string, Func<object>>
            {
                ["help"] = () => LetteraCli.Help(),
                ["types"] = () => LetteraCli.Types(),
                ["tags"] = () => LetteraCli.Tags(target),
                ["list"] = () => LetteraCli.List(),
                ["get"] = () => LetteraCli.Get(target, true),
                ["create"] = () => LetteraCli.Create("world", "Hello", "Made", null, "10,3", "1,2", "{size:1}"),
                ["set"] = () => LetteraCli.Set(target, "{text:'Hi <b>there</b>',size:30,color:'#ff0',alignment:'Center,Middle',wrap:false,overflow:Ellipsis,padding:2,lineHeight:1.2,direction:LeftToRight,language:en,renderMode:SDF}"),
                ["style_add"] = () => LetteraCli.StyleAdd(target, "Fill", null, null, null, "{paint:'#f00,#00f'}"),
                ["style_list"] = () => LetteraCli.StyleList(target),
                ["style_set"] = () => LetteraCli.StyleSet(target, "0", "{enabled:false,effect:{softness:0.5}}"),
                ["style_remove"] = () => LetteraCli.StyleRemove(target, "0"),
                ["preset_save"] = () => LetteraCli.PresetSave(target, Folder + "/Sub/Preset.asset"),
                ["preset_apply"] = () => LetteraCli.PresetApply(target, Folder + "/Sub/Preset.asset"),
                ["font_create"] = () => LetteraCli.FontCreate(fontFile),
                ["font_stack"] = () => LetteraCli.FontStack(Folder + "/Copy Lettera.asset", Folder + "/Stack.asset", target),
                ["font_info"] = () => LetteraCli.FontInfo(Folder + "/Copy Lettera.asset"),
                ["validate"] = () => LetteraCli.Validate(target, "web"),
                ["measure"] = () => LetteraCli.Measure(target, "200"),
                ["glyphs"] = () => LetteraCli.Glyphs(target),
                ["atlas"] = () => LetteraCli.Atlas(),
                ["atlas_export"] = () => LetteraCli.AtlasExport(Output("atlas.png")),
                ["atlas_reset"] = () => LetteraCli.AtlasReset(),
                ["caches"] = () => LetteraCli.Caches(),
                ["memory"] = () => LetteraCli.Memory(),
                ["export_png"] = () => LetteraCli.ExportPng(target, Output("title.png")),
                ["select"] = () => LetteraCli.Select(target),
                ["edit_in_scene"] = () => LetteraCli.EditInScene(target),
                ["formula"] = () => LetteraCli.Formula("\\frac{a}{b}", true, "24"),
                ["settings_get"] = () => LetteraCli.SettingsGet("math.scale"),
                ["settings_set"] = () => LetteraCli.SettingsSet("{disableEmoji:" + (LetteraSettings.Instance.DisableEmoji ? "true" : "false") + "}"),
            };
            Good(LetteraCli.Glyphs(null, null, "Free text"));
            Assert.Greater((int)Good(LetteraCli.Glyphs(null, null, "abc"))["total"], 0);
            foreach (KeyValuePair<string, Func<object>> pair in run)
            {
                object answer = pair.Value();
                string json = LetteraCliJson.Write(answer);
                Assert.IsFalse(answer is Dictionary<string, object> d && d.ContainsKey("error"), pair.Key + ": " + json);
                LetteraCliJson.Parse(json);
            }

            object subset = LetteraCli.FontSubset(Folder + "/Copy Lettera.asset", "U+0020-007E", Folder + "/Small.asset");
            if (LetteraFontSubset.IsAvailable) Assert.Less((int)Good(subset)["glyphsAfter"], (int)Good(subset)["glyphsBefore"]);
            else Bad(subset);

            // A scene with no changes for these, which do work on files.
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Clean.unity");
            Good(LetteraCli.MigrateScan(Folder));
            Good(LetteraCli.MigrateStatus());
            Good(LetteraCli.MigrateReport(Output("report.md")));
            Assert.IsTrue(File.Exists(Output("report.md")));
            Good(LetteraCli.MigrateFonts());
            Good(LetteraCli.MigrateComponents(true));
            Good(LetteraCli.MigrateScripts(true));
            Good(LetteraCli.MigrateScripts(false));

            // Running this for real builds a player: its way to the build is only read as far as what stops it.
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            StringAssert.Contains("unsaved", Bad(LetteraCli.DeviceCheckBuild("windows")));

            var done = new HashSet<string>(run.Keys) { "font_subset", "migrate_scan", "migrate_status", "migrate_report", "migrate_fonts", "migrate_components", "migrate_scripts", "device_check_build" };
            CollectionAssert.AreEquivalent(LetteraCli.Commands.Select(c => c.name).ToList(), done.ToList(), "every command of the table has been run");
            Assert.AreEqual(38, LetteraCli.Commands.Length);
            Assert.IsNotNull(title);
        }

        static string FontFolder()
        {
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            return Path.Combine(Path.GetDirectoryName(asmdef), "..", "Fonts~");
        }

        static string PackageFile(string name) => Path.Combine(FontFolder(), "..", "..", name);

        [TestCase("text")]
        [TestCase("world")]
        [TestCase("text3d")]
        [TestCase("input")]
        [TestCase("area")]
        [TestCase("password")]
        [TestCase("dropdown")]
        public void EachKindIsMade(string kind)
        {
            Dictionary<string, object> made = Good(LetteraCli.Create(kind, "Words", "Made " + kind));
            Assert.IsTrue(made.ContainsKey("rootId"));
            GameObject root = GameObject.Find((string)made["root"]);
            Assert.IsNotNull(root);
            Assert.IsTrue(root.scene.isDirty);
            Good(LetteraCli.Get((string)made["rootId"]));
        }

        [Test]
        public void TargetsAreAPathANameOrAnId()
        {
            GameObject go = MakeKind("world", "Named");
            Assert.AreEqual("/Named", Good(LetteraCli.Get("Named"))["path"]);
            Assert.AreEqual("/Named", Good(LetteraCli.Get("/Named"))["path"]);
            Assert.AreEqual("/Named", Good(LetteraCli.Get("#" + go.GetInstanceID()))["path"]);
            Assert.AreEqual("/Named", Good(LetteraCli.Get("#" + go.GetComponent<LetteraWorldText>().GetInstanceID()))["path"]);
            Assert.IsTrue(((List<object>)LetteraCli.List()).Cast<Dictionary<string, object>>().Any(x => (string)x["path"] == "/Named" && (string)x["kind"] == "world"));
        }

        [Test]
        public void GetShowsOnlyWhatIsNotDefaultUnlessAskedAndSetTakesWhatGetPrints()
        {
            MakeKind("world", "Plain");
            Dictionary<string, object> brief = Good(LetteraCli.Get("Plain"));
            Dictionary<string, object> full = Good(LetteraCli.Get("Plain", true));
            Assert.IsFalse(brief.ContainsKey("wordWrap"));
            Assert.IsTrue(full.ContainsKey("wordWrap"));
            Good(LetteraCli.Set("Plain", "{wordWrap:" + LetteraCliJson.Write(full["wordWrap"]) + ",horizontalAlignment:" + LetteraCliJson.Write(full["horizontalAlignment"]) + "}"));
        }

        // ------------------------------------------------------------------------------------- Undo, dirt, saving

        [Test]
        public void AnEditIsUndoableAndDirtiesTheSceneAndNothingSaves()
        {
            GameObject go = MakeKind("world", "Edited");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Edited.unity");
            var text = go.GetComponent<LetteraWorldText>();
            float size = text.FontSize;
            Assert.IsFalse(go.scene.isDirty);

            foreach (object reading in new[] { LetteraCli.Get("Edited"), LetteraCli.List(), LetteraCli.Measure("Edited"), LetteraCli.Validate("Edited"), LetteraCli.Glyphs("Edited"), LetteraCli.StyleList("Edited"), LetteraCli.Tags("Edited") }) LetteraCliJson.Write(reading);
            Assert.IsFalse(go.scene.isDirty, "reading leaves the scene clean");

            Undo.IncrementCurrentGroup();
            Good(LetteraCli.Set("Edited", "{size:77}"));
            Assert.AreEqual(77f, text.FontSize);
            Assert.IsTrue(go.scene.isDirty, "an edit dirties");
            Undo.PerformUndo();
            Assert.AreEqual(size, text.FontSize, "and undoes");
            Assert.IsTrue(go.scene.isDirty, "and nothing saved the scene");
        }

        [Test]
        public void ACreateUndoesWithWhatItMade()
        {
            Assert.AreEqual(0, Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Undo.IncrementCurrentGroup();
            Good(LetteraCli.Create("text", "x", "Gone"));
            Assert.IsNotNull(GameObject.Find("Gone"));
            Assert.AreEqual(1, Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            Undo.PerformUndo();
            Assert.IsNull(GameObject.Find("Gone"));
            Assert.AreEqual(0, Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        }

        // ------------------------------------------------------------------------------------- AC 3: hostile input

        static string[] HostileData() => new[]
        {
            "", " ", "{", "}", "[", "]", "[1,2", "{a", "{a:", "{a:1", "{a:1,", "{\"a\":\"", "'", "\"", "\\", "{1:2}", "nul", "null", "true", "123", "[]", ",", ":", "\0",
            new string('[', 200000), string.Concat(Enumerable.Repeat("{a:", 100000)), "{\"\\u12\":1}", "{a:'\\u12'}",
            "{text:{a:1}}", "{text:[1]}", "{size:'abc'}", "{size:-1}", "{size:0}", "{size:1e999}", "{size:NaN}", "{size:Infinity}", "{size:[1]}", "{size:[1e999,1]}", "{size:[-1,1]}", "{size:{}}", "{size:null}", "{fontSize:null}", "{fontSize:1e30}",
            "{color:'nope'}", "{color:[1]}", "{color:5}", "{color:null}", "{color:{r:1}}", "{font:'Assets/none.asset'}", "{font:5}", "{stack:[]}",
            "{alignment:5}", "{alignment:'Sideways'}", "{alignment:{}}", "{overflow:99}", "{overflow:'x'}", "{direction:'up'}", "{renderMode:-1}", "{wrap:'maybe'}", "{padding:'x'}", "{padding:[1,2]}",
            "{position:'a,b'}", "{position:5}", "{rotation:'x'}", "{scale:'x'}", "{active:'maybe'}", "{name:''}", "{name:null}", "{lineHeight:'x'}",
            "{styles:5}", "{styles:[5]}", "{styles:[{effect:{$type:'System.Diagnostics.Process'}}]}", "{styles:[{effect:{$type:5}}]}", "{styles:[{effect:{$type:'LetteraEffect'}}]}", "{styles:[{effect:'Nope'}]}", "{styles:[{nothing:1}]}", "{styles:[{tag:''}]}",
            "{styles:[{effect:{$type:Fill,paint:{gradient:5}}}]}", "{styles:[{effect:{$type:Fill,nothing:1}}]}", "{styles:[{effect:{$type:Stroke,width:'x'}}]}", "{styles:[{effect:{$type:Fill,paint:{angle:'x'}}}]}",
            "{presets:['Assets/none.asset']}", "{presets:5}", "{rules:[]}", "{hideFlags:1}", "{unknownKey:1}", "{Rebuilt:1}", "{TextResolver:'x'}",
            "{text:'changed',size:'abc'}", "{size:'abc',text:'changed'}",
        };

        [Test]
        public void HostileDataGivesAnErrorAsJsonAndChangesNothing()
        {
            GameObject go = MakeKind("world", "Target");
            var text = go.GetComponent<LetteraWorldText>();
            string before = EditorJsonUtility.ToJson(text);
            foreach (string data in HostileData())
            {
                string shown = data.Length > 60 ? data.Substring(0, 60) : data;
                string error = Bad(LetteraCli.Set("Target", data));
                Assert.IsNotEmpty(error, shown);
                LetteraCliJson.Write(LetteraCli.Set("Target", data));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(text), shown);
            }
            Assert.AreEqual(1, Object.FindObjectsByType<LetteraWorldText>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        }

        [Test]
        public void HostileDataElsewhereGivesAnErrorToo()
        {
            MakeKind("world", "Target");
            Good(LetteraCli.StyleAdd("Target", "Bold"));
            string settings = EditorJsonUtility.ToJson(LetteraSettings.Instance);
            int objects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            foreach (string data in new[] { "{", "[", new string('[', 200000), "null", "5", "{nothing:1}", "{effect:'Nope'}", "{effect:{$type:5}}", "{matcher:'Nope'}", "{enabled:'x'}", "{tag:''}" })
            {
                string shown = data.Length > 60 ? data.Substring(0, 60) : data;
                Assert.IsNotEmpty(Bad(LetteraCli.StyleSet("Target", "0", data)), shown);
                Assert.IsNotEmpty(Bad(LetteraCli.SettingsSet(data)), shown);
                Assert.AreEqual(settings, EditorJsonUtility.ToJson(LetteraSettings.Instance), shown);
            }
            foreach (string data in new[] { "{", "[", new string('[', 200000), "null", "5", "{nothing:1}", "{width:'x'}" })
                Bad(LetteraCli.StyleAdd("Target", "Stroke", null, null, null, data));
            foreach (string data in new[] { "{", new string('[', 200000), "5", "{size:'abc'}", "{unknownKey:1}" })
            {
                Bad(LetteraCli.Create("world", "x", "Refused", null, null, null, data));
                Bad(LetteraCli.Create("text", "x", "Refused", null, null, null, data));
                Assert.IsNull(GameObject.Find("Refused"), "a refused create leaves nothing");
                Assert.AreEqual(objects, Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "nor a canvas made for it");
            }
        }

        [Test]
        public void HostileArgumentsGiveAnErrorToo()
        {
            MakeKind("world", "Target");
            foreach (string target in new[] { null, "", " ", "#", "#abc", "#999999999", "#-1", "///", "No/Such/Path", new string('x', 100000), "\0" })
            {
                Bad(LetteraCli.Get(target));
                Bad(LetteraCli.Set(target, "{size:1}"));
                Bad(LetteraCli.ExportPng(target, Output("x.png")));
                Bad(LetteraCli.Measure(target));
                Bad(LetteraCli.Select(target));
            }
            foreach (string index in new[] { null, "", "x", "-1", "99999999999", "1.5", "5" })
            {
                Bad(LetteraCli.StyleRemove("Target", index));
                Bad(LetteraCli.StyleSet("Target", index, "{enabled:true}"));
            }
            foreach (string output in new[] { null, "", " ", "x.txt", "C:/con/x?.png", new string('x', 40000) + ".png", "\0.png" })
                Bad(LetteraCli.ExportPng("Target", output));
            foreach (string scale in new[] { "NaN", "-1", "0", "1e30", "abc", "Infinity" })
                Bad(LetteraCli.ExportPng("Target", Output("x.png"), scale));
            Bad(LetteraCli.ExportPng("Target", Output("x.png"), null, "nope"));
            foreach (string path in new[] { null, "", "../x.asset", "Assets/../x.asset", "C:/x.asset", "Assets/a:b.asset", "Assets/x.txt", "x.asset", "Assets/a\0.asset" })
                Bad(LetteraCli.PresetSave("Target", path));
            foreach (string path in new[] { null, "", "Assets/none.asset", "\0" })
            {
                Bad(LetteraCli.PresetApply("Target", path));
                Bad(LetteraCli.FontInfo(path));
                Bad(LetteraCli.FontSubset(path, "abc"));
            }
            Bad(LetteraCli.FontCreate(null));
            Bad(LetteraCli.FontCreate("Assets/none.ttf"));
            Bad(LetteraCli.FontCreate("Assets/none.txt"));
            Bad(LetteraCli.FontStack(null));
            Bad(LetteraCli.FontStack("Assets/none.asset", Folder + "/S.asset"));
            Bad(LetteraCli.Create(null));
            Bad(LetteraCli.Create("nope"));
            Bad(LetteraCli.Create("world", null, null, "No Parent"));
            Bad(LetteraCli.Create("world", null, ""));
            Bad(LetteraCli.Create("world", null, null, null, "x,y"));
            Bad(LetteraCli.Create("world", null, null, null, "1e999,1"));
            Bad(LetteraCli.Create("world", null, null, null, null, "1"));
            Bad(LetteraCli.Create("text", null, null, "Target"));
            Bad(LetteraCli.Validate("Target", "mars"));
            Bad(LetteraCli.Measure("Target", "x"));
            Bad(LetteraCli.Measure("Target", "-1"));
            Bad(LetteraCli.Glyphs());
            Bad(LetteraCli.Glyphs(null, "Assets/none.asset", "x"));
            Bad(LetteraCli.Formula(null));
            Bad(LetteraCli.Formula("x", false, "x"));
            Assert.IsFalse((bool)Good(LetteraCli.Formula("\\frac{"))["valid"]);
            Bad(LetteraCli.AtlasExport(null));
            Bad(LetteraCli.AtlasExport(Output("x.png"), "nope"));
            Bad(LetteraCli.AtlasExport(Output("x.png"), null, "9999"));
            Bad(LetteraCli.SettingsGet("no.such.key"));
            Bad(LetteraCli.MigrateScan("No/Such/Folder"));
            Bad(LetteraCli.MigrateReport(null));
            Bad(LetteraCli.DeviceCheckBuild(null));
            Bad(LetteraCli.DeviceCheckBuild("mars"));
            Bad(LetteraCli.StyleAdd("Target"));
            Bad(LetteraCli.StyleAdd("Target", "Nope"));
            Bad(LetteraCli.StyleAdd("Target", null, null, "Nope"));
            Bad(LetteraCli.StyleAdd("Target", null, "t", null, null, "{a:1}"));
        }

        [Test]
        public void AFileThatIsNoFontIsSaidToBeSo()
        {
            string file = Output("garbage.ttf");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Bad(LetteraCli.FontInfo(file));
                Bad(LetteraCli.FontCreate(file, Folder + "/Garbage.asset"));
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(Folder + "/Garbage.asset"));
        }

        // ------------------------------------------------------------------------------------- AC 5: export

        static Dictionary<string, int> Census() => new Dictionary<string, int>
        {
            ["objects"] = Resources.FindObjectsOfTypeAll<GameObject>().Length,
            ["cameras"] = Resources.FindObjectsOfTypeAll<Camera>().Length,
            ["lights"] = Resources.FindObjectsOfTypeAll<Light>().Length,
            ["canvases"] = Resources.FindObjectsOfTypeAll<Canvas>().Length,
            ["meshes"] = Resources.FindObjectsOfTypeAll<Mesh>().Length,
            ["textures"] = Resources.FindObjectsOfTypeAll<Texture2D>().Length,
        };

        [TestCase("text")]
        [TestCase("world")]
        [TestCase("text3d")]
        public void ExportDrawsLeavesNothingBehindAndLeavesTheSceneClean(string kind)
        {
            GameObject go = MakeKind(kind, "Exported", "Lettera");
            string path = Folder + "/Export.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path));
            Assert.IsFalse(go.scene.isDirty);
            int roots = go.scene.rootCount;

            string file = Output(kind + ".png");
            Good(LetteraCli.ExportPng("Exported", file));
            Dictionary<string, int> before = Census();
            var transformBefore = go.transform.position;
            Dictionary<string, object> answer = Good(LetteraCli.ExportPng("Exported", file));
            CollectionAssert.AreEquivalent(before, Census(), "nothing left behind");
            Assert.IsFalse(go.scene.isDirty, "the scene stays clean");
            Assert.AreEqual(roots, go.scene.rootCount);
            Assert.AreEqual(1, SceneManager.sceneCount);
            Assert.AreEqual(transformBefore, go.transform.position);
            Assert.IsTrue(go.activeSelf);

            Assert.Greater((int)answer["drawnPixels"], 100, "something drew");
            var picture = new Texture2D(2, 2);
            Assert.IsTrue(picture.LoadImage(File.ReadAllBytes(file)), "a readable PNG");
            Assert.AreEqual((int)answer["width"], picture.width);
            Assert.AreEqual((int)answer["height"], picture.height);
            Object.DestroyImmediate(picture);
            Debug.Log($"export_png {kind}: {answer["ms"]} ms for {answer["width"]}x{answer["height"]}, {answer["drawnPixels"]} pixels drawn");
        }

        [Test]
        public void ExportWorksOnAnInactiveTextAndALargeScaleIsHeldBack()
        {
            GameObject go = MakeKind("world", "Asleep");
            go.SetActive(false);
            Dictionary<string, object> answer = Good(LetteraCli.ExportPng("Asleep", Output("asleep.png"), "64"));
            Assert.IsFalse(go.activeSelf, "the original stays as it was");
            Assert.Greater((int)answer["drawnPixels"], 100);
            Assert.AreEqual(4096, answer["clampedTo"]);
            Assert.LessOrEqual(Math.Max((int)answer["width"], (int)answer["height"]), 4096);
        }

        [Test]
        public void ExportShowsOnlyItsTarget()
        {
            MakeKind("world", "Wanted");
            GameObject other = MakeKind("world", "Other", "OTHER TEXT OTHER");
            other.GetComponent<LetteraWorldText>().Color = Color.red;
            string file = Output("wanted.png");
            Good(LetteraCli.ExportPng("Wanted", file));
            var picture = new Texture2D(2, 2);
            picture.LoadImage(File.ReadAllBytes(file));
            Assert.IsFalse(picture.GetPixels32().Any(p => p.r > 150 && p.g < 60 && p.b < 60), "no red from the other text");
            Object.DestroyImmediate(picture);
        }

        [Test]
        public void TheTextsOverflowIsInThePicture()
        {
            GameObject go = MakeKind("text", "Spilled", "Words that run far past a tiny rect");
            go.GetComponent<LetteraText>().WordWrap = false;
            ((RectTransform)go.transform).sizeDelta = new Vector2(30, 40);
            Dictionary<string, object> answer = Good(LetteraCli.ExportPng("Spilled", Output("spilled.png"), "1"));
            Assert.Greater((int)answer["width"], 200, "the frame follows the ink");
        }

        // ------------------------------------------------------------------------------------- The refusals

        [Test]
        public void MigrationRefusesOnADirtySceneRatherThanPrompt()
        {
            string asset = Folder + "/Untouched.prefab";
            GameObject go = MakeKind("world", "Dirty");
            Assert.IsTrue(go.scene.isDirty);
            Good(LetteraCli.MigrateScan(Folder));
            StringAssert.Contains("unsaved", Bad(LetteraCli.MigrateComponents()));
            StringAssert.Contains("unsaved", Bad(LetteraCli.MigrateFonts()));
            StringAssert.Contains("unsaved", Bad(LetteraCli.MigrateScripts()));
            StringAssert.Contains("unsaved", Bad(LetteraCli.DeviceCheckBuild("windows")));
            Good(LetteraCli.MigrateScripts(true));
            Assert.IsTrue(go.scene.isDirty, "and the person's work is as it was");
            Assert.IsFalse(File.Exists(asset));
        }

        // ------------------------------------------------------------------------------------- The guide's recipes

        [Test]
        public void TheGuidesRecipesWork()
        {
            // A gradient title: the colours differ from one end of the word to the other.
            Good(LetteraCli.Create("text", "Sunset", "Title", null, "600,120", null, "{size:72,alignment:'Center,Middle'}"));
            GameObject.Find("Title").GetComponent<LetteraText>().Font = Latin;
            Good(LetteraCli.StyleAdd("Title", "Fill", null, null, null, "{paint:{gradient:'#FF0000,#0000FF',angle:0,span:Range}}"));
            string file = Output("title.png");
            Good(LetteraCli.ExportPng("Title", file));
            var picture = new Texture2D(2, 2);
            picture.LoadImage(File.ReadAllBytes(file));
            Color32[] pixels = picture.GetPixels32();
            int reddest = int.MaxValue, bluest = int.MinValue;
            for (int y = 0; y < picture.height; y++)
                for (int x = 0; x < picture.width; x++)
                {
                    Color32 p = pixels[y * picture.width + x];
                    if (p.r > 160 && p.b < 110) reddest = Math.Min(reddest, x);
                    if (p.b > 160 && p.r < 110) bluest = Math.Max(bluest, x);
                }
            Object.DestroyImmediate(picture);
            Assert.Less(reddest, picture ? 0 : bluest, "red on the left, blue on the right");

            // A card: a parent and a child, exported whole.
            Good(LetteraCli.Create("text", "Sunset", "Card", null, "600,300", null, "{size:64,alignment:'Center,Top'}"));
            Good(LetteraCli.Create("text", "Warm colours at the end of the day.", "Body", "Card", "560,120", "0,-40", "{size:28,alignment:Center}"));
            Assert.AreEqual("/Canvas/Card/Body", Good(LetteraCli.Get("Body"))["path"]);
            Assert.Greater((int)Good(LetteraCli.ExportPng("Card", Output("card.png")))["drawnPixels"], 100);

            // An input field with validation, reached by its root.
            Dictionary<string, object> email = Good(LetteraCli.Create("input", null, "Email"));
            Good(LetteraCli.Set("Email", "{rules:[{$type:SingleLine},{$type:MaxLength,limit:40},{$type:Email}]}"));
            var field = GameObject.Find((string)email["root"]).GetComponentInChildren<LetteraEditable>();
            Assert.AreEqual(3, field.Rules.Count);
            Assert.AreEqual(40, ((MaxLengthRule)field.Rules[1]).Limit);
            Assert.IsInstanceOf<EmailCharFilter>(field.Rules[2]);

            // A right-to-left paragraph.
            Good(LetteraCli.Create("text", null, "Arabic", null, "500,200"));
            Good(LetteraCli.Set("Arabic", "{text:'مرحبا بالعالم',direction:RightToLeft,language:ar,size:36}"));
            Assert.AreEqual(LetteraDirection.RightToLeft, GameObject.Find("Arabic").GetComponent<LetteraText>().BaseDirection);

            // A link, a typewriter and a formula are bound and readable.
            foreach (string text in new[] { "Read the <a=https://unity.com>docs</a>", "<reveal=,100%>Line by line</reveal>", "Half is <math>\\\\frac{1}{2}</math>" })
            {
                Good(LetteraCli.Set("Title", "{text:'" + text + "'}"));
                Dictionary<string, object> checkedText = Good(LetteraCli.Validate("Title", "windows"));
                Assert.IsTrue((bool)checkedText["ok"], text + ": " + LetteraCliJson.Write(checkedText));
            }
            Assert.IsTrue((bool)Good(LetteraCli.Formula("\\frac{a}{b}"))["valid"]);

            // Solid text.
            Dictionary<string, object> solid = Good(LetteraCli.Create("text3d", "Solid", "Logo", null, null, null, "{depth:0.25,frontBevel:{shape:Round,width:0.03,height:0.03,roundness:0.5,segments:4}}"));
            Assert.AreEqual("text3d", solid["kind"]);
            Assert.Greater((int)Good(LetteraCli.ExportPng("Logo", Output("logo.png")))["drawnPixels"], 100);
        }

        [Test]
        public void ValidateFindsAnUnboundTagAndABrokenFormula()
        {
            MakeKind("world", "Checked", "<nothing>x</nothing> <math>\\frac{</math>");
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Dictionary<string, object> answer = Good(LetteraCli.Validate("Checked", "windows"));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            string problems = LetteraCliJson.Write(answer["problems"]);
            StringAssert.Contains("nothing", problems);
            StringAssert.Contains("formula", problems);
            Assert.IsFalse((bool)answer["ok"]);
        }

        // ------------------------------------------------------------------------------------- AC 1, and the guide

        [Test]
        public void TheCommandAssemblyIsThereExactlyWhenItsPackageIs()
        {
            bool package = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.pipeline") != null;
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FirstSymbol.Lettera.Editor.Cli");
            Debug.Log($"com.unity.pipeline {(package ? "is" : "is not")} installed, and the command assembly {(assembly != null ? "is" : "is not")} loaded");
            Assert.AreEqual(package, assembly != null);
            if (assembly == null) return;

            var seen = new List<string>();
            foreach (Type type in assembly.GetTypes())
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    Attribute attribute = method.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "CliCommandAttribute");
                    if (attribute == null) continue;
                    Assert.AreEqual("lettera", attribute.GetType().GetProperty("Group").GetValue(attribute));
                    seen.Add((string)attribute.GetType().GetProperty("Name").GetValue(attribute));
                }
            CollectionAssert.AreEquivalent(LetteraCli.Commands.Select(c => c.name).ToList(), seen);
            MethodInfo help = assembly.GetType("FirstSymbol.Lettera.Editor.Cli.LetteraCliCommands").GetMethod("Help", BindingFlags.Static | BindingFlags.NonPublic);
            Good(help.Invoke(null, null));
        }

        [Test]
        public void TheGuideNamesEveryCommandAndTheReadmesNameTheGuide()
        {
            string guide = File.ReadAllText(PackageFile("Documentation~/AgentGuide.md"));
            StringAssert.StartsWith("---", guide);
            StringAssert.Contains("name: lettera", guide);
            foreach ((string name, string _, string _) in LetteraCli.Commands) StringAssert.Contains("`" + name, guide.Replace("`help`, `types`", "`help` `types`"), name);
            StringAssert.Contains("Documentation~/AgentGuide.md", File.ReadAllText(PackageFile("README.md")));
            StringAssert.Contains("Documentation~/AgentGuide.md", File.ReadAllText(PackageFile("README.ru.md")));
        }

        [Test]
        public void TheJsonReaderIsRelaxed()
        {
            var d = LetteraCliJson.Object("{a:1, 'b':\"two\", c:[true,false,null], d:{e:word}, $type:Stroke}");
            Assert.AreEqual(1.0, d["a"]);
            Assert.AreEqual("two", d["b"]);
            Assert.AreEqual(3, ((List<object>)d["c"]).Count);
            Assert.AreEqual("word", ((Dictionary<string, object>)d["d"])["e"]);
            Assert.AreEqual("Stroke", d["$type"]);
            Assert.AreEqual("{\"a\":[1,\"x\\\"y\",null]}", LetteraCliJson.Write(LetteraCliJson.Object("{a:[1,'x\"y',null]}")));
        }
    }
}
