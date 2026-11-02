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
        protected List<Object> Created => m_Created;
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

        /// <summary>Renders world text in edit mode, one local unit to a pixel, centered on the origin, over a background.</summary>
        protected Texture2D Render(int width, int height, Color background)
        {
            // A shader variant compiling in the background draws a placeholder, which no test wants to read.
            ShaderUtil.allowAsyncCompilation = false;
            LetteraWorldBatcher.Update();
            GlyphAtlas.Flush();
            var cameraObject = new GameObject("Test Camera");
            m_Created.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = height * 0.5f;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.aspect = (float)width / height;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            // The first draw of a new shader variant can come out empty even with background compiling off.
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            m_Created.Add(result);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            ShaderUtil.allowAsyncCompilation = true;
            return result;
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
