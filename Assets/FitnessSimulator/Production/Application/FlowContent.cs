using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Training;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;

namespace FitnessSimulator.Game.Application
{
    [Serializable] public sealed class FlowContentDocument
    {
        public string Version;
        public AnchorDefinition[] Anchors;
        public ConfiguredEvent[] Events;
        public NamedCondition[] Conditions;
        public TrainingFlowTemplate Training;
        public string[] DayEndSteps, EnterDaySteps;
        public int ChoiceGroupWeight;
    }
    [Serializable] public sealed class AnchorDefinition
    {
        public string Id, Name, Kind, MealSlot, UnlockCondition;
        public int Order, StandardSeconds;
        public bool TrainingAllowed, EditableTraining, PlanEditing;
        public EventBinding[] Entries, Defaults;
    }
    [Serializable] public sealed class EventBinding
    {
        public string EventId, Condition;
        public bool Enabled, IsChoice;
        public int Weight, MaximumStarts, CooldownDays;
        public string ExclusiveGroup;
        public AnchorConsumption[] Consume;
    }
    [Serializable] public sealed class AnchorConsumption
    {
        public string AnchorId;
        public int DayOffset;
        [ContentOptional] public string Weekday;
    }
    [Serializable] public sealed class ConfiguredEvent
    {
        public string Id, Title, Root;
        public ConfiguredNode[] Nodes;
    }
    [Serializable] public sealed class ConfiguredNode
    {
        public string Id, Handler, Title, Next;
        public FlowEdge[] Branches;
        public EventCallPayload Call;
    }
    [Serializable] public sealed class EventCallPayload
    {
        public string StoryId, ShopId, IncomeId, TimeMode;
        public int Seconds;
        public ConfiguredEffect[] Effects;
        [ContentOptional] public ConfiguredSocial Social;
        [ContentOptional] public ConfiguredChoiceAccess[] ChoiceAccess = Array.Empty<ConfiguredChoiceAccess>();
    }
    [Serializable] public sealed class ConfiguredSocial { public string Mode; public long BaseProbabilityMillionths; public ConfiguredEffect[] SuccessEffects, FailureEffects; }
    [Serializable] public sealed class ConfiguredChoiceAccess { public string ChoiceId, Condition, CostNodeId, ProbabilitySource; }
    [Serializable] public sealed class ConfiguredEffect
    {
        public string Kind, Target, Id;
        public long Amount;
        [ContentOptional] public bool PositiveScalable;
    }
    [Serializable] public sealed class NamedCondition
    {
        public string Id;
        public ConditionDefinition Rule;
    }
    [Serializable] public sealed class TrainingFlowTemplate
    {
        public ConfiguredNode[] GroupNodes;
        public string EntryNode, SummaryTitle;
    }
    public static class FlowContent
    {
        public static FlowContentDocument Current => ContentRuntime.Current.Get<FlowContentDocument>("flow");
        public static AnchorDefinition Anchor(string id) => Current.Anchors.Single(a => a.Id == id);
        public static bool AllowsFact(string key) => new[] {
            "day", "anchor", "actor", "restDay", "trainingHere", "firstOrdinary", "hasInjury", "overtime",
            "fullDayStudy", "practiced", "effectiveTraining", "maxKnowledge", "balance", "affluence",
            "intake", "bodyStress", "mentalStress", "bodyFat", "baseCharm", "result",
            "competition.non_champion", "competition.unfulfilled", "competition.bodybuilding_champion",
            "competition.lean_champion", "relationship.romance_success"
        }.Contains(key) || EconomyContent.AllItemIds.Any(id => key == "inventory:" + id)
            || RelationshipRules.CharacterIds.Any(id => key == "affection:" + id)
            || RegionIds.All.Any(id => key == "strength:" + id)
            || ExerciseCatalog.All.Any(e => key == "knowledge:" + e.Id)
            || Current.Events.Any(e => key == "eventStarts:" + e.Id) || key == "eventStarts:training-choice";
        public static bool Evaluate(string id, Func<string, string> read)
        {
            if (string.IsNullOrEmpty(id)) return true;
            var condition = Current.Conditions.SingleOrDefault(c => c.Id == id)
                ?? throw new InvalidOperationException("flow.json/Conditions: unknown condition " + id);
            return ConditionEvaluator.Evaluate(condition.Rule, read, AllowsFact);
        }
        public static void ValidateContent()
        {
            var d = Current;
            Unique(d.Anchors.Select(a => a.Id), "Anchors"); Unique(d.Events.Select(e => e.Id), "Events");
            Unique(d.Conditions.Select(c => c.Id), "Conditions");
            if (d.Anchors.Length == 0 || d.Anchors.Select(a => a.Order).Distinct().Count() != d.Anchors.Length || d.ChoiceGroupWeight <= 0)
                throw new InvalidOperationException("flow.json/Anchors: empty/duplicate order or invalid ChoiceGroupWeight");
            foreach (var c in d.Conditions) ConditionEvaluator.Validate(c.Rule, AllowsFact, "flow.json/Conditions/" + c.Id);
            Action<string> condition = id => { if (!string.IsNullOrEmpty(id) && !d.Conditions.Any(c => c.Id == id)) throw new InvalidOperationException("flow.json/condition reference: " + id); };
            foreach (var a in d.Anchors) {
                Unique(a.Entries.Select(b => b.EventId), "Anchors/" + a.Id + "/Entries");
                if (string.IsNullOrWhiteSpace(a.Name) || a.StandardSeconds < 0 || !new[] { "ordinary", "meal" }.Contains(a.Kind)) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + ": invalid kind/name/seconds");
                if (a.Kind == "meal" && !new[] { "Breakfast", "Lunch", "Dinner" }.Contains(a.MealSlot)) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + "/MealSlot");
                if (a.Defaults.Length == 0 || !string.IsNullOrEmpty(a.Defaults.Last().Condition)) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + ": unconditional final default required");
                condition(a.UnlockCondition);
                foreach (var b in a.Entries.Concat(a.Defaults)) {
                    condition(b.Condition);
                    if (b.EventId != "training-choice" && !d.Events.Any(e => e.Id == b.EventId)) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + "/EventId: " + b.EventId);
                    if (b.Weight <= 0 || b.MaximumStarts == 0 || b.MaximumStarts < -1 || b.CooldownDays < 0) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + ": invalid entry limits");
                    foreach (var t in b.Consume) if (t.DayOffset < 0 || (!string.IsNullOrEmpty(t.Weekday) && (t.DayOffset != 0 || !Enum.TryParse<DayOfWeek>(t.Weekday, out _))) || (t.AnchorId != "$ordinary" && !d.Anchors.Any(x => x.Id == t.AnchorId))) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + "/Consume: invalid target");
                }
                foreach (var b in a.Defaults) if (!b.Enabled || b.IsChoice || b.MaximumStarts != -1 || b.CooldownDays != 0) throw new InvalidOperationException("flow.json/Anchors/" + a.Id + "/Defaults: must be enabled/unlimited/nonchoice/no cooldown");
            }
            foreach (var e in d.Events) {
                Unique(e.Nodes.Select(n => n.Id), "Events/" + e.Id + "/Nodes");
                if (!e.Nodes.Any(n => n.Id == e.Root)) throw new InvalidOperationException("flow.json/Events/" + e.Id + "/Root");
                foreach (var n in e.Nodes) ValidateNode(n, "Events/" + e.Id, condition);
                var tree = new FlowDefinition { Id = e.Id, Version = d.Version, DefaultEntry = new EntryDefinition { DefinitionId = e.Id, RootNodeId = e.Root },
                    Nodes = e.Nodes.Select(n => new FlowNode { Id = n.Id, DefinitionId = e.Id, Handler = n.Handler, NextNodeId = n.Next, Branches = n.Branches.ToList() }).ToList() };
                FlowEngine.Validate(tree);
            }
            foreach (var n in d.Training.GroupNodes) ValidateNode(n, "Training/GroupNodes", condition);
            Unique(d.Training.GroupNodes.Select(n => n.Id), "Training/GroupNodes");
            if (!d.Training.GroupNodes.Any(n => n.Id == d.Training.EntryNode)) throw new InvalidOperationException("flow.json/Training/EntryNode");
            // Step identities and dependencies are product invariants, not arbitrary callable code.
            var end = new[] { "close", "income-summary", "nutrition", "recover-local", "recover-stress", "deficit-stress", "clear-temporary", "expiry", "unlock", "summary", "await" };
            if (d.DayEndSteps.Length != end.Length || !d.DayEndSteps.OrderBy(x => x).SequenceEqual(end.OrderBy(x => x)) || d.DayEndSteps[0] != "close" || d.DayEndSteps.Last() != "await"
                || !Before(d.DayEndSteps, "nutrition", "recover-local") || !Before(d.DayEndSteps, "recover-local", "recover-stress")
                || !Before(d.DayEndSteps, "recover-stress", "deficit-stress") || !Before(d.DayEndSteps, "deficit-stress", "clear-temporary")
                || end.Where(x => x != "summary" && x != "await").Any(x => !Before(d.DayEndSteps, x, "summary"))
                || !d.EnterDaySteps.SequenceEqual(new[] { "new-day", "plans-quota", "shops", "competition", "publish" })) throw new InvalidOperationException("flow.json/day steps violate required dependency order");
        }
        private static bool Before(string[] steps, string a, string b) => Array.IndexOf(steps, a) < Array.IndexOf(steps, b);
        private static void ValidateNode(ConfiguredNode n, string path, Action<string> condition)
        {
            if (!new[] { "calm", "walk", "work", "meal", "shop-enter", "shop", "study-item", "study-all", "study-evening", "evening-item", "busy", "injury-notice", "group-story", "play", "settle", "followers", "narrative", "effects", "social", "end-action", "next-anchor" }.Contains(n.Handler)) throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Handler: " + n.Handler);
            foreach (var b in n.Branches) if (!b.ConditionKey.StartsWith("result:", StringComparison.Ordinal) && !new[] { "workout-ended", "end-workout", "end-action", "action-changed" }.Contains(b.ConditionKey)) condition(b.ConditionKey);
            if (!new[] { "none", "fixed", "standard" }.Contains(n.Call.TimeMode) || n.Call.Seconds < 0) throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Call: invalid time");
            if (n.Call.Effects.Length > 64 || n.Handler != "effects" && n.Call.Effects.Length != 0) throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Call.Effects: effects only execute in effects nodes, maximum 64");
            Unique(n.Call.Effects.Select(e => e.Id), path + "/" + n.Id + "/Effects");
            if (!new[] { "calm", "walk", "work", "meal", "effects", "narrative", "social" }.Contains(n.Handler) && n.Call.TimeMode != "none") throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Call.TimeMode: domain owns timing for this handler");
            if ((n.Handler == "social") != (n.Call.Social != null)) throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Call.Social: handler/definition mismatch");
            var social = n.Call.Social;
            if (social != null && (!new[] { "check", "noCheck" }.Contains(social.Mode) || social.BaseProbabilityMillionths < 0 || social.BaseProbabilityMillionths > 1000000 || social.SuccessEffects == null || social.FailureEffects == null || social.SuccessEffects.Length + social.FailureEffects.Length > 64 || social.Mode == "noCheck" && (social.BaseProbabilityMillionths != 0 || social.FailureEffects.Length != 0))) throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Call.Social: invalid mode/probability/effects");
            if (n.Call.ChoiceAccess == null || n.Handler != "narrative" && n.Call.ChoiceAccess.Length != 0) throw new InvalidOperationException("flow.json/" + path + "/ChoiceAccess: only narrative accepts choice inputs");
            Unique(n.Call.ChoiceAccess.Select(a => a.ChoiceId), path + "/ChoiceAccess");
            foreach (var access in n.Call.ChoiceAccess) { condition(access.Condition); if (!string.IsNullOrEmpty(access.ProbabilitySource) && access.ProbabilitySource != "confession") throw new InvalidOperationException("flow.json/" + path + "/ChoiceAccess.ProbabilitySource"); }
            foreach (var effect in n.Call.Effects.Concat(social == null ? Array.Empty<ConfiguredEffect>() : social.SuccessEffects.Concat(social.FailureEffects))) {
                if (!new[] { "money", "money-cost", "knowledge", "stress" }.Contains(effect.Kind) || effect.Target != "self") throw new InvalidOperationException("flow.json/" + path + "/" + n.Id + "/Effects: unsupported kind/target");
                if (effect.Kind == "knowledge" && !ExerciseCatalog.All.Any(e => e.Id == effect.Id)) throw new InvalidOperationException("flow.json/" + path + "/Effects/" + effect.Id + ": unknown exercise");
                if ((effect.Kind == "money-cost" || effect.Kind == "knowledge") && effect.Amount < 0) throw new InvalidOperationException("flow.json/" + path + "/Effects/" + effect.Id + ": nonnegative amount required");
                if (effect.PositiveScalable && (social == null || social.Mode != "noCheck" || effect.Amount <= 0 || !new[] { "money", "knowledge" }.Contains(effect.Kind))) throw new InvalidOperationException("flow.json/" + path + "/Effects/" + effect.Id + ": only positive registered noCheck rewards scale");
            }
        }
        private static void Unique(IEnumerable<string> ids, string path)
        { var list = ids.ToArray(); if (list.Any(string.IsNullOrWhiteSpace) || list.Distinct().Count() != list.Length) throw new InvalidOperationException("flow.json/" + path + ": duplicate/empty ID"); }
    }
}
