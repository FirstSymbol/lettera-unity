using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class ConstructorInspectorTests : TextFixture
    {
        [Test]
        public void PickerListsEveryBuiltInEffectInAKind()
        {
            foreach (string kind in new[] { LetteraStyleTemplates.Basics, LetteraStyleTemplates.Type, LetteraStyleTemplates.Lines, LetteraStyleTemplates.Look, LetteraStyleTemplates.Layout, LetteraStyleTemplates.Links, LetteraStyleTemplates.Utility })
                Assert.IsTrue(LetteraStyleTemplates.All.Any(t => t.Kind == kind), kind);
            foreach (string driven in new[] { LetteraStyleTemplates.WholeText, LetteraStyleTemplates.Tags, LetteraStyleTemplates.InlineTags, LetteraStyleTemplates.Markdown, LetteraStyleTemplates.AutoDetect, LetteraStyleTemplates.Protection })
                Assert.IsTrue(LetteraStyleTemplates.All.Any(t => t.Driven == driven), driven);
        }

        [Test]
        public void EveryTemplateCreatesWhatItPromises()
        {
            foreach (LetteraStyleTemplate template in LetteraStyleTemplates.All)
            {
                Assert.IsNotNull(template.Matcher(), template.Name);
                template.Effect?.Invoke();
            }
        }

        [Test]
        public void SearchFindsTheGradientTemplate()
        {
            Assert.IsTrue(LetteraStyleTemplates.Find("gra").Any(t => t.Name == "Gradient Fill"));
            Assert.IsEmpty(LetteraStyleTemplates.Find("zzzzz"));
            Assert.AreEqual(LetteraStyleTemplates.All.Count, LetteraStyleTemplates.Find("").Count);
        }

        static LetteraStyleTemplate Template(string driven, string name) => LetteraStyleTemplates.All.First(t => t.Driven == driven && t.Name == name);

        (LetteraWorldText text, SerializedObject serialized, LetteraStyleList list, VisualElement root) Listed()
        {
            LetteraWorldText text = Make("x");
            var serialized = new SerializedObject(text);
            var list = new LetteraStyleList(serialized, "m_Data.Styles");
            return (text, serialized, list, list.Build());
        }

        static int PaintControls(VisualElement root) => root.Query(className: "lt-paint").ToList().Count;

        [Test]
        public void WholeTextStrokeShowsItsEssentialParameters()
        {
            var (_, _, list, root) = Listed();
            list.Add(Template(LetteraStyleTemplates.WholeText, "Stroke"));
            Assert.AreEqual(1, PaintControls(root), "The paint.");
            VisualElement body = root.Q(className: "lt-style__body");
            Assert.IsTrue(body.Query<Label>(className: "lt-row__label").ToList().Any(l => l.text == "Width"));
        }

        [Test]
        public void TagStrokeShowsNeither()
        {
            var (_, _, list, root) = Listed();
            list.Add(Template(LetteraStyleTemplates.Tags, "Stroke"));
            Assert.AreEqual(0, PaintControls(root));
            VisualElement body = root.Q(className: "lt-style__body");
            Assert.IsFalse(body.Query<Label>(className: "lt-row__label").ToList().Any(l => l.text == "Width"));
        }

        [Test]
        public void ChangedParameterAppearsEvenOnATagStyle()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new StrokeEffect { Width = 0.3f }, "k"));
            var serialized = new SerializedObject(text);
            VisualElement root = new LetteraStyleList(serialized, "m_Data.Styles").Build();
            Assert.IsTrue(root.Query<Label>(className: "lt-row__label").ToList().Any(l => l.text == "Width"));
        }

        [Test]
        public void UndoRemovesAnAddedStyleInOneStep()
        {
            var (text, serialized, list, _) = Listed();
            Undo.IncrementCurrentGroup();
            list.Add(Template(LetteraStyleTemplates.WholeText, "Fill"));
            Assert.AreEqual(1, text.Styles.Count);
            Undo.PerformUndo();
            serialized.Update();
            Assert.AreEqual(0, text.Styles.Count);
        }

        [Test]
        public void GradientFillTemplateShowsAStopPerColor()
        {
            var (_, _, list, root) = Listed();
            list.Add(Template(LetteraStyleTemplates.WholeText, "Gradient Fill"));
            Assert.AreEqual(2, root.Query(className: "lt-gradient__stop").ToList().Count);
        }

        [Test]
        public void PaintRowsThatDoNothingStayHidden()
        {
            LetteraWorldText text = Make("x");
            var fill = new FillEffect();
            LetteraPaint paint = LetteraPaint.Of(LetteraGradient.Of(Color.red, Color.blue));
            paint.Settings.Shape = LetteraPaintShape.Linear;
            fill.SetPaint(paint);
            text.Styles.Add(LetteraStyle.ForWholeText(fill));
            VisualElement root = new LetteraStyleList(new SerializedObject(text), "m_Data.Styles").Build();
            var labels = root.Query<Label>(className: "lt-row__label").ToList().Select(l => l.text).ToList();
            CollectionAssert.DoesNotContain(labels, "Fit", "A linear gradient has no fit.");
            CollectionAssert.Contains(labels, "Edge");

            paint.Settings.Shape = LetteraPaintShape.Conic;
            root = new LetteraStyleList(new SerializedObject(text), "m_Data.Styles").Build();
            labels = root.Query<Label>(className: "lt-row__label").ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "Fit");
            CollectionAssert.DoesNotContain(labels, "Edge", "A conic gradient has no edge.");
        }

        [Test]
        public void CopyAndPasteAcrossComponentsKeepsNestedChildren()
        {
            var texture = new Texture2D(1, 1);
            var fill = new FillEffect();
            fill.SetPaint(LetteraPaint.Of(texture));
            LetteraWorldText a = Make("x");
            a.Styles.Add(LetteraStyle.ForTag(new CompositeEffect(fill, new ColorEffect()), "k"));
            LetteraWorldText b = Make("x");

            LetteraStyle copy = a.Styles[0].Clone();
            b.Styles.Add(copy);
            var composite = (CompositeEffect)b.Styles[0].Effect;
            Assert.AreEqual(2, composite.Children.Count);
            Assert.AreSame(texture, ((FillEffect)composite.Children[0]).Paint.Inline.Texture);
            Assert.AreNotSame(fill, composite.Children[0]);
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void SelectingTwoTextsEditsASharedField()
        {
            LetteraWorldText a = Make("x"), b = Make("y");
            var serialized = new SerializedObject(new Object[] { a, b });
            serialized.FindProperty("m_Data.FontSize").floatValue = 44;
            serialized.ApplyModifiedProperties();
            Assert.AreEqual(44f, a.FontSize);
            Assert.AreEqual(44f, b.FontSize);
        }

        // ------------------------------------------------------------- warnings

        static string[] Chips(VisualElement root) => root.Query<Label>(className: "lt-chip").ToList().Select(l => l.text).ToArray();

        [Test]
        public void DuplicateTagWarns()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new ColorEffect(), "k"));
            text.Styles.Add(LetteraStyle.ForTag(new BoldEffect(), "k"));
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(text);
            VisualElement root = editor.CreateInspectorGUI();
            StringAssert.Contains("bound twice", string.Join(" ", Chips(root)));
            Object.DestroyImmediate(editor);
        }

        [Test]
        public void HighFitBudgetWarns()
        {
            LetteraWorldText text = Make("x");
            text.FitBudgets.Add(new FitByGlyphWidth { Limit = 5 });
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(text);
            StringAssert.Contains("above 3%", string.Join(" ", Chips(editor.CreateInspectorGUI())));
            Object.DestroyImmediate(editor);
        }

        // ------------------------------------------------------------- asset inspectors

        static void AssertBuilds(Object target)
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            VisualElement root = editor.CreateInspectorGUI();
            Assert.IsNotNull(root, target.GetType().Name);
            Assert.IsTrue(root.ClassListContains("lt-root"), target.GetType().Name);
            Object.DestroyImmediate(editor);
        }

        [Test]
        public void EveryAssetHasOurInspector()
        {
            var assets = new ScriptableObject[]
            {
                ScriptableObject.CreateInstance<LetteraFontVariant>(),
                ScriptableObject.CreateInstance<LetteraFontStack>(),
                ScriptableObject.CreateInstance<LetteraSystemFont>(),
                ScriptableObject.CreateInstance<LetteraColorFont>(),
                ScriptableObject.CreateInstance<LetteraStylePreset>(),
                ScriptableObject.CreateInstance<LetteraEffectPreset>(),
                ScriptableObject.CreateInstance<LetteraPaintLibrary>(),
            };
            foreach (ScriptableObject asset in assets)
            {
                AssertBuilds(asset);
                Object.DestroyImmediate(asset);
            }
            AssertBuilds(Latin);
        }

        // ------------------------------------------------------------- custom effects

        [Test]
        public void GeneratedCustomEffectCompilesAndRenders()
        {
            const string path = "Assets/Temp_LetteraCustomEffectTest.shader";
            File.WriteAllText(path, LetteraCustomEffectTemplate.Text("Test Effect"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            try
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assert.IsNotNull(shader);
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader), string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));

                var material = new Material(shader);
                LetteraWorldText text = Make("", 280, 280, null, 120);
                text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
                text.VerticalAlignment = LetteraVerticalAlignment.Middle;
                text.Styles.Add(LetteraStyle.ForTag(new MaterialEffect { Material = material }, "k"));
                text.MarkStylesDirty();
                text.Text = "<k>H</k>";
                text.ForceRebuild();
                Texture2D image = Render(300, 300, Color.black);
                int lit = 0;
                foreach (Color c in image.GetPixels())
                    if (c.maxColorComponent > 0.3f)
                        lit++;
                Assert.Greater(lit, 300);
                Object.DestroyImmediate(material);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
