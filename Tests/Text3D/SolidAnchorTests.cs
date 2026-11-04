using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>Rays against the drawn solids, and places on them followed through motion, deformers and rebuilds.</summary>
    public class SolidAnchorTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<GameObject> m_Made = new List<GameObject>();

        LetteraText3D Solid(string text, float size = 20)
        {
            var go = new GameObject("Solid", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 500);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = Latin;
            solid.FontSize = size;
            solid.WordWrap = false;
            solid.ShareMesh = false;
            solid.Pivot = LetteraSolidPivot.Front;
            solid.Text = text;
            solid.ForceRebuild();
            m_Made.Add(go);
            return solid;
        }

        [TearDown]
        public void Clean()
        {
            foreach (GameObject go in m_Made)
                if (go != null)
                    Object.DestroyImmediate(go);
            m_Made.Clear();
            LetteraSolidCache.Clear();
        }

        internal static Bounds GlyphBounds(LetteraText3D text, int character, bool posed)
        {
            var bounds = new Bounds();
            bool any = false;
            Unity.Collections.NativeArray<LetteraSolidMeshVertex> vertices = posed ? text.Builder.PosedVertices : text.Builder.RestVertices;
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed)
            {
                if (place.Cluster != character || place.Map != 0) continue;
                for (int i = place.VertexStart; i < place.VertexStart + place.VertexCount; i++)
                {
                    Vector3 p = vertices[i].Position;
                    if (!any) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    any = true;
                }
            }
            Assert.IsTrue(any, "character " + character + " has a solid");
            return bounds;
        }

        // A point is on the drawn surface when a short ray back along its normal meets the solid right there.
        internal static void AssertOnSurface(LetteraText3D text, Vector3 world, Vector3 up, float em, string what)
        {
            // Close, so that a ray out of a narrow counter does not start inside the stroke across it.
            Assert.IsTrue(text.Raycast(new Ray(world + up * em * 0.03f, -up), out LetteraSolidHit hit), what + ": a ray back finds the surface");
            Assert.AreEqual(0, Vector3.Distance(hit.Point, world), em * 0.005f, what + ": on the surface");
        }

        [Test]
        public void ARayFindsTheFrontOfAGlyph()
        {
            LetteraText3D text = Solid("HI");
            Bounds i = GlyphBounds(text, 1, false);
            Assert.IsTrue(text.Raycast(new Ray(new Vector3(i.center.x, i.center.y, -50), Vector3.forward), out LetteraSolidHit hit));
            Assert.AreEqual(1, hit.Character);
            Assert.AreEqual(LetteraSolidSurface.Face, hit.Anchor.Surface);
            Assert.AreEqual(-1, hit.Anchor.Layer);
            Assert.AreEqual(0, hit.Point.z, 1e-3f, "the front lies on the transform");
            Assert.AreEqual(50, hit.Distance, 1e-3f);
            Assert.Greater(Vector3.Dot(hit.Normal, Vector3.back), 0.99f);
            Assert.IsFalse(text.Raycast(new Ray(new Vector3(i.center.x, i.center.y, -50), Vector3.back), out _), "pointing away");
            Assert.IsFalse(text.Raycast(new Ray(new Vector3(i.center.x, i.center.y, -50), Vector3.forward), out _, 10), "too short");
            Assert.IsFalse(text.Raycast(new Ray(new Vector3(i.center.x, i.max.y + 50, -50), Vector3.forward), out _), "above the text");
        }

        [Test]
        public void ARayWorksInWorldSpace()
        {
            LetteraText3D text = Solid("I");
            text.transform.SetPositionAndRotation(new Vector3(100, 20, 5), Quaternion.Euler(0, 90, 0));
            text.transform.localScale = Vector3.one * 2;
            Bounds rest = GlyphBounds(text, 0, false);
            Vector3 target = text.transform.TransformPoint(new Vector3(rest.center.x, rest.center.y, 0));
            Vector3 toward = text.transform.forward;
            Assert.IsTrue(text.Raycast(new Ray(target - toward * 30, toward), out LetteraSolidHit hit));
            Assert.AreEqual(0, Vector3.Distance(target, hit.Point), 1e-3f);
            Assert.AreEqual(30, hit.Distance, 1e-3f);
            Assert.Greater(Vector3.Dot(hit.Normal, -toward), 0.99f);
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose pose));
            Assert.AreEqual(0, Vector3.Distance(target, pose.position), 1e-3f);
            Assert.Greater(Vector3.Dot(pose.up, -toward), 0.99f, "up is the outward normal");
        }

        [Test]
        public void APlaceFollowsMotion()
        {
            LetteraText3D text = Solid("<wave>abc</wave>");
            Bounds b = GlyphBounds(text, 1, true);
            Assert.IsTrue(text.Raycast(new Ray(new Vector3(b.center.x, b.max.y + 50, b.center.z), Vector3.down), out LetteraSolidHit hit));
            Assert.AreEqual(1, hit.Character);
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose before));
            Vector3 glyphBefore = GlyphBounds(text, 1, true).center;
            text.Engine.TickPose(0.3f, 0.3f);
            Vector3 moved = GlyphBounds(text, 1, true).center - glyphBefore;
            Assert.Greater(moved.magnitude, 0.05f, "the wave moved the glyph");
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose after));
            Assert.AreEqual(0, Vector3.Distance(after.position - before.position, moved), 1e-3f, "moved as its glyph did");
            AssertOnSurface(text, after.position, after.up, 20, "after motion");
        }

        [Test]
        public void APlaceFollowsDeformers()
        {
            LetteraText3D text = Solid("abI");
            text.AddDeformer(LetteraDeformer.Bend(10));
            text.ForceRebuild();
            Bounds c = GlyphBounds(text, 2, true);
            Assert.IsTrue(text.Raycast(new Ray(new Vector3(c.center.x, c.center.y, -80), Vector3.forward), out LetteraSolidHit hit));
            Assert.AreEqual(2, hit.Character);
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose before));
            text.SetDeformer(0, LetteraDeformer.Bend(120));
            text.ForceRebuild();
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose after));
            Assert.Greater(Vector3.Distance(before.position, after.position), 1f, "the bend carried it");
            Assert.Greater(Vector3.Angle(before.up, after.up), 10f, "and turned it with the surface");
            AssertOnSurface(text, after.position, after.up, 20, "after a bend");

            text.AddDeformer(LetteraDeformer.Twist(40));
            text.ForceRebuild();
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose twisted), "a second deformer");
            AssertOnSurface(text, twisted.position, twisted.up, 20, "after a twist");
        }

        [Test]
        public void APlaceSurvivesARebuildWithAnotherShape()
        {
            LetteraText3D text = Solid("ab");
            Bounds a = GlyphBounds(text, 0, true);
            Assert.IsTrue(text.Raycast(new Ray(new Vector3(a.center.x, a.center.y, -80), Vector3.forward), out LetteraSolidHit hit));
            // A deformer cuts the solids finer, so the triangle it lay in is no more.
            text.AddDeformer(LetteraDeformer.Wave(0.0f, 2));
            text.ForceRebuild();
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose pose), "found again at the nearest point");
            Assert.AreEqual(0, Vector3.Distance(pose.position, hit.Point), 0.02f);
            text.Text = "";
            text.ForceRebuild();
            Assert.IsFalse(text.TryGetPose(hit.Anchor, out _), "its glyph is gone");
        }

        [Test]
        public void ARayFindsLayersToo()
        {
            LetteraText3D text = Solid("o");
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.BackPlate));
            text.ForceRebuild();
            Bounds o = GlyphBounds(text, 0, true);
            // The middle of an o is its hole, where only the plate behind it stands.
            Assert.IsTrue(text.Raycast(new Ray(new Vector3(o.center.x, o.center.y, -80), Vector3.forward), out LetteraSolidHit hit));
            Assert.AreEqual(0, hit.Anchor.Layer);
            Assert.IsTrue(text.TryGetPose(hit.Anchor, out Pose pose));
            Assert.AreEqual(0, Vector3.Distance(pose.position, hit.Point), 1e-3f);
        }

        [Test]
        public void AnAnchorCanBeTakenByCharacter()
        {
            LetteraText3D text = Solid("xyz");
            Bounds y = GlyphBounds(text, 1, false);
            Assert.IsTrue(text.TryAnchorCharacter(1, y.center + new Vector3(0, 0, -100), out LetteraSolidAnchor anchor));
            Assert.AreEqual(1, anchor.Character);
            Assert.IsTrue(text.TryGetPose(anchor, out Pose pose, Space.Self));
            Assert.AreEqual(y.min.z, pose.position.z, 1e-3f, "nearest is the front");
            Assert.IsFalse(text.TryAnchorCharacter(7, Vector3.zero, out _));
        }
    }
}
