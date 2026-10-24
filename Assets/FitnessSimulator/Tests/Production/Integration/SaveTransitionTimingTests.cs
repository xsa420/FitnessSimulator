using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class SaveTransitionTimingTests
    {
        [Test, Timeout(90000)]
        public void CommittedTwoDayArchiveNextMorningMeasuresActualTransition()
        {
            string source = Path.Combine(Path.GetTempPath(), "Fitness-Usability-TwoDay", "688544e9df6c4d80ab4a035810d01258", "save");
            Assert.That(Directory.Exists(source), Is.True, "Actual ConfigR1 two-day source required");
            string directory = Path.Combine(Path.GetTempPath(), "Fitness-SaveExperience", Guid.NewGuid().ToString("N"));
            string target = Path.Combine(directory, "save"); Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                string dest = Path.Combine(target, file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(file, dest);
                string original = Path.Combine(directory, "source-copy", file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(original)); File.Copy(file, original);
            }
            File.WriteAllLines(Path.Combine(directory, "source-hashes.txt"), Directory.GetFiles(source, "*", SearchOption.AllDirectories).OrderBy(x => x).Select(f => f + " " + ContentHash.Sha256(File.ReadAllBytes(f))));
            var timings = new Dictionary<string, List<double>>();
            Action<string, double> record = (name, ms) => { if (!timings.ContainsKey(name)) timings[name] = new List<double>(); timings[name].Add(ms); };
            using (var session = GameSession.OpenAutomation(target, 1)) {
                var view = session.ReadView();
                session.Execute(new FrontendAction { ActionId = "continue-game", OperationId = "timing/continue", RunId = view.RunId, ExpectedRevision = view.Revision });
                view = session.ReadView(); Assert.That(view.ScreenId, Is.EqualTo("AwaitNextDay"));
                var initial = session.InspectSnapshot();
                foreach (string actor in new[] { "player", "rival", "beginner", "veteran", "coach" })
                    Assert.That(JsonUtility.FromJson<SessionState>(initial.FindFragment("session", actor).Json).Day, Is.EqualTo(2));
                long before = view.Revision;
                var next = new FrontendAction { ActionId = "next-day", OperationId = "timing/next-day", RunId = view.RunId, ExpectedRevision = view.Revision };
                var wall = Stopwatch.StartNew(); int advances = 0;
                RuntimeTiming.Observe = record;
                try {
                    using (RuntimeTiming.Measure("application.execute")) Assert.That(session.Execute(next).Accepted, Is.True);
                    for (; advances < 100; advances++) {
                        using (RuntimeTiming.Measure("application.read-view")) view = session.ReadView();
                        Assert.That(view.ScreenId, Is.Not.EqualTo("error"), view.Body);
                        Assert.That(view.Body ?? "", Does.Not.Contain("正在保存"));
                        if (view.Actions.Any(a => a.Id == "meal" && a.Enabled)) break;
                        using (RuntimeTiming.Measure("application.advance")) session.Advance(0);
                    }
                    Assert.That(advances, Is.LessThan(100));
                    string committed = JsonUtility.ToJson(session.InspectSnapshot());
                    foreach (string actor in new[] { "player", "rival", "beginner", "veteran", "coach" })
                        Assert.That(JsonUtility.FromJson<SessionState>(session.InspectSnapshot().FindFragment("session", actor).Json).Day, Is.EqualTo(3));
                    string baseline = Path.Combine(Path.GetTempPath(), "Fitness-SaveExperience", "d94400592fab459caa4a26517167d370", "final.json");
                    Assert.That(committed, Is.EqualTo(File.ReadAllText(baseline)), "Same committed business, time, receipts, random and content as measured baseline");
                    Assert.That(session.Execute(next).Accepted, Is.True);
                    Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(committed), "same operation cannot settle again");
                    File.WriteAllText(Path.Combine(directory, "final.json"), committed);
                } finally { RuntimeTiming.Observe = null; }
                var lines = timings.OrderBy(x => x.Key).Select(p => p.Key + " count=" + p.Value.Count + " totalMs=" + p.Value.Sum() + " maxMs=" + p.Value.Max()).ToList();
                lines.Add("wallMs=" + wall.Elapsed.TotalMilliseconds + " advances=" + advances + " committedRevisions=" + (view.Revision - before));
                File.WriteAllLines(Path.Combine(directory, "timing.txt"), lines); foreach (var line in lines) TestContext.WriteLine(line);
            }
            using (var reopened = GameSession.OpenAutomation(target, 1)) Assert.That(JsonUtility.ToJson(reopened.InspectSnapshot()), Is.EqualTo(File.ReadAllText(Path.Combine(directory, "final.json"))));
            TestContext.WriteLine(directory);
        }
    }
}
