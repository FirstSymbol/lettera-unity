using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Runs motion in edit mode while someone wants to see it. It writes no field, so a scene stays clean.</summary>
    internal static class LetteraMotionPreview
    {
        static double s_Last;
        static int s_Wanted;

        internal static bool IsOn => s_Wanted > 0;

        internal static void Begin()
        {
            if (s_Wanted++ > 0) return;
            s_Last = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        internal static void End()
        {
            if (s_Wanted == 0 || --s_Wanted > 0) return;
            EditorApplication.update -= Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            double now = EditorApplication.timeSinceStartup;
            float step = Mathf.Min((float)(now - s_Last), LetteraMotionClock.MaxStep);
            s_Last = now;
            if (LetteraMotionHub.Count == 0 && LetteraMotionHub.ClientCount == 0) return;
            LetteraMotionHub.Tick(step, step);
            LetteraWorldBatcher.Update();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    /// <summary>What a text holds that moves or answers, from one cheap look at its styles.</summary>
    internal static class LetteraMotionScan
    {
        internal struct Counts
        {
            public int Links, Rules, Motion, Inline, Semantics;
        }

        internal static Counts Of(IEnumerable<LetteraStyle> styles)
        {
            var counts = new Counts();
            foreach (LetteraStyle style in styles)
                if (style?.Effect != null)
                    Add(style.Effect, ref counts);
            return counts;
        }

        static void Add(LetteraEffect effect, ref Counts counts)
        {
            if (effect is CompositeEffect composite)
            {
                foreach (LetteraEffect child in composite.Children)
                    if (child != null)
                        Add(child, ref counts);
                return;
            }
            if (effect is LetteraRangeEffect range)
            {
                counts.Links++;
                counts.Rules += range.Rules.Count;
            }
            if (effect is LetteraMotionEffect || effect is RevealEffect || effect is MarqueeEffect) counts.Motion++;
            if (effect is InlineEffect) counts.Inline++;
            if (effect is SemanticEffect) counts.Semantics++;
        }

        /// <summary>What a text's author should hear about, each with what would fix it.</summary>
        internal static IEnumerable<string> Warnings(Component text, IEnumerable<LetteraStyle> styles)
        {
            Counts counts = Of(styles);
            if (counts.Links > 0)
            {
                if (text is LetteraText canvas && !canvas.raycastTarget) yield return "Ranges here take no input: Raycast Target is off.";
                if (text is LetteraWorldText world && !world.RaycastTarget) yield return "Ranges here take no input: Raycast Target is off.";
                if (text.gameObject.scene.IsValid() && UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null) yield return "No Event System in the scene, so ranges take no input.";
                if (text is LetteraWorldText && Camera.main != null && Camera.main.GetComponent<LetteraWorldRaycaster>() == null) yield return "Add a World Raycaster to the camera, or this text takes no input.";
            }
            if (counts.Motion > 0 && text is LetteraText moving)
            {
                Canvas root = moving.canvas;
                if (root != null && root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length > 64) yield return "This Canvas has many graphics, and each moving text rebatches them all. A nested Canvas keeps that small.";
            }
        }
    }

    [CustomEditor(typeof(LetteraSequencer))]
    internal sealed class LetteraSequencerEditor : UnityEditor.Editor
    {
        LetteraSequencer m_Sequencer;
        Slider m_Scrub;
        Label m_Time;
        VisualElement m_Strip, m_Chips;
        bool m_Previewing;

        void OnDisable()
        {
            if (!m_Previewing) return;
            m_Previewing = false;
            LetteraMotionPreview.End();
            if (m_Sequencer != null && !Application.isPlaying) m_Sequencer.EndPreview();
        }

        void BeginPreview()
        {
            if (m_Previewing || Application.isPlaying) return;
            m_Previewing = true;
            LetteraMotionPreview.Begin();
        }

        public override VisualElement CreateInspectorGUI()
        {
            m_Sequencer = (LetteraSequencer)target;
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Sequencer", "Ramps effect parameters on one timeline, and writes no field");

            m_Chips = LtUI.El("lt-chips");
            page.Add(m_Chips);

            m_Scrub = new Slider(0, 1) { showInputField = false };
            m_Scrub.AddToClassList("lt-grow");
            m_Scrub.RegisterValueChangedCallback(e =>
            {
                if (Mathf.Approximately(e.newValue, m_Sequencer.Progress)) return;
                BeginPreview();
                m_Sequencer.Progress = e.newValue;
            });
            m_Time = LtUI.Text("0.00 / 0.00 s", "lt-subtitle");
            VisualElement transport = LtUI.El("lt-chips",
                LtUI.Clickable("▶ Play", "lt-tool", "Plays from here", () =>
                {
                    BeginPreview();
                    m_Sequencer.Play();
                }),
                LtUI.Clickable("❚❚ Pause", "lt-tool", "Stops where it stands", () => m_Sequencer.Pause()),
                LtUI.Clickable("■ Stop", "lt-tool", "Back to the start", () => m_Sequencer.Stop()),
                LtUI.Clickable("✕ End Preview", "lt-tool", "Every value goes back, and nothing was written", () => m_Sequencer.EndPreview()),
                LtUI.Clickable("Open Sequencer", "lt-tool", "The cues on rows, to drag, split and copy", () => LetteraSequencerWindow.Open(m_Sequencer)));
            page.Add(LtUI.Card("Transport", transport, m_Scrub, m_Time));

            m_Strip = new VisualElement { style = { height = 54, position = Position.Relative, overflow = Overflow.Hidden } };
            m_Strip.AddToClassList("lt-strip");
            page.Add(LtUI.Card("Timeline", m_Strip));

            page.Add(LtFrame.Card(serializedObject, "Playback", "m_Clock", "m_Cycles", "m_Repeat", "m_Length", "m_Speed", "m_PlayOnEnable", "m_HonorReducedMotion"));

            VisualElement cues = LtUI.El("lt-list");
            page.Add(LtUI.Card("Cues", cues, LtUI.Clickable("+ Add Cue", "lt-tool", "A new cue at the start", () =>
            {
                SerializedProperty list = serializedObject.FindProperty("m_Cues");
                list.arraySize++;
                serializedObject.ApplyModifiedProperties();
                RebuildCues(cues);
            })));
            RebuildCues(cues);

            root.schedule.Execute(Refresh).Every(100);
            return root;
        }

        void RebuildCues(VisualElement holder)
        {
            holder.Clear();
            serializedObject.Update();
            SerializedProperty list = serializedObject.FindProperty("m_Cues");
            for (int i = 0; i < list.arraySize; i++) holder.Add(CueCard(list, i, holder));
            holder.Bind(serializedObject);
        }

        VisualElement CueCard(SerializedProperty list, int index, VisualElement holder)
        {
            VisualElement remove = LtUI.Clickable("✕", "lt-tool", "Remove this cue", () =>
            {
                list.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                RebuildCues(holder);
            });
            VisualElement card = CueFields(m_Sequencer, serializedObject, list.GetArrayElementAtIndex(index), index);
            card.Add(remove);
            return card;
        }

        /// <summary>One cue's whole editor, here and in the window.</summary>
        internal static VisualElement CueFields(LetteraSequencer sequencer, SerializedObject serialized, SerializedProperty cue, int index)
        {
            LetteraParamTarget paramTarget = sequencer.Cues.Count > index ? sequencer.Cues[index].Target : null;
            VisualElement values = LtUI.El("lt-list");
            void ShowValues()
            {
                values.Clear();
                LetteraParamKind kind = KindOf(sequencer, paramTarget);
                values.Add(ValueRow("From", cue.FindPropertyRelative("From"), kind));
                values.Add(ValueRow("To", cue.FindPropertyRelative("To"), kind));
                values.Bind(serialized);
            }

            var choices = LetteraParamChoices.For(sequencer.GetComponent<LetteraText>() as Component ?? sequencer.GetComponent<LetteraWorldText>());
            var picker = new DropdownField(choices.Select(c => c.Label).ToList(), choices.FindIndex(c => c.Matches(paramTarget)));
            picker.RegisterValueChangedCallback(e =>
            {
                int at = picker.index;
                if (at < 0 || at >= choices.Count) return;
                SerializedProperty stored = cue.FindPropertyRelative("Target");
                choices[at].WriteTo(stored);
                serialized.ApplyModifiedProperties();
                ShowValues();
            });
            ShowValues();

            return LtUI.Card("Cue " + (index + 1),
                LtUI.Row("Parameter", picker),
                values,
                LtFrame.Row(cue.FindPropertyRelative("Start").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("UnitSeconds").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("StepSeconds").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("Curve").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("Mix").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("Priority").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("Muted").Copy()),
                LtFrame.Row(cue.FindPropertyRelative("Lane").Copy()));
        }

        internal static LetteraParamKind KindOf(LetteraSequencer sequencer, LetteraParamTarget paramTarget)
        {
            if (paramTarget == null) return LetteraParamKind.Float;
            TextEngine engine = sequencer.Engine;
            LetteraEffect effect = engine != null ? paramTarget.Resolve(engine) : null;
            return LetteraParamTarget.Find(effect, paramTarget.Parameter)?.Kind ?? LetteraParamKind.Float;
        }

        static VisualElement ValueRow(string label, SerializedProperty value, LetteraParamKind kind)
        {
            switch (kind)
            {
                case LetteraParamKind.Color: return LtUI.Field(label, value.FindPropertyRelative("Color"));
                case LetteraParamKind.Vector2: return LtUI.Field(label, value.FindPropertyRelative("Vector"));
                case LetteraParamKind.String: return LtUI.Field(label, value.FindPropertyRelative("Text"));
                default: return LtUI.Field(label, value.FindPropertyRelative("Number"));
            }
        }

        void Refresh()
        {
            if (m_Sequencer == null) return;
            float length = m_Sequencer.ResolvedLength;
            m_Scrub.SetValueWithoutNotify(m_Sequencer.Progress);
            m_Time.text = $"{m_Sequencer.Playhead:0.00} / {length:0.00} s";

            m_Chips.Clear();
            foreach (LetteraCue cue in m_Sequencer.Cues)
                if (cue.Problem != null)
                    m_Chips.Add(LtUI.Text(cue.Problem, "lt-chip lt-chip--orange"));
            foreach (LetteraCue cue in m_Sequencer.Cues)
                if (cue.BoundInfo != null && (cue.BoundInfo.Level == DirtyLevel.Parse || cue.BoundInfo.Level == DirtyLevel.Shape))
                {
                    m_Chips.Add(LtUI.Text($"'{cue.Target.Parameter}' reshapes the text each frame it changes.", "lt-chip lt-chip--orange"));
                    break;
                }

            m_Strip.Clear();
            if (length <= 0) return;
            foreach (LetteraCue cue in m_Sequencer.Cues)
            {
                float end = cue.Start + cue.UnitSeconds;
                var bar = new VisualElement();
                bar.AddToClassList("lt-chip");
                bar.AddToClassList(cue.Problem != null ? "lt-chip--orange" : "lt-chip--accent");
                bar.style.position = Position.Absolute;
                bar.style.left = Length.Percent(cue.Start / length * 100);
                bar.style.width = Length.Percent(Mathf.Max(end - cue.Start, 0.02f * length) / length * 100);
                bar.style.top = cue.Lane % 3 * 18;
                bar.style.height = 15;
                bar.style.opacity = cue.Muted ? 0.35f : 1;
                bar.tooltip = cue.Target.Parameter;
                m_Strip.Add(bar);
            }
            var head = new VisualElement();
            head.style.position = Position.Absolute;
            head.style.left = Length.Percent(m_Sequencer.Progress * 100);
            head.style.width = 1;
            head.style.top = 0;
            head.style.bottom = 0;
            head.style.backgroundColor = new Color(1, 1, 1, 0.8f);
            m_Strip.Add(head);
        }
    }

    /// <summary>Every parameter a text's styles publish, as a menu.</summary>
    internal sealed class LetteraParamChoices
    {
        public string Label;
        public int Style;
        public int[] Node;
        public string Parameter;

        public bool Matches(LetteraParamTarget target) =>
            target != null && target.Style == Style && target.Parameter == Parameter && (target.Node ?? Array.Empty<int>()).SequenceEqual(Node);

        public void WriteTo(SerializedProperty target)
        {
            target.FindPropertyRelative("Style").intValue = Style;
            target.FindPropertyRelative("Parameter").stringValue = Parameter;
            SerializedProperty node = target.FindPropertyRelative("Node");
            node.arraySize = Node.Length;
            for (int i = 0; i < Node.Length; i++) node.GetArrayElementAtIndex(i).intValue = Node[i];
        }

        public static List<LetteraParamChoices> For(Component text)
        {
            var results = new List<LetteraParamChoices>();
            IList<LetteraStyle> styles = text is LetteraText canvas ? canvas.Styles : text is LetteraWorldText world ? world.Styles : null;
            if (styles == null) return results;
            for (int s = 0; s < styles.Count; s++)
                if (styles[s]?.Effect != null)
                    Walk(styles[s].Effect, s, new List<int>(), LetteraStyleList.Pretty(styles[s].Effect.GetType().Name, "Effect"), results);
            return results;
        }

        static void Walk(LetteraEffect effect, int style, List<int> node, string path, List<LetteraParamChoices> results)
        {
            if (effect is CompositeEffect composite)
            {
                for (int c = 0; c < composite.Children.Count; c++)
                {
                    LetteraEffect child = composite.Children[c];
                    if (child == null) continue;
                    node.Add(c);
                    Walk(child, style, node, path + " / " + LetteraStyleList.Pretty(child.GetType().Name, "Effect"), results);
                    node.RemoveAt(node.Count - 1);
                }
                return;
            }
            foreach (LetteraParamInfo info in effect.Parameters)
            {
                if (info.Slot < 0) continue;
                results.Add(new LetteraParamChoices { Label = $"{style + 1}. {path} / {info.Display}", Style = style, Node = node.ToArray(), Parameter = info.Id });
            }
        }
    }

    [CustomEditor(typeof(LetteraParamLink)), CanEditMultipleObjects]
    internal sealed class LetteraParamLinkEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Parameter Link", "One effect parameter as a field that curves and tracks can reach");
            VisualElement chips = LtUI.El("lt-chips");
            page.Add(chips);
            page.Add(LtFrame.AllFields(serializedObject, "Target"));
            void Refresh()
            {
                if (target == null) return;
                chips.Clear();
                string problem = ((LetteraParamLink)target).Problem;
                if (problem != null) chips.Add(LtUI.Text(problem, "lt-chip lt-chip--orange"));
            }
            Refresh();
            root.schedule.Execute(Refresh).Every(500);
            return root;
        }
    }

    [CustomEditor(typeof(LetteraSourceLoader)), CanEditMultipleObjects]
    internal sealed class LetteraSourceLoaderEditor : LetteraSimpleEditor
    {
        protected override string Title => "Source Loader";
        protected override string Subtitle => "Loads a file, an asset or an address into the text as a document";
    }

    [CustomEditor(typeof(LetteraDocumentScroller)), CanEditMultipleObjects]
    internal sealed class LetteraDocumentScrollerEditor : LetteraSimpleEditor
    {
        protected override string Title => "Document Scroller";
        protected override string Subtitle => "A viewport for a text: drag, wheel, inertia and scrollbars, with no rebuild";
    }

    [CustomEditor(typeof(LetteraRangeNavigator)), CanEditMultipleObjects]
    internal sealed class LetteraRangeNavigatorEditor : LetteraSimpleEditor
    {
        protected override string Title => "Range Navigator";
        protected override string Subtitle => "Steps a text's ranges with arrows and gamepads, and leaves at the ends";
    }

    [CustomEditor(typeof(LetteraRangeTopic))]
    internal sealed class LetteraRangeTopicEditor : LetteraSimpleEditor
    {
        protected override string Title => "Range Topic";
        protected override string Subtitle => "A name for ranges to listen to together. It holds nothing else";
    }

    [CustomEditor(typeof(LetteraEntranceLibrary))]
    internal sealed class LetteraEntranceLibraryEditor : LetteraSimpleEditor
    {
        protected override string Title => "Entrance Library";
        protected override string Subtitle => "Named ways for revealed text to arrive";
    }

    [CustomEditor(typeof(LetteraInlineLibrary))]
    internal sealed class LetteraInlineLibraryEditor : LetteraSimpleEditor
    {
        protected override string Title => "Inline Library";
        protected override string Subtitle => "Sprites and objects that can stand in a text's flow";
    }
}
