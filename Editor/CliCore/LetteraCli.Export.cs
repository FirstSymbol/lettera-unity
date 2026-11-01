using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor.Cli
{
    internal static partial class LetteraCli
    {
        const int MaxExportSide = 4096;

        /// <summary>
        /// A copy of the object is drawn in a preview scene of its own, so the open scene is not touched, nothing else gets in the picture,
        /// and closing that scene takes everything with it.
        /// </summary>
        internal static object ExportPng(string target, string output, string scale = null, string background = null) => Run(() =>
        {
            GameObject source = Require(target);
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output is required: a .png file.");
            string file = Path.GetFullPath(output);
            if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"'{output}' must end in .png.");
            float multiple = 2;
            if (!string.IsNullOrEmpty(scale) && (!float.TryParse(scale, NumberStyles.Float, CultureInfo.InvariantCulture, out multiple) || !(multiple > 0) || multiple > 64)) throw new ArgumentException($"--scale is a number above 0 and at most 64, got '{scale}'.");
            List<MonoBehaviour> texts = source.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c is ITextComponent).ToList();
            if (texts.Count == 0) throw new ArgumentException($"{LetteraCliScene.Path(source)} holds no Lettera text.");
            Color back = Background(background, texts[0]);
            bool canvas = source.GetComponentsInChildren<Graphic>(true).Length > 0;
            bool solid = texts.Any(t => KindOf(t) == "text3d");
            Directory.CreateDirectory(Path.GetDirectoryName(file));

            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool wasAsync = ShaderUtil.allowAsyncCompilation;
            RenderTexture wasActive = RenderTexture.active;
            RenderTexture canvasTarget = null;
            Texture2D picture = null;
            GameObject clone = null;
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                GameObject eye = Hidden("Lettera Export Camera", scene, typeof(Camera));
                var camera = eye.GetComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = back;
                GameObject root = canvas ? Hidden("Lettera Export Canvas", scene, typeof(RectTransform), typeof(Canvas)) : Hidden("Lettera Export Root", scene);
                if (canvas)
                {
                    var host = root.GetComponent<Canvas>();
                    host.renderMode = RenderMode.WorldSpace;
                    host.worldCamera = camera;
                }
                GameObject lamp = Hidden("Lettera Export Light", scene, typeof(Light));
                lamp.GetComponent<Light>().type = LightType.Directional;
                lamp.transform.rotation = Quaternion.Euler(35, 25, 0);

                clone = Object.Instantiate(source, root.transform, false);
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                Place(clone, source);
                clone.SetActive(true);
                List<MonoBehaviour> copies = clone.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c is ITextComponent).ToList();
                Settle(copies);

                Quaternion turn = solid ? Quaternion.Euler(14, -22, 0) : Quaternion.identity;
                Frame(clone, copies, turn, out Vector3 center, out Vector2 frame, out float depth);
                float perUnit = copies.Min(c => PixelsPerUnit(c)) * multiple;
                bool clamped = Mathf.Max(frame.x, frame.y) * perUnit > MaxExportSide;
                if (clamped) perUnit = MaxExportSide / Mathf.Max(frame.x, frame.y);
                int width = Mathf.Clamp(Mathf.CeilToInt(frame.x * perUnit), 1, MaxExportSide), height = Mathf.Clamp(Mathf.CeilToInt(frame.y * perUnit), 1, MaxExportSide);

                float distance = depth + Mathf.Max(frame.x, frame.y) + 10;
                eye.transform.rotation = turn;
                eye.transform.position = center - turn * Vector3.forward * distance;
                camera.orthographicSize = frame.y * 0.5f;
                camera.aspect = (float)width / height;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = distance * 2 + depth;
                canvasTarget = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = canvasTarget;
                // The first draw of a new shader variant can come out empty even with background compiling off.
                camera.Render();
                camera.Render();
                RenderTexture.active = canvasTarget;
                picture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                picture.Apply();
                camera.targetTexture = null;
                File.WriteAllBytes(file, picture.EncodeToPNG());
                int drawn = DrawnPixels(picture);
                var answer = new Dictionary<string, object>
                {
                    ["path"] = file.Replace('\\', '/'),
                    ["width"] = width,
                    ["height"] = height,
                    ["pixelsPerUnit"] = Math.Round(perUnit, 3),
                    ["background"] = back.a <= 0 ? "transparent" : "#" + ColorUtility.ToHtmlStringRGB(back),
                    ["drawnPixels"] = drawn,
                    ["ms"] = clock.ElapsedMilliseconds,
                };
                if (clamped) answer["clampedTo"] = MaxExportSide;
                if (drawn == 0) answer["warning"] = "Nothing drew: the text is empty, hidden, or the same colour as the background.";
                return answer;
            }
            finally
            {
                RenderTexture.active = wasActive;
                if (canvasTarget != null) RenderTexture.ReleaseTemporary(canvasTarget);
                if (picture != null) Object.DestroyImmediate(picture);
                if (clone != null) Object.DestroyImmediate(clone);
                EditorSceneManager.ClosePreviewScene(scene);
                LetteraWorldBatcher.ForgetScene(scene);
                ShaderUtil.allowAsyncCompilation = wasAsync;
            }
        });

        static GameObject Hidden(string name, Scene scene, params Type[] components)
        {
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // The copy stands at the origin at its own size, whatever the parents around the original were doing.
        static void Place(GameObject clone, GameObject source)
        {
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            if (clone.transform is RectTransform rect && source.transform is RectTransform original)
            {
                Vector2 size = original.rect.size;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition3D = Vector3.zero;
            }
            else clone.transform.localPosition = Vector3.zero;
        }

        static void Settle(List<MonoBehaviour> copies)
        {
            Canvas.ForceUpdateCanvases();
            foreach (MonoBehaviour copy in copies) Rebuild(copy);
            LetteraWorldBatcher.Update();
            GlyphAtlas.Flush();
            Canvas.ForceUpdateCanvases();
            GlyphAtlas.Flush();
        }

        // Rects and what was drawn, since a text can overflow its rect and the picture should show that.
        static void Frame(GameObject clone, List<MonoBehaviour> copies, Quaternion turn, out Vector3 center, out Vector2 frame, out float depth)
        {
            Quaternion back = Quaternion.Inverse(turn);
            var low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var high = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            void Add(Vector3 world)
            {
                Vector3 p = back * world;
                low = Vector3.Min(low, p);
                high = Vector3.Max(high, p);
            }
            void AddBounds(Transform owner, Bounds b)
            {
                for (int i = 0; i < 8; i++)
                    Add(owner.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1))));
            }
            var corners = new Vector3[4];
            foreach (RectTransform rect in clone.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect != clone.transform && rect.GetComponent<Graphic>() == null && !(rect.GetComponent<MonoBehaviour>() is ITextComponent)) continue;
                if (!rect.gameObject.activeInHierarchy) continue;
                rect.GetWorldCorners(corners);
                foreach (Vector3 corner in corners) Add(corner);
            }
            foreach (MonoBehaviour copy in copies)
            {
                if (!copy.gameObject.activeInHierarchy) continue;
                var ink = (Bounds)copy.GetType().GetProperty("InkBounds").GetValue(copy);
                if (ink.size.sqrMagnitude > 0) AddBounds(copy.transform, ink);
                var renderer = copy.GetComponent<Renderer>();
                if (renderer != null && renderer.bounds.size.sqrMagnitude > 0)
                {
                    Bounds world = renderer.bounds;
                    for (int i = 0; i < 8; i++)
                        Add(world.center + Vector3.Scale(world.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)));
                }
            }
            Vector3 size = high - low;
            float margin = Mathf.Max(Mathf.Min(size.x, size.y) * 0.04f, 0.01f);
            frame = new Vector2(Mathf.Max(size.x, 0.01f) + margin * 2, Mathf.Max(size.y, 0.01f) + margin * 2);
            depth = size.z;
            center = turn * ((low + high) * 0.5f);
        }

        // A Canvas unit is a pixel. A world unit is whatever the text's size makes it, so its glyphs are drawn at a readable size either way.
        static float PixelsPerUnit(MonoBehaviour copy)
        {
            if (copy is LetteraText) return 1;
            return 36f / Mathf.Max(DataOf(copy).FontSize, 0.01f);
        }

        // The default stands apart from the text's own colour, so that a first look does not come back blank.
        static Color Background(string background, Component text)
        {
            if (string.IsNullOrWhiteSpace(background))
            {
                Color own = ColorOf(text);
                return own.r * 0.3f + own.g * 0.59f + own.b * 0.11f > 0.4f ? new Color(0.12f, 0.13f, 0.15f, 1) : new Color(0.95f, 0.95f, 0.96f, 1);
            }
            return string.Equals(background.Trim(), "transparent", StringComparison.OrdinalIgnoreCase) ? new Color(0, 0, 0, 0) : LetteraCliData.ToColor(background);
        }

        static int DrawnPixels(Texture2D picture)
        {
            Color32[] pixels = picture.GetPixels32();
            if (pixels.Length == 0) return 0;
            Color32 first = pixels[0];
            int drawn = 0;
            foreach (Color32 p in pixels)
                if (Math.Abs(p.r - first.r) + Math.Abs(p.g - first.g) + Math.Abs(p.b - first.b) + Math.Abs(p.a - first.a) > 12)
                    drawn++;
            return drawn;
        }
    }
}
