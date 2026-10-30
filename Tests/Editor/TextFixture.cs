using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>Creates world texts in edit mode with the Noto fonts of Tests/Fonts~, so results do not depend on the machine's fonts.</summary>
    public abstract class TextFixture
    {
        static readonly Dictionary<string, LetteraFont> s_Fonts = new Dictionary<string, LetteraFont>();
        readonly List<Object> m_Created = new List<Object>();
        bool m_SavedFallback;

        protected static LetteraFont Font(string file)
        {
            if (s_Fonts.TryGetValue(file, out LetteraFont font) && font != null) return font;
            string asmdef = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("FirstSymbol.Lettera.Editor.Tests t:asmdef")[0]);
            string path = Path.Combine(Path.GetDirectoryName(asmdef), "..", "Fonts~", file);
            font = LetteraFont.CreateFromBytes(File.ReadAllBytes(path));
            s_Fonts[file] = font;
            return font;
        }

        protected static LetteraFont Latin => Font("NotoSans-Regular.ttf");
        protected static LetteraFont Arabic => Font("NotoSansArabic-Regular.ttf");
        protected static LetteraFont Hebrew => Font("NotoSansHebrew-Regular.ttf");
        protected static LetteraFont Devanagari => Font("NotoSansDevanagari-Regular.ttf");

        [SetUp]
        public void BaseSetUp()
        {
            m_SavedFallback = LetteraSettings.Instance.DisableSystemFontFallback;
            LetteraSettings.Instance.DisableSystemFontFallback = true;
        }

        [TearDown]
        public void BaseTearDown()
        {
            foreach (Object created in m_Created)
                if (created != null)
                    Object.DestroyImmediate(created);
            m_Created.Clear();
            LetteraSettings.Instance.DisableSystemFontFallback = m_SavedFallback;
        }

        protected LetteraFontStack Stack(params LetteraFontAsset[] fonts)
        {
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            m_Created.Add(stack);
            foreach (LetteraFontAsset font in fonts) stack.Families.Add(new LetteraFontFamily { Primary = font });
            return stack;
        }

        /// <summary>A world text with its rect's center at the origin.</summary>
        protected LetteraWorldText Make(string text, float width = 1000, float height = 500, LetteraFontAsset font = null, float size = 20)
        {
            var go = new GameObject("Test Text", typeof(RectTransform));
            m_Created.Add(go);
            var label = go.AddComponent<LetteraWorldText>();
            ((RectTransform)go.transform).sizeDelta = new Vector2(width, height);
            label.Font = font != null ? font : Latin;
            label.FontSize = size;
            label.Text = text;
            label.ForceRebuild();
            return label;
        }

        protected static List<uint> Glyphs(LetteraWorldText text)
        {
            var glyphs = new List<uint>();
            for (int i = 0; i < text.GlyphCount; i++) glyphs.Add(text.GetGlyph(i).Glyph);
            return glyphs;
        }

        protected static float WidthOf(LetteraWorldText text)
        {
            float width = 0;
            foreach (LetteraLine line in text.Lines) width = Mathf.Max(width, line.Width);
            return width;
        }
    }
}
