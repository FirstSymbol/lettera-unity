using System;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>
    /// Objects every text makes stay under 2 KB. Past that the collector of IL2CPP and Mono makes each as a large block of its own, several times
    /// slower than a small object: one engine of 2072 bytes cost 5000 world texts 14 ms of their first frame.
    /// </summary>
    public class ObjectSizeTests
    {
        // The layout a player lays out, fields in order with their natural alignment, plus the object header.
        static int InstanceSize(Type type)
        {
            int offset = 2 * IntPtr.Size;
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    Type f = field.FieldType;
                    int size = f.IsValueType ? (f.IsEnum ? UnsafeUtility.SizeOf(Enum.GetUnderlyingType(f)) : UnsafeUtility.SizeOf(f)) : IntPtr.Size;
                    int align = Math.Min(size >= 8 ? 8 : size >= 4 ? 4 : size >= 2 ? 2 : 1, IntPtr.Size);
                    offset = (offset + align - 1) / align * align + size;
                }
            }
            return offset;
        }

        [Test]
        public void ATextEngineIsASmallObjectWithRoomToGrow()
        {
            int size = InstanceSize(typeof(TextEngine));
            TestContext.WriteLine("TextEngine " + size + " bytes");
            Assert.Less(size, 1800, "fields only some texts use go in TextEngine.Aside");
            Assert.Less(InstanceSize(typeof(LetteraText)), 1800);
            Assert.Less(InstanceSize(typeof(LetteraWorldText)), 1800);
        }
    }
}
