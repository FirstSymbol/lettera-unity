using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>What draws a selection, a caret and composition underlines. All of it runs next to the text, so none of it rebuilds the text.</summary>
    internal interface ISelectionVisual
    {
        void Show(List<Rect> rects, Color color);
        void ShowCaret(Rect rect, Color color, bool visible);
        void ShowUnderlines(List<Rect> rects, Color color);
        void Follow();
        void Release();
    }

    internal static class SelectionVisuals
    {
        public static ISelectionVisual Create(LetteraSelectable owner, LetteraHighlightOrder order)
        {
            if (owner.VisualSource != null) return owner.VisualSource();
            if (owner.Host is LetteraWorldText world) return new WorldSelectionVisual(world, order);
            return new CanvasSelectionVisual(owner, order);
        }

        public static void Destroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying)
            {
                Object.Destroy(target);
                return;
            }
#if UNITY_EDITOR
            // In the middle of a parent going away the editor refuses this, so it waits for that to end.
            GameObject go = target as GameObject;
            if (go != null && !go.activeInHierarchy)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (target != null) Object.DestroyImmediate(target);
                };
                return;
            }
#endif
            Object.DestroyImmediate(target);
        }

        public const HideFlags Flags = HideFlags.HideInHierarchy | HideFlags.DontSave;
    }

    internal sealed class CanvasSelectionVisual : ISelectionVisual
    {
        readonly LetteraSelectable m_Owner;
        readonly RectTransform m_Text;
        readonly LetteraSelectionOverlay m_Highlight, m_Over;
        readonly bool m_Sibling;

        public CanvasSelectionVisual(LetteraSelectable owner, LetteraHighlightOrder order)
        {
            m_Owner = owner;
            m_Text = owner.Host.TextRect;
            // A graphic draws before its children, so what lies under the text stands next to it.
            m_Sibling = order == LetteraHighlightOrder.Behind && m_Text.parent != null && m_Text.parent.GetComponentInParent<Canvas>() != null;
            m_Highlight = m_Sibling ? MakeOverlay("Lettera Highlight", m_Text.parent, true) : MakeOverlay("Lettera Highlight", m_Text, false);
            m_Over = MakeOverlay("Lettera Overlay", m_Text, false);
            Follow();
        }

        LetteraSelectionOverlay MakeOverlay(string name, Transform parent, bool mirror)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = SelectionVisuals.Flags };
            go.layer = m_Text.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var overlay = go.AddComponent<LetteraSelectionOverlay>();
            overlay.raycastTarget = false;
            if (!mirror) FitInside(overlay.rectTransform, m_Text);
            return overlay;
        }

        // Same local coordinates as the text: filling it with the same pivot puts the origin on the same spot.
        internal static void FitInside(RectTransform child, RectTransform parent)
        {
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.pivot = parent.pivot;
            child.anchoredPosition = Vector2.zero;
            child.sizeDelta = Vector2.zero;
        }

        public void Follow()
        {
            if (!m_Sibling || m_Highlight == null || m_Text == null) return;
            RectTransform rect = m_Highlight.rectTransform;
            rect.anchorMin = m_Text.anchorMin;
            rect.anchorMax = m_Text.anchorMax;
            rect.pivot = m_Text.pivot;
            rect.anchoredPosition = m_Text.anchoredPosition;
            rect.sizeDelta = m_Text.sizeDelta;
            rect.localRotation = m_Text.localRotation;
            rect.localScale = m_Text.localScale;
            int index = m_Text.GetSiblingIndex();
            int own = rect.GetSiblingIndex();
            if (own > index) rect.SetSiblingIndex(index);
        }

        public void Show(List<Rect> rects, Color color)
        {
            m_Highlight.Begin();
            foreach (Rect rect in rects) m_Highlight.Add(rect, color);
            m_Highlight.End();
            Follow();
        }

        public void ShowUnderlines(List<Rect> rects, Color color)
        {
            m_Over.Begin();
            foreach (Rect rect in rects) m_Over.Add(rect, color);
            m_Over.End();
        }

        public void ShowCaret(Rect rect, Color color, bool visible)
        {
            LetteraCaret caret = m_Owner.Editable != null ? m_Owner.Editable.EnsureCaret() : null;
            if (caret != null) caret.SetGeometry(rect, color, visible);
        }

        public void Release()
        {
            if (m_Highlight != null) SelectionVisuals.Destroy(m_Highlight.gameObject);
            if (m_Over != null) SelectionVisuals.Destroy(m_Over.gameObject);
        }
    }

    internal sealed class WorldSelectionVisual : ISelectionVisual
    {
        static Material s_Material;
        readonly LetteraWorldText m_Text;
        readonly GameObject m_Object;
        readonly Mesh m_Mesh;
        readonly MeshRenderer m_Renderer;
        readonly int m_OrderOffset;
        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Color32> m_Colors = new List<Color32>();
        readonly List<int> m_Indices = new List<int>();
        readonly List<Rect> m_Rects = new List<Rect>(), m_Lines = new List<Rect>();
        Color m_Color, m_LineColor, m_CaretColor;
        Rect m_Caret;
        bool m_CaretOn;

        public WorldSelectionVisual(LetteraWorldText text, LetteraHighlightOrder order)
        {
            m_Text = text;
            m_OrderOffset = order == LetteraHighlightOrder.Behind ? -1 : 1;
            m_Object = new GameObject("Lettera World Overlay") { hideFlags = SelectionVisuals.Flags };
            m_Object.layer = text.gameObject.layer;
            m_Object.transform.SetParent(text.transform, false);
            m_Mesh = new Mesh { name = "Lettera Selection", hideFlags = HideFlags.HideAndDontSave };
            m_Object.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = m_Object.AddComponent<MeshRenderer>();
            if (s_Material == null)
            {
                Shader shader = Resources.Load<Shader>("Lettera/LetteraSolid");
                s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            m_Renderer.sharedMaterial = s_Material;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;
            Follow();
        }

        public void Follow()
        {
            if (m_Renderer == null || m_Text == null) return;
            m_Renderer.sortingLayerID = m_Text.SortingLayerID;
            m_Renderer.sortingOrder = m_Text.SortingOrder + m_OrderOffset;
        }

        void Rebuild()
        {
            m_Vertices.Clear();
            m_Colors.Clear();
            m_Indices.Clear();
            foreach (Rect rect in m_Rects) Quad(rect, m_Color);
            foreach (Rect rect in m_Lines) Quad(rect, m_LineColor);
            if (m_CaretOn) Quad(m_Caret, m_CaretColor);
            m_Mesh.Clear();
            m_Mesh.SetVertices(m_Vertices);
            m_Mesh.SetColors(m_Colors);
            m_Mesh.SetTriangles(m_Indices, 0);
            m_Mesh.RecalculateBounds();
        }

        void Quad(Rect r, Color32 color)
        {
            int start = m_Vertices.Count;
            m_Vertices.Add(new Vector3(r.xMin, r.yMin));
            m_Vertices.Add(new Vector3(r.xMin, r.yMax));
            m_Vertices.Add(new Vector3(r.xMax, r.yMax));
            m_Vertices.Add(new Vector3(r.xMax, r.yMin));
            for (int i = 0; i < 4; i++) m_Colors.Add(color);
            m_Indices.Add(start);
            m_Indices.Add(start + 1);
            m_Indices.Add(start + 2);
            m_Indices.Add(start + 2);
            m_Indices.Add(start + 3);
            m_Indices.Add(start);
        }

        internal int QuadCount => m_Vertices.Count / 4;
        internal IReadOnlyList<Rect> LineRects => m_Lines;

        public void Show(List<Rect> rects, Color color)
        {
            m_Rects.Clear();
            m_Rects.AddRange(rects);
            m_Color = color;
            Rebuild();
        }

        public void ShowUnderlines(List<Rect> rects, Color color)
        {
            m_Lines.Clear();
            m_Lines.AddRange(rects);
            m_LineColor = color;
            Rebuild();
        }

        public void ShowCaret(Rect rect, Color color, bool visible)
        {
            if (m_Caret == rect && m_CaretColor == color && m_CaretOn == visible) return;
            m_Caret = rect;
            m_CaretColor = color;
            m_CaretOn = visible;
            Rebuild();
        }

        public void Release()
        {
            SelectionVisuals.Destroy(m_Object);
            SelectionVisuals.Destroy(m_Mesh);
        }
    }
}
