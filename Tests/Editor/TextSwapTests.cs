using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using FirstSymbol.Lettera.Tests;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Converting a text keeps what pointed at it and what worked on it, and one undo takes it all back.</summary>
    public class TextSwapTests : TextFixture
    {
        [SetUp]
        public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        sealed class Stage
        {
            public GameObject Text;
            public SwapReferenceHolder Holder;
            public LetteraParamLink Link;
        }

        static Stage Build()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var text = go.AddComponent<LetteraText>();
            text.Text = "before";
            var selectable = go.AddComponent<LetteraSelectable>();
            selectable.SelectOnRightClick = true;
            var editable = go.AddComponent<LetteraEditable>();
            editable.PasteKeepsStyles = true;
            var sequencer = go.AddComponent<LetteraSequencer>();
            sequencer.Speed = 2.5f;
            sequencer.Cycles = 3;

            var holder = new GameObject("Holder").AddComponent<SwapReferenceHolder>();
            holder.AnyText = text;
            holder.CanvasOnly = text;
            holder.Selectable = selectable;
            holder.Sequencer = sequencer;
            var setText = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), text, "set_Text");
            UnityEventTools.AddStringPersistentListener(holder.Changed, setText, "from the event");
            holder.Changed.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);

            var link = new GameObject("Link").AddComponent<LetteraParamLink>();
            link.Text = text;
            return new Stage { Text = go, Holder = holder, Link = link };
        }

        [Test]
        public void CanvasToWorldMovesEveryReferenceAndCarriesWhatWorksOnTheText()
        {
            Stage stage = Build();
            GameObject go = stage.Text;
            LetteraWorldText world = LetteraMenus.ConvertToWorld(go.GetComponent<LetteraText>());

            Assert.IsNotNull(world, "a selectable no longer stops the conversion");
            Assert.AreEqual("before", world.Text);
            Assert.AreSame(world, stage.Holder.AnyText, "a field of a project's script");
            Assert.AreSame(world, stage.Link.Text, "a field of a Lettera component");
            Assert.AreSame(world, stage.Holder.Changed.GetPersistentTarget(0), "an event's target");
            Assert.AreEqual("set_Text", stage.Holder.Changed.GetPersistentMethodName(0));
            Assert.IsTrue(stage.Holder.CanvasOnly == null, "a field typed for Canvas text cannot hold world text, so it is left");
            Assert.AreEqual(1, LetteraTextSwap.Last.Unfit);

            var selectable = go.GetComponent<LetteraSelectable>();
            var editable = go.GetComponent<LetteraEditable>();
            var sequencer = go.GetComponent<LetteraSequencer>();
            Assert.AreEqual(1, go.GetComponents<LetteraSelectable>().Length);
            Assert.IsNotNull(editable);
            Assert.IsTrue(selectable.SelectOnRightClick, "the selectable's values came back");
            Assert.IsTrue(editable.PasteKeepsStyles, "the editable's values came back");
            Assert.AreEqual(2.5f, sequencer.Speed);
            Assert.AreEqual(3, sequencer.Cycles);
            Assert.AreEqual(3, LetteraTextSwap.Last.Carried);
            Assert.AreSame(world, selectable.TextComponent, "the selectable works on the new text");
            Assert.AreSame(selectable, stage.Holder.Selectable, "a reference to a carried component follows it");
            Assert.AreSame(sequencer, stage.Holder.Sequencer);

            // The event calls the new text: the type it names followed the target.
            stage.Holder.Changed.Invoke();
            Assert.AreEqual("from the event", world.Text);

            Undo.PerformUndo();
            var back = go.GetComponent<LetteraText>();
            Assert.IsNotNull(back, "one undo brings the Canvas text back");
            Assert.IsNull(go.GetComponent<LetteraWorldText>());
            Assert.AreEqual("before", back.Text);
            Assert.AreEqual(1, go.GetComponents<LetteraText>().Length);
            // An object brought back by undo keeps its instance id, though a field may hold another C# wrapper of it.
            Same(back, stage.Holder.AnyText);
            Same(back, stage.Holder.CanvasOnly);
            Same(back, stage.Link.Text);
            Same(back, stage.Holder.Changed.GetPersistentTarget(0));
            Assert.AreEqual(1, go.GetComponents<LetteraSelectable>().Length);
            Assert.AreEqual(1, go.GetComponents<LetteraEditable>().Length);
            Assert.AreEqual(1, go.GetComponents<LetteraSequencer>().Length);
            Same(go.GetComponent<LetteraSelectable>(), stage.Holder.Selectable);
            Assert.IsNotNull(go.GetComponentInParent<Canvas>(), "back inside its Canvas");
        }

        public static void Same(Object expected, Object actual)
        {
            Assert.IsTrue(expected != null && actual != null, $"{expected} and {actual}, both alive");
            Assert.AreEqual(expected.GetInstanceID(), actual.GetInstanceID(), $"{expected} and {actual}");
        }

        [Test]
        public void WorldToCanvasAndBackKeepsReferences()
        {
            Stage stage = Build();
            GameObject go = stage.Text;
            LetteraWorldText world = LetteraMenus.ConvertToWorld(go.GetComponent<LetteraText>());
            LetteraText canvas = LetteraMenus.ConvertToCanvas(world);
            Assert.IsNotNull(canvas);
            Assert.AreSame(canvas, stage.Holder.AnyText);
            Assert.AreSame(canvas, stage.Link.Text);
            Assert.AreSame(canvas, stage.Holder.Changed.GetPersistentTarget(0));
            Assert.AreSame(canvas, go.GetComponent<LetteraSelectable>().TextComponent);
            stage.Holder.Changed.Invoke();
            Assert.AreEqual("from the event", canvas.Text);
        }

        [Test]
        public void AComponentThatNeedsTheOldKindStopsTheConversion()
        {
            var go = new GameObject("World", typeof(RectTransform));
            var world = go.AddComponent<LetteraWorldText>();
            go.AddComponent<SwapNeedsWorldText>();
            string why = LetteraTextSwap.WhyNot(world, typeof(LetteraText));
            Assert.IsNotNull(why);
            StringAssert.Contains("World Text", why);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("needs a Lettera World Text"));
            Assert.IsNull(LetteraMenus.ConvertToCanvas(world));
            Assert.IsNotNull(go.GetComponent<LetteraWorldText>(), "nothing changed");
            Assert.IsNotNull(go.GetComponent<SwapNeedsWorldText>());
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ThoseThatNeedTheTextAreFoundInOrder()
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var text = go.AddComponent<LetteraWorldText>();
            go.AddComponent<LetteraEditable>();
            go.AddComponent<SwapNeedsWorldText>();
            var found = LetteraTextSwap.Dependents(text);
            Assert.AreEqual(3, found.Count);
            Assert.IsInstanceOf<LetteraSelectable>(found[0], "added for the editable, before it");
            Assert.IsInstanceOf<LetteraEditable>(found[1]);
            Assert.IsInstanceOf<SwapNeedsWorldText>(found[2]);
            Object.DestroyImmediate(go);
        }
    }
}
