using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Core
{
    public enum CommandSource { Player, NpcProduction, Test }
    public enum OutcomeKind { Success, BusinessFailure, WaitingInput, ControlOnly }
    public enum EventDisposition { KeepOpen, CompleteNode, AuxiliaryOnly }
    public enum CheckKind { None, NormalCompletion, EndingPresentation }
    public enum OpportunityPolicy { Keep, Consume }
    public enum CommandStatus { Committed, AlreadyCommitted, Rejected, ConfigError, StorageUnavailable, StorageOutcomeUnknown }
    public enum LoadStatus { Empty, Loaded, CorruptCommitted, IncompatibleSchema, MissingContent, IoUnavailable }
    public enum CommitStatus { Committed, Rejected, Unavailable, Unknown }

    [Serializable]
    public sealed class CommandEnvelope
    {
        public string OperationId;
        public long ExpectedRevision;
        public string RunId;
        public string ActorId;
        public string ResourceScope;
        public string EventInstanceId;
        public string SetId;
        public string ActionKind;
        public string Payload;
        public CommandSource Source;
        public string ChoiceToken;
    }

    [Serializable]
    public sealed class VersionedFragment
    {
        public string Owner;
        public string Scope;
        public int SchemaVersion;
        public long Revision;
        public string Json;
    }

    [Serializable]
    public sealed class LogPrefix
    {
        public string StreamId;
        public int Segment;
        public long Offset;
        public long FrameCount;
        public string Hash;
    }

    [Serializable]
    public sealed class OperationReceipt
    {
        public string OperationId;
        public string PayloadHash;
        public string RunId;
        public string SemanticKey;
        public string CommitId;
        public long Revision;
        public string Result;
    }

    [Serializable]
    public sealed class RootEnvelope
    {
        public int SchemaVersion = 2;
        public string ContentVersion;
        public string RunId;
        public long Generation;
        public long Revision;
        public string LastCommitId;
        public List<VersionedFragment> Fragments = new List<VersionedFragment>();
        public List<VersionedFragment> PermanentFragments = new List<VersionedFragment>();
        public List<LogPrefix> LogPrefixes = new List<LogPrefix>();
        public List<OperationReceipt> RecentReceipts = new List<OperationReceipt>();

        // Fragment JSON and IDs are immutable strings. Copy every mutable container/record,
        // without repeatedly parsing or escaping the entire frozen content package.
        public RootEnvelope DetachedCopy()
        {
            return new RootEnvelope {
                SchemaVersion = SchemaVersion, ContentVersion = ContentVersion, RunId = RunId,
                Generation = Generation, Revision = Revision, LastCommitId = LastCommitId,
                Fragments = Fragments?.Select(CopyFragment).ToList(),
                PermanentFragments = PermanentFragments?.Select(CopyFragment).ToList(),
                LogPrefixes = LogPrefixes?.Select(p => p == null ? null : new LogPrefix {
                    StreamId = p.StreamId, Segment = p.Segment, Offset = p.Offset, FrameCount = p.FrameCount, Hash = p.Hash }).ToList(),
                RecentReceipts = RecentReceipts?.Select(r => r == null ? null : new OperationReceipt {
                    OperationId = r.OperationId, PayloadHash = r.PayloadHash, RunId = r.RunId, SemanticKey = r.SemanticKey,
                    CommitId = r.CommitId, Revision = r.Revision, Result = r.Result }).ToList()
            };
        }
        private static VersionedFragment CopyFragment(VersionedFragment f) => f == null ? null : new VersionedFragment {
            Owner = f.Owner, Scope = f.Scope, SchemaVersion = f.SchemaVersion, Revision = f.Revision, Json = f.Json };

        public VersionedFragment FindFragment(string owner, string scope)
        {
            return Fragments.Find(f => f.Owner == owner && f.Scope == scope);
        }

        public void SetFragment(string owner, string scope, int schemaVersion, long revision, string json)
        {
            var value = new VersionedFragment { Owner = owner, Scope = scope, SchemaVersion = schemaVersion, Revision = revision, Json = json };
            int index = Fragments.FindIndex(f => f.Owner == owner && f.Scope == scope);
            if (index < 0) Fragments.Add(value); else Fragments[index] = value;
        }
    }

    [Serializable]
    public sealed class LogAppend
    {
        public string StreamId;
        public string Kind;
        public string Json;
    }

    public sealed class PreparedOutcome
    {
        public OutcomeKind OutcomeKind;
        public EventDisposition EventDisposition;
        public CheckKind CheckKind;
        public OpportunityPolicy OpportunityPolicy;
        public string SemanticKey;
        public string Result;
        public List<LogAppend> Appends = new List<LogAppend>();

        public void Validate(CommandSource source)
        {
            if (CheckKind != CheckKind.None && EventDisposition != EventDisposition.CompleteNode)
                throw new InvalidOperationException("Only completed nodes can request an ending check.");
            if (CheckKind == CheckKind.NormalCompletion && source == CommandSource.NpcProduction)
                throw new InvalidOperationException("NPC completion must not implicitly check player endings.");
            if (OutcomeKind == OutcomeKind.WaitingInput && EventDisposition == EventDisposition.CompleteNode)
                throw new InvalidOperationException("Waiting input cannot complete a node.");
            if (string.IsNullOrWhiteSpace(SemanticKey)) throw new InvalidOperationException("A semantic idempotency key is required.");
        }
    }

    public sealed class LoadResult
    {
        public LoadStatus Status;
        public RootEnvelope Root;
        public string Error;
    }

    public sealed class CommitResult
    {
        public CommitStatus Status;
        public RootEnvelope Root;
        public OperationReceipt Receipt;
        public string Error;
    }

    public sealed class CommandResult
    {
        public CommandStatus Status;
        public OperationReceipt Receipt;
        public long CurrentRevision;
        public string Error;
    }

    public interface IStateCodec
    {
        string Encode<T>(T value);
        T Decode<T>(string json);
    }

    public interface ICheckpointStore : IDisposable
    {
        LoadResult Load();
        CommitResult Commit(long expectedGeneration, RootEnvelope candidate, OperationReceipt receipt, IReadOnlyList<LogAppend> appends);
        OperationReceipt FindReceipt(string operationId);
        OperationReceipt FindSemanticReceipt(string runId, string semanticKey);
        IReadOnlyList<LogAppend> ReadLog(string streamId, long skip, int count);
    }
}
