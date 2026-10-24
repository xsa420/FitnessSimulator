using System;
using System.Linq;
using System.Numerics;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Body
{
    [Serializable] public sealed class RegionState
    {
        public string RegionId;
        public long StrengthMicro,LocalFatigueMicro,TemporaryFatigueMicro;
        public string TemporaryRemainderNumerator="0",TemporaryRemainderDenominator="1";
        public int InjuredDay,ExpiresAtStartOfDay;
        public bool IsInjured => ExpiresAtStartOfDay>0;
        public Rational ExactTemporary => NumericKernel.FromMicro(TemporaryFatigueMicro)+new Rational(BigInteger.Parse(TemporaryRemainderNumerator),BigInteger.Parse(TemporaryRemainderDenominator));
        public void SetTemporary(Rational value)
        {
            value=BodyContent.Clamp(value,"temporary_fatigue"); TemporaryFatigueMicro=NumericKernel.ToMicro(value);
            var remainder=value-NumericKernel.FromMicro(TemporaryFatigueMicro);
            TemporaryRemainderNumerator=remainder.Numerator.ToString(); TemporaryRemainderDenominator=remainder.Denominator.ToString();
        }
        public RegionState Clone() => (RegionState)MemberwiseClone();
    }
    [Serializable] public sealed class ExerciseBodyState
    {
        public string ExerciseId;
        public long KnowledgeMicro,ProficiencyMicro;
        public ExerciseBodyState Clone() => (ExerciseBodyState)MemberwiseClone();
    }
    [Serializable] public sealed class BodyState
    {
        public string ActorId;
        public long Revision;
        public RegionState[] Regions;
        public ExerciseBodyState[] Exercises;
        public long StressMicro,BodyFatMicro,BaseCharmMicro,MentalStressMicro;
        public static BodyState CreateFromDefinition(string actorId,BodyInitialDefinition definition)
        {
            if(definition==null||definition.Regions==null||definition.Exercises==null)throw new ArgumentException("Complete body initial definition required");
            var state=new BodyState { ActorId=actorId,StressMicro=BodyContent.Required(definition.StressMicro,"StressMicro"),BodyFatMicro=BodyContent.Required(definition.BodyFatMicro,"BodyFatMicro"),BaseCharmMicro=BodyContent.Required(definition.BaseCharmMicro,"BaseCharmMicro"),MentalStressMicro=BodyContent.Required(definition.MentalStressMicro,"MentalStressMicro"),
                Regions=definition.Regions.Select(r=>new RegionState { RegionId=r.RegionId,StrengthMicro=BodyContent.Required(r.StrengthMicro,r.RegionId+".Strength"),LocalFatigueMicro=BodyContent.Required(r.LocalFatigueMicro,r.RegionId+".F"),TemporaryFatigueMicro=BodyContent.Required(r.TemporaryFatigueMicro,r.RegionId+".T"),InjuredDay=checked((int)BodyContent.Required(r.InjuredDay,r.RegionId+".InjuredDay")),ExpiresAtStartOfDay=checked((int)BodyContent.Required(r.ExpiresAtStartOfDay,r.RegionId+".Expires")) }).ToArray(),
                Exercises=definition.Exercises.Select(e=>new ExerciseBodyState { ExerciseId=e.ExerciseId,KnowledgeMicro=BodyContent.Required(e.KnowledgeMicro,e.ExerciseId+".K"),ProficiencyMicro=BodyContent.Required(e.ProficiencyMicro,e.ExerciseId+".X") }).ToArray() };
            state.Validate();return state;
        }
        public static BodyState Create(string actorId,long[] strengthMicro,long bodyFatMicro,long baseCharmMicro,long mentalStressMicro)
        {
            if(strengthMicro==null||strengthMicro.Length!=21)throw new ArgumentException("Expected 21 strength values");
            var state=new BodyState { ActorId=actorId,BodyFatMicro=bodyFatMicro,BaseCharmMicro=baseCharmMicro,MentalStressMicro=mentalStressMicro,
                Regions=RegionIds.All.Select((id,i)=>new RegionState { RegionId=id,StrengthMicro=strengthMicro[i] }).ToArray(),
                Exercises=ExerciseCatalog.All.Select(d=>new ExerciseBodyState { ExerciseId=d.Id }).ToArray() };
            state.Validate(); return state;
        }
        public RegionState Region(string id) => Regions.Single(r=>r.RegionId==id);
        public ExerciseBodyState Exercise(string id) => Exercises.Single(r=>r.ExerciseId==id);
        public BodyState Clone() => new BodyState { ActorId=ActorId,Revision=Revision,Regions=Regions.Select(r=>r.Clone()).ToArray(),Exercises=Exercises.Select(e=>e.Clone()).ToArray(),StressMicro=StressMicro,BodyFatMicro=BodyFatMicro,BaseCharmMicro=BaseCharmMicro,MentalStressMicro=MentalStressMicro };
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(ActorId)||Revision<0||Regions==null||Regions.Length!=21||Exercises==null||Exercises.Length!=8)throw new ArgumentException("Invalid body schema");
            foreach(var id in RegionIds.All)
            {
                var r=Region(id); BodyContent.Check(r.StrengthMicro,"strength"); BodyContent.Check(r.LocalFatigueMicro,"local_fatigue"); BodyContent.Check(r.TemporaryFatigueMicro,"temporary_fatigue");
                var exact=r.ExactTemporary;
                if(exact<0||exact>NumericKernel.FromMicro(BodyContent.Attribute("temporary_fatigue").MaxMicro)||NumericKernel.ToMicro(exact)!=r.TemporaryFatigueMicro)throw new ArgumentException("Invalid fatigue remainder");
                if(r.InjuredDay<0||r.ExpiresAtStartOfDay<0||(r.ExpiresAtStartOfDay>0&&r.ExpiresAtStartOfDay<=r.InjuredDay))throw new ArgumentException("Invalid injury");
            }
            foreach(var d in ExerciseCatalog.All) { var e=Exercise(d.Id); BodyContent.Check(e.KnowledgeMicro,"knowledge"); BodyContent.Check(e.ProficiencyMicro,"proficiency"); }
            BodyContent.Check(StressMicro,"stress");BodyContent.Check(BodyFatMicro,"body_fat");BodyContent.Check(BaseCharmMicro,"base_charm");BodyContent.Check(MentalStressMicro,"mental_stress");
        }
        private static void Check(long value,int min,int max) { if(value<min*NumericKernel.Micro||value>max*NumericKernel.Micro)throw new ArgumentOutOfRangeException(nameof(value)); }
    }
    public static class BodyMetrics
    {
        public const string Version="BodyMetricsL_V1_Sqrt12_Micro6";
        public static Rational GetNormalizedAttribute(long valueMicro,string attributeId)
        {
            BodyContent.Check(valueMicro,attributeId);var a=BodyContent.Attribute(attributeId);return new Rational(valueMicro-a.MinMicro,a.MaxMicro-a.MinMicro);
        }
        public static long GetEffectiveCharmMicro(BodyState state)
        {
            state.Validate();return NumericKernel.ToMicro(NumericKernel.FromMicro(state.BaseCharmMicro)*(1+BodyContent.Evaluate(BodyContent.Current.CharmCurve,state.BodyFatMicro)));
        }
        public static Rational GetThinRegionAdaptation(long strengthMicro)
        {
            BodyContent.Check(strengthMicro,"strength");var d=BodyContent.Current.ThinBuild;
            return strengthMicro<d.RiseEndMicro?new Rational(strengthMicro,d.RiseEndMicro):strengthMicro<=d.PlateauEndMicro?(Rational)1:strengthMicro<d.FallEndMicro?new Rational(d.FallEndMicro-strengthMicro,d.FallEndMicro-d.PlateauEndMicro):(Rational)0;
        }
        public static long GetThinBuildIndexMicro(BodyState state)
        {
            state.Validate(); Rational sum=0,squares=0,adaptation=0;
            foreach(var region in state.Regions)
            {
                var s=NumericKernel.FromMicro(region.StrengthMicro); sum+=s; squares+=s*s;
                adaptation+=GetThinRegionAdaptation(region.StrengthMicro);
            }
            int count=state.Regions.Length;var definition=BodyContent.Current.ThinBuild;
            var variance=squares/count-(sum/count)*(sum/count);
            var std=new Rational(NumericKernel.SqrtRational12(variance),BigInteger.Pow(10,12));
            var e=1-Rational.Clamp(std/NumericKernel.FromMicro(definition.SpreadScaleMicro),0,1);
            return NumericKernel.ToMicro(adaptation/count*(NumericKernel.FromMicro(definition.BaseWeightMicro)+e*NumericKernel.FromMicro(definition.BalanceWeightMicro)));
        }
    }
}
