using NUnit.Framework;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class RelationshipRulesTests
    {
        static EconomyContext Context()=>new EconomyContext { RunId="r",ActorId="p",Day=1,EventInstanceId="rest",TransactionOrdinal=1 };
        [Test] public void InsufficientTeachingPreservesOpportunityThenDialogueConsumesIt()
        {
            var s=RelationshipRules.Initialize("r","p");var w=RelationshipRules.OpenWorkout(s,"w",new[]{RelationshipRules.Coach});
            var life=LifeRules.Initialize("r","p",79,0);
            var failed=RelationshipRules.PrepareTeaching(s,w,life,Context(),true,2,"commit");
            Assert.That(failed.Status,Is.EqualTo(EconomyStatus.InsufficientFunds));Assert.That(failed.Interaction.Consumed,Is.False);
            var talk=RelationshipRules.PrepareDialogue(failed.Relationships,failed.Interaction,Context(),RelationshipRules.Coach,0,true);
            Assert.That(talk.Interaction.Consumed,Is.True);Assert.That(talk.Relationships.Affections[3].Value,Is.EqualTo(4));Assert.That(s.Affections[3].Value,Is.Zero);
        }
        [Test] public void FreeTeachingStillConsumesAndStartsAtFutureSet()
        {
            var s=RelationshipRules.Initialize("r","p");s.Affections[3].Value=100;
            var w=RelationshipRules.OpenWorkout(s,"w",new[]{RelationshipRules.Coach});
            var r=RelationshipRules.PrepareTeaching(s,w,LifeRules.Initialize("r","p",0,0),Context(),true,3,"commit");
            Assert.That(r.Payment.Receipt.Amount,Is.Zero);Assert.That(r.Interaction.Consumed,Is.True);
            Assert.That(RelationshipRules.FreezeBenefits(r.Relationships,r.Interaction,"snap",3,"presence").TeachingSources[0].FirstEligibleWorkoutSetSequence,Is.EqualTo(3));
        }
        [Test] public void InviteTakesEffectOnlyNextDayAndPresenceControlsBenefits()
        {
            var s=RelationshipRules.Initialize("r","p");s.CurrentBuddy=RelationshipRules.Veteran;s.Affections[1].Value=40;
            var w=RelationshipRules.OpenWorkout(s,"w",new[]{RelationshipRules.Beginner});
            var r=RelationshipRules.PrepareInvite(s,w,Context(),RelationshipRules.Beginner,true);
            Assert.That(r.Relationships.CurrentBuddy,Is.EqualTo(RelationshipRules.Veteran));
            Assert.That(RelationshipRules.FreezeBenefits(r.Relationships,r.Interaction,"snap",1,"presence").RiskReductionBasisPoints,Is.Zero);
            Assert.That(RelationshipRules.AdvanceDay(r.Relationships,2).CurrentBuddy,Is.EqualTo(RelationshipRules.Beginner));
        }
        static TeachingClaim Claim(int sequence,int reps,long target)=>new TeachingClaim { RunId="r",ActorId="p",WorkoutId="w",Day=1,SetId="s"+sequence,ExerciseId="bench",SnapshotId="b"+sequence,WorkoutSetSequence=sequence,EffectiveReps=reps,EligibleTargetMicro=target,EligibleEntitlementIds=target==0?System.Array.Empty<string>():new[]{"t"},ClaimKey=TeachingClaim.Key("r","p","w","bench","s"+sequence) };
        [Test] public void NominalLedgerNeverFallsAndCoachOnlyTopsUpTwo()
        {
            var s=TeachingLedgerRules.Initialize("r","p",1,"w");var first=TeachingLedgerRules.Prepare(s,Claim(1,1,1000000));
            var zero=TeachingLedgerRules.Prepare(first.Ledger,Claim(2,0,0));Assert.That(zero.Ledger.Awards[0].NominalMicro,Is.EqualTo(1000000));Assert.That(zero.Resolution.DeltaMicro,Is.Zero);
            var coach=TeachingLedgerRules.Prepare(zero.Ledger,Claim(3,1,3000000));Assert.That(coach.Resolution.DeltaMicro,Is.EqualTo(2000000));
            Assert.That(TeachingLedgerRules.Prepare(coach.Ledger,Claim(4,1,3000000)).Resolution.DeltaMicro,Is.Zero);Assert.That(s.Awards,Is.Empty);
        }
        [Test] public void ConfessionUsesShownProbabilityAndFreeRetryIsImpossible()
        {
            var s=RelationshipRules.Initialize("r","p");s.Affections[1].Value=100;s.PendingConfessionId="gift";
            var snapshot=ConfessionRules.Freeze(s,500000,600000,18000000);Assert.That(snapshot.ProbabilityThousandths,Is.EqualTo(710));
            var failed=ConfessionRules.Prepare(s,snapshot,uint.MaxValue);Assert.That(failed.Receipt.Success,Is.False);Assert.That(failed.Relationships.NextConfessionDay,Is.EqualTo(9));
            Assert.Throws<System.ArgumentException>(()=>ConfessionRules.Prepare(failed.Relationships,snapshot,0));Assert.That(s.PendingConfessionId,Is.EqualTo("gift"));
        }
    }
}
