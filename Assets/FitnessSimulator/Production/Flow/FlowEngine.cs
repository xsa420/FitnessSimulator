using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Flow
{
    public enum NodePhase { NotStarted, Started, Completed }
    public enum EntryPhase { Unresolved, ChoicePending, Resolved, Finished }

    [Serializable]
    public sealed class FlowEdge
    {
        public string ConditionKey;
        public string TargetNodeId;
    }

    [Serializable]
    public sealed class FlowNode
    {
        public string Id;
        public string DefinitionId;
        public string Handler;
        public string PayloadJson;
        public string Title;
        public string NextNodeId;
        public bool RequiredNext;
        public List<FlowEdge> Branches = new List<FlowEdge>();
    }

    [Serializable]
    public sealed class AnchorTarget
    {
        public int Day;
        public string AnchorId;
        public int AnchorOrder;
    }

    [Serializable]
    public sealed class EntryDefinition
    {
        public string DefinitionId;
        public string RootNodeId;
        public bool Enabled = true;
        public string ConditionKey;
        public int MaximumStarts = -1;
        public int CooldownDays;
        public int Weight = 1;
        public string ExclusiveGroup;
        public bool IsChoice;
        public bool ExplicitConsumption;
        public List<AnchorTarget> ConsumeTargets = new List<AnchorTarget>();
    }

    [Serializable]
    public sealed class FlowDefinition
    {
        public string Id;
        public string Version;
        public bool Dynamic;
        public string FrozenPlanVersion;
        public List<FlowNode> Nodes = new List<FlowNode>();
        public List<EntryDefinition> Entries = new List<EntryDefinition>();
        public EntryDefinition DefaultEntry;
        public int ChoiceGroupWeight = 100;
    }

    [Serializable]
    public sealed class NodeCursor
    {
        public string InstanceId;
        public string NodeId;
        public NodePhase Phase;
    }

    [Serializable]
    public sealed class TriggerStat
    {
        public string ActorId;
        public string DefinitionId;
        public int Count;
        public int LastStartedDay;
    }

    [Serializable]
    public sealed class AnchorOccupancy
    {
        public int Day;
        public string AnchorId;
        public string ActorId;
        public string OwnerInstanceId;
    }

    [Serializable]
    public sealed class FlowState
    {
        public string ActorId;
        public int Day;
        public string AnchorId;
        public int AnchorOrder;
        public string TreeInstanceId;
        public bool HasTree;
        public FlowDefinition Tree;
        public EntryPhase EntryPhase;
        public string SelectedDefinitionId;
        public List<string> ChoiceDefinitionIds = new List<string>();
        public string ChoiceToken;
        public uint RandomState = 1;
        public long RandomDrawIndex;
        public List<NodeCursor> Cursors = new List<NodeCursor>();
        public string CurrentNodeId;
        public List<TriggerStat> TriggerStats = new List<TriggerStat>();
        public List<AnchorOccupancy> Occupancies = new List<AnchorOccupancy>();
    }

    // Mutates only the transaction candidate supplied by the caller; never reads UI or storage.
    public static class FlowEngine
    {
        public static void NormalizeAfterLoad(FlowState state)
        {
            if (state == null || state.Cursors == null || state.ChoiceDefinitionIds == null || state.TriggerStats == null || state.Occupancies == null)
                throw new InvalidOperationException("Missing flow state collections.");
            if (!state.HasTree)
            {
                // JsonUtility materializes inline null classes. Only an otherwise empty tree may be normalized away.
                if (state.Tree != null && (!string.IsNullOrEmpty(state.Tree.Id) || !string.IsNullOrEmpty(state.Tree.Version) ||
                    state.Tree.Nodes != null && state.Tree.Nodes.Count > 0 || state.Tree.Entries != null && state.Tree.Entries.Count > 0) || !string.IsNullOrEmpty(state.TreeInstanceId) ||
                    !string.IsNullOrEmpty(state.CurrentNodeId) || state.Cursors.Count != 0 || state.EntryPhase != EntryPhase.Unresolved)
                    throw new InvalidOperationException("HasTree=false conflicts with persisted flow references.");
                state.Tree = null;
                return;
            }
            Validate(state.Tree);
            if (string.IsNullOrWhiteSpace(state.TreeInstanceId) || string.IsNullOrWhiteSpace(state.ActorId) || state.Day < 1 || string.IsNullOrWhiteSpace(state.AnchorId))
                throw new InvalidOperationException("Incomplete flow instance identity.");
            foreach (var cursor in state.Cursors)
            {
                if (cursor == null || string.IsNullOrWhiteSpace(cursor.InstanceId)) throw new InvalidOperationException("Invalid node cursor.");
                RequireNode(state.Tree, cursor.NodeId);
            }
            if (state.Cursors.GroupBy(c => c.NodeId).Any(g => g.Count() > 1)) throw new InvalidOperationException("Duplicate node cursor.");
            if (state.EntryPhase == EntryPhase.Resolved && (Current(state) == null || Current(state).Phase == NodePhase.Completed))
                throw new InvalidOperationException("Current node reference is missing or complete.");
            if ((state.EntryPhase == EntryPhase.Resolved || state.EntryPhase == EntryPhase.Finished) &&
                !state.Tree.Entries.Concat(new[] { state.Tree.DefaultEntry }).Any(e => e.DefinitionId == state.SelectedDefinitionId))
                throw new InvalidOperationException("Selected entry reference is missing.");
            if (state.EntryPhase == EntryPhase.ChoicePending && (state.ChoiceDefinitionIds.Count < 2 || string.IsNullOrWhiteSpace(state.ChoiceToken) ||
                state.ChoiceDefinitionIds.Any(id => !state.Tree.Entries.Any(e => e.DefinitionId == id))))
                throw new InvalidOperationException("Invalid pending choice.");
            if (state.EntryPhase == EntryPhase.Unresolved || state.EntryPhase == EntryPhase.Finished && !string.IsNullOrEmpty(state.CurrentNodeId))
                throw new InvalidOperationException("Invalid resolved tree phase.");
        }

        public static void Validate(FlowDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.Version) ||
                definition.Nodes == null || definition.Entries == null || definition.DefaultEntry == null || definition.ChoiceGroupWeight <= 0)
                throw new InvalidOperationException("Incomplete flow definition.");
            if (definition.Nodes.Any(n => n == null || string.IsNullOrWhiteSpace(n.Id) || string.IsNullOrWhiteSpace(n.DefinitionId) || string.IsNullOrWhiteSpace(n.Handler) || n.Branches == null) ||
                definition.Nodes.GroupBy(n => n.Id).Any(g => g.Count() != 1)) throw new InvalidOperationException("Invalid/duplicate node.");
            foreach (var node in definition.Nodes)
            {
                if (node.RequiredNext && string.IsNullOrEmpty(node.NextNodeId) && node.Branches.Count == 0) throw new InvalidOperationException("Required successor missing: " + node.Id);
                if (!string.IsNullOrEmpty(node.NextNodeId)) RequireNode(definition, node.NextNodeId);
                foreach (var edge in node.Branches)
                {
                    if (edge == null || string.IsNullOrWhiteSpace(edge.ConditionKey)) throw new InvalidOperationException("Invalid branch.");
                    RequireNode(definition, edge.TargetNodeId);
                }
            }
            foreach (var entry in definition.Entries.Concat(new[] { definition.DefaultEntry }))
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.DefinitionId) || entry.MaximumStarts == 0 || entry.MaximumStarts < -1 ||
                    entry.CooldownDays < 0 || entry.Enabled && entry.Weight <= 0 || entry.ConsumeTargets == null)
                    throw new InvalidOperationException("Invalid entry.");
                RequireNode(definition, entry.RootNodeId);
                if (entry.ConsumeTargets.Any(t => t == null || t.Day < 1 || string.IsNullOrWhiteSpace(t.AnchorId) || t.AnchorOrder < 0))
                    throw new InvalidOperationException("Invalid anchor target.");
            }
            if (definition.Entries.Concat(new[] { definition.DefaultEntry }).GroupBy(e => e.DefinitionId).Any(g => g.Any(e => !SameEntry(g.First(), e))))
                throw new InvalidOperationException("Shared definition has conflicting entry configuration.");
            var fallback = definition.DefaultEntry;
            if (!fallback.Enabled || fallback.MaximumStarts != -1 || fallback.CooldownDays != 0 || fallback.IsChoice)
                throw new InvalidOperationException("Default must be enabled/unlimited/zero cooldown.");
            // M1 finite manifests are DAGs. Interactive repeat-rest uses distinct runtime instances.
            var visiting = new HashSet<string>(); var visited = new HashSet<string>();
            foreach (var node in definition.Nodes) Visit(definition, node.Id, visiting, visited);
        }

        public static void ResolveAnchor(FlowState state, FlowDefinition definition, string actorId, int day, string anchorId,
            int anchorOrder, string treeInstanceId, Func<string, bool> condition, Func<AnchorTarget, bool> unlocked)
        {
            if (state == null || condition == null || unlocked == null || day < 1 || string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(anchorId) || string.IsNullOrWhiteSpace(treeInstanceId))
                throw new ArgumentException("Invalid resolution context.");
            if (state.HasTree && state.ActorId == actorId && state.Day == day && state.AnchorId == anchorId) return;
            if (state.HasTree && state.EntryPhase != EntryPhase.Finished) throw new InvalidOperationException("Previous anchor is unfinished.");
            Validate(definition);
            state.ActorId = actorId; state.Day = day; state.AnchorId = anchorId; state.AnchorOrder = anchorOrder;
            state.TreeInstanceId = treeInstanceId; state.HasTree = true; state.Tree = CopyDefinition(definition); state.Cursors.Clear();
            state.CurrentNodeId = null; state.SelectedDefinitionId = null; state.ChoiceDefinitionIds.Clear(); state.ChoiceToken = null;
            state.Occupancies.RemoveAll(o => o.Day < day);
            var groups = new HashSet<string>(); var ids = new HashSet<string>(); var eligible = new List<EntryDefinition>();
            foreach (var entry in definition.Entries)
            {
                if (!IsEligible(state, entry, condition, unlocked)) continue;
                if (!string.IsNullOrEmpty(entry.ExclusiveGroup) && !groups.Add(entry.ExclusiveGroup)) continue;
                if (ids.Add(entry.DefinitionId)) eligible.Add(entry);
            }
            if (eligible.Count == 0)
            {
                if (!IsEligible(state, definition.DefaultEntry, condition, unlocked)) throw new InvalidOperationException("Default is not executable.");
                Select(state, definition.DefaultEntry); return;
            }
            var choices = eligible.Where(e => e.IsChoice).ToList(); var ordinary = eligible.Where(e => !e.IsChoice).ToList();
            long total = ordinary.Sum(e => (long)e.Weight) + (choices.Count > 0 ? definition.ChoiceGroupWeight : 0);
            if (total > int.MaxValue) throw new InvalidOperationException("Entry weight overflow.");
            int draw = Next(state, (int)total);
            foreach (var entry in ordinary) { if (draw < entry.Weight) { Select(state, entry); return; } draw -= entry.Weight; }
            if (choices.Count == 1) { Select(state, choices[0]); return; }
            state.EntryPhase = EntryPhase.ChoicePending;
            state.ChoiceDefinitionIds = choices.Select(c => c.DefinitionId).ToList();
            state.ChoiceToken = treeInstanceId + "/choice";
        }

        public static void ConfirmChoice(FlowState state, string choiceToken, string definitionId, Func<string, bool> condition, Func<AnchorTarget, bool> unlocked)
        {
            if (state.EntryPhase != EntryPhase.ChoicePending || state.ChoiceToken != choiceToken || !state.ChoiceDefinitionIds.Contains(definitionId))
                throw new InvalidOperationException("Invalid choice token/option.");
            var entry = state.Tree.Entries.First(e => e.DefinitionId == definitionId);
            if (!IsEligible(state, entry, condition, unlocked)) throw new InvalidOperationException("Choice is no longer eligible.");
            Select(state, entry);
        }

        public static NodeCursor Current(FlowState state)
        {
            return state.Cursors.Find(n => n.NodeId == state.CurrentNodeId);
        }

        public static bool StartCurrent(FlowState state)
        {
            var cursor = Current(state) ?? throw new InvalidOperationException("No current node.");
            if (cursor.Phase == NodePhase.Started) return false;
            if (cursor.Phase != NodePhase.NotStarted) throw new InvalidOperationException("Node already completed.");
            var node = RequireNode(state.Tree, cursor.NodeId);
            // The outer entry and its root node are one start when they share definition identity.
            var entry = FindEntry(state);
            if (cursor.NodeId == entry.RootNodeId)
            {
                foreach (var target in Targets(state, entry))
                {
                    var collision = state.Occupancies.Find(o => o.ActorId == state.ActorId && o.Day == target.Day && o.AnchorId == target.AnchorId);
                    if (collision != null && collision.OwnerInstanceId != cursor.InstanceId) throw new InvalidOperationException("Anchor is occupied.");
                    if (collision == null) state.Occupancies.Add(new AnchorOccupancy { ActorId = state.ActorId, Day = target.Day, AnchorId = target.AnchorId, OwnerInstanceId = cursor.InstanceId });
                }
                CountStart(state, entry.DefinitionId);
                if (node.DefinitionId != entry.DefinitionId) CountStart(state, node.DefinitionId);
            }
            else CountStart(state, node.DefinitionId);
            cursor.Phase = NodePhase.Started;
            return true;
        }

        public static void CompleteCurrent(FlowState state, Func<string, bool> condition)
        {
            var cursor = Current(state) ?? throw new InvalidOperationException("No current node.");
            if (cursor.Phase != NodePhase.Started) throw new InvalidOperationException("Node is not started.");
            var node = RequireNode(state.Tree, cursor.NodeId);
            string next = node.NextNodeId;
            foreach (var branch in node.Branches) if (condition(branch.ConditionKey)) { next = branch.TargetNodeId; break; }
            if (node.RequiredNext && string.IsNullOrEmpty(next)) throw new InvalidOperationException("No legal required successor.");
            if (!string.IsNullOrEmpty(next)) RequireNode(state.Tree, next);
            cursor.Phase = NodePhase.Completed;
            if (string.IsNullOrEmpty(next)) { state.CurrentNodeId = null; state.EntryPhase = EntryPhase.Finished; return; }
            state.CurrentNodeId = next;
            state.Cursors.Add(new NodeCursor { NodeId = next, InstanceId = state.TreeInstanceId + "/" + next, Phase = NodePhase.NotStarted });
        }

        private static void Select(FlowState state, EntryDefinition entry)
        {
            state.EntryPhase = EntryPhase.Resolved; state.SelectedDefinitionId = entry.DefinitionId; state.CurrentNodeId = entry.RootNodeId;
            state.Cursors.Add(new NodeCursor { NodeId = entry.RootNodeId, InstanceId = state.TreeInstanceId + "/" + entry.RootNodeId });
        }
        private static EntryDefinition FindEntry(FlowState state)
        { return state.Tree.Entries.FirstOrDefault(e => e.DefinitionId == state.SelectedDefinitionId) ?? state.Tree.DefaultEntry; }
        private static void CountStart(FlowState state, string id)
        {
            var stat = state.TriggerStats.Find(s => s.ActorId == state.ActorId && s.DefinitionId == id);
            if (stat == null) { stat = new TriggerStat { ActorId = state.ActorId, DefinitionId = id }; state.TriggerStats.Add(stat); }
            stat.Count = checked(stat.Count + 1); stat.LastStartedDay = state.Day;
        }
        private static bool IsEligible(FlowState state, EntryDefinition entry, Func<string, bool> condition, Func<AnchorTarget, bool> unlocked)
        {
            if (!entry.Enabled || !string.IsNullOrEmpty(entry.ConditionKey) && !condition(entry.ConditionKey)) return false;
            var stat = state.TriggerStats.Find(s => s.ActorId == state.ActorId && s.DefinitionId == entry.DefinitionId);
            if (stat != null && (entry.MaximumStarts >= 0 && stat.Count >= entry.MaximumStarts ||
                entry.CooldownDays > 0 && state.Day - stat.LastStartedDay <= entry.CooldownDays)) return false;
            return Targets(state, entry).All(t => unlocked(t) && !state.Occupancies.Any(o => o.ActorId == state.ActorId && o.Day == t.Day && o.AnchorId == t.AnchorId));
        }
        private static List<AnchorTarget> Targets(FlowState state, EntryDefinition entry)
        {
            if (!entry.ExplicitConsumption) return new List<AnchorTarget> { new AnchorTarget { Day = state.Day, AnchorId = state.AnchorId, AnchorOrder = state.AnchorOrder } };
            var targets = entry.ConsumeTargets.Where(t => t.Day > state.Day || t.Day == state.Day && t.AnchorOrder >= state.AnchorOrder).ToList();
            if (targets.Count == 0) throw new InvalidOperationException("Explicit consumption became empty after filtering past anchors.");
            return targets;
        }
        private static int Next(FlowState state, int maximum)
        {
            if (state.RandomState == 0) throw new InvalidOperationException("Random state cannot be zero.");
            uint x = state.RandomState; x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            state.RandomState = x; state.RandomDrawIndex++;
            return (int)(x % (uint)maximum);
        }
        private static FlowNode RequireNode(FlowDefinition tree, string id)
        { return tree.Nodes.FirstOrDefault(n => n.Id == id) ?? throw new InvalidOperationException("Missing node: " + id); }
        private static FlowDefinition CopyDefinition(FlowDefinition value)
        {
            return new FlowDefinition { Id = value.Id, Version = value.Version, Dynamic = value.Dynamic, FrozenPlanVersion = value.FrozenPlanVersion,
                ChoiceGroupWeight = value.ChoiceGroupWeight, DefaultEntry = CopyEntry(value.DefaultEntry), Entries = value.Entries.Select(CopyEntry).ToList(),
                Nodes = value.Nodes.Select(n => new FlowNode { Id = n.Id, DefinitionId = n.DefinitionId, Handler = n.Handler, PayloadJson = n.PayloadJson, Title = n.Title, NextNodeId = n.NextNodeId,
                    RequiredNext = n.RequiredNext, Branches = n.Branches.Select(b => new FlowEdge { ConditionKey = b.ConditionKey, TargetNodeId = b.TargetNodeId }).ToList() }).ToList() };
        }
        private static EntryDefinition CopyEntry(EntryDefinition value)
        {
            return new EntryDefinition { DefinitionId = value.DefinitionId, RootNodeId = value.RootNodeId, Enabled = value.Enabled,
                ConditionKey = value.ConditionKey, MaximumStarts = value.MaximumStarts, CooldownDays = value.CooldownDays, Weight = value.Weight,
                ExclusiveGroup = value.ExclusiveGroup, IsChoice = value.IsChoice, ExplicitConsumption = value.ExplicitConsumption,
                ConsumeTargets = value.ConsumeTargets.Select(t => new AnchorTarget { Day = t.Day, AnchorId = t.AnchorId, AnchorOrder = t.AnchorOrder }).ToList() };
        }
        private static bool SameEntry(EntryDefinition a, EntryDefinition b)
        {
            return a.DefinitionId == b.DefinitionId && a.RootNodeId == b.RootNodeId && a.Enabled == b.Enabled &&
                a.ConditionKey == b.ConditionKey && a.MaximumStarts == b.MaximumStarts && a.CooldownDays == b.CooldownDays &&
                a.Weight == b.Weight && a.ExclusiveGroup == b.ExclusiveGroup && a.IsChoice == b.IsChoice &&
                a.ExplicitConsumption == b.ExplicitConsumption && a.ConsumeTargets.Count == b.ConsumeTargets.Count &&
                a.ConsumeTargets.Select((t, i) => t.Day == b.ConsumeTargets[i].Day && t.AnchorId == b.ConsumeTargets[i].AnchorId && t.AnchorOrder == b.ConsumeTargets[i].AnchorOrder).All(equal => equal);
        }
        private static void Visit(FlowDefinition tree, string id, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException("Unbounded cycle in flow: " + id);
            var node = RequireNode(tree, id);
            foreach (string next in node.Branches.Select(b => b.TargetNodeId).Concat(new[] { node.NextNodeId }).Where(n => !string.IsNullOrEmpty(n))) Visit(tree, next, visiting, visited);
            visiting.Remove(id); visited.Add(id);
        }
    }

    [Serializable]
    public sealed class DayStepCursor
    {
        public int Day;
        public string Phase;
        public List<string> StepIds = new List<string>();
        public int NextIndex;
        public bool AwaitingNextDay;
        public string CurrentStep { get { return NextIndex < StepIds.Count ? StepIds[NextIndex] : null; } }

        public bool Complete(string stepId)
        {
            int index = StepIds.IndexOf(stepId);
            if (index < 0 || index > NextIndex) throw new InvalidOperationException("Unexpected day step.");
            if (index < NextIndex) return false;
            NextIndex++;
            if (NextIndex == StepIds.Count) AwaitingNextDay = true;
            return true;
        }
    }
}
