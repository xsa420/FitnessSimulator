using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Life.Relationships;
using NUnit.Framework;
using UnityEngine;
using static FitnessSimulator.Game.Tests.ConfigurationAcceptance.ConfigurationP1PublicRouteTests;
namespace FitnessSimulator.Game.Tests.ConfigurationAcceptance
{
 // Explicit structural fixture: this does not prove natural affection/gift/romance reachability.
 public sealed class ConfigurationProbabilityProjectionTests
 {
  [Test,Timeout(210000)]public void CurrentGiftProbability()
  {
   var fixture=new ConfigurationP1PublicRouteTests();fixture.Setup();
   try {
    string dir=fixture.CopyBundle("probability");StoryBundle(dir,0);var flow=Doc<FlowContentDocument>(dir,"flow");flow.Events.Single(x=>x.Id=="p1-story").Nodes.Single(x=>x.Handler=="narrative").Call.ChoiceAccess=new[]{new ConfiguredChoiceAccess{ChoiceId="listen",Condition="",CostNodeId="",ProbabilitySource="confession"}};Save(dir,"flow",flow);var package=fixture.Load(dir);
    using(var run=new Run(fixture,"r",package)){
     run.NewGame();run.Session.Dispose();ConfessionSnapshot frozen=null;var codec=new UnityJsonStateCodec();
     using(ContentRuntime.Push(package))using(var store=new LocalCheckpointStore(run.SavePath,codec,expectedContentVersion:SessionContent.Version)){
      var root=store.Load().Root;var dispatcher=new SerialCommandDispatcher(store,codec,root,c=>c.Source==CommandSource.Test,null);
      var reply=dispatcher.Execute(new CommandEnvelope{OperationId="structural-probability/"+Guid.NewGuid().ToString("N"),RunId=root.RunId,ActorId="player",ResourceScope="player",Source=CommandSource.Test,ActionKind="TestPrecondition",ExpectedRevision=root.Revision,Payload="STRUCTURAL fixture: formal frozen confession consumer, not a natural romance route"},candidate=>{
       var relationship=JsonUtility.FromJson<RelationshipState>(candidate.FindFragment("relationships","player").Json);relationship.Affections.Single(x=>x.CharacterId==RelationshipRules.Beginner).Value=100;relationship.PendingConfessionId="structural-gift";
       frozen=ConfessionRules.Freeze(relationship,500000,600000,18000000);
       // Current pending gift is required; no declined or stale history is accepted.
       candidate.SetFragment("relationships","player",1,candidate.Revision+1,JsonUtility.ToJson(relationship));candidate.SetFragment("confession-snapshot","player",1,candidate.Revision+1,JsonUtility.ToJson(frozen));
       return new PreparedOutcome{SemanticKey="structural-frozen-confession",OutcomeKind=OutcomeKind.Success,EventDisposition=EventDisposition.AuxiliaryOnly,CheckKind=CheckKind.None,OpportunityPolicy=OpportunityPolicy.Keep};
      });Assert.That(reply.Status,Is.EqualTo(CommandStatus.Committed));
     }
     File.WriteAllText(Path.Combine(run.Dir,"STRUCTURAL-FIXTURE.txt"),"STRUCTURAL Application consumer fixture, not a natural relationship route. Formal Freeze creates probability from current pending gift. Private BeginNarrative/ProjectNarrative are invoked directly, not gameplay actions. Current-gift legality, persisted reopen and stale-gift rejection only.");
     File.WriteAllText(Path.Combine(run.Dir,"formal-frozen-input.json"),JsonUtility.ToJson(frozen,true));
     run.Session=GameSession.OpenAutomationContent(run.SavePath,1,package);
     string expected=(frozen.ProbabilityThousandths/10m).ToString("0.0",CultureInfo.InvariantCulture)+"%";
     var call=Call();call.StoryId="branch_example";call.ChoiceAccess=new[]{new ConfiguredChoiceAccess{ChoiceId="listen",Condition="",CostNodeId="",ProbabilitySource="confession"}};
     var node=new FlowNode{Id="fixture/story",Handler="narrative",PayloadJson=ContentJson.Serialize(call)};
     var cursor=new NodeCursor{InstanceId="structural-current-gift",Phase=NodePhase.Started};
     var begin=typeof(GameSession).GetMethod("BeginNarrative",BindingFlags.NonPublic|BindingFlags.Instance);
     var project=typeof(GameSession).GetMethod("ProjectNarrative",BindingFlags.NonPublic|BindingFlags.Instance);
     Action<RootEnvelope> check=root=>{
      using(ContentRuntime.Push(package)){
       var life=JsonUtility.FromJson<LifeActorState>(root.FindFragment("life","player").Json);
       begin.Invoke(run.Session,new object[]{root,node,cursor,life,"player"});var view=new FrontendView();var options=new List<FrontendOption>();project.Invoke(run.Session,new object[]{root,node,view,options});
       Assert.That(options.Single(x=>x.Id=="story-choice"&&x.StringValue=="listen").Label,Does.Contain(expected));
      }
     };
     check(JsonUtility.FromJson<RootEnvelope>(JsonUtility.ToJson(run.Session.InspectSnapshot())));
     string before=JsonUtility.ToJson(run.Session.InspectSnapshot());run.Session.Dispose();run.Session=GameSession.OpenAutomationContent(run.SavePath,1,package);Assert.That(JsonUtility.ToJson(run.Session.InspectSnapshot()),Is.EqualTo(before));check(JsonUtility.FromJson<RootEnvelope>(before));
     foreach(string stale in new[]{"different-current-gift",""}){
      var root=JsonUtility.FromJson<RootEnvelope>(before);var rel=JsonUtility.FromJson<RelationshipState>(root.FindFragment("relationships","player").Json);rel.PendingConfessionId=stale;root.SetFragment("relationships","player",1,root.Revision+1,JsonUtility.ToJson(rel));
      var ex=Assert.Throws<TargetInvocationException>(()=>check(root));Assert.That(ex.InnerException,Is.TypeOf<InvalidOperationException>());Assert.That(ex.InnerException.Message,Does.Contain("Stale or foreign confession"));
     }
     Assert.That(JsonUtility.ToJson(run.Read<ConfessionSnapshot>("confession-snapshot")),Is.EqualTo(JsonUtility.ToJson(frozen)));

    }
   }finally{fixture.Finish();}
  }
 }
}
