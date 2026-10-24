using System;
using System.Globalization;
using System.Linq;

namespace FitnessSimulator.Game.Life.Economy
{
    public static class InventoryRules
    {
        public static string PaymentSemanticKey(EconomyContext context, string purpose)
        { EconomyGuard.Context(context); EconomyGuard.Id(purpose); return EconomyGuard.Key(context, "Payment." + purpose); }
        public static string DeliverySemanticKey(EconomyContext context)
        { EconomyGuard.Context(context); return EconomyGuard.Key(context, "Delivery"); }
        public static string ItemPointSemanticKey(EconomyContext context, string pointId)
        { EconomyGuard.Context(context); EconomyGuard.Id(pointId); return EconomyGuard.PointKey(context, pointId); }
        public static InventoryState Initialize(string runId, string actorId) => Initialize(runId, actorId, EconomyContent.EmptyInventoryId);
        public static InventoryState Initialize(string runId, string actorId, string initializationId)
        {
            EconomyGuard.Id(runId); EconomyGuard.Id(actorId);
            var initial = EconomyContent.Definition.InitialInventory;
            if (initial.Id != initializationId) throw new ArgumentException("Unknown inventory initialization reference");
            return new InventoryState { RunId = runId, ActorId = actorId, ConfigVersion = EconomyContent.Version,
                Items = initial.Items.Select(x => new ItemStack { ItemId = x.ItemId, Quantity = x.Quantity }).ToArray() };
        }
        public static InventoryState NormalizeAfterLoad(InventoryState state) { Validate(state); return Copy(state); }
        public static long Quantity(InventoryState state, string itemId)
        {
            Validate(state); EconomyContent.Kind(itemId);
            return state.Items.Single(x => x.ItemId == itemId).Quantity;
        }
        public static InventoryState PrepareGrant(InventoryState state, string itemId, long quantity)
        {
            Validate(state); EconomyContent.Kind(itemId);
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            var next = Copy(state); var item = next.Items.Single(x => x.ItemId == itemId);
            item.Quantity = checked(item.Quantity + quantity); next.Revision = checked(next.Revision + 1); return next;
        }
        public static PaymentPreparation PreparePayment(LifeActorState life, EconomyContext context, string purpose, long amount)
        {
            var copy = LifeRules.NormalizeAfterLoad(life); EconomyGuard.Context(context); EconomyGuard.Id(purpose);
            EconomyGuard.Scope(copy.RunId, copy.ActorId, context.RunId, context.ActorId);
            if (copy.Nutrition.Day != context.Day) throw new ArgumentException("Payment day mismatch.");
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (copy.Wallet.Balance < amount) return new PaymentPreparation { Status = EconomyStatus.InsufficientFunds, Life = copy };
            copy.Wallet.Balance -= amount; copy.Revision = checked(copy.Revision + 1);
            return new PaymentPreparation { Status = EconomyStatus.Prepared, Life = copy,
                Receipt = new PaymentReceipt { SemanticKey = PaymentSemanticKey(context, purpose),
                    RunId = copy.RunId, ActorId = copy.ActorId, ConfigVersion = EconomyContent.Version, Purpose = purpose, Amount = amount } };
        }
        public static ItemOperationPoint OpenPoint(EconomyContext context, string pointId) => OpenPoint(context, pointId, EconomyContent.Definition.DefaultOperationPointId);
        public static ItemOperationPoint OpenPoint(EconomyContext context, string pointId, string definitionId)
        {
            EconomyGuard.Context(context); EconomyGuard.Id(pointId);
            return new ItemOperationPoint { RunId = context.RunId, ActorId = context.ActorId, Day = context.Day,
                EventInstanceId = context.EventInstanceId, PointId = pointId, AllowedItemIds = EconomyContent.AllowedItems(definitionId) };
        }
        public static ItemPreparation PrepareUse(InventoryState inventory, ItemOperationPoint point, EconomyContext context,
            string itemId, bool skip, bool conditionSatisfied, bool fixedTargetValid)
        {
            Validate(inventory); ValidatePoint(point, context);
            EconomyGuard.Scope(inventory.RunId, inventory.ActorId, context.RunId, context.ActorId);
            var nextPoint = Copy(point); var nextInventory = Copy(inventory);
            string key = ItemPointSemanticKey(context, point.PointId);
            if (point.Consumed) return new ItemPreparation { Status = EconomyStatus.OpportunityClosed, Inventory = nextInventory, Point = nextPoint, SemanticKey = key };
            nextPoint.Consumed = true;
            if (skip) return new ItemPreparation { Status = EconomyStatus.Skipped, Inventory = nextInventory, Point = nextPoint, SemanticKey = key };
            // Unknown configuration IDs are technical errors; known but disallowed items are business failures.
            var kind = EconomyContent.Kind(itemId);
            if (kind != ItemKind.ActiveUse || !point.AllowedItemIds.Contains(itemId) || !conditionSatisfied || !fixedTargetValid || Quantity(inventory, itemId) < 1)
                return new ItemPreparation { Status = EconomyStatus.InvalidUse, Inventory = nextInventory, Point = nextPoint, SemanticKey = key };
            nextInventory.Items.Single(x => x.ItemId == itemId).Quantity--;
            nextInventory.Revision = checked(nextInventory.Revision + 1);
            var effect = EconomyContent.ItemEffect(itemId);
            return new ItemPreparation { Status = EconomyStatus.Prepared, Inventory = nextInventory, Point = nextPoint, SemanticKey = key,
                Effects = new[] { new BodyEffectIntent { SemanticKey = key, ActorId = context.ActorId, Kind = effect.Kind,
                    EffectDefinitionId = effect.Id, ConfigVersion = effect.ConfigVersion,
                    KnowledgeMicroPerExercise = effect.KnowledgeMicroPerExercise, ExerciseIds = effect.ExerciseIds } } };
        }
        public static LossPreparation PrepareMoneyLoss(LifeActorState life, EconomyContext context, long requestedLoss)
        {
            var next = LifeRules.NormalizeAfterLoad(life); EconomyGuard.Context(context);
            EconomyGuard.Scope(next.RunId, next.ActorId, context.RunId, context.ActorId);
            if (context.Day != next.Nutrition.Day || requestedLoss < 0) throw new ArgumentException("Invalid money loss.");
            long actual = Math.Min(requestedLoss, next.Wallet.Balance);
            next.Wallet.Balance -= actual; next.Revision = checked(next.Revision + 1);
            return new LossPreparation { Life = next, ActualLoss = actual, SemanticKey = EconomyGuard.Key(context, "MoneyLoss") };
        }
        public static LossPreparation PrepareItemLoss(InventoryState inventory, EconomyContext context, string itemId, long requestedLoss)
        {
            Validate(inventory); EconomyGuard.Context(context); EconomyGuard.Scope(inventory.RunId, inventory.ActorId, context.RunId, context.ActorId);
            EconomyContent.Kind(itemId);
            if (requestedLoss < 0) throw new ArgumentOutOfRangeException(nameof(requestedLoss));
            var next = Copy(inventory); var item = next.Items.Single(x => x.ItemId == itemId);
            long actual = Math.Min(requestedLoss, item.Quantity); item.Quantity -= actual; next.Revision = checked(next.Revision + 1);
            return new LossPreparation { Inventory = next, ActualLoss = actual, SemanticKey = EconomyGuard.Key(context, "ItemLoss." + itemId) };
        }
        public static ItemPreparation PrepareDelivery(InventoryState inventory, EconomyContext context, string itemId, long quantity, bool conditionSatisfied)
        {
            Validate(inventory); EconomyGuard.Context(context); EconomyGuard.Scope(inventory.RunId, inventory.ActorId, context.RunId, context.ActorId);
            if (EconomyContent.Kind(itemId) != ItemKind.StoryCondition || quantity <= 0) throw new ArgumentException("Invalid story delivery.");
            var next = Copy(inventory); string key = DeliverySemanticKey(context);
            if (!conditionSatisfied)
                return new ItemPreparation { Status = EconomyStatus.ConditionNotMet, Inventory = next, SemanticKey = key };
            if (Quantity(inventory, itemId) < quantity)
                return new ItemPreparation { Status = EconomyStatus.InsufficientItems, Inventory = next, SemanticKey = key };
            next.Items.Single(x => x.ItemId == itemId).Quantity -= quantity; next.Revision = checked(next.Revision + 1);
            return new ItemPreparation { Status = EconomyStatus.Prepared, Inventory = next, SemanticKey = key };
        }
        internal static void Validate(InventoryState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            EconomyGuard.Id(state.RunId); EconomyGuard.Id(state.ActorId);
            if (state.ConfigVersion != EconomyContent.Version || state.Revision < 0 || state.Items == null || state.Items.Length != EconomyContent.AllItemIds.Length)
                throw new ArgumentException("Invalid inventory version/shape.");
            if (state.Items.Any(x => x == null || x.Quantity < 0) || state.Items.Select(x => x.ItemId).Distinct().Count() != EconomyContent.AllItemIds.Length)
                throw new ArgumentException("Invalid inventory quantities/duplicates.");
            foreach (var item in state.Items) EconomyContent.Kind(item.ItemId);
        }
        internal static InventoryState Copy(InventoryState state) => new InventoryState { RunId = state.RunId, ActorId = state.ActorId,
            ConfigVersion = state.ConfigVersion, Revision = state.Revision, Items = state.Items.Select(x => new ItemStack { ItemId = x.ItemId, Quantity = x.Quantity }).ToArray() };
        private static ItemOperationPoint Copy(ItemOperationPoint point) => new ItemOperationPoint { RunId = point.RunId, ActorId = point.ActorId,
            EventInstanceId = point.EventInstanceId, PointId = point.PointId, Day = point.Day, Consumed = point.Consumed, AllowedItemIds = (string[])point.AllowedItemIds.Clone() };
        private static void ValidatePoint(ItemOperationPoint point, EconomyContext context)
        {
            EconomyGuard.Context(context);
            if (point == null || point.AllowedItemIds == null) throw new ArgumentException("Missing item point.");
            EconomyGuard.Id(point.PointId); EconomyGuard.Scope(point.RunId, point.ActorId, context.RunId, context.ActorId);
            if (point.EventInstanceId != context.EventInstanceId || point.Day != context.Day || point.AllowedItemIds.Distinct().Count() != point.AllowedItemIds.Length)
                throw new ArgumentException("Item point identity mismatch.");
            foreach (var item in point.AllowedItemIds) if (EconomyContent.Kind(item) != ItemKind.ActiveUse) throw new ArgumentException("Invalid allow list.");
        }
    }
    internal static class EconomyGuard
    {
        internal static void Id(string id) { if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Stable ID required."); }
        internal static void Scope(string run, string actor, string expectedRun, string expectedActor)
        { if (run != expectedRun || actor != expectedActor) throw new ArgumentException("Resource scope mismatch."); }
        internal static void Context(EconomyContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            Id(context.RunId); Id(context.ActorId); Id(context.EventInstanceId);
            if (context.Day < 1 || context.TransactionOrdinal < 0) throw new ArgumentException("Invalid context.");
        }
        internal static string Part(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        internal static string Key(EconomyContext c, string action) => "Economy|" + Part(c.RunId) + Part(c.ActorId) + Part(c.EventInstanceId) + c.Day.ToString(CultureInfo.InvariantCulture) + "|" + c.TransactionOrdinal.ToString(CultureInfo.InvariantCulture) + "|" + Part(action);
        internal static string PointKey(EconomyContext c, string point) => "ItemPoint|" + Part(c.RunId) + Part(c.ActorId) + Part(c.EventInstanceId) + Part(point);
    }
}
