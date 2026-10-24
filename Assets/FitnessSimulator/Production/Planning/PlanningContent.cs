using System;
using System.Linq;
using System.Collections.Generic;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Planning
{
    [Serializable] public sealed class PlansDocument
    {
        public string Version, DefaultTrainingAnchorId, SliceTemplateId;
        public TrainingVolume DefaultVolume;
        public PlanTemplateDefinition[] Templates;
        public CombinationDefinition[] Combinations;
        public DefaultPlanReference[] Defaults;
        public NpcPlanReference[] NpcDefaults;
    }
    [Serializable] public sealed class PlanTemplateDefinition
    {
        public string Id, Name, Description;
        public int DisplayOrder;
        public string[] SlotIds;
        public PlanDraft Draft;
    }
    [Serializable] public sealed class CombinationDefinition
    {
        public TrainingCombination Kind;
        public string Id, Name, EditableRule;
        public int DisplayOrder;
        public string[] RegionIds, PrefillExerciseIds;
    }
    [Serializable] public sealed class DefaultPlanReference { public PlanMode Mode; public string TemplateId; }
    [Serializable] public sealed class NpcPlanReference { public NpcPlanPreset Preset; public string TemplateId; }

    public static class PlanningContent
    {
        public static PlansDocument Read() => ContentRuntime.Current.Get<PlansDocument>("plans");
        public static PlanDraft Template(string id) => Read().Templates.Single(t => t.Id == id).Draft;
        public static CombinationDefinition Combination(TrainingCombination kind) => Read().Combinations.Single(c => c.Kind == kind);
        public static void ValidateContent(IEnumerable<string> knownTrainingAnchorIds)
        {
            var d=Read(); var anchors=ActorDayPlans.KnownAnchors(knownTrainingAnchorIds);
            Require(!string.IsNullOrWhiteSpace(d.Version),"Version");
            Require(anchors.Contains(d.DefaultTrainingAnchorId),"DefaultTrainingAnchorId");
            Require(Enum.IsDefined(typeof(TrainingVolume),d.DefaultVolume),"DefaultVolume");
            Require(d.Combinations!=null&&d.Combinations.Length==5&&d.Combinations.All(c=>c!=null)&&d.Combinations.Select(c=>c.Kind).Distinct().Count()==5,"Combinations");
            Require(d.Combinations.Select(c=>c.Id).Distinct().Count()==5,"Combinations.Id");
            foreach(var c in d.Combinations) {
                string path="Combinations["+c.Id+"]";
                Require(Enum.IsDefined(typeof(TrainingCombination),c.Kind)&&!string.IsNullOrWhiteSpace(c.Id)&&!string.IsNullOrWhiteSpace(c.Name),path);
                Require(c.EditableRule=="TargetIntersection",path+".EditableRule");
                Require(c.RegionIds!=null&&c.RegionIds.Length>0&&c.RegionIds.Distinct().Count()==c.RegionIds.Length&&c.RegionIds.All(RegionIds.All.Contains),path+".RegionIds");
                if(c.Kind==TrainingCombination.Free)Require(c.RegionIds.OrderBy(x=>x).SequenceEqual(RegionIds.All.OrderBy(x=>x)),path+".RegionIds");
                Require(c.PrefillExerciseIds!=null&&c.PrefillExerciseIds.Length>0&&c.PrefillExerciseIds.Distinct().Count()==c.PrefillExerciseIds.Length,path+".PrefillExerciseIds");
                foreach(string id in c.PrefillExerciseIds)Require(ExerciseCatalog.All.Any(e=>e.Id==id&&e.TargetRegionIds.Intersect(c.RegionIds).Any()),path+".PrefillExerciseIds["+id+"]");
            }
            Require(d.Templates!=null&&d.Templates.Length>0&&d.Templates.All(t=>t!=null)&&d.Templates.Select(t=>t.Id).Distinct().Count()==d.Templates.Length,"Templates");
            foreach(var t in d.Templates) {
                string path="Templates["+t.Id+"]";
                Require(!string.IsNullOrWhiteSpace(t.Id)&&!string.IsNullOrWhiteSpace(t.Name)&&t.Draft!=null,path);
                Require(t.SlotIds!=null&&t.Draft.Slots!=null&&t.SlotIds.Length==t.Draft.Slots.Length&&t.SlotIds.All(x=>!string.IsNullOrWhiteSpace(x))&&t.SlotIds.Distinct().Count()==t.SlotIds.Length,path+".SlotIds");
                var issues=PlanRules.Validate(t.Draft,1,new AnchorEligibility(1,anchors,Array.Empty<string>()),new TrainingCatalogAdapter());
                Require(issues.Count==0,path+": "+string.Join("; ",issues));
            }
            Require(d.Defaults!=null&&d.Defaults.Length==2&&d.Defaults.All(x=>x!=null)&&d.Defaults.Select(x=>x.Mode).Distinct().Count()==2,"Defaults");
            foreach(var r in d.Defaults)Require(d.Templates.Any(t=>t.Id==r.TemplateId&&t.Draft.Mode==r.Mode),"Defaults["+r.Mode+"]");
            Require(d.Templates.Any(t=>t.Id==d.SliceTemplateId),"SliceTemplateId");
            Require(d.NpcDefaults!=null&&d.NpcDefaults.Length==4&&d.NpcDefaults.All(x=>x!=null)&&d.NpcDefaults.Select(x=>x.Preset).Distinct().Count()==4,"NpcDefaults");
            foreach(var r in d.NpcDefaults)Require(Enum.IsDefined(typeof(NpcPlanPreset),r.Preset)&&d.Templates.Any(t=>t.Id==r.TemplateId&&t.Draft.Mode==PlanMode.NaturalWeek),"NpcDefaults["+r.Preset+"]");
        }
        private static void Require(bool condition,string path) { if(!condition)throw new ArgumentException("plans."+path); }
    }
}
