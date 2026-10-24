using System;
using FitnessSimulator.Game.Life;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class WorkIncomeTests
    {
        [TestCase(0)][TestCase(10)][TestCase(100)]
        public void TwoCompletedHalvesEqualFormerDailyAmount(int affluence)
        {
            var original = LifeRules.Initialize("run", "player", 300, affluence);
            var morning = LifeRules.PrepareWorkIncome(original, 1, "morning", "am");
            var afternoon = LifeRules.PrepareWorkIncome(morning.Candidate, 1, "afternoon", "pm");
            Assert.That(morning.Status, Is.EqualTo(LifePreparationStatus.Prepared));
            Assert.That(afternoon.Status, Is.EqualTo(LifePreparationStatus.Prepared));
            Assert.That(morning.Receipt.Amount, Is.EqualTo(50 + affluence));
            Assert.That(afternoon.Candidate.Wallet.Balance, Is.EqualTo(300 + 100 + 2 * affluence));
            Assert.That(afternoon.Candidate.WorkIncomeReceipts.Length, Is.EqualTo(2));
            Assert.That(afternoon.Candidate.HasLastIncome, Is.False);
            Assert.That(original.Wallet.Balance, Is.EqualTo(300));
            Assert.That(original.WorkIncomeReceipts, Is.Empty);
        }

        [Test]
        public void EachHalfUsesExecutionAffluenceAndReplayKeepsOriginalAmount()
        {
            var morning = LifeRules.PrepareWorkIncome(LifeRules.Initialize("run", "player", 0, 10), 1, "morning", "am");
            var changed = LifeRules.PrepareAffluence(morning.Candidate, 30);
            var replay = LifeRules.PrepareWorkIncome(changed, 1, "morning", "am");
            Assert.That(replay.Status, Is.EqualTo(LifePreparationStatus.AlreadyApplied));
            Assert.That(replay.Receipt.Amount, Is.EqualTo(60));
            Assert.That(replay.Receipt.AffluenceAtExecution, Is.EqualTo(10));
            Assert.That(replay.Candidate.Revision, Is.EqualTo(changed.Revision));
            var afternoon = LifeRules.PrepareWorkIncome(replay.Candidate, 1, "afternoon", "pm");
            Assert.That(afternoon.Receipt.Amount, Is.EqualTo(80));
            Assert.That(afternoon.Candidate.Wallet.Balance, Is.EqualTo(140));
        }

        [TestCase("morning")][TestCase("afternoon")]
        public void AnotherEventCannotClaimSameDayAndAnchor(string anchor)
        {
            var paid = LifeRules.PrepareWorkIncome(LifeRules.Initialize("run", "player", 0, 0), 1, anchor, "first");
            var duplicate = LifeRules.PrepareWorkIncome(paid.Candidate, 1, anchor, "different");
            Assert.That(duplicate.Status, Is.EqualTo(LifePreparationStatus.WorkAlreadyCompleted));
            Assert.That(duplicate.SettlementKey, Is.EqualTo(paid.SettlementKey));
            Assert.That(duplicate.Receipt.EventInstanceId, Is.EqualTo("first"));
            Assert.That(duplicate.Candidate.Wallet.Balance, Is.EqualTo(50));
            Assert.That(duplicate.Candidate.Revision, Is.EqualTo(paid.Candidate.Revision));
        }

        [Test]
        public void MissingWorkDoesNotAccrueAndNextWorkDayReplacesBoundedReceipts()
        {
            var day1 = LifeRules.PrepareWorkIncome(LifeRules.Initialize("run", "player", 0, 0), 1, "afternoon", "d1pm").Candidate;
            var day2 = LifeRules.StartNutritionDay(day1, 2);
            var day3 = LifeRules.StartNutritionDay(day2, 3);
            Assert.That(day3.Wallet.Balance, Is.EqualTo(50), "No catch-up income for a missed half/day");
            Assert.That(day3.WorkIncomeReceipts[0].Day, Is.EqualTo(1));
            var next = LifeRules.PrepareWorkIncome(day3, 3, "morning", "d3am");
            Assert.That(next.Candidate.Wallet.Balance, Is.EqualTo(100));
            Assert.That(next.Candidate.WorkIncomeReceipts.Length, Is.EqualTo(1));
            Assert.That(next.Receipt.Day, Is.EqualTo(3));
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareWorkIncome(next.Candidate, 1, "afternoon", "d1pm"));
        }

        [Test]
        public void ActorAndRunScopesHaveIndependentKeysAndPayments()
        {
            var player = LifeRules.PrepareWorkIncome(LifeRules.Initialize("run", "player", 0, 0), 1, "morning", "event");
            var npc = LifeRules.PrepareWorkIncome(LifeRules.Initialize("run", "coach", 0, 0), 1, "morning", "event");
            var other = LifeRules.PrepareWorkIncome(LifeRules.Initialize("other", "player", 0, 0), 1, "morning", "event");
            Assert.That(npc.Candidate.Wallet.Balance, Is.EqualTo(50));
            Assert.That(npc.SettlementKey, Is.Not.EqualTo(player.SettlementKey));
            Assert.That(other.SettlementKey, Is.Not.EqualTo(player.SettlementKey));
            npc.Candidate.WorkIncomeReceipts[0].ActorId = "player";
            Assert.Throws<ArgumentException>(() => LifeRules.NormalizeAfterLoad(npc.Candidate));
        }

        [Test]
        public void LegacyWholeDayPaymentSuppressesBothNewHalvesButNotNextDay()
        {
            var old = LifeRules.PrepareIncome(LifeRules.Initialize("run", "player", 0, 10), 1).Candidate;
            foreach (string anchor in new[] { "morning", "afternoon" })
            {
                var blocked = LifeRules.PrepareWorkIncome(old, 1, anchor, "new-event");
                Assert.That(blocked.Status, Is.EqualTo(LifePreparationStatus.LegacyIncomeAlreadyApplied));
                Assert.That(blocked.Receipt, Is.Null);
                Assert.That(blocked.Candidate.WorkIncomeReceipts, Is.Empty);
                Assert.That(blocked.Candidate.Wallet.Balance, Is.EqualTo(120));
                Assert.That(blocked.Candidate.LastIncome.SettlementKey, Is.EqualTo(old.LastIncome.SettlementKey));
                Assert.That(blocked.Candidate.Revision, Is.EqualTo(old.Revision));
            }
            var next = LifeRules.PrepareWorkIncome(LifeRules.StartNutritionDay(old, 2), 2, "morning", "d2");
            Assert.That(next.Status, Is.EqualTo(LifePreparationStatus.Prepared));
            Assert.That(next.Candidate.Wallet.Balance, Is.EqualTo(180));
            Assert.That(next.Candidate.LastIncome.Amount, Is.EqualTo(120));
        }

        [Test]
        public void MissingArrayNormalizesAndEveryCandidateDeepCopiesWorkReceipts()
        {
            var old = LifeRules.Initialize("run", "player", 0, 0); old.WorkIncomeReceipts = null;
            Assert.That(LifeRules.NormalizeAfterLoad(old).WorkIncomeReceipts, Is.Empty);
            Assert.That(old.WorkIncomeReceipts, Is.Null);
            var paid = LifeRules.PrepareWorkIncome(old, 1, "morning", "am");
            paid.Receipt.Amount = 999;
            Assert.That(paid.Candidate.WorkIncomeReceipts[0].Amount, Is.EqualTo(50));
            var copy = LifeRules.PrepareMeal(paid.Candidate, MealSlot.Breakfast, "skip", "breakfast").Candidate;
            copy.WorkIncomeReceipts[0].EventInstanceId = "changed";
            Assert.That(paid.Candidate.WorkIncomeReceipts[0].EventInstanceId, Is.EqualTo("am"));
            var normalized = LifeRules.NormalizeAfterLoad(paid.Candidate);
            normalized.WorkIncomeReceipts[0].Amount = 999;
            Assert.That(paid.Candidate.WorkIncomeReceipts[0].Amount, Is.EqualTo(50));
        }

        [TestCase("evening")][TestCase("Morning")][TestCase("")][TestCase(null)]
        public void OnlyTwoExactAnchorsAreAccepted(string anchor)
        {
            var state = LifeRules.Initialize("run", "player", 0, 0);
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareWorkIncome(state, 1, anchor, "event"));
            Assert.That(state.Wallet.Balance, Is.Zero);
        }

        [Test]
        public void InvalidDayEventAndOverflowLeaveSourceUnchanged()
        {
            var state = LifeRules.Initialize("run", "player", long.MaxValue, 0);
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareWorkIncome(state, 2, "morning", "event"));
            Assert.Throws<ArgumentException>(() => LifeRules.PrepareWorkIncome(state, 1, "morning", " "));
            Assert.Throws<OverflowException>(() => LifeRules.PrepareWorkIncome(state, 1, "morning", "event"));
            Assert.That(state.Wallet.Balance, Is.EqualTo(long.MaxValue));
            Assert.That(state.Revision, Is.Zero); Assert.That(state.WorkIncomeReceipts, Is.Empty);
        }

        [Test]
        public void TamperedReceiptFormulaDuplicateSlotsAndMixedDatesAreRejected()
        {
            var state = LifeRules.PrepareWorkIncome(LifeRules.Initialize("run", "player", 0, 10), 1, "morning", "am").Candidate;
            var formula = LifeRules.NormalizeAfterLoad(state); formula.WorkIncomeReceipts[0].Amount++;
            Assert.Throws<ArgumentException>(() => LifeRules.NormalizeAfterLoad(formula));
            var duplicate = LifeRules.NormalizeAfterLoad(state);
            duplicate.WorkIncomeReceipts = new[] { duplicate.WorkIncomeReceipts[0], duplicate.WorkIncomeReceipts[0] };
            Assert.Throws<ArgumentException>(() => LifeRules.NormalizeAfterLoad(duplicate));
            var day2 = LifeRules.StartNutritionDay(state, 2);
            var second = LifeRules.PrepareWorkIncome(day2, 2, "afternoon", "d2pm").Candidate;
            second.WorkIncomeReceipts = new[] { state.WorkIncomeReceipts[0], second.WorkIncomeReceipts[0] };
            Assert.Throws<ArgumentException>(() => LifeRules.NormalizeAfterLoad(second));
        }

#if UNITY_EDITOR
        [Test]
        public void LegacyJsonMissingArrayAndNewJsonReplayRemainCompatible()
        {
            var initial = LifeRules.Initialize("run", "player", 0, 10);
            string legacyJson = UnityEngine.JsonUtility.ToJson(initial).Replace(",\"WorkIncomeReceipts\":[]", "");
            Assert.That(legacyJson, Does.Not.Contain("WorkIncomeReceipts"));
            var old = LifeRules.NormalizeAfterLoad(UnityEngine.JsonUtility.FromJson<LifeActorState>(legacyJson));
            Assert.That(old.WorkIncomeReceipts, Is.Empty);
            var paid = LifeRules.PrepareWorkIncome(old, 1, "morning", "am");
            var loaded = LifeRules.NormalizeAfterLoad(UnityEngine.JsonUtility.FromJson<LifeActorState>(UnityEngine.JsonUtility.ToJson(paid.Candidate)));
            var replay = LifeRules.PrepareWorkIncome(loaded, 1, "morning", "am");
            Assert.That(replay.Status, Is.EqualTo(LifePreparationStatus.AlreadyApplied));
            Assert.That(replay.Candidate.Wallet.Balance, Is.EqualTo(60));
            Assert.That(replay.Receipt.EventInstanceId, Is.EqualTo("am"));
        }
#endif
    }
}
