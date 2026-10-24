using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FitnessSimulator.Game.Configuration
{
    [AttributeUsage(AttributeTargets.Field)] public sealed class ContentOptionalAttribute : Attribute { }
    public sealed class ContentException : ArgumentException
    {
        public string DocumentId { get; }
        public string Path { get; }
        public ContentException(string documentId, string path, string reason) : base(documentId + ":" + path + ": " + reason)
        { DocumentId = documentId; Path = path; }
    }
    [Serializable] public sealed class ContentDocumentSnapshot { public string Id; public string Json; public string RawHash; public string RawBase64; }
    [Serializable] public sealed class ContentPackageSnapshot { public string Version; public string Hash; public string RawHash; public string Source; public ContentDocumentSnapshot[] Documents; }
    public static class ContentJson
    {
        public static T Deserialize<T>(string id, string json) where T : class { var parsed = StrictContentJson.Parse(id, json); StrictContentJson.Validate(id, parsed, typeof(T), "$", 0); return (T)StrictContentJson.Decode(parsed, typeof(T)); }
        public static string Serialize<T>(T value) where T : class => StrictContentJson.Canonical(StrictContentJson.Encode(value));
    }

    // Immutable source documents. Every Get decodes a fresh object; no mutable DTO is cached.
    public sealed class ContentPackage
    {
        private readonly SortedDictionary<string, string> documents;
        private readonly Dictionary<string, byte[]> rawDocuments = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, object> parsedDocuments = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<Tuple<string, Type>, Func<object>> decoders = new Dictionary<Tuple<string, Type>, Func<object>>();
        private readonly object validationGate = new object();
        public string Version { get; }
        public string Hash { get; }
        public string RawHash { get; }
        public string Source { get; }
        public string[] DocumentIds => documents.Keys.ToArray();
        private ContentPackage(IDictionary<string, byte[]> source, string sourceLabel)
        {
            if (source == null || source.Count == 0) throw new ContentException("package", "$", "At least one document is required.");
            if (string.IsNullOrWhiteSpace(sourceLabel)) throw new ContentException("package", "$.Source", "Source label required.");
            Source = sourceLabel;
            documents = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var identity = new StringBuilder(); var versions = new StringBuilder(); var rawIdentity = new StringBuilder();
            foreach (var pair in source.OrderBy(p => p.Key, StringComparer.Ordinal)) {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key.Any(c => !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_'))
                    throw new ContentException(pair.Key ?? "package", "$", "Document ID must be snake_case without extension.");
                if (pair.Value == null) throw new ContentException(pair.Key, "$", "Missing raw document bytes.");
                byte[] raw = (byte[])pair.Value.Clone(); string sourceText;
                try { sourceText = new UTF8Encoding(false, true).GetString(raw); }
                catch (DecoderFallbackException) { throw new ContentException(pair.Key, "$", "Invalid UTF-8 bytes."); }
                if (sourceText.Length > 0 && sourceText[0] == '\uFEFF') sourceText = sourceText.Substring(1);
                var parsed = StrictContentJson.Parse(pair.Key, sourceText) as Dictionary<string, object>;
                if (parsed == null) throw new ContentException(pair.Key, "$", "Document must be an object.");
                if (!parsed.TryGetValue("Version", out var version) || !(version is string text) || string.IsNullOrWhiteSpace(text))
                    throw new ContentException(pair.Key, "$.Version", "Nonempty version is required.");
                string canonical = StrictContentJson.Canonical(parsed);
                parsedDocuments.Add(pair.Key, parsed);
                documents.Add(pair.Key, canonical); Add(identity, pair.Key); Add(identity, canonical); Add(versions, pair.Key); Add(versions, text);
                rawDocuments.Add(pair.Key, raw); Add(rawIdentity, pair.Key); Add(rawIdentity, Sha(raw));
            }
            Hash = Sha(identity.ToString()); RawHash = Sha(rawIdentity.ToString()); Version = "content-package.v1/" + Sha(versions.ToString());
        }
        public static ContentPackage FromDocuments(IDictionary<string, string> documents)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var document in documents) {
                if (document.Value == null) throw new ContentException(document.Key, "$", "Missing text document.");
                try { raw.Add(document.Key, new UTF8Encoding(false, true).GetBytes(document.Value)); }
                catch (EncoderFallbackException) { throw new ContentException(document.Key, "$", "Unpaired Unicode surrogate in text document."); }
            }
            return new ContentPackage(raw, "in-memory");
        }
        public static ContentPackage FromDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Content directory required.", nameof(path));
            string fullPath = System.IO.Path.GetFullPath(path);
            var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(fullPath, "*.json", SearchOption.TopDirectoryOnly)) {
                string id = System.IO.Path.GetFileNameWithoutExtension(file);
                if (raw.ContainsKey(id)) throw new ContentException(id, "$", "Duplicate document file.");
                raw.Add(id, File.ReadAllBytes(file));
            }
            return new ContentPackage(raw, "directory:" + fullPath);
        }
        internal static ContentPackage FromResourceBytes(IDictionary<string, byte[]> documents, string source = "resources:GameContent") => new ContentPackage(documents, source);
        public T Get<T>(string documentId) where T : class
        {
            if (documentId == null || !documents.TryGetValue(documentId, out var json)) throw new ContentException(documentId ?? "package", "$", "Required document is missing.");
            var key = Tuple.Create(documentId, typeof(T));
            Func<object> decode;
            lock (validationGate) {
                if (!decoders.TryGetValue(key, out decode)) {
                    StrictContentJson.Validate(documentId, parsedDocuments[documentId], typeof(T), "$", 0);
                    decode = StrictContentJson.BuildDecoder(parsedDocuments[documentId], typeof(T));
                    decoders.Add(key, decode);
                }
            }
            try { return (T)decode(); }
            catch (ArgumentException ex) when (!(ex is ContentException)) { throw new ContentException(documentId, "$", ex.Message); }
        }
        public ContentPackageSnapshot Export() => new ContentPackageSnapshot { Version = Version, Hash = Hash, RawHash = RawHash, Source = Source,
            Documents = documents.Select(p => new ContentDocumentSnapshot { Id = p.Key, Json = p.Value,
                RawHash = Sha(rawDocuments[p.Key]), RawBase64 = Convert.ToBase64String(rawDocuments[p.Key]) }).ToArray() };
        public static ContentPackage FromSnapshot(ContentPackageSnapshot snapshot)
        {
            if (snapshot?.Documents == null) throw new ContentException("snapshot", "$.Documents", "Missing document snapshot.");
            var sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var document in snapshot.Documents) {
                if (document == null || document.Id == null || sources.ContainsKey(document.Id)) throw new ContentException("snapshot", "$.Documents", "Null or duplicate document.");
                byte[] raw;
                try { raw = Convert.FromBase64String(document.RawBase64 ?? ""); }
                catch (FormatException) { throw new ContentException(document.Id, "$.RawBase64", "Invalid raw document encoding."); }
                if (Sha(raw) != document.RawHash) throw new ContentException(document.Id, "$.RawHash", "Raw document hash mismatch.");
                sources.Add(document.Id, raw);
            }
            var result = new ContentPackage(sources, snapshot.Source);
            if (result.Hash != snapshot.Hash || result.RawHash != snapshot.RawHash || result.Version != snapshot.Version) throw new ContentException("snapshot", "$", "Snapshot identity does not match its documents.");
            foreach (var document in snapshot.Documents)
                if (result.documents[document.Id] != document.Json) throw new ContentException(document.Id, "$.Json", "Canonical document disagrees with raw bytes.");
            return result;
        }
        private static void Add(StringBuilder output, string value) { output.Append(Encoding.UTF8.GetByteCount(value)).Append(':').Append(value); }
        private static string Sha(string value) => Sha(Encoding.UTF8.GetBytes(value));
        private static string Sha(byte[] value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    }

    public static class ContentRuntime
    {
        [ThreadStatic] private static Scope active;
        private static ContentPackage defaults;
        public static ContentPackage Current => active?.Package ?? (defaults ?? (defaults = LoadResources()));
        public static IDisposable Push(ContentPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            var scope = new Scope(package, active); active = scope; return scope;
        }
        public static ContentPackage LoadLegacyMigrationTarget()
        {
            var package = LoadResources("LegacyContent/M3R1");
            if (package.Hash != "823f6155979d381c91f51148cd22c248703695dc263e7455a6ce67c5bbd78681")
                throw new ContentException("legacy-adapter", "$", "Pinned m3r1-schema2-to-configr1.v1 target changed; a new explicit migration ID is required.");
            return package;
        }
        private static ContentPackage LoadResources(string path = "GameContent")
        {
            var documents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var asset in Resources.LoadAll<TextAsset>(path)) {
                if (documents.ContainsKey(asset.name)) throw new ContentException(asset.name, "$", "Duplicate resource name.");
                documents.Add(asset.name, asset.bytes);
            }
            return ContentPackage.FromResourceBytes(documents, "resources:" + path);
        }
        private sealed class Scope : IDisposable
        {
            internal readonly ContentPackage Package;
            private readonly Scope previous;
            private bool disposed;
            internal Scope(ContentPackage package, Scope previous) { Package = package; this.previous = previous; }
            public void Dispose()
            {
                if (disposed) return;
                if (!ReferenceEquals(active, this)) throw new InvalidOperationException("Content scopes must be disposed on their owning thread in LIFO order.");
                active = previous; disposed = true;
            }
        }
    }
}
