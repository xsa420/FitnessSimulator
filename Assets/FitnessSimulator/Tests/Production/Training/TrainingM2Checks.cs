using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Training
{
    public static class TrainingM2Checks
    {
        private static void Equal<T>(T expected,T actual,string label) { if(!Equals(expected,actual))throw new Exception(label+": expected "+expected+", actual "+actual); }
        private static void True(bool condition,string label) { if(!condition)throw new Exception(label); }
        private static void Reject(Action action,string label) { bool rejected=false; try { action(); } catch(ArgumentException) { rejected=true; } catch(InvalidOperationException) { rejected=true; } True(rejected,label); }
        public static BodyState Body(long strength=14000000)=>BodyState.Create("actor",Enumerable.Repeat(strength,21).ToArray(),25000000,50000000,0);
        public static TrainingProgress Progress()=>TrainingProgress.CreateInitial("actor");
        public static M2WorkoutContext Context(TrainingProgress progress)
        {
            var claim=TrainingClaims.TryClaim(TrainingClaims.CreateDay("run","actor",3,"schedule-1"),"run","actor",3,"schedule-1","workout","afternoon").NextClaim;
            var quota=DayTrainingQuota.Freeze("actor",3,"plan-1",new[]{new TrainingPlanEntry { EntryId="bench",ExerciseId="bench_press",Sets=2,Reps=6 }},progress);
            return TrainingSessionM2.Create("run","actor",3,"workout",quota,claim);
        }
        public static SetBenefitsSnapshot Benefits(int sequence=1,long nominal=3000000,int reduction=1000)
        {
            return new SetBenefitsSnapshot { SnapshotId="benefits-"+sequence,RunId="run",ActorId="actor",Day=3,WorkoutId="workout",WorkoutSetSequence=sequence,RelationshipRevision=5,RuleVersion="relationships-candidate-1",RiskReductionBasisPoints=reduction,RiskSourceIds=new[]{"partner"},TeachingSources=new[]{new TeachingSourceSnapshot { EntitlementId="coach",SourceActorId="coach-actor",SourceKind="PaidTeaching",FirstEligibleWorkoutSetSequence=sequence,NominalTargetMicro=nominal,KnowledgeRuleId="coach3",ActivatedAtCommitId="payment-commit" }} };
        }
        // Isolated settlement fixture; not a production NPC/UI result entry.
        public static TrainingSetResult Result(M2WorkoutContext state,int v)
        {
            var r=state.Workout.PendingRequest;
            return new TrainingSetResult { SetId=r.SetId,RulesVersion=r.RulesVersion,Producer=r.Producer,EffectiveReps=v,Success=v==r.TargetReps,FailedAttempts=v==r.TargetReps?0:3,TerminalReason="M2BodyFixture" };
        }
        public static TeachingResolution Resolution(TeachingClaim claim,long previous=0,long revision=8)=>new TeachingResolution { RunId=claim.RunId,ActorId=claim.ActorId,Day=claim.Day,WorkoutId=claim.WorkoutId,SetId=claim.SetId,ExerciseId=claim.ExerciseId,SnapshotId=claim.SnapshotId,WorkoutSetSequence=claim.WorkoutSetSequence,ClaimKey=claim.ClaimKey,AwardLedgerExpectedRevision=revision,PreviousNominalMicro=previous,TargetNominalMicro=claim.EligibleTargetMicro,DeltaMicro=Math.Max(0,claim.EligibleTargetMicro-previous) };
        public static void ClaimsUseFullIdentityAndNeverReopen()
        {
            var day=TrainingClaims.CreateDay("run","actor",3,"schedule");
            var first=TrainingClaims.TryClaim(day,"run","actor",3,"schedule","w","afternoon");
            Equal(TrainingDayClaimStatus.Available,day.Status,"claim pure"); Equal(TrainingClaimDisposition.Created,first.Disposition,"first claim");
            Equal(TrainingClaimDisposition.AlreadyClaimed,TrainingClaims.TryClaim(first.NextClaim,"run","actor",3,"schedule","w","afternoon").Disposition,"same arrival reuses");
            foreach(var change in new[]{new[]{"other","actor","3","schedule","w","afternoon"},new[]{"run","other","3","schedule","w","afternoon"},new[]{"run","actor","4","schedule","w","afternoon"},new[]{"run","actor","3","changed","w","afternoon"},new[]{"run","actor","3","schedule","w2","afternoon"},new[]{"run","actor","3","schedule","w","evening"}})
                Equal(TrainingClaimDisposition.Conflict,TrainingClaims.TryClaim(first.NextClaim,change[0],change[1],int.Parse(change[2]),change[3],change[4],change[5]).Disposition,"full identity conflict");
            var closed=TrainingClaims.Close(first.NextClaim,TrainingDayCloseReason.Completed);
            Equal(TrainingClaimDisposition.Closed,TrainingClaims.TryClaim(closed,"run","actor",3,"schedule","w","afternoon").Disposition,"closed same workout no restart");
            var missed=TrainingClaims.Close(day,TrainingDayCloseReason.Missed);
            Equal(false,missed.HasWorkoutId,"missed no artificial workout"); Equal(TrainingClaimDisposition.Closed,TrainingClaims.TryClaim(missed,"run","actor",3,"schedule","late","evening").Disposition,"missed no late training");
            Reject(()=>TrainingClaims.Close(day,TrainingDayCloseReason.Completed),"cannot complete absent workout");
            Reject(()=>TrainingClaims.Close(closed,TrainingDayCloseReason.EndedEarly),"different close conflicts");
        }
        public static void FrozenSnapshotIsDeepAndExactContext()
        {
            var body=Body(); var progress=Progress(); var context=Context(progress); var benefits=Benefits();
            var playing=TrainingSessionM2.BeginSet(context,body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,benefits);
            benefits.RiskSourceIds[0]="mutated"; benefits.TeachingSources[0].NominalTargetMicro=1000000; benefits.RiskReductionBasisPoints=9999;
            Equal(3000000L,playing.Benefits.TeachingSources[0].NominalTargetMicro,"deep teaching copy"); Equal("partner",playing.Benefits.RiskSourceIds[0],"deep risk copy"); Equal(1000,playing.Benefits.RiskReductionBasisPoints,"risk frozen");
            Equal(WorkoutPhase.PreSet,context.Workout.Phase,"input context unchanged");
            foreach(var field in new[]{"run","actor","day","workout","sequence"})
            {
                var bad=Benefits(); if(field=="run")bad.RunId="wrong"; if(field=="actor")bad.ActorId="wrong"; if(field=="day")bad.Day=4; if(field=="workout")bad.WorkoutId="wrong"; if(field=="sequence")bad.WorkoutSetSequence=2;
                Reject(()=>TrainingSessionM2.BeginSet(context,body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,bad),"snapshot mismatch "+field);
            }
            Reject(()=>TrainingSessionM2.BeginSet(context,body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,null),"missing benefits not silently none");
        }
        public static void TeachingUsesMaxBoundaryZeroAndNominalDelta()
        {
            var progress=Progress(); var body=Body(); var benefits=Benefits();
            benefits.TeachingSources=benefits.TeachingSources.Concat(new[]{new TeachingSourceSnapshot { EntitlementId="veteran",SourceActorId="veteran-actor",SourceKind="Partner",FirstEligibleWorkoutSetSequence=1,NominalTargetMicro=1000000,KnowledgeRuleId="veteran1",ActivatedAtCommitId="partner-commit" }}).ToArray();
            var playing=TrainingSessionM2.BeginSet(Context(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,benefits);
            var ready=TrainingSessionM2.CompleteResult(playing,Result(playing,1)); var claim=TrainingSessionM2.PrepareTeachingClaim(ready);
            Equal(3000000L,claim.EligibleTargetMicro,"max3 not sum4"); Equal(2000000L,Resolution(claim,1000000).DeltaMicro,"coach after veteran only2");
            var zero=TrainingSessionM2.PrepareTeachingClaim(TrainingSessionM2.CompleteResult(playing,Result(playing,0)));
            Equal(0L,zero.EligibleTargetMicro,"zero no target"); Equal(0L,Resolution(zero,1000000).DeltaMicro,"zero no nominal award");
            var future=Benefits(); future.TeachingSources[0].FirstEligibleWorkoutSetSequence=2;
            var first=TrainingSessionM2.BeginSet(Context(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,future);
            Equal(0L,TrainingSessionM2.PrepareTeachingClaim(TrainingSessionM2.CompleteResult(first,Result(first,1))).EligibleTargetMicro,"before payment sequence not retroactive");
            var duplicate=Benefits(); duplicate.TeachingSources=new[]{duplicate.TeachingSources[0],duplicate.TeachingSources[0].Clone()};
            Reject(()=>duplicate.Validate(),"duplicate source rejected");
        }
        public static void SettlementReadsOldKnowledgeAndStoredRisk()
        {
            var progress=Progress(); var body=Body(); body.Exercise("bench_press").KnowledgeMicro=29000000; body.Exercise("bench_press").ProficiencyMicro=99000000;
            var benefits=Benefits(); var playing=TrainingSessionM2.BeginSet(Context(progress),body,progress,TrainingSetType.StrengthChallenge,TrainingProducer.PlayerTouch,benefits);
            var ready=TrainingSessionM2.CompleteResult(playing,Result(playing,1)); var resolution=Resolution(TrainingSessionM2.PrepareTeachingClaim(ready),1000000);
            var done=TrainingSessionM2.Settle(ready,body,progress,null,resolution);
            Equal(100000000L,done.Settlement.Body.Exercise("bench_press").ProficiencyMicro,"old K caps X"); Equal(32000000L,done.Settlement.Body.Exercise("bench_press").KnowledgeMicro,"failure1+teaching2 after X");
            var repeat=TrainingSessionM2.Settle(done.Context,done.Settlement.Body,done.Settlement.Progress,null,resolution);
            True(repeat.Settlement.AlreadySettled,"same resolution replay");
            var conflict=resolution.Clone(); conflict.AwardLedgerExpectedRevision++;
            Reject(()=>TrainingSessionM2.Settle(done.Context,done.Settlement.Body,done.Settlement.Progress,null,conflict),"changed resolution conflict");
            var wrong=resolution.Clone(); wrong.DeltaMicro=3000000; Reject(()=>TrainingSessionM2.Settle(ready,body,progress,null,wrong),"wrong delta rejected");
            var wrongSequence=resolution.Clone(); wrongSequence.WorkoutSetSequence++; Reject(()=>TrainingSessionM2.Settle(ready,body,progress,null,wrongSequence),"wrong sequence rejected");
            body=Body(); foreach(var id in ExerciseCatalog.Get("bench_press").TargetRegionIds)body.Region(id).SetTemporary(85);
            var risk=TrainingSessionM2.BeginSet(Context(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,Benefits());
            var riskReady=TrainingSessionM2.CompleteResult(risk,Result(risk,6));
            var rolls=ExerciseCatalog.Get("bench_press").TargetRegionIds.ToDictionary(id=>id,id=>2619930050u);
            var safe=TrainingSessionM2.Settle(riskReady,body,progress,rolls,Resolution(TrainingSessionM2.PrepareTeachingClaim(riskReady)));
            Equal(0,safe.Settlement.Calculation.InjuredRegionIds.Length,"T125 stored10pp u.61 safe");
        }
        public static void FullKnowledgeStillConsumesResolutionAndNoneIsExplicit()
        {
            var body=Body(); body.Exercise("bench_press").KnowledgeMicro=100000000; var p=Progress();
            var playing=TrainingSessionM2.BeginSet(Context(p),body,p,TrainingSetType.Volume,TrainingProducer.PlayerTouch,Benefits());
            var ready=TrainingSessionM2.CompleteResult(playing,Result(playing,1));
            var done=TrainingSessionM2.Settle(ready,body,p,null,Resolution(TrainingSessionM2.PrepareTeachingClaim(ready)));
            Equal(100000000L,done.Settlement.Body.Exercise("bench_press").KnowledgeMicro,"K stays cap"); Equal(3000000L,done.Context.AppliedTeachingResolution.DeltaMicro,"nominal3 remains consumed for relation ledger");
            var empty=SetBenefitsSnapshot.None("none","run","actor",3,"workout",1);
            var nonePlaying=TrainingSessionM2.BeginSet(Context(p),Body(),p,TrainingSetType.Volume,TrainingProducer.PlayerTouch,empty);
            var noneReady=TrainingSessionM2.CompleteResult(nonePlaying,Result(nonePlaying,1)); Equal(0L,TrainingSessionM2.PrepareTeachingClaim(noneReady).EligibleTargetMicro,"explicit none");
            nonePlaying.HasSetBenefits=false; Reject(()=>TrainingSessionM2.NormalizeAfterLoad(nonePlaying),"M2 missing flag never legacy upgraded");
        }
        public static void NormalizationIsPureAndRequiredPayloadsStayRequired()
        {
            var p=Progress(); var context=Context(p);
            context.Benefits=new SetBenefitsSnapshot(); context.AppliedTeachingResolution=new TeachingResolution();
            var normalized=TrainingSessionM2.NormalizeAfterLoad(context);
            True(context.Benefits!=null&&context.AppliedTeachingResolution!=null,"normalize doesn't mutate input"); True(normalized.Benefits==null&&normalized.AppliedTeachingResolution==null,"preSet phantom cleared in copy");
            var playing=TrainingSessionM2.BeginSet(normalized,Body(),p,TrainingSetType.Volume,TrainingProducer.PlayerTouch,Benefits());
            var clone=TrainingSessionM2.Clone(playing); clone.Benefits.TeachingSources[0].NominalTargetMicro=1000000;
            Equal(3000000L,playing.Benefits.TeachingSources[0].NominalTargetMicro,"clone deep");
            playing.SchemaVersion=0; Reject(()=>TrainingSessionM2.Restore(playing),"no implicit M1 migration");
            Reject(()=>TrainingSessionM2.Create("wrong","actor",3,"workout",context.Workout.Quota,TrainingClaims.TryClaim(TrainingClaims.CreateDay("run","actor",3,"s"),"run","actor",3,"s","workout","afternoon").NextClaim),"run explicitly validated");
        }
        public static void RunAll(Action<string> report)
        {
            foreach(var action in new Action[]{ClaimsUseFullIdentityAndNeverReopen,FrozenSnapshotIsDeepAndExactContext,TeachingUsesMaxBoundaryZeroAndNominalDelta,SettlementReadsOldKnowledgeAndStoredRisk,FullKnowledgeStillConsumesResolutionAndNoneIsExplicit,NormalizationIsPureAndRequiredPayloadsStayRequired}) { action(); report("PASS M2 "+action.Method.Name); }
        }
#if TRAINING_M2_STANDALONE
        public static int Main() { try { TrainingChecks.RunAll(Console.WriteLine); RunAll(Console.WriteLine); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; } }
#endif
    }
}
