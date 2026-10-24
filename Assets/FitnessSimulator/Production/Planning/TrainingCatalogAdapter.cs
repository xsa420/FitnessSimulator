using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Planning
{
    public sealed class TrainingCatalogAdapter : IExercisePlanCatalog
    {
        public string Version => ExerciseCatalog.Version;
        public IReadOnlyList<string> ExerciseIds => Array.AsReadOnly(ExerciseCatalog.All.Select(x => x.Id).ToArray());
        public bool TryGet(string exerciseId, out ExercisePlanDefinition definition)
        {
            var source = ExerciseCatalog.All.FirstOrDefault(x => x.Id == exerciseId);
            if (source == null) { definition = null; return false; }
            definition = new ExercisePlanDefinition(source.Id, source.Version, source.TargetRegionIds,
                new ExerciseVolume(source.Sets[0], source.Reps[0]),
                new ExerciseVolume(source.Sets[1], source.Reps[1]),
                new ExerciseVolume(source.Sets[2], source.Reps[2]));
            return true;
        }
        public IReadOnlyList<string> GetCombinationRegions(TrainingCombination combination)
        {
            return Array.AsReadOnly(PlanningContent.Combination(combination).RegionIds);

        }

        public static TrainingPlanEntry[] ToTrainingEntries(DayPlan day)
        {
            if (day == null) throw new ArgumentNullException(nameof(day));
            return day.Slot.Entries.Select(e => new TrainingPlanEntry {
                EntryId = e.EntryId, ExerciseId = e.ExerciseId, Sets = e.TargetSets, Reps = e.TargetRepetitions }).ToArray();
        }

        public static TrainingPlanEntry[] ToTrainingEntries(ActorDayArrangement day)
        {
            if (day == null) throw new ArgumentNullException(nameof(day));
            return day.Slot.Entries.Select(e => new TrainingPlanEntry {
                EntryId = e.EntryId, ExerciseId = e.ExerciseId, Sets = e.TargetSets, Reps = e.TargetRepetitions }).ToArray();
        }
    }
}
