using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>One shape of an edge, as a text asks for it.</summary>
    [Serializable]
    public struct LetteraBevel
    {
        public LetteraBevelShape Shape;
        [Tooltip("How far it runs inward from the outline, in em.")] public float Width;
        [Tooltip("How far it recedes from the face plane, in em.")] public float Height;
        [Tooltip("Round only: 0 hollow, 0.5 a quarter circle, 1 nearly square.")] public float Roundness;
        [Tooltip("Samples of a curved shape.")] [Range(1, 16)] public int Segments;
        [Tooltip("Custom only: height from the side (0) to the face (1).")] public AnimationCurve Curve;

        public static LetteraBevel Default => new LetteraBevel { Shape = LetteraBevelShape.Round, Width = 0.02f, Height = 0.02f, Roundness = 0.5f, Segments = 4, Curve = AnimationCurve.Linear(0, 0, 1, 1) };

        internal bool IsNone => Shape == LetteraBevelShape.None || (Width <= 0 && Height == 0);

        internal int HashOf()
        {
            if (IsNone) return 0;
            unchecked
            {
                int hash = 17 + (int)Shape;
                hash = hash * 31 + Width.GetHashCode();
                hash = hash * 31 + Height.GetHashCode();
                hash = hash * 31 + Roundness.GetHashCode();
                hash = hash * 31 + Segments;
                if (Shape == LetteraBevelShape.Custom && Curve != null)
                {
                    for (int i = 0; i <= 8; i++) hash = hash * 31 + Curve.Evaluate(i / 8f).GetHashCode();
                }
                return hash;
            }
        }
    }

    /// <summary>A built solid, shared by every text with the same font and edges.</summary>
    internal sealed class LetteraSolidEntry
    {
        public LetteraSolidKey Key;
        public NativeArray<LetteraSolidVertex> Vertices;
        public NativeArray<int> Indices;
        public int[] SurfaceStarts = new int[6];
        public float FrontHeight, BackHeight;
        public int References;
        public long LastUse;
        public bool Failed;
        public long Bytes => Vertices.IsCreated ? Vertices.Length * 36L + Indices.Length * 4L + 128 : 128;

        public void Release()
        {
            if (Vertices.IsCreated) Vertices.Dispose();
            if (Indices.IsCreated) Indices.Dispose();
        }
    }

    internal struct LetteraSolidKey : IEquatable<LetteraSolidKey>
    {
        public int Instance;
        public uint Glyph;
        public int Edges;

        public bool Equals(LetteraSolidKey other) => Instance == other.Instance && Glyph == other.Glyph && Edges == other.Edges;
        public override bool Equals(object obj) => obj is LetteraSolidKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                return (Instance * 397) ^ (int)(Glyph * 2654435761u) ^ Edges;
            }
        }
    }

    /// <summary>What decides a solid's shape, apart from its glyph. Depth is not here: one solid serves every depth.</summary>
    internal struct LetteraSolidRecipe
    {
        public LetteraBevel Front, Back;
        public float Tolerance, SmoothingCos;
        public LetteraSolidFaces Faces;

        public int HashOf()
        {
            unchecked
            {
                int hash = Front.HashOf();
                hash = hash * 31 + Back.HashOf();
                hash = hash * 31 + Tolerance.GetHashCode();
                hash = hash * 31 + SmoothingCos.GetHashCode();
                hash = hash * 31 + (int)Faces;
                return hash;
            }
        }
    }

    /// <summary>Solids, one set per domain, owned by the main thread.</summary>
    internal static class LetteraSolidCache
    {
        static readonly Dictionary<LetteraSolidKey, LetteraSolidEntry> s_Entries = new Dictionary<LetteraSolidKey, LetteraSolidEntry>();
        static long s_Clock, s_Bytes;
        static bool s_WarnedBudget, s_Hooked;
        static float[] s_Commands = new float[4096];

        /// <summary>How many solids have ever been built, for tests and the inspector.</summary>
        internal static int Builds { get; private set; }
        internal static int Failures { get; private set; }
        internal static int BevelsClamped { get; private set; }
        internal static int CapsDropped { get; private set; }
        /// <summary>Why a cap last failed: 1 a bridge, 2 no ear left, 3 the area did not add up.</summary>
        internal static int LastCapFailure { get; private set; }
        internal static int Count => s_Entries.Count;
        internal static long Bytes => s_Bytes;

        internal const uint UnitBoxGlyph = 0xFFFFFFFF;

        static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            Application.quitting += Clear;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Clear;
#endif
        }

        internal static void Clear()
        {
            foreach (LetteraSolidEntry entry in s_Entries.Values) entry.Release();
            s_Entries.Clear();
            s_Bytes = 0;
        }

        internal static void Release(LetteraSolidEntry entry)
        {
            if (entry != null && entry.References > 0) entry.References--;
        }

        /// <summary>The solid of a glyph, built now if it is the first to be asked for. The caller holds one reference.</summary>
        internal static unsafe LetteraSolidEntry Acquire(FontInstance instance, uint glyph, in LetteraSolidRecipe recipe)
        {
            Hook();
            var key = new LetteraSolidKey { Instance = instance != null ? instance.Id : 0, Glyph = glyph, Edges = glyph == UnitBoxGlyph ? 1 : recipe.HashOf() };
            if (!s_Entries.TryGetValue(key, out LetteraSolidEntry entry))
            {
                entry = glyph == UnitBoxGlyph ? BuildBox(key) : BuildGlyph(key, instance, glyph, recipe);
                s_Entries[key] = entry;
                s_Bytes += entry.Bytes;
                Trim();
            }
            entry.References++;
            entry.LastUse = ++s_Clock;
            return entry;
        }

        static unsafe LetteraSolidEntry BuildGlyph(in LetteraSolidKey key, FontInstance instance, uint glyph, in LetteraSolidRecipe recipe)
        {
            int count;
            fixed (float* p = s_Commands) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_Commands.Length);
            if (count > s_Commands.Length)
            {
                if (count > 16 * 1024 * 1024) return new LetteraSolidEntry { Key = key, Failed = true };
                s_Commands = new float[count + 1024];
                fixed (float* p = s_Commands) count = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_Commands.Length);
            }
            return Build(key, s_Commands, count, 1f / Mathf.Max(instance.Face.Metrics.Upem, 1), recipe);
        }

        static readonly List<PendingBuild> s_Pending = new List<PendingBuild>();
        static readonly HashSet<LetteraSolidKey> s_Asked = new HashSet<LetteraSolidKey>();

        // In a player, many first-time glyphs of one text build on the workers, and the text shows whole all the same. The web and the editor stay inline: the one has no threads, and the other wants the same order every time.
        internal static unsafe void BuildAhead(TextEngine engine, in LetteraSolidRecipe recipe)
        {
#if UNITY_WEBGL || UNITY_EDITOR
            if (!ThreadsForTests) return;
#endif
            if (!Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount.Equals(0))
            {
                ReadOnlySpan<TextEngine.PlacedGlyph> glyphs = engine.PlacedSpan;
                if (glyphs.Length <= ThreadsAbove) return;
                int edges = recipe.HashOf();
                s_Asked.Clear();
                for (int i = 0; i < glyphs.Length; i++)
                {
                    ref readonly TextEngine.PlacedGlyph g = ref glyphs[i];
                    if (g.Instance == null || g.Special == TextEngine.SpecialRuleForSolids) continue;
                    var key = new LetteraSolidKey { Instance = g.Instance.Id, Glyph = g.Glyph, Edges = edges };
                    if (s_Entries.ContainsKey(key) || !s_Asked.Add(key)) continue;
                    int count;
                    fixed (float* p = s_Commands) count = LetteraNative.lt_glyph_outline(g.Instance.Font, g.Glyph, p, s_Commands.Length);
                    if (count > s_Commands.Length)
                    {
                        if (count > 16 * 1024 * 1024) continue;
                        s_Commands = new float[count + 1024];
                        fixed (float* p = s_Commands) count = LetteraNative.lt_glyph_outline(g.Instance.Font, g.Glyph, p, s_Commands.Length);
                    }
                    s_Pending.Add(new PendingBuild(key, s_Commands, count, 1f / Mathf.Max(g.Instance.Face.Metrics.Upem, 1), recipe));
                }
                if (s_Pending.Count <= ThreadsAbove)
                {
                    foreach (PendingBuild small in s_Pending) small.Abandon();
                    s_Pending.Clear();
                    return;
                }
                var handles = new NativeArray<JobHandle>(s_Pending.Count, Allocator.Temp);
                for (int i = 0; i < s_Pending.Count; i++) handles[i] = s_Pending[i].Job.Schedule();
                JobHandle.CompleteAll(handles);
                foreach (PendingBuild done in s_Pending)
                {
                    LetteraSolidEntry entry = done.Finish();
                    s_Entries[entry.Key] = entry;
                    s_Bytes += entry.Bytes;
                }
                s_Pending.Clear();
                Trim();
            }
        }

        internal const int ThreadsAbove = 16;
        internal static bool ThreadsForTests;

        sealed class PendingBuild
        {
            public LetteraSolidBuildJob Job;
            readonly LetteraSolidKey m_Key;
            NativeArray<float> m_Input;
            NativeList<float2> m_Front, m_Back;
            NativeList<LetteraSolidVertex> m_Vertices;
            NativeList<int> m_Indices;
            NativeArray<int> m_Starts, m_Report;

            public PendingBuild(in LetteraSolidKey key, float[] commands, int count, float scale, in LetteraSolidRecipe recipe)
            {
                m_Key = key;
                Builds++;
                m_Input = new NativeArray<float>(Mathf.Max(count, 1), Allocator.TempJob);
                NativeArray<float>.Copy(commands, m_Input, count);
                m_Front = new NativeList<float2>(16, Allocator.TempJob);
                m_Back = new NativeList<float2>(16, Allocator.TempJob);
                FillProfile(recipe.Front, ref m_Front);
                FillProfile(recipe.Back, ref m_Back);
                m_Vertices = new NativeList<LetteraSolidVertex>(1024, Allocator.TempJob);
                m_Indices = new NativeList<int>(4096, Allocator.TempJob);
                m_Starts = new NativeArray<int>(6, Allocator.TempJob);
                m_Report = new NativeArray<int>(7, Allocator.TempJob);
                Job = new LetteraSolidBuildJob
                {
                    Commands = m_Input,
                    CommandCount = count,
                    FrontProfile = m_Front.AsArray(),
                    BackProfile = m_Back.AsArray(),
                    Params = new LetteraSolidParams
                    {
                        Tolerance = recipe.Tolerance,
                        FrontWidth = recipe.Front.IsNone ? 0 : recipe.Front.Width,
                        FrontHeight = recipe.Front.IsNone ? 0 : recipe.Front.Height,
                        BackWidth = recipe.Back.IsNone ? 0 : recipe.Back.Width,
                        BackHeight = recipe.Back.IsNone ? 0 : recipe.Back.Height,
                        SmoothingCos = recipe.SmoothingCos,
                        Faces = recipe.Faces,
                        Scale = scale,
                    },
                    Vertices = m_Vertices,
                    Indices = m_Indices,
                    SurfaceStarts = m_Starts,
                    Report = m_Report,
                };
            }

            public LetteraSolidEntry Finish()
            {
                var entry = new LetteraSolidEntry { Key = m_Key, Failed = m_Report[0] != 0 };
                if (entry.Failed)
                {
                    Failures++;
                }
                else
                {
                    entry.Vertices = new NativeArray<LetteraSolidVertex>(m_Vertices.AsArray(), Allocator.Persistent);
                    entry.Indices = new NativeArray<int>(m_Indices.AsArray(), Allocator.Persistent);
                    for (int i = 0; i < 6; i++) entry.SurfaceStarts[i] = m_Starts[i];
                    entry.FrontHeight = m_Report[5] / 1000000f;
                    entry.BackHeight = m_Report[6] / 1000000f;
                    BevelsClamped += m_Report[1];
                    CapsDropped += m_Report[2];
                    if (m_Report[2] != 0) LastCapFailure = m_Report[4];
                }
                Abandon();
                return entry;
            }

            public void Abandon()
            {
                m_Input.Dispose();
                m_Front.Dispose();
                m_Back.Dispose();
                m_Vertices.Dispose();
                m_Indices.Dispose();
                m_Starts.Dispose();
                m_Report.Dispose();
            }
        }

        static LetteraSolidEntry BuildBox(in LetteraSolidKey key)
        {
            float[] square = { 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 4 };
            return Build(key, square, square.Length, 1, new LetteraSolidRecipe { Tolerance = 0.001f, SmoothingCos = 0.866f, Faces = LetteraSolidFaces.All });
        }

        /// <summary>Builds from an outline in hand. Tests use this with outlines written by hand.</summary>
        internal static LetteraSolidEntry Build(in LetteraSolidKey key, float[] commands, int count, float scale, in LetteraSolidRecipe recipe)
        {
            var pending = new PendingBuild(key, commands, count, scale, recipe);
            pending.Job.Run();
            return pending.Finish();
        }

        static void FillProfile(in LetteraBevel bevel, ref NativeList<float2> output)
        {
            if (bevel.IsNone) return;
            LetteraBevelProfile.Fill(bevel.Shape, bevel.Roundness, bevel.Segments, new AnimationCurveSampler(bevel.Curve), ref output);
        }

        // The least recently used of what no text holds. Over budget with everything held, it says so once and carries on.
        static void Trim()
        {
            long budget = LetteraSettings.Instance.Solid3D.CacheBytes;
            if (s_Bytes <= budget) return;
            var free = new List<LetteraSolidEntry>();
            foreach (LetteraSolidEntry entry in s_Entries.Values)
                if (entry.References == 0)
                    free.Add(entry);
            free.Sort((a, b) => a.LastUse.CompareTo(b.LastUse));
            foreach (LetteraSolidEntry entry in free)
            {
                if (s_Bytes <= budget) return;
                s_Entries.Remove(entry.Key);
                s_Bytes -= entry.Bytes;
                entry.Release();
            }
            if (s_Bytes > budget && !s_WarnedBudget)
            {
                s_WarnedBudget = true;
                Debug.LogWarning($"Lettera: solid text holds {s_Bytes / (1024 * 1024)} MB, over the {budget / (1024 * 1024)} MB in Project Settings > Lettera > 3D. Everything is in use, so it carries on over.");
            }
        }
    }
}
