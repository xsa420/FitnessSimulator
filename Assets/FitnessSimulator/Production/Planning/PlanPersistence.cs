using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Planning
{
    // Serializable fragment only; physical persistence and atomic root commits belong to Application/Core.
    [Serializable]
    public sealed class PlanStateRecord
    {
        public int SchemaVersion = 2;
        public string RunId, ActorId, PlanId;
        public long Revision;
        public int AppliedDay;
        // JsonUtility materializes inline null classes as default objects. Presence is explicit.
        public bool HasActive, HasPending;
        public PlanRevisionRecord Active, Pending;
    }

    [Serializable]
    public sealed class PlanRevisionRecord
    {
        public long Revision;
        public int EffectiveDay;
        public string ContentVersion, ExerciseCatalogVersion;
        public PlanDraft Draft;
    }

    public static class PlanPersistence
    {
        public static PlanStateRecord Export(PlanState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return new PlanStateRecord { RunId = state.RunId, ActorId = state.ActorId, PlanId = state.PlanId,
                Revision = state.Revision, AppliedDay = state.AppliedDay,
                HasActive = state.Active != null, HasPending = state.Pending != null,
                Active = ExportRevision(state.Active), Pending = ExportRevision(state.Pending) };
        }

        public static PlanState Restore(PlanStateRecord record, IExercisePlanCatalog catalog, IEnumerable<string> knownTrainingAnchorIds)
        {
            if (record == null || catalog == null || knownTrainingAnchorIds == null) throw new ArgumentNullException();
            // Inject the versioned content directory, never ids read from the save or today's unlock set.
            var trainingAnchors = knownTrainingAnchorIds.ToArray();
            if (trainingAnchors.Length == 0 || trainingAnchors.Any(string.IsNullOrWhiteSpace) || trainingAnchors.Distinct(StringComparer.Ordinal).Count() != trainingAnchors.Length)
                throw new ArgumentException("InvalidTrainingAnchorCatalog", nameof(knownTrainingAnchorIds));
            if (record.SchemaVersion != 2) throw new InvalidOperationException("UnsupportedPlanSchema");
            if (record.Revision < 0 || record.AppliedDay < 0) throw new ArgumentException("InvalidPlanState");
            if ((!record.HasActive && HasRevisionData(record.Active)) || (!record.HasPending && HasRevisionData(record.Pending)))
                throw new ArgumentException("PlanPresenceMismatch");
            if ((record.HasActive && record.Active == null) || (record.HasPending && record.Pending == null))
                throw new ArgumentException("MissingPresentPlan");
            if (!record.HasActive)
            {
                if (record.HasPending || record.Revision != 0 || record.AppliedDay != 0) throw new ArgumentException("InvalidEmptyPlanState");
                return new PlanState(record.RunId, record.ActorId, record.PlanId);
            }
            if (record.AppliedDay < record.Active.EffectiveDay || record.AppliedDay < 1 || record.Active.Revision > record.Revision)
                throw new ArgumentException("InvalidActivePlanBoundary");
            if (record.HasPending && (record.Pending.EffectiveDay != record.AppliedDay + 1 ||
                record.Pending.Revision <= record.Active.Revision || record.Pending.Revision != record.Revision))
                throw new ArgumentException("InvalidPendingPlanBoundary");
            return new PlanState(record.RunId, record.ActorId, record.PlanId, record.Revision, record.AppliedDay,
                RestoreRevision(record.PlanId, record.Active, catalog, trainingAnchors),
                record.HasPending ? RestoreRevision(record.PlanId, record.Pending, catalog, trainingAnchors) : null);
        }

        private static bool HasRevisionData(PlanRevisionRecord record) => record != null &&
            (record.Revision != 0 || record.EffectiveDay != 0 || !string.IsNullOrEmpty(record.ContentVersion) ||
             !string.IsNullOrEmpty(record.ExerciseCatalogVersion) ||
             (record.Draft != null && (record.Draft.Mode != PlanMode.NaturalWeek || (record.Draft.Slots != null && record.Draft.Slots.Length != 0))));

        private static PlanRevisionRecord ExportRevision(PlanRevision revision)
        {
            if (revision == null) return null;
            return new PlanRevisionRecord { Revision = revision.Revision, EffectiveDay = revision.EffectiveDay,
                ContentVersion = revision.ContentVersion, ExerciseCatalogVersion = revision.ExerciseCatalogVersion,
                Draft = new PlanDraft { Mode = revision.Mode, Slots = revision.Slots.Select(s => new PlanSlotDraft {
                    Index = s.Index, IsRest = s.IsRest, AnchorId = s.AnchorId, Combination = s.Combination, Volume = s.Volume,
                    Entries = s.Entries.Select(e => new PlanEntryDraft { EntryId = e.EntryId, ExerciseId = e.ExerciseId }).ToArray()
                }).ToArray() } };
        }

        private static PlanRevision RestoreRevision(string planId, PlanRevisionRecord record, IExercisePlanCatalog catalog, string[] trainingAnchors)
        {
            if (record == null) return null;
            if (record.ContentVersion != PlanCatalog.ContentVersion || record.ExerciseCatalogVersion != catalog.Version)
                throw new InvalidOperationException("UnsupportedPlanContentVersion");
            if (record.Revision < 1 || record.EffectiveDay < 1) throw new ArgumentException("InvalidPlanRevision");
            // Historical unlock eligibility was checked at commit. Reference existence and training capability
            // still come from trusted content; a saved string cannot grant itself that capability.
            var errors = PlanRules.Validate(record.Draft, record.EffectiveDay,
                new AnchorEligibility(record.EffectiveDay, trainingAnchors, Array.Empty<string>()), catalog);
            if (errors.Count != 0) throw new PlanValidationException(errors);
            return new PlanRevision(planId, record.Revision, record.EffectiveDay, record.Draft, catalog);
        }
    }
}
