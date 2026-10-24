using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Integration
{
    // Explicit Test-source structural fixtures, NOT naturally reached or imported legacy saves.
    public sealed class WorkIncomeRecoveryTests
    {
        [Serializable] private sealed class EventSource { public string Source; }
        private string evidence, directory;
        private GameSession session;
        private readonly UnityJsonStateCodec codec = new UnityJsonStateCodec();
        [SetUp] public void Open()
        {
            evidence = Path.Combine(Path.GetTempPath(), "Fitness-WorkIncome-Structure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence); directory = Path.Combine(evidence, "save");
            File.WriteAllText(Path.Combine(evidence, "fixture.txt"), "CommandSource.Test; explicit D1 structural boundary; NOT natural legacy-save compatibility evidence.");
            session = GameSession.OpenTest(directory, 1); CheckOpen();
            Send("new-game", TemplateCatalog.DefaultId); Send("save-plan", codec.Encode(PlanCatalog.CreateBenchPressSlice()));
        }
        [TearDown] public void Close()
        {
            if (session != null) { File.WriteAllText(Path.Combine(evidence, "final.json"), codec.Encode(session.InspectSnapshot())); session.Dispose(); }
            TestContext.WriteLine("Work income structural evidence: " + evidence);
        }
        private void CheckOpen() => Assert.That(session.InspectSnapshot(), Is.Not.Null, session.Diagnostic);
        private T Read<T>(RootEnvelope root, string owner) => codec.Decode<T>(root.FindFragment(owner, "player").Json);
        private T State<T>(string owner) => Read<T>(session.InspectSnapshot(), owner);
        private void Put<T>(RootEnvelope root, string owner, T state) => root.SetFragment(owner, "player", 1, root.Revision + 1, codec.Encode(state));
        private FrontendAction Command(string id, string value = null) => new FrontendAction { ActionId = id, StringValue = value,
            OperationId = Guid.NewGuid().ToString("N"), RunId = session.InspectSnapshot().RunId, ExpectedRevision = session.InspectSnapshot().Revision };
        private FrontendAction Send(string id, string value = null)
        {
            var command = Command(id, value); var reply = session.Execute(command);
            Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message); return command;
        }
        private void Restart(Action<StorageFaultPoint> fault = null)
        {
            session?.Dispose(); session = GameSession.OpenTest(directory, 999, fault); CheckOpen(); Send("continue-game");
        }
        private void Fixture(Action<RootEnvelope> change, bool legacyPaid = false)
        {
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec)) {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded)); var root = loaded.Root;
                var appends = new List<LogAppend>();
                if (legacyPaid) {
                    var income = LifeRules.PrepareIncome(Read<LifeActorState>(root, "life"), 1);
                    Assert.That(income.Status, Is.EqualTo(LifePreparationStatus.Prepared)); Put(root, "life", income.Candidate);
                    appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "Income", Json = codec.Encode(income.Receipt) });
                }
                change(root);
                string id = Guid.NewGuid().ToString("N");
                Assert.That(store.Commit(root.Generation, root, new OperationReceipt { RunId = root.RunId, OperationId = id,
                    SemanticKey = "Test/structural-fixture/" + id, PayloadHash = "explicit-D1-structure-not-natural-legacy" }, appends).Status, Is.EqualTo(CommitStatus.Committed));
                File.WriteAllText(Path.Combine(evidence, "fixture-root.json"), codec.Encode(store.Load().Root));
            }
            Restart();
        }
        private void WorkFixture(string anchor, bool legacyPaid = false)
        {
            Fixture(root => {
                var state = Read<SessionState>(root, "session"); state.Phase = "Anchors";
                state.AnchorIndex = Array.IndexOf(SessionContent.Anchors(state), anchor); Assert.That(state.AnchorIndex, Is.GreaterThanOrEqualTo(0));
                state.AnchorGameSeconds = 0; state.AnchorStandardSeconds = 1800; Put(root, "session", state);
                var tree = SessionContent.Build(anchor, null, false, restDay: false);
                Assert.That(tree.Nodes.Single(n => n.Id == "default").Handler, Is.EqualTo("work"));
                string instance = root.RunId + "/Test/work/" + anchor;
                var flow = new FlowState { ActorId = "player", Day = 1, AnchorId = anchor, AnchorOrder = state.AnchorIndex,
                    HasTree = true, Tree = tree, TreeInstanceId = instance, EntryPhase = EntryPhase.Resolved,
                    SelectedDefinitionId = tree.DefaultEntry.DefinitionId, CurrentNodeId = "default" };
                flow.Cursors.Add(new NodeCursor { NodeId = "default", InstanceId = instance + "/default", Phase = NodePhase.Started });
                FlowEngine.NormalizeAfterLoad(flow); Put(root, "flow", flow);
                var participants = codec.Decode<ApplicationWorldState>(root.FindFragment("world", "shared").Json).AnchorBarrier.Actors.Select(a => a.ActorId).ToArray();
                var barrier = WorldBarrier.Create(root.RunId, 1, "Anchor", anchor, participants, "player", 1, new[] { "AnchorComplete" });
                foreach (string actor in participants) barrier = WorldBarrier.Arrive(barrier, barrier.BarrierId, barrier.ScheduleRevision, actor, ArrivalKind.Skipped, "Test/structural-arrival/" + actor).State;
                barrier = WorldBarrier.BeginInteraction(barrier, barrier.BarrierId, barrier.ScheduleRevision).State;
                root.SetFragment("world", "shared", 1, root.Revision + 1, codec.Encode(new ApplicationWorldState {
                    Day = 1, Phase = "Anchors", AnchorIndex = state.AnchorIndex, BarrierId = barrier.BarrierId, AnchorBarrier = barrier }));
            }, legacyPaid);
        }
        private List<LogAppend> Records()
        {
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec)) {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded)); var records = new List<LogAppend>();
                for (int skip = 0; ; skip += 64) {
                    var page = store.ReadLog("run/" + loaded.Root.RunId + "/records", skip, 64); records.AddRange(page);
                    if (page.Count < 64) return records;
                }
            }
        }

        [TestCase("morning", StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase("morning", StorageFaultPoint.BeforeAcknowledgement, true)]
        [TestCase("afternoon", StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase("afternoon", StorageFaultPoint.BeforeAcknowledgement, true)]
        public void WorkCompletionFaultRecoversMoneyFlowCheckAndReceiptAtomically(string anchor, StorageFaultPoint point, bool committed)
        {
            WorkFixture(anchor); long balance = State<LifeActorState>("life").Wallet.Balance;
            long amount = 50 + State<LifeActorState>("life").Wallet.Affluence;
            long checks = State<SessionState>("session").CompletionChecks; bool armed = false; int hits = 0;
            var command = Command("continue"); Restart(p => { if (armed && p == point) { hits++; throw new IOException("Test work completion fault"); } });
            armed = true; Assert.That(session.Execute(command).Accepted, Is.False); armed = false; Assert.That(hits, Is.EqualTo(1));
            string frozen = codec.Encode(session.InspectSnapshot()); session.Advance(0);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(frozen));
            Restart();
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(balance + (committed ? amount : 0)));
            Assert.That(State<FlowState>("flow").Cursors.Single().Phase, Is.EqualTo(committed ? NodePhase.Completed : NodePhase.Started));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks + (committed ? 1 : 0)));
            Assert.That(State<LifeActorState>("life").WorkIncomeReceipts.Length, Is.EqualTo(committed ? 1 : 0));
            var recoveredRecords = Records();
            Assert.That(recoveredRecords.Count(r => r.Kind == "WorkIncome"), Is.EqualTo(committed ? 1 : 0));
            Restart();
            Assert.That(session.Execute(command).Accepted, Is.True);
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(balance + amount));
            Assert.That(State<FlowState>("flow").Cursors.Single().Phase, Is.EqualTo(NodePhase.Completed));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks + 1));
            Assert.That(State<SessionState>("session").AnchorGameSeconds, Is.EqualTo(1800));
            string once = codec.Encode(session.InspectSnapshot()); Restart();
            Assert.That(session.Execute(command).Accepted, Is.True); Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(once));
            command.OperationId = Guid.NewGuid().ToString("N");
            // The application has no stable-event resolver for continue: a fresh ID with stale revision is rejected.
            Assert.That(session.Execute(command).Accepted, Is.False); Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(once));
            var records = Records(); var work = records.Where(r => r.Kind == "WorkIncome").Select(r => codec.Decode<WorkIncomeReceipt>(r.Json)).ToArray();
            Assert.That(work.Length, Is.EqualTo(1)); Assert.That(work[0].Amount, Is.EqualTo(amount)); Assert.That(work[0].AnchorId, Is.EqualTo(anchor));
            var completion = records.Where(r => r.Kind == "continue").Select(r => codec.Decode<EventSource>(r.Json)).Single();
            Assert.That(completion.Source, Is.EqualTo(CommandSource.Test.ToString()));
            Assert.That(records.Count(r => r.Kind == "Income"), Is.Zero);
        }

        [Test] public void StructuralLegacyDailyIncomeSuppressesNewWorkPaymentButCompletesEvent()
        {
            WorkFixture("morning", true); long balance = State<LifeActorState>("life").Wallet.Balance;
            long checks = State<SessionState>("session").CompletionChecks;
            var probe = LifeRules.PrepareWorkIncome(State<LifeActorState>("life"), 1, "morning", FlowEngine.Current(State<FlowState>("flow")).InstanceId);
            Assert.That(probe.Status, Is.EqualTo(LifePreparationStatus.LegacyIncomeAlreadyApplied));
            var command = Send("continue"); Restart(); Assert.That(session.Execute(command).Accepted, Is.True);
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(balance));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks + 1));
            Assert.That(State<FlowState>("flow").EntryPhase, Is.EqualTo(EntryPhase.Finished));
            var records = Records(); Assert.That(records.Count(r => r.Kind == "Income"), Is.EqualTo(1)); Assert.That(records.Count(r => r.Kind == "WorkIncome"), Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void StructuralOldE01CursorOnlyAdvancesWithoutIssuingOrReissuingIncome(bool legacyPaid)
        {
            Fixture(root => {
                var state = Read<SessionState>(root, "session"); state.Phase = "DayEnd"; state.DayStep = 1; Put(root, "session", state);
                var world = codec.Decode<ApplicationWorldState>(root.FindFragment("world", "shared").Json);
                world.Phase = "DayEnd"; world.StepIndex = 1; world.BarrierId = root.RunId + "/d1/DayEnd";
                root.SetFragment("world", "shared", 1, root.Revision + 1, codec.Encode(world));
            }, legacyPaid);
            string life = codec.Encode(State<LifeActorState>("life")); long checks = State<SessionState>("session").CompletionChecks;
            var command = Send("day-step"); Assert.That(State<SessionState>("session").DayStep, Is.EqualTo(2));
            Assert.That(codec.Encode(State<LifeActorState>("life")), Is.EqualTo(life));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks));
            string after = codec.Encode(session.InspectSnapshot()); Restart(); Assert.That(session.Execute(command).Accepted, Is.True);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(after));
            var records = Records(); Assert.That(records.Count(r => r.Kind == "Income"), Is.EqualTo(legacyPaid ? 1 : 0)); Assert.That(records.Count(r => r.Kind == "WorkIncome"), Is.Zero);
        }

    }
}
