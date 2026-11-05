using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>The shipped handles: UI on a shared screen canvas, so they can reach past a masked field. Its target is at least 44 dp wide.</summary>
    [AddComponentMenu("Lettera/Touch Handles")]
    public class LetteraPrefabHandles : MonoBehaviour, ILetteraHandles, ILetteraCaretHandle, ILetteraHandleBounds
    {
        [SerializeField] RectTransform m_Start;
        [SerializeField] RectTransform m_End;
        [SerializeField] RectTransform m_Caret;
        [SerializeField, Min(0)] float m_Size = 44;

        LetteraSelectable m_Owner;

        public RectTransform StartHandle { get => m_Start; set => m_Start = value; }
        public RectTransform EndHandle { get => m_End; set => m_End = value; }
        public RectTransform CaretHandle { get => m_Caret; set => m_Caret = value; }

        internal Vector2 StartPosition => m_Start != null ? (Vector2)m_Start.position : default;
        internal Vector2 EndPosition => m_End != null ? (Vector2)m_End.position : default;
        internal bool ShowsEnds => m_Start != null && m_Start.gameObject.activeSelf;
        internal bool ShowsCaret => m_Caret != null && m_Caret.gameObject.activeSelf;

        public void Show(LetteraSelectable owner)
        {
            m_Owner = owner;
            Wire(m_Start, LetteraHandleDrag.Kind.Start);
            Wire(m_End, LetteraHandleDrag.Kind.End);
            SetActive(m_Start, true);
            SetActive(m_End, true);
            SetActive(m_Caret, false);
        }

        public void ShowCaret(LetteraSelectable owner)
        {
            m_Owner = owner;
            Wire(m_Caret, LetteraHandleDrag.Kind.Caret);
            SetActive(m_Start, false);
            SetActive(m_End, false);
            SetActive(m_Caret, true);
        }

        void Wire(RectTransform handle, LetteraHandleDrag.Kind kind)
        {
            if (handle == null) return;
            var drag = handle.GetComponent<LetteraHandleDrag>();
            if (drag == null) drag = handle.gameObject.AddComponent<LetteraHandleDrag>();
            drag.Owner = m_Owner;
            drag.Which = kind;
            if (handle.GetComponent<LetteraKeepFocus>() == null) handle.gameObject.AddComponent<LetteraKeepFocus>();
        }

        static void SetActive(RectTransform rect, bool on)
        {
            if (rect != null && rect.gameObject.activeSelf != on) rect.gameObject.SetActive(on);
        }

        public void Move(Rect startCaret, Rect endCaret)
        {
            Place(m_Start, new Vector2(startCaret.x, startCaret.yMin));
            Place(m_End, new Vector2(endCaret.x, endCaret.yMin));
        }

        public void MoveCaret(Rect caret) => Place(m_Caret, new Vector2(caret.x, caret.yMin));

        void Place(RectTransform handle, Vector2 screen)
        {
            if (handle == null) return;
            Canvas canvas = handle.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
            handle.anchorMin = handle.anchorMax = Vector2.zero;
            handle.pivot = new Vector2(0.5f, 1);
            handle.anchoredPosition = screen / scale;
            float size = Mathf.Max(m_Size, 44);
            float pixels = LetteraEditingSettings.Dp(size) / scale;
            handle.sizeDelta = new Vector2(pixels, pixels);
        }

        public void Hide()
        {
            SetActive(m_Start, false);
            SetActive(m_End, false);
        }

        public void HideCaret() => SetActive(m_Caret, false);

        public Rect Grow(Rect rect)
        {
            float size = LetteraEditingSettings.Dp(Mathf.Max(m_Size, 44));
            return Rect.MinMaxRect(rect.xMin, rect.yMin - size, rect.xMax, rect.yMax);
        }

        /// <summary>The handles every field gets unless a project names others: built here, so no asset has to ship.</summary>
        internal static LetteraPrefabHandles CreateDefault()
        {
            var root = new GameObject("Lettera Handles", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(LetteraTouchOverlay.Canvas.transform, false);
            // Handles are placed from the screen's corner, so their parent must cover the screen: a default rect of 100 in the middle put them
            // half a screen up and to the right.
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
            var handles = root.AddComponent<LetteraPrefabHandles>();
            handles.m_Start = MakeHandle(root.transform, "Start");
            handles.m_End = MakeHandle(root.transform, "End");
            handles.m_Caret = MakeHandle(root.transform, "Caret");
            handles.m_Start.gameObject.SetActive(false);
            handles.m_End.gameObject.SetActive(false);
            handles.m_Caret.gameObject.SetActive(false);
            return handles;
        }

        static RectTransform MakeHandle(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LetteraHandleGraphic)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
