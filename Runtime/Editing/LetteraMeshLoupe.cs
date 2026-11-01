using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
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
}
