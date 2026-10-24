using System.Linq;
using NUnit.Framework;
using FitnessSimulator.Game.Actors;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class TeachingBodyIntegrationTests
    {
        [TestCase(3000000,0)]
        [TestCase(1000000,2000000)]
        public void RealKernelClaimConsumesNominalAwardEvenWhenKnowledgeIsCapped(long firstTarget,long expectedSecondDelta)
        {
            // Explicit boundary fixture for initial K; every training result is obtained from the real kernel.
            string actor=RelationshipRules.Coach;
            var body=BodyState.Create(actor,Enumerable.Repeat(14000000L,21).ToArray(),20000000,50000000,0);
            body.Exercise("bench_press").KnowledgeMicro=100000000;
            var progress=TrainingProgress.CreateInitial(actor);
            var quota=DayTrainingQuota.Freeze(actor,1,"plan",new[]{new TrainingPlanEntry { EntryId="bench",ExerciseId="bench_press",Sets=ExerciseCatalog.Get("bench_press").Sets[0],Reps=ExerciseCatalog.Get("bench_press").Reps[0] }},progress);
            var dayClaim=TrainingClaims.TryClaim(TrainingClaims.CreateDay("r",actor,1,"schedule"),"r",actor,1,"schedule","w","evening").NextClaim;
            var context=TrainingSessionM2.Create("r",actor,1,"w",quota,dayClaim);
            var ledger=TeachingLedgerRules.Initialize("r",actor,1,"w");
            for(int sequence=1;sequence<=2;sequence++)
            {
                var benefits=SetBenefitsSnapshot.None("snapshot"+sequence,"r",actor,1,"w",sequence);
                benefits.TeachingSources=new[]{new TeachingSourceSnapshot { EntitlementId="teaching"+sequence,SourceActorId=RelationshipRules.Coach,SourceKind="BoundaryFixture",KnowledgeRuleId="TeachingMax3V1",ActivatedAtCommitId="commit"+sequence,FirstEligibleWorkoutSetSequence=sequence,NominalTargetMicro=sequence==1?firstTarget:3000000 }};
                context=TrainingSessionM2.BeginSet(context,body,progress,TrainingSetType.Volume,TrainingProducer.NpcPolicy,benefits);
                var trace=ActorInputPolicy.Execute(actor,context.Workout.PendingRequest,7,Enumerable.Repeat(2147483648u,20000).ToArray());
                Assert.That(trace.Result.EffectiveReps,Is.EqualTo(context.Workout.PendingRequest.TargetReps));
                context=TrainingSessionM2.CompleteResult(context,trace.Result);
                var claim=TrainingSessionM2.PrepareTeachingClaim(context);var award=TeachingLedgerRules.Prepare(ledger,claim);
                var settled=TrainingSessionM2.Settle(context,body,progress,null,award.Resolution);
                ledger=award.Ledger;body=settled.Settlement.Body;progress=settled.Settlement.Progress;
                if(sequence==1)
                {
                    Assert.That(body.Exercise("bench_press").KnowledgeMicro,Is.EqualTo(100000000));Assert.That(ledger.Awards[0].NominalMicro,Is.EqualTo(firstTarget));
                    context=TrainingSessionM2.ContinueAfterFollowers(settled.Context,body);
                    body.Exercise("bench_press").KnowledgeMicro=50000000;body.Revision++;
                }
                else
                {
                    Assert.That(award.Resolution.DeltaMicro,Is.EqualTo(expectedSecondDelta));Assert.That(body.Exercise("bench_press").KnowledgeMicro,Is.EqualTo(50000000+expectedSecondDelta));
                    Assert.That(ledger.Awards[0].NominalMicro,Is.EqualTo(3000000));
                }
            }
        }
    }
}
