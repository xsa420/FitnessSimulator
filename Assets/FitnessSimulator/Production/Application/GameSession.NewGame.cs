using System;
using System.Linq;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        // Both template and first plan remain a local draft until this one durable publication.
        private FrontendReply CreateRunWithPlan(FrontendAction action)
        {
            if (string.IsNullOrWhiteSpace(action.OperationId)) return Reply(false, "操作标识缺失。");
            RootEnvelope before = dispatcher?.Snapshot;
            bool retry = before != null && store.FindReceipt(action.OperationId)?.SemanticKey.StartsWith("new-run:", StringComparison.Ordinal) == true;
            string template = newGameTemplate ?? (retry ? Read<SessionState>(before, "session").TemplateId : null);
            if (template == null) return Reply(false, "请先选择新局模板。");
            var fresh = new RootEnvelope { RunId = ContentHash.Sha256("new-run/" + action.OperationId).Substring(0, 32), ContentVersion = SessionContent.Version };
            try {
                Initialize(fresh, template, before == null ? Array.Empty<string>() : UnlockedTemplates(before));
                uint seed = retry ? Read<SessionState>(before, "session").InitialRandomSeed : before == null || before.Generation == 0 ? initialSeed : Convert.ToUInt32(fresh.RunId.Substring(0, 8), 16) | 1u;
                var state = Read<SessionState>(fresh, "session"); state.RandomState = state.InitialRandomSeed = seed; Write(fresh, "session", state);
                var flow = Read<FlowState>(fresh, "flow"); flow.RandomState = seed; Write(fresh, "flow", flow);
                Prepare(fresh, action); // Validates first plan and freezes the quota before touching the previous run.
                InitializeRelationships(fresh);
                InitializeNpcActors(fresh);
                foreach (string actorId in new[] { Actor }.Concat(FitnessSimulator.Game.Life.Relationships.RelationshipRules.CharacterIds))
                    InitializeEconomy(fresh, Read<SessionState>(fresh, "session", actorId), actorId);
                EnsureCompetition(fresh);
                InitializeProgression(fresh);
                InitializeWorld(fresh);
            } catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return Reply(false, "开局计划未保存：" + ex.Message); }

            if (error != null) {
                if (!(store is LocalCheckpointStore local)) return Reply(false, "存储不可访问，请先重试。");
                var reset = local.ResetCorruptM1Store(true);
                if (reset.Status != StoreResetStatus.Reset) { Diagnostic = reset.Error; return Reply(false, "原数据已保留，暂不能建立新局。"); }
                OpenStore();
                if (error != null) return Reply(false, error);
                before = CurrentSnapshot;
            }
            if (before == null || dispatcher == null) return Reply(false, "存储尚未就绪。");
            var command = Envelope(action, before);
            command.ActionKind = "BeginNewRun";
            command.RunId = newGameOriginRun ?? action.RunId ?? before.RunId;
            var result = dispatcher.BeginNewRun(command, fresh);
            if (result.Status == CommandStatus.Committed || result.Status == CommandStatus.AlreadyCommitted) {
                runContent = installedContent;
                readSnapshot = null;
                newGameTemplate = null; newGameOriginRun = null; atMenu = false; SyncKernel();
                return Reply(true, "新局与首日计划已一起保存。" );
            }
            if (result.Status == CommandStatus.StorageOutcomeUnknown || result.Status == CommandStatus.StorageUnavailable) {
                Diagnostic = result.Error; error = "开局保存尚未确认，请重新读取保存结果。";
                newGameTemplate = null; newGameOriginRun = null;
            }
            return Reply(false, result.Error ?? result.Status.ToString());
        }

        private FrontendView NewGameDraftView()
        {
            var root = dispatcher?.Snapshot;
            var week = new PlanDraft { Mode = PlanMode.NaturalWeek, Slots = new PlanSlotDraft[7] };
            week.Slots[0] = PlanCatalog.Training(0, TrainingCombination.Free, PlanCatalog.BenchPressId);
            for (int i = 1; i < 7; i++) week.Slots[i] = PlanCatalog.Rest(i);
            return new FrontendView {
                RunId = root?.RunId, Revision = root?.Revision ?? 0, ScreenId = "InitialPlan", Title = "确认首日计划",
                Status = "新局草稿 · 尚未开始", Body = "模板与初始计划全部合法确认后，才会建立新局并替换旧进度。取消或非法计划不会清除旧局。\n可编辑周期或自然周计划，选择动作、训练量和休息日。",
                PlanAnchorOptions = PlanAnchors(new SessionState(), 1),
                Actions = new[] {
                    Option("save-plan", "编辑并确认卧推周期计划", codec.Encode(PlanCatalog.CreateBenchPressSlice())),
                    Option("save-plan", "编辑并确认卧推自然周计划", codec.Encode(week)),
                    Option("cancel-new-game", "取消开局 · 保留原进度") }
            };
        }
    }
}
