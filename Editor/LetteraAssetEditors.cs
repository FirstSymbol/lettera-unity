using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>The base of asset inspectors: the same look as the text inspector, with cards of fields.</summary>
    internal abstract class LtAssetEditor : UnityEditor.Editor
    {
        protected abstract string Title { get; }
        protected abstract void Fill(VisualElement root);

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement info = LtUI.El("lt-header__info", LtUI.Text(Title, "lt-title"), LtUI.Text(target.name, "lt-subtitle"));
            root.Add(LtUI.El("lt-header", LtUI.Text("Lt", "lt-mark"), info));
            Fill(root);
            return root;
        }

        protected VisualElement Field(string label, string property, string tooltip = null) => LetteraPaintField.For(serializedObject.FindProperty(property), target) is VisualElement element && !string.IsNullOrEmpty(tooltip) ? WithTip(element, tooltip) : LetteraPaintField.For(serializedObject.FindProperty(property), target);

        static VisualElement WithTip(VisualElement element, string tooltip)
        {
            element.tooltip = tooltip;
            return element;
        }

        // Every field an asset shows, one level deep.
        protected IEnumerable<SerializedProperty> TopLevel()
        {
            SerializedProperty child = serializedObject.GetIterator();
            bool enter = true;
            while (child.NextVisible(enter))
            {
                enter = false;
                yield return child.Copy();
            }
        }

        protected VisualElement FontCommon()
        {
            return LtUI.Card("Rendering",
                Field("Scale", "m_FontScale"),
                Field("Normalize", "m_NormalizeSize"),
                Field("Italic Slant", "m_SyntheticItalicSlant"),
                Field("Bold Strength", "m_SyntheticBoldStrength"));
        }

        protected VisualElement PixelCard(LetteraFontAsset font)
        {
            var grid = serializedObject.FindProperty("m_FontPixelsPerEm");
            return LtUI.Card("Pixel Font",
                LtUI.Field("Pixels Per Em", grid.Copy(), "The size of the font's pixel grid; 0 renders it as an ordinary font."),
                Field("Snap", "m_SnapToFontPixels"),
                LtUI.Clickable("Find Pixel Grid", "lt-add lt-add--small", "Look for the grid in the font's outlines", () =>
                {
                    int found = font.FindPixelGrid();
                    Undo.RecordObject(font, "Find Pixel Grid");
                    font.FontPixelsPerEm = found;
                    EditorUtility.SetDirty(font);
                    serializedObject.Update();
                }));
        }

        protected VisualElement AtlasPreview()
        {
            var image = new Image { scaleMode = ScaleMode.ScaleToFit };
            image.style.height = 220;
            image.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
            var info = LtUI.Text("", "lt-hint");

            void Show(GlyphAtlasView.Set set)
            {
                DestroyImmediate(image.image);
                image.image = GlyphAtlasView.CopyPage(set, 0, out string text);
                info.text = text;
            }

            VisualElement tabs = LtUI.El("lt-tabs");
            foreach ((string name, GlyphAtlasView.Set set) in new[] { ("SDF", GlyphAtlasView.Set.Sdf), ("MSDF", GlyphAtlasView.Set.Msdf), ("Color", GlyphAtlasView.Set.Color) })
            {
                GlyphAtlasView.Set chosen = set;
                tabs.Add(LtUI.Clickable(name, "lt-tab lt-tab__label", "Show this atlas", () => Show(chosen)));
            }
            VisualElement card = LtUI.Card("Shared Atlases", tabs, image, info);
            Show(GlyphAtlasView.Set.Sdf);
            return card;
        }
    }

    /// <summary>Reads an atlas page for an inspector.</summary>
    internal static class GlyphAtlasView
    {
        public enum Set { Sdf, Msdf, Color }

        public static Texture2D CopyPage(Set set, int page, out string info)
        {
            Texture2DArray array = GlyphAtlas.TextureOf((int)set);
            if (array == null)
            {
                info = set + " atlas: empty";
                return null;
            }
            var format = set == Set.Sdf ? TextureFormat.R8 : TextureFormat.RGBA32;
            var copy = new Texture2D(array.width, array.height, format, false, set != Set.Color) { hideFlags = HideFlags.HideAndDontSave };
            Graphics.CopyTexture(array, page, 0, copy, 0, 0);
            info = $"{set} atlas: {array.depth} of {array.width}x{array.height} pages, {GlyphAtlas.GlyphCount} glyphs in all";
            return copy;
        }
    }

    [CustomEditor(typeof(LetteraFont), true)]
    internal sealed class LetteraFontEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Font";

        protected override void Fill(VisualElement root)
        {
            var font = (LetteraFont)target;
            if (!font.HasData)
            {
                root.Add(LtUI.Text("This asset holds no font. Select a font file and use Create > Lettera > Font Asset From Selection.", "lt-note"));
                return;
            }
            if (font.LoadFailed)
            {
                root.Add(LtUI.Text("The stored font could not be read.", "lt-note"));
                return;
            }
            root.Add(LtUI.Card("Font",
                LtUI.Row("Family", LtUI.Text(font.FamilyName, "lt-row__value")),
                LtUI.Row("Stored", LtUI.Text($"{font.CompressedSize / 1024} KB of {font.OriginalSize / 1024} KB", "lt-row__value")),
                Field("Face Index", "m_FaceIndex", "The face inside a .ttc or .otc file.")));
            root.Add(FontCommon());
            root.Add(PixelCard(font));
            root.Add(AtlasPreview());
        }
    }

    [CustomEditor(typeof(LetteraFontVariant))]
    internal sealed class LetteraFontVariantEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Font Variant";

        protected override void Fill(VisualElement root)
        {
            var variant = (LetteraFontVariant)target;
            if (variant.HasError) root.Add(LtUI.Text("This variant has no readable source font, so texts fall back past it.", "lt-note"));
            root.Add(LtUI.Card("Source", Field("Source", "m_Source"), Field("Face", "m_Face")));
            root.Add(LtUI.Card("Design Space", LtUI.Field("Axes", serializedObject.FindProperty("m_Axes"))));
            root.Add(LtUI.Card("Reported As", Field("Family", "m_Family"), Field("Weight", "m_Weight"), Field("Italic", "m_Italic")));
            root.Add(FontCommon());
            root.Add(PixelCard(variant));
        }
    }

    [CustomEditor(typeof(LetteraFontStack))]
    internal sealed class LetteraFontStackEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Font Stack";

        protected override void Fill(VisualElement root)
        {
            root.Add(LtUI.Card("Families", new PropertyField(serializedObject.FindProperty("m_Families"), "")));
            root.Add(LtUI.Card("After These", Field("Fallback Stack", "m_FallbackStack")));
            root.Bind(serializedObject);
        }
    }

    [CustomEditor(typeof(LetteraSystemFont))]
    internal sealed class LetteraSystemFontEditor : LtAssetEditor
    {
        protected override string Title => "Lettera System Font";

        protected override void Fill(VisualElement root)
        {
            VisualElement card = LtUI.Card("Names To Try");
            foreach (SerializedProperty child in TopLevel())
            {
                if (child.name == "m_Script") continue;
                card.Add(LtUI.Field(child.displayName, child.Copy()));
            }
            root.Add(card);
        }
    }

    [CustomEditor(typeof(LetteraColorFont))]
    internal sealed class LetteraColorFontEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Color Font";

        protected override void Fill(VisualElement root)
        {
            VisualElement card = LtUI.Card("Font");
            foreach (SerializedProperty child in TopLevel())
            {
                if (child.name == "m_Script") continue;
                card.Add(LetteraPaintField.For(child, target));
            }
            root.Add(card);
        }
    }

    [CustomEditor(typeof(LetteraStylePreset))]
    internal sealed class LetteraStylePresetEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Style Preset";

        protected override void Fill(VisualElement root) => root.Add(new LetteraStyleList(serializedObject, "m_Styles").Build());
    }

    [CustomEditor(typeof(LetteraEffectPreset))]
    internal sealed class LetteraEffectPresetEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Effect Preset";

        protected override void Fill(VisualElement root)
        {
            VisualElement card = LtUI.Card("Effect");
            card.Add(LetteraPaintField.For(serializedObject.FindProperty("m_Root"), target));
            root.Add(card);
        }
    }

    [CustomEditor(typeof(LetteraPaintLibrary))]
    internal sealed class LetteraPaintLibraryEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Paint Library";

        VisualElement m_List;

        protected override void Fill(VisualElement root)
        {
            m_List = new VisualElement();
            root.Add(m_List);
            root.Add(LtUI.Clickable("+ Add Swatch", "lt-add", "Add a named paint", () =>
            {
                serializedObject.Update();
                SerializedProperty swatches = serializedObject.FindProperty("m_Swatches");
                swatches.InsertArrayElementAtIndex(swatches.arraySize);
                swatches.GetArrayElementAtIndex(swatches.arraySize - 1).FindPropertyRelative("Name").stringValue = "New Paint";
                serializedObject.ApplyModifiedProperties();
                Rebuild();
            }));
            Rebuild();
        }

        void Rebuild()
        {
            m_List.Clear();
            serializedObject.Update();
            SerializedProperty swatches = serializedObject.FindProperty("m_Swatches");
            for (int i = 0; i < swatches.arraySize; i++)
            {
                int index = i;
                SerializedProperty swatch = swatches.GetArrayElementAtIndex(i);
                VisualElement card = LtUI.Card(null, LtUI.Field("Name", swatch.FindPropertyRelative("Name").Copy()), LetteraPaintField.Paint(swatch.FindPropertyRelative("Paint").Copy()));
                card.Add(LtUI.Clickable("× Remove", "lt-add lt-add--small", "Remove this swatch", () =>
                {
                    serializedObject.Update();
                    serializedObject.FindProperty("m_Swatches").DeleteArrayElementAtIndex(index);
                    serializedObject.ApplyModifiedProperties();
                    Rebuild();
                }));
                m_List.Add(card);
            }
        }
    }

    /// <summary>Writes the files of an effect of your own: a shader whose one function you edit, for a Custom Material effect on Canvas and world text.</summary>
    internal static class LetteraCustomEffectTemplate
    {
        const string Template = @"Shader ""Lettera/Custom/#NAME#""
{
    // Draw this through a Custom Material style. The same pass runs on Canvas and world text, with the Built-in and Universal pipelines.
    Properties
    {
        _Tint (""Tint"", Color) = (1,1,1,1)
        [HideInInspector] _StencilComp (""Stencil Comparison"", Float) = 8
        [HideInInspector] _Stencil (""Stencil ID"", Float) = 0
        [HideInInspector] _StencilOp (""Stencil Operation"", Float) = 0
        [HideInInspector] _StencilWriteMask (""Stencil Write Mask"", Float) = 255
        [HideInInspector] _StencilReadMask (""Stencil Read Mask"", Float) = 255
        [HideInInspector] _ColorMask (""Color Mask"", Float) = 15
    }

    SubShader
    {
        Tags { ""Queue""=""Transparent"" ""IgnoreProjector""=""True"" ""RenderType""=""Transparent"" ""PreviewType""=""Plane"" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #define LETTERA_STYLED
            #include ""UnityCG.cginc""
            #include ""#INCLUDE#""

            fixed4 _Tint;

            struct LetteraCustomInput
            {
                float distance;     // 0.5 at the outline, more inside, less outside.
                fixed4 atlasColor;  // A color glyph's own color.
                fixed4 vertexColor; // The color the style asked for.
                float2 position;    // In the text's local units.
                float2 uv;          // In the atlas.
            };

            // Edit this: return a premultiplied color.
            fixed4 LetteraCustom(LetteraCustomInput i)
            {
                float alpha = smoothstep(0.5 - fwidth(i.distance), 0.5 + fwidth(i.distance), i.distance);
                fixed4 color = i.vertexColor * _Tint;
                color.rgb += 0.2 * sin(i.position.x * 0.1);
                color.a *= alpha;
                color.rgb *= color.a;
                return color;
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float2 position : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.uv0;
                o.position = v.vertex.xy;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                LetteraCustomInput input;
                input.distance = LetteraDistance(i.uv0.xy, i.uv0.z, i.uv0.w);
                input.atlasColor = LETTERA_SAMPLE_COLOR(i.uv0.xy, i.uv0.z);
                input.vertexColor = i.color;
                input.position = i.position;
                input.uv = i.uv0.xy;
                return LetteraCustom(input);
            }
            ENDCG
        }
    }
}
";

        /// <summary>The shader text for a name.</summary>
        internal static string Text(string name)
        {
            string include = "LetteraGlyph.cginc";
            foreach (string guid in AssetDatabase.FindAssets("LetteraGlyph"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("LetteraGlyph.cginc", System.StringComparison.Ordinal)) continue;
                include = path;
                break;
            }
            return Template.Replace("#NAME#", name).Replace("#INCLUDE#", include);
        }

        [MenuItem("Assets/Create/Lettera/Custom Effect", false, 120)]
        static void Create()
        {
            string folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            else if (!AssetDatabase.IsValidFolder(folder)) folder = Path.GetDirectoryName(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, "New Lettera Effect.shader").Replace('\\', '/'));
            File.WriteAllText(path, Text(Path.GetFileNameWithoutExtension(path)));
            AssetDatabase.ImportAsset(path);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, Path.ChangeExtension(path, ".mat"));
            Selection.activeObject = shader;
        }
    }

    /// <summary>What the Scene view adds to a selected text: its padding, and the circle of a curved baseline, with a handle for the radius.</summary>
    internal static class LetteraSceneHandles
    {
        public static void Draw(Component text, Vector4 padding, IReadOnlyList<LetteraStyle> styles, float fontSize)
        {
            var rect = text.transform as RectTransform;
            if (rect == null) return;
            Matrix4x4 saved = Handles.matrix;
            Handles.matrix = rect.localToWorldMatrix;
            if (padding != Vector4.zero)
            {
                Rect box = rect.rect;
                var inner = Rect.MinMaxRect(box.xMin + padding.x, box.yMin + padding.y, box.xMax - padding.z, box.yMax - padding.w);
                Handles.color = new Color(0.45f, 0.75f, 1f, 0.8f);
                Handles.DrawSolidRectangleWithOutline(inner, Color.clear, Handles.color);
            }
            foreach (LetteraStyle style in styles)
            {
                if (!(style?.Effect is CurveEffect curve)) continue;
                float radius = curve.Radius;
                Rect box = rect.rect;
                var center = new Vector3(box.center.x, box.center.y - radius, 0);
                Handles.color = new Color(1f, 0.7f, 0.2f, 0.9f);
                EditorGUI.BeginChangeCheck();
                float changed = Handles.RadiusHandle(Quaternion.identity, center, Mathf.Abs(radius));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(text, "Change Curve Radius");
                    curve.Radius = radius < 0 ? -changed : changed;
                    EditorUtility.SetDirty(text);
                }
            }
            Handles.matrix = saved;
        }
    }
}
