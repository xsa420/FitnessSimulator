using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;
namespace FitnessSimulator.Game.Actors
{
    [Serializable] public sealed class ActorContentDocument { public string Version; public ActorDefinition[] Characters; public ActorInitialDefinition[] InitialStates; public ActorPolicyDefinition[] Policies; public ActorInputDefinition[] InputPolicies; public ActorEmptyStateDefinition EmptyState; }
    [Serializable] public sealed class ActorDefinition
    {
        public string Id, Name, Sex, Role, Type, InitialStateRef, BasePlanRef, PolicyRef, InventoryRef, RelationshipRef;
        public string[] InputPolicyRefs;
    }
    [Serializable] public sealed class ActorInitialDefinition
    {
        public string Id;
        public RegionState[] Regions;
        public ExerciseBodyState[] Exercises;
        public ExerciseTrainingProgress[] Training;
        public long StressMicro, MentalStressMicro, BodyFatMicro, BaseCharmMicro, Balance;
        public int Affluence;
    }
    [Serializable] public sealed class ActorEmptyStateDefinition
    {
        public long NutritionIntakeKcal;
        public MealReceipt[] Meals;
        public WorkIncomeReceipt[] WorkIncomeReceipts;
        public MoneyDeltaReceipt[] MoneyEffectReceipts;
        public bool HasLastIncome;
        public IncomeReceipt LastIncome;
        public string[] RememberedMeals;
    }
    [Serializable] public sealed class ActorPolicyDefinition
    {
        public string Id;
        public long StopStressMicro, RestFatigueMicro, ChallengeFatigueMicro, ChallengeProficiencyMicro, StopBuyingAtKnowledgeMicro, PurchaseReserve;
        public int NormalRestSeconds, ExtraRestSeconds, MaximumExtraRests, StudyNumerator, StudyDenominator, WantedNotebookUnits;
        public ActorMealPolicy[] Meals;
    }
    [Serializable] public sealed class ActorMealPolicy { public MealSlot Slot; public string BasicOptionId, UpgradeOptionId, SkipOptionId; public long Reserve, UpgradeBalance; }
    [Serializable] public sealed class ActorInputDefinition
    {
        public string Id, ExerciseId, AdapterId;
        public double ErrorAmplitude, MistakeProbability, PositionError, FailurePositionOffset;
        public int TickMilliseconds, ControlIntervalMilliseconds, ReactionMilliseconds, TimingErrorMilliseconds, FailureTimingMilliseconds, MaximumTicksPerAttempt;
    }
    public static class ActorContent
    {
        public static ActorContentDocument Definition=>ContentRuntime.Current.Get<ActorContentDocument>("actors");
        public static ActorDefinition Get(string id)=>Definition.Characters.SingleOrDefault(x=>x.Id==id)??throw new ArgumentException("actors.Characters: unknown "+id);
        public static ActorInitialDefinition Initial(string id)=>Definition.InitialStates.SingleOrDefault(x=>x.Id==id)??throw new ArgumentException("actors.InitialStates: unknown "+id);
        public static ActorPolicyDefinition Policy(string id)=>Definition.Policies.SingleOrDefault(x=>x.Id==Get(id).PolicyRef)??throw new ArgumentException("actors.Policies: missing reference "+id);
        public static ActorInputDefinition Input(string id,string exercise)
        {
            var refs=Get(id).InputPolicyRefs;
            return Definition.InputPolicies.SingleOrDefault(x=>refs.Contains(x.Id)&&x.ExerciseId==exercise)??throw new ArgumentException("actors.InputPolicies: missing "+id+"/"+exercise);
        }
        public static void ValidateContent()
        {
            var d=Definition;var ids=RelationshipRules.CharacterIds;
            if(string.IsNullOrWhiteSpace(d.Version)||d.Characters==null||d.Characters.Length!=4||d.Characters.Any(x=>x==null||!ids.Contains(x.Id))||d.Characters.Select(x=>x.Id).Distinct().Count()!=4||d.Characters.Select(x=>x.Type).Distinct().Count()!=4||d.InitialStates==null||d.Policies==null||d.InputPolicies==null)throw new ArgumentException("actors: require four independent types and catalogs");
            if(d.InitialStates.Any(x=>x==null)||d.Policies.Any(x=>x==null)||d.InputPolicies.Any(x=>x==null)||d.InitialStates.Select(x=>x.Id).Distinct().Count()!=d.InitialStates.Length||d.Policies.Select(x=>x.Id).Distinct().Count()!=d.Policies.Length||d.InputPolicies.Select(x=>x.Id).Distinct().Count()!=d.InputPolicies.Length)throw new ArgumentException("actors: duplicate/null definitions");
            var empty=d.EmptyState;
            if(empty==null||empty.NutritionIntakeKcal!=0||empty.Meals==null||empty.Meals.Length!=0||empty.WorkIncomeReceipts==null||empty.WorkIncomeReceipts.Length!=0||empty.MoneyEffectReceipts==null||empty.MoneyEffectReceipts.Length!=0||empty.HasLastIncome||empty.LastIncome==null||empty.LastIncome.Day!=0||empty.LastIncome.Amount!=0||empty.LastIncome.AffluenceAtExecution!=0||empty.LastIncome.ConfigVersion!=""||empty.LastIncome.SettlementKey!=""||empty.RememberedMeals==null||empty.RememberedMeals.Length!=3||empty.RememberedMeals.Any(x=>x!=""))throw new ArgumentException("actors.EmptyState: explicit empty runtime baseline required");
            foreach(var c in d.Characters)
            {
                if(c.Type!=c.Id||string.IsNullOrWhiteSpace(c.Name)||string.IsNullOrWhiteSpace(c.Role)||(c.Sex!="male"&&c.Sex!="female")||c.InventoryRef!=EconomyContent.Definition.InitialInventory.Id||c.RelationshipRef!=RelationshipContent.Definition.InitialState.Id||c.InputPolicyRefs==null||c.InputPolicyRefs.Length!=8||c.InputPolicyRefs.Distinct().Count()!=8)throw new ArgumentException("actors.Characters."+c.Id+": invalid metadata/reference");
                PlanCatalog.CreateFromTemplate(c.BasePlanRef);
                var initial=Initial(c.InitialStateRef);
                if(initial.Regions==null||initial.Regions.Length!=21||initial.Regions.Any(r=>r==null)||initial.Regions.Select(r=>r.RegionId).Distinct().Count()!=21||initial.Exercises==null||initial.Exercises.Length!=8||initial.Exercises.Any(e=>e==null)||initial.Exercises.Select(e=>e.ExerciseId).Distinct().Count()!=8||initial.Training==null||initial.Training.Length!=8||initial.Training.Any(t=>t==null)||initial.Training.Select(t=>t.ExerciseId).Distinct().Count()!=8)throw new ArgumentException("actors.InitialStates."+initial.Id+": incomplete 21 regions/8 exercises/training");
                foreach(var r in initial.Regions)if(r.LocalFatigueMicro!=0||r.TemporaryFatigueMicro!=0||r.InjuredDay!=0||r.ExpiresAtStartOfDay!=0)throw new ArgumentException("actors.InitialStates."+initial.Id+": baseline fatigue/injury must be explicit zero");
                if(initial.StressMicro!=0||initial.Training.Any(t=>t.HighestUnlockedTier!=1||t.AutomaticTiers==null||t.AutomaticTiers.Length!=4||t.AutomaticTiers.Any(x=>x)))throw new ArgumentException("actors.InitialStates."+initial.Id+": no initial automatic/training privileges");
                ActorPolicies.Initialize("content-validation",c.Id);
                var p=Policy(c.Id);
                if(!TrainingContent.Current.Fatigue.RestSeconds.Contains(p.NormalRestSeconds))throw new ArgumentException("actors.Policies."+p.Id+".NormalRestSeconds: must reference training.Fatigue.RestSeconds");
                if(!TrainingContent.Current.Fatigue.RestSeconds.Contains(p.ExtraRestSeconds))throw new ArgumentException("actors.Policies."+p.Id+".ExtraRestSeconds: must reference training.Fatigue.RestSeconds");
                if(string.IsNullOrWhiteSpace(p.Id)||p.StopStressMicro<=0||p.StopStressMicro>100000000||p.RestFatigueMicro<0||p.RestFatigueMicro>=150000000||p.ChallengeFatigueMicro<0||p.ChallengeFatigueMicro>=150000000||p.ChallengeProficiencyMicro<0||p.ChallengeProficiencyMicro>1000000000||p.MaximumExtraRests<0||p.MaximumExtraRests>10||p.StudyNumerator<0||p.StudyDenominator<=0||p.StudyNumerator>p.StudyDenominator||p.WantedNotebookUnits<1||p.PurchaseReserve<0||p.StopBuyingAtKnowledgeMicro<0||p.StopBuyingAtKnowledgeMicro>100000000||p.Meals==null||p.Meals.Length!=3||p.Meals.Any(m=>m==null)||p.Meals.Select(m=>m.Slot).Distinct().Count()!=3)throw new ArgumentException("actors.Policies."+p.Id+": invalid finite policy");
                foreach(var m in p.Meals){LifeContent.Find(m.Slot,m.BasicOptionId);LifeContent.Find(m.Slot,m.UpgradeOptionId);LifeContent.Find(m.Slot,m.SkipOptionId);if(m.Reserve<0||m.UpgradeBalance<0)throw new ArgumentException("actors.Policies."+p.Id+": invalid meal reserve");}
                foreach(var exercise in ExerciseCatalog.All)
                {
                    var input=Input(c.Id,exercise.Id);
                    if(input.AdapterId!=exercise.Id||double.IsNaN(input.ErrorAmplitude)||double.IsInfinity(input.ErrorAmplitude)||input.ErrorAmplitude<0||input.ErrorAmplitude>2||double.IsNaN(input.MistakeProbability)||input.MistakeProbability<0||input.MistakeProbability>1||input.TickMilliseconds<1||input.TickMilliseconds>100||input.ControlIntervalMilliseconds<input.TickMilliseconds||input.ControlIntervalMilliseconds%input.TickMilliseconds!=0||input.ReactionMilliseconds<0||input.TimingErrorMilliseconds<0||input.FailureTimingMilliseconds<0||input.MaximumTicksPerAttempt<1||input.MaximumTicksPerAttempt>10000||double.IsNaN(input.PositionError)||double.IsNaN(input.FailurePositionOffset)||input.PositionError<0||input.PositionError>1||input.FailurePositionOffset<0||input.FailurePositionOffset>1)throw new ArgumentException("actors.InputPolicies."+input.Id+": invalid adapter/timing/error");
                }
            }
        }
    }
}