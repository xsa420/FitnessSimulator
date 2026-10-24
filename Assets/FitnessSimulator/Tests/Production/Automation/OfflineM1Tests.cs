using FitnessSimulator.Automation;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using FitnessSimulator.Game.Application;
using UnityEngine;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Automation
{
    // ScriptReplay exercises production commands and kernel input; it does not earn PlayerTouch qualification.
    public sealed class OfflineM1Tests
    {
        public static System.Collections.Generic.IEnumerable<uint> Seeds {
            get {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FITNESS_OFFLINE_SEEDS"))) {
                    foreach (string seed in Environment.GetEnvironmentVariable("FITNESS_OFFLINE_SEEDS").Split(',')) yield return uint.Parse(seed);
                } else { yield return 1u; yield return 7u; yield return 42u; }
            }
        }
        [TestCaseSource(nameof(Seeds))]
        public void ThreeDaysWithDurableCheckpointRecovery(uint seed)
        {
            int targetDays = int.TryParse(Environment.GetEnvironmentVariable("FITNESS_OFFLINE_DAYS"), out int configuredDays) ? configuredDays : 3;
            int secondsBudget = int.TryParse(Environment.GetEnvironmentVariable("FITNESS_OFFLINE_SECONDS"), out int configuredSeconds) ? configuredSeconds : 180;
            int stepBudget = Math.Max(100000, targetDays * 40000);
            string outputRoot = Environment.GetEnvironmentVariable("FITNESS_RUN_OUTPUT_ROOT") != null
                ? Path.Combine(AutomationEvidence.OutputRoot, "Fitness-Offline")
                : Environment.GetEnvironmentVariable("FITNESS_OFFLINE_OUTPUT") ?? Path.Combine(AutomationEvidence.OutputRoot, "Fitness-Offline");
            string directory = Path.Combine(outputRoot, Guid.NewGuid().ToString("N"));
            AutomationEvidence.RegisterDirectory(directory);
            var clock = Stopwatch.StartNew();
            bool trusted = Environment.GetEnvironmentVariable("FITNESS_OFFLINE_SOURCE") == "trusted";
            bool paid = Environment.GetEnvironmentVariable("FITNESS_OFFLINE_STRATEGY") == "paid-rest-auto";
            Func<GameSession> open = () => trusted ? AutomationEvidenceSession.OpenAutomation(directory, seed) : AutomationEvidenceSession.OpenTest(directory, seed);
            GameSession session = open();
            int paidMeals = 0, rests = 0, automaticSets = 0, shopLeaves = 0, itemPoints = 0;
            bool restedBeforeSet = false;
            int commands = 0, days = 0, stagnant = 0, recoveries = 0, settledSets = 0, kernelTicks = 0;
            long lastRevision = -1;
            string outcome = "BudgetExhausted";
            using (var trace = new EvidenceTrace(Path.Combine(directory, "trace.jsonl"))) {
                Action<string, string, int> send = (id, value, number) => {
                    var view = session.ReadView();
                    var action = new FrontendAction { OperationId = "offline/" + (++commands), RunId = view.RunId, ExpectedRevision = view.Revision, ActionId = id, StringValue = value, IntValue = number };
                    trace.WriteLine(JsonUtility.ToJson(view));
                    var reply = AutomationEvidenceSession.Execute(session,action);
                    trace.WriteLine(JsonUtility.ToJson(action));
                    trace.WriteLine(JsonUtility.ToJson(reply));
                    Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message);
                };
                try {
                    for (int step = 0; step < stepBudget && clock.Elapsed.TotalSeconds < secondsBudget; step++) {
                        var view = session.ReadView();
                        Assert.That(view.ScreenId, Is.Not.EqualTo("error"), view.Body);
                        if (view.BenchPress != null) {
                            var b = view.BenchPress;
                            bool left = b.Balance > .02, right = b.Balance < -.02;
                            if (left != b.LeftHeld) send(left ? "bench-left-down" : "bench-left-up", null, 0);
                            if (right != b.RightHeld) send(right ? "bench-right-down" : "bench-right-up", null, 0);
                            session.Advance(.01); kernelTicks++;
                            trace.WriteLine("{\"advanceSeconds\":0.01}");
                            continue;
                        }
                        // Observe hidden snapshots only for diagnostics and exact durable recovery comparison.
                        var root = session.InspectSnapshot();
                        if (root != null && root.Revision != lastRevision) {
                            stagnant = 0; lastRevision = root.Revision;
                            trace.WriteLine(JsonUtility.ToJson(root));
                        } else Assert.That(++stagnant, Is.LessThan(50), "Stalled at " + view.ScreenId);
                        var enabled = view.Actions.Where(a => a.Enabled).ToArray();
                        var option = enabled.FirstOrDefault(a => a.Id == "new-game");
                        if (view.ScreenId != "menu") option = null;
                        if (option == null && view.ScreenId == "InitialPlan") option = enabled.FirstOrDefault(a => a.Id == "save-plan");
                        foreach (string id in new[] { "continue-game", "meal", "choose-entry", "begin-set", "settle", "continue", "next-day", "shop-leave", "item-use" })
                            if (option == null) option = enabled.FirstOrDefault(a => a.Id == id);
                        if (paid && option != null && option.Id == "meal") {
                            option = enabled.FirstOrDefault(a => a.Id == "meal" && a.StringValue != "skip") ?? option;
                            if (option.StringValue != "skip") paidMeals++;
                        }
                        if (paid && option != null && option.Id == "begin-set") {
                            if (!restedBeforeSet) {
                                var rest = enabled.FirstOrDefault(a => a.Id == "rest" && a.IntValue == 90);
                                Assert.That(rest, Is.Not.Null, "Missing legal rest option");
                                send(rest.Id, rest.StringValue, rest.IntValue); rests++; restedBeforeSet = true; continue;
                            }
                            option = enabled.FirstOrDefault(a => a.Id == "begin-set" && a.StringValue == "auto") ?? option;
                            if (option.StringValue == "auto") automaticSets++;
                            restedBeforeSet = false;
                        }
                        if (option != null && option.Id == "next-day") {
                            string before = JsonUtility.ToJson(root);
                            session.Dispose(); session = open();
                            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(before), "Durable state changed on reopen");
                            recoveries++; send("continue-game", null, 0);
                            if (++days == targetDays) { outcome = "M1TargetDaysReached"; break; }
                        }
                        if (option != null) { send(option.Id, option.StringValue, option.IntValue); if (option.Id == "settle") settledSets++; if (option.Id == "shop-leave") shopLeaves++; if (option.Id == "item-use") itemPoints++; }
                        else { var unknown = enabled.FirstOrDefault(a => a.Id != "open-menu" && a.Id != "save-plan" && a.Id != "rest" && a.Id != "end-training" && a.Id != "end-action"); Assert.That(unknown, Is.Null, "MissingHandler: " + view.ScreenId + "/" + unknown?.Id); session.Advance(0); trace.WriteLine("{\"advanceSeconds\":0}"); }
                    }
                    Assert.That(outcome, Is.EqualTo("M1TargetDaysReached"), "Budget exhausted; " + directory);
                    Assert.That(kernelTicks, Is.GreaterThan(0), "CoverageMissing: no real kernel input; " + directory);
                    Assert.That(settledSets, Is.GreaterThan(0), "CoverageMissing: no completed set persisted; " + directory);
                    if (paid) { Assert.That(paidMeals, Is.GreaterThan(0), "CoverageMissing: paid meal"); Assert.That(rests, Is.GreaterThan(0), "CoverageMissing: rest"); if (trusted) Assert.That(automaticSets, Is.GreaterThan(0), "CoverageMissing: earned automatic capacity"); }
                } catch (Exception ex) { outcome = ex.ToString(); throw; }
                finally {
                    AutomationEvidence.WriteAllText(Path.Combine(directory, "final-state.json"), session.InspectSnapshot() == null ? "null" : JsonUtility.ToJson(session.InspectSnapshot(), true));
                    AutomationEvidence.WriteAllText(Path.Combine(directory, "report.txt"), "seed=" + seed + "\nstrategy=" + (paid ? "paid-rest-auto-v1" : "first-enabled-real-bench-v1") + "\nsource=" + (trusted ? "Test/TrustedAutomationInput" : "Test/ScriptReplay") + "\nshopLeaves=" + shopLeaves + "\nitemPoints=" + itemPoints + "\npaidMeals=" + paidMeals + "\nrests=" + rests + "\nautomaticSets=" + automaticSets + "\nconfig=" + session.InspectSnapshot()?.ContentVersion + "\ndays=" + days + "\nrecoveries=" + recoveries + "\nsettledSets=" + settledSets + "\nkernelTicks=" + kernelTicks + "\ncommands=" + commands + "\nexit=" + outcome + "\ncoverage=M1-only; endings,NPC,other-exercises,mid-set-comparison,shop-purchase,study-route missing");
                    session.Dispose(); TestContext.WriteLine("Offline evidence: " + directory);
                }
            }
        }
    }
}






