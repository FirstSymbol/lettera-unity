using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // The face's own spacing, in its units, for a face that sets too tight or too loose.
        protected VisualElement SpacingCard(LetteraFontAsset font)
        {
            FontFace face = font.LoadFace();
            string units = face != null ? "Of " + face.Metrics.Upem + " units per em." : "In font units.";
            return LtUI.Card("Spacing",
                LtUI.Field("Spacing Offset", serializedObject.FindProperty("m_SpacingOffset"), "Font units added to every glyph's advance. Negative tightens. Joined scripts such as Arabic keep theirs. " + units),
                LtUI.Field("Space Width", serializedObject.FindProperty("m_SpaceWidth"), "The advance of a space and a no-break space. 0 keeps the font's own" + (face != null && face.HasGlyph(' ') ? ", which is " + SpaceOf(face) + "." : ".") + " " + units));
        }

        static unsafe int SpaceOf(FontFace face)
        {
            uint glyph = LetteraNative.lt_font_glyph(face.Regular.Font, ' ', 0);
            int text = ' ';
            NativeGlyph* output = stackalloc NativeGlyph[4];
            int count = LetteraNative.lt_shape(face.Regular.Font, &text, 1, 0, 1, 4, LetteraNative.Tag("Latn"), null, null, 0, output, 4);
            return count > 0 && glyph != 0 ? output[0].XAdvance : 0;
        }

        // Glyphs set apart by character: a row each, and a character to add one for.
        protected VisualElement GlyphOverridesCard(LetteraFontAsset font)
        {
            SerializedProperty list = serializedObject.FindProperty("m_GlyphOverrides");
            var input = new TextField { value = "" };
            input.AddToClassList("lt-text-input");
            var rows = new LtList("Glyph Overrides", list, OverrideRow, "No glyph is set apart. Type a character below and add it.", added =>
            {
                string text = string.IsNullOrEmpty(input.value) ? "A" : input.value;
                int codepoint = char.ConvertToUtf32(text, 0);
                serializedObject.Update();
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Codepoint").intValue == codepoint)
                        return;
                list.arraySize++;
                SerializedProperty row = list.GetArrayElementAtIndex(list.arraySize - 1);
                row.FindPropertyRelative("Codepoint").intValue = codepoint;
                row.FindPropertyRelative("Advance").intValue = 0;
                row.FindPropertyRelative("Offset").vector2IntValue = Vector2Int.zero;
                row.FindPropertyRelative("Scale").floatValue = 1;
                row.FindPropertyRelative("PixelsPerEm").intValue = 0;
                serializedObject.ApplyModifiedProperties();
                added.Rebuild();
            });
            return LtUI.Card("", rows, LtUI.Row("Character", input, "The character + Add sets apart. Its glyph in this font is the one changed."));
        }

        static VisualElement OverrideRow(SerializedProperty element, int index)
        {
            int codepoint = element.FindPropertyRelative("Codepoint").intValue;
            string shown = codepoint > 0 && codepoint <= 0x10FFFF && (codepoint < 0xD800 || codepoint > 0xDFFF) ? char.ConvertFromUtf32(codepoint) : "?";
            SerializedProperty offset = element.FindPropertyRelative("Offset");
            var x = new LtNumber("X", offset.FindPropertyRelative("x"));
            var y = new LtNumber("Y", offset.FindPropertyRelative("y"));
            x.AddToClassList("lt-vector__axis");
            y.AddToClassList("lt-vector__axis");
            return LtUI.El("lt-list",
                LtUI.Text(shown + "   U+" + codepoint.ToString("X4"), "lt-row__label"),
                LtUI.Field("Advance", element.FindPropertyRelative("Advance"), "Font units added to its advance."),
                LtUI.Row("Offset", LtUI.El("lt-vector", x, y), "Font units it is drawn moved by, right and up. Its advance stays."),
                LtUI.Field("Scale", element.FindPropertyRelative("Scale"), "The size it is drawn at, from its origin. 1 is as designed."),
                LtUI.Field("Pixels Per Em", element.FindPropertyRelative("PixelsPerEm"), "Atlas pixels per em it is rasterized at. 0 takes what the text asks."));
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

        // The file's own lines, or the ones written here. Reset goes back to the file.
        protected VisualElement MetricsCard(LetteraFontAsset font)
        {
            SerializedProperty on = serializedObject.FindProperty("m_OverrideMetrics");
            FontFace face = font.LoadFace();
            VisualElement values = LtUI.El("lt-list",
                LtUI.Field("Ascender", serializedObject.FindProperty("m_MetricAscender"), "Font units above the baseline."),
                LtUI.Field("Descender", serializedObject.FindProperty("m_MetricDescender"), "Font units below it, negative."),
                LtUI.Field("Line Gap", serializedObject.FindProperty("m_MetricLineGap"), "Font units between lines."));
            LetteraTextInspector.ShowWhen(values, on, p => p.boolValue);
            return LtUI.Card("Metrics",
                new LtStatus().Line("From The File", () => face == null ? "Not read" : face.NaturalMetrics.Ascender + " up, " + face.NaturalMetrics.Descender + " down, " + face.NaturalMetrics.LineGap + " gap, of " + face.NaturalMetrics.Upem).RefreshNow(),
                LtUI.Field("Set By Hand", on, "Writes this font's vertical lines, for a font whose own are wrong. Texts using it lay out again."),
                values,
                LtUI.Clickable("Reset", "lt-add lt-add--small", "Takes the file's own values, and turns this off.", () =>
                {
                    serializedObject.Update();
                    on.boolValue = false;
                    if (face != null)
                    {
                        serializedObject.FindProperty("m_MetricAscender").intValue = face.NaturalMetrics.Ascender;
                        serializedObject.FindProperty("m_MetricDescender").intValue = face.NaturalMetrics.Descender;
                        serializedObject.FindProperty("m_MetricLineGap").intValue = face.NaturalMetrics.LineGap;
                    }
                    serializedObject.ApplyModifiedProperties();
                }));
        }

        protected static VisualElement AxesCard(LetteraFontAsset font)
        {
            FontFace face = font.LoadFace();
            if (face == null || !face.IsVariable) return LtUI.Card("Variable Axes", LtUI.Text("This font has no axes.", "lt-hint"));
            var status = new LtStatus();
            foreach (FontFace.AxisInfo axis in face.Axes)
            {
                FontFace.AxisInfo shown = axis;
                status.Line(shown.Tag, () => shown.Min.ToString("0.##") + " to " + shown.Max.ToString("0.##") + ", default " + shown.Default.ToString("0.##"));
            }
            status.Refresh();
            return LtUI.Card("Variable Axes", status, LtUI.Text("A Font Variant pins a point of these, as an asset of its own.", "lt-hint"));
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
            // The copy takes the page's own format, 8 or 16 bits, or the copy is refused.
            var copy = new Texture2D(array.width, array.height, array.graphicsFormat, UnityEngine.Experimental.Rendering.TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
            Graphics.CopyTexture(array, page, 0, copy, 0, 0);
            string depth = set == Set.Sdf ? ", " + GlyphAtlas.SdfDepth + "-bit" : set == Set.Msdf ? ", " + GlyphAtlas.MsdfDepth + "-bit" : "";
            info = $"{set} atlas: {array.depth} of {array.width}x{array.height} pages{depth}, {GlyphAtlas.GlyphCount} glyphs in all";
            return copy;
        }
    }

    [CustomEditor(typeof(LetteraFont), true)]
    internal sealed class LetteraFontEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Font";

        static string Kb(long bytes) => (bytes / 1024).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " KB";

        // Where a build keeps this font's bytes, by what the project's Font Memory card says for each target.
        internal static string WhereBytesLive(LetteraFont font)
        {
            if (font.KeepInline) return "Inside this asset, on every target: Keep Inline is on.";
            var words = new List<string>();
            LetteraFontMemorySettings memory = LetteraSettings.Instance.FontMemory;
            foreach ((string name, BuildTarget target) in new[] { ("Desktop", BuildTarget.StandaloneWindows64), ("Android", BuildTarget.Android), ("iOS", BuildTarget.iOS), ("Web", BuildTarget.WebGL) })
                words.Add(name + ": " + (memory.Packaging(target) == LetteraFontPackaging.Packed ? "packed file, mapped" : "inside the asset"));
            return string.Join(". ", words) + ".";
        }

        internal static VisualElement StatusCard(LetteraFont font)
        {
            var status = new LtStatus()
                .Line("Loaded", () => font.LoadedFace == null ? "Not yet" : font.LoadedFace.Source == LetteraFontSource.CacheMapped ? "Mapped from the cache" : font.LoadedFace.Source == LetteraFontSource.SystemMapped ? "Mapped from the system" : "In memory", () => font.LoadFailed ? LtStatus.State.Bad : font.LoadedFace != null ? LtStatus.State.Good : LtStatus.State.None)
                .Line("Held", () => font.LoadedFace != null ? Kb(font.LoadedFace.RetainedBytes) + ", " + font.LoadedFace.InstanceCount + " instance(s)" : "Nothing")
                .Line("Stored", () => font.OriginalSize > 0 ? Kb(font.CompressedSize) + " of " + Kb(font.OriginalSize) + ", " + Mathf.RoundToInt(100f * font.CompressedSize / font.OriginalSize) + "%" : Kb(font.CompressedSize))
                .Line("In A Build", () => WhereBytesLive(font))
                .Line("All Fonts", () =>
                {
                    LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
                    return sums.Faces + " faces, " + Kb(sums.RetainedBytes) + " held, " + Kb(sums.MappedBytes) + " mapped";
                });
            status.Refresh();
            return status;
        }

        static VisualElement FacePage(LetteraFontAsset font)
        {
            FontFace face = font.LoadFace();
            if (face == null) return LtUI.Text("This font could not be read.", "lt-note");
            FaceMetrics m = face.NaturalMetrics;
            LtStatus facts = new LtStatus()
                .Line("Family", () => face.Family)
                .Line("Style", () => face.StyleName)
                .Line("Weight", () => face.Weight.ToString())
                .Line("Italic", () => face.IsItalic ? "Yes" : "No")
                .Line("Units Per Em", () => m.Upem.ToString())
                .Line("Ascender", () => m.Ascender.ToString())
                .Line("Descender", () => m.Descender.ToString())
                .Line("Line Gap", () => m.LineGap.ToString())
                .Line("Cap Height", () => m.CapHeight.ToString())
                .Line("X Height", () => m.XHeight.ToString())
                .Line("Glyphs", () => LetteraNative.lt_face_glyph_count(face.Face).ToString());
            facts.Refresh();
            return facts;
        }

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
            root.Add(LtUI.Card("Status",
                StatusCard(font),
                new LtFold("About This Face", () => FacePage(font)),
                LtUI.Clickable("Clear", "lt-add lt-add--small", "Lets go of this font's loaded data until a text next asks for it.", () =>
                {
                    font.ReleaseFace();
                    root.Clear();
                    Rebuild(root);
                })));
            root.Add(LtUI.Card("Source",
                Field("Face Index", "m_FaceIndex", "The face inside a .ttc or .otc file."),
                Field("Keep Inline", "m_KeepInline", "Keeps this font inside its asset in every build, whatever the project's packaging says.")));
            root.Add(FontCommon());
            root.Add(SpacingCard(font));
            root.Add(PixelCard(font));
            root.Add(MetricsCard(font));
            root.Add(AxesCard(font));
            root.Add(GlyphOverridesCard(font));
            root.Add(LetteraGlyphGrid.Card(font));
            root.Add(LetteraSubsetCard.Create(font));
#if !LETTERA_NO_MATH
            Label mathFacts = LtUI.Text("", "lt-subtitle");
            mathFacts.style.whiteSpace = WhiteSpace.Normal;
            root.Add(LtUI.Card("Math", LtUI.Clickable("Read The Math Table", "lt-add lt-add--small", "Opens the font and says what it holds for formulas.", () => mathFacts.text = LetteraMathInspector.DescribeFont(font)), mathFacts));
#endif
            root.Add(LetteraFindUses.Card(font));
            root.Add(AtlasPreview());
        }

        void Rebuild(VisualElement root)
        {
            VisualElement info = LtUI.El("lt-header__info", LtUI.Text(Title, "lt-title"), LtUI.Text(target.name, "lt-subtitle"));
            root.Add(LtUI.El("lt-header", LtUI.Text("Lt", "lt-mark"), info));
            Fill(root);
        }
    }

    /// <summary>Makes a smaller font from this one, here in the inspector.</summary>
    internal static class LetteraSubsetCard
    {
        public static VisualElement Create(LetteraFont font)
        {
            if (!LetteraFontSubset.IsAvailable)
                return LtUI.Card("Subset", LtUI.Text("This editor's platform has no copy of the subset tool. Everything else works without it.", "lt-note"));

            var options = new LetteraSubsetOptions();
            var characters = new TextField { multiline = true, value = LetteraFontSubset.Presets["Basic Latin"] };
            characters.style.minHeight = 48;
            characters.RegisterValueChangedCallback(e => options.Characters = e.newValue);
            options.Characters = characters.value;

            var report = LtUI.Text("", "lt-note");
            report.style.display = DisplayStyle.None;
            LetteraSubsetResult last = null;

            Label mode = null;
            mode = LtUI.Clickable("Keep these", "lt-add lt-add--small", "Keep: only these characters stay. Remove: everything except these stays.", () =>
            {
                options.Mode = options.Mode == LetteraSubsetMode.Keep ? LetteraSubsetMode.Remove : LetteraSubsetMode.Keep;
                mode.text = options.Mode == LetteraSubsetMode.Keep ? "Keep these" : "Remove these";
            });

            var presets = LtUI.El("lt-row__value");
            presets.style.flexDirection = FlexDirection.Row;
            presets.style.flexWrap = Wrap.Wrap;
            foreach (KeyValuePair<string, string> preset in LetteraFontSubset.Presets)
            {
                string value = preset.Value;
                Label chip = LtUI.Clickable(preset.Key, "lt-add lt-add--small", "Use " + preset.Key, () => characters.value = value);
                chip.style.marginRight = 8;
                presets.Add(chip);
            }

            Label scan = null;
            scan = LtUI.Clickable("Off", "lt-add lt-add--small", "Also keeps every character of Lettera texts in scenes and prefabs.", () =>
            {
                options.FromProject = !options.FromProject;
                scan.text = options.FromProject ? "On" : "Off";
            });

            void Run()
            {
                last = LetteraFontSubset.Run(font, options);
                report.style.display = DisplayStyle.Flex;
                if (!last.Succeeded)
                {
                    report.text = last.Refused;
                    return;
                }
                report.text = $"{last.BytesBefore / 1024} KB to {last.BytesAfter / 1024} KB, {last.GlyphsBefore} to {last.GlyphsAfter} glyphs."
                              + (last.Warnings.Count > 0 ? " " + string.Join(" ", last.Warnings) : "");
            }

            Label preview = LtUI.Clickable("Try it", "lt-add lt-add--small", "Makes the subset and says what came of it. Nothing changes yet.", Run);
            Label saveAs = LtUI.Clickable("Save As New Asset", "lt-add lt-add--small", "Stores the subset beside this font, and leaves this one alone.", () =>
            {
                Run();
                if (!last.Succeeded) return;
                string path = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(font)).Replace('\\', '/') + "/" + font.name + " Subset.asset";
                EditorGUIUtility.PingObject(LetteraFontSubset.SaveAs(font, last.Bytes, path));
            });
            Label replace = LtUI.Clickable("Replace This Font", "lt-add lt-add--small", "Replaces this font's data. Undo brings it back; after that only reimporting its file can.", () =>
            {
                Run();
                if (!last.Succeeded) return;
                if (EditorUtility.DisplayDialog("Replace this font?", "The original file is not kept in the asset. Undo brings it back for now; after that, reimport it from its file.", "Replace", "Cancel"))
                    LetteraFontSubset.Replace(font, last.Bytes);
            });

            return LtUI.Card("Subset",
                LtUI.Row("Mode", mode),
                LtUI.Row("Presets", presets),
                LtUI.Row("Characters", characters, "Text, or ranges such as U+0400-04FF."),
                LtUI.Row("From Project", scan),
                LtUI.Row("", preview),
                LtUI.Row("", saveAs),
                LtUI.Row("", replace),
                report);
        }
    }

    [CustomEditor(typeof(LetteraFontVariant))]
    internal sealed class LetteraFontVariantEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Font Variant";

        protected override void Fill(VisualElement root)
        {
            var variant = (LetteraFontVariant)target;
            if (variant.HasError) root.Add(new LtFix("This variant has no readable source font, so texts fall back past it.", () => variant.HasError));
            root.Add(LtUI.Card("Source", Field("Source", "m_Source"), Field("Face", "m_Face")));
            SerializedProperty axes = serializedObject.FindProperty("m_Axes");
            root.Add(LtUI.Card("", new LtList("Design Space", axes, (axis, index) => AxisRow(variant, axis), "No axes pinned: the font's defaults hold.", list =>
            {
                serializedObject.Update();
                axes.arraySize++;
                SerializedProperty added = axes.GetArrayElementAtIndex(axes.arraySize - 1);
                FontFace face = variant.LoadFace();
                IReadOnlyList<FontFace.AxisInfo> known = face != null ? face.Axes : new FontFace.AxisInfo[0];
                var taken = new HashSet<string>();
                for (int i = 0; i < axes.arraySize - 1; i++) taken.Add(axes.GetArrayElementAtIndex(i).FindPropertyRelative("Tag").stringValue);
                foreach (FontFace.AxisInfo info in known)
                {
                    if (taken.Contains(info.Tag)) continue;
                    added.FindPropertyRelative("Tag").stringValue = info.Tag;
                    added.FindPropertyRelative("Value").floatValue = info.Default;
                    break;
                }
                serializedObject.ApplyModifiedProperties();
                list.Rebuild();
            })));
            root.Add(LtUI.Card("Reported As", Field("Family", "m_Family"), Field("Weight", "m_Weight"), Field("Italic", "m_Italic")));
            root.Add(FontCommon());
            root.Add(SpacingCard(variant));
            root.Add(PixelCard(variant));
            root.Add(MetricsCard(variant));
            root.Add(GlyphOverridesCard(variant));
            root.Add(LetteraGlyphGrid.Card(variant));
            root.Add(LetteraFindUses.Card(variant));
        }

        // A tag, and the value on a slider that knows the axis's own range when the font does.
        VisualElement AxisRow(LetteraFontVariant variant, SerializedProperty axis)
        {
            SerializedProperty tag = axis.FindPropertyRelative("Tag").Copy();
            SerializedProperty value = axis.FindPropertyRelative("Value").Copy();
            VisualElement line = LtUI.El("lt-axis");
            FontFace face = variant.Source != null ? variant.Source.LoadFace() : null;
            FontFace.AxisInfo known = face != null ? face.Axes.FirstOrDefault(a => a.Tag == tag.stringValue) : default;
            line.Add(LtUI.Field("Tag", tag, "The axis tag, such as wght, wdth or GRAD."));
            line.Add(LtUI.Field("Value", value));
            if (!string.IsNullOrEmpty(known.Tag))
            {
                var slider = new Slider(known.Min, known.Max);
                slider.AddToClassList("lt-grow");
                slider.BindProperty(value);
                line.Add(LtUI.Row(known.Min.ToString("0.##") + " to " + known.Max.ToString("0.##") + ", default " + known.Default.ToString("0.##"), slider));
            }
            return line;
        }
    }

    [CustomEditor(typeof(LetteraFontStack))]
    internal sealed class LetteraFontStackEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Font Stack";

        protected override void Fill(VisualElement root)
        {
            root.Add(LtUI.Card("", new LtList("Families", serializedObject.FindProperty("m_Families"), FamilyRow, "No families yet. Drag fonts into a new row, or create a stack from selected fonts in the Project window.")));
            root.Add(LtUI.Card("After These", Field("Fallback Stack", "m_FallbackStack")));
            root.Add(LetteraStackFacts.TestLine((LetteraFontStack)target));
            root.Add(LetteraFindUses.Card(target));
        }

        static VisualElement FamilyRow(SerializedProperty family, int index)
        {
            SerializedProperty primary = family.FindPropertyRelative("m_Primary").Copy();
            string name = family.FindPropertyRelative("m_Name").stringValue;
            var font = primary.objectReferenceValue as LetteraFontAsset;
            string title = !string.IsNullOrEmpty(name) ? name : font != null ? font.name : "Family " + (index + 1);
            string language = family.FindPropertyRelative("m_PreferredLanguage").stringValue;
            string coverage = LetteraStackFacts.Coverage(font);
            VisualElement row = LtUI.El("lt-family");
            row.Add(LtUI.El("lt-family__head", LtUI.Text(title, "lt-row__label lt-family__name"), LtUI.Text(string.IsNullOrEmpty(language) ? coverage : language + ". " + coverage, "lt-hint lt-family__facts")));
            row.Add(LtUI.Field("Primary", primary));
            row.Add(new LtFold("Name, Faces And Language", () => LtUI.El("lt-list",
                LtUI.Field("Name", family.FindPropertyRelative("m_Name")),
                LtUI.Field("Faces", family.FindPropertyRelative("m_Faces"), "Bold, italic and other faces of this family."),
                LtUI.Field("Language", family.FindPropertyRelative("m_PreferredLanguage"), "A BCP 47 tag this family is chosen for, such as ja."))));
            return row;
        }
    }

    [CustomEditor(typeof(LetteraSystemFont))]
    internal sealed class LetteraSystemFontEditor : LtAssetEditor
    {
        protected override string Title => "Lettera System Font";
        static readonly (string name, string field)[] s_Platforms = { ("Common", "m_Common"), ("Windows", "m_Windows"), ("macOS", "m_MacOS"), ("Linux", "m_Linux"), ("iOS", "m_IOS"), ("Android", "m_Android") };
        const string TabKey = "Lettera.Inspector.SystemFontTab";

        protected override void Fill(VisualElement root)
        {
            var font = (LetteraSystemFont)target;
            VisualElement tabs = LtUI.El("lt-tabs");
            VisualElement page = LtUI.El("lt-list");
            var items = new List<VisualElement>();
            void Show(int index)
            {
                LtPrefs.SetFloat(TabKey, index);
                for (int i = 0; i < items.Count; i++) items[i].EnableInClassList("lt-tab--active", i == index);
                page.Clear();
                (string name, string field) = s_Platforms[index];
                SerializedProperty names = serializedObject.FindProperty(field);
                if (index > 0)
                {
                    page.Add(LtUI.Row("Own List For " + name, LtUI.El("lt-switch-holder", LtUI.Toggle(() => serializedObject.FindProperty(field).arraySize > 0, on =>
                    {
                        serializedObject.Update();
                        SerializedProperty list = serializedObject.FindProperty(field);
                        list.arraySize = on ? 1 : 0;
                        if (on) list.GetArrayElementAtIndex(0).stringValue = "";
                        serializedObject.ApplyModifiedProperties();
                        Show(index);
                    })), "On: this platform tries its own names first, then Common."));
                }
                if (index == 0 || names.arraySize > 0) page.Add(new LtList("Names To Try", names, (element, row) => LtUI.Field("", element), "No names here."));
            }
            for (int i = 0; i < s_Platforms.Length; i++)
            {
                int index = i;
                Label tab = LtUI.Text(s_Platforms[i].name, "lt-tab__label");
                VisualElement item = LtUI.El("lt-tab", tab);
                item.RegisterCallback<ClickEvent>(_ => Show(index));
                items.Add(item);
                tabs.Add(item);
            }
            LtStatus status = new LtStatus()
                .Line("Answers With", () => font.ResolveFailed ? "No name here matches: the system's default face" : !string.IsNullOrEmpty(font.ResolvedFamily) ? font.ResolvedFamily : "Not looked up yet", () => font.ResolveFailed ? LtStatus.State.Warn : !string.IsNullOrEmpty(font.ResolvedFamily) ? LtStatus.State.Good : LtStatus.State.None)
                .Line("Face", () => font.LoadFace() == null ? "None" : font.LoadFace().Family + (string.IsNullOrEmpty(font.LoadFace().StyleName) ? "" : ", " + font.LoadFace().StyleName))
                .Line("Size", () => font.LoadFace() == null ? "" : (font.LoadFace().MappedBytes + font.LoadFace().RetainedBytes) / 1024 + " KB");
            status.Refresh();
            root.Add(LtUI.Card("Status", status, LtUI.Clickable("Re-Resolve", "lt-add lt-add--small", "Looks the names up again.", () =>
            {
                font.Common = font.Common;
                font.LoadFace();
                status.Refresh();
            })));
            root.Add(LtUI.Card("By Platform", tabs, page));
            Show(Mathf.Clamp((int)LtPrefs.GetFloat(TabKey, 0), 0, s_Platforms.Length - 1));
            root.Add(FontCommon());
            root.Add(LetteraFindUses.Card(font));
        }
    }

    [CustomEditor(typeof(LetteraColorFont))]
    internal sealed class LetteraColorFontEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Color Font";

        protected override void Fill(VisualElement root)
        {
            VisualElement card = LtUI.Card("Kinds");
            FontFace colorFace = target is LetteraFont stored && stored.HasData ? stored.LoadFace() : null;
            if (colorFace != null)
            {
                int flags = LetteraNative.lt_face_color_flags(colorFace.Face);
                var kinds = new List<string>();
                if ((flags & 2) != 0) kinds.Add(LetteraNative.Version >= 2 ? "COLRv1" : "COLRv1 (restart the editor to draw these)");
                if ((flags & 1) != 0) kinds.Add("COLRv0");
                if ((flags & 4) != 0) kinds.Add("Bitmaps");
                if ((flags & 8) != 0) kinds.Add("SVG (not drawn)");
                card.Add(new LtStatus().Line("Color Kinds", () => kinds.Count > 0 ? string.Join(", ", kinds) : "None", () => kinds.Count > 0 ? LtStatus.State.Good : LtStatus.State.Warn).RefreshNow());
            }
            else
            {
                card.Add(LtUI.Text("This asset holds no font yet.", "lt-hint"));
            }
            root.Add(card);
            VisualElement fields = LtUI.Card("Font");
            foreach (SerializedProperty child in TopLevel())
            {
                if (child.name == "m_Script") continue;
                fields.Add(LetteraPaintField.For(child, target));
            }
            root.Add(fields);
            root.Add(LetteraFindUses.Card(target));
        }
    }

    [CustomEditor(typeof(LetteraStylePreset))]
    internal sealed class LetteraStylePresetEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Style Preset";

        protected override void Fill(VisualElement root)
        {
            root.Add(new LetteraStyleList(serializedObject, "m_Styles").Build());
            root.Add(LetteraFindUses.Card(target));
        }
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
            root.Add(LetteraFindUses.Card(target));
        }
    }

    [CustomEditor(typeof(LetteraPaintLibrary))]
    internal sealed class LetteraPaintLibraryEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Paint Library";

        protected override void Fill(VisualElement root)
        {
            SerializedProperty swatches = serializedObject.FindProperty("m_Swatches");
            root.Add(LtUI.Card("", new LtList("Swatches", swatches, (swatch, index) => LtUI.El("lt-row lt-row--pair",
                LtUI.Field("", swatch.FindPropertyRelative("Name")),
                new LtPaint("", swatch.FindPropertyRelative("Paint"))), "No paints yet. An effect can then use only its own.", list =>
            {
                serializedObject.Update();
                swatches.InsertArrayElementAtIndex(swatches.arraySize);
                swatches.GetArrayElementAtIndex(swatches.arraySize - 1).FindPropertyRelative("Name").stringValue = "New Paint";
                serializedObject.ApplyModifiedProperties();
                list.Rebuild();
            }, "+ Add Swatch")));
            root.Add(LetteraFindUses.Card(target));
        }
    }

    [CustomEditor(typeof(LetteraSettings))]
    internal sealed class LetteraSettingsEditor : LtAssetEditor
    {
        protected override string Title => "Lettera Settings";

        protected override void Fill(VisualElement root) => root.Add(LtUI.Card("",
            LtUI.Text("Everything here is edited in one place, in Project Settings.", "lt-hint"),
            LtUI.Clickable("Open Project Settings", "lt-add lt-add--small", "Goes to Project Settings > Lettera.", () => SettingsService.OpenProjectSettings("Project/Lettera"))));
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
        internal static readonly Color PaddingColor = new Color(0.45f, 0.75f, 1f, 0.8f), RaycastColor = new Color(0.3f, 1f, 0.5f, 0.8f), InkColor = new Color(0.61f, 0.43f, 1f, 0.8f);

        /// <summary>The area that takes pointers, in a colour of its own.</summary>
        public static void DrawRaycast(Component text, Vector4 padding)
        {
            var rect = text.transform as RectTransform;
            if (rect == null || !LetteraSceneOptions.Outlines) return;
            Matrix4x4 saved = Handles.matrix;
            Handles.matrix = rect.localToWorldMatrix;
            Handles.DrawSolidRectangleWithOutline(Inside(rect.rect, padding), Color.clear, RaycastColor);
            Handles.matrix = saved;
        }

        /// <summary>Where the letters really are, in the text's own space. Empty before it has drawn anything.</summary>
        internal static Rect InkRect(TextEngine engine)
        {
            if (engine == null || engine.VertexCount == 0) return Rect.zero;
            Bounds bounds = engine.MeshBounds;
            return new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
        }

        /// <summary>What the mesh covers, in a colour of its own.</summary>
        public static void DrawInk(Component text, TextEngine engine)
        {
            Rect ink = InkRect(engine);
            if (ink.width <= 0 || !LetteraSceneOptions.Outlines) return;
            Matrix4x4 saved = Handles.matrix;
            Handles.matrix = text.transform.localToWorldMatrix;
            Handles.DrawSolidRectangleWithOutline(ink, Color.clear, InkColor);
            Handles.matrix = saved;
        }

        internal static Rect Inside(Rect box, Vector4 padding) => Rect.MinMaxRect(box.xMin + padding.x, box.yMin + padding.y, box.xMax - padding.z, box.yMax - padding.w);

        /// <summary>A line in the Scene view beside a text.</summary>
        public static void Warn(Component text, string words)
        {
            var rect = text.transform as RectTransform;
            Vector3 at = rect != null ? rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMax, 0)) : text.transform.position;
            var style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = new Color(0.96f, 0.65f, 0.14f);
            Handles.Label(at, words, style);
        }

        public static void Draw(Component text, Vector4 padding, IReadOnlyList<LetteraStyle> styles, float fontSize)
        {
            var rect = text.transform as RectTransform;
            if (rect == null) return;
            Matrix4x4 saved = Handles.matrix;
            Handles.matrix = rect.localToWorldMatrix;
            if (padding != Vector4.zero && LetteraSceneOptions.Outlines)
                Handles.DrawSolidRectangleWithOutline(Inside(rect.rect, padding), Color.clear, PaddingColor);
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
