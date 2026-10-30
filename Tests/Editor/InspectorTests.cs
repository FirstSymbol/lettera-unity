using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class InspectorTests : TextFixture
    {
        static VisualElement Build(Object target, out UnityEditor.Editor editor)
        {
            editor = UnityEditor.Editor.CreateEditor(target);
            return editor.CreateInspectorGUI();
        }

        [Test]
        public void WorldTextInspectorHasAllTabsAndControls()
        {
            LetteraWorldText text = Make("x");
            VisualElement root = Build(text, out UnityEditor.Editor editor);
            Assert.IsNotNull(root);
            Assert.AreEqual(4, root.Query(className: "lt-tab").ToList().Count);
            Assert.AreEqual(4, root.Query(className: "lt-page").ToList().Count);
            Assert.Greater(root.Query(className: "lt-seg").ToList().Count, 4);
            Assert.Greater(root.Query(className: "lt-switch").ToList().Count, 3);
            Object.DestroyImmediate(editor);
        }

        [Test]
        public void CanvasTextInspectorBuilds()
        {
            var go = new GameObject("Canvas Text", typeof(RectTransform));
            var text = go.AddComponent<LetteraText>();
            VisualElement root = Build(text, out UnityEditor.Editor editor);
            Assert.AreEqual(4, root.Query(className: "lt-tab").ToList().Count);
            Object.DestroyImmediate(editor);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void StylesShowAsCardsWithTheirTagAndEffect()
        {
            LetteraWorldText text = Make("x");
            text.Styles.Add(LetteraStyle.ForTag(new ShadowEffect(), "warning"));
            VisualElement root = Build(text, out UnityEditor.Editor editor);
            Assert.AreEqual(1, root.Query(className: "lt-style").ToList().Count);
            Assert.AreEqual("<warning>", root.Q<Label>(className: "lt-style__tag").text);
            Assert.AreEqual("Shadow", root.Q<Label>(className: "lt-style__type").text);
            Object.DestroyImmediate(editor);
        }

        [Test]
        public void LabelsShowMarkupLiterally()
        {
            LetteraWorldText text = Make("x");
            VisualElement root = Build(text, out UnityEditor.Editor editor);
            root.Query<Label>(className: "lt-vocab__tag").ForEach(label => Assert.IsFalse(label.enableRichText, label.text));
            Object.DestroyImmediate(editor);
        }
    }
}
