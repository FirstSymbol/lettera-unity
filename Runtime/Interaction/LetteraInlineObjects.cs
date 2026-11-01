using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>The tag of an inline item: exactly one U+FFFC in the shown text for each occurrence. A closer is stripped, and "/&gt;" is optional.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class InlineMediaMatcher : LetteraMatcher
    {
        [SerializeField] string m_Name = "sprite";

        public InlineMediaMatcher() { }
        public InlineMediaMatcher(string name) => m_Name = name;

        public string Name { get => m_Name; set => m_Name = value; }

        public override void Find(LetteraMatchContext context)
        {
            if (string.IsNullOrEmpty(m_Name)) return;
            IReadOnlyList<int> positions = context.LessThanPositions;
            ReadOnlySpan<char> source = context.Text;
            for (int p = 0; p < positions.Count; p++)
            {
                int at = positions[p];
                if (!context.IsFree(at, 1)) continue;
                if (TagSyntax.MatchClose(source, at, m_Name, out int closeLength))
                {
                    if (context.IsFree(at, closeLength)) context.Replace(at, closeLength);
                    continue;
                }
                if (!TagSyntax.MatchOpen(source, at, m_Name, out int length, out string[] slots, out string label, out _)) continue;
                if (!context.IsFree(at, length)) continue;
                context.ReplaceAndRange(at, length, "￼", slots, label);
            }
        }
    }

    /// <summary>A change a style makes to one key's metrics. A value left as NaN stays unset.</summary>
    [Serializable]
    public class LetteraInlineOverride
    {
        public string Key = "";
        public float Size = float.NaN, Advance = float.NaN, Lift = float.NaN, Above = float.NaN, Below = float.NaN, Turn = float.NaN;
    }

    /// <summary>
    /// Places a sprite or an object in the text's flow. Slots: name, size, advance, lift, above, below, pivot, turn, tint, aspect. In em where a length.
    /// What a name means comes from the runtime lookup, this list, then the project's library.
    /// </summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class InlineEffect : LetteraEffect
    {
        [SerializeField] List<LetteraInlineEntry> m_Items = new List<LetteraInlineEntry>();
        [SerializeField] LetteraInlineLibrary m_Library;
        [SerializeField] List<LetteraInlineOverride> m_Overrides = new List<LetteraInlineOverride>();

        /// <summary>A lookup for names known only at run time, tried first.</summary>
        public Func<string, LetteraInlineEntry> Lookup;

        readonly HashSet<string> m_Warned = new HashSet<string>();

        public List<LetteraInlineEntry> Items => m_Items;
        public LetteraInlineLibrary Library { get => m_Library; set => m_Library = value; }
        public List<LetteraInlineOverride> Overrides => m_Overrides;

        public override bool ExtendsOnType => false;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots) => attributes.AddInline(this, start, end, slots);

        LetteraInlineEntry Find(string key)
        {
            LetteraInlineEntry found = Lookup?.Invoke(key);
            if (found != null) return found;
            foreach (LetteraInlineEntry entry in m_Items)
                if (entry != null && entry.Key == key)
                    return entry;
            if (m_Library != null && m_Library.TryGet(key, out found)) return found;
            LetteraInlineLibrary shared = LetteraSettings.Instance.Motion.InlineLibrary;
            if (shared != null && shared.TryGet(key, out found)) return found;
            return null;
        }

        static float Unset(float value, float fallback) => float.IsNaN(value) ? fallback : value;

        internal virtual TextEngine.InlineItem ResolveIn(TextEngine engine, in LetteraSlots slots, int cp) => Resolve(slots, cp);

        internal TextEngine.InlineItem Resolve(in LetteraSlots slots, int cp)
        {
            string key = slots[0] ?? "";
            LetteraInlineEntry entry = Find(key);
            var item = new TextEngine.InlineItem { Effect = this, Entry = entry, Cp = cp };
            if (entry == null)
            {
                if (m_Warned.Add(key)) Debug.LogWarning($"Lettera: no inline item is called '{key}'. It keeps its place and shows nothing.");
                return item;
            }

            // Weakest first: the entry, this style's row for the key, then the style default and the tag, which the slots already compose.
            float size = entry.Size, advance = entry.Advance, lift = entry.Lift, above = entry.Above, below = entry.Below, turn = entry.Turn;
            foreach (LetteraInlineOverride row in m_Overrides)
            {
                if (row == null || row.Key != key) continue;
                size = Unset(row.Size, size);
                advance = Unset(row.Advance, advance);
                lift = Unset(row.Lift, lift);
                above = Unset(row.Above, above);
                below = Unset(row.Below, below);
                turn = Unset(row.Turn, turn);
            }
            size = slots.Float(1, size);
            advance = slots.Float(2, advance);
            lift = slots.Float(3, lift);
            above = slots.Float(4, above);
            below = slots.Float(5, below);
            turn = slots.Float(7, turn);
            bool keepAspect = entry.KeepAspect;
            if (LetteraSlots.TryBool(slots[9], out bool asked)) keepAspect = asked;

            float width = size;
            if (entry.Sprite != null && keepAspect && entry.Sprite.rect.height > 0) width = size * entry.Sprite.rect.width / entry.Sprite.rect.height;
            else if (entry.Source != null && entry.Source.transform is RectTransform rect && keepAspect && rect.rect.height > 0) width = size * rect.rect.width / rect.rect.height;

            item.Size = size;
            item.BoxSize = new Vector2(width, size);
            item.Advance = advance < 0 ? width : advance;
            item.Lift = lift;
            item.Above = above < 0 ? Mathf.Max(0, size + lift) : above;
            item.Below = below < 0 ? Mathf.Max(0, -lift) : below;
            item.Turn = turn;
            item.Pivot = entry.Pivot;
            string pivot = slots[6];
            if (!string.IsNullOrEmpty(pivot))
            {
                string[] parts = pivot.Split(' ');
                if (parts.Length == 2 && LetteraSlots.TryNumber(parts[0], out float x) && LetteraSlots.TryNumber(parts[1], out float y)) item.Pivot = new Vector2(x, y);
            }
            string tint = slots[8];
            if (string.IsNullOrEmpty(tint)) tint = entry.Tint;
            if (tint == "i") item.InheritColor = true;
            else if (!string.IsNullOrEmpty(tint) && LetteraSlots.TryColor(tint, out Color32 color))
            {
                item.Tinted = true;
                item.Tint = color;
            }
            return item;
        }
    }

    // The objects of one text. A Graphic must not be enabled inside a canvas rebuild, so all of this runs from the hub, ahead of it.
    internal sealed class LetteraInlineObjects : IMotionClient
    {
        static readonly Dictionary<TextEngine, LetteraInlineObjects> s_All = new Dictionary<TextEngine, LetteraInlineObjects>();

        sealed class Clone
        {
            public LetteraInlineEntry Entry;
            public GameObject Object;
            public RectTransform Rect;
            public int Cp;
            public bool Used;
            // For an adopted object, where it stood.
            public Vector3 HomePosition;
            public bool Adopted;
            public Rect Box;
            public Vector2 Pivot;
            public float Turn;
        }

        readonly TextEngine m_Engine;
        readonly Component m_Owner;
        readonly List<Clone> m_Clones = new List<Clone>();
        readonly HashSet<GameObject> m_AdoptWarned = new HashSet<GameObject>();
        Transform m_Root;
        CanvasGroup m_Group;
        bool m_Pending, m_InHub, m_LastDrawn = true;

        LetteraInlineObjects(TextEngine engine, Component owner)
        {
            m_Engine = engine;
            m_Owner = owner;
            engine.InlinePlaced += OnPlaced;
        }

        internal static void Attach(TextEngine engine, Component owner)
        {
            if (s_All.ContainsKey(engine)) return;
            s_All[engine] = new LetteraInlineObjects(engine, owner);
        }

        internal static void Forget(TextEngine engine)
        {
            if (!s_All.TryGetValue(engine, out LetteraInlineObjects objects)) return;
            s_All.Remove(engine);
            engine.InlinePlaced -= objects.OnPlaced;
            LetteraMotionHub.Remove(objects);
            objects.RestoreAdopted();
        }

        void OnPlaced()
        {
            m_Pending = true;
            Enter();
        }

        void Enter()
        {
            if (m_InHub) return;
            m_InHub = true;
            LetteraMotionHub.Add(this);
        }

        bool IMotionClient.TickMotion(float scaled, float unscaled)
        {
            if (m_Owner == null)
            {
                m_InHub = false;
                return false;
            }
            bool drawn = ((ITextHost)m_Owner).IsDrawn;
            if (drawn != m_LastDrawn)
            {
                m_LastDrawn = drawn;
                if (m_Root != null) m_Root.gameObject.SetActive(drawn);
            }
            if (m_Pending && m_Engine.Dirty < DirtyLevel.Mesh)
            {
                m_Pending = false;
                Sync();
            }
            bool following = m_Engine.PosesObjects && AnyFollowing();
            if (following) Follow();
            m_InHub = m_Pending || following;
            return m_InHub;
        }

        bool AnyFollowing()
        {
            foreach (Clone clone in m_Clones)
                if (clone.Used && clone.Entry.FollowMotion)
                    return true;
            return false;
        }

        Transform Root
        {
            get
            {
                if (m_Root != null) return m_Root;
                var go = new GameObject("Lettera Inline", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
                go.layer = m_Owner.gameObject.layer;
                go.transform.SetParent(m_Owner.transform, false);
                var rect = (RectTransform)go.transform;
                CanvasSelectionVisual.FitInside(rect, (RectTransform)m_Owner.transform);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                if (m_Owner is LetteraText) m_Group = go.AddComponent<CanvasGroup>();
                m_Root = go.transform;
                return m_Root;
            }
        }

        // Makes the clones match the items, keeping those whose occurrence is still there.
        void Sync()
        {
            foreach (Clone clone in m_Clones) clone.Used = false;
            IReadOnlyList<TextEngine.InlineItem> items = m_Engine.InlineItems;
            for (int i = 0; i < items.Count; i++)
            {
                TextEngine.InlineItem item = items[i];
                LetteraInlineEntry entry = item.Entry;
                if (entry == null || entry.Source == null || entry.Sprite != null) continue;
                bool placed = m_Engine.TryInlineBox(item.Cp, out Rect box, out _) && !m_Engine.IsCpRemoved(item.Cp);
                if (!placed) continue;
                Clone clone = entry.Mode == LetteraInlineMode.Adopt ? ForAdopt(entry) : ForSpawn(entry);
                if (clone == null) continue;
                clone.Used = true;
                clone.Cp = item.Cp;
                clone.Box = box;
                clone.Pivot = item.Pivot;
                clone.Turn = item.Turn;
                if (!clone.Adopted && !clone.Object.activeSelf) clone.Object.SetActive(true);
                Place(clone, LetteraGlyphPose.Identity);
            }
            foreach (Clone clone in m_Clones)
            {
                if (clone.Used) continue;
                if (clone.Adopted)
                {
                    if (clone.Object != null) clone.Object.transform.position = clone.HomePosition;
                }
                else if (clone.Object != null && clone.Object.activeSelf) clone.Object.SetActive(false);
            }
            m_Clones.RemoveAll(c => c.Adopted && !c.Used);
            if (m_Engine.PosesObjects) Follow();
        }

        Clone ForSpawn(LetteraInlineEntry entry)
        {
            foreach (Clone clone in m_Clones)
                if (!clone.Used && !clone.Adopted && clone.Entry.Source == entry.Source)
                    return clone;
            GameObject made = UnityEngine.Object.Instantiate(entry.Source, Root);
            made.hideFlags = HideFlags.DontSave;
            var added = new Clone { Entry = entry, Object = made, Rect = made.transform as RectTransform };
            m_Clones.Add(added);
            return added;
        }

        Clone ForAdopt(LetteraInlineEntry entry)
        {
            GameObject target = entry.Source;
            if (target.scene.rootCount == 0 && !target.scene.IsValid())
            {
                Debug.LogError($"Lettera: an adopted inline item must be an object in the scene, and '{target.name}' is an asset.", m_Owner);
                return null;
            }
            if (m_Owner.transform.IsChildOf(target.transform))
            {
                Debug.LogError($"Lettera: '{target.name}' holds the text it would stand in, so it cannot be adopted.", m_Owner);
                return null;
            }
            foreach (Clone clone in m_Clones)
            {
                if (!clone.Adopted || clone.Object != target) continue;
                if (!clone.Used) return clone;
                if (m_AdoptWarned.Add(target)) Debug.LogWarning($"Lettera: '{target.name}' can stand in one place only, so later occurrences show nothing.", m_Owner);
                return null;
            }
            var added = new Clone { Entry = entry, Object = target, Rect = target.transform as RectTransform, Adopted = true, HomePosition = target.transform.position };
            m_Clones.Add(added);
            return added;
        }

        void Place(Clone clone, in LetteraGlyphPose pose)
        {
            Vector2 at = new Vector2(clone.Box.x + clone.Box.width * clone.Pivot.x, clone.Box.y + clone.Box.height * clone.Pivot.y);
            Vector2 moved = new Vector2(pose.A * at.x + pose.C * at.y + pose.Tx, pose.B * at.x + pose.D * at.y + pose.Ty);
            float turn = clone.Turn + Mathf.Atan2(pose.B, pose.A) * Mathf.Rad2Deg;
            float scale = Mathf.Sqrt(pose.A * pose.A + pose.B * pose.B);
            if (clone.Adopted)
            {
                clone.Object.transform.position = m_Owner.transform.TransformPoint(moved);
                return;
            }
            if (clone.Rect != null)
            {
                Vector2 anchor = ((RectTransform)m_Owner.transform).pivot;
                clone.Rect.anchorMin = clone.Rect.anchorMax = anchor;
                clone.Rect.pivot = clone.Pivot;
                clone.Rect.sizeDelta = clone.Box.size;
                clone.Rect.anchoredPosition = moved;
                clone.Rect.localRotation = Quaternion.Euler(0, 0, turn);
                clone.Rect.localScale = new Vector3(scale, scale, 1);
            }
            else
            {
                // A plain prefab: 1 em is the font's size, so one of its units is one em.
                clone.Object.transform.localPosition = moved;
                clone.Object.transform.localRotation = Quaternion.Euler(0, 0, turn);
                float em = clone.Box.height * scale;
                clone.Object.transform.localScale = new Vector3(em, em, em);
            }
        }

        void Follow()
        {
            foreach (Clone clone in m_Clones)
            {
                if (!clone.Used || !clone.Entry.FollowMotion) continue;
                int glyph = m_Engine.FirstGlyphOf(clone.Cp);
                if (glyph < 0) continue;
                LetteraGlyphPose pose = m_Engine.PoseOfGlyph(glyph);
                Place(clone, pose);
                if (m_Group != null && m_Clones.Count == 1) m_Group.alpha = Mathf.Clamp01(pose.Alpha);
            }
        }

        void RestoreAdopted()
        {
            foreach (Clone clone in m_Clones)
                if (clone.Adopted && clone.Object != null)
                    clone.Object.transform.position = clone.HomePosition;
        }

        /// <summary>For tests: what stands in the text now.</summary>
        internal static int ActiveCount(TextEngine engine)
        {
            if (!s_All.TryGetValue(engine, out LetteraInlineObjects objects)) return 0;
            int count = 0;
            foreach (Clone clone in objects.m_Clones)
                if (clone.Used)
                    count++;
            return count;
        }
    }
}
