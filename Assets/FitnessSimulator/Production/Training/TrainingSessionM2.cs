using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Body;

namespace FitnessSimulator.Game.Training
{
    [Serializable] public sealed class M2WorkoutContext
    {
        public int SchemaVersion=2;
        public string RunId,ActorId,WorkoutId,ScheduleRevision,ArrivalAnchor;
        public int Day;
        public WorkoutSnapshot Workout;
        public bool HasSetBenefits;
        public SetBenefitsSnapshot Benefits;
        public bool HasTeachingResolution;
        public TeachingResolution AppliedTeachingResolution;
    }
    public sealed class M2TrainingSettlement
    {
        public M2WorkoutContext Context { get; internal set; }
        public TrainingSettlement Settlement { get; internal set; }
    }
    public sealed class M2TrainingRest
    {
        public M2WorkoutContext Context { get; internal set; }
        public BodyState Body { get; internal set; }
    }
    /// <summary>M2-only pure facade. M1 DTO and entry points intentionally remain unchanged.</summary>
    public static class TrainingSessionM2
    {
        private static bool Active(WorkoutPhase phase)=>phase==WorkoutPhase.Playing||phase==WorkoutPhase.ResultReady||phase==WorkoutPhase.Settled;
        public static M2WorkoutContext Create(string runId,string actorId,int day,string workoutId,DayTrainingQuota quota,TrainingDayClaim claimedDay)
        {
            claimedDay.Validate();
            if(claimedDay.Status!=TrainingDayClaimStatus.Claimed||!claimedDay.HasWorkoutId||claimedDay.RunId!=runId||claimedDay.ActorId!=actorId||claimedDay.Day!=day||claimedDay.WorkoutId!=workoutId||quota.ActorId!=actorId||quota.Day!=day)throw new ArgumentException("M2 creation requires matching claimed day/plan");
            return new M2WorkoutContext { RunId=runId,ActorId=actorId,Day=day,WorkoutId=workoutId,ScheduleRevision=claimedDay.ScheduleRevision,ArrivalAnchor=claimedDay.ArrivalAnchor,Workout=TrainingSession.Begin(workoutId,quota) };
        }
        public static M2WorkoutContext NormalizeAfterLoad(M2WorkoutContext state)=>Clone(state);
        private static void NormalizeOwnedCopy(M2WorkoutContext state)
        {
            if(state==null||state.SchemaVersion!=2||string.IsNullOrWhiteSpace(state.RunId)||string.IsNullOrWhiteSpace(state.ActorId)||string.IsNullOrWhiteSpace(state.WorkoutId)||string.IsNullOrWhiteSpace(state.ScheduleRevision)||string.IsNullOrWhiteSpace(state.ArrivalAnchor)||state.Day<1||state.Workout==null)throw new ArgumentException("Invalid M2 workout envelope");
            TrainingSession.NormalizeAfterLoad(state.Workout);
            if(state.Workout.WorkoutId!=state.WorkoutId||state.Workout.Quota==null||state.Workout.Quota.ActorId!=state.ActorId||state.Workout.Quota.Day!=state.Day)throw new ArgumentException("M2 workout identity mismatch");
            if(Active(state.Workout.Phase))
            {
                if(!state.HasSetBenefits||state.Benefits==null)throw new ArgumentException("Missing M2 frozen benefits");
                ValidateBenefits(state,state.Benefits,state.Workout.PendingRequest.WorkoutSetSequence);
            }
            else { state.HasSetBenefits=false; state.Benefits=null; }
            if(state.Workout.Phase==WorkoutPhase.Settled)
            {
                if(!state.HasTeachingResolution||state.AppliedTeachingResolution==null)throw new ArgumentException("Missing committed teaching resolution");
                state.AppliedTeachingResolution.ValidateAgainst(BuildClaim(state));
            }
            else { state.HasTeachingResolution=false; state.AppliedTeachingResolution=null; }
        }
        private static void ValidateBenefits(M2WorkoutContext state,SetBenefitsSnapshot benefits,int sequence)
        {
            benefits.Validate();
            if(benefits.RunId!=state.RunId||benefits.ActorId!=state.ActorId||benefits.Day!=state.Day||benefits.WorkoutId!=state.WorkoutId||benefits.WorkoutSetSequence!=sequence)throw new ArgumentException("Benefits not for this run/actor/day/workout/set sequence");
        }
        public static M2WorkoutContext Clone(M2WorkoutContext state)
        {
            if(state==null||state.Workout==null)throw new ArgumentException("Missing M2 context");
            var copy=new M2WorkoutContext { SchemaVersion=state.SchemaVersion,RunId=state.RunId,ActorId=state.ActorId,Day=state.Day,WorkoutId=state.WorkoutId,ScheduleRevision=state.ScheduleRevision,ArrivalAnchor=state.ArrivalAnchor,Workout=TrainingSession.Clone(state.Workout),HasSetBenefits=state.HasSetBenefits,HasTeachingResolution=state.HasTeachingResolution };
            if(Active(copy.Workout.Phase)&&state.HasSetBenefits)copy.Benefits=state.Benefits?.Clone();
            if(copy.Workout.Phase==WorkoutPhase.Settled&&state.HasTeachingResolution)copy.AppliedTeachingResolution=state.AppliedTeachingResolution?.Clone();
            NormalizeOwnedCopy(copy); return copy;
        }
        public static M2WorkoutContext Restore(M2WorkoutContext state)
        {
            var next=Clone(state); next.Workout=TrainingSession.Restore(next.Workout); return next;
        }
        public static M2WorkoutContext BeginSet(M2WorkoutContext state,BodyState body,TrainingProgress progress,TrainingSetType type,TrainingProducer producer,SetBenefitsSnapshot benefits)
        {
            var next=Clone(state);
            if(benefits==null)throw new ArgumentException("Explicit benefits or explicit None required");
            ValidateBenefits(next,benefits,next.Workout.NextSetSequence);
            next.Workout=TrainingSession.BeginSet(next.Workout,body,progress,type,producer);
            next.HasSetBenefits=true; next.Benefits=benefits.Clone(); return next;
        }
        public static M2WorkoutContext CompleteResult(M2WorkoutContext state,TrainingSetResult result)
        {
            var next=Clone(state); next.Workout=TrainingSession.CompleteResult(next.Workout,result); return next;
        }
        public static M2WorkoutContext CompleteAutomatic(M2WorkoutContext state)
        {
            var next=Clone(state); next.Workout=TrainingSession.CompleteAutomatic(next.Workout); return next;
        }
        private static TeachingClaim BuildClaim(M2WorkoutContext state)
        {
            var request=state.Workout.PendingRequest; var result=state.Workout.PendingResult; var snapshot=state.Benefits;
            var eligible=result.EffectiveReps>0?snapshot.TeachingSources.Where(s=>request.WorkoutSetSequence>=s.FirstEligibleWorkoutSetSequence).ToArray():Array.Empty<TeachingSourceSnapshot>();
            return new TeachingClaim { RunId=state.RunId,ActorId=state.ActorId,Day=state.Day,WorkoutId=state.WorkoutId,SetId=request.SetId,ExerciseId=request.ExerciseId,WorkoutSetSequence=request.WorkoutSetSequence,EffectiveReps=result.EffectiveReps,SnapshotId=snapshot.SnapshotId,SnapshotRelationshipRevision=snapshot.RelationshipRevision,EligibleTargetMicro=eligible.Length==0?0:eligible.Max(s=>s.NominalTargetMicro),EligibleEntitlementIds=eligible.Select(s=>s.EntitlementId).OrderBy(id=>id,StringComparer.Ordinal).ToArray(),ClaimKey=TeachingClaim.Key(state.RunId,state.ActorId,state.WorkoutId,request.ExerciseId,request.SetId) };
        }
        public static TeachingClaim PrepareTeachingClaim(M2WorkoutContext state)
        {
            var current=Clone(state);
            if(current.Workout.Phase!=WorkoutPhase.ResultReady)throw new InvalidOperationException("Teaching claim requires saved result");
            return BuildClaim(current);
        }
        public static M2TrainingSettlement Settle(M2WorkoutContext state,BodyState body,TrainingProgress progress,IReadOnlyDictionary<string,uint> injuryRolls,TeachingResolution resolution)
        {
            var next=Clone(state);
            if(next.Workout.Phase!=WorkoutPhase.ResultReady&&next.Workout.Phase!=WorkoutPhase.Settled)throw new InvalidOperationException("No completed set");
            if(resolution==null)throw new ArgumentException("Explicit teaching resolution required");
            resolution.ValidateAgainst(BuildClaim(next));
            if(next.Workout.Phase==WorkoutPhase.Settled&&!SameResolution(next.AppliedTeachingResolution,resolution))throw new InvalidOperationException("Conflicting replayed teaching resolution");
            var result=TrainingSession.Settle(next.Workout,body,progress,next.Day,injuryRolls,resolution.DeltaMicro,next.Benefits.RiskReductionBasisPoints);
            next.Workout=result.Workout; next.HasTeachingResolution=true; next.AppliedTeachingResolution=resolution.Clone();
            return new M2TrainingSettlement { Context=next,Settlement=result };
        }
        private static bool SameResolution(TeachingResolution a,TeachingResolution b)=>a.RunId==b.RunId&&a.ActorId==b.ActorId&&a.Day==b.Day&&a.WorkoutId==b.WorkoutId&&a.SetId==b.SetId&&a.ExerciseId==b.ExerciseId&&a.SnapshotId==b.SnapshotId&&a.WorkoutSetSequence==b.WorkoutSetSequence&&a.ClaimKey==b.ClaimKey&&a.AwardLedgerExpectedRevision==b.AwardLedgerExpectedRevision&&a.PreviousNominalMicro==b.PreviousNominalMicro&&a.TargetNominalMicro==b.TargetNominalMicro&&a.DeltaMicro==b.DeltaMicro;
        private static M2WorkoutContext FinishTransition(M2WorkoutContext state,WorkoutSnapshot workout)
        {
            state.Workout=workout; NormalizeOwnedCopy(state); return state;
        }
        public static M2WorkoutContext ContinueAfterFollowers(M2WorkoutContext state,BodyState body)
        {
            var next=Clone(state); return FinishTransition(next,TrainingSession.ContinueAfterFollowers(next.Workout,body));
        }
        public static M2WorkoutContext EndCurrentAction(M2WorkoutContext state,BodyState body)
        {
            var next=Clone(state); return FinishTransition(next,TrainingSession.EndCurrentAction(next.Workout,body));
        }
        public static M2WorkoutContext SkipUnavailableActions(M2WorkoutContext state,BodyState body)
        {
            var next=Clone(state); return FinishTransition(next,TrainingSession.SkipUnavailableActions(next.Workout,body));
        }
        public static M2WorkoutContext EndWorkout(M2WorkoutContext state)
        {
            var next=Clone(state); return FinishTransition(next,TrainingSession.EndWorkout(next.Workout));
        }
        public static M2TrainingRest Rest(M2WorkoutContext state,BodyState body,int seconds)
        {
            var next=Clone(state); var rest=TrainingSession.Rest(next.Workout,body,seconds); next.Workout=rest.Workout;
            return new M2TrainingRest { Context=next,Body=rest.Body };
        }
    }
}

