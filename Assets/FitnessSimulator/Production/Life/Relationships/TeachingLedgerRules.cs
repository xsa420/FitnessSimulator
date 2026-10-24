using System;
using System.Linq;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Life.Relationships
{
    [Serializable] public sealed class TeachingAward
    {
        public string ExerciseId;
        public long NominalMicro;
    }
    [Serializable] public sealed class TeachingLedger
    {
        public string RunId, ActorId, WorkoutId;
        public int Day;
        public long Revision;
        public TeachingAward[] Awards = Array.Empty<TeachingAward>();
    }
    public sealed class TeachingPreparation
    {
        public TeachingLedger Ledger;
        public TeachingResolution Resolution;
    }
    public static class TeachingLedgerRules
    {
        public static TeachingLedger Initialize(string runId, string actorId, int day, string workoutId)
        {
            var state = new TeachingLedger { RunId=runId, ActorId=actorId, Day=day, WorkoutId=workoutId };
            return NormalizeAfterLoad(state);
        }
        public static TeachingLedger NormalizeAfterLoad(TeachingLedger state)
        {
            if(state==null || string.IsNullOrWhiteSpace(state.RunId) || string.IsNullOrWhiteSpace(state.ActorId) || string.IsNullOrWhiteSpace(state.WorkoutId) || state.Day<1 || state.Revision<0 || state.Awards==null)
                throw new ArgumentException("Invalid teaching ledger");
            if(state.Awards.Any(a=>a==null || string.IsNullOrWhiteSpace(a.ExerciseId) || (a.NominalMicro<=0 || a.NominalMicro>3000000)) || state.Awards.Select(a=>a.ExerciseId).Distinct().Count()!=state.Awards.Length)
                throw new ArgumentException("Invalid teaching awards");
            return new TeachingLedger { RunId=state.RunId, ActorId=state.ActorId, WorkoutId=state.WorkoutId, Day=state.Day, Revision=state.Revision, Awards=state.Awards.Select(a=>new TeachingAward { ExerciseId=a.ExerciseId, NominalMicro=a.NominalMicro }).ToArray() };
        }
        // The caller obtains the claim from the training kernel and commits this candidate
        // with its body result and semantic receipt. Never accept a UI-authored claim.
        public static TeachingPreparation Prepare(TeachingLedger state, TeachingClaim claim)
        {
            var next=NormalizeAfterLoad(state);
            if(claim==null || claim.RunId!=state.RunId || claim.ActorId!=state.ActorId || claim.WorkoutId!=state.WorkoutId || claim.Day!=state.Day || string.IsNullOrWhiteSpace(claim.SetId) || string.IsNullOrWhiteSpace(claim.ExerciseId) || string.IsNullOrWhiteSpace(claim.SnapshotId) || claim.WorkoutSetSequence<1 || claim.EffectiveReps<0 || claim.SnapshotRelationshipRevision<0 || claim.EligibleEntitlementIds==null)
                throw new ArgumentException("Teaching claim scope mismatch");
            if(claim.ClaimKey!=TeachingClaim.Key(claim.RunId,claim.ActorId,claim.WorkoutId,claim.ExerciseId,claim.SetId) || (claim.EligibleTargetMicro<0 || claim.EligibleTargetMicro>3000000) || (claim.EffectiveReps==0 && claim.EligibleTargetMicro!=0) || claim.EligibleEntitlementIds.Any(string.IsNullOrWhiteSpace) || claim.EligibleEntitlementIds.Distinct().Count()!=claim.EligibleEntitlementIds.Length || ((claim.EligibleTargetMicro>0)!=(claim.EligibleEntitlementIds.Length>0)))
                throw new ArgumentException("Invalid teaching claim");
            var award=next.Awards.SingleOrDefault(a=>a.ExerciseId==claim.ExerciseId);
            long previous=award==null?0:award.NominalMicro;
            var resolution=new TeachingResolution { RunId=claim.RunId, ActorId=claim.ActorId, WorkoutId=claim.WorkoutId, Day=claim.Day, SetId=claim.SetId, ExerciseId=claim.ExerciseId, SnapshotId=claim.SnapshotId, ClaimKey=claim.ClaimKey, WorkoutSetSequence=claim.WorkoutSetSequence, AwardLedgerExpectedRevision=state.Revision, PreviousNominalMicro=previous, TargetNominalMicro=claim.EligibleTargetMicro, DeltaMicro=Math.Max(0,claim.EligibleTargetMicro-previous) };
            resolution.ValidateAgainst(claim);
            if(resolution.DeltaMicro>0)
            {
                if(award==null) next.Awards=next.Awards.Concat(new[]{new TeachingAward { ExerciseId=claim.ExerciseId, NominalMicro=claim.EligibleTargetMicro }}).ToArray();
                else award.NominalMicro=claim.EligibleTargetMicro;
                next.Revision=checked(next.Revision+1);
            }
            return new TeachingPreparation { Ledger=next, Resolution=resolution };
        }
    }
}
