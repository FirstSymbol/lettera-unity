using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FirstSymbol.Lettera.Editor;

namespace FirstSymbol.Lettera.Text3D.Tests
{
    /// <summary>Baking a solid text past a mesh asset: a static object, lightmap UVs, levels of detail, materials kept, FBX.</summary>
    public class SolidBakeTests : FirstSymbol.Lettera.Editor.Tests.TextFixture
    {
        const string Folder = "Assets/LetteraBakeTests";
        readonly List<Object> m_Made = new List<Object>();

        LetteraText3D Solid(string text)
        {
            var go = new GameObject("Baked Text", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = new Vector2(2000, 500);
            LetteraText3D solid = go.AddComponent<LetteraText3D>();
            solid.Font = Latin;
            solid.FontSize = 20;
            solid.WordWrap = false;
            solid.Text = text;
            solid.transform.SetPositionAndRotation(new Vector3(3, 4, 5), Quaternion.Euler(0, 30, 0));
            solid.ForceRebuild();
            m_Made.Add(go);
            return solid;
        }

        [SetUp]
        public void MakeFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "LetteraBakeTests");
        }

        [TearDown]
        public void Clean()
        {
            foreach (Object made in m_Made)
                if (made != null)
                    Object.DestroyImmediate(made);
            m_Made.Clear();
            AssetDatabase.DeleteAsset(Folder);
            LetteraSolidCache.Clear();
        }

        [Test]
        public void ABakedObjectIsStaticWithLightmapUvsLevelsAndItsMaterials()
        {
            LetteraText3D text = Solid("Baked");
            var runtime = new Material(Shader.Find("Lettera/Solid Lit")) { name = "Made By A Script", hideFlags = HideFlags.DontSave };
            m_Made.Add(runtime);
            text.FaceMaterial = runtime;
            text.ForceRebuild();
            int vertices = text.CurrentMesh.vertexCount;

            GameObject baked = LetteraText3DBake.Bake(text, Folder + "/Baked.asset", new LetteraBakeOptions { StaticObject = true, LightmapUvs = true, LodLevels = 3, DisableText = true });
            m_Made.Add(baked);
            Assert.IsNotNull(baked);
            Assert.IsFalse(text.gameObject.activeSelf, "the text steps aside");
            Assert.AreEqual(text.transform.position, baked.transform.position);
            Assert.AreEqual(text.transform.rotation, baked.transform.rotation);

            var lod0 = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Baked.asset");
            var lod1 = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Baked_LOD1.asset");
            var lod2 = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Baked_LOD2.asset");
            Assert.IsNotNull(lod0);
            Assert.IsNotNull(lod1);
            Assert.IsNotNull(lod2);
            Assert.GreaterOrEqual(lod0.vertexCount, vertices, "the full mesh, split where the unwrap needs");
            Assert.Less(lod1.vertexCount, lod0.vertexCount, "each level coarser");
            Assert.Less(lod2.vertexCount, lod1.vertexCount);

            LODGroup group = baked.GetComponent<LODGroup>();
            Assert.IsNotNull(group);
            LOD[] lods = group.GetLODs();
            Assert.AreEqual(3, lods.Length);
            Assert.AreSame(lod0, lods[0].renderers[0].GetComponent<MeshFilter>().sharedMesh);
            Assert.AreSame(lod2, lods[2].renderers[0].GetComponent<MeshFilter>().sharedMesh);

            foreach (Transform part in baked.GetComponentsInChildren<Transform>())
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(part.gameObject);
                Assert.IsTrue((flags & StaticEditorFlags.BatchingStatic) != 0, part.name);
                Assert.IsTrue((flags & StaticEditorFlags.ContributeGI) != 0, part.name + " takes baked light");
            }
            var uv2 = new List<Vector2>();
            lod0.GetUVs(1, uv2);
            Assert.AreEqual(lod0.vertexCount, uv2.Count);
            foreach (Vector2 uv in uv2) Assert.IsTrue(uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1, "unwrapped into the unit square");
            MeshRenderer renderer = lods[0].renderers[0] as MeshRenderer;
            Assert.AreEqual(ReceiveGI.Lightmaps, renderer.receiveGI);

            Material kept = renderer.sharedMaterials[0];
            Assert.IsTrue(EditorUtility.IsPersistent(kept), "a material made by a script is saved");
            StringAssert.StartsWith(Folder + "/", AssetDatabase.GetAssetPath(kept));
            Assert.AreEqual(runtime.shader, kept.shader);
            Assert.AreSame(kept, lods[1].renderers[0].sharedMaterials[0], "one copy for all levels");

            text.gameObject.SetActive(true);
            text.ForceRebuild();
            Assert.AreEqual(vertices, text.CurrentMesh.vertexCount, "the text is as it was");
        }

        [Test]
        public void APlainBakeMakesOnlyAssets()
        {
            LetteraText3D text = Solid("Plain");
            int objects = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Length;
            GameObject baked = LetteraText3DBake.Bake(text, Folder + "/Plain.asset", new LetteraBakeOptions { LodLevels = 1 });
            Assert.IsNull(baked);
            Assert.AreEqual(objects, Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Length);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Plain.asset");
            Assert.AreEqual(text.CurrentMesh.vertexCount, mesh.vertexCount);
            Assert.IsTrue(text.gameObject.activeSelf);
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:Mesh", new[] { Folder }).Length, "one mesh, no levels");
        }

        [Test]
        public void AStaticObjectWithoutLightmapUvsTakesNoBakedLight()
        {
            LetteraText3D text = Solid("Static");
            GameObject baked = LetteraText3DBake.Bake(text, Folder + "/Static.asset", new LetteraBakeOptions { StaticObject = true, LodLevels = 1 });
            m_Made.Add(baked);
            Assert.IsTrue(text.gameObject.activeSelf, "kept on unless asked");
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(baked);
            Assert.IsTrue((flags & StaticEditorFlags.BatchingStatic) != 0);
            Assert.IsTrue((flags & StaticEditorFlags.ContributeGI) == 0, "its second UVs are text space, which overlap");
            Assert.AreEqual(text.CurrentMesh.vertexCount, baked.GetComponent<MeshFilter>().sharedMesh.vertexCount);
            CollectionAssert.AreEqual(text.GetComponent<MeshRenderer>().sharedMaterials, baked.GetComponent<MeshRenderer>().sharedMaterials);
        }

        [Test]
        public void FbxIsWrittenWhenTheExporterIsThere()
        {
            LetteraText3D text = Solid("Fbx");
            GameObject baked = LetteraText3DBake.Bake(text, Folder + "/Fbx.asset", new LetteraBakeOptions { Fbx = true, LodLevels = 1 });
            m_Made.Add(baked);
            Assert.IsNotNull(baked, "the object FBX is written from");
            bool written = File.Exists(Folder + "/Fbx.fbx");
            Assert.AreEqual(LetteraText3DBake.FbxAvailable, written);
            if (!LetteraText3DBake.FbxAvailable) Assert.Pass("No FBX Exporter in this project: nothing written, nothing thrown.");
            AssetDatabase.ImportAsset(Folder + "/Fbx.fbx");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Fbx.fbx");
            Assert.IsNotNull(model);
            MeshFilter filter = model.GetComponentInChildren<MeshFilter>();
            Assert.IsNotNull(filter);
            Assert.Greater(filter.sharedMesh.vertexCount, 0);
        }
    }
}
