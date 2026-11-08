using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

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
            var material = new Material(Shader.Find("Unlit/Color")) { color = color };
            Created.Add(material);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Texture2D picture = Render(100, 100, Color.black);
            Object.DestroyImmediate(quad);
            return picture.GetPixel(50, 50);
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

        static string Environment_ShotsDir => Directory.Exists("D:/UnityBuilds/LetteraTools/rc2/shots_R") ? "D:/UnityBuilds/LetteraTools/rc2/shots_R" : null;

        [TestCase("<color=#3399FF>MMMM</color>", "face")]
        [TestCase("<color=#FF8040><stroke=#3399FF,0.25>MMMM</stroke></color>", "stroke")]
        [TestCase("<color=#FF8040><shadow=#3399FF,0.2,-0.2>MMMM</shadow></color>", "shadow")]
        public void WorldTextShowsTheColorAnUnlitMaterialOfItsValueShows(string markup, string which)
        {
            Color32 want = Reference(Blue), wantFace = Reference(Red);
            LetteraWorldText text = Make(markup, 100, 60, null, 50);
            text.ForceRebuild();
            Texture2D picture = Render(200, 80, Color.black);
            Save(picture, "world_builtin_" + which);
            List<Color32> shown = Dominant(picture);
            Assert.IsTrue(Has(shown, want), which + ": wanted " + want + ", got " + string.Join(" ", shown));
            if (which != "face") Assert.IsTrue(Has(shown, wantFace), which + ": the face colour is lost, got " + string.Join(" ", shown));
        }
    }
}
