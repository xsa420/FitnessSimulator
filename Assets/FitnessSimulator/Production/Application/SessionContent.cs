using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    // M1 content manifest. Each planned set has its own finite, recoverable node identities.
    public static class SessionContent
    {
        public const string Version = "M2.1/TG-0.3/LifeEconomyM2.v1/PlanningSchema2/InputKernelsV1";
        public const string EventRulesVersion = Version + "/UsabilityWorkR1";
        public static IReadOnlyList<string> TrainingAnchorIds => Array.AsReadOnly(FlowContent.Current.Anchors.Where(a => a.TrainingAllowed).Select(a => a.Id).ToArray());
        public static string[] Anchors(SessionState state) => FlowContent.Current.Anchors.OrderBy(a => a.Order)
            .Where(a => string.IsNullOrEmpty(a.UnlockCondition) || AvailableDay(state, a.Id) > 0 && state.Day >= AvailableDay(state, a.Id)).Select(a => a.Id).ToArray();
        public static int AvailableDay(SessionState state, string id)
        {
            if (id == "early" && state.EarlyAvailableDay > 0) return state.EarlyAvailableDay;
            if (id == "late" && state.LateAvailableDay > 0) return state.LateAvailableDay;
            return state.AnchorUnlocks?.FirstOrDefault(x => x.Id == id)?.Day ?? 0;
        }
        public static bool IsMeal(string anchor) => FlowContent.Anchor(anchor).Kind == "meal";
        public static string Name(string anchor) => FlowContent.Anchor(anchor).Name;
        public static FlowDefinition Build(string anchor, DayTrainingQuota quota, bool trainingHere,
            SessionState state = null, ActivityState activity = null, bool restDay = false, bool hasInjury = false,
            Func<string, bool> evaluate = null)
        {
            var content = FlowContent.Current; var a = content.Anchors.Single(x => x.Id == anchor);
            Func<string, bool> test = evaluate ?? (id => FlowContent.Evaluate(id, key => {
                switch (key) {
                    case "restDay": return restDay ? "true" : "false";
                    case "trainingHere": return trainingHere ? "true" : "false";
                    case "firstOrdinary": return state != null && Anchors(state).FirstOrDefault(x => !IsMeal(x)) == anchor ? "true" : "false";
                    case "overtime": return (state?.LastOvertime ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case "hasInjury": return hasInjury ? "true" : "false";
                    case "fullDayStudy": return activity != null && activity.FullDayStudy ? "true" : "false";
                    case "practiced": return (activity?.PracticedExercises.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    default: throw new InvalidOperationException("Flow generation needs fact: " + key);
                }
            }));
            var tree = new FlowDefinition { Id = "M1/" + anchor, Version = content.Version, ChoiceGroupWeight = content.ChoiceGroupWeight,
                Dynamic = trainingHere, FrozenPlanVersion = trainingHere ? quota.PlanRevision : null };
            var fallback = a.Defaults.First(x => x.Enabled && test(x.Condition));
            tree.DefaultEntry = AppendEvent(tree, fallback, true, state);
            foreach (var binding in a.Entries) {
                if (binding.EventId == "training-choice") {
                    if (!trainingHere || !binding.Enabled) continue;
                    AddTraining(tree, quota, content.Training);
                    tree.Entries.Add(new EntryDefinition { DefinitionId = "training-choice", RootNodeId = "training", IsChoice = binding.IsChoice,
                        Weight = binding.Weight, MaximumStarts = binding.MaximumStarts, CooldownDays = binding.CooldownDays, ConditionKey = binding.Condition, ExclusiveGroup = binding.ExclusiveGroup });
                } else tree.Entries.Add(AppendEvent(tree, binding, false, state));
            }
            FlowEngine.Validate(tree); return tree;
        }
        private static EntryDefinition AppendEvent(FlowDefinition tree, EventBinding binding, bool fallback, SessionState state)
        {
            var definition = FlowContent.Current.Events.Single(e => e.Id == binding.EventId);
            string prefix = fallback ? "default/" : definition.Id + "/";
            foreach (var n in definition.Nodes) tree.Nodes.Add(new FlowNode { Id = prefix + n.Id, DefinitionId = n.Id == definition.Root ? definition.Id : definition.Id + "/node/" + n.Id,
                Handler = n.Handler, Title = string.IsNullOrEmpty(n.Title) ? definition.Title : n.Title,
                PayloadJson = FitnessSimulator.Game.Configuration.ContentJson.Serialize(n.Call), NextNodeId = string.IsNullOrEmpty(n.Next) ? null : prefix + n.Next,
                RequiredNext = !string.IsNullOrEmpty(n.Next), Branches = n.Branches.Select(b => new FlowEdge { ConditionKey = b.ConditionKey, TargetNodeId = prefix + b.TargetNodeId }).ToList() });
            var result = new EntryDefinition { DefinitionId = fallback ? "default-" + definition.Id : definition.Id,
                RootNodeId = prefix + definition.Root, Enabled = binding.Enabled, ConditionKey = fallback ? "" : binding.Condition,
                IsChoice = binding.IsChoice, Weight = binding.Weight, MaximumStarts = binding.MaximumStarts, CooldownDays = binding.CooldownDays,
                ExclusiveGroup = binding.ExclusiveGroup, ExplicitConsumption = binding.Consume.Length > 0 };
            foreach (var target in binding.Consume) {
                if (state == null) throw new InvalidOperationException("Explicit consumption requires a date context");
                int targetDay = checked(state.Day + target.DayOffset);
                if (!string.IsNullOrEmpty(target.Weekday)) { int weekday = (int)Enum.Parse(typeof(DayOfWeek), target.Weekday); targetDay = checked((state.Day - 1) / 7 * 7 + (weekday == 0 ? 7 : weekday)); }
                var future = new SessionState { Day = targetDay, EarlyAvailableDay = state.EarlyAvailableDay, LateAvailableDay = state.LateAvailableDay, AnchorUnlocks = state.AnchorUnlocks };
                var ids = Anchors(future);
                var selected = target.AnchorId == "$ordinary" ? ids.Where(id => !IsMeal(id)) : new[] { target.AnchorId };
                foreach (string id in selected) { int order = Array.IndexOf(ids, id); if (order < 0) throw new InvalidOperationException("Consumption target is locked: " + id);
                    result.ConsumeTargets.Add(new AnchorTarget { Day = future.Day, AnchorId = id, AnchorOrder = order }); }
            }
            return result;
        }
        private static void AddTraining(FlowDefinition tree, DayTrainingQuota quota, TrainingFlowTemplate template)
        {
            Func<int, int, string> entry = (action, set) => template.EntryNode.Replace("{prefix}", Prefix(action, set));
            Add(tree, "training", "training", entry(0, 1));
            tree.Nodes.Last().DefinitionId = "training-choice";
            tree.Nodes.Last().Branches.Add(new FlowEdge { ConditionKey = "workout-ended", TargetNodeId = "summary" });
            for (int target = 0; target < quota.Entries.Length; target++) tree.Nodes.Last().Branches.Add(new FlowEdge { ConditionKey = "action-" + target, TargetNodeId = entry(target, 1) });
            for (int a = 0; a < quota.Entries.Length; a++) {
                string nextAction = a + 1 == quota.Entries.Length ? "summary" : entry(a + 1, 1);
                for (int set = 1; set <= quota.Entries[a].Plan.Sets; set++) {
                    string next = set < quota.Entries[a].Plan.Sets ? entry(a, set + 1) : nextAction;
                    Func<string, string> expand = value => string.IsNullOrEmpty(value) ? null : value.Replace("{prefix}", Prefix(a, set)).Replace("{next}", next).Replace("{nextAction}", nextAction);
                    foreach (var n in template.GroupNodes) {
                        var node = new FlowNode { Id = expand(n.Id), DefinitionId = n.Handler, Handler = n.Handler, Title = n.Title,
                            PayloadJson = FitnessSimulator.Game.Configuration.ContentJson.Serialize(n.Call), NextNodeId = expand(n.Next), RequiredNext = !string.IsNullOrEmpty(n.Next),
                            Branches = n.Branches.Select(b => new FlowEdge { ConditionKey = b.ConditionKey, TargetNodeId = expand(b.TargetNodeId) }).ToList() };
                        if (n.Handler == "play" || n.Handler == "followers") for (int target = a + 1; target < quota.Entries.Length; target++) node.Branches.Add(new FlowEdge { ConditionKey = "action-" + target, TargetNodeId = entry(target, 1) });
                        tree.Nodes.Add(node);
                    }
                }
            }
            Add(tree, "summary", "summary"); tree.Nodes.Last().Title = template.SummaryTitle;
        }
        // Only this reviewed generator/version's empty posture hint is automatic. A future
        // content version or differently shaped story retains its explicit continue action.
        public static bool IsAutomaticPostureHint(FlowState flow, TrainingDayState training, FlowNode node)
        {
            var tree = flow?.Tree;
            var workout = training?.Workout;
            if (tree == null || !tree.Dynamic || (tree.Version != Version && tree.Version != EventRulesVersion && tree.Version != FlowContent.Current.Version)
                || tree.Id != "M1/" + flow.AnchorId || flow.SelectedDefinitionId != "training-choice"
                || workout?.Phase != WorkoutPhase.PreSet || workout.Quota == null
                || tree.FrozenPlanVersion != workout.Quota.PlanRevision || workout.EntryIndex < 0
                || workout.EntryIndex >= workout.Quota.Entries.Length || node == null) return false;
            var entry = workout.Quota.Entries[workout.EntryIndex];
            if (workout.SetOrdinal < 1 || workout.SetOrdinal > entry.Plan.Sets) return false;
            string prefix = Prefix(workout.EntryIndex, workout.SetOrdinal);
            return node.Id == prefix + "story" && node.DefinitionId == "group-story" && node.Handler == "group-story"
                && node.RequiredNext && node.NextNodeId == prefix + "play" && node.Branches != null
                && node.Branches.Count == 1 && node.Branches[0].ConditionKey == "workout-ended"
                && node.Branches[0].TargetNodeId == "summary"
                && tree.Nodes.Any(n => n.Id == prefix + "play" && n.Handler == "play" && n.DefinitionId == "play");
        }

        private static string Prefix(int action, int set) => "a" + action + "s" + set + "-";
        private static void Add(FlowDefinition tree, string id, string handler, string next = null)
        { tree.Nodes.Add(new FlowNode { Id = id, DefinitionId = handler, Handler = handler, NextNodeId = next, RequiredNext = next != null }); }
    }
}
