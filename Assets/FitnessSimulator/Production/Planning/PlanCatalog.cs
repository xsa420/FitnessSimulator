using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Configuration;

namespace FitnessSimulator.Game.Planning
{
    public static class PlanCatalog
    {
        public static string ContentVersion => PlanningContent.Read().Version;
        public const string BenchPressId = "bench_press";
        public static IReadOnlyList<PlanMode> AvailableModes => Array.AsReadOnly(PlanningContent.Read().Defaults.Select(x=>x.Mode).ToArray());
        public static PlanDraft CreateDefault(PlanMode mode) => PlanningContent.Template(PlanningContent.Read().Defaults.Single(x=>x.Mode==mode).TemplateId);
        public static PlanDraft CreateBenchPressSlice() => PlanningContent.Template(PlanningContent.Read().SliceTemplateId);
        public static PlanDraft CreateNpcDefault(NpcPlanPreset preset) => PlanningContent.Template(PlanningContent.Read().NpcDefaults.Single(x=>x.Preset==preset).TemplateId);
        public static PlanDraft CreateFromTemplate(string templateId) => PlanningContent.Template(templateId);
        public static IReadOnlyList<CombinationDefinition> Combinations => Array.AsReadOnly(PlanningContent.Read().Combinations.OrderBy(c=>c.DisplayOrder).ToArray());
        public static PlanSlotDraft PrefillCombination(int index,TrainingCombination combination) => Training(index,combination,PlanningContent.Combination(combination).PrefillExerciseIds);

        public static PlanSlotDraft Rest(int index) => new PlanSlotDraft { Index = index, IsRest = true };

        public static PlanSlotDraft Training(int index, TrainingCombination combination, params string[] exerciseIds)
        {
            if (exerciseIds == null) throw new ArgumentNullException(nameof(exerciseIds));
            return new PlanSlotDraft { Index = index, IsRest = false, AnchorId = PlanningContent.Read().DefaultTrainingAnchorId, Combination = combination,
                Volume = PlanningContent.Read().DefaultVolume,
                Entries = exerciseIds.Select((id, ordinal) => new PlanEntryDraft { EntryId = "slot-" + index + "-entry-" + ordinal, ExerciseId = id }).ToArray() };
        }

        // Capability is separate from plan validity. Never silently remove unintegrated actions.
        public static IReadOnlyList<PlanIssue> GetIntegrationIssues(DayPlan dayPlan, IEnumerable<string> integratedExerciseIds)
        {
            if (dayPlan == null || integratedExerciseIds == null) throw new ArgumentNullException();
            var integrated = new HashSet<string>(integratedExerciseIds, StringComparer.Ordinal);
            return Array.AsReadOnly(dayPlan.Slot.Entries.Where(x => !integrated.Contains(x.ExerciseId))
                .Select(x => new PlanIssue("ExerciseNotIntegrated", x.EntryId,
                    x.ExerciseId + " 的独立小游戏尚未集成；本计划保留该动作，当前不能执行。M1可另选卧推单动作自定义计划。" )).ToArray());
        }
    }

    public sealed class TemplateDefinition
    {
        private readonly PlayerTemplateDefinition definition;
        private readonly TemplateInitializationDefinition initial;
        public string Id => definition.Id;
        public string Name => definition.Name;
        public string Description => definition.Description;
        public string Advantage => definition.Advantage;
        public string Drawback => definition.Drawback;
        public string LockedHint => definition.LockedHint;
        public int DisplayOrder => definition.DisplayOrder;
        public bool Enabled => definition.Enabled;
        public bool IsDefault => definition.Availability.Op == "True";
        public string EligibilityCondition => IsDefault ? null : definition.Availability.Key;
        internal bool IsAvailable(IEnumerable<string> grants) => Enabled && ConditionEvaluator.Evaluate(definition.Availability,
            key=>grants.Contains(key.Substring("templateGrant:".Length),StringComparer.Ordinal)?"true":"false",
            key=>TemplateContent.Read().Templates.Any(t=>key=="templateGrant:"+t.Id));
        // Compatibility display only: initialization must consume the complete definition below.
        public int UniformStrength => checked((int)(initial.Body.Regions[0].StrengthMicro.Value / 1000000));
        public int BodyFatPercent => checked((int)(initial.Body.BodyFatMicro.Value / 1000000));
        public int BaseCharm => checked((int)(initial.Body.BaseCharmMicro.Value / 1000000));
        public int MentalStress => checked((int)(initial.Body.MentalStressMicro.Value / 1000000));
        public int Affluence => checked((int)initial.Affluence.Value);
        public long Balance => initial.Balance.Value;
        public string InventoryInitialRef => initial.InventoryInitialRef;
        public string RelationshipInitialRef => initial.RelationshipInitialRef;
        internal TemplateDefinition(PlayerTemplateDefinition source)
        { definition=source; initial=TemplateContent.Initial(source.InitializationId); }
        public FitnessSimulator.Game.Body.BodyState CreateBody(string actorId) => FitnessSimulator.Game.Body.BodyState.CreateFromDefinition(actorId,initial.Body);
        public FitnessSimulator.Game.Training.TrainingProgress CreateTraining(string actorId) => FitnessSimulator.Game.Training.TrainingProgress.CreateFromDefinition(actorId,initial.Training);
    }
    public sealed class TemplateInitializationIntent
    {
        public string RunId { get; }
        public string ActorId { get; }
        public TemplateDefinition Template { get; }
        public string TemplateVersion => TemplateCatalog.ContentVersion;
        public string InitialInventoryRef => Template.InventoryInitialRef;
        public string RelationshipBaselineRef => Template.RelationshipInitialRef;
        public FitnessSimulator.Game.Body.BodyState CreateBody() => Template.CreateBody(ActorId);
        public FitnessSimulator.Game.Training.TrainingProgress CreateTraining() => Template.CreateTraining(ActorId);
        internal TemplateInitializationIntent(string runId,string actorId,TemplateDefinition template)
        { RunId=runId; ActorId=actorId; Template=template; }
    }
    public static class TemplateCatalog
    {
        public static string ContentVersion => TemplateContent.Read().Version;
        public static string DefaultId => TemplateContent.Read().DefaultId;
        public static string StrengthId => TemplateContent.Read().StrengthId;
        public static string SocialId => TemplateContent.Read().SocialId;
        public static IReadOnlyList<TemplateDefinition> All => Array.AsReadOnly(TemplateContent.Read().Templates.OrderBy(t=>t.DisplayOrder).Select(t=>new TemplateDefinition(t)).ToArray());
        public static void ValidateContent() => TemplateContent.ValidateContent();
        public static bool IsAvailable(string templateId,IEnumerable<string> unlockedTemplateIds)
        {
            if(unlockedTemplateIds==null)throw new ArgumentNullException(nameof(unlockedTemplateIds));
            var definition=All.FirstOrDefault(x=>x.Id==templateId);
            return definition!=null&&definition.IsAvailable(unlockedTemplateIds);
        }
        public static TemplateInitializationIntent PrepareInitialization(string templateId,IEnumerable<string> unlockedTemplateIds,string runId,string actorId)
        {
            if(string.IsNullOrWhiteSpace(runId)||string.IsNullOrWhiteSpace(actorId))throw new ArgumentException("Run/actor identity required.");
            var definition=All.FirstOrDefault(x=>x.Id==templateId);
            if(definition==null)throw new ArgumentException("UnknownTemplate",nameof(templateId));
            if(!IsAvailable(templateId,unlockedTemplateIds))throw new InvalidOperationException("TemplateLocked");
            return new TemplateInitializationIntent(runId,actorId,definition);
        }
    }
}
