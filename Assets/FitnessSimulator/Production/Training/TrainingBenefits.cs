using System;
using System.Linq;

namespace FitnessSimulator.Game.Training
{
    [Serializable] public sealed class TeachingSourceSnapshot
    {
        public string EntitlementId,SourceActorId,SourceKind,KnowledgeRuleId,ActivatedAtCommitId;
        public int FirstEligibleWorkoutSetSequence;
        public long NominalTargetMicro;
        public TeachingSourceSnapshot Clone()=>(TeachingSourceSnapshot)MemberwiseClone();
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(EntitlementId)||string.IsNullOrWhiteSpace(SourceActorId)||string.IsNullOrWhiteSpace(SourceKind)||string.IsNullOrWhiteSpace(KnowledgeRuleId)||string.IsNullOrWhiteSpace(ActivatedAtCommitId)||FirstEligibleWorkoutSetSequence<1||(NominalTargetMicro<=0||NominalTargetMicro>TrainingContent.Current.Knowledge.MaxTeachingNominalMicro))throw new ArgumentException("Invalid teaching source");
        }
    }
    [Serializable] public sealed class SetBenefitsSnapshot
    {
        public int SchemaVersion=1;
        public string SnapshotId,RunId,ActorId,WorkoutId,RuleVersion;
        public int Day,WorkoutSetSequence,RiskReductionBasisPoints;
        public long RelationshipRevision;
        public string[] RiskSourceIds;
        public TeachingSourceSnapshot[] TeachingSources;
        public static SetBenefitsSnapshot None(string snapshotId,string runId,string actorId,int day,string workoutId,int sequence)
        {
            var s=new SetBenefitsSnapshot { SnapshotId=snapshotId,RunId=runId,ActorId=actorId,Day=day,WorkoutId=workoutId,WorkoutSetSequence=sequence,RuleVersion="ExplicitNoneV1",RiskSourceIds=Array.Empty<string>(),TeachingSources=Array.Empty<TeachingSourceSnapshot>() }; s.Validate(); return s;
        }
        public void Validate()
        {
            if(SchemaVersion!=1||string.IsNullOrWhiteSpace(SnapshotId)||string.IsNullOrWhiteSpace(RunId)||string.IsNullOrWhiteSpace(ActorId)||string.IsNullOrWhiteSpace(WorkoutId)||string.IsNullOrWhiteSpace(RuleVersion)||Day<1||WorkoutSetSequence<1||RelationshipRevision<0||RiskReductionBasisPoints<0||RiskReductionBasisPoints>10000||RiskSourceIds==null||TeachingSources==null)throw new ArgumentException("Invalid benefits snapshot");
            if(RiskSourceIds.Any(string.IsNullOrWhiteSpace)||RiskSourceIds.Distinct().Count()!=RiskSourceIds.Length||(RiskReductionBasisPoints>0&&RiskSourceIds.Length==0))throw new ArgumentException("Invalid risk sources");
            foreach(var source in TeachingSources) { if(source==null)throw new ArgumentException("Missing teaching source"); source.Validate(); }
            if(TeachingSources.Select(s=>s.EntitlementId).Distinct().Count()!=TeachingSources.Length)throw new ArgumentException("Duplicate teaching entitlement");
        }
        public SetBenefitsSnapshot Clone()
        {
            Validate(); return new SetBenefitsSnapshot { SchemaVersion=SchemaVersion,SnapshotId=SnapshotId,RunId=RunId,ActorId=ActorId,WorkoutId=WorkoutId,RuleVersion=RuleVersion,Day=Day,WorkoutSetSequence=WorkoutSetSequence,RiskReductionBasisPoints=RiskReductionBasisPoints,RelationshipRevision=RelationshipRevision,RiskSourceIds=(string[])RiskSourceIds.Clone(),TeachingSources=TeachingSources.Select(s=>s.Clone()).ToArray() };
        }
    }
    [Serializable] public sealed class TeachingClaim
    {
        public string RunId,ActorId,WorkoutId,SetId,ExerciseId,SnapshotId,ClaimKey;
        public int Day,WorkoutSetSequence,EffectiveReps;
        public long SnapshotRelationshipRevision,EligibleTargetMicro;
        public string[] EligibleEntitlementIds;
        public static string Key(string run,string actor,string workout,string exercise,string set)=>string.Join("/",new[]{run,actor,workout,exercise,set,"TeachingDelta"}.Select(Uri.EscapeDataString));
    }
    [Serializable] public sealed class TeachingResolution
    {
        public string RunId,ActorId,WorkoutId,SetId,ExerciseId,SnapshotId,ClaimKey;
        public int Day,WorkoutSetSequence;
        public long AwardLedgerExpectedRevision,PreviousNominalMicro,TargetNominalMicro,DeltaMicro;
        public TeachingResolution Clone()=>(TeachingResolution)MemberwiseClone();
        public void ValidateAgainst(TeachingClaim claim)
        {
            if(claim==null||RunId!=claim.RunId||ActorId!=claim.ActorId||WorkoutId!=claim.WorkoutId||Day!=claim.Day||SetId!=claim.SetId||ExerciseId!=claim.ExerciseId||SnapshotId!=claim.SnapshotId||WorkoutSetSequence!=claim.WorkoutSetSequence||ClaimKey!=claim.ClaimKey||AwardLedgerExpectedRevision<0||PreviousNominalMicro<0||PreviousNominalMicro>TrainingContent.Current.Knowledge.MaxTeachingNominalMicro||TargetNominalMicro!=claim.EligibleTargetMicro||DeltaMicro!=Math.Max(0,TargetNominalMicro-PreviousNominalMicro))throw new ArgumentException("Teaching resolution does not match frozen claim");
            if(claim.EffectiveReps==0&&DeltaMicro!=0)throw new ArgumentException("Zero completion cannot claim teaching");
        }
    }
}
