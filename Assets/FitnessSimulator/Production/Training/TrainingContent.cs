using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Configuration;

namespace FitnessSimulator.Game.Training
{
    [Serializable] public sealed class TrainingContentDefinition
    {
        public string Version;
        public ExerciseContentDefinition[] Exercises;
        public KernelDefinition[] Kernels;
        public long[] TierLoadMicro,TierStrengthPoolMicro;
        public int[] TierProficiencyPool;
        public DifficultyDefinition Difficulty;
        public FatigueDefinition Fatigue;
        public KnowledgeDefinition Knowledge;
        public int FailureLimit,MaxTargetReps;
    }
    [Serializable] public sealed class ExerciseContentDefinition
    {
        public string Id,Name,KernelId;
        public string[] TargetRegionIds;
        public int[] WeightBasisPoints,Sets,Reps,Thresholds;
        public long[] DisplayLoadMicro;
        public string LoadUnit;
        public int SecondsPerRep,FatiguePool;
        public long StressPoolMicro,FatLossPerRepMicro;
    }
    [Serializable] public sealed class DifficultyDefinition
    {
        public long IntensityReductionMicro,IntensityWeightMicro,TemporaryWeightMicro;
        public long[] ProficiencyThresholdsMicro,ProficiencyReductionMicro;
    }
    [Serializable] public sealed class FatigueDefinition
    {
        public long DailyQuotaMicro,ChallengeMultiplierMicro,FailureMultiplierMicro,RestRecoveryPerSecondMicro,SafeMicro,MandatoryInjuryMicro;
        public int[] RestSeconds;
        public int StandardRestSeconds,InjuryFullDays;
        public string RiskCurve;
    }
    [Serializable] public sealed class KnowledgeDefinition
    {
        public long FailedChallengeMicro,EveningStudyMicro,FullDayStudyMicro,NotebookMicro;
        public long MaxTeachingNominalMicro;
        public int EveningStudySeconds,FullDayStudySecondsPerAnchor;
    }
    [Serializable] public sealed class KernelParameter { public string Key; public double Value; }
    [Serializable] public sealed class KernelPoint { public double X,Y; }
    [Serializable] public sealed class KernelDefinition
    {
        public string Id;
        public int PreparationMilliseconds,ResumeMilliseconds;
        public KernelParameter[] Parameters;
        public KernelPoint[] Points;
        public double[] Choices;
        private Dictionary<string,double> index;
        public double Number(string key)
        {
            if(index==null)index=Parameters.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
            if(!index.TryGetValue(key,out var value))throw new ArgumentException("training.Kernels."+Id+".Parameters."+key+": missing");return value;
        }
        public int Milliseconds(string key)=>checked((int)Number(key));
    }
    [Serializable] public sealed class InitialTrainingExerciseDefinition { public string ExerciseId; public InitialValue HighestUnlockedTier; public bool[] AutomaticTiers; }
    [Serializable] public sealed class TrainingInitialDefinition { public InitialTrainingExerciseDefinition[] Exercises; }
    public static class TrainingContent
    {
        public static TrainingContentDefinition Current=>ContentRuntime.Current.Get<TrainingContentDefinition>("training");
        public static KernelDefinition Kernel(string id)=>Current.Kernels.Single(x=>x.Id==id);
        public static void ValidateContent()=>Validate(Current,BodyContent.Current);
        public static void Validate(TrainingContentDefinition d,BodyContentDefinition body)
        {
            if(d==null||string.IsNullOrWhiteSpace(d.Version))throw new ArgumentException("training.Version required");
            string[] ids={"bench_press","dumbbell_curl","lat_pulldown","triceps_pushdown","squat","hip_thrust","shoulder_press","lateral_raise"};
            if(d.Exercises==null||d.Kernels==null||d.Exercises.Length!=8||d.Kernels.Length!=8||d.Exercises.Any(x=>x==null)||d.Kernels.Any(x=>x==null)||!d.Exercises.Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(ids.OrderBy(x=>x))||!d.Kernels.Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(ids.OrderBy(x=>x)))throw new ArgumentException("training: eight distinct exercise/kernel definitions required");
            if(d.TierLoadMicro==null||d.TierStrengthPoolMicro==null||d.TierProficiencyPool==null||d.TierLoadMicro.Length!=4||d.TierStrengthPoolMicro.Length!=4||d.TierProficiencyPool.Length!=4)throw new ArgumentException("training tier arrays must contain four values");
            for(int i=0;i<4;i++)if(d.TierLoadMicro[i]<=0||d.TierStrengthPoolMicro[i]<=0||d.TierProficiencyPool[i]<=0||(i>0&&(d.TierLoadMicro[i]<d.TierLoadMicro[i-1])))throw new ArgumentException("training tiers positive/ordered");
            foreach(var e in d.Exercises)
            {
                if(e.KernelId!=e.Id||string.IsNullOrWhiteSpace(e.Name)||string.IsNullOrWhiteSpace(e.LoadUnit)||e.TargetRegionIds==null||e.WeightBasisPoints==null||e.TargetRegionIds.Length==0||e.TargetRegionIds.Length!=e.WeightBasisPoints.Length||e.TargetRegionIds.Distinct().Count()!=e.TargetRegionIds.Length||e.TargetRegionIds.Any(id=>!body.Regions.Any(r=>r.Id==id))||e.WeightBasisPoints.Any(w=>w<=0)||e.WeightBasisPoints.Sum()!=10000)throw new ArgumentException("training.Exercises."+e.Id+": target/kernel/weights invalid");
                if(e.Sets==null||e.Reps==null||e.Sets.Length!=3||e.Reps.Length!=3||e.Sets.Any(n=>n<=0)||e.Reps.Any(n=>n<=0||n>d.MaxTargetReps)||e.SecondsPerRep<=0||e.FatiguePool<=0||e.StressPoolMicro<=0||e.FatLossPerRepMicro<=0||e.DisplayLoadMicro==null||e.DisplayLoadMicro.Length!=4||e.DisplayLoadMicro.Any(n=>n<=0))throw new ArgumentException("training.Exercises."+e.Id+": workload/pool/load invalid");
                if(e.Thresholds==null||e.Thresholds.Length!=4)throw new ArgumentException("training.Exercises."+e.Id+".Thresholds: four total strength thresholds required");
                for(int i=0;i<4;i++)if(e.Thresholds[i]<=0||e.Thresholds[i]>e.TargetRegionIds.Length*100||(i>0&&e.Thresholds[i]<=e.Thresholds[i-1]))throw new ArgumentException("training.Exercises."+e.Id+".Thresholds: invalid sum threshold");
                for(int i=0;i<3;i++)if(e.Sets[i]>4||(i>0&&(e.Sets[i]<=e.Sets[i-1]||e.Reps[i]<=e.Reps[i-1])))throw new ArgumentException("training.Exercises."+e.Id+": workload bands ordered, at most four sets");
            }
            if(d.Exercises.SelectMany(e=>e.TargetRegionIds).Distinct().Count()!=21)throw new ArgumentException("training.Exercises: all 21 regions must have positive contribution");
            if(d.FailureLimit!=3||d.MaxTargetReps<=0)throw new ArgumentException("training: third-error termination invariant");
            var f=d.Fatigue;if(f==null||f.DailyQuotaMicro!=80000000||f.ChallengeMultiplierMicro<=1000000||f.FailureMultiplierMicro<=0||f.RestRecoveryPerSecondMicro<=0||f.SafeMicro!=100000000||f.MandatoryInjuryMicro!=150000000||f.RiskCurve!="sqrt"||f.InjuryFullDays<0||f.RestSeconds==null||f.RestSeconds.Length!=3||f.RestSeconds.Any(x=>x<=0)||f.RestSeconds.Distinct().Count()!=3||!f.RestSeconds.SequenceEqual(f.RestSeconds.OrderBy(x=>x))||f.StandardRestSeconds!=f.RestSeconds[1])throw new ArgumentException("training.Fatigue invalid; quota80, risk100/150 and sqrt retained");
            var q=d.Difficulty;if(q==null||q.IntensityReductionMicro<0||q.IntensityReductionMicro>=1000000||q.IntensityWeightMicro<0||q.TemporaryWeightMicro<0||q.IntensityWeightMicro+q.TemporaryWeightMicro!=1000000||q.ProficiencyThresholdsMicro==null||q.ProficiencyReductionMicro==null||q.ProficiencyThresholdsMicro.Length!=q.ProficiencyReductionMicro.Length||q.ProficiencyThresholdsMicro.Length==0||q.ProficiencyThresholdsMicro[0]!=0)throw new ArgumentException("training.Difficulty invalid");
            for(int i=0;i<q.ProficiencyThresholdsMicro.Length;i++)if(q.ProficiencyReductionMicro[i]<0||q.ProficiencyReductionMicro[i]>1000000||(i>0&&q.ProficiencyThresholdsMicro[i]<=q.ProficiencyThresholdsMicro[i-1]))throw new ArgumentException("training.Difficulty proficiency steps invalid");
            var k=d.Knowledge;if(k==null||k.FailedChallengeMicro<=0||k.EveningStudyMicro<=0||k.FullDayStudyMicro<=0||k.NotebookMicro<=0||k.EveningStudySeconds<=0||k.FullDayStudySecondsPerAnchor<=0||k.MaxTeachingNominalMicro!=3000000)throw new ArgumentException("training.Knowledge invalid");
            foreach(var kernel in d.Kernels)ValidateKernel(kernel);
        }
        public static void ValidateKernel(KernelDefinition k)
        {
            if(k.PreparationMilliseconds<=0||k.ResumeMilliseconds<=0||k.Parameters==null||k.Parameters.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Key)||double.IsNaN(p.Value)||double.IsInfinity(p.Value))||k.Parameters.Select(p=>p.Key).Distinct().Count()!=k.Parameters.Length||k.Points==null||k.Choices==null)throw new ArgumentException("training.Kernels."+k.Id+": invalid arrays/parameters");
            foreach(var p in k.Points)if(p==null||double.IsNaN(p.X)||double.IsNaN(p.Y)||p.X<0||p.X>1||p.Y<0||p.Y>1)throw new ArgumentException("training.Kernels."+k.Id+": point outside panel");
            if(k.Number("window_base")<=k.Number("window_reduction"))throw new ArgumentException("training.Kernels."+k.Id+": D=1 tolerance must stay positive");
            foreach(var p in k.Parameters.Where(p=>p.Key.EndsWith("_ms",StringComparison.Ordinal)))if(p.Value<=0||p.Value!=Math.Floor(p.Value)||p.Value>int.MaxValue)throw new ArgumentException("training.Kernels."+k.Id+"."+p.Key+": positive integer ms required");
            string required;
            switch(k.Id)
            {
                case "bench_press":required="force drift_base drift_gain drift_ms attempt_ms tail_ms stable_ratio balance_limit initial_balance range_min range_max";break;
                case "dumbbell_curl":required="raise_ms lower_ms";break;
                case "lat_pulldown":required="outside_ms timeout_ms";break;
                case "triceps_pushdown":required="signal_min_ms signal_max_ms return_ms";break;
                case "squat":required="cue_ms sequence_length center_radius target_max_y left_boundary right_boundary";break;
                case "hip_thrust":required="hold_ms visible_progress_ms";break;
                case "shoulder_press":required="timeout_ms first_barrier_y second_barrier_y block_width target_radius";break;
                case "lateral_raise":required="timeout_ms target_radius half_boundary hold_ms";break;
                default:throw new ArgumentException("training.Kernels."+k.Id+": unsupported kernel");
            }
            var keys=("window_base window_reduction "+required).Split(' ');
            if(!keys.OrderBy(x=>x).SequenceEqual(k.Parameters.Select(p=>p.Key).OrderBy(x=>x)))throw new ArgumentException("training.Kernels."+k.Id+": missing/unknown parameter key");
            foreach(var p in k.Parameters)if(p.Value<0&&p.Key!="range_min"&&p.Key!="initial_balance")throw new ArgumentException("training.Kernels."+k.Id+"."+p.Key+": nonnegative value required");
            double narrow=k.Number("window_base")-k.Number("window_reduction");
            if(k.Choices.Any(x=>double.IsNaN(x)||double.IsInfinity(x))||k.Choices.Distinct().Count()!=k.Choices.Length)throw new ArgumentException("training.Kernels."+k.Id+": choices must be finite/unique");
            switch(k.Id)
            {
                case "bench_press":
                    if(k.Points.Length!=0||k.Choices.Length!=0||k.Number("force")<=0||k.Number("stable_ratio")<=0||k.Number("stable_ratio")>1||k.Number("tail_ms")>k.Number("attempt_ms")||k.Number("window_base")>=k.Number("balance_limit")||Math.Abs(k.Number("initial_balance"))>narrow||k.Number("range_min")>=-k.Number("balance_limit")||k.Number("range_max")<=k.Number("balance_limit"))throw new ArgumentException("training.Kernels.bench_press: infeasible control geometry/timing");break;
                case "dumbbell_curl":if(k.Number("raise_ms")+k.Number("window_base")*2000>=k.Number("lower_ms")||k.Points.Length!=0||k.Choices.Length!=0)throw new ArgumentException("training.Kernels.dumbbell_curl: overlapping beats");break;
                case "lat_pulldown":
                    if(k.Points.Length<3||k.Points[0].X!=k.Points.Last().X||k.Points[0].Y!=k.Points.Last().Y||k.Choices.Length!=0)throw new ArgumentException("training.Kernels.lat_pulldown: closed path required");
                    for(int i=1;i<k.Points.Length;i++)if(k.Points[i].X==k.Points[i-1].X&&k.Points[i].Y==k.Points[i-1].Y)throw new ArgumentException("training.Kernels.lat_pulldown: zero length segment");break;
                case "triceps_pushdown":if(k.Number("signal_min_ms")>=k.Number("signal_max_ms")||k.Points.Length!=0||k.Choices.Length!=0)throw new ArgumentException("training.Kernels.triceps_pushdown: random signal range required");break;
                case "squat":if(k.Number("sequence_length")!=3||k.Points.Length!=4||k.Choices.Length!=0||k.Number("center_radius")<=0||k.Number("left_boundary")>=k.Number("right_boundary")||k.Number("right_boundary")>=1||k.Number("target_max_y")>=k.Points[0].Y-k.Number("center_radius"))throw new ArgumentException("training.Kernels.squat: three cues and distinct direction/center zones required");break;
                case "hip_thrust":if(k.Points.Length!=0||k.Choices.Length==0||k.Choices.Any(x=>x<=k.Number("visible_progress_ms")||x!=Math.Floor(x)||x>int.MaxValue))throw new ArgumentException("training.Kernels.hip_thrust: visible phase must end before every target");break;
                case "shoulder_press":if(k.Points.Length!=2||k.Choices.Length==0||narrow<=k.Number("block_width")||k.Number("block_width")<=0||k.Number("target_radius")<=0||k.Number("first_barrier_y")<=k.Number("second_barrier_y")||k.Points[0].Y<=k.Number("first_barrier_y")||k.Points[1].Y>=k.Number("second_barrier_y")||k.Choices.Any(x=>x-k.Number("window_base")/2<0||x+k.Number("window_base")/2>1))throw new ArgumentException("training.Kernels.shoulder_press: invalid obstacle corridor");break;
                case "lateral_raise":if(k.Points.Length!=4||k.Choices.Length!=0||k.Number("hold_ms")>k.Number("timeout_ms")||k.Number("target_radius")<=0||k.Points[0].X>=k.Number("half_boundary")||k.Points[2].X>=k.Number("half_boundary")||k.Points[1].X<=k.Number("half_boundary")||k.Points[3].X<=k.Number("half_boundary"))throw new ArgumentException("training.Kernels.lateral_raise: two independent half-panel paths required");break;
            }
        }
    }
}
