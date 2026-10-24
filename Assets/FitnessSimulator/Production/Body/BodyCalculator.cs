using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Body
{
    public sealed class BodySetCalculation
    {
        public BodyState NextState { get; internal set; }
        public int GameSeconds { get; internal set; }
        public string[] InjuredRegionIds { get; internal set; }
    }
    public sealed class BodyRestCalculation
    {
        public BodyState NextState { get; internal set; }
        public int GameSeconds { get; internal set; }
    }
    public static class BodyCalculator
    {
        private static Rational V(long micro)=>NumericKernel.FromMicro(micro);
        private static long ClampMicro(Rational value,int min,int max)=>NumericKernel.ToMicro(Rational.Clamp(value,min,max));
        private static Rational TemporaryIncrease(TrainingSetRequest request,TrainingSetResult result,QuotaEntry quota,string regionId)
        {
            var q=quota.Quota(regionId);
            var gain=q*new Rational(result.EffectiveReps,request.TargetReps)*ExerciseCatalog.Load(request.Tier)/ExerciseCatalog.Load(quota.FrozenTier);
            if(request.SetType==TrainingSetType.StrengthChallenge) { gain*=V(TrainingContent.Current.Fatigue.ChallengeMultiplierMicro); if(!result.Success)gain+=q*V(TrainingContent.Current.Fatigue.FailureMultiplierMicro); }
            return gain;
        }
        public static string[] RequiredInjuryRollRegions(BodyState state,TrainingSetRequest request,TrainingSetResult result,QuotaEntry quota)
        {
            state.Validate(); result.Validate(request);
            if(state.ActorId!=request.ActorId||quota.Plan.EntryId!=request.PlanEntryId||quota.Plan.ExerciseId!=request.ExerciseId||quota.Plan.Reps!=request.TargetReps)throw new ArgumentException("Injury query context mismatch");
            return ExerciseCatalog.Get(request.ExerciseId).TargetRegionIds.Where(id=> {
                var t=state.Region(id).ExactTemporary+TemporaryIncrease(request,result,quota,id);
                return t>V(TrainingContent.Current.Fatigue.SafeMicro)&&t<V(TrainingContent.Current.Fatigue.MandatoryInjuryMicro);
            }).ToArray();
        }
        public static Rational BodyFatPressureReduction(long fatMicro)
        {
            return BodyContent.Evaluate(BodyContent.Current.PressureCurve,fatMicro);
        }
        public static long QuoteDifficulty(BodyState state,string exerciseId,int tier)
        {
            state.Validate(); var d=ExerciseCatalog.Get(exerciseId); var q=d.TargetRegionIds.Aggregate((Rational)0,(sum,id)=>sum+V(state.Region(id).StrengthMicro));
            Rational intensity=tier==4?1:1-V(TrainingContent.Current.Difficulty.IntensityReductionMicro)*Rational.Clamp((q-d.Threshold(tier))/(d.Threshold(tier+1)-d.Threshold(tier)),0,1);
            var x=V(state.Exercise(exerciseId).ProficiencyMicro);
            var difficulty=TrainingContent.Current.Difficulty; Rational reduction=0; for(int i=0;i<difficulty.ProficiencyThresholdsMicro.Length;i++)if(x>=V(difficulty.ProficiencyThresholdsMicro[i]))reduction=V(difficulty.ProficiencyReductionMicro[i]);
            var t=d.TargetRegionIds.Select(id=>state.Region(id).ExactTemporary).Aggregate((Rational)0,(a,b)=>a>b?a:b);
            return NumericKernel.ToMicro(Rational.Clamp(V(difficulty.IntensityWeightMicro)*intensity+V(difficulty.TemporaryWeightMicro)*t/V(TrainingContent.Current.Fatigue.MandatoryInjuryMicro)-reduction,0,1));
        }
        public static BodySetCalculation CalculateSet(BodyState state,TrainingSetRequest request,TrainingSetResult result,QuotaEntry quota,int day,IReadOnlyDictionary<string,uint> injuryRolls,long teachingKnowledgeMicro=0,int injuryReductionBasisPoints=0)
        {
            state.Validate(); result.Validate(request);
            if(result.EffectiveReps==0&&teachingKnowledgeMicro!=0)throw new ArgumentException("Zero effective reps cannot claim teaching");
            if(state.ActorId!=request.ActorId||day<1||quota.Plan.EntryId!=request.PlanEntryId||quota.Plan.ExerciseId!=request.ExerciseId||quota.Plan.Reps!=request.TargetReps||teachingKnowledgeMicro<0||injuryReductionBasisPoints<0||injuryReductionBasisPoints>10000)throw new ArgumentException("Set calculation context mismatch");
            var d=ExerciseCatalog.Get(request.ExerciseId);
            if(d.TargetRegionIds.Any(id=>state.Region(id).IsInjured))throw new InvalidOperationException("Injured target");
            var next=state.Clone(); var r=new Rational(result.EffectiveReps,request.TargetReps); var load=ExerciseCatalog.Load(request.Tier);
            var pool=ExerciseCatalog.StrengthPool(request.Tier)*r;
            var growth=BodyContent.Current.Growth; var b=V(state.StressMicro); int stressIndex=0;while(stressIndex<growth.StressThresholdsMicro.Length&&state.StressMicro>=growth.StressThresholdsMicro[stressIndex])stressIndex++;var stressRetention=V(growth.StressRetentionMicro[stressIndex]);
            var oldExercise=state.Exercise(request.ExerciseId); var newExercise=next.Exercise(request.ExerciseId);
            var k=V(oldExercise.KnowledgeMicro); var x=V(oldExercise.ProficiencyMicro); int capIndex=0;while(capIndex<growth.KnowledgeThresholdsMicro.Length&&oldExercise.KnowledgeMicro>=growth.KnowledgeThresholdsMicro[capIndex])capIndex++;var cap=V(growth.ProficiencyCapsMicro[capIndex]);
            var room=cap-x; if(room<0)room=0;
            var gain=ExerciseCatalog.ProficiencyPool(request.Tier)*r*(V(growth.KnowledgeMultiplierBaseMicro)+k/V(growth.KnowledgeMultiplierDenominatorMicro)); if(gain>room)gain=room;
            newExercise.ProficiencyMicro=NumericKernel.ToMicro(BodyContent.Clamp(x+gain,"proficiency"));
            var knowledge=V(teachingKnowledgeMicro)+(request.SetType==TrainingSetType.StrengthChallenge&&!result.Success&&result.EffectiveReps>0?V(TrainingContent.Current.Knowledge.FailedChallengeMicro):(Rational)0);
            newExercise.KnowledgeMicro=NumericKernel.ToMicro(BodyContent.Clamp(k+knowledge,"knowledge"));
            next.StressMicro=NumericKernel.ToMicro(BodyContent.Clamp(b+d.StressPool*load*r*(1-BodyFatPressureReduction(state.BodyFatMicro)),"stress"));
            next.BodyFatMicro=NumericKernel.ToMicro(BodyContent.Clamp(V(state.BodyFatMicro)-d.FatLossPerRep*result.EffectiveReps,"body_fat"));
            var injuries=new List<string>();
            for(int i=0;i<d.TargetRegionIds.Count;i++)
            {
                string id=d.TargetRegionIds[i]; var old=state.Region(id); var target=next.Region(id); var w=new Rational(d.WeightBasisPoints[i],10000);
                var s=V(old.StrengthMicro); var f=V(old.LocalFatigueMicro); var free=V(growth.FatigueFreeUntilMicro);var retention=f<=free?(Rational)1:1-V(growth.FatigueLossAtMaximumMicro)*(f-free)/(V(BodyContent.Attribute("local_fatigue").MaxMicro)-free);
                target.StrengthMicro=NumericKernel.ToMicro(BodyContent.Clamp(s+pool*w*(1-V(growth.StrengthDecayPerPointMicro)*s)*retention*stressRetention,"strength"));
                target.LocalFatigueMicro=NumericKernel.ToMicro(BodyContent.Clamp(f+d.FatiguePool*load*r*w,"local_fatigue"));
                var tGain=TemporaryIncrease(request,result,quota,id);
                var t=old.ExactTemporary+tGain; uint roll=0;
                if(t>V(TrainingContent.Current.Fatigue.SafeMicro)&&t<V(TrainingContent.Current.Fatigue.MandatoryInjuryMicro)&&(injuryRolls==null||!injuryRolls.TryGetValue(id,out roll)))throw new ArgumentException("Missing injury roll for "+id);
                if(NumericKernel.InjuryOccurs(t,injuryReductionBasisPoints,roll)) { target.InjuredDay=day; target.ExpiresAtStartOfDay=checked(day+TrainingContent.Current.Fatigue.InjuryFullDays+1); injuries.Add(id); }
                target.SetTemporary(t);
            }
            next.Revision=checked(state.Revision+1); next.Validate();
            return new BodySetCalculation { NextState=next,GameSeconds=result.EffectiveReps*d.SecondsPerRep,InjuredRegionIds=injuries.ToArray() };
        }
        private static BodyState Modify(BodyState state,Action<BodyState> change)
        {
            state.Validate(); var next=state.Clone(); change(next); next.Revision=checked(state.Revision+1); next.Validate(); return next;
        }
        public static BodyState Rest(BodyState state,int seconds)
        {
            if(!TrainingContent.Current.Fatigue.RestSeconds.Contains(seconds))throw new ArgumentException("Unknown rest duration");
            return Modify(state,n=> { foreach(var region in n.Regions)region.SetTemporary(region.ExactTemporary-seconds*V(TrainingContent.Current.Fatigue.RestRecoveryPerSecondMicro)); });
        }
        public static BodyRestCalculation CalculateRest(BodyState state,int seconds)=>new BodyRestCalculation { NextState=Rest(state,seconds),GameSeconds=seconds };
        public static BodyState RecoverLocalFatigue(BodyState state,Rational multiplier)
        {
            if(multiplier<0||multiplier>1)throw new ArgumentOutOfRangeException(nameof(multiplier));
            return Modify(state,n=> { foreach(var r in n.Regions)r.LocalFatigueMicro=NumericKernel.ToMicro(BodyContent.Clamp(V(r.LocalFatigueMicro)-V(BodyContent.Current.Growth.LocalRecoveryMicro)*multiplier,"local_fatigue")); });
        }
        public static BodyState RecoverStress(BodyState state,Rational multiplier)
        {
            if(multiplier<0||multiplier>1)throw new ArgumentOutOfRangeException(nameof(multiplier));
            return Modify(state,n=>n.StressMicro=NumericKernel.ToMicro(BodyContent.Clamp(V(n.StressMicro)-V(BodyContent.Current.Growth.StressRecoveryMicro)*multiplier,"stress")));
        }
        public static BodyState AddStress(BodyState state,Rational amount)
        {
            if(amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            return Modify(state,n=>n.StressMicro=NumericKernel.ToMicro(BodyContent.Clamp(V(n.StressMicro)+amount,"stress")));
        }
        public static BodyState ClearTemporaryFatigue(BodyState state)=>Modify(state,n=> { foreach(var r in n.Regions)r.SetTemporary(0); });
        public static BodyState ExpireInjuries(BodyState state,int enteringDay)
        {
            if(enteringDay<1)throw new ArgumentOutOfRangeException(nameof(enteringDay));
            return Modify(state,n=> { foreach(var r in n.Regions)if(r.IsInjured&&enteringDay>=r.ExpiresAtStartOfDay) { r.InjuredDay=0; r.ExpiresAtStartOfDay=0; } });
        }
        public static BodyState AddKnowledge(BodyState state,string[] exerciseIds,long amountMicro)
        {
            if(exerciseIds==null||exerciseIds.Distinct().Count()!=exerciseIds.Length)throw new ArgumentException("Invalid knowledge targets");
            return Modify(state,n=> { foreach(var id in exerciseIds) { var e=n.Exercise(id); e.KnowledgeMicro=NumericKernel.ToMicro(BodyContent.Clamp(V(e.KnowledgeMicro)+V(amountMicro),"knowledge")); } });
        }
    }
}
