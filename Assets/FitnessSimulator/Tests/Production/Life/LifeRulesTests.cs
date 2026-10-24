using System;
using FitnessSimulator.Game.Life;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class LifeRulesTests
    {
        [TestCase(300, 0)]
        [TestCase(360, 10)]
        public void InitializationUsesExplicitTemplateMoneyAndAffluence(long balance, int affluence)
        {
            var state = LifeRules.Initialize("run", "player", balance, affluence);
            Assert.That(state.ConfigVersion, Is.EqualTo(LifeContent.Version));
            Assert.That(state.Wallet.Balance, Is.EqualTo(balance));
            Assert.That(state.Wallet.Affluence, Is.EqualTo(affluence));
            Assert.That(state.Nutrition.IntakeKcal, Is.Zero);
            Assert.That(state.Nutrition.Meals, Is.Empty);
        }

        [Test]
        public void InsufficientMealPaymentLeavesChoiceOpenThenSkipConsumesOnlyThatSlot()
        {
            var state = LifeRules.Initialize("run", "player", 9, 0);
            var failed = LifeRules.PrepareMeal(state, MealSlot.Breakfast, "porridge_egg", "breakfast-event");
            Assert.That(failed.Status, Is.EqualTo(LifePreparationStatus.InsufficientFunds));
            Assert.That(failed.Candidate.Wallet.Balance, Is.EqualTo(9));
            Assert.That(failed.Candidate.Nutrition.Meals, Is.Empty);
            Assert.That(failed.Candidate.Revision, Is.EqualTo(state.Revision));
            Assert.That(failed.Receipt, Is.Null);
            var skipped = LifeRules.PrepareMeal(failed.Candidate, MealSlot.Breakfast, "skip", "breakfast-event");
            Assert.That(skipped.Status, Is.EqualTo(LifePreparationStatus.Prepared));
            Assert.That(skipped.Candidate.Nutrition.Meals.Length, Is.EqualTo(1));
            Assert.That(skipped.Candidate.Nutrition.IntakeKcal, Is.Zero);
            Assert.That(skipped.Candidate.Wallet.Balance, Is.EqualTo(9));
        }

        [Test]
        public void ThreeMinimumMealsConsumeMoneyAndImmediatelyAccumulateNutrition()
        {
            var original = LifeRules.Initialize("run", "player", 50, 10);
            var breakfast = LifeRules.PrepareMeal(original, MealSlot.Breakfast, "porridge_egg", "b");
            var lunch = LifeRules.PrepareMeal(breakfast.Candidate, MealSlot.Lunch, "home_rice", "l");
            var dinner = LifeRules.PrepareMeal(lunch.Candidate, MealSlot.Dinner, "home_noodles", "d");
            Assert.That(original.Wallet.Balance, Is.EqualTo(50));
            Assert.That(original.Nutrition.Meals, Is.Empty);
            Assert.That(dinner.Candidate.Wallet.Balance, Is.Zero);
            Assert.That(dinner.Candidate.Wallet.Affluence, Is.EqualTo(10));
            Assert.That(dinner.Candidate.Nutrition.IntakeKcal, Is.EqualTo(2000));
            Assert.That(dinner.Candidate.Nutrition.Meals.Length, Is.EqualTo(3));
            // Meals generate consumed receipts and nutrition; there is no item grant/redeem stage.
            Assert.That(breakfast.Receipt.IntakeKcal, Is.EqualTo(500));
            Assert.That(lunch.Receipt.Price, Is.EqualTo(20));
        }

        [Test]
        public void ReplayedMealDoesNotChargeAndDifferentChoiceCannotReuseSlot()
        {
            var paid = LifeRules.PrepareMeal(LifeRules.Initialize("run", "a", 300, 0), MealSlot.Breakfast, "sandwich", "b");
            var replay = LifeRules.PrepareMeal(paid.Candidate, MealSlot.Breakfast, "sandwich", "b");
            var changed = LifeRules.PrepareMeal(paid.Candidate, MealSlot.Breakfast, "porridge_egg", "new-b");
            Assert.That(replay.Status, Is.EqualTo(LifePreparationStatus.AlreadyApplied));
            Assert.That(replay.Candidate.Wallet.Balance, Is.EqualTo(285));
            Assert.That(replay.Candidate.Nutrition.IntakeKcal, Is.EqualTo(650));
            Assert.That(replay.Candidate.Revision, Is.EqualTo(paid.Candidate.Revision));
            Assert.That(changed.Status, Is.EqualTo(LifePreparationStatus.MealAlreadyChosen));
            Assert.That(changed.Candidate.Wallet.Balance, Is.EqualTo(285));
        }

        [Test]
        public void UnknownOptionAndWrongMealSlotAreRejectedWithoutDefaultSkip()
        {
            var state = LifeRules.Initialize("run", "a", 300, 0);
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareMeal(state, MealSlot.Breakfast, "unknown", "b"));
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareMeal(state, MealSlot.Breakfast, "home_rice", "b"));
            Assert.Throws<ArgumentOutOfRangeException>(() => LifeRules.PrepareMeal(state, (MealSlot)99, "skip", "b"));
            Assert.That(state.Nutrition.Meals, Is.Empty);
            Assert.That(state.Wallet.Balance, Is.EqualTo(300));
        }

        [Test]
        public void IncomeReadsCurrentAffluenceAndSemanticReceiptPreventsSecondAward()
        {
            var initial = LifeRules.Initialize("run", "a", 300, 0);
            var changed = LifeRules.PrepareAffluence(initial, 10);
            var income = LifeRules.PrepareIncome(changed, 1);
            Assert.That(initial.Wallet.Affluence, Is.Zero);
            Assert.That(income.Receipt.Amount, Is.EqualTo(120));
            Assert.That(income.Candidate.Wallet.Balance, Is.EqualTo(420));
            var laterAffluence = LifeRules.PrepareAffluence(income.Candidate, 50);
            var replay = LifeRules.PrepareIncome(laterAffluence, 1);
            Assert.That(replay.Status, Is.EqualTo(LifePreparationStatus.AlreadyApplied));
            Assert.That(replay.Receipt.AffluenceAtExecution, Is.EqualTo(10));
            Assert.That(replay.Candidate.Wallet.Balance, Is.EqualTo(420));
            Assert.That(replay.SettlementKey, Is.EqualTo(income.SettlementKey));
        }

        [TestCase(0, 2000, 8000, 16000, 2000)]
        [TestCase(1000, 5000, 8000, 8000, 2000)]
        [TestCase(2000, 8000, 8000, 0, 2000)]
        [TestCase(2500, 8000, 8000, 0, 2000)]
        [TestCase(long.MaxValue, 8000, 8000, 0, 2000)]
        public void NutritionExportsExactRatiosWithoutExcessIntakeBonus(long intake, long multiplierN, long multiplierD, long deficitN, long deficitD)
        {
            // Explicit test fixture input, not a production meal or autonomous actor claim.
            var day = new NutritionDay { RunId = "test", ActorId = "a", Day = 4, IntakeKcal = intake };
            var result = LifeRules.EvaluateNutrition(day);
            Assert.That(result.LocalRecoveryMultiplierNumerator, Is.EqualTo(multiplierN));
            Assert.That(result.LocalRecoveryMultiplierDenominator, Is.EqualTo(multiplierD));
            Assert.That(result.BodyRecoveryMultiplierNumerator, Is.EqualTo(multiplierN));
            Assert.That(result.BodyRecoveryMultiplierDenominator, Is.EqualTo(multiplierD));
            Assert.That(result.DeficitStressNumerator, Is.EqualTo(deficitN));
            Assert.That(result.DeficitStressDenominator, Is.EqualTo(deficitD));
            Assert.That(result.IntakeKcal, Is.EqualTo(intake));
            Assert.That(result.ActorId, Is.EqualTo("a"));
        }

        [Test]
        public void NewDayDoesNotEraseOldDayOrMutatePublishedInput()
        {
            var paid = LifeRules.PrepareMeal(LifeRules.Initialize("run", "a", 300, 0), MealSlot.Breakfast, "porridge_egg", "b").Candidate;
            var earned = LifeRules.PrepareIncome(paid, 1).Candidate;
            var next = LifeRules.StartNutritionDay(earned, 2);
            Assert.That(earned.Nutrition.Day, Is.EqualTo(1));
            Assert.That(earned.Nutrition.IntakeKcal, Is.EqualTo(500));
            Assert.That(earned.Nutrition.Meals.Length, Is.EqualTo(1));
            Assert.That(next.Nutrition.Day, Is.EqualTo(2));
            Assert.That(next.Nutrition.IntakeKcal, Is.Zero);
            Assert.That(next.Nutrition.Meals, Is.Empty);
            Assert.That(next.Wallet.Balance, Is.EqualTo(390));
            Assert.That(LifeRules.PrepareIncome(next, 2).Candidate.Wallet.Balance, Is.EqualTo(490));
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareIncome(next, 1));
            Assert.Throws<ArgumentException>(() => LifeRules.StartNutritionDay(earned, 3));
        }

        [Test]
        public void ReturnedReceiptAndCandidateDoNotAliasEachOtherOrInput()
        {
            var current = LifeRules.Initialize("run", "a", 300, 0);
            var result = LifeRules.PrepareMeal(current, MealSlot.Breakfast, "porridge_egg", "b");
            result.Receipt.IntakeKcal = 999;
            result.Candidate.Wallet.Balance = 1;
            Assert.That(result.Candidate.Nutrition.Meals[0].IntakeKcal, Is.EqualTo(500));
            Assert.That(current.Wallet.Balance, Is.EqualTo(300));
            var income = LifeRules.PrepareIncome(current, 1);
            income.Receipt.Amount = 0;
            Assert.That(income.Candidate.LastIncome.Amount, Is.EqualTo(100));
        }

        [Test]
        public void ActorsAndRunsHaveDistinctResourcesAndUnambiguousKeys()
        {
            var a = LifeRules.Initialize("run", "a", 300, 0);
            var b = LifeRules.Initialize("run", "b", 360, 10);
            var meal = LifeRules.PrepareMeal(a, MealSlot.Breakfast, "porridge_egg", "b");
            Assert.That(b.Wallet.Balance, Is.EqualTo(360));
            Assert.That(b.Nutrition.IntakeKcal, Is.Zero);
            Assert.That(meal.SettlementKey, Is.Not.EqualTo(LifeRules.MealKey("run", "b", 1, MealSlot.Breakfast)));
            Assert.That(LifeRules.IncomeKey("a|b", "c", 1), Is.Not.EqualTo(LifeRules.IncomeKey("a", "b|c", 1)));
            Assert.That(LifeRules.IncomeKey("run", "a", 1), Is.Not.EqualTo(LifeRules.IncomeKey("new-run", "a", 1)));
            a.Nutrition.ActorId = "b";
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareIncome(a, 1));
        }

        [Test]
        public void InvalidStateAndOverflowCannotPublishPartialEffects()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LifeRules.Initialize("run", "a", -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => LifeRules.Initialize("run", "a", 0, 101));
            var rich = LifeRules.Initialize("run", "a", long.MaxValue, 0);
            Assert.Throws<OverflowException>(() => LifeRules.PrepareIncome(rich, 1));
            Assert.That(rich.Wallet.Balance, Is.EqualTo(long.MaxValue));
            Assert.That(rich.LastIncome, Is.Null);
            var state = LifeRules.Initialize("run", "a", 300, 0);
            state.ConfigVersion = "unknown";
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareIncome(state, 1));
            Assert.Throws<ArgumentException>(() => LifeRules.EvaluateNutrition(new NutritionDay { RunId = "test", ActorId = "a", Day = 1, IntakeKcal = -1 }));
        }

        [Test]
        public void MenuQueriesCannotModifyAuthoritativePrices()
        {
            var menu = LifeContent.Menu(MealSlot.Breakfast);
            menu[1].Price = 0;
            Assert.That(LifeContent.Find(MealSlot.Breakfast, "porridge_egg").Price, Is.EqualTo(10));
            Assert.That(LifeContent.Menu(MealSlot.Lunch).Length, Is.EqualTo(4));
            Assert.That(LifeContent.Menu(MealSlot.Dinner).Length, Is.EqualTo(4));
        }
    }
}
