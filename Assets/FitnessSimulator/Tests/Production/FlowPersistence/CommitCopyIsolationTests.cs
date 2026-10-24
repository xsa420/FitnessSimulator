using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class CommitCopyIsolationTests
    {
        private sealed class CountingCodec : IStateCodec
        {
            private readonly UnityJsonStateCodec inner = new UnityJsonStateCodec();
            public int RootEncodes, RootDecodes;
            public long RootEncodedBytes;
            public string Encode<T>(T value)
            {
                string json = inner.Encode(value);
                if (typeof(T) == typeof(RootEnvelope)) { RootEncodes++; RootEncodedBytes += Encoding.UTF8.GetByteCount(json); }
                return json;
            }
            public T Decode<T>(string json)
            { if (typeof(T) == typeof(RootEnvelope)) RootDecodes++; return inner.Decode<T>(json); }
            public void Reset() { RootEncodes = RootDecodes = 0; RootEncodedBytes = 0; }
        }
        private string directory;
        private CountingCodec codec;
        [SetUp] public void Setup()
        { directory = Path.Combine(Path.GetTempPath(), "CommitCopyTests", Guid.NewGuid().ToString("N")); codec = new CountingCodec(); }
        [TearDown] public void Cleanup()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock");
        }
        private static RootEnvelope Initial()
        {
            var root = new RootEnvelope { RunId = "run", ContentVersion = "fixture" };
            root.SetFragment("body", "player", 1, 0, "old-body");
            root.PermanentFragments.Add(new VersionedFragment { Owner = "grant", Scope = "permanent", SchemaVersion = 1, Json = "old-grant" });
            return root;
        }
        private static OperationReceipt Receipt(string id, string run = "run")
        { return new OperationReceipt { RunId = run, OperationId = id, SemanticKey = "semantic/" + id, PayloadHash = "hash/" + id, Result = "result/" + id }; }
        private CommitResult Commit(LocalCheckpointStore store, RootEnvelope root, string id)
        { return store.Commit(root.Generation, root, Receipt(id, root.RunId), Array.Empty<LogAppend>()); }

        [Test] public void NormalCommitEncodesRootOnlyForDurableWrite()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var root = Initial(); root.SetFragment("large", "player", 1, 0, new string('x', 40000));
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Empty)); codec.Reset();
                var first = Commit(store, root, "first");
                Assert.That(first.Status, Is.EqualTo(CommitStatus.Committed));
                Assert.That(codec.RootEncodes, Is.EqualTo(1)); Assert.That(codec.RootDecodes, Is.EqualTo(0));
                Assert.That(codec.RootEncodedBytes, Is.GreaterThan(40000));
                codec.Reset(); var second = Commit(store, first.Root, "second");
                Assert.That(second.Status, Is.EqualTo(CommitStatus.Committed));
                Assert.That(codec.RootEncodes, Is.EqualTo(1), "Do not encode all fragments to copy 64 receipts or return the saved root.");
                Assert.That(codec.RootDecodes, Is.EqualTo(0));
                Assert.That(second.Root.RecentReceipts.Count, Is.EqualTo(2));
                Assert.That(second.Root.FindFragment("large", "player").Json.Length, Is.EqualTo(40000));
            }
        }

        [Test] public void CallerMutationOfCandidateReturnedRootAndReceiptDoesNotReachAuthority()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var candidate = Initial(); var receipt = Receipt("first");
                var result = store.Commit(0, candidate, receipt, Array.Empty<LogAppend>());
                Assert.That(result.Status, Is.EqualTo(CommitStatus.Committed));
                var safe = codec.Decode<RootEnvelope>(codec.Encode(result.Root));
                candidate.Fragments[0].Json = "candidate mutation"; candidate.PermanentFragments[0].Json = "candidate grant mutation";
                candidate.Fragments.Clear(); receipt.PayloadHash = "caller receipt mutation";
                result.Root.Fragments[0].Json = "returned body mutation";
                result.Root.PermanentFragments[0].Json = "returned grant mutation";
                result.Root.LogPrefixes[0].Hash = "returned prefix mutation";
                result.Root.RecentReceipts[0].PayloadHash = "returned cached receipt mutation";
                result.Root.RecentReceipts.Clear(); result.Receipt.Result = "returned receipt mutation";
                var repeated = store.Commit(safe.Generation, safe, Receipt("first"), Array.Empty<LogAppend>());
                Assert.That(repeated.Status, Is.EqualTo(CommitStatus.Committed));
                Assert.That(codec.Encode(repeated.Root), Is.EqualTo(codec.Encode(safe)));
                Assert.That(store.FindReceipt("first").Result, Is.EqualTo("result/first"));
                Assert.That(store.FindSemanticReceipt("run", "semantic/first").PayloadHash, Is.EqualTo("hash/first"));
                var second = Commit(store, safe, "second");
                Assert.That(second.Status, Is.EqualTo(CommitStatus.Committed));
                Assert.That(second.Root.FindFragment("body", "player").Json, Is.EqualTo("old-body"));
                Assert.That(second.Root.PermanentFragments[0].Json, Is.EqualTo("old-grant"));
                Assert.That(second.Root.RecentReceipts[0].PayloadHash, Is.EqualTo("hash/first"));
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
        public void FaultLeavesCallerCandidateUnchangedAndRecoveryKeepsWholeAuthoritativeRoot(StorageFaultPoint fault, bool newRoot)
        {
            using (var store = new LocalCheckpointStore(directory, codec)) Assert.That(Commit(store, Initial(), "base").Status, Is.EqualTo(CommitStatus.Committed));
            using (var store = new LocalCheckpointStore(directory, codec, point => { if (point == fault) throw new IOException("fault"); }))
            {
                var candidate = store.Load().Root; candidate.Fragments[0].Json = "new-body";
                candidate.PermanentFragments.Add(new VersionedFragment { Owner = "second-grant", Scope = "permanent", SchemaVersion = 1, Json = "new-grant" });
                string before = codec.Encode(candidate);
                Assert.That(Commit(store, candidate, "next").Status, Is.EqualTo(CommitStatus.Unknown));
                Assert.That(codec.Encode(candidate), Is.EqualTo(before));
                Assert.Throws<IOException>(() => store.FindReceipt("next"));
            }
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(loaded.Root.FindFragment("body", "player").Json, Is.EqualTo(newRoot ? "new-body" : "old-body"));
                Assert.That(loaded.Root.PermanentFragments.Count, Is.EqualTo(newRoot ? 2 : 1));
                Assert.That(store.FindReceipt("next") != null, Is.EqualTo(newRoot));
                loaded.Root.PermanentFragments.Clear();
                Assert.That(store.Load().Root.PermanentFragments.Count, Is.EqualTo(newRoot ? 2 : 1));
            }
        }

        [Test] public void NewRunHasIndependentEmptyReceiptListAndPreservesPermanentFragments()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var baseline = Commit(store, Initial(), "base");
                var dispatcher = new SerialCommandDispatcher(store, codec, baseline.Root, c => c.Source == CommandSource.Test, null);
                var fresh = new RootEnvelope { RunId = "new-run", ContentVersion = "fixture" };
                fresh.SetFragment("body", "player", 1, 0, "fresh-body");
                var command = new CommandEnvelope { OperationId = "new-game", ActionKind = "BeginNewRun", RunId = "run",
                    ExpectedRevision = baseline.Root.Revision, Source = CommandSource.Test };
                Assert.That(dispatcher.BeginNewRun(command, fresh).Status, Is.EqualTo(CommandStatus.Committed));
                fresh.Fragments[0].Json = "mutated fresh"; baseline.Root.PermanentFragments[0].Json = "mutated old";
                var current = dispatcher.Snapshot;
                Assert.That(current.FindFragment("body", "player").Json, Is.EqualTo("fresh-body"));
                Assert.That(current.PermanentFragments[0].Json, Is.EqualTo("old-grant"));
                Assert.That(current.RecentReceipts.Count, Is.EqualTo(1));
                Assert.That(current.RecentReceipts[0].OperationId, Is.EqualTo("new-game"));
                Assert.That(store.FindReceipt("base"), Is.Null);
            }
        }
    }
}

