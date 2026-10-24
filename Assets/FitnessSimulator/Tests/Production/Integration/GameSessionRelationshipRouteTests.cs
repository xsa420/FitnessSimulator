using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class GameSessionRelationshipRouteTests
    {
        private GameSession session;
        private string directory;
        private int commands;
        private readonly StringBuilder evidence=new StringBuilder();
        private readonly Stopwatch clock=new Stopwatch();
        [SetUp] public void Open()
        {
            directory=Path.Combine(Path.GetTempPath(),"Fitness-Relationship-Route",Guid.NewGuid().ToString("N"));
            session=GameSession.OpenAutomation(directory,1);clock.Start();
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
            Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(before),"Restart must preserve the committed root");Send("continue-game");
        }
        [Test, Timeout(660000)] public void RealCoachPaymentAppliesOnlyToLaterEffectiveSetAndSurvivesRestartRetry()
        {
            Send("new-game",TemplateCatalog.DefaultId);
            var slots=Enumerable.Range(0,7).Select(PlanCatalog.Rest).ToArray();
            foreach(int day in new[]{0,2,4,6}) { slots[day]=PlanCatalog.Training(day,TrainingCombination.Free,"bench_press");slots[day].AnchorId="evening";slots[day].Volume=TrainingVolume.Low; }
            Send("save-plan",JsonUtility.ToJson(new PlanDraft { Mode=PlanMode.NaturalWeek,Slots=slots }));
            bool paid=false,verified=false;string restedFor="",paidWorkout="";long firstKnowledge=0,beforePaidSetKnowledge=0;int paidSequence=0;
            for(int step=0;step<50000&&!verified;step++)
            {
                Bound();var view=session.ReadView();Assert.That(view.ScreenId,Is.Not.EqualTo("error"),view.Body+" / "+session.Diagnostic);
                var state=Read<SessionState>("session");if(state.Day>14)break;
                var training=Read<TrainingDayState>("training");var w=training.HasWorkout?training.Workout:null;
                var options=view.Actions.Where(a=>a.Enabled).ToArray();FrontendOption choice=null;
                if(view.BenchPress!=null)
                {
                    var live=view.BenchPress;
                    if(view.TrainingInput!=null&&!view.TrainingInput.IsPreparing&&!live.IsComplete)
                    {
                        bool left=live.Balance>.02,right=live.Balance<-.02;
                        if(left!=live.LeftHeld)Send(left?"bench-left-down":"bench-left-up");
                        if(right!=live.RightHeld)Send(right?"bench-right-down":"bench-right-up");
                    }
                    session.Advance(.01);continue;
                }
                if(options.Any(a=>a.Id=="settle"))
                {
                    long before=Read<BodyState>("body").Exercise("bench_press").KnowledgeMicro;
                    bool afterPayment=paid&&w.WorkoutId==paidWorkout&&w.PendingRequest.WorkoutSetSequence>=paidSequence;
                    int effective=w.PendingResult.EffectiveReps;Assert.That(w.PendingResult.Producer,Is.EqualTo(TrainingProducer.TrustedAutomationInput));
                    Send("settle");
                    long after=Read<BodyState>("body").Exercise("bench_press").KnowledgeMicro;
                    if(!paid) { Assert.That(after,Is.EqualTo(before),"First capacity set must not receive future coach knowledge");firstKnowledge=after; }
                    else if(afterPayment&&effective>0)
                    {
                        Assert.That(after,Is.EqualTo(Math.Min(100000000,beforePaidSetKnowledge+3000000)));
                        Assert.That(Read<TeachingLedger>("teaching-ledger").Awards.Single(a=>a.ExerciseId=="bench_press").NominalMicro,Is.EqualTo(3000000));
                        evidence.AppendLine("VERIFIED day="+state.Day+" workout="+paidWorkout+" sequence="+w.PendingRequest.WorkoutSetSequence+" v="+effective+" knowledge="+before+"->"+after);
                        Reopen();verified=true;
                    }
                    continue;
                }
                if(options.Any(a=>a.Id=="rel-teach")&&!paid)
                {
                    var teach=options.Single(a=>a.Id=="rel-teach");long balance=Read<LifeActorState>("life").Wallet.Balance;
                    Assert.That(balance,Is.GreaterThanOrEqualTo(80));Assert.That(Read<WorkoutInteraction>("workout-interaction").PresentCharacterIds,Does.Contain(RelationshipRules.Coach));
                    Assert.That(Read<TeachingLedger>("teaching-ledger").Awards,Is.Empty);
                    paidSequence=w.NextSetSequence;paidWorkout=w.WorkoutId;
                    var command=Send(teach.Id,teach.StringValue,teach.IntValue);paid=true;
                    Assert.That(Read<LifeActorState>("life").Wallet.Balance,Is.EqualTo(balance-80));
                    var interaction=Read<WorkoutInteraction>("workout-interaction");Assert.That(interaction.Consumed,Is.True);
                    Assert.That(interaction.PaidTeaching.Single().FirstEligibleWorkoutSetSequence,Is.EqualTo(paidSequence));
                    Assert.That(Read<BodyState>("body").Exercise("bench_press").KnowledgeMicro,Is.EqualTo(firstKnowledge),"Payment cannot retroactively award first-set knowledge");
                    Reopen();string saved=JsonUtility.ToJson(session.InspectSnapshot());
                    Assert.That(session.Execute(command).Accepted,Is.True,"Original operation retry must find its receipt");
                    Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(saved),"Retry must not deduct, consume or check completion twice");
                    continue;
                }
                if(options.Any(a=>a.Id=="begin-set"))
                {
                    string restKey=w.WorkoutId+"/"+w.NextSetSequence;
                    if(w.SettledSetIds.Length>0&&restedFor!=restKey)
                    {
                        choice=options.Single(a=>a.Id=="rest"&&a.IntValue==180);restedFor=restKey;
                    }
                    else if(w.SettledSetIds.Length>0&&!paid)
                    {
                        evidence.AppendLine("No coach interaction day="+state.Day+" present="+string.Join(",",Read<WorkoutInteraction>("workout-interaction").PresentCharacterIds));
                        choice=options.Single(a=>a.Id=="end-training");
                    }
                    else
                    {
                        beforePaidSetKnowledge=Read<BodyState>("body").Exercise("bench_press").KnowledgeMicro;
                        choice=options.Single(a=>a.Id=="begin-set"&&a.IntValue==0&&a.StringValue=="manual");
                    }
                }
                else if(options.Any(a=>a.Id=="meal"))choice=options.First(a=>a.Id=="meal"&&new[]{"porridge_egg","home_rice","home_noodles"}.Contains(a.StringValue));
                else if(options.Any(a=>a.Id=="choose-entry"))choice=options.FirstOrDefault(a=>a.Id=="choose-entry"&&a.StringValue=="training-choice")??options.First(a=>a.Id=="choose-entry");
                else if(options.Any(a=>a.Id=="shop-leave"))choice=options.First(a=>a.Id=="shop-leave");
                else if(options.Any(a=>a.Id=="item-use"))choice=options.First(a=>a.Id=="item-use"&&a.StringValue.Contains("\"Skip\":true"));
                else if(options.Any(a=>a.Id=="continue"))choice=options.First(a=>a.Id=="continue");
                else if(options.Any(a=>a.Id=="next-day"))
                {
                    if(paid)Assert.Fail("CoverageMissing: paid workout ended without a later effective set");
                    choice=options.First(a=>a.Id=="next-day");
                }
                if(choice!=null)Send(choice.Id,choice.StringValue,choice.IntValue);else session.Advance(0);
            }
            Assert.That(verified,Is.True,"CoverageMissing: no real coach payment and later effective set within D14 / 50000 steps; see route evidence");
        }
    }
}
