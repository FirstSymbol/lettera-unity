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
        public const byte Empty = 0, Sdf = 1, Color = 2, Msdf = 3;

        public bool HasDistance => Kind == Sdf || Kind == Msdf;

        /// <summary>One texel per font pixel, read without filtering.</summary>
        public bool Pixel;

        public byte Kind;
        public ushort Page;
        public float U0, V0, U1, V1;
        public float Left, Bottom, Right, Top;

        /// <summary>How far the distance field of this entry reaches beyond the outline, in em.</summary>
        public float Spread;
    }

    /// <summary>
    /// The atlases every text shares, bound globally so different fonts never split a batch: one SDF
    /// texture array and one color texture array. Glyphs are placed on request; pixels are computed and
    /// uploaded together by Flush, once per frame.
    /// </summary>
    internal static unsafe class GlyphAtlas
    {
        /// <summary>The SDF reaches this far from the edge, in em, until a layer asks for more.</summary>
        public const float SpreadEm = 0.1875f;

        /// <summary>The farthest a layer can reach from the edge, in em.</summary>
        public const float MaxReachEm = 1f;

        /// <summary>Pages made before the settings were read, and the size of a page when nothing says otherwise.</summary>
        internal const int DefaultPageSize = 2048;

        /// <summary>Overrides every set's page size for tests; 0 reads the settings.</summary>
        internal static int PageSizeForTests;
        // Browsers on phones stop a page long before desktops run out, so the web keeps fewer pages.
#if UNITY_WEBGL && !UNITY_EDITOR
        const int DefaultSdfPages = 3;
        const int DefaultColorPages = 1;
        const int DefaultMsdfPages = 1;
#else
        const int DefaultSdfPages = 8;
        const int DefaultColorPages = 4;
        const int DefaultMsdfPages = 4;
#endif
        const int ColorTileHeight = 96;
        const int StagingSize = 1024;
        const int Bleed = 1;

        static readonly int s_SdfAtlasId = Shader.PropertyToID("_LetteraSdfAtlas");
        static readonly int s_ColorAtlasId = Shader.PropertyToID("_LetteraColorAtlas");
        static readonly int s_MsdfAtlasId = Shader.PropertyToID("_LetteraMsdfAtlas");
        static readonly int s_SdfAtlasSizeId = Shader.PropertyToID("_LetteraSdfAtlasSize");

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

            int m_Size = DefaultPageSize;

            // A set takes the configured size while it holds no page, and keeps it while it has any.
            public int Size => Pixels.Count > 0 ? m_Size : (m_Size = ConfiguredSize(this));

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

        static int ConfiguredSize(PageSet set)
        {
            if (PageSizeForTests > 0) return PageSizeForTests;
            LetteraAtlasSettings atlas = LetteraSettings.Instance.Atlas;
            LetteraAtlasPageSize size = set == s_Sdf ? atlas.SdfPageSize : set == s_Color ? atlas.ColorPageSize : atlas.MsdfPageSize;
            return size == LetteraAtlasPageSize.Size1024 ? 1024 : 2048;
        }

        static readonly PageSet s_Sdf = new PageSet(GraphicsFormat.R8_UNorm, 1, DefaultSdfPages, s_SdfAtlasId);
        static readonly PageSet s_Color = new PageSet(GraphicsFormat.R8G8B8A8_SRGB, 4, DefaultColorPages, s_ColorAtlasId);
        static readonly PageSet s_Msdf = new PageSet(GraphicsFormat.R8G8B8A8_UNorm, 4, DefaultMsdfPages, s_MsdfAtlasId);
        static readonly Dictionary<ulong, AtlasGlyph> s_Glyphs = new Dictionary<ulong, AtlasGlyph>();

        // The last entry seen at each of a few slots, so a mesh's repeated letters skip the dictionary. Any change of the dictionary makes every slot old.
        struct Memo
        {
            public ulong Key;
            public int Version;
            public AtlasGlyph Entry;
        }

        static readonly Memo[] s_Memo = new Memo[1024];

        // For the start-up thread: the atlas's collections, without its statics, which ask Unity for property ids.
        internal static class Types
        {
            internal static void Prepare()
            {
                _ = new Dictionary<ulong, AtlasGlyph>().TryGetValue(0, out _);
                _ = new Dictionary<ulong, int>().TryGetValue(0, out _);
                _ = new List<PendingUpload>();
                _ = new List<SdfTile>();
                _ = new List<UnionPart>();
                _ = new List<PendingEmoji>();
                _ = new List<ulong>();
                _ = new Memo[1];
                var lines = new NativeList<float4>(1, Allocator.Persistent);
                lines.Dispose();
                var colors = new NativeList<byte>(1, Allocator.Persistent);
                colors.Dispose();
                var tiles = new NativeArray<SdfTile>(1, Allocator.Persistent);
                tiles.Dispose();
            }
        }
        static int s_MemoVersion = 1;

        static void GlyphsChanged() => s_MemoVersion++;
        static readonly List<PendingUpload> s_Uploads = new List<PendingUpload>();
        static readonly List<SdfTile> s_Tiles = new List<SdfTile>();
        static NativeList<float4> s_Lines;
        static NativeList<byte> s_LineColors;
        static float[] s_Commands = new float[4096];
        static readonly List<ulong> s_Evicted = new List<ulong>();
        static Texture2D s_StagingSdf, s_StagingColor, s_StagingMsdf;
        static bool s_Rebind;
        static bool s_ClearPending;
        static int s_Tick;
        // Asked of the engine once per flush, and not once per glyph.
        static int s_Frame;
        static Texture2DArray s_EmptyColor, s_EmptyMsdf, s_EmptySdf;

        /// <summary>Raised when the atlas was cleared: every text must rebuild its mesh.</summary>
        public static event Action Cleared;

        public static int SdfPageCount => s_Sdf.Pixels.Count;

        /// <summary>What the pages of every kind hold in memory, in bytes: each page is this once on the CPU and once on the GPU.</summary>
        public static long PageBytes => SetBytes(s_Sdf) + SetBytes(s_Color) + SetBytes(s_Msdf);

        /// <summary>The side of a page of each kind, in texels: distance fields, colour, multi-channel.</summary>
        public static int SdfPageSize => s_Sdf.Size;
        public static int ColorPageSize => s_Color.Size;
        public static int MsdfPageSize => s_Msdf.Size;

        static long SetBytes(PageSet set) => (long)set.Pixels.Count * set.Size * set.Size * set.Bytes;
        public static int ColorPageCount => s_Color.Pixels.Count;
        public static int MsdfPageCount => s_Msdf.Pixels.Count;

        /// <summary>The GPU copy of an atlas, for an inspector's preview; null while it has no pages.</summary>
        internal static Texture2DArray TextureOf(int set) => set == 0 ? s_Sdf.Texture : set == 1 ? s_Msdf.Texture : s_Color.Texture;
        public static int GlyphCount => s_Glyphs.Count;

        /// <summary>Limits the page counts, e.g. for tests; a set at its limit recycles its least recently used page.</summary>
        internal static void SetPageLimits(int sdf, int color)
        {
            s_Sdf.MaxPages = sdf;
            s_Color.MaxPages = color;
        }

        internal static (int sdf, int color) PageLimits => (s_Sdf.MaxPages, s_Color.MaxPages);

        // Reach only grows: an entry rasterized for more reach serves every text that needs less.
        static float SpreadFor(float reach) => reach <= SpreadEm ? SpreadEm : reach <= 0.375f ? 0.375f : reach <= 0.75f ? 0.75f : MaxReachEm;

        // Glyphs someone asked to keep sharper: every text showing them uses at least this density.
        static readonly Dictionary<ulong, int> s_MinDensity = new Dictionary<ulong, int>();

        /// <summary>Raises a glyph's density for all its users. It grows only.</summary>
        public static int Boost(FontInstance instance, uint glyph, int pixelsPerEm)
        {
            ulong key = ((ulong)(uint)instance.Id << 32) | glyph;
            if (s_MinDensity.TryGetValue(key, out int known) && known >= pixelsPerEm) return known;
            s_MinDensity[key] = pixelsPerEm;
            return pixelsPerEm;
        }

        public static AtlasGlyph Get(FontInstance instance, uint glyph, int pixelsPerEm, float reach = 0, bool multi = false)
        {
            instance.Face.LastUsedFrame = s_Frame;
            int grid = instance.Face.PixelGrid;
            if (grid > 0)
            {
                pixelsPerEm = grid;
                multi = false;
            }
            else if (s_MinDensity.Count > 0 && s_MinDensity.TryGetValue(((ulong)(uint)instance.Id << 32) | glyph, out int raised) && raised > pixelsPerEm)
                pixelsPerEm = raised;
            ulong key = ((ulong)(uint)instance.Id << 36) | ((ulong)glyph << 8) | (uint)(pixelsPerEm >> 2 & 0x7F) | (multi ? 0x80u : 0);
            ref Memo memo = ref s_Memo[(int)(key ^ key >> 13 ^ key >> 36) & (s_Memo.Length - 1)];
            AtlasGlyph entry;
            bool known = memo.Key == key && memo.Version == s_MemoVersion;
            if (known) entry = memo.Entry;
            else if (known = s_Glyphs.TryGetValue(key, out entry))
            {
                memo.Key = key;
                memo.Version = s_MemoVersion;
                memo.Entry = entry;
            }
            if (known)
            {
                if (entry.HasDistance)
                {
                    if (entry.Spread < reach)
                    {
                        AtlasGlyph wider = grid > 0 ? CreatePixel(instance, glyph, grid, SpreadFor(reach)) : CreateSdf(instance, glyph, pixelsPerEm, SpreadFor(reach), multi);
                        if (wider.HasDistance)
                        {
                            s_Glyphs[key] = wider;
                            GlyphsChanged();
                            return wider;
                        }
                    }
                    (entry.Kind == AtlasGlyph.Sdf ? s_Sdf : s_Msdf).LastUsed[entry.Page] = s_Tick;
                }
                else if (entry.Kind == AtlasGlyph.Color) s_Color.LastUsed[entry.Page] = s_Tick;
                return entry;
            }
            entry = grid > 0 ? CreatePixel(instance, glyph, grid, SpreadFor(reach)) : Create(instance, glyph, pixelsPerEm, SpreadFor(reach), multi);
            s_Glyphs[key] = entry;
            GlyphsChanged();
            return entry;
        }

        static AtlasGlyph Create(FontInstance instance, uint glyph, int pixelsPerEm, float spread, bool multi)
        {
            FontFace face = instance.Face;
            if (face.IsVirtual) return CreateBrowserEmoji(glyph);
            if (face.HasColorPaint && LetteraNative.lt_glyph_has_paint(face.Face, glyph) != 0)
            {
                AtlasGlyph painted = CreateColorPaint(instance, glyph);
                if (painted.Kind != AtlasGlyph.Empty) return painted;
            }
            if (face.HasColorLayers && LetteraNative.lt_glyph_color_layers(face.Face, glyph, null, null, 0) > 0)
                return CreateColorLayers(instance, glyph);
            if (face.HasColorBitmaps)
            {
                AtlasGlyph bitmap = CreateBitmap(instance, glyph);
                if (bitmap.Kind != AtlasGlyph.Empty) return bitmap;
            }
            return CreateSdf(instance, glyph, pixelsPerEm, spread, multi);
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

        static AtlasGlyph CreateSdf(FontInstance instance, uint glyph, int pixelsPerEm, float spread, bool multi)
        {
            int count = ReadOutline(instance, glyph);
            if (count == 0) return default;
            if (!s_Lines.IsCreated) s_Lines = new NativeList<float4>(4096, Allocator.Persistent);
            if (!s_LineColors.IsCreated) s_LineColors = new NativeList<byte>(4096, Allocator.Persistent);
            PageSet set = multi ? s_Msdf : s_Sdf;

            int upem = instance.Face.Metrics.Upem;
            Bounds2(s_Commands, count, out float minX, out float minY, out float maxX, out float maxY);
            if (maxX <= minX || maxY <= minY) return default;

            // A glyph too large for a tile at this density simply uses fewer pixels per em.
            float largest = Mathf.Max(maxX - minX, maxY - minY) / upem + 2 * spread;
            int density = Mathf.Min(pixelsPerEm, Mathf.FloorToInt((StagingSize - 2 * Bleed) / Mathf.Max(largest, 0.01f)));
            float scale = (float)density / upem;
            float spreadPixels = spread * density;

            int width = Mathf.CeilToInt((maxX - minX) * scale + 2 * spreadPixels);
            int height = Mathf.CeilToInt((maxY - minY) * scale + 2 * spreadPixels);
            if (!Allocate(set, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y))
                return default;
            x += Bleed;
            y += Bleed;

            float offsetX = spreadPixels - minX * scale, offsetY = spreadPixels - minY * scale;
            int lineStart = s_Lines.Length;
            OutlineFlattener.Flatten(s_Commands, count, scale, offsetX, offsetY, s_Lines);
            if (multi) EdgeColoring.Color(s_Lines, s_LineColors, lineStart);
            s_Tiles.Add(new SdfTile
            {
                Target = (byte*)set.Pixels[page],
                Stride = set.Size * set.Bytes,
                Multi = multi,
                X = x, Y = y, Width = width, Height = height,
                LineStart = lineStart,
                LineCount = s_Lines.Length - lineStart,
                Spread = spreadPixels,
            });
            s_Uploads.Add(new PendingUpload { Set = set, Page = page, X = x, Y = y, Width = width, Height = height });

            return new AtlasGlyph
            {
                Kind = multi ? AtlasGlyph.Msdf : AtlasGlyph.Sdf,
                Page = (ushort)page,
                U0 = (float)x / set.Size, V0 = (float)y / set.Size,
                U1 = (float)(x + width) / set.Size, V1 = (float)(y + height) / set.Size,
                Left = -offsetX / scale, Bottom = -offsetY / scale,
                Right = (width - offsetX) / scale, Top = (height - offsetY) / scale,
                Spread = spread,
            };
        }

        readonly struct UnionPart
        {
            public readonly uint Glyph;
            public readonly float X, Y;

            public UnionPart(uint glyph, float x, float y)
            {
                Glyph = glyph;
                X = x;
                Y = y;
            }
        }

        static readonly List<UnionPart> s_UnionParts = new List<UnionPart>();
        static readonly Dictionary<ulong, AtlasGlyph> s_Unions = new Dictionary<ulong, AtlasGlyph>();

        /// <summary>Starts describing a merged shape. Parts are at font unit offsets from the first glyph.</summary>
        public static void BeginUnion() => s_UnionParts.Clear();

        public static void AddUnionPart(uint glyph, float x, float y) => s_UnionParts.Add(new UnionPart(glyph, x, y));

        /// <summary>One atlas entry for several glyphs: its field has one outline, where they merge, so nothing inside it can show a seam.</summary>
        public static AtlasGlyph GetUnion(FontInstance instance, int pixelsPerEm, float reach, bool multi)
        {
            float spread = SpreadFor(reach);
            ulong key = 14695981039346656037UL;
            key = (key ^ (uint)instance.Id) * 1099511628211UL;
            key = (key ^ (uint)pixelsPerEm) * 1099511628211UL;
            key = (key ^ (uint)Mathf.RoundToInt(spread * 64) ^ (multi ? 0x10000u : 0)) * 1099511628211UL;
            foreach (UnionPart part in s_UnionParts)
            {
                key = (key ^ part.Glyph) * 1099511628211UL;
                key = (key ^ (uint)Mathf.RoundToInt(part.X)) * 1099511628211UL;
                key = (key ^ (uint)Mathf.RoundToInt(part.Y)) * 1099511628211UL;
            }
            if (s_Unions.TryGetValue(key, out AtlasGlyph known) && (multi ? s_Msdf : s_Sdf).Pixels.Count > known.Page)
            {
                (multi ? s_Msdf : s_Sdf).LastUsed[known.Page] = s_Tick;
                return known;
            }

            if (!s_Lines.IsCreated) s_Lines = new NativeList<float4>(4096, Allocator.Persistent);
            if (!s_LineColors.IsCreated) s_LineColors = new NativeList<byte>(4096, Allocator.Persistent);
            PageSet set = multi ? s_Msdf : s_Sdf;
            int upem = instance.Face.Metrics.Upem;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (UnionPart part in s_UnionParts)
            {
                int count = ReadOutline(instance, part.Glyph);
                if (count == 0) continue;
                Bounds2(s_Commands, count, out float a, out float b, out float c, out float d);
                minX = Mathf.Min(minX, a + part.X); minY = Mathf.Min(minY, b + part.Y);
                maxX = Mathf.Max(maxX, c + part.X); maxY = Mathf.Max(maxY, d + part.Y);
            }
            if (maxX <= minX || maxY <= minY) return default;

            float largest = Mathf.Max(maxX - minX, maxY - minY) / upem + 2 * spread;
            int density = Mathf.Min(pixelsPerEm, Mathf.FloorToInt((StagingSize - 2 * Bleed) / Mathf.Max(largest, 0.01f)));
            float scale = (float)density / upem;
            float spreadPixels = spread * density;
            int width = Mathf.CeilToInt((maxX - minX) * scale + 2 * spreadPixels);
            int height = Mathf.CeilToInt((maxY - minY) * scale + 2 * spreadPixels);
            if (!Allocate(set, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
            x += Bleed;
            y += Bleed;

            float offsetX = spreadPixels - minX * scale, offsetY = spreadPixels - minY * scale;
            int lineStart = s_Lines.Length;
            foreach (UnionPart part in s_UnionParts)
            {
                int count = ReadOutline(instance, part.Glyph);
                if (count == 0) continue;
                OutlineFlattener.Flatten(s_Commands, count, scale, offsetX + part.X * scale, offsetY + part.Y * scale, s_Lines);
            }
            if (multi) EdgeColoring.Color(s_Lines, s_LineColors, lineStart);
            s_Tiles.Add(new SdfTile
            {
                Target = (byte*)set.Pixels[page],
                Stride = set.Size * set.Bytes,
                Multi = multi,
                X = x, Y = y, Width = width, Height = height,
                LineStart = lineStart,
                LineCount = s_Lines.Length - lineStart,
                Spread = spreadPixels,
            });
            s_Uploads.Add(new PendingUpload { Set = set, Page = page, X = x, Y = y, Width = width, Height = height });
            var entry = new AtlasGlyph
            {
                Kind = multi ? AtlasGlyph.Msdf : AtlasGlyph.Sdf,
                Page = (ushort)page,
                U0 = (float)x / set.Size, V0 = (float)y / set.Size,
                U1 = (float)(x + width) / set.Size, V1 = (float)(y + height) / set.Size,
                Left = -offsetX / scale, Bottom = -offsetY / scale,
                Right = (width - offsetX) / scale, Top = (height - offsetY) / scale,
                Spread = spread,
            };
            s_Unions[key] = entry;
            return entry;
        }

        // A cell of the grid is filled when its center is inside the outline. Each other cell stores its distance to the nearest filled
        // cell's square, so a stroke of N covers the cells whose centers are within N of the glyph.
        static AtlasGlyph CreatePixel(FontInstance instance, uint glyph, int grid, float spread)
        {
            int count = ReadOutline(instance, glyph);
            if (count == 0) return default;
            int upem = instance.Face.Metrics.Upem;
            Bounds2(s_Commands, count, out float minX, out float minY, out float maxX, out float maxY);
            if (maxX <= minX || maxY <= minY) return default;

            float cell = (float)upem / grid;
            int margin = Mathf.CeilToInt(spread * grid) + 1;
            int cellMinX = Mathf.FloorToInt(minX / cell), cellMinY = Mathf.FloorToInt(minY / cell);
            int cellMaxX = Mathf.CeilToInt(maxX / cell), cellMaxY = Mathf.CeilToInt(maxY / cell);
            int inkWidth = cellMaxX - cellMinX, inkHeight = cellMaxY - cellMinY;
            int width = inkWidth + 2 * margin, height = inkHeight + 2 * margin;
            if (!Allocate(s_Sdf, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
            x += Bleed;
            y += Bleed;

            var lines = new NativeList<float4>(256, Allocator.Temp);
            float scale = 1f / cell;
            OutlineFlattener.Flatten(s_Commands, count, scale, -cellMinX, -cellMinY, lines);
            var filled = new NativeArray<bool>(inkWidth * inkHeight, Allocator.Temp);
            for (int row = 0; row < inkHeight; row++)
                for (int column = 0; column < inkWidth; column++)
                    filled[row * inkWidth + column] = WindingAt(lines, column + 0.5f, row + 0.5f) != 0;

            float toValue = 0.5f / (spread * grid);
            byte* target = (byte*)s_Sdf.Pixels[page];
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int cx = column - margin, cy = row - margin;
                    bool inside = cx >= 0 && cy >= 0 && cx < inkWidth && cy < inkHeight && filled[cy * inkWidth + cx];
                    float distance = float.MaxValue;
                    for (int fy = 0; fy < inkHeight; fy++)
                    {
                        for (int fx = 0; fx < inkWidth; fx++)
                        {
                            if (filled[fy * inkWidth + fx] == inside) continue;
                            float dx = Mathf.Max(Mathf.Abs(cx - fx) - 0.5f, 0), dy = Mathf.Max(Mathf.Abs(cy - fy) - 0.5f, 0);
                            float d = dx * dx + dy * dy;
                            if (d < distance) distance = d;
                        }
                    }
                    if (distance == float.MaxValue) distance = 0;
                    distance = Mathf.Sqrt(distance);
                    float value = inside ? 0.5f + distance * toValue : 0.5f - distance * toValue;
                    target[(y + row) * s_Sdf.Size + x + column] = (byte)(Mathf.Clamp01(value) * 255f + 0.5f);
                }
            }
            lines.Dispose();
            filled.Dispose();
            s_Uploads.Add(new PendingUpload { Set = s_Sdf, Page = page, X = x, Y = y, Width = width, Height = height });

            return new AtlasGlyph
            {
                Kind = AtlasGlyph.Sdf,
                Pixel = true,
                Page = (ushort)page,
                U0 = (float)x / s_Sdf.Size, V0 = (float)y / s_Sdf.Size,
                U1 = (float)(x + width) / s_Sdf.Size, V1 = (float)(y + height) / s_Sdf.Size,
                Left = (cellMinX - margin) * cell, Bottom = (cellMinY - margin) * cell,
                Right = (cellMaxX + margin) * cell, Top = (cellMaxY + margin) * cell,
                Spread = spread,
            };
        }

        static int WindingAt(NativeList<float4> lines, float x, float y)
        {
            int winding = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                float4 line = lines[i];
                if ((line.y <= y) == (line.w <= y)) continue;
                float crossing = line.x + (y - line.y) / (line.w - line.y) * (line.z - line.x);
                if (crossing > x) winding += line.w > line.y ? 1 : -1;
            }
            return winding;
        }

        /// <summary>COLRv1 glyphs painted so far, for a test that a font mixing kinds chooses glyph by glyph.</summary>
        internal static int PaintedGlyphs { get; private set; }

        static AtlasGlyph CreateColorPaint(FontInstance instance, uint glyph)
        {
            float* box = stackalloc float[4];
            if (LetteraNative.lt_glyph_paint_extents(instance.Font, glyph, box) == 0) return default;
            float minX = box[0], minY = box[1], maxX = box[2], maxY = box[3];
            if (maxX <= minX || maxY <= minY) return default;

            float scale = (ColorTileHeight - 2f) / Mathf.Max(maxY - minY, maxX - minX);
            int originX = Mathf.FloorToInt(minX * scale) - 1, originY = Mathf.FloorToInt(minY * scale) - 1;
            int width = Mathf.CeilToInt(maxX * scale) + 1 - originX, height = Mathf.CeilToInt(maxY * scale) + 1 - originY;

            // Painted first, so that a failure takes no room in the atlas.
            var painted = new NativeArray<byte>(width * height * 4, Allocator.Temp);
            byte* source = (byte*)painted.GetUnsafePtr();
            // 0x000000FF is opaque black as the library's color order has it. A glyph using the text's color does not follow it, as with COLRv0.
            if (LetteraNative.lt_glyph_paint_rgba(instance.Font, glyph, scale, 0, 0x000000FFu, originX, originY, source, width, height) == 0
                || !Allocate(s_Color, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y))
            {
                painted.Dispose();
                return default;
            }
            x += Bleed;
            y += Bleed;
            byte* pixels = (byte*)s_Color.Pixels[page];
            for (int row = 0; row < height; row++)
                UnsafeUtility.MemCpy(pixels + ((y + row) * s_Color.Size + x) * 4, source + row * width * 4, width * 4);
            painted.Dispose();
            PaintedGlyphs++;

            s_Uploads.Add(new PendingUpload { Set = s_Color, Page = page, X = x, Y = y, Width = width, Height = height });
            return new AtlasGlyph
            {
                Kind = AtlasGlyph.Color,
                Page = (ushort)page,
                U0 = (float)x / s_Color.Size, V0 = (float)y / s_Color.Size,
                U1 = (float)(x + width) / s_Color.Size, V1 = (float)(y + height) / s_Color.Size,
                Left = originX / scale, Bottom = originY / scale,
                Right = (originX + width) / scale, Top = (originY + height) / scale,
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
                new SdfTileJob { Tiles = tiles, Lines = lines.AsArray(),  }.Run(1);

                uint index = colorIndices[i];
                uint argb = index < (uint)palette.Length ? palette[index] : 0xFF000000u;
                for (int row = 0; row < height; row++)
                {
                    byte* target = pixels + ((y + row) * s_Color.Size + x) * 4;
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
                U0 = (float)x / s_Color.Size, V0 = (float)y / s_Color.Size,
                U1 = (float)(x + width) / s_Color.Size, V1 = (float)(y + height) / s_Color.Size,
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
                    byte* target = pixels + ((y + row) * s_Color.Size + x) * 4;
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
                    U0 = (float)x / s_Color.Size, V0 = (float)y / s_Color.Size,
                    U1 = (float)(x + width) / s_Color.Size, V1 = (float)(y + height) / s_Color.Size,
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

        static readonly Dictionary<ulong, AtlasGlyph> s_Rules = new Dictionary<ulong, AtlasGlyph>();

        /// <summary>A rule's tile: a true distance field of a rectangle of that thickness, in thousandths of an em, and of no real length.</summary>
        internal static AtlasGlyph GetRule(float thickness, bool vertical, int pixelsPerEm, float reach, bool multi)
        {
            int key16 = Mathf.Clamp(Mathf.RoundToInt(thickness * 4), 1, 65535);
            ulong key = ((ulong)(uint)key16 << 16) | ((ulong)(uint)(pixelsPerEm & 0x1FFF) << 2) | (vertical ? 2u : 0) | (multi ? 1u : 0);
            float spread = SpreadFor(reach);
            if (s_Rules.TryGetValue(key, out AtlasGlyph known) && known.Spread >= spread) return known;
            AtlasGlyph made = CreateRule(thickness, vertical, pixelsPerEm, spread, multi);
            s_Rules[key] = made;
            return made;
        }

        static AtlasGlyph CreateRule(float thickness, bool vertical, int pixelsPerEm, float spread, bool multi)
        {
            if (!s_Lines.IsCreated) s_Lines = new NativeList<float4>(4096, Allocator.Persistent);
            if (!s_LineColors.IsCreated) s_LineColors = new NativeList<byte>(4096, Allocator.Persistent);
            PageSet set = multi ? s_Msdf : s_Sdf;
            float density = pixelsPerEm;
            float scale = density / 1000f;
            float spreadPixels = spread * density;
            // Long enough that its ends reach nowhere near the middle column, which is then the whole field across the rule.
            float along = (2 * spreadPixels + 4) / scale;
            float w = vertical ? thickness : along, h = vertical ? along : thickness;
            int width = Mathf.Max(Mathf.CeilToInt(w * scale + 2 * spreadPixels), 1);
            int height = Mathf.Max(Mathf.CeilToInt(h * scale + 2 * spreadPixels), 1);
            if (!Allocate(set, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
            x += Bleed;
            y += Bleed;
            if (s_Commands.Length < 32) s_Commands = new float[32];
            // Counter-clockwise, as a font's outline turns where this reads it.
            int n = 0;
            s_Commands[n++] = 0; s_Commands[n++] = 0; s_Commands[n++] = 0;
            s_Commands[n++] = 1; s_Commands[n++] = 0; s_Commands[n++] = h;
            s_Commands[n++] = 1; s_Commands[n++] = w; s_Commands[n++] = h;
            s_Commands[n++] = 1; s_Commands[n++] = w; s_Commands[n++] = 0;
            s_Commands[n++] = 4;
            float offsetX = spreadPixels, offsetY = spreadPixels;
            int lineStart = s_Lines.Length;
            OutlineFlattener.Flatten(s_Commands, n, scale, offsetX, offsetY, s_Lines);
            if (multi) EdgeColoring.Color(s_Lines, s_LineColors, lineStart);
            s_Tiles.Add(new SdfTile
            {
                Target = (byte*)set.Pixels[page],
                Stride = set.Size * set.Bytes,
                Multi = multi,
                X = x, Y = y, Width = width, Height = height,
                LineStart = lineStart,
                LineCount = s_Lines.Length - lineStart,
                Spread = spreadPixels,
            });
            s_Uploads.Add(new PendingUpload { Set = set, Page = page, X = x, Y = y, Width = width, Height = height });
            return new AtlasGlyph
            {
                Kind = multi ? AtlasGlyph.Msdf : AtlasGlyph.Sdf,
                Page = (ushort)page,
                U0 = (float)x / set.Size, V0 = (float)y / set.Size,
                U1 = (float)(x + width) / set.Size, V1 = (float)(y + height) / set.Size,
                Left = -offsetX / scale, Bottom = -offsetY / scale,
                Right = (width - offsetX) / scale, Top = (height - offsetY) / scale,
                Spread = spread,
            };
        }

        struct PendingEmoji
        {
            public string Cluster;
            public int Page, X, Y, Width, Height, Baseline;
        }

        const int EmojiCanvasSide = 2048, EmojiPadding = 2;
        const double EmojiBatchMaxMs = 2;
        static readonly List<PendingEmoji> s_EmojiPending = new List<PendingEmoji>();
        static string[] s_EmojiNames = new string[64];
        static int[] s_EmojiSpots = new int[128];
        static byte[] s_EmojiCanvas;

        /// <summary>Canvas reads so far, for a test that many new emoji in a frame cost one.</summary>
        internal static int EmojiBatches { get; private set; }

        internal static byte[] ColorPixelsForTests(int page)
        {
            var copy = new byte[s_Color.Size * s_Color.Size * 4];
            fixed (byte* target = copy) UnsafeUtility.MemCpy(target, (byte*)s_Color.Pixels[page], copy.Length);
            return copy;
        }

        // The room is taken now, so a text's mesh can be built as ever. The pixels come at the frame's flush, all at once.
        static AtlasGlyph CreateBrowserEmoji(uint glyph)
        {
            string cluster = BrowserEmoji.ClusterText(glyph);
            int pixels = BrowserEmoji.TilePixels;
            int width = Mathf.Clamp(Mathf.CeilToInt(BrowserEmoji.Advance(glyph) * pixels / (float)BrowserEmoji.Upem), 1, s_Color.Size / 2);
            int ascent = Mathf.CeilToInt(BrowserEmoji.AscentEm * pixels);
            int height = ascent + Mathf.CeilToInt(BrowserEmoji.DescentEm * pixels);
            if (!Allocate(s_Color, width + 2 * Bleed, height + 2 * Bleed, out int page, out int x, out int y)) return default;
            x += Bleed;
            y += Bleed;

            // Until its pixels come, a recycled place must not show what stood there before.
            byte* clear = (byte*)s_Color.Pixels[page];
            for (int row = 0; row < height; row++) UnsafeUtility.MemClear(clear + ((y + row) * s_Color.Size + x) * 4, width * 4);
            s_EmojiPending.Add(new PendingEmoji { Cluster = cluster, Page = page, X = x, Y = y, Width = width, Height = height, Baseline = ascent });

            float unit = (float)BrowserEmoji.Upem / pixels;
            return new AtlasGlyph
            {
                Kind = AtlasGlyph.Color,
                Page = (ushort)page,
                U0 = (float)x / s_Color.Size, V0 = (float)y / s_Color.Size,
                U1 = (float)(x + width) / s_Color.Size, V1 = (float)(y + height) / s_Color.Size,
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
            if (width > set.Size || height > set.Size) return false;
            if (set.Pixels.Count == 0) AddPage(set);
            if (set.ShelfX + width > set.Size)
            {
                set.ShelfY += set.ShelfHeight;
                set.ShelfX = 0;
                set.ShelfHeight = 0;
            }
            if (set.ShelfY + height > set.Size)
            {
                // The limit is in pages of the default size, so smaller pages hold as many glyphs before the oldest is recycled.
                int perPage = DefaultPageSize / set.Size;
                if (set.Pixels.Count < set.MaxPages * perPage * perPage) AddPage(set);
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

            s_Rules.Clear();
            s_Evicted.Clear();
            foreach (KeyValuePair<ulong, AtlasGlyph> pair in s_Glyphs)
                if (pair.Value.Page == oldest && pair.Value.Kind == (set == s_Sdf ? AtlasGlyph.Sdf : set == s_Msdf ? AtlasGlyph.Msdf : AtlasGlyph.Color))
                    s_Evicted.Add(pair.Key);
            foreach (ulong key in s_Evicted) s_Glyphs.Remove(key);
            GlyphsChanged();
            s_Evicted.Clear();
            foreach (KeyValuePair<ulong, AtlasGlyph> pair in s_Unions)
                if (pair.Value.Page == oldest && pair.Value.Kind == (set == s_Sdf ? AtlasGlyph.Sdf : set == s_Msdf ? AtlasGlyph.Msdf : AtlasGlyph.Color))
                    s_Evicted.Add(pair.Key);
            foreach (ulong key in s_Evicted) s_Unions.Remove(key);
            for (int i = s_Uploads.Count - 1; i >= 0; i--)
                if (s_Uploads[i].Set == set && s_Uploads[i].Page == oldest)
                    s_Uploads.RemoveAt(i);

            UnsafeUtility.MemClear((void*)set.Pixels[oldest], (long)set.Size * set.Size * set.Bytes);
            // A page going back to the pool takes what was still to be drawn on it.
            if (set == s_Color) s_EmojiPending.RemoveAll(e => e.Page == oldest);
            set.Current = oldest;
            set.NeedsFullUpload = true;
            s_ClearPending = true;
        }

        static void AddPage(PageSet set)
        {
            long bytes = (long)set.Size * set.Size * set.Bytes;
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
            // Every page's pixels are set below and uploaded once: Unity's own clearing and first upload of the texture would be work thrown away.
            var texture = new Texture2DArray(set.Size, set.Size, set.Pixels.Count, FormatOf(set), TextureCreationFlags.DontInitializePixels | TextureCreationFlags.DontUploadUponCreate)
            {
                name = set == s_Sdf ? "Lettera SDF Atlas" : set == s_Msdf ? "Lettera MSDF Atlas" : "Lettera Color Atlas",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            for (int page = 0; page < set.Pixels.Count; page++)
            {
                var pixels = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                    (void*)set.Pixels[page], set.Size * set.Size * set.Bytes, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref pixels, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                texture.SetPixelData(pixels, 0, page);
            }
            texture.Apply(false, true);
            set.Texture = texture;
            // Bound at once: a canvas can draw before the next flush, which may run before it in the frame.
            Shader.SetGlobalTexture(set.PropertyId, texture);
            if (set == s_Sdf) Shader.SetGlobalFloat(s_SdfAtlasSizeId, set.Size);
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
            // Plain tiles read no colors, but the two lists stay in step because they share line indices.
            while (s_LineColors.Length < s_Lines.Length) s_LineColors.Add(0);
            new SdfTileJob { Tiles = tiles, Lines = s_Lines.AsArray(), LineColors = s_LineColors.GetUnsafePtr() }.Schedule(tiles.Length, 4).Complete();
            tiles.Dispose();
            s_Tiles.Clear();
            s_Lines.Clear();
            s_LineColors.Clear();
        }

        /// <summary>Computes pending pixels and uploads them. Cheap when nothing is pending.</summary>
        public static void Flush()
        {
            s_Tick++;
            s_Frame = Time.frameCount;
            EnsureBound();
            FlushEmoji();
            if (s_Uploads.Count > 0 || s_Tiles.Count > 0 || s_Sdf.NeedsFullUpload || s_Color.NeedsFullUpload || s_Msdf.NeedsFullUpload)
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
            if (s_Sdf.Texture == null && s_Sdf.Pixels.Count > 0) RecreateTexture(s_Sdf);
            if (s_Sdf.Texture == null && s_EmptySdf == null)
            {
                // No glyph yet, or none since the pages went back: a tiny placeholder until a glyph needs a page.
                s_EmptySdf = new Texture2DArray(4, 4, 1, GraphicsFormat.R8_UNorm, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
                s_EmptySdf.Apply(false, true);
                s_Rebind = true;
            }
            if (s_Color.Texture == null && s_Color.Pixels.Count > 0) RecreateTexture(s_Color);
            if (s_Color.Texture == null && s_EmptyColor == null)
            {
                // No colour glyph yet: a tiny placeholder instead of a 16 MB page nobody reads.
                s_EmptyColor = new Texture2DArray(4, 4, 1, FormatOf(s_Color), TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
                s_EmptyColor.Apply(false, true);
                s_Rebind = true;
            }
            if (s_Msdf.Texture == null && s_Msdf.Pixels.Count > 0) RecreateTexture(s_Msdf);
            if (s_Msdf.Texture == null && s_EmptyMsdf == null)
            {
                s_EmptyMsdf = new Texture2DArray(4, 4, 1, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { hideFlags = HideFlags.HideAndDontSave };
                s_EmptyMsdf.Apply(false, true);
                s_Rebind = true;
            }
            if (!s_Rebind) return;
            s_Rebind = false;
            Shader.SetGlobalTexture(s_SdfAtlasId, s_Sdf.Texture != null ? s_Sdf.Texture : s_EmptySdf);
            Shader.SetGlobalTexture(s_ColorAtlasId, s_Color.Texture != null ? s_Color.Texture : s_EmptyColor);
            Shader.SetGlobalTexture(s_MsdfAtlasId, s_Msdf.Texture != null ? s_Msdf.Texture : s_EmptyMsdf);
        }

        static void UploadPending()
        {
            UploadSet(s_Sdf, ref s_StagingSdf, TextureFormat.R8);
            UploadSet(s_Color, ref s_StagingColor, TextureFormat.RGBA32);
            UploadSet(s_Msdf, ref s_StagingMsdf, TextureFormat.RGBA32);
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
                        source + ((upload.Y + row) * set.Size + upload.X) * set.Bytes,
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

        static void FlushEmoji()
        {
            if (s_EmojiPending.Count == 0) return;
            IEmojiRenderer renderer = BrowserEmoji.Renderer;
            if (renderer == null)
            {
                s_EmojiPending.Clear();
            s_Rules.Clear();
                return;
            }
            if (renderer is IEmojiBatchRenderer batch) FlushEmojiBatches(batch);
            else FlushEmojiOneByOne(renderer);
        }

        static void FlushEmojiOneByOne(IEmojiRenderer renderer)
        {
            foreach (PendingEmoji emoji in s_EmojiPending)
            {
                int bytes = emoji.Width * emoji.Height * 4;
                if (s_EmojiPixels == null || s_EmojiPixels.Length < bytes) s_EmojiPixels = new byte[bytes];
                renderer.Render(emoji.Cluster, BrowserEmoji.TilePixels, emoji.Width, emoji.Height, emoji.Baseline, s_EmojiPixels);
                PlaceEmoji(emoji, s_EmojiPixels, emoji.Width, 0, 0);
            }
            s_EmojiPending.Clear();
        }

        static void FlushEmojiBatches(IEmojiBatchRenderer batch)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;
            while (done < s_EmojiPending.Count)
            {
                // Rows, left to right, until the canvas is full.
                int count = 0, x = 0, y = 0, rowHeight = 0, canvasWidth = 1;
                if (s_EmojiPending.Count - done > s_EmojiNames.Length)
                {
                    s_EmojiNames = new string[s_EmojiPending.Count - done];
                    s_EmojiSpots = new int[s_EmojiNames.Length * 2];
                }
                for (int i = done; i < s_EmojiPending.Count; i++)
                {
                    PendingEmoji emoji = s_EmojiPending[i];
                    if (x > 0 && x + emoji.Width > EmojiCanvasSide)
                    {
                        x = 0;
                        y += rowHeight + EmojiPadding;
                        rowHeight = 0;
                    }
                    if (count > 0 && y + emoji.Height > EmojiCanvasSide) break;
                    s_EmojiNames[count] = emoji.Cluster;
                    s_EmojiSpots[count * 2] = x;
                    s_EmojiSpots[count * 2 + 1] = y + emoji.Baseline;
                    x += emoji.Width + EmojiPadding;
                    canvasWidth = Mathf.Max(canvasWidth, x);
                    rowHeight = Mathf.Max(rowHeight, emoji.Height);
                    count++;
                }
                int canvasHeight = y + rowHeight;
                int bytes = canvasWidth * canvasHeight * 4;
                if (s_EmojiCanvas == null || s_EmojiCanvas.Length < bytes) s_EmojiCanvas = new byte[bytes];
                batch.RenderBatch(s_EmojiNames, 0, count, s_EmojiSpots, BrowserEmoji.TilePixels, canvasWidth, canvasHeight, s_EmojiCanvas);
                EmojiBatches++;
                for (int i = 0; i < count; i++)
                {
                    PendingEmoji emoji = s_EmojiPending[done + i];
                    PlaceEmoji(emoji, s_EmojiCanvas, canvasWidth, s_EmojiSpots[i * 2], s_EmojiSpots[i * 2 + 1] - emoji.Baseline);
                }
                done += count;
                // What did not fit in this frame's time comes in the next: until then it draws as nothing.
                if (clock.Elapsed.TotalMilliseconds > EmojiBatchMaxMs) break;
            }
            s_EmojiPending.RemoveRange(0, done);
        }

        // The canvas fills from the top; the atlas's first row is the bottom.
        static void PlaceEmoji(in PendingEmoji emoji, byte[] canvas, int canvasWidth, int fromX, int fromY)
        {
            byte* target = (byte*)s_Color.Pixels[emoji.Page];
            fixed (byte* source = canvas)
                for (int row = 0; row < emoji.Height; row++)
                    UnsafeUtility.MemCpy(target + ((emoji.Y + row) * s_Color.Size + emoji.X) * 4, source + ((fromY + emoji.Height - 1 - row) * canvasWidth + fromX) * 4, emoji.Width * 4);
            s_Uploads.Add(new PendingUpload { Set = s_Color, Page = emoji.Page, X = emoji.X, Y = emoji.Y, Width = emoji.Width, Height = emoji.Height });
        }

        /// <summary>Forgets every glyph, keeping one empty page per set. Texts rebuild through <see cref="Cleared"/>.</summary>
        public static void Clear()
        {
            CompleteRaster();
            s_Uploads.Clear();
            s_EmojiPending.Clear();
            s_Glyphs.Clear();
            GlyphsChanged();
            s_Unions.Clear();
            s_Rules.Clear();
            ResetSet(s_Sdf);
            bool hadColor = s_Color.Pixels.Count > 0;
            FreePages(s_Color);
            s_Color.ShelfX = s_Color.ShelfY = s_Color.ShelfHeight = 0;
            if (hadColor) AddPage(s_Color);
            bool hadMsdf = s_Msdf.Pixels.Count > 0;
            FreePages(s_Msdf);
            s_Msdf.ShelfX = s_Msdf.ShelfY = s_Msdf.ShelfHeight = 0;
            if (hadMsdf) AddPage(s_Msdf);
            s_MinDensity.Clear();
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

        /// <summary>
        /// Gives back what only speeds up new glyphs: the upload textures and the outline buffers. With <paramref name="pages"/>, every glyph
        /// and page goes too, for when no text is left to show them; texts that come later place their glyphs again.
        /// </summary>
        internal static void Release(bool pages)
        {
            if (s_Tiles.Count > 0 || s_Uploads.Count > 0 || s_EmojiPending.Count > 0) Flush();
            DestroyTexture(s_StagingSdf);
            DestroyTexture(s_StagingColor);
            DestroyTexture(s_StagingMsdf);
            s_StagingSdf = s_StagingColor = s_StagingMsdf = null;
            if (s_Lines.IsCreated) s_Lines.Dispose();
            if (s_LineColors.IsCreated) s_LineColors.Dispose();
            if (s_Commands.Length > 4096) s_Commands = new float[4096];
            s_EmojiPixels = null;
            if (!pages || (s_Sdf.Pixels.Count == 0 && s_Color.Pixels.Count == 0 && s_Msdf.Pixels.Count == 0)) return;
            s_Glyphs.Clear();
            GlyphsChanged();
            s_Unions.Clear();
            s_Rules.Clear();
            s_MinDensity.Clear();
            foreach (PageSet set in new[] { s_Sdf, s_Color, s_Msdf })
            {
                FreePages(set);
                set.ShelfX = set.ShelfY = set.ShelfHeight = 0;
                DestroyTexture(set.Texture);
                set.Texture = null;
                set.NeedsFullUpload = false;
            }
            s_Rebind = true;
            EnsureBound();
            // Kept texts that come back later rebuild their meshes, as after any clearing.
            s_ClearPending = true;
        }

        /// <summary>Re-creates the textures from the CPU pages, e.g. after the graphics context was lost.</summary>
        public static void RestoreTextures()
        {
            if (s_Sdf.Pixels.Count > 0) RecreateTexture(s_Sdf);
            if (s_Color.Pixels.Count > 0) RecreateTexture(s_Color);
            if (s_Msdf.Pixels.Count > 0) RecreateTexture(s_Msdf);
            EnsureBound();
        }

        /// <summary>Releases everything (domain reload, tests).</summary>
        public static void Shutdown()
        {
            s_Tiles.Clear();
            s_Uploads.Clear();
            s_Glyphs.Clear();
            GlyphsChanged();
            s_Unions.Clear();
            FreePages(s_Sdf);
            FreePages(s_Color);
            FreePages(s_Msdf);
            s_Sdf.ShelfX = s_Sdf.ShelfY = s_Sdf.ShelfHeight = 0;
            s_Color.ShelfX = s_Color.ShelfY = s_Color.ShelfHeight = 0;
            s_Msdf.ShelfX = s_Msdf.ShelfY = s_Msdf.ShelfHeight = 0;
            DestroyTexture(s_Sdf.Texture);
            DestroyTexture(s_Color.Texture);
            DestroyTexture(s_Msdf.Texture);
            s_Msdf.Texture = null;
            DestroyTexture(s_StagingSdf);
            DestroyTexture(s_StagingColor);
            DestroyTexture(s_StagingMsdf);
            DestroyTexture(s_EmptyColor);
            DestroyTexture(s_EmptyMsdf);
            DestroyTexture(s_EmptySdf);
            s_EmptyColor = null;
            s_EmptyMsdf = null;
            s_EmptySdf = null;
            s_MinDensity.Clear();
            s_Rebind = false;
            s_ClearPending = false;
            s_Sdf.Texture = s_Color.Texture = null;
            s_StagingSdf = s_StagingColor = null;
            if (s_Lines.IsCreated) s_Lines.Dispose();
            if (s_LineColors.IsCreated) s_LineColors.Dispose();
        }
    }
}
