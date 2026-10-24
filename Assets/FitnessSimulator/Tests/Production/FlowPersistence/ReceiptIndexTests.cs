using System;
using System.IO;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class ReceiptIndexTests
    {
        private sealed class CountingCodec : IStateCodec
        {
            private readonly UnityJsonStateCodec inner = new UnityJsonStateCodec();
            public int DataFrameReads;
            public string Encode<T>(T value) { return inner.Encode(value); }
            public T Decode<T>(string json) { if (typeof(T).Name == "DataFrame") DataFrameReads++; return inner.Decode<T>(json); }
        }
        private string directory;
        private CountingCodec codec;
        [SetUp] public void Setup()
        { directory = Path.Combine(Path.GetTempPath(), "ReceiptIndexTests", Guid.NewGuid().ToString("N")); codec = new CountingCodec(); }
        [TearDown] public void Cleanup()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock");
        }
        private static RootEnvelope Root() { return new RootEnvelope { RunId = "run", ContentVersion = "fixture" }; }
        private static CommitResult Add(LocalCheckpointStore store, RootEnvelope root, string id, string semantic = null, string result = "payload")
        {
            return store.Commit(root.Generation, root, new OperationReceipt { RunId = root.RunId, OperationId = id,
                SemanticKey = semantic ?? "key/" + id, PayloadHash = "hash/" + id, Result = result }, Array.Empty<LogAppend>());
        }
        private RootEnvelope Populate(LocalCheckpointStore store, int count, string result = "payload")
        {
            var root = Root();
            for (int i = 0; i < count; i++)
            {
                var saved = Add(store, root, "op" + i, result: result);
                Assert.That(saved.Status, Is.EqualTo(CommitStatus.Committed)); root = saved.Root;
            }
            return root;
        }

        [Test] public void NewKeyLookupsAndCommitsDoNotDecodeHistoricalFrames()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var root = Populate(store, 80); int reads = codec.DataFrameReads;
                for (int i = 0; i < 30; i++)
                {
                    Assert.That(store.FindReceipt("missing" + i), Is.Null);
                    Assert.That(store.FindSemanticReceipt("run", "missing-key" + i), Is.Null);
                }
                Assert.That(codec.DataFrameReads, Is.EqualTo(reads));
                var saved = Add(store, root, "missing0", "missing-key0");
                Assert.That(saved.Status, Is.EqualTo(CommitStatus.Committed));
                Assert.That(store.FindReceipt("missing0").SemanticKey, Is.EqualTo("missing-key0"));
                Assert.That(store.FindSemanticReceipt("run", "missing-key0").OperationId, Is.EqualTo("missing0"));
                Assert.That(codec.DataFrameReads, Is.EqualTo(reads));
            }
        }

        [Test] public void LoadBuildsIndexDuringSingleValidationPassAndColdReadsOneFrame()
        {
            using (var store = new LocalCheckpointStore(directory, codec)) Populate(store, 80);
            codec.DataFrameReads = 0;
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(codec.DataFrameReads, Is.EqualTo(80));
                Assert.That(store.FindReceipt("never"), Is.Null);
                Assert.That(store.FindSemanticReceipt("run", "key/op0").OperationId, Is.EqualTo("op0"));
                Assert.That(codec.DataFrameReads, Is.EqualTo(81));
                var receipt = store.FindReceipt("op0"); receipt.Result = "mutated";
                Assert.That(store.FindReceipt("op0").Result, Is.EqualTo("payload"));
                Assert.That(codec.DataFrameReads, Is.EqualTo(83));
            }
        }

        [Test] public void HotAndReturnedCommitReceiptsCannotMutateAuthority()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var saved = Add(store, Root(), "one");
                saved.Receipt.PayloadHash = "changed"; saved.Receipt.Result = "changed";
                var queried = store.FindReceipt("one"); queried.PayloadHash = "changed-again";
                Assert.That(store.FindSemanticReceipt("run", "key/one").PayloadHash, Is.EqualTo("hash/one"));
                Assert.That(store.FindReceipt("one").Result, Is.EqualTo("payload"));
            }
        }

        [TestCase(StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement, true)]
        public void UnknownWriteBlocksIndexUntilLoadAndNeverCachesUncommittedReceipt(StorageFaultPoint fault, bool committed)
        {
            using (var store = new LocalCheckpointStore(directory, codec)) Populate(store, 2);
            bool armed = true;
            using (var store = new LocalCheckpointStore(directory, codec, point => { if (armed && point == fault) throw new IOException("fault"); }))
            {
                var root = store.Load().Root;
                Assert.That(Add(store, root, "pending").Status, Is.EqualTo(CommitStatus.Unknown));
                Assert.Throws<IOException>(() => store.FindReceipt("pending"));
                Assert.Throws<IOException>(() => store.FindSemanticReceipt("run", "key/pending"));
                armed = false; Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(store.FindReceipt("pending") != null, Is.EqualTo(committed));
                Assert.That(store.FindSemanticReceipt("run", "key/pending") != null, Is.EqualTo(committed));
            }
        }

        [Test] public void NewRunDropsOldIndexAndResetEligibilityProbeRestoresCurrentIndex()
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var root = Populate(store, 70);
                string reason; Assert.That(store.CanResetCorruptM1Store(out reason), Is.False);
                int reads = codec.DataFrameReads;
                Assert.That(store.FindReceipt("never"), Is.Null);
                Assert.That(store.FindReceipt("op69").OperationId, Is.EqualTo("op69"));
                Assert.That(codec.DataFrameReads, Is.EqualTo(reads));
                root.RunId = "new-run";
                var saved = Add(store, root, "new-start", "key/op0");
                Assert.That(saved.Status, Is.EqualTo(CommitStatus.Committed));
                Assert.That(store.FindReceipt("op0"), Is.Null);
                Assert.That(store.FindSemanticReceipt("run", "key/op0"), Is.Null);
                Assert.That(store.FindSemanticReceipt("new-run", "key/op0").OperationId, Is.EqualTo("new-start"));
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(store.FindReceipt("op0"), Is.Null);
                Assert.That(store.FindReceipt("new-start"), Is.Not.Null);
            }
        }

        [Test] public void SegmentedColdReceiptUsesAnchoredLocationAndCorruptionStillBlocks()
        {
            using (var store = new LocalCheckpointStore(directory, codec)) Populate(store, 70, new string('x', 18000));
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                int reads = codec.DataFrameReads;
                Assert.That(store.FindReceipt("op0").Result.Length, Is.EqualTo(18000));
                Assert.That(codec.DataFrameReads, Is.EqualTo(reads + 1));
                string path = Path.Combine(directory, "log-" + ContentHash.Sha256("run/run/receipts") + "-00000000.bin");
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write)) { stream.Position = 12; stream.WriteByte(0xff); }
                Assert.Throws<IOException>(() => store.FindReceipt("op0"));
                Assert.Throws<IOException>(() => store.FindReceipt("op69"));
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.CorruptCommitted));
            }
        }
    }
}
