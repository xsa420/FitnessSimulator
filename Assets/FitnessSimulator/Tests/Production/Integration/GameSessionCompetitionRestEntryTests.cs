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
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Progression;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    // Rest-only natural-result routes, separate from the training/championship driver's ownership.
    // The long cases are explicit: ordinary test discovery must not silently launch both 42-day runs.
    public sealed class GameSessionCompetitionRestEntryTests
    {
        private const int DriverLimit=250000,RevisionLimit=300000,PreflightWallSeconds=600;
        [Serializable] private sealed class ItemPayload { public bool Skip=false; }
        [Serializable] private sealed class Report
        {
            public string Strategy="rest-single-entry-v1",RunId,Directory,Mode,Status="NotCompleted",Failure="",ResultId,FactId,AchievementId;
            public int Seed=1,Kind,DriverBudget=DriverLimit,RevisionBudget=RevisionLimit,WallBudgetSeconds,HardTimeoutMilliseconds;
            public int Calls,Commands,PaidMeals,SkippedMeals,Reopens,LastDay,PlayerRank;
            public long ElapsedMilliseconds,CommittedRevisions;
            public bool D8Verified,D41Verified,FrozenSourceVerified,ActualAttendanceVerified,NonChampionCoverage,ChampionObserved,NoteVerified,TerminalVerified,SuccessContinueOffered;
        }
        [TestCase(0),TestCase(1),Timeout(660000)]
        public void SeedOneRestSingleEntryD8Preflight(int kind) { Run((ContestKind)kind,true,PreflightWallSeconds,660000); }

        [TestCase(0),TestCase(1),Explicit("42-day route requires explicit opt-in and wall budget."),Timeout(1260000)]
        public void SeedOneRestActualContestNaturalResult(int kind)
        {
            // Explicit alone is insufficient: a class-level runner filter can select explicit tests.
            if(Environment.GetEnvironmentVariable("FITNESS_REST_ENTRY_LONG_ENABLE")!="1")
                Assert.Ignore("LongRouteNotEnabled: set FITNESS_REST_ENTRY_LONG_ENABLE=1 and FITNESS_REST_ENTRY_LONG_SECONDS explicitly.");
            int seconds;
            Assert.That(int.TryParse(Environment.GetEnvironmentVariable("FITNESS_REST_ENTRY_LONG_SECONDS"),out seconds),Is.True,
                "LongRouteBudgetRequired: explicitly provide FITNESS_REST_ENTRY_LONG_SECONDS (initial measured recommendation: 1200).");
            Assert.That(seconds,Is.InRange(1,1200),"Budget must fit the 1260-second framework timeout; changing that ceiling requires a separate reviewed change.");
            Run((ContestKind)kind,false,seconds,1260000);
        }

        private static T Read<T>(RootEnvelope root,string owner,string actor="player")=>JsonUtility.FromJson<T>(root.FindFragment(owner,actor).Json);
        private static FrontendOption Choose(FrontendView view,string id)=>view.Actions.First(a=>a.Enabled&&a.Id==id);
        private void Run(ContestKind kind,bool preflightOnly,int wallSeconds,int timeoutMilliseconds)
        {
            string directory=Path.Combine(Path.GetTempPath(),"Fitness-Competition-RestEntry",kind.ToString(),Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory); string saveDirectory=Path.Combine(directory,"save");
            var report=new Report { Kind=(int)kind,Directory=directory,Mode=preflightOnly?"D8Preflight":"D42NaturalResult",
                WallBudgetSeconds=wallSeconds,HardTimeoutMilliseconds=timeoutMilliseconds };
            var clock=Stopwatch.StartNew(); GameSession session=null; RootEnvelope last=null,barrierSource=null;
            long initialRevision=0; bool enrolled=false,closedMenu=false,entryReopened=false; string frozenJson=null;
            using(var trace=new StreamWriter(Path.Combine(directory,"trace.jsonl")) { AutoFlush=true }) {
                Action saveReport=()=> { report.ElapsedMilliseconds=clock.ElapsedMilliseconds; report.CommittedRevisions=last==null?0:last.Revision-initialRevision;
                    File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true)); };
                Action budget=()=> {
                    if(report.Calls>=DriverLimit||clock.Elapsed.TotalSeconds>=wallSeconds||last!=null&&last.Revision-initialRevision>RevisionLimit) {
                        report.Status="BudgetExceeded"; saveReport();
                        Assert.Fail("BudgetExceeded: calls="+report.Calls+"/"+DriverLimit+", revisions="+(last?.Revision-initialRevision)+"/"+RevisionLimit+", wall="+clock.Elapsed.TotalSeconds+"/"+wallSeconds+"s");
                    }
                };
                Func<FrontendOption,FrontendAction> send=option=> {
                    budget(); var view=session.ReadView(); Assert.That(option.Enabled,Is.True);
                    var command=new FrontendAction { OperationId="rest-entry/"+(++report.Commands),RunId=view.RunId,ExpectedRevision=view.Revision,
                        ActionId=option.Id,StringValue=option.StringValue,IntValue=option.IntValue };
                    trace.WriteLine(JsonUtility.ToJson(view)); trace.WriteLine(JsonUtility.ToJson(command)); report.Calls++;
                    var reply=session.Execute(command); trace.WriteLine(JsonUtility.ToJson(reply)); Assert.That(reply.Accepted,Is.True,option.Id+": "+reply.Message); return command;
                };
                Action<string> reopen=point=> {
                    budget(); var before=session.InspectSnapshot(); string saved=JsonUtility.ToJson(before);
                    File.WriteAllText(Path.Combine(directory,point+".json"),JsonUtility.ToJson(before,true));
                    session.Dispose(); session=GameSession.OpenAutomation(saveDirectory,1); report.Reopens++;
                    Assert.That(session.InspectSnapshot(),Is.Not.Null,session.Diagnostic);
                    Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(saved));
                    var resume=session.ReadView().Actions.FirstOrDefault(a=>a.Id=="continue-game"&&a.Enabled); if(resume!=null)send(resume);
                };
                try {
                    session=GameSession.OpenAutomation(saveDirectory,1); last=session.InspectSnapshot();
                    Assert.That(last,Is.Not.Null,session.Diagnostic+" / "+session.ReadView().Body); initialRevision=last.Revision; saveReport();
                    send(session.ReadView().Actions.Single(a=>a.Id=="new-game"&&a.Enabled&&a.StringValue==TemplateCatalog.DefaultId));
                    var planOption=Choose(session.ReadView(),"save-plan");
                    // Explicit public plan editing; all other option payloads remain exactly as projected.
                    send(new FrontendOption { Id=planOption.Id,Enabled=true,IntValue=planOption.IntValue,
                        StringValue=JsonUtility.ToJson(new PlanDraft { Mode=PlanMode.DayCycle,Slots=new[]{PlanCatalog.Rest(0)} }) });
                    report.RunId=session.ReadView().RunId;
                    while(true) {
                        budget(); last=session.InspectSnapshot(); var state=Read<SessionState>(last,"session"); report.LastDay=state.Day;
                        var competition=Read<CompetitionState>(last,"competition"); var edition=competition.Current;
                        var progression=Read<ProgressionRun>(last,"progression"); var world=Read<ApplicationWorldState>(last,"world","shared");
                        Assert.That(state.Day,Is.LessThanOrEqualTo(42),"Route must settle its actual contest before leaving day 42");
                        Assert.That(state.HasEffectiveTraining,Is.False,"Rest strategy must not receive NPC training results");
                        if(state.Day<42) { Assert.That(edition.Facts,Is.Empty); Assert.That(edition.ActualAttendanceCount,Is.Zero); }
                        if(state.Day==41&&edition.RegistrationClosed&&!report.D41Verified) {
                            Assert.That(state.Phase,Is.EqualTo("DayEnd")); Assert.That(state.DayStep,Is.EqualTo(1)); Assert.That(progression.HasActive,Is.False);
                            report.D41Verified=true; File.WriteAllText(Path.Combine(directory,"day41-closed.json"),JsonUtility.ToJson(last,true));
                        }
                        if(state.Day==42&&world.Phase=="DayOpenReady"&&edition.FrozenActors.Length==0) {
                            foreach(string actor in new[]{"player","rival","beginner","veteran","coach"}) {
                                var a=Read<SessionState>(last,"session",actor); Assert.That(a.Day,Is.EqualTo(42)); Assert.That(a.Phase,Is.EqualTo("DayOpenReady"));
                            }
                            barrierSource=last; File.WriteAllText(Path.Combine(directory,"day42-source.json"),JsonUtility.ToJson(last,true));
                        }
                        if(edition.FrozenActors.Length==2&&!report.FrozenSourceVerified) {
                            Assert.That(barrierSource,Is.Not.Null); Assert.That(edition.BarrierId,Is.EqualTo(Read<ApplicationWorldState>(barrierSource,"world","shared").BarrierId));
                            foreach(var snapshot in edition.FrozenActors) {
                                var body=Read<BodyState>(barrierSource,"body",snapshot.ActorId);
                                Assert.That(snapshot.SourceCommitId,Is.EqualTo(barrierSource.LastCommitId)); Assert.That(snapshot.ActorRevision,Is.EqualTo(body.Revision));
                                Assert.That(snapshot.Day,Is.EqualTo(42)); Assert.That(snapshot.Phase,Is.EqualTo("DayOpenReady"));
                                Assert.That(JsonUtility.ToJson(snapshot.Body),Is.EqualTo(JsonUtility.ToJson(body)));
                                Assert.That(barrierSource.FindFragment("body",snapshot.ActorId).Revision,Is.LessThanOrEqualTo(barrierSource.Revision));
                            }
                            Assert.That(state.Phase,Is.EqualTo("EnterDay")); Assert.That(state.DayStep,Is.EqualTo(5));
                            report.FrozenSourceVerified=true; frozenJson=JsonUtility.ToJson(edition); File.WriteAllText(Path.Combine(directory,"day42-frozen.json"),JsonUtility.ToJson(last,true));
                        }
                        var view=session.ReadView(); Assert.That(view.ScreenId,Is.Not.EqualTo("error"),view.Body+" / "+session.Diagnostic);
                        var options=view.Actions.Where(a=>a.Enabled).ToArray(); FrontendOption choice=null;
                        if(view.ScreenId=="ending-menu") {
                            Assert.That(report.ActualAttendanceVerified&&report.NoteVerified,Is.True);
                            Assert.That(progression.Active.NoteState,Is.EqualTo(AchievementNoteState.CommittedEmpty));
                            bool offersContinue=options.Any(a=>a.Id=="ending-continue");
                            Assert.That(offersContinue,Is.EqualTo(report.ChampionObserved));
                            report.TerminalVerified=report.NonChampionCoverage&&progression.TerminalLock;
                            report.SuccessContinueOffered=report.ChampionObserved&&offersContinue;
                            if(!offersContinue) {
                                string stopped=JsonUtility.ToJson(session.InspectSnapshot()); budget(); report.Calls++; session.Advance(0);
                                Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(stopped),"Terminal route must not resume its world");
                            }
                            // A genuine win is retained honestly; it does not count as NonChampion or D43 coverage.
                            report.Status=report.NonChampionCoverage?"NonChampionTerminalVerified":"ChampionObserved_NonChampionCoverageMissing";
                            reopen("ending-menu"); break;
                        }
                        if(view.ScreenId=="ending-presentation") { Assert.That(report.ActualAttendanceVerified,Is.True); choice=options.Single(a=>a.Id=="ending-segment"); }
                        else if(view.ScreenId=="ending-note")choice=options.Single(a=>a.Id=="ending-empty");
                        else if(!closedMenu&&options.Any(a=>a.Id=="competition-menu")) {
                            Assert.That(Regex.IsMatch(view.Status??"",@"第\s*8\s*日"),Is.True); choice=options.Single(a=>a.Id=="competition-menu");
                        } else if(!closedMenu&&view.ScreenId=="competition") {
                            choice=!enrolled?options.Single(a=>a.Id=="competition-enroll"&&a.IntValue==(int)kind):options.Single(a=>a.Id=="competition-close");
                        } else if(options.Any(a=>a.Id=="competition-attend")) {
                            choice=options.Single(a=>a.Id=="competition-attend"&&a.IntValue==(int)kind);
                            if(!entryReopened) { entryReopened=true; reopen("before-actual-attendance"); continue; }
                        } else if(options.Any(a=>a.Id=="meal")) {
                            choice=options.FirstOrDefault(a=>a.Id=="meal"&&a.StringValue!="skip")??options.Single(a=>a.Id=="meal"&&a.StringValue=="skip");
                            if(choice.StringValue=="skip") { report.SkippedMeals++; trace.WriteLine("InsufficientFundsForPaidMeal"); } else report.PaidMeals++;
                        } else if(options.Any(a=>a.Id=="shop-leave"))choice=options.First(a=>a.Id=="shop-leave");
                        else if(options.Any(a=>a.Id=="item-use"))choice=options.First(a=>a.Id=="item-use"&&JsonUtility.FromJson<ItemPayload>(a.StringValue).Skip);
                        else if(options.Any(a=>a.Id=="choose-entry"))choice=options.First(a=>a.Id=="choose-entry");
                        else if(options.Any(a=>a.Id=="continue"))choice=options.First(a=>a.Id=="continue");
                        else if(options.Any(a=>a.Id=="next-day"))choice=options.First(a=>a.Id=="next-day");
                        if(choice==null) { report.Calls++; session.Advance(0); if(report.Calls%100==0)saveReport(); continue; }
                        var command=send(choice); var after=session.InspectSnapshot();
                        if(choice.Id=="competition-enroll") {
                            enrolled=true; var e=Read<CompetitionState>(after,"competition").Current;
                            Assert.That(e.Contests.Single(c=>c.Kind==kind).Enrolled,Is.True); Assert.That(e.Contests.Single(c=>c.Kind!=kind).Enrolled,Is.False);
                            Assert.That(Read<SessionState>(after,"session").AnchorIndex,Is.EqualTo(state.AnchorIndex));
                            Assert.That(Read<FlowState>(after,"flow").Occupancies.Count(o=>o.Day==42&&o.ActorId=="player"&&o.AnchorId==CompetitionContent.Anchor(kind)),Is.EqualTo(1));
                        }
                        if(choice.Id=="competition-close"&&!closedMenu) {
                            closedMenu=true; Assert.That(enrolled,Is.True); report.D8Verified=true; reopen("day8-single-enrollment");
                            if(preflightOnly) { report.Status="D8PreflightVerified"; break; }
                        }
                        if(choice.Id=="competition-attend") {
                            var e=Read<CompetitionState>(after,"competition").Current; var c=e.Contests.Single(x=>x.Kind==kind);
                            var p=Read<ProgressionRun>(after,"progression"); Assert.That(c.Results.Length,Is.EqualTo(1));
                            Assert.That(e.ActualAttendanceCount,Is.EqualTo(1)); Assert.That(e.Facts.Length,Is.EqualTo(1));
                            Assert.That(e.Contests.Single(x=>x.Kind!=kind).Results,Is.Empty);
                            var result=c.Results[0]; var fact=e.Facts[0]; var player=result.Score.Participants.Single(x=>x.ActorId=="player");
                            report.PlayerRank=player.Rank; report.ResultId=result.ResultId; report.FactId=fact.FactId;
                            report.ChampionObserved=player.Rank==1; report.NonChampionCoverage=player.Rank!=1;
                            Assert.That(fact.Kind,Is.EqualTo(report.ChampionObserved?CompetitionFactKind.Champion:CompetitionFactKind.NonChampion));
                            Assert.That(fact.ResultId,Is.EqualTo(result.ResultId)); Assert.That(result.BarrierId,Is.EqualTo(e.BarrierId));
                            var recomputed=CompetitionScoring.Score(kind,e.FrozenActors[0].Body,e.FrozenActors[1].Body,c.JudgeIds);
                            Assert.That(JsonUtility.ToJson(result.Score),Is.EqualTo(JsonUtility.ToJson(recomputed)),"Post-attendance audit only");
                            var original=JsonUtility.FromJson<CompetitionEdition>(frozenJson);
                            foreach(var snapshot in e.FrozenActors)Assert.That(JsonUtility.ToJson(snapshot),Is.EqualTo(JsonUtility.ToJson(original.FrozenActors.Single(x=>x.ActorId==snapshot.ActorId))));
                            Assert.That(p.HasActive,Is.True); Assert.That(p.TerminalLock,Is.EqualTo(report.NonChampionCoverage));
                            Assert.That(p.Active.EndingId,Is.EqualTo(report.NonChampionCoverage?"competition_failed":kind==ContestKind.Bodybuilding?"bodybuilding_champion":"lean_champion"));
                            report.AchievementId=p.Active.AchievementId; report.ActualAttendanceVerified=true;
                            string committed=JsonUtility.ToJson(after); budget(); report.Calls++;
                            Assert.That(session.Execute(command).Accepted,Is.True); Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(committed));
                            reopen("actual-result");
                        }
                        if(choice.Id=="ending-empty")report.NoteVerified=true;
                        saveReport();
                    }
                    Assert.That(report.D8Verified,Is.True);
                    if(!preflightOnly)Assert.That(report.D41Verified&&report.FrozenSourceVerified&&report.ActualAttendanceVerified&&(report.TerminalVerified||report.SuccessContinueOffered),Is.True);
                } catch(Exception ex) { if(report.Status!="BudgetExceeded")report.Status="Failed"; report.Failure=ex.ToString(); throw; }
                finally {
                    if(session!=null) { last=session.InspectSnapshot(); File.WriteAllText(Path.Combine(directory,"diagnostic.txt"),session.Diagnostic??""); }
                    if(last!=null)File.WriteAllText(Path.Combine(directory,"final-root.json"),JsonUtility.ToJson(last,true));
                    saveReport(); session?.Dispose(); TestContext.WriteLine("Natural competition evidence: "+directory);
                }
            }
        }
    }
}
