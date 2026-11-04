using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Gives a range a depth, as a multiple of the text's. Only a solid text asks, so on flat text it does nothing.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SolidDepthEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            if (!float.TryParse(slots[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float multiple)) return;
            attributes.AddExtra(LetteraSolidEffects.DepthKey, start, end, Mathf.Clamp(multiple, 0, 100), null);
        }
    }

    /// <summary>Gives a range its own edge: a shape, a width and a height.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SolidBevelEffect : LetteraEffect
    {
        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            if (!Enum.TryParse(slots[0], true, out LetteraBevelShape shape)) return;
            LetteraBevel bevel = LetteraBevel.Default;
            bevel.Shape = shape;
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (float.TryParse(slots[1], System.Globalization.NumberStyles.Float, invariant, out float width)) bevel.Width = Mathf.Clamp(width, 0, 1);
            if (float.TryParse(slots[2], System.Globalization.NumberStyles.Float, invariant, out float height)) bevel.Height = Mathf.Clamp(height, -1, 1);
            attributes.AddExtra(LetteraSolidEffects.BevelKey, start, end, 0, bevel);
        }
    }

    /// <summary>
    /// Deforms a range of a solid text by its own box: <c>&lt;bend=90&gt;</c>, <c>&lt;bend=60,up&gt;</c>, <c>&lt;twist=180&gt;</c>, <c>&lt;taper=0.5&gt;</c>,
    /// <c>&lt;ripple=0.1,1.5&gt;</c> for a wave of a height and a length in em, <c>&lt;sphere=120&gt;</c>. It runs before the text's own chain.
    /// </summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SolidDeformEffect : LetteraEffect
    {
        public LetteraDeformKind Kind;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(slots[0], System.Globalization.NumberStyles.Float, invariant, out float amount)) return;
            var deformer = new LetteraDeformer { Kind = Kind, Amount = amount, Center = 0.5f, Axis = Kind == LetteraDeformKind.Taper ? LetteraDeformAxis.Up : LetteraDeformAxis.Depth, Wavelength = 1 };
            string second = slots[1];
            if (Kind == LetteraDeformKind.Wave)
            {
                if (float.TryParse(second, System.Globalization.NumberStyles.Float, invariant, out float length)) deformer.Wavelength = Mathf.Max(length, 0.01f);
                if (Enum.TryParse(slots[2], true, out LetteraDeformAxis waveAxis)) deformer.Axis = waveAxis;
            }
            else if (Enum.TryParse(second, true, out LetteraDeformAxis axis))
            {
                deformer.Axis = axis;
            }
            attributes.AddExtra(LetteraSolidEffects.DeformKey, start, end, 0, deformer);
        }
    }

    /// <summary>The range effects only a solid text has, added to its own styles first, so that the shared vocabulary stays as it was.</summary>
    internal static class LetteraSolidEffects
    {
        internal static readonly object DepthKey = new object(), BevelKey = new object(), DeformKey = new object();

        static List<LetteraStyle> s_Styles;

        // Shared by all solid texts, and never written into a text's own list: a scene saves what its owner put there.
        internal static IReadOnlyList<LetteraStyle> Styles => s_Styles ?? (s_Styles = WithRanges(new List<LetteraStyle>
        {
            LetteraStyle.ForTag(new SolidDepthEffect(), "depth"),
            LetteraStyle.ForTag(new SolidBevelEffect(), "bevel"),
            LetteraStyle.ForTag(new SolidDeformEffect { Kind = LetteraDeformKind.Bend }, "bend"),
            LetteraStyle.ForTag(new SolidDeformEffect { Kind = LetteraDeformKind.Twist }, "twist"),
            LetteraStyle.ForTag(new SolidDeformEffect { Kind = LetteraDeformKind.Taper }, "taper"),
            LetteraStyle.ForTag(new SolidDeformEffect { Kind = LetteraDeformKind.Wave }, "ripple"),
            LetteraStyle.ForTag(new SolidDeformEffect { Kind = LetteraDeformKind.Sphere }, "sphere"),
        }));

        static List<LetteraStyle> WithRanges(List<LetteraStyle> styles)
        {
            LetteraSolidRanges.AddStyles(styles);
            return styles;
        }

        internal static bool HasDeform(TextEngine engine)
        {
            ValueList<LetteraAttributes.ExtraRange> extras = engine.ExtrasForSolids;
            for (int i = 0; i < extras.Count; i++)
                if (extras[i].Key == DeformKey)
                    return true;
            return false;
        }

        // What a range's effects change for one glyph. Nearly every text has none of these, and pays one count.
        internal static void Read(TextEngine engine, int cluster, ref LetteraSolidRecipe recipe, ref float depth)
        {
            ValueList<LetteraAttributes.ExtraRange> extras = engine.ExtrasForSolids;
            if (extras.Count == 0) return;
            for (int i = 0; i < extras.Count; i++)
            {
                LetteraAttributes.ExtraRange extra = extras[i];
                if (cluster < extra.Start || cluster >= extra.End) continue;
                if (extra.Key == DepthKey)
                {
                    depth *= extra.Value;
                }
                else if (extra.Key == BevelKey && extra.Payload is LetteraBevel bevel)
                {
                    recipe.Front = bevel;
                    recipe.Back = bevel;
                }
            }
        }
    }

    /// <summary>A hidden child for what a solid cannot be: color glyphs and sprites, flat on the front plane. Made only when one exists, and never saved.</summary>
    internal static class LetteraSolidFlatChild
    {
        const string Name = "Lettera Flat";

        internal static void Update(LetteraText3D text)
        {
            TextEngine engine = text.Engine;
            bool wanted = text.ColorGlyphs == LetteraSolidColorGlyphs.Flat && engine.HasFlatForSolids;
            Transform child = text.transform.Find(Name);
            if (!wanted)
            {
                if (child != null) Destroy(child.gameObject);
                return;
            }
            if (child == null)
            {
                var go = new GameObject(Name, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.HideAndDontSave };
                child = go.transform;
                child.SetParent(text.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = new Mesh { name = Name, hideFlags = HideFlags.HideAndDontSave };
            }
            float depth = text.Pivot == LetteraSolidPivot.Front ? 0 : text.Pivot == LetteraSolidPivot.Back ? -text.Depth : -text.Depth * 0.5f;
            child.localPosition = new Vector3(0, 0, depth * text.FontSize - 0.001f * text.FontSize);
            engine.FillFlatMesh(child.GetComponent<MeshFilter>().sharedMesh, child.GetComponent<MeshRenderer>());
        }

        internal static void Remove(LetteraText3D text)
        {
            Transform child = text.transform.Find(Name);
            if (child != null) Destroy(child.gameObject);
        }

        static void Destroy(GameObject go)
        {
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying)
            {
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
