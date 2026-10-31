using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraInlineMode { Spawn, Adopt }

    /// <summary>One thing that can stand in a text's flow: a sprite, or a prefab or scene object.</summary>
    [Serializable]
    public class LetteraInlineEntry
    {
        public string Key = "";
        public Sprite Sprite;
        [Tooltip("A prefab to spawn, or a scene object to adopt.")]
        public GameObject Source;
        public LetteraInlineMode Mode;
        [Tooltip("What copying and screen readers get in its place.")]
        public string Alt = "";
        [Tooltip("The box's height, in em.")]
        [Min(0)] public float Size = 1;
        [Tooltip("How far the pen moves, in em. Below 0 takes the box's width.")]
        public float Advance = -1;
        [Tooltip("The box's bottom above the baseline, in em.")]
        public float Lift = -0.2f;
        [Tooltip("The room the line keeps above and below the baseline, in em. Below 0 takes it from the box.")]
        public float Above = -1, Below = -1;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);
        public float Turn;
        [Tooltip("Keep the sprite's shape.")]
        public bool KeepAspect = true;
        [Tooltip("Empty keeps the sprite's own color; 'i' takes the text's; or a color.")]
        public string Tint = "";
        [Tooltip("An object moves with its glyph when motion poses it.")]
        public bool FollowMotion = true;
        [Tooltip("Stay on one line with the character before it.")]
        public bool AttachToWord;

        public LetteraInlineEntry Copy() => (LetteraInlineEntry)MemberwiseClone();
    }

    /// <summary>Where names in inline tags are looked up.</summary>
    public interface ILetteraInlineSource
    {
        bool TryGet(string key, out LetteraInlineEntry entry);
    }

    /// <summary>A set of inline items to share between texts.</summary>
    [CreateAssetMenu(fileName = "Inline Library", menuName = "Lettera/Inline Library", order = 95)]
    public class LetteraInlineLibrary : ScriptableObject, ILetteraInlineSource
    {
        [SerializeField] List<LetteraInlineEntry> m_Entries = new List<LetteraInlineEntry>();

        public List<LetteraInlineEntry> Entries => m_Entries;

        public bool TryGet(string key, out LetteraInlineEntry entry)
        {
            foreach (LetteraInlineEntry candidate in m_Entries)
            {
                if (candidate == null || candidate.Key != key) continue;
                entry = candidate;
                return true;
            }
            entry = null;
            return false;
        }
    }
}
