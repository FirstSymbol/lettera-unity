using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public partial class LetteraText3D
    {
        [SerializeField, Tooltip("Materials under a name, which a range takes with <surface=name>.")] List<LetteraSolidSurfaceSet> m_SurfaceSets = new List<LetteraSolidSurfaceSet>();

        ILetteraSolidPose m_SolidPose;

        /// <summary>Named materials a range takes with <c>&lt;surface=name&gt;</c>. After changing one in place, call RefreshSolids.</summary>
        public IReadOnlyList<LetteraSolidSurfaceSet> SurfaceSets => m_SurfaceSets;

        public void AddSurfaceSet(LetteraSolidSurfaceSet set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            m_SurfaceSets.Add(set);
            MarkSolidsDirty();
        }

        public void RemoveSurfaceSetAt(int index)
        {
            m_SurfaceSets.RemoveAt(index);
            MarkSolidsDirty();
        }

        public void ClearSurfaceSets()
        {
            if (m_SurfaceSets.Count == 0) return;
            m_SurfaceSets.Clear();
            MarkSolidsDirty();
        }

        /// <summary>The set of that name, ignoring case, or null.</summary>
        public LetteraSolidSurfaceSet FindSurfaceSet(string name)
        {
            foreach (LetteraSolidSurfaceSet set in m_SurfaceSets)
                if (set != null && string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                    return set;
            return null;
        }

        /// <summary>The material a surface of a range in <c>&lt;surface=name&gt;</c> ends up with: the set's, then the text's own.</summary>
        public Material GetRangeMaterial(string setName, LetteraSolidSurface surface)
        {
            LetteraSolidSurfaceSet set = FindSurfaceSet(setName);
            Material own = set?.Get(surface);
            return own != null ? own : GetSurfaceMaterial(surface);
        }

        /// <summary>
        /// Called for each glyph every frame it shows, to pose it in depth after its motions. While one is set, the text keeps a mesh of its own.
        /// </summary>
        public ILetteraSolidPose SolidPose
        {
            get => m_SolidPose;
            set
            {
                if (m_SolidPose == value) return;
                m_SolidPose = value;
                MarkSolidsDirty();
            }
        }

        // The submeshes for the materials ranges asked for, after the text's and its layers'.
        int RangeMapSlots => m_Builder != null ? 5 * m_Builder.Ranges.Maps.Count : 0;

        void ResolveRangeMaterials(int first)
        {
            if (m_Builder == null) return;
            IReadOnlyList<LetteraSolidRanges.SurfaceMap> maps = m_Builder.Ranges.Maps;
            for (int m = 0; m < maps.Count; m++)
                for (int s = 0; s < 5; s++)
                {
                    var surface = (LetteraSolidSurface)s;
                    Material material = (maps[m].Mask & (1 << s)) != 0 ? GetRangeMaterial(maps[m].Set, surface) : GetSurfaceMaterial(surface);
                    m_SurfaceToSubmesh[first + m * 5 + s] = Submesh(material);
                }
        }
    }
}
