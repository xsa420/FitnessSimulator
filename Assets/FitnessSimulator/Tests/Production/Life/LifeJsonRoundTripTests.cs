using System;
using FitnessSimulator.Game.Life;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class LifeJsonRoundTripTests
    {
        private static LifeActorState RoundTrip(LifeActorState state)
        {
            return LifeRules.NormalizeAfterLoad(JsonUtility.FromJson<LifeActorState>(JsonUtility.ToJson(state)));
        }

        [Test]
        public void UnityJsonNewRunCanBuyFirstMealWithoutPhantomIncomeReceipt()
        {
            var loaded = RoundTrip(LifeRules.Initialize("run", "player", 300, 0));
            Assert.That(loaded.HasLastIncome, Is.False);
            Assert.That(loaded.LastIncome, Is.Null);
            var meal = LifeRules.PrepareMeal(loaded, MealSlot.Breakfast, "porridge_egg", "day1-breakfast");
            Assert.That(meal.Status, Is.EqualTo(LifePreparationStatus.Prepared));
            var savedMeal = RoundTrip(meal.Candidate);
            Assert.That(savedMeal.Wallet.Balance, Is.EqualTo(290));
            Assert.That(savedMeal.Nutrition.IntakeKcal, Is.EqualTo(500));
            Assert.That(savedMeal.HasLastIncome, Is.False);
            Assert.That(savedMeal.LastIncome, Is.Null);
        }

        [Test]
        public void UnityJsonIncomeReceiptSurvivesAndCannotAwardTwice()
        {
            var state = RoundTrip(LifeRules.Initialize("run", "player", 300, 0));
            state = LifeRules.PrepareAffluence(state, 10);
            var income = LifeRules.PrepareIncome(state, 1);
            var loaded = RoundTrip(income.Candidate);
            Assert.That(loaded.HasLastIncome, Is.True);
            Assert.That(loaded.LastIncome.Amount, Is.EqualTo(120));
            Assert.That(loaded.LastIncome.AffluenceAtExecution, Is.EqualTo(10));
            Assert.That(loaded.LastIncome.SettlementKey, Is.EqualTo(income.SettlementKey));
            var repeated = LifeRules.PrepareIncome(loaded, 1);
            Assert.That(repeated.Status, Is.EqualTo(LifePreparationStatus.AlreadyApplied));
            Assert.That(repeated.Candidate.Wallet.Balance, Is.EqualTo(420));
            var meal = LifeRules.PrepareMeal(loaded, MealSlot.Dinner, "home_noodles", "day1-dinner");
            var afterMeal = RoundTrip(meal.Candidate);
            Assert.That(afterMeal.LastIncome.Amount, Is.EqualTo(120));
            Assert.That(afterMeal.Wallet.Balance, Is.EqualTo(400));
            var next = RoundTrip(LifeRules.StartNutritionDay(afterMeal, 2));
            Assert.That(next.LastIncome.Day, Is.EqualTo(1));
            Assert.That(LifeRules.PrepareIncome(next, 2).Candidate.Wallet.Balance, Is.EqualTo(520));
        }

        [Test]
        public void AbsentDefaultInlineIncomeIsIgnoredWithoutRequiringLoadAdapter()
        {
            var state = LifeRules.Initialize("run", "player", 300, 0);
            state.LastIncome = new IncomeReceipt();
            var loaded = JsonUtility.FromJson<LifeActorState>(JsonUtility.ToJson(state));
            var prepared = LifeRules.PrepareMeal(loaded, MealSlot.Breakfast, "skip", "b");
            Assert.That(prepared.Candidate.HasLastIncome, Is.False);
            Assert.That(prepared.Candidate.LastIncome, Is.Null);
            Assert.That(loaded.LastIncome, Is.Not.Null); // Preparation must not mutate the loaded input.
        }

        [Test]
        public void PresenceFlagCannotHidePopulatedReceiptOrInventMissingReceipt()
        {
            var paid = LifeRules.PrepareIncome(LifeRules.Initialize("run", "player", 300, 0), 1).Candidate;
            paid.HasLastIncome = false;
            Assert.Throws<ArgumentException>(() => RoundTrip(paid));
            var empty = LifeRules.Initialize("run", "player", 300, 0);
            empty.HasLastIncome = true;
            Assert.Throws<ArgumentException>(() => RoundTrip(empty));
        }

        [Test]
        public void RequiredInlineWalletAndNutritionCannotBecomeSilentDefaults()
        {
            var noWallet = LifeRules.Initialize("run", "player", 300, 0);
            noWallet.Wallet = null;
            Assert.Catch<ArgumentException>(() => RoundTrip(noWallet));
            var noNutrition = LifeRules.Initialize("run", "player", 300, 0);
            noNutrition.Nutrition = null;
            Assert.Catch<ArgumentException>(() => RoundTrip(noNutrition));
            var emptyWallet = LifeRules.Initialize("run", "player", 300, 0);
            emptyWallet.Wallet = new WalletState();
            Assert.Throws<ArgumentException>(() => RoundTrip(emptyWallet));
        }
    }
}
