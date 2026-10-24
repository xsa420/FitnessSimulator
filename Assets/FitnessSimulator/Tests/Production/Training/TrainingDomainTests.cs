using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Training
{
    public sealed class TrainingDomainTests
    {
        [Test] public void NumericAndCatalog()=>TrainingChecks.NumericAndCatalog();
        [Test] public void RealBenchOutcomes()=>TrainingChecks.RealBenchOutcomes();
        [Test] public void BenchPauseReplayAndTickBatching()=>TrainingChecks.BenchPauseReplayAndTickBatching();
        [Test] public void CapacityAndPartialArithmetic()=>TrainingChecks.CapacityAndPartialArithmetic();
        [Test] public void ChallengeKnowledgeAndPenalty()=>TrainingChecks.ChallengeKnowledgeAndPenalty();
        [Test] public void QuotaRemainderAndRecovery()=>TrainingChecks.QuotaRemainderAndRecovery();
        [Test] public void InjuryBoundaries()=>TrainingChecks.InjuryBoundaries();
        [Test] public void LifecycleAutomaticAndIdempotence()=>TrainingChecks.LifecycleAutomaticAndIdempotence();
        [Test] public void ValidationAndHighFatigueAutomatic()=>TrainingChecks.ValidationAndHighFatigueAutomatic();
        [Test] public void AutomaticUsesNewDayTarget()=>TrainingChecks.AutomaticUsesNewDayTarget();
        [Test] public void UpgradeLoadAndSharedQuota()=>TrainingChecks.UpgradeLoadAndSharedQuota();
        [Test] public void InjuryRoutesToUnrelatedAction()=>TrainingChecks.InjuryRoutesToUnrelatedAction();
        [Test] public void FatigueStressAndPartnerBoundaries()=>TrainingChecks.FatigueStressAndPartnerBoundaries();
    }
}
