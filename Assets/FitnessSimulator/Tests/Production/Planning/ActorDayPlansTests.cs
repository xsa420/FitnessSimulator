using System;
using System.Linq;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Planning
{
    public sealed class ActorDayPlansTests
    {
        private static readonly string[] Known = { "early", "morning", "noon", "afternoon", "evening", "late" };
        private static AnchorEligibility Eligible(int day) => new AnchorEligibility(day, new[] { "noon", "afternoon", "evening" }, Array.Empty<string>());
        private static PlanState State(string actor, PlanDraft draft = null, string run = "run") => PlanRules.SaveInitial(
            new PlanState(run, actor, actor + "-base"), draft ?? PlanCatalog.CreateDefault(PlanMode.NaturalWeek), 1, 0, Eligible(1), new TrainingCatalogAdapter());
        private static DayPlan Player(PlanState state) => PlanRules.ResolveDay(state, state.AppliedDay, (DayOfWeek)(state.AppliedDay % 7));
        private static ActorDayArrangement Follow(PlanState npc, PlanState player) => ActorDayPlans.FollowPlayer(npc, Player(player), "binding-1", Eligible(npc.AppliedDay), Known);
        private static ActorDayArrangement Restore(ActorDayArrangementRecord r) => ActorDayArrangementPersistence.Restore(r, "run", "npc", r.Day, new TrainingCatalogAdapter(), Known);

        [Test] public void FourPresetWeeksMatchLifeScheduleAndReuseAllActionDefinitions()
        {
            var expected = new[] { new[] { 0,1,3,5 }, new[] { 0,2,5 }, new[] { 1,3,5 }, new[] { 0,2,4,6 } };
            var combos = new[] {
                new[] { TrainingCombination.ChestBiceps, TrainingCombination.BackTriceps, TrainingCombination.LegsGlutes, TrainingCombination.Shoulders },
                new[] { TrainingCombination.ChestBiceps, TrainingCombination.LegsGlutes, TrainingCombination.BackTriceps },
                new[] { TrainingCombination.BackTriceps, TrainingCombination.LegsGlutes, TrainingCombination.Shoulders },
                new[] { TrainingCombination.ChestBiceps, TrainingCombination.BackTriceps, TrainingCombination.LegsGlutes, TrainingCombination.Shoulders } };
            foreach (NpcPlanPreset preset in Enum.GetValues(typeof(NpcPlanPreset)))
            {
                var draft = PlanCatalog.CreateNpcDefault(preset);
                var training = draft.Slots.Where(s => !s.IsRest).ToArray();
                Assert.That(draft.Mode, Is.EqualTo(PlanMode.NaturalWeek));
                Assert.That(training.Select(s => s.Index), Is.EqualTo(expected[(int)preset]));
                Assert.That(training.Select(s => s.Combination), Is.EqualTo(combos[(int)preset]));
                Assert.That(training.All(s => s.AnchorId == ((int)preset >= 2 ? "evening" : "noon")), Is.True);
                Assert.That(training.All(s => s.Volume == (preset == NpcPlanPreset.Beginner ? TrainingVolume.Low : TrainingVolume.Balanced)), Is.True);
                Assert.That(PlanRules.Validate(draft, 1, Eligible(1), new TrainingCatalogAdapter()), Is.Empty);
            }
            Assert.That(PlanCatalog.CreateNpcDefault(NpcPlanPreset.Rival).Slots.SelectMany(s => s.Entries).Select(e => e.ExerciseId).Distinct().Count(), Is.EqualTo(8));
        }

        [Test] public void FollowCopiesFrozenContentsButKeepsTargetIdentityAndBaseState()
        {
            var npc = State("npc", PlanCatalog.CreateNpcDefault(NpcPlanPreset.Veteran)); var player = State("player");
            var originalActive = npc.Active;
            var result = Follow(npc, player);
            Assert.That(result.ActorId, Is.EqualTo("npc")); Assert.That(result.RunId, Is.EqualTo(npc.RunId));
            Assert.That(result.SourceActorId, Is.EqualTo("player")); Assert.That(result.SourcePlanId, Is.EqualTo(player.PlanId));
            Assert.That(result.BasePlanId, Is.EqualTo(npc.PlanId)); Assert.That(result.HasCompanionSource, Is.True);
            Assert.That(result.Slot.Entries.Select(e => e.ExerciseId), Is.EqualTo(new[] { "bench_press", "dumbbell_curl" }));
            Assert.That(result.Slot.Entries[0].TargetSets, Is.EqualTo(3)); Assert.That(result.Slot.Entries[0].TargetRepetitions, Is.EqualTo(8));
            Assert.That(result.Slot, Is.Not.SameAs(Player(player).Slot));
            Assert.That(npc.Active, Is.SameAs(originalActive)); Assert.That(npc.Pending, Is.Null); Assert.That(npc.Active.Slots[0].IsRest, Is.True);
            var mutable = TrainingCatalogAdapter.ToTrainingEntries(result); mutable[0].Sets = 99;
            Assert.That(result.Slot.Entries[0].TargetSets, Is.EqualTo(3));
        }

        [Test] public void N02PendingActivationPrecedesFollowAndFutureEditsDoNotMutateSnapshot()
        {
            var player = State("player"); var npc = State("npc");
            var draft = PlanCatalog.CreateBenchPressSlice(); draft.Slots[0].Volume = TrainingVolume.High; draft.Slots[0].AnchorId = "evening";
            player = PlanRules.SavePending(player, draft, 1, player.Revision, Eligible(1), new TrainingCatalogAdapter());
            player = PlanRules.ApplyDay(player, 2); npc = PlanRules.ApplyDay(npc, 2);
            var arrangement = Follow(npc, player);
            Assert.That(arrangement.SourceRevision, Is.EqualTo(player.Active.Revision)); Assert.That(arrangement.SourceEffectiveDay, Is.EqualTo(2));
            Assert.That(arrangement.Slot.AnchorId, Is.EqualTo("evening")); Assert.That(arrangement.Slot.Entries[0].TargetRepetitions, Is.EqualTo(10));
            player = PlanRules.SavePending(player, PlanCatalog.CreateDefault(PlanMode.NaturalWeek), 2, player.Revision, Eligible(2), new TrainingCatalogAdapter());
            Assert.That(arrangement.Slot.Entries.Count, Is.EqualTo(1)); Assert.That(arrangement.Slot.Entries[0].TargetSets, Is.EqualTo(4));
        }

        [Test] public void UnbindingResolvesUntouchedBaseOnCorrectWeekdayAfterMultipleDays()
        {
            var npc = State("npc", PlanCatalog.CreateNpcDefault(NpcPlanPreset.Veteran)); var player = State("player");
            Follow(npc, player);
            npc = PlanRules.ApplyDay(npc, 2); player = PlanRules.ApplyDay(player, 2); Follow(npc, player);
            npc = PlanRules.ApplyDay(npc, 3); player = PlanRules.ApplyDay(player, 3); Follow(npc, player);
            npc = PlanRules.ApplyDay(npc, 4);
            var restoredBase = ActorDayPlans.ResolveBase(npc, 4, DayOfWeek.Thursday, Eligible(4), Known);
            Assert.That(restoredBase.HasCompanionSource, Is.False); Assert.That(restoredBase.BindingId, Is.Empty);
            Assert.That(restoredBase.SourceActorId, Is.EqualTo("npc")); Assert.That(restoredBase.Slot.AnchorId, Is.EqualTo("evening"));
            Assert.That(restoredBase.Slot.Entries.Select(e => e.ExerciseId), Is.EqualTo(new[] { "squat", "hip_thrust" }));
        }

        [Test] public void PlayerRestProducesRestArrangementAndNoTrainingEntries()
        {
            var rest = new PlanDraft { Mode = PlanMode.DayCycle, Slots = new[] { PlanCatalog.Rest(0) } };
            var value = Follow(State("npc"), State("player", rest));
            Assert.That(value.Slot.IsRest, Is.True); Assert.That(TrainingCatalogAdapter.ToTrainingEntries(value), Is.Empty);
        }

        [Test] public void FollowRejectsWrongOwnerDateRunAndUnavailableOrUnknownAnchor()
        {
            var legacy = PlanCatalog.CreateBenchPressSlice(); legacy.Slots[0].AnchorId = "afternoon";
            var npc = State("npc"); var player = State("player", legacy);
            Assert.Throws<ArgumentException>(() => Follow(npc, State("player", run: "other")));
            Assert.Throws<ArgumentException>(() => Follow(npc, npc));
            Assert.Throws<ArgumentException>(() => Follow(PlanRules.ApplyDay(npc, 2), player));
            Assert.Throws<ArgumentException>(() => ActorDayPlans.FollowPlayer(npc, Player(player), "", Eligible(1), Known));
            Assert.Throws<InvalidOperationException>(() => ActorDayPlans.FollowPlayer(npc, Player(player), "b", new AnchorEligibility(1, new[] { "evening" }, new[] { "afternoon" }), Known));
            Assert.Throws<InvalidOperationException>(() => ActorDayPlans.FollowPlayer(npc, Player(player), "b", Eligible(1), new[] { "evening" }));
            Assert.Throws<ArgumentException>(() => ActorDayPlans.FollowPlayer(npc, Player(player), "b", null, Known));
        }

        [Test] public void SameArrangementUsesNpcOwnTrainingProgressForQuota()
        {
            var value = Follow(State("npc"), State("player", PlanCatalog.CreateBenchPressSlice()));
            var entries = TrainingCatalogAdapter.ToTrainingEntries(value);
            var npcProgress = TrainingProgress.CreateInitial("npc"); npcProgress.Exercise("bench_press").HighestUnlockedTier = 2;
            var quota = DayTrainingQuota.Freeze("npc", 1, "arrangement/run/npc/1", entries, npcProgress);
            Assert.That(quota.ActorId, Is.EqualTo("npc")); Assert.That(quota.Entries[0].FrozenTier, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => DayTrainingQuota.Freeze("npc", 1, "arrangement/run/npc/1", entries, TrainingProgress.CreateInitial("player")));
        }

        [Test] public void ArrangementRecordCopiesValuesAndRejectsTamperedOwnerAnchorAndWorkload()
        {
            var value = Follow(State("npc"), State("player")); var record = ActorDayArrangementPersistence.Export(value);
            var restored = Restore(record); record.Entries[0].TargetSets = 99;
            Assert.That(restored.Slot.Entries[0].TargetSets, Is.EqualTo(3)); Assert.Throws<ArgumentException>(() => Restore(record));
            record = ActorDayArrangementPersistence.Export(value); record.AnchorId = "breakfast";
            Assert.Throws<PlanValidationException>(() => Restore(record));
            record = ActorDayArrangementPersistence.Export(value); record.ActorId = "player";
            Assert.Throws<ArgumentException>(() => Restore(record));
            record = ActorDayArrangementPersistence.Export(value); record.HasCompanionSource = false;
            Assert.Throws<ArgumentException>(() => Restore(record));
            record = ActorDayArrangementPersistence.Export(value); record.Entries[0].ExerciseVersion = "future";
            Assert.Throws<InvalidOperationException>(() => Restore(record));
        }
    }
}
