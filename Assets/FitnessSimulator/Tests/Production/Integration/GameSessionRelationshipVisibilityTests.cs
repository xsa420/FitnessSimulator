using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Competition;
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
    public sealed class GameSessionRelationshipVisibilityTests
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
        // I evidence: projection boundaries, not natural progression/romance/elapsed-day evidence.
        private static T From<T>(RootEnvelope root,string owner)=>JsonUtility.FromJson<T>(root.FindFragment(owner,"player").Json);
        private string RowsUnchanged()
        {
            string before=JsonUtility.ToJson(session.InspectSnapshot());
            var first=session.ReadView();var second=session.ReadView();
            Assert.That(second.BodyRows,Is.EqualTo(first.BodyRows));
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(before),"ReadView must not commit, draw, consume, or change clocks");
            Assert.That(first.BodyRows[0],Is.EqualTo("人物关系"));
            Assert.That(Array.IndexOf(first.BodyRows,"身体状态"),Is.GreaterThan(0));
            return string.Join("\n",first.BodyRows);
        }
        private void SummaryFixture(int day,Action<RootEnvelope> edit=null)
        {
            Fixture("I: projection-only calendar and relationship state for target day "+day+"; not actual day progression",root=>{
                // I fixture only: preserve life receipts/wallet and construct valid nutrition days, without simulating a world day cut.
                var life=LifeRules.NormalizeAfterLoad(From<LifeActorState>(root,"life"));
                Assert.That(day,Is.GreaterThanOrEqualTo(life.Nutrition.Day),"Summary fixture must not rewind income/nutrition history");
                while(life.Nutrition.Day<day)life=LifeRules.StartNutritionDay(life,life.Nutrition.Day+1);
                Put(root,"life",life);
                var state=From<SessionState>(root,"session");state.Day=day;state.Phase="InitialPlan";Put(root,"session",state);
                var relation=From<RelationshipState>(root,"relationships");relation.Day=day;Put(root,"relationships",relation);
                edit?.Invoke(root);
            });
        }
        [TestCase(1,false)][TestCase(7,false)][TestCase(8,false)][TestCase(8,true)]
        public void PublicIdentityUsesCommittedOpeningNotCalendarAlone(int day,bool opened)
        {
            Start();SummaryFixture(day,root=>{
                var competition=CompetitionRules.Create(root.RunId,"player",RelationshipRules.Rival);
                if(opened)competition=CompetitionRules.Open(competition,8,new[]{0,0,0},new[]{0,0,0}).State;
                Put(root,"competition",competition);
            });
            string rows=RowsUnchanged();
            Assert.That(rows.Contains("周竞 · 竞争者"),Is.EqualTo(opened));
            Assert.That(rows,Does.Contain("林悦 · 初学者 · 恋爱对象 · 好感 0/100（陌生）"));
            Assert.That(rows,Does.Contain("陈砺 · 老手"));Assert.That(rows,Does.Contain("许衡 · 教练"));
            Assert.That(rows,Does.Contain("周一／周三／周六·下午"));
            Assert.That(rows,Does.Contain("计划不保证实际到场"));
            Assert.That(rows,Does.Not.Contain("力量均值区间"));Assert.That(rows,Does.Not.Contain("体脂区间"));
        }
        [TestCase(99,0)][TestCase(99,1)][TestCase(100,0)][TestCase(100,1)][Timeout(660000)]
        public void PublicGiftOptionRespectsAffectionAndStockAndSubmissionRechecks(int affection,int quantity)
        {
            Start();Until(()=>session.ReadView().Actions.Any(o=>o.Enabled&&o.Id=="rel-dialogue"&&o.Label.StartsWith("林悦",StringComparison.Ordinal)));
            Fixture("I: only affection and bookmark quantity at real public rest",root=>{
                var s=From<RelationshipState>(root,"relationships");s.Affections.Single(a=>a.CharacterId==RelationshipRules.Beginner).Value=affection;Put(root,"relationships",s);
                var inventory=From<InventoryState>(root,"inventory");inventory.Items.Single(i=>i.ItemId==EconomyContent.Bookmark).Quantity=quantity;Put(root,"inventory",inventory);
            });
            string rows=RowsUnchanged();Assert.That(rows,Does.Contain("好感 "+affection+"/100"));Assert.That(rows,Does.Contain("纪念书签："+quantity+"件"));
            var gift=session.ReadView().Actions.SingleOrDefault(o=>o.Id=="rel-gift");
            Assert.That(gift!=null,Is.EqualTo(affection==100));
            Assert.That(session.ReadView().Body,Does.Contain("本次互动：剩余1次"));
            Assert.That(session.ReadView().Body,Does.Not.Contain("已知安排"));
            if(gift==null)return;
            Assert.That(gift.Label,Is.EqualTo("赠送纪念书签1件（持有"+quantity+"）·开启表白"));
            Assert.That(gift.Enabled,Is.EqualTo(quantity==1));
            if(quantity==0)
            {
                Assert.That(gift.DisabledReason,Is.EqualTo("纪念书签不足，需要1件"));
                string before=JsonUtility.ToJson(Read<WorkoutInteraction>("workout-interaction"));
                var view=session.ReadView();var reply=session.Execute(new FrontendAction { OperationId=Guid.NewGuid().ToString("N"),RunId=view.RunId,ExpectedRevision=view.Revision,ActionId=gift.Id,StringValue=gift.StringValue });
                Assert.That(reply.Accepted,Is.False);Assert.That(JsonUtility.ToJson(Read<WorkoutInteraction>("workout-interaction")),Is.EqualTo(before));
                Assert.That(InventoryRules.Quantity(Read<InventoryState>("inventory"),EconomyContent.Bookmark),Is.Zero);
            }
            else
            {
                Send(gift.Id,gift.StringValue,gift.IntValue);
                Assert.That(RowsUnchanged(),Does.Contain("待完成表白对话"));
                Assert.That(RowsUnchanged(),Does.Contain("纪念书签：0件"));
                Assert.That(session.ReadView().Body,Does.Contain("本次互动：已用"));
            }
        }
        [Test] public void PendingAndActiveBuddyRemainDistinctAcrossDomainDayBoundary()
        {
            Start();SummaryFixture(10,root=>{
                var s=From<RelationshipState>(root,"relationships");s.CurrentBuddy=RelationshipRules.Coach;s.PendingBuddy=RelationshipRules.Beginner;s.PendingBuddyDay=11;Put(root,"relationships",s);
            });
            string before=RowsUnchanged();
            Assert.That(before,Does.Contain("当前搭子：许衡；待生效：林悦，第11日生效"));
            Assert.That(before,Does.Contain("周一／周三／周六·下午"));
            Assert.That(before.Split(new[]{"当前安排跟随你的有效计划"},StringSplitOptions.None).Length,Is.EqualTo(2));
            var advanced=RelationshipRules.AdvanceDay(Read<RelationshipState>("relationships"),11);
            SummaryFixture(11,root=>Put(root,"relationships",advanced));
            string after=RowsUnchanged();Assert.That(after,Does.Contain("当前搭子：林悦；待生效：无待变更"));
            Assert.That(after,Does.Contain("周一／周三／周五／周日·晚上"));
            Assert.That(after.Split(new[]{"当前安排跟随你的有效计划"},StringSplitOptions.None).Length,Is.EqualTo(2));
        }
        [TestCase(10,false)][TestCase(17,false)][TestCase(18,true)]
        public void FailedDay10ConfessionUsesAuthoritativeDay18(int day,bool ended)
        {
            Start();var s=RelationshipRules.Initialize(session.InspectSnapshot().RunId,"player");s.Day=10;s.Affections[1].Value=100;s.PendingConfessionId="explicit-gift-fixture";
            var snapshot=ConfessionRules.Freeze(s,0,0,50000000);
            var result=ConfessionRules.Prepare(s,snapshot,uint.MaxValue);
            Assert.That(result.Receipt.Success,Is.False);Assert.That(result.Relationships.NextConfessionDay,Is.EqualTo(18));
            result.Relationships.Day=day;
            SummaryFixture(day,root=>Put(root,"relationships",result.Relationships));
            string rows=RowsUnchanged();Assert.That(rows,Does.Contain("可再次尝试：第18日（仍需满好感、书签和相遇）"));
            Assert.That(rows.Contains("冷却已结束"),Is.EqualTo(ended));
        }
        [Test] public void EstablishedRomanceSuppressesRetryAndPendingText()
        {
            Start();SummaryFixture(18,root=>{
                var s=From<RelationshipState>(root,"relationships");s.RomanceEstablished=true;s.NextConfessionDay=18;s.Affections[1].Value=100;Put(root,"relationships",s);
            });
            string rows=RowsUnchanged();Assert.That(rows,Does.Contain("恋爱关系已建立"));
            Assert.That(rows,Does.Not.Contain("可再次尝试"));Assert.That(rows,Does.Not.Contain("冷却已结束"));Assert.That(rows,Does.Not.Contain("待完成表白"));
        }
    }
}