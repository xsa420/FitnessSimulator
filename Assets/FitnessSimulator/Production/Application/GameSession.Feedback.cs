using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        private static string Signed(long micro) => (micro > 0 ? "+" : "") + Format(micro);
        private static string SignedInteger(long value) => (value > 0 ? "+" : "") + value;
        private static string DescribeSet(WorkoutSnapshot workout, BodyState body)
        {
            var before = workout.PreSetBody;
            var request = workout.PendingRequest;
            var result = workout.PendingResult;
            var definition = ExerciseCatalog.Get(request.ExerciseId);
            var oldExercise = before.Exercise(request.ExerciseId);
            var exercise = body.Exercise(request.ExerciseId);
            var lines = new List<string> {
                "本组已结算并保存 · " + (result.Success ? "达到目标" : "未达到全部目标"),
                "有效 " + result.EffectiveReps + " / " + request.TargetReps + " 次 · 失误 " + result.FailedAttempts + " 次 · 耗时 " + result.EffectiveReps * definition.SecondsPerRep + " 秒",
                AttributeText("proficiency", exercise.ProficiencyMicro) + "（" + AttributeValue("proficiency", exercise.ProficiencyMicro - oldExercise.ProficiencyMicro, true) + "） · " + AttributeText("knowledge", exercise.KnowledgeMicro) + "（" + AttributeValue("knowledge", exercise.KnowledgeMicro - oldExercise.KnowledgeMicro, true) + "）",
                AttributeText("stress", body.StressMicro) + "（" + AttributeValue("stress", body.StressMicro - before.StressMicro, true) + "） · " + AttributeText("body_fat", body.BodyFatMicro) + "（" + AttributeValue("body_fat", body.BodyFatMicro - before.BodyFatMicro, true) + "）",
                "目标肌群：力量变化 / 局部疲劳变化 / 临时疲劳变化"
            };
            foreach (string id in definition.TargetRegionIds) {
                var previous = before.Region(id); var current = body.Region(id);
                int index = Array.IndexOf(body.Regions.Select(r => r.RegionId).ToArray(), id);
                lines.Add(BodyContent.Current.Regions.Single(r => r.Id == id).Name + "：" + AttributeValue("strength", current.StrengthMicro - previous.StrengthMicro, true) + " / " + AttributeValue("local_fatigue", current.LocalFatigueMicro - previous.LocalFatigueMicro, true) + " / " + AttributeValue("temporary_fatigue", current.TemporaryFatigueMicro - previous.TemporaryFatigueMicro, true) + (current.IsInjured ? " · 已受伤" : ""));
            }
            return string.Join("\n", lines);
        }
        private static void RecordChangedFields(SessionState state, BodyState before, BodyState after, long balanceBefore, long balanceAfter)
        {
            Action<string, long, long> record = (name, oldValue, newValue) => { if (oldValue != newValue && !state.ChangedFields.Contains(name)) state.ChangedFields.Add(name); };
            record("wallet", balanceBefore, balanceAfter); record("stress", before.StressMicro, after.StressMicro); record("fat", before.BodyFatMicro, after.BodyFatMicro);
            for (int i = 0; i < before.Regions.Length; i++) {
                string id = before.Regions[i].RegionId;
                record(id + "/S", before.Regions[i].StrengthMicro, after.Regions[i].StrengthMicro);
                record(id + "/F", before.Regions[i].LocalFatigueMicro, after.Regions[i].LocalFatigueMicro);
                record(id + "/T", before.Regions[i].TemporaryFatigueMicro, after.Regions[i].TemporaryFatigueMicro);
            }
            foreach (var exercise in before.Exercises) {
                record(exercise.ExerciseId + "/K", exercise.KnowledgeMicro, after.Exercise(exercise.ExerciseId).KnowledgeMicro);
                record(exercise.ExerciseId + "/X", exercise.ProficiencyMicro, after.Exercise(exercise.ExerciseId).ProficiencyMicro);
            }
        }
        private static string DescribeDay(SessionState state, BodyState body, LifeActorState life)
        {
            var start = state.DayStartBody; var beforeEnd = state.BeforeDayEndBody;
            var lines = new List<string> { "第 " + state.Day + " 日结束 · 摄入 " + life.Nutrition.IntakeKcal + " 千卡",
                "今日已结算工作收入 " + (life.WorkIncomeReceipts ?? Array.Empty<WorkIncomeReceipt>()).Where(r => r.Day == state.Day).Sum(r => r.Amount) + " 元（日结不再发薪）",
                "显示：当前值（全天变化；其中日结）",
                "余额 " + life.Wallet.Balance + "（" + SignedInteger(life.Wallet.Balance - state.DayStartBalance) + "；" + SignedInteger(life.Wallet.Balance - state.BeforeDayEndBalance) + "）" + (state.ChangedFields.Contains("wallet") ? "*" : ""),
                "身体压力 " + DayValue(state, "stress", body.StressMicro, start.StressMicro, beforeEnd.StressMicro),
                "体脂 " + DayValue(state, "fat", body.BodyFatMicro, start.BodyFatMicro, beforeEnd.BodyFatMicro) };
            for (int i = 0; i < body.Regions.Length; i++) {
                var current = body.Regions[i]; var first = start.Region(current.RegionId); var before = beforeEnd.Region(current.RegionId);
                lines.Add(RegionNames[i] + " 力量 " + DayValue(state, current.RegionId + "/S", current.StrengthMicro, first.StrengthMicro, before.StrengthMicro) +
                    " · 局部疲劳 " + DayValue(state, current.RegionId + "/F", current.LocalFatigueMicro, first.LocalFatigueMicro, before.LocalFatigueMicro) +
                    " · 临时疲劳 " + DayValue(state, current.RegionId + "/T", current.TemporaryFatigueMicro, first.TemporaryFatigueMicro, before.TemporaryFatigueMicro));
            }
            foreach (var current in body.Exercises) {
                var first = start.Exercise(current.ExerciseId); var before = beforeEnd.Exercise(current.ExerciseId);
                lines.Add(ExerciseName(current.ExerciseId) + " 知识 " + DayValue(state, current.ExerciseId + "/K", current.KnowledgeMicro, first.KnowledgeMicro, before.KnowledgeMicro) +
                    " · 熟练度 " + DayValue(state, current.ExerciseId + "/X", current.ProficiencyMicro, first.ProficiencyMicro, before.ProficiencyMicro));
            }
            lines.Add("* 表示当天曾变化，即使最后回到日初值。未到期伤病保留。\n最后时段超时 " + state.LastOvertime + " 秒。");
            return string.Join("\n", lines);
        }
        private static string DayValue(SessionState state, string key, long value, long start, long beforeEnd) => Format(value) +
            "（" + Signed(value - start) + "；" + Signed(value - beforeEnd) + "）" + (state.ChangedFields.Contains(key) ? "*" : "");
    }
}
