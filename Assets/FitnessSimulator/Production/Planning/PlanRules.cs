using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Planning
{
    public static class PlanRules
    {
        public static IReadOnlyList<PlanIssue> Validate(PlanDraft draft, int effectiveDay, AnchorEligibility anchors, IExercisePlanCatalog catalog)
        {
            if (anchors == null || catalog == null) throw new ArgumentNullException(anchors == null ? nameof(anchors) : nameof(catalog));
            var errors = new List<PlanIssue>();
            if (draft == null) { errors.Add(new PlanIssue("MissingPlan", "plan", "必须选择训练计划。")); return errors.AsReadOnly(); }
            if (effectiveDay != anchors.CurrentDay && effectiveDay != anchors.CurrentDay + 1)
                errors.Add(new PlanIssue("InvalidEffectiveDay", "effectiveDay", "生效日必须是当天或次日。"));
            if (!Enum.IsDefined(typeof(PlanMode), draft.Mode)) errors.Add(new PlanIssue("InvalidMode", "mode", "计划模式无效。"));
            if (string.IsNullOrWhiteSpace(catalog.Version)) errors.Add(new PlanIssue("MissingCatalogVersion", "catalog", "缺少动作目录版本。"));
            if (draft.Slots == null) { errors.Add(new PlanIssue("MissingSlots", "slots", "缺少每日安排。")); return errors.AsReadOnly(); }
            int count = draft.Slots.Length;
            if ((draft.Mode == PlanMode.NaturalWeek && count != 7) || (draft.Mode == PlanMode.DayCycle && (count < 1 || count > 14)))
                errors.Add(new PlanIssue("InvalidLength", "slots", "自然周须7项，循环须1至14项。"));
            var indices = new HashSet<int>();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                var s = draft.Slots[i]; string path = "slots[" + i + "]";
                if (s == null) { errors.Add(new PlanIssue("MissingSlot", path, "安排不可为空。")); continue; }
                if (s.Index < 0 || s.Index >= count || !indices.Add(s.Index)) errors.Add(new PlanIssue("InvalidSlotIndex", path + ".index", "日期键重复或越界。"));
                if (!Enum.IsDefined(typeof(TrainingCombination), s.Combination) || !Enum.IsDefined(typeof(TrainingVolume), s.Volume))
                    errors.Add(new PlanIssue("InvalidTrainingChoice", path, "组合或训练量档位无效。"));
                if (s.Entries == null) { errors.Add(new PlanIssue("MissingEntries", path + ".entries", "动作清单不可缺失。")); continue; }
                if (s.IsRest)
                {
                    if (s.Entries.Length != 0 || !string.IsNullOrEmpty(s.AnchorId)) errors.Add(new PlanIssue("RestHasTraining", path, "休息日不能包含训练锚点或动作。"));
                    continue;
                }
                if (!anchors.Allows(s.AnchorId, effectiveDay)) errors.Add(new PlanIssue("AnchorUnavailable", path + ".anchorId", "该锚点在生效日尚未解锁。"));
                if (s.Entries.Length < 1 || s.Entries.Length > 8) errors.Add(new PlanIssue("InvalidEntryCount", path + ".entries", "训练日须安排1至8个不同动作；清空请改为休息日。"));
                var actions = new HashSet<string>(StringComparer.Ordinal);
                for (int j = 0; j < s.Entries.Length; j++)
                {
                    var e = s.Entries[j]; string ep = path + ".entries[" + j + "]";
                    if (e == null) { errors.Add(new PlanIssue("MissingEntry", ep, "动作条目不可为空。")); continue; }
                    if (string.IsNullOrWhiteSpace(e.EntryId) || !entryIds.Add(e.EntryId)) errors.Add(new PlanIssue("InvalidEntryId", ep + ".entryId", "条目标识须在本计划唯一。"));
                    if (string.IsNullOrWhiteSpace(e.ExerciseId) || !actions.Add(e.ExerciseId)) errors.Add(new PlanIssue("DuplicateExercise", ep + ".exerciseId", "同次训练动作ID不可为空或重复。"));
                    if (string.IsNullOrWhiteSpace(e.ExerciseId) || !catalog.TryGet(e.ExerciseId, out var def) || def == null || def.Id != e.ExerciseId)
                    { errors.Add(new PlanIssue("UnknownExercise", ep + ".exerciseId", "动作不存在于正式目录。")); continue; }
                    if (s.Combination != TrainingCombination.Free && Enum.IsDefined(typeof(TrainingCombination), s.Combination))
                    {
                        var regions = catalog.GetCombinationRegions(s.Combination);
                        if (regions == null || regions.Count == 0) errors.Add(new PlanIssue("MissingCombinationRegions", path, "组合缺少区域映射。"));
                        else if (!def.TargetRegionIds.Intersect(regions, StringComparer.Ordinal).Any())
                            errors.Add(new PlanIssue("NoTargetIntersection", ep, "动作目标区域与组合无交集。"));
                    }
                }
            }
            return errors.AsReadOnly();
        }

        public static PlanState SaveInitial(PlanState state, PlanDraft draft, int currentDay, long expectedRevision, AnchorEligibility anchors, IExercisePlanCatalog catalog)
        {
            CheckState(state, currentDay, expectedRevision, anchors);
            if (state.Active != null) throw new InvalidOperationException("InitialPlanAlreadyExists");
            var errors = Validate(draft, currentDay, anchors, catalog); if (errors.Count != 0) throw new PlanValidationException(errors);
            long next = checked(state.Revision + 1);
            return new PlanState(state.RunId, state.ActorId, state.PlanId, next, currentDay,
                new PlanRevision(state.PlanId, next, currentDay, draft, catalog), null);
        }

        public static PlanState SavePending(PlanState state, PlanDraft draft, int currentDay, long expectedRevision, AnchorEligibility anchors, IExercisePlanCatalog catalog)
        {
            CheckState(state, currentDay, expectedRevision, anchors);
            if (state.Active == null || state.AppliedDay != currentDay) throw new InvalidOperationException("ApplyCurrentDayBeforeEditing");
            int effectiveDay = checked(currentDay + 1);
            var errors = Validate(draft, effectiveDay, anchors, catalog); if (errors.Count != 0) throw new PlanValidationException(errors);
            long next = checked(state.Revision + 1);
            return new PlanState(state.RunId, state.ActorId, state.PlanId, next, state.AppliedDay, state.Active,
                new PlanRevision(state.PlanId, next, effectiveDay, draft, catalog));
        }

        public static PlanState ApplyDay(PlanState state, int day)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Active == null) throw new InvalidOperationException("MissingInitialPlan");
            if (day == state.AppliedDay) return state;
            if (day != state.AppliedDay + 1) throw new InvalidOperationException("DayMustAdvanceSequentially");
            bool apply = state.Pending != null && state.Pending.EffectiveDay == day;
            if (state.Pending != null && state.Pending.EffectiveDay < day) throw new InvalidOperationException("MissedPendingPlanBoundary");
            return new PlanState(state.RunId, state.ActorId, state.PlanId, checked(state.Revision + 1), day,
                apply ? state.Pending : state.Active, apply ? null : state.Pending);
        }

        public static DayPlan ResolveDay(PlanState state, int day, DayOfWeek weekday)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Active == null || day != state.AppliedDay || day < state.Active.EffectiveDay) throw new InvalidOperationException("DayNotApplied");
            if (!Enum.IsDefined(typeof(DayOfWeek), weekday)) throw new ArgumentOutOfRangeException(nameof(weekday));
            int index = state.Active.Mode == PlanMode.NaturalWeek ? ((int)weekday + 6) % 7 : (day - state.Active.EffectiveDay) % state.Active.Slots.Count;
            return new DayPlan(state, day, state.Active.Slots[index]);
        }

        private static void CheckState(PlanState state, int day, long revision, AnchorEligibility anchors)
        {
            if (state == null || anchors == null) throw new ArgumentNullException(state == null ? nameof(state) : nameof(anchors));
            if (day < 1 || anchors.CurrentDay != day) throw new ArgumentException("AnchorDayMismatch");
            if (state.Revision != revision) throw new InvalidOperationException("StalePlanRevision");
        }
    }
}
