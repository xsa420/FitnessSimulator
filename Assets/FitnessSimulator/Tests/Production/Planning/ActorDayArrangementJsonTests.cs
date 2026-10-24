using System;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Planning
{
    public sealed class ActorDayArrangementJsonTests
    {
        [Test] public void BaseAndCompanionRecordsRoundTripWithActualJsonUtility()
        {
            var catalog = new TrainingCatalogAdapter(); var anchors = new[] { "noon", "evening" };
            var eligibility = new AnchorEligibility(1, anchors, Array.Empty<string>());
            var npc = PlanRules.SaveInitial(new PlanState("r", "npc", "npc-base"), PlanCatalog.CreateNpcDefault(NpcPlanPreset.Veteran), 1, 0, eligibility, catalog);
            var player = PlanRules.SaveInitial(new PlanState("r", "player", "player-plan"), PlanCatalog.CreateBenchPressSlice(), 1, 0, eligibility, catalog);
            var values = new[] { ActorDayPlans.ResolveBase(npc, 1, DayOfWeek.Monday, eligibility, anchors),
                ActorDayPlans.FollowPlayer(npc, PlanRules.ResolveDay(player, 1, DayOfWeek.Monday), "binding-1", eligibility, anchors) };
            foreach (var value in values)
            {
                var record = JsonUtility.FromJson<ActorDayArrangementRecord>(JsonUtility.ToJson(ActorDayArrangementPersistence.Export(value)));
                var restored = ActorDayArrangementPersistence.Restore(record, "r", "npc", 1, catalog, anchors);
                Assert.That(restored.HasCompanionSource, Is.EqualTo(value.HasCompanionSource));
                Assert.That(restored.Slot.IsRest, Is.EqualTo(value.Slot.IsRest));
                Assert.That(restored.Slot.Entries.Count, Is.EqualTo(value.Slot.Entries.Count));
                Assert.That(restored.ActorId, Is.EqualTo("npc"));
                if (restored.HasCompanionSource) {
                    Assert.That(restored.BindingId, Is.EqualTo("binding-1"));
                    Assert.That(restored.Slot.Entries[0].TargetRepetitions, Is.EqualTo(8));
                }
            }
        }
    }
}
