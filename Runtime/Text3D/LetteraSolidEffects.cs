using System;
using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Gives a range a depth, as a multiple of the text's. Only a solid text asks, so on flat text it does nothing.</summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SolidDepthEffect : LetteraEffect
    {
        static readonly LetteraParamInfo[] s_Slots =
        {
            LetteraParamInfo.ForSlot(0, "Depth", LetteraParamKind.Float, "How deep the range is, as a multiple of the text's depth. 0 is flat.", 0, 100, 0.1f, 2, defaultText: "no change", essential: true, example: "2"),
        };

        static readonly LetteraTagExample[] s_Examples = { new LetteraTagExample("2", "Twice as deep"), new LetteraTagExample("0.5", "Half as deep") };

        public override string Description => "Gives a range its own depth. Only a solid text shows it; flat text ignores it.";
        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Slots;
        public override IReadOnlyList<LetteraTagExample> Examples => s_Examples;
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
        static readonly LetteraParamInfo[] s_Slots =
        {
            LetteraParamInfo.ForSlot(0, "Shape", LetteraParamKind.Choice, "The profile of the edge. Without a shape the tag does nothing.", choices: Enum.GetNames(typeof(LetteraBevelShape)), defaultText: "none, the range is left alone", essential: true),
            LetteraParamInfo.ForSlot(1, "Width", LetteraParamKind.Float, "How far the edge reaches into the face, 0 to 1.", 0, 1, 0.05f, 2, defaultText: "the default bevel's", example: "0.3"),
            LetteraParamInfo.ForSlot(2, "Height", LetteraParamKind.Float, "How far the edge stands out, -1 to 1. Negative cuts in.", -1, 1, 0.05f, 2, defaultText: "the default bevel's", example: "0.5"),
        };

        static readonly LetteraTagExample[] s_Examples = { new LetteraTagExample("Round", "A rounded edge"), new LetteraTagExample("Chamfer,0.3,0.5", "A narrow chamfer standing out") };

        public override string Description => "Gives a range its own edge shape. Only a solid text shows it.";
        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Slots;
        public override IReadOnlyList<LetteraTagExample> Examples => s_Examples;
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
        static LetteraParamInfo Amount(string name, string hint, string example, string unit, string defaultText) =>
            LetteraParamInfo.ForSlot(0, name, LetteraParamKind.Float, hint, step: unit == "\u00b0" ? 5 : 0.05f, decimals: 2, defaultText: defaultText, essential: true, example: example, unitLabel: unit);

        static LetteraParamInfo Axis(int slot, params string[] choices) =>
            LetteraParamInfo.ForSlot(slot, "Axis", LetteraParamKind.Choice, "Which way the deformer moves the text: Depth towards and away from the viewer, Up and down in the text's plane.", choices: choices, defaultText: choices[0]);

        static readonly LetteraParamInfo[] s_Bend = { Amount("Angle", "How far the text curves, in degrees. 360 closes it in a ring.", "90", "\u00b0", "no change"), Axis(1, "Depth", "Up") };
        static readonly LetteraParamInfo[] s_Twist = { Amount("Angle", "How far the far end is turned about the reading line, in degrees.", "180", "\u00b0", "no change"), Axis(1, "Depth", "Up") };
        static readonly LetteraParamInfo[] s_Taper = { Amount("Amount", "How much the text narrows towards its end: 0 none, 1 to a point. Negative widens it.", "0.5", null, "no change"), Axis(1, "Up", "Depth", "Both") };
        static readonly LetteraParamInfo[] s_Sphere = { Amount("Angle", "How much of a ball the text wraps onto, in degrees.", "120", "\u00b0", "no change"), Axis(1, "Depth", "Up") };
        static readonly LetteraParamInfo[] s_Wave =
        {
            Amount("Height", "How high the wave rises, in em.", "0.1", "em", "no change"),
            LetteraParamInfo.ForSlot(1, "Length", LetteraParamKind.Float, "The length of one wave, in em. At least 0.01.", min: 0.01f, step: 0.1f, decimals: 2, defaultText: "1", example: "1.5", unitLabel: "em"),
            Axis(2, "Depth", "Up", "Both"),
        };
        static readonly LetteraParamInfo[] s_Other = { Amount("Amount", "The size of the deformation.", "1", null, "no change"), Axis(1, "Depth", "Up") };
        static readonly LetteraTagExample[] s_OtherExamples = { new LetteraTagExample("1", "A deformation of size 1") };

        static readonly LetteraTagExample[] s_BendExamples = { new LetteraTagExample("90", "A quarter ring"), new LetteraTagExample("60,Up", "An arc in the text's plane") };
        static readonly LetteraTagExample[] s_TwistExamples = { new LetteraTagExample("180", "A half turn along the text") };
        static readonly LetteraTagExample[] s_TaperExamples = { new LetteraTagExample("0.5", "Half as wide at the end") };
        static readonly LetteraTagExample[] s_WaveExamples = { new LetteraTagExample("0.1,1.5", "A ripple 0.1 em high and 1.5 em long") };
        static readonly LetteraTagExample[] s_SphereExamples = { new LetteraTagExample("120", "Wrapped part way round a ball") };

        public override string Description
        {
            get
            {
                switch (Kind)
                {
                    case LetteraDeformKind.Bend: return "Curves the text along its width. Only a solid text shows it.";
                    case LetteraDeformKind.Twist: return "Turns the text about its reading line, more the further along. Only a solid text shows it.";
                    case LetteraDeformKind.Taper: return "Narrows the text towards its end. Only a solid text shows it.";
                    case LetteraDeformKind.Wave: return "A sine wave along the width of the text. Only a solid text shows it.";
                    case LetteraDeformKind.Sphere: return "Wraps the text onto a ball. Only a solid text shows it.";
                    default: return "Deforms the text of a solid text.";
                }
            }
        }

        public override IReadOnlyList<LetteraParamInfo> Parameters
        {
            get
            {
                switch (Kind)
                {
                    case LetteraDeformKind.Bend: return s_Bend;
                    case LetteraDeformKind.Twist: return s_Twist;
                    case LetteraDeformKind.Taper: return s_Taper;
                    case LetteraDeformKind.Wave: return s_Wave;
                    case LetteraDeformKind.Sphere: return s_Sphere;
                    default: return s_Other;
                }
            }
        }

        public override IReadOnlyList<LetteraTagExample> Examples
        {
            get
            {
                switch (Kind)
                {
                    case LetteraDeformKind.Bend: return s_BendExamples;
                    case LetteraDeformKind.Twist: return s_TwistExamples;
                    case LetteraDeformKind.Taper: return s_TaperExamples;
                    case LetteraDeformKind.Wave: return s_WaveExamples;
                    case LetteraDeformKind.Sphere: return s_SphereExamples;
                    default: return s_OtherExamples;
                }
            }
        }

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
