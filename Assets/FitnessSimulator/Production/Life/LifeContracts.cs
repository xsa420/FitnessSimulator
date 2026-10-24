using System;

namespace FitnessSimulator.Game.Life
{
    public enum MealSlot { Breakfast, Lunch, Dinner }
    public enum LifePreparationStatus { Prepared, AlreadyApplied, InsufficientFunds, MealAlreadyChosen, WorkAlreadyCompleted, LegacyIncomeAlreadyApplied }

    [Serializable]
    public sealed class WalletState
    {
        public string ConfigVersion;
        public long Balance;
        public int Affluence;
    }

    [Serializable]
    public sealed class MealReceipt
    {
        public string SettlementKey;
        public string EventInstanceId;
        public string ConfigVersion;
        public MealSlot Slot;
        public string OptionId;
        public long Price;
        public long IntakeKcal;
    }

    [Serializable]
    public sealed class NutritionDay
    {
        public string RunId;
        public string ActorId;
        public int Day;
        public long IntakeKcal;
        public MealReceipt[] Meals = Array.Empty<MealReceipt>();
    }

    [Serializable]
    public sealed class IncomeReceipt
    {
        public string SettlementKey;
        public string ConfigVersion;
        public int Day;
        public int AffluenceAtExecution;
        public long Amount;
    }

    [Serializable]
    public sealed class WorkIncomeReceipt
    {
        public string RunId, ActorId, SettlementKey, ConfigVersion, AnchorId, EventInstanceId, EffectId;
        public int Day, AffluenceAtExecution;
        public long Amount;
    }

    [Serializable]
    public sealed class LifeActorState
    {
        public string RunId;
        public string ActorId;
        public string ConfigVersion;
        public long Revision;
        public WalletState Wallet;
        public NutritionDay Nutrition;
        public bool HasLastIncome;
        public IncomeReceipt LastIncome;
        // Only the latest paid work day, at most one receipt per half-day. Application logs retain history.
        public WorkIncomeReceipt[] WorkIncomeReceipts = Array.Empty<WorkIncomeReceipt>();
        public MoneyDeltaReceipt[] MoneyEffectReceipts = Array.Empty<MoneyDeltaReceipt>();
    }

    [Serializable]
    public sealed class MealOption
    {
        public string Id;
        public string DisplayName;
        public MealSlot Slot;
        public long Price;
        public long IntakeKcal;
    }

    // A domain candidate only: the Application transaction must publish it atomically.
    public sealed class MealPreparation
    {
        public LifePreparationStatus Status;
        public LifeActorState Candidate;
        public MealReceipt Receipt;
        public string SettlementKey;
    }

    public sealed class IncomePreparation
    {
        public LifePreparationStatus Status;
        public LifeActorState Candidate;
        public IncomeReceipt Receipt;
        public string SettlementKey;
    }

    public sealed class WorkIncomePreparation
    {
        public LifePreparationStatus Status;
        public LifeActorState Candidate;
        public WorkIncomeReceipt Receipt;
        public string SettlementKey;
    }
    [Serializable] public sealed class MoneyDeltaReceipt
    {
        public string RunId, ActorId, EventInstanceId, EffectId, SettlementKey;
        public int Day;
        public long RequestedAmount, ActualAmount, BeforeBalance, AfterBalance;
    }
    public sealed class MoneyDeltaPreparation
    {
        public LifePreparationStatus Status;
        public LifeActorState Candidate;
        public MoneyDeltaReceipt Receipt;
        public string SettlementKey;
    }

    [Serializable]
    public sealed class NutritionEvaluation
    {
        public string RunId;
        public string ActorId;
        public int Day;
        public string ConfigVersion;
        public long IntakeKcal;
        public long LocalRecoveryMultiplierNumerator;
        public long LocalRecoveryMultiplierDenominator;
        public long BodyRecoveryMultiplierNumerator;
        public long BodyRecoveryMultiplierDenominator;
        public long DeficitStressNumerator;
        public long DeficitStressDenominator;
    }
}
