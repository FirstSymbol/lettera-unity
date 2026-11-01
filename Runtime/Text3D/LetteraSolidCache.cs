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

        static LetteraSolidEntry BuildBox(in LetteraSolidKey key)
        {
            float[] square = { 0, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 4 };
            return Build(key, square, square.Length, 1, new LetteraSolidRecipe { Tolerance = 0.001f, SmoothingCos = 0.866f, Faces = LetteraSolidFaces.All });
        }

        /// <summary>Builds from an outline in hand. Tests use this with outlines written by hand.</summary>
        internal static LetteraSolidEntry Build(in LetteraSolidKey key, float[] commands, int count, float scale, in LetteraSolidRecipe recipe)
        {
            Builds++;
            var input = new NativeArray<float>(Mathf.Max(count, 1), Allocator.TempJob);
            NativeArray<float>.Copy(commands, input, count);
            var front = new NativeList<float2>(16, Allocator.TempJob);
            var back = new NativeList<float2>(16, Allocator.TempJob);
            FillProfile(recipe.Front, ref front);
            FillProfile(recipe.Back, ref back);
            var vertices = new NativeList<LetteraSolidVertex>(1024, Allocator.TempJob);
            var indices = new NativeList<int>(4096, Allocator.TempJob);
            var starts = new NativeArray<int>(6, Allocator.TempJob);
            var report = new NativeArray<int>(5, Allocator.TempJob);
            var job = new LetteraSolidBuildJob
            {
                Commands = input,
                CommandCount = count,
                FrontProfile = front.AsArray(),
                BackProfile = back.AsArray(),
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
                Vertices = vertices,
                Indices = indices,
                SurfaceStarts = starts,
                Report = report,
            };
            job.Run();
            var entry = new LetteraSolidEntry { Key = key, Failed = report[0] != 0 };
            if (entry.Failed)
            {
                Failures++;
            }
            else
            {
                entry.Vertices = new NativeArray<LetteraSolidVertex>(vertices.AsArray(), Allocator.Persistent);
                entry.Indices = new NativeArray<int>(indices.AsArray(), Allocator.Persistent);
                for (int i = 0; i < 6; i++) entry.SurfaceStarts[i] = starts[i];
                entry.FrontHeight = job.Params.FrontHeight;
                entry.BackHeight = job.Params.BackHeight;
                BevelsClamped += report[1];
                CapsDropped += report[2];
                LastCapFailure = report[4];
            }
            input.Dispose();
            front.Dispose();
            back.Dispose();
            vertices.Dispose();
            indices.Dispose();
            starts.Dispose();
            report.Dispose();
            return entry;
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
