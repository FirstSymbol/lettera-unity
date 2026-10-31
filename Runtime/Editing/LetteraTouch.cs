using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Two draggable selection ends.</summary>
    public interface ILetteraHandles
    {
        void Show(LetteraSelectable owner);
        void Move(Rect startCaret, Rect endCaret);
        void Hide();
    }

    /// <summary>One handle under a collapsed caret.</summary>
    public interface ILetteraCaretHandle
    {
        void ShowCaret(LetteraSelectable owner);
        void MoveCaret(Rect caret);
        void HideCaret();
    }

    /// <summary>Tells what room handles take, so a menu can keep clear of them.</summary>
    public interface ILetteraHandleBounds
    {
        /// <summary>A screen rectangle grown by the handles it holds.</summary>
        Rect Grow(Rect rect);
    }

    /// <summary>Magnifies the text under a finger.</summary>
    public interface ILetteraLoupe
    {
        void Show(LetteraSelectable owner);
        void Move(Vector2 screenPoint, Rect lineOnScreen);
        void Hide();
    }

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

    /// <summary>A droplet at the top of an invisible target as wide as a finger.</summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class LetteraHandleGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color32 c = new Color(0.2f, 0.565f, 1f, 1f);
            const int Sides = 16;
            float radius = Mathf.Min(r.width, r.height) * 0.22f;
            var center = new Vector2(r.center.x, r.yMax - radius * 1.2f);
            vh.AddVert(center, c, Vector4.zero);
            for (int i = 0; i <= Sides; i++)
            {
                float angle = i * Mathf.PI * 2 / Sides;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, c, Vector4.zero);
            }
            for (int i = 1; i <= Sides; i++) vh.AddTriangle(0, i, i + 1);
        }

        // The whole rect takes touches, though the drop is smaller.
        public override bool Raycast(Vector2 sp, Camera eventCamera) => true;
    }

    /// <summary>Turns a drag on a handle into a move of a selection's end, keeping where it was grabbed.</summary>
    [AddComponentMenu("")]
    internal sealed class LetteraHandleDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
    {
        internal enum Kind { Start, End, Caret }

        public LetteraSelectable Owner;
        public Kind Which;
        Vector2 m_GrabOffset;

        public void OnPointerDown(PointerEventData data)
        {
            if (Owner == null) return;
            Camera camera = Owner.EventCameraGuess();
            Rect caret = Owner.EndpointOnScreen(Which, camera);
            m_GrabOffset = new Vector2(caret.x, caret.yMin + caret.height * 0.5f) - data.position;
        }

        public void OnBeginDrag(PointerEventData data) => Owner?.BeginHandleDrag();

        public void OnDrag(PointerEventData data)
        {
            if (Owner == null) return;
            Owner.DragHandle(Which, data.position + m_GrabOffset, data.position);
        }

        public void OnEndDrag(PointerEventData data) => Owner?.EndHandleDrag();
    }

    /// <summary>
    /// The shipped loupe. It draws the text's own mesh larger, under a rounded plate, so it needs no camera and works on any canvas,
    /// but what lies behind the text does not show in it.
    /// </summary>
    [AddComponentMenu("Lettera/Mesh Loupe")]
    public class LetteraMeshLoupe : MonoBehaviour, ILetteraLoupe
    {
        [SerializeField, Min(1)] float m_Zoom = 1.25f;
        [SerializeField] Vector2 m_Size = new Vector2(120, 40);
        [SerializeField] float m_Offset = 56;
        [SerializeField] Color m_Plate = new Color(0.97f, 0.97f, 0.98f, 0.98f);

        RectTransform m_Root, m_Copy;
        CanvasRenderer m_Renderer;
        LetteraText m_Source;

        public float Zoom { get => m_Zoom; set => m_Zoom = value; }
        public Vector2 Size { get => m_Size; set => m_Size = value; }
        public float Offset { get => m_Offset; set => m_Offset = value; }
        internal RectTransform Copy => m_Copy;
        internal bool Showing => m_Root != null && m_Root.gameObject.activeSelf;

        void Build()
        {
            if (m_Root != null) return;
            var rootObject = new GameObject("Lettera Loupe", typeof(RectTransform), typeof(Image), typeof(RectMask2D)) { hideFlags = HideFlags.DontSave };
            rootObject.transform.SetParent(LetteraTouchOverlay.Canvas.transform, false);
            m_Root = (RectTransform)rootObject.transform;
            Image plate = rootObject.GetComponent<Image>();
            plate.color = m_Plate;
            plate.raycastTarget = false;
            var copyObject = new GameObject("Copy", typeof(RectTransform), typeof(CanvasRenderer)) { hideFlags = HideFlags.DontSave };
            copyObject.transform.SetParent(m_Root, false);
            m_Copy = (RectTransform)copyObject.transform;
            m_Renderer = copyObject.GetComponent<CanvasRenderer>();
            rootObject.SetActive(false);
        }

        public void Show(LetteraSelectable owner)
        {
            Build();
            m_Source = owner.TextComponent as LetteraText;
            m_Root.gameObject.SetActive(m_Source != null);
            Refresh();
        }

        // The copy borrows the mesh and materials of the text, which it does not own.
        void Refresh()
        {
            if (m_Source == null || m_Renderer == null) return;
            Mesh mesh = m_Source.MeshForLoupe;
            if (mesh == null) return;
            m_Renderer.SetMesh(mesh);
            m_Renderer.materialCount = Mathf.Max(m_Source.canvasRenderer.materialCount, 1);
            for (int i = 0; i < m_Renderer.materialCount; i++) m_Renderer.SetMaterial(m_Source.canvasRenderer.GetMaterial(i), i);
        }

        public void Move(Vector2 screenPoint, Rect lineOnScreen)
        {
            if (m_Root == null || m_Source == null) return;
            Refresh();
            Canvas canvas = m_Root.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
            Vector2 size = new Vector2(LetteraEditingSettings.Dp(m_Size.x), LetteraEditingSettings.Dp(m_Size.y)) / scale;
            m_Root.anchorMin = m_Root.anchorMax = Vector2.zero;
            m_Root.pivot = new Vector2(0.5f, 0);
            m_Root.sizeDelta = size;
            // It follows the finger across and stays on the line upward.
            m_Root.anchoredPosition = new Vector2(screenPoint.x, lineOnScreen.yMax + LetteraEditingSettings.Dp(m_Offset)) / scale;

            Canvas sourceCanvas = m_Source.canvas;
            float sourceScale = sourceCanvas != null ? sourceCanvas.scaleFactor : 1;
            float ratio = m_Zoom * sourceScale / scale;
            m_Copy.anchorMin = m_Copy.anchorMax = new Vector2(0.5f, 0.5f);
            m_Copy.pivot = new Vector2(0.5f, 0.5f);
            m_Copy.sizeDelta = Vector2.zero;
            m_Copy.localScale = new Vector3(ratio, ratio, 1);
            Camera camera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? sourceCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Source.rectTransform, new Vector2(screenPoint.x, lineOnScreen.center.y), camera, out Vector2 focus);
            m_Copy.anchoredPosition = -focus * ratio;
        }

        public void Hide()
        {
            if (m_Root != null) m_Root.gameObject.SetActive(false);
            m_Source = null;
        }

        void OnDestroy()
        {
            if (m_Root != null) SelectionVisuals.Destroy(m_Root.gameObject);
        }

        internal static LetteraMeshLoupe CreateDefault()
        {
            var go = new GameObject("Lettera Default Loupe") { hideFlags = HideFlags.DontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            return go.AddComponent<LetteraMeshLoupe>();
        }
    }

    public partial class LetteraSelectable
    {
        [Tooltip("A component with touch handles. Empty uses the built-in ones.")]
        [SerializeField] Component m_HandlesComponent;
        [Tooltip("A component that magnifies. Empty uses the built-in one.")]
        [SerializeField] Component m_LoupeComponent;
        [SerializeField] bool m_NoHandles, m_NoLoupe;

        ILetteraHandles m_Handles;
        ILetteraLoupe m_Loupe;
        bool m_HandlesShown, m_CaretHandleShown;
        static LetteraPrefabHandles s_DefaultHandles;
        static LetteraMeshLoupe s_DefaultLoupe;

        /// <summary>Handles to use; null with NoHandles leaves none.</summary>
        public Component Handles { get => m_HandlesComponent; set { m_HandlesComponent = value; m_Handles = null; } }
        public Component Loupe { get => m_LoupeComponent; set { m_LoupeComponent = value; m_Loupe = null; } }
        public bool NoHandles { get => m_NoHandles; set => m_NoHandles = value; }
        public bool NoLoupe { get => m_NoLoupe; set => m_NoLoupe = value; }

        internal ILetteraHandles ResolvedHandles
        {
            get
            {
                if (m_NoHandles) return null;
                if (m_Handles != null) return m_Handles;
                m_Handles = m_HandlesComponent as ILetteraHandles;
                if (m_Handles != null) return m_Handles;
                GameObject project = LetteraSettings.Instance.Editing.HandlesPrefab;
                if (project != null)
                {
                    GameObject instance = Instantiate(project, LetteraTouchOverlay.Canvas.transform);
                    m_Handles = instance.GetComponent<ILetteraHandles>();
                    if (m_Handles != null) return m_Handles;
                }
                if (s_DefaultHandles == null) s_DefaultHandles = LetteraPrefabHandles.CreateDefault();
                return m_Handles = s_DefaultHandles;
            }
        }

        ILetteraLoupe ResolvedLoupe
        {
            get
            {
                if (m_NoLoupe) return null;
                if (m_Loupe != null) return m_Loupe;
                m_Loupe = m_LoupeComponent as ILetteraLoupe;
                if (m_Loupe != null) return m_Loupe;
                m_Loupe = LetteraSystemUi.Loupe();
                if (m_Loupe != null) return m_Loupe;
                if (s_DefaultLoupe == null) s_DefaultLoupe = LetteraMeshLoupe.CreateDefault();
                return m_Loupe = s_DefaultLoupe;
            }
        }

        void ShowTouchAffordances(bool withMenu)
        {
            Camera camera = m_PointerCamera != null ? m_PointerCamera : EventCameraGuess();
            ILetteraHandles handles = ResolvedHandles;
            if (!m_Selection.IsCaret)
            {
                if (handles != null)
                {
                    if (handles is ILetteraCaretHandle single) single.HideCaret();
                    handles.Show(this);
                    m_HandlesShown = true;
                    m_CaretHandleShown = false;
                }
            }
            else if (Editable != null && handles is ILetteraCaretHandle caret)
            {
                handles.Hide();
                caret.ShowCaret(this);
                m_CaretHandleShown = true;
                m_HandlesShown = false;
            }
            RefreshHandlesNow(camera);
            if (withMenu) ShowTouchMenu();
        }

        partial void RefreshHandles() => RefreshHandlesNow(m_PointerCamera != null ? m_PointerCamera : EventCameraGuess());

        void RefreshHandlesNow(Camera camera)
        {
            if (!m_HandlesShown && !m_CaretHandleShown) return;
            ILetteraHandles handles = m_Handles;
            if (handles == null) return;
            Rect view = ViewportOnScreen(camera);
            if (m_HandlesShown)
            {
                if (m_Selection.IsCaret)
                {
                    HideHandles();
                    return;
                }
                Rect a = EndpointOnScreen(LetteraHandleDrag.Kind.Start, camera), b = EndpointOnScreen(LetteraHandleDrag.Kind.End, camera);
                handles.Move(a, b);
                // An end scrolled out of view takes its handle with it.
                if (handles is LetteraPrefabHandles shipped)
                {
                    if (shipped.StartHandle != null) shipped.StartHandle.gameObject.SetActive(ViewHolds(view, a));
                    if (shipped.EndHandle != null) shipped.EndHandle.gameObject.SetActive(ViewHolds(view, b));
                }
            }
            else if (handles is ILetteraCaretHandle single)
            {
                Rect c = EndpointOnScreen(LetteraHandleDrag.Kind.Caret, camera);
                single.MoveCaret(c);
                if (handles is LetteraPrefabHandles shipped && shipped.CaretHandle != null) shipped.CaretHandle.gameObject.SetActive(ViewHolds(view, c));
            }
        }

        static bool ViewHolds(Rect view, Rect caret) => caret.center.x >= view.xMin - 1 && caret.center.x <= view.xMax + 1 && caret.center.y >= view.yMin - 1 && caret.center.y <= view.yMax + 1;

        internal Rect EndpointOnScreen(LetteraHandleDrag.Kind which, Camera camera)
        {
            Engine.EnsurePositioned();
            int index = which == LetteraHandleDrag.Kind.Start ? m_Selection.Start : which == LetteraHandleDrag.Kind.End ? m_Selection.End : m_Selection.Head;
            LetteraCaretBias bias = which == LetteraHandleDrag.Kind.Start ? LetteraCaretBias.Forward : which == LetteraHandleDrag.Kind.End ? LetteraCaretBias.Backward : m_Selection.Bias;
            Engine.TryGetCaretRect(index, bias, out Rect local);
            return ToScreen(local, camera);
        }

        internal void BeginHandleDrag()
        {
            Editable?.CommitComposition();
            HideMenu();
        }

        internal void DragHandle(LetteraHandleDrag.Kind which, Vector2 target, Vector2 finger)
        {
            Camera camera = m_PointerCamera != null ? m_PointerCamera : EventCameraGuess();
            int index = HitTest(target, camera, out LetteraCaretBias bias);
            if (which == LetteraHandleDrag.Kind.Caret)
            {
                Request(LetteraSelectionRange.AtCaret(index, bias), "sel.handle");
            }
            else
            {
                bool anchorEnd = which == LetteraHandleDrag.Kind.Start ? m_Selection.Anchor <= m_Selection.Head : m_Selection.Anchor > m_Selection.Head;
                DragEndpoint(anchorEnd, index);
            }
            Editable?.ScrollCaretIntoView();
            MoveLoupe(finger);
            RefreshHandlesNow(camera);
        }

        internal void EndHandleDrag()
        {
            HideLoupe();
            ShowTouchMenu();
        }

        void HideHandles()
        {
            if (m_Handles != null)
            {
                if (m_HandlesShown) m_Handles.Hide();
                if (m_CaretHandleShown && m_Handles is ILetteraCaretHandle single) single.HideCaret();
            }
            m_HandlesShown = m_CaretHandleShown = false;
        }

        void HideHandlesOnEdit()
        {
            if (m_CaretHandleShown || m_HandlesShown) HideHandles();
        }

        void MoveLoupe(Vector2 screenPoint)
        {
            ILetteraLoupe loupe = ResolvedLoupe;
            if (loupe == null) return;
            Camera camera = m_PointerCamera != null ? m_PointerCamera : EventCameraGuess();
            if (!m_LoupeShown)
            {
                loupe.Show(this);
                m_LoupeShown = true;
            }
            TryLineOnScreen(m_Selection.Head, camera, out Rect line);
            loupe.Move(screenPoint, line);
        }

        bool m_LoupeShown;

        void HideLoupe()
        {
            if (!m_LoupeShown) return;
            m_LoupeShown = false;
            m_Loupe?.Hide();
        }
    }

    public partial class LetteraText
    {
        internal Mesh MeshForLoupe => m_Mesh;
        internal TextEngine SharedEngineForTests => Engine;
    }
}
