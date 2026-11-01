using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A sequencer's cues on rows: drag, resize, split, copy, marquee, and a ruler that scrubs with no change to the scene.</summary>
    internal sealed class LetteraSequencerWindow : LetteraToolWindow
    {
        const float GutterWidth = 44;

        [MenuItem("Tools/Lettera/Sequencer", false, 26)]
        static void Menu() => GetWindow<LetteraSequencerWindow>("Lettera Sequencer").Follow(Selection.activeGameObject);

        internal static LetteraSequencerWindow Open(LetteraSequencer sequencer)
        {
            var window = GetWindow<LetteraSequencerWindow>("Lettera Sequencer");
            window.SetTarget(sequencer);
            return window;
        }

        /// <summary>The clipboard, so that a test leaves the owner's alone.</summary>
        internal static Func<string> ReadClip = () => EditorGUIUtility.systemCopyBuffer;
        internal static Action<string> WriteClip = text => EditorGUIUtility.systemCopyBuffer = text;

        protected override string Title => "Sequencer";
        protected override string Subtitle => "Cues on rows. Nothing here changes the scene until you edit a cue";

        LetteraSequencer m_Sequencer;
        SerializedObject m_Serialized;
        SequencerModel m_Model;
        VisualElement m_Root, m_Empty, m_Body, m_Tracks, m_Ruler, m_Gutter, m_CueLayer, m_Head, m_RulerHead, m_MarqueeBox, m_SnapLine, m_Detail, m_Chips;
        Label m_Time, m_Status, m_Play, m_Message, m_EmptyText;
        VisualElement m_EmptyAction;
        readonly List<VisualElement> m_CueBoxes = new List<VisualElement>();
        readonly List<Label> m_TickLabels = new List<Label>();
        readonly List<Label> m_RowLabels = new List<Label>();
        bool m_Previewing, m_Built, m_Fitted;
        int m_FullEditor = -1, m_DetailShape = -1;
        float m_LastLength = -1, m_LastHead = -1;
        string m_LastProblems;

        enum Gesture { None, Press, Move, Marquee, Pan, Scrub }

        Gesture m_Gesture;
        int m_PressCue;
        SequencerZone m_PressZone;
        Vector2 m_PressAt, m_LastAt;
        int m_UndoGroup = -1;

        internal SequencerModel Model => m_Model;
        internal LetteraSequencer Target => m_Sequencer;
        internal VisualElement TracksElement => m_Tracks;
        internal VisualElement RulerElement => m_Ruler;
        internal VisualElement DetailElement => m_Detail;
        internal VisualElement RootElement => m_Root;
        internal string Message => m_Message?.text;

        void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            LeavePreview();
        }

        void LeavePreview()
        {
            if (!m_Previewing) return;
            m_Previewing = false;
            LetteraMotionPreview.End();
            if (m_Sequencer != null && !Application.isPlaying) m_Sequencer.EndPreview();
        }

        void BeginPreview()
        {
            if (m_Previewing || Application.isPlaying || m_Sequencer == null) return;
            m_Previewing = true;
            LetteraMotionPreview.Begin();
        }

        protected override void Build() { }

        protected override void CreateGUI() => EnsureBuilt();

        internal void EnsureBuilt()
        {
            if (m_Built) return;
            m_Built = true;
            m_Root = LtUI.Root();
            m_Root.style.flexGrow = 1;
            m_Root.focusable = true;
            m_Root.tabIndex = 0;
            rootVisualElement.Add(m_Root);
            VisualElement page = LtFrame.Header(m_Root, Title, Subtitle);
            page.AddToClassList("lt-seq-page");

            m_EmptyText = LtUI.Text("", "lt-subtitle");
            m_EmptyAction = LtUI.El("lt-chips");
            m_Empty = LtUI.Card("Nothing to show yet", m_EmptyText, m_EmptyAction);
            page.Add(m_Empty);

            m_Body = LtUI.El("lt-seq-body");
            page.Add(m_Body);
            m_Body.Add(BuildToolbar());
            m_Chips = LtUI.El("lt-chips");
            m_Body.Add(m_Chips);
            m_Body.Add(BuildTimeline());
            m_Message = LtUI.Text("", "lt-subtitle");
            m_Message.style.display = DisplayStyle.None;
            m_Body.Add(m_Message);
            m_Detail = new ScrollView();
            m_Detail.AddToClassList("lt-seq-detail");
            m_Body.Add(m_Detail);
            m_Status = LtUI.Text("", "lt-seq-status");
            m_Body.Add(m_Status);

            m_Root.RegisterCallback<KeyDownEvent>(OnKey);
            m_Root.schedule.Execute(Tick).Every(33);
            Follow(Selection.activeGameObject);
            if (m_Sequencer == null) ShowEmpty();
        }

        VisualElement BuildToolbar()
        {
            VisualElement bar = LtUI.El("lt-toolbar");
            m_Play = LtUI.Clickable("▶", "lt-tool", "Play or pause (Space)", TogglePlay);
            bar.Add(m_Play);
            bar.Add(LtUI.Clickable("■", "lt-tool", "Back to the start", () =>
            {
                if (m_Sequencer == null) return;
                BeginPreview();
                m_Sequencer.Stop();
            }));
            bar.Add(LtUI.Clickable("✕", "lt-tool", "End the preview: every value goes back", LeavePreviewAndRefresh));
            m_Time = LtUI.Text("0.00 / 0.00 s", "lt-seq-time");
            bar.Add(m_Time);
            bar.Add(LtUI.El("lt-tool-gap"));
            bar.Add(LtUI.Clickable("+ Cue", "lt-tool", "A new cue at the playhead", AddCue));
            bar.Add(LtUI.Clickable("Split", "lt-tool", "Split at the playhead (S)", SplitCommand));
            bar.Add(LtUI.Clickable("Dup", "lt-tool", "Duplicate (Ctrl+D)", DuplicateCommand));
            bar.Add(LtUI.Clickable("Copy", "lt-tool", "Copy (Ctrl+C)", CopyCommand));
            bar.Add(LtUI.Clickable("Paste", "lt-tool", "Paste at the playhead (Ctrl+V)", PasteCommand));
            bar.Add(LtUI.Clickable("Mute", "lt-tool", "Mute or unmute (M)", MuteCommand));
            bar.Add(LtUI.Clickable("Del", "lt-tool", "Delete (Delete)", DeleteCommand));
            bar.Add(LtUI.El("lt-tool-gap"));
            Label snap = null;
            snap = LtUI.Clickable("Snap", "lt-tool lt-tool--on", "Snap to cue ends, the playhead and ticks. Ctrl lets a drag go free", () =>
            {
                m_Model.SnapOn = !m_Model.SnapOn;
                snap.EnableInClassList("lt-tool--on", m_Model.SnapOn);
            });
            bar.Add(snap);
            bar.Add(LtUI.Clickable("Fit", "lt-tool", "Show everything (F)", FitCommand));
            bar.Add(PinToggle());
            return bar;
        }

        VisualElement BuildTimeline()
        {
            VisualElement timeline = LtUI.El("lt-seq-timeline");
            m_Gutter = LtUI.El("lt-seq-gutter");
            timeline.Add(m_Gutter);
            VisualElement main = LtUI.El("lt-seq-main");
            timeline.Add(main);

            m_Ruler = LtUI.El("lt-seq-ruler");
            m_Ruler.generateVisualContent += DrawRuler;
            m_RulerHead = LtUI.El("lt-seq-head");
            m_RulerHead.pickingMode = PickingMode.Ignore;
            m_Ruler.Add(m_RulerHead);
            m_Ruler.tooltip = "Drag to scrub. It changes nothing in the scene.";
            main.Add(m_Ruler);

            m_Tracks = LtUI.El("lt-seq-tracks");
            m_Tracks.generateVisualContent += DrawTracks;
            m_Tracks.focusable = true;
            main.Add(m_Tracks);
            m_CueLayer = LtUI.El("lt-seq-layer");
            m_CueLayer.pickingMode = PickingMode.Ignore;
            m_Tracks.Add(m_CueLayer);
            m_SnapLine = LtUI.El("lt-seq-snap");
            m_SnapLine.pickingMode = PickingMode.Ignore;
            m_SnapLine.style.display = DisplayStyle.None;
            m_Tracks.Add(m_SnapLine);
            m_Head = LtUI.El("lt-seq-head");
            m_Head.pickingMode = PickingMode.Ignore;
            m_Tracks.Add(m_Head);
            m_MarqueeBox = LtUI.El("lt-seq-marquee");
            m_MarqueeBox.pickingMode = PickingMode.Ignore;
            m_MarqueeBox.style.display = DisplayStyle.None;
            m_Tracks.Add(m_MarqueeBox);

            m_Tracks.RegisterCallback<PointerDownEvent>(OnTrackDown);
            m_Tracks.RegisterCallback<PointerMoveEvent>(OnTrackMove);
            m_Tracks.RegisterCallback<PointerUpEvent>(OnTrackUp);
            m_Tracks.RegisterCallback<WheelEvent>(OnWheel);
            m_Tracks.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!m_Fitted && m_Model != null && m_Tracks.resolvedStyle.width > 0)
                {
                    m_Fitted = true;
                    FitCommand();
                }
                RefreshAll();
            });
            m_Tracks.AddManipulator(new ContextualMenuManipulator(FillMenu));

            m_Ruler.RegisterCallback<PointerDownEvent>(OnRulerDown);
            m_Ruler.RegisterCallback<PointerMoveEvent>(OnRulerMove);
            m_Ruler.RegisterCallback<PointerUpEvent>(OnRulerUp);
            m_Ruler.RegisterCallback<WheelEvent>(OnWheel);
            return timeline;
        }

        // ------------------------------------------------------------- target

        protected override void SelectionChanged() => Follow(Selection.activeGameObject);

        void Follow(GameObject go)
        {
            if (go == null) return;
            var sequencer = go.GetComponent<LetteraSequencer>();
            if (sequencer != null) SetTarget(sequencer);
            else if (EngineOf(go, out _) != null) SetTarget(null, go);
        }

        internal void SetTarget(LetteraSequencer sequencer, GameObject bareText = null)
        {
            EnsureBuilt();
            if (sequencer == m_Sequencer && sequencer != null) return;
            LeavePreview();
            m_Sequencer = sequencer;
            m_Serialized = sequencer != null ? new SerializedObject(sequencer) : null;
            m_FullEditor = -1;
            m_DetailShape = -1;
            m_Fitted = false;
            m_LastLength = -1;
            if (sequencer == null)
            {
                m_Model = null;
                ShowEmpty(bareText);
                return;
            }
            m_Model = new SequencerModel(() => m_Sequencer.Cues, cue => m_Sequencer.EndOfCue(cue) - cue.Start);
            m_Empty.style.display = DisplayStyle.None;
            m_Body.style.display = DisplayStyle.Flex;
            titleContent = new GUIContent("Lettera Sequencer");
            m_Root.Unbind();
            m_Root.TrackSerializedObjectValue(m_Serialized, _ => OnOutsideEdit());
            RefreshAll();
        }

        void ShowEmpty(GameObject bareText = null)
        {
            m_Empty.style.display = DisplayStyle.Flex;
            m_Body.style.display = DisplayStyle.None;
            m_EmptyAction.Clear();
            if (bareText != null)
            {
                m_EmptyText.text = $"'{bareText.name}' has no sequencer. Add one, then give it cues here.";
                m_EmptyAction.Add(LtUI.Clickable("+ Sequencer", "lt-tool", "Adds a sequencer to this text", () => SetTarget(Undo.AddComponent<LetteraSequencer>(bareText))));
            }
            else
            {
                m_EmptyText.text = "Select a text in the Hierarchy. If it has a sequencer, its cues show here; if not, this offers to add one.";
                m_EmptyAction.Add(LtUI.Clickable("Use Selected Text", "lt-tool", "Looks at the selection again", () => Follow(Selection.activeGameObject)));
            }
        }

        void OnOutsideEdit()
        {
            if (m_Sequencer == null || m_Gesture == Gesture.Move) return;
            RefreshAll();
        }

        void OnUndoRedo()
        {
            if (m_Sequencer == null || m_Model == null) return;
            m_Model.ClampSelection();
            if (m_FullEditor >= m_Sequencer.Cues.Count) m_FullEditor = -1;
            m_Serialized.Update();
            m_DetailShape = -1;
            if (m_Previewing) m_Sequencer.Rebind();
            RefreshAll();
        }

        void LeavePreviewAndRefresh()
        {
            LeavePreview();
            RefreshAll();
        }

        // ------------------------------------------------------------- editing

        /// <summary>One change, one undo step.</summary>
        void Edit(string name, Action change)
        {
            if (m_Sequencer == null) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            Undo.RecordObject(m_Sequencer, name);
            change();
            AfterEdit();
            Undo.IncrementCurrentGroup();
        }

        void AfterEdit()
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(m_Sequencer);
            Undo.FlushUndoRecordObjects();
            m_Serialized.Update();
            if (m_Previewing) m_Sequencer.Rebind();
            m_DetailShape = -1;
            RefreshAll();
        }

        void Say(string words)
        {
            if (m_Message == null) return;
            m_Message.text = words ?? "";
            m_Message.style.display = string.IsNullOrEmpty(words) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void AddCue()
        {
            if (m_Sequencer == null) return;
            var choices = LetteraParamChoices.For(m_Sequencer.GetComponent<LetteraText>() as Component ?? m_Sequencer.GetComponent<LetteraWorldText>());
            Edit("Add Cue", () =>
            {
                var cue = new LetteraCue { Start = m_Model.Playhead, Lane = m_Model.FreeRow(m_Model.Playhead), UnitSeconds = 1, To = LetteraValue.Of(1) };
                if (choices.Count > 0)
                {
                    cue.Target.Style = choices[0].Style;
                    cue.Target.Node = choices[0].Node;
                    cue.Target.Parameter = choices[0].Parameter;
                }
                m_Sequencer.Cues.Add(cue);
                m_Model.Selected.Clear();
                m_Model.Selected.Add(m_Sequencer.Cues.Count - 1);
            });
            Say(choices.Count > 0 ? null : "This text has no style with a parameter yet. Add a style, then pick its parameter in the cue.");
        }

        void SplitCommand()
        {
            if (m_Sequencer == null) return;
            string problem = null;
            Edit("Split Cue", () => problem = m_Model.SplitSelected(m_Model.Playhead, cue => LetteraSequencerEditor.KindOf(m_Sequencer, cue.Target)));
            Say(problem);
        }

        void DuplicateCommand()
        {
            if (m_Model != null && m_Model.Selected.Count > 0) Edit("Duplicate Cues", m_Model.Duplicate);
        }

        void CopyCommand()
        {
            if (m_Model != null && m_Model.Selected.Count > 0) WriteClip(m_Model.CopyText());
        }

        void PasteCommand()
        {
            if (m_Sequencer == null) return;
            string text = ReadClip();
            if (!SequencerModel.CanPaste(text))
            {
                Say("The clipboard holds no cues.");
                return;
            }
            Edit("Paste Cues", () => m_Model.Paste(text, m_Model.Playhead));
            Say(null);
        }

        void MuteCommand()
        {
            if (m_Model != null && m_Model.Selected.Count > 0) Edit("Mute Cues", m_Model.ToggleMute);
        }

        void DeleteCommand()
        {
            if (m_Model == null || m_Model.Selected.Count == 0) return;
            m_FullEditor = -1;
            Edit("Delete Cues", m_Model.Delete);
        }

        void FitCommand()
        {
            if (m_Model == null) return;
            float width = m_Tracks.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) return;
            m_Model.View.Fit(Mathf.Max(m_Model.ContentEnd, 1), width);
            m_Model.View.ScrollY = 0;
            RefreshAll();
        }

        void TogglePlay()
        {
            if (m_Sequencer == null) return;
            if (m_Sequencer.IsPlaying)
            {
                m_Sequencer.Pause();
                return;
            }
            BeginPreview();
            m_Sequencer.Play();
        }

        void FillMenu(ContextualMenuPopulateEvent menu)
        {
            if (m_Model == null) return;
            DropdownMenuAction.Status When(bool on) => on ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            bool any = m_Model.Selected.Count > 0;
            menu.menu.AppendAction("Split At Playhead\tS", _ => SplitCommand());
            menu.menu.AppendAction("Duplicate\tCtrl+D", _ => DuplicateCommand(), When(any));
            menu.menu.AppendAction("Copy\tCtrl+C", _ => CopyCommand(), When(any));
            menu.menu.AppendAction("Paste\tCtrl+V", _ => PasteCommand());
            menu.menu.AppendAction("Mute\tM", _ => MuteCommand(), When(any));
            menu.menu.AppendAction("Delete\tDel", _ => DeleteCommand(), When(any));
            menu.menu.AppendSeparator();
            menu.menu.AppendAction("Fit\tF", _ => FitCommand());
            menu.menu.AppendAction("Play Or Pause\tSpace", _ => TogglePlay());
        }

        void OnKey(KeyDownEvent e)
        {
            if (m_Model == null) return;
            // A field being typed in keeps its keys.
            if (m_Root.panel?.focusController?.focusedElement is VisualElement focused && m_Detail.Contains(focused)) return;
            bool control = e.ctrlKey || e.commandKey;
            switch (e.keyCode)
            {
                case KeyCode.Space: TogglePlay(); break;
                case KeyCode.S when !control: SplitCommand(); break;
                case KeyCode.D when control: DuplicateCommand(); break;
                case KeyCode.C when control: CopyCommand(); break;
                case KeyCode.V when control: PasteCommand(); break;
                case KeyCode.A when control:
                    m_Model.SelectAll();
                    m_DetailShape = -1;
                    RefreshAll();
                    break;
                case KeyCode.M when !control: MuteCommand(); break;
                case KeyCode.F when !control: FitCommand(); break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteCommand();
                    break;
                case KeyCode.Escape:
                    m_Model.ClearSelection();
                    m_DetailShape = -1;
                    RefreshAll();
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        // ------------------------------------------------------------- pointer on the tracks

        Vector2 Local(VisualElement element, Vector2 world) => element.WorldToLocal(world);

        void OnTrackDown(PointerDownEvent e)
        {
            if (m_Model == null) return;
            Vector2 at = Local(m_Tracks, e.position);
            m_Root.Focus();
            m_LastAt = m_PressAt = at;
            if (e.button == 2 || (e.button == 0 && e.altKey))
            {
                m_Gesture = Gesture.Pan;
                m_Tracks.CapturePointer(e.pointerId);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            bool add = e.ctrlKey || e.commandKey || e.shiftKey;
            int hit = m_Model.Hit(at.x, at.y, out SequencerZone zone);
            if (hit >= 0)
            {
                m_Model.Click(hit, add);
                if (e.clickCount == 2)
                {
                    m_FullEditor = hit;
                    m_DetailShape = -1;
                    RefreshAll();
                    return;
                }
                if (m_FullEditor >= 0 && m_FullEditor != hit) m_FullEditor = -1;
                m_Gesture = m_Model.IsSelected(hit) ? Gesture.Press : Gesture.None;
                m_PressCue = hit;
                m_PressZone = zone;
            }
            else
            {
                if (!add) m_Model.ClearSelection();
                m_FullEditor = -1;
                m_Gesture = Gesture.Marquee;
            }
            m_DetailShape = -1;
            if (m_Gesture != Gesture.None) m_Tracks.CapturePointer(e.pointerId);
            RefreshAll();
            e.StopPropagation();
        }

        void OnTrackMove(PointerMoveEvent e)
        {
            if (m_Gesture == Gesture.None || m_Model == null) return;
            Vector2 at = Local(m_Tracks, e.position);
            switch (m_Gesture)
            {
                case Gesture.Pan:
                    m_Model.View.Pan(at.x - m_LastAt.x, at.y - m_LastAt.y);
                    m_LastAt = at;
                    RefreshAll();
                    break;
                case Gesture.Press:
                    if ((at - m_PressAt).sqrMagnitude < 9) return;
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(m_PressZone == SequencerZone.Body ? "Move Cues" : "Resize Cue");
                    m_UndoGroup = Undo.GetCurrentGroup();
                    m_Model.BeginDrag(m_PressCue, m_PressZone, m_PressAt.x, m_Model.View.RowAt(m_PressAt.y));
                    m_Gesture = Gesture.Move;
                    DragStep(at, e.ctrlKey || e.commandKey);
                    break;
                case Gesture.Move:
                    DragStep(at, e.ctrlKey || e.commandKey);
                    break;
                case Gesture.Marquee:
                    m_LastAt = at;
                    PlaceMarquee();
                    break;
            }
            e.StopPropagation();
        }

        // Each step goes on the undo stack, and the end of the drag folds them into one.
        void DragStep(Vector2 at, bool free)
        {
            Undo.RecordObject(m_Sequencer, Undo.GetCurrentGroupName());
            if (!m_Model.UpdateDrag(at.x, m_Model.View.RowAt(at.y), free)) return;
            if (m_Previewing) m_Sequencer.Rebind();
            RefreshCues();
            RefreshAxes();
            float line = m_Model.SnapLine;
            m_SnapLine.style.display = line >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (line >= 0) m_SnapLine.style.left = m_Model.View.X(line);
        }

        void OnTrackUp(PointerUpEvent e)
        {
            if (m_Gesture == Gesture.None || m_Model == null) return;
            Vector2 at = Local(m_Tracks, e.position);
            Gesture gesture = m_Gesture;
            m_Gesture = Gesture.None;
            m_Tracks.ReleasePointer(e.pointerId);
            m_SnapLine.style.display = DisplayStyle.None;
            if (gesture == Gesture.Move)
            {
                m_Model.EndDrag();
                PrefabUtility.RecordPrefabInstancePropertyModifications(m_Sequencer);
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(m_UndoGroup);
                Undo.IncrementCurrentGroup();
                m_Serialized.Update();
                m_DetailShape = -1;
            }
            else if (gesture == Gesture.Marquee)
            {
                m_MarqueeBox.style.display = DisplayStyle.None;
                if ((at - m_PressAt).sqrMagnitude >= 9)
                    m_Model.Marquee(m_PressAt.x, m_PressAt.y, at.x, at.y, e.ctrlKey || e.commandKey || e.shiftKey);
                m_DetailShape = -1;
            }
            else if (gesture == Gesture.Press && !(e.ctrlKey || e.commandKey || e.shiftKey))
            {
                // A plain click on one of many takes it alone, once it is clear no drag is coming.
                m_Model.ClearSelection();
                m_Model.Selected.Add(m_PressCue);
                m_DetailShape = -1;
            }
            RefreshAll();
            e.StopPropagation();
        }

        void PlaceMarquee()
        {
            m_MarqueeBox.style.display = DisplayStyle.Flex;
            m_MarqueeBox.style.left = Mathf.Min(m_PressAt.x, m_LastAt.x);
            m_MarqueeBox.style.top = Mathf.Min(m_PressAt.y, m_LastAt.y);
            m_MarqueeBox.style.width = Mathf.Abs(m_LastAt.x - m_PressAt.x);
            m_MarqueeBox.style.height = Mathf.Abs(m_LastAt.y - m_PressAt.y);
        }

        void OnWheel(WheelEvent e)
        {
            if (m_Model == null) return;
            Vector2 at = Local(m_Tracks, e.mousePosition);
            if (e.ctrlKey || e.commandKey) m_Model.View.ZoomAt(at.x, Mathf.Pow(1.1f, -e.delta.y));
            else if (e.shiftKey) m_Model.View.Pan(-e.delta.y * 16, 0);
            else m_Model.View.Pan(-e.delta.x * 16, -e.delta.y * 16);
            RefreshAll();
            e.StopPropagation();
        }

        // ------------------------------------------------------------- ruler

        void ScrubTo(Vector2 world)
        {
            float moment = Mathf.Max(0, m_Model.View.T(Local(m_Ruler, world).x));
            BeginPreview();
            m_Sequencer.Pause();
            m_Sequencer.Seek(moment);
            RefreshHead();
        }

        void OnRulerDown(PointerDownEvent e)
        {
            if (m_Model == null || e.button != 0) return;
            m_Gesture = Gesture.Scrub;
            m_Ruler.CapturePointer(e.pointerId);
            ScrubTo(e.position);
            e.StopPropagation();
        }

        void OnRulerMove(PointerMoveEvent e)
        {
            if (m_Gesture != Gesture.Scrub || m_Model == null) return;
            ScrubTo(e.position);
            e.StopPropagation();
        }

        void OnRulerUp(PointerUpEvent e)
        {
            if (m_Gesture != Gesture.Scrub) return;
            m_Gesture = Gesture.None;
            m_Ruler.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        // ------------------------------------------------------------- drawing

        void RefreshAll()
        {
            if (m_Model == null || m_Tracks == null) return;
            RefreshCues();
            RefreshAxes();
            RefreshHead();
            RefreshStatus();
            RefreshDetail();
        }

        void RefreshAxes()
        {
            m_Ruler.MarkDirtyRepaint();
            m_Tracks.MarkDirtyRepaint();
            RefreshTicks();
            RefreshRows();
        }

        void RefreshCues()
        {
            List<LetteraCue> cues = m_Sequencer.Cues;
            while (m_CueBoxes.Count < cues.Count)
            {
                VisualElement box = LtUI.El("lt-seq-cue");
                box.pickingMode = PickingMode.Ignore;
                Label label = LtUI.Text("", "lt-seq-cue__label");
                label.pickingMode = PickingMode.Ignore;
                box.Add(label);
                m_CueLayer.Add(box);
                m_CueBoxes.Add(box);
            }
            for (int i = 0; i < m_CueBoxes.Count; i++)
            {
                VisualElement box = m_CueBoxes[i];
                if (i >= cues.Count)
                {
                    box.style.display = DisplayStyle.None;
                    continue;
                }
                LetteraCue cue = cues[i];
                box.style.display = DisplayStyle.Flex;
                float left = m_Model.View.X(cue.Start), right = m_Model.View.X(cue.Start + m_Model.SpanOf(cue));
                box.style.left = left;
                box.style.width = Mathf.Max(right - left, 3);
                box.style.top = m_Model.View.Y(cue.Lane) + 2;
                box.style.height = SequencerView.RowHeight - 4;
                box.EnableInClassList("lt-seq-cue--on", m_Model.IsSelected(i));
                box.EnableInClassList("lt-seq-cue--muted", cue.Muted);
                box.EnableInClassList("lt-seq-cue--problem", cue.Problem != null);
                ((Label)box[0]).text = Describe(cue);
                box.tooltip = cue.Problem ?? Describe(cue);
            }
        }

        internal string Describe(LetteraCue cue)
        {
            string name = string.IsNullOrEmpty(cue.Target.Parameter) ? "No parameter" : cue.Target.Parameter;
            switch (LetteraSequencerEditor.KindOf(m_Sequencer, cue.Target))
            {
                case LetteraParamKind.Float:
                case LetteraParamKind.Int:
                    return FormattableString.Invariant($"{name}  {cue.From.Number:0.##} to {cue.To.Number:0.##}");
                default:
                    return name;
            }
        }

        void RefreshTicks()
        {
            SequencerView view = m_Model.View;
            float width = m_Ruler.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) width = 600;
            float step = view.TickStep();
            int first = Mathf.CeilToInt(view.ScrollSeconds / step), shown = 0;
            for (int n = first; ; n++)
            {
                float x = view.X(n * step);
                if (x > width) break;
                if (shown >= m_TickLabels.Count)
                {
                    Label made = LtUI.Text("", "lt-seq-tick");
                    made.pickingMode = PickingMode.Ignore;
                    m_Ruler.Add(made);
                    m_TickLabels.Add(made);
                }
                Label label = m_TickLabels[shown++];
                label.style.display = DisplayStyle.Flex;
                label.style.left = x + 3;
                label.text = (n * step).ToString(step < 0.1f ? "0.00" : step < 1 ? "0.0" : "0", CultureInfo.InvariantCulture);
            }
            for (int i = shown; i < m_TickLabels.Count; i++) m_TickLabels[i].style.display = DisplayStyle.None;
        }

        void RefreshRows()
        {
            SequencerView view = m_Model.View;
            int rows = m_Model.RowCount + 1;
            while (m_RowLabels.Count < rows)
            {
                Label made = LtUI.Text("", "lt-seq-rowlabel");
                m_Gutter.Add(made);
                m_RowLabels.Add(made);
            }
            for (int i = 0; i < m_RowLabels.Count; i++)
            {
                Label label = m_RowLabels[i];
                bool on = i < rows;
                label.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                label.text = (i + 1).ToString(CultureInfo.InvariantCulture);
                label.style.top = view.Y(i) + 22;
            }
        }

        void DrawRuler(MeshGenerationContext context)
        {
            if (m_Model == null) return;
            Painter2D p = context.painter2D;
            SequencerView view = m_Model.View;
            Rect box = m_Ruler.contentRect;
            if (float.IsNaN(box.width) || float.IsNaN(box.height)) return;
            float step = view.TickStep(), minor = step / 5;
            p.lineWidth = 1;
            p.strokeColor = m_Ruler.resolvedStyle.color;
            for (int n = Mathf.CeilToInt(view.ScrollSeconds / minor); ; n++)
            {
                float x = view.X(n * minor);
                if (x > box.width) break;
                bool major = n % 5 == 0;
                p.BeginPath();
                p.MoveTo(new Vector2(x, box.height));
                p.LineTo(new Vector2(x, box.height - (major ? box.height : 5)));
                p.Stroke();
            }
        }

        void DrawTracks(MeshGenerationContext context)
        {
            if (m_Model == null) return;
            Painter2D p = context.painter2D;
            SequencerView view = m_Model.View;
            Rect box = m_Tracks.contentRect;
            if (float.IsNaN(box.width) || float.IsNaN(box.height)) return;
            Color line = m_Tracks.resolvedStyle.color;
            p.lineWidth = 1;
            p.strokeColor = line;
            float step = view.TickStep();
            for (int n = Mathf.CeilToInt(view.ScrollSeconds / step); ; n++)
            {
                float x = view.X(n * step);
                if (x > box.width) break;
                p.BeginPath();
                p.MoveTo(new Vector2(x, 0));
                p.LineTo(new Vector2(x, box.height));
                p.Stroke();
            }
            for (int row = view.RowAt(0); ; row++)
            {
                float y = view.Y(row);
                if (y > box.height) break;
                p.BeginPath();
                p.MoveTo(new Vector2(0, y));
                p.LineTo(new Vector2(box.width, y));
                p.Stroke();
            }
        }

        void Tick()
        {
            if (m_Sequencer == null)
            {
                if (m_Model != null) SetTarget(null);
                return;
            }
            m_Play.text = m_Sequencer.IsPlaying ? "❚❚" : "▶";
            float length = m_Sequencer.ResolvedLength;
            if (!Mathf.Approximately(length, m_LastLength))
            {
                m_LastLength = length;
                RefreshCues();
                RefreshStatus();
            }
            RefreshHead();
            RefreshProblems();
        }

        void RefreshHead()
        {
            if (m_Sequencer == null || m_Model == null) return;
            float head = m_Sequencer.Playhead;
            m_Model.Playhead = head;
            float x = m_Model.View.X(head);
            if (Mathf.Approximately(x, m_LastHead) && m_Time.text.Length > 0 && m_LastLength >= 0) return;
            m_LastHead = x;
            m_Head.style.left = x;
            m_RulerHead.style.left = x;
            m_Time.text = FormattableString.Invariant($"{head:0.00} / {Mathf.Max(m_LastLength, 0):0.00} s");
        }

        void RefreshProblems()
        {
            string problems = null;
            foreach (LetteraCue cue in m_Sequencer.Cues)
                if (cue.Problem != null)
                {
                    problems = cue.Problem;
                    break;
                }
            if (problems == m_LastProblems) return;
            m_LastProblems = problems;
            m_Chips.Clear();
            if (problems != null) m_Chips.Add(LtUI.Text(problems, "lt-chip lt-chip--orange"));
            RefreshCues();
        }

        void RefreshStatus()
        {
            int cues = m_Sequencer.Cues.Count, selected = m_Model.Selected.Count;
            m_Status.text = (cues == 1 ? "1 cue" : cues + " cues") + (selected > 0 ? $", {selected} selected" : "") + FormattableString.Invariant($". {m_Model.View.PixelsPerSecond:0} px a second.");
        }

        // ------------------------------------------------------------- detail

        void RefreshDetail()
        {
            int shape = m_FullEditor >= 0 ? 100000 + m_FullEditor : m_Model.Selected.Count == 0 ? 0 : m_Model.Selected.Count;
            if (shape != m_DetailShape)
            {
                m_DetailShape = shape;
                m_Detail.Clear();
                if (m_FullEditor >= 0) m_Detail.Add(FullEditor(m_FullEditor));
                else if (m_Model.Selected.Count > 0) m_Detail.Add(SharedFields());
                else if (m_Sequencer.Cues.Count == 0)
                    m_Detail.Add(LtUI.Card("No cues yet", LtUI.Text("A cue ramps one parameter of this text over time.", "lt-subtitle"), LtUI.El("lt-chips", LtUI.Clickable("+ Add Cue", "lt-tool", "A new cue at the playhead", AddCue))));
                else m_Detail.Add(LtUI.Text("Click a cue to edit it, drag across several to edit them together, double click for all of a cue's settings.", "lt-subtitle"));
            }
        }

        LetteraCue[] SelectedCues() => m_Model.Selected.Where(i => i < m_Sequencer.Cues.Count).Select(i => m_Sequencer.Cues[i]).ToArray();

        VisualElement SharedFields()
        {
            LetteraCue[] cues = SelectedCues();
            VisualElement card = LtUI.Card(cues.Length == 1 ? "Cue" : cues.Length + " cues");
            card.name = "lt-seq-shared";
            card.Add(SharedNumber("Start", "When it begins, in seconds.", c => c.Start, (c, v) => c.Start = Mathf.Max(v, 0)));
            card.Add(SharedNumber("Length", "How long one range's ramp takes.", c => c.UnitSeconds, (c, v) => c.UnitSeconds = Mathf.Max(v, SequencerModel.MinUnit)));
            card.Add(SharedNumber("Step", "The delay from one range's start to the next's.", c => c.StepSeconds, (c, v) => c.StepSeconds = Mathf.Max(v, 0)));
            card.Add(SharedNumber("Row", "The row it shows on. It means nothing at run time.", c => c.Lane + 1, (c, v) => c.Lane = Mathf.Max(Mathf.RoundToInt(v) - 1, 0)));
            card.Add(SharedNumber("Priority", "Which wins where cues meet.", c => c.Priority, (c, v) => c.Priority = Mathf.RoundToInt(v)));
            card.Add(SharedChoice<LetteraEase>("Curve", c => c.Curve, (c, v) => c.Curve = v));
            card.Add(SharedChoice<LetteraMix>("Mix", c => c.Mix, (c, v) => c.Mix = v));
            card.Add(SharedSwitch("Muted", c => c.Muted, (c, v) => c.Muted = v));
            if (cues.Length == 1)
            {
                int index = m_Model.Selected[0];
                card.Add(LtUI.El("lt-chips", LtUI.Clickable("All Settings", "lt-tool", "Opens this cue's full editor here. A double click does the same.", () =>
                {
                    m_FullEditor = index;
                    m_DetailShape = -1;
                    RefreshDetail();
                })));
            }
            return card;
        }

        VisualElement SharedNumber(string label, string tip, Func<LetteraCue, float> read, Action<LetteraCue, float> write)
        {
            var field = new TextField { isDelayed = false };
            field.AddToClassList("lt-text-input");
            field.name = "lt-seq-field-" + label;
            void Show()
            {
                LetteraCue[] cues = SelectedCues();
                if (cues.Length == 0) return;
                bool same = cues.All(c => Mathf.Approximately(read(c), read(cues[0])));
                field.SetValueWithoutNotify(same ? ((double)read(cues[0])).ToString("0.####", CultureInfo.InvariantCulture) : "—");
            }
            void Commit()
            {
                LetteraCue[] cues = SelectedCues();
                if (cues.Length == 0) return;
                string typed = field.value;
                if (typed == "—" || !LtExpression.TryEvaluate(typed, read(cues[0]), out double value))
                {
                    Show();
                    return;
                }
                if (cues.All(c => Mathf.Approximately(read(c), (float)value)))
                {
                    Show();
                    return;
                }
                Edit("Change " + label, () =>
                {
                    foreach (LetteraCue cue in cues) write(cue, (float)value);
                });
                Show();
            }
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Commit();
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Show();
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Commit());
            Show();
            return LtUI.Row(label, field, tip);
        }

        VisualElement SharedChoice<T>(string label, Func<LetteraCue, T> read, Action<LetteraCue, T> write) where T : struct, Enum
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            var names = values.Select(v => (ObjectNames.NicifyVariableName(v.ToString()), (string)null, (object)v)).ToList();
            return new LtPick(label, () =>
            {
                LetteraCue[] cues = SelectedCues();
                return cues.All(c => read(c).Equals(read(cues[0]))) ? ObjectNames.NicifyVariableName(read(cues[0]).ToString()) : "—";
            }, () => names, chosen => Edit("Change " + label, () =>
            {
                foreach (LetteraCue cue in SelectedCues()) write(cue, (T)chosen);
            }));
        }

        VisualElement SharedSwitch(string label, Func<LetteraCue, bool> read, Action<LetteraCue, bool> write)
        {
            VisualElement track = LtUI.El("lt-switch", LtUI.El("lt-switch__knob"));
            LetteraCue[] cues = SelectedCues();
            track.EnableInClassList("lt-switch--on", cues.All(read));
            track.RegisterCallback<ClickEvent>(_ =>
            {
                bool on = !SelectedCues().All(read);
                Edit("Change " + label, () =>
                {
                    foreach (LetteraCue cue in SelectedCues()) write(cue, on);
                });
                track.EnableInClassList("lt-switch--on", on);
            });
            return LtUI.Row(label, track);
        }

        VisualElement FullEditor(int index)
        {
            m_Serialized.Update();
            SerializedProperty list = m_Serialized.FindProperty("m_Cues");
            if (index >= list.arraySize) return LtUI.Text("That cue has gone.", "lt-subtitle");
            VisualElement card = LetteraSequencerEditor.CueFields(m_Sequencer, m_Serialized, list.GetArrayElementAtIndex(index), index);
            card.name = "lt-seq-full";
            SerializedProperty select = list.GetArrayElementAtIndex(index).FindPropertyRelative("Select");
            VisualElement ranges = LtUI.Card("Which ranges");
            foreach (string field in new[] { "Label", "Primary", "Token", "TokenSlot", "BareOnly", "OverlapStart", "OverlapEnd", "Skip", "Take" })
                ranges.Add(LtFrame.Row(select.FindPropertyRelative(field).Copy()));
            VisualElement holder = LtUI.El("lt-list", card, ranges, LtUI.El("lt-chips", LtUI.Clickable("Close", "lt-tool", "Back to the short form", () =>
            {
                m_FullEditor = -1;
                m_DetailShape = -1;
                RefreshDetail();
            })));
            holder.Bind(m_Serialized);
            return holder;
        }
    }
}
