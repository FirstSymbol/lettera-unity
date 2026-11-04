using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Lettera.Editor
{
    /// <summary>A bevel's shape, drawn: what the edge of a letter looks like cut through.</summary>
    internal sealed class LetteraBevelPreview : VisualElement
    {
        readonly SerializedProperty m_Bevel;

        public LetteraBevelPreview(SerializedProperty bevel)
        {
            m_Bevel = bevel;
            style.height = 72;
            style.marginTop = 4;
            style.marginBottom = 4;
            generateVisualContent += Draw;
            this.TrackPropertyValue(bevel, _ => MarkDirtyRepaint());
        }

        void Draw(MeshGenerationContext context)
        {
            if (m_Bevel == null || m_Bevel.serializedObject == null || m_Bevel.serializedObject.targetObject == null) return;
            var shape = (LetteraBevelShape)m_Bevel.FindPropertyRelative("Shape").enumValueIndex;
            var points = new NativeList<float2>(32, Allocator.Temp);
            LetteraBevelProfile.Fill(shape, m_Bevel.FindPropertyRelative("Roundness").floatValue, m_Bevel.FindPropertyRelative("Segments").intValue, new AnimationCurveSampler(m_Bevel.FindPropertyRelative("Curve").animationCurveValue), ref points);
            Rect box = contentRect;
            float size = Mathf.Min(box.height, box.width) - 12;
            Vector2 origin = new Vector2(box.center.x - size / 2, box.center.y + size / 2);
            Painter2D painter = context.painter2D;
            painter.strokeColor = new Color(1, 1, 1, 0.12f);
            painter.lineWidth = 1;
            painter.BeginPath();
            painter.MoveTo(origin);
            painter.LineTo(origin + new Vector2(size, 0));
            painter.LineTo(origin + new Vector2(size, -size));
            painter.Stroke();
            painter.strokeColor = new Color(0.45f, 0.7f, 1f, 1);
            painter.lineWidth = 2;
            painter.BeginPath();
            if (points.Length < 2)
            {
                painter.MoveTo(origin + new Vector2(0, -size));
                painter.LineTo(origin + new Vector2(size, -size));
            }
            else
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 p = origin + new Vector2(points[i].x * size, -points[i].y * size);
                    if (i == 0) painter.MoveTo(p);
                    else painter.LineTo(p);
                }
            }
            painter.Stroke();
        }
    }

    [CustomEditor(typeof(LetteraText3D)), CanEditMultipleObjects]
    internal sealed class LetteraText3DEditor : UnityEditor.Editor
    {
        void OnSceneGUI()
        {
            var text = (LetteraText3D)target;
            LetteraSceneHandles.Draw(text, text.Padding, text.Styles, text.FontSize);
            DepthGizmo(text);
        }

        // How deep the text stands: a box around it and a handle on its back. The depth is in em, and the box in the text's own space.
        static void DepthGizmo(LetteraText3D text)
        {
            var rect = text.transform as RectTransform;
            if (!LetteraSceneOptions.Outlines || rect == null) return;
            Rect box = rect.rect;
            float size = text.Engine != null && text.Engine.CurrentFontSize > 0 ? text.Engine.CurrentFontSize : text.FontSize;
            float depth = text.Depth * size;
            Matrix4x4 saved = Handles.matrix;
            Handles.matrix = rect.localToWorldMatrix;
            Handles.color = LetteraSceneHandles.InkColor;
            Handles.DrawWireCube(new Vector3(box.center.x, box.center.y, depth * 0.5f), new Vector3(box.width, box.height, depth));
            EditorGUI.BeginChangeCheck();
            Vector3 back = new Vector3(box.center.x, box.center.y, depth);
            Vector3 moved = Handles.Slider(back, Vector3.forward, HandleUtility.GetHandleSize(back) * 0.6f, Handles.ArrowHandleCap, 0);
            if (EditorGUI.EndChangeCheck() && size > 0)
            {
                Undo.RecordObject(text, "Change Depth");
                text.Depth = Mathf.Max(moved.z, 0) / size;
                EditorUtility.SetDirty(text);
            }
            Handles.matrix = saved;
        }

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        VisualElement BevelFields(SerializedProperty bevel) => LtUI.El("",
            new LetteraBevelPreview(bevel),
            LtUI.Field("Shape", bevel.FindPropertyRelative("Shape")),
            LtUI.Field("Width", bevel.FindPropertyRelative("Width"), "How far it runs inward from the outline, in em."),
            LtUI.Field("Height", bevel.FindPropertyRelative("Height"), "How far it recedes from the face plane, in em."),
            LtUI.Field("Roundness", bevel.FindPropertyRelative("Roundness"), "Round only: 0 hollow, 0.5 a quarter circle, 1 nearly square."),
            LtUI.Field("Segments", bevel.FindPropertyRelative("Segments")),
            LtUI.Field("Curve", bevel.FindPropertyRelative("Curve"), "Custom only: height from the side to the face."));

        static readonly string[] ShapeNames = { "Outline", "Plate", "Tube" };
        static readonly string[] ScopeNames = { "Glyph", "Word", "Line", "Text" };
        static readonly string[] CutNames = { "None", "Through", "Engraved" };
        static readonly string[] KindNames = { "Bend", "Twist", "Taper", "Wave", "Sphere", "Path" };

        VisualElement LayerRow(SerializedProperty layer, int index)
        {
            SerializedProperty R(string name) => layer.FindPropertyRelative(name);
            string title = R("Name").stringValue;
            return new LtFold(string.IsNullOrEmpty(title) ? "Layer " + (index + 1) : title, () => LtUI.El("",
                LtUI.Row("On", LetteraTextEditorBase.Holder(LtUI.Switch(R("Enabled")))),
                LtUI.Field("Name", R("Name")),
                LtUI.Row("Shape", LtUI.Segments(R("Shape"), ShapeNames), "The glyphs' outline grown or shrunk, a rounded plate around the ink, or a round tube along each outline."),
                LtUI.Row("Around", LtUI.Segments(R("Scope"), ScopeNames), "One per glyph, word, line, or one for the whole text."),
                LtUI.Field("Offset", R("Offset"), "Outline: how far it grows, in em, or shrinks when negative. Plate: room around the ink. Tube: how far inside the outline it runs."),
                LtUI.Field("Radius", R("Radius"), "Plate: corners. Tube: the tube. In em."),
                LtUI.Field("Band", R("Band"), "Above zero, only a ring this wide along its edge, in em."),
                LtUI.Row("Letters", LtUI.Segments(R("Cut"), CutNames), "The glyphs cut right through it, or sunk into its front."),
                LtUI.Field("Cut Depth", R("CutDepth"), "Engraved: how deep the glyphs go, in em."),
                LtUI.Field("Depth", R("Depth"), "Its own depth, in em. A tube is as deep as it is wide."),
                LtUI.Row("From", LtUI.Segments(R("Anchor"), "Front", "Back"), "Which face of the text its place in depth is measured from."),
                LtUI.Field("Z", R("Z"), "How far behind that face its front sits, in em. Negative stands in front."),
                LtUI.Field("Shift", R("Shift"), "Moves it across the text's plane, in em."),
                LtUI.Text("Edge", "lt-subtitle"),
                BevelFields(R("Bevel")),
                LtUI.Field("Color", R("Color")),
                LtUI.Field("Material", R("Material"), "Its front, edges and back. Empty takes the text's."),
                LtUI.Field("Side Material", R("SideMaterial"), "Its sides. Empty takes its Material, then the text's."),
                LtUI.Field("Detail", R("Detail"), "Cells per em of the field it is traced from: more is finer and slower to build.")));
        }

        VisualElement DeformerRow(SerializedProperty deformer, int index)
        {
            SerializedProperty R(string name) => deformer.FindPropertyRelative(name);
            var kind = (LetteraDeformKind)R("Kind").enumValueIndex;
            SerializedProperty points = R("Points");
            return new LtFold(kind + (R("Off").boolValue ? " (off)" : ""), () => LtUI.El("",
                LtUI.Row("Kind", LtUI.Segments(R("Kind"), KindNames)),
                LtUI.Row("Off", LetteraTextEditorBase.Holder(LtUI.Switch(R("Off"))), "Kept in the chain, doing nothing."),
                LtUI.Field("Amount", R("Amount"), "Bend, twist, sphere: degrees over the text's width; 360 closes a bend in a ring. Taper: how much narrower the end is. Wave: height in em."),
                LtUI.Row("Moves", LtUI.Segments(R("Axis"), "Depth", "Up", "Both"), "Which way it moves the text: a bend into depth or in its plane, a wave and a taper likewise."),
                LtUI.Field("Center", R("Center"), "Bend, twist, sphere: where along the width it holds still."),
                LtUI.Field("Wavelength", R("Wavelength"), "Wave: one wave's length, in em."),
                LtUI.Field("Phase", R("Phase"), "Wave: degrees. Path: where the text starts along it, 0 to 1."),
                LtUI.Field("Speed", R("Speed"), "Wave: degrees a second, in Play Mode."),
                new LtList("Path Points", points, (point, i) => LtUI.Field("", point), "Path only: points in the text's own space.")));
        }

        void AddLayer(LetteraSolidLayerPreset preset)
        {
            foreach (Object selected in targets)
            {
                Undo.RecordObject(selected, "Add Layer");
                ((LetteraText3D)selected).AddLayer(LetteraSolidLayer.Preset(preset));
                EditorUtility.SetDirty(selected);
            }
            serializedObject.Update();
        }

        void AddDeformer(LetteraDeformKind kind)
        {
            foreach (Object selected in targets)
            {
                Undo.RecordObject(selected, "Add Deformer");
                LetteraDeformer deformer;
                switch (kind)
                {
                    case LetteraDeformKind.Bend: deformer = LetteraDeformer.Bend(90); break;
                    case LetteraDeformKind.Twist: deformer = LetteraDeformer.Twist(90); break;
                    case LetteraDeformKind.Taper: deformer = LetteraDeformer.Taper(0.5f); break;
                    case LetteraDeformKind.Wave: deformer = LetteraDeformer.Wave(0.1f, 2); break;
                    case LetteraDeformKind.Sphere: deformer = LetteraDeformer.Sphere(90); break;
                    default:
                        var rect = ((LetteraText3D)selected).transform as RectTransform;
                        float w = rect != null ? rect.rect.width : 10;
                        deformer = LetteraDeformer.Path(new Vector3(-w / 2, 0, 0), new Vector3(0, w / 4, w / 4), new Vector3(w / 2, 0, 0));
                        break;
                }
                ((LetteraText3D)selected).AddDeformer(deformer);
                EditorUtility.SetDirty(selected);
            }
            serializedObject.Update();
        }

        VisualElement MaterialRow(string label, string property, LetteraSolidSurface surface, string fallsBackTo)
        {
            VisualElement holder = LtUI.El("");
            holder.Add(LtUI.Field(label, P(property), "Empty falls back to " + fallsBackTo + "."));
            Label resolved = LtUI.Text("", "lt-subtitle");
            holder.Add(resolved);
            void Refresh()
            {
                var text = (LetteraText3D)target;
                bool empty = P(property).objectReferenceValue == null;
                Material material = text.GetSurfaceMaterial(surface);
                resolved.text = empty ? "Uses " + (material != null ? material.name : "nothing") + ", through " + fallsBackTo : "";
                resolved.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            }
            holder.schedule.Execute(Refresh).Every(500);
            Refresh();
            return holder;
        }

        VisualElement Chips()
        {
            VisualElement holder = LtUI.El("");
            var text = (LetteraText3D)target;
            var labels = new List<Label>();
            Label Chip(string message)
            {
                Label label = LtUI.Text(message, "lt-subtitle");
                label.style.color = new Color(1f, 0.75f, 0.35f);
                label.style.whiteSpace = WhiteSpace.Normal;
                holder.Add(label);
                labels.Add(label);
                return label;
            }
            Label layers = Chip("This text has 2D layers (stroke, shadow, glow or fill), which do not draw on a solid.");
            Label selectable = Chip("A selectable does nothing on a solid text.");
            Label overlay = Chip("Under a Screen Space Overlay canvas this does not show. Use a Camera canvas or a render texture.");
            Label canvas = Chip("Under a canvas this draws as a scene object: no masks, no batching, no layout.");
            Label instancing = Chip("Many texts share this mesh, but its material has GPU Instancing off.");
            Label over = Chip("Over the most vertices one text draws. Glyphs past it draw nothing.");
            Label failed = Chip("");
            void Refresh()
            {
                if (text == null) return;
                layers.style.display = text.Engine.HasLayersForChip ? DisplayStyle.Flex : DisplayStyle.None;
                selectable.style.display = text.GetComponent<LetteraSelectable>() != null ? DisplayStyle.Flex : DisplayStyle.None;
                Canvas parent = text.GetComponentInParent<Canvas>();
                overlay.style.display = parent != null && parent.renderMode == RenderMode.ScreenSpaceOverlay ? DisplayStyle.Flex : DisplayStyle.None;
                canvas.style.display = parent != null && parent.renderMode != RenderMode.ScreenSpaceOverlay ? DisplayStyle.Flex : DisplayStyle.None;
                Material face = text.GetSurfaceMaterial(LetteraSolidSurface.Face);
                instancing.style.display = text.ShareMesh && LetteraSharedSolidMeshes.ReferencesOf(text.CurrentMesh) > 16 && face != null && !face.enableInstancing ? DisplayStyle.Flex : DisplayStyle.None;
                LetteraSolidMeshBuilder builder = text.Builder;
                over.style.display = builder != null && builder.OverLimit ? DisplayStyle.Flex : DisplayStyle.None;
                failed.text = builder != null && builder.FailedGlyphs > 0 ? builder.FailedGlyphs + " glyphs could not be built." : "";
                failed.style.display = failed.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            holder.schedule.Execute(Refresh).Every(500);
            Refresh();
            return holder;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var text = (LetteraText3D)target;
            return new LetteraTextInspector(serializedObject, "Lettera Text 3D",
                () => LetteraTextEditorBase.StatsOf(text.GlyphCount, text.Lines.Count, text.CurrentFontSize),
                () => LetteraTextEditorBase.OverrideOf(text.TextOverrideReason),
                page =>
                {
                    page.Add(Chips());

                    Label count = LtUI.Text("", "lt-subtitle");
                    page.Add(LtUI.Card("Solid",
                        LtUI.Field("Depth", P("m_Depth"), "How deep the text is, in em. Changing it builds no glyph."),
                        LtUI.Row("Pivot", LtUI.Segments(P("m_Pivot"), "Middle", "Front", "Back"), "Which plane lies on the transform."),
                        LtUI.Row("Front Face", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_FrontFace")))),
                        LtUI.Row("Back Face", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_BackFace")))),
                        LtUI.Row("Sides", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_Sides")))),
                        LtUI.Field("Smoothing Angle", P("m_SmoothingAngle"), "Corners within this angle shade smooth."),
                        LtUI.Field("Curve Tolerance", P("m_CurveTolerance"), "How far a curve may stray from its true line, in em. Less means more vertices."),
                        count));

                    VisualElement backFields = BevelFields(P("m_BackBevel"));
                    page.Add(LtUI.Card("Edges",
                        LtUI.Text("Front", "lt-subtitle"),
                        BevelFields(P("m_FrontBevel")),
                        LtUI.Row("Mirror", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_MirrorBevel"))), "The back takes the front's edge."),
                        backFields));
                    void Mirror(SerializedProperty property) => backFields.style.display = property.boolValue ? DisplayStyle.None : DisplayStyle.Flex;
                    Mirror(P("m_MirrorBevel"));
                    backFields.TrackPropertyValue(P("m_MirrorBevel"), Mirror);

                    page.Add(LtUI.Card("Materials",
                        MaterialRow("Face", "m_FaceMaterial", LetteraSolidSurface.Face, "the default material"),
                        MaterialRow("Bevel", "m_BevelMaterial", LetteraSolidSurface.Bevel, "Face"),
                        MaterialRow("Side", "m_SideMaterial", LetteraSolidSurface.Side, "Face"),
                        MaterialRow("Back Bevel", "m_BackBevelMaterial", LetteraSolidSurface.BackBevel, "Bevel"),
                        MaterialRow("Back", "m_BackMaterial", LetteraSolidSurface.Back, "Face")));

                    page.Add(LtUI.Card("Layers",
                        LtUI.Text("Extra solids around the glyphs, each with its own depth, edge, colour and materials.", "lt-subtitle"),
                        new LtList("Layers", P("m_Layers"), LayerRow, "No layers. Add an outline, a plate or a tube around the glyphs.", list =>
                        {
                            var menu = new GenericMenu();
                            foreach (LetteraSolidLayerPreset preset in System.Enum.GetValues(typeof(LetteraSolidLayerPreset)))
                            {
                                LetteraSolidLayerPreset chosen = preset;
                                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(preset.ToString())), false, () =>
                                {
                                    AddLayer(chosen);
                                    list.Rebuild();
                                });
                            }
                            menu.ShowAsContext();
                        })));

                    page.Add(LtUI.Card("Deform",
                        LtUI.Text("A chain over the whole text, its layers and lines. A range takes its own by tags: bend, twist, taper, ripple, sphere.", "lt-subtitle"),
                        new LtList("Deformers", P("m_Deformers"), DeformerRow, "Nothing deforms this text.", list =>
                        {
                            var menu = new GenericMenu();
                            foreach (LetteraDeformKind kind in System.Enum.GetValues(typeof(LetteraDeformKind)))
                            {
                                LetteraDeformKind chosen = kind;
                                menu.AddItem(new GUIContent(kind.ToString()), false, () =>
                                {
                                    AddDeformer(chosen);
                                    list.Rebuild();
                                });
                            }
                            menu.ShowAsContext();
                        }),
                        LtUI.Field("Detail", P("m_DeformDetail"), "With a deformer, no edge across the text is longer than this, in em. Less is smoother and heavier.")));

                    var look = new UnityEditor.UIElements.ObjectField { objectType = typeof(LetteraSolidLook), allowSceneObjects = false };
                    look.AddToClassList("lt-field");
                    page.Add(LtUI.Card("Look",
                        LtUI.Row("Look", look),
                        LtUI.Clickable("Apply Look", "lt-button", "Gives this look's depth, edges and materials to the selected texts.", () =>
                        {
                            if (!(look.value is LetteraSolidLook chosen)) return;
                            foreach (Object selected in targets)
                            {
                                Undo.RecordObject(selected, "Apply Look");
                                chosen.ApplyTo((LetteraText3D)selected);
                                EditorUtility.SetDirty(selected);
                            }
                        })));

                    Label live = LtUI.Text("", "lt-subtitle");
                    page.Add(LtUI.Card("Cache And Build",
                        LtUI.Field("Warm Characters", P("m_WarmCharacters"), "Built with the text and kept, so that a counter's digits never build in a frame that shows."),
                        LtUI.Row("Share Mesh", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_ShareMesh"))), "Identical texts draw one mesh, which is what lets them instance."),
                        LtUI.Row("Color Glyphs", LtUI.Segments(P("m_ColorGlyphs"), "Flat", "Hide"), "A color glyph has no outline to build from: it draws flat on the front plane, or not at all."),
                        LtUI.Row("Raycast Target", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_RaycastTarget")))),
                        live));
                    void Stats()
                    {
                        LetteraSolidMeshBuilder builder = text != null ? text.Builder : null;
                        if (builder == null) return;
                        int submeshes = text.CurrentMesh != null ? text.CurrentMesh.subMeshCount : 0;
                        count.text = $"{builder.VertexCount:N0} vertices";
                        live.text = $"{builder.VertexCount:N0} vertices, {builder.TriangleCount:N0} triangles, {submeshes} submeshes. Solids in the cache: {LetteraSolidCache.Count}, {LetteraSolidCache.Bytes / (1024f * 1024f):0.0} MB. Failed: {LetteraSolidCache.Failures}. Clamped edges: {LetteraSolidCache.BevelsClamped}.";
                    }
                    page.schedule.Execute(Stats).Every(500);
                }).Build();
        }
    }

    /// <summary>A look's cards are the same as a text's.</summary>
    [CustomEditor(typeof(LetteraSolidLook)), CanEditMultipleObjects]
    internal sealed class LetteraSolidLookEditor : UnityEditor.Editor
    {
        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        VisualElement Bevel(SerializedProperty bevel) => LtUI.El("",
            new LetteraBevelPreview(bevel),
            LtUI.Field("Shape", bevel.FindPropertyRelative("Shape")),
            LtUI.Field("Width", bevel.FindPropertyRelative("Width")),
            LtUI.Field("Height", bevel.FindPropertyRelative("Height")),
            LtUI.Field("Roundness", bevel.FindPropertyRelative("Roundness")),
            LtUI.Field("Segments", bevel.FindPropertyRelative("Segments")),
            LtUI.Field("Curve", bevel.FindPropertyRelative("Curve")));

        public override VisualElement CreateInspectorGUI()
        {
            VisualElement root = LtUI.Root();
            VisualElement page = LtFrame.Header(root, "Solid Look", "Depth, edges and materials to give a solid text at once");
            page.Add(LtUI.Card("Solid", LtUI.Field("Depth", P("m_Depth"))));
            page.Add(LtUI.Card("Edges",
                LtUI.Text("Front", "lt-subtitle"),
                Bevel(P("m_FrontBevel")),
                LtUI.Row("Mirror", LetteraTextEditorBase.Holder(LtUI.Switch(P("m_MirrorBevel")))),
                LtUI.Text("Back", "lt-subtitle"),
                Bevel(P("m_BackBevel"))));
            page.Add(LtUI.Card("Materials",
                LtUI.Field("Face", P("m_FaceMaterial")),
                LtUI.Field("Bevel", P("m_BevelMaterial")),
                LtUI.Field("Side", P("m_SideMaterial")),
                LtUI.Field("Back Bevel", P("m_BackBevelMaterial")),
                LtUI.Field("Back", P("m_BackMaterial"))));
            page.Add(LtUI.Card("Layers",
                LtUI.Text("A text takes copies of these with the look. None leaves a text's own as they are.", "lt-subtitle"),
                new LtList("Layers", P("m_Layers"), (layer, index) => LtUI.El("",
                    LtUI.Field("Name", layer.FindPropertyRelative("Name")),
                    LtUI.Row("Shape", LtUI.Segments(layer.FindPropertyRelative("Shape"), "Outline", "Plate", "Tube")),
                    LtUI.Row("Around", LtUI.Segments(layer.FindPropertyRelative("Scope"), "Glyph", "Word", "Line", "Text")),
                    LtUI.Field("Offset", layer.FindPropertyRelative("Offset")),
                    LtUI.Field("Radius", layer.FindPropertyRelative("Radius")),
                    LtUI.Field("Depth", layer.FindPropertyRelative("Depth")),
                    LtUI.Field("Z", layer.FindPropertyRelative("Z")),
                    LtUI.Field("Color", layer.FindPropertyRelative("Color")),
                    LtUI.Field("Material", layer.FindPropertyRelative("Material"))), "No layers.")));
            root.Bind(serializedObject);
            return root;
        }
    }

    /// <summary>
    /// A ShaderGUI cannot build UI Toolkit in this Unity, so a material's inspector is one row that opens its card here,
    /// and the default look never appears.
    /// </summary>
    internal sealed class LetteraSolidMaterialGui : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (GUILayout.Button("Edit In Lettera")) LetteraSolidMaterialWindow.Open((Material)materialEditor.target);
        }
    }

    /// <summary>Creating solid text, and saving one out of the text system.</summary>
    internal sealed class LetteraText3DPalette : ILetteraPaletteSource
    {
        public IEnumerable<LetteraCommand> Commands()
        {
            yield return new LetteraCommand { Name = "Text 3D", Group = "Create", Words = "solid mesh extrude bevel", MenuPath = "GameObject/Lettera/Text 3D", Source = LetteraPaletteSource.Scene, Run = () => EditorApplication.ExecuteMenuItem("GameObject/Lettera/Text 3D") };
            yield return new LetteraCommand { Name = "Solid Look", Group = "Assets", Words = "create material 3d", MenuPath = "Assets/Create/Lettera/Solid Look", Source = LetteraPaletteSource.Project, Run = () => EditorApplication.ExecuteMenuItem("Assets/Create/Lettera/Solid Look") };
        }
    }

    internal static class LetteraText3DMenus
    {
        [MenuItem("GameObject/Lettera/Text 3D", false, 12)]
        static void Create(MenuCommand command)
        {
            GameObject go;
            GameObject prefab = LetteraSettings.Instance.Text3DPrefab;
            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, (command.context as GameObject)?.transform);
            }
            else
            {
                go = new GameObject("Text 3D (Lettera)", typeof(RectTransform));
                GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
                go.AddComponent<LetteraText3D>();
            }
            Undo.RegisterCreatedObjectUndo(go, "Create Text 3D");
            Selection.activeGameObject = go;
        }

        [MenuItem("CONTEXT/LetteraText3D/Copy Text")]
        static void CopyText(MenuCommand command) => EditorGUIUtility.systemCopyBuffer = ((LetteraText3D)command.context).Text;

        [MenuItem("CONTEXT/LetteraText3D/Paste Text")]
        static void PasteText(MenuCommand command)
        {
            var serialized = new SerializedObject(command.context);
            serialized.FindProperty("m_Data").FindPropertyRelative("Text").stringValue = EditorGUIUtility.systemCopyBuffer;
            serialized.ApplyModifiedProperties();
        }

        [MenuItem("CONTEXT/LetteraText3D/Edit In Scene")]
        static void EditInScene(MenuCommand command) => LetteraSceneEditView.Begin(new[] { command.context });

        [MenuItem("CONTEXT/LetteraText3D/Save As Default")]
        static void SaveDefault(MenuCommand command) => LetteraMenus.SaveAsDefault(((LetteraText3D)command.context).gameObject);

        [MenuItem("Lettera/3D/Allow Creating From Scripts")]
        static void AllowFromScripts()
        {
            if (!EditorUtility.DisplayDialog("Allow creating solid text from scripts", "A Text 3D made by a script needs a default material in every build, which puts the solid shader in all of them, whether a scene uses it or not.", "Allow It", "Cancel")) return;
            LetteraSettings settings = LetteraSettings.Instance;
            Undo.RecordObject(settings, "Allow Creating From Scripts");
            settings.Solid3D.DefaultMaterial = LetteraSolidDefaults.EditorDefault();
            EditorUtility.SetDirty(settings);
        }

        [MenuItem("CONTEXT/LetteraText3D/Bake To Mesh Asset...")]
        static void Bake(MenuCommand command)
        {
            var text = (LetteraText3D)command.context;
            text.ForceRebuild();
            Mesh mesh = text.CurrentMesh;
            if (mesh == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Bake To Mesh Asset", text.name, "asset", "Where to save the mesh");
            if (string.IsNullOrEmpty(path)) return;
            WriteMeshAsset(mesh, path);
        }

        internal static Mesh WriteMeshAsset(Mesh mesh, string path)
        {
            Mesh copy = Object.Instantiate(mesh);
            copy.hideFlags = HideFlags.None;
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            return copy;
        }

        [MenuItem("CONTEXT/LetteraText3D/Export OBJ...")]
        static void Export(MenuCommand command)
        {
            var text = (LetteraText3D)command.context;
            text.ForceRebuild();
            if (text.CurrentMesh == null) return;
            string path = EditorUtility.SaveFilePanel("Export OBJ", "", text.name, "obj");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, ToObj(text.CurrentMesh));
        }

        /// <summary>Positions, normals, UVs and a group per submesh, with X mirrored for OBJ's right-handed frame.</summary>
        internal static string ToObj(Mesh mesh)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            var obj = new StringBuilder(mesh.vertexCount * 64);
            obj.Append("# Lettera solid text\n");
            Vector3[] vertices = mesh.vertices, normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            for (int i = 0; i < vertices.Length; i++) obj.Append("v ").Append((-vertices[i].x).ToString("R", invariant)).Append(' ').Append(vertices[i].y.ToString("R", invariant)).Append(' ').Append(vertices[i].z.ToString("R", invariant)).Append('\n');
            for (int i = 0; i < normals.Length; i++) obj.Append("vn ").Append((-normals[i].x).ToString("R", invariant)).Append(' ').Append(normals[i].y.ToString("R", invariant)).Append(' ').Append(normals[i].z.ToString("R", invariant)).Append('\n');
            for (int i = 0; i < uvs.Length; i++) obj.Append("vt ").Append(uvs[i].x.ToString("R", invariant)).Append(' ').Append(uvs[i].y.ToString("R", invariant)).Append('\n');
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                obj.Append("g surface").Append(s).Append('\n');
                int[] triangles = mesh.GetTriangles(s);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    // The mirror turns winding round, so this keeps faces outward.
                    int a = triangles[t] + 1, b = triangles[t + 2] + 1, c = triangles[t + 1] + 1;
                    obj.Append("f ").Append(a).Append('/').Append(a).Append('/').Append(a).Append(' ').Append(b).Append('/').Append(b).Append('/').Append(b).Append(' ').Append(c).Append('/').Append(c).Append('/').Append(c).Append('\n');
                }
            }
            return obj.ToString();
        }

        [MenuItem("CONTEXT/LetteraWorldText/Convert To Text 3D")]
        static void Convert(MenuCommand command) => ConvertWorldText((LetteraWorldText)command.context);

        [MenuItem("CONTEXT/LetteraWorldText/Convert To Text 3D", true)]
        static bool CanConvert(MenuCommand command) => ((LetteraWorldText)command.context).GetComponent<LetteraSelectable>() == null;

        internal static LetteraText3D ConvertWorldText(LetteraWorldText text)
        {
            if (text == null || text.GetComponent<LetteraSelectable>() != null) return null;
            GameObject go = text.gameObject;
            string json = EditorJsonUtility.ToJson(text);
            Undo.SetCurrentGroupName("Convert To Text 3D");
            int group = Undo.GetCurrentGroup();
            Color color = text.Color;
            Undo.DestroyObjectImmediate(text);
            LetteraText3D solid = Undo.AddComponent<LetteraText3D>(go);
            EditorJsonUtility.FromJsonOverwrite(json, solid);
            solid.Color = color;
            Undo.CollapseUndoOperations(group);
            return solid;
        }
    }
}
