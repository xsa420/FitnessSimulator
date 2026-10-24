using System;
using System.Linq;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class EconomyRulesTests
    {
        private static EconomyContext Context(int day = 1, string actor = "p", long ordinal = 0) => new EconomyContext {
            RunId = "run", ActorId = actor, Day = day, EventInstanceId = "visit-" + day, TransactionOrdinal = ordinal };
        private static RandomSamples Samples(ShopState shop, params uint[] values) => new RandomSamples {
            RunId = shop.RunId, ActorId = shop.ActorId, ShopId = shop.ShopId, Purpose = "ShopRefresh", AlgorithmVersion = "TestSamples.v1", Values = values };
        private static ShopState AdvanceShop(string id, int day, string actor = "p", int veteran = 0, int beginner = 0)
        {
            var shop = ShopRules.Initialize("run", actor, id);
            for (int d = 1; d <= day; d++) {
                shop = ShopRules.ObserveUnlocks(shop, d, veteran, beginner);
                shop = ShopRules.PrepareRefresh(shop, d, Samples(shop, 0, 0, 0)).Shop;
            }
            return shop;
        }
        [Test]
        public void PublicSemanticResolversMatchPreparedReceiptsBeforeQuoteValidation()
        {
            var context = Context(); var life = LifeRules.Initialize("run", "p", 300, 0); var inv = InventoryRules.Initialize("run", "p");
            var shop = AdvanceShop(EconomyContent.Gym, 1);
            var purchase = ShopRules.PreparePurchase(life, inv, shop, ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", 1), context);
            Assert.That(purchase.Receipt.SemanticKey, Is.EqualTo(ShopRules.PurchaseSemanticKey(context)));
            Assert.That(InventoryRules.PreparePayment(life, context, "Teaching", 80).Receipt.SemanticKey, Is.EqualTo(InventoryRules.PaymentSemanticKey(context, "Teaching")));
            inv = InventoryRules.PrepareGrant(inv, EconomyContent.Bookmark, 1);
            Assert.That(InventoryRules.PrepareDelivery(inv, context, EconomyContent.Bookmark, 1, true).SemanticKey, Is.EqualTo(InventoryRules.DeliverySemanticKey(context)));
            var point = InventoryRules.OpenPoint(context, "note");
            Assert.That(InventoryRules.PrepareUse(inv, point, context, null, true, true, true).SemanticKey, Is.EqualTo(InventoryRules.ItemPointSemanticKey(context, "note")));
            Assert.That(ShopRules.PurchaseSemanticKey(Context(1, "p", 1)), Is.Not.EqualTo(purchase.Receipt.SemanticKey));
        }
        [Test]
        public void PaymentInsufficientKeepsWalletAndZeroPaymentIsExplicitSuccess()
        {
            var life = LifeRules.Initialize("run", "p", 79, 10);
            var denied = InventoryRules.PreparePayment(life, Context(), "Teaching", 80);
            Assert.That(denied.Status, Is.EqualTo(EconomyStatus.InsufficientFunds));
            Assert.That(denied.Receipt, Is.Null); Assert.That(denied.Life.Revision, Is.EqualTo(life.Revision));
            var free = InventoryRules.PreparePayment(life, Context(), "Teaching", 0);
            Assert.That(free.Status, Is.EqualTo(EconomyStatus.Prepared)); Assert.That(free.Receipt.Amount, Is.Zero);
            Assert.That(free.Life.Wallet.Balance, Is.EqualTo(79)); Assert.That(free.Life.Wallet.Affluence, Is.EqualTo(10));
            Assert.Throws<ArgumentOutOfRangeException>(() => InventoryRules.PreparePayment(life, Context(), "x", -1));
        }
        [Test]
        public void WholePackagePurchaseReturnsAtomicIndependentCandidates()
        {
            var shop = AdvanceShop(EconomyContent.Gym, 10);
            var life = LifeRules.Initialize("run", "p", 400, 0, 10);
            var inventory = InventoryRules.Initialize("run", "p");
            var quote = ShopRules.QuotePurchase(life, inventory, shop, "gym_note_3", 1);
            var result = ShopRules.PreparePurchase(life, inventory, shop, quote, Context(10));
            Assert.That(result.Status, Is.EqualTo(EconomyStatus.Prepared));
            Assert.That(result.Life.Wallet.Balance, Is.EqualTo(235));
            Assert.That(InventoryRules.Quantity(result.Inventory, EconomyContent.Notebook), Is.EqualTo(3));
            Assert.That(result.Shop.Offers.Single(x => x.ProductId == "gym_note_3").PackagesRemaining, Is.Zero);
            Assert.That(life.Wallet.Balance, Is.EqualTo(400)); Assert.That(InventoryRules.Quantity(inventory, EconomyContent.Notebook), Is.Zero);
            Assert.That(shop.Offers.Single(x => x.ProductId == "gym_note_3").PackagesRemaining, Is.EqualTo(1));
            var stale = ShopRules.PreparePurchase(result.Life, result.Inventory, result.Shop, quote, Context(10));
            Assert.That(stale.Status, Is.EqualTo(EconomyStatus.StaleQuote));
            var newQuote = ShopRules.QuotePurchase(result.Life, result.Inventory, result.Shop, "gym_note_3", 1);
            Assert.That(ShopRules.PreparePurchase(result.Life, result.Inventory, result.Shop, newQuote, Context(10, "p", 1)).Status, Is.EqualTo(EconomyStatus.OutOfStock));
            // Public receipt lookup is required to turn the original operation replay into AlreadyCommitted.
            Assert.That(result.Receipt.SemanticKey, Does.Contain("Purchase"));
        }
        [Test]
        public void NoPartialPurchaseAndForgedQuoteCannotChangeAnything()
        {
            var shop = AdvanceShop(EconomyContent.Gym, 1); var inventory = InventoryRules.Initialize("run", "p");
            var poor = LifeRules.Initialize("run", "p", 59, 0);
            var quote = ShopRules.QuotePurchase(poor, inventory, shop, "gym_note_1", 1);
            Assert.That(ShopRules.PreparePurchase(poor, inventory, shop, quote, Context()).Status, Is.EqualTo(EconomyStatus.InsufficientFunds));
            var rich = LifeRules.Initialize("run", "p", 1000, 0);
            var tooMany = ShopRules.QuotePurchase(rich, inventory, shop, "gym_note_1", 4);
            Assert.That(ShopRules.PreparePurchase(rich, inventory, shop, tooMany, Context()).Status, Is.EqualTo(EconomyStatus.OutOfStock));
            var forged = ShopRules.QuotePurchase(rich, inventory, shop, "gym_note_1", 1); forged.TotalPrice = 0;
            Assert.Throws<ArgumentException>(() => ShopRules.PreparePurchase(rich, inventory, shop, forged, Context()));
            Assert.That(rich.Wallet.Balance, Is.EqualTo(1000)); Assert.That(shop.Offers[0].PackagesRemaining, Is.EqualTo(3));
        }
        [Test]
        public void UnlockWaitsForNextRefreshAndNeverRelocks()
        {
            var d7 = AdvanceShop(EconomyContent.Gym, 7);
            var d8 = ShopRules.ObserveUnlocks(d7, 8, 40, 0);
            Assert.That(d8.Cycle, Is.EqualTo(3)); Assert.That(d8.Offers.Length, Is.EqualTo(1));
            Assert.That(d8.Unlocks.Single(x => x.ProductId == "gym_note_3").EligibleFromCycle, Is.EqualTo(4));
            var d9 = ShopRules.ObserveUnlocks(d8, 9, 0, 0);
            Assert.That(d9.Unlocks.Any(x => x.ProductId == "gym_note_5"), Is.True);
            var d10 = ShopRules.PrepareRefresh(d9, 10, Samples(d9, 1, 1));
            Assert.That(d10.Status, Is.EqualTo(EconomyStatus.Prepared)); Assert.That(d10.Shop.Offers.Length, Is.EqualTo(2));
            Assert.That(d10.Shop.Offers.Select(x => x.ProductId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(d10.Shop.Offers.Any(x => x.ProductId == "gym_note_3"), Is.True);
            Assert.That(ShopRules.Initialize("new-run", "p", EconomyContent.Gym).Unlocks.Length, Is.EqualTo(1));
        }
        [Test]
        public void SameDayUnlockUsesCommittedSnapshotBoundaryNotCalendarDelay()
        {
            var d7 = AdvanceShop(EconomyContent.Gym, 7);
            // Caller commits the unlock before locking N03 eligibility: it is eligible for cycle 4.
            var before = ShopRules.ObserveUnlocks(d7, 10, 40, 0);
            Assert.That(before.Unlocks.Single(x => x.ProductId == "gym_note_5").EligibleFromCycle, Is.EqualTo(4));
            var withUnlock = ShopRules.PrepareRefresh(before, 10, Samples(before, 2, 0));
            Assert.That(withUnlock.Shop.Offers.Any(x => x.ProductId == "gym_note_5"), Is.True);
            Assert.That(withUnlock.ExpectedShopRevision, Is.EqualTo(before.Revision));
            // A completed refresh keeps its frozen offers even if the same day later unlocks a product.
            var frozen = ShopRules.PrepareRefresh(d7, 10, Samples(d7));
            var after = ShopRules.ObserveUnlocks(frozen.Shop, 10, 40, 0);
            Assert.That(after.Unlocks.Single(x => x.ProductId == "gym_note_5").EligibleFromCycle, Is.EqualTo(5));
            Assert.That(after.Offers.Any(x => x.ProductId == "gym_note_5"), Is.False);
            var replay = ShopRules.PrepareRefresh(d7, 10, Samples(d7));
            Assert.That(replay.Shop.Offers.Select(x => x.ProductId), Is.EqualTo(frozen.Shop.Offers.Select(x => x.ProductId)));
            Assert.That(replay.SemanticKey, Is.EqualTo(frozen.SemanticKey));
            Assert.That(ShopRules.PrepareRefresh(after, 10, Samples(after)).Status, Is.EqualTo(EconomyStatus.NotDue));
        }
        [Test]
        public void SuppliedRejectionSamplesAreAuditedAndExhaustionIsAtomic()
        {
            var shop = ShopRules.ObserveUnlocks(AdvanceShop(EconomyContent.Gym, 7), 8, 40, 0);
            var insufficient = ShopRules.PrepareRefresh(shop, 10, Samples(shop, uint.MaxValue));
            Assert.That(insufficient.Status, Is.EqualTo(EconomyStatus.NeedRandomSamples));
            Assert.That(insufficient.Shop.Cycle, Is.EqualTo(3)); Assert.That(insufficient.Random.ConsumedValues, Is.Empty);
            var samples = Samples(shop, uint.MaxValue, 0, 1); samples.StartDrawIndex = 10;
            var result = ShopRules.PrepareRefresh(shop, 10, samples);
            Assert.That(result.Random.ConsumedValues, Is.EqualTo(samples.Values));
            Assert.That(result.Random.EndDrawIndex, Is.EqualTo(13));
            Assert.That(result.Shop.Cycle, Is.EqualTo(4));
            Assert.Throws<ArgumentException>(() => ShopRules.PrepareRefresh(shop, 11, samples));
        }
        [Test]
        public void TwoShopsAndFiveActorsRefreshWithoutVisitsOrSharedStock()
        {
            var shops = new[] { "p", "a", "b", "c", "d" }.SelectMany(a => new[] {
                AdvanceShop(EconomyContent.Gym, 11, a), AdvanceShop(EconomyContent.Corner, 11, a) }).ToArray();
            Assert.That(shops.Length, Is.EqualTo(10));
            foreach (var shop in shops) {
                Assert.That(shop.Cycle, Is.EqualTo(shop.ShopId == EconomyContent.Gym ? 4 : 3));
                Assert.That(shop.NextRefreshDay, Is.EqualTo(shop.ShopId == EconomyContent.Gym ? 13 : 16));
            }
            var playerShop = shops.Single(x => x.ActorId == "p" && x.ShopId == EconomyContent.Gym);
            var life = LifeRules.Initialize("run", "p", 500, 0, 11); var inv = InventoryRules.Initialize("run", "p");
            var quote = ShopRules.QuotePurchase(life, inv, playerShop, "gym_note_1", 1);
            var bought = ShopRules.PreparePurchase(life, inv, playerShop, quote, Context(11));
            Assert.That(bought.Shop.Offers.Single(x => x.ProductId == "gym_note_1").PackagesRemaining, Is.EqualTo(2));
            Assert.That(shops.Single(x => x.ActorId == "a" && x.ShopId == EconomyContent.Gym).Offers.Single(x => x.ProductId == "gym_note_1").PackagesRemaining, Is.EqualTo(3));
            Assert.Throws<ArgumentException>(() => ShopRules.PreparePurchase(life, inv, playerShop, quote, Context(11, "a")));
        }
        [Test]
        public void InvalidUseConsumesOnlyItsPointAndValidUseReturnsFixedAllExerciseIntent()
        {
            var inv = InventoryRules.Initialize("run", "p"); var ctx = Context();
            var point = InventoryRules.OpenPoint(ctx, "study-note");
            var failed = InventoryRules.PrepareUse(inv, point, ctx, EconomyContent.Notebook, false, true, true);
            Assert.That(failed.Status, Is.EqualTo(EconomyStatus.InvalidUse)); Assert.That(failed.Point.Consumed, Is.True);
            Assert.That(failed.Inventory.Revision, Is.EqualTo(inv.Revision)); Assert.That(point.Consumed, Is.False);
            Assert.That(InventoryRules.PrepareUse(inv, failed.Point, ctx, EconomyContent.Notebook, false, true, true).Status, Is.EqualTo(EconomyStatus.OpportunityClosed));
            inv = InventoryRules.PrepareGrant(inv, EconomyContent.Notebook, 1);
            var used = InventoryRules.PrepareUse(inv, point, ctx, EconomyContent.Notebook, false, true, true);
            Assert.That(InventoryRules.Quantity(used.Inventory, EconomyContent.Notebook), Is.Zero);
            Assert.That(used.Effects.Single().KnowledgeMicroPerExercise, Is.EqualTo(2000000));
            Assert.That(used.Effects.Single().ExerciseIds, Is.EquivalentTo(ExerciseCatalog.All.Select(x => x.Id)));
            // Body may clip every effect to zero at K100; the consumed item is not refunded.
            Assert.That(used.Point.Consumed, Is.True);
            Assert.That(InventoryRules.PrepareUse(inv, point, ctx, null, true, true, true).Status, Is.EqualTo(EconomyStatus.Skipped));
        }
        [Test]
        public void InvoluntaryLossClampsAtZeroWithoutDebtOrPartialStorySuccess()
        {
            var life = LifeRules.Initialize("run", "p", 50, 10);
            var money = InventoryRules.PrepareMoneyLoss(life, Context(), long.MaxValue);
            Assert.That(money.ActualLoss, Is.EqualTo(50)); Assert.That(money.Life.Wallet.Balance, Is.Zero);
            Assert.That(money.Life.Wallet.Affluence, Is.EqualTo(10)); Assert.That(life.Wallet.Balance, Is.EqualTo(50));
            var inv = InventoryRules.PrepareGrant(InventoryRules.Initialize("run", "p"), EconomyContent.Bookmark, 2);
            var items = InventoryRules.PrepareItemLoss(inv, Context(), EconomyContent.Bookmark, long.MaxValue);
            Assert.That(items.ActualLoss, Is.EqualTo(2)); Assert.That(InventoryRules.Quantity(items.Inventory, EconomyContent.Bookmark), Is.Zero);
            Assert.That(InventoryRules.Quantity(inv, EconomyContent.Bookmark), Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => InventoryRules.PrepareMoneyLoss(life, Context(), -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => InventoryRules.PrepareItemLoss(inv, Context(), EconomyContent.Bookmark, -1));
        }
        [Test]
        public void DeliveryConditionFailureIsDistinctFromMissingItemsAndNeitherConsumesAnything()
        {
            var inventory = InventoryRules.PrepareGrant(InventoryRules.Initialize("run", "p"), EconomyContent.Bookmark, 1);
            var conditionFailure = InventoryRules.PrepareDelivery(inventory, Context(), EconomyContent.Bookmark, 1, false);
            var insufficient = InventoryRules.PrepareDelivery(inventory, Context(), EconomyContent.Bookmark, 2, true);
            Assert.That(conditionFailure.Status, Is.EqualTo(EconomyStatus.ConditionNotMet));
            Assert.That(insufficient.Status, Is.EqualTo(EconomyStatus.InsufficientItems));
            foreach (var result in new[] { conditionFailure, insufficient }) {
                Assert.That(InventoryRules.Quantity(result.Inventory, EconomyContent.Bookmark), Is.EqualTo(1));
                Assert.That(result.Inventory.Revision, Is.EqualTo(inventory.Revision));
                Assert.That(result.Effects, Is.Empty);
                Assert.That(result.Point, Is.Null); // No character-interaction opportunity is owned or consumed here.
            }
            Assert.That(InventoryRules.Quantity(inventory, EconomyContent.Bookmark), Is.EqualTo(1));
        }
        [Test]
        public void CornerUnlockChecksBothDayAndAffectionAndKeepsCurrentOffers()
        {
            var d1 = AdvanceShop(EconomyContent.Corner, 1);
            var at39 = ShopRules.ObserveUnlocks(d1, 2, 0, 39);
            Assert.That(at39.Unlocks.Any(x => x.ProductId == "corner_bookmark_1"), Is.False);
            var at40 = ShopRules.ObserveUnlocks(at39, 2, 0, 40);
            Assert.That(at40.Unlocks.Single(x => x.ProductId == "corner_bookmark_1").EligibleFromCycle, Is.EqualTo(2));
            Assert.That(at40.Offers.Any(x => x.ProductId == "corner_bookmark_1"), Is.False);

            var d26 = AdvanceShop(EconomyContent.Corner, 26, beginner: 39);
            Assert.That(d26.Cycle, Is.EqualTo(6));
            var d27At80 = ShopRules.ObserveUnlocks(d26, 27, 0, 80);
            var d28At79 = ShopRules.ObserveUnlocks(d26, 28, 0, 79);
            Assert.That(d27At80.Unlocks.Any(x => x.ProductId == "corner_bookmark_2"), Is.False);
            Assert.That(d28At79.Unlocks.Any(x => x.ProductId == "corner_bookmark_2"), Is.False);
            var d28At80 = ShopRules.ObserveUnlocks(d26, 28, 0, 80);
            Assert.That(d28At80.Unlocks.Single(x => x.ProductId == "corner_bookmark_2").EligibleFromCycle, Is.EqualTo(7));
            Assert.That(d28At80.Offers.Select(x => x.ProductId), Is.EqualTo(d26.Offers.Select(x => x.ProductId)));
            Assert.That(d28At80.GeneratedAtDay, Is.EqualTo(26));
            var dropped = ShopRules.ObserveUnlocks(d28At80, 29, 0, 0);
            Assert.That(dropped.Unlocks.Any(x => x.ProductId == "corner_bookmark_2"), Is.True);
            var refreshed = ShopRules.PrepareRefresh(dropped, 31, Samples(dropped, 2, 0));
            Assert.That(refreshed.Shop.Cycle, Is.EqualTo(7));
            Assert.That(refreshed.Shop.Offers.Any(x => x.ProductId == "corner_bookmark_2"), Is.True);
            Assert.That(ShopRules.ObserveUnlocks(refreshed.Shop, 32, 0, 0).Unlocks.Any(x => x.ProductId == "corner_bookmark_2"), Is.True);
        }
        [Test]
        public void PreviousCycleQuoteIsRejectedAfterRefreshEvenForSameProduct()
        {
            var oldShop = AdvanceShop(EconomyContent.Gym, 1);
            var life = LifeRules.Initialize("run", "p", 300, 0);
            var inventory = InventoryRules.Initialize("run", "p");
            var quote = ShopRules.QuotePurchase(life, inventory, oldShop, "gym_note_1", 1);
            var newShop = ShopRules.PrepareRefresh(oldShop, 4, Samples(oldShop)).Shop;
            for (int day = 2; day <= 4; day++) life = LifeRules.StartNutritionDay(life, day);
            Assert.That(ShopRules.PreparePurchase(life, inventory, newShop, quote, Context(4)).Status, Is.EqualTo(EconomyStatus.StaleQuote));
            // Isolate the cycle guard from the independently tested revision guards.
            quote.LifeRevision = life.Revision; quote.ShopRevision = newShop.Revision;
            var stale = ShopRules.PreparePurchase(life, inventory, newShop, quote, Context(4));
            Assert.That(stale.Status, Is.EqualTo(EconomyStatus.StaleQuote));
            Assert.That(stale.Life.Wallet.Balance, Is.EqualTo(300));
            Assert.That(InventoryRules.Quantity(stale.Inventory, EconomyContent.Notebook), Is.Zero);
            Assert.That(stale.Shop.Offers[0].PackagesRemaining, Is.EqualTo(3));
        }
        [Test]
        public void StoryDeliveryRequiresFullQuantityAndDoesNotEmitKnowledge()
        {
            var inv = InventoryRules.PrepareGrant(InventoryRules.Initialize("run", "p"), EconomyContent.Bookmark, 1);
            var failed = InventoryRules.PrepareDelivery(inv, Context(), EconomyContent.Bookmark, 2, true);
            Assert.That(failed.Status, Is.EqualTo(EconomyStatus.InsufficientItems)); Assert.That(InventoryRules.Quantity(failed.Inventory, EconomyContent.Bookmark), Is.EqualTo(1));
            var delivered = InventoryRules.PrepareDelivery(inv, Context(), EconomyContent.Bookmark, 1, true);
            Assert.That(InventoryRules.Quantity(delivered.Inventory, EconomyContent.Bookmark), Is.Zero); Assert.That(delivered.Effects, Is.Empty);
            Assert.Throws<ArgumentException>(() => InventoryRules.PrepareDelivery(inv, Context(), EconomyContent.Notebook, 1, true));
        }
        [Test]
        public void ActualUnityJsonRoundTripsEmptyAndPopulatedOwnedStates()
        {
            var inv = InventoryRules.Initialize("run", "p");
            inv = InventoryRules.NormalizeAfterLoad(JsonUtility.FromJson<InventoryState>(JsonUtility.ToJson(inv)));
            Assert.That(InventoryRules.Quantity(inv, EconomyContent.Notebook), Is.Zero);
            var shop = ShopRules.Initialize("run", "p", EconomyContent.Gym);
            shop = ShopRules.NormalizeAfterLoad(JsonUtility.FromJson<ShopState>(JsonUtility.ToJson(shop)));
            Assert.That(shop.Cycle, Is.Zero);
            shop = ShopRules.PrepareRefresh(shop, 1, Samples(shop)).Shop;
            var life = LifeRules.Initialize("run", "p", 100, 0);
            var result = ShopRules.PreparePurchase(life, inv, shop, ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", 1), Context());
            var loadedLife = LifeRules.NormalizeAfterLoad(JsonUtility.FromJson<LifeActorState>(JsonUtility.ToJson(result.Life)));
            var loadedInventory = InventoryRules.NormalizeAfterLoad(JsonUtility.FromJson<InventoryState>(JsonUtility.ToJson(result.Inventory)));
            var loadedShop = ShopRules.NormalizeAfterLoad(JsonUtility.FromJson<ShopState>(JsonUtility.ToJson(result.Shop)));
            Assert.That(loadedLife.Wallet.Balance, Is.EqualTo(40)); Assert.That(InventoryRules.Quantity(loadedInventory, EconomyContent.Notebook), Is.EqualTo(1));
            Assert.That(loadedShop.Offers[0].PackagesRemaining, Is.EqualTo(2));
            var point = InventoryRules.OpenPoint(Context(), "point"); point.Consumed = true;
            point = JsonUtility.FromJson<ItemOperationPoint>(JsonUtility.ToJson(point));
            Assert.That(InventoryRules.PrepareUse(loadedInventory, point, Context(), EconomyContent.Notebook, false, true, true).Status, Is.EqualTo(EconomyStatus.OpportunityClosed));
        }
        [Test]
        public void SuppliedRandomScopeAndAllQuoteRevisionsMustMatch()
        {
            var shop = ShopRules.Initialize("run", "p", EconomyContent.Gym);
            var wrongRun = Samples(shop); wrongRun.RunId = "other";
            var wrongActor = Samples(shop); wrongActor.ActorId = "a";
            var wrongShop = Samples(shop); wrongShop.ShopId = EconomyContent.Corner;
            Assert.Throws<ArgumentException>(() => ShopRules.PrepareRefresh(shop, 1, wrongRun));
            Assert.Throws<ArgumentException>(() => ShopRules.PrepareRefresh(shop, 1, wrongActor));
            Assert.Throws<ArgumentException>(() => ShopRules.PrepareRefresh(shop, 1, wrongShop));
            shop = ShopRules.PrepareRefresh(shop, 1, Samples(shop)).Shop;
            var life = LifeRules.Initialize("run", "p", 100, 0); var inv = InventoryRules.Initialize("run", "p");
            foreach (int field in new[] { 0, 1, 2 }) {
                var quote = ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", 1);
                if (field == 0) quote.LifeRevision++; else if (field == 1) quote.InventoryRevision++; else quote.ShopRevision++;
                Assert.That(ShopRules.PreparePurchase(life, inv, shop, quote, Context()).Status, Is.EqualTo(EconomyStatus.StaleQuote));
            }
            var wrongQuote = ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", 1); wrongQuote.ActorId = "a";
            Assert.Throws<ArgumentException>(() => ShopRules.PreparePurchase(life, inv, shop, wrongQuote, Context()));
            wrongQuote.ActorId = "p"; wrongQuote.RunId = "other";
            Assert.Throws<ArgumentException>(() => ShopRules.PreparePurchase(life, inv, shop, wrongQuote, Context()));
            wrongQuote.RunId = "run"; wrongQuote.ShopId = EconomyContent.Corner;
            Assert.Throws<ArgumentException>(() => ShopRules.PreparePurchase(life, inv, shop, wrongQuote, Context()));
        }
        [Test]
        public void RefreshIsIdempotentInCycleAndReplacesUnsoldAndSoldStock()
        {
            var shop = AdvanceShop(EconomyContent.Gym, 1); var life = LifeRules.Initialize("run", "p", 500, 0);
            var inv = InventoryRules.Initialize("run", "p");
            var bought = ShopRules.PreparePurchase(life, inv, shop, ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", 2), Context());
            var repeated = ShopRules.PrepareRefresh(bought.Shop, 1, Samples(bought.Shop, 123));
            Assert.That(repeated.Status, Is.EqualTo(EconomyStatus.NotDue));
            Assert.That(repeated.Shop.Offers[0].PackagesRemaining, Is.EqualTo(1)); Assert.That(repeated.Random.ConsumedValues, Is.Empty);
            var refreshed = ShopRules.PrepareRefresh(bought.Shop, 4, Samples(bought.Shop));
            Assert.That(refreshed.Shop.Offers[0].PackagesRemaining, Is.EqualTo(3)); Assert.That(refreshed.Shop.Cycle, Is.EqualTo(2));
            Assert.That(ShopRules.PrepareRefresh(refreshed.Shop, 4, Samples(refreshed.Shop)).Status, Is.EqualTo(EconomyStatus.NotDue));
            Assert.Throws<ArgumentException>(() => ShopRules.ObserveUnlocks(shop, 8, 0, 0));
        }
        [Test]
        public void InvalidConditionOrFixedTargetClosesPointWithoutItemLoss()
        {
            var inv = InventoryRules.PrepareGrant(InventoryRules.Initialize("run", "p"), EconomyContent.Notebook, 1);
            foreach (int failure in new[] { 0, 1, 2 }) {
                var point = InventoryRules.OpenPoint(Context(), "point");
                var result = InventoryRules.PrepareUse(inv, point, Context(), failure == 2 ? EconomyContent.Bookmark : EconomyContent.Notebook,
                    false, failure != 0, failure != 1);
                Assert.That(result.Status, Is.EqualTo(EconomyStatus.InvalidUse)); Assert.That(result.Point.Consumed, Is.True);
                Assert.That(InventoryRules.Quantity(result.Inventory, EconomyContent.Notebook), Is.EqualTo(1)); Assert.That(result.Effects, Is.Empty);
            }
        }
        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveQuantitiesAreRejected(int quantity)
        {
            var inv = InventoryRules.Initialize("run", "p"); var life = LifeRules.Initialize("run", "p", 100, 0); var shop = AdvanceShop(EconomyContent.Gym, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => InventoryRules.PrepareGrant(inv, EconomyContent.Notebook, quantity));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", quantity));
            Assert.Throws<ArgumentException>(() => InventoryRules.PrepareDelivery(inv, Context(), EconomyContent.Bookmark, quantity, true));
        }
        [Test]
        public void CorruptMissingStatesAndOverflowDoNotBecomeFreeGoods()
        {
            Assert.Throws<ArgumentException>(() => InventoryRules.NormalizeAfterLoad(JsonUtility.FromJson<InventoryState>("{}")));
            Assert.Throws<ArgumentException>(() => ShopRules.NormalizeAfterLoad(JsonUtility.FromJson<ShopState>("{}")));
            var inv = InventoryRules.PrepareGrant(InventoryRules.Initialize("run", "p"), EconomyContent.Notebook, long.MaxValue);
            var life = LifeRules.Initialize("run", "p", 100, 0); var shop = AdvanceShop(EconomyContent.Gym, 1);
            var quote = ShopRules.QuotePurchase(life, inv, shop, "gym_note_1", 1);
            Assert.Throws<OverflowException>(() => ShopRules.PreparePurchase(life, inv, shop, quote, Context()));
            Assert.That(life.Wallet.Balance, Is.EqualTo(100)); Assert.That(shop.Offers[0].PackagesRemaining, Is.EqualTo(3));
        }
    }
}
