#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FirstSymbol.Lettera
{
    /// <summary>The Windows clipboard with all its formats, through the system's own calls, so it needs no native library of ours.</summary>
    internal sealed class LetteraWindowsClipboard : ILetteraClipboardHost
    {
        const uint CF_UNICODETEXT = 13, CF_DIB = 8, CF_DIBV5 = 17, CF_HDROP = 15, GMEM_MOVEABLE = 2;
        const int OpenTries = 5, OpenWaitMs = 5, MaxReadBytes = 64 * 1024 * 1024;

        [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll", SetLastError = true)] static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError = true)] static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormatW(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("kernel32.dll", SetLastError = true)] static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder name, uint size);

        readonly uint m_Html = RegisterClipboardFormatW("HTML Format");
        readonly uint m_Markdown = RegisterClipboardFormatW(LetteraClipFormat.Markdown);
        readonly uint m_Own = RegisterClipboardFormatW(LetteraClipFormat.Lettera);
        readonly uint m_Png = RegisterClipboardFormatW("PNG");
        readonly uint m_Jpeg = RegisterClipboardFormatW("JFIF");
        readonly uint m_Gif = RegisterClipboardFormatW("GIF");
        IntPtr m_Owner;

        // With no owner a write would empty the clipboard and then fail, so the owner is one of this process's own windows.
        IntPtr Owner()
        {
            if (m_Owner != IntPtr.Zero) return m_Owner;
            uint self = (uint)Process.GetCurrentProcess().Id;
            IntPtr window = IntPtr.Zero;
            while ((window = FindWindowExW(IntPtr.Zero, window, null, null)) != IntPtr.Zero)
            {
                GetWindowThreadProcessId(window, out uint process);
                if (process != self || !IsWindowVisible(window)) continue;
                m_Owner = window;
                break;
            }
            return m_Owner;
        }

        // Another program can hold the clipboard, so this tries a few times, briefly, and then gives up.
        static bool Open(IntPtr owner)
        {
            for (int i = 0; i < OpenTries; i++)
            {
                if (OpenClipboard(owner)) return true;
                if (i + 1 < OpenTries) Thread.Sleep(OpenWaitMs);
            }
            return false;
        }

        public LetteraClipPresence Probe()
        {
            if (IsClipboardFormatAvailable(m_Own) || IsClipboardFormatAvailable(m_Html) || IsClipboardFormatAvailable(m_Markdown)) return LetteraClipPresence.Rich;
            if (IsClipboardFormatAvailable(CF_UNICODETEXT)) return LetteraClipPresence.Text;
            if (IsClipboardFormatAvailable(m_Png) || IsClipboardFormatAvailable(CF_DIB) || IsClipboardFormatAvailable(CF_DIBV5)) return LetteraClipPresence.Image;
            return LetteraClipPresence.Empty;
        }

        public bool TryWrite(IReadOnlyList<LetteraClipItem> items)
        {
            IntPtr owner = Owner();
            if (owner == IntPtr.Zero || !Open(owner)) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                foreach (LetteraClipItem item in items)
                {
                    switch (item.Format)
                    {
                        case LetteraClipFormat.PlainText:
                        case LetteraClipFormat.Url:
                            byte[] text = Encoding.Unicode.GetBytes(ToCrLf(item.Text) + "\0");
                            if (!Put(CF_UNICODETEXT, text)) return false;
                            break;
                        case LetteraClipFormat.Html:
                            if (!Put(m_Html, Terminated(LetteraCfHtml.Encode(item.Text)))) return false;
                            break;
                        case LetteraClipFormat.Markdown:
                            if (!Put(m_Markdown, Terminated(item.Bytes))) return false;
                            break;
                        case LetteraClipFormat.Lettera:
                            if (!Put(m_Own, Terminated(item.Bytes))) return false;
                            break;
                        case LetteraClipFormat.Png:
                            if (!Put(m_Png, item.Bytes)) return false;
                            // So that programs which do not know PNG on the clipboard can read it too.
                            if (LetteraDib.FromPng(item.Bytes, out int width, out int height, out byte[] rgba) && !Put(CF_DIBV5, LetteraDib.WriteV5(width, height, rgba))) return false;
                            break;
                    }
                }
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        public bool TryRead(List<LetteraClipItem> items)
        {
            if (!Open(Owner())) return false;
            try
            {
                byte[] data;
                if ((data = Take(m_Own)) != null) items.Add(new LetteraClipItem(LetteraClipFormat.Lettera, Unterminated(data)));
                if ((data = Take(m_Html)) != null)
                {
                    string fragment = LetteraCfHtml.Decode(data);
                    if (fragment != null) items.Add(new LetteraClipItem(LetteraClipFormat.Html, fragment));
                }
                if ((data = Take(m_Markdown)) != null) items.Add(new LetteraClipItem(LetteraClipFormat.Markdown, Unterminated(data)));
                if ((data = Take(CF_UNICODETEXT)) != null) items.Add(new LetteraClipItem(LetteraClipFormat.PlainText, FromCrLf(Utf16(data))));
                int maxImage = LetteraSettings.Instance.Platform.PasteImageMaxBytes;
                if ((data = Take(m_Png)) != null && data.Length <= maxImage) items.Add(new LetteraClipItem(LetteraClipFormat.Png, data));
                else if ((data = Take(CF_DIBV5) ?? Take(CF_DIB)) != null && LetteraDib.TryRead(data, maxImage, out int width, out int height, out byte[] rgba)) items.Add(new LetteraClipItem(LetteraClipFormat.Png, LetteraDib.ToPng(width, height, rgba)));
                if ((data = Take(m_Jpeg)) != null && data.Length <= maxImage) items.Add(new LetteraClipItem(LetteraClipFormat.Jpeg, data));
                if ((data = Take(m_Gif)) != null && data.Length <= maxImage) items.Add(new LetteraClipItem(LetteraClipFormat.Gif, data));
                string files = DroppedFiles();
                if (files != null) items.Add(new LetteraClipItem(LetteraClipFormat.Files, files));
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }

        static string DroppedFiles()
        {
            if (!IsClipboardFormatAvailable(CF_HDROP)) return null;
            IntPtr drop = GetClipboardData(CF_HDROP);
            if (drop == IntPtr.Zero) return null;
            uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            var builder = new StringBuilder();
            for (uint i = 0; i < count && i < 1024; i++)
            {
                uint length = DragQueryFileW(drop, i, null, 0);
                if (length == 0 || length > 32768) continue;
                var name = new StringBuilder((int)length + 1);
                DragQueryFileW(drop, i, name, length + 1);
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(name);
            }
            return builder.Length > 0 ? builder.ToString() : null;
        }

        static bool Put(uint format, byte[] data)
        {
            IntPtr memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
            if (memory == IntPtr.Zero) return false;
            IntPtr locked = GlobalLock(memory);
            if (locked == IntPtr.Zero)
            {
                GlobalFree(memory);
                return false;
            }
            Marshal.Copy(data, 0, locked, data.Length);
            GlobalUnlock(memory);
            // From here on the system owns it, unless this fails.
            if (SetClipboardData(format, memory) != IntPtr.Zero) return true;
            GlobalFree(memory);
            return false;
        }

        static byte[] Take(uint format)
        {
            if (format == 0 || !IsClipboardFormatAvailable(format)) return null;
            IntPtr memory = GetClipboardData(format);
            if (memory == IntPtr.Zero) return null;
            ulong size = GlobalSize(memory).ToUInt64();
            if (size == 0 || size > MaxReadBytes) return null;
            IntPtr locked = GlobalLock(memory);
            if (locked == IntPtr.Zero) return null;
            try
            {
                var data = new byte[(int)size];
                Marshal.Copy(locked, data, 0, data.Length);
                return data;
            }
            finally
            {
                GlobalUnlock(memory);
            }
        }

        static byte[] Terminated(byte[] data)
        {
            var copy = new byte[data.Length + 1];
            Array.Copy(data, copy, data.Length);
            return copy;
        }

        static byte[] Unterminated(byte[] data)
        {
            int length = Array.IndexOf(data, (byte)0);
            if (length < 0) return data;
            var copy = new byte[length];
            Array.Copy(data, copy, length);
            return copy;
        }

        // A size from the system can run past the terminator.
        static string Utf16(byte[] data)
        {
            string text = Encoding.Unicode.GetString(data, 0, data.Length & ~1);
            int end = text.IndexOf('\0');
            return end >= 0 ? text.Substring(0, end) : text;
        }

        static string ToCrLf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        static string FromCrLf(string text) => text.Replace("\r\n", "\n");
    }
}
#endif
