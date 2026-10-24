using System;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Narrative;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;
using FitnessSimulator.Game.Progression;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Actors;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        [Serializable] private sealed class ReadingState { public string[] Keys = Array.Empty<string>(); }
        private ReadingState Reading(RootEnvelope root) { var fragment = root.PermanentFragments.SingleOrDefault(f => f.Owner == "reading" && f.Scope == "permanent"); return fragment == null ? new ReadingState() : codec.Decode<ReadingState>(fragment.Json); }
        private ContentPackage installedContent, runContent;
        [Serializable] private sealed class ContentMigrationRecord { public string MigrationId, SourceRulesVersion, SourceRootHash, TargetContentHash, SourceEndingContinuation; public long SourceRevision, SourceGeneration; }
        private ContentMigrationRecord pendingContentMigration;
        public string LoadedContentHash => runContent?.Hash;
        public string LoadedContentVersion => runContent?.Version;
        public string LoadedContentRawHash => runContent?.RawHash;
        public string LoadedContentSource => runContent?.Source;
        public ContentPackageSnapshot InspectContent() => runContent?.Export();
        public ContentPackage ReadContent() => newGameTemplate != null ? installedContent : runContent ?? installedContent;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static GameSession OpenAutomationContent(string directory, uint seed, ContentPackage content)
            => new GameSession(directory, CommandSource.Test, seed, null, true, content);
#endif
        private IDisposable ContentScope(bool installed = false) => ContentRuntime.Push(installed ? installedContent : runContent ?? installedContent);
        private void BindRunContent(RootEnvelope root)
        {
            var fragment = root.FindFragment("content-package", "shared");
            pendingContentMigration = null;
            ContentPackage migrationTarget = null;
            if (fragment == null && root.FindFragment("session", Actor) != null) {
                if (root.SchemaVersion != 2 || root.ContentVersion != SessionContent.Version || root.Fragments.Any(f => f.SchemaVersion != 1)) throw new InvalidOperationException("Unsupported legacy content schema; original save retained");
                migrationTarget = ContentRuntime.LoadLegacyMigrationTarget();
                ValidateContentPackage(migrationTarget);
                pendingContentMigration = new ContentMigrationRecord { MigrationId = "m3r1-schema2-to-configr1.v1", SourceRulesVersion = root.ContentVersion,
                    SourceRootHash = ContentHash.Sha256(codec.Encode(root)), SourceRevision = root.Revision, SourceGeneration = root.Generation, TargetContentHash = migrationTarget.Hash };
                Diagnostic = "Explicit content migration pending: " + pendingContentMigration.MigrationId;
            }
            runContent = fragment == null ? migrationTarget ?? installedContent : ContentPackage.FromSnapshot(codec.Decode<ContentPackageSnapshot>(fragment.Json));
        }
        private void EnsureContentSnapshot(RootEnvelope root)
        {
            if (root.FindFragment("content-package", "shared") == null) {
                var progression = ReadProgressionRun(root);
                if (progression.HasActive) {
                    VerifyEndingContinuation(root, progression.Active.NormalContinuationJson);
                    if (pendingContentMigration == null) throw new InvalidOperationException("Missing ending migration provenance");
                    pendingContentMigration.SourceEndingContinuation = progression.Active.NormalContinuationJson;
                }
                if (root.FindFragment("session", Actor) != null) {
                    if (pendingContentMigration == null) throw new InvalidOperationException("Legacy content requires explicit migration provenance");
                    root.SetFragment("content-migration", "shared", 1, root.Revision + 1, codec.Encode(pendingContentMigration));
                }
                root.SetFragment("content-package", "shared", 1, root.Revision + 1, codec.Encode(ContentRuntime.Current.Export()));
                if (progression.HasActive) {
                    progression.Active.NormalContinuationJson = FreezeEndingContinuation(root);
                    Write(root, "progression", progression);
                }
            }
        }
        private void RestoreNarratives(RootEnvelope root)
        {
            if (root.FindFragment("session", Actor) == null || IsProgressionPaused(root)) return;
            foreach (var fragment in root.Fragments.Where(f => f.Owner == "narrative")) {
                var story = codec.Decode<NarrativeState>(fragment.Json);
                if (!story.Completed) fragment.Json = codec.Encode(NarrativeRuntime.Reveal(NarrativeRuntime.Restore(story)));
            }
        }
        private void ApplyConfiguredEffects(RootEnvelope root, FlowNode node, NodeCursor cursor, string actorId,
            ref BodyState body, ref LifeActorState life, PreparedOutcome outcome, ConfiguredEffect[] suppliedEffects = null)
        {
            var effects = suppliedEffects ?? Call(node).Effects;
            for (int i = 0; i < effects.Length; i++) {
                var effect = effects[i];
                long amount = effect.PositiveScalable ? SocialRules.ScalePositiveEffect(body, effect.Kind == "money" ? "MoneyGain" : "Knowledge", effect.Amount, true) : effect.Amount;
                if (effect.Target != "self") throw new InvalidOperationException("Unsupported effect target");
                switch (effect.Kind) {
                    case "money":
                    case "money-cost":
                        var money = effect.Kind == "money-cost" ? LifeRules.PrepareMoneyCost(life, amount, cursor.InstanceId, effect.Id) : LifeRules.PrepareMoneyDelta(life, amount, cursor.InstanceId, effect.Id);
                        if (money.Status != LifePreparationStatus.Prepared) throw new InvalidOperationException("Money effect failed: " + money.Status);
                        life = money.Candidate;
                        outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "MoneyEffect", Json = codec.Encode(money.Receipt) });
                        break;
                    case "knowledge": body = BodyCalculator.AddKnowledge(body, new[] { effect.Id }, amount); break;
                    case "stress": body = BodyCalculator.AddStress(body, NumericKernel.FromMicro(amount)); break;
                    default: throw new InvalidOperationException("Unknown effect " + effect.Kind);
                }
            }
        }
        [Serializable] private sealed class SocialRecord { public SocialCheckSnapshot Snapshot; public uint Sample; public bool Success; public string Mode; }
        private string ApplySocial(RootEnvelope root, FlowNode node, NodeCursor cursor, SessionState state, string actorId, ref BodyState body, ref LifeActorState life, PreparedOutcome outcome)
        {
            var definition = Call(node).Social; var record = new SocialRecord { Mode = definition.Mode, Success = true };
            if (definition.Mode == "check") {
                record.Snapshot = SocialRules.FreezeCheck(cursor.InstanceId, body, definition.BaseProbabilityMillionths);
                record.Sample = NextRandom(state); record.Success = SocialRules.Resolve(record.Snapshot, record.Sample).Success;
            }
            ApplyConfiguredEffects(root, node, cursor, actorId, ref body, ref life, outcome, record.Success ? definition.SuccessEffects : definition.FailureEffects);
            outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "SocialOutcome", Json = codec.Encode(record) });
            return record.Success ? "success" : "failure";
        }
        public static void ValidateContentPackage(ContentPackage content)
        {
            using (ContentRuntime.Push(content)) {
                FlowContent.ValidateContent();
                NarrativeRuntime.ValidateContent();
                FitnessSimulator.Game.Presentation.PresentationContent.ValidateContent();
                FitnessSimulator.Game.Presentation.AudioContent.ValidateContent();
                FitnessSimulator.Game.Presentation.KnowledgeContent.ValidateContent();
                EndingProgression.ValidateContent(null, id => content.Get<NarrativeDocument>("narrative").Stories.Any(s => s.Id == id && s.Recovery == "ending"), id => TemplateContent.Read().Templates.Any(t => t.Id == id));
                BodyContent.ValidateContent(); TrainingContent.ValidateContent();
                LifeContent.ValidateContent(); EconomyContent.ValidateContent(); RelationshipContent.ValidateContent(); ActorContent.ValidateContent();
                PlanningContent.ValidateContent(SessionContent.TrainingAnchorIds); TemplateCatalog.ValidateContent(); CompetitionContent.ValidateContent();
                TemplateContent.ValidateReferences(id => id == EconomyContent.Definition.InitialInventory.Id, id => id == RelationshipContent.Definition.InitialState.Id);
                CompetitionConfiguration.ValidateReferences(FlowContent.Current.Anchors.OrderBy(a => a.Order).Select(a => a.Id).ToArray(),
                    id => id == Actor || RelationshipRules.CharacterIds.Contains(id),
                    id => FitnessSimulator.Game.Presentation.PresentationContent.Read().Texts.Any(t => t.Id == id));
                foreach (var ending in EndingProgression.DefaultCatalog())
                    if (!IsBuiltInEndingFact(ending.ConditionKey) && !FlowContent.Current.Conditions.Any(c => c.Id == ending.ConditionKey))
                        throw new ContentException("endings", "$.Endings[" + ending.Id + "].ConditionKey", "Unknown condition " + ending.ConditionKey);
                var stories = content.Get<NarrativeDocument>("narrative");
                var relationships = RelationshipContent.Definition;
                var responseIds = relationships.Characters.SelectMany(c => c.Dialogues.Select(d => d.ResponseEventId).Concat(new[] { c.FamiliarResponseEventId }))
                    .Concat(new[] { relationships.Romance.PendingEventId, relationships.Romance.SuccessEventId, relationships.Romance.FailureEventId, relationships.Romance.DeclineEventId });
                foreach (string responseId in responseIds) {
                    var response = FlowContent.Current.Events.SingleOrDefault(e => e.Id == responseId);
                    if (response == null || response.Nodes.Length != 1 || response.Nodes[0].Handler != "narrative") throw new ContentException("relationships", "$.ResponseEventId[" + responseId + "]", "Response must reference a text event");
                    var story = stories.Stories.SingleOrDefault(s => s.Id == response.Nodes[0].Call.StoryId);
                    if (story == null || story.Variables.Length != 0 || story.Nodes.Length != 2 || story.Nodes.SingleOrDefault(n => n.Id == story.Entry)?.Kind != "line" || story.Nodes.Count(n => n.Kind == "return") != 1) throw new ContentException("narrative", "$.Stories[" + responseId + "]", "Immediate relationship response requires one text segment and return");
                }
                foreach (var e in FlowContent.Current.Events) foreach (var n in e.Nodes)
                    if (n.Handler == "narrative" && !stories.Stories.Any(s => s.Id == n.Call.StoryId))
                        throw new ContentException("flow", "$.Events[" + e.Id + "].Nodes[" + n.Id + "].Call.StoryId", "Unknown story");
                foreach (var e in FlowContent.Current.Events) foreach (var n in e.Nodes.Where(x => x.Handler == "narrative")) {
                    var choices = stories.Stories.Single(s => s.Id == n.Call.StoryId).Nodes.SelectMany(x => x.Choices ?? Array.Empty<NarrativeChoice>()).ToArray();
                    if (n.Call.ChoiceAccess.Any(a => !choices.Any(c => c.Id == a.ChoiceId)) || choices.Any(c => c.RequiresAccess && !n.Call.ChoiceAccess.Any(a => a.ChoiceId == c.Id))) throw new ContentException("flow", "$.Events[" + e.Id + "].Nodes[" + n.Id + "].Call.ChoiceAccess", "Missing or unknown choice input");
                    foreach (var a in n.Call.ChoiceAccess) if (!string.IsNullOrEmpty(a.CostNodeId) && !e.Nodes.Any(x => x.Id == a.CostNodeId && x.Handler == "effects" && x.Call.Effects.Any(effect => effect.Kind == "money-cost"))) throw new ContentException("flow", "$.Events[" + e.Id + "].Nodes[" + n.Id + "].Call.ChoiceAccess.CostNodeId", "Expected a cost effects node in this event");
                }
            }
        }
        private static bool IsBuiltInEndingFact(string key) => new[] { "competition.non_champion", "competition.unfulfilled", "competition.bodybuilding_champion", "competition.lean_champion", "relationship.romance_success" }.Contains(key);
        private bool EvaluateFlow(RootEnvelope root, string id, string actorId, SessionState state = null, BodyState body = null, LifeActorState life = null, string result = "")
            => FlowContent.Evaluate(id, key => FlowFact(root, actorId, key, state, body, life, result));
        private string FlowFact(RootEnvelope root, string actorId, string key, SessionState state = null, BodyState body = null, LifeActorState life = null, string result = "")
        {
            state = state ?? Read<SessionState>(root, "session", actorId);
            body = body ?? Read<BodyState>(root, "body", actorId);
            life = life ?? Read<LifeActorState>(root, "life", actorId);
            Func<bool, string> flag = b => b ? "true" : "false";
            Func<long, string> number = n => n.ToString(CultureInfo.InvariantCulture);
            var anchors = SessionContent.Anchors(state);
            string anchor = state.AnchorIndex < anchors.Length ? anchors[state.AnchorIndex] : "";
            switch (key) {
                case "day": return number(state.Day);
                case "anchor": return anchor;
                case "actor": return actorId;
                case "restDay": return flag(DayPlan(root, state, actorId).Slot.IsRest);
                case "trainingHere":
                    var day = DayPlan(root, state, actorId);
                    return flag(!day.Slot.IsRest && day.Slot.AnchorId == anchor);
                case "firstOrdinary": return flag(anchors.FirstOrDefault(x => !SessionContent.IsMeal(x)) == anchor);
                case "hasInjury": return flag(body.Regions.Any(r => r.IsInjured));
                case "overtime": return number(state.LastOvertime);
                case "fullDayStudy": return flag(Read<ActivityState>(root, "activity", actorId).FullDayStudy);
                case "practiced": return number(Read<ActivityState>(root, "activity", actorId).PracticedExercises.Length);
                case "effectiveTraining": return flag(state.HasEffectiveTraining);
                case "maxKnowledge": return number(body.Exercises.Max(e => e.KnowledgeMicro));
                case "balance": return number(life.Wallet.Balance);
                case "affluence": return number(life.Wallet.Affluence);
                case "intake": return number(life.Nutrition.IntakeKcal);
                case "bodyStress": return number(body.StressMicro);
                case "mentalStress": return number(body.MentalStressMicro);
                case "bodyFat": return number(body.BodyFatMicro);
                case "baseCharm": return number(body.BaseCharmMicro);
                case "result": return result ?? "";
                case "relationship.romance_success": return flag(Read<RelationshipState>(root, "relationships").RomanceEstablished);
            }
            if (key.StartsWith("competition.", StringComparison.Ordinal)) {
                if (root.FindFragment("competition", Actor) == null) return "false";
                var editions = ReadCompetition(root).Editions;
                if (key == "competition.non_champion") return flag(editions.Any(e => e.Facts.Any(f => f.Kind == CompetitionFactKind.NonChampion)));
                if (key == "competition.unfulfilled") return flag(editions.Any(e => e.Facts.Any(f => f.Kind == CompetitionFactKind.Unfulfilled)));
                if (key == "competition.bodybuilding_champion" || key == "competition.lean_champion") {
                    var kind = key == "competition.bodybuilding_champion" ? ContestKind.Bodybuilding : ContestKind.ThinBuild;
                    return flag(editions.Any(e => e.Facts.Any(f => f.Kind == CompetitionFactKind.Champion && e.Contests.Single(c => c.ContestId == f.ContestId).Kind == kind)));
                }
            }
            if (key.StartsWith("affection:", StringComparison.Ordinal)) return number(Read<RelationshipState>(root, "relationships", actorId).Affections.Single(a => a.CharacterId == key.Substring(10)).Value);
            if (key.StartsWith("inventory:", StringComparison.Ordinal)) return number(InventoryRules.Quantity(Read<InventoryState>(root, "inventory", actorId), key.Substring(10)));
            if (key.StartsWith("strength:", StringComparison.Ordinal)) return number(body.Region(key.Substring(9)).StrengthMicro);
            if (key.StartsWith("knowledge:", StringComparison.Ordinal)) return number(body.Exercise(key.Substring(10)).KnowledgeMicro);
            if (key.StartsWith("eventStarts:", StringComparison.Ordinal)) return number(Read<FlowState>(root, "flow", actorId).TriggerStats.Where(t => t.ActorId == actorId && t.DefinitionId == key.Substring(12)).Sum(t => t.Count));
            throw new InvalidOperationException("Unsupported flow fact: " + key);
        }
        private static EventCallPayload Call(FlowNode node) => string.IsNullOrEmpty(node?.PayloadJson) ? null : ContentJson.Deserialize<EventCallPayload>("flow-call", node.PayloadJson);
        private static int EventSeconds(FlowNode node, SessionState state, int legacy)
        {
            var call = Call(node);
            return call == null ? legacy : call.TimeMode == "standard" ? state.AnchorStandardSeconds : call.TimeMode == "fixed" ? call.Seconds : 0;
        }
        private void BeginNarrative(RootEnvelope root, FlowNode node, NodeCursor cursor, LifeActorState life, string actorId)
        {
            var values = new[] { new NarrativeValue { Key = "playerDisplayName", Value = actorId }, new NarrativeValue { Key = "balance", Value = life.Wallet.Balance.ToString(CultureInfo.InvariantCulture) }, new NarrativeValue { Key = "income", Value = (LifeContent.BaseHalfDayIncome + life.Wallet.Affluence).ToString(CultureInfo.InvariantCulture) } };
            var story = ContentRuntime.Current.Get<NarrativeDocument>("narrative").Stories.Single(s => s.Id == Call(node).StoryId);
            var flow = Read<FlowState>(root, "flow", actorId);
            var context = new NarrativeContext { Values = story.Variables.Select(v => values.SingleOrDefault(x => x.Key == v.Name) ?? throw new InvalidOperationException("No event input for story variable " + v.Name)).ToArray(),
                Choices = Call(node).ChoiceAccess.Select(a => {
                    long cost = 0;
                    if (!string.IsNullOrEmpty(a.CostNodeId)) { var costNode = flow.Tree.Nodes.Single(n => n.Id.Substring(n.Id.LastIndexOf('/') + 1) == a.CostNodeId); cost = Call(costNode).Effects.Where(e => e.Kind == "money-cost").Sum(e => e.Amount); }
                    bool eligible = EvaluateFlow(root, a.Condition, actorId) && life.Wallet.Balance >= cost;
                    string probability = "";
                    if (a.ProbabilitySource == "confession") { var frozen = Read<ConfessionSnapshot>(root, "confession-snapshot", actorId); var relationship=Read<RelationshipState>(root,"relationships",actorId); if (frozen.RunId != root.RunId || frozen.ActorId != actorId || string.IsNullOrEmpty(relationship.PendingConfessionId) || frozen.GiftId != relationship.PendingConfessionId) throw new InvalidOperationException("Stale or foreign confession probability input"); probability = (frozen.ProbabilityThousandths / 10m).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
                    return new NarrativeChoiceAccess { ChoiceId = a.ChoiceId, Enabled = eligible, DisabledReason = eligible ? "" : life.Wallet.Balance < cost ? "余额不足" : "条件未满足", CostText = cost == 0 ? "" : "费用 " + cost + " 元", ProbabilityText = probability };
                }).ToArray() };
            Write(root, "narrative", NarrativeRuntime.Reveal(NarrativeRuntime.Begin(story.Id, cursor.InstanceId, context)), actorId);
        }
        private bool TryNarrative(RootEnvelope root, FlowNode node, NodeCursor cursor, FrontendAction action, string actorId, out bool complete, out string result)
        {
            complete = false; result = null;
            if (node?.Handler != "narrative" || (action.ActionId != "story-continue" && action.ActionId != "story-choice" && action.ActionId != "story-skip-read")) return false;
            if (cursor?.Phase != NodePhase.Started) throw new InvalidOperationException("Narrative event not started");
            var story = Read<NarrativeState>(root, "narrative", actorId);
            if (story.InstanceId != cursor.InstanceId) throw new InvalidOperationException("Stale story instance");
            var reading = Reading(root);
            story = action.ActionId == "story-choice" ? NarrativeRuntime.Choose(story, action.StringValue) : action.ActionId == "story-skip-read" ? NarrativeRuntime.SkipRead(story, reading.Keys) : NarrativeRuntime.Advance(story);
            reading.Keys = reading.Keys.Concat(story.ReadKeys).Distinct(StringComparer.Ordinal).ToArray();
            root.PermanentFragments.RemoveAll(f => f.Owner == "reading" && f.Scope == "permanent");
            root.PermanentFragments.Add(new VersionedFragment { Owner = "reading", Scope = "permanent", SchemaVersion = 1, Revision = root.Revision + 1, Json = codec.Encode(reading) });
            story = NarrativeRuntime.Reveal(story);
            Write(root, "narrative", story, actorId); complete = story.Completed; result = story.Result;
            return true;
        }
        private bool ProjectNarrative(RootEnvelope root, FlowNode node, FrontendView view, System.Collections.Generic.List<FrontendOption> actions)
        {
            if (node?.Handler != "narrative") return false;
            var story = NarrativeRuntime.Project(Read<NarrativeState>(root, "narrative"));
            view.ScreenId = "narrative"; view.Body = (string.IsNullOrEmpty(story.Speaker) ? "" : story.Speaker + "\n") + story.Text;
            foreach (var choice in story.Choices) actions.Add(new FrontendOption { Id = "story-choice", StringValue = choice.Id, Label = choice.Text + (string.IsNullOrEmpty(choice.CostText) ? "" : "\n" + choice.CostText) + (string.IsNullOrEmpty(choice.ProbabilityText) ? "" : "\n" + choice.ProbabilityText), Enabled = choice.Enabled, DisabledReason = choice.DisabledReason });
            if (story.Choices.Length == 0) actions.Add(Option("story-continue", "继续"));
            if (!story.Completed && story.Choices.Length == 0 && Reading(root).Keys.Contains(story.ReadKey)) actions.Add(Option("story-skip-read", "跳过已读"));
            return true;
        }
    }
}
