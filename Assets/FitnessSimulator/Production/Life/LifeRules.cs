using System;
using System.Collections.Generic;
using System.Globalization;

namespace FitnessSimulator.Game.Life
{
    public static class LifeRules
    {
        public static LifeActorState Initialize(string runId, string actorId, long initialBalance, int affluence, int day = 1)
        {
            RequireId(runId, nameof(runId));
            RequireId(actorId, nameof(actorId));
            if (initialBalance < 0) throw new ArgumentOutOfRangeException(nameof(initialBalance));
            ValidateAffluence(affluence);
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            return new LifeActorState {
                RunId = runId, ActorId = actorId, ConfigVersion = LifeContent.Version,
                Wallet = new WalletState { ConfigVersion = LifeContent.Version, Balance = initialBalance, Affluence = affluence },
                HasLastIncome = false,
                Nutrition = new NutritionDay { RunId = runId, ActorId = actorId, Day = day }
            };
        }

        // JsonUtility can materialize an absent inline class as a default object.
        // The presence bit is authoritative; populated contradictory payloads are rejected.
        public static LifeActorState NormalizeAfterLoad(LifeActorState state)
        {
            Validate(state);
            return Copy(state);
        }

        public static LifeActorState PrepareAffluence(LifeActorState current, int affluence)
        {
            Validate(current);
            ValidateAffluence(affluence);
            var candidate = Copy(current);
            candidate.Wallet.Affluence = affluence;
            candidate.Revision = checked(current.Revision + 1);
            return candidate;
        }

        public static MealPreparation PrepareMeal(LifeActorState current, MealSlot slot, string optionId, string eventInstanceId)
        {
            Validate(current);
            RequireId(eventInstanceId, nameof(eventInstanceId));
            var option = LifeContent.Find(slot, optionId);
            string key = MealKey(current.RunId, current.ActorId, current.Nutrition.Day, slot);
            foreach (var prior in current.Nutrition.Meals)
            {
                if (prior.Slot != slot) continue;
                bool replay = prior.OptionId == optionId && prior.EventInstanceId == eventInstanceId;
                return new MealPreparation {
                    Status = replay ? LifePreparationStatus.AlreadyApplied : LifePreparationStatus.MealAlreadyChosen,
                    Candidate = Copy(current), Receipt = Copy(prior), SettlementKey = key
                };
            }
            if (current.Wallet.Balance < option.Price)
                return new MealPreparation {
                    Status = LifePreparationStatus.InsufficientFunds, Candidate = Copy(current), SettlementKey = key
                };

            var receipt = new MealReceipt {
                SettlementKey = key, EventInstanceId = eventInstanceId, ConfigVersion = LifeContent.Version,
                Slot = slot, OptionId = option.Id, Price = option.Price, IntakeKcal = option.IntakeKcal
            };
            var candidate = Copy(current);
            candidate.Wallet.Balance = checked(candidate.Wallet.Balance - option.Price);
            candidate.Nutrition.IntakeKcal = checked(candidate.Nutrition.IntakeKcal + option.IntakeKcal);
            var meals = new List<MealReceipt>(candidate.Nutrition.Meals) { Copy(receipt) };
            candidate.Nutrition.Meals = meals.ToArray();
            candidate.Revision = checked(current.Revision + 1);
            // No inventory or body patch: these meals are consumed immediately; recovery occurs at day end.
            return new MealPreparation { Status = LifePreparationStatus.Prepared, Candidate = candidate, Receipt = receipt, SettlementKey = key };
        }

        public static IncomePreparation PrepareIncome(LifeActorState current, int day)
        {
            Validate(current);
            if (day != current.Nutrition.Day) throw new ArgumentException("Income day must match the actor's current nutrition day.", nameof(day));
            string key = IncomeKey(current.RunId, current.ActorId, day);
            if (current.HasLastIncome && current.LastIncome.Day == day)
                return new IncomePreparation {
                    Status = LifePreparationStatus.AlreadyApplied, Candidate = Copy(current),
                    Receipt = Copy(current.LastIncome), SettlementKey = key
                };
            long amount = checked(LifeContent.BaseDailyIncome + LifeContent.IncomePerAffluence * current.Wallet.Affluence);
            var receipt = new IncomeReceipt {
                SettlementKey = key, ConfigVersion = LifeContent.Version, Day = day,
                AffluenceAtExecution = current.Wallet.Affluence, Amount = amount
            };
            var candidate = Copy(current);
            candidate.Wallet.Balance = checked(current.Wallet.Balance + amount);
            candidate.LastIncome = Copy(receipt);
            candidate.HasLastIncome = true;
            candidate.Revision = checked(current.Revision + 1);
            return new IncomePreparation { Status = LifePreparationStatus.Prepared, Candidate = candidate, Receipt = receipt, SettlementKey = key };
        }

        // Application verifies the effective plan and actual completed work event before calling.
        public static WorkIncomePreparation PrepareWorkIncome(LifeActorState current, int day, string anchorId, string eventInstanceId) => PrepareWorkIncome(current, day, anchorId, eventInstanceId, LifeContent.Definition.DefaultWorkEffectId);
        public static WorkIncomePreparation PrepareWorkIncome(LifeActorState current, int day, string anchorId, string eventInstanceId, string effectId)
        {
            Validate(current);
            if (day != current.Nutrition.Day) throw new ArgumentException("Work income day must match the current nutrition day.", nameof(day));
            RequireId(eventInstanceId, nameof(eventInstanceId));
            string key = WorkIncomeKey(current.RunId, current.ActorId, day, anchorId);
            var effect = LifeContent.WorkEffect(effectId);
            if (Array.IndexOf(effect.Anchors, anchorId) < 0) throw new ArgumentException("Work effect is not allowed at this anchor.");
            if (current.HasLastIncome && current.LastIncome.Day == day)
                return new WorkIncomePreparation { Status = LifePreparationStatus.LegacyIncomeAlreadyApplied,
                    Candidate = Copy(current), SettlementKey = key };
            foreach (var prior in current.WorkIncomeReceipts ?? Array.Empty<WorkIncomeReceipt>())
            {
                if (prior.Day != day || prior.AnchorId != anchorId) continue;
                return new WorkIncomePreparation {
                    Status = prior.EventInstanceId == eventInstanceId ? LifePreparationStatus.AlreadyApplied : LifePreparationStatus.WorkAlreadyCompleted,
                    Candidate = Copy(current), Receipt = Copy(prior), SettlementKey = key
                };
            }
            var receipt = new WorkIncomeReceipt {
                RunId = current.RunId, ActorId = current.ActorId, Day = day, AnchorId = anchorId, EventInstanceId = eventInstanceId,
                ConfigVersion = LifeContent.WorkIncomeVersion, SettlementKey = key, EffectId = effectId,
                AffluenceAtExecution = current.Wallet.Affluence, Amount = checked(effect.BaseAmount + effect.PerAffluence * current.Wallet.Affluence)
            };
            var candidate = Copy(current);
            candidate.Wallet.Balance = checked(candidate.Wallet.Balance + receipt.Amount);
            var receipts = new List<WorkIncomeReceipt>();
            foreach (var prior in candidate.WorkIncomeReceipts)
                if (prior.Day == day) receipts.Add(prior);
            receipts.Add(Copy(receipt));
            candidate.WorkIncomeReceipts = receipts.ToArray();
            candidate.Revision = checked(current.Revision + 1);
            return new WorkIncomePreparation { Status = LifePreparationStatus.Prepared, Candidate = candidate, Receipt = receipt, SettlementKey = key };
        }

        public static MoneyDeltaPreparation PrepareMoneyDelta(LifeActorState current,long amount,string eventInstanceId,string effectId)
        {
            Validate(current);RequireId(eventInstanceId,nameof(eventInstanceId));RequireId(effectId,nameof(effectId));
            string key=MoneyDeltaKey(current.RunId,current.ActorId,eventInstanceId,effectId);
            foreach(var prior in current.MoneyEffectReceipts??Array.Empty<MoneyDeltaReceipt>())
            {
                if(prior.SettlementKey!=key)continue;
                if(prior.RequestedAmount!=amount)throw new ArgumentException("Money effect replay parameters changed");
                return new MoneyDeltaPreparation { Status=LifePreparationStatus.AlreadyApplied,Candidate=Copy(current),Receipt=Copy(prior),SettlementKey=key };
            }
            var candidate=Copy(current);long before=candidate.Wallet.Balance;
            long actual=amount < -before ? -before : amount;
            candidate.Wallet.Balance=checked(before+actual);candidate.Revision=checked(candidate.Revision+1);
            var receipt=new MoneyDeltaReceipt { RunId=current.RunId,ActorId=current.ActorId,Day=current.Nutrition.Day,EventInstanceId=eventInstanceId,EffectId=effectId,SettlementKey=key,RequestedAmount=amount,ActualAmount=actual,BeforeBalance=before,AfterBalance=candidate.Wallet.Balance };
            var ledger=new List<MoneyDeltaReceipt>();
            foreach(var prior in candidate.MoneyEffectReceipts)if(prior.EventInstanceId==eventInstanceId)ledger.Add(prior);
            if(ledger.Count>=64)throw new ArgumentException("Too many money effects in one event");
            ledger.Add(Copy(receipt));candidate.MoneyEffectReceipts=ledger.ToArray();
            return new MoneyDeltaPreparation { Status=LifePreparationStatus.Prepared,Candidate=candidate,Receipt=receipt,SettlementKey=key };
        }
        // Cost is distinct from clamped loss: insufficient balance leaves both money and event opportunity untouched.
        public static MoneyDeltaPreparation PrepareMoneyCost(LifeActorState current,long amount,string eventInstanceId,string effectId)
        {
            Validate(current);if(amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            string key=MoneyDeltaKey(current.RunId,current.ActorId,eventInstanceId,effectId);
            foreach(var prior in current.MoneyEffectReceipts??Array.Empty<MoneyDeltaReceipt>())
                if(prior.SettlementKey==key)return PrepareMoneyDelta(current,-amount,eventInstanceId,effectId);
            if(current.Wallet.Balance<amount)return new MoneyDeltaPreparation { Status=LifePreparationStatus.InsufficientFunds,Candidate=Copy(current),SettlementKey=key };
            return PrepareMoneyDelta(current,-amount,eventInstanceId,effectId);
        }
        public static string MoneyDeltaKey(string run,string actor,string instance,string effect)
        { RequireId(run,nameof(run));RequireId(actor,nameof(actor));RequireId(instance,nameof(instance));RequireId(effect,nameof(effect));return "MoneyEffect|"+Part(run)+Part(actor)+Part(instance)+Part(effect); }
        public static NutritionEvaluation EvaluateNutrition(NutritionDay day)
        {
            ValidateNutrition(day);
            var formula = LifeContent.Definition.Nutrition;
            long capped = Math.Min(day.IntakeKcal, formula.MinimumIntakeKcal);
            return new NutritionEvaluation {
                RunId = day.RunId, ActorId = day.ActorId, Day = day.Day, ConfigVersion = LifeContent.Version,
                IntakeKcal = day.IntakeKcal,
                LocalRecoveryMultiplierNumerator = checked(formula.LocalBaseNumerator + formula.LocalPerKcal * capped), LocalRecoveryMultiplierDenominator = formula.LocalDenominator,
                BodyRecoveryMultiplierNumerator = checked(formula.BodyBaseNumerator + formula.BodyPerKcal * capped), BodyRecoveryMultiplierDenominator = formula.BodyDenominator,
                DeficitStressNumerator = checked(formula.DeficitStressNumerator * (formula.MinimumIntakeKcal - capped)), DeficitStressDenominator = formula.MinimumIntakeKcal
            };
        }

        // Caller archives the old NutritionDay in the same N01 transaction before publishing this candidate.
        // This method does not advance the world date or clear the supplied state's old records.
        public static LifeActorState StartNutritionDay(LifeActorState current, int nextDay)
        {
            Validate(current);
            if (nextDay != checked(current.Nutrition.Day + 1)) throw new ArgumentException("Only the next logical day can be prepared.", nameof(nextDay));
            var candidate = Copy(current);
            candidate.Nutrition = new NutritionDay { RunId = current.RunId, ActorId = current.ActorId, Day = nextDay };
            candidate.Revision = checked(current.Revision + 1);
            return candidate;
        }

        public static string IncomeKey(string runId, string actorId, int day) => Key(runId, actorId, day, "E01.Income");
        public static string WorkIncomeKey(string runId, string actorId, int day, string anchorId)
        {
            if (anchorId != "morning" && anchorId != "afternoon") throw new ArgumentException("Work income requires morning or afternoon.", nameof(anchorId));
            return Key(runId, actorId, day, "WorkIncome." + anchorId);
        }
        public static string MealKey(string runId, string actorId, int day, MealSlot slot)
        {
            if (!Enum.IsDefined(typeof(MealSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            return Key(runId, actorId, day, "Meal." + slot);
        }

        private static string Key(string runId, string actorId, int day, string step)
        {
            RequireId(runId, nameof(runId)); RequireId(actorId, nameof(actorId));
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            return "Life|" + Part(runId) + Part(actorId) + day.ToString(CultureInfo.InvariantCulture) + "|" + step;
        }
        private static string Part(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        private static void RequireId(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A stable identity is required.", name);
        }
        private static void ValidateAffluence(int value)
        {
            if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
        }
        private static void ValidateNutrition(NutritionDay day)
        {
            if (day == null) throw new ArgumentNullException(nameof(day));
            RequireId(day.RunId, nameof(day.RunId)); RequireId(day.ActorId, nameof(day.ActorId));
            if (day.Day < 1 || day.IntakeKcal < 0 || day.Meals == null) throw new ArgumentException("Invalid nutrition state.");
            var slots = new HashSet<MealSlot>();
            long mealIntake = 0;
            foreach (var meal in day.Meals)
            {
                if (meal == null || !Enum.IsDefined(typeof(MealSlot), meal.Slot) || !slots.Add(meal.Slot))
                    throw new ArgumentException("Invalid or duplicate meal receipt.");
                if (meal.ConfigVersion != LifeContent.Version || meal.Price < 0 || meal.IntakeKcal < 0 ||
                    meal.SettlementKey != MealKey(day.RunId, day.ActorId, day.Day, meal.Slot))
                    throw new ArgumentException("Invalid meal receipt contents.");
                RequireId(meal.EventInstanceId, nameof(meal.EventInstanceId));
                RequireId(meal.OptionId, nameof(meal.OptionId));
                var configured = LifeContent.Find(meal.Slot, meal.OptionId);
                if (meal.Price != configured.Price || meal.IntakeKcal != configured.IntakeKcal)
                    throw new ArgumentException("Meal receipt does not match its content version.");
                mealIntake = checked(mealIntake + meal.IntakeKcal);
            }
            if (mealIntake > day.IntakeKcal) throw new ArgumentException("Nutrition total cannot omit completed meals.");
        }
        private static void Validate(LifeActorState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            RequireId(state.RunId, nameof(state.RunId)); RequireId(state.ActorId, nameof(state.ActorId));
            if (state.ConfigVersion != LifeContent.Version) throw new ArgumentException("Unsupported life content version.");
            if (state.Revision < 0 || state.Wallet == null || state.Wallet.ConfigVersion != LifeContent.Version || state.Wallet.Balance < 0)
                throw new ArgumentException("Invalid or missing wallet state.");
            ValidateAffluence(state.Wallet.Affluence); ValidateNutrition(state.Nutrition);
            if (state.Nutrition.RunId != state.RunId || state.Nutrition.ActorId != state.ActorId)
                throw new ArgumentException("Nutrition resource scope does not match wallet owner.");
            if (!state.HasLastIncome && !IsAbsentIncomePayload(state.LastIncome))
                throw new ArgumentException("Income payload contradicts its presence bit; explicit migration is required.");
            if (state.HasLastIncome && (state.LastIncome == null || state.LastIncome.Day < 1 || state.LastIncome.Day > state.Nutrition.Day ||
                state.LastIncome.ConfigVersion != LifeContent.Version || state.LastIncome.Amount < 0 ||
                state.LastIncome.SettlementKey != IncomeKey(state.RunId, state.ActorId, state.LastIncome.Day)))
                throw new ArgumentException("Invalid income receipt.");
            if (state.HasLastIncome)
            {
                ValidateAffluence(state.LastIncome.AffluenceAtExecution);
                if (state.LastIncome.Amount != LifeContent.BaseDailyIncome + LifeContent.IncomePerAffluence * state.LastIncome.AffluenceAtExecution)
                    throw new ArgumentException("Income receipt does not match its execution snapshot.");
            }
            var moneyKeys=new HashSet<string>(StringComparer.Ordinal);string moneyEvent=null;
            if(state.MoneyEffectReceipts!=null&&state.MoneyEffectReceipts.Length>64)throw new ArgumentException("Too many current-event money receipts");
            foreach(var receipt in state.MoneyEffectReceipts??Array.Empty<MoneyDeltaReceipt>())
            {
                if(receipt==null||receipt.RunId!=state.RunId||receipt.ActorId!=state.ActorId||receipt.Day<1||receipt.Day>state.Nutrition.Day||receipt.BeforeBalance<0||receipt.AfterBalance<0||receipt.ActualAmount!=(receipt.RequestedAmount < -receipt.BeforeBalance ? -receipt.BeforeBalance : receipt.RequestedAmount)||checked(receipt.BeforeBalance+receipt.ActualAmount)!=receipt.AfterBalance||receipt.SettlementKey!=MoneyDeltaKey(state.RunId,state.ActorId,receipt.EventInstanceId,receipt.EffectId)||!moneyKeys.Add(receipt.SettlementKey))throw new ArgumentException("Invalid money effect receipt");
                if(moneyEvent!=null&&moneyEvent!=receipt.EventInstanceId)throw new ArgumentException("Mixed money event receipts");moneyEvent=receipt.EventInstanceId;
            }
            // A missing additive field is an old save, not evidence of any work payment.
            var work = state.WorkIncomeReceipts ?? Array.Empty<WorkIncomeReceipt>();
            if (work.Length > 2) throw new ArgumentException("Too many work receipts.");
            int workDay = 0;
            var anchors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in work)
            {
                if (receipt == null || receipt.RunId != state.RunId || receipt.ActorId != state.ActorId ||
                    receipt.ConfigVersion != LifeContent.WorkIncomeVersion || receipt.Day < 1 || receipt.Day > state.Nutrition.Day)
                    throw new ArgumentException("Invalid work receipt scope or day.");
                if (workDay != 0 && workDay != receipt.Day || !anchors.Add(receipt.AnchorId))
                    throw new ArgumentException("Work receipts must cover distinct anchors of one day.");
                workDay = receipt.Day;
                RequireId(receipt.EventInstanceId, nameof(receipt.EventInstanceId));
                ValidateAffluence(receipt.AffluenceAtExecution);
                if (receipt.SettlementKey != WorkIncomeKey(state.RunId, state.ActorId, receipt.Day, receipt.AnchorId) ||
                    receipt.Amount != WorkReceiptAmount(receipt))
                    throw new ArgumentException("Work receipt does not match its execution snapshot.");
            }
        }
        private static long WorkReceiptAmount(WorkIncomeReceipt receipt)
        {
            // Explicit pre-configuration receipt schema: no effect identity existed.
            if (string.IsNullOrEmpty(receipt.EffectId)) return 50L + receipt.AffluenceAtExecution;
            var effect = LifeContent.WorkEffect(receipt.EffectId);
            if (Array.IndexOf(effect.Anchors, receipt.AnchorId) < 0) throw new ArgumentException("Invalid work receipt effect anchor");
            return checked(effect.BaseAmount + effect.PerAffluence * receipt.AffluenceAtExecution);
        }
        private static bool IsAbsentIncomePayload(IncomeReceipt receipt)
        {
            return receipt == null || (string.IsNullOrEmpty(receipt.SettlementKey) &&
                string.IsNullOrEmpty(receipt.ConfigVersion) && receipt.Day == 0 &&
                receipt.AffluenceAtExecution == 0 && receipt.Amount == 0);
        }
        private static LifeActorState Copy(LifeActorState state)
        {
            var meals = new MealReceipt[state.Nutrition.Meals.Length];
            for (int i = 0; i < meals.Length; i++) meals[i] = Copy(state.Nutrition.Meals[i]);
            var sourceWork = state.WorkIncomeReceipts ?? Array.Empty<WorkIncomeReceipt>();
            var work = new WorkIncomeReceipt[sourceWork.Length];
            for (int i = 0; i < work.Length; i++) work[i] = Copy(sourceWork[i]);
            return new LifeActorState {
                RunId = state.RunId, ActorId = state.ActorId, ConfigVersion = state.ConfigVersion, Revision = state.Revision,
                Wallet = new WalletState { ConfigVersion = state.Wallet.ConfigVersion, Balance = state.Wallet.Balance, Affluence = state.Wallet.Affluence },
                Nutrition = new NutritionDay { RunId = state.RunId, ActorId = state.ActorId, Day = state.Nutrition.Day,
                    IntakeKcal = state.Nutrition.IntakeKcal, Meals = meals },
                HasLastIncome = state.HasLastIncome,
                LastIncome = state.HasLastIncome ? Copy(state.LastIncome) : null,
                WorkIncomeReceipts = work,
                MoneyEffectReceipts = Array.ConvertAll(state.MoneyEffectReceipts ?? Array.Empty<MoneyDeltaReceipt>(), Copy)
            };
        }
        private static MealReceipt Copy(MealReceipt receipt) => new MealReceipt {
            SettlementKey = receipt.SettlementKey, EventInstanceId = receipt.EventInstanceId, ConfigVersion = receipt.ConfigVersion,
            Slot = receipt.Slot, OptionId = receipt.OptionId, Price = receipt.Price, IntakeKcal = receipt.IntakeKcal
        };
        private static IncomeReceipt Copy(IncomeReceipt receipt) => new IncomeReceipt {
            SettlementKey = receipt.SettlementKey, ConfigVersion = receipt.ConfigVersion, Day = receipt.Day,
            AffluenceAtExecution = receipt.AffluenceAtExecution, Amount = receipt.Amount
        };
        private static MoneyDeltaReceipt Copy(MoneyDeltaReceipt receipt) => new MoneyDeltaReceipt { RunId=receipt.RunId,ActorId=receipt.ActorId,Day=receipt.Day,EventInstanceId=receipt.EventInstanceId,EffectId=receipt.EffectId,SettlementKey=receipt.SettlementKey,RequestedAmount=receipt.RequestedAmount,ActualAmount=receipt.ActualAmount,BeforeBalance=receipt.BeforeBalance,AfterBalance=receipt.AfterBalance };
        private static WorkIncomeReceipt Copy(WorkIncomeReceipt receipt) => new WorkIncomeReceipt {
            RunId = receipt.RunId, ActorId = receipt.ActorId, SettlementKey = receipt.SettlementKey, ConfigVersion = receipt.ConfigVersion,
            Day = receipt.Day, AnchorId = receipt.AnchorId, EventInstanceId = receipt.EventInstanceId, EffectId = receipt.EffectId,
            AffluenceAtExecution = receipt.AffluenceAtExecution, Amount = receipt.Amount
        };
    }
}
