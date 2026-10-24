using System;

namespace FitnessSimulator.Persistence
{
    [Serializable]
    public sealed class RunSaveEnvelope
    {
        public int schemaVersion = 1;
        public string runId;
        public int gameDay;
        public string currentAnchorId;
        public string nextStoryEventId;
        public bool awaitingStoryChoice;
        public uint randomState;
        public string committedStateJson;
        public string activePlanJson;
        public string pendingPlanJson;
    }

    [Serializable]
    public sealed class EndingHistoryEntry
    {
        public string runId;
        public string endingId;
        public int gameDay;
        public string achievedAtUtc;
        public string immutableSnapshotJson;
        public bool noteWasSet;
        public string note;
    }

    [Serializable]
    public sealed class EndingHistoryEnvelope
    {
        public int schemaVersion = 1;
        public EndingHistoryEntry[] entries = Array.Empty<EndingHistoryEntry>();
    }
}
