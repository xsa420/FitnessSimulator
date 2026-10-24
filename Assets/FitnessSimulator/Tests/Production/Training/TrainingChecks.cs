using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Training
{
    public static class TrainingChecks
    {
        private static void Equal<T>(T expected,T actual,string label) { if(!Equals(expected,actual))throw new Exception(label+": expected "+expected+", actual "+actual); }
        private static void True(bool condition,string label) { if(!condition)throw new Exception(label); }
        private static void Reject(Action action,string label) { bool rejected=false; try { action(); } catch(ArgumentException) { rejected=true; } catch(InvalidOperationException) { rejected=true; } True(rejected,label); }
        private static BodyState Body(long strength=10000000)=>BodyState.Create("player",Enumerable.Repeat(strength,21).ToArray(),25000000,50000000,0);
        private static DayTrainingQuota Quota(TrainingProgress progress,int sets=2,int reps=6)=>DayTrainingQuota.Freeze("player",3,"plan1",new[]{new TrainingPlanEntry { EntryId="bench-entry",ExerciseId="bench_press",Sets=sets,Reps=reps }},progress);
        private static WorkoutSnapshot Start(BodyState body,TrainingProgress progress,TrainingSetType type=TrainingSetType.Volume)=>TrainingSession.BeginSet(TrainingSession.Begin("workout1",Quota(progress)),body,progress,type,TrainingProducer.PlayerTouch);
        // Body-only test fixture. Never exposed as a production operation or gameplay evidence.
        private static TrainingSetResult FixtureResult(TrainingSetRequest r,int v)=>new TrainingSetResult { SetId=r.SetId,RulesVersion=r.RulesVersion,Producer=r.Producer,EffectiveReps=v,Success=v==r.TargetReps,FailedAttempts=v==r.TargetReps?0:3,TerminalReason="BodyCalculationFixture" };
        private static void Steer(BenchPressSession game)
        {
            var view=game.View;
            if(view.Balance>0.02) { game.Press(BenchButton.Left); game.Release(BenchButton.Right); }
            else if(view.Balance< -0.02) { game.Press(BenchButton.Right); game.Release(BenchButton.Left); }
            else { game.Release(BenchButton.Left); game.Release(BenchButton.Right); }
        }
        private static BenchPressSession RealGame(TrainingSetRequest request,int desiredValid,int frame=1)
        {
            var game=BenchPressSession.Begin(request,123456);
            for(int i=0;i<100000&&!game.View.IsComplete;i+=frame)
            {
                if(game.View.EffectiveReps<desiredValid)Steer(game); else { game.Release(BenchButton.Left); game.Press(BenchButton.Right); }
                game.Advance(frame);
            }
            True(game.View.IsComplete,"game terminates"); return game;
        }
        public static void NumericAndCatalog()
        {
            Equal(new BigInteger(1414213562373),NumericKernel.SqrtRational12(new Rational(2,1)),"sqrt2");
            Equal(new BigInteger(3),NumericKernel.SqrtRational12(new Rational(25,4*BigInteger.Pow(10,24))),"sqrt half-up");
            foreach(var d in ExerciseCatalog.All)Equal(10000,d.WeightBasisPoints.Sum(),d.Id+" weights");
            Equal(21,ExerciseCatalog.All.SelectMany(d=>d.TargetRegionIds).Distinct().Count(),"21 region coverage");
            var body=Body(20000000); body.Regions[20].StrengthMicro=21000000;
            Equal(996958L,BodyMetrics.GetThinBuildIndexMicro(body),"L golden");
        }
        public static void RealBenchOutcomes()
        {
            var request=Start(Body(),TrainingProgress.CreateInitial("player")).PendingRequest;
            var success=RealGame(request,6); Equal(6,success.Result.EffectiveReps,"real success"); Equal(0,success.Result.FailedAttempts,"controller no errors");
            var partial=RealGame(request,3); Equal(3,partial.Result.EffectiveReps,"real partial"); Equal(3,partial.Result.FailedAttempts,"third error");
            var zero=RealGame(request,0); Equal(0,zero.Result.EffectiveReps,"real zero"); Equal(false,zero.Result.Success,"zero failure");
        }
        public static void BenchPauseReplayAndTickBatching()
        {
            var request=Start(Body(),TrainingProgress.CreateInitial("player")).PendingRequest;
            var a=BenchPressSession.Begin(request,42); var b=BenchPressSession.Begin(request,42);
            // Identical event stream with different rendering-sized advance chunks.
            for(int segment=0;segment<100&&!a.View.IsComplete;segment++)
            {
                a.Press(BenchButton.Right); b.Press(BenchButton.Right);
                a.Advance(100); for(int tick=0;tick<100;tick++)b.Advance(1);
            }
            Equal(a.Result.EffectiveReps,b.Result.EffectiveReps,"batch result"); Equal(a.Result.FailedAttempts,b.Result.FailedAttempts,"batch failures");
            var paused=BenchPressSession.Begin(request,5); paused.Press(BenchButton.Left); paused.Advance(400);
            var before=paused.View; paused.Suspend(); paused.Advance(20000);
            Equal(before.AttemptMilliseconds,paused.View.AttemptMilliseconds,"pause no time"); Equal(before.Balance,paused.View.Balance,"pause no movement");
            Reject(()=>paused.Resume(false,false),"must rebuild held left"); paused.Resume(true,false); paused.Advance(350);
            Equal(before.Balance,paused.View.Balance,"resume preparation no movement"); paused.Advance(1); True(before.Balance!=paused.View.Balance,"continues original attempt");
            var sameTick=BenchPressSession.Begin(request,7); sameTick.Advance(350); sameTick.Press(BenchButton.Left); sameTick.Press(BenchButton.Right); sameTick.Release(BenchButton.Left);
            True(!sameTick.View.LeftHeld&&sameTick.View.RightHeld,"same-tick input order retained");
            Equal(RealGame(request,6).Result.EffectiveReps,RealGame(request,6).Result.EffectiveReps,"restart same request and seed");
        }
        public static void CapacityAndPartialArithmetic()
        {
            foreach(int v in new[]{6,3,0})
            {
                var body=Body(); var progress=TrainingProgress.CreateInitial("player"); var started=Start(body,progress);
                var result=FixtureResult(started.PendingRequest,v);
                var calc=BodyCalculator.CalculateSet(body,started.PendingRequest,result,started.Quota.Entries[0],3,null);
                Equal(v*6,calc.GameSeconds,"effective time");
                Equal(v==6?1472000L:v==3?736000L:0L,calc.NextState.Regions.Sum(x=>x.StrengthMicro)-body.Regions.Sum(x=>x.StrengthMicro),"single pool growth");
                Equal(v==6?8000000L:v==3?4000000L:0L,calc.NextState.Exercise("bench_press").ProficiencyMicro,"proficiency");
                Equal(v==6?40000000L:v==3?20000000L:0L,calc.NextState.Region("chest_upper").TemporaryFatigueMicro,"quota");
                Equal(0L,body.Region("chest_upper").TemporaryFatigueMicro,"pure input unchanged");
            }
        }
        public static void ChallengeKnowledgeAndPenalty()
        {
            var body=Body(14000000); body.Exercise("bench_press").KnowledgeMicro=29000000; body.Exercise("bench_press").ProficiencyMicro=99000000;
            var started=Start(body,TrainingProgress.CreateInitial("player"),TrainingSetType.StrengthChallenge);
            foreach(int v in new[]{1,0})
            {
                var calc=BodyCalculator.CalculateSet(body,started.PendingRequest,FixtureResult(started.PendingRequest,v),started.Quota.Entries[0],3,null);
                Equal(v==1?72000000L:60000000L,calc.NextState.Region("chest_upper").TemporaryFatigueMicro,"full failed penalty");
                Equal(v==1?30000000L:29000000L,calc.NextState.Exercise("bench_press").KnowledgeMicro,"failure K");
                Equal(v==1?100000000L:99000000L,calc.NextState.Exercise("bench_press").ProficiencyMicro,"old K cap");
                Equal(v==1?355200L:0L,calc.NextState.Regions.Sum(x=>x.StrengthMicro)-body.Regions.Sum(x=>x.StrengthMicro),"challenge pool");
            }
        }
        public static void QuotaRemainderAndRecovery()
        {
            var progress=TrainingProgress.CreateInitial("player"); var quota=Quota(progress,3,8); Equal(324,quota.StandardGameSeconds,"3x8 standard");
            var body=Body();
            for(int i=0;i<3;i++)
            {
                var request=new TrainingSetRequest { ActorId="player",SetId="q"+i,ExerciseId="bench_press",PlanEntryId="bench-entry",Tier=1,TargetReps=8,Producer=TrainingProducer.PlayerTouch };
                body=BodyCalculator.CalculateSet(body,request,FixtureResult(request,8),quota.Entries[0],3,null).NextState;
            }
            Equal(80000000L,body.Region("chest_upper").TemporaryFatigueMicro,"full quota exactly80"); Equal("0",body.Region("chest_upper").TemporaryRemainderNumerator,"no residual after full plan");
            var oldQuota=quota.Entries[0].Quota("chest_upper"); progress.Exercise("bench_press").HighestUnlockedTier=2;
            Equal(oldQuota.ToString(),quota.Entries[0].Quota("chest_upper").ToString(),"upgrade doesn't change denominator");
            body.Region("chest_upper").InjuredDay=3; body.Region("chest_upper").ExpiresAtStartOfDay=6;
            body=BodyCalculator.ClearTemporaryFatigue(body); True(body.Region("chest_upper").IsInjured,"T clear not cure");
            True(BodyCalculator.ExpireInjuries(body,5).Region("chest_upper").IsInjured,"full day5 injured");
            True(!BodyCalculator.ExpireInjuries(body,6).Region("chest_upper").IsInjured,"day6 healed");
            body.StressMicro=0; body=BodyCalculator.RecoverStress(body,new Rational(1,4)); body=BodyCalculator.AddStress(body,8); Equal(8000000L,body.StressMicro,"recover before deficit");
        }
        public static void InjuryBoundaries()
        {
            Equal(false,NumericKernel.InjuryOccurs(100,0,0),"100 safe"); Equal(true,NumericKernel.InjuryOccurs(150,10000,uint.MaxValue),"150 mandatory");
            Equal(false,NumericKernel.InjuryOccurs(new Rational(225,2),0,2147483648),"risk equality safe");
            Equal(true,NumericKernel.InjuryOccurs(new Rational(225,2),0,2147483647),"below risk threshold injury");
            var body=Body(); foreach(var id in ExerciseCatalog.Get("bench_press").TargetRegionIds)body.Region(id).SetTemporary(110);
            var start=Start(body,TrainingProgress.CreateInitial("player"));
            Equal(0,BodyCalculator.RequiredInjuryRollRegions(body,start.PendingRequest,FixtureResult(start.PendingRequest,6),start.Quota.Entries[0]).Length,"mandatory injury consumes no random");
            var calc=BodyCalculator.CalculateSet(body,start.PendingRequest,FixtureResult(start.PendingRequest,6),start.Quota.Entries[0],3,null,0,10000);
            Equal(7,calc.InjuredRegionIds.Length,"all involved mandatory injuries"); True(!calc.NextState.Region("glutes").IsInjured,"unrelated region no injury");
            foreach(var id in ExerciseCatalog.Get("bench_press").TargetRegionIds)body.Region(id).SetTemporary(80);
            Equal(7,BodyCalculator.RequiredInjuryRollRegions(body,start.PendingRequest,FixtureResult(start.PendingRequest,6),start.Quota.Entries[0]).Length,"only probabilistic regions require draws");
        }
        public static void LifecycleAutomaticAndIdempotence()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player"); var start=Start(body,progress);
            Reject(()=>TrainingSession.EndWorkout(start),"cannot end during game");
            var result=RealGame(start.PendingRequest,6).Result;
            var ready=TrainingSession.CompleteResult(start,result); Equal(WorkoutPhase.ResultReady,TrainingSession.Restore(ready).Phase,"result survives reload");
            var settlement=TrainingSession.Settle(ready,body,progress,3,null);
            var repeated=TrainingSession.Settle(settlement.Workout,settlement.Body,settlement.Progress,3,null); True(repeated.AlreadySettled,"settlement idempotent");
            Equal(settlement.Body.Revision,repeated.Body.Revision,"no duplicate effects");
            var next=TrainingSession.ContinueAfterFollowers(settlement.Workout,settlement.Body);
            var rest=TrainingSession.Rest(next,settlement.Body,30); rest=TrainingSession.Rest(rest.Workout,rest.Body,30);
            Equal(96000000L,(long)rest.Workout.ElapsedGameSeconds*1000000,"two rests plus36 sec");
            Equal(34000000L,rest.Body.Region("chest_upper").TemporaryFatigueMicro,"two rests minus6");
            var auto=TrainingSession.BeginSet(rest.Workout,rest.Body,settlement.Progress,TrainingSetType.Volume,TrainingProducer.AutoCapacity);
            var autoReady=TrainingSession.CompleteAutomatic(auto); Equal(6,autoReady.PendingResult.EffectiveReps,"auto current N");
            Reject(()=>TrainingSession.BeginSet(next,settlement.Body,settlement.Progress,TrainingSetType.StrengthChallenge,TrainingProducer.AutoCapacity),"no auto challenge");
            var end=TrainingSession.Settle(autoReady,rest.Body,settlement.Progress,3,null);
            var ended=TrainingSession.ContinueAfterFollowers(end.Workout,end.Body); Equal(WorkoutPhase.Ended,ended.Phase,"last group ends");
            Reject(()=>TrainingSession.Rest(ended,end.Body,30),"no rest after final group");
            Equal(WorkoutPhase.Ended,TrainingSession.EndCurrentAction(settlement.Workout,settlement.Body).Phase,"control skips rest of action");
            Reject(()=>TrainingSession.EndCurrentAction(start,body),"cannot end action before settlement");
            Equal(0,TrainingSession.OvertimeSeconds(ended,0),"under standard");
            Equal(30,TrainingSession.OvertimeSeconds(ended,60),"followers counted once");
        }
        public static void ValidationAndHighFatigueAutomatic()
        {
            var body=Body(40000000); var progress=TrainingProgress.CreateInitial("player");
            progress.Exercise("bench_press").HighestUnlockedTier=4; progress.Exercise("bench_press").AutomaticTiers[3]=true;
            long difficulty=BodyCalculator.QuoteDifficulty(body,"bench_press",4);
            foreach(var r in body.Regions)r.StrengthMicro=80000000;
            Equal(difficulty,BodyCalculator.QuoteDifficulty(body,"bench_press",4),"highest tier strength fixed");
            foreach(var id in ExerciseCatalog.Get("bench_press").TargetRegionIds)body.Region(id).SetTemporary(120);
            var workout=TrainingSession.Begin("highT",Quota(progress));
            var auto=TrainingSession.BeginSet(workout,body,progress,TrainingSetType.Volume,TrainingProducer.AutoCapacity);
            var settled=TrainingSession.Settle(TrainingSession.CompleteAutomatic(auto),body,progress,3,null);
            Equal(7,settled.Calculation.InjuredRegionIds.Length,"auto still normal mandatory injury");
            var quota=Quota(progress); quota.Entries[0].QuotaNumerators[0]="1";
            Reject(()=>TrainingSession.Begin("tampered",quota),"reject changed quota");
            var clean=Start(Body(),TrainingProgress.CreateInitial("player"));
            var ready=TrainingSession.CompleteResult(clean,RealGame(clean.PendingRequest,6).Result);
            var changed=ready.PendingResult.Clone(); changed.InputDigest="different";
            Reject(()=>TrainingSession.CompleteResult(ready,changed),"conflicting callback");
            var newer=Body(); newer.Revision=1;
            Reject(()=>TrainingSession.Settle(ready,newer,TrainingProgress.CreateInitial("player"),3,null),"related state revision conflict");
        }
        public static void AutomaticUsesNewDayTarget()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player"); var started=Start(body,progress);
            var first=TrainingSession.Settle(TrainingSession.CompleteResult(started,RealGame(started.PendingRequest,6).Result),body,progress,3,null);
            var nextDayBody=BodyCalculator.ClearTemporaryFatigue(first.Body);
            var quota=DayTrainingQuota.Freeze("player",4,"balanced-next-day",new[]{new TrainingPlanEntry { EntryId="balanced",ExerciseId="bench_press",Sets=3,Reps=8 }},first.Progress);
            var next=TrainingSession.BeginSet(TrainingSession.Begin("workout-next-day",quota),nextDayBody,first.Progress,TrainingSetType.Volume,TrainingProducer.AutoCapacity);
            var ready=TrainingSession.CompleteAutomatic(next); Equal(8,ready.PendingResult.EffectiveReps,"auto uses current target8 not historical6");
            var settled=TrainingSession.Settle(ready,nextDayBody,first.Progress,4,null);
            Equal(48,settled.Calculation.GameSeconds,"auto current game time");
            Equal("80/3",settled.Body.Region("chest_upper").ExactTemporary.ToString(),"new day balanced quota");
        }
        public static void UpgradeLoadAndSharedQuota()
        {
            var body=Body(14000000); var progress=TrainingProgress.CreateInitial("player");
            var challenge=Start(body,progress,TrainingSetType.StrengthChallenge);
            var promoted=TrainingSession.Settle(TrainingSession.CompleteResult(challenge,RealGame(challenge.PendingRequest,6).Result),body,progress,3,null);
            Equal(2,promoted.Progress.Exercise("bench_press").HighestUnlockedTier,"actual challenge promotes");
            var next=TrainingSession.ContinueAfterFollowers(promoted.Workout,promoted.Body);
            var capacity=TrainingSession.BeginSet(next,promoted.Body,promoted.Progress,TrainingSetType.Volume,TrainingProducer.AutoCapacity);
            var rolls=RegionIds.All.ToDictionary(id=>id,id=>uint.MaxValue);
            var second=TrainingSession.Settle(TrainingSession.CompleteAutomatic(capacity),promoted.Body,promoted.Progress,3,rolls);
            Equal(48000000L,second.Body.Region("chest_upper").TemporaryFatigueMicro-promoted.Body.Region("chest_upper").TemporaryFatigueMicro,"g2/g0 ratio adds48");
            Equal(1,second.Workout.Quota.Entries[0].FrozenTier,"frozen tier remains1");
            body=Body(); progress=TrainingProgress.CreateInitial("player");
            var shared=DayTrainingQuota.Freeze("player",3,"shared",new[]{new TrainingPlanEntry{EntryId="bench",ExerciseId="bench_press",Sets=2,Reps=6},new TrainingPlanEntry{EntryId="shoulder",ExerciseId="shoulder_press",Sets=2,Reps=6}},progress);
            Equal("8/1",shared.Entries[0].Quota("shoulder_front").ToString(),"shared bench q8");
            Equal("32/1",shared.Entries[1].Quota("shoulder_front").ToString(),"shared shoulder q32");
            var workout=TrainingSession.Begin("shared-workout",shared);
            for(int i=0;i<2;i++)
            {
                var s=TrainingSession.BeginSet(workout,body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
                var done=TrainingSession.Settle(TrainingSession.CompleteResult(s,RealGame(s.PendingRequest,6).Result),body,progress,3,null);
                body=done.Body; progress=done.Progress; workout=TrainingSession.ContinueAfterFollowers(done.Workout,body);
            }
            workout=TrainingSession.EndWorkout(workout);
            Equal(16000000L,body.Region("shoulder_front").TemporaryFatigueMicro,"early end preserves16 not80");
            Equal("32/1",workout.Quota.Entries[1].Quota("shoulder_front").ToString(),"remaining share unchanged");
        }
        public static void InjuryRoutesToUnrelatedAction()
        {
            var body=Body(); body.Region("triceps_long").SetTemporary(149);
            var progress=TrainingProgress.CreateInitial("player");
            var plan=new[]{new TrainingPlanEntry{EntryId="bench",ExerciseId="bench_press",Sets=2,Reps=6},new TrainingPlanEntry{EntryId="push",ExerciseId="triceps_pushdown",Sets=2,Reps=8},new TrainingPlanEntry{EntryId="squat",ExerciseId="squat",Sets=2,Reps=5}};
            var quota=DayTrainingQuota.Freeze("player",3,"injury-route",plan,progress);
            var started=TrainingSession.BeginSet(TrainingSession.Begin("injury-workout",quota),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
            var done=TrainingSession.Settle(TrainingSession.CompleteResult(started,RealGame(started.PendingRequest,6).Result),body,progress,3,null);
            True(done.Body.Region("triceps_long").IsInjured,"injured after first group"); Equal(3,done.Workout.SetOrdinal,"current action remaining group cancelled");
            var next=TrainingSession.ContinueAfterFollowers(done.Workout,done.Body);
            Equal(2,next.EntryIndex,"related pushdown skipped"); Equal(WorkoutPhase.PreSet,next.Phase,"unrelated remains available");
            var squat=TrainingSession.BeginSet(next,done.Body,done.Progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
            Equal("squat",squat.PendingRequest.ExerciseId,"unrelated squat begins");
        }
        public static void FatigueStressAndPartnerBoundaries()
        {
            Equal(true,NumericKernel.InjuryOccurs(125,1000,2576980377),"T125 partner10 u.60 injury");
            Equal(false,NumericKernel.InjuryOccurs(125,1000,2619930050),"T125 partner10 u.61 safe");
            foreach(int f in new[]{20,100})foreach(int b in new[]{24,25,50,75})
            {
                var body=Body(); body.Region("chest_upper").LocalFatigueMicro=f*1000000L; body.StressMicro=b*1000000L;
                var start=Start(body,TrainingProgress.CreateInitial("player"));
                var calc=BodyCalculator.CalculateSet(body,start.PendingRequest,FixtureResult(start.PendingRequest,6),start.Quota.Entries[0],3,null);
                long retention=b<25?100:b<50?90:b<75?75:60;
                long expected=(new Rational(294400,1)*new Rational(f==20?10:3,10)*new Rational(retention,100)).RoundNonNegative();
                Equal(expected,calc.NextState.Region("chest_upper").StrengthMicro-body.Region("chest_upper").StrengthMicro,"F/B boundary "+f+"/"+b);
            }
            var original=Body(); var p=TrainingProgress.CreateInitial("player"); var first=Start(original,p);
            var calc1=BodyCalculator.CalculateSet(original,first.PendingRequest,FixtureResult(first.PendingRequest,6),first.Quota.Entries[0],3,null);
            var request2=first.PendingRequest.Clone(); request2.SetId="second";
            var calc2=BodyCalculator.CalculateSet(calc1.NextState,request2,FixtureResult(request2,6),first.Quota.Entries[0],3,null);
            Equal(293646L,calc2.NextState.Region("chest_upper").StrengthMicro-calc1.NextState.Region("chest_upper").StrengthMicro,"second set reads grown strength");
        }
        public static void RunAll(Action<string> report)
        {
            foreach(var action in new Action[]{NumericAndCatalog,RealBenchOutcomes,BenchPauseReplayAndTickBatching,CapacityAndPartialArithmetic,ChallengeKnowledgeAndPenalty,QuotaRemainderAndRecovery,InjuryBoundaries,LifecycleAutomaticAndIdempotence,ValidationAndHighFatigueAutomatic,AutomaticUsesNewDayTarget,UpgradeLoadAndSharedQuota,InjuryRoutesToUnrelatedAction,FatigueStressAndPartnerBoundaries}) { action(); report("PASS "+action.Method.Name); }
        }
#if TRAINING_STANDALONE
        public static int Main() { try { RunAll(Console.WriteLine); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; } }
#endif
    }
}
