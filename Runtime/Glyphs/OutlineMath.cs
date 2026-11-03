using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

// The outline rasterizer's few vector types and list, with the names, members and arithmetic of the Mathematics and Collections packages it
// was written against. Referencing those packages from this assembly put both into every IL2CPP player whole, since a build copies a
// package assembly entire at the default stripping level: some 3 MB of a Windows player for the four structs and one list used here.
// The bodies are those packages' own formulas, so Burst compiles the same arithmetic and the results are bit for bit the same.
namespace FirstSymbol.Lettera.Outlines
{
#pragma warning disable IDE1006 // the packages' lower-case names, so that the rasterizer reads as it did
    internal struct bool2
    {
        public bool x, y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool2(bool x, bool y) { this.x = x; this.y = y; }
    }

    internal struct float2 : IEquatable<float2>
    {
        public float x, y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2(float x, float y) { this.x = x; this.y = y; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator +(float2 a, float2 b) => new float2(a.x + b.x, a.y + b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator -(float2 a, float2 b) => new float2(a.x - b.x, a.y - b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator -(float2 a) => new float2(-a.x, -a.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator *(float2 a, float2 b) => new float2(a.x * b.x, a.y * b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator *(float2 a, float b) => new float2(a.x * b, a.y * b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator *(float a, float2 b) => new float2(a * b.x, a * b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float2 operator /(float2 a, float b) => new float2(a.x / b, a.y / b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool2 operator ==(float2 a, float2 b) => new bool2(a.x == b.x, a.y == b.y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool2 operator !=(float2 a, float2 b) => new bool2(a.x != b.x, a.y != b.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(float2 rhs) => x == rhs.x && y == rhs.y;

        public override bool Equals(object o) => o is float2 other && Equals(other);
        public override int GetHashCode() => x.GetHashCode() * 397 ^ y.GetHashCode();
        public override string ToString() => $"float2({x}f, {y}f)";
    }

    internal struct float3
    {
        public float x, y, z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float3(float v) => new float3(v, v, v);
    }

    internal struct float4 : IEquatable<float4>
    {
        public float x, y, z, w;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float4(float2 xy, float2 zw) { x = xy.x; y = xy.y; z = zw.x; w = zw.y; }

        public float2 xy
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new float2(x, y);
        }

        public float2 zw
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => new float2(z, w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float4 operator +(float4 a, float4 b) => new float4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float4 operator -(float4 a, float4 b) => new float4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(float4 rhs) => x == rhs.x && y == rhs.y && z == rhs.z && w == rhs.w;

        public override bool Equals(object o) => o is float4 other && Equals(other);
        public override int GetHashCode() => ((x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode()) * 397 ^ w.GetHashCode();
        public override string ToString() => $"float4({x}f, {y}f, {z}f, {w}f)";
    }

    internal static class math
    {
        public const float PI = (float)Math.PI;

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int min(int x, int y) => x < y ? x : y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int max(int x, int y) => x > y ? x : y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float min(float x, float y) => float.IsNaN(y) || x < y ? x : y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float max(float x, float y) => float.IsNaN(y) || x > y ? x : y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int clamp(int valueToClamp, int lowerBound, int upperBound) => max(lowerBound, min(upperBound, valueToClamp));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float clamp(float valueToClamp, float lowerBound, float upperBound) => max(lowerBound, min(upperBound, valueToClamp));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float saturate(float x) => clamp(x, 0.0f, 1.0f);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static unsafe float abs(float x) { uint bits = *(uint*)&x & 0x7FFFFFFF; return *(float*)&bits; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float sqrt(float x) => (float)Math.Sqrt(x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float floor(float x) => (float)Math.Floor(x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float ceil(float x) => (float)Math.Ceiling(x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float cos(float x) => (float)Math.Cos(x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float sin(float x) => (float)Math.Sin(x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float radians(float x) => x * (float)(Math.PI / 180.0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float dot(float2 x, float2 y) => x.x * y.x + x.y * y.y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float lengthsq(float2 x) => dot(x, x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float length(float2 x) => sqrt(dot(x, x));
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float distance(float2 x, float2 y) => length(y - x);
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool all(bool2 x) => x.x && x.y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int countbits(int x)
        {
            uint v = (uint)x;
            v = v - ((v >> 1) & 0x55555555);
            v = (v & 0x33333333) + ((v >> 2) & 0x33333333);
            return (int)((((v + (v >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24);
        }
    }
#pragma warning restore IDE1006

    /// <summary>A growable list in unmanaged memory. Copies share one list, as the package's list does.</summary>
    internal unsafe struct NativeList<T> : IDisposable where T : unmanaged
    {
        struct Header
        {
            public T* Items;
            public int Length, Capacity;
            public Allocator Allocator;
        }

        Header* m_Header;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
#endif

        public NativeList(int capacity, Allocator allocator)
        {
            m_Header = (Header*)UnsafeUtility.Malloc(sizeof(Header), UnsafeUtility.AlignOf<Header>(), allocator);
            capacity = Math.Max(capacity, 1);
            m_Header->Items = (T*)UnsafeUtility.Malloc((long)capacity * sizeof(T), UnsafeUtility.AlignOf<T>(), allocator);
            m_Header->Length = 0;
            m_Header->Capacity = capacity;
            m_Header->Allocator = allocator;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        public bool IsCreated => m_Header != null;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_Header->Length;
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((uint)index >= (uint)m_Header->Length) throw new IndexOutOfRangeException();
#endif
                return m_Header->Items[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if ((uint)index >= (uint)m_Header->Length) throw new IndexOutOfRangeException();
#endif
                m_Header->Items[index] = value;
            }
        }

        public void Add(T value)
        {
            Header* header = m_Header;
            if (header->Length == header->Capacity)
            {
                int capacity = header->Capacity * 2;
                var items = (T*)UnsafeUtility.Malloc((long)capacity * sizeof(T), UnsafeUtility.AlignOf<T>(), header->Allocator);
                UnsafeUtility.MemCpy(items, header->Items, (long)header->Length * sizeof(T));
                UnsafeUtility.Free(header->Items, header->Allocator);
                header->Items = items;
                header->Capacity = capacity;
            }
            header->Items[header->Length++] = value;
        }

        public void Clear() => m_Header->Length = 0;

        public T* GetUnsafePtr() => m_Header->Items;

        /// <summary>The items as an array over the same memory, for a job, while the list does not grow.</summary>
        public NativeArray<T> AsArray()
        {
            NativeArray<T> array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(m_Header->Items, m_Header->Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
#endif
            return array;
        }

        public void Dispose()
        {
            if (m_Header == null) return;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            Allocator allocator = m_Header->Allocator;
            UnsafeUtility.Free(m_Header->Items, allocator);
            UnsafeUtility.Free(m_Header, allocator);
            m_Header = null;
        }
    }
}
