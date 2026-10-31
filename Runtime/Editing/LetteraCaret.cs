using UnityEngine;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>
    /// Draws the caret of a field on a Canvas. Subclass and override OnPopulateMesh for another shape, reading
    /// <see cref="CaretRect"/> and <see cref="IsBlinkOn"/>.
    /// </summary>
    [AddComponentMenu("Lettera/Caret")]
    [RequireComponent(typeof(CanvasRenderer))]
    public class LetteraCaret : MaskableGraphic
    {
        [Tooltip("Percent of the line height. 0 uses the project's. Never thinner than a device pixel.")]
        [SerializeField] float m_WidthPercent;
        [Tooltip("Trims the box, in percent of its height: left, bottom, right, top. Negative grows it.")]
        [SerializeField] Vector4 m_Inset;
        [SerializeField] bool m_OverrideColor;
        [Tooltip("Seconds per phase. 0 uses the project's.")]
        [SerializeField] float m_BlinkSeconds;
        [Tooltip("Seconds until the caret stops blinking. 0 uses the project's.")]
        [SerializeField] float m_BlinkTimeout;

        /// <summary>Stops blinking everywhere.</summary>
        public static bool PrefersSteady;

        Rect m_Box;
        Color m_Tint = Color.white;

        public float WidthPercent { get => m_WidthPercent > 0 ? m_WidthPercent : LetteraSettings.Instance.Editing.CaretWidthPercent; set { m_WidthPercent = value; SetVerticesDirty(); } }
        public Vector4 Inset { get => m_Inset; set { m_Inset = value; SetVerticesDirty(); } }
        public float BlinkSeconds { get => m_BlinkSeconds > 0 ? m_BlinkSeconds : LetteraSettings.Instance.Editing.CaretBlinkInterval; set => m_BlinkSeconds = value; }
        public float BlinkTimeout { get => m_BlinkTimeout > 0 ? m_BlinkTimeout : LetteraSettings.Instance.Editing.CaretBlinkTimeout; set => m_BlinkTimeout = value; }

        /// <summary>The caret's box in the text's local space, before width and inset.</summary>
        public Rect CaretRect => m_Box;

        /// <summary>The caret shows in this phase of its blink.</summary>
        public bool IsBlinkOn { get; private set; }

        /// <summary>The rectangle this caret draws, in local space.</summary>
        public Rect DrawnRect => Fitted(m_Box);

        internal void SetGeometry(Rect box, Color glyphColor, bool on)
        {
            bool changed = box != m_Box || (!m_OverrideColor && glyphColor != m_Tint);
            m_Box = box;
            if (!m_OverrideColor) m_Tint = glyphColor;
            if (changed) SetVerticesDirty();
            SetBlink(on);
        }

        internal void SetBlink(bool on)
        {
            if (IsBlinkOn == on && canvasRenderer.GetAlpha() == (on ? 1f : 0f)) return;
            IsBlinkOn = on;
            // Showing and hiding must not touch any geometry.
            canvasRenderer.SetAlpha(on ? 1 : 0);
        }

        Rect Fitted(Rect box)
        {
            float height = box.height;
            Canvas parentCanvas = canvas;
            float pixel = parentCanvas != null && parentCanvas.scaleFactor > 0 ? 1f / parentCanvas.scaleFactor : 1f;
            float width = Mathf.Max(height * WidthPercent * 0.01f, pixel);
            float left = box.x - width * 0.5f + m_Inset.x * 0.01f * height;
            float right = box.x + width * 0.5f - m_Inset.z * 0.01f * height;
            return Rect.MinMaxRect(left, box.yMin + m_Inset.y * 0.01f * height, right, box.yMax - m_Inset.w * 0.01f * height);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = Fitted(m_Box);
            Color32 c = m_OverrideColor ? color : m_Tint * color;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector4.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
