using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>Compiled only with the FBX Exporter package, which then writes a baked text as FBX.</summary>
    internal static class LetteraText3DFbx
    {
        [InitializeOnLoadMethod]
        static void Register() => LetteraText3DBake.FbxExport = (go, path) => ModelExporter.ExportObject(path, go);
    }
}
