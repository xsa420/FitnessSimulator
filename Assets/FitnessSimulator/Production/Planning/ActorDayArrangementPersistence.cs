using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Planning
{
    [Serializable]
    public sealed class ActorDayArrangementRecord
    {
        public int SchemaVersion = 1;
        public string RunId, ActorId, BasePlanId, BindingId, SourceActorId, SourcePlanId, ExerciseCatalogVersion;
        public int Day, SourceEffectiveDay;
        public long BaseRevision, SourceRevision;
        public bool HasCompanionSource;
        public int SourceSlotIndex;
        public bool IsRest;
        public string AnchorId;
        public TrainingCombination Combination;
        public TrainingVolume Volume;
        public FrozenPlanEntryRecord[] Entries;
    }

    [Serializable]
    public sealed class FrozenPlanEntryRecord
    {
        public string EntryId, ExerciseId, ExerciseVersion;
        public int TargetSets, TargetRepetitions;
    }

    public static class ActorDayArrangementPersistence
    {
        public static ActorDayArrangementRecord Export(ActorDayArrangement value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new ActorDayArrangementRecord {
                RunId = value.RunId, ActorId = value.ActorId, Day = value.Day, BasePlanId = value.BasePlanId, BaseRevision = value.BaseRevision,
                HasCompanionSource = value.HasCompanionSource, BindingId = value.BindingId, SourceActorId = value.SourceActorId,
                SourcePlanId = value.SourcePlanId, SourceRevision = value.SourceRevision, SourceEffectiveDay = value.SourceEffectiveDay,
                ExerciseCatalogVersion = value.ExerciseCatalogVersion, SourceSlotIndex = value.Slot.Index, IsRest = value.Slot.IsRest,
                AnchorId = value.Slot.AnchorId, Combination = value.Slot.Combination, Volume = value.Slot.Volume,
                Entries = value.Slot.Entries.Select(e => new FrozenPlanEntryRecord { EntryId = e.EntryId, ExerciseId = e.ExerciseId,
                    ExerciseVersion = e.ExerciseVersion, TargetSets = e.TargetSets, TargetRepetitions = e.TargetRepetitions }).ToArray()
            };
        }

        public static ActorDayArrangement Restore(ActorDayArrangementRecord record, string expectedRunId, string expectedActorId,
            int expectedDay, IExercisePlanCatalog catalog, IEnumerable<string> knownTrainingAnchorIds)
        {
            if (record == null || catalog == null) throw new ArgumentNullException();
            var anchors = ActorDayPlans.KnownAnchors(knownTrainingAnchorIds);
            if (record.SchemaVersion != 1 || record.ExerciseCatalogVersion != catalog.Version) throw new InvalidOperationException("UnsupportedArrangementVersion");
            if (new[] { record.RunId, record.ActorId, record.BasePlanId, record.SourceActorId, record.SourcePlanId }.Any(string.IsNullOrWhiteSpace) ||
                record.RunId != expectedRunId || record.ActorId != expectedActorId || record.Day != expectedDay || record.Day < 1 ||
                record.BaseRevision < 1 || record.SourceRevision < 1 || record.SourceEffectiveDay < 1 || record.SourceEffectiveDay > record.Day ||
                record.SourceSlotIndex < 0 || record.SourceSlotIndex > 13)
                throw new ArgumentException("InvalidArrangementContext");
            if (record.HasCompanionSource ? string.IsNullOrWhiteSpace(record.BindingId) || record.SourceActorId == record.ActorId :
                !string.IsNullOrEmpty(record.BindingId) || record.SourceActorId != record.ActorId || record.SourcePlanId != record.BasePlanId || record.SourceRevision != record.BaseRevision)
                throw new ArgumentException("InvalidArrangementSource");
            if (record.Entries == null || record.Entries.Any(e => e == null)) throw new ArgumentException("MissingArrangementEntries");
            var slot = new PlanSlotDraft { Index = 0, IsRest = record.IsRest, AnchorId = record.AnchorId, Combination = record.Combination, Volume = record.Volume,
                Entries = record.Entries.Select(e => new PlanEntryDraft { EntryId = e.EntryId, ExerciseId = e.ExerciseId }).ToArray() };
            var errors = PlanRules.Validate(new PlanDraft { Mode = PlanMode.DayCycle, Slots = new[] { slot } }, record.Day,
                new AnchorEligibility(record.Day, anchors, Array.Empty<string>()), catalog);
            if (errors.Count > 0) throw new PlanValidationException(errors);
            foreach (var e in record.Entries)
            {
                if (!catalog.TryGet(e.ExerciseId, out var definition) || e.ExerciseVersion != definition.Version)
                    throw new InvalidOperationException("UnsupportedArrangementExerciseVersion");
                var amount = definition.GetVolume(record.Volume);
                if (e.TargetSets != amount.Sets || e.TargetRepetitions != amount.Repetitions) throw new ArgumentException("FrozenWorkloadMismatch");
            }
            // Validate against the frozen version but construct using the recorded exact values, not a new day plan.
            var frozenSlot = new PlanSlot(record.SourceSlotIndex, record.IsRest, record.AnchorId, record.Combination, record.Volume,
                record.Entries.Select(e => new PlannedExercise(e.EntryId, e.ExerciseId, e.ExerciseVersion, e.TargetSets, e.TargetRepetitions)));
            return new ActorDayArrangement(record.RunId, record.ActorId, record.Day, record.BasePlanId, record.BaseRevision,
                record.HasCompanionSource, record.BindingId, record.SourceActorId, record.SourcePlanId, record.SourceRevision,
                record.SourceEffectiveDay, record.ExerciseCatalogVersion, frozenSlot);
        }
    }
}
