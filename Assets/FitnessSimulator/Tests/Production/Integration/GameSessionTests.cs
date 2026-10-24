using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class GameSessionTests
    {
        private string directory;
        private GameSession session;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "Fitness-M1-Integration", Guid.NewGuid().ToString("N")); session = GameSession.OpenTest(directory, 1); }
        [TearDown] public void TearDown() { session?.Dispose(); }
        private T State<T>(string owner) => JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner, "player").Json);
        private FrontendAction Command(string id, string value = null, int number = 0) => new FrontendAction { OperationId = Guid.NewGuid().ToString("N"), RunId = session.ReadView().RunId, ExpectedRevision = session.ReadView().Revision, ActionId = id, StringValue = value, IntValue = number };
        private FrontendAction Send(string id, string value = null, int number = 0)
        { var action = Command(id, value, number); var reply = session.Execute(action); Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message); return action; }
        private void Restart() { session.Dispose(); session = GameSession.OpenTest(directory, 999); Assert.That(session.ReadView().ScreenId, Is.Not.EqualTo("error"), session.ReadView().Body); Send("continue-game"); }
        private void TickUntil(Func<bool> done, int limit = 3000)
        { for (int i = 0; i < limit && !done(); i++) session.Advance(0); Assert.That(done(), Is.True, session.ReadView().Title + " / " + session.ReadView().Body); }
        private bool Has(string action) => session.ReadView().Actions.Any(a => a.Id == action && a.Enabled);
        private void Start(bool restDay = false, string exercise = null)
        {
            Send("new-game", TemplateCatalog.DefaultId);
            var plan = PlanCatalog.CreateBenchPressSlice(); plan.Slots[0].Volume = TrainingVolume.Low;
            if (exercise != null) plan.Slots[0].Entries[0].ExerciseId = exercise;
            if (restDay) plan.Slots[0] = PlanCatalog.Rest(0);
            Send("save-plan", JsonUtility.ToJson(plan));
        }
        private void ReachPreSet()
        {
            for (int i = 0; i < 3000 && !Has("begin-set"); i++) {
                if (Has("meal")) Send("meal", "skip");
                else if (Has("continue")) Send("continue");
                else if (Has("choose-entry")) Send("choose-entry", "training-choice");
                else session.Advance(0);
            }
            Assert.That(Has("begin-set"), Is.True, session.ReadView().Body);
        }
        private void SteerToCompletion()
        {
            // Real deterministic kernel input. No result object or success field is injected.
            for (int i = 0; i < 6000 && session.ReadView().BenchPress != null; i++) {
                var view = session.ReadView().BenchPress;
                bool left = view.Balance > .02, right = view.Balance < -.02;
                if (left != view.LeftHeld) Send(left ? "bench-left-down" : "bench-left-up");
                if (right != view.RightHeld) Send(right ? "bench-right-down" : "bench-right-up");
                session.Advance(.01);
            }
            Assert.That(State<TrainingDayState>("training").Workout.Phase, Is.EqualTo(WorkoutPhase.ResultReady));
            Assert.That(State<TrainingDayState>("training").Workout.PendingResult.Success, Is.True);
            TickUntil(() => Has("settle"));
        }

        [TestCase("bench_press")] [TestCase("dumbbell_curl")] [TestCase("lat_pulldown")] [TestCase("triceps_pushdown")]
        [TestCase("squat")] [TestCase("hip_thrust")] [TestCase("shoulder_press")] [TestCase("lateral_raise")]
        public void EveryPlannedExerciseStartsRealKernelAndRestartsUnfinishedSet(string exercise)
        {
            Start(exercise: exercise); ReachPreSet(); Send("begin-set", "manual");
            Assert.That(session.ReadView().TrainingInput.ExerciseId, Is.EqualTo(exercise));
            string set = State<TrainingDayState>("training").Workout.PendingRequest.SetId;
            uint seed = State<SessionState>("session").SetSeed;
            session.Advance(.6);
            Assert.That(session.ReadView().TrainingInput.AttemptMilliseconds, Is.GreaterThan(0));
            Restart();
            Assert.That(session.ReadView().TrainingInput.AttemptMilliseconds, Is.Zero);
            Assert.That(State<TrainingDayState>("training").Workout.PendingRequest.SetId, Is.EqualTo(set));
            Assert.That(State<SessionState>("session").SetSeed, Is.EqualTo(seed));
        }

        [TestCase(false)] [TestCase(true)]
        public void CurlQualificationRequiresTrustedRealInputAndSurvivesReplay(bool trusted)
        {
            if (trusted) { session.Dispose(); session = GameSession.OpenAutomation(directory, 1); }
            Start(exercise: "dumbbell_curl"); ReachPreSet();
            Assert.That(session.Execute(Command("begin-set", "TrustedAutomationInput")).Accepted, Is.False);
            Send("begin-set", "manual");
            Assert.That(session.Execute(Command("result-ready", "{\"Success\":true}")).Accepted, Is.False);
            Assert.That(session.Execute(Command("training-input", "{\"Kind\":\"0\",\"Button\":\"Raise\"}")).Accepted, Is.False);
            Assert.That(session.Execute(Command("bench-left-down")).Accepted, Is.False);
            for (int i = 0; i < 3000 && session.ReadView().TrainingInput != null; i++) {
                var input = session.ReadView().TrainingInput;
                if (!input.IsPreparing && ((input.Step == 0 && input.AttemptMilliseconds >= 700) || (input.Step == 1 && input.AttemptMilliseconds >= 1500)))
                    Send("training-input", JsonUtility.ToJson(new FrontendTrainingInput { Kind = "ButtonDown", Button = input.Step == 0 ? "Raise" : "Lower" }));
                session.Advance(.01);
            }
            var result = State<TrainingDayState>("training").Workout.PendingResult;
            Assert.That(result.Success, Is.True);
            Assert.That(result.Producer, Is.EqualTo(trusted ? TrainingProducer.TrustedAutomationInput : TrainingProducer.ScriptReplay));
            TickUntil(() => Has("settle"));
            var settle = Send("settle");
            string body = JsonUtility.ToJson(State<BodyState>("body"));
            Restart(); Assert.That(session.Execute(settle).Accepted, Is.True);
            Assert.That(JsonUtility.ToJson(State<BodyState>("body")), Is.EqualTo(body));
            ReachPreSet();
            Assert.That(session.ReadView().Actions.Single(a => a.Id == "begin-set" && a.StringValue == "auto").Enabled, Is.EqualTo(trusted));
            if (trusted) {
                Send("begin-set", "auto"); session.Advance(0);
                Assert.That(State<TrainingDayState>("training").Workout.PendingResult.Producer, Is.EqualTo(TrainingProducer.AutoCapacity));
            }
        }

        [Test] public void CompletedResultAndGroupSurviveRestartWithoutDoubleSettlement()
        {
            Start(); ReachPreSet(); Send("begin-set", "manual"); SteerToCompletion();
            long before = State<BodyState>("body").Region("chest_middle").StrengthMicro;
            var ready = State<TrainingDayState>("training").Workout;
            Assert.That(ready.PendingResult.Producer, Is.EqualTo(TrainingProducer.ScriptReplay));
            Restart(); Assert.That(Has("settle"), Is.True); Assert.That(session.ReadView().BenchPress, Is.Null);
            var settle = Send("settle");
            long after = State<BodyState>("body").Region("chest_middle").StrengthMicro;
            Assert.That(after, Is.GreaterThan(before));
            Restart(); Assert.That(session.Execute(settle).Accepted, Is.True);
            Assert.That(State<BodyState>("body").Region("chest_middle").StrengthMicro, Is.EqualTo(after));
            Assert.That(State<TrainingDayState>("training").Workout.SettledSetIds.Length, Is.EqualTo(1));
            ReachPreSet(); Send("rest", number: 90);
            long fatigue = State<BodyState>("body").Region("chest_middle").TemporaryFatigueMicro;
            Send("begin-set", "manual"); session.Advance(.8);
            string setId = State<TrainingDayState>("training").Workout.PendingRequest.SetId;
            uint seed = State<SessionState>("session").SetSeed;
            Restart();
            Assert.That(session.ReadView().BenchPress.Progress, Is.Zero);
            Assert.That(session.ReadView().BenchPress.EffectiveReps, Is.Zero);
            Assert.That(State<SessionState>("session").SetSeed, Is.EqualTo(seed));
            Assert.That(State<TrainingDayState>("training").Workout.PendingRequest.SetId, Is.EqualTo(setId));
            Assert.That(State<TrainingDayState>("training").Workout.SettledSetIds.Length, Is.EqualTo(1));
            Assert.That(State<BodyState>("body").Region("chest_middle").TemporaryFatigueMicro, Is.EqualTo(fatigue));
            Assert.That(State<BodyState>("body").Region("chest_middle").StrengthMicro, Is.EqualTo(after));
        }

        [Test] public void FirstSetRepeatedRestPersistsAndPlayingRejectsRestOrForgedResult()
        {
            Start(); ReachPreSet(); long initialChecks = State<SessionState>("session").CompletionChecks;
            var first = Send("rest", number: 30); Send("rest", number: 30);
            Assert.That(session.Execute(first).Accepted, Is.True);
            Assert.That(State<SessionState>("session").AnchorGameSeconds, Is.EqualTo(60));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(initialChecks + 2));
            Restart(); Assert.That(State<TrainingDayState>("training").Workout.SetOrdinal, Is.EqualTo(1));
            Assert.That(State<BodyState>("body").Regions.All(r => r.TemporaryFatigueMicro == 0), Is.True);
            Send("begin-set", "manual");
            Assert.That(session.Execute(Command("rest", number: 30)).Accepted, Is.False);
            Assert.That(session.Execute(Command("result-ready", "{\"Success\":true,\"EffectiveReps\":999}")).Accepted, Is.False);
            Assert.That(State<TrainingDayState>("training").Workout.Phase, Is.EqualTo(WorkoutPhase.Playing));
        }

        [Test] public void LastSavedPendingPlanSurvivesUnfinishedEventAndActivatesOnlyNextDay()
        {
            Start(restDay: true); TickUntil(() => Has("meal"));
            var low = PlanCatalog.CreateBenchPressSlice(); low.Slots[0].Volume = TrainingVolume.Low;
            Send("save-plan", JsonUtility.ToJson(low));
            var high = PlanCatalog.CreateBenchPressSlice(); high.Slots[0].Volume = TrainingVolume.High;
            Send("save-plan", JsonUtility.ToJson(high));
            var plan = State<PlanStateRecord>("plan");
            Assert.That(plan.Active.Draft.Slots[0].IsRest, Is.True);
            Assert.That(State<TrainingDayState>("training").HasQuota, Is.False);
            Restart(); Assert.That(State<PlanStateRecord>("plan").Pending.Draft.Slots[0].Volume, Is.EqualTo(TrainingVolume.High));
            FinishDay(); Send("next-day"); TickUntil(() => State<SessionState>("session").Phase == "Anchors");
            plan = State<PlanStateRecord>("plan"); Assert.That(plan.HasPending, Is.False); Assert.That(plan.Active.EffectiveDay, Is.EqualTo(2));
            Assert.That(State<TrainingDayState>("training").Quota.Entries[0].Plan.Sets, Is.EqualTo(4));
            Assert.That(State<TrainingDayState>("training").Quota.Entries[0].Plan.Reps, Is.EqualTo(10));
        }

        private void FinishDay()
        {
            for (int i = 0; i < 3000 && !Has("next-day"); i++) {
                if (Has("meal")) Send("meal", "skip");
                else if (Has("continue")) Send("continue");
                else if (Has("end-training")) Send("end-training");
                else if (Has("choose-entry")) { var option = session.ReadView().Actions.First(a => a.Id == "choose-entry" && a.Enabled); Send(option.Id, option.StringValue, option.IntValue); }
                else if (Has("shop-leave")) Send("shop-leave");
                else if (Has("item-use")) { var option = session.ReadView().Actions.Last(a => a.Id == "item-use" && a.Enabled); Send(option.Id, option.StringValue, option.IntValue); }
                else session.Advance(0);
            }
            Assert.That(State<SessionState>("session").Phase, Is.EqualTo("AwaitNextDay"), session.ReadView().Body);
        }

        [Test] public void IncomeAndRecoveryResumeAtExactDayStepAndWaitForClick()
        {
            Start(restDay: true);
            for (int i = 0; i < 3000 && State<SessionState>("session").Phase == "Anchors"; i++) {
                if (Has("meal")) Send("meal", "skip"); else if (Has("continue")) Send("continue");
                else if (Has("choose-entry")) { var option = session.ReadView().Actions.First(a => a.Id == "choose-entry" && a.Enabled); Send(option.Id, option.StringValue, option.IntValue); }
                else if (Has("shop-leave")) Send("shop-leave");
                else if (Has("item-use")) { var option = session.ReadView().Actions.Last(a => a.Id == "item-use" && a.Enabled); Send(option.Id, option.StringValue, option.IntValue); }
                else session.Advance(0);
            }
            TickUntil(() => State<SessionState>("session").DayStep == 2);
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(400));
            Restart(); TickUntil(() => State<SessionState>("session").DayStep == 5);
            Assert.That(State<BodyState>("body").StressMicro, Is.Zero);
            Restart(); TickUntil(() => State<SessionState>("session").Phase == "AwaitNextDay");
            Assert.That(State<BodyState>("body").StressMicro, Is.EqualTo(8000000));
            Restart(); for (int i = 0; i < 30; i++) session.Advance(.016);
            Assert.That(State<SessionState>("session").Day, Is.EqualTo(1));
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(400));
            var next = Send("next-day"); session.Advance(0); Restart();
            Assert.That(session.Execute(next).Accepted, Is.True);
            TickUntil(() => State<SessionState>("session").Phase == "Anchors");
            Assert.That(State<SessionState>("session").Day, Is.EqualTo(2));
            Assert.That(State<LifeActorState>("life").Nutrition.IntakeKcal, Is.Zero);
        }

        [Test] public void MealRetryAfterMovingCursorDoesNotChargeAgainAndConflictIsRejected()
        {
            Start(restDay: true); TickUntil(() => Has("meal"));
            var meal = Send("meal", "porridge_egg");
            TickUntil(() => State<SessionState>("session").AnchorIndex > 0); Restart();
            Assert.That(session.Execute(meal).Accepted, Is.True);
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(290));
            Assert.That(State<LifeActorState>("life").Nutrition.IntakeKcal, Is.EqualTo(500));
            meal.StringValue = "large_breakfast";
            Assert.That(session.Execute(meal).Accepted, Is.False);
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(290));
        }

        [Test] public void LivePauseRequiresMatchingInputRebindAndPreservesCurrentAttempt()
        {
            Start(); ReachPreSet(); Send("begin-set", "manual"); Send("bench-left-down"); session.Advance(.5);
            var before = session.ReadView().BenchPress;
            session.SetSuspended(true); session.Advance(15);
            Assert.That(session.ReadView().BenchPress.Progress, Is.EqualTo(before.Progress));
            session.SetSuspended(false); session.Advance(1);
            Assert.That(session.ReadView().BenchPress.Balance, Is.EqualTo(before.Balance));
            Assert.That(session.Execute(Command("bench-rebind", number: 0)).Accepted, Is.False);
            Send("bench-rebind", number: 1); session.Advance(.350);
            Assert.That(session.ReadView().BenchPress.Progress, Is.EqualTo(before.Progress));
            session.Advance(.001);
            Assert.That(session.ReadView().BenchPress.Progress, Is.GreaterThan(before.Progress));
        }

        [Test] public void AuxiliaryReadsDoNotRerollOrStartAnEntryTwice()
        {
            Start(); ReachPreSet();
            string flowBefore = session.InspectSnapshot().FindFragment("flow", "player").Json;
            for (int i = 0; i < 30; i++) session.ReadView();
            Restart();
            Assert.That(session.InspectSnapshot().FindFragment("flow", "player").Json, Is.EqualTo(flowBefore));
        }

        [Test] public void MenuCancelPreservesRunAndConfirmedReplacementIsAtomicAndRetryable()
        {
            Start(restDay: true); TickUntil(() => Has("meal")); Send("meal", "porridge_egg");
            string previousRun = session.InspectSnapshot().RunId;
            long balance = State<LifeActorState>("life").Wallet.Balance;
            Send("open-menu");
            Assert.That(session.ReadView().Actions.Single(a => a.Id == "new-game" && a.StringValue == TemplateCatalog.DefaultId).RequiresConfirmation, Is.True);
            // Cancelling the UI confirmation sends no business command.
            Send("continue-game");
            Assert.That(session.InspectSnapshot().RunId, Is.EqualTo(previousRun));
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(balance));
            Send("open-menu"); Send("new-game", TemplateCatalog.DefaultId);
            Assert.That(session.InspectSnapshot().RunId, Is.EqualTo(previousRun));
            var replacement = Send("save-plan", JsonUtility.ToJson(new PlanDraft { Mode = PlanMode.DayCycle, Slots = new[] { PlanCatalog.Rest(0) } }));
            string newRun = session.InspectSnapshot().RunId;
            Assert.That(newRun, Is.Not.EqualTo(previousRun));
            Assert.That(State<SessionState>("session").Phase, Is.EqualTo("Anchors"));
            Assert.That(State<PlanStateRecord>("plan").HasActive, Is.True);
            Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(300));
            Restart(); Assert.That(session.Execute(replacement).Accepted, Is.True);
            Assert.That(session.InspectSnapshot().RunId, Is.EqualTo(newRun));
            Assert.That(State<LifeActorState>("life").Nutrition.Meals.Length, Is.Zero);
        }

        [Test] public void CorruptCurrentRootCanBeExplicitlyReplacedWhileOriginalBytesArePreserved()
        {
            Start(restDay: true);
            long generation = session.InspectSnapshot().Generation;
            string filename = generation % 2 == 0 ? "state-a.bin" : "state-b.bin";
            session.Dispose(); File.WriteAllBytes(Path.Combine(directory, filename), new byte[] { 7, 8, 9 });
            session = GameSession.OpenTest(directory, 1);
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("error"));
            Assert.That(session.ReadView().Actions.Single(a => a.Id == "new-game").RequiresConfirmation, Is.True);
            Send("open-menu"); Assert.That(File.ReadAllBytes(Path.Combine(directory, filename)), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Send("new-game", TemplateCatalog.DefaultId);
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("InitialPlan"));
            Assert.That(File.ReadAllBytes(Path.Combine(directory, filename)), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Send("save-plan", JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()));
            Assert.That(State<SessionState>("session").Phase, Is.EqualTo("Anchors"));
            string backup = Directory.GetDirectories(Path.GetDirectoryName(directory), Path.GetFileName(directory) + ".corrupt-*").Single();
            Assert.That(File.ReadAllBytes(Path.Combine(backup, filename)), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Restart(); Assert.That(State<LifeActorState>("life").Wallet.Balance, Is.EqualTo(300));
        }

        [Test] public void NewGameDraftCancelInvalidPlanAndExitNeverReplaceExistingRun()
        {
            Start(restDay: true); TickUntil(() => Has("meal")); Send("meal", "porridge_egg");
            string before = JsonUtility.ToJson(session.InspectSnapshot());
            Send("open-menu"); Send("new-game", TemplateCatalog.DefaultId);
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("InitialPlan"));
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(before));
            var invalid = PlanCatalog.CreateBenchPressSlice(); invalid.Slots[0].AnchorId = "breakfast";
            Assert.That(session.Execute(Command("save-plan", JsonUtility.ToJson(invalid))).Accepted, Is.False);
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(before));
            Send("cancel-new-game"); Send("continue-game");
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(before));
            Send("open-menu"); Send("new-game", TemplateCatalog.DefaultId); Restart();
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(before));
        }

        [Test] public void FirstGameIsNotPublishedWithoutAValidInitialPlan()
        {
            Send("new-game", TemplateCatalog.DefaultId);
            Assert.That(session.InspectSnapshot().Generation, Is.Zero);
            Assert.That(session.InspectSnapshot().FindFragment("session", "player"), Is.Null);
            Assert.That(session.Execute(Command("save-plan", "{}")).Accepted, Is.False);
            Assert.That(session.InspectSnapshot().Generation, Is.Zero);
            Send("save-plan", JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()));
            Assert.That(session.InspectSnapshot().Generation, Is.EqualTo(1));
            Assert.That(State<PlanStateRecord>("plan").HasActive, Is.True);
            Assert.That(State<TrainingDayState>("training").HasQuota, Is.True);
        }

        [TestCase(StorageFaultPoint.BeforeRootWrite)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement)]
        public void NewRunFaultRestoresEitherOldCompleteRunOrNewRunWithEffectivePlan(StorageFaultPoint point)
        {
            Start(restDay: true); string previous = session.InspectSnapshot().RunId; session.Dispose();
            bool fired = false;
            session = GameSession.OpenTest(directory, 1, p => { if (!fired && p == point) { fired = true; throw new IOException("NewRun fault"); } });
            Send("new-game", TemplateCatalog.DefaultId);
            var final = Command("save-plan", JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()));
            Assert.That(session.Execute(final).Accepted, Is.False);
            Restart();
            if (point == StorageFaultPoint.BeforeRootWrite) {
                Assert.That(session.InspectSnapshot().RunId, Is.EqualTo(previous));
                Assert.That(State<PlanStateRecord>("plan").Active.Draft.Slots[0].IsRest, Is.True);
            } else {
                Assert.That(session.InspectSnapshot().RunId, Is.Not.EqualTo(previous));
                Assert.That(State<SessionState>("session").Phase, Is.EqualTo("Anchors"));
                Assert.That(State<PlanStateRecord>("plan").HasActive, Is.True);
                Assert.That(State<TrainingDayState>("training").HasQuota, Is.True);
                Assert.That(session.Execute(final).Accepted, Is.True);
            }
        }
    }
}
