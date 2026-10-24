using System;
using System.Linq;
using FitnessSimulator.Game.Life.Economy;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Life.Relationships
{
    [Serializable] public sealed class AffectionEntry
    {
        public string CharacterId;
        public int Value;
        public bool FamiliarTextGranted;
    }
    [Serializable] public sealed class RelationshipState
    {
        public string RunId, ActorId;
        public int Day=1, PendingBuddyDay, NextConfessionDay=1;
        public long Revision;
        public string CurrentBuddy="", PendingBuddy="", PendingConfessionId="";
        public bool RomanceEstablished;
        public AffectionEntry[] Affections;
    }
    [Serializable] public sealed class WorkoutInteraction
    {
        public string RunId, ActorId, WorkoutId;
        public int Day;
        public bool Consumed;
        public string[] PresentCharacterIds;
        public TeachingSourceSnapshot[] PaidTeaching=Array.Empty<TeachingSourceSnapshot>();
    }
    public sealed class RelationshipPreparation
    {
        public EconomyStatus Status;
        public string ResponseEventId, FamiliarResponseEventId;
        public int AppliedAffectionDelta;
        public RelationshipState Relationships;
        public WorkoutInteraction Interaction;
        public PaymentPreparation Payment;
        public ItemPreparation Delivery;
    }
    public static class RelationshipRules
    {
        public const string Rival="rival", Beginner="beginner", Veteran="veteran", Coach="coach";
        public static string[] CharacterIds=>new[]{Rival,Beginner,Veteran,Coach};
        public static RelationshipState Initialize(string run,string actor) => Initialize(run,actor,RelationshipContent.EmptyRelationshipId);
        public static RelationshipState Initialize(string run,string actor,string initializationId)
        {
            var initial=RelationshipContent.Definition.InitialState;
            if(initial.Id!=initializationId)throw new ArgumentException("Unknown relationship initialization reference");
            return NormalizeAfterLoad(new RelationshipState { RunId=run,ActorId=actor,Affections=initial.Affections,CurrentBuddy=initial.CurrentBuddy,PendingBuddy=initial.PendingBuddy,PendingBuddyDay=initial.PendingBuddyDay,PendingConfessionId=initial.PendingConfessionId,RomanceEstablished=initial.RomanceEstablished,NextConfessionDay=initial.NextConfessionDay });
        }
        public static RelationshipState NormalizeAfterLoad(RelationshipState s)
        {
            if(s==null || string.IsNullOrWhiteSpace(s.RunId) || string.IsNullOrWhiteSpace(s.ActorId) || s.Day<1 || s.Revision<0 || s.NextConfessionDay<1 || s.Affections==null || s.Affections.Length!=4 || s.Affections.Any(a=>a==null || !CharacterIds.Contains(a.CharacterId) || a.Value<0 || a.Value>100) || s.Affections.Select(a=>a.CharacterId).Distinct().Count()!=4 || s.CurrentBuddy==null || s.PendingBuddy==null || s.PendingConfessionId==null || (s.CurrentBuddy!=""&&!CharacterIds.Contains(s.CurrentBuddy)) || (s.PendingBuddy!=""&&!CharacterIds.Contains(s.PendingBuddy)) || (s.PendingBuddy=="" ? s.PendingBuddyDay!=0 : s.PendingBuddyDay!=s.Day+1) || (s.RomanceEstablished && s.PendingConfessionId!="")) throw new ArgumentException("Invalid relationships");
            return new RelationshipState { RunId=s.RunId,ActorId=s.ActorId,Day=s.Day,Revision=s.Revision,CurrentBuddy=s.CurrentBuddy,PendingBuddy=s.PendingBuddy,PendingBuddyDay=s.PendingBuddyDay,PendingConfessionId=s.PendingConfessionId,RomanceEstablished=s.RomanceEstablished,NextConfessionDay=s.NextConfessionDay,Affections=s.Affections.Select(a=>new AffectionEntry { CharacterId=a.CharacterId,Value=a.Value,FamiliarTextGranted=a.FamiliarTextGranted }).ToArray() };
        }
        public static WorkoutInteraction OpenWorkout(RelationshipState s,string workoutId,string[] present)
        {
            NormalizeAfterLoad(s);
            if(string.IsNullOrWhiteSpace(workoutId)||present==null||present.Any(id=>!CharacterIds.Contains(id))||present.Distinct().Count()!=present.Length) throw new ArgumentException("Invalid presence");
            return new WorkoutInteraction { RunId=s.RunId,ActorId=s.ActorId,Day=s.Day,WorkoutId=workoutId,PresentCharacterIds=(string[])present.Clone() };
        }
        static WorkoutInteraction Copy(WorkoutInteraction w)
        {
            if(w==null||string.IsNullOrWhiteSpace(w.WorkoutId)||w.PresentCharacterIds==null||w.PaidTeaching==null||w.PresentCharacterIds.Any(id=>!CharacterIds.Contains(id))||w.PresentCharacterIds.Distinct().Count()!=w.PresentCharacterIds.Length||w.PaidTeaching.Length>1) throw new ArgumentException("Invalid interaction");
            foreach(var t in w.PaidTeaching) { if(t==null)throw new ArgumentException("Missing entitlement"); t.Validate(); }
            return new WorkoutInteraction { RunId=w.RunId,ActorId=w.ActorId,Day=w.Day,WorkoutId=w.WorkoutId,Consumed=w.Consumed,PresentCharacterIds=(string[])w.PresentCharacterIds.Clone(),PaidTeaching=w.PaidTeaching.Select(t=>t.Clone()).ToArray() };
        }
        static RelationshipPreparation Begin(RelationshipState s,WorkoutInteraction w,EconomyContext c,string target,bool legalRest)
        {
            var r=new RelationshipPreparation { Relationships=NormalizeAfterLoad(s),Interaction=Copy(w),Status=EconomyStatus.Prepared };
            if(c==null||c.RunId!=s.RunId||c.ActorId!=s.ActorId||c.Day!=s.Day||w.RunId!=s.RunId||w.ActorId!=s.ActorId||w.Day!=s.Day||string.IsNullOrWhiteSpace(c.EventInstanceId)||c.TransactionOrdinal<0||!CharacterIds.Contains(target)) throw new ArgumentException("Interaction scope mismatch");
            if(w.Consumed) r.Status=EconomyStatus.OpportunityClosed;
            else if(!legalRest||!w.PresentCharacterIds.Contains(target))r.Status=EconomyStatus.ConditionNotMet;
            return r;
        }
        static void Consume(RelationshipPreparation r) { r.Interaction.Consumed=true; r.Relationships.Revision=checked(r.Relationships.Revision+1); }
        public static RelationshipPreparation PrepareDialogue(RelationshipState s,WorkoutInteraction w,EconomyContext c,string target,int option,bool legalRest)
        {
            var r=Begin(s,w,c,target,legalRest); if(r.Status!=EconomyStatus.Prepared)return r;
            var a=r.Relationships.Affections.Single(x=>x.CharacterId==target);
            var rule=RelationshipContent.Character(target).Dialogues.SingleOrDefault(x=>x.Option==option);
            if(rule==null)throw new ArgumentOutOfRangeException(nameof(option));
            if(a.Value<rule.MinimumAffection) { r.Status=EconomyStatus.ConditionNotMet;return r; }
            int before=a.Value;bool familiarBefore=a.FamiliarTextGranted;
            a.Value=Math.Min(RelationshipContent.Definition.AffectionMaximum,a.Value+rule.FixedDelta); if(a.Value>=RelationshipContent.Definition.FamiliarThreshold)a.FamiliarTextGranted=true;
            r.AppliedAffectionDelta=a.Value-before;r.ResponseEventId=rule.ResponseEventId;
            if(!familiarBefore&&a.FamiliarTextGranted)r.FamiliarResponseEventId=RelationshipContent.Character(target).FamiliarResponseEventId;
            Consume(r);return r;
        }
        public static RelationshipPreparation PrepareInvite(RelationshipState s,WorkoutInteraction w,EconomyContext c,string target,bool legalRest)
        {
            var r=Begin(s,w,c,target,legalRest);if(r.Status!=EconomyStatus.Prepared)return r;
            if(s.Affections.Single(a=>a.CharacterId==target).Value<RelationshipContent.Definition.InviteThreshold){r.Status=EconomyStatus.ConditionNotMet;return r;}
            r.Relationships.PendingBuddy=target;r.Relationships.PendingBuddyDay=checked(s.Day+1);Consume(r);return r;
        }
        public static RelationshipPreparation PrepareTeaching(RelationshipState s,WorkoutInteraction w,LifeActorState life,EconomyContext c,bool legalRest,int nextSetSequence,string commitId)
        {
            var r=Begin(s,w,c,Coach,legalRest);if(r.Status!=EconomyStatus.Prepared)return r;
            if(s.CurrentBuddy==Coach||w.PaidTeaching.Length>0){r.Status=EconomyStatus.ConditionNotMet;return r;}
            if(nextSetSequence<1||string.IsNullOrWhiteSpace(commitId))throw new ArgumentException("Missing activation point");
            r.Payment=InventoryRules.PreparePayment(life,c,"CoachTeaching",s.Affections.Single(a=>a.CharacterId==Coach).Value>=RelationshipContent.Definition.FreeTeachingThreshold?0:RelationshipContent.Definition.TeachingPrice);
            if(r.Payment.Status!=EconomyStatus.Prepared){r.Status=r.Payment.Status;return r;}
            r.Interaction.PaidTeaching=new[]{new TeachingSourceSnapshot { EntitlementId=r.Payment.Receipt.SemanticKey,SourceActorId=Coach,SourceKind="PaidCoach",KnowledgeRuleId="TeachingMax3V1",ActivatedAtCommitId=commitId,FirstEligibleWorkoutSetSequence=nextSetSequence,NominalTargetMicro=RelationshipContent.Definition.PaidTeachingMicro }};
            Consume(r);return r;
        }
        public static RelationshipPreparation PrepareGift(RelationshipState s,WorkoutInteraction w,InventoryState inventory,EconomyContext c,bool legalRest)
        {
            var r=Begin(s,w,c,Beginner,legalRest);if(r.Status!=EconomyStatus.Prepared)return r;
            if(s.RomanceEstablished||s.PendingConfessionId!=""||s.Day<s.NextConfessionDay||s.Affections.Single(a=>a.CharacterId==Beginner).Value!=RelationshipContent.Definition.Romance.RequiredAffection){r.Status=EconomyStatus.ConditionNotMet;return r;}
            r.Delivery=InventoryRules.PrepareDelivery(inventory,c,RelationshipContent.Definition.Romance.GiftItemId,RelationshipContent.Definition.Romance.GiftQuantity,true);
            if(r.Delivery.Status!=EconomyStatus.Prepared){r.Status=r.Delivery.Status;return r;}
            r.Relationships.PendingConfessionId=r.Delivery.SemanticKey;r.ResponseEventId=RelationshipContent.Definition.Romance.PendingEventId;Consume(r);return r;
        }
        public static RelationshipState AdvanceDay(RelationshipState s,int day)
        {
            var n=NormalizeAfterLoad(s);if(day!=s.Day+1||s.PendingConfessionId!="")throw new ArgumentException("Invalid relationship day advance");
            if(n.PendingBuddy!=""){n.CurrentBuddy=n.PendingBuddy;n.PendingBuddy="";n.PendingBuddyDay=0;}
            n.Day=day;n.Revision=checked(n.Revision+1);return n;
        }
        public static SetBenefitsSnapshot FreezeBenefits(RelationshipState s,WorkoutInteraction w,string snapshotId,int sequence,string presenceCommitId)
        {
            NormalizeAfterLoad(s);var copy=Copy(w);
            if(w.RunId!=s.RunId||w.ActorId!=s.ActorId||w.Day!=s.Day||string.IsNullOrWhiteSpace(presenceCommitId))throw new ArgumentException("Benefits scope mismatch");
            var b=SetBenefitsSnapshot.None(snapshotId,s.RunId,s.ActorId,s.Day,w.WorkoutId,sequence);b.RuleVersion=RelationshipContent.Definition.Version;b.RelationshipRevision=s.Revision;
            b.TeachingSources=copy.PaidTeaching;
            if(s.CurrentBuddy!=""&&w.PresentCharacterIds.Contains(s.CurrentBuddy))
            {
                string key=TeachingClaim.Key(s.RunId,s.ActorId,w.WorkoutId,s.CurrentBuddy,"Buddy");
                var benefit=RelationshipContent.Character(s.CurrentBuddy);
                b.RiskSourceIds=new[]{key};b.RiskReductionBasisPoints=benefit.RiskReductionBasisPoints;
                if(benefit.BuddyKnowledgeMicro>0)b.TeachingSources=b.TeachingSources.Concat(new[]{new TeachingSourceSnapshot { EntitlementId=key,SourceActorId=s.CurrentBuddy,SourceKind="Buddy",KnowledgeRuleId="TeachingMax3V1",ActivatedAtCommitId=presenceCommitId,FirstEligibleWorkoutSetSequence=1,NominalTargetMicro=benefit.BuddyKnowledgeMicro }}).ToArray();
            }
            b.Validate();return b;
        }
    }
}
