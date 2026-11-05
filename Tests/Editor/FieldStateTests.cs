using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A field's text, selection, scroll and running composition, kept and put back: by a script, across a pause, and when the screen turns.</summary>
    public class FieldStateTests : EditingFixture
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            LetteraEditable.ScreenSizeOverride = null;
            foreach (GameObject go in m_Objects)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Objects.Clear();
        }

        static readonly LetteraClause[] Clauses = { new LetteraClause { Length = 1, Kind = LetteraClauseKind.Converted }, new LetteraClause { Length = 1, Kind = LetteraClauseKind.ActiveRaw } };

        LetteraEditable Composing(string text, int start, int end, string composition)
        {
            LetteraEditable field = Field(text);
            field.Selectable.Select(start, end);
            field.Selectable.Input.Composition(composition, composition.Length, Clauses);
            Assert.IsTrue(field.IsComposing);
            return field;
        }

        [Test]
        public void ARunningCompositionIsKeptAndComesBackOverItsSelection()
        {
            LetteraEditable field = Composing("hello world", 6, 11, "にほ");
            int depth = field.UndoDepth;
            LetteraEditable.State state = field.CaptureState(true);
            Assert.IsTrue(field.IsComposing, "keeping it does not end it");
            Assert.AreEqual("hello world", state.Source, "the source without the composition");
            Assert.AreEqual((6, 11), (state.Anchor, state.Head), "the selection it replaces");
            Assert.AreEqual("にほ", state.Composition);
            Assert.AreEqual(2, state.CompositionCaret);
            Assert.AreEqual(2, state.Clauses.Length);

            field.DiscardComposition();
            field.SetSource("something else");
            field.RestoreState(state);
            Assert.AreEqual("hello world", field.PlainText, "the committed text is the saved one");
            Assert.IsTrue(field.IsComposing);
            Assert.AreEqual("hello にほ", field.ShownText);
            Assert.AreEqual(8, field.Selectable.Selection.Head, "the caret inside the composition");

            Type(field, "x");
            Assert.IsFalse(field.IsComposing);
            Assert.AreEqual("hello にほx", field.PlainText, "typing commits what was put back, then types");
            Assert.Greater(field.UndoDepth, depth);
        }

        [Test]
        public void AKeyAfterARestoredCompositionCommitsItAndActs()
        {
            LetteraEditable field = Composing("ab", 2, 2, "cd");
            LetteraEditable.State state = field.CaptureState(true);
            field.DiscardComposition();
            field.RestoreState(state);
            Press(field, LetteraKeyCode.Backspace);
            Assert.AreEqual("abc", field.PlainText, "no keyboard knows of it, so a key is not swallowed");
            field.RestoreState(state);
            field.Selectable.Input.Composition("ef", 2, null);
            Assert.AreEqual("abcd", field.PlainText, "a new composition commits the old first");
            Assert.AreEqual("abcdef", field.ShownText);
        }

        [Test]
        public void TheStateWithoutKeepingCommitsAsBefore()
        {
            LetteraEditable field = Composing("ab", 2, 2, "cd");
            LetteraEditable.State state = field.CaptureState();
            Assert.IsFalse(field.IsComposing);
            Assert.AreEqual("abcd", state.Source);
            Assert.IsTrue(string.IsNullOrEmpty(state.Composition));
        }

        [Test]
        public void AStateGoesThroughJson()
        {
            LetteraEditable field = Composing("hello", 0, 5, "日本");
            LetteraEditable.State state = field.CaptureState(true);
            state.Scroll = new Vector2(-12.5f, 3);
            LetteraEditable.State back = JsonUtility.FromJson<LetteraEditable.State>(JsonUtility.ToJson(state));
            Assert.AreEqual(state.Source, back.Source);
            Assert.AreEqual((state.Anchor, state.Head, state.Bias), (back.Anchor, back.Head, back.Bias));
            Assert.AreEqual(state.Scroll, back.Scroll);
            Assert.AreEqual(state.Composition, back.Composition);
            Assert.AreEqual(state.CompositionCaret, back.CompositionCaret);
            Assert.AreEqual(LetteraClauseKind.ActiveRaw, back.Clauses[1].Kind);
        }

        [Test]
        public void APauseKeepsAndPutsBackWhenSwitchedOn()
        {
            LetteraEditable field = Composing("hello", 5, 5, "ab");
            field.KeepStateOnPause = true;
            int depth = field.UndoDepth;
            int version = field.Version;
            string committed = null;
            field.ValueChanged += v => committed = v;

            field.gameObject.SendMessage("OnApplicationFocus", false);
            field.gameObject.SendMessage("OnApplicationPause", true);
            Assert.IsFalse(field.IsComposing, "away, the composition is out of the field");
            Assert.AreEqual("hello", field.PlainText, "and not in its value");
            Assert.AreEqual(depth, field.UndoDepth);
            Assert.AreEqual(version, field.Version);
            Assert.IsNull(committed);

            field.gameObject.SendMessage("OnApplicationPause", false);
            field.gameObject.SendMessage("OnApplicationFocus", true);
            Assert.IsTrue(field.IsComposing, "back, it runs again");
            Assert.AreEqual("helloab", field.ShownText);
            Assert.AreEqual(7, field.Selectable.Selection.Head);
            Assert.AreEqual(depth, field.UndoDepth, "with no undo step");
        }

        [Test]
        public void SwitchedOffAFocusLossCommitsAndAPauseLeavesItAlone()
        {
            LetteraEditable field = Composing("hello", 5, 5, "ab");
            Assert.IsFalse(field.KeepStateOnPause, "off by default");
            field.gameObject.SendMessage("OnApplicationPause", true);
            Assert.IsTrue(field.IsComposing);
            field.gameObject.SendMessage("OnApplicationPause", false);
            field.gameObject.SendMessage("OnApplicationFocus", false);
            Assert.IsFalse(field.IsComposing);
            Assert.AreEqual("helloab", field.PlainText);
        }

        [Test]
        public void WhatChangedWhileAwayStays()
        {
            LetteraEditable changed = Composing("hello", 5, 5, "ab");
            changed.KeepStateOnPause = true;
            changed.AppLeaving(true);
            changed.SetSource("from a script");
            changed.AppReturning();
            Assert.AreEqual("from a script", changed.PlainText, "a script's change wins");
            Assert.IsFalse(changed.IsComposing);

            LetteraEditable blurred = Composing("hello", 5, 5, "ab");
            blurred.KeepStateOnPause = true;
            blurred.AppLeaving(false);
            blurred.Selectable.Deactivate();
            blurred.AppReturning();
            Assert.IsFalse(blurred.IsComposing, "with no focus there is no keyboard to finish it");
            Assert.AreEqual("helloab", blurred.PlainText, "so what was composed goes in");
        }

        // ------------------------------------------------------------- scroll and rotation

        LetteraEditable InViewport(string value, float width, out RectTransform viewport, out LetteraText text)
        {
            var canvas = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
            m_Objects.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(canvas.transform, false);
            viewport = (RectTransform)viewportObject.transform;
            viewport.sizeDelta = new Vector2(width, 40);
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(viewport, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 40);
            text = go.AddComponent<LetteraText>();
            text.Font = Latin;
            text.FontSize = 20;
            text.WordWrap = false;
            text.Text = value;
            text.ForceRebuild();
            var field = go.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            return field;
        }

        [Test]
        public void TheScrollComesBack()
        {
            LetteraEditable field = InViewport("", 100, out _, out LetteraText text);
            Vector2 home = text.rectTransform.anchoredPosition;
            Type(field, new string('a', 60));
            field.ScrollCaretIntoView();
            Vector2 scrolled = text.rectTransform.anchoredPosition;
            Assert.Less(scrolled.x, home.x - 50);
            LetteraEditable.State state = field.CaptureState();
            field.Selectable.PlaceCaret(0);
            field.ScrollCaretIntoView();
            Assert.AreEqual(home.x, text.rectTransform.anchoredPosition.x, 0.01f);
            field.RestoreState(state);
            Assert.AreEqual(scrolled, text.rectTransform.anchoredPosition);
            Assert.AreEqual(60, field.Selectable.Selection.Head);
        }

        [Test]
        public void WhenTheScreenTurnsTheCaretStaysInViewOnTheFrameAfter()
        {
            var screen = new Vector2Int(1080, 1920);
            LetteraEditable.ScreenSizeOverride = () => screen;
            LetteraEditable field = InViewport("", 100, out RectTransform viewport, out LetteraText text);
            field.KeepStateOnPause = true;
            Vector2 home = text.rectTransform.anchoredPosition;
            Type(field, new string('a', 60));
            field.ScrollCaretIntoView();
            field.Selectable.Tick();
            Vector2 scrolled = text.rectTransform.anchoredPosition;
            Assert.Less(scrolled.x, home.x - 50);

            // Turned: the field is now wide enough for all of it, which the canvas lays out at the end of the frame.
            screen = new Vector2Int(1920, 1080);
            field.Selectable.Tick();
            Assert.AreEqual(scrolled, text.rectTransform.anchoredPosition, "not before the layout");
            viewport.sizeDelta = new Vector2(2000, 40);
            text.rectTransform.sizeDelta = new Vector2(2000, 40);
            text.ForceRebuild();
            field.Selectable.Tick();
            Assert.AreEqual(home.x, text.rectTransform.anchoredPosition.x, 0.01f, "what fits does not scroll");
            Assert.AreEqual(60, field.Selectable.Selection.Head, "the selection stays");
        }

        [Test]
        public void SwitchedOffTheScreenTurningMovesNothing()
        {
            var screen = new Vector2Int(1080, 1920);
            LetteraEditable.ScreenSizeOverride = () => screen;
            LetteraEditable field = InViewport("", 100, out RectTransform viewport, out LetteraText text);
            Type(field, new string('a', 60));
            field.ScrollCaretIntoView();
            field.Selectable.Tick();
            Vector2 scrolled = text.rectTransform.anchoredPosition;
            screen = new Vector2Int(1920, 1080);
            field.Selectable.Tick();
            viewport.sizeDelta = new Vector2(2000, 40);
            text.rectTransform.sizeDelta = new Vector2(2000, 40);
            text.ForceRebuild();
            field.Selectable.Tick();
            Assert.AreEqual(scrolled, text.rectTransform.anchoredPosition);
        }

        [Test]
        public void TheInspectorHasTheSwitch()
        {
            LetteraEditable field = Field("x");
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(field);
            try
            {
                var labels = new List<string>();
                UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(editor.CreateInspectorGUI()).ForEach(l => labels.Add(l.text));
                CollectionAssert.Contains(labels, "Keep On Pause");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
