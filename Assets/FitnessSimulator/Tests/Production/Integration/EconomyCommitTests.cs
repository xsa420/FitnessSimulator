using System;
using System.IO;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Integration
{
    // This exercises the real domain preparation and durable dispatcher together.
    // It is not evidence that a shop has been wired into the player UI.
    public sealed class EconomyCommitTests
    {
        private readonly IStateCodec codec = new UnityJsonStateCodec();
        private string directory;
        private LocalCheckpointStore store;
        private SerialCommandDispatcher dispatcher;
        private StorageFaultPoint? armedFault;
        private const string Run = "economy-integration-run";
        private const string Actor = "player";

        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "Fitness-M2-Economy", Guid.NewGuid().ToString("N"));
            var root = new RootEnvelope { RunId = Run, ContentVersion = "M2-Economy-Integration" };
            foreach (string actor in new[] { Actor, "npc-a" })
            {
                Put(root, "life", actor, LifeRules.Initialize(Run, actor, 300, 0));
                Put(root, "inventory", actor, InventoryRules.Initialize(Run, actor));
                var shop = ShopRules.Initialize(Run, actor, EconomyContent.Gym);
                shop = ShopRules.PrepareRefresh(shop, 1, new RandomSamples {
                    RunId = Run, ActorId = actor, ShopId = EconomyContent.Gym,
                    Purpose = "ShopRefresh", AlgorithmVersion = "integration-samples-v1"
                }).Shop;
                Put(root, ShopOwner, actor, shop);
            }
            Open(root);
            var initialized = dispatcher.Execute(Command("initialize", "initial"), _ => new PreparedOutcome {
                SemanticKey = "fixture-init", EventDisposition = EventDisposition.AuxiliaryOnly, CheckKind = CheckKind.None,
                OutcomeKind = OutcomeKind.Success, OpportunityPolicy = OpportunityPolicy.Keep
            });
            Assert.That(initialized.Status, Is.EqualTo(CommandStatus.Committed));
        }

        [TearDown] public void TearDown() { store?.Dispose(); }
        private string ShopOwner => "shop/" + EconomyContent.Gym;
        private EconomyContext Context() => new EconomyContext { RunId = Run, ActorId = Actor, Day = 1, EventInstanceId = "day1/shop-visit", TransactionOrdinal = 0 };
        private T Read<T>(RootEnvelope root, string owner, string actor = Actor) => codec.Decode<T>(root.FindFragment(owner, actor).Json);
        private void Put<T>(RootEnvelope root, string owner, string actor, T value) => root.SetFragment(owner, actor, 1, root.Revision + 1, codec.Encode(value));
        private CommandEnvelope Command(string operation, string payload) => new CommandEnvelope {
            RunId = Run, ActorId = Actor, ResourceScope = Actor, Source = CommandSource.Test,
            OperationId = operation, ExpectedRevision = dispatcher?.Snapshot.Revision ?? 0,
            EventInstanceId = "day1/shop-visit", ActionKind = "purchase", Payload = payload
        };
        private void Open(RootEnvelope initial)
        {
            store = new LocalCheckpointStore(directory, codec, point => {
                if (armedFault == point) { armedFault = null; throw new IOException("Injected economy commit boundary"); }
            });
            var loaded = store.Load();
            Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Empty).Or.EqualTo(LoadStatus.Loaded));
            dispatcher = new SerialCommandDispatcher(store, codec, loaded.Root ?? initial,
                c => c.Source == CommandSource.Test && c.ActorId == Actor && c.ResourceScope == Actor,
                (candidate, _) => {
                    var fragment = candidate.FindFragment("completion-count", Actor);
                    var count = fragment == null ? new CompletionCount() : codec.Decode<CompletionCount>(fragment.Json);
                    count.Value++; Put(candidate, "completion-count", Actor, count);
                });
        }
        private PurchaseQuote Quote(int packages = 2)
        {
            var root = dispatcher.Snapshot;
            return ShopRules.QuotePurchase(Read<LifeActorState>(root, "life"), Read<InventoryState>(root, "inventory"), Read<ShopState>(root, ShopOwner), "gym_note_1", packages);
        }
        private CommandResult Purchase(CommandEnvelope command, PurchaseQuote quote, EconomyContext context)
        {
            return dispatcher.Execute(command, _ => ShopRules.PurchaseSemanticKey(context), candidate => {
                if (context.RunId != candidate.RunId || context.ActorId != command.ActorId) throw new InvalidOperationException("Context/capability mismatch");
                var prepared = ShopRules.PreparePurchase(Read<LifeActorState>(candidate, "life"), Read<InventoryState>(candidate, "inventory"), Read<ShopState>(candidate, ShopOwner), quote, context);
                if (prepared.Status != EconomyStatus.Prepared) throw new InvalidOperationException("Preparation failed: " + prepared.Status);
                Put(candidate, "life", Actor, prepared.Life);
                Put(candidate, "inventory", Actor, prepared.Inventory);
                Put(candidate, ShopOwner, Actor, prepared.Shop);
                var result = new PreparedOutcome { SemanticKey = prepared.Receipt.SemanticKey, Result = "PurchaseCompleted",
                    OutcomeKind = OutcomeKind.Success, EventDisposition = EventDisposition.CompleteNode,
                    CheckKind = CheckKind.NormalCompletion, OpportunityPolicy = OpportunityPolicy.Keep };
                result.Appends.Add(new LogAppend { StreamId = "run/" + Run + "/records", Kind = "Purchase", Json = codec.Encode(prepared.Receipt) });
                return result;
            });
        }
        private void AssertPurchased(bool expected)
        {
            var root = dispatcher.Snapshot;
            Assert.That(Read<LifeActorState>(root, "life").Wallet.Balance, Is.EqualTo(expected ? 180 : 300));
            Assert.That(InventoryRules.Quantity(Read<InventoryState>(root, "inventory"), EconomyContent.Notebook), Is.EqualTo(expected ? 2 : 0));
            Assert.That(Read<ShopState>(root, ShopOwner).Offers[0].PackagesRemaining, Is.EqualTo(expected ? 1 : 3));
            var count = root.FindFragment("completion-count", Actor);
            Assert.That(count == null ? 0 : codec.Decode<CompletionCount>(count.Json).Value, Is.EqualTo(expected ? 1 : 0));
            Assert.That(Read<LifeActorState>(root, "life", "npc-a").Wallet.Balance, Is.EqualTo(300));
            Assert.That(Read<ShopState>(root, ShopOwner, "npc-a").Offers[0].PackagesRemaining, Is.EqualTo(3));
        }

        [Test] public void PurchaseCommitsThreeDomainsOnceAndOldQuoteCanReplayAfterRestart()
        {
            var quote = Quote(); var context = Context(); var command = Command("purchase-1", codec.Encode(quote));
            Assert.That(Purchase(command, quote, context).Status, Is.EqualTo(CommandStatus.Committed));
            AssertPurchased(true);
            store.Dispose(); Open(new RootEnvelope { RunId = Run });
            Assert.That(Purchase(command, quote, context).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
            var newOperation = Command("purchase-retry-new-id", command.Payload);
            newOperation.ExpectedRevision = 0; // Semantic replay precedes a now stale root revision and quote.
            Assert.That(Purchase(newOperation, quote, context).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
            AssertPurchased(true);
            Assert.That(store.ReadLog("run/" + Run + "/records", 0, 10).Count, Is.EqualTo(1));
        }

        [Test] public void SameTransactionWithDifferentPurchaseIsConflictAndCannotReturnFalseSuccess()
        {
            var quote = Quote(); var context = Context();
            Assert.That(Purchase(Command("first", codec.Encode(quote)), quote, context).Status, Is.EqualTo(CommandStatus.Committed));
            var different = Quote(1);
            var result = Purchase(Command("different", codec.Encode(different)), different, context);
            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected));
            Assert.That(result.Error, Is.EqualTo("SemanticConflict"));
            AssertPurchased(true);
        }

        [TestCase(StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement, true)]
        public void StorageFaultPublishesEitherWholePurchaseOrNoneAndRetryNeverDoubleCharges(StorageFaultPoint point, bool committed)
        {
            var quote = Quote(); var context = Context(); var command = Command("faulted-purchase", codec.Encode(quote));
            armedFault = point;
            Assert.That(Purchase(command, quote, context).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
            Assert.That(dispatcher.Recover().Status, Is.EqualTo(LoadStatus.Loaded));
            AssertPurchased(committed);
            var retry = Purchase(command, quote, context);
            Assert.That(retry.Status, Is.EqualTo(committed ? CommandStatus.AlreadyCommitted : CommandStatus.Committed));
            AssertPurchased(true);
        }

        [Test] public void UnauthorizedActorCannotUseSemanticReceiptResolver()
        {
            var quote = Quote(); var command = Command("foreign", codec.Encode(quote)); command.ActorId = "npc-a";
            bool resolverCalled = false;
            var result = dispatcher.Execute(command, _ => { resolverCalled = true; return ShopRules.PurchaseSemanticKey(Context()); }, _ => throw new Exception("Must not run"));
            Assert.That(result.Status, Is.EqualTo(CommandStatus.Rejected)); Assert.That(resolverCalled, Is.False);
            AssertPurchased(false);
        }
        [Serializable] private sealed class CompletionCount { public int Value; }
    }
}
