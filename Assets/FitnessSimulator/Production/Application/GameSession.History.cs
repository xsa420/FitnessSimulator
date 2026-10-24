using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        private const int HistoryPageSize = 16;
        private bool historyOpen;
        private string historyScreen = "catalog";
        private int historyPage, historySelectedIndex;
        private string historySelectedId;
        private string historyViewCacheKey, historyViewCacheJson;
        [Serializable] private sealed class HistoryEndingHeader { public string EndingId; }

        private bool IsHistoryOpen => historyOpen;
        private bool CanOpenHistory(RootEnvelope root)
        {
            if (root == null || newGameTemplate != null || error != null) return false;
            if (root.FindFragment("session", Actor) == null) return true;
            var progression = ReadProgressionRun(root);
            ProgressionRules.Validate(progression, ReadProgressionPermanent(root));
            if (progression.HasActive && progression.Active.Phase != EndingPhase.MainMenu) return false;
            if (root.FindFragment("training", Actor) != null && Read<TrainingDayState>(root, "training").Workout?.Phase == WorkoutPhase.Playing) return false;
            return true;
        }

        private bool TryExecuteHistory(FrontendAction action, out FrontendReply reply)
        {
            reply = null;
            if (action == null) return false;
            bool historyAction = action.ActionId != null && action.ActionId.StartsWith("history-", StringComparison.Ordinal);
            if (!historyAction && !historyOpen) return false;
            if (action.ActionId == "history-close")
            {
                historyOpen = false; historyScreen = "catalog"; historyPage = 0; historySelectedId = null;
                historyViewCacheKey = historyViewCacheJson = null;
                reply = Reply(true, null); return true;
            }
            if (!historyAction) { reply = Reply(false, "请先关闭结局历史，再进行其他操作。"); return true; }
            try
            {
                if (dispatcher == null || error != null || !CanOpenHistory(CurrentSnapshot))
                    throw new InvalidOperationException("请先完成当前训练或结局记录，再查看历史。");
                if (action.ActionId != "history-open" && !historyOpen) throw new InvalidOperationException("请先打开结局历史。");
                switch (action.ActionId)
                {
                    case "history-open": historyOpen = true; historyScreen = "catalog"; historyPage = 0; break;
                    case "history-catalog": historyScreen = "catalog"; break;
                    case "history-records": historyScreen = "records"; historyPage = 0; break;
                    case "history-page":
                        if (action.IntValue < 0 || action.IntValue > int.MaxValue / HistoryPageSize)
                            throw new InvalidOperationException("历史页码无效。");
                        // Only adjacent pages exposed by the current view are navigable.
                        if (historyScreen != "records" || Math.Abs((long)action.IntValue - historyPage) != 1
                            || !ReadHistoryView(CurrentSnapshot).Actions.Any(o => o.Id == "history-page" && o.IntValue == action.IntValue && o.Enabled))
                            throw new InvalidOperationException("该历史页当前不可用。");
                        historyPage = action.IntValue; break;
                    case "history-detail":
                        if (historyScreen != "records" || action.IntValue < historyPage * HistoryPageSize
                            || (long)action.IntValue >= (long)historyPage * HistoryPageSize + HistoryPageSize
                            || !ReadHistoryView(CurrentSnapshot).Actions.Any(o => o.Id == "history-detail" && o.IntValue == action.IntValue && o.StringValue == action.StringValue))
                            throw new InvalidOperationException("请选择当前页中的达成记录。");
                        historySelectedIndex = action.IntValue; historySelectedId = action.StringValue; historyScreen = "detail"; break;
                    case "history-back": historyScreen = "records"; break;
                    default: throw new InvalidOperationException("未知历史操作。");
                }
                historyViewCacheKey = historyViewCacheJson = null;
                reply = Reply(true, null);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException || ex is OverflowException)
            { reply = Reply(false, ex.Message); }
            return true;
        }

        private FrontendView ReadHistoryView(RootEnvelope root)
        {
            if (!historyOpen) return null;
            string key = root.RunId + ":" + root.Generation + ":" + historyScreen + ":" + historyPage + ":" + historySelectedIndex + ":" + historySelectedId;
            if (historyViewCacheKey == key && historyViewCacheJson != null) return codec.Decode<FrontendView>(historyViewCacheJson);
            FrontendView view;
            try
            {
                if (!CanOpenHistory(root)) throw new InvalidOperationException("当前状态不能浏览历史，请返回原页面。");
                view = historyScreen == "catalog" ? HistoryCatalog(root) : historyScreen == "records" ? HistoryRecords(root) : HistoryDetail(root);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException || ex is OverflowException)
            {
                view = new FrontendView { RunId = root.RunId, Revision = root.Revision, ScreenId = "history-error", Title = "历史暂时无法读取",
                    Body = "历史数据未被清除。请关闭页面并重试读取。", Status = ex.GetType().Name,
                    Actions = new[] { Option("history-close", "返回原页面") } };
            }
            historyViewCacheKey = key; historyViewCacheJson = codec.Encode(view);
            return codec.Decode<FrontendView>(historyViewCacheJson);
        }

        private FrontendView HistoryCatalog(RootEnvelope root)
        {
            var definitions = EndingProgression.DefaultCatalog();
            var catalogIds = new HashSet<string>(definitions.Select(d => d.Id), StringComparer.Ordinal);
            var achieved = new HashSet<string>(StringComparer.Ordinal);
            long offset = 0;
            while (true)
            {
                var page = store.ReadLog("permanent/history", offset, 64);
                if (page.Count == 0) break;
                foreach (var frame in page)
                    if (frame.Kind == "AchievementFrozen.v1")
                    {
                        var header = codec.Decode<HistoryEndingHeader>(frame.Json);
                        if (header == null || string.IsNullOrEmpty(header.EndingId)) throw new InvalidOperationException("历史条目身份缺失。");
                        if (catalogIds.Contains(header.EndingId)) achieved.Add(header.EndingId);
                    }
                    else if (frame.Kind != "NoteFinal.v1") throw new InvalidOperationException("历史记录版本不受支持。");
                offset += page.Count;
            }
            return new FrontendView { RunId = root.RunId, Revision = root.Revision, ScreenId = "history-catalog", Title = "结局历史",
                Body = string.Join("\n", definitions.OrderBy(d => d.Order).Select(d => d.Title + " · " + (achieved.Contains(d.Id) ? "已达成" : "未知结局"))),
                Actions = new[] { Option("history-records", "查看已达成记录"), Option("history-close", "返回原页面") } };
        }

        private FrontendView HistoryRecords(RootEnvelope root)
        {
            int skip = checked(historyPage * HistoryPageSize);
            var entries = ProgressionRules.ReadHistory(store, skip, HistoryPageSize + 1);
            var actions = new List<FrontendOption>();
            for (int i = 0; i < Math.Min(entries.Count, HistoryPageSize); i++)
            {
                var snapshot = entries[i].Snapshot;
                actions.Add(Option("history-detail", snapshot.EndingTitle + " · 第 " + snapshot.Day + " 日 · " + HistoryTime(snapshot), snapshot.AchievementId, checked(skip + i)));
            }
            if (historyPage > 0) actions.Add(Option("history-page", "上一页", number: historyPage - 1));
            if (entries.Count > HistoryPageSize) actions.Add(Option("history-page", "下一页", number: checked(historyPage + 1)));
            actions.Add(Option("history-catalog", "返回结局目录")); actions.Add(Option("history-close", "返回原页面"));
            return new FrontendView { RunId = root.RunId, Revision = root.Revision, ScreenId = "history-records", Title = "达成记录 · 第 " + (historyPage + 1) + " 页",
                Body = entries.Count == 0 ? "尚无达成记录。" : "记录按达成顺序排列；只能阅读，不能作为存档继续。", Actions = actions.ToArray() };
        }

        private FrontendView HistoryDetail(RootEnvelope root)
        {
            var entries = ProgressionRules.ReadHistory(store, historySelectedIndex, 1);
            if (entries.Count != 1 || entries[0].Snapshot.AchievementId != historySelectedId) throw new InvalidOperationException("历史记录位置已改变。");
            var entry = entries[0]; var snapshot = entry.Snapshot;
            var body = codec.Decode<BodyState>(snapshot.CharacterAndMusclesJson);
            if (body == null) throw new InvalidOperationException("历史身体快照缺失。");
            body.Validate();
            var planRecord = codec.Decode<PlanStateRecord>(snapshot.TrainingPlanJson);
            if (planRecord == null || planRecord.RunId != snapshot.RunId || planRecord.ActorId != body.ActorId)
                throw new InvalidOperationException("历史计划与角色身份不一致。");
            var plan = PlanPersistence.Restore(planRecord, catalog, SessionContent.TrainingAnchorIds);
            string note = entry.NoteState == AchievementNoteState.Pending ? "尚未提交备注" : entry.NoteState == AchievementNoteState.CommittedEmpty ? "已明确留空" : entry.Note;
            string planText = DescribePlan(plan); string[] rows = BodyRows(body);
            return new FrontendView { RunId = root.RunId, Revision = root.Revision, ScreenId = "history-detail", Title = snapshot.EndingTitle,
                Body = "游戏第 " + snapshot.Day + " 日\n达成时间：" + HistoryTime(snapshot) + "\n\n当时的身体与肌肉状态\n" + string.Join("\n", rows)
                    + "\n\n当时的训练计划\n" + planText + "\n\n备注：" + note,
                PlanText = planText, BodyRows = rows, CanOpenAuxiliary = false,
                Actions = new[] { Option("history-back", "返回记录列表"), Option("history-close", "返回原页面") } };
        }
        private static string HistoryTime(AchievementSnapshot snapshot)
        {
            return DateTimeOffset.ParseExact(snapshot.DeviceTimeIso8601, "o", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        }
    }
}
