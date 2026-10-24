using System;
using System.Linq;
using FitnessSimulator.Game.Configuration;
namespace FitnessSimulator.Game.Life
{
    [Serializable] public sealed class LifeContentDocument
    {
        public string Version, StateVersion, WorkReceiptVersion, DefaultWorkEffectId;
        public long LegacyBaseDailyIncome, LegacyIncomePerAffluence;
        public MealMenuDefinition[] Meals;
        public WorkIncomeDefinition[] WorkEffects;
        public NutritionDefinition Nutrition;
    }
    [Serializable] public sealed class MealMenuDefinition { public MealSlot Slot; public MealOption[] Options; }
    [Serializable] public sealed class WorkIncomeDefinition { public string Id; public string[] Anchors; public long BaseAmount, PerAffluence; }
    [Serializable] public sealed class NutritionDefinition
    {
        public long MinimumIntakeKcal, LocalBaseNumerator, LocalPerKcal, LocalDenominator;
        public long BodyBaseNumerator, BodyPerKcal, BodyDenominator, DeficitStressNumerator;
    }
    public static partial class LifeContent
    {
        public static LifeContentDocument Definition => ContentRuntime.Current.Get<LifeContentDocument>("life");
        public static WorkIncomeDefinition WorkEffect(string id)
            => Definition.WorkEffects.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("life.WorkEffects: unknown id " + id);
        public static void ValidateContent()
        {
            var d = Definition;
            if (string.IsNullOrWhiteSpace(d.Version) || string.IsNullOrWhiteSpace(d.StateVersion) || string.IsNullOrWhiteSpace(d.WorkReceiptVersion) ||
                d.LegacyBaseDailyIncome < 0 || d.LegacyIncomePerAffluence < 0) throw new ArgumentException("life: invalid version/legacy income");
            if (d.Meals == null || d.Meals.Length != 3 || d.Meals.Any(x => x == null) || d.Meals.Select(x => x.Slot).Distinct().Count() != 3)
                throw new ArgumentException("life.Meals: require three distinct meals");
            foreach (var m in d.Meals)
            {
                if (!Enum.IsDefined(typeof(MealSlot), m.Slot) || m.Options == null || m.Options.Length == 0 || m.Options.Any(x => x == null ||
                    string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.DisplayName) || x.Slot != m.Slot || x.Price < 0 || x.IntakeKcal < 0) ||
                    m.Options.Select(x => x.Id).Distinct().Count() != m.Options.Length || !m.Options.Any(x => x.Id == "skip" && x.Price == 0 && x.IntakeKcal == 0))
                    throw new ArgumentException("life.Meals." + m.Slot + ": invalid menu or missing explicit skip");
            }
            if (d.WorkEffects == null || d.WorkEffects.Length == 0 || d.WorkEffects.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) ||
                x.BaseAmount < 0 || x.PerAffluence < 0 || x.Anchors == null || x.Anchors.Length == 0 || x.Anchors.Any(a => a != "morning" && a != "afternoon") || x.Anchors.Distinct().Count() != x.Anchors.Length) ||
                d.WorkEffects.Select(x => x.Id).Distinct().Count() != d.WorkEffects.Length || !d.WorkEffects.Any(x => x.Id == d.DefaultWorkEffectId))
                throw new ArgumentException("life.WorkEffects: invalid definitions/default");
            var n = d.Nutrition;
            if (n == null || n.MinimumIntakeKcal <= 0 || n.LocalBaseNumerator < 0 || n.BodyBaseNumerator < 0 || n.LocalPerKcal < 0 || n.BodyPerKcal < 0 ||
                n.LocalDenominator <= 0 || n.BodyDenominator <= 0 || n.DeficitStressNumerator < 0 ||
                checked(n.LocalBaseNumerator + n.LocalPerKcal * n.MinimumIntakeKcal) != n.LocalDenominator ||
                checked(n.BodyBaseNumerator + n.BodyPerKcal * n.MinimumIntakeKcal) != n.BodyDenominator)
                throw new ArgumentException("life.Nutrition: invalid bounded recovery formula");
        }
    }
}