using System;
using System.Security.Cryptography;
using System.Text;

namespace FitnessSimulator.Game.Core
{
    public static class ContentHash
    {
        public static string Sha256(string text) { return Sha256(Encoding.UTF8.GetBytes(text ?? string.Empty)); }
        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    // All delegates receive an isolated candidate. They must not mutate external live state.
    public sealed class SerialCommandDispatcher
    {
        private readonly object gate = new object();
        private readonly ICheckpointStore store;
        private readonly IStateCodec codec;
        private readonly Func<CommandEnvelope, bool> authorize;
        private readonly Action<RootEnvelope, PreparedOutcome> checkEndings;
        private RootEnvelope current;
        private bool uncertain;
        private bool executing;

        public SerialCommandDispatcher(ICheckpointStore store, IStateCodec codec, RootEnvelope initialRoot,
            Func<CommandEnvelope, bool> authorize, Action<RootEnvelope, PreparedOutcome> checkEndings)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
            this.checkEndings = checkEndings;
            current = Clone(initialRoot ?? throw new ArgumentNullException(nameof(initialRoot)));
        }

        public RootEnvelope Snapshot { get { lock (gate) return Clone(current); } }
        public bool IsStorageUncertain { get { lock (gate) return uncertain; } }

        public LoadResult Recover()
        {
            lock (gate)
            {
                var result = store.Load();
                if (result.Status == LoadStatus.Loaded) { current = Clone(result.Root); uncertain = false; }
                // Empty is only safe before the first successful commit.
                else if (result.Status == LoadStatus.Empty && current.Generation == 0) uncertain = false;
                return result;
            }
        }

        public CommandResult Execute(CommandEnvelope command, Func<RootEnvelope, PreparedOutcome> prepare)
        { return Execute(command, null, prepare); }

        // The application derives a stable key from authenticated command identity, never current opportunities.
        public CommandResult Execute(CommandEnvelope command, Func<CommandEnvelope, string> resolveSemanticKey,
            Func<RootEnvelope, PreparedOutcome> prepare)
        {
            lock (gate)
            {
                if (executing) return Result(CommandStatus.Rejected, "ReentrantCommand");
                if (uncertain) return Result(CommandStatus.StorageOutcomeUnknown, "RecoverBeforeRetry");
                if (command == null || prepare == null || string.IsNullOrWhiteSpace(command.OperationId) ||
                    string.IsNullOrWhiteSpace(command.ActionKind) || command.RunId != current.RunId || !authorize(command))
                    return Result(CommandStatus.Rejected, "InvalidIdentityOrCapability");
                executing = true;
                try
                {
                    string hash = Fingerprint(command);
                    var old = store.FindReceipt(command.OperationId);
                    if (old != null) return old.PayloadHash == hash
                        ? Result(CommandStatus.AlreadyCommitted, null, old) : Result(CommandStatus.Rejected, "OperationConflict");
                    string resolvedKey = resolveSemanticKey == null ? null : resolveSemanticKey(command);
                    if (resolveSemanticKey != null)
                    {
                        if (string.IsNullOrWhiteSpace(resolvedKey)) throw new InvalidOperationException("Missing resolved semantic key.");
                        var replay = store.FindSemanticReceipt(current.RunId, resolvedKey);
                        if (replay != null) return replay.PayloadHash == hash
                            ? Result(CommandStatus.AlreadyCommitted, null, replay) : Result(CommandStatus.Rejected, "SemanticConflict");
                    }
                    if (command.ExpectedRevision != current.Revision) return Result(CommandStatus.Rejected, "StaleRevision");
                    var candidate = Clone(current);
                    var outcome = prepare(candidate) ?? throw new InvalidOperationException("Missing prepared outcome.");
                    outcome.Validate(command.Source);
                    if (resolveSemanticKey != null && resolvedKey != outcome.SemanticKey)
                        throw new InvalidOperationException("Prepared semantic key differs from authenticated key.");
                    var duplicate = store.FindSemanticReceipt(current.RunId, outcome.SemanticKey);
                    if (duplicate != null) return duplicate.PayloadHash == hash
                        ? Result(CommandStatus.AlreadyCommitted, null, duplicate) : Result(CommandStatus.Rejected, "SemanticConflict");
                    if (candidate.RunId != current.RunId || candidate.Generation != current.Generation || candidate.Revision != current.Revision)
                        throw new InvalidOperationException("Prepare cannot change identity or commit counters.");
                    if (outcome.CheckKind == CheckKind.NormalCompletion)
                    {
                        if (checkEndings == null) throw new InvalidOperationException("Normal completion requires an ending checker.");
                        checkEndings(candidate, outcome);
                    }
                    var receipt = new OperationReceipt {
                        OperationId = command.OperationId, PayloadHash = hash, RunId = command.RunId,
                        SemanticKey = outcome.SemanticKey, Result = outcome.Result,
                        CommitId = command.OperationId, Revision = checked(current.Revision + 1)
                    };
                    var saved = store.Commit(current.Generation, candidate, receipt, outcome.Appends);
                    if (saved.Status == CommitStatus.Committed)
                    {
                        current = Clone(saved.Root);
                        return Result(CommandStatus.Committed, null, saved.Receipt);
                    }
                    if (saved.Status == CommitStatus.Unknown) uncertain = true;
                    return Result(saved.Status == CommitStatus.Unknown ? CommandStatus.StorageOutcomeUnknown :
                        saved.Status == CommitStatus.Rejected ? CommandStatus.Rejected : CommandStatus.StorageUnavailable, saved.Error);
                }
                catch (System.IO.IOException ex) { uncertain = true; return Result(CommandStatus.StorageOutcomeUnknown, ex.Message); }
                catch (InvalidOperationException ex) { return Result(CommandStatus.ConfigError, ex.Message); }
                catch (ArgumentException ex) { return Result(CommandStatus.ConfigError, ex.Message); }
                finally { executing = false; }
            }
        }

        // Caller supplies a fresh initialized run, never a mutated live root. Permanent state is inherited here.
        public CommandResult BeginNewRun(CommandEnvelope command, RootEnvelope initializedRun)
        {
            lock (gate)
            {
                if (executing) return Result(CommandStatus.Rejected, "ReentrantCommand");
                if (uncertain) return Result(CommandStatus.StorageOutcomeUnknown, "RecoverBeforeRetry");
                if (command == null || initializedRun == null || string.IsNullOrWhiteSpace(command.OperationId) ||
                    command.ActionKind != "BeginNewRun" || !authorize(command)) return Result(CommandStatus.Rejected, "InvalidIdentityOrCapability");
                executing = true;
                try
                {
                    string hash = ContentHash.Sha256(Fingerprint(command) + codec.Encode(initializedRun));
                    var old = store.FindReceipt(command.OperationId);
                    if (old != null) return old.PayloadHash == hash ? Result(CommandStatus.AlreadyCommitted, null, old) : Result(CommandStatus.Rejected, "OperationConflict");
                    if (command.RunId != current.RunId || command.ExpectedRevision != current.Revision)
                        return Result(CommandStatus.Rejected, "StaleRevisionOrRun");
                    if (string.IsNullOrWhiteSpace(initializedRun.RunId) || initializedRun.RunId == current.RunId || initializedRun.Generation != 0 || initializedRun.Revision != 0 ||
                        initializedRun.LogPrefixes == null || initializedRun.LogPrefixes.Count != 0 || initializedRun.RecentReceipts == null || initializedRun.RecentReceipts.Count != 0 ||
                        initializedRun.PermanentFragments == null || initializedRun.PermanentFragments.Count != 0 || initializedRun.Fragments == null ||
                        initializedRun.Fragments.Exists(IsPermanentScope)) return Result(CommandStatus.Rejected, "InitializerMustContainOnlyFreshRunState");
                    var candidate = Clone(current);
                    var fresh = Clone(initializedRun);
                    candidate.RunId = fresh.RunId; candidate.ContentVersion = fresh.ContentVersion;
                    candidate.Fragments.RemoveAll(f => !IsPermanentScope(f));
                    candidate.Fragments.AddRange(fresh.Fragments);
                    var receipt = new OperationReceipt { OperationId = command.OperationId, PayloadHash = hash, RunId = candidate.RunId,
                        SemanticKey = "new-run:" + candidate.RunId, Result = "NewRunStarted", CommitId = command.OperationId, Revision = current.Revision + 1 };
                    var saved = store.Commit(current.Generation, candidate, receipt, Array.Empty<LogAppend>());
                    if (saved.Status == CommitStatus.Committed) { current = Clone(saved.Root); return Result(CommandStatus.Committed, null, saved.Receipt); }
                    if (saved.Status == CommitStatus.Unknown) uncertain = true;
                    return Result(saved.Status == CommitStatus.Unknown ? CommandStatus.StorageOutcomeUnknown :
                        saved.Status == CommitStatus.Rejected ? CommandStatus.Rejected : CommandStatus.StorageUnavailable, saved.Error);
                }
                catch (System.IO.IOException ex) { uncertain = true; return Result(CommandStatus.StorageOutcomeUnknown, ex.Message); }
                catch (ArgumentException ex) { return Result(CommandStatus.ConfigError, ex.Message); }
                finally { executing = false; }
            }
        }

        private static bool IsPermanentScope(VersionedFragment fragment)
        { return fragment != null && (fragment.Scope == "permanent" || fragment.Scope != null && fragment.Scope.StartsWith("permanent/", StringComparison.Ordinal)); }

        public static string Fingerprint(CommandEnvelope command)
        {
            // Length prefixes prevent delimiter collisions. Revision is intentionally excluded for retries.
            var builder = new StringBuilder();
            foreach (string value in new[] { command.RunId, command.ActorId, command.ResourceScope, command.EventInstanceId,
                command.SetId, command.ActionKind, command.Payload, command.Source.ToString(), command.ChoiceToken })
            {
                string item = value ?? string.Empty;
                builder.Append(item.Length).Append(':').Append(item);
            }
            return ContentHash.Sha256(builder.ToString());
        }

        private RootEnvelope Clone(RootEnvelope value) { return value?.DetachedCopy(); }
        private CommandResult Result(CommandStatus status, string error, OperationReceipt receipt = null)
        { return new CommandResult { Status = status, Error = error, Receipt = receipt, CurrentRevision = current.Revision }; }
    }
}
