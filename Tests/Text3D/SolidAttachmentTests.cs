using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>Copies of prefabs on glyphs: where they stand, how big, and that they stay on their glyph through motion and deformers.</summary>
    public class SolidAttachmentTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<Object> m_Made = new List<Object>();
        const float Em = 20;

        LetteraText3D Solid(string text)
        {
            var go = new GameObject("Solid", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 500);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = Latin;
            solid.FontSize = Em;
            solid.WordWrap = false;
            solid.ShareMesh = false;
            solid.Pivot = LetteraSolidPivot.Front;
            solid.Depth = 0.3f;
            solid.Text = text;
            solid.ForceRebuild();
            m_Made.Add(go);
            return solid;
        }

        GameObject Prefab(PrimitiveType type = PrimitiveType.Cube, Color? color = null)
        {
            var go = new GameObject("Bead " + type, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(type == PrimitiveType.Sphere ? "Sphere.fbx" : type == PrimitiveType.Cylinder ? "Cylinder.fbx" : "Cube.fbx");
            var material = new Material(Shader.Find("Standard")) { color = color ?? new Color(1f, 0.55f, 0.1f) };
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.transform.position = new Vector3(0, -10000, 0);
            m_Made.Add(go);
            m_Made.Add(material);
            return go;
        }

        LetteraGlyphAttachments Attach(LetteraText3D text, params LetteraGlyphAttachment[] rules)
        {
            LetteraGlyphAttachments attachments = text.gameObject.AddComponent<LetteraGlyphAttachments>();
            attachments.Attachments.AddRange(rules);
            attachments.Rebuild();
            return attachments;
        }

        static List<GameObject> Copies(LetteraGlyphAttachments attachments, List<int> characters = null)
        {
            var copies = new List<GameObject>();
            attachments.GetCopies(copies, characters);
            return copies;
        }

        [TearDown]
        public void Clean()
        {
            foreach (Object made in m_Made)
                if (made != null)
                    Object.DestroyImmediate(made);
            m_Made.Clear();
            LetteraSolidCache.Clear();
        }

        [Test]
        public void TopBottomAndFrontSitOnTheGlyphsExtremes()
        {
            LetteraText3D text = Solid("AIL");
            GameObject bead = Prefab();
            LetteraGlyphAttachments attachments = Attach(text,
                new LetteraGlyphAttachment { Prefab = bead, Place = LetteraGlyphAttachPlace.Top },
                new LetteraGlyphAttachment { Prefab = bead, Place = LetteraGlyphAttachPlace.Bottom },
                new LetteraGlyphAttachment { Prefab = bead, Place = LetteraGlyphAttachPlace.Front });
            var characters = new List<int>();
            List<GameObject> copies = Copies(attachments, characters);
            Assert.AreEqual(9, copies.Count, "one of each on three glyphs");
            for (int i = 0; i < copies.Count; i++)
            {
                Bounds glyph = SolidAnchorTests.GlyphBounds(text, characters[i], false);
                Transform t = copies[i].transform;
                int rule = i / 3;
                Assert.IsTrue(copies[i].transform.IsChildOf(text.transform), "a copy is the text's own");
                Assert.AreEqual(HideFlags.HideAndDontSave, copies[i].hideFlags, "never saved with the scene");
                if (rule == 0)
                {
                    Assert.AreEqual(glyph.max.y, t.localPosition.y, 0.01f * Em, "on top of " + characters[i]);
                    Assert.Greater(Vector3.Dot(t.up, Vector3.up), 0.7f);
                }
                else if (rule == 1)
                {
                    Assert.AreEqual(glyph.min.y, t.localPosition.y, 0.01f * Em, "under " + characters[i]);
                    Assert.Greater(Vector3.Dot(t.up, Vector3.down), 0.7f);
                }
                else
                {
                    Assert.AreEqual(glyph.min.z, t.localPosition.z, 1e-3f, "on the front of " + characters[i]);
                    Assert.Greater(Vector3.Dot(t.up, Vector3.back), 0.99f);
                    Assert.IsTrue(glyph.min.x <= t.localPosition.x && t.localPosition.x <= glyph.max.x);
                }
                SolidAnchorTests.AssertOnSurface(text, t.position, t.up, Em, "copy " + i);
            }
            // The top of an A is its apex, in the middle; the top of an L is its stem, at the left.
            Bounds l = SolidAnchorTests.GlyphBounds(text, 2, false);
            Assert.Less(copies[2].transform.localPosition.x, l.center.x);
        }

        [Test]
        public void CopiesAreSizedInEmAndVaryBySeed()
        {
            LetteraText3D text = Solid("mmmmmm");
            GameObject bead = Prefab();
            bead.transform.localScale = new Vector3(1, 2, 1);
            var rule = new LetteraGlyphAttachment { Prefab = bead, Place = LetteraGlyphAttachPlace.Top, Size = 0.5f };
            LetteraGlyphAttachments attachments = Attach(text, rule);
            foreach (GameObject copy in Copies(attachments))
                Assert.AreEqual(new Vector3(0.5f * Em, Em, 0.5f * Em), copy.transform.localScale, "half an em, keeping the prefab's own shape");

            rule.ScaleVariation = 0.5f;
            rule.SpinVariation = 90;
            attachments.Rebuild();
            var sizes = new List<float>();
            var turns = new List<float>();
            foreach (GameObject copy in Copies(attachments))
            {
                float size = copy.transform.localScale.x;
                Assert.That(size, Is.InRange(0.25f * Em - 1e-3f, 0.75f * Em + 1e-3f));
                sizes.Add(size);
                turns.Add(Vector3.SignedAngle(Vector3.back, Vector3.ProjectOnPlane(copy.transform.forward, copy.transform.up), copy.transform.up));
            }
            Assert.Greater(Spread(sizes), 0.05f * Em, "sizes vary");
            Assert.Greater(Spread(turns), 10f, "turns vary");
            attachments.Rebuild();
            List<GameObject> again = Copies(attachments);
            for (int i = 0; i < again.Count; i++) Assert.AreEqual(sizes[i], again[i].transform.localScale.x, 1e-4f, "the same seed, the same sizes");
            rule.Seed = 7;
            attachments.Rebuild();
            List<GameObject> other = Copies(attachments);
            float differ = 0;
            for (int i = 0; i < other.Count; i++) differ = Mathf.Max(differ, Mathf.Abs(sizes[i] - other[i].transform.localScale.x));
            Assert.Greater(differ, 1e-3f, "another seed, other sizes");
        }

        static float Spread(List<float> values)
        {
            float low = float.MaxValue, high = float.MinValue;
            foreach (float v in values)
            {
                low = Mathf.Min(low, v);
                high = Mathf.Max(high, v);
            }
            return high - low;
        }

        [Test]
        public void OnlyTheCharactersAskedForGetCopies()
        {
            LetteraText3D text = Solid("Hello world");
            LetteraGlyphAttachments attachments = Attach(text, new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Top, Characters = "lo" });
            var characters = new List<int>();
            Copies(attachments, characters);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 7, 9 }, characters);
        }

        [Test]
        public void StrokesAreSpacedAlongTheOutline()
        {
            LetteraText3D text = Solid("O");
            var rule = new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Strokes, Spacing = 0.05f, MaxPerGlyph = 400, Size = 0.02f };
            LetteraGlyphAttachments attachments = Attach(text, rule);
            List<GameObject> copies = Copies(attachments);
            // Two rings, outside and inside, some four em around in all; a stroke is wider than the spacing, so both are kept.
            Assert.That(copies.Count, Is.InRange(50, 120));
            Bounds o = SolidAnchorTests.GlyphBounds(text, 0, false);
            for (int i = 0; i < copies.Count; i++)
            {
                Assert.AreEqual(LetteraSolidSurface.Side, attachments.AnchorOf(i).Surface);
                Vector3 p = copies[i].transform.localPosition;
                Assert.AreEqual(o.center.z, p.z, 0.05f * Em, "at mid depth");
                Assert.Less(Mathf.Abs(copies[i].transform.up.z), 0.2f, "standing out of the side");
                for (int j = 0; j < i; j++)
                    Assert.GreaterOrEqual(Vector3.Distance(p, copies[j].transform.localPosition), 0.05f * Em - 1e-3f, "a spacing apart");
            }
            rule.MaxPerGlyph = 5;
            attachments.Rebuild();
            Assert.AreEqual(5, attachments.CopyCount, "capped per glyph");
        }

        [Test]
        public void ScatterCoversWhatFacesUpBySeed()
        {
            LetteraText3D text = Solid("T");
            text.Depth = 1;
            var rule = new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Scatter, Density = 200, MaxPerGlyph = 500, MaxSlope = 20, Size = 0.03f };
            LetteraGlyphAttachments attachments = Attach(text, rule);
            List<GameObject> copies = Copies(attachments);
            Assert.Greater(copies.Count, 10);
            var first = new List<Vector3>();
            foreach (GameObject copy in copies)
            {
                Assert.Greater(Vector3.Dot(copy.transform.up, Vector3.up), Mathf.Cos(20 * Mathf.Deg2Rad) - 0.1f, "on a surface facing up");
                first.Add(copy.transform.localPosition);
            }
            // The top of a T's bar is wide and deep: a T one em deep has some 0.5 square em of it facing up.
            Assert.That(copies.Count, Is.InRange(40, 200));
            attachments.Rebuild();
            List<GameObject> again = Copies(attachments);
            for (int i = 0; i < again.Count; i++) Assert.AreEqual(first[i], again[i].transform.localPosition);
            rule.Seed = 3;
            attachments.Rebuild();
            Assert.AreNotEqual(first[0], Copies(attachments)[0].transform.localPosition, "another seed, other places");
        }

        [Test]
        public void CopiesStayOnTheirGlyphThroughMotion()
        {
            LetteraText3D text = Solid("<wave>abc</wave>");
            LetteraGlyphAttachments attachments = Attach(text,
                new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Top },
                new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Scatter, Density = 100, Seed = 2 });
            var characters = new List<int>();
            List<GameObject> copies = Copies(attachments, characters);
            Assert.Greater(copies.Count, 3);
            var before = new List<Vector3>();
            var glyphs = new List<Vector3>();
            foreach (GameObject copy in copies) before.Add(copy.transform.localPosition);
            for (int c = 0; c < 3; c++) glyphs.Add(SolidAnchorTests.GlyphBounds(text, c, true).center);
            int builds = LetteraSolidMeshBuilder.IndexWrites;
            text.Engine.TickPose(0.3f, 0.3f);
            Assert.AreEqual(builds, LetteraSolidMeshBuilder.IndexWrites, "a pose frame, not a rebuild");
            float moved = 0;
            for (int i = 0; i < copies.Count; i++)
            {
                Vector3 glyph = SolidAnchorTests.GlyphBounds(text, characters[i], true).center - glyphs[characters[i]];
                Vector3 copy = copies[i].transform.localPosition - before[i];
                Assert.AreEqual(0, Vector3.Distance(glyph, copy), 1e-3f, "copy " + i + " moved as its glyph");
                moved = Mathf.Max(moved, copy.magnitude);
            }
            Assert.Greater(moved, 0.05f);
        }

        [Test]
        public void CopiesStayOnTheirGlyphThroughDeformers()
        {
            LetteraText3D text = Solid("Bend");
            text.AddDeformer(LetteraDeformer.Bend(0));
            text.ForceRebuild();
            LetteraGlyphAttachments attachments = Attach(text,
                new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Top },
                new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Strokes, Spacing = 0.3f });
            int count = attachments.CopyCount;
            var before = new List<Vector3>();
            foreach (GameObject copy in Copies(attachments)) before.Add(copy.transform.position);
            text.SetDeformer(0, LetteraDeformer.Bend(180, LetteraDeformAxis.Up));
            text.ForceRebuild();
            Assert.AreEqual(count, attachments.CopyCount);
            List<GameObject> copies = Copies(attachments);
            float moved = 0;
            for (int i = 0; i < copies.Count; i++)
            {
                SolidAnchorTests.AssertOnSurface(text, copies[i].transform.position, copies[i].transform.up, Em, "copy " + i);
                moved = Mathf.Max(moved, Vector3.Distance(before[i], copies[i].transform.position));
            }
            Assert.Greater(moved, 0.5f * Em, "the bend carried them");
        }

        // Bytes the engine's pose frames allocate, less those the same frames allocate without what follows them.
        internal static long FollowBytes(LetteraText3D text, System.Action add)
        {
            long Frames()
            {
                for (int i = 0; i < 5; i++) text.Engine.TickPose(0.016f, 0.016f);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 60; i++) text.Engine.TickPose(0.016f, 0.016f);
                return System.GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long plain = Frames();
            add();
            return Frames() - plain;
        }

        [Test]
        public void FollowingMotionAllocatesNothing()
        {
            LetteraText3D text = Solid("<wave>Lettera</wave>");
            long bytes = FollowBytes(text, () => Attach(text,
                new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Top },
                new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Scatter, Density = 60 }));
            Assert.Greater(text.GetComponent<LetteraGlyphAttachments>().CopyCount, 7);
            Assert.LessOrEqual(bytes, 0, "60 pose frames with copies following");
        }

        [Test]
        public void AGlyphFadedToNothingHidesItsCopies()
        {
            LetteraText3D text = Solid("ab");
            text.Color = new Color(1, 1, 1, 0);
            LetteraGlyphAttachments attachments = Attach(text, new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Top });
            text.Engine.TickPose(0.1f, 0.1f);
            foreach (GameObject copy in Copies(attachments)) Assert.IsFalse(copy.activeSelf);
        }

        [Test]
        public void TurningItOffTakesItsCopiesAway()
        {
            LetteraText3D text = Solid("abc");
            LetteraGlyphAttachments attachments = Attach(text, new LetteraGlyphAttachment { Prefab = Prefab(), Place = LetteraGlyphAttachPlace.Top });
            Assert.AreEqual(3, attachments.CopyCount);
            int children = text.transform.childCount;
            attachments.enabled = false;
            Assert.AreEqual(children - 1, text.transform.childCount);
            attachments.enabled = true;
            Assert.AreEqual(3, attachments.CopyCount);
            text.Text = "abcd";
            text.ForceRebuild();
            Assert.AreEqual(4, attachments.CopyCount, "built again with the text");
            Object.DestroyImmediate(attachments);
            Assert.AreEqual(children - 1, text.transform.childCount);
        }

        // ------------------------------------------------------------------ pictures

        [Test]
        public void Pictures()
        {
            string folder = System.Environment.GetEnvironmentVariable("LETTERA_SHOTS");
            if (string.IsNullOrEmpty(folder)) Assert.Ignore("LETTERA_SHOTS names no folder");
            Directory.CreateDirectory(folder);
            ShaderUtil.allowAsyncCompilation = false;
            var light = new GameObject("Light");
            m_Made.Add(light);
            Light sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);

            LetteraText3D text = Solid("Lettera");
            text.HorizontalAlignment = LetteraHorizontalAlignment.Center;
            text.VerticalAlignment = LetteraVerticalAlignment.Middle;
            text.Color = new Color(0.25f, 0.45f, 0.9f);
            GameObject sphere = Prefab(PrimitiveType.Sphere, new Color(1f, 0.8f, 0.2f));
            GameObject cube = Prefab(PrimitiveType.Cube, new Color(0.9f, 0.2f, 0.3f));
            GameObject cone = Prefab(PrimitiveType.Cylinder, new Color(0.2f, 0.85f, 0.4f));

            var top = new LetteraGlyphAttachment { Prefab = sphere, Place = LetteraGlyphAttachPlace.Top, Size = 0.18f, Lift = 0.09f };
            var strokes = new LetteraGlyphAttachment { Prefab = sphere, Place = LetteraGlyphAttachPlace.Strokes, Size = 0.06f, Spacing = 0.12f, MaxPerGlyph = 60 };
            var scatter = new LetteraGlyphAttachment { Prefab = cone, Place = LetteraGlyphAttachPlace.Scatter, Size = 0.05f, Density = 120, ScaleVariation = 0.5f, SpinVariation = 180, MaxPerGlyph = 80 };
            var front = new LetteraGlyphAttachment { Prefab = cube, Place = LetteraGlyphAttachPlace.Front, Size = 0.12f, Spin = 45 };
            LetteraGlyphAttachments attachments = Attach(text, top);

            void Shot(string name, Vector3 eye)
            {
                text.ForceRebuild();
                Assert.Greater(attachments.CopyCount, 0, name);
                Texture2D picture = RenderSolid(eye, new Vector3(0, 0, 0), 960, 540);
                File.WriteAllBytes(Path.Combine(folder, "d7_" + name + ".png"), picture.EncodeToPNG());
            }
            Shot("top", new Vector3(5, 30, -85));
            attachments.Attachments.Clear();
            attachments.Attachments.Add(strokes);
            attachments.Rebuild();
            Shot("strokes", new Vector3(-15, 20, -80));
            attachments.Attachments.Clear();
            attachments.Attachments.Add(scatter);
            text.Depth = 0.8f;
            text.ForceRebuild();
            attachments.Rebuild();
            Shot("scatter", new Vector3(0, 75, -55));
            attachments.Attachments.Clear();
            attachments.Attachments.Add(top);
            attachments.Attachments.Add(front);
            text.Depth = 0.3f;
            text.AddDeformer(LetteraDeformer.Bend(140, LetteraDeformAxis.Up));
            text.ForceRebuild();
            attachments.Rebuild();
            Shot("bend", new Vector3(0, 20, -95));
            text.Text = "<wave>Lettera</wave>";
            text.ClearDeformers();
            text.ForceRebuild();
            text.Engine.TickPose(0.35f, 0.35f);
            Shot("wave", new Vector3(5, 30, -85));
            ShaderUtil.allowAsyncCompilation = true;
        }

        Texture2D RenderSolid(Vector3 eye, Vector3 at, int width, int height)
        {
            var cameraObject = new GameObject("Shot Camera");
            m_Made.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40;
            camera.transform.position = eye;
            camera.transform.LookAt(at);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            camera.nearClipPlane = 1;
            camera.farClipPlane = 1000;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.antiAliasing = 4;
            camera.targetTexture = target;
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            m_Made.Add(result);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }
    }
}
