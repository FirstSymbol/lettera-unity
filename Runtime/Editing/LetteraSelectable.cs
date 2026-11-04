using System;
using System.Collections.Generic;
using FirstSymbol.Lettera.Unicode;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FirstSymbol.Lettera
{
    /// <summary>Lets a user select a text and copy from it. Works on Canvas and on world text.</summary>
    [AddComponentMenu("Lettera/Selectable")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public partial class LetteraSelectable : MonoBehaviour, ISelectHandler, IDeselectHandler, IUpdateSelectedHandler, ILetteraInputSink, ILetteraInputHost, ILetteraKeyboardSink, ILetteraMirrorField
    {
        [SerializeField] Color m_HighlightColor;
        [SerializeField] bool m_UseProjectHighlightColor = true;
        [SerializeField] LetteraHighlightOrder m_HighlightOrder;
        [SerializeField] LetteraInactiveHighlight m_InactiveHighlight;
        [SerializeField] bool m_DragAutoScroll = true;
        [SerializeField] bool m_SelectOnRightClick = true;
        [SerializeField] RectTransform m_Viewport;

        ITextComponent m_Host;
        LetteraSelectionRange m_Selection;
        bool m_HasFocus, m_BlurPending, m_VisualsDirty;
        float m_RememberedX = float.NaN;
        internal LetteraEditable Editable;

        readonly ValueList<(LetteraSelectionFilter handler, int order)> m_Filters = new ValueList<(LetteraSelectionFilter, int)>();
        readonly List<Rect> m_Rects = new List<Rect>();
        ISelectionVisual m_Visual;
        ILetteraInputSource m_Source;

        /// <summary>The text that has the keyboard, per EventSystem at most one.</summary>
        public static LetteraSelectable Current { get; private set; }

        /// <summary>Raised after any change of the selection.</summary>
        public event Action<LetteraSelectionChange> SelectionChanged;

        /// <summary>A drag selection began, moved or ended. The value is the moving end.</summary>
        public event Action<int> DragStarted, DragUpdated, DragEnded;

        /// <summary>Raised when the text takes or loses the keyboard.</summary>
        public event Action FocusGained;
        public event Action<LetteraBlurReason> FocusLost;

        public LetteraSelectionRange Selection => m_Selection;
        public bool HasFocus => m_HasFocus;
        public bool IsSelecting { get; private set; }

        public Color HighlightColor
        {
            get => m_UseProjectHighlightColor ? LetteraSettings.Instance.Editing.DefaultHighlightColor : m_HighlightColor;
            set
            {
                m_UseProjectHighlightColor = false;
                m_HighlightColor = value;
                MarkVisualsDirty();
            }
        }

        public LetteraHighlightOrder HighlightOrder
        {
            get => m_HighlightOrder;
            set
            {
                m_HighlightOrder = value;
                DestroyVisual();
                MarkVisualsDirty();
            }
        }

        public LetteraInactiveHighlight InactiveHighlight { get => m_InactiveHighlight; set { m_InactiveHighlight = value; MarkVisualsDirty(); } }
        public bool DragAutoScroll { get => m_DragAutoScroll; set => m_DragAutoScroll = value; }
        public bool SelectOnRightClick { get => m_SelectOnRightClick; set => m_SelectOnRightClick = value; }

        /// <summary>The mask the text scrolls inside. Empty uses the direct parent if it masks.</summary>
        public RectTransform Viewport
        {
            get
            {
                if (m_Viewport != null) return m_Viewport;
                Transform parent = transform.parent;
                return parent != null && parent.GetComponent<UnityEngine.UI.RectMask2D>() != null ? (RectTransform)parent : null;
            }
            set => m_Viewport = value;
        }

        internal ITextComponent Host
        {
            get
            {
                if (m_Host != null) return m_Host;
                m_Host = GetComponent<LetteraText>();
                if (m_Host == null) m_Host = GetComponent<LetteraWorldText>();
                return m_Host;
            }
        }

        internal TextEngine Engine => Host.SharedEngine;

        // An editing session in the Scene view works on a text from an object of its own, so that nothing is ever added to what is edited.
        internal void BindHost(ITextComponent host) => m_Host = host;

        /// <summary>The text went away under a session: nothing may reach for it again.</summary>
        internal void ForgetHost()
        {
            if (m_HasFocus)
            {
                m_HasFocus = false;
                if (Current == this) Current = null;
                m_Source?.Detach();
                m_Source = null;
            }
            m_Host = null;
        }

        /// <summary>Set for a session in the Scene view: what draws the selection and which camera looks at the text.</summary>
        internal Func<ISelectionVisual> VisualSource;
        internal Camera PointerCamera;

        /// <summary>Links do not answer while a text is being written.</summary>
        internal bool Authoring;

        /// <summary>On a field of the text under a session: its value is being written by someone else, and it hears at the end.</summary>
        internal bool SourceWritesIgnored;

        /// <summary>The Canvas or world text this works on.</summary>
        public Component TextComponent => (Component)Host;

        void Reset() => m_InactiveHighlight = GetComponent<LetteraEditable>() != null ? LetteraInactiveHighlight.Dim : LetteraInactiveHighlight.Hide;

        void OnEnable()
        {
            if (Host == null)
            {
                Debug.LogError("LetteraSelectable needs a LetteraText or a LetteraWorldText on the same object.", this);
                return;
            }
            Host.Rebuilt += OnTextRebuilt;
            Engine.WindowPlaced += OnWindowPlaced;
            Host.SourceWritten += OnSourceWritten;
            Editable = GetComponent<LetteraEditable>();
            MarkVisualsDirty();
        }

        void OnDisable()
        {
            if (m_Host == null) return;
            if (m_HasFocus) EndSession(LetteraBlurReason.Disabled);
            m_Host.Rebuilt -= OnTextRebuilt;
            m_Host.SharedEngine.WindowPlaced -= OnWindowPlaced;
            m_Host.SourceWritten -= OnSourceWritten;
            DestroyVisual();
            OnDisablePointer();
        }

        void OnDestroy() => DestroyVisual();

        bool m_MappingWindow;

        void OnTextRebuilt(LetteraChanges changes)
        {
            if ((changes & (LetteraChanges.Text | LetteraChanges.Layout | LetteraChanges.Positions)) != 0) m_VisualsDirty = true;
        }

        // The window moved under the selection: it is read again from the document, held to what shows.
        void OnWindowPlaced()
        {
            m_VisualsDirty = true;
            if (!Engine.IsWindowed || !Engine.View.HasDocumentSelection) return;
            (int anchor, int head) = Engine.View.DocumentSelection;
            m_MappingWindow = true;
            SetSelectionUnchecked(new LetteraSelectionRange(Engine.ShownOfDocument(anchor), Engine.ShownOfDocument(head)), "sel.window");
            m_MappingWindow = false;
        }

        /// <summary>Selects by places in the document, wherever the window stands.</summary>
        public void SetDocumentSelection(int anchorChar, int headChar)
        {
            if (!Engine.IsWindowed) return;
            Engine.View.SetDocumentSelection(anchorChar, headChar);
            m_MappingWindow = true;
            SetSelectionUnchecked(new LetteraSelectionRange(Engine.ShownOfDocument(anchorChar), Engine.ShownOfDocument(headChar)), "sel.document");
            m_MappingWindow = false;
        }

        /// <summary>The selection in chars of the document's source, or the shown indices on a plain text.</summary>
        public (int Anchor, int Head) DocumentSelection => Engine.IsWindowed ? Engine.View.DocumentSelection : (m_Selection.Anchor, m_Selection.Head);

        void OnSourceWritten()
        {
            if (SourceWritesIgnored) return;
            if (Editable != null) Editable.OnSourceWrittenFromOutside();
            else if (m_Selection.End > 0) ClampAfterTextChange();
        }

        void ClampAfterTextChange()
        {
            Engine.EnsureParsed();
            SetSelectionUnchecked(m_Selection.Clamp(Engine.ShownLength), "sel.clamp");
        }

        void LateUpdate() => Tick();

        /// <summary>The per-frame part: pending focus changes, keeping the drawing in step.</summary>
        internal void Tick()
        {
            if (Authoring && m_Host is UnityEngine.Object gone && gone == null) return;
            if (m_BlurPending) ResolveBlur();
            TickPointer();
            if (Editable != null) Editable.Tick();
            if (m_VisualsDirty) RefreshVisuals();
            m_Visual?.Follow();
        }

        // ------------------------------------------------------------- selection

        /// <summary>Lower orders run first.</summary>
        public void AddSelectionFilter(LetteraSelectionFilter handler, int order = 0)
        {
            int at = m_Filters.Count;
            while (at > 0 && m_Filters[at - 1].order > order) at--;
            m_Filters.Insert(at, (handler, order));
        }

        public void RemoveSelectionFilter(LetteraSelectionFilter handler)
        {
            for (int i = m_Filters.Count - 1; i >= 0; i--)
                if (m_Filters[i].handler == handler)
                    m_Filters.RemoveAt(i);
        }

        internal ReadOnlySpan<int> Shown
        {
            get
            {
                Engine.EnsureParsed();
                return Engine.ShownCodepoints;
            }
        }

        /// <summary>An index on a stop: not inside a grapheme cluster or a matcher's replacement.</summary>
        int Snap(int index, bool forward)
        {
            ReadOnlySpan<int> text = Shown;
            index = Mathf.Clamp(index, 0, text.Length);
            if (Engine.AtomicAround(index, out int start, out int end)) return forward ? end : start;
            if (LetteraTextNavigator.IsStop(text, index)) return index;
            return forward ? LetteraTextNavigator.NextStop(text, index) : LetteraTextNavigator.PreviousStop(text, index);
        }

        int StepForward(int index)
        {
            int next = LetteraTextNavigator.NextStop(Shown, index);
            return Engine.AtomicAround(next, out _, out int end) ? end : next;
        }

        int StepBack(int index)
        {
            int previous = LetteraTextNavigator.PreviousStop(Shown, index);
            return Engine.AtomicAround(previous, out int start, out _) ? start : previous;
        }

        // A change someone asked for goes past the filters. Edits do not: the caret has to follow the text.
        bool Request(LetteraSelectionRange wanted, string reason)
        {
            if (Engine.Dirty >= DirtyLevel.Parse) Engine.EnsureParsed();
            wanted = wanted.Clamp(Engine.ShownLength);
            var proposal = new LetteraSelectionProposal { Current = m_Selection, Wanted = wanted, Reason = reason };
            foreach ((LetteraSelectionFilter handler, int _) in m_Filters.ToArray())
            {
                handler(ref proposal);
                if (proposal.Veto) return false;
            }
            return SetSelectionUnchecked(proposal.Wanted.Clamp(Engine.ShownLength), reason);
        }

        internal bool SetSelectionUnchecked(LetteraSelectionRange value, string reason)
        {
            if (value == m_Selection) return false;
            LetteraSelectionRange previous = m_Selection;
            m_Selection = value;
            if (Engine.IsWindowed && !m_MappingWindow) Engine.View.SetDocumentSelection(Engine.DocCharOf(value.Anchor), Engine.DocCharOf(value.Head));
            MarkVisualsDirty();
            Editable?.OnSelectionMoved(previous, value, reason);
            SelectionChanged?.Invoke(new LetteraSelectionChange(previous, value, reason));
            return true;
        }

        internal void MarkVisualsDirty() => m_VisualsDirty = true;

        public bool PlaceCaret(int index, LetteraCaretBias bias = LetteraCaretBias.Forward)
        {
            ForgetX();
            index = Snap(index, bias == LetteraCaretBias.Backward);
            return Request(LetteraSelectionRange.AtCaret(index, bias), "sel.set");
        }

        public bool Select(int anchor, int head)
        {
            ForgetX();
            bool forward = head >= anchor;
            return Request(new LetteraSelectionRange(Snap(anchor, !forward), Snap(head, forward)), "sel.set");
        }

        public bool ExtendTo(int head)
        {
            ForgetX();
            return Request(new LetteraSelectionRange(m_Selection.Anchor, Snap(head, head >= m_Selection.Anchor)), "sel.extend");
        }

        /// <summary>Moves one end without ever collapsing: onto the fixed end it stays a cluster away, and past it the two swap roles.</summary>
        public bool DragEndpoint(bool anchorEnd, int index, string reason = "sel.handle")
        {
            ForgetX();
            int moving = anchorEnd ? m_Selection.Anchor : m_Selection.Head;
            int fixedEnd = anchorEnd ? m_Selection.Head : m_Selection.Anchor;
            index = Snap(index, index > moving);
            if (index == fixedEnd) index = moving < fixedEnd ? StepBack(fixedEnd) : StepForward(fixedEnd);
            if (index == fixedEnd) return false;
            return Request(new LetteraSelectionRange(fixedEnd, index), reason);
        }

        public bool SelectWordAt(int index)
        {
            ForgetX();
            LetteraTextNavigator.WordAt(Shown, index, out int start, out int end);
            if (Engine.MaskGlyph != 0)
            {
                start = 0;
                end = Engine.ShownLength;
            }
            return Request(new LetteraSelectionRange(start, end), "sel.word");
        }

        public bool SelectLineAt(int index)
        {
            ForgetX();
            Engine.EnsurePositioned();
            if (Engine.Lines.Count == 0) return false;
            LetteraLine line = Engine.Lines[Engine.LineOf(index, m_Selection.Bias)];
            return Request(new LetteraSelectionRange(line.Start, line.End, LetteraCaretBias.Backward), "sel.line");
        }

        public bool SelectParagraphAt(int index)
        {
            ForgetX();
            LetteraTextNavigator.ParagraphAt(Shown, index, out int start, out _, out int end);
            return Request(new LetteraSelectionRange(start, end), "sel.paragraph");
        }

        public bool SelectAll()
        {
            ForgetX();
            return Request(new LetteraSelectionRange(0, Shown.Length), "sel.all");
        }

        public bool CollapseSelection()
        {
            ForgetX();
            return Request(LetteraSelectionRange.AtCaret(m_Selection.Head, m_Selection.Bias), "sel.clear");
        }

        /// <summary>The selected characters, in logical order, without markup. Text cut off by eliding counts.</summary>
        internal bool ConcealsSelection => !m_Selection.IsCaret && (Editable == null || Editable.ReadOnly) && Engine.SpanConceals(m_Selection.Start, m_Selection.End);

        public string SelectedPlainText()
        {
            if (Engine.IsWindowed && Engine.View.HasDocumentSelection)
            {
                (int anchor, int head) = Engine.View.DocumentSelection;
                return Engine.DocumentPlainBetween(Mathf.Min(anchor, head), Mathf.Max(anchor, head));
            }
            if (m_Selection.IsCaret) return "";
            string plain = PlainOf(Shown, m_Selection.Start, m_Selection.End);
            if (Engine.HasInline) plain = WithInlineAlt(m_Selection.Start, m_Selection.End);
            if (Editable != null && !Editable.ReadOnly || !Engine.SpanConceals(m_Selection.Start, m_Selection.End)) return plain;
            var masked = new System.Text.StringBuilder(plain);
            Engine.MaskConcealed(masked, m_Selection.Start, m_Selection.End);
            return masked.ToString();
        }

        // An inline item copies as what stands for it in words, or as nothing.
        string WithInlineAlt(int start, int end)
        {
            ReadOnlySpan<int> text = Shown;
            var builder = new System.Text.StringBuilder(end - start);
            for (int i = start; i < end && i < text.Length; i++)
            {
                if (text[i] == 0xFFFC) builder.Append(Engine.InlineAltAt(i));
                else builder.Append(text[i] >= 0xD800 && text[i] < 0xE000 ? "\uFFFD" : char.ConvertFromUtf32(text[i]));
            }
            return builder.ToString();
        }

        internal static string PlainOf(ReadOnlySpan<int> text, int start, int end)
        {
            var builder = new System.Text.StringBuilder(end - start);
            for (int i = start; i < end && i < text.Length; i++)
            {
                int cp = text[i];
                builder.Append(cp >= 0xD800 && cp < 0xE000 ? "�" : char.ConvertFromUtf32(cp));
            }
            return builder.ToString();
        }

        // ------------------------------------------------------------- geometry

        /// <summary>The caret index and side at a screen point.</summary>
        public int HitTest(Vector2 screenPoint, Camera camera, out LetteraCaretBias bias)
        {
            bias = LetteraCaretBias.Forward;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Host.TextRect, screenPoint, camera, out Vector2 local)) return 0;
            Engine.EnsurePositioned();
            return Engine.HitTestCaret(local, out bias);
        }

        /// <summary>Whether an I-beam belongs at a screen point.</summary>
        public bool WantsIBeam(Vector2 screenPoint, Camera camera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Host.TextRect, screenPoint, camera, out Vector2 local)) return false;
            if (Editable != null) return Host.TextRect.rect.Contains(local);
            Engine.EnsurePositioned();
            return Engine.IsOverLine(local);
        }

        /// <summary>The selection's bounds, in screen pixels from the bottom left. False for a caret, or one scrolled away. For a presenter of one's own.</summary>
        public bool SelectionOnScreen(out Rect screen) => TrySelectionOnScreen(EventCameraGuess(), out screen);

        /// <summary>The caret's bottom, in screen pixels from the bottom left.</summary>
        public bool CaretOnScreen(out Vector2 point)
        {
            point = default;
            if (!TryCaretOnScreen(EventCameraGuess(), out Rect caret)) return false;
            point = new Vector2(caret.center.x, caret.yMin);
            return true;
        }

        /// <summary>Where a selection's handles stand, in screen pixels from the bottom left. False for a caret.</summary>
        public bool HandlesOnScreen(out Vector2 start, out Vector2 end)
        {
            start = end = default;
            if (m_Selection.IsCaret) return false;
            Engine.EnsurePositioned();
            Camera camera = EventCameraGuess();
            if (!Engine.TryGetCaretRect(m_Selection.Start, LetteraCaretBias.Forward, out Rect a) || !Engine.TryGetCaretRect(m_Selection.End, LetteraCaretBias.Backward, out Rect b)) return false;
            Rect first = ToScreen(a, camera), last = ToScreen(b, camera);
            start = new Vector2(first.center.x, first.yMin);
            end = new Vector2(last.center.x, last.yMin);
            return true;
        }

        /// <summary>The box of a line, in screen pixels from the bottom left.</summary>
        public bool LineOnScreen(int line, out Rect screen)
        {
            screen = default;
            Engine.EnsurePositioned();
            if (line < 0 || line >= Engine.Lines.Count) return false;
            return TryLineOnScreen(Engine.Lines[line].Start, EventCameraGuess(), out screen);
        }

        /// <summary>The text's frame, in screen pixels from the bottom left.</summary>
        public bool ViewportOnScreen(out Rect screen)
        {
            screen = ((ILetteraMirrorField)this).FieldScreenRect;
            return screen.width > 0;
        }

        /// <summary>Whether this text's menu shows.</summary>
        public bool IsMenuShown => m_Menu != null && m_Menu.IsVisible;

        /// <summary>The caret's box in screen pixels.</summary>
        public bool TryCaretOnScreen(Camera camera, out Rect screen)
        {
            screen = default;
            Engine.EnsurePositioned();
            if (!Engine.TryGetCaretRect(m_Selection.Head, m_Selection.Bias, out Rect local)) return false;
            screen = ToScreen(local, camera);
            return true;
        }

        /// <summary>The selection's bounds in screen pixels.</summary>
        public bool TrySelectionOnScreen(Camera camera, out Rect screen)
        {
            screen = default;
            Engine.EnsurePositioned();
            if (m_Selection.IsCaret || !Engine.TryGetRangeBounds(m_Selection.Start, m_Selection.End, out Rect local)) return false;
            screen = ToScreen(local, camera);
            return true;
        }

        /// <summary>The box of the line a caret stands on, in screen pixels.</summary>
        public bool TryLineOnScreen(int index, Camera camera, out Rect screen)
        {
            screen = default;
            Engine.EnsurePositioned();
            if (Engine.Lines.Count == 0) return false;
            LetteraLine line = Engine.Lines[Engine.LineOf(index, LetteraCaretBias.Forward)];
            screen = ToScreen(new Rect(line.X, line.Baseline - line.Descent, line.Width, line.Height), camera);
            return true;
        }

        /// <summary>What the viewport shows, in screen pixels. Without a viewport, the text's rect.</summary>
        public Rect ViewportOnScreen(Camera camera)
        {
            RectTransform rect = Viewport != null ? Viewport : Host.TextRect;
            return RectToScreen(rect, rect.rect, camera);
        }

        internal Rect ToScreen(Rect local, Camera camera) => RectToScreen(Host.TextRect, local, camera);

        internal static Rect RectToScreen(RectTransform space, Rect local, Camera camera)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3(i == 0 || i == 3 ? local.xMin : local.xMax, i < 2 ? local.yMin : local.yMax);
                Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, space.TransformPoint(corner));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // ------------------------------------------------------------- focus

        /// <summary>Takes the keyboard. A pointer press does this too.</summary>
        public void Activate()
        {
            m_BlurPending = false;
            EventSystem events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != gameObject && !events.alreadySelecting) events.SetSelectedGameObject(gameObject);
            BeginSession();
        }

        /// <summary>Gives the keyboard up.</summary>
        public void Deactivate() => EndSession(LetteraBlurReason.Code);

        void BeginSession()
        {
            if (m_HasFocus) return;
            if (Current != null && Current != this) Current.EndSession(LetteraBlurReason.Pointer);
            Current = this;
            m_HasFocus = true;
            m_BlurPending = false;
            m_Source = LetteraInput.CreateSource();
            m_Source?.Attach(this, this);
            MarkVisualsDirty();
            Editable?.OnFocusGained();
            FocusGained?.Invoke();
        }

        internal void EndSession(LetteraBlurReason reason)
        {
            if (!m_HasFocus) return;
            Editable?.OnBeforeFocusLost(reason);
            m_HasFocus = false;
            m_BlurPending = false;
            if (Current == this) Current = null;
            m_Source?.Detach();
            m_Source = null;
            EventSystem events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == gameObject && !events.alreadySelecting) events.SetSelectedGameObject(null);
            if (m_InactiveHighlight == LetteraInactiveHighlight.Hide && Editable == null && !m_Selection.IsCaret) SetSelectionUnchecked(LetteraSelectionRange.AtCaret(m_Selection.Head, m_Selection.Bias), "sel.clear");
            HideMenu();
            MarkVisualsDirty();
            Editable?.OnFocusLost(reason);
            FocusLost?.Invoke(reason);
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            if (Editable != null && !Editable.Interactable || !GroupsAllowInteraction()) return;
            BeginSession();
        }

        void IDeselectHandler.OnDeselect(BaseEventData eventData)
        {
            // A press on a toolbar deselects first and reaches its target after: the next frame tells which.
            if (m_HasFocus) m_BlurPending = true;
        }

        void ResolveBlur()
        {
            m_BlurPending = false;
            if (!m_HasFocus) return;
            if (LetteraKeepFocus.PressedRecently)
            {
                EventSystem events = EventSystem.current;
                if (events != null && !events.alreadySelecting) events.SetSelectedGameObject(gameObject);
                return;
            }
            EndSession(LetteraBlurReason.Pointer);
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && m_HasFocus) Editable?.CommitComposition();
        }

        void IUpdateSelectedHandler.OnUpdateSelected(BaseEventData eventData)
        {
            if (!m_HasFocus) return;
            ReleaseIfForgotten();
            m_Source?.Pump();
            eventData.Use();
        }

        // ------------------------------------------------------------- input

        void ILetteraInputSink.Key(LetteraKeyCode key, LetteraKeyModifiers modifiers, bool repeat) => HandleKey(key, modifiers, repeat);

        void ILetteraInputSink.Text(string text)
        {
            if (Editable != null) Editable.OnTextInput(text);
        }

        void ILetteraInputSink.Composition(string text, int caret, LetteraClause[] clauses) => Editable?.OnComposition(text, caret, clauses);
        void ILetteraInputSink.CompositionEnd(bool commit) => Editable?.OnCompositionEnd(commit);
        void ILetteraInputSink.Action(string verb) => Editable?.OnInputAction(verb);

        /// <summary>Where keys and text arrive. Input sources and tests write here.</summary>
        public ILetteraInputSink Input => this;

        /// <summary>Runs a key through the rules and the table. True when it did something.</summary>
        internal bool HandleKey(LetteraKeyCode key, LetteraKeyModifiers modifiers, bool repeat)
        {
            if (m_Menu != null && m_Menu.IsVisible && m_Menu.HandleKey(key, modifiers)) return true;
            if (RangeKey(key, modifiers)) return true;
            LetteraEditVerb verb = LetteraKeyTable.Resolve(key, modifiers, LetteraKeyTable.Family);
            if (Editable != null)
            {
                var decision = new LetteraKeyDecision { Key = key, Modifiers = modifiers, IsRepeat = repeat, Verb = verb };
                Editable.DecideKey(ref decision);
                verb = decision.Verb;
            }
            if (verb == LetteraEditVerb.None || verb == LetteraEditVerb.Pass) return false;
            if (verb == LetteraEditVerb.Swallow) return true;
            if (Editable != null && Editable.IsComposing) return true;
            if (LetteraKeyTable.FollowsParagraphDirection(key))
            {
                Engine.EnsurePositioned();
                if (Engine.ParagraphIsRtlAt(m_Selection.Head)) verb = LetteraKeyTable.Mirror(verb);
            }
            return RunVerb(verb, repeat);
        }

        // Keys that belong to ranges come first. In a field only the one that follows a link does.
        bool RangeKey(LetteraKeyCode key, LetteraKeyModifiers modifiers)
        {
            LetteraRangeRouter router = RangeRouter;
            if (router == null) return false;
            bool enter = key == LetteraKeyCode.Enter || key == LetteraKeyCode.KeypadEnter;
            if (Editable != null && !Editable.ReadOnly)
            {
                LetteraKeyModifiers follow = LetteraKeyTable.Family == LetteraPlatformFamily.Apple ? LetteraKeyModifiers.Command : LetteraKeyModifiers.Control;
                if (!enter || (modifiers & follow) == 0) return false;
                foreach (TextEngine.ActiveRange range in Engine.ActiveRanges)
                {
                    if (m_Selection.Head < range.Start || m_Selection.Head > range.End) continue;
                    router.Activate(range.Id, true);
                    return true;
                }
                return false;
            }
            if (key == LetteraKeyCode.Tab) return router.StepFocus((modifiers & LetteraKeyModifiers.Shift) == 0);
            if (router.Focused == 0) return false;
            if (enter && modifiers == LetteraKeyModifiers.None) return router.ActivateFocused();
            if (key == LetteraKeyCode.Escape)
            {
                router.ClearFocus();
                return true;
            }
            if (key == LetteraKeyCode.Menu || (key == LetteraKeyCode.F10 && (modifiers & LetteraKeyModifiers.Shift) != 0)) return router.MenuOnFocused();
            return false;
        }

        /// <summary>Runs a verb directly. What a read-only text may not do does nothing.</summary>
        public bool RunVerb(LetteraEditVerb verb, bool repeat = false)
        {
            if (LetteraKeyTable.IsMovement(verb))
            {
                // Without a caret to show, only extending means anything on a read-only text.
                if (Editable == null && !LetteraKeyTable.IsExtend(verb)) return true;
                Move(verb);
                return true;
            }
            switch (verb)
            {
                case LetteraEditVerb.SelectAll:
                    SelectAll();
                    return true;
                case LetteraEditVerb.Copy:
                    Copy();
                    return true;
                case LetteraEditVerb.OpenMenu:
                    OpenMenuFromKeyboard();
                    return true;
                case LetteraEditVerb.Cancel:
                    if (Editable == null)
                    {
                        CollapseSelection();
                        return true;
                    }
                    break;
            }
            return Editable != null && Editable.RunEditVerb(verb, repeat);
        }

        void ForgetX() => m_RememberedX = float.NaN;

        void Move(LetteraEditVerb verb)
        {
            Engine.EnsurePositioned();
            bool extend = LetteraKeyTable.IsExtend(verb);
            LetteraEditVerb move = extend ? verb - (LetteraEditVerb.ExtendCharPrev - LetteraEditVerb.MoveCharPrev) : verb;
            bool vertical = move == LetteraEditVerb.MoveUp || move == LetteraEditVerb.MoveDown || move == LetteraEditVerb.MovePageUp || move == LetteraEditVerb.MovePageDown;
            if (!vertical) ForgetX();

            ReadOnlySpan<int> text = Shown;
            int head = m_Selection.Head, target = head;
            LetteraCaretBias bias = LetteraCaretBias.Forward;
            bool hasRange = !m_Selection.IsCaret;
            switch (move)
            {
                case LetteraEditVerb.MoveCharPrev:
                    if (hasRange && !extend) target = m_Selection.Start;
                    else target = StepBack(head);
                    break;
                case LetteraEditVerb.MoveCharNext:
                    if (hasRange && !extend) target = m_Selection.End;
                    else
                    {
                        target = StepForward(head);
                        bias = AtDirectionChange(target) ? LetteraCaretBias.Backward : LetteraCaretBias.Forward;
                    }
                    break;
                case LetteraEditVerb.MoveWordPrev:
                    target = LetteraTextNavigator.PreviousWordStop(text, head);
                    break;
                case LetteraEditVerb.MoveWordNext:
                    target = LetteraTextNavigator.NextWordStop(text, head, LetteraKeyTable.Family == LetteraPlatformFamily.Apple);
                    bias = LetteraKeyTable.Family == LetteraPlatformFamily.Apple ? LetteraCaretBias.Backward : LetteraCaretBias.Forward;
                    break;
                case LetteraEditVerb.MoveLineStart:
                    target = LineStartOf(head);
                    break;
                case LetteraEditVerb.MoveLineEnd:
                    target = LineEndOf(head);
                    bias = LetteraCaretBias.Backward;
                    break;
                case LetteraEditVerb.MoveParagraphStart:
                    LetteraTextNavigator.ParagraphAt(text, head, out int paragraphStart, out _, out _);
                    target = head == paragraphStart && head > 0 ? ParagraphStartBefore(text, head) : paragraphStart;
                    break;
                case LetteraEditVerb.MoveParagraphEnd:
                    LetteraTextNavigator.ParagraphAt(text, head, out _, out int contentEnd, out int paragraphEnd);
                    if (head == contentEnd && paragraphEnd > contentEnd) LetteraTextNavigator.ParagraphAt(text, paragraphEnd, out _, out contentEnd, out _);
                    target = contentEnd;
                    break;
                case LetteraEditVerb.MoveDocStart:
                    target = 0;
                    break;
                case LetteraEditVerb.MoveDocEnd:
                    target = text.Length;
                    break;
                default:
                    target = VerticalTarget(move, hasRange && !extend, out bias);
                    break;
            }
            target = Snap(target, target > head);
            string reason = extend ? "sel.key.extend" : "sel.key";
            bool changed = extend ? Request(new LetteraSelectionRange(m_Selection.Anchor, target, bias), reason) : Request(LetteraSelectionRange.AtCaret(target, bias), reason);
            if (changed) OnKeyboardMoved();
        }

        partial void OnKeyboardMovedExtra();

        void OnKeyboardMoved()
        {
            Editable?.ScrollCaretIntoView();
            OnKeyboardMovedExtra();
        }

        bool AtDirectionChange(int index)
        {
            if (index <= 0 || index >= Engine.ShownLength) return false;
            Engine.TryGetCaretRect(index, LetteraCaretBias.Backward, out Rect a);
            Engine.TryGetCaretRect(index, LetteraCaretBias.Forward, out Rect b);
            return Engine.LineOf(index, LetteraCaretBias.Backward) == Engine.LineOf(index, LetteraCaretBias.Forward) && Mathf.Abs(a.x - b.x) > 0.01f;
        }

        int ParagraphStartBefore(ReadOnlySpan<int> text, int index)
        {
            LetteraTextNavigator.ParagraphAt(text, index - 1, out int start, out _, out _);
            return start;
        }

        int LineStartOf(int index) => Engine.Lines.Count == 0 ? 0 : Engine.Lines[Engine.LineOf(index, m_Selection.Bias)].Start;

        int LineEndOf(int index)
        {
            if (Engine.Lines.Count == 0) return 0;
            LetteraLine line = Engine.Lines[Engine.LineOf(index, m_Selection.Bias)];
            ReadOnlySpan<int> text = Shown;
            int end = line.End;
            if (end > line.Start && end <= text.Length && (LetteraTextNavigator.IsParagraphSeparator(text[end - 1]) || text[end - 1] == 0x2028)) end = LetteraTextNavigator.PreviousStop(text, end);
            return end;
        }

        int VerticalTarget(LetteraEditVerb move, bool fromRange, out LetteraCaretBias bias)
        {
            bias = LetteraCaretBias.Forward;
            int count = Engine.Lines.Count;
            int head = fromRange ? (move == LetteraEditVerb.MoveUp || move == LetteraEditVerb.MovePageUp ? m_Selection.Start : m_Selection.End) : m_Selection.Head;
            if (count == 0) return 0;
            int line = Engine.LineOf(head, m_Selection.Bias);
            if (float.IsNaN(m_RememberedX))
            {
                Engine.TryGetCaretRect(head, m_Selection.Bias, out Rect caret);
                m_RememberedX = caret.x;
            }
            int step = 1;
            if (move == LetteraEditVerb.MovePageUp || move == LetteraEditVerb.MovePageDown)
            {
                float page = Viewport != null ? Viewport.rect.height : Host.TextRect.rect.height;
                step = Mathf.Max(1, Mathf.FloorToInt(page / Mathf.Max(Engine.Lines[line].Height, 1f)) - 1);
            }
            bool up = move == LetteraEditVerb.MoveUp || move == LetteraEditVerb.MovePageUp;
            int targetLine = line + (up ? -step : step);
            if (targetLine < 0)
            {
                if (line == 0) return 0;
                targetLine = 0;
            }
            if (targetLine >= count)
            {
                if (line == count - 1) return Engine.ShownLength;
                targetLine = count - 1;
            }
            return Engine.HitTestLine(targetLine, m_RememberedX, out bias);
        }

        /// <summary>Copies the selection. False when nothing was written.</summary>
        public bool Copy()
        {
            if (m_Selection.IsCaret) return false;
            if (Editable != null && !Editable.CanCopy) return false;
            LetteraClipboard.WriteSelection(this);
            return true;
        }

        // ------------------------------------------------------------- drawing

        void RefreshVisuals()
        {
            m_VisualsDirty = false;
            bool visible = Host.IsVisible && !m_SteppedAside;
            bool showRange = visible && !m_Selection.IsCaret && (m_HasFocus || m_InactiveHighlight != LetteraInactiveHighlight.Hide);
            m_Rects.Clear();
            if (showRange)
            {
                Engine.EnsurePositioned();
                Engine.GetRangeRects(m_Selection.Start, m_Selection.End, m_Rects);
            }
            Color color = HighlightColor;
            if (!m_HasFocus && m_InactiveHighlight == LetteraInactiveHighlight.Dim) color.a *= 0.5f;
            if (m_Rects.Count == 0 && m_Visual == null && (Editable == null || !Editable.WantsVisual)) return;
            if (m_Visual == null) m_Visual = SelectionVisuals.Create(this, m_HighlightOrder);
            m_Visual.Show(m_Rects, color);
            Editable?.DrawInto(m_Visual);
            RefreshHandles();
        }

        partial void RefreshHandles();

        void DestroyVisual()
        {
            m_Visual?.Release();
            m_Visual = null;
        }

        internal ISelectionVisual Visual => m_Visual;

        // ------------------------------------------------------------- what input sources ask

        string ILetteraInputHost.CommittedPlainText => Editable != null ? Editable.PlainText : SelectedPlainText();

        Rect ILetteraInputHost.CaretScreenRect
        {
            get
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : (canvas == null ? Camera.main : null);
                return TryCaretOnScreen(camera, out Rect screen) ? screen : default;
            }
        }

        LetteraKeyboardTraits ILetteraInputHost.Traits => Editable != null ? Editable.KeyboardTraits : default;

        void ILetteraKeyboardSink.Reconcile(string text, int caret) => Editable?.Reconcile(text, caret);

        void ILetteraMirrorField.ReplaceRange(int start, int end, string text, string reason) => Editable?.ReplaceUtf16(start, end, text, reason);

        void ILetteraMirrorField.SelectRange(int start, int end)
        {
            string plain = ((ILetteraInputHost)this).CommittedPlainText ?? "";
            var range = new LetteraSelectionRange(CodepointsTo(plain, start), CodepointsTo(plain, end));
            // A control knows only a start and an end. Told what it already holds it says it back, and that must not turn a selection growing leftward around.
            LetteraSelectionRange now = Selection;
            if (range.Start == now.Start && range.End == now.End) return;
            Request(range, "sel.os");
        }

        void ILetteraMirrorField.ComposeOver(int start, int end, string text, int caret, LetteraClause[] clauses) => Editable?.ComposeOverUtf16(start, end, text, caret, clauses);

        void ILetteraMirrorField.SelectAll() => SelectAll();

        void ILetteraMirrorField.PasteItems(IReadOnlyList<LetteraClipItem> items) => Editable?.PasteItems(items);

        void ILetteraMirrorField.StepAsideForReplica(bool aside)
        {
            m_SteppedAside = aside;
            foreach (CanvasRenderer renderer in GetComponents<CanvasRenderer>()) renderer.SetAlpha(aside ? 0 : 1);
            if (Editable != null)
                foreach (LetteraFieldRule rule in Editable.ActiveRulesForReplica)
                    if (rule is HintDecorator hint && hint.Target != null)
                        hint.Target.canvasRenderer.SetAlpha(aside ? 0 : 1);
            MarkVisualsDirty();
        }

        Rect ILetteraMirrorField.FieldScreenRect
        {
            get
            {
                LetteraFieldPressArea area = GetComponentInParent<LetteraFieldPressArea>();
                var rect = (RectTransform)(area != null ? area.transform : transform);
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Canvas canvas = rect.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
        }

        bool m_SteppedAside;

        void ILetteraMirrorField.PlaceCaretAt(Vector2 screen)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            int hit = HitTest(screen, camera, out LetteraCaretBias bias);
            if (hit >= 0) Request(new LetteraSelectionRange(hit, hit, bias), "sel.os");
        }

        internal static int CodepointsTo(string text, int utf16)
        {
            int count = 0;
            utf16 = Mathf.Clamp(utf16, 0, text.Length);
            for (int i = 0; i < utf16; i++)
            {
                count++;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            }
            return count;
        }

        /// <summary>For tests: one frame of what the input source does.</summary>
        internal void PumpInputForTests() => m_Source?.Pump();

        internal void NotifyKeyboardOfChange()
        {
            if (m_Source is ILetteraKeyboardWriter writer && Editable != null) writer.TextChanged(Editable.PlainText);
        }
    }
}
