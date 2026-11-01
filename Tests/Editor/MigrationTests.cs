using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using FirstSymbol.Lettera.Editor.Migration;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class MigrationTests
    {
        [TestCase("plain", "plain")]
        [TestCase("<b>x</b>", "<b>x</b>")]
        [TestCase("<sprite index=3>", "<sprite=3>")]
        [TestCase("<sprite name=\"fire\">", "<sprite=fire>")]
        [TestCase("<sprite=\"Icons\" name=\"fire\">", "<sprite=fire>")]
        [TestCase("<sprite=2>", "<sprite=2>")]
        [TestCase("<link=\"id\">a</link>", "<link=id>a</link>")]
        [TestCase("one<page>two", "onetwo")]
        [TestCase("<quad size=2>", "<noparse><quad size=2></noparse>")]
        [TestCase("a < b and c > d", "a < b and c > d")]
        [TestCase("a <\nb>", "a <\nb>")]
        [TestCase("<color=#F00>r</color>", "<color=#F00>r</color>")]
        [TestCase("", "")]
        public void MarkupConverts(string source, string expected) => Assert.AreEqual(expected, LetteraTmpMarkup.Convert(source, new List<string>()));

        [Test]
        public void MarkupSaysWhatItCouldNotCarry()
        {
            var notes = new List<string>();
            LetteraTmpMarkup.Convert("<page>x<alpha=#80><sprite anim=\"1,2,3\" index=1>", notes);
            Assert.AreEqual(3, notes.Count);
        }

        [Test]
        public void MarkupIsLinearOnAHostileText()
        {
            double Time(int size)
            {
                string text = new string('<', size);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                LetteraTmpMarkup.Convert(text, null);
                return clock.Elapsed.TotalMilliseconds;
            }
            Time(1000);
            Assert.Less(Time(1000000), 2000, "a million openers");
        }

        static string Script(string body) => "using TMPro;\nclass A : MonoBehaviour {\n" + body + "\n}\n";

        static string Rewritten(string body, List<LetteraTmpNote> notes = null) => LetteraTmpScript.Rewrite(Script(body), notes ?? new List<LetteraTmpNote>());

        [TestCase("public TextMeshProUGUI label;", "public LetteraText label;")]
        [TestCase("public TextMeshPro label;", "public LetteraWorldText label;")]
        [TestCase("public TMP_Text label;", "public LetteraText label;")]
        [TestCase("TMP_FontAsset font;", "LetteraFontAsset font;")]
        [TestCase("TextMeshProUGUI label; void F() { label.text = \"x\"; }", "LetteraText label; void F() { label.Text = \"x\"; }")]
        [TestCase("TMP_Text t; void F() { t.fontSize = 3; t.fontSizeMin = 1; t.fontSizeMax = 2; t.enableAutoSizing = true; }", "LetteraText t; void F() { t.FontSize = 3; t.MinFontSize = 1; t.MaxFontSize = 2; t.AutoSize = true; }")]
        [TestCase("void F() { GetComponent<TMP_Text>().text = \"x\"; }", "void F() { GetComponent<LetteraText>().Text = \"x\"; }")]
        [TestCase("void F() { var t = GetComponent<TextMeshProUGUI>(); t.text = \"x\"; }", "void F() { var t = GetComponent<LetteraText>(); t.Text = \"x\"; }")]
        [TestCase("TMP_Text[] all; void F() { all[0].text = \"x\"; }", "LetteraText[] all; void F() { all[0].Text = \"x\"; }")]
        [TestCase("TMP_Text[] all; void F() { foreach (var t in all) t.text = \"x\"; }", "LetteraText[] all; void F() { foreach (var t in all) t.Text = \"x\"; }")]
        public void ScriptsRewrite(string body, string expected) => StringAssert.Contains(expected, Rewritten(body));

        [TestCase("string s = \"TextMeshProUGUI label.text\";")]
        [TestCase("string s = @\"TMP_Text\"\"x.text\";")]
        [TestCase("// TextMeshProUGUI label.text")]
        [TestCase("/* TMP_Text */")]
        [TestCase("string s = $\"{TMP_Text}\";")]
        public void WordsInStringsAndCommentsStay(string body)
        {
            string done = Rewritten(body);
            StringAssert.Contains(body, done);
        }

        [Test]
        public void AnotherClassesTextStays()
        {
            string done = Rewritten("Other o; void F() { o.text = \"x\"; }");
            StringAssert.Contains("o.text", done);
        }

        [Test]
        public void APreprocessorLineStays()
        {
            string done = LetteraTmpScript.Rewrite("#if TMP_PRESENT\nusing TMPro;\n#endif\nclass A {}\n", new List<LetteraTmpNote>());
            StringAssert.Contains("#if TMP_PRESENT", done);
        }

        [Test]
        public void UsingBecomesOursAndStaysWhileSomethingRemains()
        {
            string done = Rewritten("TextMeshProUGUI a;");
            StringAssert.Contains("using FirstSymbol.Lettera;", done);
            StringAssert.DoesNotContain("using TMPro;", done);
            string kept = Rewritten("TMP_SpriteAsset s;");
            StringAssert.Contains("using TMPro;", kept);
            StringAssert.Contains("using FirstSymbol.Lettera;", kept);
        }

        [Test]
        public void AlignmentSplitsInTwoInsideBraces()
        {
            string done = Rewritten("TMP_Text t; void F() { if (x) t.alignment = TextAlignmentOptions.TopRight; }");
            StringAssert.Contains("if (x) { t.HorizontalAlignment = LetteraHorizontalAlignment.End; t.VerticalAlignment = LetteraVerticalAlignment.Top; }", done);
        }

        [Test]
        public void ListenersBecomeEvents()
        {
            string done = Rewritten("TMP_InputField f; void G() { f.onValueChanged.AddListener(H); f.onValueChanged.RemoveListener(H); }");
            StringAssert.Contains("f.ValueChanged += H;", done);
            StringAssert.Contains("f.ValueChanged -= H;", done);
        }

        [Test]
        public void WhatHasNoCounterpartIsReportedWithTheWayOut()
        {
            var notes = new List<LetteraTmpNote>();
            Rewritten("TMP_Text t; void F() { t.ForceMeshUpdate(); var w = t.preferredWidth; t.fontStyle = 0; }", notes);
            Assert.GreaterOrEqual(notes.Count(n => n.Warning), 3);
            Assert.IsTrue(notes.Any(n => n.Message.Contains("ForceRebuild")));
        }

        [Test]
        public void AFileWithNothingOfItStaysToTheByte()
        {
            const string other = "using System;\nclass A { string text; void F() { text = \"\"; } }\n";
            Assert.AreEqual(other, LetteraTmpScript.Rewrite(other, new List<LetteraTmpNote>()));
        }

        [Test]
        public void TheRewriteIsLinear()
        {
            string line = "    void F() { label.text = \"x\"; }\n";
            string big = "using TMPro;\nclass A { TextMeshProUGUI label;\n" + string.Concat(Enumerable.Repeat(line, 30000)) + "}\n";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string done = LetteraTmpScript.Rewrite(big, new List<LetteraTmpNote>());
            Assert.Less(clock.Elapsed.TotalSeconds, 15);
            StringAssert.Contains("label.Text", done);
        }

        const string CanvasDoc = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_Name: Label
--- !u!114 &200
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: f4688fdb7df04437aeb418b961361dc5, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  m_Material: {fileID: 0}
  m_Color: {r: 1, g: 1, b: 1, a: 1}
  m_RaycastTarget: 0
  m_Maskable: 1
  m_text: ""Hello <sprite index=2> world\nline""
  m_isRightToLeft: 0
  m_fontAsset: {fileID: 11400000, guid: aaaabbbb, type: 2}
  m_sharedMaterial: {fileID: 2100000, guid: aaaabbbb, type: 2}
  m_fontColor: {r: 0.5, g: 0.25, b: 0, a: 1}
  m_fontSize: 24
  m_fontSizeMin: 12
  m_fontSizeMax: 48
  m_enableAutoSizing: 1
  m_fontStyle: 1
  m_HorizontalAlignment: 2
  m_VerticalAlignment: 1024
  m_TextWrappingMode: 0
  m_overflowMode: 1
  m_margin: {x: 1, y: 2, z: 3, w: 4}
  m_isRichText: 1
";

        static LetteraTmpYaml Yaml()
        {
            var yaml = new LetteraTmpYaml { LetteraCanvasGuid = "cccc", LetteraWorldGuid = "wwww" };
            yaml.FontMap["aaaabbbb"] = "ffff";
            return yaml;
        }

        [Test]
        public void AComponentSwapsWhereItStandsAndKeepsItsId()
        {
            LetteraTmpYaml yaml = Yaml();
            string done = yaml.Convert(CanvasDoc.Replace("\r\n", "\n"), "Scene.unity");
            Assert.AreEqual(1, yaml.Converted);
            StringAssert.Contains("--- !u!114 &200", done);
            StringAssert.Contains("m_GameObject: {fileID: 100}", done);
            StringAssert.Contains("guid: cccc", done);
            StringAssert.DoesNotContain("f4688fdb", done);
            StringAssert.Contains("Text: \"<b>Hello <sprite=2> world\\nline</b>\"", done);
            StringAssert.Contains("Font: {fileID: 11400000, guid: ffff, type: 2}", done);
            StringAssert.Contains("FontSize: 24", done);
            StringAssert.Contains("MinFontSize: 12", done);
            StringAssert.Contains("AutoSize: 1", done);
            StringAssert.Contains("HorizontalAlignment: 1", done);
            StringAssert.Contains("VerticalAlignment: 2", done);
            StringAssert.Contains("WordWrap: 0", done);
            StringAssert.Contains("Overflow: 1", done);
            StringAssert.Contains("m_Color: {r: 0.5, g: 0.25, b: 0, a: 1}", done);
            StringAssert.Contains("m_RaycastTarget: 0", done);
            StringAssert.Contains("Padding: {x: 1, y: 4, z: 3, w: 2}", done);
        }

        [Test]
        public void AFileWithNoTextStaysToTheByte()
        {
            const string other = "%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: x\n";
            LetteraTmpYaml yaml = Yaml();
            Assert.AreEqual(other, yaml.Convert(other));
            Assert.AreEqual(0, yaml.Converted);
        }

        [Test]
        public void ASecondPassChangesNothing()
        {
            LetteraTmpYaml yaml = Yaml();
            string once = yaml.Convert(CanvasDoc.Replace("\r\n", "\n"));
            Assert.AreEqual(once, Yaml().Convert(once));
        }

        [Test]
        public void ATextInsideAnInputFieldStaysWithIt()
        {
            string doc = CanvasDoc.Replace("\r\n", "\n") + "--- !u!114 &300\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: 2da0c512f12947e489f739169773d7ca, type: 3}\n  m_TextComponent: {fileID: 200}\n";
            LetteraTmpYaml yaml = Yaml();
            string done = yaml.Convert(doc, "Form.prefab");
            Assert.AreEqual(0, yaml.Converted);
            Assert.AreEqual(1, yaml.SkippedInFields);
            StringAssert.Contains("f4688fdb", done);
            Assert.IsTrue(yaml.Losses.Any(l => l.Contains("input field")));
        }

        [Test]
        public void HelpersGoAndEventsAndOverridesFollow()
        {
            string doc = "%YAML 1.1\n--- !u!114 &1\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: 058cba836c1846c3aa1c5fd2e28aea77, type: 3}\n" +
                "--- !u!114 &2\nMonoBehaviour:\n  m_Script: {fileID: 11500000, guid: 11112222, type: 3}\n  m_OnClick:\n    m_TargetAssemblyTypeName: TMPro.TMP_Text, Unity.TextMeshPro\n    m_MethodName: set_text\n" +
                "--- !u!1001 &3\nPrefabInstance:\n  m_Modification:\n    - target: {fileID: 5, guid: 9}\n      propertyPath: m_text\n      value: x\n    - target: {fileID: 5, guid: 9}\n      propertyPath: m_fontStyle\n      value: 1\n";
            LetteraTmpYaml yaml = Yaml();
            string done = yaml.Convert(doc, "x");
            Assert.AreEqual(1, yaml.Removed);
            StringAssert.DoesNotContain("058cba83", done);
            StringAssert.Contains("FirstSymbol.Lettera.LetteraText, FirstSymbol.Lettera", done);
            StringAssert.Contains("m_MethodName: set_Text", done);
            StringAssert.Contains("propertyPath: m_Data.Text", done);
            Assert.IsTrue(yaml.Losses.Any(l => l.Contains("m_fontStyle")));
        }
    }
}
