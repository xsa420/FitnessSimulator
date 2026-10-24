using System;
using System.Linq;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Planning
{
    public sealed class PlanningTests
    {
        private static TrainingCatalogAdapter Catalog() => new TrainingCatalogAdapter();
        private static readonly string[] KnownTrainingAnchors = { "early", "morning", "noon", "afternoon", "evening", "late" };
        private static AnchorEligibility Anchors(int day) => new AnchorEligibility(day,
            new[] { "morning", "noon", "afternoon", "evening" }, Array.Empty<string>());
        private static PlanState Initial(PlanDraft draft = null) => PlanRules.SaveInitial(
            new PlanState("run", "player", "plan"), draft ?? PlanCatalog.CreateDefault(PlanMode.NaturalWeek), 1, 0, Anchors(1), Catalog());
        private static PlanState ToDay(PlanState state, int day)
        { while (state.AppliedDay < day) state = PlanRules.ApplyDay(state, state.AppliedDay + 1); return state; }

        [Test] public void DefaultTemplateDoesNotGrantLockedTemplatesOrCarryProgress()
        {
            var intent = TemplateCatalog.PrepareInitialization(TemplateCatalog.DefaultId, Array.Empty<string>(), "new-run", "player");
            Assert.That(intent.Template.UniformStrength, Is.EqualTo(10));
            Assert.That(intent.Template.BodyFatPercent, Is.EqualTo(25));
            Assert.That(intent.Template.BaseCharm, Is.EqualTo(50));
            Assert.That(intent.Template.MentalStress, Is.EqualTo(10));
            Assert.That(intent.Template.Balance, Is.EqualTo(300));
            Assert.That(intent.Template.Affluence, Is.Zero);
            Assert.That(TemplateCatalog.All.Count, Is.EqualTo(3));
            Assert.Throws<InvalidOperationException>(() => TemplateCatalog.PrepareInitialization(TemplateCatalog.StrengthId, Array.Empty<string>(), "r", "p"));
            Assert.Throws<ArgumentException>(() => TemplateCatalog.PrepareInitialization("unknown_template", Array.Empty<string>(), "r", "p"));
            Assert.That(TemplateCatalog.PrepareInitialization(TemplateCatalog.StrengthId, new[] { TemplateCatalog.StrengthId }, "r", "p").Template.UniformStrength, Is.EqualTo(13));
            Assert.That(TemplateCatalog.PrepareInitialization(TemplateCatalog.SocialId, new[] { TemplateCatalog.SocialId }, "r", "p").Template.Balance, Is.EqualTo(360));
        }

        [Test] public void DefaultPlanKeepsAllProductActionsWhileSliceIsExplicit()
        {
            var state = Initial(); var day = PlanRules.ResolveDay(state, 1, DayOfWeek.Monday);
            Assert.That(day.Slot.Entries.Select(x => x.ExerciseId), Is.EqualTo(new[] { "bench_press", "dumbbell_curl" }));
            Assert.That(day.Slot.Entries[0].TargetSets, Is.EqualTo(3));
            Assert.That(day.Slot.Entries[0].TargetRepetitions, Is.EqualTo(8));
            Assert.That(state.Active.Slots.SelectMany(x => x.Entries).Select(x => x.ExerciseId).Distinct().Count(), Is.EqualTo(8));
            Assert.That(PlanCatalog.GetIntegrationIssues(day, new[] { "bench_press" }).Single().Code, Is.EqualTo("ExerciseNotIntegrated"));
            var slice = PlanRules.ResolveDay(Initial(PlanCatalog.CreateBenchPressSlice()), 1, DayOfWeek.Monday);
            Assert.That(PlanCatalog.GetIntegrationIssues(slice, new[] { "bench_press" }), Is.Empty);
            Assert.That(TrainingCatalogAdapter.ToTrainingEntries(slice).Single().Reps, Is.EqualTo(8));
        }

        [Test] public void NewDefaultsUseNoonAndSelectedEveningAppliesOnlyTomorrow()
        {
            foreach (var draft in new[] { PlanCatalog.CreateDefault(PlanMode.NaturalWeek),
                PlanCatalog.CreateDefault(PlanMode.DayCycle), PlanCatalog.CreateBenchPressSlice() })
            {
                Assert.That(draft.Slots.Where(s => !s.IsRest).All(s => s.AnchorId == "noon"), Is.True);
                Assert.That(draft.Slots.Where(s => s.IsRest).All(s => string.IsNullOrEmpty(s.AnchorId)), Is.True);
            }
            var editing = new AnchorEligibility(1, new[] { "noon", "evening" }, Array.Empty<string>());
            var state = PlanRules.SaveInitial(new PlanState("run", "player", "plan"),
                PlanCatalog.CreateBenchPressSlice(), 1, 0, editing, Catalog());
            var evening = PlanCatalog.CreateBenchPressSlice(); evening.Slots[0].AnchorId = "evening";
            var pending = PlanRules.SavePending(state, evening, 1, state.Revision, editing, Catalog());
            Assert.That(PlanRules.ResolveDay(pending, 1, DayOfWeek.Monday).Slot.AnchorId, Is.EqualTo("noon"));
            var restored = PlanPersistence.Restore(PlanPersistence.Export(pending), Catalog(), KnownTrainingAnchors);
            Assert.That(PlanRules.ResolveDay(PlanRules.ApplyDay(restored, 2), 2, DayOfWeek.Tuesday).Slot.AnchorId, Is.EqualTo("evening"));
        }

        [Test] public void LegacyActivePendingAndCompanionArrangementKeepOriginalAnchors()
        {
            foreach (string anchor in new[] { "morning", "afternoon" })
            {
                var legacy = PlanCatalog.CreateBenchPressSlice(); legacy.Slots[0].AnchorId = anchor;
                var original = Initial(legacy);
                original = PlanRules.SavePending(original, legacy, 1, original.Revision, Anchors(1), Catalog());
                var restored = PlanPersistence.Restore(PlanPersistence.Export(original), Catalog(), KnownTrainingAnchors);
                Assert.That(restored.Active.Slots[0].AnchorId, Is.EqualTo(anchor));
                Assert.That(restored.Pending.Slots[0].AnchorId, Is.EqualTo(anchor));
                var npc = PlanRules.SaveInitial(new PlanState("run", "npc", "npc-plan"),
                    PlanCatalog.CreateNpcDefault(NpcPlanPreset.Rival), 1, 0, Anchors(1), Catalog());
                var following = ActorDayPlans.FollowPlayer(npc, PlanRules.ResolveDay(restored, 1, DayOfWeek.Monday),
                    "binding", Anchors(1), KnownTrainingAnchors);
                var frozen = ActorDayArrangementPersistence.Restore(ActorDayArrangementPersistence.Export(following),
                    "run", "npc", 1, Catalog(), KnownTrainingAnchors);
                Assert.That(frozen.Slot.AnchorId, Is.EqualTo(anchor));
                Assert.That(npc.Active.Slots[0].AnchorId, Is.EqualTo("noon"));
                Assert.That(PlanRules.ResolveDay(PlanRules.ApplyDay(restored, 2), 2, DayOfWeek.Tuesday).Slot.AnchorId, Is.EqualTo(anchor));
                var editing = new AnchorEligibility(1, new[] { "noon", "evening" }, Array.Empty<string>());
                Assert.Throws<PlanValidationException>(() => PlanRules.SavePending(restored, legacy, 1, restored.Revision, editing, Catalog()));
                Assert.That(restored.Active.Slots[0].AnchorId, Is.EqualTo(anchor));
                Assert.That(frozen.Slot.AnchorId, Is.EqualTo(anchor));
            }
        }

        [Test] public void LastPendingSaveWinsWithoutChangingCurrentDayOrDraftAliasing()
        {
            var state = ToDay(Initial(), 3);
            var pendingCycle = PlanRules.SavePending(state, PlanCatalog.CreateDefault(PlanMode.DayCycle), 3, state.Revision, Anchors(3), Catalog());
            var draft = PlanCatalog.CreateDefault(PlanMode.NaturalWeek);
            var saved = PlanRules.SavePending(pendingCycle, draft, 3, pendingCycle.Revision, Anchors(3), Catalog());
            draft.Slots[3].Entries[0].ExerciseId = "bench_press";
            Assert.That(saved.Active, Is.SameAs(state.Active));
            Assert.That(saved.Pending.Mode, Is.EqualTo(PlanMode.NaturalWeek));
            Assert.That(saved.Pending.Slots[3].Entries[0].ExerciseId, Is.EqualTo("squat"));
            Assert.That(PlanRules.ResolveDay(saved, 3, DayOfWeek.Wednesday).Slot.IsRest, Is.True);
            var tomorrow = PlanRules.ApplyDay(saved, 4);
            Assert.That(PlanRules.ResolveDay(tomorrow, 4, DayOfWeek.Thursday).Slot.Entries[0].ExerciseId, Is.EqualTo("squat"));
            Assert.That(tomorrow.Pending, Is.Null);
        }

        [Test] public void CycleUsesEffectiveDayAndDoesNotWaitForTrainingCompletion()
        {
            var state = ToDay(Initial(), 3);
            state = PlanRules.SavePending(state, PlanCatalog.CreateDefault(PlanMode.DayCycle), 3, state.Revision, Anchors(3), Catalog());
            state = PlanRules.ApplyDay(state, 4);
            Assert.That(PlanRules.ResolveDay(state, 4, DayOfWeek.Thursday).Slot.Index, Is.Zero);
            Assert.That(PlanRules.ApplyDay(state, 4), Is.SameAs(state));
            state = PlanRules.ApplyDay(state, 5);
            Assert.That(PlanRules.ResolveDay(state, 5, DayOfWeek.Friday).Slot.IsRest, Is.True);
            state = ToDay(state, 10);
            Assert.That(PlanRules.ResolveDay(state, 10, DayOfWeek.Wednesday).Slot.Index, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => PlanRules.ApplyDay(state, 12));
        }

        [Test] public void FullRestAndCycleBoundariesAreLegalButEmptyTrainingIsNot()
        {
            foreach (int length in new[] { 1, 14 })
            {
                var draft = new PlanDraft { Mode = PlanMode.DayCycle, Slots = Enumerable.Range(0, length).Select(PlanCatalog.Rest).ToArray() };
                Assert.That(PlanRules.Validate(draft, 1, Anchors(1), Catalog()), Is.Empty);
                Assert.That(PlanRules.ResolveDay(Initial(draft), 1, DayOfWeek.Monday).Slot.Entries, Is.Empty);
            }
            foreach (int length in new[] { 0, 15 })
                Assert.That(PlanRules.Validate(new PlanDraft { Mode = PlanMode.DayCycle, Slots = Enumerable.Range(0, length).Select(PlanCatalog.Rest).ToArray() }, 1, Anchors(1), Catalog()).Any(x => x.Code == "InvalidLength"), Is.True);
            var week = new PlanDraft { Mode = PlanMode.NaturalWeek, Slots = Enumerable.Range(0, 7).Select(PlanCatalog.Rest).ToArray() };
            Assert.That(PlanRules.Validate(week, 1, Anchors(1), Catalog()), Is.Empty);
            var empty = PlanCatalog.CreateBenchPressSlice(); empty.Slots[0].Entries = Array.Empty<PlanEntryDraft>();
            Assert.That(PlanRules.Validate(empty, 1, Anchors(1), Catalog()).Any(x => x.Code == "InvalidEntryCount"), Is.True);
        }

        [Test] public void DuplicateActionRejectedRegardlessOfEntryIdButCrossDayAllowed()
        {
            var draft = PlanCatalog.CreateBenchPressSlice();
            draft.Slots[0].Entries = new[] { new PlanEntryDraft { EntryId = "a", ExerciseId = "bench_press" }, new PlanEntryDraft { EntryId = "b", ExerciseId = "bench_press" } };
            Assert.That(PlanRules.Validate(draft, 1, Anchors(1), Catalog()).Any(x => x.Code == "DuplicateExercise"), Is.True);
            draft.Slots = new[] { PlanCatalog.Training(0, TrainingCombination.Free, "bench_press"), PlanCatalog.Training(1, TrainingCombination.Free, "bench_press") };
            Assert.That(PlanRules.Validate(draft, 1, Anchors(1), Catalog()), Is.Empty);
        }

        [Test] public void CombinationUsesActualTrainingTargetsRatherThanDefaultNames()
        {
            var draft = PlanCatalog.CreateBenchPressSlice();
            draft.Slots[0] = PlanCatalog.Training(0, TrainingCombination.ChestBiceps, "lat_pulldown");
            Assert.That(PlanRules.Validate(draft, 1, Anchors(1), Catalog()), Is.Empty);
            draft.Slots[0].Entries[0].ExerciseId = "squat";
            Assert.That(PlanRules.Validate(draft, 1, Anchors(1), Catalog()).Any(x => x.Code == "NoTargetIntersection"), Is.True);
            draft.Slots[0] = PlanCatalog.Training(0, TrainingCombination.Free, Catalog().ExerciseIds.ToArray());
            Assert.That(PlanRules.Validate(draft, 1, Anchors(1), Catalog()), Is.Empty);
        }

        [Test] public void AnnouncedAnchorIsOnlyUsableStartingTomorrow()
        {
            var draft = PlanCatalog.CreateBenchPressSlice(); draft.Slots[0].AnchorId = "early";
            var anchors = new AnchorEligibility(3, new[] { "afternoon" }, new[] { "early" });
            Assert.That(PlanRules.Validate(draft, 3, anchors, Catalog()).Any(x => x.Code == "AnchorUnavailable"), Is.True);
            Assert.That(PlanRules.Validate(draft, 4, anchors, Catalog()), Is.Empty);
            Assert.That(PlanRules.Validate(draft, 4, Anchors(3), Catalog()).Any(x => x.Code == "AnchorUnavailable"), Is.True);
        }

        [Test] public void StaleEditAndSecondInitialSaveCannotOverwriteCommittedPlan()
        {
            var state = Initial();
            var next = PlanRules.SavePending(state, PlanCatalog.CreateBenchPressSlice(), 1, state.Revision, Anchors(1), Catalog());
            Assert.Throws<InvalidOperationException>(() => PlanRules.SavePending(next, PlanCatalog.CreateDefault(PlanMode.NaturalWeek), 1, state.Revision, Anchors(1), Catalog()));
            Assert.Throws<InvalidOperationException>(() => PlanRules.SaveInitial(state, PlanCatalog.CreateBenchPressSlice(), 1, state.Revision, Anchors(1), Catalog()));
            Assert.That(state.Pending, Is.Null);
        }

        [Test] public void ShapeErrorsAreLocatedAndMissingPlanIsNotDefaulted()
        {
            Assert.That(PlanRules.Validate(null, 1, Anchors(1), Catalog()).Single().Code, Is.EqualTo("MissingPlan"));
            var week = PlanCatalog.CreateDefault(PlanMode.NaturalWeek); week.Slots[1].Index = 0;
            Assert.That(PlanRules.Validate(week, 1, Anchors(1), Catalog()).Any(x => x.Path == "slots[1].index"), Is.True);
            var invalid = PlanCatalog.CreateBenchPressSlice(); invalid.Slots[0].Entries[0].ExerciseId = "missing";
            Assert.Throws<PlanValidationException>(() => Initial(invalid));
        }

        [Test] public void PersistenceRestoresPendingWithoutSharingMutableRecords()
        {
            var state = Initial(); state = PlanRules.SavePending(state, PlanCatalog.CreateBenchPressSlice(), 1, state.Revision, Anchors(1), Catalog());
            var record = PlanPersistence.Export(state); var restored = PlanPersistence.Restore(record, Catalog(), KnownTrainingAnchors);
            record.Pending.Draft.Slots[0].Entries[0].ExerciseId = "squat";
            Assert.That(restored.Pending.Slots[0].Entries[0].ExerciseId, Is.EqualTo("bench_press"));
            Assert.That(restored.Revision, Is.EqualTo(state.Revision));
            Assert.That(PlanRules.ResolveDay(PlanRules.ApplyDay(restored, 2), 2, DayOfWeek.Tuesday).Slot.Entries.Single().ExerciseId, Is.EqualTo("bench_press"));
        }

        [Test] public void PersistenceRejectsContentDriftAndMalformedPendingBoundary()
        {
            var record = PlanPersistence.Export(Initial()); record.Active.ExerciseCatalogVersion = "future";
            Assert.Throws<InvalidOperationException>(() => PlanPersistence.Restore(record, Catalog(), KnownTrainingAnchors));
            var state = Initial(); state = PlanRules.SavePending(state, PlanCatalog.CreateBenchPressSlice(), 1, state.Revision, Anchors(1), Catalog());
            record = PlanPersistence.Export(state); record.Pending.EffectiveDay = 3;
            Assert.Throws<ArgumentException>(() => PlanPersistence.Restore(record, Catalog(), KnownTrainingAnchors));
        }

        [Test] public void RestoreRejectsUnknownAndNonTrainingAnchorsInActiveAndPending()
        {
            foreach (var anchorId in new[] { "unknown_anchor", "breakfast" })
            {
                var active = PlanPersistence.Export(Initial(PlanCatalog.CreateBenchPressSlice()));
                active.Active.Draft.Slots[0].AnchorId = anchorId;
                var activeError = Assert.Throws<PlanValidationException>(() => PlanPersistence.Restore(active, Catalog(), KnownTrainingAnchors));
                Assert.That(activeError.Issues.Any(x => x.Code == "AnchorUnavailable"), Is.True);
                var state = Initial();
                state = PlanRules.SavePending(state, PlanCatalog.CreateBenchPressSlice(), 1, state.Revision, Anchors(1), Catalog());
                var pending = PlanPersistence.Export(state); pending.Pending.Draft.Slots[0].AnchorId = anchorId;
                Assert.Throws<PlanValidationException>(() => PlanPersistence.Restore(pending, Catalog(), KnownTrainingAnchors));
            }
        }

        [Test] public void RestoreKeepsKnownHistoricalAndPendingUnlockAnchorsWithoutTodaysEligibility()
        {
            var draft = PlanCatalog.CreateBenchPressSlice(); draft.Slots[0].AnchorId = "early";
            var state = PlanRules.SaveInitial(new PlanState("run", "player", "plan"), draft, 1, 0,
                new AnchorEligibility(1, new[] { "early" }, Array.Empty<string>()), Catalog());
            var restored = PlanPersistence.Restore(PlanPersistence.Export(state), Catalog(), KnownTrainingAnchors);
            Assert.That(PlanRules.ResolveDay(restored, 1, DayOfWeek.Monday).Slot.AnchorId, Is.EqualTo("early"));
            var pending = PlanRules.SavePending(Initial(), draft, 1, 1,
                new AnchorEligibility(1, new[] { "afternoon" }, new[] { "early" }), Catalog());
            restored = PlanPersistence.Restore(PlanPersistence.Export(pending), Catalog(), KnownTrainingAnchors);
            Assert.That(PlanRules.ResolveDay(PlanRules.ApplyDay(restored, 2), 2, DayOfWeek.Tuesday).Slot.AnchorId, Is.EqualTo("early"));
            Assert.Throws<ArgumentNullException>(() => PlanPersistence.Restore(PlanPersistence.Export(state), Catalog(), null));
            Assert.Throws<ArgumentException>(() => PlanPersistence.Restore(PlanPersistence.Export(state), Catalog(), Array.Empty<string>()));
        }
    }
}
