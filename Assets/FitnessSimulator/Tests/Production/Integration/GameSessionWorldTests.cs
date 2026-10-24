using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class GameSessionWorldTests
    {
        private GameSession session;
        private string directory;
        private int operation;
        [SetUp] public void Open() {
            directory = Path.Combine(Path.GetTempPath(), "Fitness-M2-World", Guid.NewGuid().ToString("N"));
            session = GameSession.OpenAutomation(directory, 1);
        }
        [TearDown] public void Close() {
            if (session != null) {
                File.WriteAllText(Path.Combine(directory, "world-final.json"), JsonUtility.ToJson(session.InspectSnapshot(), true));
                File.WriteAllText(Path.Combine(directory, "diagnostic.txt"), session.Diagnostic ?? "");
                TestContext.WriteLine("World evidence: " + directory); session.Dispose();
            }
        }
        private T Read<T>(string owner, string actor = "player") => JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner, actor).Json);
        private FrontendAction Send(string id, string value = null, int number = 0) {
            var view = session.ReadView();
            var command = new FrontendAction { OperationId = "world-test/" + (++operation), RunId = view.RunId, ExpectedRevision = view.Revision, ActionId = id, StringValue = value, IntValue = number };
            var reply = session.Execute(command); Assert.That(reply.Accepted, Is.True, id + ": " + reply.Message); return command;
        }
        private void StartRestRun() {
            Send("new-game", TemplateCatalog.DefaultId);
            Send("save-plan", JsonUtility.ToJson(new PlanDraft { Mode = PlanMode.DayCycle, Slots = new[] { PlanCatalog.Rest(0) } }));
        }
        private void Reopen() {
            string before = JsonUtility.ToJson(session.InspectSnapshot());
            session.Dispose(); session = GameSession.OpenAutomation(directory, 999);
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()), Is.EqualTo(before)); Send("continue-game");
        }

        [Test] public void RealShopStudyAndFourActorsReachDayBoundaryWithoutInjectedGrowth()
        {
            StartRestRun(); bool purchased = false, used = false, learned = false; int paidMeals = 0, days = 0;
            for (int step = 0; step < 20000 && days < 14; step++) {
                var view = session.ReadView(); Assert.That(view.ScreenId, Is.Not.EqualTo("error"), view.Body + " / " + session.Diagnostic);
                var options = view.Actions.Where(a => a.Enabled).ToArray(); FrontendOption choice = null;
                if (options.Any(a => a.Id == "next-day")) {
                    days++;
                    foreach (string id in new[] { "player", "rival", "beginner", "veteran", "coach" }) {
                        var state = Read<SessionState>("session", id);
                        Assert.That(state.Day, Is.EqualTo(days), id); Assert.That(state.Phase, Is.EqualTo("AwaitNextDay"), id);
                    }
                    Reopen();
                    if (purchased && used && learned && days >= 3) break;
                    choice = options.First(a => a.Id == "next-day");
                } else if (options.Any(a => a.Id == "meal")) {
                    // Real paid menu option; no wallet or nutrition fixture is injected.
                    choice = options.Where(a => a.Id == "meal" && a.StringValue != "skip").LastOrDefault();
                    Assert.That(choice, Is.Not.Null, "Normal meal route ran out of funds"); paidMeals++;
                } else if (options.Any(a => a.Id == "shop-buy") && Read<InventoryState>("inventory").Items.Single(i => i.ItemId == EconomyContent.Notebook).Quantity == 0) {
                    choice = options.First(a => a.Id == "shop-buy");
                } else if (options.Any(a => a.Id == "shop-leave")) choice = options.First(a => a.Id == "shop-leave");
                else if (options.Any(a => a.Id == "item-use")) choice = options.First(a => a.Id == "item-use");
                else if (options.Any(a => a.Id == "choose-entry")) choice = options.First(a => a.Id == "choose-entry");
                else if (options.Any(a => a.Id == "continue")) choice = options.First(a => a.Id == "continue");
                if (choice == null) { session.Advance(0); continue; }
                bool study = view.ScreenId.StartsWith("study-all/", StringComparison.Ordinal);
                long beforeBooks = Read<InventoryState>("inventory").Items.Single(i => i.ItemId == EconomyContent.Notebook).Quantity;
                var command = Send(choice.Id, choice.StringValue, choice.IntValue);
                if (choice.Id == "shop-buy") {
                    purchased = true;
                    long balance = Read<LifeActorState>("life").Wallet.Balance;
                    long count = Read<InventoryState>("inventory").Items.Single(i => i.ItemId == EconomyContent.Notebook).Quantity;
                    int time = Read<SessionState>("session").AnchorGameSeconds;
                    Reopen();
                    command.OperationId += "/new-id";
                    Assert.That(session.Execute(command).Accepted, Is.True, "Same purchase must find its semantic receipt before stale revision");
                    Assert.That(Read<LifeActorState>("life").Wallet.Balance, Is.EqualTo(balance));
                    Assert.That(Read<InventoryState>("inventory").Items.Single(i => i.ItemId == EconomyContent.Notebook).Quantity, Is.EqualTo(count));
                    Assert.That(Read<SessionState>("session").AnchorGameSeconds, Is.EqualTo(time)); Assert.That(time, Is.EqualTo(300));
                }
                if (choice.Id == "item-use" && beforeBooks > Read<InventoryState>("inventory").Items.Single(i => i.ItemId == EconomyContent.Notebook).Quantity) used = true;
                if (study) {
                    learned = true;
                    Assert.That(Read<BodyState>("body").Exercises.All(e => e.KnowledgeMicro >= 12000000), Is.True);
                    Assert.That(Read<SessionState>("session").AnchorGameSeconds, Is.EqualTo(7200));
                }
            }
            Assert.That(purchased && used && learned, Is.True, "CoverageMissing: shop, notebook and all-day study must all occur within D14");
            Assert.That(paidMeals, Is.EqualTo(days * 3), "All-day study must preserve three meal anchors");
            Assert.That(new[] { "rival", "beginner", "veteran", "coach" }.Any(id => Read<SessionState>("session", id).HasEffectiveTraining), Is.True, "NPC policy must complete at least one real effective set");
            Assert.That(Read<SessionState>("session").HasEffectiveTraining, Is.False, "NPC growth must not mark the player's rest plan trained");
        }
    }
}
