using FitnessSimulator.Automation;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Life;
using NUnit.Framework;
using UnityEngine;
namespace FitnessSimulator.Game.Tests.Automation {
public sealed class PublicTwoDayUsabilityTests {
 [Serializable]private sealed class Preference{public bool IsPreferred;}
 [Serializable]private sealed class Work{public string SettlementKey,ActorId,AnchorId;public int Day,AffluenceAtExecution;public long Amount;}
 [Serializable]private sealed class LifeProbe{public WalletState Wallet;public NutritionDay Nutrition;public bool HasLastIncome;public Work[] WorkIncomeReceipts;}
 private GameSession s;private string dir;private EvidenceTrace log;private Stopwatch timer;private int commands,steps,meals,planEdits,reopens,workCount;private long initialBalance,paid,earned;private readonly HashSet<string> workKeys=new HashSet<string>();private readonly Dictionary<string,string> chosen=new Dictionary<string,string>();
 private static readonly string[] Actors={"player","rival","beginner","veteran","coach"};
 private T Read<T>(string owner,string actor="player")=>JsonUtility.FromJson<T>(s.InspectSnapshot().FindFragment(owner,actor).Json);
 private void Bound(){Assert.That(timer.Elapsed.TotalSeconds,Is.LessThan(240),"Wall budget");Assert.That(++steps,Is.LessThan(30000),"Step budget");}
 private void Audit(){foreach(var actor in Actors){var life=Read<LifeProbe>("life",actor);Assert.That(life.HasLastIncome,Is.False,"New run must not mint legacy E01 daily income");foreach(var w in life.WorkIncomeReceipts??Array.Empty<Work>()){Assert.That(w.ActorId,Is.EqualTo(actor));Assert.That(new[]{"morning","afternoon"},Does.Contain(w.AnchorId));Assert.That(w.Amount,Is.EqualTo(50L+w.AffluenceAtExecution));if(workKeys.Add(w.SettlementKey)){workCount++;log.WriteLine("work="+JsonUtility.ToJson(w));if(actor=="player"){Assert.That(w.Day,Is.EqualTo(1),"D2 rest has no work pay");earned+=w.Amount;}}}Assert.That((life.WorkIncomeReceipts??Array.Empty<Work>()).Select(w=>w.Day+"/"+w.AnchorId).Distinct().Count(),Is.EqualTo((life.WorkIncomeReceipts??Array.Empty<Work>()).Length));}}
 private FrontendAction Send(FrontendOption o,bool replay=false){Bound();var v=s.ReadView();var a=new FrontendAction{OperationId="usability-two-day/"+(++commands),RunId=v.RunId,ExpectedRevision=v.Revision,ActionId=o.Id,StringValue=o.StringValue,IntValue=o.IntValue};log.WriteLine(JsonUtility.ToJson(v));log.WriteLine(JsonUtility.ToJson(a));var r=AutomationEvidenceSession.Execute(s,a);log.WriteLine(JsonUtility.ToJson(r));Assert.That(r.Accepted,Is.True,r.Message);if(replay){var root=JsonUtility.ToJson(s.InspectSnapshot());Assert.That(AutomationEvidenceSession.Execute(s,a).Accepted,Is.True);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(root));}return a;}
 private void Resume(){var before=JsonUtility.ToJson(s.InspectSnapshot());s.Dispose();s=AutomationEvidenceSession.OpenAutomation(Path.Combine(dir,"save"),1);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(before));reopens++;var o=s.ReadView().Actions.Single(a=>a.Enabled&&a.Id=="continue-game");Send(o);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(before));}
 [Test,Timeout(270000)]public void MealsWorkEveningPlanAndRecoveryAcrossTwoDays(){
  dir=Path.Combine(AutomationEvidence.OutputRoot,"Fitness-Usability-TwoDay",Guid.NewGuid().ToString("N"));AutomationEvidence.RegisterDirectory(dir);s=AutomationEvidenceSession.OpenAutomation(Path.Combine(dir,"save"),1);log=new EvidenceTrace(Path.Combine(dir,"trace.jsonl"));timer=Stopwatch.StartNew();string outcome="Incomplete",error="";bool nav=false,eveningSaved=false;
  try{
   Send(s.ReadView().Actions.First(a=>a.Id=="new-game"&&a.Enabled));var draftView=s.ReadView();Assert.That(draftView.PlanAnchorOptions.Where(a=>a.Enabled).Select(a=>a.Id).OrderBy(x=>x).ToArray(),Is.EqualTo(new[]{"evening","noon"}));var initial=draftView.Actions.First(a=>a.Id=="save-plan"&&a.Enabled);var draft=JsonUtility.FromJson<PlanDraft>(initial.StringValue);Assert.That(draft.Slots.Where(x=>!x.IsRest).All(x=>x.AnchorId=="noon"),Is.True);Send(initial,true);
   initialBalance=Read<LifeProbe>("life").Wallet.Balance;
   foreach(var actor in Actors.Skip(1)){var plan=Read<PlanStateRecord>("plan",actor);string expected=actor=="rival"||actor=="beginner"?"noon":"evening";Assert.That(plan.Active.Draft.Slots.Where(x=>!x.IsRest).All(x=>x.AnchorId==expected),Is.True,actor+" default anchor");}
   for(;;){Bound();Audit();var v=s.ReadView();Assert.That(v.ScreenId,Is.Not.EqualTo("error"),v.Body);var state=Read<SessionState>("session");Assert.That(state.Day,Is.LessThanOrEqualTo(2));var options=v.Actions.Where(a=>a.Enabled).ToArray();var anchors=SessionContent.Anchors(state);string anchor=state.Phase=="Anchors"&&state.AnchorIndex<anchors.Length?anchors[state.AnchorIndex]:"";
    if(state.Day==2&&v.ScreenId=="AwaitNextDay"){outcome="TwoDaysCompleted";break;}
    if(options.Any(a=>a.Id=="save-plan")){Assert.That(anchor,Is.EqualTo("evening"));Assert.That(v.CanOpenAuxiliary,Is.True);}
    if(!nav&&options.Any(a=>a.Id=="open-menu")){var before=JsonUtility.ToJson(s.InspectSnapshot());Send(options.First(a=>a.Id=="open-menu"));Assert.That(s.ReadView().ScreenId,Is.EqualTo("menu"));Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(before));Send(s.ReadView().Actions.First(a=>a.Id=="continue-game"));Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(before));nav=true;continue;}
    if(state.Day==1&&!eveningSaved&&options.Any(a=>a.Id=="save-plan")){var plan=Read<PlanStateRecord>("plan");string active=JsonUtility.ToJson(plan.Active);var save=options.First(a=>a.Id=="save-plan");save.StringValue=JsonUtility.ToJson(new PlanDraft{Mode=PlanMode.DayCycle,Slots=new[]{PlanCatalog.Rest(0)}});Send(save,true);var after=Read<PlanStateRecord>("plan");Assert.That(JsonUtility.ToJson(after.Active),Is.EqualTo(active));Assert.That(after.HasPending,Is.True);Assert.That(after.Pending.EffectiveDay,Is.EqualTo(2));planEdits++;eveningSaved=true;Resume();continue;}
    if(state.Day==2&&state.Phase=="Anchors"){var plan=Read<PlanStateRecord>("plan");Assert.That(plan.Active.Draft.Slots.All(x=>x.IsRest),Is.True);}
    if(options.Any(a=>a.Id=="meal")){
     var mealOptions=options.Where(a=>a.Id=="meal").ToArray();Assert.That(mealOptions.All(a=>!a.RequiresConfirmation),Is.True);string id=anchor=="breakfast"?"porridge_egg":anchor=="lunch"?"home_rice":"home_noodles";var o=mealOptions.Single(a=>a.StringValue==id);if(state.Day==2){Assert.That(chosen[anchor],Is.EqualTo(id));Assert.That(JsonUtility.FromJson<Preference>(JsonUtility.ToJson(o)).IsPreferred,Is.True);Assert.That(mealOptions.Count(a=>JsonUtility.FromJson<Preference>(JsonUtility.ToJson(a)).IsPreferred),Is.EqualTo(1));}
     long wallet=Read<LifeProbe>("life").Wallet.Balance;string before=JsonUtility.ToJson(s.InspectSnapshot());s.ReadView();Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(before),"Projection does not pay automatically");Send(o,true);var life=Read<LifeProbe>("life");var receipt=life.Nutrition.Meals.Single(m=>m.OptionId==id);Assert.That(life.Wallet.Balance,Is.EqualTo(wallet-receipt.Price));var item=LifeContent.Find(receipt.Slot,id);Assert.That(Read<SessionState>("session").LastMessage,Does.Contain(item.DisplayName));Assert.That(Read<SessionState>("session").LastMessage,Does.Contain(receipt.Price.ToString()).And.Contain(receipt.IntakeKcal.ToString()));paid+=receipt.Price;chosen[anchor]=id;meals++;Resume();continue;
    }
    if(v.TrainingInput!=null){Assert.That(PublicEightInputDriver.DriveActiveSet(s,v.TrainingInput.ExerciseId,line=>{Assert.That(timer.Elapsed.TotalSeconds,Is.LessThan(240));log.WriteLine(line);}),Is.EqualTo("AwaitProductionResult"));continue;}
    FrontendOption next=options.FirstOrDefault(a=>a.Id=="begin-set"&&a.StringValue=="auto")??options.FirstOrDefault(a=>a.Id=="begin-set"&&a.StringValue=="manual"&&a.IntValue==0);
    if(next==null&&options.Any(a=>a.Id=="choose-entry"))next=options.FirstOrDefault(a=>a.Id=="choose-entry"&&a.StringValue=="training-choice")??options.First(a=>a.Id=="choose-entry");
    foreach(string id in new[]{"settle","continue","shop-leave","item-use","next-day"})if(next==null)next=options.FirstOrDefault(a=>a.Id==id&&(id!="item-use"||a.StringValue.Contains("\"Skip\":true")));
    if(next!=null)Send(next,next.Id=="continue"&&v.ScreenId.StartsWith("work/"));else{var unknown=options.FirstOrDefault(a=>!new[]{"open-menu","save-plan","competition-menu","rel-dialogue","rel-invite","rel-teach"}.Contains(a.Id));Assert.That(unknown,Is.Null,"MissingHandler "+v.ScreenId);log.WriteLine("advance-ms=0");s.Advance(0);}
   }
   Audit();Assert.That(meals,Is.EqualTo(6));Assert.That(planEdits,Is.EqualTo(1));Assert.That(nav,Is.True);Assert.That(earned,Is.GreaterThan(0),"CoverageMissing: no player work selected in fixed seed");Assert.That(Read<LifeProbe>("life").Wallet.Balance,Is.EqualTo(initialBalance+earned-paid));Resume();
  }catch(Exception ex){error=ex.ToString();throw;}finally{AutomationEvidence.WriteAllText(Path.Combine(dir,"final.json"),JsonUtility.ToJson(s.InspectSnapshot(),true));AutomationEvidence.WriteAllText(Path.Combine(dir,"report.txt"),"outcome="+outcome+"\nmeals="+meals+"\nplanEdits="+planEdits+"\nworkReceipts="+workCount+"\nplayerEarned="+earned+"\nplayerPaid="+paid+"\nreopens="+reopens+"\ncommands="+commands+"\nsteps="+steps+"\nmaxDays=2\nmaxSeconds=240\nexception="+error);log.Dispose();s.Dispose();TestContext.WriteLine(dir);}
 }
}
}
