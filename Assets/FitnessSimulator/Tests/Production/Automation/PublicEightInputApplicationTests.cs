using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;
namespace FitnessSimulator.Game.Tests.Automation {
public sealed class PublicEightInputApplicationTests {
 [TestCase("bench_press")][TestCase("dumbbell_curl")][TestCase("lat_pulldown")][TestCase("triceps_pushdown")]
 [TestCase("squat")][TestCase("hip_thrust")][TestCase("shoulder_press")][TestCase("lateral_raise")]
 [Timeout(660000)]
 public void PublicInputSettlesAndEarnsCapacityExactlyOnce(string exercise) {
  const uint seed=1;const int maxDays=8,maxSteps=100000,maxSeconds=600;
  string dir=Path.Combine(Path.GetTempPath(),"Fitness-Public-Eight",exercise,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  var s=GameSession.OpenAutomation(dir,seed);var clock=Stopwatch.StartNew();int count=0,days=0,inputCommands=0,inputClockCalls=0;string exit="BudgetExhausted";
  using(var trace=new StreamWriter(Path.Combine(dir,"trace.jsonl"))) {
   Func<string,TrainingDayState> training=ignored=>JsonUtility.FromJson<TrainingDayState>(s.InspectSnapshot().FindFragment("training","player").Json);
   Func<long> strength=()=>JsonUtility.FromJson<BodyState>(s.InspectSnapshot().FindFragment("body","player").Json).Regions.Sum(r=>r.StrengthMicro);
   Func<FrontendOption,FrontendAction> send=o=>{var v=s.ReadView();var a=new FrontendAction {OperationId="eight/"+(++count),RunId=v.RunId,ExpectedRevision=v.Revision,ActionId=o.Id,StringValue=o.StringValue,IntValue=o.IntValue};trace.WriteLine(JsonUtility.ToJson(v));trace.WriteLine(JsonUtility.ToJson(a));var reply=s.Execute(a);trace.WriteLine(JsonUtility.ToJson(reply));Assert.That(reply.Accepted,Is.True,reply.Message);return a;};
   try {
    for(int step=0;step<maxSteps && clock.Elapsed.TotalSeconds<maxSeconds;step++) {
     var v=s.ReadView();Assert.That(v.ScreenId,Is.Not.EqualTo("error"),v.Body);var options=v.Actions.Where(a=>a.Enabled).ToArray();
     if(v.TrainingInput!=null){Assert.That(PublicEightInputDriver.DriveActiveSet(s,exercise,line=>{if(line.StartsWith("advance-ms="))inputClockCalls++;if(line.Contains("\"ActionId\":\"training-input\""))inputCommands++;trace.WriteLine(line);}),Is.EqualTo("AwaitProductionResult"));continue;}
     var settle=options.FirstOrDefault(a=>a.Id=="settle");
     if(settle!=null){
      var ready=training(null).Workout;Assert.That(ready.Phase,Is.EqualTo(WorkoutPhase.ResultReady));Assert.That(ready.PendingResult.Producer,Is.EqualTo(TrainingProducer.TrustedAutomationInput));Assert.That(ready.PendingResult.Success,Is.True);Assert.That(ready.PendingResult.EffectiveReps,Is.EqualTo(ready.PendingRequest.TargetReps));
      string setId=ready.PendingRequest.SetId;int tier=ready.PendingRequest.Tier;long before=strength();var command=send(settle);
      Assert.That(strength(),Is.GreaterThan(before));Assert.That(training(null).Workout.SettledSetIds.Count(x=>x==setId),Is.EqualTo(1));Assert.That(training(null).Progress.Exercise(exercise).AutomaticTiers[tier-1],Is.True);
      string saved=JsonUtility.ToJson(s.InspectSnapshot());var retry=s.Execute(command);Assert.That(retry.Accepted,Is.True);Assert.That(JsonUtility.ToJson(s.InspectSnapshot()),Is.EqualTo(saved));exit="RealSetSettledAndQualified";break;
     }
     FrontendOption o=null;
     if(v.ScreenId=="menu")o=options.FirstOrDefault(a=>a.Id=="new-game");
     if(v.ScreenId=="InitialPlan"){o=options.First(a=>a.Id=="save-plan");var slot=PlanCatalog.Training(0,TrainingCombination.Free,exercise);slot.Volume=TrainingVolume.Low;o.StringValue=JsonUtility.ToJson(new PlanDraft {Mode=PlanMode.DayCycle,Slots=new[]{slot}});}
     o=o??options.FirstOrDefault(a=>a.Id=="begin-set"&&a.StringValue=="manual"&&a.IntValue==0);
     if(o==null && options.Any(a=>a.Id=="choose-entry"))o=options.FirstOrDefault(a=>a.Id=="choose-entry"&&a.StringValue=="training-choice")??options.First(a=>a.Id=="choose-entry");
     foreach(string id in new[]{"meal","continue","shop-leave","item-use","next-day"})if(o==null)o=options.FirstOrDefault(a=>a.Id==id);
     if(o!=null){if(o.Id=="next-day")Assert.That(++days,Is.LessThan(maxDays),"CoverageMissing: no eligible set by date budget");send(o);}else {var unknown=options.FirstOrDefault(a=>a.Id!="save-plan"&&a.Id!="open-menu"&&a.Id!="rest"&&a.Id!="end-training");Assert.That(unknown,Is.Null,"MissingHandler: "+v.ScreenId+"/"+unknown?.Id);s.Advance(0);}
    }
    Assert.That(exit,Is.EqualTo("RealSetSettledAndQualified"));
   }catch(Exception ex){exit=ex.ToString();throw;}
   finally{File.WriteAllText(Path.Combine(dir,"final.json"),JsonUtility.ToJson(s.InspectSnapshot(),true));File.WriteAllText(Path.Combine(dir,"report.txt"),"strategy=public-ui-input-v1\nseed=1\nexercise="+exercise+"\nmaxDays=8\nmaxSteps=100000\nmaxSeconds=600\ninputMaxCommandsPerSet=100000\ninputMaxClockCallsPerSet=100000\nbusinessCommands="+count+"\ninputCommands="+inputCommands+"\ninputClockCalls="+inputClockCalls+"\nexit="+exit);s.Dispose();TestContext.WriteLine(dir);}
  }
 }
}
}


