using System;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Relationships;
using NUnit.Framework;
using static FitnessSimulator.Game.Tests.ConfigurationAcceptance.ConfigurationP1PublicRouteTests;
namespace FitnessSimulator.Game.Tests.ConfigurationAcceptance
{
 public sealed class ConfigurationP2PublicRouteTests
 {
  private ConfigurationP1PublicRouteTests fixture;
  [SetUp]public void Setup(){fixture=new ConfigurationP1PublicRouteTests();fixture.Setup();}
  [TearDown]public void Finish()=>fixture.Finish();
  private static ConfiguredNode Node(string id,string handler,EventCallPayload call,string next="")=>new ConfiguredNode{Id=id,Handler=handler,Title="P2 "+id,Next=next,Branches=Array.Empty<FlowEdge>(),Call=call};
  private static ConfiguredEffect Effect(string kind,long amount,bool scale=false,string id="p2")=>new ConfiguredEffect{Kind=kind,Target="self",Id=id,Amount=amount,PositiveScalable=scale};
  private static void Install(string dir,ConfiguredNode[] nodes,AnchorConsumption[] consume=null)
  {
   var f=Doc<FlowContentDocument>(dir,"flow");f.Events=f.Events.Concat(new[]{new ConfiguredEvent{Id="p2-event",Title="P2配置事件",Root=nodes[0].Id,Nodes=nodes}}).ToArray();
   var anchor=f.Anchors.Single(x=>x.Id=="morning");anchor.Entries=Array.Empty<EventBinding>();var binding=Binding("p2-event");binding.Consume=consume??Array.Empty<AnchorConsumption>();anchor.Defaults=new[]{binding};Save(dir,"flow",f);
  }
  [Test,Timeout(210000)]public void SocialModesConsumeFilesWithOneRandomCheckAndUnscaledCosts()
  {
   foreach(string mode in new[]{"noCheck","check"}){
    uint? expectedRandom=null;
    foreach(int variant in new[]{0,1}){
     string dir=fixture.CopyBundle(mode+variant);var call=Call();var effects=new[]{Effect("money",20+20*variant,mode=="noCheck"),Effect("knowledge",1000000,mode=="noCheck","bench_press"),Effect("money",-7,false,"p2-cost")};
     call.Social=new ConfiguredSocial{Mode=mode,BaseProbabilityMillionths=mode=="check"?1000000:0,SuccessEffects=effects,FailureEffects=mode=="check"?new[]{Effect("money",-99)}:Array.Empty<ConfiguredEffect>()};
     Install(dir,new[]{Node("social","social",call)});var package=fixture.Load(dir);
     using(var r=new Run(fixture,mode+variant+"-run",package)){
      r.NewGame();r.Until(v=>v.ScreenId.StartsWith("social/")&&v.Actions.Any(x=>x.Id=="continue"&&x.Enabled));var before=r.Read<LifeActorState>("life").Wallet.Balance;var body=r.Read<BodyState>("body");long k=body.Exercises.Single(x=>x.ExerciseId=="bench_press").KnowledgeMicro;uint random=r.Read<SessionState>("session").RandomState;
      decimal multiplier=1;using(ContentRuntime.Push(package)){if(mode=="noCheck"){var range=BodyContent.Attribute("base_charm");decimal x=Math.Max(0,Math.Min(1,(decimal)(BodyMetrics.GetEffectiveCharmMicro(body)-range.MinMicro)/(range.MaxMicro-range.MinMicro)));multiplier+=RelationshipContent.Definition.Social.PositiveLinearMillionths/1000000m*x;}}
      r.Send(r.Session.ReadView().Actions.Single(x=>x.Id=="continue"&&x.Enabled),true);
      Assert.That(r.Read<LifeActorState>("life").Wallet.Balance-before,Is.EqualTo((long)Math.Round((20+20*variant)*multiplier,MidpointRounding.AwayFromZero)-7));Assert.That(r.Read<BodyState>("body").Exercises.Single(x=>x.ExerciseId=="bench_press").KnowledgeMicro-k,Is.EqualTo((long)Math.Round(1000000*multiplier,MidpointRounding.AwayFromZero)));
      if(mode=="check"){random^=random<<13;random^=random>>17;random^=random<<5;}Assert.That(r.Read<SessionState>("session").RandomState,Is.EqualTo(random));if(expectedRandom.HasValue)Assert.That(random,Is.EqualTo(expectedRandom.Value));expectedRandom=random;r.Reopen(package,package);
     }
    }
   }
  }
  [Test,Timeout(210000)]public void StoryAccessCostAndReadSkipPersistAcrossReopenAndNewGame()
  {
   foreach(string choice in new[]{"listen","ask"}){
    string dir=fixture.CopyBundle(choice);StoryBundle(dir,5);var f=Doc<FlowContentDocument>(dir,"flow");var ev=f.Events.Single(x=>x.Id=="p1-story");var story=ev.Nodes.Single(x=>x.Handler=="narrative");story.Call.ChoiceAccess=new[]{new ConfiguredChoiceAccess{ChoiceId="listen",Condition="",CostNodeId="heard-effect",ProbabilitySource=""},new ConfiguredChoiceAccess{ChoiceId="ask",Condition="",CostNodeId="asked-effect",ProbabilitySource=""}};
    f.Conditions=f.Conditions.Concat(new[]{new NamedCondition{Id="p2-unavailable",Rule=new ConditionDefinition{Op="False",Key="",Value="",Children=Array.Empty<ConditionDefinition>()}}}).ToArray();story.Call.ChoiceAccess.Single(x=>x.ChoiceId!=choice).Condition="p2-unavailable";
    foreach(var n in ev.Nodes.Where(x=>x.Handler=="effects"))n.Call.Effects=new[]{Effect("money-cost",n.Id=="heard-effect"?10:20)};Save(dir,"flow",f);var package=fixture.Load(dir);
    using(var r=new Run(fixture,choice+"-run",package)){
     r.NewGame();r.Until(v=>v.ScreenId=="narrative"&&v.Actions.Any(x=>x.Id=="story-choice"));var option=r.Session.ReadView().Actions.Single(x=>x.Id=="story-choice"&&x.StringValue==choice);Assert.That(option.Enabled,Is.True);Assert.That(r.Session.ReadView().Actions.Single(x=>x.Id=="story-choice"&&x.StringValue!=choice).Enabled,Is.False);Assert.That(option.Label,Does.Contain(choice=="listen"?"10":"20"));long before=r.Read<LifeActorState>("life").Wallet.Balance;r.Send(option);Assert.That(r.Session.ReadView().Actions.Any(x=>x.Id=="story-skip-read"&&x.Enabled),Is.False);
     r.Send(r.Session.ReadView().Actions.Single(x=>x.Id=="story-continue"&&x.Enabled),true);Assert.That(r.Read<LifeActorState>("life").Wallet.Balance,Is.EqualTo(before-(choice=="listen"?10:20)));r.Reopen(package,package);r.Send(r.Session.ReadView().Actions.Single(x=>x.Id=="open-menu"&&x.Enabled));r.NewGame();r.Until(v=>v.ScreenId=="narrative"&&v.Actions.Any(x=>x.Id=="story-choice"));r.Send(r.Session.ReadView().Actions.Single(x=>x.Id=="story-choice"&&x.StringValue==choice));r.Send(r.Session.ReadView().Actions.Single(x=>x.Id=="story-skip-read"&&x.Enabled),true);
    }
   }
  }
  [Test,Timeout(210000)]public void WeekdayConsumptionAndNextAnchorDoNotExecuteAbandonedSuccessor()
  {
   string dir=fixture.CopyBundle("weekday");var forbidden=Call(999);Install(dir,new[]{Node("skip","next-anchor",Call(),"unreached"),Node("unreached","effects",forbidden)},new[]{new AnchorConsumption{AnchorId="morning",DayOffset=0,Weekday="Monday"},new AnchorConsumption{AnchorId="afternoon",DayOffset=0,Weekday="Monday"}});var package=fixture.Load(dir);
   using(var r=new Run(fixture,"weekday-run",package)){
    r.NewGame();r.Until(v=>v.ScreenId.StartsWith("next-anchor/")&&v.Actions.Any(x=>x.Id=="continue"&&x.Enabled));var flow=r.Read<FlowState>("flow");Assert.That(flow.Occupancies.Count(x=>x.ActorId=="player"&&x.Day==1&&x.AnchorId=="afternoon"),Is.EqualTo(1));long money=r.Read<LifeActorState>("life").Wallet.Balance;r.Send(r.Session.ReadView().Actions.Single(x=>x.Id=="continue"&&x.Enabled),true);Assert.That(r.Read<LifeActorState>("life").Wallet.Balance,Is.EqualTo(money));Assert.That(r.Read<FlowState>("flow").EntryPhase,Is.EqualTo(EntryPhase.Finished));r.Reopen(package,package);
   }
  }
 }
}
