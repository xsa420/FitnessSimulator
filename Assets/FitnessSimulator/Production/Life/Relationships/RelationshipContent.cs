using System;
using System.Linq;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Life.Economy;
namespace FitnessSimulator.Game.Life.Relationships
{
    [Serializable] public sealed class RelationshipContentDocument
    {
        public string Version;
        public RelationshipInitialDefinition InitialState;
        public int AffectionMaximum, FamiliarThreshold, InviteThreshold, FreeTeachingThreshold;
        public long TeachingPrice, PaidTeachingMicro;
        public AffectionTierDefinition[] Tiers;
        public CharacterRelationshipDefinition[] Characters;
        public RomanceDefinition Romance;
        public SocialCurveDefinition Social;
    }
    [Serializable] public sealed class RelationshipInitialDefinition
    {
        public string Id, CurrentBuddy, PendingBuddy, PendingConfessionId;
        public int PendingBuddyDay, NextConfessionDay;
        public bool RomanceEstablished;
        public AffectionEntry[] Affections;
    }
    [Serializable] public sealed class AffectionTierDefinition { public int Threshold; public string Name; }
    [Serializable] public sealed class DialogueDefinition { public string Id, Text, ResponseEventId; public int Option, MinimumAffection, FixedDelta; }
    [Serializable] public sealed class CharacterRelationshipDefinition
    {
        public string CharacterId, FamiliarResponseEventId;
        public DialogueDefinition[] Dialogues;
        public int RiskReductionBasisPoints;
        public long BuddyKnowledgeMicro;
    }
    [Serializable] public sealed class RomanceDefinition
    {
        public string Id, TargetCharacterId, GiftItemId, PendingEventId, SuccessEventId, FailureEventId, DeclineEventId;
        public int RequiredAffection, GiftQuantity;
        public ConfessionFormula Formula;
    }
    [Serializable] public sealed class ConfessionFormula
    {
        public string Version;
        public long BaseMillionths, CharmMillionths, LeanMillionths, FatMillionths, MinimumMillionths, MaximumMillionths;
        public long[] BodyFatKnotsMicro;
        public int CooldownFullDays;
    }
    [Serializable] public sealed class SocialCurveDefinition
    {
        public long CharmSqrtMillionths, MentalSquareMillionths, PositiveLinearMillionths;
        public string[] ScalableEffectKinds;
    }
    public static class RelationshipContent
    {
        public const string EmptyRelationshipId="LC.initial-unmet-zero-no-partner.v1";
        public static RelationshipContentDocument Definition=>ContentRuntime.Current.Get<RelationshipContentDocument>("relationships");
        public static CharacterRelationshipDefinition Character(string id)=>Definition.Characters.SingleOrDefault(x=>x.CharacterId==id)??throw new ArgumentException("relationships.Characters: unknown "+id);
        public static string TierName(int affection)=>Definition.Tiers.Where(x=>x.Threshold<=affection).OrderByDescending(x=>x.Threshold).First().Name;
        public static void ValidateFormula(ConfessionFormula f)
        {
            if(f==null||string.IsNullOrWhiteSpace(f.Version)||f.BaseMillionths<0||f.CharmMillionths<0||f.LeanMillionths<0||f.FatMillionths<0||f.MinimumMillionths<=0||f.MaximumMillionths>=1000000||f.MinimumMillionths>f.MaximumMillionths||(f.CooldownFullDays<0||f.CooldownFullDays>365)||f.BodyFatKnotsMicro==null||f.BodyFatKnotsMicro.Length!=4||f.BodyFatKnotsMicro[0]<5000000||f.BodyFatKnotsMicro[3]>50000000||!f.BodyFatKnotsMicro.SequenceEqual(f.BodyFatKnotsMicro.OrderBy(x=>x))||f.BodyFatKnotsMicro.Distinct().Count()!=4)
                throw new ArgumentException("relationships.Romance.Formula: invalid coefficients/knots/cooldown");
        }
        public static void ValidateContent()
        {
            var d=Definition;
            if(string.IsNullOrWhiteSpace(d.Version)||d.AffectionMaximum!=100||d.FamiliarThreshold<1||d.FamiliarThreshold>100||d.InviteThreshold<1||d.InviteThreshold>100||d.FreeTeachingThreshold!=100||d.TeachingPrice<0||d.PaidTeachingMicro<=0||d.PaidTeachingMicro>3000000)throw new ArgumentException("relationships: invalid thresholds/teaching");
            if(d.Tiers==null||d.Tiers.Length==0||d.Tiers.Any(t=>t==null||t.Threshold<0||t.Threshold>100||string.IsNullOrWhiteSpace(t.Name))||!d.Tiers.Any(t=>t.Threshold==0)||d.Tiers.Select(t=>t.Threshold).Distinct().Count()!=d.Tiers.Length)throw new ArgumentException("relationships.Tiers: invalid thresholds");
            var ids=RelationshipRules.CharacterIds;
            if(d.Characters==null||d.Characters.Length!=4||d.Characters.Any(c=>c==null||!ids.Contains(c.CharacterId))||d.Characters.Select(c=>c.CharacterId).Distinct().Count()!=4)throw new ArgumentException("relationships.Characters: require four independent definitions");
            foreach(var c in d.Characters)
            {
                if(c.RiskReductionBasisPoints<0||c.RiskReductionBasisPoints>10000||c.BuddyKnowledgeMicro<0||c.BuddyKnowledgeMicro>3000000||string.IsNullOrWhiteSpace(c.FamiliarResponseEventId)||c.Dialogues==null||c.Dialogues.Length==0||c.Dialogues.Any(x=>x==null)||c.Dialogues.Select(x=>x.Option).Distinct().Count()!=c.Dialogues.Length||c.Dialogues.Select(x=>x.Id).Distinct().Count()!=c.Dialogues.Length||c.Dialogues.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.Text)||string.IsNullOrWhiteSpace(x.ResponseEventId)||x.Option<0||x.MinimumAffection<0||x.MinimumAffection>100||x.FixedDelta<0||x.FixedDelta>100))throw new ArgumentException("relationships.Characters."+c.CharacterId+": invalid dialogue/benefit");
            }
            var init=d.InitialState;
            if(init==null||init.Id!=EmptyRelationshipId||init.CurrentBuddy!=""||init.PendingBuddy!=""||init.PendingConfessionId!=""||init.PendingBuddyDay!=0||init.NextConfessionDay!=1||init.RomanceEstablished||init.Affections==null||init.Affections.Length!=4||init.Affections.Any(a=>a==null||!ids.Contains(a.CharacterId)||a.Value!=0||a.FamiliarTextGranted)||init.Affections.Select(a=>a.CharacterId).Distinct().Count()!=4)throw new ArgumentException("relationships.InitialState: explicit empty relationship baseline required");
            var r=d.Romance;
            if(r==null||string.IsNullOrWhiteSpace(r.Id)||r.TargetCharacterId!=RelationshipRules.Beginner||r.RequiredAffection!=100||r.GiftQuantity!=1||EconomyContent.Kind(r.GiftItemId)!=ItemKind.StoryCondition||new[]{r.PendingEventId,r.SuccessEventId,r.FailureEventId,r.DeclineEventId}.Any(string.IsNullOrWhiteSpace))throw new ArgumentException("relationships.Romance: invalid existing route/gift");
            ValidateFormula(r.Formula);
            var s=d.Social;
            if(s==null||s.CharmSqrtMillionths<0||s.CharmSqrtMillionths>1000000||s.MentalSquareMillionths<0||s.MentalSquareMillionths>1000000||s.PositiveLinearMillionths<0||s.PositiveLinearMillionths>1000000||s.ScalableEffectKinds==null||s.ScalableEffectKinds.Distinct().Count()!=s.ScalableEffectKinds.Length||s.ScalableEffectKinds.Any(k=>k!="Knowledge"&&k!="Proficiency"&&k!="MoneyGain"))throw new ArgumentException("relationships.Social: invalid independent curve/exclusion registry");
        }
    }
}