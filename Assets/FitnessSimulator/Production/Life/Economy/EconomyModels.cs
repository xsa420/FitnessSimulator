using System;

namespace FitnessSimulator.Game.Life.Economy
{
    public enum EconomyStatus { Prepared, InsufficientFunds, InsufficientItems, OutOfStock, StaleQuote, AlreadyApplied, OpportunityClosed, InvalidUse, Skipped, NotDue, NeedRandomSamples, ConditionNotMet }
    public enum ItemKind { ActiveUse, StoryCondition }
    [Serializable] public sealed class EconomyContext
    {
        public string RunId, ActorId, EventInstanceId;
        public int Day;
        public long TransactionOrdinal;
    }
    [Serializable] public sealed class ItemStack { public string ItemId; public long Quantity; }
    [Serializable] public sealed class InventoryState
    {
        public string RunId, ActorId, ConfigVersion;
        public long Revision;
        public ItemStack[] Items = Array.Empty<ItemStack>();
    }
    [Serializable] public sealed class PaymentReceipt
    {
        public string SemanticKey, ConfigVersion, RunId, ActorId, Purpose;
        public long Amount;
    }
    public sealed class PaymentPreparation
    {
        public EconomyStatus Status;
        public LifeActorState Life;
        public PaymentReceipt Receipt;
    }
    [Serializable] public sealed class ItemOperationPoint
    {
        public string RunId, ActorId, EventInstanceId, PointId;
        public int Day;
        public bool Consumed;
        public string[] AllowedItemIds = Array.Empty<string>();
    }
    [Serializable] public sealed class BodyEffectIntent
    {
        public string SemanticKey, ActorId, Kind, EffectDefinitionId, ConfigVersion;
        public long KnowledgeMicroPerExercise;
        public string[] ExerciseIds = Array.Empty<string>();
    }
    public sealed class LossPreparation
    {
        public LifeActorState Life;
        public InventoryState Inventory;
        public long ActualLoss;
        public string SemanticKey;
    }
    [Serializable] public sealed class NotebookEffectDefinition
    {
        public string Id, ConfigVersion, Kind;
        public long KnowledgeMicroPerExercise;
        public string[] ExerciseIds;
    }
    public sealed class ItemPreparation
    {
        public EconomyStatus Status;
        public InventoryState Inventory;
        public ItemOperationPoint Point;
        public string SemanticKey;
        public BodyEffectIntent[] Effects = Array.Empty<BodyEffectIntent>();
    }
    [Serializable] public sealed class ProductDefinition
    {
        public string Id, ShopId, ItemId;
        public long UnitsPerPackage, Price;
        public int PackagesPerRefresh;
    }
    [Serializable] public sealed class ProductUnlock { public string ProductId; public int EligibleFromCycle; }
    [Serializable] public sealed class ShopOffer { public string ProductId; public int PackagesRemaining; }
    [Serializable] public sealed class ShopState
    {
        public string RunId, ActorId, ShopId, ConfigVersion;
        public long Revision;
        public int Cycle, GeneratedAtDay, NextRefreshDay;
        public ShopOffer[] Offers = Array.Empty<ShopOffer>();
        public ProductUnlock[] Unlocks = Array.Empty<ProductUnlock>();
    }
    [Serializable] public sealed class PurchaseQuote
    {
        public string RunId, ActorId, ShopId, ProductId, ConfigVersion;
        public long ShopRevision, LifeRevision, InventoryRevision;
        public int Cycle, PackageCount;
        public long TotalPrice, TotalUnits;
    }
    [Serializable] public sealed class PurchaseReceipt
    {
        public string SemanticKey, RunId, ActorId, ShopId, ProductId, ItemId, ConfigVersion;
        public int Cycle, PackageCount;
        public long Paid, Units;
    }
    public sealed class PurchasePreparation
    {
        public EconomyStatus Status;
        public LifeActorState Life;
        public InventoryState Inventory;
        public ShopState Shop;
        public PurchaseReceipt Receipt;
    }
    [Serializable] public sealed class RandomSamples
    {
        public string RunId, ActorId, ShopId, Purpose, AlgorithmVersion;
        public long StartDrawIndex;
        public uint[] Values = Array.Empty<uint>();
    }
    [Serializable] public sealed class RandomConsumption
    {
        public string AlgorithmVersion, Purpose;
        public long StartDrawIndex, EndDrawIndex;
        public uint[] ConsumedValues = Array.Empty<uint>();
    }
    public sealed class RefreshPreparation
    {
        public EconomyStatus Status;
        public ShopState Shop;
        public long ExpectedShopRevision;
        public string SemanticKey;
        public RandomConsumption Random;
    }
}
