using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>What a bake makes besides the mesh asset.</summary>
    internal struct LetteraBakeOptions
    {
        /// <summary>An object in the scene that draws the baked mesh, marked static.</summary>
        public bool StaticObject;
        /// <summary>A second UV set unwrapped for lightmaps, and the object lightmapped.</summary>
        public bool LightmapUvs;
        /// <summary>Above one, coarser meshes for distance in a level-of-detail group.</summary>
        public int LodLevels;
        /// <summary>The text itself turned off once its baked object stands in.</summary>
        public bool DisableText;
        /// <summary>The baked object also written as FBX, when the FBX Exporter package is in the project.</summary>
        public bool Fbx;

        public static LetteraBakeOptions Default => new LetteraBakeOptions { StaticObject = true, LodLevels = 1, DisableText = true };
    }

    /// <summary>Solid text baked out of the text system: a mesh asset, a static object, lightmap UVs, levels of detail and FBX.</summary>
    internal static class LetteraText3DBake
    {
        /// <summary>Set by the optional FBX assembly, which compiles only with the FBX Exporter package: object, path, written path.</summary>
        internal static Func<GameObject, string, string> FbxExport;

        internal static bool FbxAvailable => FbxExport != null;

        /// <summary>Writes the meshes and any material that lives only in memory beside meshPath, and makes the object asked for.</summary>
        internal static GameObject Bake(LetteraText3D text, string meshPath, LetteraBakeOptions options)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            text.ForceRebuild();
            if (text.CurrentMesh == null) return null;
            string folder = Path.GetDirectoryName(meshPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(meshPath);
            int levels = Mathf.Clamp(options.LodLevels, 1, 4);
            var meshes = new List<Mesh> { SaveMesh(text.CurrentMesh, meshPath, options.LightmapUvs) };
            for (int level = 1; level < levels; level++)
                meshes.Add(SaveMesh(Coarser(text, level), folder + "/" + name + "_LOD" + level + ".asset", options.LightmapUvs, true));
            if (levels > 1) text.ForceRebuild();
            Material[] materials = SaveMaterials(text.GetComponent<MeshRenderer>().sharedMaterials, folder, name);
            if (options.LightmapUvs && UsesTextSpace(materials))
                Debug.LogWarning("Lettera: lightmap UVs take the second UV set, where a material mapped across the whole text read its texture from. " + name + " maps it per surface now.");
            AssetDatabase.SaveAssets();
            if (!options.StaticObject && levels == 1 && !options.Fbx) return null;

            Undo.SetCurrentGroupName("Bake Text 3D");
            int group = Undo.GetCurrentGroup();
            var root = new GameObject(text.name + " (Baked)");
            Transform source = text.transform;
            root.transform.SetParent(source.parent, false);
            root.transform.SetPositionAndRotation(source.position, source.rotation);
            root.transform.localScale = source.localScale;
            root.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);
            root.layer = text.gameObject.layer;
            MeshRenderer from = text.GetComponent<MeshRenderer>();
            if (levels == 1)
            {
                Dress(root, meshes[0], materials, from, options.LightmapUvs);
            }
            else
            {
                var lods = new LOD[levels];
                for (int level = 0; level < levels; level++)
                {
                    var child = new GameObject(name + "_LOD" + level) { layer = root.layer };
                    child.transform.SetParent(root.transform, false);
                    MeshRenderer renderer = Dress(child, meshes[level], materials, from, options.LightmapUvs);
                    // Each level holds until the text is half as tall on screen; the last until it is a sliver.
                    float height = level == levels - 1 ? 0.02f : 0.5f / (1 << level);
                    lods[level] = new LOD(height, new Renderer[] { renderer });
                }
                LODGroup lodGroup = root.AddComponent<LODGroup>();
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }
            if (options.StaticObject)
            {
                StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                // Without its own unwrap a mesh would be lightmapped by the text-space UVs, which overlap.
                if (options.LightmapUvs) flags |= StaticEditorFlags.ContributeGI;
                foreach (Transform part in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(part.gameObject, flags);
            }
            Undo.RegisterCreatedObjectUndo(root, "Bake Text 3D");
            if (options.DisableText)
            {
                Undo.RecordObject(text.gameObject, "Bake Text 3D");
                text.gameObject.SetActive(false);
            }
            Undo.CollapseUndoOperations(group);
            if (options.Fbx && FbxExport != null) FbxExport(root, folder + "/" + name + ".fbx");
            return root;
        }

        static MeshRenderer Dress(GameObject go, Mesh mesh, Material[] materials, MeshRenderer from, bool lightmapped)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            if (from != null)
            {
                renderer.shadowCastingMode = from.shadowCastingMode;
                renderer.receiveShadows = from.receiveShadows;
                renderer.lightProbeUsage = from.lightProbeUsage;
                renderer.reflectionProbeUsage = from.reflectionProbeUsage;
                renderer.renderingLayerMask = from.renderingLayerMask;
            }
            renderer.receiveGI = lightmapped ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            return renderer;
        }

        static Mesh SaveMesh(Mesh mesh, string path, bool lightmapUvs, bool owned = false)
        {
            Mesh copy = owned ? mesh : Object.Instantiate(mesh);
            copy.hideFlags = HideFlags.None;
            copy.name = Path.GetFileNameWithoutExtension(path);
            if (lightmapUvs) Unwrapping.GenerateSecondaryUVSet(copy);
            copy.RecalculateBounds();
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        // The same text with coarser curves and fewer bevel steps, each level twice as coarse again.
        static Mesh Coarser(LetteraText3D text, int level)
        {
            float tolerance = text.CurveTolerance, detail = text.DeformDetail;
            LetteraBevel front = text.FrontBevel, back = text.BackBevel;
            bool share = text.ShareMesh;
            try
            {
                text.ShareMesh = false;
                text.CurveTolerance = tolerance * (1 << (2 * level));
                text.DeformDetail = detail * (1 << level);
                LetteraBevel f = front, b = back;
                f.Segments = Mathf.Max(1, front.Segments >> level);
                b.Segments = Mathf.Max(1, back.Segments >> level);
                text.FrontBevel = f;
                text.BackBevel = b;
                text.ForceRebuild();
                return Object.Instantiate(text.CurrentMesh);
            }
            finally
            {
                text.CurveTolerance = tolerance;
                text.DeformDetail = detail;
                text.FrontBevel = front;
                text.BackBevel = back;
                text.ShareMesh = share;
            }
        }

        /// <summary>Materials with no asset of their own, made by a script or a fallback, are saved beside the mesh so the bake keeps them.</summary>
        static Material[] SaveMaterials(Material[] materials, string folder, string name)
        {
            var saved = new Material[materials.Length];
            var done = new Dictionary<Material, Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || EditorUtility.IsPersistent(material))
                {
                    saved[i] = material;
                    continue;
                }
                if (!done.TryGetValue(material, out Material copy))
                {
                    copy = new Material(material) { hideFlags = HideFlags.None };
                    string safe = string.Join("_", material.name.Split(Path.GetInvalidFileNameChars())).Trim();
                    copy.name = name + " " + (safe.Length > 0 ? safe : "Material " + i);
                    AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + copy.name + ".mat"));
                    done[material] = copy;
                }
                saved[i] = copy;
            }
            return saved;
        }

        static bool UsesTextSpace(Material[] materials)
        {
            foreach (Material material in materials)
                if (material != null && material.HasProperty("_TextureSpace") && material.GetFloat("_TextureSpace") > 0.5f)
                    return true;
            return false;
        }

        [MenuItem("CONTEXT/LetteraText3D/Bake...")]
        static void Open(MenuCommand command) => LetteraBakeWindow.Open((LetteraText3D)command.context);
    }
}
