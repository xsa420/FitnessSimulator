using System;
using System.Linq;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life.Relationships;

namespace FitnessSimulator.Game.Application
{
    [Serializable] public sealed class ApplicationWorldState
    {
        public int Day = 1;
        public string Phase = "Anchors";
        public string BarrierId;
        public int AnchorIndex;
        public int StepIndex;
        public WorldBarrierState AnchorBarrier;
    }

    public sealed partial class GameSession
    {
        private static readonly string[] Participants = new[] { Actor }.Concat(RelationshipRules.CharacterIds).ToArray();
        private ApplicationWorldState World(RootEnvelope root)
        {
            var fragment = root.FindFragment("world", "shared");
            if (fragment == null || fragment.SchemaVersion != 1) throw new InvalidOperationException("Missing world cursor");
            var world = codec.Decode<ApplicationWorldState>(fragment.Json);
            if (world == null || world.Day < 1 || string.IsNullOrEmpty(world.BarrierId)) throw new InvalidOperationException("Invalid world cursor");
            if (world.Phase == "Anchors") WorldBarrier.Validate(world.AnchorBarrier);
            return world;
        }
        private void WriteWorld(RootEnvelope root, ApplicationWorldState world)
            => root.SetFragment("world", "shared", 1, root.Revision + 1, codec.Encode(world));
        private void InitializeWorld(RootEnvelope root)
        {
            var world = new ApplicationWorldState(); BeginWorldAnchor(root, world); WriteWorld(root, world);
        }
        private void BeginWorldAnchor(RootEnvelope root, ApplicationWorldState world)
        {
            string anchor = SessionContent.Anchors(Read<SessionState>(root, "session"))[world.AnchorIndex];
            world.AnchorBarrier = WorldBarrier.Create(root.RunId, world.Day, "Anchor", anchor, Participants, Actor, world.Day, new[] { "AnchorComplete" });
            world.BarrierId = world.AnchorBarrier.BarrierId;
        }

        private bool PlayerCanAct(RootEnvelope root, string action)
        {
            if (root.FindFragment("world", "shared") == null || action == "save-plan" || action.StartsWith("ending-", StringComparison.Ordinal)) return true;
            var world = World(root); var state = Read<SessionState>(root, "session");
            if (world.Phase == "Anchors") {
                if (state.AnchorIndex != world.AnchorIndex) return false;
                var player = world.AnchorBarrier.Actors.Single(a => a.ActorId == Actor);
                return world.AnchorBarrier.Stage == BarrierStage.Interacting || player.Arrival == ArrivalKind.Pending
                    && (action == "resolve-anchor" || action == "choose-entry" || action == "start-node");
            }
            if (world.Phase == "AwaitNextDay") return action == "next-day" || action.StartsWith("competition-", StringComparison.Ordinal);
            if (world.Phase == "DayEnd" || world.Phase == "EnterDay") return action == "day-step" && state.DayStep == world.StepIndex;
            return false;
        }

        // Publishes at most one actor command or one shared barrier transition per call.
        // No actor may run tomorrow or a later anchor while another is still here.
        private bool AdvanceWorld()
        {
            var root = CurrentSnapshot;
            if (root.FindFragment("world", "shared") == null) return false;
            var world = World(root); string target = null, transition = null;
            if (world.Phase == "Anchors") {
                if (world.AnchorBarrier.Stage == BarrierStage.Complete) transition = "next-anchor";
                else if (world.AnchorBarrier.Stage == BarrierStage.Arriving)
                    target = world.AnchorBarrier.Actors.FirstOrDefault(a => a.ActorId != Actor && a.Arrival == ArrivalKind.Pending)?.ActorId;
                else target = world.AnchorBarrier.Actors.FirstOrDefault(a => a.ActorId != Actor && !a.BarrierReached)?.ActorId;
            } else if (world.Phase == "DayOpenReady") transition = "open-day";
            else if (world.Phase == "DayEnd" || world.Phase == "EnterDay") {
                // Player N01 activates the shared anchor catalog before NPC N01; player N02 applies its plan before companions follow it.
                bool playerFirst = world.Phase == "EnterDay" && Read<SessionState>(root, "session").DayStep == world.StepIndex;
                if (!playerFirst) target = RelationshipRules.CharacterIds.FirstOrDefault(id => {
                    var state = Read<SessionState>(root, "session", id);
                    return state.Phase == world.Phase && state.DayStep == world.StepIndex;
                });
            }
            if (target == null && transition == null) return false;
            var command = new CommandEnvelope { RunId = root.RunId, ActorId = target ?? Actor, ResourceScope = target ?? Actor,
                Source = target == null ? source : CommandSource.NpcProduction, ExpectedRevision = root.Revision,
                OperationId = root.RunId + "/world/" + root.Revision, ActionKind = target == null ? "WorldTransition" : "NpcStep",
                Payload = codec.Encode(new WorldCommand { BarrierId = world.BarrierId, ActorId = target, Transition = transition, Step = world.StepIndex }) };
            var result = dispatcher.Execute(command, candidate => {
                if (target == null) return PrepareWorldTransition(candidate, transition);
                var action = SelectNpcAction(candidate, target);
                var prepared = PrepareActor(candidate, action, target);
                UpdateWorld(candidate, target, action.ActionId);
                return prepared;
            });
            if (result.Status != CommandStatus.Committed && result.Status != CommandStatus.AlreadyCommitted) {
                Diagnostic = result.Status + ": " + result.Error + "; actor=" + target + "; transition=" + transition + "; barrier=" + world.BarrierId;
                error = "人物流程已停在最近保存点：" + result.Error;
            } else { readSnapshot = null; SyncKernel(); }
            return true;
        }

        private PreparedOutcome PrepareWorldTransition(RootEnvelope root, string transition)
        {
            var world = World(root);
            var outcome = Outcome(root.RunId + "/world/" + world.BarrierId + "/" + transition, "时段进度已保存。");
            if (transition == "next-anchor") {
                if (world.Phase != "Anchors" || !WorldBarrier.CanAdvance(world.AnchorBarrier)) throw new InvalidOperationException("World anchor incomplete");
                world.AnchorIndex++;
                if (world.AnchorIndex < SessionContent.Anchors(Read<SessionState>(root, "session")).Length) BeginWorldAnchor(root, world);
                else {
                    world.Phase = "DayEnd"; world.StepIndex = 0; world.BarrierId = root.RunId + "/d" + world.Day + "/DayEnd";
                    foreach (string id in Participants) {
                        var state = Read<SessionState>(root, "session", id);
                        state.Phase = "DayEnd"; state.DayStep = 0; Write(root, "session", state, id);
                    }
                }
            } else if (transition == "open-day") {
                if (world.Phase != "DayOpenReady") throw new InvalidOperationException("Day-open barrier missing");
                PrepareCompetitionDayOpen(root, Read<SessionState>(root, "session"), outcome);
                world.Phase = "EnterDay"; world.StepIndex = 5;
                foreach (string id in Participants) {
                    var state = Read<SessionState>(root, "session", id); state.Phase = "EnterDay"; state.DayStep = 5; Write(root, "session", state, id);
                }
            } else throw new InvalidOperationException("Unknown world transition");
            WriteWorld(root, world); return outcome;
        }

        private void UpdateWorld(RootEnvelope root, string actorId, string action)
        {
            if (root.FindFragment("world", "shared") == null) return;
            var world = World(root); var state = Read<SessionState>(root, "session", actorId);
            if (world.Phase == "Anchors") {
                var barrier = world.AnchorBarrier; var participant = barrier.Actors.Single(a => a.ActorId == actorId);
                if (participant.Arrival == ArrivalKind.Pending) {
                    var flow = Read<FlowState>(root, "flow", actorId); var cursor = FlowEngine.Current(flow);
                    bool skipped = state.AnchorIndex > world.AnchorIndex;
                    bool started = state.AnchorIndex == world.AnchorIndex && flow.Day == world.Day
                        && flow.AnchorId == barrier.AnchorOccurrenceId && cursor?.Phase == NodePhase.Started;
                    if (skipped || started) {
                        var workout = Read<TrainingDayState>(root, "training", actorId).Workout;
                        bool training = started && workout != null && workout.WorkoutId == flow.TreeInstanceId;
                        barrier = WorldBarrier.Arrive(barrier, barrier.BarrierId, barrier.ScheduleRevision, actorId,
                            training ? ArrivalKind.Arrived : ArrivalKind.Skipped, skipped ? "consumed/" + world.Day + "/" + world.AnchorIndex : flow.TreeInstanceId + "/" + flow.SelectedDefinitionId).State;
                    }
                }
                if (barrier.Stage == BarrierStage.Arriving && barrier.Actors.All(a => a.Arrival != ArrivalKind.Pending)) {
                    barrier = WorldBarrier.BeginInteraction(barrier, barrier.BarrierId, barrier.ScheduleRevision).State;
                    if (barrier.Actors.Single(a => a.ActorId == Actor).Arrival == ArrivalKind.Arrived) {
                        var workout = Read<TrainingDayState>(root, "training").Workout;
                        OpenPlayerInteraction(root, workout, barrier.Actors.Where(a => a.ActorId != Actor && a.Arrival == ArrivalKind.Arrived).Select(a => a.ActorId).ToArray());
                    }
                }
                if (barrier.Stage == BarrierStage.Interacting) {
                    foreach (var done in barrier.Actors.Where(a => !a.BarrierReached).Select(a => a.ActorId).ToArray()) {
                        var current = Read<SessionState>(root, "session", done);
                        if (current.AnchorIndex <= world.AnchorIndex) continue;
                        barrier = WorldBarrier.CompleteStep(barrier, barrier.BarrierId, barrier.ScheduleRevision, done, "AnchorComplete").State;
                        barrier = WorldBarrier.CompleteActor(barrier, barrier.BarrierId, barrier.ScheduleRevision, done, root.RunId + "/" + done + "/d" + world.Day + "/a" + world.AnchorIndex).State;
                    }
                }
                world.AnchorBarrier = barrier;
            } else if (world.Phase == "AwaitNextDay" && actorId == Actor && action == "next-day") {
                world.Phase = "EnterDay"; world.StepIndex = 1; world.BarrierId = root.RunId + "/d" + (world.Day + 1) + "/EnterDay";
                foreach (string id in RelationshipRules.CharacterIds) {
                    var next = Read<SessionState>(root, "session", id);
                    if (next.Phase != "AwaitNextDay" || next.Day != world.Day) throw new InvalidOperationException("NPC day summary incomplete");
                    next.Phase = "EnterDay"; next.DayStep = 1; Write(root, "session", next, id);
                }
            } else if (world.Phase == "DayEnd" || world.Phase == "EnterDay") {
                var all = Participants.Select(id => Read<SessionState>(root, "session", id)).ToArray();
                if (world.Phase == "DayEnd" && all.All(s => s.Phase == "AwaitNextDay")) {
                    world.Phase = "AwaitNextDay"; world.StepIndex = 0;
                } else if (world.Phase == "EnterDay" && all.All(s => s.Phase == "Anchors")) {
                    world.Day = all[0].Day; world.Phase = "Anchors"; world.StepIndex = 0; world.AnchorIndex = 0; BeginWorldAnchor(root, world);
                } else if (all.All(s => s.DayStep > world.StepIndex)) {
                    if (world.Phase == "EnterDay" && world.StepIndex == 1) world.Day = all[0].Day;
                    world.StepIndex++;
                    if (world.Phase == "EnterDay" && world.StepIndex == 4) {
                        world.Phase = "DayOpenReady"; world.BarrierId = root.RunId + "/d" + world.Day + "/DayOpenReady";
                        foreach (string id in Participants) {
                            var ready = Read<SessionState>(root, "session", id); ready.Phase = "DayOpenReady"; Write(root, "session", ready, id);
                        }
                    }
                }
            }
            WriteWorld(root, world);
        }

        [Serializable] private sealed class WorldCommand { public string BarrierId, ActorId, Transition; public int Step; }
    }
}
