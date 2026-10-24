using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Training
{
    public static class RegionIds
    {
        public static IReadOnlyList<string> All => Array.AsReadOnly(BodyContent.Current.Regions.Select(r=>r.Id).ToArray());
    }
    public sealed class ExerciseDefinition
    {
        public string Id { get; }
        public string Version => ExerciseCatalog.Version;
        public IReadOnlyList<string> TargetRegionIds { get; }
        public IReadOnlyList<int> WeightBasisPoints { get; }
        public IReadOnlyList<int> Sets { get; }
        public IReadOnlyList<int> Reps { get; }
        public int SecondsPerRep { get; }
        public int FatiguePool { get; }
        public Rational StressPool { get; }
        public Rational FatLossPerRep { get; }
        public string Name { get; }
        public string KernelId { get; }
        public string LoadUnit { get; }
        public IReadOnlyList<long> DisplayLoadMicro { get; }
        private readonly int[] thresholds;
        internal ExerciseDefinition(ExerciseContentDefinition definition)
        {
            Id=definition.Id;Name=definition.Name;KernelId=definition.KernelId;LoadUnit=definition.LoadUnit;
            TargetRegionIds=Array.AsReadOnly((string[])definition.TargetRegionIds.Clone());WeightBasisPoints=Array.AsReadOnly((int[])definition.WeightBasisPoints.Clone());
            Sets=Array.AsReadOnly((int[])definition.Sets.Clone());Reps=Array.AsReadOnly((int[])definition.Reps.Clone());DisplayLoadMicro=Array.AsReadOnly((long[])definition.DisplayLoadMicro.Clone());
            SecondsPerRep=definition.SecondsPerRep;FatiguePool=definition.FatiguePool;StressPool=NumericKernel.FromMicro(definition.StressPoolMicro);FatLossPerRep=NumericKernel.FromMicro(definition.FatLossPerRepMicro);thresholds=(int[])definition.Thresholds.Clone();
        }
        public int Threshold(int tier)
        {
            if(tier<1||tier>4) throw new ArgumentOutOfRangeException(nameof(tier));
            return thresholds[tier-1];
        }
    }
    public static class ExerciseCatalog
    {
        public static string Version=>TrainingContent.Current.Version;
        public static IReadOnlyList<ExerciseDefinition> All { get { var d=TrainingContent.Current;return Array.AsReadOnly(d.Exercises.Select(e=>new ExerciseDefinition(e)).ToArray()); } }
        public static ExerciseDefinition Get(string id)
        {
            foreach(var d in All) if(d.Id==id)return d;
            throw new ArgumentException("Unknown exercise: "+id,nameof(id));
        }
        public static Rational Load(int tier)
        {
            if(tier<1||tier>4)throw new ArgumentOutOfRangeException(nameof(tier));
            return NumericKernel.FromMicro(TrainingContent.Current.TierLoadMicro[tier-1]);
        }
        public static Rational StrengthPool(int tier) { Load(tier); return NumericKernel.FromMicro(TrainingContent.Current.TierStrengthPoolMicro[tier-1]); }
        public static int ProficiencyPool(int tier) { Load(tier); return TrainingContent.Current.TierProficiencyPool[tier-1]; }
    }
}
