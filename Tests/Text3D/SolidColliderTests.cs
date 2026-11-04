#if LETTERA_PHYSICS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>Colliders that keep matching the drawn text: per glyph boxes and hulls, one mesh, and a compound body.</summary>
    public class SolidColliderTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        readonly List<GameObject> m_Made = new List<GameObject>();
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

        static List<Collider> All(LetteraText3DColliders colliders)
        {
            var list = new List<Collider>();
            colliders.GetColliders(list);
            return list;
        }

        // Every vertex of the character's glyph, in world space, lies inside its collider.
        static void AssertHolds(LetteraText3D text, Collider collider, int character, string what)
        {
            Physics.SyncTransforms();
            int checkedCount = 0;
            foreach (LetteraSolidMeshBuilder.Placement place in text.Builder.Placed)
            {
                if (place.Cluster != character || place.Map != 0) continue;
                for (int i = place.VertexStart; i < place.VertexStart + place.VertexCount; i += 7)
                {
                    Vector3 world = text.transform.TransformPoint(text.Builder.PosedVertices[i].Position);
                    Vector3 closest = collider.ClosestPoint(world);
                    Assert.AreEqual(0, Vector3.Distance(world, closest), 0.02f * Em, what + ": vertex " + i + " inside");
                    checkedCount++;
                }
            }
            Assert.Greater(checkedCount, 0, what);
        }

        static float Volume(Collider collider) => collider.bounds.size.x * collider.bounds.size.y * collider.bounds.size.z;

        [Test]
        public void ABoxPerGlyphHoldsItsGlyph()
        {
            LetteraText3D text = Solid("Ab c");
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            List<Collider> list = All(colliders);
            Assert.AreEqual(3, list.Count, "none for the space");
            int[] characters = { 0, 1, 3 };
            for (int i = 0; i < list.Count; i++)
            {
                Assert.IsInstanceOf<BoxCollider>(list[i]);
                Assert.AreEqual(characters[i], colliders.CharacterOf(list[i]));
                Assert.AreEqual(HideFlags.HideAndDontSave, list[i].gameObject.hideFlags);
                AssertHolds(text, list[i], characters[i], "box " + i);
                Bounds glyph = SolidAnchorTests.GlyphBounds(text, characters[i], true);
                Bounds box = list[i].bounds;
                Assert.Less(box.size.x * box.size.y, glyph.size.x * glyph.size.y * 1.05f, "tight around its glyph");
            }
        }

        [Test]
        public void ARayHitsTheGlyphItAims()
        {
            LetteraText3D text = Solid("Hi");
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            Physics.SyncTransforms();
            Bounds h = SolidAnchorTests.GlyphBounds(text, 0, true);
            Vector3 aim = text.transform.TransformPoint(h.center);
            Assert.IsTrue(Physics.Raycast(aim + Vector3.back * 100, Vector3.forward, out RaycastHit hit, 200));
            Assert.AreEqual(0, colliders.CharacterOf(hit.collider));
            Assert.IsTrue(text.Raycast(new Ray(aim + Vector3.back * 100, Vector3.forward), out LetteraSolidHit exact), "the exact ray, from the same ray");
            Assert.AreEqual(0, exact.Character);
        }

        [Test]
        public void HullsAreCloserThanBoxes()
        {
            LetteraText3D text = Solid("A");
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            float box = Volume(All(colliders)[0]);
            colliders.Shape = LetteraSolidColliderShape.Hulls;
            List<Collider> list = All(colliders);
            Assert.AreEqual(1, list.Count);
            var hull = (MeshCollider)list[0];
            Assert.IsTrue(hull.convex);
            AssertHolds(text, hull, 0, "hull");
            // A ray through the corner beside an A's leg meets its box, not its hull.
            Physics.SyncTransforms();
            Bounds a = SolidAnchorTests.GlyphBounds(text, 0, true);
            Vector3 corner = text.transform.TransformPoint(new Vector3(a.min.x + a.size.x * 0.05f, a.max.y - a.size.y * 0.05f, a.center.z));
            Assert.IsFalse(hull.Raycast(new Ray(corner + Vector3.back * 100, Vector3.forward), out _, 200), "the hull leaves the corner open");
            colliders.Shape = LetteraSolidColliderShape.Boxes;
            Assert.IsTrue(All(colliders)[0].Raycast(new Ray(corner + Vector3.back * 100, Vector3.forward), out _, 200), "the box does not");
            Assert.Greater(box, 0);
        }

        [Test]
        public void OneMeshIsTheDrawnMesh()
        {
            LetteraText3D text = Solid("Hey");
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            colliders.Shape = LetteraSolidColliderShape.Mesh;
            List<Collider> list = All(colliders);
            Assert.AreEqual(1, list.Count);
            var mesh = (MeshCollider)list[0];
            Assert.AreSame(text.CurrentMesh, mesh.sharedMesh);
            Assert.AreEqual(1, colliders.ColliderCount);
            Physics.SyncTransforms();
            Bounds h = SolidAnchorTests.GlyphBounds(text, 0, true);
            Vector3 aim = text.transform.TransformPoint(h.center);
            Assert.IsTrue(text.Raycast(new Ray(aim + Vector3.back * 100, Vector3.forward), out LetteraSolidHit exact));
            Assert.IsTrue(mesh.Raycast(new Ray(aim + Vector3.back * 100, Vector3.forward), out RaycastHit hit, 200));
            Assert.AreEqual(exact.Distance, hit.distance, 1e-2f);
        }

        [Test]
        public void CollidersFollowMotionAndDeformers()
        {
            LetteraText3D text = Solid("<wave>abc</wave>");
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            Physics.SyncTransforms();
            List<Collider> list = All(colliders);
            Vector3 before = list[1].bounds.center;
            text.Engine.TickPose(0.3f, 0.3f);
            Physics.SyncTransforms();
            Assert.Greater(Vector3.Distance(before, list[1].bounds.center), 0.05f, "moved with the wave");
            AssertHolds(text, list[1], 1, "after motion");

            text.Text = "abc";
            text.AddDeformer(LetteraDeformer.Bend(160));
            text.ForceRebuild();
            list = All(colliders);
            Assert.AreEqual(3, list.Count);
            for (int i = 0; i < 3; i++) AssertHolds(text, list[i], i, "bent box " + i);
            colliders.Shape = LetteraSolidColliderShape.Hulls;
            list = All(colliders);
            for (int i = 0; i < 3; i++) AssertHolds(text, list[i], i, "bent hull " + i);
        }

        [Test]
        public void ARigidbodyTakesThemAsOneCompoundBody()
        {
            LetteraText3D text = Solid("Lettera");
            Rigidbody body = text.gameObject.AddComponent<Rigidbody>();
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            foreach (Collider collider in All(colliders)) Assert.AreSame(body, collider.attachedRigidbody);
            Assert.AreEqual(7, colliders.ColliderCount);
            colliders.Shape = LetteraSolidColliderShape.Hulls;
            foreach (Collider collider in All(colliders)) Assert.AreSame(body, collider.attachedRigidbody);
        }

        [Test]
        public void LayersCollideWhenAsked()
        {
            LetteraText3D text = Solid("ab");
            text.AddLayer(LetteraSolidLayer.Preset(LetteraSolidLayerPreset.BackPlate));
            text.ForceRebuild();
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            int with = colliders.ColliderCount;
            Assert.Greater(with, 2);
            colliders.IncludeLayers = false;
            Assert.AreEqual(2, colliders.ColliderCount);
            colliders.IsTrigger = true;
            foreach (Collider collider in All(colliders)) Assert.IsTrue(collider.isTrigger);
        }

        [Test]
        public void TurningItOffTakesThemAway()
        {
            LetteraText3D text = Solid("abc");
            int children = text.transform.childCount;
            LetteraText3DColliders colliders = text.gameObject.AddComponent<LetteraText3DColliders>();
            Assert.AreEqual(children + 1, text.transform.childCount);
            Object.DestroyImmediate(colliders);
            Assert.AreEqual(children, text.transform.childCount);
        }
    }
}
#endif
