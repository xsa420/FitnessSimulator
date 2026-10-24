using System;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Planning
{
    // Actual engine serializer regression. Intentionally excluded from the standalone .NET runner.
    public sealed class PlanJsonUtilityTests
    {
        private static readonly string[] TrainingAnchors = { "early", "morning", "noon", "afternoon", "evening", "late" };
        private static PlanState Initial()
        {
            return PlanRules.SaveInitial(new PlanState("run", "player", "plan"), PlanCatalog.CreateBenchPressSlice(),
                1, 0, new AnchorEligibility(1, new[] { "noon" }, Array.Empty<string>()), new TrainingCatalogAdapter());
        }
        private static PlanStateRecord RoundTrip(PlanStateRecord record) => JsonUtility.FromJson<PlanStateRecord>(JsonUtility.ToJson(record));
        private static PlanState Restore(PlanStateRecord record) => PlanPersistence.Restore(record, new TrainingCatalogAdapter(), TrainingAnchors);

        [Test] public void EmptyPlanSurvivesActualJsonUtilityInlineNullMaterialization()
        {
            var record = RoundTrip(PlanPersistence.Export(new PlanState("run", "player", "plan")));
            Assert.That(record.HasActive, Is.False);
            Assert.That(record.HasPending, Is.False);
            var restored = Restore(record);
            Assert.That(restored.Active, Is.Null);
            Assert.That(restored.Pending, Is.Null);
            Assert.That(restored.Revision, Is.Zero);
            Assert.That(restored.AppliedDay, Is.Zero);
        }

        [Test] public void ActivePlanWithAbsentPendingSurvivesActualJsonUtility()
        {
            var record = RoundTrip(PlanPersistence.Export(Initial()));
            Assert.That(record.HasActive, Is.True);
            Assert.That(record.HasPending, Is.False);
            var restored = Restore(record);
            Assert.That(restored.Pending, Is.Null);
            Assert.That(PlanRules.ResolveDay(restored, 1, DayOfWeek.Monday).Slot.Entries[0].ExerciseId, Is.EqualTo("bench_press"));
        }

        [Test] public void PendingPlanSurvivesJsonUtilityAndIsAbsentAfterApplyAndResave()
        {
            var state = Initial();
            state = PlanRules.SavePending(state, PlanCatalog.CreateDefault(PlanMode.NaturalWeek), 1, state.Revision,
                new AnchorEligibility(1, new[] { "noon" }, Array.Empty<string>()), new TrainingCatalogAdapter());
            var record = RoundTrip(PlanPersistence.Export(state));
            Assert.That(record.HasPending, Is.True);
            var restored = Restore(record);
            Assert.That(restored.Pending.EffectiveDay, Is.EqualTo(2));
            var applied = Restore(RoundTrip(PlanPersistence.Export(PlanRules.ApplyDay(restored, 2))));
            Assert.That(applied.Pending, Is.Null);
            Assert.That(PlanRules.ResolveDay(applied, 2, DayOfWeek.Tuesday).Slot.Entries[0].ExerciseId, Is.EqualTo("lat_pulldown"));
        }

        [Test] public void PresenceFlagsCannotHideRealPlanPayloadAndOldSchemaIsRejected()
        {
            var record = RoundTrip(PlanPersistence.Export(Initial()));
            record.HasActive = false;
            Assert.Throws<ArgumentException>(() => Restore(record));
            record = RoundTrip(PlanPersistence.Export(Initial()));
            record.SchemaVersion = 1;
            Assert.Throws<InvalidOperationException>(() => Restore(record));
            record = PlanPersistence.Export(Initial());
            record.Active = null;
            Assert.Throws<ArgumentException>(() => Restore(record));
        }
    }
}
