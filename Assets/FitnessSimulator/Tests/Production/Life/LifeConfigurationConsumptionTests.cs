using System;
using System.Linq;
using FitnessSimulator.Game.Actors;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class LifeConfigurationConsumptionTests
    {
        private static ContentPackage Changed<T>(string id, Action<T> change) where T : class
        {
            var package = ContentRuntime.Current;
            var definition = package.Get<T>(id);
            change(definition);
            var documents = package.Export().Documents.ToDictionary(d => d.Id, d => d.Json);
            documents[id] = JsonUtility.ToJson(definition);
            return ContentPackage.FromDocuments(documents);
        }

        [Test]
        public void SocialCurveChangesFrozenOutcomeAndOnlyEligiblePositiveRewards()
        {
            var body = ActorPolicies.Initialize("r", RelationshipRules.Rival).Body;
            body.BaseCharmMicro = 100000000;
            body.MentalStressMicro = 0;
            var before = SocialRules.FreezeCheck("social", body, 400000);
            Assert.That(SocialRules.Resolve(before, 2147483648u).Success, Is.True);
            using (ContentRuntime.Push(Changed<RelationshipContentDocument>("relationships", d => {
                d.Social.CharmSqrtMillionths = 0;
                d.Social.PositiveLinearMillionths = 250000;
            }))) {
                RelationshipContent.ValidateContent();
                var after = SocialRules.FreezeCheck("social", body, 400000);
                Assert.That(SocialRules.Resolve(after, 2147483648u).Success, Is.False);
                Assert.That(SocialRules.Resolve(before, 2147483648u).Success, Is.True);
                string eligible = RelationshipContent.Definition.Social.ScalableEffectKinds[0];
                Assert.That(SocialRules.ScalePositiveEffect(body, eligible, 100, true), Is.EqualTo(125));
                Assert.That(SocialRules.ScalePositiveEffect(body, "fixedAffection", 4, false), Is.EqualTo(4));
                Assert.That(SocialRules.ScalePositiveEffect(body, "stress", -10, true), Is.EqualTo(-10));
                Assert.Throws<ArgumentException>(() => SocialRules.ScalePositiveEffect(body, "fixedAffection", 4, true));
            }
        }

        [Test]
        public void ConfiguredDialogueReturnsBothSuccessorsAndConsumesOnlyOneOpportunity()
        {
            string target = RelationshipRules.Coach;
            var original = RelationshipContent.Character(target);
            string response = original.Dialogues[1].ResponseEventId;
            using (ContentRuntime.Push(Changed<RelationshipContentDocument>("relationships", d => {
                var character = d.Characters.Single(c => c.CharacterId == target);
                character.Dialogues[0].FixedDelta = 7;
                character.Dialogues[0].ResponseEventId = response;
            }))) {
                RelationshipContent.ValidateContent();
                var state = RelationshipRules.Initialize("r", "p");
                state.Affections.Single(a => a.CharacterId == target).Value = 15;
                var workout = RelationshipRules.OpenWorkout(state, "w", new[] { target });
                var context = new EconomyContext { RunId="r", ActorId="p", Day=1, EventInstanceId="rest", TransactionOrdinal=1 };
                var result = RelationshipRules.PrepareDialogue(state, workout, context, target, 0, true);
                Assert.That(result.AppliedAffectionDelta, Is.EqualTo(7));
                Assert.That(result.ResponseEventId, Is.EqualTo(response));
                Assert.That(result.FamiliarResponseEventId, Is.EqualTo(original.FamiliarResponseEventId));
                Assert.That(result.Interaction.Consumed, Is.True);
                Assert.That(RelationshipRules.PrepareDialogue(result.Relationships, result.Interaction, context, target, 0, true).Status,
                    Is.Not.EqualTo(EconomyStatus.Prepared));
                Assert.That(state.Affections.Single(a => a.CharacterId == target).Value, Is.EqualTo(15));
            }
        }

        private static ShopState Refresh(string shopId, string actor)
        {
            var state = ShopRules.Initialize("r", actor, shopId);
            return ShopRules.PrepareRefresh(state, 1, new RandomSamples {
                RunId="r", ActorId=actor, ShopId=shopId, Purpose="ShopRefresh", AlgorithmVersion="ConfigSamples.v1", Values=new uint[]{0,0,0}
            }).Shop;
        }

        [Test]
        public void ShopPoolAndPackageChangesRemainPrivateToShopAndActor()
        {
            var cornerBefore = Refresh(EconomyContent.Corner, "rival");
            using (ContentRuntime.Push(Changed<EconomyContentDocument>("economy", d => {
                var shop = d.Shops.Single(s => s.Id == EconomyContent.Gym);
                var first = shop.Products.Single(p => p.Product.Id == shop.InitiallyUnlockedProductIds[0]);
                first.Product.UnitsPerPackage = 2;
                first.Product.Price = 17;
                shop.Products = new[] { first };
                shop.InitiallyUnlockedProductIds = new[] { first.Product.Id };
                shop.OfferSlots = 3;
            }))) {
                EconomyContent.ValidateContent();
                var gym = Refresh(EconomyContent.Gym, "rival");
                var other = Refresh(EconomyContent.Gym, "beginner");
                var corner = Refresh(EconomyContent.Corner, "rival");
                Assert.That(gym.Offers.Length, Is.EqualTo(1));
                Assert.That(corner.Offers.Select(o => o.ProductId), Is.EqualTo(cornerBefore.Offers.Select(o => o.ProductId)));
                var actor = ActorPolicies.Initialize("r", "rival");
                var quote = ShopRules.QuotePurchase(actor.Life, actor.Inventory, gym, gym.Offers[0].ProductId, 1);
                var purchase = ShopRules.PreparePurchase(actor.Life, actor.Inventory, gym, quote,
                    new EconomyContext { RunId="r", ActorId="rival", Day=1, EventInstanceId="shop", TransactionOrdinal=1 });
                Assert.That(purchase.Status, Is.EqualTo(EconomyStatus.Prepared));
                Assert.That(purchase.Life.Wallet.Balance, Is.EqualTo(actor.Life.Wallet.Balance - 17));
                Assert.That(InventoryRules.Quantity(purchase.Inventory, EconomyContent.Notebook), Is.EqualTo(2));
                Assert.That(other.Offers[0].PackagesRemaining, Is.EqualTo(gym.Offers[0].PackagesRemaining));
                Assert.That(purchase.Shop.Offers[0].PackagesRemaining, Is.EqualTo(other.Offers[0].PackagesRemaining - 1));
            }
        }

        [Test]
        public void ActorInitialAndDecisionFieldsChangeOnlyTargetActor()
        {
            var before = ActorPolicies.Initialize("r", "rival");
            using (ContentRuntime.Push(Changed<ActorContentDocument>("actors", d => {
                var character = d.Characters.Single(c => c.Id == "rival");
                d.InitialStates.Single(s => s.Id == character.InitialStateRef).Balance = 777;
                var policy = d.Policies.Single(p => p.Id == character.PolicyRef);
                policy.StudyNumerator = policy.StudyDenominator;
                policy.WantedNotebookUnits = 2;
                policy.NormalRestSeconds = 90;
            }))) {
                ActorContent.ValidateContent();
                Assert.That(ActorPolicies.Initialize("r", "rival").Life.Wallet.Balance, Is.EqualTo(777));
                Assert.That(ActorPolicies.Initialize("r", "beginner").Life.Wallet.Balance, Is.EqualTo(450));
                Assert.That(before.Life.Wallet.Balance, Is.EqualTo(480));
                var choices = new[] { new ActorEventOption { EventId="rest", Kind=ActorEventKind.Ordinary }, new ActorEventOption { EventId="study", Kind=ActorEventKind.AllDayStudy } };
                Assert.That(ActorPolicies.ChooseEvent("rival", choices, 0, true, uint.MaxValue).EventId, Is.EqualTo("study"));
                Assert.That(ActorPolicies.ChooseEvent("beginner", choices, 0, true, uint.MaxValue).EventId, Is.EqualTo("rest"));
                Assert.That(ActorPolicies.ChooseNotebookPackage("rival", Refresh(EconomyContent.Gym, "rival"), 777, 0), Is.Empty);
            }
        }

        [TestCase(true, 0)]
        [TestCase(true, 31)]
        [TestCase(false, -1)]
        [TestCase(false, 181)]
        public void ActorRestPolicyRejectsDurationsOutsideConfiguredTrainingTiers(bool normal, int seconds)
        {
            using (ContentRuntime.Push(Changed<ActorContentDocument>("actors", d => {
                if (normal) d.Policies[0].NormalRestSeconds = seconds;
                else d.Policies[0].ExtraRestSeconds = seconds;
            }))) {
                var error = Assert.Throws<ArgumentException>(() => ActorContent.ValidateContent());
                Assert.That(error.Message, Does.Contain(normal ? ".NormalRestSeconds" : ".ExtraRestSeconds"));
            }
        }
    }
}
