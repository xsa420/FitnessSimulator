using System;
using System.Linq;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Body
{
    [Serializable] public sealed class BodyContentDefinition
    {
        public string Version;
        public RegionDefinition[] Regions;
        public AttributeDefinition[] Attributes;
        public GrowthDefinition Growth;
        public BodyFatCurveDefinition CharmCurve,PressureCurve;
        public ThinBuildDefinition ThinBuild;
    }
    [Serializable] public sealed class RegionDefinition { public string Id,Name,Group; }
    [Serializable] public sealed class AttributeDefinition { public string Id,Name,Unit,Scope; public long MinMicro,MaxMicro; public int DisplayDecimals; }
    [Serializable] public sealed class GrowthDefinition
    {
        public long StrengthDecayPerPointMicro,FatigueFreeUntilMicro,FatigueLossAtMaximumMicro;
        public long[] StressThresholdsMicro,StressRetentionMicro,KnowledgeThresholdsMicro,ProficiencyCapsMicro;
        public long KnowledgeMultiplierBaseMicro,KnowledgeMultiplierDenominatorMicro,LocalRecoveryMicro,StressRecoveryMicro;
    }
    [Serializable] public sealed class BodyFatCurveDefinition { public long[] FatMicro,ValueMicro; }
    [Serializable] public sealed class ThinBuildDefinition { public long RiseEndMicro,PlateauEndMicro,FallEndMicro,SpreadScaleMicro,BaseWeightMicro,BalanceWeightMicro; }
    // Required reference wrappers distinguish absent initial fields from explicitly configured zero.
    [Serializable] public sealed class InitialValue { public long Value; }
    [Serializable] public sealed class InitialRegionDefinition { public string RegionId; public InitialValue StrengthMicro,LocalFatigueMicro,TemporaryFatigueMicro,InjuredDay,ExpiresAtStartOfDay; }
    [Serializable] public sealed class InitialExerciseDefinition { public string ExerciseId; public InitialValue KnowledgeMicro,ProficiencyMicro; }
    [Serializable] public sealed class BodyInitialDefinition
    {
        public InitialRegionDefinition[] Regions;
        public InitialExerciseDefinition[] Exercises;
        public InitialValue StressMicro,BodyFatMicro,BaseCharmMicro,MentalStressMicro;
    }
    public static class BodyContent
    {
        public static BodyContentDefinition Current=>ContentRuntime.Current.Get<BodyContentDefinition>("body");
        public static AttributeDefinition Attribute(string id)=>Current.Attributes.Single(x=>x.Id==id);
        public static long Required(InitialValue value,string path) { if(value==null)throw new ArgumentException("initial."+path+": explicit value required");return value.Value; }
        public static void Check(long value,string attribute)
        {
            var rule=Attribute(attribute);if(value<rule.MinMicro||value>rule.MaxMicro)throw new ArgumentOutOfRangeException(attribute,"body.Attributes."+attribute+": outside range");
        }
        public static Rational Clamp(Rational value,string attribute)
        {
            var rule=Attribute(attribute);return Rational.Clamp(value,NumericKernel.FromMicro(rule.MinMicro),NumericKernel.FromMicro(rule.MaxMicro));
        }
        public static Rational Evaluate(BodyFatCurveDefinition curve,long fatMicro)
        {
            Check(fatMicro,"body_fat");
            for(int i=1;i<curve.FatMicro.Length;i++)if(fatMicro<=curve.FatMicro[i])return NumericKernel.FromMicro(curve.ValueMicro[i-1])+new Rational(fatMicro-curve.FatMicro[i-1],curve.FatMicro[i]-curve.FatMicro[i-1])*NumericKernel.FromMicro(curve.ValueMicro[i]-curve.ValueMicro[i-1]);
            throw new ArgumentException("body curve does not cover fat range");
        }
        public static void ValidateContent()=>Validate(Current);
        public static void Validate(BodyContentDefinition d)
        {
            if(d==null||string.IsNullOrWhiteSpace(d.Version))throw new ArgumentException("body.Version required");
            if(d.Regions==null||d.Regions.Length!=21||d.Regions.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.Name)||string.IsNullOrWhiteSpace(x.Group))||d.Regions.Select(x=>x.Id).Distinct().Count()!=21)throw new ArgumentException("body.Regions: 21 unique named/grouped regions required");
            string[] regionIds={"chest_upper","chest_middle","chest_lower","back_upper","back_middle","back_lats","back_lower","leg_quads","leg_hamstrings","leg_adductors","leg_calves","shoulder_front","shoulder_side","shoulder_rear","biceps_long","biceps_short","triceps_long","triceps_lateral","triceps_medial","abs","glutes"};
            string[] groups={"chest","chest","chest","back","back","back","back","legs","legs","legs","legs","shoulders","shoulders","shoulders","biceps","biceps","triceps","triceps","triceps","abs","glutes"};
            if(!d.Regions.Select(r=>r.Id).SequenceEqual(regionIds)||!d.Regions.Select(r=>r.Group).SequenceEqual(groups))throw new ArgumentException("body.Regions: stable IDs/order/group membership required");
            string[] ids={"strength","local_fatigue","temporary_fatigue","stress","body_fat","base_charm","mental_stress","knowledge","proficiency"};
            if(d.Attributes==null||d.Attributes.Length!=ids.Length||d.Attributes.Any(x=>x==null)||!d.Attributes.Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(ids.OrderBy(x=>x)))throw new ArgumentException("body.Attributes: complete unique dictionary required");
            foreach(var a in d.Attributes)
            {
                string scope=new[]{"strength","local_fatigue","temporary_fatigue"}.Contains(a.Id)?"actor_region":new[]{"knowledge","proficiency"}.Contains(a.Id)?"actor_exercise":"actor";
                long max=a.Id=="temporary_fatigue"?150000000:a.Id=="proficiency"?1000000000:a.Id=="body_fat"?50000000:100000000;
                long min=a.Id=="body_fat"?5000000:0;
                if(string.IsNullOrWhiteSpace(a.Name)||string.IsNullOrWhiteSpace(a.Unit)||a.Scope!=scope||a.MinMicro!=min||a.MaxMicro!=max||a.DisplayDecimals<0||a.DisplayDecimals>6)throw new ArgumentException("body.Attributes."+a.Id+": invalid unit/scope/locked range/precision");
            }
            if(d.Attributes.Single(x=>x.Id=="temporary_fatigue").MaxMicro!=150000000)throw new ArgumentException("body.Attributes.temporary_fatigue: mandatory injury cap must remain 150");
            var g=d.Growth;
            if(g==null||g.StrengthDecayPerPointMicro<0||g.StrengthDecayPerPointMicro>1000000||g.FatigueFreeUntilMicro<0||g.FatigueLossAtMaximumMicro<0||g.FatigueLossAtMaximumMicro>=1000000||g.KnowledgeMultiplierBaseMicro<=0||g.KnowledgeMultiplierDenominatorMicro<=0||g.LocalRecoveryMicro!=20000000||g.StressRecoveryMicro!=10000000)throw new ArgumentException("body.Growth invalid; daily recovery 20/10 retained");
            if(g.FatigueFreeUntilMicro>=d.Attributes.Single(x=>x.Id=="local_fatigue").MaxMicro||new Rational(g.StrengthDecayPerPointMicro,1000000)*new Rational(d.Attributes.Single(x=>x.Id=="strength").MaxMicro,1000000)>1)throw new ArgumentException("body.Growth: retention becomes negative or denominator zero");
            ValidateSteps(g.StressThresholdsMicro,g.StressRetentionMicro,"Stress",true);
            ValidateSteps(g.KnowledgeThresholdsMicro,g.ProficiencyCapsMicro,"Knowledge",false);
            foreach(var curve in new[]{d.CharmCurve,d.PressureCurve})
            {
                if(curve==null||curve.FatMicro==null||curve.ValueMicro==null||curve.FatMicro.Length<2||curve.FatMicro.Length!=curve.ValueMicro.Length)throw new ArgumentException("body fat curve shape");
                for(int i=0;i<curve.FatMicro.Length;i++)if(curve.ValueMicro[i]<0||curve.ValueMicro[i]>1000000||(i>0&&curve.FatMicro[i]<=curve.FatMicro[i-1]))throw new ArgumentException("body fat curve must be ordered, values 0..1");
                var fat=d.Attributes.Single(x=>x.Id=="body_fat");if(curve.FatMicro[0]!=fat.MinMicro||curve.FatMicro.Last()!=fat.MaxMicro)throw new ArgumentException("body fat curve endpoints must cover range");
            }
            var t=d.ThinBuild;if(t==null||t.RiseEndMicro<=0||t.PlateauEndMicro<t.RiseEndMicro||t.FallEndMicro<=t.PlateauEndMicro||t.SpreadScaleMicro<=0||t.BaseWeightMicro<0||t.BalanceWeightMicro<0||t.BaseWeightMicro+t.BalanceWeightMicro!=1000000)throw new ArgumentException("body.ThinBuild invalid");
        }
        private static void ValidateSteps(long[] thresholds,long[] values,string path,bool ratios)
        {
            if(thresholds==null||values==null||values.Length!=thresholds.Length+1||values.Length==0)throw new ArgumentException("body.Growth."+path+": step shape");
            for(int i=0;i<thresholds.Length;i++)if(thresholds[i]<=0||(i>0&&thresholds[i]<=thresholds[i-1]))throw new ArgumentException("body.Growth."+path+": threshold ordering");
            for(int i=0;i<values.Length;i++)if(values[i]<0||(ratios&&values[i]>1000000)||(!ratios&&(values[i]<=0||(i>0&&values[i]<values[i-1]))))throw new ArgumentException("body.Growth."+path+": invalid values");
        }
    }
}
