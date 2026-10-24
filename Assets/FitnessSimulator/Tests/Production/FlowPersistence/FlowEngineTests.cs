using System;
using System.Collections.Generic;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class FlowEngineTests
    {
        private static FlowDefinition Definition(bool choice = false)
        {
            return new FlowDefinition { Id = "fixture", Version = "M1Baseline",
                Nodes = new List<FlowNode> {
                    new FlowNode { Id = "a", DefinitionId = "event-a", Handler = "text", NextNodeId = "b", RequiredNext = true },
                    new FlowNode { Id = "b", DefinitionId = "event-b", Handler = "text" },
                    new FlowNode { Id = "default", DefinitionId = "default", Handler = "text" } },
                Entries = new List<EntryDefinition> {
                    new EntryDefinition { DefinitionId = "event-a", RootNodeId = "a", IsChoice = choice },
                    new EntryDefinition { DefinitionId = "event-b", RootNodeId = "b", IsChoice = choice } },
                DefaultEntry = new EntryDefinition { DefinitionId = "default", RootNodeId = "default" } };
        }
        private static void Resolve(FlowState state, FlowDefinition definition, int day = 1, string anchor = "morning")
        { FlowEngine.ResolveAnchor(state, definition, "player", day, anchor, 1, day + "/" + anchor, c => true, t => true); }

        [Test] public void ChoiceDisplayDoesNotCountStartAndRestoredStartCountsOnce()
        {
            var state = new FlowState(); Resolve(state, Definition(true));
            Assert.That(state.EntryPhase, Is.EqualTo(EntryPhase.ChoicePending)); Assert.That(state.TriggerStats, Is.Empty);
            FlowEngine.ConfirmChoice(state, state.ChoiceToken, "event-a", c => true, t => true);
            Assert.That(state.TriggerStats, Is.Empty);
            Assert.That(FlowEngine.StartCurrent(state), Is.True);
            var codec = new UnityJsonStateCodec(); state = codec.Decode<FlowState>(codec.Encode(state));
            Assert.That(FlowEngine.StartCurrent(state), Is.False);
            Assert.That(state.TriggerStats[0].Count, Is.EqualTo(1)); Assert.That(state.Occupancies.Count, Is.EqualTo(1));
            FlowEngine.CompleteCurrent(state, c => true);
            Assert.That(FlowEngine.Current(state).NodeId, Is.EqualTo("b"));
            Assert.That(FlowEngine.Current(state).Phase, Is.EqualTo(NodePhase.NotStarted));
        }

        [Test] public void ResolvedAnchorNeverReRollsOnQueryOrReload()
        {
            var state = new FlowState(); Resolve(state, Definition());
            long draws = state.RandomDrawIndex; string selected = state.SelectedDefinitionId;
            Resolve(state, Definition());
            Assert.That(state.RandomDrawIndex, Is.EqualTo(draws)); Assert.That(state.SelectedDefinitionId, Is.EqualTo(selected));
        }

        [Test] public void CooldownSkipsTwoFullDaysAndDefaultRemainsUnlimited()
        {
            var definition = Definition(); definition.Entries.RemoveAt(1); definition.Entries[0].CooldownDays = 2;
            var state = new FlowState(); Resolve(state, definition, 3); FlowEngine.StartCurrent(state);
            FlowEngine.CompleteCurrent(state, c => true); FlowEngine.StartCurrent(state); FlowEngine.CompleteCurrent(state, c => true);
            for (int day = 4; day <= 6; day++)
            {
                Resolve(state, definition, day);
                Assert.That(state.SelectedDefinitionId, Is.EqualTo(day == 6 ? "event-a" : "default"));
                FlowEngine.StartCurrent(state); FlowEngine.CompleteCurrent(state, c => true);
            }
            Assert.That(state.TriggerStats.Find(s => s.DefinitionId == "event-a").Count, Is.EqualTo(2));
        }

        [Test] public void ExplicitConsumptionDoesNotOccupyInterveningMeal()
        {
            var definition = Definition(); definition.Entries.RemoveAt(1);
            definition.Entries[0].ExplicitConsumption = true;
            definition.Entries[0].ConsumeTargets = new List<AnchorTarget> {
                new AnchorTarget { Day = 1, AnchorId = "morning", AnchorOrder = 1 },
                new AnchorTarget { Day = 1, AnchorId = "afternoon", AnchorOrder = 4 } };
            var state = new FlowState(); Resolve(state, definition); FlowEngine.StartCurrent(state);
            Assert.That(state.Occupancies.Count, Is.EqualTo(2)); Assert.That(state.Occupancies.Exists(o => o.AnchorId == "lunch"), Is.False);
        }

        [Test] public void InvalidRequiredSuccessorCannotFallBackToDefault()
        {
            var definition = Definition(); definition.Nodes[0].NextNodeId = "missing";
            Assert.Throws<InvalidOperationException>(() => Resolve(new FlowState(), definition));
        }

        [Test] public void DynamicManifestUsesSameCursorAndKeepsFrozenPlan()
        {
            var definition = Definition(true); definition.Dynamic = true; definition.FrozenPlanVersion = "plan-7";
            var state = new FlowState(); Resolve(state, definition);
            FlowEngine.ConfirmChoice(state, state.ChoiceToken, "event-a", c => true, t => true); FlowEngine.StartCurrent(state);
            definition.FrozenPlanVersion = "changed-after-start";
            FlowEngine.CompleteCurrent(state, c => true);
            var codec = new UnityJsonStateCodec(); state = codec.Decode<FlowState>(codec.Encode(state));
            Assert.That(state.Tree.FrozenPlanVersion, Is.EqualTo("plan-7")); Assert.That(state.CurrentNodeId, Is.EqualTo("b"));
            Assert.That(state.Cursors[0].Phase, Is.EqualTo(NodePhase.Completed));
        }

        [Test] public void DayStepsResumeRemainingWorkAndWaitForClick()
        {
            var cursor = new DayStepCursor { Day = 3, Phase = "day-end", StepIds = new List<string> { "income", "recover", "shortage" } };
            Assert.That(cursor.Complete("income"), Is.True);
            var codec = new UnityJsonStateCodec(); cursor = codec.Decode<DayStepCursor>(codec.Encode(cursor));
            Assert.That(cursor.Complete("income"), Is.False); Assert.That(cursor.CurrentStep, Is.EqualTo("recover"));
            Assert.Throws<InvalidOperationException>(() => cursor.Complete("shortage"));
            cursor.Complete("recover"); cursor.Complete("shortage");
            Assert.That(cursor.AwaitingNextDay, Is.True); Assert.That(cursor.Day, Is.EqualTo(3));
        }

        [TestCase("enabled", true)]
        [TestCase("condition", true)]
        [TestCase("consumption", true)]
        [TestCase("consumption", false)]
        public void ConflictingSharedEntryDefinitionsAreRejectedBeforeSelection(string difference, bool choice)
        {
            var definition = Definition(choice);
            var duplicate = new EntryDefinition { DefinitionId = "event-a", RootNodeId = "a", IsChoice = choice };
            if (difference == "enabled") duplicate.Enabled = false;
            if (difference == "condition") duplicate.ConditionKey = "different-condition";
            if (difference == "consumption")
            {
                duplicate.ExplicitConsumption = true;
                duplicate.ConsumeTargets.Add(new AnchorTarget { Day = 1, AnchorId = "afternoon", AnchorOrder = 4 });
            }
            definition.Entries.Insert(0, duplicate);
            var state = new FlowState();
            Assert.Throws<InvalidOperationException>(() => Resolve(state, definition));
            Assert.That(state.TriggerStats, Is.Empty); Assert.That(state.Occupancies, Is.Empty);
        }

        [Test] public void IdenticalSharedEntriesDeduplicateAndStartTheSameCanonicalDefinition()
        {
            var definition = Definition(true);
            definition.Entries.Insert(0, new EntryDefinition { DefinitionId = "event-a", RootNodeId = "a", IsChoice = true });
            var state = new FlowState(); Resolve(state, definition);
            Assert.That(state.ChoiceDefinitionIds, Is.EqualTo(new[] { "event-a", "event-b" }));
            FlowEngine.ConfirmChoice(state, state.ChoiceToken, "event-a", c => true, t => true);
            FlowEngine.StartCurrent(state);
            Assert.That(state.TriggerStats.Find(s => s.DefinitionId == "event-a").Count, Is.EqualTo(1));
            Assert.That(state.Occupancies.Count, Is.EqualTo(1));
        }

        [Test] public void UnresolvedFlowJsonRoundTripDoesNotMaterializeAnActiveTree()
        {
            var codec = new UnityJsonStateCodec();
            var state = codec.Decode<FlowState>(codec.Encode(new FlowState()));
            FlowEngine.NormalizeAfterLoad(state);
            Assert.That(state.HasTree, Is.False); Assert.That(state.Tree, Is.Null);
            Resolve(state, Definition(true)); Assert.That(state.HasTree, Is.True);
            state = codec.Decode<FlowState>(codec.Encode(state));
            FlowEngine.NormalizeAfterLoad(state);
            Assert.That(state.EntryPhase, Is.EqualTo(EntryPhase.ChoicePending));
        }

        [Test] public void NormalizeDoesNotHideConflictingOrMissingTreeReferences()
        {
            Assert.Throws<InvalidOperationException>(() => FlowEngine.NormalizeAfterLoad(new FlowState { Tree = Definition() }));
            var state = new FlowState(); Resolve(state, Definition()); state.CurrentNodeId = "missing";
            Assert.Throws<InvalidOperationException>(() => FlowEngine.NormalizeAfterLoad(state));
        }
    }
}
