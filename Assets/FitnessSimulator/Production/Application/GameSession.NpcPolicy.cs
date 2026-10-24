using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Actors;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        [Serializable] private sealed class NpcPolicyProgress
        {
            public string WorkoutId="",SetKey="";
            public int ExtraRests;
            public bool TookNormalRest;
            public string[] FailedChallengeExercises=Array.Empty<string>();
        }
        private void InitializeNpcActors(RootEnvelope root)
        {
            var world=Read<SessionState>(root,"session");
            if(world.Day!=1)throw new InvalidOperationException("NPC initialization is new-run only");
            foreach(var initial in ActorPolicies.InitializeFour(root.RunId))
            {
                string id=initial.CharacterId;
                if(root.FindFragment("session",id)!=null)throw new InvalidOperationException("NPC already initialized");
                var seed=DomainRandom.NextUInt(DomainRandom.Create(root.RunId,id,"ActorSession",world.InitialRandomSeed.ToString(CultureInfo.InvariantCulture))).Value|1u;
                var state=new SessionState { Day=1,Phase="Anchors",TemplateId=id,RandomState=seed,InitialRandomSeed=seed,DayStartBody=initial.Body.Clone(),DayStartBalance=initial.Life.Wallet.Balance,EarlyAvailableDay=world.EarlyAvailableDay,LateAvailableDay=world.LateAvailableDay };
                var plan=PlanRules.SaveInitial(new PlanState(root.RunId,id,id+"-plan"),initial.Plan,1,0,Eligibility(world),catalog);
                Write(root,"session",state,id);Write(root,"body",initial.Body,id);Write(root,"life",initial.Life,id);
                Write(root,"plan",PlanPersistence.Export(plan),id);Write(root,"flow",new FlowState { RandomState=seed },id);
                var training=new TrainingDayState { Progress=initial.Training };FreezeQuota(root,state,training,id);Write(root,"training",training,id);
                Write(root,"npc-policy",new NpcPolicyProgress(),id);
                Write(root,"relationships",RelationshipRules.Initialize(root.RunId,id,ActorContent.Get(id).RelationshipRef),id);
            }
        }
        private DomainRandomState NpcStream(RootEnvelope root,string actorId,string owner,string purpose)
        {
            var stream=root.FindFragment(owner,actorId)==null?DomainRandom.Create(root.RunId,actorId,purpose,Read<SessionState>(root,"session",actorId).InitialRandomSeed.ToString(CultureInfo.InvariantCulture)):Read<DomainRandomState>(root,owner,actorId);
            DomainRandom.Validate(stream);
            if(stream.RunId!=root.RunId||stream.ActorId!=actorId||stream.Purpose!=purpose)throw new InvalidOperationException("NPC random scope mismatch");
            return stream;
        }
        private TrainingSetResult CompleteNpcInput(RootEnvelope root,SessionState state,WorkoutSnapshot workout,PreparedOutcome outcome)
        {
            var request=workout.PendingRequest;string id=request.ActorId;ActorPolicies.Index(id);
            if(request.Producer!=TrainingProducer.NpcPolicy||workout.Quota.Day!=state.Day)throw new InvalidOperationException("Invalid NPC input context");
            var start=NpcStream(root,id,"npc-input-random","NpcInput");var seedDraw=DomainRandom.NextUInt(start);
            var values=new List<uint>();var states=new List<DomainRandomState>{seedDraw.State};ActorInputTrace trace=null;
            int wanted=1024;
            while(trace==null)
            {
                while(values.Count<wanted){var draw=DomainRandom.NextUInt(states[states.Count-1]);values.Add(draw.Value);states.Add(draw.State);}
                try { trace=ActorInputPolicy.Execute(id,request,seedDraw.Value,values.ToArray()); }
                catch(InvalidOperationException ex) when(ex.Message=="NeedNpcInputSamples"&&wanted<1048576){wanted=checked(wanted*2);}
            }
            var after=states[trace.SamplesConsumed];Write(root,"npc-input-random",after,id);state.SetSeed=seedDraw.Value;
            outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/inputs",Kind="NpcInputTrace",Json=codec.Encode(new NpcInputEvidence { RunId=root.RunId,ActorId=id,Day=state.Day,WorkoutId=workout.WorkoutId,StartDrawIndex=start.DrawIndex,EndDrawIndex=after.DrawIndex,Request=request,Trace=trace }) });
            var policy=Read<NpcPolicyProgress>(root,"npc-policy",id);
            if(policy.WorkoutId!=workout.WorkoutId)policy=new NpcPolicyProgress { WorkoutId=workout.WorkoutId };
            if(request.SetType==TrainingSetType.StrengthChallenge&&!trace.Result.Success&&!policy.FailedChallengeExercises.Contains(request.ExerciseId))policy.FailedChallengeExercises=policy.FailedChallengeExercises.Concat(new[]{request.ExerciseId}).ToArray();
            Write(root,"npc-policy",policy,id);return trace.Result;
        }
        [Serializable] private sealed class NpcInputEvidence
        {
            public string RunId,ActorId,WorkoutId;
            public int Day;
            public long StartDrawIndex,EndDrawIndex;
            public TrainingSetRequest Request;
            public ActorInputTrace Trace;
        }
        private FrontendAction SelectNpcAction(RootEnvelope root,string actorId)
        {
            ActorPolicies.Index(actorId);
            var state=Read<SessionState>(root,"session",actorId);var flow=Read<FlowState>(root,"flow",actorId);
            var training=Read<TrainingDayState>(root,"training",actorId);var body=Read<BodyState>(root,"body",actorId);
            var action=new FrontendAction { RunId=root.RunId,ExpectedRevision=root.Revision,OperationId=root.RunId+"/npc/"+actorId+"/"+root.Revision };
            if(state.Phase=="DayEnd"||state.Phase=="EnterDay")action.ActionId="day-step";
            else if(state.Phase=="AwaitNextDay")throw new InvalidOperationException("NPC awaits world day barrier");
            else if(state.Phase!="Anchors")throw new InvalidOperationException("Unsupported NPC phase");
            else if(state.AnchorIndex>=SessionContent.Anchors(state).Length||flow.Tree==null||flow.Day!=state.Day||flow.AnchorId!=SessionContent.Anchors(state)[state.AnchorIndex])action.ActionId="resolve-anchor";
            else if(flow.EntryPhase==EntryPhase.Finished)action.ActionId="finish-anchor";
            else if(flow.EntryPhase==EntryPhase.ChoicePending)
            {
                var options=flow.ChoiceDefinitionIds.Select(id=>{var entry=flow.Tree.Entries.Single(e=>e.DefinitionId==id);string h=flow.Tree.Nodes.Single(n=>n.Id==entry.RootNodeId).Handler;return new ActorEventOption { EventId=id,Kind=h=="training"?ActorEventKind.Training:h=="calm"||h=="walk"?ActorEventKind.Rest:h=="study-evening"?ActorEventKind.EveningStudy:h=="study-item"||h=="study-all"?ActorEventKind.AllDayStudy:h=="shop-enter"||h=="shop"?ActorEventKind.Shop:ActorEventKind.Ordinary };}).ToArray();
                var random=NpcStream(root,actorId,"npc-choice-random","NpcChoice");var decision=ActorPolicies.ChooseEvent(actorId,options,body.StressMicro,DayPlan(root,state,actorId).Slot.IsRest,DomainRandom.PeekUInt(random));
                if(decision.RandomSamplesConsumed>0)Write(root,"npc-choice-random",DomainRandom.NextUInt(random).State,actorId);
                action.ActionId="choose-entry";action.StringValue=decision.EventId;
            }
            else
            {
                var cursor=FlowEngine.Current(flow);if(cursor==null)throw new InvalidOperationException("NPC missing current node");
                var node=flow.Tree.Nodes.Single(n=>n.Id==cursor.NodeId);
                if(cursor.Phase==NodePhase.NotStarted)action.ActionId="start-node";
                else if(node.Handler=="narrative") { var story=FitnessSimulator.Game.Narrative.NarrativeRuntime.Project(Read<FitnessSimulator.Game.Narrative.NarrativeState>(root,"narrative",actorId)); var choice=story.Choices.FirstOrDefault(c=>c.Enabled); action.ActionId=choice==null?"story-continue":"story-choice"; action.StringValue=choice?.Id; }
                else if(node.Handler=="meal"){action.ActionId="meal";action.StringValue=ActorPolicies.ChooseMeal(actorId,Meal(flow.AnchorId),Read<LifeActorState>(root,"life",actorId).Wallet.Balance);}
                else if(node.Handler=="settle")action.ActionId="settle";
                else if(node.Handler=="shop")
                {
                    var activity=Read<ActivityState>(root,"activity",actorId);var inventory=Read<InventoryState>(root,"inventory",actorId);var life=Read<LifeActorState>(root,"life",actorId);
                    var shop=Read<ShopState>(root,"shop/"+activity.ShopId,actorId);
                    string product=InventoryRules.Quantity(inventory,EconomyContent.Notebook)>=ActorContent.Policy(actorId).WantedNotebookUnits?"":ActorPolicies.ChooseNotebookPackage(actorId,shop,life.Wallet.Balance,body.Exercises.Min(e=>e.KnowledgeMicro));
                    if(product=="")action.ActionId="shop-leave";
                    else { action.ActionId="shop-buy";action.StringValue=codec.Encode(new ShopCommand { Context=EconomyAt(root,state,actorId,activity.VisitId,activity.PurchaseOrdinal),Quote=ShopRules.QuotePurchase(life,inventory,shop,product,1) }); }
                }
                else if(node.Handler=="study-item"||node.Handler=="evening-item")
                {
                    var activity=Read<ActivityState>(root,"activity",actorId);var inventory=Read<InventoryState>(root,"inventory",actorId);
                    action.ActionId="item-use";action.StringValue=codec.Encode(new ItemCommand { Context=EconomyAt(root,state,actorId,cursor.InstanceId),PointId=activity.ItemPoint.PointId,ItemId=EconomyContent.Notebook,Skip=InventoryRules.Quantity(inventory,EconomyContent.Notebook)==0||body.Exercises.All(e=>e.KnowledgeMicro>=100000000) });
                }
                else if(node.Handler=="play")
                {
                    var w=training.Workout;
                    if(w.Phase==WorkoutPhase.Playing)action.ActionId="result-ready";
                    else if(w.Phase==WorkoutPhase.PreSet)
                    {
                        var entry=w.Quota.Entries[w.EntryIndex];var definition=ExerciseCatalog.Get(entry.Plan.ExerciseId);var progress=training.Progress.Exercise(entry.Plan.ExerciseId);
                        var policy=Read<NpcPolicyProgress>(root,"npc-policy",actorId);
                        string setKey=w.WorkoutId+"/"+w.NextSetSequence;
                        if(policy.WorkoutId!=w.WorkoutId)policy=new NpcPolicyProgress { WorkoutId=w.WorkoutId };
                        if(policy.SetKey!=setKey){policy.SetKey=setKey;policy.ExtraRests=0;policy.TookNormalRest=false;}
                        var regions=definition.TargetRegionIds.Select(body.Region).ToArray();
                        bool eligible=progress.HighestUnlockedTier<4&&regions.Sum(r=>r.StrengthMicro)>=definition.Threshold(Math.Min(4,progress.HighestUnlockedTier+1))*1000000;
                        var decision=ActorPolicies.ChooseSet(actorId,body.StressMicro,regions.Max(r=>r.TemporaryFatigueMicro),body.Exercise(entry.Plan.ExerciseId).ProficiencyMicro,regions.Any(r=>r.IsInjured),eligible,progress.AutomaticTiers[progress.HighestUnlockedTier-1],w.SetOrdinal==1,policy.FailedChallengeExercises.Contains(entry.Plan.ExerciseId),policy.ExtraRests);
                        if(decision!=ActorSetDecision.EndWorkout&&decision!=ActorSetDecision.SkipInjuredExercise&&w.SettledSetIds.Length>0&&!policy.TookNormalRest){action.ActionId="rest";action.IntValue=ActorContent.Policy(actorId).NormalRestSeconds;policy.TookNormalRest=true;}
                        else if(decision==ActorSetDecision.LongRest){action.ActionId="rest";action.IntValue=ActorContent.Policy(actorId).ExtraRestSeconds;policy.ExtraRests++;}
                        else if(decision==ActorSetDecision.SkipInjuredExercise)action.ActionId="npc-skip-injured";
                        else if(decision==ActorSetDecision.EndWorkout)action.ActionId="end-training";
                        else { action.ActionId="begin-set";action.StringValue=decision==ActorSetDecision.AutoCapacity?"auto":"manual";action.IntValue=decision==ActorSetDecision.Challenge?1:0; }
                        Write(root,"npc-policy",policy,actorId);
                    }
                    else throw new InvalidOperationException("Unexpected NPC workout phase");
                }
                else action.ActionId="continue";
            }
            return action;
        }
    }
}
