using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Progression;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class ContentPackageTests
    {
        [Serializable] public sealed class Sample { public string Version; public int Amount; public Child[] Children; }
        [Serializable] public sealed class Child { public string Name; }
        private const string Valid = "{\"Version\":\"v1\",\"Amount\":50,\"Children\":[{\"Name\":\"one\"}]}";
        private static ContentPackage Package(string json = Valid) => ContentPackage.FromDocuments(new Dictionary<string, string> { { "sample", json } });

        [Test] public void DiskPackagePreservesActualRawBytesAndProvenanceAcrossSnapshotRestore()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ContentPackageTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                byte[] bytes = Encoding.UTF8.GetBytes("\uFEFF" + Valid + "\r\n");
                File.WriteAllBytes(Path.Combine(directory, "sample.json"), bytes);
                var disk = ContentPackage.FromDirectory(directory); var memory = Package();
                Assert.That(disk.Hash, Is.EqualTo(memory.Hash)); Assert.That(disk.RawHash, Is.Not.EqualTo(memory.RawHash));
                Assert.That(disk.Source, Is.EqualTo("directory:" + Path.GetFullPath(directory))); Assert.That(memory.Source, Is.EqualTo("in-memory"));
                var snapshot = disk.Export(); string expected;
                using (var sha = SHA256.Create()) expected = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Assert.That(snapshot.Documents[0].RawHash, Is.EqualTo(expected)); Assert.That(Convert.FromBase64String(snapshot.Documents[0].RawBase64), Is.EqualTo(bytes));
                var restored = ContentPackage.FromSnapshot(snapshot); Assert.That(restored.RawHash, Is.EqualTo(disk.RawHash)); Assert.That(restored.Source, Is.EqualTo(disk.Source));
                snapshot.Documents[0].RawBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Valid));
                Assert.Throws<ContentException>(() => ContentPackage.FromSnapshot(snapshot));
            } finally { Directory.Delete(directory, true); }
        }

        [Test] public void CanonicalIdentityDefensiveCopiesAndSnapshotRestoreAreStable()
        {
            var source = new Dictionary<string, string> { { "sample", Valid } }; var package = ContentPackage.FromDocuments(source);
            source["sample"] = "invalid";
            var reordered = Package(" {\"Children\":[{\"Name\":\"one\"}], \"Amount\":50,\"Version\":\"v1\"} ");
            Assert.That(package.Hash, Is.EqualTo(reordered.Hash)); Assert.That(package.Version, Is.EqualTo(reordered.Version));
            var first = package.Get<Sample>("sample"); first.Amount = 99; first.Children[0].Name = "modified";
            Assert.That(package.Get<Sample>("sample").Amount, Is.EqualTo(50)); Assert.That(package.Get<Sample>("sample").Children[0].Name, Is.EqualTo("one"));
            var codec = new UnityJsonStateCodec(); var snapshot = codec.Decode<ContentPackageSnapshot>(codec.Encode(package.Export()));
            var restored = ContentPackage.FromSnapshot(snapshot); Assert.That(restored.Hash, Is.EqualTo(package.Hash));
            snapshot.Documents[0].Json = Valid.Replace("50", "51"); Assert.Throws<ContentException>(() => ContentPackage.FromSnapshot(snapshot));
            Assert.That(package.Get<Sample>("sample").Amount, Is.EqualTo(50));
        }
        [Test] public void NestedPackageScopesRestoreAndRejectOutOfOrderDisposal()
        {
            var a = Package(); var b = Package(Valid.Replace("50", "51"));
            var outer = ContentRuntime.Push(a); var inner = ContentRuntime.Push(b);
            try {
                Assert.That(ContentRuntime.Current.Get<Sample>("sample").Amount, Is.EqualTo(51));
                Assert.Throws<InvalidOperationException>(() => outer.Dispose()); inner.Dispose();
                Assert.That(ContentRuntime.Current.Hash, Is.EqualTo(a.Hash));
            } finally { inner.Dispose(); outer.Dispose(); }
        }
        [TestCase("{\"Version\":\"v1\",\"Children\":[]}", "$.Amount")]
        [TestCase("{\"Version\":\"v1\",\"Amount\":50,\"Children\":[],\"Unexpected\":0}", "$.Unexpected")]
        [TestCase("{\"Version\":\"v1\",\"Amount\":\"50\",\"Children\":[]}", "$.Amount")]
        [TestCase("{\"Version\":\"v1\",\"Amount\":50,\"Children\":[{}]}", "$.Children[0].Name")]
        [TestCase("{\"Version\":\"v1\",\"Amount\":2147483648,\"Children\":[]}", "$.Amount")]
        [TestCase("{\"Version\":\"v1\",\"Amount\":50,\"Children\":null}", "$.Children")]
        public void InvalidDtoShapeReportsExactDocumentAndField(string json, string path)
        {
            var package = Package(json); var error = Assert.Throws<ContentException>(() => package.Get<Sample>("sample"));
            Assert.That(error.DocumentId, Is.EqualTo("sample")); Assert.That(error.Path, Is.EqualTo(path));
        }
        [TestCase("{\"Version\":\"v1\",\"Version\":\"v2\"}")]
        [TestCase("{\"Version\":\"v1\",}")]
        [TestCase("{\"Version\":\"v1\",\"N\":01}")]
        [TestCase("{\"Version\":\"v1\",\"N\":NaN}")]
        [TestCase("{\"Version\":\"v1\",\"N\":\"\\uD800\"}")]
        [TestCase("{\"Amount\":50}")]
        public void InvalidSyntaxOrMissingVersionNeverProducesPackage(string json) => Assert.Throws<ContentException>(() => Package(json));
        [Test] public void MissingDocumentAndUnsupportedIdentifierFailWithoutFallback()
        {
            Assert.Throws<ContentException>(() => Package().Get<Sample>("missing"));
            Assert.Throws<ContentException>(() => ContentPackage.FromDocuments(new Dictionary<string, string> { { "../sample.json", Valid } }));
        }
        private static ConditionDefinition Leaf(string key, string op, string value) => new ConditionDefinition { Op = op, Key = key, Value = value, Children = Array.Empty<ConditionDefinition>() };
        private static ConditionDefinition Group(string op, params ConditionDefinition[] children) => new ConditionDefinition { Op = op, Key = "", Value = "", Children = children };
        [Test] public void NestedConditionsUseOnlyWhitelistedFactsAndRejectUnknownEvenInUnreachedBranch()
        {
            var condition = Group("All", Leaf("day", "Gte", "2"), Group("Any", Leaf("work", "Eq", "done"), Group("Not", Leaf("blocked", "Eq", "true"))));
            var facts = new Dictionary<string, string> { { "day", "3" }, { "work", "pending" }, { "blocked", "false" } };
            Assert.That(ConditionEvaluator.Evaluate(condition, key => facts[key], facts.ContainsKey), Is.True);
            facts["blocked"] = "true"; Assert.That(ConditionEvaluator.Evaluate(condition, key => facts[key], facts.ContainsKey), Is.False);
            int reads = 0; var invalid = Group("Any", Group("True"), Leaf("state.private", "Eq", "x"));
            Assert.Throws<ContentException>(() => ConditionEvaluator.Evaluate(invalid, key => { reads++; return "x"; }, facts.ContainsKey)); Assert.That(reads, Is.Zero);
            Assert.Throws<ContentException>(() => ConditionEvaluator.Validate(Group("All"), facts.ContainsKey));
            Assert.Throws<ContentException>(() => ConditionEvaluator.Validate(Group("Not", Leaf("day", "Eq", "1"), Leaf("day", "Eq", "2")), facts.ContainsKey));
            Assert.Throws<ContentException>(() => ConditionEvaluator.Validate(Leaf("day", "Gt", "not-a-number"), facts.ContainsKey));
        }
        [Test] public void FlowFreezePreservesPayloadAndTitleAcrossDefinitionMutationAndCodecRestore()
        {
            var node = new FlowNode { Id = "node", DefinitionId = "default", Handler = "story", Title = "frozen", PayloadJson = "{\"story\":\"old\"}" };
            var definition = new FlowDefinition { Id = "config-flow", Version = "v1", Nodes = new List<FlowNode> { node },
                DefaultEntry = new EntryDefinition { DefinitionId = "default", RootNodeId = "node" } };
            var state = new FlowState(); FlowEngine.ResolveAnchor(state, definition, "player", 1, "morning", 1, "instance", key => true, target => true);
            node.Title = "new"; node.PayloadJson = "{}";
            var codec = new UnityJsonStateCodec(); state = codec.Decode<FlowState>(codec.Encode(state));
            Assert.That(state.Tree.Nodes[0].Title, Is.EqualTo("frozen")); Assert.That(state.Tree.Nodes[0].PayloadJson, Is.EqualTo("{\"story\":\"old\"}"));
        }
        [Test] public void EndingCatalogActuallyConsumesScopedDocumentAndRejectsBrokenReferences()
        {
            string json = "{\"Version\":\"test-v1\",\"Endings\":[{\"Id\":\"configured\",\"Order\":9,\"Title\":\"Configured title\",\"ConditionKey\":\"known\",\"Terminal\":false,\"PresentationSegments\":[\"segment\"],\"UnlockTemplateIds\":[]}]}";
            using (ContentRuntime.Push(ContentPackage.FromDocuments(new Dictionary<string, string> { { "endings", json } }))) {
                EndingProgression.ValidateContent(key => key == "known", key => key == "segment", key => false);
                Assert.That(EndingProgression.DefaultCatalog()[0].Title, Is.EqualTo("Configured title"));
                var changed = EndingProgression.DefaultCatalog(); changed[0].Title = "mutated";
                Assert.That(EndingProgression.DefaultCatalog()[0].Title, Is.EqualTo("Configured title"));
                var error = Assert.Throws<ContentException>(() => EndingProgression.ValidateContent(key => false)); Assert.That(error.Path, Is.EqualTo("$.Endings[0].ConditionKey"));
            }
        }
    }
}
