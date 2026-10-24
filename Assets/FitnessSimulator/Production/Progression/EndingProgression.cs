using System;
using System.Collections.Generic;
using System.Globalization;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Configuration;

namespace FitnessSimulator.Game.Progression
{
    [Serializable] public sealed class EndingsDocument
    {
        public string Version;
        public List<EndingDefinition> Endings;
    }
    public enum EndingPhase { Presenting, NotePending, MainMenu }
    public enum AchievementNoteState { Pending, CommittedEmpty, CommittedText }

    [Serializable] public sealed class EndingDefinition
    {
        public string Id;
        public int Order;
        public string Title;
        public string ConditionKey;
        public bool Terminal;
        public List<string> PresentationSegments = new List<string>();
        public List<string> UnlockTemplateIds = new List<string>();
    }

    [Serializable] public sealed class AchievementSnapshot
    {
        public string AchievementId;
        public string RunId;
        public string EndingId;
        public string EndingTitle;
        public bool Terminal;
        public int Day;
        public string DeviceTimeIso8601;
        public int UtcOffsetMinutes;
        public string ContentVersion;
        public string CharacterAndMusclesJson;
        public string TrainingPlanJson;
        public string EvidenceJson;
    }

    [Serializable] public sealed class ActiveEnding
    {
        public string AchievementId;
        public string EndingId;
        public bool Terminal;
        public EndingPhase Phase;
        public List<string> SegmentIds = new List<string>();
        public int NextSegmentIndex;
        public string NormalContinuationJson;
        public AchievementNoteState NoteState;
        public string NoteDraft;
        public string FinalNote;
    }

    [Serializable] public sealed class ProgressionRun
    {
        public string RunId;
        public List<string> TriggeredEndingIds = new List<string>();
        public string LastCheckedCheckpoint;
        public bool TerminalLock;
        public bool HasActive;
        public ActiveEnding Active = new ActiveEnding();
    }

    [Serializable] public sealed class TemplateGrant
    {
        public string TemplateId;
        public string AchievementId;
    }

    [Serializable] public sealed class ProgressionPermanent
    {
        public List<TemplateGrant> TemplateGrants = new List<TemplateGrant>();
    }

    [Serializable] public sealed class AchievementNoteFinal
    {
        public string AchievementId;
        public AchievementNoteState State;
        public string Text;
    }

    public sealed class ProgressionTransition
    {
        public ProgressionRun Run;
        public ProgressionPermanent Permanent;
        public List<LogAppend> Appends = new List<LogAppend>();
        public bool Changed;
        public bool HasResume;
        public string ResumeJson;
    }

    public sealed class AchievementHistoryEntry
    {
        public AchievementSnapshot Snapshot;
        public AchievementNoteState NoteState;
        public string Note;
    }

    // The caller applies both candidates and append intents to its single existing transaction.
    public sealed class EndingProgression
    {
        private readonly IStateCodec codec;
        public EndingProgression(IStateCodec codec) { this.codec = codec ?? throw new ArgumentNullException(nameof(codec)); }
        public ProgressionRun CreateRun(string runId) { Required(runId); return new ProgressionRun { RunId = runId }; }
        public static List<EndingDefinition> DefaultCatalog()
        {
            var document = ContentRuntime.Current.Get<EndingsDocument>("endings");
            ValidateDocument(document, null, null, null);
            return document.Endings;
        }
        public static void ValidateContent(Func<string, bool> conditionExists = null, Func<string, bool> segmentExists = null, Func<string, bool> templateExists = null)
        { ValidateDocument(ContentRuntime.Current.Get<EndingsDocument>("endings"), conditionExists, segmentExists, templateExists); }
        private static void ValidateDocument(EndingsDocument document, Func<string, bool> conditionExists, Func<string, bool> segmentExists, Func<string, bool> templateExists)
        {
            if (document.Endings == null || document.Endings.Count == 0) throw new ContentException("endings", "$.Endings", "Nonempty ending catalog required.");
            var ids = new HashSet<string>(StringComparer.Ordinal); var orders = new HashSet<int>();
            for (int i = 0; i < document.Endings.Count; i++) {
                var ending = document.Endings[i]; string path = "$.Endings[" + i + "]";
                if (ending == null || string.IsNullOrWhiteSpace(ending.Id) || !ids.Add(ending.Id)) throw new ContentException("endings", path + ".Id", "Missing or duplicate ending identity.");
                if (!orders.Add(ending.Order)) throw new ContentException("endings", path + ".Order", "Duplicate ending order.");
                if (string.IsNullOrWhiteSpace(ending.Title)) throw new ContentException("endings", path + ".Title", "Missing display title.");
                if (string.IsNullOrWhiteSpace(ending.ConditionKey) || conditionExists != null && !conditionExists(ending.ConditionKey)) throw new ContentException("endings", path + ".ConditionKey", "Missing or unresolved condition reference.");
                ValidateReferences(ending.PresentationSegments, path + ".PresentationSegments", segmentExists, true);
                ValidateReferences(ending.UnlockTemplateIds, path + ".UnlockTemplateIds", templateExists, false);
            }
        }
        private static void ValidateReferences(List<string> values, string path, Func<string, bool> exists, bool nonempty)
        {
            if (values == null || nonempty && values.Count == 0) throw new ContentException("endings", path, "Explicit reference list required; presentation cannot be empty.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < values.Count; i++)
                if (string.IsNullOrWhiteSpace(values[i]) || !unique.Add(values[i]) || exists != null && !exists(values[i])) throw new ContentException("endings", path + "[" + i + "]", "Missing, duplicate or unresolved reference: " + values[i]);
        }

        // History stays in committed append logs. Only the requested snapshot page is retained here.
        public List<AchievementHistoryEntry> ReadHistory(ICheckpointStore store, int skip, int count)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (skip < 0 || count < 1 || count > 64) throw new ArgumentOutOfRangeException("History page must contain 1..64 entries.");
            var entries = new List<AchievementHistoryEntry>(); long offset = 0, seen = 0;
            while (true)
            {
                var frames = store.ReadLog("permanent/history", offset, 64);
                if (frames.Count == 0) break;
                foreach (var frame in frames)
                {
                    if (frame.Kind == "AchievementFrozen.v1")
                    {
                        if (seen++ < skip || entries.Count >= count) continue;
                        var snapshot = codec.Decode<AchievementSnapshot>(frame.Json);
                        ValidateSnapshotInput(snapshot, snapshot.RunId);
                        if (snapshot.AchievementId != AchievementKey(snapshot.RunId, snapshot.EndingId))
                            throw new InvalidOperationException("Corrupt achievement key.");
                        entries.Add(new AchievementHistoryEntry { Snapshot = snapshot, NoteState = AchievementNoteState.Pending, Note = "" });
                    }
                    else if (frame.Kind == "NoteFinal.v1")
                    {
                        var note = codec.Decode<AchievementNoteFinal>(frame.Json);
                        var entry = entries.Find(e => e.Snapshot.AchievementId == note.AchievementId);
                        if (entry == null) continue;
                        if (entry.NoteState != AchievementNoteState.Pending || note.Text == null
                            || note.State != AchievementNoteState.CommittedText && note.State != AchievementNoteState.CommittedEmpty
                            || note.State == AchievementNoteState.CommittedText && string.IsNullOrWhiteSpace(note.Text)
                            || note.State == AchievementNoteState.CommittedEmpty && note.Text.Length != 0)
                            throw new InvalidOperationException("Corrupt final note.");
                        CheckText(note.Text); entry.NoteState = note.State; entry.Note = note.Text;
                    }
                }
                offset += frames.Count;
            }
            return entries;
        }
        public static string AchievementKey(string runId, string endingId)
        { Required(runId); Required(endingId); return Field(runId) + Field(endingId); }

        public void Validate(ProgressionRun run, ProgressionPermanent permanent)
        {
            if (run == null || permanent == null) throw new InvalidOperationException("Missing progression state.");
            Required(run.RunId); Unique(run.TriggeredEndingIds); 
            if (permanent.TemplateGrants == null) throw new InvalidOperationException("Missing permanent grants.");
            var grants = new HashSet<string>(StringComparer.Ordinal);
            foreach (var grant in permanent.TemplateGrants)
            {
                if (grant == null) throw new InvalidOperationException("Missing grant.");
                Required(grant.TemplateId); Required(grant.AchievementId);
                if (!grants.Add(grant.TemplateId)) throw new InvalidOperationException("Duplicate template grant.");
            }
            if (!run.HasActive)
            {
                if (run.TerminalLock) throw new InvalidOperationException("Terminal run lost its ending.");
                // JsonUtility can materialize empty objects, but meaningful orphan state must not be discarded.
                if (run.Active != null && (!string.IsNullOrEmpty(run.Active.AchievementId) || !string.IsNullOrEmpty(run.Active.EndingId)
                    || !string.IsNullOrEmpty(run.Active.NormalContinuationJson) || run.Active.NextSegmentIndex != 0
                    || run.Active.SegmentIds != null && run.Active.SegmentIds.Count != 0
                    || !string.IsNullOrEmpty(run.Active.NoteDraft) || !string.IsNullOrEmpty(run.Active.FinalNote)
                    || run.Active.Terminal || run.Active.Phase != EndingPhase.Presenting || run.Active.NoteState != AchievementNoteState.Pending))
                    throw new InvalidOperationException("Orphan ending state.");
                return;
            }
            var active = run.Active;
            if (active == null || active.Terminal != run.TerminalLock || !run.TriggeredEndingIds.Contains(active.EndingId)
                || active.AchievementId != AchievementKey(run.RunId, active.EndingId)
                || !Enum.IsDefined(typeof(EndingPhase), active.Phase) || !Enum.IsDefined(typeof(AchievementNoteState), active.NoteState))
                throw new InvalidOperationException("Invalid active ending identity.");
            Required(run.LastCheckedCheckpoint); Required(active.NormalContinuationJson); Unique(active.SegmentIds);
            if (active.NextSegmentIndex < 0 || active.NextSegmentIndex > active.SegmentIds.Count
                || (active.Phase == EndingPhase.Presenting) != (active.NextSegmentIndex < active.SegmentIds.Count)
                || (active.Phase == EndingPhase.MainMenu) != (active.NoteState != AchievementNoteState.Pending))
                throw new InvalidOperationException("Inconsistent ending cursor.");
            CheckText(active.NoteDraft ?? ""); CheckText(active.FinalNote ?? "");
            if (active.NoteState == AchievementNoteState.CommittedText && string.IsNullOrWhiteSpace(active.FinalNote)
                || active.NoteState != AchievementNoteState.CommittedText && !string.IsNullOrEmpty(active.FinalNote)
                || active.NoteState != AchievementNoteState.Pending && !string.IsNullOrEmpty(active.NoteDraft))
                throw new InvalidOperationException("Invalid note state.");
        }

        public ProgressionTransition Check(ProgressionRun run, ProgressionPermanent permanent,
            IList<EndingDefinition> catalog, CheckKind checkKind, CommandSource source, string checkpointId,
            Func<string, bool> fulfilled, AchievementSnapshot snapshot, string normalContinuationJson)
        {
            var result = Copy(run, permanent);
            if (!Enum.IsDefined(typeof(CheckKind), checkKind) || !Enum.IsDefined(typeof(CommandSource), source))
                throw new InvalidOperationException("Invalid completion source.");
            if (checkKind != CheckKind.NormalCompletion) return result;
            if (source == CommandSource.NpcProduction) throw new InvalidOperationException("NPC cannot award player history.");
            Required(checkpointId);
            if (run.LastCheckedCheckpoint == checkpointId) return result;
            if (run.HasActive || run.TerminalLock) throw new InvalidOperationException("Resolve the active ending first.");
            if (fulfilled == null || catalog == null) throw new ArgumentNullException("Ending catalog/condition reader");
            var ordered = new List<EndingDefinition>(catalog);
            var orders = new HashSet<int>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in ordered)
            {
                if (definition == null) throw new InvalidOperationException("Missing ending definition.");
                Required(definition.Id); Required(definition.Title); Required(definition.ConditionKey);
                Unique(definition.PresentationSegments); Unique(definition.UnlockTemplateIds);
                if (!orders.Add(definition.Order) || !ids.Add(definition.Id)) throw new InvalidOperationException("Duplicate ending order/identity.");
            }
            ordered.Sort((a, b) => a.Order.CompareTo(b.Order));
            result.Run.LastCheckedCheckpoint = checkpointId; result.Changed = true;
            foreach (var definition in ordered)
            {
                if (run.TriggeredEndingIds.Contains(definition.Id) || !fulfilled(definition.ConditionKey)) continue;
                Required(normalContinuationJson);
                var frozen = Clone(snapshot ?? throw new InvalidOperationException("Missing achievement snapshot."));
                ValidateSnapshotInput(frozen, run.RunId);
                frozen.AchievementId = AchievementKey(run.RunId, definition.Id);
                frozen.EndingId = definition.Id; frozen.EndingTitle = definition.Title; frozen.Terminal = definition.Terminal;
                result.Run.TriggeredEndingIds.Add(definition.Id);
                result.Run.TerminalLock = definition.Terminal; result.Run.HasActive = true;
                result.Run.Active = new ActiveEnding { AchievementId = frozen.AchievementId, EndingId = definition.Id, Terminal = definition.Terminal,
                    SegmentIds = new List<string>(definition.PresentationSegments), NormalContinuationJson = normalContinuationJson,
                    Phase = definition.PresentationSegments.Count == 0 ? EndingPhase.NotePending : EndingPhase.Presenting };
                foreach (string template in definition.UnlockTemplateIds)
                    if (!result.Permanent.TemplateGrants.Exists(g => g.TemplateId == template))
                        result.Permanent.TemplateGrants.Add(new TemplateGrant { TemplateId = template, AchievementId = frozen.AchievementId });
                result.Appends.Add(Append("AchievementFrozen.v1", frozen));
                break;
            }
            Validate(result.Run, result.Permanent); return result;
        }

        public ProgressionTransition ConfirmSegment(ProgressionRun run, ProgressionPermanent permanent, string achievementId, string segmentId)
        {
            var result = ActiveCopy(run, permanent, achievementId); var active = result.Run.Active;
            int index = active.SegmentIds.IndexOf(segmentId);
            if (index < 0) throw new InvalidOperationException("Unknown presentation segment.");
            if (index < active.NextSegmentIndex) return result;
            if (active.Phase != EndingPhase.Presenting || index != active.NextSegmentIndex)
                throw new InvalidOperationException("Out of order presentation.");
            active.NextSegmentIndex++;
            if (active.NextSegmentIndex == active.SegmentIds.Count) active.Phase = EndingPhase.NotePending;
            result.Changed = true; Validate(result.Run, result.Permanent); return result;
        }

        public ProgressionTransition SaveDraft(ProgressionRun run, ProgressionPermanent permanent, string achievementId, string draft)
        {
            var result = ActiveCopy(run, permanent, achievementId); var active = result.Run.Active;
            if (active.Phase != EndingPhase.NotePending) throw new InvalidOperationException("Note is not editable.");
            if (draft == null) throw new ArgumentNullException(nameof(draft)); CheckText(draft);
            result.Changed = active.NoteDraft != draft; active.NoteDraft = draft; return result;
        }

        public ProgressionTransition CommitNote(ProgressionRun run, ProgressionPermanent permanent, string achievementId, string text, bool leaveEmpty)
        {
            var result = ActiveCopy(run, permanent, achievementId); var active = result.Run.Active;
            if (active.Phase != EndingPhase.NotePending || active.NoteState != AchievementNoteState.Pending)
                throw new InvalidOperationException("Note already committed or presentation unfinished.");
            if (text == null) throw new ArgumentNullException(nameof(text)); CheckText(text);
            if (leaveEmpty ? text.Length != 0 : string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Explicit empty choice or nonblank text required.");
            active.NoteState = leaveEmpty ? AchievementNoteState.CommittedEmpty : AchievementNoteState.CommittedText;
            active.FinalNote = text; active.NoteDraft = ""; active.Phase = EndingPhase.MainMenu;
            result.Appends.Add(Append("NoteFinal.v1", new AchievementNoteFinal { AchievementId = achievementId, State = active.NoteState, Text = text }));
            result.Changed = true; Validate(result.Run, result.Permanent); return result;
        }

        public ProgressionTransition Continue(ProgressionRun run, ProgressionPermanent permanent, string achievementId)
        {
            var result = ActiveCopy(run, permanent, achievementId);
            if (run.TerminalLock || run.Active.Phase != EndingPhase.MainMenu)
                throw new InvalidOperationException("Continue must restore presentation/note or is terminal.");
            result.HasResume = true; result.ResumeJson = run.Active.NormalContinuationJson;
            result.Run.HasActive = false; result.Run.Active = new ActiveEnding(); result.Changed = true;
            Validate(result.Run, result.Permanent); return result;
        }

        private ProgressionTransition Copy(ProgressionRun run, ProgressionPermanent permanent)
        { Validate(run, permanent); return new ProgressionTransition { Run = Clone(run), Permanent = Clone(permanent) }; }
        private ProgressionTransition ActiveCopy(ProgressionRun run, ProgressionPermanent permanent, string achievementId)
        {
            var result = Copy(run, permanent);
            if (!run.HasActive || run.Active.AchievementId != achievementId) throw new InvalidOperationException("Stale achievement.");
            return result;
        }
        private T Clone<T>(T value) { return codec.Decode<T>(codec.Encode(value)); }
        private LogAppend Append<T>(string kind, T value) { return new LogAppend { StreamId = "permanent/history", Kind = kind, Json = codec.Encode(value) }; }
        private static void ValidateSnapshotInput(AchievementSnapshot snapshot, string runId)
        {
            if (snapshot.RunId != runId || snapshot.Day < 1 || snapshot.UtcOffsetMinutes < -840 || snapshot.UtcOffsetMinutes > 840)
                throw new InvalidOperationException("Invalid snapshot identity/date.");
            Required(snapshot.ContentVersion); Required(snapshot.CharacterAndMusclesJson); Required(snapshot.TrainingPlanJson); Required(snapshot.EvidenceJson);
            DateTimeOffset timestamp;
            if (snapshot.DeviceTimeIso8601 == null || !DateTimeOffset.TryParseExact(snapshot.DeviceTimeIso8601, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out timestamp) || timestamp.Offset.TotalMinutes != snapshot.UtcOffsetMinutes)
                throw new InvalidOperationException("Snapshot needs explicit device time and matching offset.");
        }
        private static void Unique(List<string> values)
        {
            if (values == null) throw new InvalidOperationException("Missing list.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values) { Required(value); if (!seen.Add(value)) throw new InvalidOperationException("Duplicate identity."); }
        }
        private static void CheckText(string text)
        {
            if (new StringInfo(text).LengthInTextElements > 200) throw new ArgumentException("Note exceeds 200 text elements.");
        }
        private static string Field(string value) { return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }
        private static void Required(string value) { if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Required progression field is missing."); }
    }
}
