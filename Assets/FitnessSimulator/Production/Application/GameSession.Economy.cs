using System;
using FitnessSimulator.Game.Planning;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    [Serializable] public sealed class ActivityState
    {
        public int Day = 1;
        public string[] PracticedExercises = Array.Empty<string>();
        public bool FullDayStudy;
        public int StudyAnchorCount;
        public bool HasItemPoint;
        public ItemOperationPoint ItemPoint;
        public string ShopId, VisitId;
        public long PurchaseOrdinal;
    }

    public sealed partial class GameSession
    {
        [Serializable] private sealed class ShopCommand { public EconomyContext Context; public PurchaseQuote Quote; }
        [Serializable] private sealed class ItemCommand { public EconomyContext Context; public string PointId, ItemId; public bool Skip; }
        private static string[] ShopIds => EconomyContent.AllShopIds.ToArray();

        private void InitializeEconomy(RootEnvelope root, SessionState state, string actorId = Actor)
        {
            if (root.FindFragment("inventory", actorId) != null) throw new InvalidOperationException("Economy already initialized");
            string inventoryRef = actorId == Actor ? TemplateCatalog.All.Single(t => t.Id == state.TemplateId).InventoryInitialRef : FitnessSimulator.Game.Actors.ActorContent.Get(actorId).InventoryRef;
            Write(root, "inventory", InventoryRules.Initialize(root.RunId, actorId, inventoryRef), actorId);
            Write(root, "activity", new ActivityState { Day = state.Day }, actorId);
            foreach (string shopId in ShopIds) {
                Write(root, "shop/" + shopId, ShopRules.Initialize(root.RunId, actorId, shopId), actorId);
                Write(root, "shop-random/" + shopId, DomainRandom.Create(root.RunId, actorId, "ShopRefresh/" + shopId,
                    state.InitialRandomSeed.ToString(CultureInfo.InvariantCulture)), actorId);
            }
            RefreshShops(root, state, actorId, null);
        }

        private void ObserveShops(RootEnvelope root, int day, string actorId)
        {
            var relationships = Read<RelationshipState>(root, "relationships", actorId);
            int veteran = relationships.Affections.Single(a => a.CharacterId == RelationshipRules.Veteran).Value;
            int beginner = relationships.Affections.Single(a => a.CharacterId == RelationshipRules.Beginner).Value;
            foreach (string shopId in ShopIds) {
                var current = Read<ShopState>(root, "shop/" + shopId, actorId);
                var next = ShopRules.ObserveUnlocks(current, day, veteran, beginner);
                if (next.Revision != current.Revision) Write(root, "shop/" + shopId, next, actorId);
            }
        }

        private void RefreshShops(RootEnvelope root, SessionState state, string actorId, PreparedOutcome outcome)
        {
            foreach (string shopId in ShopIds) {
                var shop = Read<ShopState>(root, "shop/" + shopId, actorId);
                if (state.Day < shop.NextRefreshDay) continue;
                var stream = Read<DomainRandomState>(root, "shop-random/" + shopId, actorId);
                var samples = new List<uint>(); var states = new List<DomainRandomState> { stream };
                RefreshPreparation refresh;
                do {
                    refresh = ShopRules.PrepareRefresh(shop, state.Day, new RandomSamples { RunId = root.RunId,
                        ActorId = actorId, ShopId = shopId, Purpose = "ShopRefresh", AlgorithmVersion = "splitmix64-v1",
                        StartDrawIndex = stream.DrawIndex, Values = samples.ToArray() });
                    if (refresh.Status == EconomyStatus.NeedRandomSamples) {
                        var draw = DomainRandom.NextUInt(states.Last()); samples.Add(draw.Value); states.Add(draw.State);
                    }
                } while (refresh.Status == EconomyStatus.NeedRandomSamples);
                if (refresh.Status != EconomyStatus.Prepared) throw new InvalidOperationException("Shop refresh did not prepare");
                Write(root, "shop/" + shopId, refresh.Shop, actorId);
                Write(root, "shop-random/" + shopId, states[refresh.Random.ConsumedValues.Length], actorId);
                outcome?.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "ShopRefresh",
                    Json = codec.Encode(new RefreshAudit { ActorId = actorId, ShopId = shopId, Day = state.Day, Random = refresh.Random }) });
            }
        }

        private static EconomyContext EconomyAt(RootEnvelope root, SessionState state, string actorId, string instance, long ordinal = 0)
            => new EconomyContext { RunId = root.RunId, ActorId = actorId, Day = state.Day, EventInstanceId = instance, TransactionOrdinal = ordinal };

        private static void SameEconomyContext(EconomyContext supplied, EconomyContext expected)
        {
            if (supplied == null || supplied.RunId != expected.RunId || supplied.ActorId != expected.ActorId || supplied.Day != expected.Day
                || supplied.EventInstanceId != expected.EventInstanceId || supplied.TransactionOrdinal != expected.TransactionOrdinal)
                throw new ArgumentException("Stale or foreign economy opportunity");
        }

        private string EconomySemanticKey(CommandEnvelope command)
        {
            var request = codec.Decode<RequestPayload>(command.Payload);
            EconomyContext context; string key;
            if (command.ActionKind == "shop-buy") {
                var purchase = codec.Decode<ShopCommand>(request.StringValue);
                context = purchase?.Context; key = ShopRules.PurchaseSemanticKey(context);
            } else if (command.ActionKind == "item-use") {
                var item = codec.Decode<ItemCommand>(request.StringValue);
                context = item?.Context; key = InventoryRules.ItemPointSemanticKey(context, item?.PointId);
            } else throw new ArgumentException("Not an economy command");
            if (context.RunId != command.RunId || context.ActorId != command.ActorId || command.ResourceScope != context.ActorId)
                throw new ArgumentException("Foreign economy command");
            return key;
        }

        private void StartActivity(RootEnvelope root, SessionState state, FlowState flow, FlowNode node, NodeCursor cursor, string actorId)
        {
            var activity = Read<ActivityState>(root, "activity", actorId);
            if (activity.Day != state.Day) throw new InvalidOperationException("Activity day mismatch");
            if (node.Handler == "shop-enter") {
                activity.ShopId = Call(node)?.ShopId ?? (flow.AnchorId == "afternoon" ? EconomyContent.Corner : EconomyContent.Gym);
                activity.VisitId = cursor.InstanceId; activity.PurchaseOrdinal = 0;
            } else if (node.Handler == "study-item" || node.Handler == "evening-item") {
                activity.ItemPoint = InventoryRules.OpenPoint(EconomyAt(root, state, actorId, cursor.InstanceId), cursor.InstanceId + "/notebook");
                activity.HasItemPoint = true;
                if (node.Handler == "study-item") {
                    activity.FullDayStudy = true;
                    activity.StudyAnchorCount = flow.Tree.Entries.Single(e => e.DefinitionId == flow.SelectedDefinitionId).ConsumeTargets.Count;
                    state.AnchorStandardSeconds = checked(TrainingContent.Current.Knowledge.FullDayStudySecondsPerAnchor * activity.StudyAnchorCount);
                }
            }
            Write(root, "activity", activity, actorId);
        }

        private bool PrepareActivity(RootEnvelope root, SessionState state, FlowState flow, FlowNode node, NodeCursor cursor,
            FrontendAction action, string actorId, ref BodyState body, ref LifeActorState life, PreparedOutcome outcome, out bool complete)
        {
            complete = false;
            if (action.ActionId != "shop-buy" && action.ActionId != "shop-leave" && action.ActionId != "item-use"
                && !(action.ActionId == "continue" && node != null && new[] { "shop-enter", "study-all", "study-evening", "busy", "injury-notice" }.Contains(node.Handler))) return false;
            RequireStarted(state, cursor);
            var activity = Read<ActivityState>(root, "activity", actorId);
            if (activity.Day != state.Day) throw new InvalidOperationException("Activity day mismatch");
            switch (action.ActionId) {
                case "shop-buy": {
                    RequireHandler(state, cursor, node, "shop");
                    var purchase = codec.Decode<ShopCommand>(action.StringValue);
                    var context = EconomyAt(root, state, actorId, activity.VisitId, activity.PurchaseOrdinal);
                    SameEconomyContext(purchase?.Context, context);
                    if (purchase.Quote.ShopId != activity.ShopId) throw new ArgumentException("Wrong shop visit");
                    var prepared = ShopRules.PreparePurchase(life, Read<InventoryState>(root, "inventory", actorId),
                        Read<ShopState>(root, "shop/" + activity.ShopId, actorId), purchase.Quote, context);
                    if (prepared.Status != EconomyStatus.Prepared) throw new InvalidOperationException("购买未完成：" + prepared.Status);
                    life = prepared.Life;
                    Write(root, "inventory", prepared.Inventory, actorId); Write(root, "shop/" + activity.ShopId, prepared.Shop, actorId);
                    activity.PurchaseOrdinal++;
                    outcome.SemanticKey = prepared.Receipt.SemanticKey;
                    outcome.CheckKind = CheckKind.NormalCompletion; outcome.EventDisposition = EventDisposition.CompleteNode;
                    outcome.Result = "购买已付款并保存。";
                    outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "Purchase", Json = codec.Encode(prepared.Receipt) });
                    break;
                }
                case "shop-leave": RequireHandler(state, cursor, node, "shop"); complete = true; break;
                case "item-use": {
                    if ((node.Handler != "study-item" && node.Handler != "evening-item") || !activity.HasItemPoint) throw new InvalidOperationException("No item opportunity");
                    var item = codec.Decode<ItemCommand>(action.StringValue);
                    var context = EconomyAt(root, state, actorId, cursor.InstanceId);
                    SameEconomyContext(item?.Context, context);
                    if (item.PointId != activity.ItemPoint.PointId) throw new ArgumentException("Wrong item point");
                    var prepared = InventoryRules.PrepareUse(Read<InventoryState>(root, "inventory", actorId), activity.ItemPoint,
                        context, item.ItemId, item.Skip, true, true);
                    if (prepared.Status == EconomyStatus.OpportunityClosed) throw new InvalidOperationException("Item opportunity closed");
                    foreach (var effect in prepared.Effects) {
                        if (effect.ActorId != actorId || effect.Kind != "AllExerciseKnowledge") throw new InvalidOperationException("Unknown body effect intent");
                        body = BodyCalculator.AddKnowledge(body, effect.ExerciseIds, effect.KnowledgeMicroPerExercise);
                    }
                    Write(root, "inventory", prepared.Inventory, actorId); activity.ItemPoint = prepared.Point;
                    outcome.SemanticKey = prepared.SemanticKey; outcome.Result = "道具操作已保存：" + prepared.Status;
                    outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "ItemUse", Json = codec.Encode(item) });
                    complete = true; break;
                }
                default:
                    if (node.Handler == "shop-enter") AddTime(state, 300);
                    else if (node.Handler == "study-all") {
                        var studied = TrainingKnowledgeRules.FullDay(body, DayPlan(root, state, actorId).Slot.IsRest, activity.StudyAnchorCount);
                        body = studied.NextBody; AddTime(state, studied.GameSeconds);
                    } else if (node.Handler == "study-evening") {
                        if (activity.FullDayStudy || activity.PracticedExercises.Length == 0) throw new InvalidOperationException("No practiced action for evening study");
                        var studied = TrainingKnowledgeRules.Evening(body, activity.PracticedExercises, activity.FullDayStudy); body = studied.NextBody; AddTime(state, studied.GameSeconds);
                    }
                    complete = true; break;
            }
            Write(root, "activity", activity, actorId); return true;
        }

        private bool ProjectActivity(RootEnvelope root, SessionState state, FlowState flow, FlowNode node, NodeCursor cursor,
            FrontendView view, List<FrontendOption> actions)
        {
            var activity = Read<ActivityState>(root, "activity");
            switch (node.Handler) {
                case "shop-enter": view.Body = "前往商店，本次访问耗时 5 分钟。"; actions.Add(Option("continue", "进入商店")); return true;
                case "shop": {
                    var shop = Read<ShopState>(root, "shop/" + activity.ShopId);
                    var inventory = Read<InventoryState>(root, "inventory"); var life = Read<LifeActorState>(root, "life");
                    view.Body = EconomyContent.Shop(activity.ShopId).Name + "\n下一轮刷新：第 " + shop.NextRefreshDay + " 日。\n训练笔记 " + InventoryRules.Quantity(inventory, EconomyContent.Notebook) + " 本；纪念书签 " + InventoryRules.Quantity(inventory, EconomyContent.Bookmark) + " 件。";
                    foreach (var offer in shop.Offers) {
                        var product = EconomyContent.Product(shop.ShopId, offer.ProductId);
                        var quote = ShopRules.QuotePurchase(life, inventory, shop, product.Id, 1);
                        var option = Option("shop-buy", EconomyContent.Item(product.ItemId).Name + "×" + product.UnitsPerPackage + " · " + product.Price + " 元 · 余 " + offer.PackagesRemaining + " 包",
                            codec.Encode(new ShopCommand { Context = EconomyAt(root, state, Actor, activity.VisitId, activity.PurchaseOrdinal), Quote = quote }));
                        option.Enabled = offer.PackagesRemaining > 0 && life.Wallet.Balance >= product.Price;
                        option.DisabledReason = offer.PackagesRemaining <= 0 ? "本轮已售罄" : "余额不足"; actions.Add(option);
                    }
                    actions.Add(Option("shop-leave", "离开商店")); return true;
                }
                case "study-item": case "evening-item": {
                    view.Body = "可使用一本训练笔记，所有动作知识各增加 2；也可以不使用。";
                    var item = new ItemCommand { Context = EconomyAt(root, state, Actor, cursor.InstanceId), PointId = activity.ItemPoint.PointId, ItemId = EconomyContent.Notebook };
                    var use = Option("item-use", "使用训练笔记", codec.Encode(item));
                    use.Enabled = InventoryRules.Quantity(Read<InventoryState>(root, "inventory"), EconomyContent.Notebook) > 0;
                    use.DisabledReason = "没有训练笔记"; actions.Add(use);
                    item.Skip = true; actions.Add(Option("item-use", "不使用", codec.Encode(item))); return true;
                }
                case "study-all": view.Body = "全天学习：所有动作知识各增加 12。今日普通活动时段已占用，三餐保留。"; actions.Add(Option("continue", "完成学习")); return true;
                case "study-evening": view.Body = "复习今天实际练过的动作，各增加知识 3，耗时 15 分钟。"; actions.Add(Option("continue", "完成晚间学习")); return true;
                case "busy": view.Body = "时间紧，今天简短交流。"; actions.Add(Option("continue", "结束交流")); return true;
                case "injury-notice": view.Body = "当前存在受伤部位，相关动作受到限制。此提示不会改变伤病。"; actions.Add(Option("continue", "继续")); return true;
                default: return false;
            }
        }

        [Serializable] private sealed class RefreshAudit { public string ActorId, ShopId; public int Day; public RandomConsumption Random; }
    }
}
