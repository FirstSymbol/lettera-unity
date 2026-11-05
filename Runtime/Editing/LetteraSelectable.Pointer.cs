using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera
{
    public enum LetteraPointerKind { Mouse, Touch, Pen }

    // Gestures. Mouse and pen select where they press; a finger swipes unless it held still first.
    public partial class LetteraSelectable : IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        enum DragUnit { Cluster, Word, Paragraph }

        DragUnit m_Unit;
        int m_UnitStart, m_UnitEnd;
        int m_ClickCount;
        double m_LastClickTime = -1000, m_PressTime;
        Vector2 m_LastClickPosition, m_PressPosition, m_PointerPosition;
        Camera m_PointerCamera;
        int m_PointerId = int.MinValue;
        bool m_Pressed, m_Touch, m_Moved, m_Cancelled, m_Passing, m_LongPressed, m_SelectionDrag;

        /// <summary>Replaces how a pointer is classified. Tests use this.</summary>
        internal static Func<PointerEventData, LetteraPointerKind> ClassifyOverride;
        static PropertyInfo s_PointerType;
        static Type s_PointerTypeOwner;

        // The Input System package reports what pressed through its own event data, which this assembly does not reference.
        internal static LetteraPointerKind Classify(PointerEventData data)
        {
            if (ClassifyOverride != null) return ClassifyOverride(data);
            Type type = data.GetType();
            if (type != typeof(PointerEventData))
            {
                if (s_PointerTypeOwner != type)
                {
                    s_PointerTypeOwner = type;
                    s_PointerType = type.GetProperty("pointerType");
                }
                if (s_PointerType != null)
                {
                    string kind = s_PointerType.GetValue(data)?.ToString();
                    if (kind == "Touch") return LetteraPointerKind.Touch;
                    if (kind == "Pen" || kind == "Tracked") return LetteraPointerKind.Pen;
                    return LetteraPointerKind.Mouse;
                }
            }
            return data.pointerId >= 0 ? LetteraPointerKind.Touch : LetteraPointerKind.Mouse;
        }

        static EventSystem s_NoEvents;

        bool Interactive => Host != null && Host.IsVisible && (Editable == null || Editable.Interactable) && GroupsAllowInteraction();

        /// <summary>False when the text is hidden, the field is off, or a Canvas Group above it is not interactable. A press then places nothing and a drag goes to what is above.</summary>
        public bool IsInteractable => Interactive;

        static readonly System.Collections.Generic.List<CanvasGroup> s_Groups = new System.Collections.Generic.List<CanvasGroup>();

        // The same walk uGUI's own controls make: up to a group that ignores its parents.
        bool GroupsAllowInteraction()
        {
            bool allowed = true;
            for (Transform t = transform; t != null && allowed; t = t.parent)
            {
                t.GetComponents(s_Groups);
                bool stop = false;
                for (int i = 0; i < s_Groups.Count; i++)
                {
                    CanvasGroup group = s_Groups[i];
                    if (!group.enabled) continue;
                    allowed &= group.interactable;
                    stop |= group.ignoreParentGroups;
                }
                if (stop) break;
            }
            s_Groups.Clear();
            return allowed;
        }

        // A group above went off under a session: the text gives the keyboard up, as uGUI's own fields do.
        void OnCanvasGroupChanged()
        {
            if (m_HasFocus && !GroupsAllowInteraction()) EndSession(LetteraBlurReason.Code);
        }

        void OnTransformParentChanged() => OnCanvasGroupChanged();

        public void OnPointerDown(PointerEventData data)
        {
            if (!Interactive)
            {
                // Nothing is placed, and a drag from here belongs to whatever scrolls.
                m_PointerId = data.pointerId;
                m_Pressed = false;
                m_Passing = true;
                return;
            }
            if (m_Pressed && data.pointerId != m_PointerId)
            {
                // A second finger cancels the first, without a click.
                m_Cancelled = true;
                if (m_Claimed)
                {
                    m_Claimed = false;
                    RangeRouter?.CancelPress();
                }
                HideLoupe();
                return;
            }
            LetteraPointerKind kind = Classify(data);
            m_PointerCamera = PointerCamera != null ? PointerCamera : data.pressEventCamera != null ? data.pressEventCamera : data.enterEventCamera;
            m_PointerId = data.pointerId;
            m_PressPosition = m_PointerPosition = data.position;
            m_PressTime = LetteraEditClock.Now;
            m_Touch = kind == LetteraPointerKind.Touch;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (m_Touch && Editable != null && !Editable.ReadOnly) LetteraWebInputSource.LetteraInput_Arm();
#endif
            m_Moved = m_Cancelled = m_Passing = m_LongPressed = m_SelectionDrag = false;
            Editable?.CommitComposition();

            bool secondary = data.button == PointerEventData.InputButton.Right || (data.button == PointerEventData.InputButton.Left && LetteraKeyTable.Family == LetteraPlatformFamily.Apple && IsControlHeld());
            if (data.button == PointerEventData.InputButton.Middle) return;
            if (TryRangePress(data, secondary)) return;
            if (secondary)
            {
                Activate();
                SelectForSecondary(data.position);
                OpenMenu(new LetteraMenuPlacement { Point = data.position, Trigger = LetteraMenuTrigger.Pointer, Camera = m_PointerCamera });
                return;
            }

            m_Pressed = true;
            if (m_Touch)
            {
                CountClick(data.position, LetteraSettings.Instance.Editing.MultiTapSeconds, LetteraEditingSettings.Dp(LetteraSettings.Instance.Editing.MultiTapSlop));
                if (m_ClickCount >= 2)
                {
                    Activate();
                    ApplyClick(data.position, false);
                    m_SelectionDrag = true;
                }
                return;
            }

            Activate();
            CountClick(data.position, LetteraSettings.Instance.Editing.MultiClickSeconds, LetteraEditingSettings.Dp(LetteraSettings.Instance.Editing.MultiClickSlop));
            HideMenu();
            ApplyClick(data.position, IsShiftHeld(data));
            m_SelectionDrag = true;
            IsSelecting = true;
            DragStarted?.Invoke(m_Selection.Head);
        }

        bool m_Claimed;

        /// <summary>True while a range holds this press: selection stays as it is, and no selection event goes out.</summary>
        public bool Claimed => m_Claimed;

        LetteraRangeRouter RangeRouter => !Authoring && Engine.ActiveRanges.Count > 0 ? LetteraRangeRouter.Of(Host) : null;

        /// <summary>Control, or Command on Apple systems: what makes a range in a field answer.</summary>
        internal static bool IsLinkModifierHeld => s_LinkModifierOverride ?? (LetteraKeyTable.Family == LetteraPlatformFamily.Apple ? LegacyKey(KeyCode.LeftCommand) || LegacyKey(KeyCode.RightCommand) : IsControlHeld());

        internal static bool? s_LinkModifierOverride;

        // Only one object handles a press: the router first on what only reads, and on a field only with the modifier.
        bool TryRangePress(PointerEventData data, bool secondary)
        {
            LetteraRangeRouter router = RangeRouter;
            if (router == null) return false;
            bool editing = Editable != null && !Editable.ReadOnly;
            if (editing && (secondary || !IsLinkModifierHeld)) return false;
            if (secondary) return router.Press(data);

            // A second press on the same spot is a double click, which selects the word.
            LetteraEditingSettings settings = LetteraSettings.Instance.Editing;
            float seconds = m_Touch ? settings.MultiTapSeconds : settings.MultiClickSeconds;
            float slop = LetteraEditingSettings.Dp(m_Touch ? settings.MultiTapSlop : settings.MultiClickSlop);
            if (LetteraEditClock.Now - m_LastClickTime <= seconds && (data.position - m_LastClickPosition).magnitude <= slop) return false;

            if (!router.Press(data, true)) return false;
            CountClick(data.position, seconds, slop);
            m_Claimed = true;
            m_Pressed = true;
            return true;
        }

        void CountClick(Vector2 position, float seconds, float slop)
        {
            double now = LetteraEditClock.Now;
            bool close = now - m_LastClickTime <= seconds && (position - m_LastClickPosition).magnitude <= slop;
            m_ClickCount = close ? m_ClickCount % 3 + 1 : 1;
            m_LastClickTime = now;
            m_LastClickPosition = position;
        }

        // Shift and Control come from the module when it knows them; a test event reports none.
        static bool IsShiftHeld(PointerEventData data)
        {
            Event current = Event.current;
            if (s_ShiftOverride.HasValue) return s_ShiftOverride.Value;
            return current != null && current.shift || LegacyKey(KeyCode.LeftShift) || LegacyKey(KeyCode.RightShift);
        }

        internal static bool? s_ShiftOverride;

        static bool IsControlHeld() => LegacyKey(KeyCode.LeftControl) || LegacyKey(KeyCode.RightControl);

        static bool s_LegacyInputBroken;

        static bool LegacyKey(KeyCode key)
        {
            if (s_LegacyInputBroken) return LetteraInputSystemBridge.IsKeyHeld(key);
            try
            {
                return UnityEngine.Input.GetKey(key);
            }
            catch (InvalidOperationException)
            {
                s_LegacyInputBroken = true;
                return LetteraInputSystemBridge.IsKeyHeld(key);
            }
        }

        // The cluster under the pointer, not the caret spot: a click on a word's last letter belongs to that word.
        int ClusterAt(int index, LetteraCaretBias bias) => bias == LetteraCaretBias.Backward && index > 0 ? StepBack(index) : index;

        void ApplyClick(Vector2 position, bool extend)
        {
            int hit = HitTest(position, m_PointerCamera, out LetteraCaretBias bias);
            if (extend)
            {
                m_Unit = DragUnit.Cluster;
                m_UnitStart = m_UnitEnd = m_Selection.Anchor;
                Request(new LetteraSelectionRange(m_Selection.Anchor, hit, bias), "sel.pointer.extend");
                return;
            }
            ForgetX();
            int cluster = ClusterAt(hit, bias);
            switch (m_ClickCount)
            {
                case 2:
                    m_Unit = DragUnit.Word;
                    SelectWordAt(cluster);
                    m_UnitStart = m_Selection.Start;
                    m_UnitEnd = m_Selection.End;
                    break;
                case 3:
                    m_Unit = DragUnit.Paragraph;
                    SelectParagraphAt(cluster);
                    m_UnitStart = m_Selection.Start;
                    m_UnitEnd = m_Selection.End;
                    break;
                default:
                    m_Unit = DragUnit.Cluster;
                    m_UnitStart = m_UnitEnd = hit;
                    Request(LetteraSelectionRange.AtCaret(hit, bias), "sel.pointer");
                    break;
            }
        }

        void SelectForSecondary(Vector2 position)
        {
            if (!m_SelectOnRightClick) return;
            int hit = HitTest(position, m_PointerCamera, out LetteraCaretBias bias);
            int cluster = ClusterAt(hit, bias);
            if (!m_Selection.IsCaret && cluster >= m_Selection.Start && cluster < m_Selection.End) return;
            SelectWordAt(cluster);
        }

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (!Classify(data).Equals(LetteraPointerKind.Touch)) data.useDragThreshold = false;
            Transform parent = transform.parent;
            if (parent != null) ExecuteEvents.ExecuteHierarchy(parent.gameObject, data, ExecuteEvents.initializePotentialDrag);
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (m_Passing && !m_Pressed)
            {
                PassDrag(data);
                return;
            }
            if (m_Cancelled) return;
            m_Moved = true;
            if (m_Touch && !m_SelectionDrag && !m_LongPressed)
            {
                if (m_Claimed)
                {
                    m_Claimed = false;
                    RangeRouter?.CancelPress();
                }
                // A swipe that did not hold first belongs to whatever scrolls.
                m_Passing = true;
                m_Pressed = false;
                PassDrag(data);
            }
        }

        void PassDrag(PointerEventData data)
        {
            Transform parent = transform.parent;
            GameObject target = parent != null ? ExecuteEvents.GetEventHandler<IDragHandler>(parent.gameObject) : null;
            if (target == null) return;
            ExecuteEvents.Execute(target, data, ExecuteEvents.beginDragHandler);
            data.pointerDrag = target;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void LetteraInput_TrackMouse();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int LetteraInput_MouseHeld();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void TrackMouse() => LetteraInput_TrackMouse();

        // The press that opened a session moved the page's focus to the text box, and Unity then never sees that button's release: its event system would go on dragging for as long as the pointer moves.
        void ReleaseIfForgotten()
        {
            if (m_Pressed && !m_Touch && LetteraInput_MouseHeld() == 0) OnPointerUp(new PointerEventData(EventSystem.current) { pointerId = m_PointerId, position = m_PointerPosition });
        }
#else
        void ReleaseIfForgotten() { }
#endif

        public void OnDrag(PointerEventData data)
        {
            ReleaseIfForgotten();
            if (!m_Pressed || m_Cancelled || m_Passing) return;
            if (m_Claimed)
            {
                m_PointerPosition = data.position;
                LetteraRangeRouter router = RangeRouter;
                if (router != null && !router.DragTo(data)) return;
                // Past the slop the press stops being one, and a mouse goes on to select from where it began.
                m_Claimed = false;
                m_ClickCount = 1;
                Activate();
                ApplyClick(m_PressPosition, false);
                m_SelectionDrag = true;
                IsSelecting = true;
                DragStarted?.Invoke(m_Selection.Head);
            }
            if ((data.position - m_PressPosition).magnitude > 0) m_Moved = true;
            m_PointerPosition = data.position;
            ExtendDragTo(data.position);
            if (m_Touch) MoveLoupe(data.position);
        }

        void ExtendDragTo(Vector2 position)
        {
            int hit = HitTest(position, m_PointerCamera, out LetteraCaretBias bias);
            int cluster = ClusterAt(hit, bias);
            LetteraSelectionRange wanted;
            switch (m_Unit)
            {
                case DragUnit.Word:
                case DragUnit.Paragraph:
                    int a, b;
                    if (m_Unit == DragUnit.Word) Unicode.LetteraTextNavigator.WordAt(Shown, cluster, out a, out b);
                    else Unicode.LetteraTextNavigator.ParagraphAt(Shown, cluster, out a, out _, out b);
                    wanted = a < m_UnitStart ? new LetteraSelectionRange(m_UnitEnd, a) : new LetteraSelectionRange(m_UnitStart, Mathf.Max(b, m_UnitEnd));
                    break;
                default:
                    if (m_LongPressed && Editable != null && m_Selection.IsCaret) wanted = LetteraSelectionRange.AtCaret(hit, bias);
                    else wanted = new LetteraSelectionRange(m_Selection.Anchor, hit, bias);
                    break;
            }
            if (Request(wanted, m_Unit == DragUnit.Cluster && !wanted.IsCaret ? "sel.pointer.extend" : "sel.pointer")) DragUpdated?.Invoke(m_Selection.Head);
            AutoScrollDrag(position);
        }

        void AutoScrollDrag(Vector2 position)
        {
            if (!m_DragAutoScroll || Viewport == null) return;
            Editable?.ScrollCaretIntoView();
        }

        public void OnEndDrag(PointerEventData data) { }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.pointerId != m_PointerId) return;
            bool wasPressed = m_Pressed;
            m_Pressed = false;
            if (m_Claimed)
            {
                m_Claimed = false;
                RangeRouter?.Release(data);
                return;
            }
            if (IsSelecting)
            {
                IsSelecting = false;
                DragEnded?.Invoke(m_Selection.Head);
            }
            if (m_SelectAllScheduled)
            {
                m_SelectAllScheduled = false;
                if (!m_Moved) SelectAll();
            }
            if (!wasPressed || m_Cancelled) return;
            HideLoupe();
            if (!m_Touch) return;
            if (m_LongPressed || m_SelectionDrag)
            {
                ShowTouchAffordances(true);
                return;
            }
            if (m_Moved) return;
            HandleTap(data.position);
        }

        void HandleTap(Vector2 position)
        {
            if (Editable == null)
            {
                HideMenu();
                HideHandles();
                CollapseSelection();
                return;
            }
            bool hadFocus = m_HasFocus;
            int before = m_Selection.Head;
            Activate();
            int hit = HitTest(position, m_PointerCamera, out LetteraCaretBias bias);
            if (hadFocus && m_Selection.IsCaret && hit == before)
            {
                ToggleMenuAtCaret();
                return;
            }
            // A finger cannot aim between letters: on a touch system it lands on the edge of the word.
            if (Application.isMobilePlatform || LetteraKeyTable.FamilyOverride.HasValue)
            {
                Unicode.LetteraTextNavigator.WordAt(Shown, ClusterAt(hit, bias), out int a, out int b);
                if (hit > a && hit < b) hit = hit - a < b - hit ? a : b;
            }
            ForgetX();
            Request(LetteraSelectionRange.AtCaret(hit, bias), "sel.pointer");
            HideMenu();
            ShowTouchAffordances(false);
        }

        void TickPointer()
        {
            if (!m_Pressed || !m_Touch || m_Cancelled || m_LongPressed || m_SelectionDrag || m_Moved) return;
            float hold = LetteraSettings.Instance.Editing.LongPressSeconds;
            if (LetteraEditClock.Now - m_PressTime < hold)
            {
                if (m_Claimed) RangeRouter?.HoldProgress(hold > 0 ? (float)((LetteraEditClock.Now - m_PressTime) / hold) : 1);
                return;
            }
            m_LongPressed = true;
            if (m_Claimed)
            {
                LetteraRangeRouter router = RangeRouter;
                // Someone took the menu to themselves: selection stays out of it, and the release activates nothing.
                if (router != null && router.HoldEnded()) return;
                m_Claimed = false;
                router?.CancelPress();
            }
            Activate();
            int hit = HitTest(m_PointerPosition, m_PointerCamera, out LetteraCaretBias bias);
            bool insideSelection = !m_Selection.IsCaret && hit >= m_Selection.Start && hit <= m_Selection.End;
            // On Apple systems a long press in a field places the caret, and the finger then moves it with the loupe. Elsewhere, as on Android,
            // it selects the word under the finger, and only a press on no word places the caret.
            if (Editable != null && !insideSelection && (LetteraKeyTable.Family == LetteraPlatformFamily.Apple || !IsWordAt(ClusterAt(hit, bias))))
            {
                m_Unit = DragUnit.Cluster;
                Request(LetteraSelectionRange.AtCaret(hit, bias), "sel.pointer");
            }
            else if (!insideSelection)
            {
                SelectWordAt(ClusterAt(hit, bias));
                m_Unit = DragUnit.Word;
                m_UnitStart = m_Selection.Start;
                m_UnitEnd = m_Selection.End;
            }
            MoveLoupe(m_PointerPosition);
        }

        // Something stands at this cluster, so a long press there means it: a word, a symbol, an emoji or a formula. Only a space, a line's end or
        // nothing places the caret, as on Android.
        bool IsWordAt(int cluster)
        {
            ReadOnlySpan<int> shown = Shown;
            if (cluster < 0 || cluster >= shown.Length) return false;
            int cp = shown[cluster];
            if (cp > 0x10FFFF) return true;
            return cp < 0x10000 ? !char.IsWhiteSpace((char)cp) && !char.IsControl((char)cp) : true;
        }

        void OnDisablePointer()
        {
            if (m_Claimed)
            {
                m_Claimed = false;
                RangeRouter?.CancelPress();
            }
            m_Pressed = false;
            IsSelecting = false;
            HideHandles();
            HideLoupe();
        }
    }

    /// <summary>Keyboard state without a compile-time link to the Input System package.</summary>
    internal static class LetteraInputSystemBridge
    {
        static PropertyInfo s_Current;
        static MethodInfo s_Index;
        static bool s_Looked;
        static PropertyInfo s_IsPressed;

        public static bool IsKeyHeld(KeyCode key)
        {
            if (!s_Looked)
            {
                s_Looked = true;
                Type keyboard = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                s_Current = keyboard?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                s_Index = keyboard?.GetMethod("get_Item", new[] { Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem") });
                s_IsPressed = Type.GetType("UnityEngine.InputSystem.Controls.ButtonControl, Unity.InputSystem")?.GetProperty("isPressed");
            }
            if (s_Current == null || s_Index == null || s_IsPressed == null) return false;
            object current = s_Current.GetValue(null);
            if (current == null) return false;
            string name;
            switch (key)
            {
                case KeyCode.LeftShift: name = "LeftShift"; break;
                case KeyCode.RightShift: name = "RightShift"; break;
                case KeyCode.LeftControl: name = "LeftCtrl"; break;
                case KeyCode.RightControl: name = "RightCtrl"; break;
                default: return false;
            }
            object control = s_Index.Invoke(current, new[] { Enum.Parse(s_Index.GetParameters()[0].ParameterType, name) });
            return control != null && (bool)s_IsPressed.GetValue(control);
        }
    }
}
