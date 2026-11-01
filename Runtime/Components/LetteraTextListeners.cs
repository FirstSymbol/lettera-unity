using System.Collections.Generic;

namespace FirstSymbol.Lettera
{
    /// <summary>What a text hears from what it depends on.</summary>
    internal interface ITextListener
    {
        void OnFontAssetChanged(LetteraFontAsset asset);
        void OnStackChanged(LetteraFontStack stack);
        void OnPresetChanged(LetteraStylePreset preset);
        void OnEffectPresetChanged(LetteraEffectPreset preset);
        void OnSettingsChanged();
        void OnAtlasCleared();

        /// <summary>Where it stands in the list, -1 outside it.</summary>
        int ListenerSlot { get; set; }
    }

    /// <summary>
    /// One subscription to each change, shared by all texts. A delegate per text would copy a list of every text on each enable and disable,
    /// which at 1000 texts is 25 MB of garbage in a frame and grows with the square of their number.
    /// </summary>
    internal static class LetteraTextListeners
    {
        static readonly List<ITextListener> s_Texts = new List<ITextListener>();
        static bool s_Subscribed;

        internal static int Count => s_Texts.Count;

        public static void Add(ITextListener text)
        {
            if (text.ListenerSlot >= 0) return;
            if (!s_Subscribed) Subscribe();
            text.ListenerSlot = s_Texts.Count;
            s_Texts.Add(text);
        }

        public static void Remove(ITextListener text)
        {
            int slot = text.ListenerSlot;
            if (slot < 0) return;
            int last = s_Texts.Count - 1;
            ITextListener moved = s_Texts[last];
            s_Texts[slot] = moved;
            moved.ListenerSlot = slot;
            s_Texts.RemoveAt(last);
            text.ListenerSlot = -1;
        }

        static void Subscribe()
        {
            s_Subscribed = true;
            LetteraFontAsset.FontAssetChanged += asset => { for (int i = s_Texts.Count - 1; i >= 0; i = Next(i)) s_Texts[i].OnFontAssetChanged(asset); };
            LetteraFontStack.StackChanged += stack => { for (int i = s_Texts.Count - 1; i >= 0; i = Next(i)) s_Texts[i].OnStackChanged(stack); };
            LetteraStylePreset.PresetChanged += preset => { for (int i = s_Texts.Count - 1; i >= 0; i = Next(i)) s_Texts[i].OnPresetChanged(preset); };
            LetteraEffectPreset.PresetChanged += preset => { for (int i = s_Texts.Count - 1; i >= 0; i = Next(i)) s_Texts[i].OnEffectPresetChanged(preset); };
            LetteraSettings.Changed += () => { for (int i = s_Texts.Count - 1; i >= 0; i = Next(i)) s_Texts[i].OnSettingsChanged(); };
            GlyphAtlas.Cleared += () => { for (int i = s_Texts.Count - 1; i >= 0; i = Next(i)) s_Texts[i].OnAtlasCleared(); };
        }

        // From the end, so a text that leaves during the call only moves one already told into its place. Several may leave at once.
        static int Next(int i) => System.Math.Min(i, s_Texts.Count) - 1;
    }
}
