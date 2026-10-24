using FitnessSimulator.Game.Presentation;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Presentation
{
    public sealed class PointerHoldStateTests
    {
        [Test] public void TwoFingersDoNotReleaseUntilLastFingerLifts()
        {
            var input = new PointerHoldState();
            Assert.That(input.Press(10), Is.True);
            Assert.That(input.Press(11), Is.False);
            Assert.That(input.Release(10), Is.False);
            Assert.That(input.IsHeld, Is.True);
            Assert.That(input.Release(11), Is.True);
            Assert.That(input.IsHeld, Is.False);
        }
        [Test] public void DuplicateOrUnknownPointerDoesNotGenerateEdge()
        {
            var input = new PointerHoldState();
            input.Press(1);
            Assert.That(input.Press(1), Is.False);
            Assert.That(input.Release(2), Is.False);
            Assert.That(input.IsHeld, Is.True);
        }
        [Test] public void SystemCancelClearsPhysicalPointersAndOldUpCannotReleaseNewHold()
        {
            var input = new PointerHoldState();
            input.Press(1);
            input.CancelSystemPointers();
            Assert.That(input.IsHeld, Is.False);
            Assert.That(input.Press(2), Is.True);
            Assert.That(input.Release(1), Is.False);
            Assert.That(input.IsHeld, Is.True);
        }
    }
}
