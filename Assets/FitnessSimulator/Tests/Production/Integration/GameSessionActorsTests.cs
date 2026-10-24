using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using FitnessSimulator.Game.Actors;
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
    public sealed class GameSessionActorsTests
    {
        private string directory;
        private GameSession session;
        [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),"FitnessActorsIntegration",Guid.NewGuid().ToString("N"));session=GameSession.OpenAutomation(directory,7);}
        [TearDown] public void Teardown(){session?.Dispose();}
        private void Send(string id,string value=null)
        {
            var view=session.ReadView();var reply=session.Execute(new FrontendAction { ActionId=id,StringValue=value,OperationId=Guid.NewGuid().ToString("N"),RunId=view.RunId,ExpectedRevision=view.Revision });
            Assert.That(reply.Accepted,Is.True,reply.Message);
        }
        private T Read<T>(string owner,string actor)=>JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner,actor).Json);
        [Test] public void NewRunInitializesFourPrivateActorsAndRestartPreservesThem()
        {
            Send("new-game",TemplateCatalog.DefaultId);Send("save-plan",JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()));
            var root=session.InspectSnapshot();
            foreach(string id in RelationshipRules.CharacterIds)
            {
                Assert.That(Read<SessionState>("session",id).Day,Is.EqualTo(1));Assert.That(Read<LifeActorState>("life",id).ActorId,Is.EqualTo(id));
                Assert.That(Read<BodyState>("body",id).ActorId,Is.EqualTo(id));Assert.That(Read<InventoryState>("inventory",id).ActorId,Is.EqualTo(id));
                Assert.That(Read<ShopState>("shop/"+EconomyContent.Gym,id).Cycle,Is.EqualTo(1));
                Assert.That(Read<RelationshipState>("relationships",id).ActorId,Is.EqualTo(id));
                Assert.That(root.FindFragment("plan",id),Is.Not.Null);
            }
            var before=RelationshipRules.CharacterIds.Select(id=>root.FindFragment("body",id).Json+root.FindFragment("life",id).Json+root.FindFragment("plan",id).Json).ToArray();
            session.Dispose();session=GameSession.OpenAutomation(directory,999);Send("continue-game");
            var after=session.InspectSnapshot();
            Assert.That(RelationshipRules.CharacterIds.Select(id=>after.FindFragment("body",id).Json+after.FindFragment("life",id).Json+after.FindFragment("plan",id).Json).ToArray(),Is.EqualTo(before));
        }
        [TestCase("squat")][TestCase("lateral_raise")]
        public void PersistedNpcTraceContainsCoordinatesAndReplaysIdentically(string exercise)
        {
            var request=new TrainingSetRequest { ActorId=RelationshipRules.Coach,ExerciseId=exercise,SetId="trace",Tier=1,TargetReps=1,Producer=TrainingProducer.NpcPolicy };
            var trace=ActorInputPolicy.Execute(RelationshipRules.Coach,request,7,Enumerable.Repeat(2147483648u,20000).ToArray());
            var saved=JsonUtility.FromJson<ActorInputTrace>(JsonUtility.ToJson(trace));
            Assert.That(saved.Steps.Any(s=>s.HasInput&&s.Y>0),Is.True);
            var kernel=TrainingInputFactory.Begin(request,saved.KernelSeed);
            foreach(var step in saved.Steps){if(step.HasInput)kernel.Submit(step.Input);else kernel.Advance(step.AdvanceMilliseconds);}
            Assert.That(kernel.Result.InputDigest,Is.EqualTo(trace.Result.InputDigest));Assert.That(kernel.Result.EffectiveReps,Is.EqualTo(trace.Result.EffectiveReps));
        }
        [Test] public void ActualNpcSkipBranchPreservesLegalThirdAction()
        {
            Send("new-game",TemplateCatalog.DefaultId);Send("save-plan",JsonUtility.ToJson(PlanCatalog.CreateBenchPressSlice()));
            var root=session.InspectSnapshot();string actor=RelationshipRules.Rival;
            var training=Read<TrainingDayState>("training",actor);var body=Read<BodyState>("body",actor);
            var plan=new[]{"bench_press","lat_pulldown","squat"}.Select((id,i)=>new TrainingPlanEntry { EntryId="e"+i,ExerciseId=id,Sets=ExerciseCatalog.Get(id).Sets[0],Reps=ExerciseCatalog.Get(id).Reps[0] }).ToArray();
            var quota=DayTrainingQuota.Freeze(actor,1,"fixture-plan",plan,training.Progress);
            training.Quota=quota;training.Workout=TrainingSession.Begin("injury-fixture",quota);training.HasQuota=training.HasWorkout=true;
            body.Region("chest_upper").InjuredDay=1;body.Region("chest_upper").ExpiresAtStartOfDay=3;
            body.Region("back_upper").InjuredDay=1;body.Region("back_upper").ExpiresAtStartOfDay=3;
            var flow=new FlowState { ActorId=actor,Day=1,AnchorId="afternoon",AnchorOrder=4,HasTree=true,Tree=SessionContent.Build("afternoon",quota,true),TreeInstanceId="injury-fixture",EntryPhase=EntryPhase.Resolved,SelectedDefinitionId="training-choice",CurrentNodeId="a0s1-play",Cursors=new List<NodeCursor>{new NodeCursor { NodeId="a0s1-play",InstanceId="injury-fixture/play",Phase=NodePhase.Started }} };
            var state=Read<SessionState>("session",actor);state.AnchorIndex=4;
            root.SetFragment("training",actor,1,root.Revision+1,JsonUtility.ToJson(training));root.SetFragment("body",actor,1,root.Revision+1,JsonUtility.ToJson(body));
            root.SetFragment("flow",actor,1,root.Revision+1,JsonUtility.ToJson(flow));root.SetFragment("session",actor,1,root.Revision+1,JsonUtility.ToJson(state));
            string beforeBody=root.FindFragment("body",actor).Json;
            var action=(FrontendAction)Hook("SelectNpcAction",root,actor);Assert.That(action.ActionId,Is.EqualTo("npc-skip-injured"));
            Hook("PrepareActor",root,action,actor);
            var after=JsonUtility.FromJson<TrainingDayState>(root.FindFragment("training",actor).Json);
            var nextFlow=JsonUtility.FromJson<FlowState>(root.FindFragment("flow",actor).Json);
            Assert.That(after.Workout.EntryIndex,Is.EqualTo(2));Assert.That(after.Workout.Phase,Is.EqualTo(WorkoutPhase.PreSet));
            Assert.That(nextFlow.CurrentNodeId,Is.EqualTo("a2s1-story"));Assert.That(after.Workout.SettledSetIds,Is.Empty);
            Assert.That(root.FindFragment("body",actor).Json,Is.EqualTo(beforeBody));
            Assert.Throws<InvalidOperationException>(()=>Hook("PrepareActor",root,action,actor));
        }
        private object Hook(string name,params object[] args)
        {
            try{return typeof(GameSession).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(session,args);}
            catch(TargetInvocationException ex){throw ex.InnerException;}
        }
        private static void Put<T>(RootEnvelope root,string owner,T value)=>root.SetFragment(owner,"player",1,1,JsonUtility.ToJson(value));
        private static T From<T>(RootEnvelope root,string owner)=>JsonUtility.FromJson<T>(root.FindFragment(owner,"player").Json);
        private FrontendAction RelationshipOption(RootEnvelope root,string id)
        {
            var options=new List<FrontendOption>();Hook("AppendRelationshipOptions",root,options);var option=options.Single(o=>o.Id==id);
            return new FrontendAction { ActionId=id,StringValue=option.StringValue,IntValue=option.IntValue };
        }
        private PreparedOutcome PrepareRelationship(RootEnvelope root,FrontendAction action)
        {
            object[] args={root,action,null};Assert.That(Hook("TryPrepareRelationship",args),Is.True);return (PreparedOutcome)args[2];
        }
        [Test] public void GiftRequiredFollowupSurvivesSerializationAndBlocksExitWithoutRefund()
        {
            // Explicit relationship boundary fixture. This proves application hooks, not a natural full-game route.
            var root=new RootEnvelope { RunId="fixture",ContentVersion=SessionContent.Version };
            var relationships=RelationshipRules.Initialize(root.RunId,"player");relationships.Affections[1].Value=100;
            Put(root,"relationships",relationships);Put(root,"session",new SessionState());
            var inventory=InventoryRules.PrepareGrant(InventoryRules.Initialize(root.RunId,"player"),EconomyContent.Bookmark,1);Put(root,"inventory",inventory);
            var progress=TrainingProgress.CreateInitial("player");var quota=DayTrainingQuota.Freeze("player",1,"p",new[]{new TrainingPlanEntry { EntryId="e",ExerciseId="bench_press",Sets=ExerciseCatalog.Get("bench_press").Sets[0],Reps=ExerciseCatalog.Get("bench_press").Reps[0] }},progress);
            var workout=TrainingSession.Begin("w",quota);workout.NextSetSequence=2;workout.SettledSetIds=new[]{"earlier-fixture-group"};
            Put(root,"training",new TrainingDayState { HasQuota=true,HasWorkout=true,Quota=quota,Workout=workout,Progress=progress });
            Put(root,"body",BodyState.Create("player",Enumerable.Repeat(14000000L,21).ToArray(),18000000,50000000,0));
            var definition=new FlowDefinition { Id="fixture-flow",Version="1",Nodes=new List<FlowNode>{new FlowNode { Id="play",DefinitionId="fixture",Handler="play" }},DefaultEntry=new EntryDefinition { DefinitionId="fixture",RootNodeId="play" } };
            Put(root,"flow",new FlowState { HasTree=true,Tree=definition,ActorId="player",Day=1,AnchorId="afternoon",TreeInstanceId="w",EntryPhase=EntryPhase.Resolved,SelectedDefinitionId="fixture",CurrentNodeId="play",Cursors=new List<NodeCursor>{new NodeCursor { NodeId="play",InstanceId="rest",Phase=NodePhase.Started }} });
            Hook("OpenPlayerInteraction",root,workout,new[]{RelationshipRules.Beginner});Hook("MarkRelationshipRest",root,workout);
            var gift=PrepareRelationship(root,RelationshipOption(root,"rel-gift"));
            Assert.That(gift.CheckKind,Is.EqualTo(CheckKind.NormalCompletion));Assert.That(From<InventoryState>(root,"inventory").Items[1].Quantity,Is.Zero);
            Assert.That(From<WorkoutInteraction>(root,"workout-interaction").Consumed,Is.True);Assert.That(From<FlowState>(root,"flow").CurrentNodeId,Is.EqualTo("play"));
            root=new UnityJsonStateCodec().Decode<RootEnvelope>(new UnityJsonStateCodec().Encode(root));
            foreach(string forbidden in new[]{"end-training","rest","next-day","day-step"})
                Assert.Throws<InvalidOperationException>(()=>PrepareRelationship(root,new FrontendAction { ActionId=forbidden }));
            string pending=From<RelationshipState>(root,"relationships").PendingConfessionId;Assert.That(pending,Is.Not.Empty);
            var preview=PrepareRelationship(root,RelationshipOption(root,"rel-confession-preview"));Assert.That(preview.CheckKind,Is.EqualTo(CheckKind.None));
            var declined=PrepareRelationship(root,RelationshipOption(root,"rel-decline"));Assert.That(declined.CheckKind,Is.EqualTo(CheckKind.NormalCompletion));
            Assert.That(From<RelationshipState>(root,"relationships").PendingConfessionId,Is.Empty);Assert.That(From<InventoryState>(root,"inventory").Items[1].Quantity,Is.Zero);
            Assert.That(From<FlowState>(root,"flow").CurrentNodeId,Is.EqualTo("play"));
        }
    }
}
