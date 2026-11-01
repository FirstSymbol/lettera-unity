using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#pragma warning disable CS0618 // Find calls with a sort mode exist in every supported Unity version.

namespace FirstSymbol.Lettera.Editor.Cli
{
    /// <summary>
    /// What the "lettera" CLI commands do. It sits apart from the commands themselves so that it compiles, and is tested, without the
    /// pipeline package. Every method answers plain data, an error included, and throws nothing. Edits are undoable and dirty the scene. Nothing saves a scene.
    /// </summary>
    internal static partial class LetteraCli
    {
        /// <summary>Every command, with its arguments and what it is for.</summary>
        internal static readonly (string name, string arguments, string purpose)[] Commands =
        {
            ("help", "", "Commands, JSON conventions, examples"),
            ("types", "", "Effects, matchers and field rules, with parameters and defaults"),
            ("tags", "[--target <t>]", "The vocabulary in force, with parameters"),
            ("list", "", "Every Lettera text in the open scenes: path, id, kind, glyphs"),
            ("get", "--target <t> [--full]", "A text's description; non-default values unless --full"),
            ("create", "--kind text|world|text3d|input|area|password|dropdown [--text] [--name] [--parent] [--size w,h] [--position x,y] [--data patch]", "A new object"),
            ("set", "--target <t> --data <patch>", "Patch: text, font, stack, size, color, alignment, wrap, overflow, padding, lineHeight, direction, language, renderMode and any property of the component"),
            ("style_add", "--target <t> [--effect <type>] [--tag <name>] [--matcher <type>] [--parameter <text>] [--data <effect fields>]", "Adds a style. With no tag and no matcher it covers the whole text"),
            ("style_remove", "--target <t> --index <i>", "Removes a style"),
            ("style_list", "--target <t>", "The text's own styles, by index"),
            ("style_set", "--target <t> --index <i> --data {effect,matcher,tag,enabled,parameter}", "Patches a style"),
            ("preset_save", "--target <t> --path Assets/...asset", "Saves the text's own styles as a preset"),
            ("preset_apply", "--target <t> --path Assets/...asset", "Adds a preset to the text"),
            ("font_create", "--path <font file> [--output Assets/...asset]", "A font asset from a font file"),
            ("font_stack", "--fonts a.asset,b.asset [--path Assets/...asset] [--target <t>]", "A font stack from font assets"),
            ("font_info", "--path <font asset or file>", "Family, style, metrics, glyph count, color, axes"),
            ("font_subset", "--path <font asset> --keep <text or U+0020-007E> [--output Assets/...asset]", "A smaller copy of a font, as a new asset"),
            ("validate", "--target <t> [--platform web|android|ios]", "Problems: unbound tags, broken formulas, missing fonts, bound twice"),
            ("measure", "--target <t> [--width <w>]", "Size, bounds, lines"),
            ("glyphs", "--target <t> | --text <text> [--font <asset>]", "The shaping result, glyph by glyph"),
            ("atlas", "", "The shared atlases: pages, glyphs, limits, bytes"),
            ("atlas_reset", "", "Empties the atlases; each text makes what it needs again"),
            ("atlas_export", "--output <file.png> [--set sdf|msdf|color] [--page <n>]", "One atlas page as a PNG"),
            ("caches", "", "The shared text caches: hits, misses, bytes"),
            ("memory", "", "What loaded fonts cost"),
            ("export_png", "--target <t> --output <file.png> [--scale <n>] [--background #rrggbb|transparent]", "Renders a text through a temporary camera, so that you can look at it. Leaves nothing behind"),
            ("select", "--target <t>", "Selects and frames an object, for the person watching"),
            ("edit_in_scene", "--target <t>", "Starts typing into a text in the Scene view, for the person watching"),
            ("formula", "--source <tex> [--display] [--size <n>]", "Checks a formula and measures it"),
            ("migrate_scan", "[--root <folder>]", "Reads the project for TextMesh Pro, and changes nothing"),
            ("migrate_status", "", "Counts from the last scan and stages"),
            ("migrate_report", "--output <file>", "Everything found, done and not carried"),
            ("migrate_fonts", "", "Stage one: a Lettera font for each TextMesh Pro font. Refuses on a dirty open scene"),
            ("migrate_components", "[--simple]", "Stage two: rewrites scenes and prefabs. Refuses on a dirty open scene"),
            ("migrate_scripts", "[--preview]", "Stage three: rewrites C#, or with --preview lists what would change. Refuses on a dirty open scene"),
            ("settings_get", "[--key math.scale]", "LetteraSettings"),
            ("settings_set", "--data {...} [--create]", "Patches LetteraSettings; --create makes the asset if the project has none"),
            ("device_check_build", "--target android|ios|web|windows|mac", "Builds the device check sample. Refuses on a dirty open scene"),
        };

        // Written once here because an effect's slots are not data anywhere else.
        static readonly Dictionary<string, string> s_TagHints = new Dictionary<string, string>
        {
            ["b"] = "weight 100-900; f forces synthetic, r real faces only", ["strong"] = "as b", ["i"] = "slant in percent of the glyph height", ["em"] = "as i",
            ["size"] = "24, 150%, +10, -5, 1.2em", ["color"] = "#RGB, #RRGGBB, #RRGGBBAA or a name", ["alpha"] = "0.5, 50%, #80", ["font"] = "a family name of the font stack", ["lang"] = "a BCP 47 tag",
            ["u"] = "optional color", ["s"] = "optional color", ["strikethrough"] = "optional color", ["outline"] = "color, width in em or 2px", ["shadow"] = "color, x, y, softness, dilate",
            ["fill"] = "paint, dilate, softness, then paint settings", ["stroke"] = "paint, width (em or 2px), inside center outside or -1 to 1, softness, round or sharp", ["glow"] = "paint, radius, softness, dilate",
            ["inner-shadow"] = "paint, x, y, softness, choke", ["extrude"] = "x, y, near color, far color, dilate, softness", ["filter"] = "intensity, grayscale invert sepia", ["warp"] = "amplitude, scale, travel",
            ["mark"] = "paint, glyph line range block, paint span, radius, padding, behind above, height", ["smallcaps"] = "scale of synthetic ones", ["otf"] = "onum, -dlig, cv02 3",
            ["axes"] = "wght, wdth, ital, slnt, opsz; 640, 120%, +80, ~; GRAD 50", ["size-match"] = "XHeight or CapHeight", ["tracking"] = "local units or em", ["word-spacing"] = "local units or em", ["cell"] = "width, or auto",
            ["line-height"] = "multiplier, mode, leading", ["para-spacing"] = "after, before", ["line-gap"] = "amount", ["indent"] = "local units, em, % of the rect", ["align"] = "alignment, justify mode, last line", ["trim"] = "top edge, bottom edge",
            ["curve"] = "radius, anchor", ["link"] = "URL", ["a"] = "URL", ["ellipsis"] = "position: 0 start, 0.5 middle, 1 end", ["truncate"] = "position: 0 start, 0.5 middle, 1 end", ["dir"] = "ltr, rtl, auto",
            ["reveal"] = "entrance, reach (100% is all, a number counts units), unit", ["math"] = "inline or display, then words for a screen reader",
        };

        /// <summary>Runs a command's body. Nothing gets out: a thrown exception comes back as {"error": ...}.</summary>
        static object Run(Func<object> body)
        {
            try
            {
                return body();
            }
            catch (Exception e)
            {
                while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
                return new Dictionary<string, object> { ["error"] = e.Message, ["kind"] = e.GetType().Name };
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Discovery
        // -----------------------------------------------------------------------------------------------------

        internal static object Help() => Run(() => new Dictionary<string, object>
        {
            ["about"] = "Lettera text engine. Targets are hierarchy paths (\"/Canvas/Title\"), names or instance ids (\"#1234\"). Answers are JSON; a failure is {\"error\":...}. Edits are undoable and dirty the scene. Nothing saves a scene.",
            ["firstRule"] = "After every visual change: 'lettera export_png --target <t> --output preview.png', then look at the image.",
            ["workflow"] = new List<object>
            {
                "1. 'lettera help', 'lettera types', 'lettera tags' say what exists.",
                "2. 'lettera list' says which texts exist.",
                "3. 'lettera create --kind text --text Hello --name Title' or 'lettera set --target Title --data {...}'.",
                "4. 'lettera export_png --target Title --output preview.png' and look. Fix, and look again.",
                "5. 'lettera validate --target Title' before calling it done.",
            },
            ["commands"] = Commands.Select(c => (object)$"{c.name} {c.arguments}".TrimEnd() + "   - " + c.purpose).ToList(),
            ["json"] = new Dictionary<string, object>
            {
                ["relaxed"] = "Unquoted keys and words, single quotes: {text:'Hi',wrap:false}",
                ["colors"] = "\"#RRGGBB\", \"#RRGGBBAA\", colour names (\"red\") or [r,g,b,a]",
                ["vectors"] = "[x,y] or \"x,y\"; padding is one number or [left,bottom,right,top]",
                ["enums"] = "names: \"Center\", \"RightToLeft\"",
                ["assets"] = "asset paths: \"Assets/Fonts/Inter Lettera.asset\"; null clears",
                ["polymorphic"] = "effects, matchers and rules take \"$type\": {$type:Stroke,width:0.05}; or just the name: \"Bold\"",
                ["paints"] = "\"#f00\", \"#f00,#00f\" (a gradient), or {gradient:'#f00,#00f',angle:90,span:Range}",
                ["lists"] = "an array replaces a list; an object {\"0\":{...}} patches by index",
                ["size"] = "in 'set', a number is the font size and [w,h] the rect",
            },
            ["examples"] = new List<object>
            {
                "unity command lettera create --kind text --name Title --text \"Sunset\" --size 600,120 --data \"{size:72,alignment:Center}\"",
                "unity command lettera style_add --target Title --effect Fill --data \"{paint:{gradient:'#FF8A00,#E52E71',angle:0,span:Range}}\"",
                "unity command lettera export_png --target Title --output preview.png",
                "unity command lettera set --target Title --data \"{text:'مرحبا',direction:RightToLeft,language:ar}\"",
            },
        });

        static Dictionary<string, object> DescribeType(Type type)
        {
            var d = new Dictionary<string, object> { ["type"] = LetteraCliData.ShortName(type) };
            try
            {
                object fresh = Activator.CreateInstance(type);
                if (fresh is LetteraEffect effect) d["parameters"] = Parameters(effect);
                d["defaults"] = LetteraCliData.ToData(fresh);
            }
            catch (Exception e)
            {
                d["note"] = "Could not be made with defaults: " + e.Message;
            }
            return d;
        }

        static List<object> Parameters(LetteraEffect effect) => effect.Parameters.Select(p => (object)new Dictionary<string, object>
        {
            ["id"] = p.Id, ["slot"] = p.Slot, ["kind"] = p.Kind.ToString(),
        }).ToList();

        static List<object> DescribeAll(Type baseType) => LetteraCliData.TypesOf(baseType).Values.Distinct().OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => (object)DescribeType(t)).ToList();

        internal static object Types() => Run(() => new Dictionary<string, object>
        {
            ["effects"] = DescribeAll(typeof(LetteraEffect)),
            ["matchers"] = DescribeAll(typeof(LetteraMatcher)),
            ["rules"] = DescribeAll(typeof(LetteraFieldRule)),
            ["paint"] = LetteraCliData.ToData(new LetteraPaint()),
            ["kinds"] = new List<object> { "text", "world", "text3d", "input", "area", "password", "dropdown" },
        });

        static object DescribeStyle(LetteraStyle style)
        {
            string tag = style?.Matcher is TagMatcher t ? t.Name : style?.Matcher?.GetType().GetProperty("Name")?.GetValue(style.Matcher) as string;
            var d = new Dictionary<string, object>
            {
                ["tag"] = tag,
                ["matcher"] = style?.Matcher != null ? LetteraCliData.ShortName(style.Matcher.GetType()) : null,
                ["effect"] = style?.Effect != null ? LetteraCliData.ShortName(style.Effect.GetType()) : null,
                ["enabled"] = style?.Enabled ?? false,
            };
            if (!string.IsNullOrEmpty(style?.DefaultParameter)) d["parameter"] = style.DefaultParameter;
            if (tag != null && s_TagHints.TryGetValue(tag, out string hint)) d["slots"] = hint;
            if (style?.Effect != null && style.Effect.Parameters.Count > 0) d["parameters"] = Parameters(style.Effect);
            return d;
        }

        internal static object Tags(string target = null) => Run(() =>
        {
            LetteraStylePreset global = LetteraSettings.Instance.GlobalStylePreset;
            var d = new Dictionary<string, object>
            {
                ["source"] = global != null ? AssetDatabase.GetAssetPath(global) : "built-in vocabulary",
                ["tags"] = (global != null ? global.Styles : LetteraDefaultStyles.Styles).Select(DescribeStyle).ToList(),
            };
            if (!string.IsNullOrEmpty(target))
            {
                LetteraTextData data = DataOf(Text(Require(target)));
                d["ownStyles"] = data.Styles.Select(DescribeStyle).ToList();
                d["presets"] = data.StylePresets.Where(p => p != null).Select(p => (object)AssetDatabase.GetAssetPath(p)).ToList();
                d["usesGlobal"] = data.UseGlobalStylePreset;
            }
            return d;
        });

        static List<MonoBehaviour> AllTexts() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c is ITextComponent && c.gameObject.scene.IsValid() && c.gameObject.hideFlags == HideFlags.None).ToList();

        internal static string KindOf(Component text)
        {
            if (text is LetteraText) return "text";
            if (text is LetteraWorldText) return "world";
            return "text3d";
        }

        internal static object List() => Run(() => AllTexts().Select(c =>
        {
            Rebuild(c);
            TextEngine engine = ((ITextComponent)c).SharedEngine;
            string source = ((ITextComponent)c).SourceText ?? "";
            return (object)new Dictionary<string, object>
            {
                ["path"] = LetteraCliScene.Path(c.gameObject),
                ["id"] = "#" + c.gameObject.GetInstanceID(),
                ["kind"] = KindOf(c),
                ["active"] = c.gameObject.activeInHierarchy,
                ["glyphs"] = engine.GlyphCount,
                ["text"] = source.Length > 60 ? source.Substring(0, 60) + "..." : source,
            };
        }).OrderBy(x => (string)((Dictionary<string, object>)x)["path"], StringComparer.Ordinal).ToList());

        internal static object Get(string target, bool full = false) => Run(() => Describe(Text(Require(target)), full));

        static Dictionary<string, object> Describe(MonoBehaviour text, bool full = false)
        {
            Rebuild(text);
            GameObject go = text.gameObject;
            var engine = ((ITextComponent)text).SharedEngine;
            var d = new Dictionary<string, object>
            {
                ["path"] = LetteraCliScene.Path(go),
                ["id"] = "#" + go.GetInstanceID(),
                ["kind"] = KindOf(text),
                ["active"] = go.activeInHierarchy,
                ["rect"] = LetteraCliData.ToData(((RectTransform)go.transform).rect.size),
                ["position"] = LetteraCliData.ToData(text is LetteraText ? (Vector3)((RectTransform)go.transform).anchoredPosition : go.transform.localPosition),
                ["color"] = LetteraCliData.ToData(ColorOf(text)),
                ["glyphs"] = engine.GlyphCount,
                ["lines"] = engine.Lines.Count,
            };
            LetteraCliData.Compact = !full;
            try
            {
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)LetteraCliData.ToData(DataOf(text))) d[kv.Key] = kv.Value;
            }
            finally
            {
                LetteraCliData.Compact = false;
            }
            if (text.TryGetComponent(out LetteraEditable field)) d["rules"] = LetteraCliData.ToData(field.Rules);
            return d;
        }

        // -----------------------------------------------------------------------------------------------------
        // Creation and editing
        // -----------------------------------------------------------------------------------------------------

        internal static object Create(string kind, string text = null, string name = null, string parent = null, string size = null, string position = null, string data = null) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("--kind is required: text, world, text3d, input, area, password or dropdown.");
            kind = kind.Trim().ToLowerInvariant();
            bool onCanvas = kind == "text" || kind == "input" || kind == "area" || kind == "password" || kind == "dropdown";
            if (!onCanvas && kind != "world" && kind != "text3d") throw new ArgumentException($"Unknown kind '{kind}': text, world, text3d, input, area, password or dropdown.");
            Vector2? rect = size != null ? (Vector2?)Rect(size) : null;
            Vector2? place = position != null ? (Vector2?)ToVector2(position) : null;
            Dictionary<string, object> patch = string.IsNullOrWhiteSpace(data) ? null : LetteraCliJson.Object(data);
            Transform parentTransform = string.IsNullOrWhiteSpace(parent) ? null : Require(parent).transform;
            if (onCanvas && parentTransform != null && parentTransform.GetComponentInParent<Canvas>(true) == null)
                throw new ArgumentException($"A {kind} goes inside a Canvas, and {LetteraCliScene.Path(parentTransform.gameObject)} is not in one. Leave --parent out to use or make one.");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Lettera CLI create");
            int group = Undo.GetCurrentGroup();
            GameObject go = null;
            try
            {
                if (onCanvas) parentTransform = parentTransform != null ? parentTransform : LetteraMenus.FindOrCreateCanvas(null);
                go = Make(kind, parentTransform);
                Undo.RegisterCreatedObjectUndo(go, "Lettera CLI create");
                if (name != null)
                {
                    if (name.Trim().Length == 0) throw new ArgumentException("--name cannot be empty.");
                    go.name = name;
                }
                var transform = (RectTransform)go.transform;
                if (rect.HasValue) transform.sizeDelta = rect.Value;
                if (place.HasValue)
                {
                    if (onCanvas) transform.anchoredPosition = place.Value;
                    else transform.localPosition = place.Value;
                }
                MonoBehaviour component = FindText(go);
                if (component != null && text != null && kind != "dropdown") ((ITextComponent)component).SourceText = text;
                if (patch != null) Patch(component != null ? component : throw new ArgumentException("This kind has no text to patch."), patch);
                Touch(go);
                Dictionary<string, object> described = component != null ? Describe(component) : new Dictionary<string, object> { ["kind"] = kind };
                described["root"] = LetteraCliScene.Path(go);
                described["rootId"] = "#" + go.GetInstanceID();
                return described;
            }
            catch
            {
                // A bad patch must not leave a half-made object, or a canvas made for it.
                Undo.RevertAllDownToGroup(group);
                if (go != null) Object.DestroyImmediate(go);
                throw;
            }
        });

        static GameObject Make(string kind, Transform parent)
        {
            LetteraSettings settings = LetteraSettings.Instance;
            switch (kind)
            {
                case "input": return LetteraFieldFactory.CreateInputField(parent);
                case "area": return LetteraFieldFactory.CreateTextArea(parent);
                case "password": return LetteraFieldFactory.CreatePasswordField(parent);
                case "dropdown": return LetteraFieldFactory.CreateDropdown(parent);
                case "text":
                {
                    if (settings.TextPrefab != null) return (GameObject)PrefabUtility.InstantiatePrefab(settings.TextPrefab, parent);
                    var go = new GameObject("Text (Lettera)", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(220, 50);
                    go.AddComponent<LetteraText>().Text = "New Text";
                    return go;
                }
                case "world":
                {
                    if (settings.WorldTextPrefab != null) return (GameObject)PrefabUtility.InstantiatePrefab(settings.WorldTextPrefab, parent);
                    var go = new GameObject("World Text (Lettera)", typeof(RectTransform));
                    if (parent != null) go.transform.SetParent(parent, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(20, 5);
                    var text = go.AddComponent<LetteraWorldText>();
                    text.FontSize = 2;
                    text.Text = "New Text";
                    return go;
                }
                default:
                {
                    Type solid = Type.GetType("FirstSymbol.Lettera.LetteraText3D, FirstSymbol.Lettera.Text3D");
                    if (solid == null) throw new InvalidOperationException("3D text is compiled out of this project (LETTERA_NO_3D).");
                    var go = new GameObject("Text 3D (Lettera)", typeof(RectTransform));
                    if (parent != null) go.transform.SetParent(parent, false);
                    ((RectTransform)go.transform).sizeDelta = new Vector2(20, 5);
                    var component = (MonoBehaviour)go.AddComponent(solid);
                    SetProperty(component, "FontSize", 2f);
                    ((ITextComponent)component).SourceText = "3D Text";
                    return go;
                }
            }
        }

        internal static object Set(string target, string data) => Run(() =>
        {
            GameObject go = Require(target);
            MonoBehaviour text = Text(go);
            Patch(text, LetteraCliJson.Object(data ?? throw new ArgumentException("--data is required.")));
            Touch(text.gameObject);
            return Describe(text);
        });

        static readonly Dictionary<string, string> s_Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["stack"] = "FontStack", ["wrap"] = "WordWrap", ["direction"] = "BaseDirection",
        };

        // Each key becomes a step first, and only when all of them could be read does anything change: a bad patch leaves the text as it was.
        static void Patch(MonoBehaviour text, Dictionary<string, object> patch)
        {
            var steps = new List<Action>();
            foreach (KeyValuePair<string, object> kv in patch) steps.Add(Plan(text, kv.Key, kv.Value));
            GameObject go = text.gameObject;
            Undo.SetCurrentGroupName("Lettera CLI edit");
            Undo.RecordObject(text, "Lettera CLI edit");
            Undo.RecordObject(go.transform, "Lettera CLI edit");
            Undo.RecordObject(go, "Lettera CLI edit");
            if (text.TryGetComponent(out LetteraEditable field)) Undo.RecordObject(field, "Lettera CLI edit");
            foreach (Action step in steps) step();
        }

        static Action Plan(MonoBehaviour text, string key, object value)
        {
            var rect = (RectTransform)text.transform;
            var host = (ITextComponent)text;
            switch (key.ToLowerInvariant())
            {
                case "name":
                {
                    var name = (string)LetteraCliData.Convert(value, typeof(string));
                    if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name cannot be empty.");
                    return () => text.gameObject.name = name;
                }
                case "active":
                {
                    var active = (bool)LetteraCliData.Convert(value, typeof(bool));
                    return () => text.gameObject.SetActive(active);
                }
                case "text":
                {
                    string source = (string)LetteraCliData.Convert(value, typeof(string)) ?? "";
                    return () => host.SourceText = source;
                }
                case "size":
                    if (value is double || value is string s && !s.Contains(","))
                    {
                        float fontSize = FontSize(value);
                        return () => SetProperty(text, "FontSize", fontSize);
                    }
                    {
                        Vector2 size = Rect(value);
                        return () => rect.sizeDelta = size;
                    }
                case "fontsize":
                {
                    float fontSize = FontSize(value);
                    return () => SetProperty(text, "FontSize", fontSize);
                }
                case "rect":
                {
                    Vector2 size = Rect(value);
                    return () => rect.sizeDelta = size;
                }
                case "position":
                {
                    Vector3 position = value is List<object> l && l.Count == 3 ? (Vector3)LetteraCliData.Convert(value, typeof(Vector3)) : (Vector3)ToVector2(value);
                    if (text is LetteraText) return () => rect.anchoredPosition = position;
                    return () => rect.localPosition = position;
                }
                case "rotation":
                {
                    Vector3 rotation = value is List<object> ? (Vector3)LetteraCliData.Convert(value, typeof(Vector3)) : new Vector3(0, 0, (float)LetteraCliData.Convert(value, typeof(float)));
                    return () => rect.localEulerAngles = rotation;
                }
                case "scale":
                {
                    Vector3 scale = value is List<object> ? (Vector3)LetteraCliData.Convert(value, typeof(Vector3)) : Vector3.one * (float)LetteraCliData.Convert(value, typeof(float));
                    return () => rect.localScale = scale;
                }
                case "color":
                case "colour":
                {
                    Color color = LetteraCliData.ToColor(value);
                    return () => SetColor(text, color);
                }
                case "alignment":
                case "align":
                    return PlanAlignment(text, value);
                case "padding":
                {
                    Vector4 padding = value is double ? Vector4.one * (float)LetteraCliData.Convert(value, typeof(float)) : (Vector4)LetteraCliData.Convert(value, typeof(Vector4));
                    return () => SetProperty(text, "Padding", padding);
                }
                case "lineheight":
                {
                    var height = (float)LetteraCliData.Convert(value, typeof(float));
                    return () =>
                    {
                        SetProperty(text, "LineHeightValue", height);
                        if (DataOf(text).LineHeightMode == LetteraLineHeightMode.Default) SetProperty(text, "LineHeightMode", LetteraLineHeightMode.Scaled);
                    };
                }
                case "styles":
                {
                    var styles = (List<LetteraStyle>)LetteraCliData.Convert(value, typeof(List<LetteraStyle>));
                    return () =>
                    {
                        DataOf(text).Styles = styles;
                        host.Dirty(DirtyLevel.Parse);
                    };
                }
                case "presets":
                case "stylepresets":
                {
                    var presets = (List<LetteraStylePreset>)LetteraCliData.Convert(value, typeof(List<LetteraStylePreset>));
                    return () =>
                    {
                        DataOf(text).StylePresets = presets;
                        host.Dirty(DirtyLevel.Parse);
                    };
                }
                case "rules":
                {
                    if (!text.TryGetComponent(out LetteraEditable field)) throw new ArgumentException("Rules belong to a field, and this text is not editable.");
                    var rules = (List<LetteraFieldRule>)LetteraCliData.Convert(value, typeof(List<LetteraFieldRule>));
                    return () =>
                    {
                        foreach (LetteraFieldRule old in field.Rules.ToList()) field.RemoveRule(old);
                        foreach (LetteraFieldRule rule in rules) field.AddRule(rule);
                    };
                }
            }

            string wanted = s_Aliases.TryGetValue(key, out string alias) ? alias : key;
            PropertyInfo property = Settable(text.GetType()).FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (property == null)
                throw new ArgumentException($"A {KindOf(text)} has no '{key}'. It has name, active, text, size, rect, position, rotation, scale, color, alignment, padding, lineHeight, styles, presets, rules, stack, wrap, direction, " + string.Join(", ", Settable(text.GetType()).Select(p => LetteraCliData.Key(p.Name))));
            object converted = LetteraCliData.Convert(value, property.PropertyType, property.GetValue(text));
            return () => property.SetValue(text, converted);
        }

        static IEnumerable<PropertyInfo> Settable(Type type) => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0 && p.DeclaringType.Namespace == typeof(LetteraText).Namespace && p.Name != "color" && !typeof(Delegate).IsAssignableFrom(p.PropertyType) && !p.PropertyType.IsInterface);

        static Action PlanAlignment(MonoBehaviour text, object value)
        {
            object horizontal = null, vertical = null;
            if (value is Dictionary<string, object> d)
            {
                d.TryGetValue("horizontal", out horizontal);
                d.TryGetValue("vertical", out vertical);
                if (horizontal == null && vertical == null) throw new ArgumentException("Alignment is {horizontal:...,vertical:...}.");
            }
            else
            {
                List<object> parts = value is string s ? s.Split(',').Select(x => (object)x.Trim()).ToList() : value as List<object> ?? throw new ArgumentException("Alignment is \"Center\", \"Center,Middle\" or {horizontal,vertical}.");
                foreach (object part in parts)
                {
                    if (part is string name && Enum.TryParse(name, true, out LetteraVerticalAlignment _) && !Enum.TryParse(name, true, out LetteraHorizontalAlignment _)) vertical = name;
                    else horizontal = part;
                }
            }
            var h = horizontal != null ? (LetteraHorizontalAlignment?)LetteraCliData.Convert(horizontal, typeof(LetteraHorizontalAlignment)) : null;
            var v = vertical != null ? (LetteraVerticalAlignment?)LetteraCliData.Convert(vertical, typeof(LetteraVerticalAlignment)) : null;
            return () =>
            {
                if (h.HasValue) SetProperty(text, "HorizontalAlignment", h.Value);
                if (v.HasValue) SetProperty(text, "VerticalAlignment", v.Value);
            };
        }

        static float FontSize(object value)
        {
            var size = (float)LetteraCliData.Convert(value, typeof(float));
            if (size <= 0 || size > 10000) throw new ArgumentException($"A font size is above 0 and at most 10000, got {size}.");
            return size;
        }

        static Vector2 Rect(object value)
        {
            Vector2 size = ToVector2(value);
            if (size.x < 0 || size.y < 0 || size.x > 1000000 || size.y > 1000000) throw new ArgumentException($"A size is 0 to 1000000 on each side, got {size}.");
            return size;
        }

        static Vector2 ToVector2(object value) => (Vector2)LetteraCliData.Convert(value, typeof(Vector2));

        // -----------------------------------------------------------------------------------------------------
        // Styles and presets
        // -----------------------------------------------------------------------------------------------------

        static int Index(string index, int count)
        {
            if (!int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) throw new ArgumentException($"--index must be a whole number, got '{index}'.");
            if (i < 0 || i >= count) throw new ArgumentException($"Index {i} is out of range (count {count}).");
            return i;
        }

        static List<object> StyleRows(LetteraTextData data) => data.Styles.Select((s, i) =>
        {
            var row = (Dictionary<string, object>)LetteraCliData.ToData(s);
            row["index"] = i;
            return (object)row;
        }).ToList();

        internal static object StyleList(string target) => Run(() => new Dictionary<string, object>
        {
            ["styles"] = StyleRows(DataOf(Text(Require(target)))),
        });

        internal static object StyleAdd(string target, string effect = null, string tag = null, string matcher = null, string parameter = null, string data = null) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            if (string.IsNullOrWhiteSpace(effect) && string.IsNullOrWhiteSpace(matcher) && string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("Give --effect, --tag or --matcher. 'lettera types' lists them.");
            var style = new LetteraStyle();
            if (!string.IsNullOrWhiteSpace(effect))
            {
                style.Effect = (LetteraEffect)Activator.CreateInstance(LetteraCliData.ResolveType(effect, typeof(LetteraEffect)));
                if (!string.IsNullOrWhiteSpace(data)) LetteraCliData.Apply(style.Effect, LetteraCliJson.Object(data));
            }
            else if (!string.IsNullOrWhiteSpace(data)) throw new ArgumentException("--data holds an effect's fields, and no --effect was given.");
            if (!string.IsNullOrWhiteSpace(tag)) style.Matcher = new TagMatcher(tag.Trim());
            else if (!string.IsNullOrWhiteSpace(matcher)) style.Matcher = (LetteraMatcher)Activator.CreateInstance(LetteraCliData.ResolveType(matcher, typeof(LetteraMatcher)));
            else style.Matcher = new WholeTextMatcher();
            if (parameter != null) style.DefaultParameter = parameter;
            Undo.RecordObject(text, "Lettera CLI style");
            DataOf(text).Styles.Add(style);
            ((ITextComponent)text).Dirty(DirtyLevel.Parse);
            Touch(text.gameObject);
            return new Dictionary<string, object> { ["index"] = DataOf(text).Styles.Count - 1, ["styles"] = StyleRows(DataOf(text)) };
        });

        internal static object StyleRemove(string target, string index) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            int i = Index(index, DataOf(text).Styles.Count);
            Undo.RecordObject(text, "Lettera CLI style");
            DataOf(text).Styles.RemoveAt(i);
            ((ITextComponent)text).Dirty(DirtyLevel.Parse);
            Touch(text.gameObject);
            return new Dictionary<string, object> { ["styles"] = StyleRows(DataOf(text)) };
        });

        internal static object StyleSet(string target, string index, string data) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            int i = Index(index, DataOf(text).Styles.Count);
            LetteraStyle patched = LetteraCliData.ToStyle(LetteraCliJson.Object(data ?? throw new ArgumentException("--data is required.")), DataOf(text).Styles[i]);
            Undo.RecordObject(text, "Lettera CLI style");
            DataOf(text).Styles[i] = patched;
            ((ITextComponent)text).Dirty(DirtyLevel.Parse);
            Touch(text.gameObject);
            return new Dictionary<string, object> { ["styles"] = StyleRows(DataOf(text)) };
        });

        internal static object PresetSave(string target, string path) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            string asset = AssetPath(path, ".asset");
            LetteraStylePreset preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(asset);
            if (preset == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(asset) != null) throw new ArgumentException($"{asset} is something else already.");
                preset = ScriptableObject.CreateInstance<LetteraStylePreset>();
                MakeFolders(asset);
                AssetDatabase.CreateAsset(preset, asset);
            }
            else Undo.RecordObject(preset, "Lettera CLI preset");
            preset.Styles.Clear();
            preset.Styles.AddRange(DataOf(text).Styles.Where(s => s != null).Select(s => s.Clone()));
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssetIfDirty(preset);
            preset.NotifyChanged();
            return new Dictionary<string, object> { ["preset"] = asset, ["styles"] = preset.Styles.Count };
        });

        internal static object PresetApply(string target, string path) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            LetteraStylePreset preset = AssetDatabase.LoadAssetAtPath<LetteraStylePreset>(path ?? "");
            if (preset == null) throw new ArgumentException($"No style preset at '{path}'.");
            Undo.RecordObject(text, "Lettera CLI preset");
            if (!DataOf(text).StylePresets.Contains(preset)) DataOf(text).StylePresets.Add(preset);
            ((ITextComponent)text).Dirty(DirtyLevel.Parse);
            Touch(text.gameObject);
            return new Dictionary<string, object> { ["presets"] = DataOf(text).StylePresets.Select(p => (object)AssetDatabase.GetAssetPath(p)).ToList() };
        });

        // -----------------------------------------------------------------------------------------------------
        // For the person watching
        // -----------------------------------------------------------------------------------------------------

        internal static object Select(string target) => Run(() =>
        {
            GameObject go = Require(target);
            Selection.activeGameObject = go;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            return new Dictionary<string, object> { ["selected"] = LetteraCliScene.Path(go) };
        });

        const string EditMenu = "Tools/Lettera/Edit Text In Scene";

        internal static object EditInScene(string target) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            Selection.activeGameObject = text.gameObject;
            // Asked for by its menu, so that this stays right if the session's code moves. A missing menu would log an error.
            bool exists = TypeCache.GetMethodsWithAttribute<MenuItem>().Any(m => m.GetCustomAttributes<MenuItem>().Any(a => a.menuItem == EditMenu));
            if (!exists) return new Dictionary<string, object> { ["selected"] = LetteraCliScene.Path(text.gameObject), ["started"] = false, ["why"] = "This version has no in-scene editing yet. The text is selected." };
            return new Dictionary<string, object> { ["selected"] = LetteraCliScene.Path(text.gameObject), ["started"] = EditorApplication.ExecuteMenuItem(EditMenu) };
        });

        // -----------------------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------------------

        static GameObject Require(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("--target is required: a hierarchy path, a name or #id.");
            return LetteraCliScene.Find(target) ?? throw new ArgumentException($"Nothing found at '{target}'. 'lettera list' shows the texts there are.");
        }

        static MonoBehaviour FindText(GameObject go)
        {
            foreach (MonoBehaviour c in go.GetComponents<MonoBehaviour>())
                if (c is ITextComponent)
                    return c;
            // A field's root holds its text further in.
            LetteraEditable field = go.GetComponentInChildren<LetteraEditable>(true);
            if (field != null && field is Component && field.GetComponent<ITextComponent>() is MonoBehaviour fieldText) return fieldText;
            LetteraDropdown dropdown = go.GetComponent<LetteraDropdown>();
            return dropdown != null ? dropdown.CaptionText : null;
        }

        static MonoBehaviour Text(GameObject go) => FindText(go) ?? throw new ArgumentException($"{LetteraCliScene.Path(go)} holds no Lettera text.");

        static LetteraTextData DataOf(Component text) => ((ITextHost)text).Data;

        static void Rebuild(Component text) => text.GetType().GetMethod("ForceRebuild", Type.EmptyTypes)?.Invoke(text, null);

        static void SetProperty(Component text, string property, object value) => text.GetType().GetProperty(property).SetValue(text, value);

        static Color ColorOf(Component text) => text is Graphic graphic ? graphic.color : (Color)text.GetType().GetProperty("Color").GetValue(text);

        static void SetColor(Component text, Color color)
        {
            if (text is Graphic graphic) graphic.color = color;
            else SetProperty(text, "Color", color);
        }

        static void Touch(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                EditorUtility.SetDirty(c);
                if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            }
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }

        // Written here rather than left to the asset database, which logs an error for each thing wrong with a path.
        static string AssetPath(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required: Assets/...");
            string clean = path.Replace('\\', '/').Trim();
            if (!clean.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException($"'{path}' is not under Assets/.");
            if (clean.Contains("..") || clean.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0 || clean.Any(char.IsControl)) throw new ArgumentException($"'{path}' is not a usable path.");
            if (!clean.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"'{path}' must end in {extension}.");
            return clean;
        }

        static void MakeFolders(string assetPath)
        {
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(current + "/" + parts[i])) AssetDatabase.CreateFolder(current, parts[i]);
                current += "/" + parts[i];
            }
        }
    }
}
