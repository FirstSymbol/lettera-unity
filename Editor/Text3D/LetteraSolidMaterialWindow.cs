using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    internal sealed class LetteraSolidMaterialWindow : EditorWindow
    {
        [SerializeField] Material m_Material;

        internal static void Open(Material material)
        {
            var window = GetWindow<LetteraSolidMaterialWindow>(true, "Lettera Solid Material");
            window.m_Material = material;
            window.minSize = new Vector2(360, 300);
            window.Build();
        }

        void OnEnable() => Build();

        void Build()
        {
            rootVisualElement.Clear();
            if (m_Material == null) return;
            VisualElement root = LtUI.Root();
            rootVisualElement.Add(root);
            VisualElement page = LtFrame.Header(root, m_Material.name, "Lettera/Solid Lit");
            void Changed(string what)
            {
                Undo.RecordObject(m_Material, what);
                EditorUtility.SetDirty(m_Material);
            }
            VisualElement Slider(string label, string property, string tip)
            {
                var slider = new UnityEngine.UIElements.Slider(0, 1) { value = m_Material.GetFloat(property), showInputField = true };
                slider.RegisterValueChangedCallback(e =>
                {
                    Changed(label);
                    m_Material.SetFloat(property, e.newValue);
                });
                return LtUI.Row(label, slider, tip);
            }
            VisualElement ColorRow(string label, string property, bool hdr)
            {
                var color = new UnityEditor.UIElements.ColorField { value = m_Material.GetColor(property), hdr = hdr };
                color.RegisterValueChangedCallback(e =>
                {
                    Changed(label);
                    m_Material.SetColor(property, e.newValue);
                });
                return LtUI.Row(label, color);
            }
            var texture = new UnityEditor.UIElements.ObjectField { objectType = typeof(Texture), allowSceneObjects = false, value = m_Material.GetTexture("_BaseMap") };
            texture.RegisterValueChangedCallback(e =>
            {
                Changed("Texture");
                m_Material.SetTexture("_BaseMap", e.newValue as Texture);
            });
            var space = new UnityEngine.UIElements.Toggle { value = m_Material.GetFloat("_TextureSpace") > 0.5f };
            space.RegisterValueChangedCallback(e =>
            {
                Changed("Texture Space");
                m_Material.SetFloat("_TextureSpace", e.newValue ? 1 : 0);
            });
            var instancing = new UnityEngine.UIElements.Toggle { value = m_Material.enableInstancing };
            instancing.RegisterValueChangedCallback(e =>
            {
                Changed("GPU Instancing");
                m_Material.enableInstancing = e.newValue;
            });
            page.Add(LtUI.Card("Color", ColorRow("Tint", "_BaseColor", false), LtUI.Row("Texture", texture), LtUI.Row("Across The Text", space, "Off: each surface maps on its own. On: one mapping across the whole text.")));
            page.Add(LtUI.Card("Surface", Slider("Metallic", "_Metallic", null), Slider("Smoothness", "_Smoothness", null), ColorRow("Emission", "_EmissionColor", true), Slider("Light Influence", "_LightInfluence", "0 is unlit, 1 is fully lit.")));
            page.Add(LtUI.Card("Drawing", LtUI.Row("GPU Instancing", instancing, "Identical texts sharing one mesh draw as one batch.")));
        }
    }
}
