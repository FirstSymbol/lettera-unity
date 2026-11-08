using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>World text shows the colors it was given, as an unlit material of the same color does, in either color space.</summary>
    public class WorldColorTests : EditingFixture
    {
        static readonly Color32 Blue = new Color32(0x33, 0x99, 0xFF, 255);
        static readonly Color32 Red = new Color32(0xFF, 0x80, 0x40, 255);

        // The color a quad of the Unlit/Color shader gives for the same value, which is what a user compares against.
        Color32 Reference(Color32 color)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Created.Add(quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.localScale = new Vector3(100, 100, 1);
            var material = new Material(UnlitShader(out string colorProperty));
            material.SetColor(colorProperty, color);
            Created.Add(material);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Texture2D picture = Render(100, 100, Color.black);
            Object.DestroyImmediate(quad);
            return picture.GetPixel(50, 50);
        }

        // HDRP scales an unlit material by the camera's exposure and tone-maps the picture; unlit text keeps its colors as written, whatever the exposure.
        // A volume that fixes the exposure at a multiplier of 1 and drops the tone map and the bloom makes the two comparable.
        void FixHdrpExposure()
        {
            if (PipelineName != "hdrp") return;
            Assembly hd = Loaded("Unity.RenderPipelines.HighDefinition.Runtime"), core = Loaded("Unity.RenderPipelines.Core.Runtime");
            Assert.IsTrue(hd != null && core != null, "the render pipeline assemblies are loaded");
            System.Type profileType = core.GetType("UnityEngine.Rendering.VolumeProfile"), volumeType = core.GetType("UnityEngine.Rendering.Volume");
            var profile = ScriptableObject.CreateInstance(profileType);
            Created.Add(profile);
            Set(profile, hd.GetType("UnityEngine.Rendering.HighDefinition.Exposure"), ("mode", "Fixed"), ("fixedExposure", -0.263034f));
            Set(profile, hd.GetType("UnityEngine.Rendering.HighDefinition.Tonemapping"), ("mode", "None"));
            Set(profile, hd.GetType("UnityEngine.Rendering.HighDefinition.Bloom"), ("intensity", 0f));
            Set(profile, hd.GetType("UnityEngine.Rendering.HighDefinition.Vignette"), ("intensity", 0f));
            var go = new GameObject("Fixed exposure");
            Created.Add(go);
            var volume = go.AddComponent(volumeType);
            volumeType.GetProperty("isGlobal").SetValue(volume, true);
            volumeType.GetField("priority").SetValue(volume, 1000f);
            volumeType.GetField("sharedProfile").SetValue(volume, profile);
        }

        static Assembly Loaded(string name)
        {
            foreach (Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == name) return a;
            return null;
        }

        static void Set(Object profile, System.Type type, params (string field, object value)[] values)
        {
            object component = profile.GetType().GetMethod("Add", new[] { typeof(System.Type), typeof(bool) }).Invoke(profile, new object[] { type, true });
            foreach (var (field, value) in values)
            {
                object parameter = type.GetField(field).GetValue(component);
                System.Type valueType = parameter.GetType();
                while (!valueType.IsGenericType) valueType = valueType.BaseType;
                valueType = valueType.GetGenericArguments()[0];
                object converted = valueType.IsEnum ? System.Enum.Parse(valueType, (string)value) : value;
                parameter.GetType().GetMethod("Override", new[] { valueType }).Invoke(parameter, new[] { converted });
            }
        }

        static Shader UnlitShader(out string colorProperty)
        {
            string pipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "";
            colorProperty = "_Color";
            if (pipeline.Contains("Universal")) { colorProperty = "_BaseColor"; return Shader.Find("Universal Render Pipeline/Unlit"); }
            if (pipeline.Contains("HDRenderPipeline")) { colorProperty = "_UnlitColor"; return Shader.Find("HDRP/Unlit"); }
            return Shader.Find("Unlit/Color");
        }

        // The colors that fill a good part of the picture, so edges between them do not count.
        static List<Color32> Dominant(Texture2D picture)
        {
            var counts = new Dictionary<Color32, int>();
            foreach (Color32 pixel in picture.GetPixels32())
            {
                counts.TryGetValue(pixel, out int n);
                counts[pixel] = n + 1;
            }
            var result = new List<Color32>();
            foreach (var pair in counts)
                if (pair.Value >= 150) result.Add(pair.Key);
            return result;
        }

        static bool Has(List<Color32> colors, Color32 want)
        {
            foreach (Color32 c in colors)
                if (Mathf.Abs(c.r - want.r) <= 2 && Mathf.Abs(c.g - want.g) <= 2 && Mathf.Abs(c.b - want.b) <= 2) return true;
            return false;
        }

        void Save(Texture2D picture, string name)
        {
            string dir = Environment_ShotsDir;
            if (dir == null) return;
            File.WriteAllBytes(Path.Combine(dir, name + "_" + QualitySettings.activeColorSpace + ".png"), picture.EncodeToPNG());
        }

        static string PipelineName
        {
            get
            {
                string name = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "";
                return name.Contains("Universal") ? "urp" : name.Contains("HDRenderPipeline") ? "hdrp" : "builtin";
            }
        }

        static string Environment_ShotsDir => Directory.Exists("D:/UnityBuilds/LetteraTools/rc2/shots_R") ? "D:/UnityBuilds/LetteraTools/rc2/shots_R" : null;

        [TestCase("<color=#3399FF>MMMM</color>", "face")]
        [TestCase("<color=#FF8040><stroke=#3399FF,0.25>MMMM</stroke></color>", "stroke")]
        [TestCase("<color=#FF8040><shadow=#3399FF,0.2,-0.2>MMMM</shadow></color>", "shadow")]
        public void WorldTextShowsTheColorAnUnlitMaterialOfItsValueShows(string markup, string which)
        {
            FixHdrpExposure();
            Color32 want = Reference(Blue), wantFace = Reference(Red);
            LetteraWorldText text = Make(markup, 100, 60, null, 50);
            text.ForceRebuild();
            Texture2D picture = Render(200, 80, Color.black);
            Save(picture, "world_" + PipelineName + "_" + which);
            List<Color32> shown = Dominant(picture);
            Assert.IsTrue(Has(shown, want), which + ": wanted " + want + ", got " + string.Join(" ", shown));
            if (which != "face") Assert.IsTrue(Has(shown, wantFace), which + ": the face colour is lost, got " + string.Join(" ", shown));
        }
    }
}
