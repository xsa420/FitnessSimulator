using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Planning
{
    public enum NpcPlanPreset { Rival, Beginner, Veteran, Coach }

    // A daily value, never a replacement for the actor's persistent base PlanState.
    public sealed class ActorDayArrangement
    {
        public string RunId { get; }
        public string ActorId { get; }
        public int Day { get; }
        public string BasePlanId { get; }
        public long BaseRevision { get; }
        public bool HasCompanionSource { get; }
        public string BindingId { get; }
        public string SourceActorId { get; }
        public string SourcePlanId { get; }
        public long SourceRevision { get; }
        public int SourceEffectiveDay { get; }
        public string ExerciseCatalogVersion { get; }
        public PlanSlot Slot { get; }
        internal ActorDayArrangement(string runId, string actorId, int day, string basePlanId, long baseRevision,
            bool hasCompanion, string bindingId, string sourceActorId, string sourcePlanId, long sourceRevision,
            int sourceEffectiveDay, string catalogVersion, PlanSlot slot)
        {
            RunId = runId; ActorId = actorId; Day = day; BasePlanId = basePlanId; BaseRevision = baseRevision;
            HasCompanionSource = hasCompanion; BindingId = bindingId; SourceActorId = sourceActorId;
            SourcePlanId = sourcePlanId; SourceRevision = sourceRevision; SourceEffectiveDay = sourceEffectiveDay;
            ExerciseCatalogVersion = catalogVersion;
            Slot = new PlanSlot(slot.Index, slot.IsRest, slot.AnchorId, slot.Combination, slot.Volume, slot.Entries);
        }
    }

    public static class ActorDayPlans
    {
        public static string PresetVersion => PlanningContent.Read().Version;

        public static ActorDayArrangement ResolveBase(PlanState basePlan, int day, DayOfWeek weekday,
            AnchorEligibility eligibility, IEnumerable<string> knownTrainingAnchorIds)
        {
            var source = PlanRules.ResolveDay(basePlan, day, weekday);
            ValidateAnchor(source.Slot, day, eligibility, knownTrainingAnchorIds);
            return Create(basePlan, source, false, "");
        }

        // Caller supplies only the life domain's currently effective, authorized binding.
        // This service does not infer a relationship or grant target anchor unlocks.
        public static ActorDayArrangement FollowPlayer(PlanState npcBasePlan, DayPlan playerDay, string bindingId,
            AnchorEligibility targetEligibility, IEnumerable<string> knownTrainingAnchorIds)
        {
            if (npcBasePlan == null || playerDay == null) throw new ArgumentNullException();
            if (npcBasePlan.Active == null || npcBasePlan.RunId != playerDay.RunId || npcBasePlan.AppliedDay != playerDay.Day ||
                npcBasePlan.ActorId == playerDay.ActorId || string.IsNullOrWhiteSpace(bindingId))
                throw new ArgumentException("InvalidCompanionPlanContext");
            if (npcBasePlan.Active.ExerciseCatalogVersion != playerDay.ExerciseCatalogVersion)
                throw new InvalidOperationException("CompanionCatalogVersionMismatch");
            ValidateAnchor(playerDay.Slot, playerDay.Day, targetEligibility, knownTrainingAnchorIds);
            return Create(npcBasePlan, playerDay, true, bindingId);
        }

        private static ActorDayArrangement Create(PlanState target, DayPlan source, bool companion, string bindingId) =>
            new ActorDayArrangement(target.RunId, target.ActorId, source.Day, target.PlanId, target.Active.Revision,
                companion, bindingId, source.ActorId, source.PlanId, source.PlanRevision, source.EffectiveDay,
                source.ExerciseCatalogVersion, source.Slot);

        internal static string[] KnownAnchors(IEnumerable<string> knownTrainingAnchorIds)
        {
            var ids = knownTrainingAnchorIds?.ToArray() ?? throw new ArgumentNullException(nameof(knownTrainingAnchorIds));
            if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new ArgumentException("InvalidTrainingAnchorCatalog");
            return ids;
        }

        private static void ValidateAnchor(PlanSlot slot, int day, AnchorEligibility eligibility, IEnumerable<string> knownTrainingAnchorIds)
        {
            var known = KnownAnchors(knownTrainingAnchorIds);
            if (eligibility == null || eligibility.CurrentDay != day) throw new ArgumentException("TargetAnchorEligibilityRequired");
            if (!slot.IsRest && (!known.Contains(slot.AnchorId, StringComparer.Ordinal) || !eligibility.Allows(slot.AnchorId, day)))
                throw new InvalidOperationException("TargetTrainingAnchorUnavailable");
        }
    }
}
