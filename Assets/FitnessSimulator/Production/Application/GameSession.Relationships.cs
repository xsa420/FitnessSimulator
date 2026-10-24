using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        [Serializable] private sealed class RelationshipRestWindow
        {
            public string WorkoutId;
            public int NextSetSequence;
            public bool Open;
        }
        [Serializable] private sealed class RelationshipCommand
        {
            public string RunId,ActorId,WorkoutId,TargetId,GiftId;
            public int Day;
        }
        private string RelationshipSemanticKey(CommandEnvelope envelope)
        {
            var request=codec.Decode<RequestPayload>(envelope.Payload);
            var command=codec.Decode<RelationshipCommand>(request.StringValue);
            if(command==null||command.RunId!=envelope.RunId||command.ActorId!=Actor||envelope.ActorId!=Actor||envelope.ResourceScope!=Actor||command.Day<1||string.IsNullOrWhiteSpace(command.WorkoutId))throw new ArgumentException("Foreign relationship request");
            if(envelope.ActionKind=="rel-dialogue"||envelope.ActionKind=="rel-invite"||envelope.ActionKind=="rel-teach"||envelope.ActionKind=="rel-gift")return command.WorkoutId+"/interaction";
            if(string.IsNullOrWhiteSpace(command.GiftId))throw new ArgumentException("Missing gift identity");
            if(envelope.ActionKind=="rel-confess")return command.GiftId+"/confession";
            if(envelope.ActionKind=="rel-confession-preview")return command.GiftId+"/preview";
            if(envelope.ActionKind=="rel-decline")return command.GiftId+"/decline";
            throw new ArgumentException("Unknown relationship semantic action");
        }
        private void InitializeRelationships(RootEnvelope root,string actorId=Actor)
        {
            if(root.FindFragment("relationships",actorId)!=null)throw new InvalidOperationException("Relationships already initialized");
            Write(root,"relationships",RelationshipRules.Initialize(root.RunId,actorId,TemplateCatalog.All.Single(t=>t.Id==Read<SessionState>(root,"session").TemplateId).RelationshipInitialRef),actorId);
        }
        private void AdvanceRelationships(RootEnvelope root,int day,string actorId=Actor)
        {
            var old=Read<RelationshipState>(root,"relationships",actorId);
            if(old.Day==day)return;
            Write(root,"relationships",RelationshipRules.AdvanceDay(old,day),actorId);
        }
        private void OpenPlayerInteraction(RootEnvelope root,WorkoutSnapshot workout,string[] actualPresentIds)
        {
            var s=RelationshipRules.NormalizeAfterLoad(Read<RelationshipState>(root,"relationships"));
            if(workout.Quota.ActorId!=Actor||workout.Quota.Day!=s.Day)throw new InvalidOperationException("Player presence scope mismatch");
            if(root.FindFragment("workout-interaction",Actor)!=null)
            {
                var prior=Read<WorkoutInteraction>(root,"workout-interaction");
                if(prior.WorkoutId==workout.WorkoutId)
                {
                    if(!prior.PresentCharacterIds.SequenceEqual(actualPresentIds))throw new InvalidOperationException("Presence already frozen");
                    return;
                }
            }
            Write(root,"workout-interaction",RelationshipRules.OpenWorkout(s,workout.WorkoutId,actualPresentIds));
            Write(root,"teaching-ledger",TeachingLedgerRules.Initialize(root.RunId,Actor,s.Day,workout.WorkoutId));
            Write(root,"relationship-rest",new RelationshipRestWindow { WorkoutId=workout.WorkoutId,NextSetSequence=workout.NextSetSequence });
        }
        private void MarkRelationshipRest(RootEnvelope root,WorkoutSnapshot workout)
        {
            var w=Read<WorkoutInteraction>(root,"workout-interaction");
            if(w.WorkoutId!=workout.WorkoutId||workout.Phase!=WorkoutPhase.PreSet)throw new InvalidOperationException("Invalid interaction rest");
            Write(root,"relationship-rest",new RelationshipRestWindow { WorkoutId=w.WorkoutId,NextSetSequence=workout.NextSetSequence,Open=workout.SettledSetIds.Length>0 });
        }
        private void FreezePlayerBenefits(RootEnvelope root,WorkoutSnapshot workout)
        {
            var s=Read<RelationshipState>(root,"relationships");var w=Read<WorkoutInteraction>(root,"workout-interaction");
            if(workout.Phase!=WorkoutPhase.PreSet||w.WorkoutId!=workout.WorkoutId||s.PendingConfessionId!="")throw new InvalidOperationException("Unresolved interaction before group");
            var benefits=RelationshipRules.FreezeBenefits(s,w,workout.WorkoutId+"/benefits/"+workout.NextSetSequence,workout.NextSetSequence,workout.WorkoutId+"/presence");
            Write(root,"set-benefits",benefits);
            Write(root,"relationship-rest",new RelationshipRestWindow { WorkoutId=workout.WorkoutId,NextSetSequence=workout.NextSetSequence });
        }
        private TrainingSettlement SettlePlayerTeaching(RootEnvelope root,SessionState state,TrainingDayState training,BodyState body,IReadOnlyDictionary<string,uint> rolls,PreparedOutcome outcome)
        {
            var workout=training.Workout;var benefits=Read<SetBenefitsSnapshot>(root,"set-benefits");
            var flow=Read<FlowState>(root,"flow");
            var context=new M2WorkoutContext { RunId=root.RunId,ActorId=Actor,Day=state.Day,WorkoutId=workout.WorkoutId,ScheduleRevision=workout.Quota.PlanRevision,ArrivalAnchor=flow.AnchorId,Workout=workout,HasSetBenefits=true,Benefits=benefits };
            var claim=TrainingSessionM2.PrepareTeachingClaim(context);
            var prepared=TeachingLedgerRules.Prepare(Read<TeachingLedger>(root,"teaching-ledger"),claim);
            var settled=TrainingSessionM2.Settle(context,body,training.Progress,rolls,prepared.Resolution);
            Write(root,"teaching-ledger",prepared.Ledger);Write(root,"teaching-resolution",prepared.Resolution);
            outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="TeachingResolution",Json=codec.Encode(prepared.Resolution) });
            return settled.Settlement;
        }
        private bool LegalRelationshipRest(RootEnvelope root,WorkoutSnapshot workout)
        {
            if(workout==null||workout.Phase!=WorkoutPhase.PreSet||root.FindFragment("relationship-rest",Actor)==null)return false;
            var window=Read<RelationshipRestWindow>(root,"relationship-rest");var flow=Read<FlowState>(root,"flow");var cursor=FlowEngine.Current(flow);
            return window.Open&&window.WorkoutId==workout.WorkoutId&&window.NextSetSequence==workout.NextSetSequence&&cursor?.Phase==NodePhase.Started&&flow.Tree.Nodes.Single(n=>n.Id==cursor.NodeId).Handler=="play";
        }
        private bool TryPrepareRelationship(RootEnvelope root,FrontendAction action,out PreparedOutcome outcome)
        {
            RequireRelationshipContinuation(root,action);
            outcome=null;if(!action.ActionId.StartsWith("rel-",StringComparison.Ordinal))return false;
            var s=RelationshipRules.NormalizeAfterLoad(Read<RelationshipState>(root,"relationships"));var state=Read<SessionState>(root,"session");
            var workout=Read<TrainingDayState>(root,"training").Workout;
            var command=codec.Decode<RelationshipCommand>(action.StringValue);
            if(command==null||command.RunId!=root.RunId||command.ActorId!=Actor||command.Day!=state.Day||workout==null||command.WorkoutId!=workout.WorkoutId)throw new ArgumentException("Stale relationship opportunity");
            if((action.ActionId=="rel-confess"||action.ActionId=="rel-confession-preview"||action.ActionId=="rel-decline")&&(string.IsNullOrWhiteSpace(command.GiftId)||command.GiftId!=s.PendingConfessionId))throw new ArgumentException("Stale gift opportunity");
            bool legal=LegalRelationshipRest(root,workout);
            if(!legal)throw new InvalidOperationException("仅在已完成休息的组间互动");
            var flow=Read<FlowState>(root,"flow");var cursor=FlowEngine.Current(flow);
            outcome=Outcome(cursor.InstanceId+"/"+action.ActionId,"互动已保存。");
            if(action.ActionId=="rel-confession-preview")
            {
                outcome.SemanticKey=command.GiftId+"/preview";
                if(root.FindFragment("confession-snapshot",Actor)!=null&&Read<ConfessionSnapshot>(root,"confession-snapshot").GiftId==s.PendingConfessionId)return true;
                var b=Read<BodyState>(root,"body");
                Write(root,"confession-snapshot",ConfessionRules.Freeze(s,b.BaseCharmMicro/100,BodyMetrics.GetThinBuildIndexMicro(b),b.BodyFatMicro));return true;
            }
            if(action.ActionId=="rel-decline") { Write(root,"relationships",ConfessionRules.Decline(s));outcome.SemanticKey=command.GiftId+"/decline";outcome.Result="已送出的礼物保留，今天暂不表白。";EmitRelationshipResponse(root,RelationshipContent.Definition.Romance.DeclineEventId,command.GiftId,outcome);outcome.CheckKind=CheckKind.NormalCompletion;outcome.EventDisposition=EventDisposition.CompleteNode;return true; }
            if(action.ActionId=="rel-confess")
            {
                var snapshot=Read<ConfessionSnapshot>(root,"confession-snapshot");
                var random=root.FindFragment("confession-random",Actor)==null?DomainRandom.Create(root.RunId,Actor,"Confession",state.InitialRandomSeed.ToString(CultureInfo.InvariantCulture)):Read<DomainRandomState>(root,"confession-random");
                DomainRandom.Validate(random);if(random.RunId!=root.RunId||random.ActorId!=Actor||random.Purpose!="Confession")throw new InvalidOperationException("Confession random scope mismatch");
                var draw=DomainRandom.NextUInt(random);var result=ConfessionRules.Prepare(s,snapshot,draw.Value);
                Write(root,"relationships",result.Relationships);Write(root,"confession-random",draw.State);Write(root,"confession-result",result.Receipt);
                outcome.SemanticKey=snapshot.GiftId+"/confession";outcome.Result=result.Receipt.Success?"恋爱关系成立。":"她想再相处一段时间。纪念书签已消耗，不退还。可再次尝试：第"+result.Relationships.NextConfessionDay+"日（仍需满好感、书签和相遇）。";
                outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="Confession",Json=codec.Encode(result.Receipt) });
                EmitRelationshipResponse(root,result.ResponseEventId,snapshot.GiftId,outcome);
                outcome.CheckKind=CheckKind.NormalCompletion;outcome.EventDisposition=EventDisposition.CompleteNode;return true;
            }
            if(s.PendingConfessionId!="")throw new InvalidOperationException("请先完成赠礼后对话");
            var interaction=Read<WorkoutInteraction>(root,"workout-interaction");
            var c=new EconomyContext { RunId=root.RunId,ActorId=Actor,Day=state.Day,EventInstanceId=cursor.InstanceId,TransactionOrdinal=0 };
            RelationshipPreparation prepared;
            switch(action.ActionId)
            {
                case "rel-dialogue":prepared=RelationshipRules.PrepareDialogue(s,interaction,c,command.TargetId,action.IntValue,legal);break;
                case "rel-invite":prepared=RelationshipRules.PrepareInvite(s,interaction,c,command.TargetId,legal);break;
                case "rel-teach":prepared=RelationshipRules.PrepareTeaching(s,interaction,Read<LifeActorState>(root,"life"),c,legal,workout.NextSetSequence,root.RunId+"/commit/"+(root.Revision+1));break;
                case "rel-gift":prepared=RelationshipRules.PrepareGift(s,interaction,Read<InventoryState>(root,"inventory"),c,legal);break;
                default:throw new InvalidOperationException("Unknown relationship action");
            }
            if(prepared.Status!=EconomyStatus.Prepared)throw new InvalidOperationException("互动未完成："+prepared.Status);
            Write(root,"relationships",prepared.Relationships);Write(root,"workout-interaction",prepared.Interaction);
            if(prepared.Payment!=null)Write(root,"life",prepared.Payment.Life);
            if(prepared.Delivery!=null)Write(root,"inventory",prepared.Delivery.Inventory);
            EmitRelationshipResponse(root,prepared.ResponseEventId,workout.WorkoutId+"/response",outcome);
            EmitRelationshipResponse(root,prepared.FamiliarResponseEventId,workout.WorkoutId+"/familiar",outcome);
            outcome.SemanticKey=workout.WorkoutId+"/interaction";
            outcome.CheckKind=CheckKind.NormalCompletion;outcome.EventDisposition=EventDisposition.CompleteNode;
            outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="RelationshipInteraction",Json=codec.Encode(new RelationshipEvidence { Day=state.Day,WorkoutId=workout.WorkoutId,Action=action.ActionId,CharacterId=command.TargetId,Payment=prepared.Payment?.Receipt,DeliveryKey=prepared.Delivery?.SemanticKey }) });
            return true;
        }
        [Serializable] private sealed class RelationshipEvidence { public int Day;public string WorkoutId,Action,CharacterId,DeliveryKey;public PaymentReceipt Payment; }
        [Serializable] private sealed class RelationshipResponseRecord { public string EventId, InstanceId, ContentHash, Text; }
        private void EmitRelationshipResponse(RootEnvelope root,string eventId,string instanceId,PreparedOutcome outcome)
        {
            if(string.IsNullOrEmpty(eventId))return;
            var definition=FlowContent.Current.Events.Single(e=>e.Id==eventId);
            var story=FitnessSimulator.Game.Narrative.NarrativeRuntime.Reveal(FitnessSimulator.Game.Narrative.NarrativeRuntime.Begin(definition.Nodes.Single().Call.StoryId,instanceId,new FitnessSimulator.Game.Narrative.NarrativeContext()));
            var projection=FitnessSimulator.Game.Narrative.NarrativeRuntime.Project(story);
            string text=(string.IsNullOrEmpty(projection.Speaker)?"":projection.Speaker+"：")+projection.Text;
            outcome.Result=(string.IsNullOrEmpty(outcome.Result)?"":outcome.Result+"\n")+text;
            outcome.Appends.Add(new LogAppend { StreamId="run/"+root.RunId+"/records",Kind="RelationshipResponse",Json=codec.Encode(new RelationshipResponseRecord { EventId=eventId,InstanceId=instanceId,ContentHash=runContent.Hash,Text=text }) });
        }
        private void RequireRelationshipContinuation(RootEnvelope root,FrontendAction action)
        {
            if(root.FindFragment("relationships",Actor)==null)return;
            var s=Read<RelationshipState>(root,"relationships");
            if(s.PendingConfessionId!=""&&!new[]{"rel-confession-preview","rel-confess","rel-decline","open-menu","continue-game","retry-storage"}.Contains(action.ActionId))throw new InvalidOperationException("请先完成赠礼后的对话，可选择今天暂不说。");
        }
        private void AppendRelationshipOptions(RootEnvelope root,List<FrontendOption> actions)
        {
            if(root.FindFragment("relationships",Actor)==null)return;
            var workout=Read<TrainingDayState>(root,"training").Workout;if(!LegalRelationshipRest(root,workout))return;
            var s=Read<RelationshipState>(root,"relationships");
            Func<string,string> payload=target=>codec.Encode(new RelationshipCommand { RunId=root.RunId,ActorId=Actor,WorkoutId=workout.WorkoutId,Day=s.Day,TargetId=target,GiftId=s.PendingConfessionId });
            if(s.PendingConfessionId!="")
            {
                actions.Clear();
                if(root.FindFragment("confession-snapshot",Actor)==null||Read<ConfessionSnapshot>(root,"confession-snapshot").GiftId!=s.PendingConfessionId)actions.Add(Option("rel-confession-preview","认真表白 · 查看概率",payload(RelationshipRules.Beginner)));
                else { var snap=Read<ConfessionSnapshot>(root,"confession-snapshot");var option=Option("rel-confess","确认表白 · 成功率 "+(snap.ProbabilityThousandths/10m).ToString("0.0",CultureInfo.InvariantCulture)+"%",payload(RelationshipRules.Beginner));option.RequiresConfirmation=true;option.ConfirmationText=option.Label;actions.Add(option); }
                actions.Add(Option("rel-decline","今天先不说",payload(RelationshipRules.Beginner)));return;
            }
            var w=Read<WorkoutInteraction>(root,"workout-interaction");if(w.Consumed)return;
            foreach(string id in w.PresentCharacterIds)
            {
                int affection=s.Affections.Single(a=>a.CharacterId==id).Value;
                string name=RelationshipPublicName(id);var content=RelationshipContent.Definition;
                foreach(var dialogue in RelationshipContent.Character(id).Dialogues.Where(d=>affection>=d.MinimumAffection).OrderBy(d=>d.Option))
                    actions.Add(Option("rel-dialogue",name+" · "+dialogue.Text,payload(id),dialogue.Option));
                if(affection>=content.InviteThreshold)actions.Add(Option("rel-invite",name+" · 邀为明日起搭子",payload(id)));
                if(id==RelationshipRules.Coach&&s.CurrentBuddy!=id)
                {
                    var teach=Option("rel-teach",affection>=content.FreeTeachingThreshold?"免费教导 · 仅后续有效组":"教导"+content.TeachingPrice+" · 仅后续有效组",payload(id));teach.RequiresConfirmation=true;
                    teach.ConfirmationText="使用本次唯一互动机会；仅后续有效组获得教导，提前结束不退款。";actions.Add(teach);
                }
                if(id==content.Romance.TargetCharacterId&&affection>=content.Romance.RequiredAffection&&!s.RomanceEstablished&&stateDay(root)>=s.NextConfessionDay)
                {
                    long quantity=InventoryRules.Quantity(Read<InventoryState>(root,"inventory"),content.Romance.GiftItemId);
                    var gift=Option("rel-gift","赠送纪念书签"+content.Romance.GiftQuantity+"件（持有"+quantity+"）·开启表白",payload(id));
                    gift.Enabled=quantity>=content.Romance.GiftQuantity;gift.DisabledReason=gift.Enabled?"":"纪念书签不足，需要"+content.Romance.GiftQuantity+"件";actions.Add(gift);
                }
            }
        }
        // Basic identities/plans are public; rival role follows the existing competition entry disclosure.
        private string[] ProjectRelationshipSummary(RootEnvelope root)
        {
            if(root.FindFragment("relationships",Actor)==null)return Array.Empty<string>();
            var s=RelationshipRules.NormalizeAfterLoad(Read<RelationshipState>(root,"relationships"));
            bool rivalRolePublic=false;
            if(root.FindFragment(CompetitionOwner,Actor)!=null)
            {
                var edition=ReadCompetition(root).Current;
                // Same predicate as CompetitionQueries.Entries.EntryVisible; no new calendar threshold.
                rivalRolePublic=edition.Opened||edition.Ordinal>1;
            }
            var lines=new List<string>{"人物关系", "当前搭子："+RelationshipPublicName(s.CurrentBuddy)+"；待生效："+
                (s.PendingBuddy==""?"无待变更":RelationshipPublicName(s.PendingBuddy)+"，第"+s.PendingBuddyDay+"日生效")};
            long quantity=InventoryRules.Quantity(Read<InventoryState>(root,"inventory"),EconomyContent.Bookmark);
            lines.Add("纪念书签："+quantity+"件");
            string[] identities={rivalRolePublic?"竞争者":"","初学者 · 恋爱对象","老手","教练"};
            var presets=new[]{NpcPlanPreset.Rival,NpcPlanPreset.Beginner,NpcPlanPreset.Veteran,NpcPlanPreset.Coach};
            string[] weekdays={"周一","周二","周三","周四","周五","周六","周日"};
            foreach(string id in RelationshipRules.CharacterIds)
            {
                int index=Array.IndexOf(RelationshipRules.CharacterIds,id);
                int affection=s.Affections.Single(a=>a.CharacterId==id).Value;
                string tier=RelationshipContent.TierName(affection);
                lines.Add(RelationshipPublicName(id)+(identities[index]==""?"":" · "+identities[index])+" · 好感 "+affection+"/100（"+tier+"）"+
                    (id==RelationshipRules.Beginner&&s.RomanceEstablished?" · 恋爱关系已建立":""));
                if(s.CurrentBuddy==id)lines.Add("已知安排：当前安排跟随你的有效计划");
                else
                {
                    var slots=PlanCatalog.CreateNpcDefault(presets[index]).Slots.Where(slot=>!slot.IsRest);
                    lines.Add("已知安排："+string.Join("；",slots.GroupBy(slot=>slot.AnchorId).Select(group=>
                        string.Join("／",group.Select(slot=>weekdays[slot.Index]))+"·"+SessionContent.Name(group.Key))));
                }
            }
            lines.Add("计划不保证实际到场。");
            if(!s.RomanceEstablished)
            {
                if(s.PendingConfessionId!="")lines.Add("待完成表白对话");
                else if(s.NextConfessionDay>1)lines.Add((stateDay(root)>=s.NextConfessionDay?"冷却已结束；":"")+
                    "可再次尝试：第"+s.NextConfessionDay+"日（仍需满好感、书签和相遇）");
            }
            return lines.ToArray();
        }
        private static string RelationshipPublicName(string id)
        {
            if(string.IsNullOrEmpty(id))return "无";
            int index=Array.IndexOf(RelationshipRules.CharacterIds,id);
            if(index<0)throw new ArgumentException("Unknown relationship character");
            return FitnessSimulator.Game.Actors.ActorContent.Get(id).Name;
        }
        private string ProjectRelationshipRestSummary(RootEnvelope root)
        {
            if(root.FindFragment("relationships",Actor)==null||root.FindFragment("workout-interaction",Actor)==null)return "";
            var workout=Read<TrainingDayState>(root,"training").Workout;
            if(!LegalRelationshipRest(root,workout))return "";
            var s=Read<RelationshipState>(root,"relationships");var w=Read<WorkoutInteraction>(root,"workout-interaction");
            return "本次互动："+(w.Consumed?"已用":"剩余1次")+"；当前搭子："+RelationshipPublicName(s.CurrentBuddy);
        }
        private int stateDay(RootEnvelope root)=>Read<SessionState>(root,"session").Day;
    }
}
