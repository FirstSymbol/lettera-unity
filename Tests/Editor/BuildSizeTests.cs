using System.Linq;
using NUnit.Framework;
using FirstSymbol.Lettera.Outlines;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Lettera.Editor.Tests
{
    public class BuildSizeTests
    {
        // An IL2CPP player at the default stripping level copies every package assembly it references whole: these two cost 4.8 MB of a
        // Windows player for a few structs. 3D text references them from its own assembly, which a build leaves out when nothing uses it.
        [Test]
        public void CoreAssemblyLeavesMathematicsAndCollectionsOut()
        {
            string[] references = typeof(LetteraText).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            CollectionAssert.DoesNotContain(references, "Unity.Mathematics");
            CollectionAssert.DoesNotContain(references, "Unity.Collections");
            CollectionAssert.Contains(references, "Unity.Burst");
        }

        [Test]
        public void WebFontStaysOnlyWhereASystemMayHaveNoFonts()
        {
            var web = Resources.Load<LetteraFont>(FontChain.WebFontResource);
            Assert.IsNotNull(web);
            Assert.AreEqual(web, LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.StandaloneWindows64));
            Assert.AreEqual(web, LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.StandaloneOSX));
            Assert.AreEqual(web, LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.Android));
            Assert.AreEqual(web, LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.iOS));
            Assert.AreEqual(web, LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.tvOS));
            Assert.AreEqual(web, LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.VisionOS));
            Assert.IsNull(LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.WSAPlayer), "UWP may not reach the font folder");
            Assert.IsNull(LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.WebGL));
            Assert.IsNull(LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.StandaloneLinux64));
        }

        [Test]
        public void WebFontStaysWhenTheProjectNamesIt()
        {
            LetteraSettings settings = LetteraSettings.Instance;
            LetteraFontAsset before = settings.WebGLFallbackFont;
            try
            {
                var web = Resources.Load<LetteraFont>(FontChain.WebFontResource);
                settings.WebGLFallbackFont = web;
                Assert.IsNull(LetteraFontBuildProcessor.WebFontLeftOut(BuildTarget.StandaloneWindows64));
            }
            finally
            {
                settings.WebGLFallbackFont = before;
            }
        }

        // The rasterizer's own vector types keep the package's arithmetic, so that a field comes out byte for byte as it did.
        [Test]
        public void OutlineMathMatchesThePackagesFormulas()
        {
            Assert.AreEqual(1f, math.min(1f, float.NaN));
            Assert.AreEqual(2f, math.max(2f, float.NaN));
            Assert.AreEqual(0f, math.saturate(-3f));
            Assert.AreEqual(1f, math.saturate(7f));
            Assert.AreEqual(0.5f, math.saturate(0.5f));
            Assert.AreEqual(3f, math.abs(-3f));
            Assert.AreEqual(5f, math.length(new float2(3, 4)));
            Assert.AreEqual(5f, math.distance(new float2(1, 1), new float2(4, 5)));
            Assert.AreEqual((float)System.Math.Cos(20f * (float)(System.Math.PI / 180.0)), math.cos(math.radians(20f)));
            Assert.AreEqual(8, math.countbits(0xFF));
            Assert.AreEqual(4, math.clamp(9, 1, 4));
            Assert.IsTrue(math.all(new float4(1, 2, 3, 4).zw == new float2(3, 4)));
            Assert.IsFalse(math.all(new float4(1, 2, 3, 4).xy == new float2(1, 3)));
            float3 filled = 2;
            Assert.AreEqual(2f, filled.z);
        }

        struct Item
        {
            public int Key, Order;
            public string Name;
        }

        // A value list does what a List does with the same calls, in the same order, so that turning one into the other changes no result.
        [Test]
        public void ValueListMatchesList()
        {
            var random = new System.Random(7);
            var list = new System.Collections.Generic.List<Item>();
            var values = new ValueList<Item>();
            for (int step = 0; step < 4000; step++)
            {
                var item = new Item { Key = random.Next(8), Order = step, Name = "n" + step };
                switch (random.Next(9))
                {
                    case 0:
                    case 1:
                    case 2: list.Add(item); values.Add(item); break;
                    case 3:
                        int at = random.Next(list.Count + 1);
                        list.Insert(at, item);
                        values.Insert(at, item);
                        break;
                    case 4:
                        if (list.Count == 0) break;
                        int remove = random.Next(list.Count);
                        list.RemoveAt(remove);
                        values.RemoveAt(remove);
                        break;
                    case 5:
                        if (list.Count < 4) break;
                        int start = random.Next(list.Count - 3), count = random.Next(4);
                        list.RemoveRange(start, count);
                        values.RemoveRange(start, count);
                        break;
                    case 6:
                        list.Sort((x, y) => x.Key.CompareTo(y.Key));
                        values.Sort((x, y) => x.Key.CompareTo(y.Key));
                        break;
                    case 7:
                        int key = random.Next(8);
                        Assert.AreEqual(list.FindIndex(x => x.Key == key), values.FindIndex(x => x.Key == key));
                        if (random.Next(20) == 0) Assert.AreEqual(list.RemoveAll(x => x.Key == key), values.RemoveAll(x => x.Key == key));
                        break;
                    case 8:
                        if (random.Next(50) == 0)
                        {
                            list.Clear();
                            values.Clear();
                        }
                        break;
                }
                Assert.AreEqual(list.Count, values.Count);
            }
            Assert.Greater(list.Count, 100);
            int index = 0;
            foreach (Item item in values)
            {
                Assert.AreEqual(list[index].Order, item.Order);
                Assert.AreEqual(list[index].Name, item.Name);
                index++;
            }
            Assert.AreEqual(list.Count, index);
            CollectionAssert.AreEqual(list.ConvertAll(x => x.Order), System.Array.ConvertAll(values.ToArray(), x => x.Order));

            var more = new ValueList<Item>();
            more.Add(new Item { Order = -1 });
            more.Add(new Item { Order = -2 });
            values.InsertRange(3, more);
            list.InsertRange(3, new[] { new Item { Order = -1 }, new Item { Order = -2 } });
            values.AddRange(more);
            list.AddRange(new[] { new Item { Order = -1 }, new Item { Order = -2 } });
            values.Reverse();
            list.Reverse();
            CollectionAssert.AreEqual(list.ConvertAll(x => x.Order), System.Array.ConvertAll(values.ToArray(), x => x.Order));
            values.Capacity = values.Count;
            Assert.AreEqual(values.Count, values.Capacity);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = values[values.Count]);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => values.RemoveAt(-1));
        }

        [Test]
        public void OutlineListGrowsAndIsSharedByCopies()
        {
            var list = new NativeList<float4>(1, Unity.Collections.Allocator.Persistent);
            try
            {
                NativeList<float4> copy = list;
                for (int i = 0; i < 100; i++) copy.Add(new float4(i, 0, 0, 0));
                Assert.AreEqual(100, list.Length);
                Assert.AreEqual(57f, list[57].x);
                Unity.Collections.NativeArray<float4> array = list.AsArray();
                Assert.AreEqual(100, array.Length);
                Assert.AreEqual(99f, array[99].x);
                list.Clear();
                Assert.AreEqual(0, copy.Length);
            }
            finally
            {
                list.Dispose();
            }
            Assert.IsFalse(list.IsCreated);
        }
    }
}
