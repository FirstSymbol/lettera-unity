using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace FirstSymbol.Lettera.Tests
{
    /// <summary>Fields in a running player loop, with a real EventSystem and frames between the steps.</summary>
    public class EditingPlayTests
    {
        GameObject m_Canvas, m_Events;

        [SetUp]
        public void SetUp()
        {
            m_Canvas = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            m_Canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            m_Events = new GameObject("Events", typeof(EventSystem));
            LetteraInput.NoDefaultSource = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (LetteraSelectable.Current != null) LetteraSelectable.Current.Deactivate();
            LetteraInput.NoDefaultSource = false;
            Object.Destroy(m_Canvas);
            Object.Destroy(m_Events);
        }

        LetteraEditable MakeField(string text = "")
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(m_Canvas.transform, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(400, 100);
            var label = go.AddComponent<LetteraText>();
            label.Text = text;
            return go.AddComponent<LetteraEditable>();
        }

        [UnityTest]
        public IEnumerator ActivatingHoldsTheEventSystemsSelection()
        {
            LetteraEditable field = MakeField("abc");
            field.Selectable.Activate();
            yield return null;
            Assert.AreSame(field.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.AreSame(field, LetteraEditable.Current);
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            yield return null;
            Assert.IsFalse(field.Selectable.HasFocus, "a deselect that nothing holds ends the session");
        }

        [UnityTest]
        public IEnumerator PressingAToolbarKeepsTheFocus()
        {
            LetteraEditable field = MakeField("abc");
            field.Selectable.Activate();
            yield return null;
            int lost = 0;
            field.FocusLost += _ => lost++;
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LetteraKeepFocus));
            bar.transform.SetParent(m_Canvas.transform, false);
            // A press deselects first, reaches its target after that, and the check runs at the end of the frame.
            EventSystem.current.SetSelectedGameObject(null);
            bar.GetComponent<LetteraKeepFocus>().OnPointerDown(new PointerEventData(EventSystem.current));
            yield return null;
            yield return null;
            Assert.AreEqual(0, lost);
            Assert.IsTrue(field.Selectable.HasFocus);
            Assert.AreSame(field.gameObject, EventSystem.current.currentSelectedGameObject);
            field.Selectable.Input.Text("x");
            Assert.AreEqual("xabc", field.Source, "a first activation stands at the start");
        }

        [UnityTest]
        public IEnumerator TheCaretAndHighlightDrawBesideTheTextWithoutRebuildingIt()
        {
            LetteraEditable field = MakeField("Some text");
            var label = field.GetComponent<LetteraText>();
            field.Selectable.Activate();
            yield return null;
            yield return null;
            LetteraCaret caret = field.GetComponentInChildren<LetteraCaret>();
            Assert.IsNotNull(caret);
            Assert.Greater(caret.rectTransform.rect.width, 0);
            int rebuilds = label.RebuildCount;
            field.Selectable.Select(0, 4);
            yield return null;
            yield return null;
            Assert.AreEqual(rebuilds, label.RebuildCount);
            Assert.IsNotNull(m_Canvas.GetComponentInChildren<LetteraSelectionOverlay>());
            Assert.IsFalse(caret.IsBlinkOn, "a range shows no caret");
        }

        [UnityTest]
        public IEnumerator TheCaretLandsInTheFrameOfTheKeystroke()
        {
            LetteraEditable field = MakeField("");
            var label = field.GetComponent<LetteraText>();
            field.Selectable.Activate();
            yield return null;
            yield return null;
            LetteraCaret caret = field.GetComponentInChildren<LetteraCaret>();
            float before = caret.CaretRect.x;
            field.Selectable.Input.Text("a");
            yield return new WaitForEndOfFrame();
            Assert.Greater(caret.CaretRect.x, before);
            Assert.AreEqual(1, label.GlyphCount);
        }

        [UnityTest]
        public IEnumerator DestroyingAFieldLeavesNothingBehind()
        {
            LetteraEditable field = MakeField("abc");
            field.Selectable.Activate();
            field.Selectable.SelectAll();
            yield return null;
            yield return null;
            Object.Destroy(field.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(0, m_Canvas.transform.childCount);
            Assert.IsNull(LetteraSelectable.Current);
        }
    }
}
