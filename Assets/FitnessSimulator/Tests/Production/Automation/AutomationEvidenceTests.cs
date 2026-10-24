using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FitnessSimulator.Automation;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Automation
{
    // Unity's edit-mode runner executes these synchronous cases serially.
    public sealed class AutomationEvidenceTests
    {
        private string directory;
        private readonly Dictionary<string, string> previous = new Dictionary<string, string>();
        [SetUp] public void SetUp()
        {
            foreach (string name in new[] { "FITNESS_TRACE_MAX_BYTES", "FITNESS_SNAPSHOT_MAX_BYTES", "FITNESS_SNAPSHOT_SET_MAX_BYTES" })
            { previous[name] = Environment.GetEnvironmentVariable(name); Environment.SetEnvironmentVariable(name, "4096"); }
            directory = Path.Combine(AutomationEvidence.OutputRoot, "Fitness-Evidence-Boundaries", Guid.NewGuid().ToString("N"));
            AutomationEvidence.RegisterDirectory(directory);
        }
        [TearDown] public void TearDown()
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
        [Test] public void TraceRejectsWholeUtf8LineBeforeOverflow()
        {
            string path = Path.Combine(directory, "trace.jsonl");
            long length = Encoding.UTF8.GetByteCount("中" + Environment.NewLine);
            Environment.SetEnvironmentVariable("FITNESS_TRACE_MAX_BYTES", length.ToString());
            using (var trace = new EvidenceTrace(path))
            {
                trace.WriteLine("中");
                Assert.That(trace.Bytes, Is.EqualTo(length));
                Assert.Throws<InvalidOperationException>(() => trace.WriteLine("x"));
                trace.Flush(); Assert.That(new FileInfo(path).Length, Is.EqualTo(length));
            }
            Assert.That(File.ReadAllText(path), Is.EqualTo("中" + Environment.NewLine));
        }
        [Test] public void RepeatedViewsUseResolvableReferencesAndKeepActions()
        {
            string path = Path.Combine(directory, "trace.jsonl");
            using (var trace = new EvidenceTrace(path))
            {
                trace.WriteLine("{\"RunId\":\"a\",\"ScreenId\":\"menu\"}");
                trace.WriteLine("{\"RunId\":\"a\",\"ScreenId\":\"menu\"}");
                trace.WriteLine("{\"ActionId\":\"continue\"}");
                Assert.That(trace.ViewReferences, Is.EqualTo(1));
            }
            string[] lines = File.ReadAllLines(path);
            Assert.That(lines[0], Does.StartWith("{\"viewHash\":"));
            string hash = lines[1].Split('"')[3];
            Assert.That(lines[0], Does.Contain(hash));
            Assert.That(lines[2], Is.EqualTo("{\"ActionId\":\"continue\"}"));
        }
        [Test] public void OversizedSnapshotPreservesPreviousEvidence()
        {
            string path = Path.Combine(directory, "snapshot.json");
            Environment.SetEnvironmentVariable("FITNESS_SNAPSHOT_MAX_BYTES", "4");
            AutomationEvidence.WriteAllText(path, "1234");
            Assert.Throws<InvalidOperationException>(() => AutomationEvidence.WriteAllText(path, "12345"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("1234"));
        }
        [Test] public void SnapshotSetCountsNestedSavesAndReplacementOnlyOnce()
        {
            Environment.SetEnvironmentVariable("FITNESS_SNAPSHOT_SET_MAX_BYTES", "8");
            string save = Path.Combine(directory, "save"); Directory.CreateDirectory(save);
            string path = Path.Combine(save, "state.bin");
            AutomationEvidence.WriteAllText(path, "1234");
            AutomationEvidence.WriteAllText(path, "12345678");
            string second = Path.Combine(directory, "final.json");
            Assert.Throws<InvalidOperationException>(() => AutomationEvidence.WriteAllText(second, "x"));
            Assert.That(File.Exists(second), Is.False);
            Assert.That(AutomationEvidence.ValidateDirectory(directory), Is.EqualTo(8));
        }
        [Test] public void CopyRejectsOversizedSourceWithoutCreatingDestination()
        {
            string source = Path.Combine(directory, "source.bin"), target = Path.Combine(directory, "clone.bin");
            File.WriteAllBytes(source, new byte[5]);
            Environment.SetEnvironmentVariable("FITNESS_SNAPSHOT_MAX_BYTES", "4");
            Assert.Throws<InvalidOperationException>(() => AutomationEvidence.Copy(source, target));
            Assert.That(File.Exists(target), Is.False);
        }
        [Test] public void ExternallyWrittenSaveOversizeIsDetectedWithoutDeletingEvidence()
        {
            string path = Path.Combine(directory, "state-a.bin");
            File.WriteAllBytes(path, new byte[5]);
            Environment.SetEnvironmentVariable("FITNESS_SNAPSHOT_MAX_BYTES", "4");
            Assert.Throws<InvalidOperationException>(() => AutomationEvidence.ValidateFor(path));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(5));
        }
        [TestCase("0")][TestCase("-1")][TestCase("unlimited")][TestCase("9223372036854775808")]
        public void InvalidBudgetFailsExplicitly(string value)
        {
            Environment.SetEnvironmentVariable("FITNESS_TRACE_MAX_BYTES", value);
            Assert.Throws<InvalidOperationException>(() => new EvidenceTrace(Path.Combine(directory, "trace.jsonl")));
        }
    }
}
