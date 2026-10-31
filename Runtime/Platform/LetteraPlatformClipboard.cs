using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    /// <summary>What a clipboard holds, as far as can be told without reading it.</summary>
    public enum LetteraClipPresence { Unknown, Empty, Text, Rich, Image }

    /// <summary>A clipboard that can say what it holds with no read, so no system notice shows.</summary>
    public interface ILetteraClipboardProbe
    {
        LetteraClipPresence Probe();
    }

    /// <summary>Marks a clipboard that cannot be read at once: a paste has to go through ReadAsync.</summary>
    public interface ILetteraClipboardAsyncOnly { }

    /// <summary>One platform's real clipboard. A false return means the system's plain text path takes over.</summary>
    internal interface ILetteraClipboardHost
    {
        bool TryWrite(IReadOnlyList<LetteraClipItem> items);
        bool TryRead(List<LetteraClipItem> items);
        LetteraClipPresence Probe();
    }

    /// <summary>A real clipboard, with the last copy kept here as a fast path. Any failure of the host falls back to plain text, as before there were hosts.</summary>
    internal class LetteraPlatformClipboard : ILetteraClipboardBackend, ILetteraClipboardProbe
    {
        protected readonly ILetteraClipboardHost Host;
        protected readonly LetteraSystemClipboard Fallback = new LetteraSystemClipboard();
        protected readonly List<LetteraClipItem> Shadow = new List<LetteraClipItem>();
        protected string ShadowPlain;

        internal LetteraPlatformClipboard(ILetteraClipboardHost host) => Host = host;

        /// <summary>The clipboard to use here, or null when no host is on.</summary>
        internal static ILetteraClipboardBackend CreateDefault()
        {
            if (!LetteraPlatform.IsActive(LetteraNativeFeature.Clipboard)) return null;
#if UNITY_WEBGL && !UNITY_EDITOR
            return new LetteraWebClipboard();
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new LetteraPlatformClipboard(new LetteraWindowsClipboard());
#else
            return null;
#endif
        }

        public void Write(IReadOnlyList<LetteraClipItem> items)
        {
            Shadow.Clear();
            ShadowPlain = "";
            foreach (LetteraClipItem item in items)
            {
                Shadow.Add(item);
                if (item.Format == LetteraClipFormat.PlainText) ShadowPlain = item.Text;
            }
            if (Attempt(() => Host.TryWrite(items), "write")) return;
            Fallback.Write(items);
        }

        public virtual void Read(List<LetteraClipItem> items)
        {
            int before = items.Count;
            if (!Attempt(() => Host.TryRead(items), "read"))
            {
                items.RemoveRange(before, items.Count - before);
                Fallback.Read(items);
                return;
            }
            PreferShadow(items, before);
        }

        // A copy of ours that a system only kept part of is still ours, as long as its text is.
        protected void PreferShadow(List<LetteraClipItem> items, int from)
        {
            if (ShadowPlain == null) return;
            for (int i = from; i < items.Count; i++)
            {
                if (items[i].Format != LetteraClipFormat.PlainText) continue;
                if (!SameText(items[i].Text, ShadowPlain)) return;
                items.RemoveRange(from, items.Count - from);
                items.AddRange(Shadow);
                return;
            }
        }

        static bool SameText(string a, string b) => a == b || a.Replace("\r\n", "\n") == b.Replace("\r\n", "\n");

        public virtual void ReadAsync(Action<List<LetteraClipItem>> done)
        {
            var items = new List<LetteraClipItem>();
            Read(items);
            done(items);
        }

        public LetteraClipPresence Probe()
        {
            try
            {
                return Host.Probe();
            }
            catch (Exception e)
            {
                LetteraPlatform.Report(LetteraNativeFeature.Clipboard, "Asking what the clipboard holds failed: " + e.Message);
                return LetteraClipPresence.Unknown;
            }
        }

        bool Attempt(Func<bool> call, string what)
        {
            bool done;
            try
            {
                done = call();
            }
            catch (Exception e)
            {
                LetteraPlatform.Report(LetteraNativeFeature.Clipboard, "The clipboard " + what + " failed: " + e.Message);
                return false;
            }
            if (!done) LetteraPlatform.Report(LetteraNativeFeature.Clipboard, "The clipboard " + what + " found it busy, so plain text was used.");
            return done;
        }
    }

    /// <summary>The HTML clipboard format of Windows: a header of byte offsets, then the page. Hostile headers read as nothing, never as a large read.</summary>
    internal static class LetteraCfHtml
    {
        const string StartMark = "<!--StartFragment-->", EndMark = "<!--EndFragment-->";
        const string Template = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

        internal static byte[] Encode(string fragment)
        {
            string open = "<html><body>" + StartMark, close = EndMark + "</body></html>";
            int header = string.Format(CultureInfo.InvariantCulture, Template, 0, 0, 0, 0).Length;
            int openBytes = Encoding.UTF8.GetByteCount(open), fragmentBytes = Encoding.UTF8.GetByteCount(fragment), closeBytes = Encoding.UTF8.GetByteCount(close);
            string head = string.Format(CultureInfo.InvariantCulture, Template, header, header + openBytes + fragmentBytes + closeBytes, header + openBytes, header + openBytes + fragmentBytes);
            return Encoding.UTF8.GetBytes(head + open + fragment + close);
        }

        /// <summary>The fragment, by the header when it makes sense and by the markers otherwise. Null when neither holds anything.</summary>
        internal static string Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            int length = Array.IndexOf(data, (byte)0);
            if (length < 0) length = data.Length;
            int headerEnd = Math.Min(length, 512);
            string header = Encoding.ASCII.GetString(data, 0, headerEnd);
            int start = Offset(header, "StartFragment:"), end = Offset(header, "EndFragment:");
            if (start >= 0 && end >= start && end <= length) return Encoding.UTF8.GetString(data, start, end - start);
            string whole = Encoding.UTF8.GetString(data, 0, length);
            int mark = whole.IndexOf("<!--StartFragment", StringComparison.OrdinalIgnoreCase);
            if (mark >= 0) return whole.Substring(mark);
            int page = whole.IndexOf('<');
            return page >= 0 ? whole.Substring(page) : null;
        }

        static int Offset(string header, string name)
        {
            int at = header.IndexOf(name, StringComparison.Ordinal);
            if (at < 0) return -1;
            at += name.Length;
            int value = 0, digits = 0;
            for (; at < header.Length && digits < 10 && header[at] >= '0' && header[at] <= '9'; at++, digits++)
            {
                long next = value * 10L + (header[at] - '0');
                if (next > int.MaxValue) return -1;
                value = (int)next;
            }
            return digits > 0 ? value : -1;
        }
    }

    /// <summary>Device independent bitmaps, which is how pictures travel on Windows. Sizes are checked before anything is allocated.</summary>
    internal static class LetteraDib
    {
        const int V5Size = 124;

        /// <summary>False for anything that does not hold what its header claims.</summary>
        internal static bool TryRead(byte[] dib, int maxBytes, out int width, out int height, out byte[] rgba)
        {
            width = height = 0;
            rgba = null;
            if (dib == null || dib.Length < 40) return false;
            int headerSize = BitConverter.ToInt32(dib, 0);
            if (headerSize < 40 || headerSize > dib.Length) return false;
            int w = BitConverter.ToInt32(dib, 4), h = BitConverter.ToInt32(dib, 8);
            int bits = BitConverter.ToInt16(dib, 14), compression = BitConverter.ToInt32(dib, 16);
            // No real picture is this large, and past it the sizes below would not fit a number.
            const int MaxSide = 1 << 16;
            if (w <= 0 || w > MaxSide || h == 0 || h < -MaxSide || h > MaxSide || (bits != 24 && bits != 32)) return false;
            // 3 means bit fields, which here are always the plain order or masks after a short header.
            if (compression != 0 && compression != 3 && compression != 6) return false;
            bool topDown = h < 0;
            long rows = Math.Abs((long)h);
            long stride = ((w * (long)bits + 31) / 32) * 4;
            long total = rows * w * 4;
            if (total > maxBytes) return false;
            long pixels = headerSize + (headerSize == 40 && compression == 3 ? 12 : 0);
            if (pixels + rows * stride > dib.Length) return false;
            width = w;
            height = (int)rows;
            rgba = new byte[total];
            int step = bits / 8;
            for (long y = 0; y < rows; y++)
            {
                long source = pixels + (topDown ? y : rows - 1 - y) * stride;
                long target = y * w * 4;
                for (long x = 0; x < w; x++)
                {
                    long s = source + x * step, t = target + x * 4;
                    rgba[t] = dib[s + 2];
                    rgba[t + 1] = dib[s + 1];
                    rgba[t + 2] = dib[s];
                    rgba[t + 3] = bits == 32 ? dib[s + 3] : (byte)255;
                }
            }
            // A 32-bit picture with no alpha in it reads as all clear, which nobody means.
            if (bits == 32)
            {
                bool any = false;
                for (long i = 3; i < total && !any; i += 4) any = rgba[i] != 0;
                if (!any)
                    for (long i = 3; i < total; i += 4)
                        rgba[i] = 255;
            }
            return true;
        }

        /// <summary>A top-down 32-bit picture other programs can read, with the rows given from the top.</summary>
        internal static byte[] WriteV5(int width, int height, byte[] rgba)
        {
            var dib = new byte[V5Size + width * height * 4];
            void Int(int at, int value) => Array.Copy(BitConverter.GetBytes(value), 0, dib, at, 4);
            void UInt(int at, uint value) => Array.Copy(BitConverter.GetBytes(value), 0, dib, at, 4);
            Int(0, V5Size);
            Int(4, width);
            Int(8, -height);
            dib[12] = 1;
            dib[14] = 32;
            Int(16, 3);
            Int(20, width * height * 4);
            UInt(40, 0x00FF0000);
            UInt(44, 0x0000FF00);
            UInt(48, 0x000000FF);
            UInt(52, 0xFF000000);
            UInt(56, 0x73524742); // sRGB
            Int(108, 4); // LCS_GM_IMAGES
            for (int i = 0; i < width * height; i++)
            {
                int s = i * 4, t = V5Size + i * 4;
                dib[t] = rgba[s + 2];
                dib[t + 1] = rgba[s + 1];
                dib[t + 2] = rgba[s];
                dib[t + 3] = rgba[s + 3];
            }
            return dib;
        }

        internal static byte[] ToPng(int width, int height, byte[] rgba)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                // A texture's rows run from the bottom.
                var flipped = new byte[rgba.Length];
                for (int y = 0; y < height; y++) Array.Copy(rgba, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
                texture.LoadRawTextureData(flipped);
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        internal static bool FromPng(byte[] png, out int width, out int height, out byte[] rgba)
        {
            width = height = 0;
            rgba = null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, png)) return false;
                width = texture.width;
                height = texture.height;
                byte[] raw = texture.GetRawTextureData();
                rgba = new byte[width * height * 4];
                for (int y = 0; y < height; y++) Array.Copy(raw, y * width * 4, rgba, (height - 1 - y) * width * 4, width * 4);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
