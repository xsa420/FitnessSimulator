using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Planning;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    // Candidate-contract tests deliberately do not claim a 42-day production NPC trajectory.
    // No synthetic opponent body or successful competition result is inserted into a saved game.
    public sealed class GameSessionCompetitionTests
    {
        private GameSession session;
        [SetUp] public void SetUp()
        {
            session=GameSession.OpenTest(Path.Combine(Path.GetTempPath(),"Fitness-Competition-Integration",Guid.NewGuid().ToString("N")),17);
            Send("new-game",TemplateCatalog.DefaultId);
            var plan=PlanCatalog.CreateBenchPressSlice(); plan.Slots[0]=PlanCatalog.Rest(0);
            Send("save-plan",JsonUtility.ToJson(plan));
        }
        [TearDown] public void TearDown() { session?.Dispose(); }
        private void Send(string id,string value=null)
        {
            var view=session.ReadView(); var result=session.Execute(new FrontendAction { OperationId=Guid.NewGuid().ToString("N"),RunId=view.RunId,ExpectedRevision=view.Revision,ActionId=id,StringValue=value });
            Assert.That(result.Accepted,Is.True,result.Message);
        }
        private object Hook(string method,params object[] arguments)
        {
            try { return typeof(GameSession).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session,arguments); }
            catch(TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        private static T Read<T>(RootEnvelope root,string owner)=>JsonUtility.FromJson<T>(root.FindFragment(owner,"player").Json);
        private static void Write<T>(RootEnvelope root,string owner,T value)=>root.SetFragment(owner,"player",1,root.Revision+1,JsonUtility.ToJson(value));
        private RootEnvelope CandidateAt(int day)
        {
            var root=session.InspectSnapshot(); var state=Read<SessionState>(root,"session"); state.Day=day; state.Phase="Anchors"; state.AnchorIndex=0;
            Write(root,"session",state); Hook("EnsureCompetition",root,"rival"); return root;
        }
        private RootEnvelope OpenCandidate()
        {
            var root=CandidateAt(8); Hook("PrepareCompetitionDayOpen",root,Read<SessionState>(root,"session"),new PreparedOutcome()); return root;
        }
        private PreparedOutcome Action(RootEnvelope root,string id,int kind=0)
        {
            object[] args={root,new FrontendAction { ActionId=id,IntValue=kind },null};
            Assert.That((bool)Hook("TryPrepareCompetitionAction",args),Is.True); return (PreparedOutcome)args[2];
        }
        [Test] public void PublicEarlyEnrollmentCannotMutateTheActualNewRun()
        {
            var before=session.InspectSnapshot(); var view=session.ReadView();
            var rejected=session.Execute(new FrontendAction { OperationId=Guid.NewGuid().ToString("N"),RunId=view.RunId,ExpectedRevision=view.Revision,ActionId="competition-enroll" });
            Assert.That(rejected.Accepted,Is.False); Assert.That(session.InspectSnapshot().Revision,Is.EqualTo(before.Revision));
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(JsonUtility.ToJson(before)));
        }
        [Test] public void CandidateOpeningPersistsDomainRandomOnceWithoutPublishingOrInventingRival()
        {
            var actual=session.InspectSnapshot(); var root=CandidateAt(8); var state=Read<SessionState>(root,"session"); var first=new PreparedOutcome();
            Hook("PrepareCompetitionDayOpen",root,state,first);
            var competition=Read<CompetitionState>(root,"competition"); var random=Read<DomainRandomState>(root,"competition-random");
            Assert.That(competition.Current.Opened,Is.True); Assert.That(competition.Current.Contests.All(c=>c.JudgeIds.Distinct().Count()==3),Is.True);
            Assert.That(first.Appends.Count(a=>a.Kind=="CompetitionJudgeDraw"),Is.EqualTo(1)); Assert.That(random.DrawIndex,Is.GreaterThanOrEqualTo(6));
            string saved=JsonUtility.ToJson(random); var retry=new PreparedOutcome(); Hook("PrepareCompetitionDayOpen",root,state,retry);
            Assert.That(JsonUtility.ToJson(Read<DomainRandomState>(root,"competition-random")),Is.EqualTo(saved)); Assert.That(retry.Appends,Is.Empty);
            Assert.That(root.FindFragment("body","rival")?.Json,Is.EqualTo(actual.FindFragment("body","rival")?.Json)); Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(JsonUtility.ToJson(actual)));
        }
        [Test] public void CandidateEnrollmentAndWithdrawalShareFlowReservationAndNeverEmitAttendance()
        {
            var root=OpenCandidate(); var enroll=Action(root,"competition-enroll");
            var competition=Read<CompetitionState>(root,"competition"); var flow=Read<FlowState>(root,"flow");
            Assert.That(competition.Current.Contests[0].Enrolled,Is.True);
            Assert.That(flow.Occupancies.Single(o=>o.Day==42&&o.AnchorId=="morning").OwnerInstanceId,Is.EqualTo(competition.Current.Contests[0].ContestId+"/reservation"));
            Assert.That(enroll.CheckKind,Is.EqualTo(CheckKind.None)); Assert.That(enroll.Appends,Is.Empty);
            Action(root,"competition-withdraw"); Assert.That(Read<FlowState>(root,"flow").Occupancies.Any(o=>o.Day==42&&o.AnchorId=="morning"),Is.False);
            Assert.That(Read<CompetitionState>(root,"competition").Current.ActualAttendanceCount,Is.Zero);
        }
        [Test] public void CandidateEnrollmentRejectsExistingConsumptionWithoutOverwritingIt()
        {
            var root=OpenCandidate(); var flow=Read<FlowState>(root,"flow");
            flow.Occupancies.Add(new AnchorOccupancy { ActorId="player",Day=42,AnchorId="morning",OwnerInstanceId="existing-consumption" }); Write(root,"flow",flow);
            string before=JsonUtility.ToJson(root);
            Assert.Throws<InvalidOperationException>(()=>Action(root,"competition-enroll")); Assert.That(JsonUtility.ToJson(root),Is.EqualTo(before));
        }
        [Test] public void CandidateFreezeRejectsMissingUncommittedOrStaleWorldInsteadOfCreatingNpc()
        {
            var root=CandidateAt(42); var state=Read<SessionState>(root,"session");
            root.Fragments.RemoveAll(f=>f.Owner=="world"&&f.Scope=="shared"||f.Scope=="rival"&&(f.Owner=="body"||f.Owner=="session"));
            Assert.Throws<InvalidOperationException>(()=>Hook("ReadCompetitionFreezeInput",root,state));
            const string ready="{\"Day\":42,\"Phase\":\"DayOpenReady\",\"BarrierId\":\"test-boundary\"}";
            root.SetFragment("world","shared",1,root.Revision+1,ready);
            Assert.Throws<InvalidOperationException>(()=>Hook("ReadCompetitionFreezeInput",root,state));
            root.SetFragment("world","shared",1,root.Revision,ready.Replace("42","41"));
            Assert.Throws<InvalidOperationException>(()=>Hook("ReadCompetitionFreezeInput",root,state));
            root.SetFragment("world","shared",1,root.Revision,ready);
            Assert.Throws<InvalidOperationException>(()=>Hook("ReadCompetitionFreezeInput",root,state));
            Assert.That(root.FindFragment("body","rival"),Is.Null); Assert.That(Read<CompetitionState>(root,"competition").Current.FrozenActors,Is.Empty);
        }
        [Test] public void CandidateMenuProjectsHiddenSeatsAndClosesWithoutSpendingAnchor()
        {
            var root=OpenCandidate(); var state=Read<SessionState>(root,"session"); Action(root,"competition-menu");
            Assert.That((bool)Hook("IsCompetitionMenuOpen",root),Is.True);
            var view=new FrontendView { CanOpenAuxiliary=true }; var actions=new List<FrontendOption>();
            Assert.That((bool)Hook("TryProjectCompetition",root,state,view,actions),Is.True);
            Assert.That(view.Body,Does.Contain("尚未公布")); Assert.That(view.Body,Does.Not.Contain("衡岳")); Assert.That(actions.Count(a=>a.Id=="competition-enroll"),Is.EqualTo(2));
            Action(root,"competition-close"); Assert.That((bool)Hook("IsCompetitionMenuOpen",root),Is.False);
            Assert.That(Read<SessionState>(root,"session").AnchorIndex,Is.EqualTo(state.AnchorIndex));
        }
        [Test] public void ReadViewCompetitionEarlyReturnWaitsForWorldArrivalAndKeepsMenuAvailable()
        {
            var actual=session.InspectSnapshot(); string saved=JsonUtility.ToJson(actual);
            var root=session.InspectSnapshot(); var state=Read<SessionState>(root,"session");
            state.Day=42; state.Phase="Anchors";
            state.AnchorIndex=Array.IndexOf(SessionContent.Anchors(state),"morning");
            Write(root,"session",state);

            // Schedule-only projection fixture: reuse the actual new-run body values unchanged.
            // These metadata stamps do not assert that the saved game has reached day 42.
            var competition=CompetitionRules.Open(CompetitionRules.Create(root.RunId,"player","rival"),8,new[]{0,0,0},new[]{0,0,0}).State;
            competition=CompetitionRules.SetEnrollment(competition,ContestKind.Bodybuilding,8,true,true).State;
            competition=CompetitionRules.AdvanceDisclosure(competition,35).State;
            competition=CompetitionRules.CloseRegistration(competition,41,"AllAnchorsFinishedBeforeDayEnd").State;
            Func<string,CompetitionSourceKind,CompetitionActorSnapshot> snapshot=(actor,kind)=> {
                var body=JsonUtility.FromJson<BodyState>(root.FindFragment("body",actor).Json);
                return new CompetitionActorSnapshot { ActorId=actor,Body=body,ActorRevision=body.Revision,SourceCommitId=actual.LastCommitId,
                    SourceKind=kind,Day=42,Phase=CompetitionContent.FreezePhase };
            };
            competition=CompetitionRules.Freeze(competition,new CompetitionFreezeInput { BarrierId="projection-only/day42",Day=42,
                Phase=CompetitionContent.FreezePhase,Player=snapshot("player",CompetitionSourceKind.PlayerProduction),Rival=snapshot("rival",CompetitionSourceKind.NpcProduction) }).State;
            Write(root,"competition",competition);
            var flow=Read<FlowState>(root,"flow");
            Assert.That((bool)Hook("TryResolveCompetitionAnchor",root,state,flow,new PreparedOutcome()),Is.True);
            FlowEngine.StartCurrent(flow); Write(root,"flow",flow); Write(root,"session",state);

            var world=JsonUtility.FromJson<ApplicationWorldState>(root.FindFragment("world","shared").Json);
            var participants=world.AnchorBarrier.Actors.Select(a=>a.ActorId).ToArray();
            var barrier=WorldBarrier.Create(root.RunId,42,"Anchor","morning",participants,"player",42,new[]{"AnchorComplete"});
            barrier=WorldBarrier.Arrive(barrier,barrier.BarrierId,barrier.ScheduleRevision,"player",ArrivalKind.Skipped,flow.TreeInstanceId).State;
            world.Day=42; world.Phase="Anchors"; world.AnchorIndex=state.AnchorIndex; world.StepIndex=0;
            world.BarrierId=barrier.BarrierId; world.AnchorBarrier=barrier;
            root.SetFragment("world","shared",1,root.Revision+1,JsonUtility.ToJson(world));

            var cache=typeof(GameSession).GetField("readSnapshot",BindingFlags.Instance|BindingFlags.NonPublic);
            var previous=cache.GetValue(session);
            try {
                cache.SetValue(session,root);
                var waiting=session.ReadView();
                Assert.That(waiting.ScreenId,Is.EqualTo("competition"));
                foreach(string id in new[]{"competition-attend","competition-decline"}) {
                    var option=waiting.Actions.Single(a=>a.Id==id);
                    Assert.That(option.Enabled,Is.False,id+" must wait for the world barrier");
                    Assert.That(option.DisabledReason,Is.Not.Null.And.Not.Empty);
                }
                Assert.That(waiting.Actions.Single(a=>a.Id=="open-menu").Enabled,Is.True);
                foreach(string actor in participants.Where(a=>a!="player"))
                    barrier=WorldBarrier.Arrive(barrier,barrier.BarrierId,barrier.ScheduleRevision,actor,ArrivalKind.Skipped,"projection-arrival/"+actor).State;
                barrier=WorldBarrier.BeginInteraction(barrier,barrier.BarrierId,barrier.ScheduleRevision).State;
                world.AnchorBarrier=barrier;
                root.SetFragment("world","shared",1,root.Revision+1,JsonUtility.ToJson(world));
                var ready=session.ReadView();
                Assert.That(ready.ScreenId,Is.EqualTo("competition"));
                foreach(string id in new[]{"competition-attend","competition-decline","open-menu"})
                    Assert.That(ready.Actions.Single(a=>a.Id==id).Enabled,Is.True,id+" must be available after arrivals");
                Assert.That(Read<CompetitionState>(root,"competition").Current.ActualAttendanceCount,Is.Zero);
                Assert.That(Read<CompetitionState>(root,"competition").Current.Contests.All(c=>c.Results.Length==0),Is.True);
                foreach(string actor in new[]{"player","rival"})
                    Assert.That(root.FindFragment("body",actor).Json,Is.EqualTo(actual.FindFragment("body",actor).Json));
            } finally { cache.SetValue(session,previous); }
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(saved),"Read-only projection must not publish its candidate");
        }
    }
}
