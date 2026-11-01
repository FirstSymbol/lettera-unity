using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Lettera
{
    /// <summary>Gives world text pointer events: add this to the camera.</summary>
    [AddComponentMenu("Lettera/World Raycaster")]
    [RequireComponent(typeof(Camera))]
    public class LetteraWorldRaycaster : BaseRaycaster
    {
        [Tooltip("Layers that hide text behind them. Empty ignores physics.")]
        [SerializeField] LayerMask m_Blockers;
        [Tooltip("Extra room around each text's rect, in its local units.")]
        [SerializeField] float m_Padding;

        Camera m_Camera;

        public override Camera eventCamera => m_Camera != null ? m_Camera : m_Camera = GetComponent<Camera>();
        public LayerMask Blockers { get => m_Blockers; set => m_Blockers = value; }
        public float Padding { get => m_Padding; set => m_Padding = value; }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Camera camera = eventCamera;
            if (camera == null) return;
            Vector3 viewport = camera.ScreenToViewportPoint(eventData.position);
            if (viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) return;
            Ray ray = camera.ScreenPointToRay(eventData.position);
            float limit = float.MaxValue;
            if (m_Blockers.value != 0 && Physics.Raycast(ray, out RaycastHit block, camera.farClipPlane, m_Blockers)) limit = block.distance;

            foreach (LetteraWorldText text in LetteraWorldBatcher.Texts)
            {
                if (text == null || !text.RaycastTarget || !text.IsVisible || (camera.cullingMask & (1 << text.gameObject.layer)) == 0) continue;
                var rect = (RectTransform)text.transform;
                var plane = new Plane(rect.forward, rect.position);
                if (!plane.Raycast(ray, out float distance) || distance > limit || distance > camera.farClipPlane || distance < camera.nearClipPlane) continue;
                Vector3 point = ray.GetPoint(distance);
                Vector2 local = rect.InverseTransformPoint(point);
                Rect area = rect.rect;
                area.xMin -= m_Padding;
                area.yMin -= m_Padding;
                area.xMax += m_Padding;
                area.yMax += m_Padding;
                if (!area.Contains(local)) continue;
                if (text.Route == LetteraRoute.None) continue;
                if (text.Route == LetteraRoute.Ranges && LetteraRangeRouter.Of((ITextComponent)text).HitTest(eventData.position, camera) == 0) continue;
                resultAppendList.Add(new RaycastResult
                {
                    gameObject = text.gameObject, module = this, distance = distance, worldPosition = point, worldNormal = -rect.forward,
                    screenPosition = eventData.position, displayIndex = camera.targetDisplay, index = resultAppendList.Count,
                    sortingLayer = text.SortingLayerID, sortingOrder = text.SortingOrder,
                });
            }
        }
    }
}
