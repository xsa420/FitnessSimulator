using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FitnessSimulator.Game.Presentation
{
    public sealed class TrainingPointerSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public Action<string, int, Vector2> Input;
        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
        public void OnPointerDown(PointerEventData data) => Emit("PointerDown", data);
        public void OnPointerUp(PointerEventData data) => Emit("PointerUp", data);
        public void OnDrag(PointerEventData data) => Emit("PointerMove", data);
        private void Emit(string kind, PointerEventData data)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var local)) return;
            var bounds = rect.rect;
            if (bounds.width <= 0 || bounds.height <= 0) return;
            Input?.Invoke(kind, data.pointerId, new Vector2(Mathf.Clamp01((local.x - bounds.xMin) / bounds.width), Mathf.Clamp01((local.y - bounds.yMin) / bounds.height)));
        }
    }
}
