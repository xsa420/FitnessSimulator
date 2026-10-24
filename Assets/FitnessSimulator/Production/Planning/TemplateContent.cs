using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Training;
using FitnessSimulator.Game.Configuration;

namespace FitnessSimulator.Game.Planning
{
    [Serializable] public sealed class TemplatesDocument
    {
        public string Version, DefaultId, StrengthId, SocialId;
        public PlayerTemplateDefinition[] Templates;
        public TemplateInitializationDefinition[] Initializations;
    }
    [Serializable] public sealed class PlayerTemplateDefinition
    {
        public string Id, Name, Description, Advantage, Drawback, LockedHint, InitializationId;
        public ConditionDefinition Availability;
        public int DisplayOrder;
        public bool Enabled;
    }
    [Serializable] public sealed class TemplateInitializationDefinition
    {
        public string Id, InventoryInitialRef, RelationshipInitialRef;
        public BodyInitialDefinition Body;
        public TrainingInitialDefinition Training;
        public InitialValue Affluence, Balance;
    }
    public static class TemplateContent
    {
        public static TemplatesDocument Read() => ContentRuntime.Current.Get<TemplatesDocument>("templates");
        public static TemplateInitializationDefinition Initial(string id) => Read().Initializations.Single(x=>x.Id==id);
        public static void ValidateReferences(Func<string,bool> inventoryExists,Func<string,bool> relationshipExists)
        {
            if(inventoryExists==null||relationshipExists==null)throw new ArgumentNullException();
            foreach(var i in Read().Initializations)Require(inventoryExists(i.InventoryInitialRef)&&relationshipExists(i.RelationshipInitialRef),"Initializations["+i.Id+"].LifeReferences");
        }
        public static void ValidateContent()
        {
            var d=Read();
            Require(!string.IsNullOrWhiteSpace(d.Version),"Version");
            Require(d.Templates!=null&&d.Templates.Length==3&&d.Templates.All(t=>t!=null)&&d.Templates.Select(t=>t.Id).Distinct().Count()==3,"Templates");
            Require(d.Initializations!=null&&d.Initializations.All(x=>x!=null)&&d.Initializations.Select(x=>x.Id).Distinct().Count()==d.Initializations.Length,"Initializations");
            Require(d.Templates.Any(t=>t.Id==d.DefaultId&&t.Enabled&&t.Availability!=null&&t.Availability.Op=="True"),"DefaultId");
            Require(d.Templates.Any(t=>t.Id==d.StrengthId)&&d.Templates.Any(t=>t.Id==d.SocialId)&&new[]{d.DefaultId,d.StrengthId,d.SocialId}.Distinct().Count()==3,"TemplateIds");
            foreach(var t in d.Templates) {
                string path="Templates["+t.Id+"]";
                Require(new[]{t.Id,t.Name,t.Description,t.Advantage,t.Drawback,t.LockedHint,t.InitializationId}.All(x=>!string.IsNullOrWhiteSpace(x)),path);
                Require(d.Initializations.Count(x=>x.Id==t.InitializationId)==1,path+".InitializationId");
                ConditionEvaluator.Validate(t.Availability,key=>d.Templates.Any(x=>key=="templateGrant:"+x.Id),"templates."+path+".Availability");
            }
            foreach(var i in d.Initializations) {
                string path="Initializations["+i.Id+"]";
                Require(!string.IsNullOrWhiteSpace(i.Id)&&i.Affluence!=null&&i.Balance!=null&&i.Affluence.Value>=0&&i.Affluence.Value<=100&&i.Balance.Value>=0,path+".Wallet");
                Require(i.InventoryInitialRef=="LC.empty-notebook0-bookmark0.v1"&&i.RelationshipInitialRef=="LC.initial-unmet-zero-no-partner.v1",path+".LifeReferences");
                try {
                    var body=BodyState.CreateFromDefinition("validation-player",i.Body);
                    var training=TrainingProgress.CreateFromDefinition("validation-player",i.Training);
                    Require(body.StressMicro==0&&body.Regions.All(r=>r.LocalFatigueMicro==0&&r.TemporaryFatigueMicro==0&&r.InjuredDay==0&&r.ExpiresAtStartOfDay==0)&&body.Exercises.All(e=>e.KnowledgeMicro==0&&e.ProficiencyMicro==0),path+".InitialAccrual");
                    Require(training.Exercises.All(e=>e.HighestUnlockedTier==1&&e.AutomaticTiers.All(v=>!v)),path+".TrainingInitialRights");
                    foreach(var exercise in ExerciseCatalog.All)Require(exercise.TargetRegionIds.Sum(id=>body.Region(id).StrengthMicro)>=exercise.Threshold(1)*1000000L,path+".FirstTier["+exercise.Id+"]");
                } catch(Exception e) { throw new ArgumentException("templates."+path+": "+e.Message,e); }
            }
        }
        private static void Require(bool ok,string path) { if(!ok)throw new ArgumentException("templates."+path); }
    }
}
