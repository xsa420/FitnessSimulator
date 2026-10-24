using System;
using System.Collections.Generic;
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
    // Historical records are explicit storage fixtures, not evidence of naturally achieved endings.
    public sealed class GameSessionHistoryTests
    {
        private string directory;
        private GameSession session;
        private readonly UnityJsonStateCodec codec = new UnityJsonStateCodec();
        [SetUp] public void Setup()
        { directory = Path.Combine(Path.GetTempPath(), "Fitness-History-Tests", Guid.NewGuid().ToString("N")); session = GameSession.OpenTest(directory, 1); }
        [TearDown] public void Cleanup()
        {
            session?.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock");
        }
        private FrontendAction Action(string id, string value = null, int number = 0)
        {
            var view = session.ReadView();
            return new FrontendAction { ActionId = id, StringValue = value, IntValue = number, RunId = view.RunId,
                ExpectedRevision = view.Revision, OperationId = Guid.NewGuid().ToString("N") };
        }
        private void Send(string id, string value = null, int number = 0)
        { var reply = session.Execute(Action(id, value, number)); Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message); }
        private void Choose(FrontendOption option) { Send(option.Id, option.StringValue, option.IntValue); }
        private bool Has(string id) => session.ReadView().Actions.Any(a => a.Id == id && a.Enabled);
        private void Start(string template = null)
        { Send("new-game", template ?? TemplateCatalog.DefaultId); Send("save-plan", JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice())); }
        private string Saved() => codec.Encode(session.InspectSnapshot());
        private void CommitFixture(Action<RootEnvelope, List<LogAppend>> change)
        {
            session.Dispose(); session = null;
            using (var store = new LocalCheckpointStore(directory, codec))
            {
                var root = store.Load().Root; var appends = new List<LogAppend>(); change(root, appends);
                string id = Guid.NewGuid().ToString("N");
                Assert.That(store.Commit(root.Generation, root, new OperationReceipt { OperationId = id, RunId = root.RunId,
                    PayloadHash = "history-fixture", SemanticKey = "fixture/" + id }, appends).Status, Is.EqualTo(CommitStatus.Committed));
            }
            session = GameSession.OpenTest(directory, 1);
        }
        private void SeedPastRecords(int count)
        {
            CommitFixture((root, appends) => {
                var rules = new EndingProgression(codec); var permanent = new ProgressionPermanent();
                for (int i = 0; i < count; i++)
                {
                    string pastRun = "past-run-" + i;
                    var plan = codec.Decode<PlanStateRecord>(root.FindFragment("plan", "player").Json); plan.RunId = pastRun;
                    var catalog = EndingProgression.DefaultCatalog(); foreach (var d in catalog) d.PresentationSegments.Clear();
                    var triggered = rules.Check(rules.CreateRun(pastRun), permanent, catalog, CheckKind.NormalCompletion, CommandSource.Test,
                        "fixture-check", k => k == "competition.bodybuilding_champion", new AchievementSnapshot { RunId = pastRun, Day = 42 + i,
                            DeviceTimeIso8601 = "2026-10-23T12:34:56.0000000+05:30", UtcOffsetMinutes = 330, ContentVersion = root.ContentVersion,
                            CharacterAndMusclesJson = root.FindFragment("body", "player").Json, TrainingPlanJson = codec.Encode(plan), EvidenceJson = "{\"fixture\":true}" }, "fixture-continuation");
                    appends.AddRange(triggered.Appends); permanent = triggered.Permanent;
                    if (i % 3 != 0)
                    {
                        var note = rules.CommitNote(triggered.Run, permanent, triggered.Run.Active.AchievementId, i % 3 == 1 ? "" : "历史备注" + i, i % 3 == 1);
                        appends.AddRange(note.Appends);
                    }
                }
                root.PermanentFragments.RemoveAll(f => f.Owner == "progression");
                root.PermanentFragments.Add(new VersionedFragment { Owner = "progression", Scope = "permanent", SchemaVersion = 1,
                    Revision = root.Revision + 1, Json = codec.Encode(permanent) });
            });
        }

        [Test] public void UnknownCatalogIsReadOnlyAndCloseReturnsOriginalPage()
        {
            string before = Saved(); string originalScreen = session.ReadView().ScreenId;
            Send("history-open"); var view = session.ReadView();
            Assert.That(view.ScreenId, Is.EqualTo("history-catalog"));
            Assert.That(view.Body.Split('\n').Length, Is.EqualTo(5));
            Assert.That(view.Body.Split('\n').All(line => line.EndsWith("未知结局")), Is.True);
            Assert.That(view.Body, Does.Not.Contain("competition."));
            Assert.That(view.Body, Does.Not.Contain("满好感"));
            session.Advance(30);
            Assert.That(session.Execute(Action("new-game", TemplateCatalog.DefaultId)).Accepted, Is.False);
            for (int i = 0; i < 5; i++) session.ReadView();
            Assert.That(Saved(), Is.EqualTo(before));
            Send("history-records"); Assert.That(session.ReadView().Body, Does.Contain("尚无达成记录"));
            Send("history-close"); Assert.That(session.ReadView().ScreenId, Is.EqualTo(originalScreen));
            Assert.That(Saved(), Is.EqualTo(before));
        }

        [Test] public void BoundedPagesExposeFrozenDetailTimePlanAndAllThreeNoteStates()
        {
            Start(); SeedPastRecords(18); string before = Saved();
            Send("history-open"); Assert.That(session.ReadView().Body, Does.Contain("健美冠军 · 已达成"));
            Assert.That(session.ReadView().Body, Does.Contain("恋爱成功 · 未知结局"));
            Send("history-records");
            Assert.That(session.ReadView().Actions.Count(a => a.Id == "history-detail"), Is.EqualTo(16));
            for (int index = 0; index < 3; index++)
            {
                Choose(session.ReadView().Actions.Single(a => a.Id == "history-detail" && a.IntValue == index));
                var detail = session.ReadView();
                Assert.That(detail.ScreenId, Is.EqualTo("history-detail"));
                Assert.That(detail.Body, Does.Contain("2026-10-23 12:34:56 +05:30"));
                Assert.That(detail.Body, Does.Contain("上胸：10 /"));
                Assert.That(detail.PlanText, Does.Contain("卧推"));
                Assert.That(detail.Body, Does.Contain(index == 0 ? "尚未提交备注" : index == 1 ? "已明确留空" : "历史备注2"));
                Assert.That(detail.EndingCanEditNote, Is.False);
                Assert.That(detail.Actions.All(a => a.Id.StartsWith("history-")), Is.True);
                Assert.That(session.Execute(Action("ending-note", "{}")).Accepted, Is.False);
                Send("history-back");
            }
            Choose(session.ReadView().Actions.Single(a => a.Id == "history-page" && a.IntValue == 1));
            Assert.That(session.ReadView().Actions.Count(a => a.Id == "history-detail"), Is.EqualTo(2));
            Assert.That(session.ReadView().Actions.Any(a => a.Id == "history-page" && a.IntValue == 2), Is.False);
            Assert.That(session.Execute(Action("history-page", number: 2)).Accepted, Is.False);
            Assert.That(Saved(), Is.EqualTo(before));
        }

        [Test] public void NewRunAndRestartRetainOldSnapshotWithoutLoadingOldRun()
        {
            Start(); SeedPastRecords(1); string oldCurrent = session.InspectSnapshot().RunId;
            Start(TemplateCatalog.StrengthId);
            Assert.That(session.InspectSnapshot().RunId, Is.Not.EqualTo(oldCurrent));
            session.Dispose(); session = GameSession.OpenTest(directory, 99); string before = Saved();
            Send("history-open"); Send("history-records"); Choose(session.ReadView().Actions.Single(a => a.Id == "history-detail"));
            Assert.That(session.ReadView().Body, Does.Contain("上胸：10 /"));
            Assert.That(session.ReadView().Body, Does.Not.Contain("上胸：13 /"));
            Assert.That(session.ReadView().Actions.Any(a => a.Id == "continue-game" || a.Id == "ending-continue"), Is.False);
            Send("history-close"); Assert.That(Saved(), Is.EqualTo(before));
        }

        [Test] public void PresentationAndPendingNoteCannotBeBypassedButEndingMenuCanBrowse()
        {
            Start();
            for (int i = 0; i < 3000 && !Has("meal"); i++) session.Advance(0);
            Assert.That(Has("meal"), Is.True, session.ReadView().Body);
            CommitFixture((root, appends) => {
                var relationship = RelationshipRules.Initialize(root.RunId, "player");
                relationship.Affections.Single(a => a.CharacterId == RelationshipRules.Beginner).Value = 100;
                relationship.PendingConfessionId = "fixture-gift";
                var prepared = ConfessionRules.Prepare(relationship, ConfessionRules.Freeze(relationship, 1000000, 1000000, 15000000), 0);
                root.SetFragment("relationships", "player", 1, root.Revision + 1, codec.Encode(prepared.Relationships));
            });
            Send("continue-game"); Send("meal", "skip");
            Assert.That(session.Execute(Action("history-open")).Accepted, Is.False);
            Choose(session.ReadView().Actions.Single(a => a.Id == "ending-segment"));
            Assert.That(session.Execute(Action("history-open")).Accepted, Is.False);
            Choose(session.ReadView().Actions.Single(a => a.Id == "ending-empty")); string before = Saved();
            Send("history-open"); session.Advance(30); Send("history-close");
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("ending-menu")); Assert.That(Saved(), Is.EqualTo(before));
        }

        [Test] public void PlayingCannotOpenHistoryOrPauseTheSetThroughAuxiliaryNavigation()
        {
            Start();
            for (int i = 0; i < 3000 && !Has("begin-set"); i++)
            {
                if (Has("meal")) Send("meal", "skip");
                else if (Has("continue")) Send("continue");
                else if (Has("choose-entry")) Send("choose-entry", "training-choice");
                else session.Advance(0);
            }
            Assert.That(Has("begin-set"), Is.True, session.ReadView().Body); Send("begin-set", "manual");
            string before = Saved(); Assert.That(session.Execute(Action("history-open")).Accepted, Is.False);
            Assert.That(session.ReadView().TrainingInput, Is.Not.Null); Assert.That(Saved(), Is.EqualTo(before));
        }
    }
}
