using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    // Controlled domain-fact fixtures verify application composition, not the full gift/romance route.
    public sealed class GameSessionProgressionTests
    {
        [Serializable] private sealed class Payload { public string AchievementId; public string SegmentId; public string Text; }
        private string directory;
        private GameSession session;
        private readonly UnityJsonStateCodec codec = new UnityJsonStateCodec();
        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "Fitness-Progression-Integration", Guid.NewGuid().ToString("N"));
            session = GameSession.OpenTest(directory, 1);
        }
        [TearDown] public void Cleanup()
        {
            session?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock");
        }
        private FrontendAction Action(string id, string value = null)
        {
            var view = session.ReadView();
            return new FrontendAction { ActionId = id, StringValue = value, RunId = view.RunId,
                ExpectedRevision = view.Revision, OperationId = Guid.NewGuid().ToString("N") };
        }
        private FrontendAction Send(string id, string value = null)
        {
            var action = Action(id, value); var result = session.Execute(action);
            Assert.That(result.Accepted, Is.True, id + ": " + result.Message); return action;
        }
        private FrontendAction EndingAction(string id, string text = null)
        {
            var option = session.ReadView().Actions.Single(a => a.Id == id);
            var payload = JsonUtility.FromJson<Payload>(option.StringValue);
            if (text != null) payload.Text = text;
            return Action(id, JsonUtility.ToJson(payload));
        }
        private FrontendAction SendEnding(string id, string text = null)
        { var action = EndingAction(id, text); var result = session.Execute(action); Assert.That(result.Accepted, Is.True, result.Message); return action; }
        private T State<T>(string owner) => JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner, "player").Json);
        private void StartAtMeal()
        {
            Send("new-game", TemplateCatalog.DefaultId);
            var plan = PlanCatalog.CreateBenchPressSlice(); plan.Slots[0] = PlanCatalog.Rest(0);
            Send("save-plan", JsonUtility.ToJson(plan));
            for (int i = 0; i < 3000 && !session.ReadView().Actions.Any(a => a.Id == "meal" && a.Enabled); i++) session.Advance(0);
            Assert.That(session.ReadView().Actions.Any(a => a.Id == "meal" && a.Enabled), Is.True,
                session.ReadView().ScreenId + ": " + session.ReadView().Body + " / " + session.Diagnostic);
        }
        private void StoreFixture(Action<RootEnvelope> mutate)
        {
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                var root = loaded.Root; mutate(root);
                string id = Guid.NewGuid().ToString("N");
                var saved = store.Commit(root.Generation, root, new OperationReceipt { RunId = root.RunId, OperationId = id,
                    CommitId = id, SemanticKey = "fixture/" + id, PayloadHash = "explicit-domain-fixture", Revision = root.Revision + 1 }, Array.Empty<LogAppend>());
                Assert.That(saved.Status, Is.EqualTo(CommitStatus.Committed));
            }
            session = GameSession.OpenTest(directory, 1);
        }
        private void TriggerRomanceFixture()
        {
            StartAtMeal();
            StoreFixture(root => {
                var relationships = RelationshipRules.Initialize(root.RunId, "player");
                relationships.Day = JsonUtility.FromJson<SessionState>(root.FindFragment("session", "player").Json).Day;
                relationships.Affections.Single(a => a.CharacterId == RelationshipRules.Beginner).Value = 100;
                relationships.PendingConfessionId = "fixture-gift-receipt";
                var snapshot = ConfessionRules.Freeze(relationships, 1000000, 1000000, 15000000);
                var result = ConfessionRules.Prepare(relationships, snapshot, 0);
                Assert.That(result.Receipt.Success, Is.True);
                root.SetFragment("relationships", "player", 1, root.Revision + 1, JsonUtility.ToJson(result.Relationships));
            });
            Send("continue-game"); Send("meal", "skip");
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-presentation"));
        }
        private string OrdinaryFragments()
        {
            return string.Join("|", session.InspectSnapshot().Fragments.Where(f => f.Owner != "progression")
                .OrderBy(f => f.Owner).ThenBy(f => f.Scope).Select(f => f.Owner + "/" + f.Scope + "/" + f.Revision + "/" + f.Json));
        }
        private void Restart()
        { session.Dispose(); session = GameSession.OpenTest(directory, 99); Assert.That(session.ReadView().ScreenId, Is.Not.EqualTo("error"), session.ReadView().Body); }

        [Test] public void SuccessPausesAllBusinessAndRestoresExactContinuationWithoutExtraCheck()
        {
            TriggerRomanceFixture(); string frozen = OrdinaryFragments(); long checks = State<SessionState>("session").CompletionChecks;
            session.Advance(30); Assert.That(OrdinaryFragments(), Is.EqualTo(frozen));
            Assert.That(session.Execute(Action("meal", "skip")).Accepted, Is.False);
            Assert.That(session.Execute(Action("save-plan", JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()))).Accepted, Is.False);
            Assert.That(session.Execute(Action("training-input", "{}")).Accepted, Is.False);
            Restart(); Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-presentation"));
            SendEnding("ending-segment"); SendEnding("ending-draft", "草稿恢复"); Restart();
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-note"));
            Assert.That(session.ReadView().EndingNoteDraft, Is.EqualTo("草稿恢复"));
            SendEnding("ending-empty"); Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-menu"));
            var continued = SendEnding("ending-continue");
            Assert.That(OrdinaryFragments(), Is.EqualTo(frozen));
            Assert.That(State<SessionState>("session").CompletionChecks, Is.EqualTo(checks));
            Assert.That(State<ProgressionRun>("progression").HasActive, Is.False);
            Assert.That(session.Execute(continued).Accepted, Is.True);
            Assert.That(State<ProgressionRun>("progression").TriggeredEndingIds.Count, Is.EqualTo(1));
        }

        [Test] public void FinalNoteEnforcesTwoHundredBoundaryAndSemanticConflictCannotRewrite()
        {
            TriggerRomanceFixture(); SendEnding("ending-segment");
            var tooLong = EndingAction("ending-note", new string('字', 201));
            Assert.That(session.Execute(tooLong).Accepted, Is.False);
            var emptyImplicit = EndingAction("ending-note", " "); Assert.That(session.Execute(emptyImplicit).Accepted, Is.False);
            var note = SendEnding("ending-note", new string('字', 200));
            var replay = new FrontendAction { ActionId = note.ActionId, StringValue = note.StringValue, RunId = note.RunId,
                ExpectedRevision = note.ExpectedRevision, OperationId = Guid.NewGuid().ToString("N") };
            Assert.That(session.Execute(replay).Accepted, Is.True);
            var payload = JsonUtility.FromJson<Payload>(note.StringValue); payload.Text = "不能改写";
            replay.OperationId = Guid.NewGuid().ToString("N"); replay.StringValue = JsonUtility.ToJson(payload);
            Assert.That(session.Execute(replay).Accepted, Is.False);
            Assert.That(State<ProgressionRun>("progression").Active.FinalNote, Is.EqualTo(new string('字', 200)));
        }

        [TestCase(StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement, true)]
        public void UnknownFinalNoteWriteRecoversExactlyOneFinalNote(StorageFaultPoint point, bool committed)
        {
            TriggerRomanceFixture(); SendEnding("ending-segment");
            var note = EndingAction("ending-note", "唯一备注");
            session.Dispose(); bool armed = true;
            session = GameSession.OpenTest(directory, 1, p => { if (armed && p == point) throw new IOException("note fault"); });
            Assert.That(session.Execute(note).Accepted, Is.False); armed = false;
            Restart();
            Assert.That(State<ProgressionRun>("progression").Active.NoteState,
                Is.EqualTo(committed ? AchievementNoteState.CommittedText : AchievementNoteState.Pending));
            Assert.That(session.Execute(note).Accepted, Is.True);
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                var history = new EndingProgression(codec).ReadHistory(store, 0, 64);
                Assert.That(history.Count, Is.EqualTo(1)); Assert.That(history[0].Note, Is.EqualTo("唯一备注"));
                Assert.That(store.ReadLog("permanent/history", 0, 64).Count(f => f.Kind == "NoteFinal.v1"), Is.EqualTo(1));
            }
        }

        [Test] public void UnlockedTemplateDraftPreservesCurrentRunAndFinalNewRunKeepsHistory()
        {
            TriggerRomanceFixture(); SendEnding("ending-segment"); SendEnding("ending-empty");
            var menu = session.ReadView();
            Assert.That(menu.Actions.Count(a => a.Id == "new-game"), Is.EqualTo(3));
            Assert.That(menu.Actions.Single(a => a.StringValue == TemplateCatalog.SocialId).Enabled, Is.True);
            Assert.That(menu.Actions.Single(a => a.StringValue == TemplateCatalog.StrengthId).Enabled, Is.False);
            string oldRun = session.InspectSnapshot().RunId; string oldRoot = codec.Encode(session.InspectSnapshot());
            Send("new-game", TemplateCatalog.SocialId);
            Assert.That(codec.Encode(session.InspectSnapshot()), Is.EqualTo(oldRoot));
            Send("cancel-new-game"); Assert.That(session.InspectSnapshot().RunId, Is.EqualTo(oldRun));
            Send("new-game", TemplateCatalog.SocialId);
            Send("save-plan", JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()));
            Assert.That(session.InspectSnapshot().RunId, Is.Not.EqualTo(oldRun));
            Assert.That(State<SessionState>("session").TemplateId, Is.EqualTo(TemplateCatalog.SocialId));
            Assert.That(State<ProgressionRun>("progression").TriggeredEndingIds, Is.Empty);
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                Assert.That(store.Load().Status, Is.EqualTo(LoadStatus.Loaded));
                Assert.That(new EndingProgression(codec).ReadHistory(store, 0, 64).Single().Snapshot.RunId, Is.EqualTo(oldRun));
            }
        }

        [Test] public void ChangedNormalContinuationIsRejectedOnRestore()
        {
            TriggerRomanceFixture();
            StoreFixture(root => {
                var ordinary = JsonUtility.FromJson<SessionState>(root.FindFragment("session", "player").Json);
                ordinary.DayStep++;
                root.SetFragment("session", "player", 1, root.Revision + 1, JsonUtility.ToJson(ordinary));
            });
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("error"));
        }

        [Test] public void OrdinaryCompletionWithoutDomainFactDoesNotInventEnding()
        {
            StartAtMeal(); Send("meal", "skip");
            var progression = State<ProgressionRun>("progression");
            Assert.That(progression.HasActive, Is.False); Assert.That(progression.TriggeredEndingIds, Is.Empty);
            Assert.That(progression.LastCheckedCheckpoint, Is.Not.Null.And.Not.Empty);
            Assert.That(session.InspectSnapshot().LogPrefixes.Any(p => p.StreamId == "permanent/history"), Is.False);
        }
    }
}
