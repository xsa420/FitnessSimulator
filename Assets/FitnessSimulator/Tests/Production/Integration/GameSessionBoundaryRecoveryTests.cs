using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using FitnessSimulator.Game.Tests.Automation;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    // G05 is a natural partial-world checkpoint; G06 explicitly injects only ending-condition facts.
    public sealed class GameSessionBoundaryRecoveryTests
    {
        private string evidenceDirectory, saveDirectory;
        private GameSession session;
        private StreamWriter trace;
        private readonly UnityJsonStateCodec codec = new UnityJsonStateCodec();
        [SetUp] public void Setup()
        {
            evidenceDirectory = Path.Combine(Path.GetTempPath(), "Fitness-Boundary-Recovery", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidenceDirectory); saveDirectory = Path.Combine(evidenceDirectory, "main");
            trace = new StreamWriter(Path.Combine(evidenceDirectory, "trace.txt")) { AutoFlush = true };
            session = GameSession.OpenAutomation(saveDirectory, 1);
            AssertOpened(session, "Initial Open");
        }
        [TearDown] public void Cleanup()
        {
            if (session != null) { File.WriteAllText(Path.Combine(evidenceDirectory, "final-root.json"), codec.Encode(session.InspectSnapshot())); session.Dispose(); }
            trace?.Dispose(); TestContext.WriteLine("Boundary evidence: " + evidenceDirectory);
        }
        private static T Read<T>(RootEnvelope root, string owner, string actor = "player")
        { return JsonUtility.FromJson<T>(root.FindFragment(owner, actor).Json); }
        private static ApplicationWorldState World(RootEnvelope root) => Read<ApplicationWorldState>(root, "world", "shared");
        private static void AssertOpened(GameSession target, string context)
        { Assert.That(target.InspectSnapshot(), Is.Not.Null, context + ": " + target.Diagnostic); }
        private FrontendAction Send(GameSession target, string id, string value = null, int number = 0)
        {
            var view = target.ReadView(); var action = new FrontendAction { RunId = view.RunId, ExpectedRevision = view.Revision,
                OperationId = view.RunId + "/boundary/" + view.Revision + "/" + id, ActionId = id, StringValue = value, IntValue = number };
            var reply = target.Execute(action); trace.WriteLine(codec.Encode(action) + " => " + codec.Encode(reply));
            Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message); return action;
        }
        private void Choose(GameSession target, FrontendOption option) { Send(target, option.Id, option.StringValue, option.IntValue); }
        private static bool Has(GameSession target, string id) => target.ReadView().Actions.Any(a => a.Enabled && a.Id == id);
        private void Start(bool rest)
        {
            Send(session, "new-game", TemplateCatalog.DefaultId);
            var plan = PlanCatalog.CreateBenchPressSlice(); plan.Slots[0].Volume = TrainingVolume.Low;
            if (rest) plan = new PlanDraft { Mode = PlanMode.DayCycle, Slots = new[] { PlanCatalog.Rest(0) } };
            Send(session, "save-plan", codec.Encode(plan));
        }
        private void Step(GameSession target)
        {
            var view = target.ReadView(); Assert.That(view.ScreenId, Is.Not.EqualTo("error"), view.Body + " / " + target.Diagnostic);
            if (view.TrainingInput != null) { PublicEightInputDriver.DriveActiveSet(target, view.TrainingInput.ExerciseId, trace.WriteLine); return; }
            var options = view.Actions.Where(a => a.Enabled).ToArray(); FrontendOption choice = null;
            if (options.Any(a => a.Id == "meal")) choice = options.First(a => a.Id == "meal" && a.StringValue == "skip");
            else if (options.Any(a => a.Id == "begin-set")) choice = options.First(a => a.Id == "begin-set" && a.StringValue == "manual");
            else if (options.Any(a => a.Id == "settle")) choice = options.First(a => a.Id == "settle");
            else if (options.Any(a => a.Id == "choose-entry")) choice = options.FirstOrDefault(a => a.Id == "choose-entry" && a.StringValue == "training-choice") ?? options.First(a => a.Id == "choose-entry");
            else if (options.Any(a => a.Id == "continue")) choice = options.First(a => a.Id == "continue");
            else if (options.Any(a => a.Id == "shop-leave")) choice = options.First(a => a.Id == "shop-leave");
            else if (options.Any(a => a.Id == "item-use")) choice = options.Last(a => a.Id == "item-use");
            else if (options.Any(a => a.Id == "rest")) choice = options.First(a => a.Id == "rest");
            else if (options.Any(a => a.Id == "next-day")) choice = options.First(a => a.Id == "next-day");
            if (choice != null) Choose(target, choice); else target.Advance(0);
        }
        private void Until(GameSession target, Func<RootEnvelope, bool> done, int calls = 12000, int seconds = 180)
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < calls; i++)
            {
                if (done(target.InspectSnapshot())) return;
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(seconds), "Boundary budget exhausted; no skipped steps.");
                Step(target);
            }
            Assert.Fail("Boundary not reached within call budget: " + target.ReadView().Body + " / " + target.Diagnostic);
        }
        private void Reopen()
        {
            string before = codec.Encode(session.InspectSnapshot()); session.Dispose(); session = GameSession.OpenAutomation(saveDirectory, 999);
            AssertOpened(session, "Reopen");
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(before)); Send(session, "continue-game");
        }
        private static string ActorFragments(RootEnvelope root, string actor)
        { return string.Join("\n", root.Fragments.Where(f => f.Scope == actor).OrderBy(f => f.Owner).Select(f => f.Owner + ":" + f.Revision + ":" + f.Json)); }
        private List<LogAppend> Records(string directory, string run)
        {
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded)); var result = new List<LogAppend>();
                for (int offset = 0; ; offset += 64) { var page = store.ReadLog("run/" + run + "/records", offset, 64); result.AddRange(page); if (page.Count < 64) return result; }
            }
        }

        [Test, Timeout(300000)] public void G05PartialNpcCompletionAndLockedResultResumeToSameWorldBoundary()
        {
            Start(true);
            Until(session, root => {
                var world = World(root);
                Assert.That(world.Day, Is.LessThanOrEqualTo(3), "G05 checkpoint search exceeded three natural days.");
                return world.Phase == "Anchors" && world.AnchorBarrier.Stage == BarrierStage.Interacting
                    && world.AnchorBarrier.Actors.Any(a => a.ActorId != "player" && a.BarrierReached)
                    && world.AnchorBarrier.Actors.Any(a => a.ActorId != "player" && !a.BarrierReached
                        && Read<TrainingDayState>(root, "training", a.ActorId).HasWorkout
                        && Read<TrainingDayState>(root, "training", a.ActorId).Workout.Phase == WorkoutPhase.ResultReady);
            });
            var checkpoint = session.InspectSnapshot(); var barrier = World(checkpoint).AnchorBarrier;
            Assert.That(World(checkpoint).Day, Is.LessThanOrEqualTo(3), "Do not replace the requested bounded checkpoint with a long route.");
            string completed = barrier.Actors.First(a => a.ActorId != "player" && a.BarrierReached).ActorId;
            string pending = barrier.Actors.First(a => a.ActorId != "player" && !a.BarrierReached
                && Read<TrainingDayState>(checkpoint, "training", a.ActorId).HasWorkout
                && Read<TrainingDayState>(checkpoint, "training", a.ActorId).Workout.Phase == WorkoutPhase.ResultReady).ActorId;
            string setId = Read<TrainingDayState>(checkpoint, "training", pending).Workout.PendingResult.SetId;
            Assert.That(Read<TrainingDayState>(checkpoint, "training", pending).Workout.SettledSetIds.Contains(setId), Is.False);
            string completedState = ActorFragments(checkpoint, completed);
            File.WriteAllText(Path.Combine(evidenceDirectory, "g05-checkpoint.json"), codec.Encode(checkpoint));
            string forkDirectory = Path.Combine(evidenceDirectory, "restarted"); Directory.CreateDirectory(forkDirectory);
            foreach (string file in Directory.GetFiles(saveDirectory)) File.Copy(file, Path.Combine(forkDirectory, Path.GetFileName(file)));
            Func<RootEnvelope, bool> reached = root => World(root).Phase == "Anchors" && World(root).AnchorBarrier.BarrierId == barrier.BarrierId
                && World(root).AnchorBarrier.Stage == BarrierStage.Complete;
            Until(session, reached);
            var baseline = session.InspectSnapshot(); Assert.That(ActorFragments(baseline, completed), Is.EqualTo(completedState));
            File.WriteAllText(Path.Combine(evidenceDirectory, "g05-uninterrupted.json"), codec.Encode(baseline));
            Assert.That(Read<TrainingDayState>(baseline, "training", pending).Workout.SettledSetIds.Count(id => id == setId), Is.EqualTo(1));
            using (var resumed = GameSession.OpenAutomation(forkDirectory, 999))
            {
                AssertOpened(resumed, "G05 fork Open");
                Assert.That(codec.Encode(resumed.InspectSnapshot()), Is.EqualTo(codec.Encode(checkpoint)));
                Assert.That(codec.Encode(Read<TrainingDayState>(resumed.InspectSnapshot(), "training", pending).Workout.PendingResult),
                    Is.EqualTo(codec.Encode(Read<TrainingDayState>(checkpoint, "training", pending).Workout.PendingResult)));
                Send(resumed, "continue-game"); Until(resumed, reached);
                Assert.That(codec.Encode(resumed.InspectSnapshot()), Is.EqualTo(codec.Encode(baseline)), "Includes all actor resources, random streams, cursors and committed prefixes.");
                File.WriteAllText(Path.Combine(evidenceDirectory, "g05-resumed.json"), codec.Encode(resumed.InspectSnapshot()));
            }
            session.Dispose(); session = null;
            var originalRecords = Records(saveDirectory, baseline.RunId); var resumedRecords = Records(forkDirectory, baseline.RunId);
            Assert.That(originalRecords.Select(r => r.Kind + ":" + r.Json).ToArray(), Is.EqualTo(resumedRecords.Select(r => r.Kind + ":" + r.Json).ToArray()));
            Assert.That(resumedRecords.Count(r => r.Kind == "TrainingSet" && codec.Decode<TrainingSetResult>(r.Json).SetId == setId), Is.EqualTo(1));
        }

        private void PrepareConditionFixtureBeforeCompletion()
        {
            // Explicit G06 contract fixture: no score, body, result or time is written here.
            trace.WriteLine("G06_CONTRACT_FIXTURE: relationship condition only, before actual NormalCompletion; not a natural ending route.");
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(saveDirectory, codec))
            {
                var root = store.Load().Root; var relationship = RelationshipRules.NormalizeAfterLoad(Read<RelationshipState>(root, "relationships"));
                Assert.That(relationship.RomanceEstablished, Is.False);
                relationship.Affections.Single(a => a.CharacterId == RelationshipRules.Beginner).Value = 100;
                relationship.PendingConfessionId = "G06-condition-boundary-fixture";
                var frozen = ConfessionRules.Freeze(relationship, 1000000, 1000000, 15000000);
                var change = ConfessionRules.Prepare(relationship, frozen, 0);
                root.SetFragment("relationships", "player", 1, root.Revision + 1, codec.Encode(change.Relationships));
                var receipt = new OperationReceipt { RunId = root.RunId, OperationId = "G06-condition-fixture", SemanticKey = "G06-condition-fixture",
                    PayloadHash = "explicit-contract-boundary-not-natural-route", Result = "Condition prepared before normal completion" };
                Assert.That(store.Commit(root.Generation, root, receipt, Array.Empty<LogAppend>()).Status, Is.EqualTo(CommitStatus.Committed));
                File.WriteAllText(Path.Combine(evidenceDirectory, "g06-condition-before-completion.json"), codec.Encode(store.Load().Root));
            }
            session = GameSession.OpenAutomation(saveDirectory, 1); AssertOpened(session, "G06 condition fixture Reopen"); Send(session, "continue-game");
        }
        private void FinishEndingWithRestarts()
        {
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-presentation")); Reopen();
            Choose(session, session.ReadView().Actions.Single(a => a.Id == "ending-segment")); Reopen();
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-note"));
            Choose(session, session.ReadView().Actions.Single(a => a.Id == "ending-empty")); Reopen();
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-menu"));
            Choose(session, session.ReadView().Actions.Single(a => a.Id == "ending-continue")); Reopen();
        }

        [Test, Timeout(300000)] public void G06RealKernelResultReadyEndingResumesOriginalSetExactlyOnce()
        {
            Start(false); Until(session, root => Has(session, "begin-set")); Send(session, "begin-set", "manual");
            Assert.That(Read<TrainingDayState>(session.InspectSnapshot(), "training").Workout.Phase, Is.EqualTo(WorkoutPhase.Playing));
            PrepareConditionFixtureBeforeCompletion();
            string bodyBefore = session.InspectSnapshot().FindFragment("body", "player").Json;
            PublicEightInputDriver.DriveActiveSet(session, "bench_press", trace.WriteLine);
            var ready = Read<TrainingDayState>(session.InspectSnapshot(), "training").Workout;
            Assert.That(ready.Phase, Is.EqualTo(WorkoutPhase.ResultReady));
            Assert.That(ready.PendingResult.Producer, Is.EqualTo(TrainingProducer.TrustedAutomationInput));
            Assert.That(ready.PendingResult.Success, Is.True);
            Assert.That(session.InspectSnapshot().FindFragment("body", "player").Json, Is.EqualTo(bodyBefore));
            string result = codec.Encode(ready.PendingResult); string setId = ready.PendingResult.SetId;
            long checks = Read<SessionState>(session.InspectSnapshot(), "session").CompletionChecks;
            File.WriteAllText(Path.Combine(evidenceDirectory, "g06-result-ready-ending.json"), codec.Encode(session.InspectSnapshot()));
            FinishEndingWithRestarts();
            Assert.That(codec.Encode(Read<TrainingDayState>(session.InspectSnapshot(), "training").Workout.PendingResult), Is.EqualTo(result));
            Assert.That(Read<SessionState>(session.InspectSnapshot(), "session").CompletionChecks, Is.EqualTo(checks));
            Until(session, root => Has(session, "settle")); var settle = Send(session, "settle");
            var settled = Read<TrainingDayState>(session.InspectSnapshot(), "training").Workout;
            Assert.That(settled.SettledSetIds.Count(id => id == setId), Is.EqualTo(1));
            Assert.That(session.InspectSnapshot().FindFragment("body", "player").Json, Is.Not.EqualTo(bodyBefore));
            string after = codec.Encode(session.InspectSnapshot()); Reopen();
            Assert.That(session.Execute(settle).Accepted, Is.True); Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(after));
            File.WriteAllText(Path.Combine(evidenceDirectory, "g06-settled-once.json"), after);
            string run = session.InspectSnapshot().RunId; session.Dispose(); session = null;
            Assert.That(Records(saveDirectory, run).Count(r => r.Kind == "TrainingSet" && codec.Decode<TrainingSetResult>(r.Json).SetId == setId), Is.EqualTo(1));
        }

        [Test, Timeout(300000)] public void G06DayEndE03EndingResumesE04WithoutRepeatingIncomeOrRecovery()
        {
            Start(false);
            Until(session, root => Read<SessionState>(root, "session").Phase == "DayEnd" && Read<SessionState>(root, "session").DayStep == 3);
            var before = session.InspectSnapshot(); var state = Read<SessionState>(before, "session"); var body = Read<BodyState>(before, "body");
            Assert.That(body.Regions.Any(r => r.LocalFatigueMicro > 0), Is.True, "Real training must provide a nonzero recovery witness.");
            var expected = BodyCalculator.RecoverLocalFatigue(body, new Rational(state.Nutrition.LocalRecoveryMultiplierNumerator, state.Nutrition.LocalRecoveryMultiplierDenominator));
            var life = Read<LifeActorState>(before, "life"); string incomeKey = life.LastIncome.SettlementKey; long balance = life.Wallet.Balance;
            Assert.That(life.HasLastIncome, Is.True); Assert.That(incomeKey, Is.Not.Null.And.Not.Empty);
            PrepareConditionFixtureBeforeCompletion();
            Until(session, root => Read<ProgressionRun>(root, "progression").HasActive);
            Assert.That(Read<SessionState>(session.InspectSnapshot(), "session").DayStep, Is.EqualTo(4));
            Assert.That(codec.Encode(Read<BodyState>(session.InspectSnapshot(), "body")), Is.EqualTo(codec.Encode(expected)));
            long checks = Read<SessionState>(session.InspectSnapshot(), "session").CompletionChecks;
            File.WriteAllText(Path.Combine(evidenceDirectory, "g06-e03-ending.json"), codec.Encode(session.InspectSnapshot()));
            FinishEndingWithRestarts();
            Assert.That(Read<SessionState>(session.InspectSnapshot(), "session").DayStep, Is.EqualTo(4));
            Assert.That(Read<SessionState>(session.InspectSnapshot(), "session").CompletionChecks, Is.EqualTo(checks));
            Until(session, root => Read<SessionState>(root, "session").Phase == "AwaitNextDay");
            var finished = session.InspectSnapshot();
            Assert.That(Read<LifeActorState>(finished, "life").Wallet.Balance, Is.EqualTo(balance));
            Assert.That(Read<BodyState>(finished, "body").Regions.Select(r => r.LocalFatigueMicro).ToArray(), Is.EqualTo(expected.Regions.Select(r => r.LocalFatigueMicro).ToArray()));
            File.WriteAllText(Path.Combine(evidenceDirectory, "g06-day-end-once.json"), codec.Encode(finished));
            session.Dispose(); session = null;
            Assert.That(Records(saveDirectory, finished.RunId).Count(r => r.Kind == "Income" && codec.Decode<IncomeReceipt>(r.Json).SettlementKey == incomeKey), Is.EqualTo(1));
        }
    }
}
