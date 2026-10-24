using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Training
{
    public enum WorkoutPhase { PreSet,Playing,ResultReady,Settled,Ended }
    [Serializable] public sealed class WorkoutSnapshot
    {
        public string WorkoutId;
        public DayTrainingQuota Quota;
        public WorkoutPhase Phase;
        public int EntryIndex,SetOrdinal=1,NextSetSequence=1,ElapsedGameSeconds;
        public TrainingSetRequest PendingRequest;
        public TrainingSetResult PendingResult;
        public BodyState PreSetBody;
        public long PreSetTrainingRevision;
        public string[] SettledSetIds=Array.Empty<string>();
        public string[] PracticedExerciseIds=Array.Empty<string>();
    }
    public sealed class TrainingSettlement
    {
        public WorkoutSnapshot Workout { get; internal set; }
        public BodyState Body { get; internal set; }
        public TrainingProgress Progress { get; internal set; }
        public BodySetCalculation Calculation { get; internal set; }
        public bool AlreadySettled { get; internal set; }
    }
    public sealed class TrainingRest
    {
        public WorkoutSnapshot Workout { get; internal set; }
        public BodyState Body { get; internal set; }
    }
    /// <summary>Pure transitions. Caller atomically commits returned snapshots with event receipts.</summary>
    public static class TrainingSession
    {
        public static WorkoutSnapshot Begin(string workoutId,DayTrainingQuota quota)
        {
            if(string.IsNullOrWhiteSpace(workoutId)||quota==null||quota.Entries==null||quota.Entries.Length==0||quota.RulesVersion!=ExerciseCatalog.Version)throw new ArgumentException("Invalid workout");
            quota.Validate();
            return new WorkoutSnapshot { WorkoutId=workoutId,Quota=CopyQuota(quota),Phase=WorkoutPhase.PreSet };
        }
        public static WorkoutSnapshot BeginSet(WorkoutSnapshot state,BodyState body,TrainingProgress progress,TrainingSetType type,TrainingProducer producer)
        {
            RequireActor(state,body,progress); RequirePhase(state,WorkoutPhase.PreSet); body.Validate();
            progress.Validate();
            var next=Clone(state); var entry=Current(state); var d=ExerciseCatalog.Get(entry.Plan.ExerciseId); var p=progress.Exercise(d.Id);
            if(d.TargetRegionIds.Any(id=>body.Region(id).IsInjured))throw new InvalidOperationException("Injured target: skip via SkipUnavailableActions");
            int tier=type==TrainingSetType.Volume?p.HighestUnlockedTier:p.HighestUnlockedTier+1;
            if(tier>4)throw new InvalidOperationException("Maximum tier");
            var strength=d.TargetRegionIds.Sum(id=>body.Region(id).StrengthMicro);
            if(strength<d.Threshold(tier)*NumericKernel.Micro)throw new InvalidOperationException("Strength threshold");
            if(producer==TrainingProducer.TestInjected)throw new InvalidOperationException("No production result injection");
            if(producer==TrainingProducer.AutoCapacity&&(type!=TrainingSetType.Volume||!p.AutomaticTiers[tier-1]))throw new InvalidOperationException("No automatic eligibility");
            next.PendingRequest=new TrainingSetRequest { SetId=state.WorkoutId+"/"+Uri.EscapeDataString(entry.Plan.EntryId)+"/"+state.SetOrdinal,ActorId=body.ActorId,ExerciseId=d.Id,PlanEntryId=entry.Plan.EntryId,Tier=tier,TargetReps=entry.Plan.Reps,SetOrdinal=state.SetOrdinal,WorkoutSetSequence=next.NextSetSequence++,SetType=type,Producer=producer,DifficultyMicro=BodyCalculator.QuoteDifficulty(body,d.Id,tier) };
            next.PendingRequest.Validate(); next.PreSetBody=body.Clone(); next.PreSetTrainingRevision=progress.Revision; next.Phase=WorkoutPhase.Playing; return next;
        }
        public static WorkoutSnapshot CompleteResult(WorkoutSnapshot state,TrainingSetResult result)
        {
            if(state.Phase==WorkoutPhase.ResultReady)
            {
                result.Validate(state.PendingRequest);
                if(!SameResult(result,state.PendingResult))throw new InvalidOperationException("Conflicting duplicate result");
                return Clone(state);
            }
            RequirePhase(state,WorkoutPhase.Playing); result.Validate(state.PendingRequest);
            var next=Clone(state); next.PendingResult=result.Clone(); next.Phase=WorkoutPhase.ResultReady; return next;
        }
        public static WorkoutSnapshot CompleteAutomatic(WorkoutSnapshot state)
        {
            RequirePhase(state,WorkoutPhase.Playing); var r=state.PendingRequest;
            if(r.Producer!=TrainingProducer.AutoCapacity||r.SetType!=TrainingSetType.Volume)throw new InvalidOperationException("Not automatic capacity");
            return CompleteResult(state,new TrainingSetResult { SetId=r.SetId,RulesVersion=r.RulesVersion,Producer=r.Producer,EffectiveReps=r.TargetReps,FailedAttempts=0,Success=true,TerminalReason="AutomaticCapacity" });
        }
        public static TrainingSettlement Settle(WorkoutSnapshot state,BodyState currentBody,TrainingProgress progress,int day,IReadOnlyDictionary<string,uint> injuryRolls,long teachingKnowledgeMicro=0,int injuryReductionBasisPoints=0)
        {
            RequireActor(state,currentBody,progress);
            if(state.Phase==WorkoutPhase.Settled&&state.PendingRequest!=null&&state.SettledSetIds.Contains(state.PendingRequest.SetId))return new TrainingSettlement { Workout=Clone(state),Body=currentBody.Clone(),Progress=progress.Clone(),AlreadySettled=true };
            RequirePhase(state,WorkoutPhase.ResultReady);
            if(currentBody.Revision!=state.PreSetBody.Revision||progress.Revision!=state.PreSetTrainingRevision||day!=state.Quota.Day)throw new InvalidOperationException("Set context revision/day conflict");
            var request=state.PendingRequest; var result=state.PendingResult;
            var calc=BodyCalculator.CalculateSet(state.PreSetBody,request,result,Current(state),day,injuryRolls,teachingKnowledgeMicro,injuryReductionBasisPoints);
            var next=Clone(state); var nextProgress=progress.Clone();
            if(result.Success)
            {
                var p=nextProgress.Exercise(request.ExerciseId);
                if(request.SetType==TrainingSetType.StrengthChallenge)p.HighestUnlockedTier=request.Tier;
                if(request.Producer!=TrainingProducer.ScriptReplay)p.AutomaticTiers[request.Tier-1]=true;
            }
            nextProgress.Revision=checked(progress.Revision+1);
            next.ElapsedGameSeconds=checked(next.ElapsedGameSeconds+calc.GameSeconds);
            next.SettledSetIds=next.SettledSetIds.Concat(new[]{request.SetId}).ToArray();
            if(result.EffectiveReps>0)next.PracticedExerciseIds=next.PracticedExerciseIds.Concat(new[]{request.ExerciseId}).Distinct().ToArray();
            next.SetOrdinal++;
            if(calc.InjuredRegionIds.Length>0)next.SetOrdinal=Current(state).Plan.Sets+1;
            next.Phase=WorkoutPhase.Settled;
            return new TrainingSettlement { Workout=next,Body=calc.NextState,Progress=nextProgress,Calculation=calc };
        }
        public static WorkoutSnapshot ContinueAfterFollowers(WorkoutSnapshot state,BodyState body)
        {
            RequirePhase(state,WorkoutPhase.Settled); var next=Clone(state);
            next.PendingRequest=null; next.PendingResult=null; next.PreSetBody=null;
            if(next.SetOrdinal>Current(next).Plan.Sets) { next.EntryIndex++; next.SetOrdinal=1; }
            next.Phase=next.EntryIndex>=next.Quota.Entries.Length?WorkoutPhase.Ended:WorkoutPhase.PreSet;
            return SkipUnavailableActions(next,body);
        }
        public static WorkoutSnapshot SkipUnavailableActions(WorkoutSnapshot state,BodyState body)
        {
            if(state.Quota.ActorId!=body.ActorId)throw new InvalidOperationException("Wrong actor");
            if(state.Phase==WorkoutPhase.Ended)return Clone(state);
            RequirePhase(state,WorkoutPhase.PreSet); var next=Clone(state);
            while(next.EntryIndex<next.Quota.Entries.Length&&ExerciseCatalog.Get(Current(next).Plan.ExerciseId).TargetRegionIds.Any(id=>body.Region(id).IsInjured)) { next.EntryIndex++; next.SetOrdinal=1; }
            if(next.EntryIndex==next.Quota.Entries.Length)next.Phase=WorkoutPhase.Ended;
            return next;
        }
        public static TrainingRest Rest(WorkoutSnapshot state,BodyState body,int seconds)
        {
            RequirePhase(state,WorkoutPhase.PreSet);
            if(state.Quota.ActorId!=body.ActorId)throw new InvalidOperationException("Wrong actor");
            var next=Clone(state); var nextBody=BodyCalculator.Rest(body,seconds); next.ElapsedGameSeconds=checked(next.ElapsedGameSeconds+seconds);
            return new TrainingRest { Workout=next,Body=nextBody };
        }
        public static WorkoutSnapshot EndWorkout(WorkoutSnapshot state)
        {
            RequirePhase(state,WorkoutPhase.PreSet); var next=Clone(state); next.Phase=WorkoutPhase.Ended; return next;
        }
        public static WorkoutSnapshot EndCurrentAction(WorkoutSnapshot state,BodyState body)
        {
            RequirePhase(state,WorkoutPhase.Settled);
            var next=Clone(state); next.SetOrdinal=Current(next).Plan.Sets+1;
            return ContinueAfterFollowers(next,body);
        }
        public static WorkoutSnapshot Restore(WorkoutSnapshot state)
        {
            state=Clone(state);
            NormalizeAfterLoad(state);
            state.Quota.Validate();
            if(!Enum.IsDefined(typeof(WorkoutPhase),state.Phase)||state.EntryIndex<0||state.EntryIndex>state.Quota.Entries.Length||state.SetOrdinal<1||state.NextSetSequence<1||state.ElapsedGameSeconds<0)throw new ArgumentException("Invalid workout cursor");
            if(state.Phase!=WorkoutPhase.Ended&&state.EntryIndex==state.Quota.Entries.Length)throw new ArgumentException("Missing current entry");
            if(state.Phase==WorkoutPhase.Playing||state.Phase==WorkoutPhase.ResultReady||state.Phase==WorkoutPhase.Settled)
            {
                state.PendingRequest.Validate(); state.PreSetBody.Validate();
                if(state.PendingRequest.ActorId!=state.Quota.ActorId||state.PreSetBody.ActorId!=state.Quota.ActorId||state.PendingRequest.PlanEntryId!=Current(state).Plan.EntryId||state.PendingRequest.TargetReps!=Current(state).Plan.Reps)throw new ArgumentException("Invalid pending set context");
                if(state.Phase!=WorkoutPhase.Playing)state.PendingResult.Validate(state.PendingRequest);
                if(state.Phase==WorkoutPhase.Settled&&!state.SettledSetIds.Contains(state.PendingRequest.SetId))throw new ArgumentException("Missing settlement receipt");
            }
            var next=Clone(state);
            // Playing is restarted using the same immutable request/body/seed supplied by the application.
            // ResultReady/Settled stay exactly at their committed boundaries.
            return next;
        }
        public static int OvertimeSeconds(WorkoutSnapshot state)=>Math.Max(0,state.ElapsedGameSeconds-state.Quota.StandardGameSeconds);
        public static int OvertimeSeconds(WorkoutSnapshot state,int explicitFollowerSeconds)
        {
            if(explicitFollowerSeconds<0)throw new ArgumentOutOfRangeException(nameof(explicitFollowerSeconds));
            return Math.Max(0,checked(state.ElapsedGameSeconds+explicitFollowerSeconds)-state.Quota.StandardGameSeconds);
        }
        private static QuotaEntry Current(WorkoutSnapshot state)=>state.Quota.Entries[state.EntryIndex];
        private static void RequirePhase(WorkoutSnapshot s,WorkoutPhase p) { if(s.Phase!=p)throw new InvalidOperationException("Wrong workout phase: "+s.Phase); }
        private static void RequireActor(WorkoutSnapshot s,BodyState b,TrainingProgress p) { if(s.Quota.ActorId!=b.ActorId||p.ActorId!=b.ActorId)throw new InvalidOperationException("Wrong actor"); }
        private static bool SameResult(TrainingSetResult a,TrainingSetResult b)=>a.SetId==b.SetId&&a.RulesVersion==b.RulesVersion&&a.Producer==b.Producer&&a.EffectiveReps==b.EffectiveReps&&a.FailedAttempts==b.FailedAttempts&&a.Success==b.Success&&a.TerminalReason==b.TerminalReason&&a.InputDigest==b.InputDigest&&a.InputEventCount==b.InputEventCount&&a.MinigameSeed==b.MinigameSeed;
        private static bool HasPendingSet(WorkoutPhase phase)=>phase==WorkoutPhase.Playing||phase==WorkoutPhase.ResultReady||phase==WorkoutPhase.Settled;
        private static bool HasPendingResult(WorkoutPhase phase)=>phase==WorkoutPhase.ResultReady||phase==WorkoutPhase.Settled;
        private static void ValidatePendingFields(WorkoutSnapshot state)
        {
            if(state==null||!Enum.IsDefined(typeof(WorkoutPhase),state.Phase))throw new ArgumentException("Invalid workout phase");
            if(!HasPendingSet(state.Phase))return;
            if(state.PendingRequest==null||state.PreSetBody==null||state.PreSetBody.Regions==null||state.PreSetBody.Exercises==null)throw new ArgumentException("Missing required active set payload");
            state.PendingRequest.Validate(); state.PreSetBody.Validate();
            if(HasPendingResult(state.Phase))
            {
                if(state.PendingResult==null)throw new ArgumentException("Missing required result payload");
                state.PendingResult.Validate(state.PendingRequest);
            }
        }
        /// <summary>
        /// Phase is the persisted union discriminator, not reference nullness. Unity inline
        /// serialization may create empty objects for absent references. Required payloads
        /// remain mandatory and are validated; they are never synthesized as successful sets.
        /// Call after decoding a present WorkoutSnapshot, before exposing it to consumers.
        /// </summary>
        public static void NormalizeAfterLoad(WorkoutSnapshot state)
        {
            if(state==null||string.IsNullOrWhiteSpace(state.WorkoutId)||!Enum.IsDefined(typeof(WorkoutPhase),state.Phase))throw new ArgumentException("Invalid workout identity/phase");
            if(!HasPendingSet(state.Phase))
            {
                state.PendingRequest=null; state.PendingResult=null; state.PreSetBody=null; state.PreSetTrainingRevision=0;
            }
            else if(!HasPendingResult(state.Phase))state.PendingResult=null;
            ValidatePendingFields(state);
        }
        private static DayTrainingQuota CopyQuota(DayTrainingQuota q)=>new DayTrainingQuota { ActorId=q.ActorId,Day=q.Day,PlanRevision=q.PlanRevision,RulesVersion=q.RulesVersion,StandardGameSeconds=q.StandardGameSeconds,Entries=q.Entries.Select(e=>new QuotaEntry { Plan=e.Plan.Clone(),FrozenTier=e.FrozenTier,RegionIds=(string[])e.RegionIds.Clone(),QuotaNumerators=(string[])e.QuotaNumerators.Clone(),QuotaDenominators=(string[])e.QuotaDenominators.Clone() }).ToArray() };
        public static WorkoutSnapshot Clone(WorkoutSnapshot s)
        {
            ValidatePendingFields(s);
            return new WorkoutSnapshot { WorkoutId=s.WorkoutId,Quota=CopyQuota(s.Quota),Phase=s.Phase,EntryIndex=s.EntryIndex,SetOrdinal=s.SetOrdinal,NextSetSequence=s.NextSetSequence,ElapsedGameSeconds=s.ElapsedGameSeconds,PendingRequest=HasPendingSet(s.Phase)?s.PendingRequest.Clone():null,PendingResult=HasPendingResult(s.Phase)?s.PendingResult.Clone():null,PreSetBody=HasPendingSet(s.Phase)?s.PreSetBody.Clone():null,PreSetTrainingRevision=HasPendingSet(s.Phase)?s.PreSetTrainingRevision:0,SettledSetIds=(string[])s.SettledSetIds.Clone(),PracticedExerciseIds=(string[])s.PracticedExerciseIds.Clone() };
        }
    }
}
