using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    [CustomEditor(typeof(LetteraGlyphAttachments)), CanEditMultipleObjects]
    internal sealed class LetteraGlyphAttachmentsEditor : UnityEditor.Editor
    {
        static readonly string[] PlaceNames = { "Top", "Bottom", "Front", "Strokes", "Scatter" };

        static VisualElement Row(SerializedProperty rule, int index)
        {
            SerializedProperty R(string name) => rule.FindPropertyRelative(name);
            Object prefab = R("Prefab").objectReferenceValue;
            VisualElement strokes = LtUI.Field("Spacing", R("Spacing"), "Room between copies along the outline, in em.");
            VisualElement scatter = LtUI.El("",
                LtUI.Field("Density", R("Density"), "Copies per square em of surface facing up."),
                LtUI.Field("Max Slope", R("MaxSlope"), "How far from straight up a surface may face, in degrees."));
            void Show(SerializedProperty place)
            {
                strokes.style.display = place.enumValueIndex == (int)LetteraGlyphAttachPlace.Strokes ? DisplayStyle.Flex : DisplayStyle.None;
                scatter.style.display = place.enumValueIndex == (int)LetteraGlyphAttachPlace.Scatter ? DisplayStyle.Flex : DisplayStyle.None;
            }
            VisualElement body = LtUI.El("",
                LtUI.Row("On", LetteraTextEditorBase.Holder(LtUI.Switch(R("Enabled")))),
                LtUI.Field("Prefab", R("Prefab"), "Copied onto the glyphs. At scale one it is one em."),
                LtUI.Row("Where", LtUI.Segments(R("Place"), PlaceNames), "On the highest point, the lowest, the front face, along the outline, or scattered over what faces up."),
                LtUI.Field("Characters", R("Characters"), "Only these characters get copies. Empty: every glyph."),
                strokes,
                scatter,
                LtUI.Field("Max Per Glyph", R("MaxPerGlyph")),
                LtUI.Field("Size", R("Size"), "In em."),
                LtUI.Field("Lift", R("Lift"), "How far out of the surface, in em. Negative sinks it in."),
                LtUI.Field("Scale Variation", R("ScaleVariation"), "Each copy's size varies by up to this share, either way."),
                LtUI.Field("Spin", R("Spin"), "Degrees each copy is turned about its up."),
                LtUI.Field("Spin Variation", R("SpinVariation"), "Each copy's turn varies by up to this, either way."),
                LtUI.Field("Seed", R("Seed"), "Another seed gives other sizes, turns and scattered places."),
                LtUI.Row("Align To Surface", LetteraTextEditorBase.Holder(LtUI.Switch(R("AlignToSurface"))), "On: up is the surface's normal. Off: the way the place faces, turned with the glyph."),
                LtUI.Row("Scale With Glyph", LetteraTextEditorBase.Holder(LtUI.Switch(R("ScaleWithGlyph"))), "A copy grows and shrinks with its glyph's motion."));
            Show(R("Place"));
            body.TrackPropertyValue(R("Place"), Show);
            return new LtFold(prefab != null ? prefab.name : "Copies " + (index + 1), () => body, true);
        }

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Text 3D Attachments", "Copies of prefabs that ride on the glyphs");
            Label count = LtUI.Text("", "lt-subtitle");
            page.Add(LtUI.Card("Copies",
                LtUI.Text("Each sits on its glyph's surface and follows it through motion and deformers. A glyph faded to nothing hides its copies.", "lt-subtitle"),
                new LtList("Attachments", serializedObject.FindProperty("m_Attachments"), Row, "No copies. Add a prefab to put on the glyphs."),
                count));
            page.schedule.Execute(() =>
            {
                var attachments = target as LetteraGlyphAttachments;
                if (attachments != null) count.text = attachments.CopyCount + " copies stand now.";
            }).Every(500);
            root.Bind(serializedObject);
            return root;
        }
    }

#if LETTERA_PHYSICS
    [CustomEditor(typeof(LetteraText3DColliders)), CanEditMultipleObjects]
    internal sealed class LetteraText3DCollidersEditor : UnityEditor.Editor
    {
        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Text 3D Colliders", "Colliders that keep matching the drawn text");
            Label count = LtUI.Text("", "lt-subtitle");
            Label warning = LtUI.Text("A Mesh cannot be on a moving Rigidbody. Make the body kinematic, or use Boxes or Hulls.", "lt-subtitle");
            warning.style.color = new Color(1f, 0.75f, 0.35f);
            warning.style.whiteSpace = WhiteSpace.Normal;
            page.Add(LtUI.Card("Shape",
                LtUI.Row("Shape", LtUI.Segments(P("m_Shape"), "Boxes", "Hulls", "Mesh"), "A box per glyph, a convex hull per glyph, or the drawn mesh itself."),
                LtUI.Row("Layers", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_IncludeLayers"))), "Layers around the glyphs collide too."),
                LtUI.Row("Follow Motion", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_FollowMotion"))), "Follow motion and moving deformers every frame. A Mesh is cooked again each frame it moves."),
                LtUI.Row("Is Trigger", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_IsTrigger"))), "Boxes and hulls only."),
                LtUI.Field("Material", P("m_Material")),
                warning,
                count));
            page.Add(LtUI.Text("A Rigidbody on this object takes every glyph's collider as one compound body.", "lt-subtitle"));
            void Refresh()
            {
                var colliders = target as LetteraText3DColliders;
                if (colliders == null) return;
                count.text = colliders.ColliderCount + " colliders.";
                Rigidbody body = colliders.GetComponentInParent<Rigidbody>();
                warning.style.display = colliders.Shape == LetteraSolidColliderShape.Mesh && body != null && !body.isKinematic ? DisplayStyle.Flex : DisplayStyle.None;
            }
            page.schedule.Execute(Refresh).Every(500);
            root.Bind(serializedObject);
            return root;
        }
    }
#endif
}
