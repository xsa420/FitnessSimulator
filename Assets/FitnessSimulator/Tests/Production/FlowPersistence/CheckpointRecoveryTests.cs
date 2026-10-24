using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class CheckpointRecoveryTests
    {
        private string directory;
        private readonly IStateCodec codec = new UnityJsonStateCodec();
        private readonly List<string> backups = new List<string>();

        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "FitnessM1Tests", Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock");
            foreach (string backup in backups) if (Directory.Exists(backup)) Directory.Delete(backup, true);
            backups.Clear();
        }

        private static RootEnvelope NewRoot() { return new RootEnvelope { RunId = "test-run", ContentVersion = "M1Baseline" }; }
        private static CommandEnvelope Command(string id, long revision, CommandSource source = CommandSource.Test)
        {
            return new CommandEnvelope { OperationId = id, ExpectedRevision = revision, RunId = "test-run", ActorId = "player",
                ResourceScope = "test-run/player", ActionKind = "fixture", Payload = "payload", Source = source };
        }
        private SerialCommandDispatcher Dispatcher(ICheckpointStore store, RootEnvelope root, Action<RootEnvelope, PreparedOutcome> check = null)
        { return new SerialCommandDispatcher(store, codec, root, c => c.Source == CommandSource.Test, check ?? ((r, o) => { })); }
        private static PreparedOutcome Prepare(RootEnvelope root, string key, string value)
        {
            root.SetFragment("fixture", "player", 1, root.Revision + 1, value);
            return new PreparedOutcome { SemanticKey = key, Result = value, OutcomeKind = OutcomeKind.Success,
                EventDisposition = EventDisposition.AuxiliaryOnly, CheckKind = CheckKind.None,
                Appends = new List<LogAppend> { new LogAppend { StreamId = "run/test-run/records", Kind = "fixture", Json = value } } };
        }

        [Test] public void AuthenticatedSemanticReplayPrecedesExpiredPreparationAndChecksPayload()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                Assert.That(dispatch.Execute(Command("original", 0), c => "purchase/1", r => Prepare(r, "purchase/1", "paid")).Status, Is.EqualTo(CommandStatus.Committed));
                Func<RootEnvelope, PreparedOutcome> expired = r => { throw new InvalidOperationException("expired quote"); };
                var replay = dispatch.Execute(Command("retry", 0), c => "purchase/1", expired);
                Assert.That(replay.Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                Assert.That(replay.Receipt.OperationId, Is.EqualTo("original"));
                var conflict = Command("conflict", 0); conflict.Payload = "different";
                Assert.That(dispatch.Execute(conflict, c => "purchase/1", expired).Error, Is.EqualTo("SemanticConflict"));
                Assert.That(dispatch.Execute(Command("original", 0), c => { throw new InvalidOperationException("must not resolve"); }, expired).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                Assert.That(dispatch.Execute(Command("unauthorized", 0, CommandSource.NpcProduction), c => "purchase/1", expired).Status, Is.EqualTo(CommandStatus.Rejected));
                Assert.That(dispatch.Snapshot.Revision, Is.EqualTo(1));
            }
        }

        [Test] public void PreparedSemanticDuplicateChecksPayloadAndResolvedKeyMustAgree()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                dispatch.Execute(Command("first", 0), r => Prepare(r, "key", "paid"));
                Assert.That(dispatch.Execute(Command("same", 1), r => Prepare(r, "key", "paid")).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                var different = Command("different", 1); different.Payload = "different";
                Assert.That(dispatch.Execute(different, r => Prepare(r, "key", "wrong")).Error, Is.EqualTo("SemanticConflict"));
                Assert.That(dispatch.Execute(Command("mismatch", 1), c => "new-key", r => Prepare(r, "other-key", "wrong")).Status, Is.EqualTo(CommandStatus.ConfigError));
                Assert.That(dispatch.Snapshot.Revision, Is.EqualTo(1));
            }
        }

        [TestCase(StorageFaultPoint.BeforeLogAppend, false)]
        [TestCase(StorageFaultPoint.AfterLogFlush, false)]
        [TestCase(StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase(StorageFaultPoint.AfterRootFlush, false)]
        [TestCase(StorageFaultPoint.BeforeCommitReceipt, false)]
        [TestCase(StorageFaultPoint.DuringCommitReceipt, false)]
        [TestCase(StorageFaultPoint.AfterCommitReceipt, true)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement, true)]
        public void FaultBoundaryPublishesWholeOldOrNewState(StorageFaultPoint point, bool newStateExpected)
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                Assert.That(dispatch.Execute(Command("baseline", 0), r => Prepare(r, "baseline", "old")).Status, Is.EqualTo(CommandStatus.Committed));
            }
            using (var store = new LocalCheckpointStore(directory, codec, p => { if (p == point) throw new IOException("injected " + p); }))
            {
                var loaded = store.Load();
                var dispatch = Dispatcher(store, loaded.Root);
                Assert.That(dispatch.Execute(Command("new", 1), r => Prepare(r, "new", "new")).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
                Assert.That(dispatch.Execute(Command("other", 1), r => Prepare(r, "other", "wrong")).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
            }
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(loaded.Root.FindFragment("fixture", "player").Json, Is.EqualTo(newStateExpected ? "new" : "old"));
                Assert.That(store.ReadLog("run/test-run/records", 0, 64).Count, Is.EqualTo(newStateExpected ? 2 : 1));
                var dispatch = Dispatcher(store, loaded.Root);
                var retried = dispatch.Execute(Command("new", loaded.Root.Revision), r => Prepare(r, "new", "new"));
                Assert.That(retried.Status, Is.EqualTo(newStateExpected ? CommandStatus.AlreadyCommitted : CommandStatus.Committed));
                Assert.That(store.ReadLog("run/test-run/records", 0, 64).Count, Is.EqualTo(2));
            }
        }

        [Test] public void CommittedRootCorruptionNeverFallsBackToOlderSlot()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                dispatch.Execute(Command("one", 0), r => Prepare(r, "one", "one"));
                dispatch.Execute(Command("two", 1), r => Prepare(r, "two", "two"));
            }
            File.WriteAllText(Path.Combine(directory, "state-a.bin"), "corrupt latest generation 2");
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.CorruptCommitted));
        }

        [Test] public void ReceiptBeyondHotCacheRemainsIdempotentAfterReopen()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                for (int i = 0; i < 70; i++) Assert.That(dispatch.Execute(Command("op" + i, i), r => Prepare(r, "key" + i, "v" + i)).Status, Is.EqualTo(CommandStatus.Committed));
                Assert.That(dispatch.Snapshot.RecentReceipts.Count, Is.EqualTo(64));
                Assert.That(dispatch.Snapshot.RecentReceipts.Any(r => r.OperationId == "op0"), Is.False);
            }
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, store.Load().Root);
                Assert.That(dispatch.Execute(Command("op0", 0), r => throw new Exception("must not prepare retry")).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                Assert.That(dispatch.Snapshot.Revision, Is.EqualTo(70));
                Assert.That(dispatch.Execute(Command("fresh-id", 70), r => Prepare(r, "key0", "duplicate")).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                Assert.That(dispatch.Snapshot.FindFragment("fixture", "player").Json, Is.EqualTo("v69"));
            }
        }

        [Test] public void ConflictingPayloadAndStaleRevisionCannotMutateState()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                dispatch.Execute(Command("op", 0), r => Prepare(r, "key", "one"));
                var changed = Command("op", 1); changed.Payload = "changed";
                Assert.That(dispatch.Execute(changed, r => Prepare(r, "key", "two")).Error, Is.EqualTo("OperationConflict"));
                Assert.That(dispatch.Execute(Command("old", 0), r => Prepare(r, "old", "two")).Error, Is.EqualTo("StaleRevision"));
                Assert.That(dispatch.Snapshot.FindFragment("fixture", "player").Json, Is.EqualTo("one"));
            }
        }

        [Test] public void AuxiliaryPlanSaveDoesNotCaptureUncommittedWorkingCopy()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatch = Dispatcher(store, NewRoot());
                var inFlight = dispatch.Snapshot; inFlight.SetFragment("body", "player", 1, 1, "unearned-growth");
                dispatch.Execute(Command("plan1", 0), r => { r.SetFragment("plan", "player", 1, 1, "first"); return new PreparedOutcome { SemanticKey = "plan1", EventDisposition = EventDisposition.AuxiliaryOnly }; });
                dispatch.Execute(Command("plan2", 1), r => { r.SetFragment("plan", "player", 1, 2, "last"); return new PreparedOutcome { SemanticKey = "plan2", EventDisposition = EventDisposition.AuxiliaryOnly }; });
            }
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var root = store.Load().Root;
                Assert.That(root.FindFragment("body", "player"), Is.Null);
                Assert.That(root.FindFragment("plan", "player").Json, Is.EqualTo("last"));
            }
        }

        [Test] public void AuthorizedTestPlayerUsesSameEndingCheckButProductionRejectsTest()
        {
            int checks = 0;
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var production = new SerialCommandDispatcher(store, codec, NewRoot(), c => c.Source == CommandSource.Player, (r, o) => checks++);
                Assert.That(production.Execute(Command("forbidden", 0), r => new PreparedOutcome()).Status, Is.EqualTo(CommandStatus.Rejected));
                var test = Dispatcher(store, NewRoot(), (r, o) => { checks++; r.SetFragment("ending", "player", 1, 1, "triggered"); });
                var result = test.Execute(Command("test-player", 0), r => new PreparedOutcome { SemanticKey = "normal", EventDisposition = EventDisposition.CompleteNode, CheckKind = CheckKind.NormalCompletion });
                Assert.That(result.Status, Is.EqualTo(CommandStatus.Committed)); Assert.That(checks, Is.EqualTo(1));
                Assert.That(store.Load().Root.FindFragment("ending", "player").Json, Is.EqualTo("triggered"));
            }
        }

        [Test] public void MissingCommittedLogIsCorruptionNotEmptyState()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("one", 0), r => Prepare(r, "one", "value"));
            string path = Directory.GetFiles(directory, "log-*.bin").First(); File.Delete(path);
            using (var store = new LocalCheckpointStore(directory, codec)) Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.CorruptCommitted));
        }

        [Test] public void DirectoryHasExactlyOneWriter()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.Throws<IOException>(() => { using (var second = new LocalCheckpointStore(directory, codec)) { } });
        }

        [Test] public void RolloverNeverAbsorbsAnUncommittedTail()
        {
            Func<string, int, PreparedOutcome> log = (key, bytes) => new PreparedOutcome { SemanticKey = key, EventDisposition = EventDisposition.AuxiliaryOnly,
                Appends = new List<LogAppend> { new LogAppend { StreamId = "run/test-run/records", Kind = "large", Json = new string('x', bytes) } } };
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.That(Dispatcher(store, NewRoot()).Execute(Command("base", 0), r => log("base", 700000)).Status, Is.EqualTo(CommandStatus.Committed));
            using (var store = new LocalCheckpointStore(directory, codec, p => { if (p == StorageFaultPoint.AfterLogFlush) throw new IOException("tail"); }))
                Assert.That(Dispatcher(store, store.Load().Root).Execute(Command("orphan", 1), r => log("orphan", 200000)).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatcher = Dispatcher(store, store.Load().Root);
                Assert.That(dispatcher.Execute(Command("roll", 1), r => log("roll", 400000)).Status, Is.EqualTo(CommandStatus.Committed));
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(store.ReadLog("run/test-run/records", 0, 64).Count, Is.EqualTo(2));
                Assert.That(store.FindReceipt("orphan"), Is.Null);
            }
        }

        [Test] public void NewRunArchivesOldPrefixesWithoutGrowingRootOrDroppingPermanentHistory()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var first = NewRoot();
                var receipt = new OperationReceipt { OperationId = "first", PayloadHash = "hash", RunId = first.RunId, SemanticKey = "first" };
                var result = store.Commit(0, first, receipt, new[] { new LogAppend { StreamId = "permanent/history", Kind = "achievement", Json = "immutable" } });
                Assert.That(result.Status, Is.EqualTo(CommitStatus.Committed));
                for (int i = 0; i < 5; i++)
                {
                    var next = result.Root; next.RunId = "new-" + i; next.Fragments.Clear();
                    result = store.Commit(next.Generation, next, new OperationReceipt { OperationId = "new" + i, PayloadHash = "hash", RunId = next.RunId, SemanticKey = "new-run" }, Array.Empty<LogAppend>());
                    Assert.That(result.Status, Is.EqualTo(CommitStatus.Committed));
                    Assert.That(result.Root.LogPrefixes.Count, Is.EqualTo(3));
                }
                Assert.That(store.ReadLog("permanent/history", 0, 64)[0].Json, Is.EqualTo("immutable"));
                Assert.That(store.ReadLog("permanent/archive", 0, 64).Count, Is.EqualTo(5));
            }
        }

        [Test] public void MissingContentVersionBlocksLoadWithoutFallback()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("first", 0), r => Prepare(r, "first", "value"));
            using (var store = new LocalCheckpointStore(directory, codec, expectedContentVersion: "different-content"))
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.MissingContent));
        }

        [Test] public void MissingLedgerDoesNotMasqueradeAsFreshInstall()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("first", 0), r => Prepare(r, "first", "value"));
            File.Delete(Path.Combine(directory, "commit-ledger.bin"));
            using (var store = new LocalCheckpointStore(directory, codec)) Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.CorruptCommitted));
        }

        [Test] public void PartialUncommittedLogTailIsInvisibleAndRemovedOnAppend()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("first", 0), r => Prepare(r, "first", "old"));
            string path = Path.Combine(directory, "log-" + ContentHash.Sha256("run/test-run/records") + "-00000000.bin");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
            { stream.Write(new byte[] { 0x32, 0x4d, 0x53, 0x46, 0xff }, 0, 5); stream.Flush(true); }
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(store.ReadLog("run/test-run/records", 0, 64).Count, Is.EqualTo(1));
                Assert.That(Dispatcher(store, loaded.Root).Execute(Command("second", 1), r => Prepare(r, "second", "new")).Status, Is.EqualTo(CommandStatus.Committed));
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(store.ReadLog("run/test-run/records", 0, 64).Count, Is.EqualTo(2));
            }
        }

        [Test] public void BeginNewRunPreservesPermanentStateAndIsIdempotentAcrossRunIdentityChange()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatcher = Dispatcher(store, NewRoot());
                dispatcher.Execute(Command("initial", 0), r => {
                    r.PermanentFragments.Add(new VersionedFragment { Owner = "progression", Scope = "permanent", SchemaVersion = 1, Json = "unlocked" });
                    var result = Prepare(r, "initial", "old-body");
                    result.Appends.Add(new LogAppend { StreamId = "permanent/history", Kind = "achievement", Json = "old-history" });
                    return result;
                });
                var initialized = new RootEnvelope { RunId = "next-run", ContentVersion = "M1Baseline" };
                initialized.SetFragment("body", "player", 1, 1, "fresh-body");
                var command = Command("new-run-op", 1); command.ActionKind = "BeginNewRun";
                Assert.That(dispatcher.BeginNewRun(command, initialized).Status, Is.EqualTo(CommandStatus.Committed));
                Assert.That(dispatcher.BeginNewRun(command, initialized).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                Assert.That(dispatcher.Snapshot.RunId, Is.EqualTo("next-run")); Assert.That(dispatcher.Snapshot.Revision, Is.EqualTo(2));
                Assert.That(dispatcher.Snapshot.PermanentFragments[0].Json, Is.EqualTo("unlocked"));
                Assert.That(dispatcher.Snapshot.FindFragment("fixture", "player"), Is.Null);
                Assert.That(store.ReadLog("permanent/history", 0, 64)[0].Json, Is.EqualTo("old-history"));
                initialized.RunId = "different";
                Assert.That(dispatcher.BeginNewRun(command, initialized).Error, Is.EqualTo("OperationConflict"));
            }
        }

        [Test] public void BeginNewRunRejectsUntrustedSourceAndStaleRevision()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatcher = new SerialCommandDispatcher(store, codec, NewRoot(), c => c.Source == CommandSource.Player, (r, o) => { });
                var fresh = new RootEnvelope { RunId = "next", ContentVersion = "M1Baseline" };
                var command = Command("new", 0); command.ActionKind = "BeginNewRun";
                Assert.That(dispatcher.BeginNewRun(command, fresh).Status, Is.EqualTo(CommandStatus.Rejected));
                command.Source = CommandSource.Player; command.ExpectedRevision = 5;
                Assert.That(dispatcher.BeginNewRun(command, fresh).Error, Is.EqualTo("StaleRevisionOrRun"));
                command.ExpectedRevision = 0;
                Assert.That(dispatcher.BeginNewRun(command, fresh).Status, Is.EqualTo(CommandStatus.Committed));
            }
        }

        [Test] public void ConfirmedCorruptM1ResetMovesAllEvidenceAndAllowsFreshInitialization()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("first", 0), r => Prepare(r, "first", "body"));
            string brokenRoot = Path.Combine(directory, "state-b.bin"); File.WriteAllText(brokenRoot, "broken-m1");
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.ResetCorruptM1Store(false).Status, Is.EqualTo(StoreResetStatus.Refused));
                var reset = store.ResetCorruptM1Store(true); backups.Add(reset.BackupPath);
                Assert.That(reset.Status, Is.EqualTo(StoreResetStatus.Reset));
                Assert.That(File.ReadAllText(Path.Combine(reset.BackupPath, "state-b.bin")), Is.EqualTo("broken-m1"));
                Assert.That(File.Exists(Path.Combine(reset.BackupPath, "commit-ledger.bin")), Is.True);
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Empty));
                Assert.That(Dispatcher(store, NewRoot()).Execute(Command("fresh", 0), r => Prepare(r, "fresh", "fresh-body")).Status, Is.EqualTo(CommandStatus.Committed));
            }
        }

        [Test] public void CorruptStoreWithPermanentStateOrUnverifiableLedgerRefusesReset()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("first", 0), r => {
                    var result = Prepare(r, "first", "body"); result.Appends.Add(new LogAppend { StreamId = "permanent/history", Kind = "achievement", Json = "must-not-lose" }); return result;
                });
            File.WriteAllText(Path.Combine(directory, "state-b.bin"), "broken");
            using (var store = new LocalCheckpointStore(directory, codec)) Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
            File.WriteAllText(Path.Combine(directory, "commit-ledger.bin"), "broken-ledger");
            using (var store = new LocalCheckpointStore(directory, codec)) Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
            Assert.That(File.Exists(Path.Combine(directory, "state-b.bin")), Is.True);
        }

        [TestCase(StorageFaultPoint.BeforeLogAppend)]
        [TestCase(StorageFaultPoint.AfterLogFlush)]
        [TestCase(StorageFaultPoint.BeforeRootWrite)]
        [TestCase(StorageFaultPoint.AfterRootFlush)]
        [TestCase(StorageFaultPoint.BeforeCommitReceipt)]
        [TestCase(StorageFaultPoint.DuringCommitReceipt)]
        public void FirstCommitFailureCanBeExplicitlyResetUsingUntouchedGenesis(StorageFaultPoint point)
        {
            using (var store = new LocalCheckpointStore(directory, codec, p => { if (p == point) throw new IOException("first-commit-fault"); }))
                Assert.That(Dispatcher(store, NewRoot()).Execute(Command("initial", 0), r => Prepare(r, "initial", "body")).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.ResetCorruptM1Store(false).Status, Is.EqualTo(StoreResetStatus.Refused));
                var reset = store.ResetCorruptM1Store(true); backups.Add(reset.BackupPath);
                Assert.That(reset.Status, Is.EqualTo(StoreResetStatus.Reset));
                Assert.That(File.Exists(Path.Combine(reset.BackupPath, "genesis.bin")), Is.True);
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Empty));
                Assert.That(Dispatcher(store, NewRoot()).Execute(Command("fresh", 0), r => Prepare(r, "fresh", "new")).Status, Is.EqualTo(CommandStatus.Committed));
            }
        }

        [Test] public void FirstUncertainPermanentWriteRevokesGenesisOnlyReset()
        {
            using (var store = new LocalCheckpointStore(directory, codec, p => { if (p == StorageFaultPoint.BeforeLogAppend) throw new IOException("permanent-intent"); }))
                Assert.That(Dispatcher(store, NewRoot()).Execute(Command("initial", 0), r => {
                    var prepared = Prepare(r, "initial", "body");
                    prepared.Appends.Add(new LogAppend { StreamId = "permanent/history", Kind = "achievement", Json = "unknown" }); return prepared;
                }).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
            Assert.That(File.Exists(Path.Combine(directory, "permanent-touched.bin")), Is.True);
        }

        [Test] public void InvalidGenesisDoesNotProveFirstStoreEmpty()
        {
            using (var store = new LocalCheckpointStore(directory, codec)) Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Empty));
            File.WriteAllText(Path.Combine(directory, "genesis.bin"), "broken-proof");
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.CorruptCommitted));
                Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
            }
        }

        [Test] public void OldEmptyReceiptCannotOverrideANewerUnknownPermanentWrite()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("old-empty", 0), r => Prepare(r, "old-empty", "old"));
            using (var store = new LocalCheckpointStore(directory, codec, p => { if (p == StorageFaultPoint.BeforeCommitReceipt) throw new IOException("unknown-permanent"); }))
                Assert.That(Dispatcher(store, store.Load().Root).Execute(Command("permanent", 1), r => {
                    var prepared = Prepare(r, "permanent", "candidate");
                    prepared.Appends.Add(new LogAppend { StreamId = "permanent/history", Kind = "achievement", Json = "uncertain" }); return prepared;
                }).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
            File.WriteAllText(Path.Combine(directory, "state-b.bin"), "old-current-corrupt");
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
            Assert.That(File.Exists(Path.Combine(directory, "permanent-touched.bin")), Is.True);
        }

        [Test] public void InvalidPermanentMarkerStopsThePermanentCommitBeforeDataAppend()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var dispatcher = Dispatcher(store, NewRoot());
                dispatcher.Execute(Command("base", 0), r => Prepare(r, "base", "old"));
                File.WriteAllText(Path.Combine(directory, "permanent-touched.bin"), "partial-marker");
                Assert.That(dispatcher.Execute(Command("permanent", 1), r => {
                    r.PermanentFragments.Add(new VersionedFragment { Owner = "progression", Scope = "permanent", SchemaVersion = 1, Json = "must-not-commit" });
                    return Prepare(r, "permanent", "new");
                }).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
                var restored = store.Load(); Assert.That(restored.Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(restored.Root.Revision, Is.EqualTo(1)); Assert.That(restored.Root.PermanentFragments, Is.Empty);
            }
        }

        [Test] public void UnreadableOrNonFileMarkerDoesNotCountAsAbsent()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("base", 0), r => Prepare(r, "base", "old"));
            File.WriteAllText(Path.Combine(directory, "state-b.bin"), "broken");
            string marker = Path.Combine(directory, "permanent-touched.bin");
            Directory.CreateDirectory(marker);
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
            Directory.Delete(marker);
            using (var locked = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var store = new LocalCheckpointStore(directory, codec))
                Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
        }

        [TestCase(StoreResetFaultPoint.BeforeMove)]
        [TestCase(StoreResetFaultPoint.AfterMove)]
        [TestCase(StoreResetFaultPoint.BeforeInitialize)]
        [TestCase(StoreResetFaultPoint.AfterInitialize)]
        public void ResetInterruptionRetainsOriginalEvidenceOrLocatableBackup(StoreResetFaultPoint point)
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("base", 0), r => Prepare(r, "base", "old"));
            File.WriteAllText(Path.Combine(directory, "state-b.bin"), "broken-evidence");
            using (var store = new LocalCheckpointStore(directory, codec, resetInject: p => { if (p == point) throw new IOException("reset-interrupted"); }))
            {
                var cancel = store.ResetCorruptM1Store(false);
                Assert.That(cancel.Status, Is.EqualTo(StoreResetStatus.Refused));
                Assert.That(File.ReadAllText(Path.Combine(directory, "state-b.bin")), Is.EqualTo("broken-evidence"));
                var reset = store.ResetCorruptM1Store(true); Assert.That(reset.Status, Is.EqualTo(StoreResetStatus.Unknown));
                if (point == StoreResetFaultPoint.BeforeMove)
                    Assert.That(File.ReadAllText(Path.Combine(directory, "state-b.bin")), Is.EqualTo("broken-evidence"));
                else
                {
                    backups.Add(reset.BackupPath);
                    Assert.That(store.GetPreservedBackupPaths(), Does.Contain(reset.BackupPath));
                    Assert.That(File.ReadAllText(Path.Combine(reset.BackupPath, "state-b.bin")), Is.EqualTo("broken-evidence"));
                    Assert.That(File.Exists(Path.Combine(reset.BackupPath, "commit-ledger.bin")), Is.True);
                }
            }
        }

        [Test] public void ResetEligibilityIsReadOnlyAndConfirmationRechecksChangedEvidence()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
                Dispatcher(store, NewRoot()).Execute(Command("base", 0), r => Prepare(r, "base", "old"));
            File.WriteAllText(Path.Combine(directory, "state-b.bin"), "broken");
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var before = Directory.GetFiles(directory).ToDictionary(p => p, p => ContentHash.Sha256(File.ReadAllBytes(p)));
                string reason;
                Assert.That(store.CanResetCorruptM1Store(out reason), Is.True);
                Assert.That(reason, Is.Not.Empty);
                Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(before.Count));
                foreach (var item in before) Assert.That(ContentHash.Sha256(File.ReadAllBytes(item.Key)), Is.EqualTo(item.Value));
                Assert.That(store.GetPreservedBackupPaths(), Is.Empty);
                File.WriteAllText(Path.Combine(directory, "permanent-touched.bin"), "new-uncertain-marker");
                Assert.That(store.CanResetCorruptM1Store(out reason), Is.False);
                Assert.That(store.ResetCorruptM1Store(true).Status, Is.EqualTo(StoreResetStatus.Refused));
                Assert.That(File.ReadAllText(Path.Combine(directory, "state-b.bin")), Is.EqualTo("broken"));
                Assert.That(store.GetPreservedBackupPaths(), Is.Empty);
            }
        }
    }
}
