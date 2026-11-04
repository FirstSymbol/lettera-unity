using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    internal sealed partial class LetteraText3DEditor
    {
        /// <summary>The looks that ship with the package, wherever it is installed.</summary>
        internal static List<LetteraSolidLook> ShippedLooks()
        {
            var looks = new List<LetteraSolidLook>();
            foreach (string guid in AssetDatabase.FindAssets("t:LetteraSolidLook"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.Contains("/Runtime/Text3D/Looks/")) continue;
                var look = AssetDatabase.LoadAssetAtPath<LetteraSolidLook>(path);
                if (look != null) looks.Add(look);
            }
            looks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return looks;
        }

        void Apply(LetteraSolidLook look)
        {
            foreach (Object selected in targets)
            {
                Undo.RecordObject(selected, "Apply Look");
                look.ApplyTo((LetteraText3D)selected);
                EditorUtility.SetDirty(selected);
            }
            serializedObject.Update();
        }

        VisualElement ShippedLookButtons()
        {
            VisualElement row = LtUI.El("lt-row--pair");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            foreach (LetteraSolidLook look in ShippedLooks())
            {
                LetteraSolidLook chosen = look;
                Label button = LtUI.Clickable(look.name, "lt-button", "Gives the " + look.name + " look to the selected texts: depth, edges, materials" + (look.Layers.Count > 0 ? " and layers." : "."), () => Apply(chosen));
                button.style.marginRight = 4;
                button.style.marginBottom = 4;
                row.Add(button);
            }
            return LtUI.El("", LtUI.Text("Shipped", "lt-subtitle"), row);
        }

        VisualElement SurfaceSetRow(SerializedProperty set, int index)
        {
            SerializedProperty R(string name) => set.FindPropertyRelative(name);
            string title = R("Name").stringValue;
            return new LtFold(string.IsNullOrEmpty(title) ? "Set " + (index + 1) : title, () => LtUI.El("",
                LtUI.Field("Name", R("Name"), "What a tag calls it: <surface=name>. Case does not matter."),
                LtUI.Field("Face", R("Face"), "Empty keeps the text's own."),
                LtUI.Field("Bevel", R("Bevel"), "Empty falls back to this set's Face."),
                LtUI.Field("Side", R("Side"), "Empty falls back to this set's Face."),
                LtUI.Field("Back Bevel", R("BackBevel"), "Empty falls back to this set's Bevel."),
                LtUI.Field("Back", R("Back"), "Empty falls back to this set's Face.")));
        }

        VisualElement SurfaceSetsCard() => LtUI.Card("Ranges",
            LtUI.Text("A range takes a set's materials with <surface=name>, or only some surfaces with <surface=name,face,side>. "
                + "<pose=pitch,yaw,roll,lift,scale> stands it in a fixed pose, and <revolve>, <swing>, <surge>, <orbit> and <tumble> move it in depth. "
                + "Reveals turnin, flyin, deepen and tumblein bring it in through depth.", "lt-subtitle"),
            new LtList("Surface Sets", P("m_SurfaceSets"), SurfaceSetRow, "No sets. Add one, name it, and use its name in <surface=...>."));
    }
}
