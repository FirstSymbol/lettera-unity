using System.Collections.Generic;
using FirstSymbol.Lettera.Editor;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Timeline.Editor
{
    /// <summary>Which objects of the open scenes a track can take, and how to bind one. An object that lacks the component is offered too, and binding it adds that.</summary>
    internal static class LetteraTrackBinding
    {
        internal readonly struct Candidate
        {
            public readonly Object Target;
            public readonly string Label;
            public readonly bool NeedsAdd;

            public Candidate(Object target, string label, bool needsAdd)
            {
                Target = target;
                Label = label;
                NeedsAdd = needsAdd;
            }
        }

        static bool HasText(GameObject go) => go != null && go.GetComponent<ITextComponent>() != null;

        internal static string WantedName(TrackAsset track) => track is LetteraSequenceTrack ? "Sequencer" : track is LetteraParamTrack ? "Parameter Link" : "text";

        internal static List<Candidate> Candidates(TrackAsset track)
        {
            var results = new List<Candidate>();
            var seen = new HashSet<GameObject>();
            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null) continue;
                GameObject go = behaviour.gameObject;
                bool first = seen.Add(go);
                if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                switch (track)
                {
                    case LetteraSequenceTrack _:
                        if (behaviour is LetteraSequencer) results.Add(new Candidate(behaviour, go.name, false));
                        else if (first && HasText(go) && go.GetComponent<LetteraSequencer>() == null) results.Add(new Candidate(go, go.name, true));
                        break;
                    case LetteraParamTrack _:
                        if (behaviour is LetteraParamLink) results.Add(new Candidate(behaviour, go.name, false));
                        else if (first && HasText(go) && go.GetComponent<LetteraParamLink>() == null) results.Add(new Candidate(go, go.name, true));
                        break;
                    default:
                        if (first && HasText(go)) results.Add(new Candidate(go, go.name, false));
                        break;
                }
            }
            results.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
            return results;
        }

        internal static bool Accepts(TrackAsset track, Object candidate)
        {
            GameObject go = candidate as GameObject ?? (candidate as Component)?.gameObject;
            if (go == null) return false;
            switch (track)
            {
                case LetteraSequenceTrack _: return go.GetComponent<LetteraSequencer>() != null || HasText(go);
                case LetteraParamTrack _: return go.GetComponent<LetteraParamLink>() != null || HasText(go);
                default: return HasText(go);
            }
        }

        /// <summary>What is wrong with a binding, in words, or null. No binding at all is Timeline's own to show.</summary>
        internal static string Problem(TrackAsset track, Object binding)
        {
            if (binding == null) return null;
            return track is LetteraTextTrack && !HasText(binding as GameObject) ? "This object has no Lettera text. Bind one that has." : null;
        }

        /// <summary>One undo step, with the component it had to add.</summary>
        internal static Object Bind(PlayableDirector director, TrackAsset track, Candidate candidate)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Bind Track");
            Object target = candidate.Target;
            if (candidate.NeedsAdd)
            {
                var go = (GameObject)target;
                if (track is LetteraSequenceTrack) target = Undo.AddComponent<LetteraSequencer>(go);
                else if (track is LetteraParamTrack) target = Undo.AddComponent<LetteraParamLink>(go);
            }
            Undo.RecordObject(director, "Bind Track");
            director.SetGenericBinding(track, target);
            EditorUtility.SetDirty(director);
            Undo.IncrementCurrentGroup();
            return target;
        }
    }

    /// <summary>The little letter a track carries in Timeline's header, drawn here and not loaded: a dot of the accent on a letter form.</summary>
    internal static class LetteraTrackIcons
    {
        static readonly Dictionary<char, Texture2D> s_Made = new Dictionary<char, Texture2D>();

        static readonly Dictionary<char, string[]> s_Forms = new Dictionary<char, string[]>
        {
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        };

        internal static Texture2D Of(char letter)
        {
            if (s_Made.TryGetValue(letter, out Texture2D icon) && icon != null) return icon;
            icon = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave, name = "Lettera Track " + letter };
            Color ink = EditorGUIUtility.isProSkin ? new Color(0.91f, 0.9f, 0.93f) : new Color(0.12f, 0.11f, 0.16f);
            Color accent = EditorGUIUtility.isProSkin ? new Color(0.61f, 0.43f, 1f) : new Color(0.43f, 0.25f, 0.94f);
            var pixels = new Color[256];
            string[] form = s_Forms[letter];
            for (int y = 0; y < form.Length; y++)
                for (int x = 0; x < 5; x++)
                    if (form[y][x] == '#')
                    {
                        // Each cell is drawn at one and a half in height by doubling every other row, so it reads at 16 pixels.
                        pixels[(14 - y * 2) * 16 + x * 2 + 1] = ink;
                        pixels[(14 - y * 2) * 16 + x * 2 + 2] = ink;
                        pixels[(13 - y * 2) * 16 + x * 2 + 1] = ink;
                        pixels[(13 - y * 2) * 16 + x * 2 + 2] = ink;
                    }
            for (int y = 1; y < 4; y++)
                for (int x = 12; x < 15; x++)
                    pixels[y * 16 + x] = accent;
            icon.SetPixels(pixels);
            icon.Apply();
            s_Made[letter] = icon;
            return icon;
        }
    }

    abstract class LetteraTrackEditor : TrackEditor
    {
        protected abstract char Letter { get; }

        public override TrackDrawOptions GetTrackOptions(TrackAsset track, Object binding)
        {
            TrackDrawOptions options = base.GetTrackOptions(track, binding);
            options.icon = LetteraTrackIcons.Of(Letter);
            string problem = LetteraTrackBinding.Problem(track, binding);
            if (problem != null) options.errorText = problem;
            return options;
        }

        // A drag finds only what fits. An object with a text but no component still gets Timeline's own offer to add it.
        public override bool IsBindingAssignableFrom(Object candidate, TrackAsset track) => LetteraTrackBinding.Accepts(track, candidate);
    }

    [CustomTimelineEditor(typeof(LetteraSequenceTrack))]
    sealed class LetteraSequenceTrackEditor : LetteraTrackEditor
    {
        protected override char Letter => 'S';
    }

    [CustomTimelineEditor(typeof(LetteraParamTrack))]
    sealed class LetteraParamTrackEditor : LetteraTrackEditor
    {
        protected override char Letter => 'P';
    }

    [CustomTimelineEditor(typeof(LetteraTextTrack))]
    sealed class LetteraTextTrackEditor : LetteraTrackEditor
    {
        protected override char Letter => 'T';
    }

    abstract class LetteraShotEditor : ClipEditor
    {
        protected abstract string Describe(TimelineClip clip);

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom) => OnClipChanged(clip);

        public override void OnClipChanged(TimelineClip clip)
        {
            string line = Describe(clip);
            if (line != null) clip.displayName = line;
        }

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.tooltip = Describe(clip) ?? "";
            return options;
        }
    }

    [CustomTimelineEditor(typeof(LetteraSequenceShot))]
    sealed class LetteraSequenceShotEditor : LetteraShotEditor
    {
        protected override string Describe(TimelineClip clip) => (clip.asset as LetteraSequenceShot)?.Describe();
    }

    [CustomTimelineEditor(typeof(LetteraParamShot))]
    sealed class LetteraParamShotEditor : LetteraShotEditor
    {
        protected override string Describe(TimelineClip clip) => (clip.asset as LetteraParamShot)?.Describe();
    }

    [CustomTimelineEditor(typeof(LetteraTextShot))]
    sealed class LetteraTextShotEditor : LetteraShotEditor
    {
        protected override string Describe(TimelineClip clip) => (clip.asset as LetteraTextShot)?.Describe();
    }

    // ------------------------------------------------------------- clip inspectors

    /// <summary>A clip's fields, under a line saying what it does now.</summary>
    abstract class LetteraShotInspector : UnityEditor.Editor
    {
        protected abstract string Title { get; }
        protected abstract string Subtitle { get; }
        protected abstract string Describe(Object shot);
        protected abstract void Fill(VisualElement card);

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, Title, Subtitle);
            Label line = LtUI.Text("", "lt-chip lt-chip--accent");
            line.name = "lt-shot-line";
            page.Add(LtUI.El("lt-chips", line));
            void Refresh()
            {
                if (target == null) return;
                line.text = targets.Length > 1 ? targets.Length + " clips" : Describe(target);
            }
            VisualElement card = LtUI.Card("Clip");
            Fill(card);
            page.Add(card);
            root.Bind(serializedObject);
            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            Refresh();
            return root;
        }
    }

    [CustomEditor(typeof(LetteraTextShot)), CanEditMultipleObjects]
    sealed class LetteraTextShotInspector : LetteraShotInspector
    {
        protected override string Title => "Text Clip";
        protected override string Subtitle => "A line this text says for as long as the clip plays. Its own text stays as it is";
        protected override string Describe(Object shot) => ((LetteraTextShot)shot).Describe();

        protected override void Fill(VisualElement card)
        {
            SerializedProperty text = serializedObject.FindProperty("Text");
            var field = new TextField { multiline = true };
            field.AddToClassList("lt-text-input");
            field.BindProperty(text);
            card.Add(LtUI.El("lt-editor", field));

            SerializedProperty typewriter = serializedObject.FindProperty("Typewriter");
            card.Add(LtUI.Row("Typewriter", LtUI.Switch(typewriter), typewriter.tooltip));
            VisualElement unit = LtUI.Row("Unit", LtUI.Segments(serializedObject.FindProperty("Unit"), "Letter", "Word", "Line", "Paragraph"), "What the typewriter shows at a time.");
            card.Add(unit);
            void ShowUnit(SerializedProperty p) => unit.style.display = p.boolValue && !p.hasMultipleDifferentValues ? DisplayStyle.Flex : DisplayStyle.None;
            ShowUnit(typewriter);
            card.TrackPropertyValue(typewriter, ShowUnit);
        }
    }

    [CustomEditor(typeof(LetteraSequenceShot)), CanEditMultipleObjects]
    sealed class LetteraSequenceShotInspector : LetteraShotInspector
    {
        protected override string Title => "Sequence Clip";
        protected override string Subtitle => "Takes the bound sequencer from one place to another over the clip";
        protected override string Describe(Object shot) => ((LetteraSequenceShot)shot).Describe();

        protected override void Fill(VisualElement card)
        {
            card.Add(LtUI.Row("From", LtUI.SizeSlider(serializedObject.FindProperty("From"), 0, 1), "Where it stands as the clip begins, as a share of the sequencer's length."));
            card.Add(LtUI.Row("To", LtUI.SizeSlider(serializedObject.FindProperty("To"), 0, 1), "Where it stands as the clip ends."));
        }
    }

    [CustomEditor(typeof(LetteraParamShot)), CanEditMultipleObjects]
    sealed class LetteraParamShotInspector : LetteraShotInspector
    {
        protected override string Title => "Parameter Clip";
        protected override string Subtitle => "Takes the bound link's value along a curve over the clip";
        protected override string Describe(Object shot) => ((LetteraParamShot)shot).Describe();

        protected override void Fill(VisualElement card)
        {
            card.Add(new LtNumber("From", serializedObject.FindProperty("From"), "The value as the clip begins."));
            card.Add(new LtNumber("To", serializedObject.FindProperty("To"), "The value as the clip ends."));
            var curve = new CurveField();
            curve.BindProperty(serializedObject.FindProperty("Curve"));
            card.Add(LtUI.Row("Curve", curve, "How it goes from one to the other."));
        }
    }

    // ------------------------------------------------------------- track inspectors

    /// <summary>A track's page: what it is bound to, and the objects it could be. One with a text but no component says it will get one.</summary>
    abstract class LetteraTrackInspector : UnityEditor.Editor
    {
        protected abstract string Title { get; }
        protected abstract string Subtitle { get; }

        public override VisualElement CreateInspectorGUI() => Build(TimelineEditor.inspectedDirector);

        internal VisualElement Build(PlayableDirector director)
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, Title, Subtitle);
            VisualElement card = LtUI.Card("Binding");
            page.Add(card);
            var track = (TrackAsset)target;

            void Refresh()
            {
                card.Clear();
                card.Add(LtUI.Text("BINDING", "lt-card__title"));
                if (director == null)
                {
                    card.Add(LtUI.Text("Open this timeline from a Playable Director to bind it.", "lt-subtitle"));
                    return;
                }
                Object bound = director.GetGenericBinding(track);
                card.Add(LtUI.Row("Bound To", LtUI.Text(bound != null ? bound.name : "Nothing", "lt-row__value")));
                string problem = LetteraTrackBinding.Problem(track, bound);
                if (problem != null) card.Add(new LtFix(problem, () => LetteraTrackBinding.Problem(track, director.GetGenericBinding(track)) != null));

                List<LetteraTrackBinding.Candidate> candidates = LetteraTrackBinding.Candidates(track);
                if (candidates.Count == 0)
                {
                    card.Add(LtUI.Text("No text in the open scenes. Add one, and it shows here.", "lt-subtitle"));
                    return;
                }
                string wanted = LetteraTrackBinding.WantedName(track);
                foreach (LetteraTrackBinding.Candidate candidate in candidates)
                {
                    LetteraTrackBinding.Candidate chosen = candidate;
                    string label = chosen.NeedsAdd ? "+ " + wanted + " on " + chosen.Label : chosen.Label;
                    string tip = chosen.NeedsAdd ? "This text has no " + wanted + ". Binding adds one." : "Bind this track to it.";
                    Label row = LtUI.Clickable(label, "lt-picker__item", tip, () =>
                    {
                        LetteraTrackBinding.Bind(director, track, chosen);
                        TimelineEditor.Refresh(RefreshReason.ContentsModified);
                        Refresh();
                    });
                    row.name = "lt-binding-choice";
                    row.EnableInClassList("lt-seg__item--on", chosen.Target == bound);
                    card.Add(row);
                }
            }

            Refresh();
            return root;
        }
    }

    [CustomEditor(typeof(LetteraSequenceTrack))]
    sealed class LetteraSequenceTrackInspector : LetteraTrackInspector
    {
        protected override string Title => "Sequence Track";
        protected override string Subtitle => "Plays a sequencer on this timeline";
    }

    [CustomEditor(typeof(LetteraParamTrack))]
    sealed class LetteraParamTrackInspector : LetteraTrackInspector
    {
        protected override string Title => "Parameter Track";
        protected override string Subtitle => "Takes one linked parameter along this timeline";
    }

    [CustomEditor(typeof(LetteraTextTrack))]
    sealed class LetteraTextTrackInspector : LetteraTrackInspector
    {
        protected override string Title => "Text Track";
        protected override string Subtitle => "The lines of a cut scene: each clip says what the bound text shows";
    }
}
