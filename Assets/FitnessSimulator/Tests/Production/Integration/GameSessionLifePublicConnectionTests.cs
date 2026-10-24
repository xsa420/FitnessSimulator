using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class GameSessionLifePublicConnectionTests
    {
        private GameSession session;
        private string directory;
        private int commands;
        private readonly StringBuilder evidence=new StringBuilder();
        private readonly Stopwatch clock=new Stopwatch();
        [SetUp] public void Open()
        {
            commands=0;steps=0;restKey="";zeroInput=false;evidence.Clear();clock.Reset();session=null;
            directory=Path.Combine(Path.GetTempPath(),"Fitness-Relationship-Route",Guid.NewGuid().ToString("N"));
            session=GameSession.OpenAutomation(directory,1);RequireSnapshot("first Open");clock.Start();
            evidence.AppendLine("Budgets: driver=600 seconds; NUnit Timeout=660000 milliseconds; maximum day=14; loop steps=50000; commands=30000.");
        }
        [TearDown] public void Close()
        {
            if(session==null)return;
            File.WriteAllText(Path.Combine(directory,"route.txt"),evidence.ToString());
            File.WriteAllText(Path.Combine(directory,"final.json"),JsonUtility.ToJson(session.InspectSnapshot(),true));
            File.WriteAllText(Path.Combine(directory,"diagnostic.txt"),session.Diagnostic??"");
            TestContext.WriteLine("Relationship real-route evidence: "+directory);session.Dispose();
        }
        private T Read<T>(string owner)=>JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner,"player").Json);
        private void Bound()
        {
            Assert.That(clock.Elapsed.TotalSeconds,Is.LessThan(600),"CoverageMissing: 600-second driver budget (NUnit Timeout=660000 milliseconds)");
            Assert.That(commands,Is.LessThan(30000),"CoverageMissing: 30000-command route budget");
        }
        private FrontendAction Send(string id,string payload=null,int number=0)
        {
            Bound();var view=session.ReadView();var action=new FrontendAction { OperationId="relationship-route/"+(++commands),RunId=view.RunId,ExpectedRevision=view.Revision,ActionId=id,StringValue=payload,IntValue=number };
            var reply=session.Execute(action);
            if(!id.StartsWith("bench-",StringComparison.Ordinal))evidence.AppendLine(view.Revision+" "+id+" accepted="+reply.Accepted+" "+reply.Message);
            Assert.That(reply.Accepted,Is.True,id+": "+reply.Message+" / "+session.Diagnostic);return action;
        }
        private void Reopen()
        {
            string before=JsonUtility.ToJson(session.InspectSnapshot());session.Dispose();session=GameSession.OpenAutomation(directory,999);
            RequireSnapshot("Reopen");
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(before),"Restart must preserve the committed root");Send("continue-game");
        }
        private void RequireSnapshot(string phase)
        {
            Assert.That(session.InspectSnapshot(),Is.Not.Null,phase+" / "+session.Diagnostic);
            Assert.That(session.ReadView().ScreenId,Is.Not.EqualTo("error"),phase+" / "+session.Diagnostic+" / "+session.ReadView().Body);
        }
        private int steps;
        private string restKey="";
        private bool zeroInput;
        private WorkoutSnapshot Workout(){var t=Read<TrainingDayState>("training");return t.HasWorkout?t.Workout:null;}
        private T Other<T>(string owner,string actor)=>JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner,actor).Json);
        private void Start(bool multiple=false,bool rest=false)
        {
            Send("new-game",TemplateCatalog.DefaultId);
            var slot=rest?PlanCatalog.Rest(0):PlanCatalog.Training(0,TrainingCombination.Free,multiple?new[]{"bench_press","lat_pulldown","squat"}:new[]{"bench_press"});slot.Volume=TrainingVolume.Low;
            Send("save-plan",JsonUtility.ToJson(new PlanDraft { Mode=PlanMode.DayCycle,Slots=new[]{slot} }));
        }
        private void Fixture(string description,Action<RootEnvelope> edit)
        {
            evidence.AppendLine("EXPLICIT PRECONDITION: "+description);session.Dispose();
            var codec=new UnityJsonStateCodec();using(var store=new LocalCheckpointStore(directory,codec,expectedContentVersion:SessionContent.Version))
            {
                var root=store.Load().Root;var dispatch=new SerialCommandDispatcher(store,codec,root,c=>c.Source==CommandSource.Test,null);
                var result=dispatch.Execute(new CommandEnvelope { OperationId="fixture/"+Guid.NewGuid().ToString("N"),RunId=root.RunId,ActorId="player",ResourceScope="player",Source=CommandSource.Test,ActionKind="TestPrecondition",ExpectedRevision=root.Revision,Payload=description },candidate=>{edit(candidate);return new PreparedOutcome { SemanticKey="fixture/"+description,OutcomeKind=OutcomeKind.Success,EventDisposition=EventDisposition.AuxiliaryOnly,CheckKind=CheckKind.None,OpportunityPolicy=OpportunityPolicy.Keep };});
                Assert.That(result.Status,Is.EqualTo(CommandStatus.Committed));
            }
            session=GameSession.OpenAutomation(directory,999);RequireSnapshot("Fixture reopen");Send("continue-game");
        }
        private static void Put<T>(RootEnvelope r,string owner,T value)=>r.SetFragment(owner,"player",1,r.Revision+1,JsonUtility.ToJson(value));
        private void Until(Func<bool> done)
        {
            while(!done()) { Bound();Assert.That(++steps,Is.LessThan(50000));Assert.That(Read<SessionState>("session").Day,Is.LessThanOrEqualTo(14),"CoverageMissing D14");Step(); }
        }
        private void Step()
        {
            var v=session.ReadView();Assert.That(v.ScreenId,Is.Not.EqualTo("error"),v.Body+" / "+session.Diagnostic);
            if(v.BenchPress!=null)
            {
                var b=v.BenchPress;if(!v.TrainingInput.IsPreparing&&!b.IsComplete)
                {
                    bool left=!zeroInput&&b.Balance>.02,right=zeroInput||b.Balance<-.02;
                    if(left!=b.LeftHeld)Send(left?"bench-left-down":"bench-left-up");if(right!=b.RightHeld)Send(right?"bench-right-down":"bench-right-up");
                }
                session.Advance(.01);return;
            }
            var options=v.Actions.Where(o=>o.Enabled).ToArray();FrontendOption next=null;
            if(options.Any(o=>o.Id=="settle"))next=options.First(o=>o.Id=="settle");
            else if(options.Any(o=>o.Id=="begin-set"))
            {
                var w=Workout();string key=w.WorkoutId+"/"+w.NextSetSequence;
                if(w.SettledSetIds.Length>0&&restKey!=key){restKey=key;next=options.Single(o=>o.Id=="rest"&&o.IntValue==90);}
                else next=options.Single(o=>o.Id=="begin-set"&&o.StringValue=="manual"&&o.IntValue==0);
            }
            else if(options.Any(o=>o.Id=="meal"))next=options.First(o=>o.Id=="meal"&&new[]{"porridge_egg","home_rice","home_noodles"}.Contains(o.StringValue));
            else if(options.Any(o=>o.Id=="choose-entry"))next=options.FirstOrDefault(o=>o.Id=="choose-entry"&&o.StringValue=="training-choice")??options.FirstOrDefault(o=>o.Id=="choose-entry"&&o.StringValue=="evening-study")??options.First(o=>o.Id=="choose-entry");
            else if(options.Any(o=>o.Id=="item-use"))next=options.First(o=>o.Id=="item-use"&&o.StringValue.Contains("\"Skip\":true"));
            else if(options.Any(o=>o.Id=="shop-leave"))next=options.First(o=>o.Id=="shop-leave");
            else if(options.Any(o=>o.Id=="continue"))next=options.First(o=>o.Id=="continue");
            else if(options.Any(o=>o.Id=="next-day"))next=options.First(o=>o.Id=="next-day");
            if(next==null)session.Advance(0);else Send(next.Id,next.StringValue,next.IntValue);
        }
        [TestCase(false)][TestCase(true)][Timeout(660000)] public void G02PublicStartAndFollowersWithRestart(bool followers)
        {
            Start(multiple:true);
            if(followers)Until(()=>session.ReadView().Actions.Any(o=>o.Id=="begin-set"));
            else Until(()=>{var f=Read<FlowState>("flow");var c=f.HasTree?FlowEngine.Current(f):null;return c!=null&&c.Phase==NodePhase.NotStarted&&f.Tree.Nodes.Single(n=>n.Id==c.NodeId).Handler=="training";});
            int day=Read<SessionState>("session").Day;
            Fixture(followers?"pre-set fatigue149; injury must be produced by actual input":"two existing injured actions",root=>{
                var b=JsonUtility.FromJson<BodyState>(root.FindFragment("body","player").Json);
                if(followers)foreach(string id in ExerciseCatalog.Get("bench_press").TargetRegionIds)b.Region(id).SetTemporary(149);
                else foreach(string id in new[]{"chest_upper","back_upper"}){b.Region(id).InjuredDay=day;b.Region(id).ExpiresAtStartOfDay=day+3;}
                Put(root,"body",b);
            });
            if(followers)
            {
                Until(()=>Workout()?.Phase==WorkoutPhase.ResultReady);Assert.That(Workout().PendingResult.EffectiveReps,Is.GreaterThan(0));
                Until(()=>Workout()?.Phase==WorkoutPhase.Settled);Assert.That(Read<BodyState>("body").Region("chest_upper").IsInjured,Is.True);Reopen();
                var ids=Workout().SettledSetIds.ToArray();Until(()=>Workout()?.EntryIndex==1);Assert.That(Workout().SettledSetIds,Is.EqualTo(ids));
            }
            else {Until(()=>Workout()?.EntryIndex==2);Reopen();Assert.That(Workout().SettledSetIds,Is.Empty);}
            Assert.That(Workout().Phase,Is.EqualTo(WorkoutPhase.PreSet));
            string quota=JsonUtility.ToJson(Workout().Quota);var settledIds=Workout().SettledSetIds.ToArray();
            string expected=followers?"lat_pulldown":"squat";
            Until(()=>session.ReadView().Actions.Any(o=>o.Id=="begin-set"&&o.Enabled&&o.StringValue=="manual"&&o.IntValue==0));
            Assert.That(Workout().Quota.Entries[Workout().EntryIndex].Plan.ExerciseId,Is.EqualTo(expected));
            Assert.That(Read<BodyState>("body").Region("chest_upper").IsInjured,Is.True);
            if(!followers)Assert.That(Read<BodyState>("body").Region("back_upper").IsInjured,Is.True);
            Assert.That(ExerciseCatalog.Get(expected).TargetRegionIds.All(id=>!Read<BodyState>("body").Region(id).IsInjured),Is.True);
            Assert.That(Workout().SettledSetIds,Is.EqualTo(settledIds));Assert.That(JsonUtility.ToJson(Workout().Quota),Is.EqualTo(quota));
            var begin=session.ReadView().Actions.Single(o=>o.Id=="begin-set"&&o.Enabled&&o.StringValue=="manual"&&o.IntValue==0);
            Send(begin.Id,begin.StringValue,begin.IntValue);
            Assert.That(Workout().Phase,Is.EqualTo(WorkoutPhase.Playing));Assert.That(Workout().PendingRequest.ExerciseId,Is.EqualTo(expected));
            Assert.That(Workout().SettledSetIds,Is.EqualTo(settledIds));Assert.That(JsonUtility.ToJson(Workout().Quota),Is.EqualTo(quota));
        }
        [Test,Timeout(660000)] public void G03PublicInviteAndNextDayQuota()
        {
            Start();Until(()=>session.ReadView().Actions.Any(o=>o.Id=="rel-dialogue"&&o.StringValue.Contains("\"TargetId\":\"beginner\"")));
            int day=Read<SessionState>("session").Day;long time=Read<SessionState>("session").TotalGameSeconds;
            Fixture("affection40 only, existing actual beginner presence/rest",root=>{var r=JsonUtility.FromJson<RelationshipState>(root.FindFragment("relationships","player").Json);r.Affections.Single(a=>a.CharacterId==RelationshipRules.Beginner).Value=40;Put(root,"relationships",r);});
            var choices=session.ReadView().Actions;var invite=choices.Single(o=>o.Id=="rel-invite");var second=choices.First(o=>o.Id=="rel-dialogue");Send(invite.Id,invite.StringValue,invite.IntValue);
            Assert.That(Read<RelationshipState>("relationships").CurrentBuddy,Is.Empty);Assert.That(Read<RelationshipState>("relationships").PendingBuddyDay,Is.EqualTo(day+1));
            var view=session.ReadView();Assert.That(session.Execute(new FrontendAction { OperationId="second-interaction",RunId=view.RunId,ExpectedRevision=view.Revision,ActionId=second.Id,StringValue=second.StringValue,IntValue=second.IntValue }).Accepted,Is.False);
            Assert.That(Read<SessionState>("session").TotalGameSeconds,Is.EqualTo(time));Reopen();
            Until(()=>Read<SessionState>("session").Day==day+1&&Other<SessionState>("session",RelationshipRules.Beginner).Phase=="Anchors");
            Assert.That(Read<RelationshipState>("relationships").CurrentBuddy,Is.EqualTo(RelationshipRules.Beginner));
            Assert.That(Other<TrainingDayState>("training",RelationshipRules.Beginner).Quota.Entries.Select(e=>e.Plan.ExerciseId),Is.EqualTo(Read<TrainingDayState>("training").Quota.Entries.Select(e=>e.Plan.ExerciseId)));
            Until(()=>Workout()?.Phase==WorkoutPhase.Playing&&Workout().Quota.Day>=day+1);
            bool present=Read<WorkoutInteraction>("workout-interaction").PresentCharacterIds.Contains(RelationshipRules.Beginner);
            Assert.That(Read<SetBenefitsSnapshot>("set-benefits").RiskReductionBasisPoints,Is.EqualTo(present?500:0));evidence.AppendLine("Actual buddy present="+present+"; opposite presence branch not claimed");
        }
        [TestCase(true)][TestCase(false)][Timeout(660000)] public void G04PublicTrainingAndEveningStudy(bool effective)
        {
            zeroInput=!effective;Start();
            if(effective)Fixture("one notebook only; evening study and notebook awards remain separate",root=>{var inventory=JsonUtility.FromJson<InventoryState>(root.FindFragment("inventory","player").Json);Put(root,"inventory",InventoryRules.PrepareGrant(inventory,EconomyContent.Notebook,1));});
            Until(()=>Workout()?.Phase==WorkoutPhase.ResultReady);
            if(effective)Assert.That(Workout().PendingResult.EffectiveReps,Is.GreaterThan(0));else Assert.That(Workout().PendingResult.EffectiveReps,Is.Zero);
            int day=Read<SessionState>("session").Day;
            if(!effective)
            {
                Until(()=>Read<SessionState>("session").Day==day&&Read<SessionState>("session").Phase=="AwaitNextDay");
                Assert.That(Read<ActivityState>("activity").PracticedExercises,Is.Empty);Assert.That(Read<BodyState>("body").Exercises.All(e=>e.KnowledgeMicro==0),Is.True);return;
            }
            Until(()=>session.ReadView().ScreenId.StartsWith("study-evening/",StringComparison.Ordinal)&&session.ReadView().Actions.Any(o=>o.Id=="continue"));
            var before=Read<BodyState>("body");long time=Read<SessionState>("session").TotalGameSeconds;var study=session.ReadView().Actions.First(o=>o.Id=="continue");Send(study.Id,study.StringValue,study.IntValue);
            var after=Read<BodyState>("body");foreach(var e in before.Exercises)Assert.That(after.Exercise(e.ExerciseId).KnowledgeMicro,Is.EqualTo(e.KnowledgeMicro+(e.ExerciseId=="bench_press"?3000000:0)));
            Assert.That(Read<SessionState>("session").TotalGameSeconds-time,Is.EqualTo(900));Reopen();
            Until(()=>session.ReadView().Actions.Any(o=>o.Id=="item-use"));var use=session.ReadView().Actions.Single(o=>o.Id=="item-use"&&!o.StringValue.Contains("\"Skip\":true"));Assert.That(use.Enabled,Is.True);
            long itemTime=Read<SessionState>("session").TotalGameSeconds;Send(use.Id,use.StringValue,use.IntValue);
            var final=Read<BodyState>("body");foreach(var e in after.Exercises)Assert.That(final.Exercise(e.ExerciseId).KnowledgeMicro,Is.EqualTo(e.KnowledgeMicro+2000000));
            Assert.That(Read<SessionState>("session").TotalGameSeconds,Is.EqualTo(itemTime));Assert.That(InventoryRules.Quantity(Read<InventoryState>("inventory"),EconomyContent.Notebook),Is.Zero);
        }
        [Test,Timeout(660000)] public void G04PublicShopInsteadOfTrainingHasNoEveningKnowledge()
        {
            Start();Until(()=>Read<FlowState>("flow").AnchorId=="afternoon"&&session.ReadView().Actions.Any(o=>o.Id=="choose-entry"&&o.StringValue=="corner-shop"));
            int day=Read<SessionState>("session").Day;var shop=session.ReadView().Actions.Single(o=>o.Id=="choose-entry"&&o.StringValue=="corner-shop");Send(shop.Id,shop.StringValue,shop.IntValue);
            Until(()=>Read<SessionState>("session").Day==day&&Read<SessionState>("session").Phase=="AwaitNextDay");
            Assert.That(Read<ActivityState>("activity").PracticedExercises,Is.Empty);Assert.That(Read<BodyState>("body").Exercises.All(e=>e.KnowledgeMicro==0),Is.True);Reopen();
        }
    }
}

