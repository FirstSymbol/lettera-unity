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

    public partial class LetteraSelectable
    {
        [Tooltip("A component with touch handles. Empty uses the built-in ones.")]
        [SerializeField] Component m_HandlesComponent;
        [Tooltip("A component that magnifies. Empty uses the built-in one.")]
        [SerializeField] Component m_LoupeComponent;
        [SerializeField] bool m_NoHandles, m_NoLoupe;
        [Tooltip("Whose menu this text shows on Android. Project follows Project Settings > FirstSymbol > Lettera.")]
        [SerializeField] LetteraMenuOverride m_AndroidMenu;
        [Tooltip("Whose menu this text shows on iOS. Project follows Project Settings > FirstSymbol > Lettera.")]
        [SerializeField] LetteraMenuOverride m_IosMenu;
        [Tooltip("Whose loupe this text shows on a phone. Project follows Project Settings > FirstSymbol > Lettera.")]
        [SerializeField] LetteraLoupeOverride m_LoupeChoice;

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

        /// <summary>Whose menu this text shows on Android, against the project's choice. System still needs its gate on.</summary>
        public LetteraMenuOverride AndroidMenu { get => m_AndroidMenu; set => m_AndroidMenu = value; }

        /// <summary>Whose menu this text shows on iOS, against the project's choice. System still needs its gate on, and iOS 16.</summary>
        public LetteraMenuOverride IosMenu { get => m_IosMenu; set => m_IosMenu = value; }

        /// <summary>Whose loupe this text shows on a phone, against the project's choice. A Loupe component still comes first.</summary>
        public LetteraLoupeOverride LoupeChoice
        {
            get => m_LoupeChoice;
            set
            {
                if (m_LoupeChoice == value) return;
                HideLoupe();
                m_LoupeChoice = value;
                m_Loupe = null;
            }
        }

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

        internal ILetteraLoupe ResolvedLoupe
        {
            get
            {
                if (m_NoLoupe) return null;
                if (m_Loupe != null) return m_Loupe;
                m_Loupe = m_LoupeComponent as ILetteraLoupe;
                if (m_Loupe != null) return m_Loupe;
                m_Loupe = LetteraSystemUi.Loupe(m_LoupeChoice);
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
