using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// A list of structs with only the members every list needs. IL2CPP writes out every method of a generic class for each struct it holds:
    /// a <see cref="List{T}"/> of a struct brings some 130 functions with it (its interfaces, comparers, sorting and enumerators), this
    /// about a dozen. What only some lists do (sort, search, remove by a test) are extension methods, made for a struct only where called.
    /// The members behave as <see cref="List{T}"/>'s do, except that the enumerator does not check for changes.
    /// </summary>
    internal sealed class ValueList<T> where T : struct
    {
        internal T[] m_Items;
        internal int m_Count;

        public ValueList() => m_Items = Array.Empty<T>();

        public ValueList(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Items = capacity == 0 ? Array.Empty<T>() : new T[capacity];
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_Count;
        }

        public int Capacity
        {
            get => m_Items.Length;
            set
            {
                if (value < m_Count) throw new ArgumentOutOfRangeException(nameof(value));
                if (value == m_Items.Length) return;
                var items = value == 0 ? Array.Empty<T>() : new T[value];
                if (m_Count > 0) Array.Copy(m_Items, items, m_Count);
                m_Items = items;
            }
        }

        public T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)m_Count) throw new ArgumentOutOfRangeException(nameof(index));
                return m_Items[index];
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if ((uint)index >= (uint)m_Count) throw new ArgumentOutOfRangeException(nameof(index));
                m_Items[index] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            if (m_Count == m_Items.Length) Grow(m_Count + 1);
            m_Items[m_Count++] = item;
        }

        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)m_Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (m_Count == m_Items.Length) Grow(m_Count + 1);
            if (index < m_Count) Array.Copy(m_Items, index, m_Items, index + 1, m_Count - index);
            m_Items[index] = item;
            m_Count++;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)m_Count) throw new ArgumentOutOfRangeException(nameof(index));
            m_Count--;
            if (index < m_Count) Array.Copy(m_Items, index + 1, m_Items, index, m_Count - index);
            m_Items[m_Count] = default;
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0 || count < 0 || m_Count - index < count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count == 0) return;
            m_Count -= count;
            if (index < m_Count) Array.Copy(m_Items, index + count, m_Items, index, m_Count - index);
            Array.Clear(m_Items, m_Count, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            if (m_Count > 0 && RuntimeHelpers.IsReferenceOrContainsReferences<T>()) Array.Clear(m_Items, 0, m_Count);
            m_Count = 0;
        }

        internal void Grow(int needed)
        {
            int capacity = m_Items.Length == 0 ? 4 : m_Items.Length * 2;
            if (capacity < needed) capacity = needed;
            Array.Resize(ref m_Items, capacity);
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator
        {
            readonly ValueList<T> m_List;
            int m_Index;

            internal Enumerator(ValueList<T> list)
            {
                m_List = list;
                m_Index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => ++m_Index < m_List.m_Count;

            public T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)] get => m_List.m_Items[m_Index];
            }
        }
    }

    /// <summary>What only some value lists do. Each is made for a struct only where it is called.</summary>
    internal static class ValueListExtensions
    {
        public static void AddRange<T>(this ValueList<T> list, ValueList<T> items) where T : struct
        {
            int count = items.m_Count;
            if (count == 0) return;
            if (list.m_Count + count > list.m_Items.Length) list.Grow(list.m_Count + count);
            Array.Copy(items.m_Items, 0, list.m_Items, list.m_Count, count);
            list.m_Count += count;
        }

        public static void AddRange<T>(this ValueList<T> list, T[] items) where T : struct
        {
            int count = items.Length;
            if (count == 0) return;
            if (list.m_Count + count > list.m_Items.Length) list.Grow(list.m_Count + count);
            Array.Copy(items, 0, list.m_Items, list.m_Count, count);
            list.m_Count += count;
        }

        public static void InsertRange<T>(this ValueList<T> list, int index, ValueList<T> items) where T : struct
        {
            if ((uint)index > (uint)list.m_Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (ReferenceEquals(list, items)) throw new ArgumentException("A list cannot be inserted into itself.", nameof(items));
            int count = items.m_Count;
            if (count == 0) return;
            if (list.m_Count + count > list.m_Items.Length) list.Grow(list.m_Count + count);
            if (index < list.m_Count) Array.Copy(list.m_Items, index, list.m_Items, index + count, list.m_Count - index);
            Array.Copy(items.m_Items, 0, list.m_Items, index, count);
            list.m_Count += count;
        }

        /// <summary>The same unstable sort as <see cref="List{T}.Sort(Comparison{T})"/>, so equal items end in the same order.</summary>
        public static void Sort<T>(this ValueList<T> list, Comparison<T> comparison) where T : struct
        {
            if (comparison == null) throw new ArgumentNullException(nameof(comparison));
            if (list.m_Count > 1) Array.Sort(list.m_Items, 0, list.m_Count, Comparer<T>.Create(comparison));
        }

        public static int FindIndex<T>(this ValueList<T> list, Predicate<T> match) where T : struct
        {
            for (int i = 0; i < list.m_Count; i++)
                if (match(list.m_Items[i])) return i;
            return -1;
        }

        public static bool Exists<T>(this ValueList<T> list, Predicate<T> match) where T : struct => list.FindIndex(match) >= 0;

        public static int RemoveAll<T>(this ValueList<T> list, Predicate<T> match) where T : struct
        {
            int kept = 0;
            for (int i = 0; i < list.m_Count; i++)
            {
                if (match(list.m_Items[i])) continue;
                if (kept != i) list.m_Items[kept] = list.m_Items[i];
                kept++;
            }
            int removed = list.m_Count - kept;
            if (removed > 0) Array.Clear(list.m_Items, kept, removed);
            list.m_Count = kept;
            return removed;
        }

        public static void Reverse<T>(this ValueList<T> list) where T : struct => Array.Reverse(list.m_Items, 0, list.m_Count);

        public static T[] ToArray<T>(this ValueList<T> list) where T : struct
        {
            if (list.m_Count == 0) return Array.Empty<T>();
            var array = new T[list.m_Count];
            Array.Copy(list.m_Items, array, list.m_Count);
            return array;
        }
    }
}
