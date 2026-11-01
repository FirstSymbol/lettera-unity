using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FirstSymbol.Lettera.Editor.Migration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Cli
{
    internal static partial class LetteraCli
    {
        // -----------------------------------------------------------------------------------------------------
        // Fonts
        // -----------------------------------------------------------------------------------------------------

        static bool IsFontFile(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".ttf" || extension == ".otf" || extension == ".ttc" || extension == ".otc";
        }

        internal static object FontCreate(string path, string output = null) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("--path is required: a font file.");
            if (!IsFontFile(path)) throw new ArgumentException($"'{path}' is not a .ttf, .otf, .ttc or .otc file.");
            if (!File.Exists(path)) throw new ArgumentException($"No file at '{path}'.");
            string target = output != null ? AssetPath(output, ".asset") : Path.ChangeExtension(path, null).Replace('\\', '/') + " Lettera.asset";
            if (output == null && !target.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("This file is outside Assets/: give --output Assets/...asset");
            var font = ScriptableObject.CreateInstance<LetteraFont>();
            font.SetFontData(File.ReadAllBytes(path));
            if (font.LoadFace() == null)
            {
                Object.DestroyImmediate(font);
                throw new ArgumentException($"'{path}' could not be read as a font.");
            }
            MakeFolders(target);
            target = AssetDatabase.GenerateUniqueAssetPath(target);
            AssetDatabase.CreateAsset(font, target);
            AssetDatabase.SaveAssetIfDirty(font);
            return new Dictionary<string, object> { ["font"] = target, ["family"] = font.FamilyName };
        });

        internal static object FontStack(string fonts, string path = null, string target = null) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(fonts)) throw new ArgumentException("--fonts is required: font asset paths, separated by commas.");
            MonoBehaviour text = string.IsNullOrWhiteSpace(target) ? null : Text(Require(target));
            string asset = AssetPath(path ?? "Assets/Lettera/Fonts/Lettera Font Stack.asset", ".asset");
            var stack = ScriptableObject.CreateInstance<LetteraFontStack>();
            foreach (string name in fonts.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0))
            {
                LetteraFontAsset font = AssetDatabase.LoadAssetAtPath<LetteraFontAsset>(name);
                if (font == null)
                {
                    Object.DestroyImmediate(stack);
                    throw new ArgumentException($"No font asset at '{name}'.");
                }
                stack.Families.Add(new LetteraFontFamily { Primary = font });
            }
            MakeFolders(asset);
            asset = AssetDatabase.GenerateUniqueAssetPath(asset);
            AssetDatabase.CreateAsset(stack, asset);
            AssetDatabase.SaveAssetIfDirty(stack);
            if (text != null)
            {
                Undo.RecordObject(text, "Lettera CLI font stack");
                SetProperty(text, "FontStack", stack);
                Touch(text.gameObject);
            }
            return new Dictionary<string, object> { ["stack"] = asset, ["families"] = stack.Families.Count };
        });

        static Dictionary<string, object> DescribeFace(FontFace face) => new Dictionary<string, object>
        {
            ["family"] = face.Family,
            ["style"] = face.StyleName,
            ["italic"] = face.IsItalic,
            ["upem"] = face.Metrics.Upem,
            ["glyphs"] = face.IsVirtual ? 0 : LetteraNative.lt_face_glyph_count(face.Face),
            ["color"] = face.IsColor,
            ["variable"] = face.IsVariable,
            ["retainedBytes"] = face.RetainedBytes,
        };

        internal static object FontInfo(string path) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("--path is required: a font asset or a font file.");
            LetteraFontAsset asset = AssetDatabase.LoadAssetAtPath<LetteraFontAsset>(path);
            if (asset != null)
            {
                FontFace face = asset.LoadFace();
                if (face == null) throw new ArgumentException($"{path} holds no readable font.");
                Dictionary<string, object> info = DescribeFace(face);
                info["path"] = path;
                return info;
            }
            if (!IsFontFile(path) || !File.Exists(path)) throw new ArgumentException($"No font asset or font file at '{path}'.");
            LetteraFont temporary = LetteraFont.CreateFromBytes(File.ReadAllBytes(path));
            try
            {
                FontFace face = temporary.LoadFace();
                if (face == null) throw new ArgumentException($"'{path}' could not be read as a font.");
                Dictionary<string, object> info = DescribeFace(face);
                info["path"] = path;
                return info;
            }
            finally
            {
                Object.DestroyImmediate(temporary);
            }
        });

        internal static object FontSubset(string path, string keep, string output = null) => Run(() =>
        {
            LetteraFont font = AssetDatabase.LoadAssetAtPath<LetteraFont>(path ?? "");
            if (font == null) throw new ArgumentException($"No font asset at '{path}'.");
            if (string.IsNullOrEmpty(keep)) throw new ArgumentException("--keep is required: text, ranges such as U+0020-007E, or both.");
            string target = output != null ? AssetPath(output, ".asset") : AssetPath(Path.ChangeExtension(path, null).Replace('\\', '/') + " Subset.asset", ".asset");
            LetteraSubsetResult result = LetteraFontSubset.Run(font, new LetteraSubsetOptions { Mode = LetteraSubsetMode.Keep, Characters = keep });
            if (!result.Succeeded) throw new InvalidOperationException(result.Refused ?? "The subset came out empty.");
            MakeFolders(target);
            LetteraFont made = LetteraFontSubset.SaveAs(font, result.Bytes, target);
            return new Dictionary<string, object>
            {
                ["font"] = AssetDatabase.GetAssetPath(made),
                ["bytesBefore"] = result.BytesBefore,
                ["bytesAfter"] = result.BytesAfter,
                ["glyphsBefore"] = result.GlyphsBefore,
                ["glyphsAfter"] = result.GlyphsAfter,
                ["warnings"] = result.Warnings.ToList<object>(),
            };
        });

        // -----------------------------------------------------------------------------------------------------
        // Looking at a text
        // -----------------------------------------------------------------------------------------------------

        static readonly Regex s_LiteralTag = new Regex(@"</?([A-Za-z][\w-]*)[^<>]*>", RegexOptions.CultureInvariant);

        internal static object Validate(string target, string platform = null) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            string where = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim().ToLowerInvariant();
            if (where != null && where != "web" && where != "android" && where != "ios" && where != "windows" && where != "mac" && where != "linux") throw new ArgumentException($"Unknown platform '{platform}': web, android, ios, windows, mac or linux.");
            Rebuild(text);
            TextEngine engine = ((ITextComponent)text).SharedEngine;
            engine.EnsureParsed();
            var problems = new List<object>();
            problems.AddRange(LetteraRangeProblems.Of(engine));
            // A tag nothing claims stays in what is shown, so what is shown is the test. An escape or noparse shows one on purpose.
            string source = ((ITextComponent)text).SourceText ?? "";
            bool literalAllowed = source.Contains("\\<") || source.Contains("<noparse");
            if (!literalAllowed)
                foreach (string name in s_LiteralTag.Matches(engine.CleanText ?? "").Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
                    problems.Add($"<{name}> is not bound to anything, and shows as written. 'lettera tags' lists what is, and 'lettera style_add --tag {name}' binds it.");

            int missing = 0;
            foreach (TextEngine.PlacedGlyph glyph in Placed(engine))
                if (glyph.Glyph == 0 && glyph.Special == 0)
                    missing++;
            if (missing > 0) problems.Add($"{missing} glyphs are the missing-character box: no font in use has them.");

            LetteraTextData data = DataOf(text);
            LetteraSettings settings = LetteraSettings.Instance;
            bool ownFont = data.Font != null || data.FontStack != null || settings.DefaultFont != null;
            var formulas = new List<LetteraFormulaInfo>();
            engine.CollectFormulas(formulas);
            // With no platform named these are worth knowing, and no fault of the text: ok answers for what is wrong anywhere.
            List<object> onPlatform = where != null ? problems : new List<object>();
            foreach (string place in where != null ? new[] { where } : new[] { "web", "android", "ios" })
            {
                if (place == "web" && !ownFont && settings.WebGLFallbackFont == null) onPlatform.Add("On the web a browser exposes no OS fonts, and this text has no font, and the project no WebGL fallback font.");
                if ((place == "web" || place == "android" || place == "ios") && formulas.Count > 0 && settings.Math.Font == null) onPlatform.Add($"On {place} there is no system math font, and the project sets none: its formulas use fallback layout.");
            }
            var answer = new Dictionary<string, object> { ["path"] = LetteraCliScene.Path(text.gameObject), ["ok"] = problems.Count == 0, ["problems"] = problems };
            if (where == null) answer["otherPlatforms"] = onPlatform;
            return answer;
        });

        static List<TextEngine.PlacedGlyph> Placed(TextEngine engine)
        {
            ReadOnlySpan<TextEngine.PlacedGlyph> span = engine.PlacedSpan;
            var list = new List<TextEngine.PlacedGlyph>(span.Length);
            for (int i = 0; i < span.Length; i++) list.Add(span[i]);
            return list;
        }

        internal static object Measure(string target, string width = null) => Run(() =>
        {
            MonoBehaviour text = Text(Require(target));
            var options = new LetteraMeasureOptions();
            if (!string.IsNullOrWhiteSpace(width))
            {
                var limit = (float)LetteraCliData.Convert(width, typeof(float));
                if (limit <= 0) throw new ArgumentException($"--width must be above 0, got {limit}.");
                options.MaxWidth = limit;
            }
            Rebuild(text);
            var size = (Vector2)text.GetType().GetMethod("MeasureText").Invoke(text, new object[] { options });
            TextEngine engine = ((ITextComponent)text).SharedEngine;
            return new Dictionary<string, object>
            {
                ["size"] = LetteraCliData.ToData(size),
                ["rect"] = LetteraCliData.ToData(((RectTransform)text.transform).rect.size),
                ["layoutBounds"] = LetteraCliData.ToData(engine.LayoutBounds),
                ["lines"] = engine.Lines.Count,
                ["glyphs"] = engine.GlyphCount,
                ["minContentWidth"] = LetteraCliData.ToData(engine.GetMinContentWidth()),
                ["maxContentWidth"] = LetteraCliData.ToData(engine.GetMaxContentWidth()),
            };
        });

        const int MostGlyphRows = 2000;

        static Dictionary<string, object> GlyphTable(TextEngine engine)
        {
            List<TextEngine.PlacedGlyph> glyphs = Placed(engine);
            ReadOnlySpan<int> shown = engine.ShownCodepoints;
            var rows = new List<object>();
            for (int i = 0; i < glyphs.Count && i < MostGlyphRows; i++)
            {
                TextEngine.PlacedGlyph g = glyphs[i];
                FontFace face = g.Instance?.Face;
                rows.Add(new Dictionary<string, object>
                {
                    ["glyph"] = (long)g.Glyph,
                    ["cluster"] = g.Cluster,
                    ["codepoint"] = g.Cluster >= 0 && g.Cluster < shown.Length ? "U+" + shown[g.Cluster].ToString("X4") : null,
                    ["line"] = g.Line,
                    ["x"] = LetteraCliData.ToData(g.X),
                    ["y"] = LetteraCliData.ToData(g.Y),
                    ["advance"] = LetteraCliData.ToData(g.Advance),
                    ["size"] = LetteraCliData.ToData(g.Size),
                    ["font"] = face != null ? (face.IsVirtual ? "browser" : face.Family) : null,
                    ["color"] = face != null && face.IsColor,
                });
            }
            return new Dictionary<string, object> { ["total"] = glyphs.Count, ["shown"] = rows.Count, ["glyphs"] = rows };
        }

        internal static object Glyphs(string target = null, string font = null, string text = null) => Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                MonoBehaviour component = Text(Require(target));
                Rebuild(component);
                return GlyphTable(((ITextComponent)component).SharedEngine);
            }
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("Give --target, or --text with an optional --font.");
            LetteraFontAsset face = null;
            if (!string.IsNullOrWhiteSpace(font))
            {
                face = AssetDatabase.LoadAssetAtPath<LetteraFontAsset>(font);
                if (face == null) throw new ArgumentException($"No font asset at '{font}'.");
            }
            var bench = new GameObject("Lettera Glyphs Bench", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                ((RectTransform)bench.transform).sizeDelta = new Vector2(100000, 1000);
                var label = bench.AddComponent<LetteraWorldText>();
                label.WordWrap = false;
                label.Hide();
                label.Font = face;
                label.Text = text;
                label.ForceRebuild();
                return GlyphTable(label.Engine);
            }
            finally
            {
                Object.DestroyImmediate(bench);
            }
        });

        internal static object Formula(string source, bool display = false, string size = null) => Run(() =>
        {
            if (source == null) throw new ArgumentException("--source is required.");
            float em = 36;
            if (!string.IsNullOrWhiteSpace(size))
            {
                em = (float)LetteraCliData.Convert(size, typeof(float));
                if (em <= 0) throw new ArgumentException($"--size must be above 0, got {em}.");
            }
            bool valid = LetteraMath.Validate(source, out string message, out int position);
            var d = new Dictionary<string, object> { ["valid"] = valid };
            if (!valid)
            {
                d["message"] = message;
                d["position"] = position;
                return d;
            }
            Vector3 measured = LetteraMath.Measure(source, display, null, em);
            d["width"] = LetteraCliData.ToData(measured.x);
            d["ascent"] = LetteraCliData.ToData(measured.y);
            d["descent"] = LetteraCliData.ToData(measured.z);
            return d;
        });

        // -----------------------------------------------------------------------------------------------------
        // Atlases, caches, memory
        // -----------------------------------------------------------------------------------------------------

        internal static object Atlas() => Run(() =>
        {
            const long Page = 2048L * 2048;
            return new Dictionary<string, object>
            {
                ["glyphs"] = GlyphAtlas.GlyphCount,
                ["sdfPages"] = GlyphAtlas.SdfPageCount,
                ["msdfPages"] = GlyphAtlas.MsdfPageCount,
                ["colorPages"] = GlyphAtlas.ColorPageCount,
                ["sdfLimit"] = GlyphAtlas.PageLimits.sdf,
                ["colorLimit"] = GlyphAtlas.PageLimits.color,
                ["bytes"] = GlyphAtlas.SdfPageCount * Page + GlyphAtlas.MsdfPageCount * Page * 4 + GlyphAtlas.ColorPageCount * Page * 4,
            };
        });

        internal static object AtlasReset() => Run(() =>
        {
            GlyphAtlas.Clear();
            return new Dictionary<string, object> { ["glyphs"] = GlyphAtlas.GlyphCount };
        });

        internal static object AtlasExport(string output, string set = null, string page = null) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output is required: a .png file.");
            string file = Path.GetFullPath(output);
            if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"'{output}' must end in .png.");
            GlyphAtlasView.Set which = string.IsNullOrWhiteSpace(set) ? GlyphAtlasView.Set.Sdf : (GlyphAtlasView.Set)LetteraCliData.Convert(set, typeof(GlyphAtlasView.Set));
            Texture2DArray array = GlyphAtlas.TextureOf((int)which);
            if (array == null) throw new InvalidOperationException($"The {which} atlas is empty. A text that draws fills it.");
            int index = string.IsNullOrWhiteSpace(page) ? 0 : Index(page, array.depth);
            Texture2D copy = GlyphAtlasView.CopyPage(which, index, out string info);
            RenderTexture target = RenderTexture.GetTemporary(copy.width, copy.height, 0);
            RenderTexture before = RenderTexture.active;
            var readable = new Texture2D(copy.width, copy.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Graphics.Blit(copy, target);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, copy.width, copy.height), 0, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, readable.EncodeToPNG());
                return new Dictionary<string, object> { ["path"] = file.Replace('\\', '/'), ["width"] = copy.width, ["height"] = copy.height, ["info"] = info };
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readable);
                Object.DestroyImmediate(copy);
            }
        });

        internal static object Caches() => Run(() =>
        {
            LetteraCaches.Statistics stats = LetteraCaches.Stats;
            return new Dictionary<string, object>
            {
                ["enabled"] = LetteraCaches.Enabled,
                ["limitBytes"] = LetteraCaches.ParagraphCacheBytes,
                ["bytes"] = stats.Bytes,
                ["hits"] = stats.Hits,
                ["misses"] = stats.Misses,
                ["evictions"] = stats.Evictions,
                ["shapeCalls"] = stats.ShapeCalls,
            };
        });

        internal static object Memory() => Run(() =>
        {
            LetteraFontMemory.Sums sums = LetteraFontMemory.Totals;
            var entries = new List<LetteraFontMemoryEntry>();
            LetteraFontMemory.Snapshot(entries);
            return new Dictionary<string, object>
            {
                ["faces"] = sums.Faces,
                ["instances"] = sums.Instances,
                ["retainedBytes"] = sums.RetainedBytes,
                ["mappedBytes"] = sums.MappedBytes,
                ["cacheFolderBytes"] = sums.CacheFolderBytes,
                ["entries"] = entries.Select(e => LetteraCliData.ToData(e)).ToList(),
            };
        });

        // -----------------------------------------------------------------------------------------------------
        // Settings
        // -----------------------------------------------------------------------------------------------------

        const string SettingsPath = "Assets/Resources/" + LetteraSettings.ResourceName + ".asset";

        internal static object SettingsGet(string key = null) => Run(() =>
        {
            LetteraSettings settings = LetteraSettings.Instance;
            Dictionary<string, object> all = LetteraCliData.Members(settings);
            object value = all;
            if (!string.IsNullOrWhiteSpace(key))
            {
                foreach (string part in key.Split('.'))
                {
                    if (!(value is Dictionary<string, object> level) || !level.TryGetValue(part, out value))
                        throw new ArgumentException($"No setting '{key}'. There are: {string.Join(", ", all.Keys)}");
                }
            }
            return new Dictionary<string, object> { ["persisted"] = EditorUtility.IsPersistent(settings), ["value"] = value };
        });

        internal static object SettingsSet(string data, bool create = false) => Run(() =>
        {
            Dictionary<string, object> patch = LetteraCliJson.Object(data ?? throw new ArgumentException("--data is required."));
            LetteraSettings settings = LetteraSettings.Instance;
            if (!EditorUtility.IsPersistent(settings) && create)
            {
                MakeFolders(SettingsPath);
                settings = ScriptableObject.CreateInstance<LetteraSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            bool persisted = EditorUtility.IsPersistent(settings);
            string before = EditorJsonUtility.ToJson(settings);
            if (persisted) Undo.RecordObject(settings, "Lettera CLI settings");
            try
            {
                LetteraCliData.Apply(settings, patch);
            }
            catch
            {
                EditorJsonUtility.FromJsonOverwrite(before, settings);
                throw;
            }
            if (persisted) EditorUtility.SetDirty(settings);
            LetteraSettings.RaiseChanged();
            var answer = new Dictionary<string, object> { ["persisted"] = persisted };
            if (!persisted) answer["note"] = "This project has no settings asset, so this lasts until a domain reload. --create makes one.";
            return answer;
        });

        // -----------------------------------------------------------------------------------------------------
        // Migration and the device check
        // -----------------------------------------------------------------------------------------------------

        static LetteraMigrationRun s_Migration;

        static LetteraMigrationRun Migration()
        {
            if (s_Migration != null) return s_Migration;
            s_Migration = new LetteraMigrationRun();
            s_Migration.Scan();
            return s_Migration;
        }

        // A prompt would hang an agent, and a person's unsaved work is not the command's to settle.
        static void RefuseOnDirtyScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) throw new InvalidOperationException($"The open scene '{(string.IsNullOrEmpty(scene.path) ? "Untitled" : scene.path)}' has unsaved changes. Save or discard them first. Nothing was changed.");
            }
        }

        static Dictionary<string, object> MigrationStatus(LetteraMigrationRun run)
        {
            return new Dictionary<string, object>
            {
                ["scanned"] = true,
                ["files"] = run.Findings.Count,
                ["texts"] = run.Findings.Sum(f => f.Texts),
                ["fields"] = run.Findings.Sum(f => f.Fields),
                ["byKind"] = run.Findings.GroupBy(f => f.Kind.ToString()).ToDictionary(g => g.Key, g => (object)g.Count()),
                ["byStatus"] = run.Findings.GroupBy(f => f.Status ?? "").ToDictionary(g => g.Key, g => (object)g.Count()),
                ["notCarried"] = run.Losses.Count,
                ["log"] = run.Log.Skip(Math.Max(0, run.Log.Count - 20)).ToList<object>(),
            };
        }

        internal static object MigrateScan(string root = null) => Run(() =>
        {
            string folder = string.IsNullOrWhiteSpace(root) ? "Assets" : root;
            if (folder.Contains("..") || !Directory.Exists(folder)) throw new ArgumentException($"No folder at '{root}'.");
            var run = new LetteraMigrationRun();
            run.Scan(folder);
            s_Migration = run;
            Dictionary<string, object> status = MigrationStatus(run);
            status["findings"] = run.Findings.Take(200).Select(f => LetteraCliData.ToData(f)).ToList();
            return status;
        });

        internal static object MigrateStatus() => Run(() => s_Migration == null ? new Dictionary<string, object> { ["scanned"] = false } : MigrationStatus(s_Migration));

        internal static object MigrateReport(string output) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output is required.");
            string file = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, Migration().Report());
            return new Dictionary<string, object> { ["path"] = file.Replace('\\', '/') };
        });

        internal static object MigrateFonts() => Run(() =>
        {
            RefuseOnDirtyScene();
            LetteraMigrationRun run = Migration();
            int made = run.MigrateFonts();
            Dictionary<string, object> status = MigrationStatus(run);
            status["made"] = made;
            return status;
        });

        internal static object MigrateComponents(bool simple = false) => Run(() =>
        {
            RefuseOnDirtyScene();
            if (!LetteraMigrationRun.ForceText) throw new InvalidOperationException("Asset Serialization must be Force Text, so that scenes and prefabs can be read. Nothing was changed.");
            LetteraMigrationRun run = Migration();
            if (!run.MigrateComponents(false)) throw new InvalidOperationException("The stage stopped: " + run.Log.LastOrDefault());
            Dictionary<string, object> status = MigrationStatus(run);
            // Only what converts by itself is converted today, so --simple asks for nothing else.
            status["simpleOnly"] = true;
            return status;
        });

        internal static object MigrateScripts(bool preview = false) => Run(() =>
        {
            LetteraMigrationRun run = Migration();
            if (preview)
                return new Dictionary<string, object>
                {
                    ["preview"] = run.PreviewScripts().Select(p => (object)new Dictionary<string, object>
                    {
                        ["path"] = p.path,
                        ["changes"] = p.before != p.after,
                        ["notes"] = p.notes.Select(n => (object)$"{n.Line}: {n.Message}").ToList(),
                    }).ToList(),
                };
            RefuseOnDirtyScene();
            int written = run.ApplyScripts();
            Dictionary<string, object> status = MigrationStatus(run);
            status["written"] = written;
            return status;
        });

        internal static object DeviceCheckBuild(string target) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("--target is required: android, ios, web, windows or mac.");
            if (!LetteraDeviceCheckMenu.Platforms.TryGetValue(target.Trim().ToLowerInvariant(), out (BuildTarget target, BuildTargetGroup group, string output) platform))
                throw new ArgumentException($"Unknown target '{target}': android, ios, web, windows or mac.");
            RefuseOnDirtyScene();
            var open = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (!string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    open.Add(SceneManager.GetSceneAt(i).path);
            string problem = LetteraDeviceCheckMenu.Build(platform.target, platform.group, platform.output, out string result, out string file);
            if (problem != null) throw new InvalidOperationException(problem);
            for (int i = 0; i < open.Count; i++) EditorSceneManager.OpenScene(open[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
            return new Dictionary<string, object> { ["result"] = result, ["path"] = file };
        });
    }
}
