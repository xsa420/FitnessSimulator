using System.Collections.Generic;

namespace FitnessSimulator.Game.Presentation
{
    // Each button owns a set of physical pointers; one finger lifting must not
    // release another finger still holding the same control.
    public sealed class PointerHoldState
    {
        private readonly HashSet<int> pointers = new HashSet<int>();
        public bool IsHeld => pointers.Count != 0;
        public bool Press(int pointerId)
        {
            bool wasHeld = IsHeld;
            pointers.Add(pointerId);
            return !wasHeld && IsHeld;
        }
        public bool Release(int pointerId)
        {
            bool wasHeld = IsHeld;
            pointers.Remove(pointerId);
            return wasHeld && !IsHeld;
        }
        // System cancel is deliberately NOT a successful gameplay release.
        public void CancelSystemPointers() => pointers.Clear();
    }
}
