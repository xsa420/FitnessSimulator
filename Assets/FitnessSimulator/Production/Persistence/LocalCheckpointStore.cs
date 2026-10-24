using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FitnessSimulator.Game.Core;

namespace FitnessSimulator.Game.Persistence
{
    public enum StorageFaultPoint { BeforeLogAppend, AfterLogFlush, BeforeRootWrite, AfterRootFlush, BeforeCommitReceipt, DuringCommitReceipt, AfterCommitReceipt, BeforeAcknowledgement }
    public enum StoreResetStatus { Reset, Refused, Unknown }
    public enum StoreResetFaultPoint { BeforeMove, AfterMove, BeforeInitialize, AfterInitialize }
    public sealed class StoreResetResult
    {
        public StoreResetStatus Status;
        public string BackupPath;
        public string Error;
    }

    [Serializable]
    internal sealed class CommitRecord
    {
        public long Generation;
        public int Slot;
        public string RootHash;
        public string PreviousHash;
        public string OperationId;
        public bool PermanentStateKnownEmpty;
    }

    [Serializable]
    internal sealed class DataFrame
    {
        public string Kind;
        public string Json;
        public string PreviousHash;
        public long Sequence;
    }

    [Serializable]
    internal sealed class ArchivedRunManifest
    {
        public string RunId;
        public List<LogPrefix> Prefixes;
    }

    [Serializable]
    internal sealed class GenesisRecord
    {
        public int Version = 1;
        public string StoreId;
        public bool InitiallyEmpty = true;
    }

    // One process owns the directory for this object's lifetime. No domain calls this directly.
    public sealed class LocalCheckpointStore : ICheckpointStore
    {
        private const int Magic = 0x46534d32;
        private const int Trailer = 0x324d5346;
        private const int MaximumFrameBytes = 16 * 1024 * 1024;
        private const int SegmentBytes = 1024 * 1024;
        private const string ReceiptKind = "operation-receipt";
        private readonly string directory;
        private readonly IStateCodec codec;
        private readonly Action<StorageFaultPoint> inject;
        private readonly string expectedContentVersion;
        private readonly Action<StoreResetFaultPoint> resetInject;
        private readonly FileStream writerLock;
        private RootEnvelope current;
        private long ledgerLength;
        private string ledgerHash = string.Empty;
        private bool loaded;
        private bool blocked;
        private bool disposed;
        private CommitRecord lastVerifiedCommit;
        private bool committedDataCorrupt;
        private sealed class ReceiptLocation
        {
            public int Segment;
            public long Start, End;
            public string FrameHash, OperationId, SemanticKey;
        }
        // Derived only from a fully verified committed prefix; never persisted as authority.
        private sealed class ReceiptIndex
        {
            public string RunId, PrefixHash;
            public long Generation, Offset, Count;
            public int Segment;
            public readonly Dictionary<string, ReceiptLocation> Operations = new Dictionary<string, ReceiptLocation>(StringComparer.Ordinal);
            public readonly Dictionary<string, ReceiptLocation> Semantics = new Dictionary<string, ReceiptLocation>(StringComparer.Ordinal);
        }
        private ReceiptIndex receiptIndex;

        public LocalCheckpointStore(string directory, IStateCodec codec, Action<StorageFaultPoint> inject = null, string expectedContentVersion = null,
            Action<StoreResetFaultPoint> resetInject = null)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory required.");
            string fullPath = Path.GetFullPath(directory);
            if (fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) == Path.GetPathRoot(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                throw new ArgumentException("A dedicated storage subdirectory is required.");
            this.directory = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            this.inject = inject;
            this.expectedContentVersion = expectedContentVersion;
            this.resetInject = resetInject;
            Directory.CreateDirectory(this.directory);
            // Stable sibling lease survives moving the complete data directory during an explicitly confirmed reset.
            writerLock = new FileStream(this.directory + ".writer-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                // The proof is created only for a truly empty, exclusively leased directory.
                if (Directory.GetFileSystemEntries(this.directory).Length == 0)
                {
                    using (var stream = new FileStream(Path.Combine(this.directory, "genesis.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { WriteFrame(stream, codec.Encode(new GenesisRecord { StoreId = Guid.NewGuid().ToString("N") })); stream.Flush(true); }
                }
            }
            catch { writerLock.Dispose(); throw; }
        }

        public LoadResult Load()
        {
            ThrowIfDisposed();
            current = null; loaded = false; blocked = false; ledgerLength = 0; ledgerHash = string.Empty;
            receiptIndex = null;
            lastVerifiedCommit = null; committedDataCorrupt = false;
            try
            {
                string ledger = Path.Combine(directory, "commit-ledger.bin");
                CommitRecord last = null;
                if (File.Exists(ledger))
                {
                    using (var stream = File.OpenRead(ledger))
                    {
                        while (stream.Position < stream.Length)
                        {
                            string json;
                            if (!ReadFrame(stream, stream.Length, true, out json)) break;
                            var record = codec.Decode<CommitRecord>(json);
                            if (record == null || record.Generation != (last == null ? 1 : last.Generation + 1) ||
                                record.PreviousHash != ledgerHash || record.Slot != (record.Generation % 2))
                                throw new InvalidDataException("Invalid commit chain.");
                            ledgerHash = ContentHash.Sha256(json); ledgerLength = stream.Position; last = record;
                        }
                    }
                }
                if (last == null)
                {
                    // With data but no valid authority we cannot prove this is a fresh install.
                    if (File.Exists(RootPath(0)) || File.Exists(RootPath(1)) || Directory.GetFiles(directory, "log-*.bin").Length > 0)
                        throw new InvalidDataException("Data exists without a valid commit receipt; refusing empty-state fallback.");
                    if (!HasUntouchedGenesisProof()) throw new InvalidDataException("Empty-store provenance is not verifiable.");
                    loaded = true;
                    return new LoadResult { Status = LoadStatus.Empty };
                }
                lastVerifiedCommit = last;
                string rootJson;
                using (var stream = File.OpenRead(RootPath(last.Slot)))
                {
                    if (!ReadFrame(stream, stream.Length, false, out rootJson) || stream.Position != stream.Length || ContentHash.Sha256(rootJson) != last.RootHash)
                        throw new InvalidDataException("Committed root hash mismatch.");
                }
                var root = codec.Decode<RootEnvelope>(rootJson);
                if (root == null) throw new InvalidDataException("Missing root.");
                if (root.SchemaVersion != 2) { blocked = true; return new LoadResult { Status = LoadStatus.IncompatibleSchema, Error = "Unsupported root schema." }; }
                if (expectedContentVersion != null && root.ContentVersion != expectedContentVersion)
                { blocked = true; return new LoadResult { Status = LoadStatus.MissingContent, Error = "Required content version: " + root.ContentVersion }; }
                ValidateRoot(root);
                if (root.Generation != last.Generation || root.LastCommitId != last.OperationId)
                    throw new InvalidDataException("Root/receipt identity mismatch.");
                var verifiedIndex = new ReceiptIndex();
                foreach (var prefix in root.LogPrefixes)
                {
                    Action<DataFrame, ReceiptLocation> indexFrame = null;
                    if (prefix.StreamId == ReceiptStream(root.RunId)) indexFrame = (frame, location) => {
                        if (frame.Kind != ReceiptKind) throw new InvalidDataException("Unexpected receipt stream entry.");
                        IndexReceipt(verifiedIndex, codec.Decode<OperationReceipt>(frame.Json), root, location);
                    };
                    foreach (var frame in ReadData(prefix, indexFrame)) { }
                }
                // ReadData must be exhausted, including its final prefix hash check, before publication.
                StampIndex(verifiedIndex, root);
                current = root; receiptIndex = verifiedIndex; loaded = true;
                return new LoadResult { Status = LoadStatus.Loaded, Root = Clone(root) };
            }
            catch (InvalidDataException ex) { blocked = true; committedDataCorrupt = true; return new LoadResult { Status = LoadStatus.CorruptCommitted, Error = ex.Message }; }
            catch (FileNotFoundException ex) { blocked = true; committedDataCorrupt = true; return new LoadResult { Status = LoadStatus.CorruptCommitted, Error = ex.Message }; }
            catch (IOException ex) { blocked = true; return new LoadResult { Status = LoadStatus.IoUnavailable, Error = ex.Message }; }
            catch (UnauthorizedAccessException ex) { blocked = true; return new LoadResult { Status = LoadStatus.IoUnavailable, Error = ex.Message }; }
            catch (ArgumentException ex) { blocked = true; committedDataCorrupt = true; return new LoadResult { Status = LoadStatus.CorruptCommitted, Error = ex.Message }; }
        }

        public bool CanResetCorruptM1Store(out string reason)
        {
            ThrowIfDisposed();
            var cachedRoot = current;
            bool cachedLoaded = loaded, cachedBlocked = blocked, cachedCorrupt = committedDataCorrupt;
            long cachedLength = ledgerLength;
            string cachedHash = ledgerHash;
            var cachedCommit = lastVerifiedCommit;
            var cachedIndex = receiptIndex;
            try { return EvaluateResetEligibility(out reason); }
            finally
            {
                current = cachedRoot; loaded = cachedLoaded; blocked = cachedBlocked; committedDataCorrupt = cachedCorrupt;
                ledgerLength = cachedLength; ledgerHash = cachedHash; lastVerifiedCommit = cachedCommit;
                receiptIndex = cachedIndex;
            }
        }

        private bool EvaluateResetEligibility(out string reason)
        {
            var load = Load(); // Read-only filesystem verification; never repairs/truncates logs.
            bool genesisProvesEmpty = HasUntouchedGenesisProof();
            if ((!committedDataCorrupt && load.Status != LoadStatus.Empty) || !genesisProvesEmpty ||
                lastVerifiedCommit != null && !lastVerifiedCommit.PermanentStateKnownEmpty)
            {
                reason = "Cannot prove permanent state empty; preserve and refuse reset.";
                return false;
            }
            reason = "Verified empty permanent state; explicit confirmation will replace the current run and preserve its directory.";
            return true;
        }

        public StoreResetResult ResetCorruptM1Store(bool userConfirmed)
        {
            ThrowIfDisposed();
            if (!userConfirmed) return new StoreResetResult { Status = StoreResetStatus.Refused, Error = "Explicit user confirmation required." };
            string reason;
            if (!EvaluateResetEligibility(out reason)) // Confirmation rechecks; a prior query never grants a reusable permit.
                return new StoreResetResult { Status = StoreResetStatus.Refused, Error = reason };
            string backup = directory + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
            try
            {
                resetInject?.Invoke(StoreResetFaultPoint.BeforeMove);
                Directory.Move(directory, backup); // Same-volume rename; no deletion or overwrite.
                resetInject?.Invoke(StoreResetFaultPoint.AfterMove);
                Directory.CreateDirectory(directory);
                resetInject?.Invoke(StoreResetFaultPoint.BeforeInitialize);
                using (var stream = new FileStream(Path.Combine(directory, "genesis.bin"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { WriteFrame(stream, codec.Encode(new GenesisRecord { StoreId = Guid.NewGuid().ToString("N") })); stream.Flush(true); }
                resetInject?.Invoke(StoreResetFaultPoint.AfterInitialize);
                current = null; loaded = true; blocked = false; ledgerLength = 0; ledgerHash = string.Empty;
                receiptIndex = null;
                lastVerifiedCommit = null; committedDataCorrupt = false;
                return new StoreResetResult { Status = StoreResetStatus.Reset, BackupPath = backup };
            }
            catch (IOException ex) { receiptIndex = null; blocked = true; return new StoreResetResult { Status = StoreResetStatus.Unknown, BackupPath = backup, Error = ex.Message }; }
            catch (UnauthorizedAccessException ex) { receiptIndex = null; blocked = true; return new StoreResetResult { Status = StoreResetStatus.Unknown, BackupPath = backup, Error = ex.Message }; }
        }

        public IReadOnlyList<string> GetPreservedBackupPaths()
        {
            ThrowIfDisposed();
            string parent = Path.GetDirectoryName(directory);
            return Directory.GetDirectories(parent, Path.GetFileName(directory) + ".corrupt-*").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        public CommitResult Commit(long expectedGeneration, RootEnvelope candidate, OperationReceipt receipt, IReadOnlyList<LogAppend> appends)
        {
            using (RuntimeTiming.Measure("store.commit")) return CommitCore(expectedGeneration, candidate, receipt, appends);
        }
        private CommitResult CommitCore(long expectedGeneration, RootEnvelope candidate, OperationReceipt receipt, IReadOnlyList<LogAppend> appends)
        {
            ThrowIfDisposed();
            EnsureLoaded();
            if (blocked) return Failure(CommitStatus.Unavailable, "Storage requires a valid load.");
            if (candidate == null || receipt == null || expectedGeneration != (current?.Generation ?? 0)) return Failure(CommitStatus.Rejected, "Stale generation or missing payload.");
            var prior = FindReceipt(receipt.OperationId);
            if (prior != null) return prior.PayloadHash == receipt.PayloadHash
                ? new CommitResult { Status = CommitStatus.Committed, Root = Clone(current), Receipt = prior }
                : Failure(CommitStatus.Rejected, "OperationConflict");
            bool writing = false;
            try
            {
                var root = Clone(candidate);
                ValidateRoot(root);
                if (expectedContentVersion != null && root.ContentVersion != expectedContentVersion) return Failure(CommitStatus.Rejected, "Content version mismatch.");
                if (root.Generation != expectedGeneration || root.Revision != (current?.Revision ?? 0)) return Failure(CommitStatus.Rejected, "Candidate counters changed.");
                // Visibility is storage-owned; candidates cannot expose arbitrary log bytes.
                if (!SamePrefixes(root.LogPrefixes, current?.LogPrefixes ?? new List<LogPrefix>())) return Failure(CommitStatus.Rejected, "Caller changed log visibility.");
                if (string.IsNullOrWhiteSpace(receipt.OperationId) || string.IsNullOrWhiteSpace(receipt.SemanticKey)
                    || string.IsNullOrWhiteSpace(receipt.PayloadHash) || receipt.RunId != root.RunId)
                    return Failure(CommitStatus.Rejected, "Invalid receipt.");
                if (FindSemanticReceipt(receipt.RunId, receipt.SemanticKey) != null) return Failure(CommitStatus.Rejected, "SemanticAlreadyCommitted");
                root.Generation = checked(expectedGeneration + 1); root.Revision = checked((current?.Revision ?? 0) + 1);
                root.LastCommitId = receipt.OperationId;
                root.RecentReceipts = current == null || current.RunId != root.RunId
                    ? new List<OperationReceipt>() : current.RecentReceipts.Select(CopyReceipt).ToList();
                var savedReceipt = codec.Decode<OperationReceipt>(codec.Encode(receipt));
                savedReceipt.Revision = root.Revision; savedReceipt.CommitId = root.LastCommitId;
                var additions = new List<LogAppend>(appends ?? Array.Empty<LogAppend>());
                if (additions.Any(a => a == null || a.Kind == ReceiptKind || a.StreamId == ReceiptStream(root.RunId)))
                    return Failure(CommitStatus.Rejected, "Reserved receipt stream or malformed append.");
                if (current != null && current.RunId != root.RunId)
                {
                    var archivePrefix = current.LogPrefixes.Find(p => p.StreamId == "permanent/archive");
                    if (archivePrefix != null && ReadData(archivePrefix).Any(f => codec.Decode<ArchivedRunManifest>(f.Json).RunId == root.RunId))
                        return Failure(CommitStatus.Rejected, "RunIdAlreadyUsed");
                    var archived = root.LogPrefixes.Where(p => p.StreamId.StartsWith("run/" + current.RunId + "/", StringComparison.Ordinal)).ToList();
                    additions.Insert(0, new LogAppend { StreamId = "permanent/archive", Kind = "archived-run",
                        Json = codec.Encode(new ArchivedRunManifest { RunId = current.RunId, Prefixes = archived }) });
                    root.LogPrefixes.RemoveAll(p => archived.Contains(p));
                    root.RecentReceipts.Clear();
                }
                additions.Add(new LogAppend { StreamId = ReceiptStream(root.RunId), Kind = ReceiptKind, Json = codec.Encode(savedReceipt) });
                if (additions.Any(a => !AllowedStream(a.StreamId, root.RunId))) return Failure(CommitStatus.Rejected, "Unsupported stream kind.");
                writing = true;
                if (root.PermanentFragments.Count > 0 || root.Fragments.Any(f => f.Scope == "permanent" || f.Scope.StartsWith("permanent/", StringComparison.Ordinal)) ||
                    additions.Any(a => a.StreamId.StartsWith("permanent/", StringComparison.Ordinal) && a.StreamId != "permanent/archive"))
                    MarkPermanentTouched();
                Hit(StorageFaultPoint.BeforeLogAppend);
                ReceiptLocation appendedReceipt = null;
                using (RuntimeTiming.Measure("store.append-flush")) foreach (var append in additions)
                {
                    var location = Append(root, append);
                    if (append.Kind == ReceiptKind) appendedReceipt = location;
                }
                Hit(StorageFaultPoint.AfterLogFlush);
                root.RecentReceipts.Add(savedReceipt);
                if (root.RecentReceipts.Count > 64) root.RecentReceipts.RemoveRange(0, root.RecentReceipts.Count - 64);
                Hit(StorageFaultPoint.BeforeRootWrite);
                string json = codec.Encode(root);
                using (RuntimeTiming.Measure("store.root-write-flush"))
                using (var stream = new FileStream(RootPath((int)(root.Generation % 2)), FileMode.Create, FileAccess.Write, FileShare.None))
                { WriteFrame(stream, json); stream.Flush(true); }
                using (RuntimeTiming.Measure("store.root-verify"))
                using (var stream = File.OpenRead(RootPath((int)(root.Generation % 2))))
                {
                    string verify;
                    if (!ReadFrame(stream, stream.Length, false, out verify) || verify != json) throw new InvalidDataException("Root reread failed.");
                }
                Hit(StorageFaultPoint.AfterRootFlush);
                var record = new CommitRecord { Generation = root.Generation, Slot = (int)(root.Generation % 2), RootHash = ContentHash.Sha256(json),
                    PreviousHash = ledgerHash, OperationId = root.LastCommitId,
                    PermanentStateKnownEmpty = root.PermanentFragments.Count == 0 &&
                        !root.Fragments.Any(f => f.Scope == "permanent" || f.Scope.StartsWith("permanent/", StringComparison.Ordinal)) &&
                        !root.LogPrefixes.Any(p => p.StreamId.StartsWith("permanent/", StringComparison.Ordinal) && p.StreamId != "permanent/archive") };
                string recordJson = codec.Encode(record);
                Hit(StorageFaultPoint.BeforeCommitReceipt);
                using (RuntimeTiming.Measure("store.ledger-flush"))
                using (var stream = new FileStream(Path.Combine(directory, "commit-ledger.bin"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.SetLength(ledgerLength); stream.Position = ledgerLength;
                    byte[] encoded = EncodeFrame(recordJson);
                    int half = encoded.Length / 2;
                    stream.Write(encoded, 0, half); stream.Flush(true);
                    Hit(StorageFaultPoint.DuringCommitReceipt);
                    stream.Write(encoded, half, encoded.Length - half); stream.Flush(true);
                    ledgerLength = stream.Position;
                }
                ledgerHash = ContentHash.Sha256(recordJson); current = root;
                Hit(StorageFaultPoint.AfterCommitReceipt);
                Hit(StorageFaultPoint.BeforeAcknowledgement);
                var nextIndex = receiptIndex != null && receiptIndex.RunId == root.RunId ? receiptIndex : new ReceiptIndex();
                IndexReceipt(nextIndex, savedReceipt, root, appendedReceipt);
                StampIndex(nextIndex, root); receiptIndex = nextIndex;
                // The exact encoded root was flushed and reread above; the returned records remain isolated.
                return new CommitResult { Status = CommitStatus.Committed, Root = root.DetachedCopy(), Receipt = CopyReceipt(savedReceipt) };
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            {
                if (writing) { receiptIndex = null; loaded = false; blocked = true; }
                return Failure(writing ? CommitStatus.Unknown : CommitStatus.Rejected, ex.Message);
            }
        }

        public OperationReceipt FindReceipt(string operationId)
        {
            EnsureLoaded(); EnsureReadable();
            if (current == null) return null;
            if (operationId == null) return null;
            EnsureReceiptIndex();
            ReceiptLocation found;
            return receiptIndex.Operations.TryGetValue(operationId, out found) ? ReadIndexedReceipt(found) : null;
        }

        public OperationReceipt FindSemanticReceipt(string runId, string semanticKey)
        {
            EnsureLoaded(); EnsureReadable();
            if (current == null || current.RunId != runId) return null;
            if (semanticKey == null) return null;
            EnsureReceiptIndex();
            ReceiptLocation found;
            return receiptIndex.Semantics.TryGetValue(semanticKey, out found) ? ReadIndexedReceipt(found) : null;
        }

        public IReadOnlyList<LogAppend> ReadLog(string streamId, long skip, int count)
        {
            EnsureLoaded(); EnsureReadable();
            if (skip < 0 || count < 0 || count > 64) throw new ArgumentOutOfRangeException(nameof(count));
            var prefix = current?.LogPrefixes.Find(p => p.StreamId == streamId);
            if (prefix == null || count == 0) return Array.Empty<LogAppend>();
            return ReadData(prefix).Skip(checked((int)skip)).Take(count)
                .Select(f => new LogAppend { StreamId = streamId, Kind = f.Kind, Json = f.Json }).ToArray();
        }

        private OperationReceipt CopyReceipt(OperationReceipt value)
        { return codec.Decode<OperationReceipt>(codec.Encode(value)); }

        private static void IndexReceipt(ReceiptIndex index, OperationReceipt value, RootEnvelope root, ReceiptLocation location)
        {
            if (value == null || value.RunId != root.RunId || string.IsNullOrWhiteSpace(value.OperationId)
                || string.IsNullOrWhiteSpace(value.SemanticKey) || string.IsNullOrWhiteSpace(value.PayloadHash)
                || value.CommitId != value.OperationId || value.Revision < 1 || value.Revision > root.Revision
                || location == null || location.Start < 0 || location.End <= location.Start || string.IsNullOrEmpty(location.FrameHash)
                || index.Operations.ContainsKey(value.OperationId) || index.Semantics.ContainsKey(value.SemanticKey))
                throw new InvalidDataException("Invalid or duplicate committed receipt.");
            // Retain keys and locator only. Result/PayloadHash and the full receipt are not kept in this index.
            location.OperationId = value.OperationId; location.SemanticKey = value.SemanticKey;
            index.Operations.Add(value.OperationId, location); index.Semantics.Add(value.SemanticKey, location);
        }

        private OperationReceipt ReadIndexedReceipt(ReceiptLocation location)
        {
            // Reuse the existing bounded root cache; all cold payloads are read from their anchored frame.
            var hot = current.RecentReceipts.Find(r => r.OperationId == location.OperationId && r.SemanticKey == location.SemanticKey);
            if (hot != null) return CopyReceipt(hot);
            try
            {
                string json;
                using (var stream = File.OpenRead(LogPath(ReceiptStream(current.RunId), location.Segment)))
                {
                    if (location.End > stream.Length) throw new InvalidDataException("Indexed receipt is truncated.");
                    stream.Position = location.Start;
                    if (!ReadFrame(stream, location.End, false, out json) || stream.Position != location.End
                        || ContentHash.Sha256(json) != location.FrameHash)
                        throw new InvalidDataException("Indexed receipt hash mismatch.");
                }
                var frame = codec.Decode<DataFrame>(json);
                if (frame == null || frame.Kind != ReceiptKind) throw new InvalidDataException("Invalid indexed receipt frame.");
                var receipt = codec.Decode<OperationReceipt>(frame.Json);
                if (receipt == null || receipt.RunId != current.RunId || receipt.OperationId != location.OperationId || receipt.SemanticKey != location.SemanticKey)
                    throw new InvalidDataException("Indexed receipt identity mismatch.");
                return receipt;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                receiptIndex = null; loaded = false; blocked = true;
                throw new IOException("Committed receipt is unavailable; reload required.", ex);
            }
        }

        private static void StampIndex(ReceiptIndex index, RootEnvelope root)
        {
            var prefix = root.LogPrefixes.Find(p => p.StreamId == ReceiptStream(root.RunId));
            if (index.Operations.Count != (prefix == null ? 0 : prefix.FrameCount) || index.Semantics.Count != index.Operations.Count)
                throw new InvalidDataException("Receipt index does not cover the complete committed prefix.");
            index.RunId = root.RunId; index.Generation = root.Generation;
            index.Segment = prefix == null ? -1 : prefix.Segment;
            index.Offset = prefix == null ? 0 : prefix.Offset;
            index.Count = prefix == null ? 0 : prefix.FrameCount;
            index.PrefixHash = prefix == null ? null : prefix.Hash;
        }

        private void EnsureReceiptIndex()
        {
            var prefix = current.LogPrefixes.Find(p => p.StreamId == ReceiptStream(current.RunId));
            if (receiptIndex == null || receiptIndex.RunId != current.RunId || receiptIndex.Generation != current.Generation
                || receiptIndex.Segment != (prefix == null ? -1 : prefix.Segment)
                || receiptIndex.Offset != (prefix == null ? 0 : prefix.Offset)
                || receiptIndex.Count != (prefix == null ? 0 : prefix.FrameCount)
                || receiptIndex.PrefixHash != (prefix == null ? null : prefix.Hash))
                throw new IOException("Receipt index is not bound to the verified state; reload required.");
        }

        private ReceiptLocation Append(RootEnvelope root, LogAppend append)
        {
            if (string.IsNullOrWhiteSpace(append.StreamId) || string.IsNullOrWhiteSpace(append.Kind) || append.Json == null)
                throw new ArgumentException("Log append requires stream/kind/payload.");
            var prefix = root.LogPrefixes.Find(p => p.StreamId == append.StreamId);
            if (prefix == null) { prefix = new LogPrefix { StreamId = append.StreamId, Hash = string.Empty }; root.LogPrefixes.Add(prefix); }
            var data = new DataFrame { Kind = append.Kind, Json = append.Json, PreviousHash = prefix.Hash, Sequence = prefix.FrameCount + 1 };
            string json = codec.Encode(data);
            byte[] bytes = EncodeFrame(json);
            if (prefix.Offset > 0 && prefix.Offset + bytes.Length > SegmentBytes)
            {
                // Remove any uncommitted tail before this segment becomes an entirely visible old segment.
                using (var previous = new FileStream(LogPath(prefix.StreamId, prefix.Segment), FileMode.Open, FileAccess.Write, FileShare.None))
                { previous.SetLength(prefix.Offset); previous.Flush(true); }
                prefix.Segment++; prefix.Offset = 0;
            }
            long start = prefix.Offset;
            using (var stream = new FileStream(LogPath(prefix.StreamId, prefix.Segment), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                stream.SetLength(prefix.Offset); stream.Position = prefix.Offset;
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true); prefix.Offset = stream.Position;
            }
            prefix.FrameCount++; prefix.Hash = ContentHash.Sha256(json);
            return new ReceiptLocation { Segment = prefix.Segment, Start = start, End = prefix.Offset, FrameHash = prefix.Hash };
        }

        private IEnumerable<DataFrame> ReadData(LogPrefix prefix, Action<DataFrame, ReceiptLocation> onFrame = null)
        {
            long sequence = 0; string previous = string.Empty;
            for (int segment = 0; segment <= prefix.Segment; segment++)
            {
                using (var stream = File.OpenRead(LogPath(prefix.StreamId, segment)))
                {
                    long limit = segment == prefix.Segment ? prefix.Offset : stream.Length;
                    if (limit > stream.Length || limit <= 0) throw new InvalidDataException("Committed log prefix missing.");
                    while (stream.Position < limit)
                    {
                        long start = stream.Position;
                        string json;
                        if (!ReadFrame(stream, limit, false, out json)) throw new InvalidDataException("Incomplete committed log.");
                        var data = codec.Decode<DataFrame>(json);
                        if (data == null || data.PreviousHash != previous || data.Sequence != ++sequence)
                            throw new InvalidDataException("Invalid data chain.");
                        previous = ContentHash.Sha256(json);
                        if (onFrame != null) onFrame(data, new ReceiptLocation { Segment = segment, Start = start, End = stream.Position, FrameHash = previous });
                        yield return data;
                    }
                }
            }
            if (sequence != prefix.FrameCount || previous != prefix.Hash) throw new InvalidDataException("Log prefix hash mismatch.");
        }

        private static byte[] EncodeFrame(string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            if (payload.Length > MaximumFrameBytes) throw new ArgumentException("Frame exceeds size limit.");
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
            {
                writer.Write(Magic); writer.Write(payload.Length); writer.Write(payload);
                writer.Write(HexBytes(ContentHash.Sha256(payload))); writer.Write(Trailer); writer.Flush();
                return memory.ToArray();
            }
        }

        private static void WriteFrame(Stream stream, string json) { byte[] bytes = EncodeFrame(json); stream.Write(bytes, 0, bytes.Length); }
        private static bool ReadFrame(Stream stream, long limit, bool allowPartialTail, out string json)
        {
            json = null;
            if (limit - stream.Position < 8) { if (allowPartialTail) return false; throw new InvalidDataException("Incomplete frame header."); }
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Frame magic mismatch.");
                int length = reader.ReadInt32();
                if (length < 0 || length > MaximumFrameBytes) throw new InvalidDataException("Invalid frame length.");
                if (limit - stream.Position < length + 36L) { if (allowPartialTail) return false; throw new InvalidDataException("Incomplete frame body."); }
                byte[] payload = reader.ReadBytes(length); byte[] digest = reader.ReadBytes(32);
                if (reader.ReadInt32() != Trailer || !HexBytes(ContentHash.Sha256(payload)).SequenceEqual(digest)) throw new InvalidDataException("Frame checksum mismatch.");
                json = new UTF8Encoding(false, true).GetString(payload); return true;
            }
        }

        private static byte[] HexBytes(string text)
        {
            var bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return bytes;
        }
        private static bool SamePrefixes(List<LogPrefix> a, List<LogPrefix> b)
        {
            return a.Count == b.Count && a.All(x => b.Any(y => x.StreamId == y.StreamId && x.Segment == y.Segment && x.Offset == y.Offset && x.FrameCount == y.FrameCount && x.Hash == y.Hash));
        }
        private static void ValidateRoot(RootEnvelope root)
        {
            if (root.SchemaVersion != 2 || string.IsNullOrWhiteSpace(root.RunId) || string.IsNullOrWhiteSpace(root.ContentVersion) ||
                root.Generation < 0 || root.Revision < 0 || root.Fragments == null || root.PermanentFragments == null || root.LogPrefixes == null || root.RecentReceipts == null ||
                root.RecentReceipts.Count > 64 || root.RecentReceipts.Any(r => r == null || string.IsNullOrWhiteSpace(r.OperationId))) throw new InvalidDataException("Invalid root envelope.");
            if (root.Fragments.Any(f => f == null || string.IsNullOrWhiteSpace(f.Owner) || string.IsNullOrWhiteSpace(f.Scope) || f.SchemaVersion <= 0 || f.Json == null) ||
                root.Fragments.GroupBy(f => f.Owner + "\0" + f.Scope).Any(g => g.Count() > 1)) throw new InvalidDataException("Invalid or duplicate fragment.");
            if (root.PermanentFragments.Any(f => f == null || string.IsNullOrWhiteSpace(f.Owner) || string.IsNullOrWhiteSpace(f.Scope) || f.SchemaVersion <= 0 || f.Json == null) ||
                root.PermanentFragments.GroupBy(f => f.Owner + "\0" + f.Scope).Any(g => g.Count() > 1)) throw new InvalidDataException("Invalid permanent fragment.");
            if (root.LogPrefixes.Any(p => p == null || string.IsNullOrWhiteSpace(p.StreamId) || p.Segment < 0 || p.Offset <= 0 || p.FrameCount <= 0 || string.IsNullOrEmpty(p.Hash)) ||
                root.LogPrefixes.GroupBy(p => p.StreamId).Any(g => g.Count() > 1)) throw new InvalidDataException("Invalid log prefix.");
        }
        private static string ReceiptStream(string runId) { return "run/" + runId + "/receipts"; }
        private void MarkPermanentTouched()
        {
            string marker = Path.Combine(directory, "permanent-touched.bin");
            if (File.Exists(marker))
            {
                using (var stream = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    string text;
                    if (!ReadFrame(stream, stream.Length, false, out text) || stream.Position != stream.Length || text != "permanent-write-may-exist")
                        throw new InvalidDataException("Permanent-write marker is not verifiable.");
                    stream.Flush(true);
                }
                return;
            }
            using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { WriteFrame(stream, "permanent-write-may-exist"); stream.Flush(true); }
        }
        private bool HasUntouchedGenesisProof()
        {
            try
            {
                // Successful enumeration proves absence. File.Exists alone turns permission/I/O failures into false.
                if (Directory.GetFileSystemEntries(directory).Any(p => string.Equals(Path.GetFileName(p), "permanent-touched.bin", StringComparison.OrdinalIgnoreCase))) return false;
                using (var stream = File.OpenRead(Path.Combine(directory, "genesis.bin")))
                {
                    string json;
                    if (!ReadFrame(stream, stream.Length, false, out json) || stream.Position != stream.Length) return false;
                    var proof = codec.Decode<GenesisRecord>(json);
                    return proof != null && proof.Version == 1 && proof.InitiallyEmpty && !string.IsNullOrWhiteSpace(proof.StoreId);
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
        }
        private static bool AllowedStream(string id, string runId)
        {
            return id == ReceiptStream(runId) || id == "run/" + runId + "/records" || id == "run/" + runId + "/inputs" ||
                id == "permanent/archive" || id == "permanent/history" || id == "permanent/read";
        }
        private string RootPath(int slot) { return Path.Combine(directory, slot == 0 ? "state-a.bin" : "state-b.bin"); }
        private string LogPath(string id, int segment) { return Path.Combine(directory, "log-" + ContentHash.Sha256(id) + "-" + segment.ToString("D8") + ".bin"); }
        private void Hit(StorageFaultPoint point) { inject?.Invoke(point); }
        private RootEnvelope Clone(RootEnvelope root) { return root?.DetachedCopy(); }
        private void EnsureLoaded() { ThrowIfDisposed(); if (!loaded && !blocked) Load(); }
        private void EnsureReadable() { if (blocked || !loaded) throw new IOException("Storage state unavailable; call Load before proceeding."); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(LocalCheckpointStore)); }
        private static CommitResult Failure(CommitStatus status, string error) { return new CommitResult { Status = status, Error = error }; }
        public void Dispose() { if (disposed) return; writerLock.Dispose(); disposed = true; }
    }
}
