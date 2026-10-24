using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Narrative;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    // The composition root is the only coordinator of domain candidates and durable publication.
    public sealed partial class GameSession : IGameFrontend, IFrontendContentProvider, IDisposable
    {
        private const string Actor = "player";
        private readonly IStateCodec codec = new UnityJsonStateCodec();
        private ICheckpointStore store;
        private SerialCommandDispatcher dispatcher;
        private readonly TrainingCatalogAdapter catalog = new TrainingCatalogAdapter();
        private readonly CommandSource source;
        private readonly bool trustedAutomation;
        private readonly uint initialSeed;
        private readonly string saveDirectory;
        private readonly Action<StorageFaultPoint> storageFault;
        private ITrainingInputSession game;
        // Internal read-only snapshot, invalidated after each publication or store reopen.
        // Public inspection still returns a defensive copy from the dispatcher.
        private RootEnvelope readSnapshot;
        private RootEnvelope CurrentSnapshot => readSnapshot ?? (readSnapshot = dispatcher.Snapshot);
        private string liveSetId;
        private double fractionalMilliseconds;
        private long liveMilliseconds;
        private readonly List<InputEdge> edges = new List<InputEdge>();
        private string error;
        public string Diagnostic { get; private set; }
        private bool suspended;
        private bool disposed;
        private bool atMenu;
        private string newGameTemplate;
        private string newGameOriginRun;

        public GameSession(string saveDirectory) : this(saveDirectory, CommandSource.Player, NewSeed()) { }

        // A separate fixture directory and Test source are mandatory for deterministic automation.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static GameSession OpenTest(string fixtureDirectory, uint seed, Action<StorageFaultPoint> fault = null) => new GameSession(fixtureDirectory, CommandSource.Test, seed, fault);
        public static GameSession OpenAutomation(string fixtureDirectory, uint seed, Action<StorageFaultPoint> fault = null) => new GameSession(fixtureDirectory, CommandSource.Test, seed, fault, true);
#endif

        private GameSession(string directory, CommandSource source, uint seed, Action<StorageFaultPoint> fault = null, bool trustedAutomation = false, ContentPackage content = null)
        {
            installedContent = runContent = content ?? ContentRuntime.Current;
            ValidateContentPackage(installedContent);
            this.source = source; initialSeed = seed == 0 ? 1u : seed;
            this.trustedAutomation = trustedAutomation;
            saveDirectory = directory;
            storageFault = fault;
            OpenStore();
            atMenu = true;
        }

        private void OpenStore()
        {
            try {
                store?.Dispose(); dispatcher = null; readSnapshot = null; error = null;
                store = new LocalCheckpointStore(saveDirectory, codec, inject: storageFault, expectedContentVersion: SessionContent.Version);
                var load = store.Load();
                if (load.Status != LoadStatus.Empty && load.Status != LoadStatus.Loaded) {
                    Diagnostic = load.Status + ": " + load.Error;
                    error = "当前存档不能读取。可以重试；若选择重新开始，会先保留原数据，再建立新局。"; return;
                }
                var root = load.Root ?? new RootEnvelope { RunId = Guid.NewGuid().ToString("N"), ContentVersion = SessionContent.Version };
                if (root.ContentVersion != SessionContent.Version) { error = "存档内容版本不兼容，已停止读取。"; return; }
                BindRunContent(root);
                using var contentScope = ContentScope();
                ValidateContentPackage(runContent);
                RestoreNarratives(root);
                dispatcher = new SerialCommandDispatcher(store, codec, root,
                    c => c.ResourceScope == c.ActorId && (c.Source == source && c.ActorId == Actor
                        || c.Source == CommandSource.NpcProduction && FitnessSimulator.Game.Life.Relationships.RelationshipRules.CharacterIds.Contains(c.ActorId)), CheckCompletion);
                if (root.FindFragment("session", Actor) != null) ValidateLoaded(root);
                SyncKernel();
            } catch (Exception ex) when (ex is System.IO.IOException || ex is ArgumentException || ex is InvalidOperationException) {
                dispatcher = null; Diagnostic = ex.ToString(); error = "无法打开当前局。原数据已保留，请重试读取。";
            }
        }

        private static uint NewSeed() => BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) | 1u;
        public RootEnvelope InspectSnapshot() => dispatcher?.Snapshot;
        private T Read<T>(RootEnvelope root, string owner, string actorId = Actor) where T : class
        {
            var fragment = root.FindFragment(owner, actorId);
            if (fragment == null) throw new InvalidOperationException("Missing fragment: " + owner);
            if (fragment.SchemaVersion != 1) throw new InvalidOperationException("Unsupported fragment: " + owner);
            var value = codec.Decode<T>(fragment.Json) ?? throw new InvalidOperationException("Invalid fragment: " + owner);
            if (value is TrainingDayState training) {
                if (!training.HasQuota) training.Quota = null;
                if (!training.HasWorkout) training.Workout = null;
                else TrainingSession.NormalizeAfterLoad(training.Workout);
            }
            if (value is FlowState flow) FlowEngine.NormalizeAfterLoad(flow);
            if (value is LifeActorState life) value = LifeRules.NormalizeAfterLoad(life) as T;
            return value;
        }
        private void Write<T>(RootEnvelope root, string owner, T value, string actorId = Actor)
        {
            if (value is TrainingDayState training) { training.HasQuota = training.Quota != null; training.HasWorkout = training.Workout != null; }
            string json = codec.Encode(value);
            var existing = root.FindFragment(owner, actorId);
            // A legacy migration must not rewrite unchanged receipts merely to materialize new optional fields.
            if (pendingContentMigration != null && existing != null &&
                codec.Encode(codec.Decode<T>(existing.Json)) == json) json = existing.Json;
            root.SetFragment(owner, actorId, 1, root.Revision + 1, json);
        }
        private PlanState Plan(RootEnvelope root, string actorId = Actor) => PlanPersistence.Restore(Read<PlanStateRecord>(root, "plan", actorId), catalog, SessionContent.TrainingAnchorIds);
        private static DayOfWeek Weekday(int day) => (DayOfWeek)(day % 7);
        private DayPlan DayPlan(RootEnvelope root, SessionState state, string actorId = Actor)
        {
            var plan = Plan(root, actorId);
            if (actorId != Actor && root.FindFragment("day-arrangement", actorId) != null) {
                var record = Read<ActorDayArrangementRecord>(root, "day-arrangement", actorId);
                if (record.Day == state.Day) {
                    var arrangement = ActorDayArrangementPersistence.Restore(record, root.RunId, actorId, state.Day, catalog, SessionContent.TrainingAnchorIds);
                    return new DayPlan(plan, state.Day, arrangement.Slot);
                }
            }
            return PlanRules.ResolveDay(plan, state.Day, Weekday(state.Day));
        }
        private AnchorEligibility Eligibility(SessionState state)
        {
            var now = SessionContent.Anchors(state).Where(x => !SessionContent.IsMeal(x)).ToArray();
            var next = codec.Decode<SessionState>(codec.Encode(state)); next.Day++;
            return new AnchorEligibility(state.Day, now, SessionContent.Anchors(next).Where(x => !SessionContent.IsMeal(x)).Except(now));
        }
        private void ValidateLoaded(RootEnvelope root)
        {
            var state = Read<SessionState>(root, "session");
            if (state.Day < 1 || state.RandomState == 0) throw new InvalidOperationException("Invalid session");
            Read<BodyState>(root, "body").Validate(); Plan(root);
            var life = Read<LifeActorState>(root, "life");
            if (life.RunId != root.RunId || life.ActorId != Actor || life.Nutrition.Day != state.Day)
                throw new InvalidOperationException("Life/session identity mismatch");
            var flow = Read<FlowState>(root, "flow");
            if (flow.Tree != null) FlowEngine.Validate(flow.Tree);
            var training = Read<TrainingDayState>(root, "training");
            if (training.Progress.ActorId != Actor) throw new InvalidOperationException("Training identity mismatch");
            ValidateProgression(root);
        }

        public FrontendReply Execute(FrontendAction action)
        {
            if (action?.ActionId == null || !(action.ActionId.StartsWith("bench-", StringComparison.Ordinal) || action.ActionId == "training-input" || action.ActionId == "training-rebind")) liveProjectionRoot = null;
            using (ContentScope(newGameTemplate != null || action?.ActionId == "new-game")) return ExecuteBound(action);
        }
        private FrontendReply ExecuteBound(FrontendAction action)
        {
            if (disposed) return Reply(false, "会话已关闭。");
            if (action == null) return Reply(false, "缺少操作。");
            if (TryExecuteHistory(action, out var historyReply)) return historyReply;
            if (action.ActionId == "cancel-new-game") { newGameTemplate = null; newGameOriginRun = null; atMenu = true; return Reply(true, "开局草稿已放弃，原进度保留。"); }
            if (action.ActionId == "new-game") {
                if (dispatcher != null && IsProgressionPaused(CurrentSnapshot) && ReadProgressionView(CurrentSnapshot).ScreenId != "ending-menu") return Reply(false, "请先完成当前结局演出和备注。");
                try { TemplateCatalog.PrepareInitialization(action.StringValue, dispatcher == null ? Array.Empty<string>() : UnlockedTemplates(CurrentSnapshot), "draft", Actor); }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Reply(false, ex.Message); }
                if (error != null && (!(store is LocalCheckpointStore resettable) || !resettable.CanResetCorruptM1Store(out _))) return Reply(false, "旧数据尚不能验证，请先重试读取。");
                newGameTemplate = action.StringValue; newGameOriginRun = dispatcher?.Snapshot.RunId;
                atMenu = true; return Reply(true, "请选择初始计划；保存合法计划后才建立新局。");
            }
            if (action.ActionId == "save-plan" && newGameTemplate != null) return CreateRunWithPlan(action);
            if (action.ActionId == "open-menu") {
                if (game != null && game.Result == null) return Reply(false, "本组进行中不能返回菜单。");
                atMenu = true; return Reply(true, null);
            }
            if (action.ActionId == "retry-storage") {
                OpenStore(); return Reply(error == null, error ?? "已重新读取，请继续。");
            }
            if (error != null || dispatcher == null) return Reply(false, error ?? "会话不可用。");
            if (action.ActionId == "continue-game") { atMenu = false; SyncKernel(); return Reply(true, null); }
            if (string.IsNullOrWhiteSpace(action.OperationId)) return Reply(false, "操作标识缺失。");
            if (action.ActionId == "save-plan" && store.FindReceipt(action.OperationId)?.SemanticKey.StartsWith("new-run:", StringComparison.Ordinal) == true)
                return CreateRunWithPlan(action);
            if (action.ActionId.StartsWith("bench-", StringComparison.Ordinal) || action.ActionId == "training-input" || action.ActionId == "training-rebind") return Input(action);
            var root = CurrentSnapshot;
            var command = Envelope(action, root);
            var result = action.ActionId.StartsWith("rel-", StringComparison.Ordinal)
                ? dispatcher.Execute(command, RelationshipSemanticKey, candidate => Prepare(candidate, action))
                : IsEndingAction(action.ActionId)
                ? dispatcher.Execute(command, ProgressionSemanticKey, candidate => Prepare(candidate, action))
                : action.ActionId == "shop-buy" || action.ActionId == "item-use"
                ? dispatcher.Execute(command, EconomySemanticKey, candidate => Prepare(candidate, action))
                : dispatcher.Execute(command, candidate => Prepare(candidate, action));
            if (result.Status == CommandStatus.StorageOutcomeUnknown || result.Status == CommandStatus.StorageUnavailable) {
                Diagnostic = result.Status + ": " + result.Error;
                error = "保存尚未确认，请重试读取后继续。";
            }
            if (result.Status == CommandStatus.Committed || result.Status == CommandStatus.AlreadyCommitted) {
                readSnapshot = null;
                if (action.ActionId == "ending-continue") atMenu = false;
                if (action.ActionId == "new-game") atMenu = false;
                SyncKernel(); return Reply(true, result.Receipt?.Result ?? "已保存。");
            }
            return Reply(false, result.Error ?? result.Status.ToString());
        }

        private CommandEnvelope Envelope(FrontendAction action, RootEnvelope root) => new CommandEnvelope {
            OperationId = action.OperationId, ExpectedRevision = action.ExpectedRevision, RunId = root.RunId,
            ActorId = Actor, ResourceScope = Actor, Source = source, ActionKind = action.ActionId,
            // Revision validates the target state. The retry fingerprint contains only immutable request data,
            // never a cursor read from the later state after the original operation has completed.
            Payload = codec.Encode(new RequestPayload { ActionId = action.ActionId, StringValue = action.StringValue, IntValue = action.IntValue }) };

        private PreparedOutcome Outcome(string key, string text, bool complete = false) => new PreparedOutcome {
            SemanticKey = key, Result = text, OutcomeKind = OutcomeKind.Success,
            EventDisposition = complete ? EventDisposition.CompleteNode : EventDisposition.AuxiliaryOnly,
            CheckKind = complete ? CheckKind.NormalCompletion : CheckKind.None, OpportunityPolicy = OpportunityPolicy.Keep };

        private PreparedOutcome Prepare(RootEnvelope root, FrontendAction action)
        {
            if (!PlayerCanAct(root, action.ActionId)) throw new InvalidOperationException("其他人物尚未完成当前时段。");
            var outcome = PrepareActor(root, action, Actor);
            if (!IsEndingAction(action.ActionId)) UpdateWorld(root, Actor, action.ActionId);
            return outcome;
        }

        private PreparedOutcome PrepareActor(RootEnvelope root, FrontendAction action, string actorId)
        {
            EnsureContentSnapshot(root);
            if (actorId == Actor) {
                var ending = PrepareProgressionAction(root, action);
                if (ending != null) return ending;
                if (TryPrepareCompetitionAction(root, action, out var competition)) return competition;
                if (TryPrepareRelationship(root, action, out var relationship)) return relationship;
            } else if (IsProgressionPaused(root)) throw new InvalidOperationException("World is paused for the player ending");
            var state = Read<SessionState>(root, "session", actorId);
            var flow = Read<FlowState>(root, "flow", actorId);
            var training = Read<TrainingDayState>(root, "training", actorId);
            var body = Read<BodyState>(root, "body", actorId);
            var life = Read<LifeActorState>(root, "life", actorId);
            var previousBody = body;
            long previousBalance = life.Wallet.Balance;
            int previousDay = state.Day;
            string key = root.RunId + "/" + action.ActionId + "/" + root.Revision;
            var outcome = Outcome(key, "已保存。");
            var cursor = FlowEngine.Current(flow);
            var node = cursor == null ? null : flow.Tree.Nodes.Single(n => n.Id == cursor.NodeId);
            bool complete = false;
            string branch = null;
            if (!TryNarrative(root, node, cursor, action, actorId, out complete, out branch) && !PrepareActivity(root, state, flow, node, cursor, action, actorId, ref body, ref life, outcome, out complete)) switch (action.ActionId) {
                case "save-plan": {
                    if (training.Workout?.Phase == WorkoutPhase.Playing) throw new InvalidOperationException("小游戏中不能编辑计划。");
                    if (state.Phase != "InitialPlan" && (atMenu || !IsEveningPlanWindow(state))) throw new InvalidOperationException("请在晚上修改明日起的计划。");
                    var draft = codec.Decode<PlanDraft>(action.StringValue);
                    var plan = Plan(root, actorId);
                    plan = state.Phase == "InitialPlan"
                        ? PlanRules.SaveInitial(plan, draft, state.Day, plan.Revision, EditingEligibility(state), catalog)
                        : PlanRules.SavePending(plan, draft, state.Day, plan.Revision, EditingEligibility(state), catalog);
                    Write(root, "plan", PlanPersistence.Export(plan), actorId);
                    if (state.Phase == "InitialPlan") { state.Phase = "Anchors"; FreezeQuota(root, state, training, actorId); }
                    outcome.Result = state.Phase == "Anchors" && plan.Pending == null ? "首日计划已生效。" : "计划已保存，明日起生效。";
                    break;
                }
                case "resolve-anchor": {
                    if (state.Phase != "Anchors") throw new InvalidOperationException("Not in anchors");
                    if (flow.Tree != null && flow.EntryPhase != EntryPhase.Finished) throw new InvalidOperationException("Previous event unfinished");
                    var anchors = SessionContent.Anchors(state);
                    if (actorId == Actor && PrepareCompetitionPassedWindows(root, state, flow, outcome)) break;
                    if (state.AnchorIndex >= anchors.Length) { state.Phase = "DayEnd"; state.DayStep = 0; break; }
                    string anchor = anchors[state.AnchorIndex];
                    if (actorId == Actor && TryResolveCompetitionAnchor(root, state, flow, outcome)) break;
                    if (flow.Occupancies.Any(o => o.ActorId == actorId && o.Day == state.Day && o.AnchorId == anchor)) { state.AnchorIndex++; break; }
                    var dayPlan = DayPlan(root, state, actorId);
                    bool trainingHere = !dayPlan.Slot.IsRest && dayPlan.Slot.AnchorId == anchor;
                    var definition = SessionContent.Build(anchor, training.Quota, trainingHere, state, Read<ActivityState>(root, "activity", actorId), dayPlan.Slot.IsRest, body.Regions.Any(r => r.IsInjured), id => EvaluateFlow(root, id, actorId, state, body, life));
                    FlowEngine.ResolveAnchor(flow, definition, actorId, state.Day, anchor, state.AnchorIndex,
                        root.RunId + "/" + actorId + "/d" + state.Day + "/" + anchor, id => EvaluateFlow(root, id, actorId, state, body, life), t => SessionContent.Anchors(new SessionState { Day = t.Day, EarlyAvailableDay = state.EarlyAvailableDay, LateAvailableDay = state.LateAvailableDay, AnchorUnlocks = state.AnchorUnlocks }).Contains(t.AnchorId));
                    state.AnchorGameSeconds = 0; state.AnchorStandardSeconds = FlowContent.Anchor(anchor).StandardSeconds;
                    state.IncomingOvertime = state.LastOvertime;
                    training.Workout = null;
                    break;
                }
                case "choose-entry":
                    FlowEngine.ConfirmChoice(flow, flow.ChoiceToken, action.StringValue, id => EvaluateFlow(root, id, actorId, state, body, life), t => SessionContent.Anchors(new SessionState { Day = t.Day, EarlyAvailableDay = state.EarlyAvailableDay, LateAvailableDay = state.LateAvailableDay, AnchorUnlocks = state.AnchorUnlocks }).Contains(t.AnchorId)); break;
                case "start-node":
                    if (state.Phase != "Anchors" || cursor == null || cursor.Phase != NodePhase.NotStarted) throw new InvalidOperationException("Node cannot start");
                    FlowEngine.StartCurrent(flow);
                    outcome.SemanticKey = cursor.InstanceId + "/start";
                    if (node.Handler == "training") {
                        if (PlanCatalog.GetIntegrationIssues(DayPlan(root, state, actorId), ExerciseCatalog.All.Select(e => e.Id)).Count > 0)
                            throw new InvalidOperationException("当前计划包含尚未接入的小游戏，不能用自动结果替代。");
                        training.Workout = TrainingSession.SkipUnavailableActions(TrainingSession.Begin(flow.TreeInstanceId, training.Quota), body);
                        state.AnchorStandardSeconds = training.Quota.StandardGameSeconds;
                    }
                    StartActivity(root, state, flow, node, cursor, actorId);
                    if (node.Handler == "narrative") BeginNarrative(root, node, cursor, life, actorId);
                    break;
                case "continue":
                    RequireStarted(state, cursor);
                    if (node.Handler == "work") {
                        if (flow.Day != state.Day || flow.ActorId != actorId || DayPlan(root, state, actorId).Slot.IsRest)
                            throw new InvalidOperationException("当前不是有效工作机会。");
                        var work = LifeRules.PrepareWorkIncome(life, state.Day, flow.AnchorId, cursor.InstanceId, Call(node)?.IncomeId ?? LifeContent.Definition.DefaultWorkEffectId);
                        if (work.Status == LifePreparationStatus.Prepared) {
                            life = work.Candidate;
                            outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "WorkIncome", Json = codec.Encode(work.Receipt) });
                            outcome.Result = SessionContent.Name(flow.AnchorId) + "工作已完成，收入 " + work.Receipt.Amount + " 元，余额 " + life.Wallet.Balance + " 元。";
                        } else if (work.Status == LifePreparationStatus.AlreadyApplied || work.Status == LifePreparationStatus.WorkAlreadyCompleted
                            || work.Status == LifePreparationStatus.LegacyIncomeAlreadyApplied) {
                            outcome.Result = "本时段工作已完成；收入此前已结算，本次不重复发放。";
                        } else throw new InvalidOperationException("工作收入未能结算。");
                        AddTime(state, EventSeconds(node, state, state.AnchorStandardSeconds)); complete = true;
                    } else if (node.Handler == "calm" || node.Handler == "walk") {
                        AddTime(state, EventSeconds(node, state, node.Handler == "walk" ? 300 : 0));
                        complete = true;
                    } else if (node.Handler == "effects") {
                        ApplyConfiguredEffects(root, node, cursor, actorId, ref body, ref life, outcome);
                        AddTime(state, EventSeconds(node, state, 0)); complete = true;
                    } else if (node.Handler == "social") {
                        branch = ApplySocial(root, node, cursor, state, actorId, ref body, ref life, outcome);
                        AddTime(state, EventSeconds(node, state, 0)); complete = true;
                    } else if (node.Handler == "end-action") {
                        if (training.Workout?.Phase != WorkoutPhase.Settled) throw new InvalidOperationException("结束动作仅允许在已结算后继阶段。");
                        training.Workout = TrainingSession.EndCurrentAction(training.Workout, body);
                        branch = training.Workout.Phase == WorkoutPhase.Ended ? "workout-ended" : "action-" + training.Workout.EntryIndex; complete = true;
                    } else if (node.Handler == "next-anchor") {
                        if (training.Workout != null && training.Workout.Phase != WorkoutPhase.Ended) throw new InvalidOperationException("不能跳过未完成训练的结算或后继。");
                        cursor.Phase = NodePhase.Completed; flow.CurrentNodeId = null; flow.EntryPhase = EntryPhase.Finished;
                        outcome.CheckKind = CheckKind.NormalCompletion; outcome.EventDisposition = EventDisposition.CompleteNode;
                    } else if (node.Handler == "training") {
                        branch = training.Workout.Phase == WorkoutPhase.Ended ? "workout-ended" : "action-" + training.Workout.EntryIndex;
                        complete = true;
                    } else if (node.Handler == "group-story") {
                        if (training.Workout.Phase == WorkoutPhase.Ended) branch = "workout-ended";
                        complete = true;
                    } else if (node.Handler == "followers") {
                        int oldAction = training.Workout.EntryIndex;
                        training.Workout = TrainingSession.ContinueAfterFollowers(training.Workout, body);
                        if (training.Workout.Phase == WorkoutPhase.Ended) branch = "workout-ended";
                        else if (oldAction != training.Workout.EntryIndex) branch = "action-" + training.Workout.EntryIndex;
                        complete = true;
                    } else if (node.Handler == "summary") {
                        // Container summary only: all effects/checks belong to child completions.
                        FlowEngine.CompleteCurrent(flow, _ => false);
                    } else throw new InvalidOperationException("This event needs its specific action");
                    break;
                case "begin-set":
                    RequireHandler(state, cursor, node, "play");
                    if (training.Workout.Phase != WorkoutPhase.PreSet) throw new InvalidOperationException("Not at pre-set");
                    var type = action.IntValue == 1 ? TrainingSetType.StrengthChallenge : TrainingSetType.Volume;
                    if (action.IntValue < 0 || action.IntValue > 1) throw new ArgumentException("Unknown set type");
                    TrainingProducer producer;
                    if (action.StringValue == "auto") producer = TrainingProducer.AutoCapacity;
                    else if (string.IsNullOrEmpty(action.StringValue) || action.StringValue == "manual") producer = actorId != Actor ? TrainingProducer.NpcPolicy : trustedAutomation ? TrainingProducer.TrustedAutomationInput : source == CommandSource.Test ? TrainingProducer.ScriptReplay : TrainingProducer.PlayerTouch;
                    else throw new ArgumentException("Unknown input producer");
                    if (actorId == Actor) FreezePlayerBenefits(root, training.Workout);
                    training.Workout = TrainingSession.BeginSet(training.Workout, body, training.Progress, type, producer);
                    state.SetSeed = NextRandom(state);
                    break;
                case "result-ready":
                    RequireHandler(state, cursor, node, "play");
                    if (training.Workout.Phase != WorkoutPhase.Playing) throw new InvalidOperationException("No live set");
                    if (training.Workout.PendingRequest.Producer == TrainingProducer.AutoCapacity)
                        training.Workout = TrainingSession.CompleteAutomatic(training.Workout);
                    else if (actorId != Actor)
                        training.Workout = TrainingSession.CompleteResult(training.Workout, CompleteNpcInput(root, state, training.Workout, outcome));
                    else {
                        if (game?.Result == null || liveSetId != training.Workout.PendingRequest.SetId) throw new InvalidOperationException("No completed input kernel result");
                        training.Workout = TrainingSession.CompleteResult(training.Workout, game.Result);
                    }
                    if (actorId == Actor) outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/inputs", Kind = "SetInput", Json = codec.Encode(new InputRecord { SetId = training.Workout.PendingRequest.SetId, Seed = state.SetSeed, Source = source.ToString(), KernelVersion = game?.KernelVersion, Edges = edges.ToArray(), Result = training.Workout.PendingResult }) });
                    complete = true; break;
                case "settle":
                    RequireHandler(state, cursor, node, "settle");
                    var rolls = new Dictionary<string, uint>();
                    foreach (string id in BodyCalculator.RequiredInjuryRollRegions(training.Workout.PreSetBody, training.Workout.PendingRequest,
                        training.Workout.PendingResult, training.Workout.Quota.Entry(training.Workout.PendingRequest.PlanEntryId))) rolls.Add(id, NextRandom(state));
                    var settled = actorId == Actor ? SettlePlayerTeaching(root, state, training, body, rolls, outcome)
                        : TrainingSession.Settle(training.Workout, body, training.Progress, state.Day, rolls);
                    training.Workout = settled.Workout; training.Progress = settled.Progress; body = settled.Body;
                    AddTime(state, settled.Calculation.GameSeconds);
                    if (training.Workout.PendingResult.EffectiveReps > 0) {
                        state.HasEffectiveTraining = true;
                        var activity = Read<ActivityState>(root, "activity", actorId);
                        activity.PracticedExercises = activity.PracticedExercises.Concat(new[] { training.Workout.PendingRequest.ExerciseId }).Distinct().ToArray();
                        Write(root, "activity", activity, actorId);
                    }
                    outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "TrainingSet", Json = codec.Encode(training.Workout.PendingResult) });
                    complete = true; break;
                case "rest":
                    RequireHandler(state, cursor, node, "play");
                    var rest = TrainingSession.Rest(training.Workout, body, action.IntValue);
                    training.Workout = rest.Workout; body = rest.Body; AddTime(state, action.IntValue);
                    if (actorId == Actor) MarkRelationshipRest(root, training.Workout);
                    outcome.CheckKind = CheckKind.NormalCompletion; outcome.EventDisposition = EventDisposition.CompleteNode;
                    outcome.Result = "休息已完成并保存。"; break;
                case "end-action":
                    RequireHandler(state, cursor, node, "followers");
                    training.Workout = TrainingSession.EndCurrentAction(training.Workout, body);
                    branch = training.Workout.Phase == WorkoutPhase.Ended ? "workout-ended" : "action-" + training.Workout.EntryIndex;
                    complete = true; break;
                case "npc-skip-injured":
                    RequireHandler(state, cursor, node, "play");
                    if (actorId == Actor || training.Workout.Phase != WorkoutPhase.PreSet) throw new InvalidOperationException("NPC injury skip unavailable");
                    int priorAction = training.Workout.EntryIndex;
                    training.Workout = TrainingSession.SkipUnavailableActions(training.Workout, body);
                    if (training.Workout.EntryIndex == priorAction) throw new InvalidOperationException("No injured action to skip");
                    branch = training.Workout.Phase == WorkoutPhase.Ended ? "workout-ended" : "action-" + training.Workout.EntryIndex;
                    complete = true; break;
                case "end-training":
                    RequireHandler(state, cursor, node, "play");
                    training.Workout = TrainingSession.EndWorkout(training.Workout);
                    // This explicit control event closes the anchor only at a pre-set boundary.
                    cursor.Phase = NodePhase.Completed; flow.CurrentNodeId = null; flow.EntryPhase = EntryPhase.Finished;
                    outcome.CheckKind = CheckKind.NormalCompletion; outcome.EventDisposition = EventDisposition.CompleteNode;
                    outcome.Result = "本次训练已结束，已完成的内容保留。"; break;
                case "meal":
                    RequireHandler(state, cursor, node, "meal");
                    if (flow.Day != state.Day || flow.ActorId != actorId || life.Nutrition.Day != state.Day) throw new InvalidOperationException("Stale meal context");
                    var meal = LifeRules.PrepareMeal(life, Meal(flow.AnchorId), action.StringValue, cursor.InstanceId);
                    if (meal.Status == LifePreparationStatus.InsufficientFunds) throw new InvalidOperationException("余额不足，请选择其他餐食。");
                    if (meal.Status != LifePreparationStatus.Prepared) throw new InvalidOperationException("Meal already completed");
                    life = meal.Candidate; AddTime(state, EventSeconds(node, state, 600)); complete = true;
                    RememberMeal(state, Meal(flow.AnchorId), meal.Receipt.OptionId);
                    var chosenMeal = LifeContent.Find(meal.Receipt.Slot, meal.Receipt.OptionId);
                    outcome.Result = meal.Receipt.OptionId == "skip" ? "本餐选择不吃，未扣款，摄入 0 千卡。"
                        : "已享用" + chosenMeal.DisplayName + "，花费 " + meal.Receipt.Price + " 元，摄入 " + meal.Receipt.IntakeKcal + " 千卡。";
                    outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "Meal", Json = codec.Encode(meal.Receipt) }); break;
                case "finish-anchor":
                    if (flow.EntryPhase != EntryPhase.Finished || state.Phase != "Anchors") throw new InvalidOperationException("Anchor unfinished");
                    state.LastOvertime = Math.Max(0, state.AnchorGameSeconds - state.AnchorStandardSeconds); state.AnchorIndex++; break;
                case "day-step":
                    outcome = PrepareDayStep(root, state, training, ref body, ref life, actorId); break;
                case "next-day":
                    if (state.Phase != "AwaitNextDay") throw new InvalidOperationException("Day summary not ready");
                    state.Phase = "EnterDay"; state.DayStep = 1; break;
                default: throw new InvalidOperationException("Unknown action: " + action.ActionId);
            }
            if (complete) {
                if (node.Handler == "narrative") AddTime(state, EventSeconds(node, state, 0));
                FlowEngine.CompleteCurrent(flow, condition => condition == branch || condition == "result:" + branch || FlowContent.Current.Conditions.Any(c => c.Id == condition) && EvaluateFlow(root, condition, actorId, state, body, life, branch));
                if (node.Handler == "narrative") {
                    // A returned local choice and its business effects share one durable commit.
                    // The effects remain event nodes, never narrative instructions or UI confirmations.
                    for (int count = 0; FlowEngine.Current(flow) != null; count++) {
                        var nextCursor = FlowEngine.Current(flow);
                        var nextNode = flow.Tree.Nodes.Single(n => n.Id == nextCursor.NodeId);
                        if (nextNode.Handler != "effects") break;
                        if (count >= 64) throw new InvalidOperationException("Narrative effect chain exceeds limit");
                        FlowEngine.StartCurrent(flow);
                        ApplyConfiguredEffects(root, nextNode, nextCursor, actorId, ref body, ref life, outcome);
                        AddTime(state, EventSeconds(nextNode, state, 0));
                        FlowEngine.CompleteCurrent(flow, condition => condition == "result:" + branch || FlowContent.Current.Conditions.Any(c => c.Id == condition) && EvaluateFlow(root, condition, actorId, state, body, life, branch));
                    }
                }
                if (action.ActionId != "item-use") outcome.SemanticKey = cursor.InstanceId + "/complete";
                outcome.CheckKind = CheckKind.NormalCompletion; outcome.EventDisposition = EventDisposition.CompleteNode;
                if (outcome.Result == "已保存。") outcome.Result = "事件已完成并保存。";
            }
            state.LastMessage = outcome.Result;
            if (state.Day == previousDay) RecordChangedFields(state, previousBody, body, previousBalance, life.Wallet.Balance);
            Write(root, "session", state, actorId); Write(root, "flow", flow, actorId); Write(root, "training", training, actorId);
            Write(root, "body", body, actorId); Write(root, "life", life, actorId);
            if (outcome.CheckKind == CheckKind.NormalCompletion)
                outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = action.ActionId, Json = codec.Encode(new EventRecord { Day = state.Day, ActorId = actorId, Anchor = flow.AnchorId, Instance = cursor?.InstanceId, Text = outcome.Result, Source = (actorId == Actor ? source : CommandSource.NpcProduction).ToString(), GameSeconds = state.TotalGameSeconds }) });
            if (actorId != Actor && outcome.CheckKind == CheckKind.NormalCompletion) {
                CheckActorCompletion(root, outcome, actorId);
                outcome.CheckKind = CheckKind.None;
            }
            return outcome;
        }

        private PreparedOutcome Initialize(RootEnvelope root, string template, string[] unlocked)
        {
            if (root.FindFragment("session", Actor) != null) throw new InvalidOperationException("已有当前局；M1不提供未经确认的覆盖入口。");
            var intent = TemplateCatalog.PrepareInitialization(template, unlocked, root.RunId, Actor);
            var t = intent.Template;
            EnsureContentSnapshot(root);
            var initialBody = intent.CreateBody();
            Write(root, "session", new SessionState { Phase = "InitialPlan", TemplateId = t.Id, RandomState = initialSeed, InitialRandomSeed = initialSeed, DayStartBody = initialBody.Clone(), DayStartBalance = t.Balance });
            Write(root, "body", initialBody);
            Write(root, "training", new TrainingDayState { Progress = intent.CreateTraining() });
            Write(root, "life", LifeRules.Initialize(root.RunId, Actor, t.Balance, t.Affluence));
            Write(root, "plan", PlanPersistence.Export(new PlanState(root.RunId, Actor, "player-plan")));
            Write(root, "flow", new FlowState { RandomState = initialSeed });
            return Outcome(root.RunId + "/initialize", "新局已建立，请确认首日计划。");
        }
        private void FreezeQuota(RootEnvelope root, SessionState state, TrainingDayState training, string actorId = Actor)
        {
            if (actorId != Actor) {
                var ownPlan = Plan(root, actorId);
                var relationship = Read<FitnessSimulator.Game.Life.Relationships.RelationshipState>(root, "relationships");
                var arrangement = relationship.CurrentBuddy == actorId
                    ? ActorDayPlans.FollowPlayer(ownPlan, DayPlan(root, Read<SessionState>(root, "session")),
                        root.RunId + "/buddy/" + actorId + "/" + state.Day, Eligibility(state), SessionContent.TrainingAnchorIds)
                    : ActorDayPlans.ResolveBase(ownPlan, state.Day, Weekday(state.Day), Eligibility(state), SessionContent.TrainingAnchorIds);
                Write(root, "day-arrangement", ActorDayArrangementPersistence.Export(arrangement), actorId);
                training.Quota = arrangement.Slot.IsRest ? null : DayTrainingQuota.Freeze(actorId, state.Day,
                    arrangement.BaseRevision + "/" + arrangement.SourceActorId + "/" + arrangement.SourceRevision + "/" + arrangement.BindingId,
                    TrainingCatalogAdapter.ToTrainingEntries(arrangement), training.Progress);
                training.Workout = null; return;
            }
            var day = DayPlan(root, state, actorId);
            training.Quota = day.Slot.IsRest ? null : DayTrainingQuota.Freeze(actorId, state.Day, day.PlanRevision.ToString(CultureInfo.InvariantCulture), TrainingCatalogAdapter.ToTrainingEntries(day), training.Progress);
            training.Workout = null;
        }
        private void CheckCompletion(RootEnvelope root, PreparedOutcome outcome)
        { CheckActorCompletion(root, outcome, Actor); CheckProgression(root, outcome); }

        private void CheckActorCompletion(RootEnvelope root, PreparedOutcome outcome, string actorId)
        {
            var state = Read<SessionState>(root, "session", actorId);
            state.CompletionChecks++;
            if (actorId == Actor) {
                if (state.AnchorUnlocks == null) state.AnchorUnlocks = new List<AnchorUnlock>();
                foreach (var anchor in FlowContent.Current.Anchors)
                    if (!string.IsNullOrEmpty(anchor.UnlockCondition) && SessionContent.AvailableDay(state, anchor.Id) == 0
                        && EvaluateFlow(root, anchor.UnlockCondition, actorId, state)) {
                        state.AnchorUnlocks.Add(new AnchorUnlock { Id = anchor.Id, Day = state.Day + 1 });
                        if (anchor.Id == "early") state.EarlyAvailableDay = state.Day + 1;
                        if (anchor.Id == "late") state.LateAvailableDay = state.Day + 1;
                    }
            }
            // M1 has no competition/romance facts. Their ordered ending catalog is an M3 delivery,
            // not fabricated from body thresholds. The common completion boundary is exercised now.
            Write(root, "session", state, actorId);
            ObserveShops(root, state.Day, actorId);
        }
        private static void RequireStarted(SessionState state, NodeCursor cursor)
        { if (state.Phase != "Anchors" || cursor?.Phase != NodePhase.Started) throw new InvalidOperationException("Event is not started"); }
        private static void RequireHandler(SessionState state, NodeCursor cursor, FlowNode node, string handler)
        { RequireStarted(state, cursor); if (node.Handler != handler) throw new InvalidOperationException("Wrong event handler"); }
        private static MealSlot Meal(string anchor)
        { return (MealSlot)Enum.Parse(typeof(MealSlot), FlowContent.Anchor(anchor).MealSlot); }
        private static void AddTime(SessionState state, int seconds)
        { state.AnchorGameSeconds = checked(state.AnchorGameSeconds + seconds); state.TotalGameSeconds = checked(state.TotalGameSeconds + seconds); }
        private static uint NextRandom(SessionState state)
        { uint x = state.RandomState; x ^= x << 13; x ^= x >> 17; x ^= x << 5; state.RandomState = x; return x; }
        private static FrontendReply Reply(bool accepted, string message) => new FrontendReply { Accepted = accepted, Message = message };
        public void Dispose() { if (disposed) return; disposed = true; store?.Dispose(); }

        [Serializable] private sealed class InputRecord { public string SetId; public uint Seed; public string Source; public string KernelVersion; public InputEdge[] Edges; public TrainingSetResult Result; }
        [Serializable] private sealed class RequestPayload { public string ActionId; public string StringValue; public int IntValue; }
        [Serializable] private sealed class EventRecord { public int Day; public string ActorId; public string Anchor; public string Instance; public string Text; public string Source; public long GameSeconds; }
    }
}
