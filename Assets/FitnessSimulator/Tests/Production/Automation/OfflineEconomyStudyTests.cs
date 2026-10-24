using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using NUnit.Framework;
using UnityEngine;
namespace FitnessSimulator.Game.Tests.Automation {
 public sealed class OfflineEconomyStudyTests {
  [Serializable] private sealed class Buy { public PurchaseQuote Quote; }
  [Serializable] private sealed class Item { public bool Skip; }
  private static T Read<T>(GameSession s,string owner) => JsonUtility.FromJson<T>(s.InspectSnapshot().FindFragment(owner,"player").Json);
  [Test, Timeout(660000)] public void FixedSeedPurchaseNotebookThenStudyWithRecovery() {
   const uint seed=1; const int maxCommands=20000, maxSeconds=600;
   int maxDays=int.TryParse(Environment.GetEnvironmentVariable("FITNESS_ECONOMY_DAYS"),out int d)?d:14;
   string dir=Path.Combine(Path.GetTempPath(),"Fitness-Economy-Study",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
   GameSession s=GameSession.OpenAutomation(dir,seed); var watch=Stopwatch.StartNew();
   int ordinal=0,days=0,meals=0,recoveries=0,study=0; bool purchased=false,used=false; string outcome="BudgetExhausted";
   using(var log=new StreamWriter(Path.Combine(dir,"trace.jsonl"))) {
    Action<FrontendOption> send=o=>{
     var v=s.ReadView(); log.WriteLine(JsonUtility.ToJson(v));
     var a=new FrontendAction {OperationId="economy/"+(++ordinal),RunId=v.RunId,ExpectedRevision=v.Revision,ActionId=o.Id,StringValue=o.StringValue,IntValue=o.IntValue};
     Assert.That(ordinal,Is.LessThanOrEqualTo(maxCommands),"CommandBudgetExhausted"); log.WriteLine(JsonUtility.ToJson(a)); var r=s.Execute(a); log.WriteLine(JsonUtility.ToJson(r)); Assert.That(r.Accepted,Is.True,r.Message);
    };
    Action restart=()=>{string before=JsonUtility.ToJson(s.InspectSnapshot()); s.Dispose();s=GameSession.OpenAutomation(dir,seed);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(before));recoveries++;send(s.ReadView().Actions.Single(a=>a.Id=="continue-game"));};
    try {
     for(int step=0;step<maxCommands && watch.Elapsed.TotalSeconds<maxSeconds;step++) {
      var v=s.ReadView();log.WriteLine(JsonUtility.ToJson(v)); Assert.That(v.ScreenId,Is.Not.EqualTo("error"),v.Body);
      var options=v.Actions.Where(a=>a.Enabled).ToArray(); FrontendOption o=null;
      if(v.ScreenId=="menu")o=options.FirstOrDefault(a=>a.Id=="new-game");
      if(v.ScreenId=="InitialPlan") {o=options.First(a=>a.Id=="save-plan");o.StringValue=JsonUtility.ToJson(new PlanDraft {Mode=PlanMode.DayCycle,Slots=new[]{PlanCatalog.Rest(0)}});}
      if(o==null && options.Any(a=>a.Id=="choose-entry")) {
       var choices=options.Where(a=>a.Id=="choose-entry").ToArray();
       o= purchased?choices.FirstOrDefault(a=>a.StringValue=="all-day-study"):choices.FirstOrDefault(a=>a.StringValue=="corner-shop"||a.StringValue=="gym-shop"); o=o??choices.First();
      }
      if(o==null && options.Any(a=>a.Id=="meal")) {
       o=options.FirstOrDefault(a=>a.Id=="meal" && new[]{"porridge_egg","home_rice","home_noodles"}.Contains(a.StringValue));
       Assert.That(o,Is.Not.Null,"Required normal meal unavailable"); send(o); meals++;restart();continue;
      }
      if(o==null && options.Any(a=>a.Id=="shop-leave")) {
       // Conservative reserve uses only the public wallet status, never diagnostic state.
       var match=System.Text.RegularExpressions.Regex.Match(v.Status??"",@"余额\s+(\d+)");
       long wallet=match.Success?long.Parse(match.Groups[1].Value):0;
       if(!purchased) o=options.FirstOrDefault(a=>a.Id=="shop-buy" && JsonUtility.FromJson<Buy>(a.StringValue).Quote.TotalUnits==1 && JsonUtility.FromJson<Buy>(a.StringValue).Quote.ProductId.EndsWith("note_1") && wallet-JsonUtility.FromJson<Buy>(a.StringValue).Quote.TotalPrice>=100);
       if(o!=null) {var q=JsonUtility.FromJson<Buy>(o.StringValue).Quote;long before=Read<LifeActorState>(s,"life").Wallet.Balance;long count=InventoryRules.Quantity(Read<InventoryState>(s,"inventory"),EconomyContent.Notebook);send(o);Assert.That(Read<LifeActorState>(s,"life").Wallet.Balance,Is.EqualTo(before-q.TotalPrice));Assert.That(InventoryRules.Quantity(Read<InventoryState>(s,"inventory"),EconomyContent.Notebook),Is.EqualTo(count+1));purchased=true;restart();continue;}
       o=options.First(a=>a.Id=="shop-leave");
      }
      if(o==null && options.Any(a=>a.Id=="item-use")) {
       o=options.First(a=>a.Id=="item-use");bool skip=JsonUtility.FromJson<Item>(o.StringValue).Skip;
       var before=Read<BodyState>(s,"body").Exercises.Select(e=>e.KnowledgeMicro).ToArray();long count=InventoryRules.Quantity(Read<InventoryState>(s,"inventory"),EconomyContent.Notebook);send(o);
       if(!skip){Assert.That(InventoryRules.Quantity(Read<InventoryState>(s,"inventory"),EconomyContent.Notebook),Is.EqualTo(count-1));var after=Read<BodyState>(s,"body").Exercises;for(int i=0;i<before.Length;i++)Assert.That(after[i].KnowledgeMicro,Is.EqualTo(Math.Min(100000000,before[i]+2000000)));used=true;}
       restart();continue;
      }
      if(o==null && v.ScreenId.StartsWith("study-all/") && options.Any(a=>a.Id=="continue")) {
       var before=Read<BodyState>(s,"body").Exercises.Select(e=>e.KnowledgeMicro).ToArray();send(options.First(a=>a.Id=="continue"));var after=Read<BodyState>(s,"body").Exercises;for(int i=0;i<before.Length;i++)Assert.That(after[i].KnowledgeMicro,Is.EqualTo(Math.Min(100000000,before[i]+12000000)));study++;restart();continue;
      }
      if(o==null && options.Any(a=>a.Id=="next-day")) {days++;restart();if(purchased&&used&&study>0){outcome="PurchaseUseStudyReached";break;}Assert.That(days,Is.LessThan(maxDays),"CoverageMissing: route not reached by day budget");o=s.ReadView().Actions.First(a=>a.Id=="next-day");}
      o=o??options.FirstOrDefault(a=>a.Id=="continue");
      if(o!=null)send(o);else {var unknown=options.FirstOrDefault(a=>a.Id!="open-menu"&&a.Id!="save-plan");Assert.That(unknown,Is.Null,"MissingHandler: "+v.ScreenId+"/"+unknown?.Id);s.Advance(0);}
     }
     Assert.That(outcome,Is.EqualTo("PurchaseUseStudyReached"));Assert.That(meals,Is.EqualTo(days*3));
    } catch(Exception ex){outcome=ex.ToString();throw;}
    finally{File.WriteAllText(Path.Combine(dir,"final.json"),JsonUtility.ToJson(s.InspectSnapshot(),true));File.WriteAllText(Path.Combine(dir,"report.txt"),"strategy=economy-study-v1\nseed=1\nmaxCommands="+maxCommands+"\nmaxSeconds="+maxSeconds+"\nelapsedSeconds="+watch.Elapsed.TotalSeconds+"\nmaxDays="+maxDays+"\ndays="+days+"\nrecoveries="+recoveries+"\ncommands="+ordinal+"\nexit="+outcome+"\nmissing=H7200,total meal cost/calories,stale-operation retry,shop package stock delta");s.Dispose();TestContext.WriteLine(dir);}
   }
  }
 }
}



