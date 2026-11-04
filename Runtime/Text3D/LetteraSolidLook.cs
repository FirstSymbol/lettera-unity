using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>A depth, edges and materials to give a solid text at once.</summary>
    [CreateAssetMenu(menuName = "Lettera/Solid Look", fileName = "New Solid Look")]
    public partial class LetteraSolidLook : ScriptableObject
    {
        [SerializeField, HideInInspector] internal int LetteraSerializedVersion;

        [SerializeField, Tooltip("How deep the text is, in em.")] float m_Depth = 0.15f;
        [SerializeField] LetteraBevel m_FrontBevel = LetteraBevel.Default;
        [SerializeField] LetteraBevel m_BackBevel = LetteraBevel.Default;
        [SerializeField] bool m_MirrorBevel = true;
        [SerializeField] Material m_FaceMaterial, m_BevelMaterial, m_SideMaterial, m_BackBevelMaterial, m_BackMaterial;
        [SerializeField, Tooltip("Extra solids a text takes with this look. None leaves a text's own as they are.")] List<LetteraSolidLayer> m_Layers = new List<LetteraSolidLayer>();

        public float Depth { get => m_Depth; set => m_Depth = value; }
        public LetteraBevel FrontBevel { get => m_FrontBevel; set => m_FrontBevel = value; }
        public LetteraBevel BackBevel { get => m_BackBevel; set => m_BackBevel = value; }
        public bool MirrorBevel { get => m_MirrorBevel; set => m_MirrorBevel = value; }
        public Material FaceMaterial { get => m_FaceMaterial; set => m_FaceMaterial = value; }
        public Material BevelMaterial { get => m_BevelMaterial; set => m_BevelMaterial = value; }
        public Material SideMaterial { get => m_SideMaterial; set => m_SideMaterial = value; }
        public Material BackBevelMaterial { get => m_BackBevelMaterial; set => m_BackBevelMaterial = value; }
        public Material BackMaterial { get => m_BackMaterial; set => m_BackMaterial = value; }
        /// <summary>Extra solids a text takes with this look, as copies. An empty list leaves a text's own as they are.</summary>
        public List<LetteraSolidLayer> Layers => m_Layers;

        /// <summary>Gives this look to a text.</summary>
        public void ApplyTo(LetteraText3D text)
        {
            if (text == null) return;
            text.Depth = m_Depth;
            text.FrontBevel = m_FrontBevel;
            text.BackBevel = m_BackBevel;
            text.MirrorBevel = m_MirrorBevel;
            text.FaceMaterial = m_FaceMaterial;
            text.BevelMaterial = m_BevelMaterial;
            text.SideMaterial = m_SideMaterial;
            text.BackBevelMaterial = m_BackBevelMaterial;
            text.BackMaterial = m_BackMaterial;
            if (m_Layers.Count == 0) return;
            text.ClearLayers();
            foreach (LetteraSolidLayer layer in m_Layers)
                if (layer != null)
                    text.AddLayer(layer.Clone());
        }
    }

    /// <summary>Reserved for depth motion, deformers and other poses a later phase adds. Nothing calls it yet.</summary>
    public interface ILetteraSolidPose
    {
        /// <summary>Called for each glyph's vertices, with the glyph's centre.</summary>
        void Pose(int glyph, Vector3 centre, ref Matrix4x4 transform);
    }
}
