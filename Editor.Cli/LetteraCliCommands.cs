using Unity.Pipeline.Commands;

namespace FirstSymbol.Lettera.Editor.Cli
{
    /// <summary>The "lettera" group of the Unity CLI: names and arguments only. What each does is in <see cref="LetteraCli"/>. Compiled only with com.unity.pipeline.</summary>
    internal static class LetteraCliCommands
    {
        const string Group = "lettera";
        const string TargetHelp = "Hierarchy path, name or #id";

        [CliCommand(Group, "help", "Commands, JSON conventions, examples")]
        static object Help() => LetteraCli.Help();

        [CliCommand(Group, "types", "Effects, matchers and field rules, with parameters and defaults")]
        static object Types() => LetteraCli.Types();

        [CliCommand(Group, "tags", "The vocabulary in force, with parameters")]
        static object Tags([CliArg("target", "A text, to add its own styles and presets")] string target = null) => LetteraCli.Tags(target);

        [CliCommand(Group, "list", "Every Lettera text in the open scenes: path, id, kind, glyphs")]
        static object List() => LetteraCli.List();

        [CliCommand(Group, "get", "A text's description; non-default values unless --full")]
        static object Get([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("full", "Include values still at their default")] bool full = false) => LetteraCli.Get(target, full);

        [CliCommand(Group, "create", "A new object: text, world, text3d, input, area, password or dropdown")]
        static object Create(
            [CliArg("kind", "text, world, text3d, input, area, password or dropdown", Required = true)] string kind,
            [CliArg("text", "Its text")] string text = null,
            [CliArg("name", "GameObject name")] string name = null,
            [CliArg("parent", "Parent path, name or #id")] string parent = null,
            [CliArg("size", "Rect: w,h")] string size = null,
            [CliArg("position", "x,y")] string position = null,
            [CliArg("data", "Patch applied after creation, as for 'set'")] string data = null) => LetteraCli.Create(kind, text, name, parent, size, position, data);

        [CliCommand(Group, "set", "Patches a text: text, font, stack, size, color, alignment, wrap, overflow, padding, lineHeight, direction, language, renderMode, ...")]
        static object Set([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("data", "Patch, e.g. {size:48,color:'#fff'}", Required = true)] string data) => LetteraCli.Set(target, data);

        [CliCommand(Group, "style_add", "Adds a style; with no tag and no matcher it covers the whole text")]
        static object StyleAdd(
            [CliArg("target", TargetHelp, Required = true)] string target,
            [CliArg("effect", "Effect type, e.g. Fill, Stroke, Bold")] string effect = null,
            [CliArg("tag", "Tag name that turns it on")] string tag = null,
            [CliArg("matcher", "Matcher type, in place of a tag")] string matcher = null,
            [CliArg("parameter", "Used when a tag gives none")] string parameter = null,
            [CliArg("data", "The effect's fields")] string data = null) => LetteraCli.StyleAdd(target, effect, tag, matcher, parameter, data);

        [CliCommand(Group, "style_remove", "Removes a style by index")]
        static object StyleRemove([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("index", "Style index", Required = true)] string index) => LetteraCli.StyleRemove(target, index);

        [CliCommand(Group, "style_list", "The text's own styles, by index")]
        static object StyleList([CliArg("target", TargetHelp, Required = true)] string target) => LetteraCli.StyleList(target);

        [CliCommand(Group, "style_set", "Patches a style by index")]
        static object StyleSet([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("index", "Style index", Required = true)] string index, [CliArg("data", "{effect,matcher,tag,enabled,parameter}", Required = true)] string data) => LetteraCli.StyleSet(target, index, data);

        [CliCommand(Group, "preset_save", "Saves the text's own styles as a preset asset")]
        static object PresetSave([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("path", "Assets/...asset", Required = true)] string path) => LetteraCli.PresetSave(target, path);

        [CliCommand(Group, "preset_apply", "Adds a preset to the text")]
        static object PresetApply([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("path", "Assets/...asset", Required = true)] string path) => LetteraCli.PresetApply(target, path);

        [CliCommand(Group, "font_create", "A font asset from a font file")]
        static object FontCreate([CliArg("path", "A .ttf, .otf, .ttc or .otc file", Required = true)] string path, [CliArg("output", "Assets/...asset; next to the file by default")] string output = null) => LetteraCli.FontCreate(path, output);

        [CliCommand(Group, "font_stack", "A font stack from font assets")]
        static object FontStack([CliArg("fonts", "Font asset paths, separated by commas", Required = true)] string fonts, [CliArg("path", "Assets/...asset")] string path = null, [CliArg("target", "A text to give it to")] string target = null) => LetteraCli.FontStack(fonts, path, target);

        [CliCommand(Group, "font_info", "Family, style, metrics, glyph count, color, axes")]
        static object FontInfo([CliArg("path", "A font asset or a font file", Required = true)] string path) => LetteraCli.FontInfo(path);

        [CliCommand(Group, "font_subset", "A smaller copy of a font, as a new asset")]
        static object FontSubset([CliArg("path", "A font asset", Required = true)] string path, [CliArg("keep", "Text, ranges such as U+0020-007E, or both", Required = true)] string keep, [CliArg("output", "Assets/...asset")] string output = null) => LetteraCli.FontSubset(path, keep, output);

        [CliCommand(Group, "validate", "Problems: unbound tags, broken formulas, missing fonts, bound twice")]
        static object Validate([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("platform", "web, android, ios, windows, mac or linux")] string platform = null) => LetteraCli.Validate(target, platform);

        [CliCommand(Group, "measure", "Size, bounds, lines")]
        static object Measure([CliArg("target", TargetHelp, Required = true)] string target, [CliArg("width", "Measure as if this wide")] string width = null) => LetteraCli.Measure(target, width);

        [CliCommand(Group, "glyphs", "The shaping result, glyph by glyph, of a text or of --text with --font")]
        static object Glyphs([CliArg("target", TargetHelp)] string target = null, [CliArg("font", "A font asset, with --text")] string font = null, [CliArg("text", "Text to shape")] string text = null) => LetteraCli.Glyphs(target, font, text);

        [CliCommand(Group, "atlas", "The shared atlases: pages, glyphs, limits, bytes")]
        static object Atlas() => LetteraCli.Atlas();

        [CliCommand(Group, "atlas_reset", "Empties the atlases; each text makes what it needs again")]
        static object AtlasReset() => LetteraCli.AtlasReset();

        [CliCommand(Group, "atlas_export", "One atlas page as a PNG")]
        static object AtlasExport([CliArg("output", "A .png file", Required = true)] string output, [CliArg("set", "sdf, msdf or color")] string set = null, [CliArg("page", "Page number")] string page = null) => LetteraCli.AtlasExport(output, set, page);

        [CliCommand(Group, "caches", "The shared text caches: hits, misses, bytes")]
        static object Caches() => LetteraCli.Caches();

        [CliCommand(Group, "memory", "What loaded fonts cost")]
        static object Memory() => LetteraCli.Memory();

        [CliCommand(Group, "export_png", "Renders a text through a temporary camera, so that you can look at it. Leaves nothing behind")]
        static object ExportPng(
            [CliArg("target", TargetHelp, Required = true)] string target,
            [CliArg("output", "A .png file", Required = true)] string output,
            [CliArg("scale", "Multiplies the resolution; 2 by default")] string scale = null,
            [CliArg("background", "A colour, or transparent")] string background = null) => LetteraCli.ExportPng(target, output, scale, background);

        [CliCommand(Group, "select", "Selects and frames an object, for the person watching")]
        static object Select([CliArg("target", TargetHelp, Required = true)] string target) => LetteraCli.Select(target);

        [CliCommand(Group, "edit_in_scene", "Starts typing into a text in the Scene view, for the person watching")]
        static object EditInScene([CliArg("target", TargetHelp, Required = true)] string target) => LetteraCli.EditInScene(target);

        [CliCommand(Group, "formula", "Checks a formula and measures it")]
        static object Formula([CliArg("source", "TeX source", Required = true)] string source, [CliArg("display", "As a paragraph of its own")] bool display = false, [CliArg("size", "Font size")] string size = null) => LetteraCli.Formula(source, display, size);

        [CliCommand(Group, "migrate_scan", "Reads the project for TextMesh Pro, and changes nothing")]
        static object MigrateScan([CliArg("root", "Folder; Assets by default")] string root = null) => LetteraCli.MigrateScan(root);

        [CliCommand(Group, "migrate_status", "Counts from the last scan and stages")]
        static object MigrateStatus() => LetteraCli.MigrateStatus();

        [CliCommand(Group, "migrate_report", "Writes everything found, done and not carried")]
        static object MigrateReport([CliArg("output", "A file", Required = true)] string output) => LetteraCli.MigrateReport(output);

        [CliCommand(Group, "migrate_fonts", "Stage one: a Lettera font for each TextMesh Pro font. Refuses on a dirty open scene")]
        static object MigrateFonts() => LetteraCli.MigrateFonts();

        [CliCommand(Group, "migrate_components", "Stage two: rewrites scenes and prefabs. Refuses on a dirty open scene")]
        static object MigrateComponents([CliArg("simple", "Only what converts by itself, which is all this version does")] bool simple = false) => LetteraCli.MigrateComponents(simple);

        [CliCommand(Group, "migrate_scripts", "Stage three: rewrites C#, or lists what would change. Refuses on a dirty open scene")]
        static object MigrateScripts([CliArg("preview", "List only")] bool preview = false) => LetteraCli.MigrateScripts(preview);

        [CliCommand(Group, "settings_get", "LetteraSettings")]
        static object SettingsGet([CliArg("key", "A dotted key, e.g. math.scale")] string key = null) => LetteraCli.SettingsGet(key);

        [CliCommand(Group, "settings_set", "Patches LetteraSettings")]
        static object SettingsSet([CliArg("data", "Patch, e.g. {disableEmoji:true,math:{scale:1.2}}", Required = true)] string data, [CliArg("create", "Make the asset if the project has none")] bool create = false) => LetteraCli.SettingsSet(data, create);

        [CliCommand(Group, "device_check_build", "Builds the device check sample. Refuses on a dirty open scene")]
        static object DeviceCheckBuild([CliArg("target", "android, ios, web, windows or mac", Required = true)] string target) => LetteraCli.DeviceCheckBuild(target);
    }
}
