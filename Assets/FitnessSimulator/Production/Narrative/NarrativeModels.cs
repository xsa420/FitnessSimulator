using System;
using FitnessSimulator.Game.Configuration;

namespace FitnessSimulator.Game.Narrative
{
    [Serializable] public sealed class NarrativeDocument
    {
        public string Version;
        public NarrativeStory[] Stories = Array.Empty<NarrativeStory>();
    }
    [Serializable] public sealed class NarrativeStory
    {
        public string Id, Version, Entry, Recovery;
        public NarrativeVariable[] Variables = Array.Empty<NarrativeVariable>();
        public NarrativeNode[] Nodes = Array.Empty<NarrativeNode>();
    }
    [Serializable] public sealed class NarrativeVariable { public string Name, Type; }
    [Serializable] public sealed class NarrativeNode
    {
        public string Id, Kind;
        [ContentOptional] public string Speaker, Text, Next, Result;
        [ContentOptional]
        public NarrativeChoice[] Choices = Array.Empty<NarrativeChoice>();
    }
    [Serializable] public sealed class NarrativeChoice
    {
        public string Id, Text, Next;
        public bool RequiresAccess;
    }
    [Serializable] public sealed class NarrativeValue { public string Key, Value; }
    [Serializable] public sealed class NarrativeChoiceAccess
    {
        public string ChoiceId;
        public bool Enabled;
        public string DisabledReason, CostText, ProbabilityText;
    }
    [Serializable] public sealed class NarrativeContext
    {
        public NarrativeValue[] Values = Array.Empty<NarrativeValue>();
        public NarrativeChoiceAccess[] Choices = Array.Empty<NarrativeChoiceAccess>();
    }
    [Serializable] public sealed class NarrativeState
    {
        public string InstanceId, ContentVersion, ContentHash, DocumentVersion, NodeId, Result, SelectedChoiceId;
        public NarrativeStory Story;
        public NarrativeContext Context;
        public bool FullyShown, Completed;
        public string[] CompletedSegments = Array.Empty<string>();
        public string[] ReadKeys = Array.Empty<string>();
    }
    [Serializable] public sealed class NarrativeOptionView
    {
        public string Id, Text, DisabledReason, CostText, ProbabilityText;
        public bool Enabled;
    }
    [Serializable] public sealed class NarrativeProjection
    {
        public string InstanceId, NodeId, Speaker, Text, ReadKey, Result, SelectedChoiceId;
        public bool FullyShown, Completed;
        public NarrativeOptionView[] Choices = Array.Empty<NarrativeOptionView>();
    }
}
