using System;
using System.Collections.Generic;
using System.IO;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Progression;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class EndingProgressionTests
    {
        private readonly UnityJsonStateCodec codec = new UnityJsonStateCodec();
        private EndingProgression Rules { get { return new EndingProgression(codec); } }
        private static AchievementSnapshot Snapshot()
        {
            return new AchievementSnapshot { RunId = "run", Day = 42,
                DeviceTimeIso8601 = "2026-10-23T12:00:00.0000000+08:00", UtcOffsetMinutes = 480,
                ContentVersion = "fixture-v1", CharacterAndMusclesJson = "{\"strength\":10}",
                TrainingPlanJson = "{\"plan\":1}", EvidenceJson = "{\"factId\":\"fixture-fact\"}" };
        }
        private ProgressionTransition Trigger(bool terminal = false, bool segments = true)
        {
            var catalog = EndingProgression.DefaultCatalog();
            if (!segments) foreach (var definition in catalog) definition.PresentationSegments.Clear();
            return Rules.Check(Rules.CreateRun("run"), new ProgressionPermanent(), catalog,
                CheckKind.NormalCompletion, CommandSource.Test, "completed-1",
                key => key == (terminal ? "competition.non_champion" : "competition.bodybuilding_champion"),
                Snapshot(), "{\"phase\":\"Settle\",\"setId\":\"saved-result\"}");
        }
        private ProgressionTransition Present(ProgressionTransition state)
        {
            while (state.Run.Active.Phase == EndingPhase.Presenting)
                state = Rules.ConfirmSegment(state.Run, state.Permanent, state.Run.Active.AchievementId,
                    state.Run.Active.SegmentIds[state.Run.Active.NextSegmentIndex]);
            return state;
        }

        [Test] public void DefaultOrderFirstMatchAndSnapshotAreFrozenWithoutMutatingInputs()
        {
            var catalog = EndingProgression.DefaultCatalog(); catalog.Reverse();
            var run = Rules.CreateRun("run"); var permanent = new ProgressionPermanent(); var snapshot = Snapshot();
            var result = Rules.Check(run, permanent, catalog, CheckKind.NormalCompletion, CommandSource.Test,
                "cp", key => true, snapshot, "continuation");
            Assert.That(result.Run.Active.EndingId, Is.EqualTo("competition_failed"));
            Assert.That(result.Run.TerminalLock, Is.True); Assert.That(result.Run.TriggeredEndingIds.Count, Is.EqualTo(1));
            Assert.That(run.TriggeredEndingIds, Is.Empty); Assert.That(permanent.TemplateGrants, Is.Empty);
            snapshot.TrainingPlanJson = "changed"; catalog[4].Title = "changed";
            var frozen = codec.Decode<AchievementSnapshot>(result.Appends[0].Json);
            Assert.That(frozen.EndingTitle, Is.EqualTo("比赛失败")); Assert.That(frozen.TrainingPlanJson, Is.EqualTo("{\"plan\":1}"));
            Assert.That(frozen.DeviceTimeIso8601, Is.EqualTo("2026-10-23T12:00:00.0000000+08:00"));
        }

        [Test] public void ContinueDoesNotRescanCheckpointAndNextRealCompletionAwardsNextOnce()
        {
            var state = Trigger(false, false); string achievement = state.Run.Active.AchievementId;
            state = Rules.CommitNote(state.Run, state.Permanent, achievement, "", true);
            state = Rules.Continue(state.Run, state.Permanent, achievement);
            Assert.That(state.HasResume, Is.True);
            Assert.That(state.ResumeJson, Is.EqualTo("{\"phase\":\"Settle\",\"setId\":\"saved-result\"}"));
            state = Rules.Check(state.Run, state.Permanent, EndingProgression.DefaultCatalog(), CheckKind.NormalCompletion,
                CommandSource.Test, "completed-1", key => { throw new Exception("same checkpoint must not scan"); }, Snapshot(), "other");
            Assert.That(state.Changed, Is.False); Assert.That(state.Appends, Is.Empty);
            state = Rules.Check(state.Run, state.Permanent, EndingProgression.DefaultCatalog(), CheckKind.NormalCompletion,
                CommandSource.Test, "completed-2", key => key.EndsWith("champion") && !key.Contains("non_champion"), Snapshot(), "day-end-E04");
            Assert.That(state.Run.Active.EndingId, Is.EqualTo("lean_champion"));
            Assert.That(state.Run.TriggeredEndingIds.Count, Is.EqualTo(2));
            Assert.That(state.Permanent.TemplateGrants.Count, Is.EqualTo(2));
        }

        [Test] public void JsonPresentationAndDraftResumeAtUnfinishedSegmentAndDoNotAwardAgain()
        {
            var catalog = EndingProgression.DefaultCatalog();
            catalog[2].PresentationSegments = new List<string> { "first", "second" };
            var state = Rules.Check(Rules.CreateRun("run"), new ProgressionPermanent(), catalog,
                CheckKind.NormalCompletion, CommandSource.Test, "cp", k => k == "competition.bodybuilding_champion", Snapshot(), "next");
            string id = state.Run.Active.AchievementId;
            Assert.Throws<InvalidOperationException>(() => Rules.ConfirmSegment(state.Run, state.Permanent, id, "second"));
            state = Rules.ConfirmSegment(state.Run, state.Permanent, id, "first");
            state.Run = codec.Decode<ProgressionRun>(codec.Encode(state.Run));
            state.Permanent = codec.Decode<ProgressionPermanent>(codec.Encode(state.Permanent));
            Rules.Validate(state.Run, state.Permanent);
            Assert.That(state.Run.Active.NextSegmentIndex, Is.EqualTo(1));
            Assert.That(Rules.ConfirmSegment(state.Run, state.Permanent, id, "first").Changed, Is.False);
            state = Rules.ConfirmSegment(state.Run, state.Permanent, id, "second");
            Assert.That(state.Appends, Is.Empty);
            state = Rules.SaveDraft(state.Run, state.Permanent, id, "未提交草稿");
            state.Run = codec.Decode<ProgressionRun>(codec.Encode(state.Run));
            Assert.That(state.Run.Active.NoteDraft, Is.EqualTo("未提交草稿"));
            Assert.That(state.Run.Active.NoteState, Is.EqualTo(AchievementNoteState.Pending));
            Assert.Throws<InvalidOperationException>(() => Rules.Continue(state.Run, state.Permanent, id));
        }

        [TestCase(true)] [TestCase(false)]
        public void FinalNoteIsExplicitOnceAndImmutable(bool leaveEmpty)
        {
            var state = Present(Trigger()); string id = state.Run.Active.AchievementId;
            Assert.Throws<InvalidOperationException>(() => Rules.CommitNote(state.Run, state.Permanent, id, " ", false));
            Assert.Throws<ArgumentException>(() => Rules.SaveDraft(state.Run, state.Permanent, id, new string('字', 201)));
            state = Rules.CommitNote(state.Run, state.Permanent, id, leaveEmpty ? "" : "记录", leaveEmpty);
            state.Run = codec.Decode<ProgressionRun>(codec.Encode(state.Run)); Rules.Validate(state.Run, state.Permanent);
            Assert.That(state.Run.Active.Phase, Is.EqualTo(EndingPhase.MainMenu));
            Assert.That(state.Run.Active.NoteState, Is.EqualTo(leaveEmpty ? AchievementNoteState.CommittedEmpty : AchievementNoteState.CommittedText));
            Assert.Throws<InvalidOperationException>(() => Rules.CommitNote(state.Run, state.Permanent, id, "改写", false));
            Assert.Throws<InvalidOperationException>(() => Rules.SaveDraft(state.Run, state.Permanent, id, "改写"));
        }

        [Test] public void TerminalJsonRestoreCannotResumeAndWrongAchievementCannotEdit()
        {
            var state = Present(Trigger(true)); string id = state.Run.Active.AchievementId;
            state = Rules.CommitNote(state.Run, state.Permanent, id, "", true);
            state.Run = codec.Decode<ProgressionRun>(codec.Encode(state.Run)); Rules.Validate(state.Run, state.Permanent);
            Assert.Throws<InvalidOperationException>(() => Rules.Continue(state.Run, state.Permanent, id));
            Assert.Throws<InvalidOperationException>(() => Rules.SaveDraft(state.Run, state.Permanent, "wrong", "text"));
            state.Run.TerminalLock = false;
            Assert.Throws<InvalidOperationException>(() => Rules.Validate(state.Run, state.Permanent));
        }

        [Test] public void NpcAndNonNormalChecksCannotAwardPlayerHistory()
        {
            var run = Rules.CreateRun("run"); var permanent = new ProgressionPermanent();
            foreach (var kind in new[] { CheckKind.None, CheckKind.EndingPresentation })
            {
                var result = Rules.Check(run, permanent, null, kind, CommandSource.Player, null, null, null, null);
                Assert.That(result.Changed, Is.False); Assert.That(result.Appends, Is.Empty);
            }
            Assert.Throws<InvalidOperationException>(() => Rules.Check(run, permanent, EndingProgression.DefaultCatalog(),
                CheckKind.NormalCompletion, CommandSource.NpcProduction, "cp", k => true, Snapshot(), "next"));
        }

        [Test] public void CorruptPresenceCursorCatalogAndTimestampAreRejected()
        {
            var state = Trigger(); state.Run.HasActive = false;
            Assert.Throws<InvalidOperationException>(() => Rules.Validate(codec.Decode<ProgressionRun>(codec.Encode(state.Run)), state.Permanent));
            state = Trigger(); state.Run.Active.NextSegmentIndex = 100;
            Assert.Throws<InvalidOperationException>(() => Rules.Validate(state.Run, state.Permanent));
            var catalog = EndingProgression.DefaultCatalog(); catalog[0].Order = catalog[1].Order;
            Assert.Throws<InvalidOperationException>(() => Rules.Check(Rules.CreateRun("run"), new ProgressionPermanent(), catalog,
                CheckKind.NormalCompletion, CommandSource.Test, "cp", k => true, Snapshot(), "next"));
            var snapshot = Snapshot(); snapshot.UtcOffsetMinutes = 0;
            Assert.Throws<InvalidOperationException>(() => Rules.Check(Rules.CreateRun("run"), new ProgressionPermanent(), EndingProgression.DefaultCatalog(),
                CheckKind.NormalCompletion, CommandSource.Test, "cp", k => true, snapshot, "next"));
        }

        [Test] public void TemplateFirstEvidenceSurvivesTwoSuccessesAndNewRun()
        {
            var catalog = EndingProgression.DefaultCatalog();
            var first = Rules.Check(Rules.CreateRun("run"), new ProgressionPermanent(), catalog, CheckKind.NormalCompletion,
                CommandSource.Test, "cp", k => k == "competition.lean_champion", Snapshot(), "next");
            first = Present(first); string firstId = first.Run.Active.AchievementId;
            first = Rules.CommitNote(first.Run, first.Permanent, firstId, "", true);
            first = Rules.Continue(first.Run, first.Permanent, firstId);
            var second = Rules.Check(first.Run, first.Permanent, catalog, CheckKind.NormalCompletion, CommandSource.Test,
                "cp2", k => k == "relationship.romance_success", Snapshot(), "next2");
            Assert.That(second.Permanent.TemplateGrants.Count, Is.EqualTo(1));
            Assert.That(second.Permanent.TemplateGrants[0].AchievementId, Is.EqualTo(firstId));
            var fresh = Rules.CreateRun("new-run"); Rules.Validate(fresh, second.Permanent);
            Assert.That(fresh.TriggeredEndingIds, Is.Empty); Assert.That(fresh.HasActive, Is.False);
        }

        private RootEnvelope Root()
        {
            var root = new RootEnvelope { RunId = "run", ContentVersion = "fixture-v1" };
            Apply(root, new ProgressionTransition { Run = Rules.CreateRun("run"), Permanent = new ProgressionPermanent() });
            return root;
        }
        private void Apply(RootEnvelope root, ProgressionTransition transition)
        {
            root.SetFragment("progression", "player", 1, root.Revision + 1, codec.Encode(transition.Run));
            root.PermanentFragments.RemoveAll(f => f.Owner == "progression");
            root.PermanentFragments.Add(new VersionedFragment { Owner = "progression", Scope = "permanent", SchemaVersion = 1,
                Revision = root.Revision + 1, Json = codec.Encode(transition.Permanent) });
        }
        private static CommandEnvelope Command(string id, long revision, string payload = "trigger")
        { return new CommandEnvelope { OperationId = id, ExpectedRevision = revision, RunId = "run", ActorId = "player",
            ActionKind = "progression-fixture", Payload = payload, Source = CommandSource.Test }; }
        private PreparedOutcome TriggerInto(RootEnvelope root)
        {
            var transition = Trigger(false, false); Apply(root, transition);
            return new PreparedOutcome { SemanticKey = "achievement/fixture", EventDisposition = EventDisposition.AuxiliaryOnly,
                CheckKind = CheckKind.None, Appends = transition.Appends };
        }

        [TestCase(StorageFaultPoint.BeforeRootWrite, false)]
        [TestCase(StorageFaultPoint.BeforeAcknowledgement, true)]
        public void UnknownAchievementWriteRecoversWholeHistoryGrantAndRunOnce(StorageFaultPoint point, bool committed)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ProgressionTests", Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new LocalCheckpointStore(directory, codec))
                {
                    var dispatch = new SerialCommandDispatcher(store, codec, Root(), c => c.Source == CommandSource.Test, null);
                    Assert.That(dispatch.Execute(Command("baseline", 0), r => new PreparedOutcome { SemanticKey = "baseline" }).Status, Is.EqualTo(CommandStatus.Committed));
                }
                using (var store = new LocalCheckpointStore(directory, codec, p => { if (p == point) throw new IOException("injected"); }))
                {
                    var dispatch = new SerialCommandDispatcher(store, codec, store.Load().Root, c => c.Source == CommandSource.Test, null);
                    Assert.That(dispatch.Execute(Command("award", 1), c => "achievement/fixture", TriggerInto).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
                    Assert.That(dispatch.Execute(Command("other", 1), c => "achievement/fixture", TriggerInto).Status, Is.EqualTo(CommandStatus.StorageOutcomeUnknown));
                }
                using (var store = new LocalCheckpointStore(directory, codec))
                {
                    var loaded = store.Load(); Assert.That(loaded.Status, Is.EqualTo(LoadStatus.Loaded));
                    var run = codec.Decode<ProgressionRun>(loaded.Root.FindFragment("progression", "player").Json);
                    var permanent = codec.Decode<ProgressionPermanent>(loaded.Root.PermanentFragments.Find(f => f.Owner == "progression").Json);
                    Assert.That(run.HasActive, Is.EqualTo(committed));
                    Assert.That(permanent.TemplateGrants.Count, Is.EqualTo(committed ? 1 : 0));
                    Assert.That(Rules.ReadHistory(store, 0, 64).Count, Is.EqualTo(committed ? 1 : 0));
                    var dispatch = new SerialCommandDispatcher(store, codec, loaded.Root, c => c.Source == CommandSource.Test, null);
                    Assert.That(dispatch.Execute(Command("award", loaded.Root.Revision), c => "achievement/fixture", TriggerInto).Status,
                        Is.EqualTo(committed ? CommandStatus.AlreadyCommitted : CommandStatus.Committed));
                    Assert.That(Rules.ReadHistory(store, 0, 64).Count, Is.EqualTo(1));
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock"); }
        }

        [Test] public void FinalNoteSemanticReplayAndNewRunPreserveQueryableImmutableHistory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ProgressionTests", Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new LocalCheckpointStore(directory, codec))
                {
                    var dispatch = new SerialCommandDispatcher(store, codec, Root(), c => c.Source == CommandSource.Test, null);
                    Assert.That(dispatch.Execute(Command("award", 0), c => "achievement/fixture", TriggerInto).Status, Is.EqualTo(CommandStatus.Committed));
                    Func<RootEnvelope, PreparedOutcome> note = r => {
                        var run = codec.Decode<ProgressionRun>(r.FindFragment("progression", "player").Json);
                        var permanent = codec.Decode<ProgressionPermanent>(r.PermanentFragments.Find(f => f.Owner == "progression").Json);
                        var change = Rules.CommitNote(run, permanent, run.Active.AchievementId, "最终备注", false); Apply(r, change);
                        return new PreparedOutcome { SemanticKey = "note/fixture", Appends = change.Appends };
                    };
                    Assert.That(dispatch.Execute(Command("note", 1, "最终备注"), c => "note/fixture", note).Status, Is.EqualTo(CommandStatus.Committed));
                    Assert.That(dispatch.Execute(Command("retry-note", 0, "最终备注"), c => "note/fixture", note).Status, Is.EqualTo(CommandStatus.AlreadyCommitted));
                    Assert.That(dispatch.Execute(Command("conflict", 0, "改写"), c => "note/fixture", note).Error, Is.EqualTo("SemanticConflict"));
                    var fresh = new RootEnvelope { RunId = "new-run", ContentVersion = "fixture-v1" };
                    fresh.SetFragment("progression", "player", 1, 0, codec.Encode(Rules.CreateRun("new-run")));
                    var start = Command("new-run", dispatch.Snapshot.Revision); start.ActionKind = "BeginNewRun";
                    Assert.That(dispatch.BeginNewRun(start, fresh).Status, Is.EqualTo(CommandStatus.Committed));
                    var history = Rules.ReadHistory(store, 0, 1);
                    Assert.That(history.Count, Is.EqualTo(1)); Assert.That(history[0].Note, Is.EqualTo("最终备注"));
                    Assert.That(history[0].Snapshot.RunId, Is.EqualTo("run"));
                    Assert.That(history[0].Snapshot.TrainingPlanJson, Is.EqualTo("{\"plan\":1}"));
                    Assert.That(dispatch.Snapshot.PermanentFragments.Count, Is.EqualTo(1));
                    Assert.That(Rules.ReadHistory(store, 1, 1), Is.Empty);
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); if (File.Exists(directory + ".writer-lock")) File.Delete(directory + ".writer-lock"); }
        }
    }
}
