using System;
using System.IO;
using FitnessSimulator.Persistence;
using FitnessSimulator.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Tests
{
    public sealed class FoundationTests
    {
        private string directory;
        private LocalJsonStore store;

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "FitnessSimulatorTests", Guid.NewGuid().ToString("N"));
            store = new LocalJsonStore(directory);
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void OverwritingCurrentRunPreservesSeparateEndingHistory()
        {
            store.Save("ending-history", new EndingHistoryEnvelope { entries = new[] {
                new EndingHistoryEntry { runId = "old", endingId = "first", immutableSnapshotJson = "{\"strength\":7}", noteWasSet = true, note = "一次备注" }
            }});
            store.Save("current-run", new RunSaveEnvelope { runId = "old", gameDay = 10 });
            store.Save("current-run", new RunSaveEnvelope { runId = "new", gameDay = 1, randomState = 125 });
            Assert.That(store.TryLoad<RunSaveEnvelope>("current-run", out var run), Is.True);
            Assert.That(run.runId, Is.EqualTo("new"));
            Assert.That(run.randomState, Is.EqualTo(125));
            Assert.That(store.TryLoad<EndingHistoryEnvelope>("ending-history", out var history), Is.True);
            Assert.That(history.entries[0].note, Is.EqualTo("一次备注"));
            Assert.That(history.entries[0].runId, Is.EqualTo("old"));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void StoryContinuationAndPendingPlanRoundTrip()
        {
            store.Save("current-run", new RunSaveEnvelope { runId = "run", gameDay = 3,
                nextStoryEventId = "event-b", pendingPlanJson = "{\"anchor\":\"night\"}", committedStateJson = "{\"value\":42}" });
            Assert.That(store.TryLoad<RunSaveEnvelope>("current-run", out var run), Is.True);
            Assert.That(run.nextStoryEventId, Is.EqualTo("event-b"));
            Assert.That(run.committedStateJson, Is.EqualTo("{\"value\":42}"));
            Assert.That(run.pendingPlanJson, Does.Contain("night"));
        }

        [Test]
        public void SlotCannotEscapeItsDirectory()
        {
            Assert.Throws<ArgumentException>(() => store.Save("../outside", new RunSaveEnvelope()));
            Assert.That(Directory.Exists(directory), Is.False);
        }

        [Test]
        public void CatalogEntriesHaveUniqueIdsAndLoadablePresentations()
        {
            var catalog = Resources.Load<ExerciseCatalog>("ExerciseCatalog");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.exercises.Length, Is.EqualTo(8));
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var entry in catalog.exercises)
            {
                Assert.That(ids.Add(entry.exerciseId), Is.True);
                Assert.That(entry.stagePrefab, Is.Not.Null, entry.exerciseId);
                Assert.That(entry.demonstrationClip, Is.Not.Null, entry.exerciseId);
                Assert.That(entry.demonstrationClip.length, Is.GreaterThan(0));
                Assert.That(entry.demonstrationOnly, Is.True);
                Assert.That(entry.stagePrefab.GetComponentInChildren<Animation>(), Is.Not.Null);
            }
        }
    }
}
