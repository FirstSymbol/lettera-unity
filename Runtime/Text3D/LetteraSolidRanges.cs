using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>Materials under a name, which a range of a solid text takes with <c>&lt;surface=name&gt;</c>. An empty slot falls back as the text's do, and then to the text's own.</summary>
    [Serializable]
    public class LetteraSolidSurfaceSet
    {
        public string Name = "";
        public Material Face, Bevel, Side, BackBevel, Back;

        public LetteraSolidSurfaceSet() { }

        public LetteraSolidSurfaceSet(string name, Material face) : this()
        {
            Name = name;
            Face = face;
        }

        /// <summary>A set with a look's materials.</summary>
        public static LetteraSolidSurfaceSet FromLook(string name, LetteraSolidLook look) => new LetteraSolidSurfaceSet
        {
            Name = name,
            Face = look.FaceMaterial,
            Bevel = look.BevelMaterial,
            Side = look.SideMaterial,
            BackBevel = look.BackBevelMaterial,
            Back = look.BackMaterial,
        };

        /// <summary>This set's own material for a surface, through its fallbacks, or null where it has none.</summary>
        public Material Get(LetteraSolidSurface surface)
        {
            switch (surface)
            {
                case LetteraSolidSurface.Bevel: return Bevel != null ? Bevel : Face;
                case LetteraSolidSurface.Side: return Side != null ? Side : Face;
                case LetteraSolidSurface.BackBevel: return BackBevel != null ? BackBevel : Get(LetteraSolidSurface.Bevel);
                case LetteraSolidSurface.Back: return Back != null ? Back : Face;
                default: return Face;
            }
        }
    }

    /// <summary>A range's fixed pose in depth, each glyph about its own middle or the range as one.</summary>
    [Serializable]
    public struct LetteraSolidRangePose
    {
        [Tooltip("Degrees about X: the top tips away.")] public float Pitch;
        [Tooltip("Degrees about Y: the right side comes forward.")] public float Yaw;
        [Tooltip("Degrees about Z: counterclockwise as seen.")] public float Roll;
        [Tooltip("Towards the viewer, in em.")] public float Lift;
        public float Scale;
        [Tooltip("The range turns as one body, rather than each glyph about its own middle.")] public bool Whole;

        internal ulong HashOf()
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                void Mix(float v) => h = (h ^ (uint)v.GetHashCode()) * 1099511628211UL;
                Mix(Pitch);
                Mix(Yaw);
                Mix(Roll);
                Mix(Lift);
                Mix(Scale);
                Mix(Whole ? 1 : 0);
                return h;
            }
        }
    }

    /// <summary>
    /// Gives a range of a solid text a fixed pose: <c>&lt;pose=pitch,yaw,roll,lift,scale&gt;</c>, degrees, em and a factor. A sixth slot <c>whole</c>
    /// turns the range as one body. Motion and deformers act on top of it. Flat text ignores it.
    /// </summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SolidPoseEffect : LetteraEffect
    {
        static LetteraParamInfo Turn(int slot, string name, string hint) =>
            LetteraParamInfo.ForSlot(slot, name, LetteraParamKind.Float, hint, step: 5, decimals: 1, defaultText: "0", essential: slot == 0, example: "20", unitLabel: "\u00b0");

        static readonly LetteraParamInfo[] s_Slots =
        {
            Turn(0, "Pitch", "Turns about X, in degrees: the top tips away."),
            Turn(1, "Yaw", "Turns about Y, in degrees: the right side comes forward."),
            Turn(2, "Roll", "Turns about Z, in degrees: counterclockwise as seen."),
            LetteraParamInfo.ForSlot(3, "Lift", LetteraParamKind.Float, "Moves towards the viewer, in em.", step: 0.05f, decimals: 2, defaultText: "0", example: "0.2", unitLabel: "em"),
            LetteraParamInfo.ForSlot(4, "Scale", LetteraParamKind.Float, "Scales the range, 0 to 100.", 0, 100, 0.1f, 2, defaultText: "1", example: "1.5"),
            LetteraParamInfo.ForSlot(5, "Turns as", LetteraParamKind.Choice, "whole turns the range as one body; glyph turns each glyph about its own middle.", choices: new[] { "whole", "glyph" },
                choiceLabels: new[] { "One body", "Each glyph" }, defaultText: "glyph"),
        };

        static readonly LetteraTagExample[] s_Examples = { new LetteraTagExample("0,25", "Turned 25 degrees about Y"), new LetteraTagExample("0,0,10,0,1,whole", "Rolled 10 degrees as one body") };

        public override string Description => "Holds a range at a fixed pose in depth. Only a solid text shows it.";
        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Slots;
        public override IReadOnlyList<LetteraTagExample> Examples => s_Examples;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            var pose = new LetteraSolidRangePose
            {
                Pitch = slots.Float(0, 0),
                Yaw = slots.Float(1, 0),
                Roll = slots.Float(2, 0),
                Lift = slots.Float(3, 0),
                Scale = Mathf.Clamp(slots.Float(4, 1), 0, 100),
                Whole = string.Equals(slots[5], "whole", StringComparison.OrdinalIgnoreCase),
            };
            attributes.AddExtra(LetteraSolidRanges.PoseKey, start, end, 0, pose);
        }
    }

    /// <summary>
    /// Gives a range of a solid text the materials of a named set on the text: <c>&lt;surface=gold&gt;</c> for every surface, or
    /// <c>&lt;surface=gold,face,side&gt;</c> for those named: face, bevel, side, backbevel, back. Flat text ignores it.
    /// </summary>
    [Serializable, UnityEngine.Scripting.Preserve]
    public class SolidSurfaceEffect : LetteraEffect
    {
        static readonly string[] s_Surfaces = { "all", "Face", "Bevel", "Side", "BackBevel", "Back" };

        static LetteraParamInfo Surface(int slot) =>
            LetteraParamInfo.ForSlot(slot, "Surface " + slot, LetteraParamKind.Choice, "A surface the set applies to. Leave all of them empty for every surface.", choices: s_Surfaces, defaultText: "all");

        static readonly LetteraParamInfo[] s_Slots =
        {
            LetteraParamInfo.ForSlot(0, "Set", LetteraParamKind.String, "The name of a material set on the text.", defaultText: "none, so nothing changes", essential: true, example: "gold"),
            Surface(1), Surface(2), Surface(3),
        };

        static readonly LetteraTagExample[] s_Examples = { new LetteraTagExample("gold", "Every surface in the gold set"), new LetteraTagExample("gold,Face,Side", "Only the face and the sides") };

        public override string Description => "Gives a range the materials of a named set on the text. Only a solid text shows it.";
        public override IReadOnlyList<LetteraParamInfo> Parameters => s_Slots;
        public override IReadOnlyList<LetteraTagExample> Examples => s_Examples;

        public override void Apply(LetteraAttributes attributes, int start, int end, in LetteraSlots slots)
        {
            string name = slots[0];
            if (string.IsNullOrEmpty(name)) return;
            int mask = 0;
            for (int i = 1; i < slots.Count; i++)
            {
                string surface = slots[i];
                if (string.IsNullOrEmpty(surface)) continue;
                if (surface.Equals("all", StringComparison.OrdinalIgnoreCase)) mask = 31;
                else if (Enum.TryParse(surface, true, out LetteraSolidSurface s)) mask |= 1 << (int)s;
            }
            attributes.AddExtra(LetteraSolidRanges.SurfaceKey, start, end, mask == 0 ? 31 : mask, name.Trim());
        }
    }

    /// <summary>What ranges of a solid text take for themselves: materials by surface, and a fixed pose. Applied while the text is built.</summary>
    internal sealed class LetteraSolidRanges : IDisposable
    {
        internal static readonly object PoseKey = new object(), SurfaceKey = new object();

        internal struct SurfaceMap
        {
            public string Set;
            public int Mask;
        }

        readonly List<SurfaceMap> m_Maps = new List<SurfaceMap>();
        int m_Posed;
        int[] m_GlyphPose = Array.Empty<int>(), m_Main = Array.Empty<int>();
        NativeArray<RestItem> m_Items;
        ulong m_Hash;

        /// <summary>Surface maps the ranges asked for, after the text's own and its layers'.</summary>
        internal IReadOnlyList<SurfaceMap> Maps => m_Maps;
        internal bool HasPoses => m_Posed > 0;
        internal ulong HashOf() => m_Hash;

        public void Dispose()
        {
            if (m_Items.IsCreated) m_Items.Dispose();
            if (m_Spans.IsCreated) m_Spans.Dispose();
        }

        internal static void AddStyles(List<LetteraStyle> styles)
        {
            styles.Add(LetteraStyle.ForTag(new SolidPoseEffect(), "pose"));
            styles.Add(LetteraStyle.ForTag(new SolidSurfaceEffect(), "surface"));
            styles.Add(LetteraStyle.ForTag(new RevolveMotion(), "revolve"));
            styles.Add(LetteraStyle.ForTag(new SwingMotion(), "swing"));
            styles.Add(LetteraStyle.ForTag(new SurgeMotion(), "surge"));
            styles.Add(LetteraStyle.ForTag(new OrbitMotion(), "orbit"));
            styles.Add(LetteraStyle.ForTag(new TumbleMotion(), "tumble"));
        }

        /// <summary>Gives each glyph its range's surface map and pose. Nearly every text has neither, and pays one look at its extras.</summary>
        internal void Assign(TextEngine engine, List<LetteraSolidMeshBuilder.Placement> placed, int glyphPlacements, int firstMap)
        {
            m_Maps.Clear();
            m_Posed = 0;
            m_Hash = 0;
            ValueList<LetteraAttributes.ExtraRange> extras = engine.ExtrasForSolids;
            if (extras.Count == 0) return;
            bool any = false;
            for (int e = 0; e < extras.Count && !any; e++) any = extras[e].Key == PoseKey || extras[e].Key == SurfaceKey;
            if (!any) return;
            int glyphs = engine.PlacedSpan.Length;
            if (m_GlyphPose.Length < glyphs)
            {
                m_GlyphPose = new int[Mathf.Max(glyphs, m_GlyphPose.Length * 2)];
                m_Main = new int[m_GlyphPose.Length];
            }
            Array.Clear(m_GlyphPose, 0, m_GlyphPose.Length);
            ulong hash = 1469598103934665603UL;
            for (int p = 0; p < glyphPlacements; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.Cluster < 0) continue;
                int map = -1, pose = 0;
                for (int e = 0; e < extras.Count; e++)
                {
                    LetteraAttributes.ExtraRange extra = extras[e];
                    if (place.Cluster < extra.Start || place.Cluster >= extra.End) continue;
                    // The innermost range is the last, and wins.
                    if (extra.Key == SurfaceKey && extra.Payload is string set) map = MapOf(set, (int)extra.Value);
                    else if (extra.Key == PoseKey && extra.Payload is LetteraSolidRangePose) pose = e + 1;
                }
                if (map >= 0)
                {
                    place.Map = firstMap + map;
                    placed[p] = place;
                }
                if (pose > 0 && place.GlyphIndex >= 0 && place.GlyphIndex < glyphs)
                {
                    m_GlyphPose[place.GlyphIndex] = pose;
                    m_Main[place.GlyphIndex] = p;
                    m_Posed++;
                }
                unchecked
                {
                    hash = (hash ^ (ulong)(map + 1)) * 1099511628211UL;
                    hash = (hash ^ (pose > 0 ? ((LetteraSolidRangePose)extras[pose - 1].Payload).HashOf() : 0)) * 1099511628211UL;
                }
            }
            foreach (SurfaceMap m in m_Maps)
                unchecked { hash = (hash ^ (ulong)(uint)(m.Set.GetHashCode() ^ m.Mask)) * 1099511628211UL; }
            m_Hash = hash;
        }

        int MapOf(string set, int mask)
        {
            for (int i = 0; i < m_Maps.Count; i++)
                if (m_Maps[i].Mask == mask && string.Equals(m_Maps[i].Set, set, StringComparison.OrdinalIgnoreCase))
                    return i;
            m_Maps.Add(new SurfaceMap { Set = set, Mask = mask });
            return m_Maps.Count - 1;
        }

        struct RestItem
        {
            public int Start, Count;
            public float4x4 Matrix;
            public float3x3 Normal;
        }

        /// <summary>Turns the posed glyphs where they rest, and their layers with them. Bounds and each placement's ends follow.</summary>
        internal void PoseRest(TextEngine engine, List<LetteraSolidMeshBuilder.Placement> placed, NativeArray<LetteraSolidMeshVertex> rest, NativeArray<float3> ends, ref float3 min, ref float3 max)
        {
            if (m_Posed == 0) return;
            ValueList<LetteraAttributes.ExtraRange> extras = engine.ExtrasForSolids;
            int glyphs = engine.PlacedSpan.Length;
            if (!m_Items.IsCreated || m_Items.Length < placed.Count)
            {
                if (m_Items.IsCreated) m_Items.Dispose();
                m_Items = new NativeArray<RestItem>(Mathf.Max(placed.Count, 64), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            int count = 0;
            m_WholeStamp++;
            for (int p = 0; p < placed.Count; p++)
            {
                LetteraSolidMeshBuilder.Placement place = placed[p];
                if (place.Cluster < 0 || place.GlyphIndex < 0 || place.GlyphIndex >= glyphs || place.VertexCount == 0) continue;
                int index = m_GlyphPose[place.GlyphIndex];
                if (index == 0) continue;
                LetteraAttributes.ExtraRange extra = extras[index - 1];
                var pose = (LetteraSolidRangePose)extra.Payload;
                int main = m_Main[place.GlyphIndex];
                LetteraSolidMeshBuilder.Placement glyph = placed[main];
                float3 centre = (ends[main * 2] + ends[main * 2 + 1]) * 0.5f;
                if (pose.Whole) centre = WholeCentre(index, placed, ends, glyphs, centre);
                var solid = LetteraSolidPose.Identity;
                solid.Rotation = Quaternion.Euler(pose.Pitch, pose.Yaw, pose.Roll);
                solid.Scale = pose.Scale;
                solid.Lift(pose.Lift * glyph.SizeZ);
                solid.Pivot = new Vector3(centre.x, centre.y, 0);
                float4x4 matrix = solid.Matrix(centre.z);
                float3x3 linear = new float3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz);
                m_Items[count++] = new RestItem
                {
                    Start = place.VertexStart, Count = place.VertexCount, Matrix = matrix,
                    Normal = math.abs(math.determinant(linear)) > 1e-12f ? math.transpose(math.inverse(linear)) : linear,
                };
            }
            if (count == 0) return;
            if (!m_Spans.IsCreated || m_Spans.Length < placed.Count)
            {
                if (m_Spans.IsCreated) m_Spans.Dispose();
                m_Spans = new NativeArray<int2>(Mathf.Max(placed.Count, 64), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            for (int p = 0; p < placed.Count; p++) m_Spans[p] = new int2(placed[p].VertexStart, placed[p].VertexCount);
            var bounds = new NativeArray<float3>(2, Allocator.TempJob);
            // Every placement's ends again: a pose may have moved any, and ranges of deformers measure by them.
            new RestJob { Items = m_Items, Count = count, Vertices = rest, Spans = m_Spans, Placements = placed.Count, Ends = ends, Bounds = bounds }.Run();
            min = bounds[0];
            max = bounds[1];
            bounds.Dispose();
        }

        NativeArray<int2> m_Spans;
        float3[] m_Whole = Array.Empty<float3>();
        int[] m_WholeFor = Array.Empty<int>();
        int m_WholeStamp;

        // The middle of all of a range's glyphs, measured once a build.
        float3 WholeCentre(int index, List<LetteraSolidMeshBuilder.Placement> placed, NativeArray<float3> ends, int glyphs, float3 fallback)
        {
            if (m_WholeFor.Length <= index)
            {
                Array.Resize(ref m_Whole, Mathf.Max(index + 1, m_Whole.Length * 2));
                Array.Resize(ref m_WholeFor, m_Whole.Length);
            }
            if (m_WholeFor[index] == m_WholeStamp) return m_Whole[index];
            float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
            for (int q = 0; q < placed.Count; q++)
            {
                int g = placed[q].GlyphIndex;
                if (g < 0 || g >= glyphs || m_GlyphPose[g] != index || m_Main[g] != q) continue;
                low = math.min(low, ends[q * 2]);
                high = math.max(high, ends[q * 2 + 1]);
            }
            m_Whole[index] = low.x <= high.x ? (low + high) * 0.5f : fallback;
            m_WholeFor[index] = m_WholeStamp;
            return m_Whole[index];
        }

        [BurstCompile]
        struct RestJob : IJob
        {
            [ReadOnly] public NativeArray<RestItem> Items;
            public int Count;
            public NativeArray<LetteraSolidMeshVertex> Vertices;
            [ReadOnly] public NativeArray<int2> Spans;
            public int Placements;
            public NativeArray<float3> Ends, Bounds;

            public void Execute()
            {
                for (int r = 0; r < Count; r++)
                {
                    RestItem item = Items[r];
                    for (int i = item.Start; i < item.Start + item.Count; i++)
                    {
                        LetteraSolidMeshVertex v = Vertices[i];
                        v.Position = math.transform(item.Matrix, v.Position);
                        v.Normal = math.normalizesafe(math.mul(item.Normal, v.Normal), v.Normal);
                        Vertices[i] = v;
                    }
                }
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int p = 0; p < Placements; p++)
                {
                    int2 span = Spans[p];
                    if (span.y == 0) continue;
                    float3 low = new float3(float.MaxValue), high = new float3(float.MinValue);
                    for (int i = span.x; i < span.x + span.y; i++)
                    {
                        float3 position = Vertices[i].Position;
                        low = math.min(low, position);
                        high = math.max(high, position);
                    }
                    Ends[p * 2] = low;
                    Ends[p * 2 + 1] = high;
                    min = math.min(min, low);
                    max = math.max(max, high);
                }
                Bounds[0] = min;
                Bounds[1] = max;
            }
        }
    }
}
