using System;
using System.Linq;
using System.Numerics;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Body;

namespace FitnessSimulator.Game.Training
{
    public enum TrainingSetType { Volume,StrengthChallenge }
    public enum TrainingProducer { PlayerTouch=0,NpcPolicy=1,AutoCapacity=2,ScriptReplay=3,TestInjected=4,TrustedAutomationInput=5 }
    [Serializable] public sealed class TrainingSetRequest
    {
        public string SetId,ActorId,ExerciseId,PlanEntryId;
        public string RulesVersion=ExerciseCatalog.Version;
        public int Tier,TargetReps,SetOrdinal,WorkoutSetSequence;
        public TrainingSetType SetType;
        public TrainingProducer Producer;
        public long DifficultyMicro;
        public TrainingSetRequest Clone() => (TrainingSetRequest)MemberwiseClone();
        public void Validate()
        {
            ExerciseCatalog.Get(ExerciseId); ExerciseCatalog.Load(Tier);
            if(string.IsNullOrWhiteSpace(SetId)||string.IsNullOrWhiteSpace(ActorId)||TargetReps<=0||TargetReps>TrainingContent.Current.MaxTargetReps||DifficultyMicro<0||DifficultyMicro>1000000||RulesVersion!=ExerciseCatalog.Version)throw new ArgumentException("Invalid set request");
            if(!Enum.IsDefined(typeof(TrainingSetType),SetType)||!Enum.IsDefined(typeof(TrainingProducer),Producer))throw new ArgumentException("Invalid source/type");
            if(Producer==TrainingProducer.AutoCapacity&&SetType!=TrainingSetType.Volume)throw new InvalidOperationException("Challenges are manual");
        }
    }
    [Serializable] public sealed class TrainingSetResult
    {
        public string SetId,RulesVersion;
        public TrainingProducer Producer;
        public int EffectiveReps,FailedAttempts;
        public bool Success;
        public string TerminalReason;
        public string InputDigest;
        public int InputEventCount;
        public uint MinigameSeed;
        public int Attempts => EffectiveReps+FailedAttempts;
        public TrainingSetResult Clone() => (TrainingSetResult)MemberwiseClone();
        public void Validate(TrainingSetRequest request)
        {
            request.Validate();
            if(SetId!=request.SetId||RulesVersion!=request.RulesVersion||Producer!=request.Producer||EffectiveReps<0||EffectiveReps>request.TargetReps||FailedAttempts<0||FailedAttempts>3)throw new ArgumentException("Result context mismatch");
            if(Success?(EffectiveReps!=request.TargetReps||FailedAttempts>=3):(EffectiveReps>=request.TargetReps||FailedAttempts!=3))throw new ArgumentException("Invalid terminal result");
            if(Producer==TrainingProducer.AutoCapacity&&(!Success||FailedAttempts!=0))throw new ArgumentException("Invalid auto result");
        }
    }
    [Serializable] public sealed class TrainingPlanEntry
    {
        public string EntryId,ExerciseId;
        public int Sets,Reps;
        public TrainingPlanEntry Clone() => (TrainingPlanEntry)MemberwiseClone();
    }
    [Serializable] public sealed class ExerciseTrainingProgress
    {
        public string ExerciseId;
        public int HighestUnlockedTier=1;
        public bool[] AutomaticTiers=new bool[4];
        public ExerciseTrainingProgress Clone() => new ExerciseTrainingProgress { ExerciseId=ExerciseId,HighestUnlockedTier=HighestUnlockedTier,AutomaticTiers=(bool[])AutomaticTiers.Clone() };
    }
    [Serializable] public sealed class TrainingProgress
    {
        public string ActorId;
        public long Revision;
        public ExerciseTrainingProgress[] Exercises;
        public static TrainingProgress CreateInitial(string actorId) => new TrainingProgress { ActorId=actorId,Exercises=ExerciseCatalog.All.Select(d=>new ExerciseTrainingProgress { ExerciseId=d.Id }).ToArray() };
        public static TrainingProgress CreateFromDefinition(string actorId,TrainingInitialDefinition definition)
        {
            if(definition?.Exercises==null)throw new ArgumentException("Complete training initial definition required");
            var result=new TrainingProgress { ActorId=actorId,Exercises=definition.Exercises.Select(e=>new ExerciseTrainingProgress { ExerciseId=e.ExerciseId,HighestUnlockedTier=checked((int)BodyContent.Required(e.HighestUnlockedTier,e.ExerciseId+".HighestUnlockedTier")),AutomaticTiers=e.AutomaticTiers==null?null:(bool[])e.AutomaticTiers.Clone() }).ToArray() };result.Validate();return result;
        }
        public ExerciseTrainingProgress Exercise(string id) => Exercises.Single(e=>e.ExerciseId==id);
        public TrainingProgress Clone() => new TrainingProgress { ActorId=ActorId,Revision=Revision,Exercises=Exercises.Select(e=>e.Clone()).ToArray() };
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(ActorId)||Revision<0||Exercises==null||Exercises.Length!=8)throw new ArgumentException("Invalid training progress");
            foreach(var d in ExerciseCatalog.All)
            {
                var p=Exercise(d.Id);
                if(p.HighestUnlockedTier<1||p.HighestUnlockedTier>4||p.AutomaticTiers==null||p.AutomaticTiers.Length!=4)throw new ArgumentException("Invalid tier progress");
                for(int i=p.HighestUnlockedTier;i<4;i++)if(p.AutomaticTiers[i])throw new ArgumentException("Automatic tier not unlocked");
            }
        }
    }
    [Serializable] public sealed class QuotaEntry
    {
        public TrainingPlanEntry Plan;
        public int FrozenTier;
        public string[] RegionIds;
        public string[] QuotaNumerators,QuotaDenominators;
        public Rational Quota(string regionId)
        {
            int i=Array.IndexOf(RegionIds,regionId);
            if(i<0)throw new ArgumentException("Region outside quota");
            return new Rational(BigInteger.Parse(QuotaNumerators[i]),BigInteger.Parse(QuotaDenominators[i]));
        }
    }
    [Serializable] public sealed class DayTrainingQuota
    {
        public string ActorId,PlanRevision,RulesVersion;
        public int Day,StandardGameSeconds;
        public QuotaEntry[] Entries;
        public QuotaEntry Entry(string id) => Entries.Single(e=>e.Plan.EntryId==id);
        public void Validate()
        {
            if(RulesVersion!=ExerciseCatalog.Version||Entries==null||Entries.Length==0||Entries.Length>8)throw new ArgumentException("Invalid quota version/entries");
            var referenceProgress=TrainingProgress.CreateInitial(ActorId);
            foreach(var entry in Entries)referenceProgress.Exercise(entry.Plan.ExerciseId).HighestUnlockedTier=entry.FrozenTier;
            var reference=Freeze(ActorId,Day,PlanRevision,Entries.Select(e=>e.Plan).ToArray(),referenceProgress);
            if(StandardGameSeconds!=reference.StandardGameSeconds)throw new ArgumentException("Invalid standard time");
            for(int i=0;i<Entries.Length;i++)
            {
                var actual=Entries[i]; var expected=reference.Entries[i];
                if(actual.RegionIds==null||actual.QuotaNumerators==null||actual.QuotaDenominators==null||!actual.RegionIds.SequenceEqual(expected.RegionIds)||actual.QuotaNumerators.Length!=expected.RegionIds.Length||actual.QuotaDenominators.Length!=expected.RegionIds.Length)throw new ArgumentException("Invalid quota shape");
                foreach(var region in expected.RegionIds)if(actual.Quota(region).CompareTo(expected.Quota(region))!=0)throw new ArgumentException("Invalid quota value");
            }
        }
        public static DayTrainingQuota Freeze(string actorId,int day,string planRevision,TrainingPlanEntry[] plan,TrainingProgress progress)
        {
            if(progress==null)throw new ArgumentNullException(nameof(progress));
            progress.Validate();
            if(day<1||string.IsNullOrWhiteSpace(planRevision)||progress.ActorId!=actorId||plan==null||plan.Length==0||plan.Length>8)throw new ArgumentException("Invalid day plan");
            if(plan.Select(e=>e.EntryId).Distinct().Count()!=plan.Length||plan.Select(e=>e.ExerciseId).Distinct().Count()!=plan.Length)throw new ArgumentException("Duplicate plan entry/exercise");
            var totals=RegionIds.All.ToDictionary(id=>id,id=>(Rational)0); int sets=0,seconds=0;
            foreach(var p in plan)
            {
                var d=ExerciseCatalog.Get(p.ExerciseId);
                if(string.IsNullOrWhiteSpace(p.EntryId)||!Enumerable.Range(0,3).Any(i=>p.Sets==d.Sets[i]&&p.Reps==d.Reps[i]))throw new ArgumentException("Unsupported workload");
                var h=ExerciseCatalog.Load(progress.Exercise(p.ExerciseId).HighestUnlockedTier);
                for(int i=0;i<d.TargetRegionIds.Count;i++)totals[d.TargetRegionIds[i]]+=p.Sets*p.Reps*h*new Rational(d.WeightBasisPoints[i],10000);
                sets+=p.Sets; seconds+=p.Sets*p.Reps*d.SecondsPerRep;
            }
            var entries=plan.Select(p=> {
                var d=ExerciseCatalog.Get(p.ExerciseId); int tier=progress.Exercise(p.ExerciseId).HighestUnlockedTier;
                var q=d.TargetRegionIds.Select((id,i)=>NumericKernel.FromMicro(TrainingContent.Current.Fatigue.DailyQuotaMicro)*p.Reps*ExerciseCatalog.Load(tier)*new Rational(d.WeightBasisPoints[i],10000)/totals[id]).ToArray();
                return new QuotaEntry { Plan=p.Clone(),FrozenTier=tier,RegionIds=d.TargetRegionIds.ToArray(),QuotaNumerators=q.Select(x=>x.Numerator.ToString()).ToArray(),QuotaDenominators=q.Select(x=>x.Denominator.ToString()).ToArray() };
            }).ToArray();
            return new DayTrainingQuota { ActorId=actorId,Day=day,PlanRevision=planRevision,RulesVersion=ExerciseCatalog.Version,Entries=entries,StandardGameSeconds=seconds+TrainingContent.Current.Fatigue.StandardRestSeconds*(sets-1) };
        }
    }
}
