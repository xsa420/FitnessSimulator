using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class UsabilityApplicationBoundaryTests
    {
        private string directory;
        private GameSession session;
        [SetUp] public void Open()
        {
            directory = Path.Combine(Path.GetTempPath(), "Fitness-Usability-Boundary", Guid.NewGuid().ToString("N"));
            session = GameSession.OpenTest(directory, 1);
        }
        [TearDown] public void Close() { session?.Dispose(); TestContext.WriteLine(directory); }
        private string Root() => JsonUtility.ToJson(session.InspectSnapshot());
        private FrontendReply Send(string id, string value = null)
        {
            var view = session.ReadView();
            return session.Execute(new FrontendAction { ActionId = id, StringValue = value,
                OperationId = Guid.NewGuid().ToString("N"), RunId = view.RunId, ExpectedRevision = view.Revision });
        }

        [TestCase("morning")]
        [TestCase("afternoon")]
        [TestCase("early")]
        [TestCase("late")]
        public void NewPlanRejectsLegacyOrLockedAnchorWithoutReplacingRoot(string anchor)
        {
            Assert.That(Send("new-game", TemplateCatalog.DefaultId).Accepted, Is.True);
            var before = Root();
            var draft = PlanCatalog.CreateBenchPressSlice(); draft.Slots[0].AnchorId = anchor;
            Assert.That(Send("save-plan", JsonUtility.ToJson(draft)).Accepted, Is.False);
            Assert.That(Root(), Is.EqualTo(before));
            Assert.That(session.ReadView().ScreenId, Is.EqualTo("InitialPlan"));
        }

        [Test] public void DaytimeForgedPlanAndInvalidMealDoNotWriteAndSkipIsRemembered()
        {
            Assert.That(Send("new-game", TemplateCatalog.DefaultId).Accepted, Is.True);
            var plan = PlanCatalog.CreateBenchPressSlice();
            Assert.That(Send("save-plan", JsonUtility.ToJson(plan)).Accepted, Is.True);
            for (int i = 0; i < 2000 && !session.ReadView().Actions.Any(a => a.Id == "meal" && a.Enabled); i++) session.Advance(0);
            Assert.That(session.ReadView().Actions.Any(a => a.Id == "meal" && a.Enabled), Is.True);
            Assert.That(session.ReadView().Actions.Any(a => a.Id == "save-plan"), Is.False);
            string before = Root();
            Assert.That(Send("save-plan", JsonUtility.ToJson(plan)).Accepted, Is.False);
            Assert.That(Root(), Is.EqualTo(before));
            Assert.That(Send("meal", "invalid-food").Accepted, Is.False);
            Assert.That(Root(), Is.EqualTo(before));
            var reply = Send("meal", "skip");
            Assert.That(reply.Accepted, Is.True); Assert.That(reply.Message, Does.Contain("不吃").And.Contain("未扣款"));
            var state = JsonUtility.FromJson<SessionState>(session.InspectSnapshot().FindFragment("session", "player").Json);
            Assert.That(state.LastBreakfastOption, Is.EqualTo("skip"));
            Assert.That(string.IsNullOrEmpty(state.LastLunchOption), Is.True);
            Assert.That(string.IsNullOrEmpty(state.LastDinnerOption), Is.True);
            string saved = Root(); session.Dispose(); session = GameSession.OpenTest(directory, 999);
            Assert.That(Root(), Is.EqualTo(saved));
        }

        [TestCase("morning")]
        [TestCase("afternoon")]
        public void WorkIsAReachableBaselineOnlyOnNonRestDays(string anchor)
        {
            // Configuration/selection test, not a claimed natural multi-day route.
            var definition = SessionContent.Build(anchor, null, false, restDay: false);
            Assert.That(definition.DefaultEntry.DefinitionId, Is.EqualTo("default-work"));
            Assert.That(definition.Entries.Single(e => e.DefinitionId == "ordinary-work").Weight, Is.EqualTo(25));
            Assert.That(definition.Entries.Any(e => e.DefinitionId == "ordinary-walk"), Is.False);
            var flow = new FlowState { RandomState = 1 };
            FlowEngine.ResolveAnchor(flow, definition, "player", 1, anchor, 1, "test-work", _ => true, _ => true);
            Assert.That(definition.Nodes.Single(n => n.Id == FlowEngine.Current(flow).NodeId).Handler, Is.EqualTo("work"));
            var rest = SessionContent.Build(anchor, null, false, new SessionState(), new ActivityState(), true);
            Assert.That(rest.Nodes.Any(n => n.Handler == "work"), Is.False);
        }
    }
}
