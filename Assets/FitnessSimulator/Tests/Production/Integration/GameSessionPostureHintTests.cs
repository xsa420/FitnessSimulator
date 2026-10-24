using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using FitnessSimulator.Game.Tests.Automation;
using FitnessSimulator.Game.Training;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Integration
{
    // IR01/02 application contracts. The ending condition fixture is NOT a natural romance route.
    public sealed class GameSessionPostureHintTests
    {
        private string evidence, directory;
        private GameSession session;
        private StreamWriter trace;
        private readonly UnityJsonStateCodec codec = new UnityJsonStateCodec();
        [SetUp] public void Open()
        {
            evidence = Path.Combine(Path.GetTempPath(), "Fitness-PostureHint-Evidence", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence); directory = Path.Combine(evidence, "save");
            trace = new StreamWriter(Path.Combine(evidence, "trace.txt")) { AutoFlush = true };
            session = GameSession.OpenAutomation(directory, 1); CheckOpen();
        }
        [TearDown] public void Close()
        {
            if (session != null) { Save("final"); session.Dispose(); }
            trace?.Dispose(); TestContext.WriteLine("Posture hint evidence: " + evidence);
        }
        private void CheckOpen() => Assert.That(session.InspectSnapshot(), Is.Not.Null, session.Diagnostic);
        private T State<T>(string owner, string scope = "player") => codec.Decode<T>(session.InspectSnapshot().FindFragment(owner, scope).Json);
        private void Save(string name) => File.WriteAllText(Path.Combine(evidence, name + ".json"), codec.Encode(session.InspectSnapshot()));
        private FrontendAction Action(string id, string value = null, int number = 0)
        {
            var root = session.InspectSnapshot();
            return new FrontendAction { ActionId = id, StringValue = value, IntValue = number, RunId = root.RunId,
                ExpectedRevision = root.Revision, OperationId = "posture-test/" + Guid.NewGuid().ToString("N") };
        }
        private FrontendAction Send(string id, string value = null, int number = 0)
        {
            var action = Action(id, value, number); var reply = session.Execute(action);
            trace.WriteLine(codec.Encode(action) + " => " + codec.Encode(reply));
            Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message); return action;
        }
        private bool Has(string id) => session.ReadView().Actions.Any(a => a.Enabled && a.Id == id);
        private void Start()
        {
            Send("new-game", TemplateCatalog.DefaultId);
            // Default three-set plan supplies the first-set and inter-set application witnesses.
            Send("save-plan", codec.Encode(PlanCatalog.CreateBenchPressSlice()));
        }
        private void Reopen(Action<StorageFaultPoint> fault = null)
        {
            string before = codec.Encode(session.InspectSnapshot()); session.Dispose();
            session = GameSession.OpenAutomation(directory, 999, fault); CheckOpen();
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(before)); Send("continue-game");
        }
        private void Step()
        {
            var view = session.ReadView(); Assert.That(view.ScreenId, Is.Not.EqualTo("error"), session.Diagnostic);
            var options = view.Actions.Where(a => a.Enabled).ToArray(); FrontendOption selected = null;
            if (options.Any(a => a.Id == "meal")) selected = options.First(a => a.Id == "meal" && a.StringValue == "skip");
            else if (options.Any(a => a.Id == "choose-entry")) selected = options.FirstOrDefault(a => a.Id == "choose-entry" && a.StringValue == "training-choice") ?? options.First(a => a.Id == "choose-entry");
            else if (options.Any(a => a.Id == "continue")) selected = options.First(a => a.Id == "continue");
            if (selected != null) Send(selected.Id, selected.StringValue, selected.IntValue); else session.Advance(0);
        }
        private void Until(Func<bool> reached)
        {
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 12000; i++) {
                if (reached()) return;
                Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(180), "Finite boundary budget exhausted; do not skip."); Step();
            }
            Assert.Fail("Boundary not reached: " + session.ReadView().Body + " / " + session.Diagnostic);
        }
        private bool NpcsComplete()
        {
            var world = State<ApplicationWorldState>("world", "shared");
            return world.Phase == "Anchors" && world.AnchorBarrier.Stage == BarrierStage.Interacting
                && world.AnchorBarrier.Actors.Where(a => a.ActorId != "player").All(a => a.BarrierReached);
        }
        private bool AtStory(int ordinal)
        {
            var flow = State<FlowState>("flow"); var cursor = FlowEngine.Current(flow);
            return cursor != null && cursor.Phase == NodePhase.Started && cursor.NodeId == "a0s" + ordinal + "-story" && NpcsComplete();
        }
        private FrontendAction InternalContinue()
        {
            var root = session.InspectSnapshot();
            // Exact public command generated by Advance; RunId is intentionally omitted there.
            return new FrontendAction { ActionId = "continue", ExpectedRevision = root.Revision,
                OperationId = root.RunId + "/system/" + root.Revision + "/continue" };
        }
        private void AssertCompleteOnce(string instance, long checks)
        {
            var flow = State<FlowState>("flow");
            Assert.That(flow.Cursors.Single(c => c.InstanceId == instance).Phase, Is.EqualTo(NodePhase.Completed));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks + 1));
        }

        [Test, Timeout(300000)] public void StartedHintRestartsAndCompletesOnceThenPreSetAndFollowersStayExplicit()
        {
            Start(); Until(() => AtStory(1)); Save("first-story-started");
            string instance = FlowEngine.Current(State<FlowState>("flow")).InstanceId;
            long checks = State<SessionState>("session").CompletionChecks;
            Assert.That(Has("continue"), Is.False); Reopen();
            session.Advance(0); AssertCompleteOnce(instance, checks); Save("first-story-completed"); Reopen();
            Until(() => Has("begin-set")); string preset = codec.Encode(session.InspectSnapshot());
            for (int i = 0; i < 20; i++) session.Advance(1);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(preset));
            Assert.That(State<TrainingDayState>("training").Workout.Phase, Is.EqualTo(WorkoutPhase.PreSet));
            Assert.That(Has("rest"), Is.True); Assert.That(Has("save-plan"), Is.True); Assert.That(Has("open-menu"), Is.True);
            Send("begin-set", "manual"); PublicEightInputDriver.DriveActiveSet(session, "bench_press", trace.WriteLine);
            Until(() => Has("settle")); Send("settle");
            Until(() => session.ReadView().ScreenId.StartsWith("followers/", StringComparison.Ordinal) && Has("continue"));
            string followers = codec.Encode(session.InspectSnapshot()); string feedback = session.ReadView().Body;
            for (int i = 0; i < 20; i++) session.Advance(1);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(followers)); Assert.That(session.ReadView().Body, Is.EqualTo(feedback));
            Assert.That(feedback, Is.Not.Empty); Save("followers-explicit"); Send("continue");
            Until(() => AtStory(2)); Save("second-story-started"); Reopen();
            instance = FlowEngine.Current(State<FlowState>("flow")).InstanceId; checks = State<SessionState>("session").CompletionChecks;
            session.Advance(0); AssertCompleteOnce(instance, checks);
            Until(() => Has("begin-set")); Assert.That(State<TrainingDayState>("training").Workout.SetOrdinal, Is.EqualTo(2));
        }

        [TestCase(StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement, true)]
        [Timeout(300000)] public void AutomaticCompleteFaultFreezesAndOriginalReceiptResolvesOnce(StorageFaultPoint point, bool committed)
        {
            Start(); Until(() => AtStory(1)); bool armed = false; int hits = 0;
            Reopen(p => { if (armed && p == point) { hits++; throw new IOException("IR target player hint Complete fault"); } });
            Assert.That(NpcsComplete(), Is.True, "Do not arm before other actors have finished.");
            var original = InternalContinue(); string instance = FlowEngine.Current(State<FlowState>("flow")).InstanceId;
            long checks = State<SessionState>("session").CompletionChecks;
            armed = true; session.Advance(0); armed = false;
            Assert.That(hits, Is.EqualTo(1)); Assert.That(session.ReadView().ScreenId, Is.EqualTo("error"));
            string frozen = codec.Encode(session.InspectSnapshot());
            for (int i = 0; i < 10; i++) session.Advance(1);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(frozen));
            string originalReceiptJson = null;
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec)) {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                var receipt = store.FindReceipt(original.OperationId);
                Assert.That(receipt != null, Is.EqualTo(committed));
                if (receipt != null) { originalReceiptJson = codec.Encode(receipt); File.WriteAllText(Path.Combine(evidence, "original-receipt.json"), originalReceiptJson); }
            }
            session = GameSession.OpenAutomation(directory, 999); CheckOpen(); Send("continue-game");
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks + (committed ? 1 : 0)));
            Assert.That(session.Execute(original).Accepted, Is.True); AssertCompleteOnce(instance, checks);
            string resolved = codec.Encode(session.InspectSnapshot());
            Assert.That(session.Execute(original).Accepted, Is.True); Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(resolved)); Save("resolved-once");
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec)) {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded)); Assert.That(store.FindReceipt(original.OperationId), Is.Not.Null);
                if (committed) Assert.That(codec.Encode(store.FindReceipt(original.OperationId)), Is.EqualTo(originalReceiptJson), "Committed unknown must resolve the original receipt, not replace it.");
            }
        }

        [Test, Timeout(300000)] public void ActualHintCompletionTriggersEndingAndResumesOriginalSuccessor()
        {
            Start(); Until(() => AtStory(1));
            string instance = FlowEngine.Current(State<FlowState>("flow")).InstanceId;
            long checks = State<SessionState>("session").CompletionChecks;
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec)) {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded)); var root = loaded.Root;
                var relationship = RelationshipRules.NormalizeAfterLoad(codec.Decode<RelationshipState>(root.FindFragment("relationships", "player").Json));
                Assert.That(relationship.RomanceEstablished, Is.False);
                relationship.Affections.Single(a => a.CharacterId == RelationshipRules.Beginner).Value = 100;
                relationship.PendingConfessionId = "IR-explicit-condition-fixture";
                var change = ConfessionRules.Prepare(relationship, ConfessionRules.Freeze(relationship, 1000000, 1000000, 15000000), 0);
                root.SetFragment("relationships", "player", 1, root.Revision + 1, codec.Encode(change.Relationships));
                Assert.That(store.Commit(root.Generation, root, new OperationReceipt { RunId = root.RunId,
                    OperationId = "IR-condition", SemanticKey = "IR-condition", PayloadHash = "explicit-condition-not-natural-route" }, Array.Empty<LogAppend>()).Status, Is.EqualTo(CommitStatus.Committed));
            }
            trace.WriteLine("EXPLICIT CONDITION FIXTURE: relationship facts only; no ending or flow injection; not a natural romance route.");
            session = GameSession.OpenAutomation(directory, 999); CheckOpen(); Send("continue-game");
            Save("condition-before-complete"); session.Advance(0); AssertCompleteOnce(instance, checks);
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-presentation")); Save("complete-ending-paused");
            string paused = codec.Encode(session.InspectSnapshot());
            for (int i = 0; i < 20; i++) session.Advance(1);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(paused)); Reopen();
            foreach (string id in new[] { "ending-segment", "ending-empty", "ending-continue" }) {
                var option = session.ReadView().Actions.Single(a => a.Id == id); Send(id, option.StringValue, option.IntValue);
            }
            AssertCompleteOnce(instance, checks); Assert.That(State<ProgressionRun>("progression").HasActive, Is.False);
            Assert.That(FlowEngine.Current(State<FlowState>("flow")).NodeId, Is.EqualTo("a0s1-play"));
            // Successful ending-continue clears atMenu. A fresh open returns to the public
            // main menu; verify and use that menu explicitly, as in the G06 recovery route.
            string continued = codec.Encode(session.InspectSnapshot()); session.Dispose();
            session = GameSession.OpenAutomation(directory, 999); CheckOpen();
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(continued));
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("menu")); Assert.That(Has("continue-game"), Is.True);
            Send("continue-game");
            Until(() => Has("begin-set")); AssertCompleteOnce(instance, checks);
        }

        [Test, Timeout(300000)] public void NaturalArrivalGateAndChoicePendingNeverCompletePlayerOrChooseAutomatically()
        {
            Start();
            var world = State<ApplicationWorldState>("world", "shared");
            Assert.That(world.AnchorBarrier.Stage, Is.EqualTo(BarrierStage.Arriving));
            string playerFlow = codec.Encode(State<FlowState>("flow")); long checks = State<SessionState>("session").CompletionChecks;
            Assert.That(session.Execute(Action("continue")).Accepted, Is.False);
            Assert.That(codec.Encode(State<FlowState>("flow")), Is.EqualTo(playerFlow));
            session.Advance(0); // Other actors may legitimately move: compare only the player.
            Assert.That(codec.Encode(State<FlowState>("flow")), Is.EqualTo(playerFlow));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks)); Save("arrival-gate");
            Until(() => State<FlowState>("flow").EntryPhase == EntryPhase.ChoicePending && Has("choose-entry"));
            var choice = State<FlowState>("flow"); string token = choice.ChoiceToken;
            playerFlow = codec.Encode(choice); checks = State<SessionState>("session").CompletionChecks;
            for (int i = 0; i < 30; i++) session.Advance(0);
            Assert.That(codec.Encode(State<FlowState>("flow")), Is.EqualTo(playerFlow));
            Assert.That(State<FlowState>("flow").ChoiceToken, Is.EqualTo(token)); Assert.That(Has("choose-entry"), Is.True);
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks)); Save("choice-still-pending");
        }

        private static TrainingDayState StructureTraining()
        {
            var quota = DayTrainingQuota.Freeze("player", 1, "IR-plan", new[] {
                new TrainingPlanEntry { EntryId = "first", ExerciseId = "bench_press", Sets = 3, Reps = 8 },
                new TrainingPlanEntry { EntryId = "next", ExerciseId = "dumbbell_curl", Sets = 3, Reps = 10 }
            }, TrainingProgress.CreateInitial("player"));
            return new TrainingDayState { HasQuota = true, Quota = quota, HasWorkout = true,
                Workout = new WorkoutSnapshot { Phase = WorkoutPhase.PreSet, Quota = quota, EntryIndex = 0, SetOrdinal = 1 } };
        }
        [TestCase(0, 1)] [TestCase(0, 2)] [TestCase(1, 1)]
        public void GeneratedFirstInterSetAndLaterActionPostureHintsAreWhitelisted(int entry, int ordinal)
        {
            var training = StructureTraining(); training.Workout.EntryIndex = entry; training.Workout.SetOrdinal = ordinal;
            var flow = new FlowState { AnchorId = "morning", SelectedDefinitionId = "training-choice", EntryPhase = EntryPhase.Resolved,
                Tree = SessionContent.Build("morning", training.Quota, true) };
            var node = flow.Tree.Nodes.Single(n => n.Id == "a" + entry + "s" + ordinal + "-story");
            Assert.That(SessionContent.IsAutomaticPostureHint(flow, training, node), Is.True);
        }
        [TestCase("futureVersion")] [TestCase("fixedTree")] [TestCase("definition")] [TestCase("handler")]
        [TestCase("branch")] [TestCase("successor")] [TestCase("missingPlay")] [TestCase("planRevision")]
        [TestCase("followers")] [TestCase("settle")] [TestCase("wrongGroup")] [TestCase("wrongSelection")]
        public void UnreviewedStructureNeverQualifiesForAutomaticCompletion(string mutation)
        {
            var training = StructureTraining();
            var flow = new FlowState { AnchorId = "morning", SelectedDefinitionId = "training-choice", EntryPhase = EntryPhase.Resolved,
                Tree = SessionContent.Build("morning", training.Quota, true) };
            var node = flow.Tree.Nodes.Single(n => n.Id == "a0s1-story");
            switch (mutation) {
                case "futureVersion": flow.Tree.Version += "/future"; break;
                case "fixedTree": flow.Tree.Dynamic = false; break;
                case "definition": node.DefinitionId = "unread-story"; break;
                case "handler": node.Handler = "unread-story"; break;
                case "branch": node.Branches.Add(new FlowEdge { ConditionKey = "decision", TargetNodeId = "summary" }); break;
                case "successor": node.NextNodeId = "summary"; break;
                case "missingPlay": flow.Tree.Nodes.RemoveAll(n => n.Id == "a0s1-play"); break;
                case "planRevision": flow.Tree.FrozenPlanVersion += "/changed"; break;
                case "followers": node = flow.Tree.Nodes.Single(n => n.Id == "a0s1-followers"); break;
                case "settle": node = flow.Tree.Nodes.Single(n => n.Id == "a0s1-settle"); break;
                case "wrongGroup": training.Workout.SetOrdinal = 2; break;
                case "wrongSelection": flow.SelectedDefinitionId = "other-choice"; break;
                default: throw new ArgumentException(mutation);
            }
            Assert.That(SessionContent.IsAutomaticPostureHint(flow, training, node), Is.False);
        }
    }
}
