using System;
using NUnit.Framework;
using UnityEngine;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class RelationshipBoundaryTests
    {
        static EconomyContext Context()=>new EconomyContext { RunId="r",ActorId="p",Day=1,EventInstanceId="e",TransactionOrdinal=1 };
        static T RoundTrip<T>(T value)=>JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        [Test] public void GiftRequiresFullConditionsAndDeclineRequiresAnotherGift()
        {
            var s=RelationshipRules.Initialize("r","p");s.Affections[1].Value=99;
            var w=RelationshipRules.OpenWorkout(s,"w",new[]{RelationshipRules.Beginner});
            var inv=InventoryRules.Initialize("r","p");inv.Items[1].Quantity=1;
            Assert.That(RelationshipRules.PrepareGift(s,w,inv,Context(),true).Status,Is.EqualTo(EconomyStatus.ConditionNotMet));
            s.Affections[1].Value=100;inv.Items[1].Quantity=0;
            var missing=RelationshipRules.PrepareGift(s,w,inv,Context(),true);Assert.That(missing.Status,Is.EqualTo(EconomyStatus.InsufficientItems));Assert.That(missing.Interaction.Consumed,Is.False);
            inv.Items[1].Quantity=1;s.NextConfessionDay=2;
            Assert.That(RelationshipRules.PrepareGift(s,w,inv,Context(),true).Status,Is.EqualTo(EconomyStatus.ConditionNotMet));
            s.NextConfessionDay=1;var gift=RelationshipRules.PrepareGift(s,w,inv,Context(),true);
            Assert.That(gift.Delivery.Inventory.Items[1].Quantity,Is.Zero);Assert.That(gift.Interaction.Consumed,Is.True);Assert.That(gift.Relationships.PendingConfessionId,Is.Not.Empty);
            Assert.That(inv.Items[1].Quantity,Is.EqualTo(1));
            var declined=ConfessionRules.Decline(gift.Relationships);Assert.That(declined.PendingConfessionId,Is.Empty);
            Assert.Throws<ArgumentException>(()=>ConfessionRules.Freeze(declined,500000,600000,18000000));
            Assert.That(RelationshipRules.PrepareGift(declined,gift.Interaction,gift.Delivery.Inventory,Context(),true).Status,Is.EqualTo(EconomyStatus.OpportunityClosed));
        }
        [Test] public void CoachBuddyAndInvalidRestCannotChargeAgain()
        {
            var s=RelationshipRules.Initialize("r","p");s.CurrentBuddy=RelationshipRules.Coach;
            var w=RelationshipRules.OpenWorkout(s,"w",new[]{RelationshipRules.Coach});var life=LifeRules.Initialize("r","p",300,0);
            Assert.That(RelationshipRules.PrepareTeaching(s,w,life,Context(),true,1,"c").Status,Is.EqualTo(EconomyStatus.ConditionNotMet));
            s.CurrentBuddy="";
            Assert.That(RelationshipRules.PrepareTeaching(s,w,life,Context(),false,1,"c").Payment,Is.Null);
            var absent=RelationshipRules.OpenWorkout(s,"w",new string[0]);Assert.That(RelationshipRules.PrepareTeaching(s,absent,life,Context(),true,1,"c").Payment,Is.Null);
            var paid=RelationshipRules.PrepareTeaching(s,w,life,Context(),true,2,"c");
            Assert.That(RelationshipRules.PrepareTeaching(paid.Relationships,paid.Interaction,paid.Payment.Life,Context(),true,3,"d").Status,Is.EqualTo(EconomyStatus.OpportunityClosed));
            Assert.That(life.Wallet.Balance,Is.EqualTo(300));
        }
        [Test] public void AdjacentUintBoundaryAndStaleSnapshotAreRejectedPrecisely()
        {
            var s=RelationshipRules.Initialize("r","p");s.Affections[1].Value=100;s.PendingConfessionId="gift";
            var snap=ConfessionRules.Freeze(s,500000,600000,18000000);
            uint firstFailure=(uint)(((ulong)710*4294967296UL+999)/1000);
            Assert.That(ConfessionRules.Prepare(s,snap,firstFailure-1).Receipt.Success,Is.True);
            Assert.That(ConfessionRules.Prepare(s,snap,firstFailure).Receipt.Success,Is.False);
            Assert.That(ConfessionRules.Freeze(s,1000000,1000000,18000000).ProbabilityThousandths,Is.EqualTo(950));
            s.Revision++;Assert.Throws<ArgumentException>(()=>ConfessionRules.Prepare(s,snap,0));
        }
        [Test] public void ActualSerializationPreservesPendingGiftAndTeachingAndRejectsActorMismatch()
        {
            var s=RelationshipRules.Initialize("r","p");s.Affections[3].Value=100;
            var w=RelationshipRules.OpenWorkout(s,"w",new[]{RelationshipRules.Coach});
            var paid=RelationshipRules.PrepareTeaching(s,w,LifeRules.Initialize("r","p",0,0),Context(),true,2,"c");
            var restored=RelationshipRules.NormalizeAfterLoad(RoundTrip(paid.Relationships));var interaction=RoundTrip(paid.Interaction);
            Assert.That(RelationshipRules.FreezeBenefits(restored,interaction,"snapshot",2,"presence").TeachingSources[0].NominalTargetMicro,Is.EqualTo(3000000));
            interaction.ActorId="other";Assert.Throws<ArgumentException>(()=>RelationshipRules.FreezeBenefits(restored,interaction,"snapshot",2,"presence"));
            s.Affections[1].Value=100;s.PendingConfessionId="gift";
            var snap=RoundTrip(ConfessionRules.Freeze(s,500000,600000,18000000));
            Assert.That(ConfessionRules.Prepare(RelationshipRules.NormalizeAfterLoad(RoundTrip(s)),snap,0).Receipt.Success,Is.True);
            var ledger=TeachingLedgerRules.Initialize("r","p",1,"w");ledger.Awards=new[]{new TeachingAward { ExerciseId="bench_press",NominalMicro=3000000 }};
            Assert.That(TeachingLedgerRules.NormalizeAfterLoad(RoundTrip(ledger)).Awards[0].NominalMicro,Is.EqualTo(3000000));
        }
    }
}
