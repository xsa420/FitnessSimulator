using NUnit.Framework;
namespace FitnessSimulator.Game.Tests.Training
{
    public sealed class TrainingInputDomainTests
    {
        [TestCase("bench_press")][TestCase("dumbbell_curl")][TestCase("lat_pulldown")][TestCase("triceps_pushdown")][TestCase("squat")][TestCase("hip_thrust")][TestCase("shoulder_press")][TestCase("lateral_raise")]
        public void RealInputSuccessPartialZero(string id)=>TrainingInputChecks.Outcomes(id);
        [TestCase("bench_press")][TestCase("dumbbell_curl")][TestCase("lat_pulldown")][TestCase("triceps_pushdown")][TestCase("squat")][TestCase("hip_thrust")][TestCase("shoulder_press")][TestCase("lateral_raise")]
        public void SuspensionAndClockBatching(string id)=>TrainingInputChecks.PauseAndBatching(id);
        [Test] public void DistinctRuleBoundaries()=>TrainingInputChecks.DistinctRuleBoundaries();
        [Test] public void SourceEligibilityAndMetrics()=>TrainingInputChecks.SourceEligibilityAndMetrics();
        [Test] public void RecoveryMaskAndToleranceBoundaries()=>TrainingInputChecks.RecoveryMaskAndToleranceBoundaries();
    }
}
