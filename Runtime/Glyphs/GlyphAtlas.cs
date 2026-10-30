using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace FirstSymbol.Lettera
{
    /// <summary>Where a glyph lives in the shared atlases and the quad that shows it, in font units.</summary>
    internal struct AtlasGlyph
    {
        public const byte Empty = 0, Sdf = 1, Color = 2;

        public byte Kind;
        public ushort Page;
        public float U0, V0, U1, V1;
        public float Left, Bottom, Right, Top;
    }

    /// <summary>
    /// The atlases every text shares, bound globally so different fonts never split a batch: one SDF
    /// texture array and one color texture array. Glyphs are placed on request; pixels are computed and
    /// uploaded together by Flush, once per frame.
    /// </summary>
    internal static unsafe class GlyphAtlas
    {
        /// <summary>The SDF reaches this far from the edge, in em; it bounds outline and shadow widths.</summary>
        public const float SpreadEm = 0.1875f;

        const int PageSize = 2048;
        // Browsers on phones stop a page long before desktops run out, so the web keeps fewer pages.
#if UNITY_WEBGL && !UNITY_EDITOR
        const int DefaultSdfPages = 3;
        const int DefaultColorPages = 1;
#else
        const int DefaultSdfPages = 8;
        const int DefaultColorPages = 4;
#endif
        const int ColorTileHeight = 96;
        const int StagingSize = 1024;
        const int Bleed = 1;

        static readonly int s_SdfAtlasId = Shader.PropertyToID("_LetteraSdfAtlas");
        static readonly int s_ColorAtlasId = Shader.PropertyToID("_LetteraColorAtlas");

        sealed class PageSet
        {
            public readonly List<IntPtr> Pixels = new List<IntPtr>();
            public readonly GraphicsFormat Format;
            public readonly int Bytes;
            public readonly List<int> LastUsed = new List<int>();
            public int MaxPages;
            public readonly int PropertyId;
            public Texture2DArray Texture;
            public int ShelfX, ShelfY, ShelfHeight;
            public int Current;
            public bool NeedsFullUpload;

            public PageSet(GraphicsFormat format, int bytes, int maxPages, int propertyId)
            {
                Format = format;
                Bytes = bytes;
                MaxPages = maxPages;
                PropertyId = propertyId;
            }
        }

        struct PendingUpload
        {
            public PageSet Set;
            public int Page, X, Y, Width, Height;
        }

        static readonly PageSet s_Sdf = new PageSet(GraphicsFormat.R8_UNorm, 1, DefaultSdfPages, s_SdfAtlasId);
        static readonly PageSet s_Color = new PageSet(GraphicsFormat.R8G8B8A8_SRGB, 4, DefaultColorPages, s_ColorAtlasId);
        static readonly Dictionary<ulong, AtlasGlyph> s_Glyphs = new Dictionary<ulong, AtlasGlyph>();
        static readonly List<PendingUpload> s_Uploads = new List<PendingUpload>();
        static readonly List<SdfTile> s_Tiles = new List<SdfTile>();
        static NativeList<float4> s_Lines;
        static float[] s_Commands = new float[4096];
        static readonly List<ulong> s_Evicted = new List<ulong>();
        static Texture2D s_StagingSdf, s_StagingColor;
        static bool s_Rebind;
        static bool s_ClearPending;
        static int s_Tick;
        static Texture2DArray s_EmptyColor;

        /// <summary>Raised when the atlas was cleared: every text must rebuild its mesh.</summary>
        public static event Action Cleared;

        public static int SdfPageCount => s_Sdf.Pixels.Count;
        public static int ColorPageCount => s_Color.Pixels.Count;
        public static int GlyphCount => s_Glyphs.Count;

        /// <summary>Limits the page counts, e.g. for tests; a set at its limit recycles its least recently used page.</summary>
        internal static void SetPageLimits(int sdf, int color)
        {
            s_Sdf.MaxPages = sdf;
            s_Color.MaxPages = color;
        }

        internal static (int sdf, int color) PageLimits => (s_Sdf.MaxPages, s_Color.MaxPages);

        public static AtlasGlyph Get(FontInstance instance, uint glyph, int pixelsPerEm)
        {
            ulong key = ((ulong)(uint)instance.Id << 36) | ((ulong)glyph << 8) | (uint)(pixelsPerEm >> 2 & 0xFF);
            if (s_Glyphs.TryGetValue(key, out AtlasGlyph entry))
            {
                if (entry.Kind == AtlasGlyph.Sdf) s_Sdf.LastUsed[entry.Page] = s_Tick;
                else if (entry.Kind == AtlasGlyph.Color) s_Color.LastUsed[entry.Page] = s_Tick;
                return entry;
            }
            entry = Create(instance, glyph, pixelsPerEm);
            s_Glyphs[key] = entry;
            return entry;
        }

        static AtlasGlyph Create(FontInstance instance, uint glyph, int pixelsPerEm)
        {
            FontFace face = instance.Face;
            if (face.IsVirtual) return CreateBrowserEmoji(glyph);
            if (face.HasColorLayers && LetteraNative.lt_glyph_color_layers(face.Face, glyph, null, null, 0) > 0)
                return CreateColorLayers(instance, glyph);
            if (face.HasColorBitmaps)
            {
                AtlasGlyph bitmap = CreateBitmap(instance, glyph);
                if (bitmap.Kind != AtlasGlyph.Empty) return bitmap;
            }
            return CreateSdf(instance, glyph, pixelsPerEm);
        }

        static int ReadOutline(FontInstance instance, uint glyph)
        {
            int needed;
            fixed (float* p = s_Commands) needed = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_Commands.Length);
            if (needed > s_Commands.Length)
            {
                s_Commands = new float[needed * 2];
                fixed (float* p = s_Commands) needed = LetteraNative.lt_glyph_outline(instance.Font, glyph, p, s_Commands.Length);
            }
            return needed;
        }

        static AtlasGlyph CreateSdf(FontInstance instance, uint glyph, int pixelsPerEm)
        {
            int count = ReadOutline(instance, glyph);
            if (count == 0) return default;
            if (!s_Lines.IsCreated) s_Lines = new NativeList<float4>(4096, Allocator.Persistent);

            int upem = instance.Face.Metrics.Upem;
            Bounds2(s_Commands, count, out float minX, out float minY, out float maxX, out float maxY);
            if (maxX <= minX || maxY <= minY) return default;

            // A glyph too large for a tile at this density simply uses fewer pixels per em.
            float largest = Mathf.Max(maxX - minX, maxY - minY) / upem + 2 * SpreadEm;
            int density = Mathf.Min(pixelsPerEm, Mathf.FloorToInt((StagingSize - 2 * Bleed) / Mathf.Max(largest, 0.01f)));
            float scale = (float)density / upem;
            float spreadPixels = SpreadEm * density;

            int width = Mathf.CeilToInt((maxX - minX) * scale + 2 * spreadPixels);
            int height = Mathf.CeilToInt((maxY - minY) * scale + 2 * spreadPixels);
            if (!Allocate(s_Sdf, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y))
                return default;
            x += Bleed;
            y += Bleed;

            float offsetX = spreadPixels - minX * scale, offsetY = spreadPixels - minY * scale;
            int lineStart = s_Lines.Length;
            OutlineFlattener.Flatten(s_Commands, count, scale, offsetX, offsetY, s_Lines);
            s_Tiles.Add(new SdfTile
            {
                Target = (byte*)s_Sdf.Pixels[page],
                Stride = PageSize,
                X = x, Y = y, Width = width, Height = height,
                LineStart = lineStart,
                LineCount = s_Lines.Length - lineStart,
                Spread = spreadPixels,
            });
            s_Uploads.Add(new PendingUpload { Set = s_Sdf, Page = page, X = x, Y = y, Width = width, Height = height });

            return new AtlasGlyph
            {
                Kind = AtlasGlyph.Sdf,
                Page = (ushort)page,
                U0 = (float)x / PageSize, V0 = (float)y / PageSize,
                U1 = (float)(x + width) / PageSize, V1 = (float)(y + height) / PageSize,
                Left = -offsetX / scale, Bottom = -offsetY / scale,
                Right = (width - offsetX) / scale, Top = (height - offsetY) / scale,
            };
        }

        static AtlasGlyph CreateColorLayers(FontInstance instance, uint glyph)
        {
            FontFace face = instance.Face;
            int layerCount = Mathf.Min(LetteraNative.lt_glyph_color_layers(face.Face, glyph, null, null, 0), 256);
            uint* layerGlyphs = stackalloc uint[layerCount];
            uint* colorIndices = stackalloc uint[layerCount];
            LetteraNative.lt_glyph_color_layers(face.Face, glyph, layerGlyphs, colorIndices, layerCount);

            uint[] palette = face.Palette;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < layerCount; i++)
            {
                int count = ReadOutline(instance, layerGlyphs[i]);
                if (count == 0) continue;
                Bounds2(s_Commands, count, out float a, out float b, out float c, out float d);
                minX = Mathf.Min(minX, a); minY = Mathf.Min(minY, b); maxX = Mathf.Max(maxX, c); maxY = Mathf.Max(maxY, d);
            }
            if (maxX <= minX || maxY <= minY) return default;

            float scale = (ColorTileHeight - 2f) / Mathf.Max(maxY - minY, maxX - minX);
            int width = Mathf.CeilToInt((maxX - minX) * scale) + 2;
            int height = Mathf.CeilToInt((maxY - minY) * scale) + 2;
            if (!Allocate(s_Color, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
            x += Bleed;
            y += Bleed;

            float offsetX = 1 - minX * scale, offsetY = 1 - minY * scale;
            var coverage = new NativeArray<byte>(width * height, Allocator.Temp);
            var lines = new NativeList<float4>(1024, Allocator.TempJob);
            var tiles = new NativeArray<SdfTile>(1, Allocator.TempJob);
            byte* pixels = (byte*)s_Color.Pixels[page];
            for (int i = 0; i < layerCount; i++)
            {
                int count = ReadOutline(instance, layerGlyphs[i]);
                if (count == 0) continue;
                lines.Clear();
                OutlineFlattener.Flatten(s_Commands, count, scale, offsetX, offsetY, lines);
                // A half pixel spread turns the distance into antialiased coverage.
                tiles[0] = new SdfTile { Target = (byte*)coverage.GetUnsafePtr(), Stride = width, Width = width, Height = height, LineCount = lines.Length, Spread = 0.5f };
                new SdfTileJob { Tiles = tiles, Lines = lines.AsArray() }.Run(1);

                uint index = colorIndices[i];
                uint argb = index < (uint)palette.Length ? palette[index] : 0xFF000000u;
                for (int row = 0; row < height; row++)
                {
                    byte* target = pixels + ((y + row) * PageSize + x) * 4;
                    for (int column = 0; column < width; column++, target += 4)
                        BlendOver(target, argb, coverage[row * width + column]);
                }
            }
            lines.Dispose();
            tiles.Dispose();
            coverage.Dispose();

            s_Uploads.Add(new PendingUpload { Set = s_Color, Page = page, X = x, Y = y, Width = width, Height = height });
            return new AtlasGlyph
            {
                Kind = AtlasGlyph.Color,
                Page = (ushort)page,
                U0 = (float)x / PageSize, V0 = (float)y / PageSize,
                U1 = (float)(x + width) / PageSize, V1 = (float)(y + height) / PageSize,
                Left = -offsetX / scale, Bottom = -offsetY / scale,
                Right = (width - offsetX) / scale, Top = (height - offsetY) / scale,
            };
        }

        static void BlendOver(byte* target, uint argb, byte coverage)
        {
            float sourceAlpha = ((argb >> 24) & 255) / 255f * (coverage / 255f);
            if (sourceAlpha <= 0) return;
            float targetAlpha = target[3] / 255f;
            float alpha = sourceAlpha + targetAlpha * (1 - sourceAlpha);
            float k = sourceAlpha / alpha;
            target[0] = (byte)(((argb >> 16) & 255) * k + target[0] * (1 - k));
            target[1] = (byte)(((argb >> 8) & 255) * k + target[1] * (1 - k));
            target[2] = (byte)((argb & 255) * k + target[2] * (1 - k));
            target[3] = (byte)(alpha * 255f + 0.5f);
        }

        static AtlasGlyph CreateBitmap(FontInstance instance, uint glyph)
        {
            int length = LetteraNative.lt_glyph_png(instance.Font, glyph, null, 0);
            if (length <= 0) return default;
            var png = new byte[length];
            fixed (byte* p = png) LetteraNative.lt_glyph_png(instance.Font, glyph, p, length);

            int* extents = stackalloc int[4];
            if (LetteraNative.lt_glyph_extents(instance.Font, glyph, extents) == 0) return default;

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!decoded.LoadImage(png, false)) return default;
                float shrink = Mathf.Min(1f, (float)ColorTileHeight / Mathf.Max(decoded.width, decoded.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(decoded.width * shrink));
                int height = Mathf.Max(1, Mathf.RoundToInt(decoded.height * shrink));
                if (!Allocate(s_Color, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
                x += Bleed;
                y += Bleed;

                byte* pixels = (byte*)s_Color.Pixels[page];
                for (int row = 0; row < height; row++)
                {
                    byte* target = pixels + ((y + row) * PageSize + x) * 4;
                    for (int column = 0; column < width; column++, target += 4)
                    {
                        Color32 c = decoded.GetPixelBilinear((column + 0.5f) / width, (row + 0.5f) / height);
                        target[0] = c.r; target[1] = c.g; target[2] = c.b; target[3] = c.a;
                    }
                }
                s_Uploads.Add(new PendingUpload { Set = s_Color, Page = page, X = x, Y = y, Width = width, Height = height });

                // hb reports y-bearing at the top, height negative.
                return new AtlasGlyph
                {
                    Kind = AtlasGlyph.Color,
                    Page = (ushort)page,
                    U0 = (float)x / PageSize, V0 = (float)y / PageSize,
                    U1 = (float)(x + width) / PageSize, V1 = (float)(y + height) / PageSize,
                    Left = extents[0], Top = extents[1],
                    Right = extents[0] + extents[2], Bottom = extents[1] + extents[3],
                };
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(decoded);
                else UnityEngine.Object.DestroyImmediate(decoded);
            }
        }

        static byte[] s_EmojiPixels;

        static AtlasGlyph CreateBrowserEmoji(uint glyph)
        {
            string cluster = BrowserEmoji.ClusterText(glyph);
            int pixels = BrowserEmoji.TilePixels;
            int width = Mathf.Clamp(Mathf.CeilToInt(BrowserEmoji.Advance(glyph) * pixels / (float)BrowserEmoji.Upem), 1, PageSize - 2 * Bleed);
            int ascent = Mathf.CeilToInt(BrowserEmoji.AscentEm * pixels);
            int height = ascent + Mathf.CeilToInt(BrowserEmoji.DescentEm * pixels);
            int bytes = width * height * 4;
            if (s_EmojiPixels == null || s_EmojiPixels.Length < bytes) s_EmojiPixels = new byte[bytes];
            BrowserEmoji.Renderer.Render(cluster, pixels, width, height, ascent, s_EmojiPixels);
            if (!Allocate(s_Color, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
            x += Bleed;
            y += Bleed;

            // The canvas fills from the top; the atlas's first row is the bottom.
            byte* target = (byte*)s_Color.Pixels[page];
            fixed (byte* source = s_EmojiPixels)
                for (int row = 0; row < height; row++)
                    UnsafeUtility.MemCpy(target + ((y + row) * PageSize + x) * 4, source + (height - 1 - row) * width * 4, width * 4);
            s_Uploads.Add(new PendingUpload { Set = s_Color, Page = page, X = x, Y = y, Width = width, Height = height });

            float unit = (float)BrowserEmoji.Upem / pixels;
            return new AtlasGlyph
            {
                Kind = AtlasGlyph.Color,
                Page = (ushort)page,
                U0 = (float)x / PageSize, V0 = (float)y / PageSize,
                U1 = (float)(x + width) / PageSize, V1 = (float)(y + height) / PageSize,
                Left = 0, Right = width * unit,
                Top = ascent * unit, Bottom = (ascent - height) * unit,
            };
        }

        static void Bounds2(float[] commands, int count, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            int i = 0;
            while (i < count)
            {
                int op = (int)commands[i++];
                int points = op == 2 ? 2 : op == 3 ? 3 : op == 4 ? 0 : 1;
                for (int p = 0; p < points; p++, i += 2)
                {
                    float x = commands[i], y = commands[i + 1];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        static bool Allocate(PageSet set, int width, int height, out int page, out int x, out int y)
        {
            page = x = y = 0;
            if (width > PageSize || height > PageSize) return false;
            if (set.Pixels.Count == 0) AddPage(set);
            if (set.ShelfX + width > PageSize)
            {
                set.ShelfY += set.ShelfHeight;
                set.ShelfX = 0;
                set.ShelfHeight = 0;
            }
            if (set.ShelfY + height > PageSize)
            {
                if (set.Pixels.Count < set.MaxPages) AddPage(set);
                else RecycleOldestPage(set);
                set.ShelfX = set.ShelfY = set.ShelfHeight = 0;
            }
            page = set.Current;
            set.LastUsed[page] = s_Tick;
            x = set.ShelfX;
            y = set.ShelfY;
            set.ShelfX += width;
            set.ShelfHeight = Mathf.Max(set.ShelfHeight, height);
            return true;
        }

        // Glyphs on the page that went unused longest make room; texts still showing one get it placed again.
        static void RecycleOldestPage(PageSet set)
        {
            CompleteRaster();
            int oldest = 0;
            for (int i = 1; i < set.LastUsed.Count; i++)
                if (set.LastUsed[i] < set.LastUsed[oldest])
                    oldest = i;

            s_Evicted.Clear();
            foreach (KeyValuePair<ulong, AtlasGlyph> pair in s_Glyphs)
                if (pair.Value.Page == oldest && pair.Value.Kind == (set == s_Sdf ? AtlasGlyph.Sdf : AtlasGlyph.Color))
                    s_Evicted.Add(pair.Key);
            foreach (ulong key in s_Evicted) s_Glyphs.Remove(key);
            for (int i = s_Uploads.Count - 1; i >= 0; i--)
                if (s_Uploads[i].Set == set && s_Uploads[i].Page == oldest)
                    s_Uploads.RemoveAt(i);

            UnsafeUtility.MemClear((void*)set.Pixels[oldest], (long)PageSize * PageSize * set.Bytes);
            set.Current = oldest;
            set.NeedsFullUpload = true;
            s_ClearPending = true;
        }

        static void AddPage(PageSet set)
        {
            long bytes = (long)PageSize * PageSize * set.Bytes;
            void* memory = UnsafeUtility.MallocTracked(bytes, 16, Allocator.Persistent, 0);
            UnsafeUtility.MemClear(memory, bytes);
            set.Pixels.Add((IntPtr)memory);
            set.LastUsed.Add(s_Tick);
            set.Current = set.Pixels.Count - 1;
            CompleteRaster();
            RecreateTexture(set);
        }

        static void RecreateTexture(PageSet set)
        {
            DestroyTexture(set.Texture);
            var texture = new Texture2DArray(PageSize, PageSize, set.Pixels.Count, FormatOf(set), TextureCreationFlags.None)
            {
                name = set == s_Sdf ? "Lettera SDF Atlas" : "Lettera Color Atlas",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            for (int page = 0; page < set.Pixels.Count; page++)
            {
                var pixels = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                    (void*)set.Pixels[page], PageSize * PageSize * set.Bytes, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref pixels, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                texture.SetPixelData(pixels, 0, page);
            }
            texture.Apply(false, true);
            set.Texture = texture;
            s_Rebind = true;
        }

        // Only a linear project decodes sRGB colors back to what the shader works in; a gamma project would darken them.
        static GraphicsFormat FormatOf(PageSet set) =>
            set.Format == GraphicsFormat.R8G8B8A8_SRGB && QualitySettings.activeColorSpace != ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_UNorm : set.Format;

        static void DestroyTexture(Texture texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        static void CompleteRaster()
        {
            if (s_Tiles.Count == 0) return;
            var tiles = new NativeArray<SdfTile>(s_Tiles.Count, Allocator.TempJob);
            for (int i = 0; i < s_Tiles.Count; i++) tiles[i] = s_Tiles[i];
            new SdfTileJob { Tiles = tiles, Lines = s_Lines.AsArray() }.Schedule(tiles.Length, 4).Complete();
            tiles.Dispose();
            s_Tiles.Clear();
            s_Lines.Clear();
        }

        /// <summary>Computes pending pixels and uploads them. Cheap when nothing is pending.</summary>
        public static void Flush()
        {
            s_Tick++;
            EnsureBound();
            if (s_Uploads.Count > 0 || s_Tiles.Count > 0 || s_Sdf.NeedsFullUpload || s_Color.NeedsFullUpload)
            {
                CompleteRaster();
                UploadPending();
            }
            if (s_ClearPending)
            {
                s_ClearPending = false;
                Cleared?.Invoke();
            }
        }

        static void EnsureBound()
        {
            if (s_Sdf.Texture == null && s_Sdf.Pixels.Count == 0) AddPage(s_Sdf);
            if (s_Sdf.Texture == null) RecreateTexture(s_Sdf);
            if (s_Color.Texture == null && s_Color.Pixels.Count > 0) RecreateTexture(s_Color);
            if (s_Color.Texture == null && s_EmptyColor == null)
            {
                // No colour glyph yet: a tiny placeholder instead of a 16 MB page nobody reads.
                s_EmptyColor = new Texture2DArray(4, 4, 1, FormatOf(s_Color), TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
                s_EmptyColor.Apply(false, true);
                s_Rebind = true;
            }
            if (!s_Rebind) return;
            s_Rebind = false;
            Shader.SetGlobalTexture(s_SdfAtlasId, s_Sdf.Texture);
            Shader.SetGlobalTexture(s_ColorAtlasId, s_Color.Texture != null ? s_Color.Texture : s_EmptyColor);
        }

        static void UploadPending()
        {
            UploadSet(s_Sdf, ref s_StagingSdf, TextureFormat.R8);
            UploadSet(s_Color, ref s_StagingColor, TextureFormat.RGBA32);
            s_Uploads.Clear();
        }

        static void UploadSet(PageSet set, ref Texture2D staging, TextureFormat format)
        {
            bool any = false;
            foreach (PendingUpload upload in s_Uploads) any |= upload.Set == set;
            if (!any && !set.NeedsFullUpload) return;

            // Copying from a 2D texture into an array needs this flag, which some browsers lack.
            const UnityEngine.Rendering.CopyTextureSupport needed = UnityEngine.Rendering.CopyTextureSupport.Basic | UnityEngine.Rendering.CopyTextureSupport.DifferentTypes;
            if (set.NeedsFullUpload || (SystemInfo.copyTextureSupport & needed) != needed)
            {
                set.NeedsFullUpload = false;
                RecreateTexture(set);
                EnsureBound();
                return;
            }

            if (staging == null)
                staging = new Texture2D(StagingSize, StagingSize, format, false, FormatOf(set) != GraphicsFormat.R8G8B8A8_SRGB) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };

            byte* stagingPixels = (byte*)staging.GetRawTextureData<byte>().GetUnsafePtr();
            int stride = StagingSize * set.Bytes;
            int shelfX = 0, shelfY = 0, shelfHeight = 0;
            int batchStart = -1;
            for (int i = 0; i < s_Uploads.Count; i++)
            {
                PendingUpload upload = s_Uploads[i];
                if (upload.Set != set) continue;
                if (batchStart < 0) batchStart = i;
                if (shelfX + upload.Width > StagingSize)
                {
                    shelfY += shelfHeight;
                    shelfX = 0;
                    shelfHeight = 0;
                }
                if (shelfY + upload.Height > StagingSize)
                {
                    CopyBatch(set, staging, batchStart, i);
                    batchStart = i;
                    stagingPixels = (byte*)staging.GetRawTextureData<byte>().GetUnsafePtr();
                    shelfX = shelfY = shelfHeight = 0;
                }
                byte* source = (byte*)set.Pixels[upload.Page];
                int rowBytes = upload.Width * set.Bytes;
                for (int row = 0; row < upload.Height; row++)
                {
                    UnsafeUtility.MemCpy(
                        stagingPixels + (shelfY + row) * stride + shelfX * set.Bytes,
                        source + ((upload.Y + row) * PageSize + upload.X) * set.Bytes,
                        rowBytes);
                }
                shelfX += upload.Width;
                shelfHeight = Mathf.Max(shelfHeight, upload.Height);
            }
            if (batchStart >= 0) CopyBatch(set, staging, batchStart, s_Uploads.Count);
        }

        static void CopyBatch(PageSet set, Texture2D staging, int start, int end)
        {
            staging.Apply(false, false);
            int shelfX = 0, shelfY = 0, shelfHeight = 0;
            for (int i = start; i < end; i++)
            {
                PendingUpload upload = s_Uploads[i];
                if (upload.Set != set) continue;
                if (shelfX + upload.Width > StagingSize)
                {
                    shelfY += shelfHeight;
                    shelfX = 0;
                    shelfHeight = 0;
                }
                Graphics.CopyTexture(staging, 0, 0, shelfX, shelfY, upload.Width, upload.Height,
                    set.Texture, upload.Page, 0, upload.X, upload.Y);
                shelfX += upload.Width;
                shelfHeight = Mathf.Max(shelfHeight, upload.Height);
            }
        }

        /// <summary>Forgets every glyph, keeping one empty page per set. Texts rebuild through <see cref="Cleared"/>.</summary>
        public static void Clear()
        {
            CompleteRaster();
            s_Uploads.Clear();
            s_Glyphs.Clear();
            ResetSet(s_Sdf);
            bool hadColor = s_Color.Pixels.Count > 0;
            FreePages(s_Color);
            s_Color.ShelfX = s_Color.ShelfY = s_Color.ShelfHeight = 0;
            if (hadColor) AddPage(s_Color);
            s_ClearPending = true;
        }

        static void ResetSet(PageSet set)
        {
            FreePages(set);
            set.ShelfX = set.ShelfY = set.ShelfHeight = 0;
            AddPage(set);
        }

        static void FreePages(PageSet set)
        {
            foreach (IntPtr page in set.Pixels) UnsafeUtility.FreeTracked((void*)page, Allocator.Persistent);
            set.Pixels.Clear();
            set.LastUsed.Clear();
            set.Current = 0;
        }

        /// <summary>Re-creates the textures from the CPU pages, e.g. after the graphics context was lost.</summary>
        public static void RestoreTextures()
        {
            if (s_Sdf.Pixels.Count > 0) RecreateTexture(s_Sdf);
            if (s_Color.Pixels.Count > 0) RecreateTexture(s_Color);
            EnsureBound();
        }

        /// <summary>Releases everything (domain reload, tests).</summary>
        public static void Shutdown()
        {
            s_Tiles.Clear();
            s_Uploads.Clear();
            s_Glyphs.Clear();
            FreePages(s_Sdf);
            FreePages(s_Color);
            s_Sdf.ShelfX = s_Sdf.ShelfY = s_Sdf.ShelfHeight = 0;
            s_Color.ShelfX = s_Color.ShelfY = s_Color.ShelfHeight = 0;
            DestroyTexture(s_Sdf.Texture);
            DestroyTexture(s_Color.Texture);
            DestroyTexture(s_StagingSdf);
            DestroyTexture(s_StagingColor);
            DestroyTexture(s_EmptyColor);
            s_EmptyColor = null;
            s_Rebind = false;
            s_ClearPending = false;
            s_Sdf.Texture = s_Color.Texture = null;
            s_StagingSdf = s_StagingColor = null;
            if (s_Lines.IsCreated) s_Lines.Dispose();
        }
    }
}
