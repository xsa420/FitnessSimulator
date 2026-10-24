using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FitnessSimulator.Game.Planning
{
    public enum PlanMode { NaturalWeek, DayCycle }
    public enum TrainingVolume { Low, Balanced, High }
    public enum TrainingCombination { Free, ChestBiceps, BackTriceps, LegsGlutes, Shoulders }

    // Mutable input only. Committed revisions and resolved days copy these values.
    [Serializable]
    public sealed class PlanEntryDraft
    {
        public string EntryId;
        public string ExerciseId;
    }

    [Serializable]
    public sealed class PlanSlotDraft
    {
        // NaturalWeek: Monday=0 through Sunday=6. DayCycle: zero-based index.
        public int Index;
        public bool IsRest;
        public string AnchorId;
        public TrainingCombination Combination;
        public TrainingVolume Volume;
        public PlanEntryDraft[] Entries = Array.Empty<PlanEntryDraft>();
    }

    [Serializable]
    public sealed class PlanDraft
    {
        public PlanMode Mode;
        public PlanSlotDraft[] Slots = Array.Empty<PlanSlotDraft>();
    }

    public sealed class PlanIssue
    {
        public string Code { get; }
        public string Path { get; }
        public string Message { get; }
        public PlanIssue(string code, string path, string message)
        { Code = code; Path = path; Message = message; }
        public override string ToString() => Code + " at " + Path + ": " + Message;
    }

    public sealed class PlanValidationException : ArgumentException
    {
        public IReadOnlyList<PlanIssue> Issues { get; }
        public PlanValidationException(IEnumerable<PlanIssue> issues)
            : this(issues.ToArray()) { }
        private PlanValidationException(PlanIssue[] issues)
            : base(string.Join("; ", issues.Select(x => x.ToString())))
        { Issues = Array.AsReadOnly(issues); }
    }

    public sealed class ExerciseVolume
    {
        public int Sets { get; }
        public int Repetitions { get; }
        public ExerciseVolume(int sets, int repetitions)
        {
            if (sets <= 0 || repetitions <= 0) throw new ArgumentOutOfRangeException(nameof(sets));
            Sets = sets; Repetitions = repetitions;
        }
    }

    // Training owns the actual eight-action target/volume table; Planning consumes a projection.
    public interface IExercisePlanCatalog
    {
        string Version { get; }
        IReadOnlyList<string> ExerciseIds { get; }
        bool TryGet(string exerciseId, out ExercisePlanDefinition definition);
        IReadOnlyList<string> GetCombinationRegions(TrainingCombination combination);
    }

    public sealed class ExercisePlanDefinition
    {
        public string Id { get; }
        public string Version { get; }
        public IReadOnlyList<string> TargetRegionIds { get; }
        private readonly ExerciseVolume[] volumes;
        public ExercisePlanDefinition(string id, string version, IEnumerable<string> targetRegions,
            ExerciseVolume low, ExerciseVolume balanced, ExerciseVolume high)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Missing exercise identity/version.");
            var targets = targetRegions?.ToArray() ?? throw new ArgumentNullException(nameof(targetRegions));
            if (targets.Length == 0 || targets.Any(string.IsNullOrWhiteSpace) || targets.Distinct().Count() != targets.Length)
                throw new ArgumentException("Invalid exercise target regions.");
            Id = id; Version = version; TargetRegionIds = Array.AsReadOnly(targets);
            volumes = new[] { low, balanced, high };
            if (volumes.Any(x => x == null)) throw new ArgumentException("All three volumes required.");
        }
        public ExerciseVolume GetVolume(TrainingVolume volume)
        {
            if (!Enum.IsDefined(typeof(TrainingVolume), volume)) throw new ArgumentOutOfRangeException(nameof(volume));
            return volumes[(int)volume];
        }
    }

    public sealed class AnchorEligibility
    {
        public int CurrentDay { get; }
        private readonly HashSet<string> available;
        private readonly HashSet<string> announcedTomorrow;
        public AnchorEligibility(int currentDay, IEnumerable<string> availableNow, IEnumerable<string> announcedForTomorrow)
        {
            if (currentDay < 1) throw new ArgumentOutOfRangeException(nameof(currentDay));
            CurrentDay = currentDay;
            available = new HashSet<string>(availableNow ?? throw new ArgumentNullException(nameof(availableNow)), StringComparer.Ordinal);
            announcedTomorrow = new HashSet<string>(announcedForTomorrow ?? throw new ArgumentNullException(nameof(announcedForTomorrow)), StringComparer.Ordinal);
            if (available.Any(string.IsNullOrWhiteSpace) || announcedTomorrow.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Anchor identity cannot be empty.");
        }
        public bool Allows(string anchorId, int effectiveDay) => !string.IsNullOrWhiteSpace(anchorId) &&
            (available.Contains(anchorId) || (effectiveDay == CurrentDay + 1 && announcedTomorrow.Contains(anchorId)));
    }

    public sealed class PlannedExercise
    {
        public string EntryId { get; }
        public string ExerciseId { get; }
        public string ExerciseVersion { get; }
        public int TargetSets { get; }
        public int TargetRepetitions { get; }
        internal PlannedExercise(PlanEntryDraft entry, ExercisePlanDefinition definition, TrainingVolume volume)
        {
            EntryId = entry.EntryId; ExerciseId = entry.ExerciseId; ExerciseVersion = definition.Version;
            var amount = definition.GetVolume(volume); TargetSets = amount.Sets; TargetRepetitions = amount.Repetitions;
        }
        internal PlannedExercise(string entryId, string exerciseId, string version, int sets, int repetitions)
        { EntryId = entryId; ExerciseId = exerciseId; ExerciseVersion = version; TargetSets = sets; TargetRepetitions = repetitions; }
    }

    public sealed class PlanSlot
    {
        public int Index { get; }
        public bool IsRest { get; }
        public string AnchorId { get; }
        public TrainingCombination Combination { get; }
        public TrainingVolume Volume { get; }
        public IReadOnlyList<PlannedExercise> Entries { get; }
        internal PlanSlot(PlanSlotDraft slot, IExercisePlanCatalog catalog)
        {
            Index = slot.Index; IsRest = slot.IsRest; AnchorId = slot.AnchorId;
            Combination = slot.Combination; Volume = slot.Volume;
            Entries = Array.AsReadOnly(slot.Entries.Select(e =>
            {
                if (!catalog.TryGet(e.ExerciseId, out var definition)) throw new ArgumentException("Catalog changed during preparation.");
                return new PlannedExercise(e, definition, slot.Volume);
            }).ToArray());
        }
        internal PlanSlot(int index, bool isRest, string anchorId, TrainingCombination combination,
            TrainingVolume volume, IEnumerable<PlannedExercise> entries)
        {
            Index = index; IsRest = isRest; AnchorId = anchorId; Combination = combination; Volume = volume;
            Entries = Array.AsReadOnly(entries.Select(e => new PlannedExercise(e.EntryId, e.ExerciseId,
                e.ExerciseVersion, e.TargetSets, e.TargetRepetitions)).ToArray());
        }
    }

    public sealed class PlanRevision
    {
        public string PlanId { get; }
        public long Revision { get; }
        public int EffectiveDay { get; }
        public string ContentVersion { get; }
        public string ExerciseCatalogVersion { get; }
        public PlanMode Mode { get; }
        public IReadOnlyList<PlanSlot> Slots { get; }
        internal PlanRevision(string planId, long revision, int effectiveDay, PlanDraft draft, IExercisePlanCatalog catalog)
        {
            PlanId = planId; Revision = revision; EffectiveDay = effectiveDay; Mode = draft.Mode;
            ContentVersion = PlanCatalog.ContentVersion; ExerciseCatalogVersion = catalog.Version;
            Slots = Array.AsReadOnly(draft.Slots.OrderBy(x => x.Index).Select(x => new PlanSlot(x, catalog)).ToArray());
        }
    }

    public sealed class PlanState
    {
        public string RunId { get; }
        public string ActorId { get; }
        public string PlanId { get; }
        public long Revision { get; }
        public int AppliedDay { get; }
        public PlanRevision Active { get; }
        public PlanRevision Pending { get; }
        public PlanState(string runId, string actorId, string planId)
            : this(runId, actorId, planId, 0, 0, null, null) { }
        internal PlanState(string runId, string actorId, string planId, long revision, int appliedDay, PlanRevision active, PlanRevision pending)
        {
            if (new[] { runId, actorId, planId }.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Plan owner identity required.");
            RunId = runId; ActorId = actorId; PlanId = planId; Revision = revision;
            AppliedDay = appliedDay; Active = active; Pending = pending;
        }
    }

    public sealed class DayPlan
    {
        public string RunId { get; }
        public string ActorId { get; }
        public int Day { get; }
        public long PlanRevision { get; }
        public int EffectiveDay { get; }
        public string PlanId { get; }
        public string ExerciseCatalogVersion { get; }
        public PlanSlot Slot { get; }
        internal DayPlan(PlanState state, int day, PlanSlot slot)
        { RunId = state.RunId; ActorId = state.ActorId; Day = day; PlanRevision = state.Active.Revision; EffectiveDay = state.Active.EffectiveDay; Slot = slot;
            PlanId = state.PlanId; ExerciseCatalogVersion = state.Active.ExerciseCatalogVersion; }
    }
}
