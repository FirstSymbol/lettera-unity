using System;
using System.Collections.Generic;
using System.Linq;
using FirstSymbol.Lettera.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Selection on a Canvas: what it draws, the shipped fields, touch, menus and inspectors.</summary>
    public class EditingCanvasTests : EditingFixture
    {
        readonly List<GameObject> m_Objects = new List<GameObject>();
        Canvas m_Canvas;
        Transform m_RootTransform;

        Transform Root
        {
            get
            {
                if (m_RootTransform != null) return m_RootTransform;
                var go = new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster));
                m_Objects.Add(go);
                m_Canvas = go.GetComponent<Canvas>();
                m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                m_RootTransform = go.transform;
                return m_RootTransform;
            }
        }

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_Objects)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Objects.Clear();
            m_RootTransform = null;
        }

        LetteraText CanvasText(string text, float width = 400, float height = 200, Transform parent = null)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : Root, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            var label = go.AddComponent<LetteraText>();
            label.Font = Latin;
            label.FontSize = 20;
            label.Text = text;
            label.ForceRebuild();
            return label;
        }

        // ------------------------------------------------------------- drawing

        [Test]
        public void TheHighlightStandsBehindAndSoDoesNotTouchTheText()
        {
            LetteraText text = CanvasText("Some text");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.Activate();
            Material before = text.materialForRendering;
            selectable.Select(0, 4);
            selectable.Tick();
            Transform overlay = Root.Cast<Transform>().First(t => t != text.transform);
            var graphic = overlay.GetComponent<LetteraSelectionOverlay>();
            Assert.IsNotNull(graphic);
            Assert.AreEqual(1, graphic.RectCount);
            Assert.Less(overlay.GetSiblingIndex(), text.transform.GetSiblingIndex(), "behind means an earlier sibling");
            Assert.AreEqual(((RectTransform)text.transform).sizeDelta, overlay.GetComponent<RectTransform>().sizeDelta);
            Assert.AreEqual(HideFlags.HideInHierarchy | HideFlags.DontSave, overlay.gameObject.hideFlags);
            Assert.AreSame(before, text.materialForRendering, "a plain text stays on its plain material");
        }

        [Test]
        public void AboveStandsInsideTheText()
        {
            LetteraText text = CanvasText("Some text");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.HighlightOrder = LetteraHighlightOrder.Above;
            selectable.Activate();
            selectable.Select(0, 4);
            selectable.Tick();
            Assert.IsNotNull(text.GetComponentInChildren<LetteraSelectionOverlay>());
            Assert.AreEqual(1, Root.childCount);
        }

        [Test]
        public void RemovingTheComponentRemovesWhatItDrew()
        {
            LetteraText text = CanvasText("Some text");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            field.Selectable.Tick();
            field.Selectable.SelectAll();
            field.Selectable.Tick();
            field.enabled = false;
            field.Selectable.enabled = false;
            Assert.AreEqual(1, Root.childCount);
            Assert.AreEqual(0, text.transform.childCount);
        }

        [Test]
        public void TheAutoCaretIsNotInTheHierarchyOrAScene()
        {
            LetteraText text = CanvasText("");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            field.Selectable.Tick();
            LetteraCaret caret = text.GetComponentInChildren<LetteraCaret>();
            Assert.IsNotNull(caret);
            Assert.AreEqual(HideFlags.HideInHierarchy | HideFlags.DontSave, caret.gameObject.hideFlags);
            Assert.Greater(caret.CaretRect.height, 0);
            Assert.AreEqual(6f / 100, caret.DrawnRect.width / caret.CaretRect.height, 0.001f);
        }

        [Test]
        public void ABlinkTouchesNoGeometry()
        {
            LetteraText text = CanvasText("Some text");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            field.Selectable.Tick();
            text.ForceRebuild();
            int rebuilds = text.RebuildCount;
            for (int i = 0; i < 40; i++)
            {
                Now += 0.1;
                field.Selectable.Tick();
            }
            Assert.AreEqual(rebuilds, text.RebuildCount);
        }

        [Serializable]
        class BlockCaret : LetteraCaret
        {
            public Rect Read;
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                Read = CaretRect;
                vh.Clear();
            }
        }

        [Test]
        public void ACustomCaretReadsItsRect()
        {
            LetteraText text = CanvasText("Some text");
            var childObject = new GameObject("Own Caret", typeof(RectTransform));
            childObject.transform.SetParent(text.transform, false);
            var own = childObject.AddComponent<BlockCaret>();
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Caret = own;
            field.Selectable.Activate();
            field.Selectable.Tick();
            Assert.Greater(own.CaretRect.height, 0);
        }

        // ------------------------------------------------------------- scrolling

        LetteraEditable InViewport(string value, float viewportWidth, out RectTransform viewport, out LetteraText text, bool wrap = false)
        {
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(Root, false);
            viewport = (RectTransform)viewportObject.transform;
            viewport.sizeDelta = new Vector2(viewportWidth, 40);
            text = CanvasText(value, viewportWidth, 40, viewport);
            text.WordWrap = wrap;
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            return field;
        }

        [Test]
        public void TypingPastTheEndScrollsWithoutARebuildAndDeletingScrollsBack()
        {
            LetteraEditable field = InViewport("", 100, out RectTransform viewport, out LetteraText text);
            Vector2 home = text.rectTransform.anchoredPosition;
            Type(field, new string('a', 60));
            field.ScrollCaretIntoView();
            Assert.Less(text.rectTransform.anchoredPosition.x, home.x - 50);
            field.Selectable.TryCaretOnScreen(null, out Rect caret);
            Rect view = field.Selectable.ViewportOnScreen(null);
            Assert.GreaterOrEqual(caret.x, view.xMin - 1);
            Assert.LessOrEqual(caret.x, view.xMax + 1);

            text.ForceRebuild();
            int rebuilds = text.RebuildCount;
            field.Selectable.PlaceCaret(0);
            field.ScrollCaretIntoView();
            Assert.AreEqual(home.x, text.rectTransform.anchoredPosition.x, 0.01f);
            Assert.AreEqual(rebuilds, text.RebuildCount, "scrolling rebuilds nothing");

            field.Selectable.PlaceCaret(60);
            field.ScrollCaretIntoView();
            for (int i = 0; i < 60; i++) Press(field, LetteraKeyCode.Backspace);
            field.ScrollCaretIntoView();
            Assert.AreEqual(home.x, text.rectTransform.anchoredPosition.x, 0.01f);
        }

        [Test]
        public void WhatFitsNeverScrolls()
        {
            LetteraEditable field = InViewport("", 300, out _, out LetteraText text);
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            Vector2 home = text.rectTransform.anchoredPosition;
            Type(field, "ab");
            field.ScrollCaretIntoView();
            Assert.AreEqual(home, text.rectTransform.anchoredPosition);
        }

        // ------------------------------------------------------------- focus

        [Test]
        public void AnotherFieldTakesTheFocusInOrder()
        {
            LetteraEditable first = CanvasText("a").gameObject.AddComponent<LetteraEditable>();
            LetteraEditable second = CanvasText("b").gameObject.AddComponent<LetteraEditable>();
            var log = new List<string>();
            first.FocusLost += r => log.Add("first lost " + r);
            second.FocusGained += () => log.Add("second gained");
            first.Selectable.Activate();
            second.Selectable.Activate();
            CollectionAssert.AreEqual(new[] { "first lost Pointer", "second gained" }, log);
            Assert.AreSame(second, LetteraEditable.Current);
        }

        [Test]
        public void AHiddenTextStaysHiddenUnderAMask()
        {
            LetteraText text = CanvasText("abc");
            text.Hide();
            text.Cull(new Rect(-1000, -1000, 2000, 2000), true);
            Assert.IsTrue(text.canvasRenderer.cull);
            text.Show();
            text.Cull(new Rect(-1000, -1000, 2000, 2000), true);
            Assert.IsFalse(text.canvasRenderer.cull);
        }

        // ------------------------------------------------------------- shipped fields

        [Test]
        public void TheInputFieldWorksAndSubmits()
        {
            GameObject root = LetteraFieldFactory.CreateInputField(Root);
            m_Objects.Add(root);
            var field = root.GetComponentInChildren<LetteraEditable>();
            Assert.IsNotNull(field.GetRule<SingleLineRule>());
            LetteraText hint = root.transform.Find("Viewport/Hint").GetComponent<LetteraText>();
            Assert.IsTrue(hint.IsVisible);
            field.Selectable.Activate();
            Type(field, "<b>");
            Assert.IsFalse(hint.IsVisible);
            Assert.AreEqual("<b>", field.PlainText, "a form field takes its characters as they are");
            int submits = 0;
            field.Submitted += _ => submits++;
            Press(field, LetteraKeyCode.Enter);
            Assert.AreEqual(1, submits);
        }

        [Test]
        public void TheTextAreaBreaksLines()
        {
            GameObject root = LetteraFieldFactory.CreateTextArea(Root);
            m_Objects.Add(root);
            var field = root.GetComponentInChildren<LetteraEditable>();
            field.Selectable.Activate();
            Type(field, "a");
            Press(field, LetteraKeyCode.Enter);
            Type(field, "b");
            Assert.AreEqual("a\nb", field.PlainText);
        }

        [Test]
        public void ThePasswordFieldShowsDotsAndItsToggleShowsTheValue()
        {
            GameObject root = LetteraFieldFactory.CreatePasswordField(Root);
            m_Objects.Add(root);
            var field = root.GetComponentInChildren<LetteraEditable>();
            field.Selectable.Activate();
            Type(field, "abc");
            Assert.AreEqual("•••", field.ShownText);
            var toggle = root.GetComponentInChildren<LetteraPasswordToggle>();
            toggle.Flip();
            Assert.AreEqual("abc", field.ShownText);
        }

        [Test]
        public void FieldsReferToNothingOutsideThePackage()
        {
            foreach (Func<Transform, GameObject> make in new Func<Transform, GameObject>[] { p => LetteraFieldFactory.CreateInputField(p), p => LetteraFieldFactory.CreateTextArea(p), p => LetteraFieldFactory.CreatePasswordField(p), LetteraFieldFactory.CreateDropdown })
            {
                GameObject root = make(Root);
                m_Objects.Add(root);
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true)) Assert.IsNotNull(behaviour, "a missing script");
            }
        }

        // ------------------------------------------------------------- dropdown

        LetteraDropdown MakeDropdown(params string[] options)
        {
            GameObject root = LetteraFieldFactory.CreateDropdown(Root);
            m_Objects.Add(root);
            var dropdown = root.GetComponent<LetteraDropdown>();
            dropdown.AddOptions(options);
            dropdown.FadeSeconds = 0;
            return dropdown;
        }

        [Test]
        public void AValueUpdatesTheCaptionAndNotifiesOnce()
        {
            LetteraDropdown dropdown = MakeDropdown("a", "b", "c");
            int changes = 0;
            dropdown.OnValueChanged.AddListener(_ => changes++);
            dropdown.Value = 2;
            Assert.AreEqual("c", dropdown.CaptionText.Text);
            Assert.AreEqual(1, changes);
            dropdown.Value = 2;
            Assert.AreEqual(1, changes);
            dropdown.SetValueWithoutNotify(1);
            Assert.AreEqual("b", dropdown.CaptionText.Text);
            Assert.AreEqual(1, changes);
            dropdown.Value = 99;
            Assert.AreEqual(-1, dropdown.Value);
            Assert.AreEqual("", dropdown.CaptionText.Text);
        }

        [Test]
        public void OptionsGoThroughLettera()
        {
            LetteraDropdown dropdown = MakeDropdown("مرحبا", "<b>x</b>");
            dropdown.Show();
            Assert.AreEqual(2, dropdown.Rows.Count);
            Assert.IsInstanceOf<LetteraText>(dropdown.Rows[0].Label);
            dropdown.Rows[1].Label.ForceRebuild();
            Assert.AreEqual("x", dropdown.Rows[1].Label.CleanText);
            dropdown.Hide();
            dropdown.ParseMarkup = false;
            dropdown.Show();
            dropdown.Rows[1].Label.ForceRebuild();
            Assert.AreEqual("<b>x</b>", dropdown.Rows[1].Label.CleanText);
            dropdown.Hide();
        }

        [Test]
        public void KeysPickSkippingDisabledAndCancelChangesNothing()
        {
            LetteraDropdown dropdown = MakeDropdown("a", "b", "c");
            dropdown.Options[1].Disabled = true;
            dropdown.SetValueWithoutNotify(0);
            dropdown.Show();
            dropdown.Step(1);
            Assert.AreEqual(2, dropdown.HighlightIndex);
            dropdown.OnCancel(null);
            Assert.IsFalse(dropdown.IsOpen);
            Assert.AreEqual(0, dropdown.Value);
            dropdown.Show();
            dropdown.Step(1);
            dropdown.OnSubmit(null);
            Assert.AreEqual(2, dropdown.Value);
            Assert.IsFalse(dropdown.IsOpen);
            dropdown.Show();
            dropdown.Choose(1);
            Assert.AreEqual(2, dropdown.Value, "a disabled option cannot be chosen");
            dropdown.Hide();
        }

        [Test]
        public void TypingALetterJumps()
        {
            LetteraDropdown dropdown = MakeDropdown("apple", "banana", "berry");
            dropdown.JumpToLetter('b');
            Assert.AreEqual(1, dropdown.Value);
            dropdown.JumpToLetter('b');
            Assert.AreEqual(2, dropdown.Value);
        }

        [Test]
        public void AnEmptyDropdownOpens()
        {
            LetteraDropdown dropdown = MakeDropdown();
            dropdown.Show();
            Assert.IsTrue(dropdown.IsOpen);
            dropdown.Hide();
        }

        [Test]
        public void NearTheBottomTheListOpensUpward()
        {
            LetteraDropdown dropdown = MakeDropdown("a", "b", "c");
            var rect = (RectTransform)dropdown.transform;
            var rootRect = (RectTransform)Root;
            rootRect.sizeDelta = new Vector2(800, 600);
            Canvas.ForceUpdateCanvases();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 2);
            dropdown.Show();
            var corners = new Vector3[4];
            dropdown.ListRect.GetWorldCorners(corners);
            var own = new Vector3[4];
            rect.GetWorldCorners(own);
            Assert.GreaterOrEqual(corners[0].y, own[1].y - 0.5f, "it stands over the control");
            dropdown.Hide();
        }

        // ------------------------------------------------------------- menu

        [Test]
        public void TheBuiltInMenuShowsARowForEachCommandAndRunsThem()
        {
            LetteraText text = CanvasText("abc");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            field.Selectable.Select(0, 1);
            LetteraContextMenu menu = LetteraContextMenu.CreateDefault();
            m_Objects.Add(menu.gameObject);
            field.Selectable.MenuPresenter = menu;
            field.Selectable.OpenMenu(new LetteraMenuPlacement { Point = new Vector2(10, 10), Trigger = LetteraMenuTrigger.Keyboard });
            Assert.IsTrue(menu.IsVisible);
            Assert.AreEqual(3, menu.ShownCount, "cut, copy and select all");
            Assert.AreEqual(0, menu.HighlightIndex, "the keyboard starts on the first entry");
            menu.HandleKey(LetteraKeyCode.Down, LetteraKeyModifiers.None);
            Assert.AreEqual(1, menu.HighlightIndex);
            menu.HandleKey(LetteraKeyCode.Up, LetteraKeyModifiers.None);
            menu.HandleKey(LetteraKeyCode.Up, LetteraKeyModifiers.None);
            Assert.AreEqual(2, menu.HighlightIndex, "it wraps");
            Assert.IsTrue(menu.HandleKey(LetteraKeyCode.A, LetteraKeyModifiers.None), "typed keys stay out of the field");
            menu.HandleKey(LetteraKeyCode.Escape, LetteraKeyModifiers.None);
            Assert.IsFalse(menu.IsVisible);
            Assert.AreEqual("abc", field.Source);
        }

        [Test]
        public void AMenuStaysOnScreen()
        {
            LetteraText text = CanvasText("abc");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.Activate();
            selectable.SelectAll();
            LetteraContextMenu menu = LetteraContextMenu.CreateDefault();
            m_Objects.Add(menu.gameObject);
            selectable.MenuPresenter = menu;
            selectable.OpenMenu(new LetteraMenuPlacement { Point = new Vector2(Screen.width + 500, -500), Trigger = LetteraMenuTrigger.Pointer });
            Vector2 at = menu.Panel.anchoredPosition;
            Assert.LessOrEqual(at.x, Screen.width);
            Assert.GreaterOrEqual(at.y, 0);
            menu.Hide();
        }

        [Test]
        public void AnObjectWithNoButtonRunsItsCommandOnAClick()
        {
            LetteraText text = CanvasText("abc");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.Activate();
            var go = new GameObject("Menu", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var item = new GameObject("Item", typeof(RectTransform));
            item.transform.SetParent(go.transform, false);
            var menu = go.AddComponent<LetteraContextMenu>();
            menu.Items.Add(new LetteraContextMenu.Item { Target = item, Command = new SelectAllCommand() });
            selectable.MenuPresenter = menu;
            selectable.OpenMenu(new LetteraMenuPlacement { Trigger = LetteraMenuTrigger.Pointer });
            item.GetComponent<LetteraMenuClick>().OnPointerClick(null);
            Assert.AreEqual(3, selectable.Selection.Length);
        }

        [Test]
        public void AToolbarToggleFollowsTheCaretAndKeepsTheFocus()
        {
            LetteraText text = CanvasText("<b>a</b>b");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.Activate();
            var go = new GameObject("Bold", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Toggle));
            go.transform.SetParent(Root, false);
            var control = go.AddComponent<LetteraCommandControl>();
            control.Command = new ToggleEffectCommand();
            control.Target = field.Selectable;
            typeof(LetteraCommandControl).GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(control, null);
            field.Selectable.PlaceCaret(1);
            control.Run();
            Assert.IsNotNull(go.GetComponent<LetteraKeepFocus>());
            Assert.IsTrue(field.Selectable.HasFocus);
        }

        // ------------------------------------------------------------- touch

        Vector2 ScreenOf(LetteraSelectable s, int index)
        {
            LetteraText text = s.GetComponent<LetteraText>();
            text.TryGetCaretRect(index, LetteraCaretBias.Forward, out Rect r);
            return RectTransformUtility.WorldToScreenPoint(null, text.transform.TransformPoint(new Vector3(r.x, r.center.y)));
        }

        [Test]
        public void TapsCountByTimeAndDistance()
        {
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            LetteraText text = CanvasText("one two three");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.NoHandles = true;
            selectable.Commands.Clear();
            var data = Pointer(ScreenOf(selectable, 5) + new Vector2(2, 0), id: 0);
            selectable.OnPointerDown(data);
            selectable.OnPointerUp(data);
            Now += 0.1;
            selectable.OnPointerDown(data);
            selectable.OnPointerUp(data);
            Assert.AreEqual((4, 7), (selectable.Selection.Start, selectable.Selection.End));
            Now += 1;
            data.position = ScreenOf(selectable, 5);
            selectable.OnPointerDown(data);
            selectable.OnPointerUp(data);
            Assert.IsTrue(selectable.Selection.IsCaret, "a lone tap clears it");
        }

        [Test]
        public void ALongPressSelectsAWordOrPlacesACaret()
        {
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            LetteraText text = CanvasText("one two three");
            var reader = text.gameObject.AddComponent<LetteraSelectable>();
            reader.NoHandles = true;
            reader.NoLoupe = true;
            reader.Commands.Clear();
            var data = Pointer(ScreenOf(reader, 5) + new Vector2(2, 0), id: 0);
            reader.OnPointerDown(data);
            reader.Tick();
            Assert.IsTrue(reader.Selection.IsCaret);
            Now += 0.6;
            reader.Tick();
            Assert.AreEqual((4, 7), (reader.Selection.Start, reader.Selection.End));
            reader.OnPointerUp(data);

            LetteraText other = CanvasText("one two three");
            var field = other.gameObject.AddComponent<LetteraEditable>();
            field.Selectable.NoHandles = true;
            field.Selectable.NoLoupe = true;
            field.Selectable.Commands.Clear();
            data = Pointer(ScreenOf(field.Selectable, 5) + new Vector2(2, 0), id: 0);
            Now += 2;
            field.Selectable.OnPointerDown(data);
            Now += 0.6;
            field.Selectable.Tick();
            Assert.IsTrue(field.Selectable.Selection.IsCaret);
            Assert.AreEqual(5, field.Selectable.Selection.Head);
            data.position = ScreenOf(field.Selectable, 9);
            field.Selectable.OnBeginDrag(data);
            field.Selectable.OnDrag(data);
            Assert.AreEqual(9, field.Selectable.Selection.Head, "the finger moves the caret");
            field.Selectable.OnPointerUp(data);
        }

        [Test]
        public void HandlesShowForTouchAndNotForAMouse()
        {
            LetteraText text = CanvasText("one two three");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.Commands.Clear();
            var mouse = Pointer(ScreenOf(selectable, 5) + new Vector2(2, 0));
            selectable.OnPointerDown(mouse);
            selectable.OnPointerUp(mouse);
            Now += 0.1;
            selectable.OnPointerDown(mouse);
            selectable.OnPointerUp(mouse);
            Assert.AreEqual(3, selectable.Selection.Length);

            var handlesObject = LetteraPrefabHandles.CreateDefault();
            m_Objects.Add(handlesObject.gameObject);
            Assert.IsFalse(handlesObject.ShowsEnds);
            selectable.Handles = handlesObject;

            Now += 2;
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            var finger = Pointer(ScreenOf(selectable, 5) + new Vector2(2, 0), id: 0);
            selectable.OnPointerDown(finger);
            selectable.OnPointerUp(finger);
            Now += 0.1;
            selectable.OnPointerDown(finger);
            selectable.OnPointerUp(finger);
            Assert.IsTrue(handlesObject.ShowsEnds);
            Assert.Greater(handlesObject.EndPosition.x, handlesObject.StartPosition.x);
        }

        [Test]
        public void AHandleDragNeverCollapsesAndSwaps()
        {
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            LetteraText text = CanvasText("one two three");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.NoLoupe = true;
            selectable.Commands.Clear();
            selectable.Select(4, 7);
            selectable.DragHandle(LetteraHandleDrag.Kind.End, ScreenOf(selectable, 4), ScreenOf(selectable, 4));
            Assert.AreEqual((4, 5), (selectable.Selection.Start, selectable.Selection.End));
            selectable.DragHandle(LetteraHandleDrag.Kind.End, ScreenOf(selectable, 2), ScreenOf(selectable, 2));
            Assert.AreEqual((2, 4), (selectable.Selection.Start, selectable.Selection.End));
        }

        [Test]
        public void AGrabKeepsItsOffset()
        {
            LetteraText text = CanvasText("one two three");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.NoLoupe = true;
            selectable.Commands.Clear();
            selectable.Select(4, 7);
            var go = new GameObject("Handle", typeof(RectTransform));
            m_Objects.Add(go);
            var drag = go.AddComponent<LetteraHandleDrag>();
            drag.Owner = selectable;
            drag.Which = LetteraHandleDrag.Kind.End;
            Rect end = selectable.EndpointOnScreen(LetteraHandleDrag.Kind.End, null);
            var data = Pointer(new Vector2(end.x + 10, end.center.y - 3));
            drag.OnPointerDown(data);
            drag.OnDrag(data);
            Assert.AreEqual(7, selectable.Selection.End, "no jump at the press");
        }

        [Test]
        public void TheLoupeShowsTheTextLarger()
        {
            LetteraText text = CanvasText("one two three");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            var loupe = LetteraMeshLoupe.CreateDefault();
            m_Objects.Add(loupe.gameObject);
            selectable.Loupe = loupe;
            ((ILetteraLoupe)loupe).Show(selectable);
            selectable.TryLineOnScreen(0, null, out Rect line);
            ((ILetteraLoupe)loupe).Move(line.center, line);
            Assert.IsTrue(loupe.Showing);
            Assert.AreEqual(loupe.Zoom, loupe.Copy.localScale.x, 0.001f);
            Assert.Greater(loupe.Copy.GetComponent<CanvasRenderer>().GetMesh().vertexCount, 0);
            ((ILetteraLoupe)loupe).Hide();
            Assert.IsFalse(loupe.Showing);
        }

        [Test]
        public void NothingAssignedStillWorks()
        {
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            LetteraText text = CanvasText("one two");
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.NoHandles = true;
            selectable.NoLoupe = true;
            selectable.Commands.Clear();
            var data = Pointer(ScreenOf(selectable, 2), id: 0);
            selectable.OnPointerDown(data);
            selectable.OnPointerUp(data);
            Now += 0.1;
            selectable.OnPointerDown(data);
            selectable.OnPointerUp(data);
            Assert.Greater(selectable.Selection.Length, 0);
        }

        [Test]
        public void ASwipeGoesToWhatScrollsUnlessAHoldCameFirst()
        {
            LetteraSelectable.ClassifyOverride = _ => LetteraPointerKind.Touch;
            var scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(Root, false);
            LetteraText text = CanvasText("one two three", 400, 100, scrollObject.transform);
            var selectable = text.gameObject.AddComponent<LetteraSelectable>();
            selectable.Commands.Clear();
            selectable.NoHandles = true;
            var data = Pointer(ScreenOf(selectable, 2), id: 0);
            selectable.OnPointerDown(data);
            selectable.OnBeginDrag(data);
            Assert.AreSame(scrollObject, data.pointerDrag, "the drag now belongs to the scroll view");
            selectable.OnPointerUp(data);
            Assert.IsTrue(selectable.Selection.IsCaret);
        }

        // ------------------------------------------------------------- inspectors

        static void AssertCustom(UnityEditor.Editor editor)
        {
            VisualElement root = editor.CreateInspectorGUI();
            Assert.IsNotNull(root, editor.GetType().Name);
            Assert.IsTrue(root.ClassListContains("lt-root"), editor.GetType().Name);
            Assert.AreEqual(0, root.Query<IMGUIContainer>().ToList().Count, editor.GetType().Name);
        }

        [Test]
        public void EveryNewComponentHasItsOwnInspector()
        {
            LetteraText text = CanvasText("abc");
            var go = text.gameObject;
            var field = go.AddComponent<LetteraEditable>();
            var targets = new List<Object>
            {
                field, field.Selectable, go.AddComponent<LetteraKeepFocus>(), go.AddComponent<LetteraContextMenu>(), go.AddComponent<LetteraPrefabHandles>(), go.AddComponent<LetteraMeshLoupe>(),
                go.AddComponent<LetteraCommandControl>(), go.AddComponent<LetteraDropdownRow>(), go.AddComponent<LetteraPasswordToggle>(),
            };
            var carrier = new GameObject("Caret", typeof(RectTransform));
            carrier.transform.SetParent(Root, false);
            targets.Add(carrier.AddComponent<LetteraCaret>());
            var dropdownObject = new GameObject("Dropdown", typeof(RectTransform));
            dropdownObject.transform.SetParent(Root, false);
            targets.Add(dropdownObject.AddComponent<LetteraDropdown>());
            var camera = new GameObject("Camera", typeof(Camera));
            m_Objects.Add(camera);
            targets.Add(camera.AddComponent<LetteraWorldRaycaster>());
            var preset = ScriptableObject.CreateInstance<LetteraFieldPreset>();
            targets.Add(preset);
            foreach (Object target in targets)
            {
                UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
                AssertCustom(editor);
                Object.DestroyImmediate(editor);
            }
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void ThePickerListsEveryRuleAndSearchFindsThem()
        {
            List<(string group, string title, Type type)> rules = LetteraRuleCatalog.Rules;
            foreach (Type type in new[] { typeof(SingleLineRule), typeof(PasswordRule), typeof(MaxLengthRule), typeof(PatternMaskRule), typeof(LetterCaseRule), typeof(SelectAllOnFocusRule), typeof(ReleaseOnCancelRule), typeof(EnterKeyRule), typeof(TabKeyRule), typeof(FormattingRule), typeof(CaretWatchRule), typeof(PastePlainRule), typeof(PasteLinkRule), typeof(KeyboardTraitsRule), typeof(KeyboardLiftRule), typeof(ValidationRule), typeof(HintDecorator), typeof(RaisedLabelDecorator), typeof(HelperTextDecorator), typeof(LengthCounterDecorator), typeof(WholeNumberCharFilter), typeof(DecimalCharFilter), typeof(EmailCharFilter), typeof(PersonNameCharFilter), typeof(AlphaNumericCharFilter) })
                Assert.IsTrue(rules.Any(r => r.type == type), type.Name);
            Assert.IsTrue(LetteraTypePicker.Matching(rules, "pass").Any(r => r.type == typeof(PasswordRule)));
        }

        [Test]
        public void AddingARuleIsOneUndoAndTheOrderWarningFollows()
        {
            LetteraText text = CanvasText("abc");
            var field = text.gameObject.AddComponent<LetteraEditable>();
            field.Rules.Add(new MaxLengthRule());
            field.Rules.Add(new SingleLineRule());
            var serialized = new SerializedObject(field);
            SerializedProperty rules = serialized.FindProperty("m_Rules");
            Assert.IsNotNull(LetteraRuleCatalog.OrderWarning(rules, 0));
            rules.MoveArrayElement(0, 1);
            serialized.ApplyModifiedProperties();
            Assert.IsNull(LetteraRuleCatalog.OrderWarning(serialized.FindProperty("m_Rules"), 0));

            var list = new LetteraReferenceList(serialized.FindProperty("m_Rules"), () => LetteraRuleCatalog.Rules);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            list.AddOf(typeof(LetterCaseRule));
            Assert.AreEqual(3, field.Rules.Count);
            Undo.RevertAllDownToGroup(group);
            Assert.AreEqual(2, field.Rules.Count);
        }

        [Test]
        public void TheSettingsPageIsCustom()
        {
            VisualElement root = LetteraSettingsProvider.Build();
            Assert.IsTrue(root.ClassListContains("lt-root"));
            Assert.AreEqual(0, root.Query<IMGUIContainer>().ToList().Count);
        }

        [Test]
        public void TheTextInspectorOffersInteraction()
        {
            LetteraText text = CanvasText("abc");
            VisualElement card = LetteraEditingMenus.InteractionCard(new Object[] { text });
            Assert.IsNotNull(card);
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(text);
            VisualElement root = editor.CreateInspectorGUI();
            Assert.IsTrue(root.Query<Label>().ToList().Any(l => l.text == "INTERACTION"));
            Object.DestroyImmediate(editor);
        }

        [Test]
        public void AWarningShowsForWorldTextWithNoRaycaster()
        {
            LetteraWorldText label = Make("abc");
            var selectable = label.gameObject.AddComponent<LetteraSelectable>();
            var made = new GameObject("Main", typeof(Camera)) { tag = "MainCamera" };
            m_Objects.Add(made);
            GameObject camera = Camera.main.gameObject;
            Assert.IsTrue(LetteraSelectableEditor.Warnings(selectable).Any(w => w.Contains("Raycaster")));
            var raycaster = camera.AddComponent<LetteraWorldRaycaster>();
            Assert.IsFalse(LetteraSelectableEditor.Warnings(selectable).Any(w => w.Contains("Raycaster")));
            Object.DestroyImmediate(raycaster);
        }

        [Test]
        public void TheRaycasterHitsWorldTextWithinItsRect()
        {
            LetteraWorldText label = Make("abc", 100, 50);
            var cameraObject = new GameObject("Cam", typeof(Camera));
            m_Objects.Add(cameraObject);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Screen.height * 0.5f;
            camera.transform.position = new Vector3(0, 0, -100);
            var raycaster = cameraObject.AddComponent<LetteraWorldRaycaster>();
            var results = new List<RaycastResult>();
            raycaster.Raycast(new PointerEventData(null) { position = camera.WorldToScreenPoint(Vector3.zero) }, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreSame(label.gameObject, results[0].gameObject);
            results.Clear();
            raycaster.Raycast(new PointerEventData(null) { position = camera.WorldToScreenPoint(new Vector3(90, 0)) }, results);
            Assert.AreEqual(0, results.Count);
            label.RaycastTarget = false;
            raycaster.Raycast(new PointerEventData(null) { position = camera.WorldToScreenPoint(Vector3.zero) }, results);
            Assert.AreEqual(0, results.Count);
        }
    }
}
