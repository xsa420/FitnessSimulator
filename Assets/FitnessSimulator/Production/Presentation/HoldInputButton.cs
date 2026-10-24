using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Presentation
{
    public sealed class HoldInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private readonly PointerHoldState state = new PointerHoldState();
        public Action<int> Pressed;
        public Action<int> Released;
        public bool AcceptInput = true;
        public bool IsHeld => state.IsHeld;
        public void OnPointerDown(PointerEventData data)
        {
            if (!AcceptInput) return;
            if (state.Press(data.pointerId)) Pressed?.Invoke(data.pointerId);
            Paint();
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (!AcceptInput) return;
            if (state.Release(data.pointerId)) Released?.Invoke(data.pointerId);
            Paint();
        }
        public void CancelSystemPointers() { state.CancelSystemPointers(); Paint(); }
        private void OnDisable() => CancelSystemPointers();
        private void Paint()
        {
            var graphic = GetComponent<Image>();
            if (graphic) graphic.color = IsHeld ? new Color(.13f, .65f, .52f) : new Color(.15f, .27f, .36f);
        }
    }
}
