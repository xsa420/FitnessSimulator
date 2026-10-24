using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using FitnessSimulator.Game.Configuration;
using UnityEngine;

namespace FitnessSimulator.Game.Narrative
{
    // Local text execution only. No clock, actor, wallet, random or persistence writes.
    public static class NarrativeRuntime
    {
        public static void ValidateContent() => Validate(ContentRuntime.Current.Get<NarrativeDocument>("narrative"));
        public static NarrativeState Begin(string storyId, string instanceId, NarrativeContext context)
        {
            var document = ContentRuntime.Current.Get<NarrativeDocument>("narrative"); Validate(document);
            var story = document.Stories.SingleOrDefault(x => x.Id == storyId) ?? throw Error("Stories", "Unknown story " + storyId);
            return Begin(story, instanceId, context, ContentRuntime.Current.Version, ContentRuntime.Current.Hash, document.Version);
        }
        public static NarrativeState Begin(NarrativeStory story, string instanceId, NarrativeContext context, string contentVersion, string contentHash, string documentVersion)
        {
            Validate(new NarrativeDocument { Version = documentVersion, Stories = new[] { story } });
            if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(contentVersion) || string.IsNullOrWhiteSpace(contentHash)) throw Error("state", "Missing instance/content identity");
            var state = new NarrativeState { InstanceId = instanceId, Story = Copy(story), Context = Copy(context ?? new NarrativeContext()), ContentVersion = contentVersion, ContentHash = contentHash, DocumentVersion = documentVersion };
            ValidateContext(state);
            Enter(state, story.Entry); return state;
        }
        public static NarrativeState Reveal(NarrativeState source)
        {
            var state = CheckedCopy(source); if (!state.Completed) state.FullyShown = true; return state;
        }
        public static NarrativeState Advance(NarrativeState source)
        {
            var state = CheckedCopy(source); if (state.Completed) return state;
            var node = Node(state);
            if (!state.FullyShown) { state.FullyShown = true; return state; }
            if (node.Kind != "line") throw Error("node." + node.Id, "A choice requires explicit Choose");
            CompleteSegment(state); Enter(state, node.Next); return state;
        }
        public static NarrativeState Choose(NarrativeState source, string choiceId)
        {
            var state = CheckedCopy(source); if (state.Completed) throw Error("state", "Story already returned");
            var node = Node(state);
            if (node.Kind != "choice" || !state.FullyShown) throw Error("node." + node.Id, "Choice is not visible");
            var choice = node.Choices.SingleOrDefault(x => x.Id == choiceId) ?? throw Error("choice", "Unknown choice " + choiceId);
            var access = Access(state, choice);
            if (!access.Enabled) throw Error("choice." + choiceId, "Choice disabled");
            state.SelectedChoiceId = choice.Id; CompleteSegment(state); Enter(state, choice.Next); return state;
        }
        public static NarrativeState Restore(NarrativeState source)
        {
            var state = CheckedCopy(source); if (state.Completed) return state;
            if (state.Story.Recovery == "ordinary")
            {
                state.CompletedSegments = Array.Empty<string>(); state.ReadKeys = Array.Empty<string>(); state.SelectedChoiceId = null; Enter(state, state.Story.Entry);
            }
            else state.FullyShown = false;
            return state;
        }
        public static NarrativeState SkipRead(NarrativeState source, string[] persistedReadKeys)
        {
            var state = CheckedCopy(source); var read = new HashSet<string>(persistedReadKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
            var visited = new HashSet<string>();
            while (!state.Completed && Node(state).Kind == "line" && read.Contains(ReadKey(state)))
            {
                if (!visited.Add(state.NodeId)) throw Error("node." + state.NodeId, "Read-skip cycle");
                state.FullyShown = true; state = Advance(state);
            }
            return state;
        }
        public static NarrativeProjection Project(NarrativeState source)
        {
            var state = CheckedCopy(source);
            if (state.Completed) return new NarrativeProjection { InstanceId = state.InstanceId, NodeId = state.NodeId, Completed = true, Result = state.Result, SelectedChoiceId = state.SelectedChoiceId };
            var node = Node(state);
            return new NarrativeProjection { InstanceId = state.InstanceId, NodeId = node.Id, Speaker = Format(node.Speaker, state), Text = Format(node.Text, state), ReadKey = ReadKey(state), FullyShown = state.FullyShown,
                Choices = node.Kind == "choice" && state.FullyShown ? node.Choices.Select(choice => {
                    var access = Access(state, choice);
                    return new NarrativeOptionView { Id = choice.Id, Text = Format(choice.Text, state), Enabled = access.Enabled, DisabledReason = access.DisabledReason, CostText = access.CostText, ProbabilityText = access.ProbabilityText };
                }).ToArray() : Array.Empty<NarrativeOptionView>() };
        }
        public static void Validate(NarrativeDocument document)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.Version) || document.Stories == null || document.Stories.Length == 0) throw Error("Version/Stories", "Missing content");
            var storyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var story in document.Stories)
            {
                if (story == null || string.IsNullOrWhiteSpace(story.Id) || !storyIds.Add(story.Id)) throw Error("Stories.Id", "Missing/duplicate story");
                string path = "Stories[" + story.Id + "]";
                if (string.IsNullOrWhiteSpace(story.Version) || (story.Recovery != "ordinary" && story.Recovery != "ending")) throw Error(path, "Version or Recovery invalid");
                if (story.Variables == null || story.Variables.Any(x => x == null || !Regex.IsMatch(x.Name ?? "", @"^[A-Za-z][A-Za-z0-9_]*$") || (x.Type != "string" && x.Type != "int" && x.Type != "decimal")) || story.Variables.Select(x => x.Name).Distinct().Count() != story.Variables.Length) throw Error(path + ".Variables", "Invalid variable whitelist/type");
                if (story.Nodes == null || story.Nodes.Length == 0 || story.Nodes.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id)) || story.Nodes.Select(x => x.Id).Distinct().Count() != story.Nodes.Length) throw Error(path + ".Nodes", "Invalid nodes");
                var ids = new HashSet<string>(story.Nodes.Select(x => x.Id));
                if (!ids.Contains(story.Entry)) throw Error(path + ".Entry", "Missing entry");
                var choiceIds = new HashSet<string>();
                foreach (var node in story.Nodes)
                {
                    string np = path + ".Nodes[" + node.Id + "]";
                    if (node.Kind != "line" && node.Kind != "choice" && node.Kind != "return") throw Error(np + ".Kind", "Unknown handler");
                    ValidateText(node.Speaker, story, np + ".Speaker"); ValidateText(node.Text, story, np + ".Text");
                    if (node.Kind == "line" && !ids.Contains(node.Next)) throw Error(np + ".Next", "Missing target");
                    if (node.Kind == "return" && string.IsNullOrWhiteSpace(node.Result)) throw Error(np + ".Result", "Missing return value");
                    if (node.Kind != "choice" && node.Choices != null && node.Choices.Length != 0) throw Error(np + ".Choices", "Choices on non-choice node");
                    if (node.Kind != "choice") continue;
                    if (node.Choices == null || node.Choices.Length == 0) throw Error(np + ".Choices", "Empty choice list");
                    foreach (var choice in node.Choices)
                    {
                        if (choice == null || string.IsNullOrWhiteSpace(choice.Id) || !choiceIds.Add(choice.Id) || !ids.Contains(choice.Next)) throw Error(np + ".Choices", "Invalid ID/target");
                        ValidateText(choice.Text, story, np + ".Choices[" + choice.Id + "].Text");
                    }
                }
                var reachable = new HashSet<string>(); var pending = new Stack<string>(); pending.Push(story.Entry);
                while (pending.Count > 0) { var id = pending.Pop(); if (!reachable.Add(id)) continue; var node = story.Nodes.Single(x => x.Id == id); if (node.Kind == "line") pending.Push(node.Next); if (node.Kind == "choice") foreach (var choice in node.Choices) pending.Push(choice.Next); }
                if (reachable.Count != story.Nodes.Length || !story.Nodes.Any(x => x.Kind == "return")) throw Error(path + ".Nodes", "Unreachable nodes or no return");
            }
        }
        private static void ValidateText(string text, NarrativeStory story, string path)
        {
            foreach (Match match in Regex.Matches(text ?? "", @"\{([^{}]+)\}"))
                if (!story.Variables.Any(x => x.Name == match.Groups[1].Value)) throw Error(path, "Undeclared variable " + match.Groups[1].Value);
        }
        private static void ValidateContext(NarrativeState state)
        {
            var values = state.Context.Values;
            if (values == null || values.Any(x => x == null || x.Value == null || !state.Story.Variables.Any(v => v.Name == x.Key)) || values.Select(x => x.Key).Distinct().Count() != values.Length || state.Story.Variables.Any(key => !values.Any(x => x.Key == key.Name))) throw Error("Context.Values", "Whitelist mismatch");
            foreach (var definition in state.Story.Variables)
            {
                string value = values.Single(x => x.Key == definition.Name).Value;
                if (definition.Type == "int" && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) || definition.Type == "decimal" && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) throw Error("Context.Values." + definition.Name, "Variable type mismatch");
            }
            var choices = state.Context.Choices;
            var definitions = state.Story.Nodes.Where(x => x.Kind == "choice").SelectMany(x => x.Choices).ToArray();
            var ids = definitions.Select(x => x.Id).ToArray();
            if (choices == null || choices.Any(x => x == null || !ids.Contains(x.ChoiceId) || !x.Enabled && string.IsNullOrWhiteSpace(x.DisabledReason)) || choices.Select(x => x.ChoiceId).Distinct().Count() != choices.Length || definitions.Any(choice => choice.RequiresAccess && !choices.Any(x => x.ChoiceId == choice.Id))) throw Error("Context.Choices", "Required external eligibility missing");
        }
        private static NarrativeChoiceAccess Access(NarrativeState state, NarrativeChoice choice) => state.Context.Choices.SingleOrDefault(x => x.ChoiceId == choice.Id) ?? new NarrativeChoiceAccess { ChoiceId = choice.Id, Enabled = !choice.RequiresAccess };
        private static string Format(string text, NarrativeState state) => Regex.Replace(text ?? "", @"\{([^{}]+)\}", match => state.Context.Values.Single(x => x.Key == match.Groups[1].Value).Value);
        private static NarrativeState CheckedCopy(NarrativeState source)
        {
            if (source == null || source.Story == null || source.Context == null) throw Error("state", "Missing narrative state");
            var state = Copy(source); Validate(new NarrativeDocument { Version = state.DocumentVersion, Stories = new[] { state.Story } }); ValidateContext(state);
            if (!state.Story.Nodes.Any(x => x.Id == state.NodeId)) throw Error("state.NodeId", "Missing node");
            if (state.Completed != (Node(state).Kind == "return") || state.Completed && state.Result != Node(state).Result) throw Error("state.Result", "Terminal mismatch");
            return state;
        }
        private static NarrativeNode Node(NarrativeState state) => state.Story.Nodes.Single(x => x.Id == state.NodeId);
        private static string ReadKey(NarrativeState state) => state.Story.Id + "/" + state.Story.Version + "/" + state.NodeId;
        private static void CompleteSegment(NarrativeState state)
        {
            state.CompletedSegments = (state.CompletedSegments ?? Array.Empty<string>()).Concat(new[] { state.NodeId }).Distinct().ToArray();
            state.ReadKeys = (state.ReadKeys ?? Array.Empty<string>()).Concat(new[] { ReadKey(state) }).Distinct().ToArray();
        }
        private static void Enter(NarrativeState state, string id)
        { state.NodeId = id; state.FullyShown = false; state.Completed = Node(state).Kind == "return"; state.Result = state.Completed ? Node(state).Result : null; }
        private static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        private static ArgumentException Error(string path, string message) => new ArgumentException("narrative.json:" + path + ": " + message);
    }
}
