using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        private RootEnvelope liveProjectionRoot;
        private string liveProjectionContentHash, liveProjectionJson;
        public FrontendView ReadView()
        {
            using (ContentScope(newGameTemplate != null)) {
                // During an input set only kernel state changes; the committed business/config projection is stable.
                // Cache serialized data, never the caller's mutable view or mutable content DTOs.
                bool live = dispatcher != null && error == null && !atMenu && newGameTemplate == null && game != null;
                if (live && ReferenceEquals(liveProjectionRoot, CurrentSnapshot) && liveProjectionContentHash == ContentRuntime.Current.Hash) {
                    var cached = codec.Decode<FrontendView>(liveProjectionJson);
                    ProjectLiveInput(cached); return cached;
                }
                var view = ReadViewBound();
                if (live && view.TrainingInput != null) {
                    liveProjectionRoot = CurrentSnapshot; liveProjectionContentHash = ContentRuntime.Current.Hash;
                    liveProjectionJson = codec.Encode(view);
                }
                return view;
            }
        }
        private FrontendView ReadViewBound()
        {
            if (newGameTemplate != null) return NewGameDraftView();
            if (error != null || dispatcher == null) {
                var reset = Option("new-game", "保留损坏现场并新建当前局", TemplateCatalog.DefaultId);
                reset.Enabled = store is LocalCheckpointStore local && local.CanResetCorruptM1Store(out _);
                reset.DisabledReason = "旧数据状态尚不能验证，请先重试读取。";
                return new FrontendView { ScreenId = "error", Title = atMenu ? "主菜单 · 当前局不可读取" : "当前进度已暂停", Body = error ?? "会话不可用。", Actions = new[] {
                    Option("retry-storage", "重新读取保存结果"), Option("open-menu", "返回主菜单"), reset } };
            }
            var root = CurrentSnapshot;
            var historyView = ReadHistoryView(root);
            if (historyView != null) return historyView;
            var endingView = ReadProgressionView(root);
            if (endingView != null) {
                if (endingView.ScreenId == "ending-menu" && CanOpenHistory(root))
                    endingView.Actions = endingView.Actions.Concat(new[] { Option("history-open", "结局历史") }).ToArray();
                return endingView;
            }
            if (atMenu && root.FindFragment("session", Actor) != null) return new FrontendView {
                RunId = root.RunId, Revision = root.Revision, ScreenId = "menu", Title = "健身人生", Body = "继续当前进度，或重新开始。",
                Actions = new[] { Option("continue-game", "继续游戏") }.Concat(TemplateOptions(root)).Concat(new[] { Option("history-open", "结局历史") }).ToArray() };
            if (root.FindFragment("session", Actor) == null) return new FrontendView {
                RunId = root.RunId, Revision = root.Revision, ScreenId = "menu", Title = "健身人生", Body = "从稳步新手开始，安排训练、用餐并度过每一天。",
                Actions = TemplateOptions(root).Concat(new[] { Option("history-open", "结局历史") }).ToArray() };
            var state = Read<SessionState>(root, "session");
            var training = Read<TrainingDayState>(root, "training");
            var flow = Read<FlowState>(root, "flow");
            if (IsAutomaticTransition(state, flow, training)) return TransitionView(root, state);
            var body = Read<BodyState>(root, "body");
            var life = Read<LifeActorState>(root, "life");
            var plan = Plan(root);
            var actions = new List<FrontendOption>();
            var view = new FrontendView { RunId = root.RunId, Revision = root.Revision, ScreenId = state.Phase, Title = "正在推进日程", Body = state.LastMessage,
                Status = "第 " + state.Day + " 日  ·  余额 " + life.Wallet.Balance + "  ·  摄入 " + life.Nutrition.IntakeKcal + " 千卡",
                CanOpenAuxiliary = training.Workout?.Phase != WorkoutPhase.Playing,
                PlanText = DescribePlan(plan), ScheduleText = string.Join(" → ", SessionContent.Anchors(state).Select(SessionContent.Name)),
                BodyRows = ProjectRelationshipSummary(root).Concat(new[] { "", "身体状态" }).Concat(BodyRows(body)).ToArray(), PlanAnchorOptions = PlanAnchors(state, state.Phase == "InitialPlan" ? state.Day : state.Day + 1) };
            if (TryProjectCompetition(root, state, view, actions)) { ApplyWorldAvailability(root, actions); view.Actions = actions.ToArray(); return view; }
            if (state.Phase == "InitialPlan") {
                view.Title = "确认首日计划";
                view.Body = "首次保存立即生效；之后修改的计划明日起生效。可选择动作、训练量、训练时段和休息日。";
                actions.Add(Option("save-plan", "编辑并保存首日计划", codec.Encode(PlanCatalog.CreateBenchPressSlice())));
            } else if (state.Phase == "AwaitNextDay") {
                view.Title = "日结"; view.Body = state.DaySummary;
                actions.Add(Option("next-day", "进入次日"));
            } else if (state.Phase == "DayEnd" || state.Phase == "EnterDay") {
                return TransitionView(root, state);
            } else if (flow.Tree != null && flow.Day == state.Day && state.AnchorIndex < SessionContent.Anchors(state).Length && flow.AnchorId == SessionContent.Anchors(state)[state.AnchorIndex]) {
                view.Title = SessionContent.Name(flow.AnchorId);
                if (flow.EntryPhase == EntryPhase.ChoicePending) {
                    view.ScreenId = "choice"; view.Body = "选择这个时段要做的事。确认后进入。";
                    foreach (string id in flow.ChoiceDefinitionIds) actions.Add(Option("choose-entry", EntryName(id), id));
                } else {
                    var cursor = FlowEngine.Current(flow);
                    var node = cursor == null ? null : flow.Tree.Nodes.Single(n => n.Id == cursor.NodeId);
                    if (cursor?.Phase == NodePhase.Started && ProjectNarrative(root, node, view, actions)) { ApplyWorldAvailability(root, actions); view.Actions = actions.ToArray(); return view; }
                    if (cursor?.Phase == NodePhase.Started) {
                        view.ScreenId = node.Handler + "/" + cursor.InstanceId;
                        if (!ProjectActivity(root, state, flow, node, cursor, view, actions)) switch (node.Handler) {
                            case "calm": view.Body = MissedTrainingNotice(root, state, flow, training, "平静度过") + "平静度过这个时段。"; actions.Add(Option("continue", "继续")); break;
                            case "walk": view.Body = MissedTrainingNotice(root, state, flow, training, "散步") + "出门走走，看看周围的生活。\n本次耗时：5 分钟。"; actions.Add(Option("continue", "完成散步")); break;
                            case "work":
                                view.Body = "完成这个时段的工作后获得收入。\n本次收入：" + (LifeContent.BaseHalfDayIncome + life.Wallet.Affluence) + " 元；未完成不发放，日结不再重复发薪。";
                                if (life.HasLastIncome && life.LastIncome.Day == state.Day)
                                    view.Body = "旧存档已结清今天收入；完成本时段工作不会重复发薪。";
                                actions.Add(Option("continue", "完成工作")); break;
                            case "meal":
                                view.Body = "点击餐食即可付款并用餐；也可以选择不吃。上次成功的选择会保留标记。";
                                foreach (var meal in LifeContent.Menu(Meal(flow.AnchorId))) {
                                    var choice = Option("meal", meal.DisplayName + " · " + meal.Price + " 元 · " + meal.IntakeKcal + " 千卡", meal.Id);
                                    choice.Enabled = life.Wallet.Balance >= meal.Price; choice.DisabledReason = "余额不足";
                                    choice.IsPreferred = LastMeal(state, Meal(flow.AnchorId)) == meal.Id;
                                    if (choice.IsPreferred) choice.Label += " · 上次选择";
                                    actions.Add(choice);
                                }
                                break;
                            case "effects": case "social": case "end-action": case "next-anchor": view.Body = node.Title; actions.Add(Option("continue", "继续")); break;
                            case "training":
                                view.Body = "今天的训练计划已固定。按组训练，已完成的每组会保留。";
                                actions.Add(Option("continue", "进入训练")); break;
                            case "group-story":
                                view.Body = training.Workout.Phase == WorkoutPhase.Ended ? "当前没有可继续的动作。" : "调整姿势，准备下一组" + ExerciseName(training.Workout.Quota.Entries[training.Workout.EntryIndex].Plan.ExerciseId) + "。\n第 " + training.Workout.SetOrdinal + " 组 / " + training.Workout.Quota.Entries[training.Workout.EntryIndex].Plan.Sets + " 组。";
                                if (!SessionContent.IsAutomaticPostureHint(flow, training, node)) actions.Add(Option("continue", "继续"));
                                break;
                            case "play": BuildTrainingView(view, actions, training, body); break;
                            case "settle":
                                var result = training.Workout.PendingResult;
                                view.Body = "本组结果已保存：有效完成 " + result.EffectiveReps + " 次，失误 " + result.FailedAttempts + " 次。\n确认后结算成长、疲劳与本组耗时。";
                                actions.Add(Option("settle", "结算本组")); break;
                            case "followers":
                                view.Body = DescribeSet(training.Workout, body) + "\n确认后继续训练流程；受伤动作的剩余组会结束。";
                                actions.Add(Option("continue", "继续训练流程"));
                                break;
                            case "summary":
                                view.Body = "训练结束。\n已完成 " + training.Workout.SettledSetIds.Length + " 组\n训练与休息耗时 " + state.AnchorGameSeconds + " 秒\n已完成结果已保存。";
                                actions.Add(Option("continue", "返回日程")); break;
                            default: throw new InvalidOperationException("Unknown handler projection: " + node.Handler);
                        }
                    } else return TransitionView(root, state);
                }
            }
            if (view.CanOpenAuxiliary && plan.Active != null && IsEveningPlanWindow(state)) {
                var record = PlanPersistence.Export(plan);
                actions.Add(Option("save-plan", "修改明日起的计划", codec.Encode((record.Pending ?? record.Active).Draft)));
            }
            var relationshipSummary = ProjectRelationshipRestSummary(root);
            if (!string.IsNullOrEmpty(relationshipSummary)) view.Body += "\n" + relationshipSummary;
            AppendRelationshipOptions(root, actions);
            ApplyWorldAvailability(root, actions);
            view.Actions = actions.ToArray();
            if (view.CanOpenAuxiliary) view.Actions = view.Actions.Concat(new[] { Option("open-menu", "返回主菜单") }).ToArray();
            return view;
        }

        private static bool IsAutomaticTransition(SessionState state, FlowState flow, TrainingDayState training)
        {
            if (state.Phase == "DayEnd" || state.Phase == "EnterDay") return true;
            if (state.Phase != "Anchors") return false;
            var anchors = SessionContent.Anchors(state);
            if (state.AnchorIndex >= anchors.Length || flow.Tree == null || flow.Day != state.Day || flow.AnchorId != anchors[state.AnchorIndex]) return true;
            if (flow.EntryPhase == EntryPhase.ChoicePending) return false;
            var cursor = FlowEngine.Current(flow);
            if (cursor == null || cursor.Phase != NodePhase.Started) return true;
            var node = flow.Tree.Nodes.Single(n => n.Id == cursor.NodeId);
            return SessionContent.IsAutomaticPostureHint(flow, training, node)
                || node.Handler == "play" && training.Workout != null && training.Workout.Phase != WorkoutPhase.Playing && training.Workout.Phase != WorkoutPhase.PreSet;
        }

        private FrontendView TransitionView(RootEnvelope root, SessionState state)
        {
            var life = Read<LifeActorState>(root, "life");
            var anchors = SessionContent.Anchors(state);
            string anchor = state.AnchorIndex < anchors.Length ? SessionContent.Name(anchors[state.AnchorIndex]) : "日末";
            return new FrontendView {
                RunId = root.RunId, Revision = root.Revision, ScreenId = state.Phase,
                IsTransitioning = true, CanOpenAuxiliary = false,
                Title = "第 " + state.Day + " 日 · " + anchor,
                Body = "当前日程：" + string.Join(" → ", anchors.Select(SessionContent.Name)),
                Status = "第 " + state.Day + " 日  ·  余额 " + life.Wallet.Balance + "  ·  摄入 " + life.Nutrition.IntakeKcal + " 千卡"
            };
        }

        private void ApplyWorldAvailability(RootEnvelope root, List<FrontendOption> actions)
        {
            foreach (var action in actions) if (action.Enabled && action.Id != "open-menu" && !PlayerCanAct(root, action.Id)) {
                action.Enabled = false; action.DisabledReason = "其他人物正在完成当前时段";
            }
        }

        private string MissedTrainingNotice(RootEnvelope root, SessionState state, FlowState flow, TrainingDayState training, string eventName)
        {
            var today = DayPlan(root, state).Slot;
            if (today.IsRest || today.AnchorId != flow.AnchorId || training.Workout != null) return "";
            // This describes the already selected current event; it never predicts later draws.
            return "今天计划：" + SessionContent.Name(today.AnchorId) + " · " +
                string.Join("、", today.Entries.Select(e => ExerciseName(e.ExerciseId) + " " + e.TargetSets + "×" + e.TargetRepetitions)) +
                "。\n本时段发生" + eventName + "。\n本次训练没有开始。\n\n";
        }

        private void BuildTrainingView(FrontendView view, List<FrontendOption> actions, TrainingDayState training, BodyState body)
        {
            var workout = training.Workout;
            if (workout.Phase == WorkoutPhase.Playing) {
                view.Body = "根据提示完成当前动作。";
                ProjectLiveInput(view);
                return;
            }
            if (workout.Phase != WorkoutPhase.PreSet) { view.IsTransitioning = true; view.Body = "本组有效完成 " + (workout.PendingResult?.EffectiveReps ?? 0) + " 次。"; return; }
            var entry = workout.Quota.Entries[workout.EntryIndex];
            var progress = training.Progress.Exercise(entry.Plan.ExerciseId);
            var definition = ExerciseCatalog.Get(entry.Plan.ExerciseId);
            long strength = definition.TargetRegionIds.Sum(id => body.Region(id).StrengthMicro);
            var maximum = definition.TargetRegionIds.Select(id => body.Region(id)).OrderByDescending(r => r.TemporaryFatigueMicro).First();
            int regionIndex = Array.IndexOf(body.Regions.Select(r => r.RegionId).ToArray(), maximum.RegionId);
            string fatigueName = RegionNames[regionIndex];
            view.Body = "调整姿势，准备" + ExerciseName(entry.Plan.ExerciseId) + " · 第 " + workout.SetOrdinal + " 组 / " + entry.Plan.Sets + " 组\n目标 " + entry.Plan.Reps + " 次 · 容量梯度 " + progress.HighestUnlockedTier + "\n开始后按当前动作提示操作；三次失误结束本组。\n目标肌群最高临时疲劳：" + fatigueName + " " + Format(maximum.TemporaryFatigueMicro) + "。\n结算后超过100开始有受伤风险，达到150必定受伤；可在组前休息降低临时疲劳。";
            actions.Add(Option("begin-set", "手动容量组", "manual"));
            var challenge = Option("begin-set", "力量挑战", "manual", 1);
            challenge.Enabled = progress.HighestUnlockedTier < 4 && strength >= definition.Threshold(Math.Min(4, progress.HighestUnlockedTier + 1)) * NumericKernel.Micro;
            challenge.DisabledReason = "尚未达到下一梯度力量要求，或已达最高梯度"; actions.Add(challenge);
            var automatic = Option("begin-set", "自动容量组", "auto");
            automatic.Enabled = progress.AutomaticTiers[progress.HighestUnlockedTier - 1]; automatic.DisabledReason = "先以真实输入成功完成此梯度"; actions.Add(automatic);
            foreach (int seconds in TrainingContent.Current.Fatigue.RestSeconds) actions.Add(Option("rest", "休息 " + seconds + " 秒", null, seconds));
            actions.Add(Option("end-training", "结束训练并进入下一时段"));
        }

        private void ProjectLiveInput(FrontendView view)
        {
            if (game == null) return;
            var live = game.View; view.TrainingInput = ProjectInput(live); view.BenchPress = null;
            if (live.ExerciseId == PlanCatalog.BenchPressId) view.BenchPress = new FrontendBenchPressView { SetId = liveSetId, Balance = live.Balance,
                WindowHalfWidth = live.Window, Progress = live.AttemptMilliseconds / 2400d,
                EffectiveReps = live.EffectiveReps, TargetReps = live.TargetReps, Failures = live.FailedAttempts,
                Instruction = live.IsPreparing ? "准备：保持手指状态" : "向左或向右修正，让标记停留在绿色区间",
                IsSuspended = live.IsSuspended, IsComplete = live.IsComplete,
                LeftHeld = live.HeldInputs.Any(i => i.Kind == TrainingInputKind.ButtonDown && i.Button == TrainingButton.Left),
                RightHeld = live.HeldInputs.Any(i => i.Kind == TrainingInputKind.ButtonDown && i.Button == TrainingButton.Right) };
        }

        private FrontendOption[] PlanAnchors(SessionState state, int effectiveDay)
        {
            var eligibility = EditingEligibility(state);
            return EditableTrainingAnchors.Select(id => new FrontendOption { Id = id, Label = SessionContent.Name(id),
                Enabled = eligibility.Allows(id, effectiveDay), DisabledReason = "计划生效日尚未解锁此时段" }).ToArray();
        }

        private FrontendOption[] TemplateOptions(RootEnvelope root) => TemplateCatalog.All.Select(template => {
            var option = Option("new-game", "新游戏 · " + template.Name + "\n" + template.Description + "\n优势：" + template.Advantage + "；短板：" + template.Drawback, template.Id);
            option.Enabled = TemplateCatalog.IsAvailable(template.Id, UnlockedTemplates(root));
            option.DisabledReason = option.Enabled ? "" : template.LockedHint; return option;
        }).ToArray();

        private static string EntryName(string id)
        {
            var configured = FlowContent.Current.Events.FirstOrDefault(e => e.Id == id);
            if (configured != null) return configured.Title;
            switch (id) {
                case "training-choice": return "训练"; case "all-day-study": return "全天学习";
                case "evening-study": return "晚间学习"; case "corner-shop": return "街角书屋";
                case "gym-shop": return "馆内书架"; default: return id;
            }
        }

        private static FrontendTrainingInputView ProjectInput(TrainingInputView v) => new FrontendTrainingInputView {
            ExerciseId = v.ExerciseId, Stage = v.Stage, Feedback = v.Feedback, AttemptMilliseconds = v.AttemptMilliseconds,
            EffectiveReps = v.EffectiveReps, FailedAttempts = v.FailedAttempts, TargetReps = v.TargetReps, Step = v.Step,
            VisibleDirection = v.VisibleDirection, IsPreparing = v.IsPreparing, IsSuspended = v.IsSuspended, IsComplete = v.IsComplete,
            SignalGreen = v.SignalGreen, Balance = v.Balance, Window = v.Window, TargetSeconds = v.TargetSeconds,
            FirstOpening = v.FirstOpening, SecondOpening = v.SecondOpening,
            HeldInputs = v.HeldInputs.Select(i => new FrontendTrainingInput { Kind = i.Kind.ToString(),
                Button = i.Kind == TrainingInputKind.ButtonDown || i.Kind == TrainingInputKind.ButtonUp ? i.Button.ToString() : null,
                PointerId = i.PointerId, X = i.X, Y = i.Y }).ToArray() };
        private static FrontendOption Option(string id, string label, string value = null, int number = 0) => new FrontendOption {
            Id = id, Label = label, StringValue = value, IntValue = number,
            RequiresConfirmation = id == "choose-entry" || id == "end-action" || id == "end-training" || id == "new-game",
            ConfirmationText = id == "new-game" ? "进入新局计划编辑？只有合法计划确认保存后才替换当前局；取消会保留原进度。" : "确认“" + label + "”？" };
        private static string Format(long micro) => (micro / (double)NumericKernel.Micro).ToString("0.##", CultureInfo.InvariantCulture);
        private static string AttributeValue(string id, long micro, bool signed = false)
        {
            var attribute = BodyContent.Attribute(id); double value = micro / 1000000d;
            string format = attribute.DisplayDecimals == 0 ? "0" : "0." + new string('#', attribute.DisplayDecimals);
            string result = value.ToString(format, CultureInfo.InvariantCulture);
            if (micro != 0 && Math.Round(value, attribute.DisplayDecimals) == 0) result = (micro < 0 ? "-" : "") + "<" + Math.Pow(10, -attribute.DisplayDecimals).ToString(format, CultureInfo.InvariantCulture);
            return (signed && micro > 0 ? "+" : "") + result + " " + attribute.Unit;
        }
        private static string AttributeText(string id, long micro) => BodyContent.Attribute(id).Name + " " + AttributeValue(id, micro);
        private static string ExerciseName(string id)
        {
            return TrainingContent.Current.Exercises.Single(e => e.Id == id).Name;
        }
        private static string DescribePlan(PlanState plan)
        {
            if (plan.Active == null) return "首日计划尚未保存。";
            return "当前：" + DescribeRevision(plan.Active) + (plan.Pending == null ? "" : "\n\n明日起：" + DescribeRevision(plan.Pending));
        }
        private static string DescribeRevision(PlanRevision plan) => (plan.Mode == PlanMode.NaturalWeek ? "自然周" : "天数循环") + "，第 " + plan.EffectiveDay + " 日生效\n" +
            string.Join("\n", plan.Slots.Select(s => "第 " + (s.Index + 1) + " 项：" + (s.IsRest ? "休息" : SessionContent.Name(s.AnchorId) + " " + string.Join("、", s.Entries.Select(e => ExerciseName(e.ExerciseId) + " " + e.TargetSets + "×" + e.TargetRepetitions)))));
        private static string[] RegionNames => BodyContent.Current.Regions.Select(r => r.Name).ToArray();
        private static string[] BodyRows(BodyState body)
        {
            var rows = new List<string> { AttributeText("stress", body.StressMicro) + " · " + AttributeText("mental_stress", body.MentalStressMicro) + " · " + AttributeText("body_fat", body.BodyFatMicro) + " · " + AttributeText("base_charm", body.BaseCharmMicro), FitnessSimulator.Game.Presentation.PresentationContent.ContourDescription(body.BodyFatMicro / 1000000d), string.Join(" / ", new[] { "strength", "local_fatigue", "temporary_fatigue" }.Select(id => BodyContent.Attribute(id).Name)) };
            foreach (var definition in BodyContent.Current.Regions) { var r = body.Regions.Single(x => x.RegionId == definition.Id); rows.Add(definition.Name + "：" + AttributeValue("strength", r.StrengthMicro) + " / " + AttributeValue("local_fatigue", r.LocalFatigueMicro) + " / " + AttributeValue("temporary_fatigue", r.TemporaryFatigueMicro) + " · " + FitnessSimulator.Game.Presentation.PresentationContent.StrengthDescription(r.StrengthMicro / 1000000d) + (r.IsInjured ? " · 受伤，进入第 " + r.ExpiresAtStartOfDay + " 日解除" : "")); }
            rows.Add("各动作：" + BodyContent.Attribute("knowledge").Name + " / " + BodyContent.Attribute("proficiency").Name);
            foreach (var exercise in body.Exercises) rows.Add(ExerciseName(exercise.ExerciseId) + "：" + AttributeValue("knowledge", exercise.KnowledgeMicro) + " / " + AttributeValue("proficiency", exercise.ProficiencyMicro));
            return rows.ToArray();
        }
    }
}
