using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class GameSessionCompetitionRouteTests
    {
        private const int DriverBudget=250000, RevisionBudget=300000, DefaultWallSeconds=600;
        private int wallSeconds=DefaultWallSeconds;
        private GameSession session;
        private string directory,saveDirectory;
        private StreamWriter trace;
        private Stopwatch watch;
        private RootEnvelope last, freezeSource;
        private long initialRevision;
        private int operation, calls;
        private bool enrolledBody,enrolledThin,menuClosed,morningReopened;
        private bool enrollRoute;
        private RouteReport report;
        [Serializable] private sealed class ItemPayload { public bool Skip=false; }
        [Serializable] private sealed class RouteReport
        {
            public int Seed=1,DriverLimit=DriverBudget,RevisionLimit=RevisionBudget,WallLimitSeconds=DefaultWallSeconds,MinimumWallSeconds=180,MaximumWallSeconds=1200,TimeoutMilliseconds=1260000;
            public bool ExplicitWallBudget;
            public bool NeverEnrolledVerified;
            public string Route;
            public string RequestedWallBudget;
            public int DriverCalls,PublicCommands,PaidMeals,SkippedMeals,Reopens,LastDay;
            public long CommittedRevisions,ElapsedMilliseconds;
            public bool D8Enrollment,D41ClosedWithoutFailure,D42CommittedBarrier,D42FrozenFromBarrier,MorningDeclinedWithoutFailure,UniqueUnfulfilled,NoteCommitted,TerminalCannotContinue,NewRunPreservesHistory;
            public string Status="NotCompleted",Failure="",Directory,OriginalRunId,NewRunId,AchievementId;
        }
        [Serializable] private sealed class TraceRow
        {
            public long ElapsedMilliseconds,Revision;
            public int Calls,Day,AnchorIndex;
            public string Kind,Action,Payload,Screen,RunId,Phase,Message;
            public int IntValue;
        }
        [SetUp] public void Open()
        {
            session=null; trace=null; last=null; freezeSource=null; initialRevision=0; operation=0; calls=0;
            enrolledBody=false; enrolledThin=false; menuClosed=false; morningReopened=false; enrollRoute=true;
            directory=Path.Combine(Path.GetTempPath(),"Fitness-Competition-42Day",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            saveDirectory=Path.Combine(directory,"save");
            report=new RouteReport { Directory=directory };
            watch=Stopwatch.StartNew();
            string requested=Environment.GetEnvironmentVariable("FITNESS_COMPETITION_ROUTE_SECONDS");
            report.RequestedWallBudget=requested; report.ExplicitWallBudget=requested!=null;
            wallSeconds=DefaultWallSeconds;
            if(requested!=null) {
                if(!int.TryParse(requested,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out wallSeconds)||wallSeconds<180||wallSeconds>1200) {
                    report.Status="InvalidBudget"; report.Failure="FITNESS_COMPETITION_ROUTE_SECONDS must be an integer in 180..1200; received: "+requested;
                    SaveReport(); Assert.Fail(report.Failure);
                }
            }
            report.WallLimitSeconds=wallSeconds;
            trace=new StreamWriter(Path.Combine(directory,"trace.jsonl")) { AutoFlush=true };
            session=GameSession.OpenAutomation(saveDirectory,1);
            var initial=session.InspectSnapshot();
            Assert.That(initial,Is.Not.Null,"Cannot initialize route save: "+session.Diagnostic+" / "+session.ReadView().Body+"; save="+saveDirectory);
            initialRevision=initial.Revision;
            SaveReport(); TestContext.WriteLine("Competition route evidence: "+directory);
        }
        [TearDown] public void Close()
        {
            try {
                if(session!=null)last=session.InspectSnapshot();
                if(last!=null)File.WriteAllText(Path.Combine(directory,"final-root.json"),JsonUtility.ToJson(last,true));
                if(report.Status!="Passed") { report.Status=report.Status=="BudgetExceeded"?report.Status:"Failed"; report.Failure=TestContext.CurrentContext.Result.Message; }
                File.WriteAllText(Path.Combine(directory,"diagnostic.txt"),session?.Diagnostic??""); SaveReport();
            } finally { session?.Dispose(); trace?.Dispose(); }
        }
        private static T Read<T>(RootEnvelope root,string owner,string actor="player")=>JsonUtility.FromJson<T>(root.FindFragment(owner,actor).Json);
        private void Budget()
        {
            if(calls>=DriverBudget||watch.Elapsed.TotalSeconds>=wallSeconds||last!=null&&last.Revision-initialRevision>RevisionBudget) {
                report.Status="BudgetExceeded"; SaveReport();
                Assert.Fail("BudgetExceeded: driver="+calls+"/"+DriverBudget+", revisions="+(last?.Revision-initialRevision)+"/"+RevisionBudget+", wall="+watch.Elapsed.TotalSeconds+"/"+wallSeconds+"s; evidence="+directory);
            }
        }
        private void SaveReport()
        {
            report.DriverCalls=calls; report.PublicCommands=operation; report.ElapsedMilliseconds=watch.ElapsedMilliseconds;
            report.CommittedRevisions=last==null?0:last.Revision-initialRevision;
            File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true));
        }
        private void Log(string kind,FrontendView view,FrontendOption option=null,string message=null)
        {
            var state=last?.FindFragment("session","player")==null?null:Read<SessionState>(last,"session");
            trace.WriteLine(JsonUtility.ToJson(new TraceRow { Kind=kind,Action=option?.Id,Payload=option?.StringValue,IntValue=option?.IntValue??0,
                ElapsedMilliseconds=watch.ElapsedMilliseconds,Calls=calls,Revision=view.Revision,RunId=view.RunId,Screen=view.ScreenId,
                Day=state?.Day??0,Phase=state?.Phase,AnchorIndex=state?.AnchorIndex??0,Message=message }));
        }
        private FrontendAction Send(FrontendOption option)
        {
            Budget(); Assert.That(option,Is.Not.Null); Assert.That(option.Enabled,Is.True,option.Id);
            var view=session.ReadView(); Log("Command",view,option);
            var command=new FrontendAction { OperationId="competition-route/"+(++operation),RunId=view.RunId,ExpectedRevision=view.Revision,
                ActionId=option.Id,StringValue=option.StringValue,IntValue=option.IntValue };
            calls++; var reply=session.Execute(command); Log("Reply",session.ReadView(),option,reply.Message);
            Assert.That(reply.Accepted,Is.True,option.Id+": "+reply.Message); return command;
        }
        private void Advance()
        {
            Budget(); calls++; session.Advance(0);
            if(calls%100==0) { last=session.InspectSnapshot(); Log("AdvanceCheckpoint",session.ReadView()); SaveReport(); }
        }
        private void Reopen(string point)
        {
            Budget(); var before=session.InspectSnapshot(); string json=JsonUtility.ToJson(before);
            File.WriteAllText(Path.Combine(directory,point+"-root.json"),JsonUtility.ToJson(before,true));
            session.Dispose(); session=GameSession.OpenAutomation(saveDirectory,1); report.Reopens++;
            var restored=session.InspectSnapshot();
            Assert.That(restored,Is.Not.Null,"Cannot reopen route save at "+point+": "+session.Diagnostic+" / "+session.ReadView().Body);
            Assert.That(JsonUtility.ToJson(restored),Is.EqualTo(json),"Reopen changed committed root at "+point);
            var view=session.ReadView(); Assert.That(view.ScreenId,Is.Not.EqualTo("error"),view.Body);
            var resume=view.Actions.SingleOrDefault(a=>a.Id=="continue-game"&&a.Enabled);
            if(resume!=null)Send(resume);
        }
        private void Observe()
        {
            last=session.InspectSnapshot(); if(last.FindFragment("session","player")==null)return;
            var state=Read<SessionState>(last,"session"); report.LastDay=state.Day;
            var competition=Read<CompetitionState>(last,"competition"); var progression=Read<ProgressionRun>(last,"progression");
            var edition=competition.Current;
            if(!enrollRoute) {
                Assert.That(edition.Contests.All(c=>!c.Enrolled&&!c.Declined&&c.Results.Length==0),Is.True,"Never-enrolled route changed participation state");
                Assert.That(edition.ActualAttendanceCount,Is.Zero);
                if(state.Day==8&&edition.Opened&&!report.NeverEnrolledVerified) {
                    report.NeverEnrolledVerified=true;
                    File.WriteAllText(Path.Combine(directory,"day8-never-enrolled.json"),JsonUtility.ToJson(last,true)); SaveReport();
                }
                if(state.Day==42&&!edition.Contests.All(c=>c.WindowClosed)) {
                    Assert.That(edition.Facts,Is.Empty,"Failure before the final attendance window closed");
                    Assert.That(progression.HasActive||progression.TerminalLock,Is.False);
                }
                if(edition.Facts.Length>0) {
                    Assert.That(state.Day,Is.EqualTo(42));
                    Assert.That(edition.Contests.All(c=>c.WindowClosed),Is.True);
                    int finalAnchor=Array.IndexOf(SessionContent.Anchors(state),CompetitionContent.Anchor(ContestKind.ThinBuild));
                    Assert.That(finalAnchor,Is.GreaterThanOrEqualTo(0));
                    Assert.That(state.AnchorIndex,Is.GreaterThan(finalAnchor));
                    Assert.That(edition.Facts.Length,Is.EqualTo(1));
                    Assert.That(edition.Facts[0].Kind,Is.EqualTo(CompetitionFactKind.Unfulfilled));
                    Assert.That(edition.Facts[0].Reason,Is.EqualTo("NotEnrolled"));
                    Assert.That(edition.Facts[0].ActualAttendanceCount,Is.Zero);
                    Assert.That(progression.HasActive&&progression.TerminalLock,Is.True);
                    Assert.That(progression.Active.EndingId,Is.EqualTo("competition_unfulfilled"));
                    if(!report.UniqueUnfulfilled) {
                        report.UniqueUnfulfilled=true; report.AchievementId=progression.Active.AchievementId;
                        Reopen("day42-never-enrolled-unfulfilled"); SaveReport();
                    }
                }
            }
            if(state.Day<42) {
                Assert.That(edition.ActualAttendanceCount,Is.Zero); Assert.That(edition.Facts,Is.Empty);
                Assert.That(progression.TerminalLock,Is.False,"Premature competition failure");
            }
            if(state.Day==41&&edition.RegistrationClosed&&!report.D41ClosedWithoutFailure) {
                Assert.That(state.Phase,Is.EqualTo("DayEnd")); Assert.That(state.DayStep,Is.EqualTo(1),"Capture registration closure at E00, before income");
                Assert.That(edition.Contests.All(c=>c.Enrolled==enrollRoute),Is.True); Assert.That(progression.HasActive,Is.False);
                report.D41ClosedWithoutFailure=true; File.WriteAllText(Path.Combine(directory,"day41-deadline.json"),JsonUtility.ToJson(last,true)); SaveReport();
            }
            var world=Read<ApplicationWorldState>(last,"world","shared");
            if(state.Day==42&&world.Phase=="DayOpenReady"&&edition.FrozenActors.Length==0) {
                foreach(string actor in new[]{"player","rival","beginner","veteran","coach"}) {
                    var actorState=Read<SessionState>(last,"session",actor);
                    Assert.That(actorState.Day,Is.EqualTo(42)); Assert.That(actorState.Phase,Is.EqualTo("DayOpenReady"));
                    Assert.That(last.FindFragment("body",actor).Revision,Is.LessThanOrEqualTo(last.Revision));
                }
                Assert.That(last.FindFragment("world","shared").Revision,Is.LessThanOrEqualTo(last.Revision));
                freezeSource=last; report.D42CommittedBarrier=true;
                File.WriteAllText(Path.Combine(directory,"day42-committed-barrier.json"),JsonUtility.ToJson(last,true));
            }
            if(state.Day==42&&edition.FrozenActors.Length==2&&!report.D42FrozenFromBarrier) {
                Assert.That(freezeSource,Is.Not.Null,"Missing committed barrier observation");
                Assert.That(edition.BarrierId,Is.EqualTo(Read<ApplicationWorldState>(freezeSource,"world","shared").BarrierId));
                foreach(var snapshot in edition.FrozenActors) {
                    var expected=Read<BodyState>(freezeSource,"body",snapshot.ActorId);
                    Assert.That(snapshot.SourceCommitId,Is.EqualTo(freezeSource.LastCommitId)); Assert.That(snapshot.ActorRevision,Is.EqualTo(expected.Revision));
                    Assert.That(snapshot.SourceKind,Is.EqualTo(snapshot.ActorId=="player"?CompetitionSourceKind.PlayerProduction:CompetitionSourceKind.NpcProduction));
                    Assert.That(snapshot.Day,Is.EqualTo(42)); Assert.That(snapshot.Phase,Is.EqualTo("DayOpenReady"));
                    Assert.That(JsonUtility.ToJson(snapshot.Body),Is.EqualTo(JsonUtility.ToJson(expected)));
                    Assert.That(last.FindFragment("body",snapshot.ActorId).Json,Is.EqualTo(freezeSource.FindFragment("body",snapshot.ActorId).Json));
                }
                Assert.That(state.Phase,Is.EqualTo("EnterDay")); Assert.That(state.DayStep,Is.EqualTo(5));
                Assert.That(edition.Contests.All(c=>c.Results.Length==0),Is.True);
                report.D42FrozenFromBarrier=true; File.WriteAllText(Path.Combine(directory,"day42-frozen.json"),JsonUtility.ToJson(last,true)); SaveReport();
            }
        }
        private void StartFromPublishedOptions()
        {
            Send(session.ReadView().Actions.Single(a=>a.Id=="new-game"&&a.StringValue==TemplateCatalog.DefaultId&&a.Enabled));
            // Plan editing is an explicit public capability; only the editable plan payload is authored.
            var save=session.ReadView().Actions.First(a=>a.Id=="save-plan"&&a.Enabled);
            Send(new FrontendOption { Id=save.Id,Enabled=save.Enabled,Label=save.Label,IntValue=save.IntValue,
                StringValue=JsonUtility.ToJson(new PlanDraft { Mode=PlanMode.DayCycle,Slots=new[]{PlanCatalog.Rest(0)} }) });
        }
        [Test,Timeout(1260000)] public void SeedOnePublic42DayDoubleDeclineEndsOnceAndNewRunKeepsHistory()
        {
            RunRoute(true);
        }
        [Test,Explicit("Serial 42-day route; requires explicit 1200-second budget"),Timeout(1260000)]
        public void SeedOnePublic42DayNeverEnrolledEndsOnceAndNewRunKeepsHistory()
        {
            Assert.That(report.ExplicitWallBudget&&wallSeconds==1200,Is.True,"Set FITNESS_COMPETITION_ROUTE_SECONDS=1200 explicitly");
            RunRoute(false);
        }
        private void RunRoute(bool enroll)
        {
            enrollRoute=enroll; report.Route=enroll?"DoubleDecline":"NeverEnrolled";
            StartFromPublishedOptions(); report.OriginalRunId=session.ReadView().RunId;
            while(true) {
                Budget(); Observe(); var view=session.ReadView();
                Assert.That(view.ScreenId,Is.Not.EqualTo("error"),view.Body+" / "+session.Diagnostic);
                var options=view.Actions.Where(a=>a.Enabled).ToArray(); FrontendOption choice=null;
                if(view.ScreenId=="ending-menu")break;
                if(view.ScreenId=="ending-presentation") {
                    Assert.That(report.UniqueUnfulfilled,Is.True); choice=options.Single(a=>a.Id=="ending-segment");
                } else if(view.ScreenId=="ending-note") {
                    choice=options.Single(a=>a.Id=="ending-empty");
                } else if(enrollRoute&&!menuClosed&&options.Any(a=>a.Id=="competition-menu")) {
                    Assert.That(Regex.IsMatch(view.Status??"",@"第\s*8\s*日"),Is.True,"Enrollment must occur on D8");
                    choice=options.Single(a=>a.Id=="competition-menu");
                } else if(enrollRoute&&!menuClosed&&view.ScreenId=="competition") {
                    choice=!enrolledBody?options.Single(a=>a.Id=="competition-enroll"&&a.IntValue==0):
                        !enrolledThin?options.Single(a=>a.Id=="competition-enroll"&&a.IntValue==1):options.Single(a=>a.Id=="competition-close");
                } else if(enrollRoute&&options.Any(a=>a.Id=="competition-decline")) {
                    choice=options.Single(a=>a.Id=="competition-decline");
                    if(choice.IntValue==0&&!morningReopened) { morningReopened=true; Reopen("day42-before-morning-decline"); continue; }
                } else if(options.Any(a=>a.Id=="meal")) {
                    choice=options.FirstOrDefault(a=>a.Id=="meal"&&a.StringValue!="skip")??options.Single(a=>a.Id=="meal"&&a.StringValue=="skip");
                    if(choice.StringValue=="skip") { report.SkippedMeals++; Log("InsufficientFundsForPaidMeal",view,choice); } else report.PaidMeals++;
                } else if(options.Any(a=>a.Id=="end-training"))choice=options.First(a=>a.Id=="end-training");
                else if(options.Any(a=>a.Id=="shop-leave"))choice=options.First(a=>a.Id=="shop-leave");
                else if(options.Any(a=>a.Id=="item-use"))choice=options.First(a=>a.Id=="item-use"&&JsonUtility.FromJson<ItemPayload>(a.StringValue).Skip);
                else if(options.Any(a=>a.Id=="choose-entry"))choice=options.First(a=>a.Id=="choose-entry");
                else if(options.Any(a=>a.Id=="continue"))choice=options.First(a=>a.Id=="continue");
                else if(options.Any(a=>a.Id=="next-day"))choice=options.First(a=>a.Id=="next-day");
                if(choice==null) { Advance(); continue; }
                if(!enrollRoute)Assert.That(choice.Id.StartsWith("competition-",StringComparison.Ordinal),Is.False,"Never-enrolled route must not issue competition commands");
                var before=session.InspectSnapshot(); var command=Send(choice); var after=session.InspectSnapshot();
                if(choice.Id=="competition-enroll") {
                    var e=Read<CompetitionState>(after,"competition").Current;
                    Assert.That(Read<SessionState>(after,"session").AnchorIndex,Is.EqualTo(Read<SessionState>(before,"session").AnchorIndex));
                    Assert.That(e.ActualAttendanceCount,Is.Zero);
                    string anchor=CompetitionContent.Anchor((ContestKind)choice.IntValue);
                    Assert.That(Read<FlowState>(after,"flow").Occupancies.Count(o=>o.ActorId=="player"&&o.Day==42&&o.AnchorId==anchor),Is.EqualTo(1));
                    if(choice.IntValue==0)enrolledBody=true; else enrolledThin=true;
                }
                if(choice.Id=="competition-close"&&!menuClosed) {
                    menuClosed=true; Assert.That(enrolledBody&&enrolledThin,Is.True);
                    var e=Read<CompetitionState>(after,"competition").Current;
                    Assert.That(e.Opened,Is.True); Assert.That(e.Contests.All(c=>c.JudgeIds.Length==3&&c.JudgeIds.Distinct().Count()==3),Is.True);
                    Assert.That(Read<DomainRandomState>(after,"competition-random").DrawIndex,Is.GreaterThanOrEqualTo(6));
                    report.D8Enrollment=true; Reopen("day8-enrolled"); SaveReport();
                }
                if(choice.Id=="competition-decline") {
                    var e=Read<CompetitionState>(after,"competition").Current; var p=Read<ProgressionRun>(after,"progression");
                    Assert.That(e.ActualAttendanceCount,Is.Zero); Assert.That(e.Contests.All(c=>c.Results.Length==0),Is.True);
                    if(choice.IntValue==0) { Assert.That(e.Facts,Is.Empty); Assert.That(p.HasActive||p.TerminalLock,Is.False); report.MorningDeclinedWithoutFailure=true; }
                    else {
                        Assert.That(report.MorningDeclinedWithoutFailure,Is.True); Assert.That(e.Facts.Length,Is.EqualTo(1));
                        Assert.That(e.Facts[0].Kind,Is.EqualTo(CompetitionFactKind.Unfulfilled)); Assert.That(e.Facts[0].ActualAttendanceCount,Is.Zero);
                        Assert.That(p.TerminalLock&&p.HasActive,Is.True); Assert.That(p.Active.EndingId,Is.EqualTo("competition_unfulfilled"));
                        report.AchievementId=p.Active.AchievementId; report.UniqueUnfulfilled=true;
                    }
                    string committed=JsonUtility.ToJson(after); Budget(); calls++;
                    Assert.That(session.Execute(command).Accepted,Is.True,"Replay the same public decision receipt");
                    Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(committed));
                    Reopen(choice.IntValue==0?"day42-after-morning-decline":"day42-after-unfulfilled"); SaveReport();
                }
                if(choice.Id=="ending-empty") { report.NoteCommitted=true; Reopen("terminal-note-committed"); }
            }
            Assert.That((enrollRoute?report.D8Enrollment:report.NeverEnrolledVerified)&&report.D41ClosedWithoutFailure&&report.D42CommittedBarrier&&report.D42FrozenFromBarrier&&report.UniqueUnfulfilled&&report.NoteCommitted,Is.True,"Missing required route checkpoint");
            var terminal=session.InspectSnapshot(); var terminalProgress=Read<ProgressionRun>(terminal,"progression");
            Assert.That(terminalProgress.Active.NoteState,Is.EqualTo(AchievementNoteState.CommittedEmpty));
            Assert.That(session.ReadView().Actions.Any(a=>a.Id=="ending-continue"&&a.Enabled),Is.False);
            string stopped=JsonUtility.ToJson(terminal); Advance(); Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(stopped));
            report.TerminalCannotContinue=true; File.WriteAllText(Path.Combine(directory,"terminal-root.json"),JsonUtility.ToJson(terminal,true));
            StartFromPublishedOptions(); last=session.InspectSnapshot(); report.NewRunId=last.RunId;
            Assert.That(last.RunId,Is.Not.EqualTo(report.OriginalRunId)); Assert.That(Read<SessionState>(last,"session").Day,Is.EqualTo(1));
            Assert.That(Read<ProgressionRun>(last,"progression").HasActive,Is.False);
            session.Dispose(); session=null;
            using(var store=new LocalCheckpointStore(saveDirectory,new UnityJsonStateCodec(),expectedContentVersion:SessionContent.Version)) {
                var history=new EndingProgression(new UnityJsonStateCodec()).ReadHistory(store,0,64);
                var record=history.Single(h=>h.Snapshot.AchievementId==report.AchievementId);
                Assert.That(record.Snapshot.RunId,Is.EqualTo(report.OriginalRunId)); Assert.That(record.Snapshot.Day,Is.EqualTo(42));
                Assert.That(record.Snapshot.EndingId,Is.EqualTo("competition_unfulfilled")); Assert.That(record.NoteState,Is.EqualTo(AchievementNoteState.CommittedEmpty));
                Assert.That(record.Note,Is.Empty); Assert.That(history.Count,Is.EqualTo(1));
                File.WriteAllText(Path.Combine(directory,"retained-achievement.json"),JsonUtility.ToJson(record.Snapshot,true));
            }
            report.NewRunPreservesHistory=true; report.Status="Passed"; SaveReport();
        }
    }
}
