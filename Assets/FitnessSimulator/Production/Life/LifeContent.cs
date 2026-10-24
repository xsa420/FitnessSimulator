using System;
using System.Linq;
namespace FitnessSimulator.Game.Life
{
    public static partial class LifeContent
    {
        public static string Version => Definition.StateVersion;
        public static long BaseDailyIncome => Definition.LegacyBaseDailyIncome;
        public static long IncomePerAffluence => Definition.LegacyIncomePerAffluence;
        public static string WorkIncomeVersion => Definition.WorkReceiptVersion;
        public static long BaseHalfDayIncome => WorkEffect(Definition.DefaultWorkEffectId).BaseAmount;
        public static long MinimumIntakeKcal => Definition.Nutrition.MinimumIntakeKcal;
        public static MealOption[] Menu(MealSlot slot)
            => Definition.Meals.SingleOrDefault(x => x.Slot == slot)?.Options ?? throw new ArgumentOutOfRangeException(nameof(slot));
        public static MealOption Find(MealSlot slot, string optionId)
            => Menu(slot).SingleOrDefault(x => x.Id == optionId) ?? throw new ArgumentException("Unknown meal option for this slot: " + optionId);
    }
}