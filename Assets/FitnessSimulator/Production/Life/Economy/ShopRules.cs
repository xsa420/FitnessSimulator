using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Life.Economy
{
    public static class ShopRules
    {
        public static string PurchaseSemanticKey(EconomyContext context)
        { EconomyGuard.Context(context); return EconomyGuard.Key(context, "Purchase"); }
        public static ShopState Initialize(string runId, string actorId, string shopId)
        {
            EconomyGuard.Id(runId); EconomyGuard.Id(actorId); EconomyContent.RefreshPeriod(shopId);
            return new ShopState { RunId = runId, ActorId = actorId, ShopId = shopId, ConfigVersion = EconomyContent.Version,
                NextRefreshDay = 1, Unlocks = EconomyContent.Shop(shopId).InitiallyUnlockedProductIds.Select(id => new ProductUnlock { ProductId = id, EligibleFromCycle = 1 }).ToArray() };
        }
        public static ShopState NormalizeAfterLoad(ShopState state) { Validate(state); return Copy(state); }
        // Affection inputs are authoritative caller snapshots, not access to another actor's private relationship state.
        public static ShopState ObserveUnlocks(ShopState current, int day, int veteranAffection, int beginnerAffection)
        {
            Validate(current);
            if (day < 1 || day < current.GeneratedAtDay || day > current.NextRefreshDay || veteranAffection < 0 || veteranAffection > 100 || beginnerAffection < 0 || beginnerAffection > 100)
                throw new ArgumentException("Invalid unlock inputs.");
            var next = Copy(current); var unlocks = new List<ProductUnlock>(next.Unlocks);
            foreach (var product in EconomyContent.Products(current.ShopId))
            {
                bool eligible = EconomyContent.ProductUnlocked(current.ShopId, product.Id, day, veteranAffection, beginnerAffection);
                if (eligible && !unlocks.Any(x => x.ProductId == product.Id))
                    unlocks.Add(new ProductUnlock { ProductId = product.Id, EligibleFromCycle = checked(current.Cycle + 1) });
            }
            if (unlocks.Count != next.Unlocks.Length) { next.Unlocks = unlocks.ToArray(); next.Revision = checked(next.Revision + 1); }
            return next;
        }
        public static RefreshPreparation PrepareRefresh(ShopState current, int day, RandomSamples samples)
        {
            Validate(current); ValidateSamples(current, samples);
            if (day < 1 || day < current.GeneratedAtDay) throw new ArgumentException("Invalid refresh day.");
            var unchanged = Copy(current);
            if (day < current.NextRefreshDay) return RefreshResult(EconomyStatus.NotDue, unchanged, samples, 0, null);
            if (day != current.NextRefreshDay) throw new ArgumentException("Missed refresh boundary; replay logical day steps, do not skip cycles.");
            int cycle = checked(current.Cycle + 1), used = 0;
            var pool = EconomyContent.Products(current.ShopId).Where(p => current.Unlocks.Any(u => u.ProductId == p.Id && u.EligibleFromCycle <= cycle)).ToList();
            var offers = new List<ShopOffer>();
            while (pool.Count > 0 && offers.Count < EconomyContent.Shop(current.ShopId).OfferSlots)
            {
                int index = 0;
                if (pool.Count > 1)
                {
                    // Rejection sampling makes every candidate equally likely; no local PRNG is used.
                    ulong range = 1UL << 32, limit = range - range % (uint)pool.Count;
                    uint value;
                    do {
                        if (used >= samples.Values.Length) return RefreshResult(EconomyStatus.NeedRandomSamples, unchanged, samples, 0, null);
                        value = samples.Values[used++];
                    } while ((ulong)value >= limit);
                    index = (int)(value % (uint)pool.Count);
                }
                var selected = pool[index]; pool.RemoveAt(index);
                offers.Add(new ShopOffer { ProductId = selected.Id, PackagesRemaining = selected.PackagesPerRefresh });
            }
            var next = Copy(current); next.Cycle = cycle; next.GeneratedAtDay = day;
            next.NextRefreshDay = checked(day + EconomyContent.RefreshPeriod(current.ShopId));
            next.Offers = offers.ToArray(); next.Revision = checked(next.Revision + 1);
            string key = "ShopRefresh|" + EconomyGuard.Part(current.RunId) + EconomyGuard.Part(current.ActorId) + EconomyGuard.Part(current.ShopId) + cycle;
            var result = RefreshResult(EconomyStatus.Prepared, next, samples, used, key);
            result.ExpectedShopRevision = current.Revision;
            return result;
        }
        public static PurchaseQuote QuotePurchase(LifeActorState life, InventoryState inventory, ShopState shop, string productId, int packageCount)
        {
            ValidateTogether(life, inventory, shop);
            if (packageCount <= 0) throw new ArgumentOutOfRangeException(nameof(packageCount));
            var product = EconomyContent.Product(shop.ShopId, productId);
            if (!shop.Offers.Any(x => x.ProductId == productId)) throw new ArgumentException("Product is not in this cycle's offers.");
            return new PurchaseQuote { RunId = shop.RunId, ActorId = shop.ActorId, ShopId = shop.ShopId, ProductId = productId,
                ConfigVersion = EconomyContent.Version, ShopRevision = shop.Revision, LifeRevision = life.Revision, InventoryRevision = inventory.Revision,
                Cycle = shop.Cycle, PackageCount = packageCount, TotalPrice = checked(product.Price * packageCount), TotalUnits = checked(product.UnitsPerPackage * packageCount) };
        }
        public static PurchasePreparation PreparePurchase(LifeActorState life, InventoryState inventory, ShopState shop, PurchaseQuote quote, EconomyContext context)
        {
            ValidateTogether(life, inventory, shop); EconomyGuard.Context(context);
            EconomyGuard.Scope(shop.RunId, shop.ActorId, context.RunId, context.ActorId);
            if (context.Day != life.Nutrition.Day || context.Day < shop.GeneratedAtDay || context.Day >= shop.NextRefreshDay)
                throw new ArgumentException("Purchase day/refresh boundary mismatch.");
            if (quote == null) throw new ArgumentNullException(nameof(quote));
            EconomyGuard.Scope(shop.RunId, shop.ActorId, quote.RunId, quote.ActorId);
            if (quote.ShopId != shop.ShopId || quote.ConfigVersion != EconomyContent.Version) throw new ArgumentException("Quote shop/version mismatch.");
            var next = new PurchasePreparation { Life = LifeRules.NormalizeAfterLoad(life), Inventory = InventoryRules.Copy(inventory), Shop = Copy(shop) };
            if (quote.Cycle != shop.Cycle || quote.ShopRevision != shop.Revision || quote.LifeRevision != life.Revision || quote.InventoryRevision != inventory.Revision)
            { next.Status = EconomyStatus.StaleQuote; return next; }
            var expected = QuotePurchase(life, inventory, shop, quote.ProductId, quote.PackageCount);
            if (quote.TotalPrice != expected.TotalPrice || quote.TotalUnits != expected.TotalUnits) throw new ArgumentException("Quote was modified.");
            var offer = next.Shop.Offers.Single(x => x.ProductId == quote.ProductId);
            if (offer.PackagesRemaining < quote.PackageCount) { next.Status = EconomyStatus.OutOfStock; return next; }
            var payment = InventoryRules.PreparePayment(life, context, "ShopPurchase", quote.TotalPrice);
            if (payment.Status != EconomyStatus.Prepared) { next.Status = payment.Status; return next; }
            var product = EconomyContent.Product(shop.ShopId, quote.ProductId);
            next.Inventory = InventoryRules.PrepareGrant(inventory, product.ItemId, quote.TotalUnits);
            next.Life = payment.Life; offer.PackagesRemaining -= quote.PackageCount; next.Shop.Revision = checked(shop.Revision + 1);
            next.Status = EconomyStatus.Prepared;
            next.Receipt = new PurchaseReceipt { SemanticKey = PurchaseSemanticKey(context), RunId = shop.RunId, ActorId = shop.ActorId,
                ShopId = shop.ShopId, ProductId = quote.ProductId, ItemId = product.ItemId, ConfigVersion = EconomyContent.Version,
                Cycle = shop.Cycle, PackageCount = quote.PackageCount, Paid = quote.TotalPrice, Units = quote.TotalUnits };
            return next;
        }
        private static RefreshPreparation RefreshResult(EconomyStatus status, ShopState shop, RandomSamples samples, int used, string key)
            => new RefreshPreparation { Status = status, Shop = shop, ExpectedShopRevision = shop.Revision, SemanticKey = key,
                Random = new RandomConsumption { AlgorithmVersion = samples.AlgorithmVersion, Purpose = samples.Purpose, StartDrawIndex = samples.StartDrawIndex,
                    EndDrawIndex = checked(samples.StartDrawIndex + used), ConsumedValues = samples.Values.Take(used).ToArray() } };
        private static void ValidateSamples(ShopState shop, RandomSamples samples)
        {
            if (samples == null || samples.Values == null || samples.StartDrawIndex < 0 || samples.Purpose != "ShopRefresh" || samples.ShopId != shop.ShopId)
                throw new ArgumentException("Invalid supplied random stream.");
            EconomyGuard.Id(samples.AlgorithmVersion); EconomyGuard.Scope(shop.RunId, shop.ActorId, samples.RunId, samples.ActorId);
        }
        private static void ValidateTogether(LifeActorState life, InventoryState inventory, ShopState shop)
        {
            LifeRules.NormalizeAfterLoad(life); InventoryRules.Validate(inventory); Validate(shop);
            EconomyGuard.Scope(shop.RunId, shop.ActorId, life.RunId, life.ActorId);
            EconomyGuard.Scope(shop.RunId, shop.ActorId, inventory.RunId, inventory.ActorId);
        }
        private static void Validate(ShopState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            EconomyGuard.Id(state.RunId); EconomyGuard.Id(state.ActorId);
            int period = EconomyContent.RefreshPeriod(state.ShopId);
            if (state.ConfigVersion != EconomyContent.Version || state.Revision < 0 || state.Cycle < 0 || state.Offers == null || state.Unlocks == null || state.Offers.Length > EconomyContent.Shop(state.ShopId).OfferSlots)
                throw new ArgumentException("Invalid shop state.");
            if (state.Cycle == 0 ? state.GeneratedAtDay != 0 || state.NextRefreshDay != 1 || state.Offers.Length != 0 :
                state.GeneratedAtDay != checked(1 + (state.Cycle - 1) * period) || state.NextRefreshDay != checked(state.GeneratedAtDay + period))
                throw new ArgumentException("Invalid shop refresh cursor.");
            if (state.Cycle > 0 && state.Offers.Length == 0) throw new ArgumentException("Missing generated offers.");
            var unlocked = new HashSet<string>();
            foreach (var entry in state.Unlocks)
            {
                if (entry == null || entry.EligibleFromCycle < 1 || entry.EligibleFromCycle > checked(state.Cycle + 1) || !unlocked.Add(entry.ProductId))
                    throw new ArgumentException("Invalid unlock ledger.");
                EconomyContent.Product(state.ShopId, entry.ProductId);
            }
            if (EconomyContent.Shop(state.ShopId).InitiallyUnlockedProductIds.Any(id => !unlocked.Contains(id))) throw new ArgumentException("Missing initially unlocked product.");
            var products = new HashSet<string>();
            foreach (var offer in state.Offers)
            {
                if (offer == null || !products.Add(offer.ProductId)) throw new ArgumentException("Invalid offer list.");
                var product = EconomyContent.Product(state.ShopId, offer.ProductId);
                if (offer.PackagesRemaining < 0 || offer.PackagesRemaining > product.PackagesPerRefresh ||
                    !state.Unlocks.Any(x => x.ProductId == offer.ProductId && x.EligibleFromCycle <= state.Cycle)) throw new ArgumentException("Invalid offer inventory/unlock.");
            }
        }
        private static ShopState Copy(ShopState state) => new ShopState { RunId = state.RunId, ActorId = state.ActorId, ShopId = state.ShopId,
            ConfigVersion = state.ConfigVersion, Revision = state.Revision, Cycle = state.Cycle, GeneratedAtDay = state.GeneratedAtDay, NextRefreshDay = state.NextRefreshDay,
            Offers = state.Offers.Select(x => new ShopOffer { ProductId = x.ProductId, PackagesRemaining = x.PackagesRemaining }).ToArray(),
            Unlocks = state.Unlocks.Select(x => new ProductUnlock { ProductId = x.ProductId, EligibleFromCycle = x.EligibleFromCycle }).ToArray() };
    }
}
