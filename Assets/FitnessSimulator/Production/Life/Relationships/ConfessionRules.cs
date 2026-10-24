using System;
using System.Linq;
namespace FitnessSimulator.Game.Life.Relationships
{
    [Serializable] public sealed class ConfessionSnapshot
    {
        public string RunId,ActorId,GiftId;
        public int Day, ProbabilityThousandths;
        public long RelationshipRevision, CharmMicro, LeanIndexMicro, BodyFatMicro;
        public ConfessionFormula Formula;
        public string SuccessEventId, FailureEventId;
    }
    [Serializable] public sealed class ConfessionReceipt { public ConfessionSnapshot Snapshot; public uint Sample; public bool Success; }
    public sealed class ConfessionPreparation { public RelationshipState Relationships; public ConfessionReceipt Receipt; public string ResponseEventId; }
    public static class ConfessionRules
    {
        public static ConfessionSnapshot Freeze(RelationshipState state,long charmMicro,long leanIndexMicro,long bodyFatMicro)
        {
            ValidateInputs(state,charmMicro,leanIndexMicro,bodyFatMicro);
            var route=RelationshipContent.Definition.Romance;
            return new ConfessionSnapshot { RunId=state.RunId,ActorId=state.ActorId,Day=state.Day,GiftId=state.PendingConfessionId,RelationshipRevision=state.Revision,
                CharmMicro=charmMicro,LeanIndexMicro=leanIndexMicro,BodyFatMicro=bodyFatMicro,Formula=route.Formula,SuccessEventId=route.SuccessEventId,FailureEventId=route.FailureEventId,
                ProbabilityThousandths=Probability(route.Formula,charmMicro,leanIndexMicro,bodyFatMicro) };
        }
        private static void ValidateInputs(RelationshipState state,long charm,long lean,long fat)
        {
            RelationshipRules.NormalizeAfterLoad(state);
            if(state.PendingConfessionId==""||state.RomanceEstablished||state.Day<state.NextConfessionDay||state.Affections.Single(a=>a.CharacterId==RelationshipRules.Beginner).Value!=100||charm<0||charm>1000000||lean<0||lean>1000000||fat<5000000||fat>50000000)throw new ArgumentException("Invalid confession inputs");
        }
        private static int Probability(ConfessionFormula formula,long charm,long lean,long fat)
        {
            RelationshipContent.ValidateFormula(formula);var knots=formula.BodyFatKnotsMicro;
            decimal b=fat<knots[1]?Math.Max(0,(decimal)(fat-knots[0])/(knots[1]-knots[0])):fat<=knots[2]?1:Math.Max(0,(decimal)(knots[3]-fat)/(knots[3]-knots[2]));
            decimal p=(formula.BaseMillionths+formula.CharmMillionths*(charm/1000000m)+formula.LeanMillionths*(lean/1000000m)+formula.FatMillionths*b)/1000000m;
            p=Math.Min(formula.MaximumMillionths/1000000m,Math.Max(formula.MinimumMillionths/1000000m,p));
            return (int)decimal.Round(p*1000,0,MidpointRounding.AwayFromZero);
        }
        // Only snapshots from the explicit pre-configuration schema lack Formula. Never substitute the installed package.
        private static ConfessionFormula LegacyFormula()=>new ConfessionFormula { Version="legacy-confession-v1",BaseMillionths=150000,CharmMillionths=300000,LeanMillionths=350000,FatMillionths=200000,MinimumMillionths=50000,MaximumMillionths=950000,BodyFatKnotsMicro=new long[]{5000000,15000000,20000000,50000000},CooldownFullDays=7 };
        public static ConfessionPreparation Prepare(RelationshipState state,ConfessionSnapshot snapshot,uint sample)
        {
            if(snapshot==null)throw new ArgumentNullException(nameof(snapshot));
            ValidateInputs(state,snapshot.CharmMicro,snapshot.LeanIndexMicro,snapshot.BodyFatMicro);
            var formula=snapshot.Formula;
            if(formula==null||string.IsNullOrEmpty(formula.Version))
            {
                if(formula!=null&&(formula.BaseMillionths!=0||formula.CharmMillionths!=0||formula.LeanMillionths!=0||formula.FatMillionths!=0||formula.MinimumMillionths!=0||formula.MaximumMillionths!=0||formula.CooldownFullDays!=0||(formula.BodyFatKnotsMicro!=null&&formula.BodyFatKnotsMicro.Length!=0)))throw new ArgumentException("Contradictory legacy confession formula");
                formula=LegacyFormula();
            }
            if(snapshot.RunId!=state.RunId||snapshot.ActorId!=state.ActorId||snapshot.GiftId!=state.PendingConfessionId||snapshot.Day!=state.Day||snapshot.RelationshipRevision!=state.Revision||snapshot.ProbabilityThousandths!=Probability(formula,snapshot.CharmMicro,snapshot.LeanIndexMicro,snapshot.BodyFatMicro))throw new ArgumentException("Stale or changed confession snapshot");
            bool success=(ulong)sample*1000UL<(ulong)snapshot.ProbabilityThousandths*4294967296UL;
            var next=RelationshipRules.NormalizeAfterLoad(state);next.PendingConfessionId="";next.RomanceEstablished=success;next.NextConfessionDay=checked(state.Day+formula.CooldownFullDays+1);next.Revision=checked(next.Revision+1);
            return new ConfessionPreparation { Relationships=next,Receipt=new ConfessionReceipt { Snapshot=snapshot,Sample=sample,Success=success },ResponseEventId=success?snapshot.SuccessEventId:snapshot.FailureEventId };
        }
        public static RelationshipState Decline(RelationshipState state)
        {
            var next=RelationshipRules.NormalizeAfterLoad(state);if(next.PendingConfessionId=="")throw new ArgumentException("No pending confession");
            next.PendingConfessionId="";next.Revision=checked(next.Revision+1);return next;
        }
    }
}