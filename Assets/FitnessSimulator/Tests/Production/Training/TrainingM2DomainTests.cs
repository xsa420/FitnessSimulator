using NUnit.Framework;
namespace FitnessSimulator.Game.Tests.Training
{
    public sealed class TrainingM2DomainTests
    {
        [Test] public void ClaimsUseFullIdentityAndNeverReopen()=>TrainingM2Checks.ClaimsUseFullIdentityAndNeverReopen();
        [Test] public void FrozenSnapshotIsDeepAndExactContext()=>TrainingM2Checks.FrozenSnapshotIsDeepAndExactContext();
        [Test] public void TeachingUsesMaxBoundaryZeroAndNominalDelta()=>TrainingM2Checks.TeachingUsesMaxBoundaryZeroAndNominalDelta();
        [Test] public void SettlementReadsOldKnowledgeAndStoredRisk()=>TrainingM2Checks.SettlementReadsOldKnowledgeAndStoredRisk();
        [Test] public void FullKnowledgeStillConsumesResolutionAndNoneIsExplicit()=>TrainingM2Checks.FullKnowledgeStillConsumesResolutionAndNoneIsExplicit();
        [Test] public void NormalizationIsPureAndRequiredPayloadsStayRequired()=>TrainingM2Checks.NormalizationIsPureAndRequiredPayloadsStayRequired();
    }
}
