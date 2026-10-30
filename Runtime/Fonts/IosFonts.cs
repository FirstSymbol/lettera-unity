#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;

namespace FirstSymbol.Lettera
{
    /// <summary>Asks CoreText which font files iOS uses (Plugins/iOS/LetteraFontsIOS.mm).</summary>
    internal static class IosFonts
    {
        [DllImport("__Internal")] static extern int lt_ios_default_path(byte[] output, int capacity);
        [DllImport("__Internal")] static extern int lt_ios_named_path(string name, byte[] output, int capacity);
        [DllImport("__Internal")] static extern int lt_ios_fallback_path(int codepoint, string language, byte[] output, int capacity);

        static readonly byte[] s_Buffer = new byte[2048];

        static string Read(int length) => length > 0 ? Encoding.UTF8.GetString(s_Buffer, 0, length) : null;

        public static string DefaultPath() => Read(lt_ios_default_path(s_Buffer, s_Buffer.Length));
        public static string NamedPath(string name) => Read(lt_ios_named_path(name, s_Buffer, s_Buffer.Length));
        public static string FallbackPath(int codepoint, string language) => Read(lt_ios_fallback_path(codepoint, language, s_Buffer, s_Buffer.Length));
    }
}
#endif
