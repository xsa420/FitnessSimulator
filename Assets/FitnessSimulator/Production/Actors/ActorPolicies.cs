using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Actors
{
    public sealed class ActorInitialState
    {
        public string CharacterId;
        public BodyState Body;
        public TrainingProgress Training;
        public LifeActorState Life;
        public InventoryState Inventory;
        public ShopState[] Shops;
        public PlanDraft Plan;
        public RelationshipState Relationships;
        public string[] RememberedMeals;
    }
    public enum ActorEventKind { Ordinary, Training, Rest, EveningStudy, AllDayStudy, Shop }
    public sealed class ActorEventOption { public string EventId; public ActorEventKind Kind; }
    public sealed class ActorEventDecision { public string EventId, Reason, PolicyVersion; public int RandomSamplesConsumed; }
    public enum ActorSetDecision { EndWorkout, SkipInjuredExercise, LongRest, Challenge, AutoCapacity, ManualCapacity }
    public static class ActorPolicies
    {
        public static string Version=>ActorContent.Definition.Version;
        public static int Index(string id)
        {
            switch(ActorContent.Get(id).Type){case "rival":return 0;case "beginner":return 1;case "veteran":return 2;case "coach":return 3;default:throw new ArgumentException("Unknown character type");}
        }
        public static ActorInitialState Initialize(string runId,string characterId)
        {
            var definition=ActorContent.Get(characterId);var initial=ActorContent.Initial(definition.InitialStateRef);var empty=ActorContent.Definition.EmptyState;
            var body=new BodyState { ActorId=characterId,Regions=initial.Regions,Exercises=initial.Exercises,StressMicro=initial.StressMicro,MentalStressMicro=initial.MentalStressMicro,BodyFatMicro=initial.BodyFatMicro,BaseCharmMicro=initial.BaseCharmMicro };
            body.Validate();var training=new TrainingProgress { ActorId=characterId,Exercises=initial.Training };training.Validate();
            var life=LifeRules.Initialize(runId,characterId,initial.Balance,initial.Affluence);
            life.Nutrition.IntakeKcal=empty.NutritionIntakeKcal;life.Nutrition.Meals=empty.Meals;life.WorkIncomeReceipts=empty.WorkIncomeReceipts;life.MoneyEffectReceipts=empty.MoneyEffectReceipts;life.HasLastIncome=empty.HasLastIncome;life.LastIncome=empty.LastIncome;
            return new ActorInitialState { CharacterId=characterId,Body=body,Training=training,Life=LifeRules.NormalizeAfterLoad(life),Inventory=InventoryRules.Initialize(runId,characterId,definition.InventoryRef),Shops=EconomyContent.AllShopIds.Select(id=>ShopRules.Initialize(runId,characterId,id)).ToArray(),Plan=PlanCatalog.CreateFromTemplate(definition.BasePlanRef),Relationships=RelationshipRules.Initialize(runId,characterId,definition.RelationshipRef),RememberedMeals=empty.RememberedMeals };
        }
        public static ActorInitialState[] InitializeFour(string runId)=>ActorContent.Definition.Characters.Select(c=>Initialize(runId,c.Id)).ToArray();
        // Candidates are the event system's already drawn legal options for this anchor.
        // No fallback event is synthesized here and no world clock is advanced.
        public static ActorEventDecision ChooseEvent(string characterId,ActorEventOption[] legal,long stressMicro,bool restDay,uint sample)
        {
            var policy=ActorContent.Policy(characterId);
            if(legal==null||legal.Length==0||legal.Any(e=>e==null||string.IsNullOrWhiteSpace(e.EventId)||!Enum.IsDefined(typeof(ActorEventKind),e.Kind))||legal.Select(e=>e.EventId).Distinct().Count()!=legal.Length||stressMicro<0||stressMicro>100000000)throw new ArgumentException("Missing or invalid legal options");
            ActorEventOption chosen=null;string reason="StableFirstLegal";int consumed=0;
            if(stressMicro>=policy.StopStressMicro) { chosen=legal.FirstOrDefault(e=>e.Kind==ActorEventKind.Rest);if(chosen!=null)reason="HighStressRest"; }
            if(chosen==null){chosen=legal.FirstOrDefault(e=>e.Kind==ActorEventKind.Training);if(chosen!=null)reason="LegalTraining";}
            if(chosen==null&&!restDay){chosen=legal.FirstOrDefault(e=>e.Kind==ActorEventKind.EveningStudy);if(chosen!=null)reason="EveningStudy";}
            if(chosen==null&&restDay)
            {
                var study=legal.FirstOrDefault(e=>e.Kind==ActorEventKind.AllDayStudy);
                var ordinary=legal.FirstOrDefault(e=>e.Kind==ActorEventKind.Ordinary||e.Kind==ActorEventKind.Rest);
                if(study!=null&&ordinary!=null)
                {
                    int numerator=policy.StudyNumerator,denominator=policy.StudyDenominator;
                    chosen=(ulong)sample*(ulong)denominator<4294967296UL*(ulong)numerator?study:ordinary;consumed=1;reason="RestDayWeightedChoice";
                }
            }
            if(chosen==null)chosen=legal[0];
            return new ActorEventDecision { EventId=chosen.EventId,Reason=reason,RandomSamplesConsumed=consumed,PolicyVersion=Version };
        }
        public static ActorSetDecision ChooseSet(string characterId,long stressMicro,long highestTargetFatigueMicro,long proficiencyMicro,bool injured,bool challengeEligible,bool autoEligible,bool firstExerciseSet,bool challengeFailed,int extraRests)
        {
            var policy=ActorContent.Policy(characterId);
            if(stressMicro<0||stressMicro>100000000||highestTargetFatigueMicro<0||highestTargetFatigueMicro>150000000||proficiencyMicro<0||proficiencyMicro>1000000000||extraRests<0)throw new ArgumentException("Invalid set decision inputs");
            if(stressMicro>=policy.StopStressMicro)return ActorSetDecision.EndWorkout;
            if(injured)return ActorSetDecision.SkipInjuredExercise;
            if(highestTargetFatigueMicro>policy.RestFatigueMicro)return extraRests>=policy.MaximumExtraRests?ActorSetDecision.EndWorkout:ActorSetDecision.LongRest;
            if(firstExerciseSet&&!challengeFailed&&challengeEligible&&highestTargetFatigueMicro<policy.ChallengeFatigueMicro&&proficiencyMicro>=policy.ChallengeProficiencyMicro)return ActorSetDecision.Challenge;
            return autoEligible?ActorSetDecision.AutoCapacity:ActorSetDecision.ManualCapacity;
        }
        public static string ChooseMeal(string characterId,MealSlot slot,long balance)
        {
            var policy=ActorContent.Policy(characterId);if(balance<0)throw new ArgumentOutOfRangeException(nameof(balance));
            var meal=policy.Meals.Single(m=>m.Slot==slot);
            var upgrade=LifeContent.Find(slot,meal.UpgradeOptionId);var basic=LifeContent.Find(slot,meal.BasicOptionId);
            if(balance-meal.Reserve>=meal.UpgradeBalance&&balance>=checked(meal.Reserve+upgrade.Price))return upgrade.Id;
            return balance>=basic.Price?basic.Id:meal.SkipOptionId;
        }
        public static string ChooseNotebookPackage(string characterId,ShopState shop,long balance,long lowestKnowledgeMicro)
        {
            var policy=ActorContent.Policy(characterId);ShopRules.NormalizeAfterLoad(shop);
            if(shop.ActorId!=characterId||balance<0||lowestKnowledgeMicro<0||lowestKnowledgeMicro>100000000)throw new ArgumentException("Invalid shopping policy scope");
            if(lowestKnowledgeMicro>=policy.StopBuyingAtKnowledgeMicro)return "";
            int wanted=policy.WantedNotebookUnits;
            var eligible=shop.Offers.Where(o=>o.PackagesRemaining>0).Select(o=>EconomyContent.Product(shop.ShopId,o.ProductId)).Where(p=>p.ItemId==EconomyContent.Notebook&&p.UnitsPerPackage>=wanted&&p.Price<=balance-policy.PurchaseReserve).OrderBy(p=>p.Price).ThenBy(p=>p.Id,StringComparer.Ordinal).FirstOrDefault();
            return eligible==null?"":eligible.Id;
        }
    }
}
