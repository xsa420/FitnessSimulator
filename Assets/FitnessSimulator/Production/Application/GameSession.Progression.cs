using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        [Serializable] private sealed class EndingActionPayload
        { public string AchievementId; public string SegmentId; public string Text; }
        [Serializable] private sealed class EndingContinuation
        { public string RunId; public int Day; public string Phase; public int DayStep; public List<EndingFragmentIdentity> Fragments = new List<EndingFragmentIdentity>(); }
        [Serializable] private sealed class EndingFragmentIdentity
        { public string Owner; public string Scope; public int SchemaVersion; public long Revision; public string Hash; }
        [Serializable] private sealed class EndingEvidence
        { public CompetitionFact[] Competition = Array.Empty<CompetitionFact>(); public bool RomanceEstablished; public long RelationshipRevision; }

        private EndingProgression ProgressionRules => new EndingProgression(codec);

        private void InitializeProgression(RootEnvelope fresh)
        { Write(fresh, "progression", ProgressionRules.CreateRun(fresh.RunId)); }

        private ProgressionRun ReadProgressionRun(RootEnvelope root)
        {
            // Pre-progression M1 saves contain no ending facts. New runs always initialize the fragment.
            return root.FindFragment("progression", Actor) == null ? ProgressionRules.CreateRun(root.RunId) : Read<ProgressionRun>(root, "progression");
        }
        private ProgressionPermanent ReadProgressionPermanent(RootEnvelope root)
        {
            var fragments = root.PermanentFragments.Where(f => f.Owner == "progression" && f.Scope == "permanent").ToArray();
            if (fragments.Length == 0) return new ProgressionPermanent();
            if (fragments.Length != 1 || fragments[0].SchemaVersion != 1) throw new InvalidOperationException("永久进度版本或身份无效。");
            return codec.Decode<ProgressionPermanent>(fragments[0].Json) ?? throw new InvalidOperationException("永久进度不能读取。");
        }
        private string[] UnlockedTemplates(RootEnvelope root)
        {
            if (root == null) return Array.Empty<string>();
            var permanent = ReadProgressionPermanent(root);
            ProgressionRules.Validate(ProgressionRules.CreateRun(root.RunId), permanent);
            return permanent.TemplateGrants.Select(g => g.TemplateId).ToArray();
        }
        private bool IsProgressionPaused(RootEnvelope root)
        {
            if (root == null || root.FindFragment("session", Actor) == null) return false;
            var run = ReadProgressionRun(root); ProgressionRules.Validate(run, ReadProgressionPermanent(root));
            return run.HasActive || run.TerminalLock;
        }
        private void ValidateProgression(RootEnvelope root)
        {
            var run = ReadProgressionRun(root); var permanent = ReadProgressionPermanent(root);
            if (run.RunId != root.RunId) throw new InvalidOperationException("结局与当前局身份不一致。");
            ProgressionRules.Validate(run, permanent);
            if (run.HasActive) VerifyEndingContinuation(root, run.Active.NormalContinuationJson);
        }

        private string FreezeEndingContinuation(RootEnvelope root)
        {
            var session = Read<SessionState>(root, "session");
            var continuation = new EndingContinuation { RunId = root.RunId, Day = session.Day, Phase = session.Phase, DayStep = session.DayStep };
            foreach (var fragment in root.Fragments.Where(f => f.Owner != "progression").OrderBy(f => f.Owner, StringComparer.Ordinal).ThenBy(f => f.Scope, StringComparer.Ordinal))
                continuation.Fragments.Add(new EndingFragmentIdentity { Owner = fragment.Owner, Scope = fragment.Scope, SchemaVersion = fragment.SchemaVersion,
                    Revision = fragment.Revision, Hash = ContentHash.Sha256(fragment.Json) });
            return codec.Encode(continuation);
        }
        private void VerifyEndingContinuation(RootEnvelope root, string frozen)
        {
            // Only identities/hashes are frozen. Never restore an old root over committed notes or counters.
            if (FreezeEndingContinuation(root) != frozen) throw new InvalidOperationException("结局暂停期间正常后继已变化，停止恢复以保护原进度。");
        }

        private void CheckProgression(RootEnvelope root, PreparedOutcome outcome)
        {
            var run = ReadProgressionRun(root); var permanent = ReadProgressionPermanent(root);
            var facts = new List<CompetitionFact>(); var championKinds = new HashSet<ContestKind>();
            if (root.FindFragment("competition", Actor) != null)
            {
                var competition = ReadCompetition(root);
                foreach (var edition in competition.Editions)
                    foreach (var fact in edition.Facts)
                    {
                        if (fact.RunId != root.RunId || fact.ActorId != Actor) throw new InvalidOperationException("赛事事实不属于当前玩家。");
                        facts.Add(fact);
                        if (fact.Kind == CompetitionFactKind.Champion)
                            championKinds.Add(edition.Contests.Single(c => c.ContestId == fact.ContestId).Kind);
                    }
            }
            bool romance = false; long relationshipRevision = 0;
            if (root.FindFragment("relationships", Actor) != null)
            {
                var relationships = RelationshipRules.NormalizeAfterLoad(Read<RelationshipState>(root, "relationships"));
                if (relationships.RunId != root.RunId || relationships.ActorId != Actor) throw new InvalidOperationException("关系事实不属于当前玩家。");
                romance = relationships.RomanceEstablished; relationshipRevision = relationships.Revision;
            }
            Func<string, bool> fulfilled = key => {
                switch (key) {
                    case "competition.non_champion": return facts.Any(f => f.Kind == CompetitionFactKind.NonChampion);
                    case "competition.unfulfilled": return facts.Any(f => f.Kind == CompetitionFactKind.Unfulfilled);
                    case "competition.bodybuilding_champion": return championKinds.Contains(ContestKind.Bodybuilding);
                    case "competition.lean_champion": return championKinds.Contains(ContestKind.ThinBuild);
                    case "relationship.romance_success": return romance;
                    default: return EvaluateFlow(root, key, Actor);
                }
            };
            var definitions = EndingProgression.DefaultCatalog();
            AchievementSnapshot snapshot = null; string continuation = null;
            // Keep Check's validation/checkpoint update even when no ending can trigger.
            if (outcome.CheckKind == CheckKind.NormalCompletion && !run.HasActive && !run.TerminalLock
                && run.LastCheckedCheckpoint != outcome.SemanticKey
                && definitions.Any(d => !run.TriggeredEndingIds.Contains(d.Id) && fulfilled(d.ConditionKey)))
            {
                var now = DateTimeOffset.Now; var session = Read<SessionState>(root, "session");
                snapshot = new AchievementSnapshot { RunId = root.RunId, Day = session.Day, ContentVersion = root.ContentVersion,
                    DeviceTimeIso8601 = now.ToString("o", CultureInfo.InvariantCulture), UtcOffsetMinutes = (int)now.Offset.TotalMinutes,
                    CharacterAndMusclesJson = root.FindFragment("body", Actor).Json, TrainingPlanJson = root.FindFragment("plan", Actor).Json,
                    EvidenceJson = codec.Encode(new EndingEvidence { Competition = facts.ToArray(), RomanceEstablished = romance, RelationshipRevision = relationshipRevision }) };
                continuation = FreezeEndingContinuation(root);
            }
            var change = ProgressionRules.Check(run, permanent, definitions, outcome.CheckKind, source,
                outcome.SemanticKey, fulfilled, snapshot, continuation);
            ApplyProgression(root, outcome, change);
        }

        private void ApplyProgression(RootEnvelope root, PreparedOutcome outcome, ProgressionTransition change)
        {
            if (!change.Changed) return;
            Write(root, "progression", change.Run);
            // Do not create a permanent fragment for every ordinary completion or fresh-run initialization.
            if (change.Permanent.TemplateGrants.Count > 0 || root.PermanentFragments.Any(f => f.Owner == "progression"))
            {
                root.PermanentFragments.RemoveAll(f => f.Owner == "progression" && f.Scope == "permanent");
                root.PermanentFragments.Add(new VersionedFragment { Owner = "progression", Scope = "permanent", SchemaVersion = 1,
                    Revision = root.Revision + 1, Json = codec.Encode(change.Permanent) });
            }
            outcome.Appends.AddRange(change.Appends);
        }

        private static bool IsEndingAction(string action)
        { return action == "ending-segment" || action == "ending-draft" || action == "ending-note" || action == "ending-empty" || action == "ending-continue"; }
        private EndingActionPayload ParseEndingPayload(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("缺少结局操作目标。");
            var payload = codec.Decode<EndingActionPayload>(value);
            if (payload == null || string.IsNullOrWhiteSpace(payload.AchievementId)) throw new InvalidOperationException("结局操作目标无效。");
            return payload;
        }
        private string ProgressionSemanticKey(CommandEnvelope command)
        {
            if (!IsEndingAction(command.ActionKind)) return null;
            var request = codec.Decode<RequestPayload>(command.Payload);
            if (request == null || request.ActionId != command.ActionKind) throw new InvalidOperationException("结局命令载荷不一致。");
            return EndingSemantic(command.RunId, command.ActionKind, command.OperationId, ParseEndingPayload(request.StringValue));
        }
        private static string EndingSemantic(string runId, string action, string operationId, EndingActionPayload payload)
        {
            string kind = action == "ending-empty" ? "ending-note" : action;
            string extra = kind == "ending-segment" ? payload.SegmentId : kind == "ending-draft" ? operationId : "";
            return "ending/" + ContentHash.Sha256(EndingField(runId) + EndingField(kind) + EndingField(payload.AchievementId) + EndingField(extra));
        }
        private static string EndingField(string value) { value = value ?? ""; return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }

        private PreparedOutcome PrepareProgressionAction(RootEnvelope root, FrontendAction action)
        {
            if (!IsEndingAction(action.ActionId))
            {
                if (IsProgressionPaused(root)) throw new InvalidOperationException("请先完成结局演出和记录；终止局不能继续原流程。");
                return null;
            }
            var payload = ParseEndingPayload(action.StringValue);
            if (payload.Text != null && new StringInfo(payload.Text).LengthInTextElements > 200)
                throw new InvalidOperationException("备注最多200字，请缩短后再保存；内容不会被截断。");
            if (action.ActionId == "ending-note" && string.IsNullOrWhiteSpace(payload.Text))
                throw new InvalidOperationException("请输入备注正文，或选择明确留空。");
            if (action.ActionId == "ending-empty" && !string.IsNullOrEmpty(payload.Text))
                throw new InvalidOperationException("明确留空不能同时提交备注正文。");
            var run = ReadProgressionRun(root); var permanent = ReadProgressionPermanent(root);
            VerifyEndingContinuation(root, run.Active.NormalContinuationJson);
            ProgressionTransition change;
            switch (action.ActionId) {
                case "ending-segment": change = ProgressionRules.ConfirmSegment(run, permanent, payload.AchievementId, payload.SegmentId); break;
                case "ending-draft": change = ProgressionRules.SaveDraft(run, permanent, payload.AchievementId, payload.Text ?? ""); break;
                case "ending-note": change = ProgressionRules.CommitNote(run, permanent, payload.AchievementId, payload.Text ?? "", false); break;
                case "ending-empty": change = ProgressionRules.CommitNote(run, permanent, payload.AchievementId, "", true); break;
                case "ending-continue": change = ProgressionRules.Continue(run, permanent, payload.AchievementId); break;
                default: throw new InvalidOperationException("未知结局操作。");
            }
            var outcome = Outcome(EndingSemantic(root.RunId, action.ActionId, action.OperationId, payload), "已保存结局进度。");
            ApplyProgression(root, outcome, change);
            return outcome;
        }

        private FrontendView ReadProgressionView(RootEnvelope root)
        {
            if (!IsProgressionPaused(root)) return null;
            ValidateProgression(root);
            var run = ReadProgressionRun(root); var active = run.Active;
            var definition = EndingProgression.DefaultCatalog().Single(d => d.Id == active.EndingId);
            var view = new FrontendView { RunId = root.RunId, Revision = root.Revision, Title = definition.Title,
                ScreenId = active.Phase == EndingPhase.Presenting ? "ending-presentation" : active.Phase == EndingPhase.NotePending ? "ending-note" : "ending-menu",
                CanOpenAuxiliary = false, EndingNoteDraft = active.NoteDraft ?? "", EndingCanEditNote = active.Phase == EndingPhase.NotePending };
            if (active.Phase == EndingPhase.Presenting)
            {
                view.Body = EndingPresentationText(active.SegmentIds[active.NextSegmentIndex]);
                view.Actions = new[] { EndingOption("ending-segment", "继续", active, active.SegmentIds[active.NextSegmentIndex]) };
            }
            else if (active.Phase == EndingPhase.NotePending)
            {
                view.Body = "达成记录已保存。可以写下一次备注，或明确留空；确认后不能修改。";
                view.Actions = new[] { EndingOption("ending-draft", "保存草稿", active), EndingOption("ending-note", "提交备注", active), EndingOption("ending-empty", "明确留空", active) };
            }
            else
            {
                view.Body = run.TerminalLock ? "本局已结束，达成记录已永久保留。" : "达成记录已永久保留。继续将回到此前的正常进度。";
                var options = new List<FrontendOption>();
                if (!run.TerminalLock) options.Add(EndingOption("ending-continue", "继续当前局", active));
                foreach (var template in TemplateCatalog.All)
                {
                    var option = Option("new-game", "新游戏 · " + template.Name, template.Id);
                    option.Enabled = TemplateCatalog.IsAvailable(template.Id, UnlockedTemplates(root));
                    option.DisabledReason = "尚未取得此模板资格"; options.Add(option);
                }
                view.Actions = options.ToArray();
            }
            return view;
        }
        private FrontendOption EndingOption(string action, string label, ActiveEnding active, string segment = null)
        {
            return Option(action, label, codec.Encode(new EndingActionPayload { AchievementId = active.AchievementId,
                SegmentId = segment ?? "", Text = action == "ending-empty" ? "" : active.NoteDraft ?? "" }));
        }
        private static string EndingPresentationText(string id)
        {
            var state = FitnessSimulator.Game.Narrative.NarrativeRuntime.Begin(id, "ending-preview/" + id, new FitnessSimulator.Game.Narrative.NarrativeContext());
            var lines = new List<string>();
            for (int count = 0; !state.Completed; count++) {
                if (count >= 128) throw new InvalidOperationException("Ending segment exceeds text limit");
                state = FitnessSimulator.Game.Narrative.NarrativeRuntime.Reveal(state);
                var projection = FitnessSimulator.Game.Narrative.NarrativeRuntime.Project(state);
                if (projection.Choices.Length != 0) throw new InvalidOperationException("Ending presentation segment must contain text only");
                lines.Add((string.IsNullOrEmpty(projection.Speaker) ? "" : projection.Speaker + "：") + projection.Text);
                state = FitnessSimulator.Game.Narrative.NarrativeRuntime.Advance(state);
            }
            return string.Join("\n", lines);
        }
    }
}
