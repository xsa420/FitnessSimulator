using System;
using System.Linq;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Competition
{
    [Serializable] public sealed class CompetitionDocument
    {
        public string Version, ScoringVersion, NumericPolicyId, ScheduleId, FreezePhase, ClosePhase;
        public int CycleDays, OpenDay, RegistrationCloseDay, ContestDay;
        public int StrengthNormalizer, CharmNormalizer, FatMinimum, FatMaximum;
        public ContestDefinition[] Contests;
        public RevealStageDefinition[] RevealStages;
        public JudgeContentDefinition[] Judges;
        public CommentaryProfileDefinition[] CommentaryProfiles;
        public int StrengthBucketWidth, BodyFatBucketWidth;
        public DisclosureGroupDefinition[] DisclosureGroups;
    }
    [Serializable] public sealed class ContestDefinition
    {
        public ContestKind Kind;
        public string Id, Name, AnchorId, FormulaKind;
        public string[] JudgeIds, ParticipantRefs;
        public int DrawCount;
    }
    [Serializable] public sealed class RevealStageDefinition { public int Day, Count; public string[] Fields; }
    [Serializable] public sealed class DisclosureGroupDefinition { public string Id; public string[] RegionIds; }
    [Serializable] public sealed class JudgeContentDefinition
    {
        public string Id, Name, Description, CommentaryProfileId;
        public ContestKind Kind;
        public bool Balanced;
        public string[] PreferredRegions;
        public int[] WeightsPercent;
        public int FatLower, FatUpper;
    }
    [Serializable] public sealed class CommentaryProfileDefinition
    {
        public string Id, FallbackTextRef;
        public CommentaryRuleDefinition[] Rules;
    }
    [Serializable] public sealed class CommentaryRuleDefinition
    {
        public string Predicate, TextRef;
        public long ThresholdMicro;
    }
    public static class CompetitionConfiguration
    {
        public static CompetitionDocument Read() => ContentRuntime.Current.Get<CompetitionDocument>("competition");
        public static void ValidateReferences(string[] orderedAnchorIds,Func<string,bool> actorExists,Func<string,bool> textExists)
        {
            if(orderedAnchorIds==null||actorExists==null||textExists==null)throw new ArgumentNullException();
            var d=Read(); var indices=d.Contests.Select(c=>Array.IndexOf(orderedAnchorIds,c.AnchorId)).ToArray();
            Require(indices.Length==2&&indices[0]>=0&&indices[1]>indices[0],"Contests.AnchorId/order");
            foreach(var c in d.Contests)Require(c.ParticipantRefs.All(actorExists),"Contests["+c.Id+"].ParticipantRefs");
            foreach(var p in d.CommentaryProfiles)Require(textExists(p.FallbackTextRef)&&p.Rules.All(r=>textExists(r.TextRef)),"CommentaryProfiles["+p.Id+"].TextRef");
        }
        public static void ValidateContent()
        {
            var d=Read();
            Require(new[]{d.Version,d.ScoringVersion,d.ScheduleId}.All(x=>!string.IsNullOrWhiteSpace(x)),"Identity");
            Require(d.NumericPolicyId=="ScoreUnits4.TieMicro6.MeanCents2"&&d.FreezePhase=="DayOpenReady"&&d.ClosePhase=="AllAnchorsFinishedBeforeDayEnd","ExecutionPolicy");
            Require(d.OpenDay>=1&&d.OpenDay<=d.RegistrationCloseDay&&d.RegistrationCloseDay<d.ContestDay&&d.ContestDay<=d.CycleDays,"Schedule");
            Require(d.StrengthNormalizer==100&&d.CharmNormalizer==100&&d.FatMinimum==5&&d.FatMaximum==50,"Normalization");
            Require(d.RevealStages!=null&&d.RevealStages.Length==3&&d.RevealStages.All(x=>x!=null),"RevealStages");
            string[][] supported={new[]{"StrengthBucket"},new[]{"StrengthBucket","HighestGroups","LowestGroups","BalanceDescription","BodyFatBucket"},new[]{"StrengthBucket","HighestGroups","LowestGroups","BalanceDescription","BodyFatBucket","Regions","BodyFat","BaseCharm"}};
            for(int i=0;i<3;i++) {
                var r=d.RevealStages[i];
                Require(r.Day>=d.OpenDay&&r.Day<=d.RegistrationCloseDay&&(i==0||r.Day>d.RevealStages[i-1].Day)&&r.Count==i+1,"RevealStages["+i+"]");
                Require(r.Fields!=null&&r.Fields.Distinct().Count()==r.Fields.Length&&r.Fields.All(supported[i].Contains),"RevealStages["+i+"].Fields");
            }
            Require(d.StrengthBucketWidth>0&&d.BodyFatBucketWidth>0,"BucketWidths");
            Require(d.DisclosureGroups!=null&&d.DisclosureGroups.Length>0&&d.DisclosureGroups.All(g=>g!=null&&!string.IsNullOrWhiteSpace(g.Id)&&g.RegionIds!=null&&g.RegionIds.Length>0&&g.RegionIds.Distinct().Count()==g.RegionIds.Length&&g.RegionIds.All(RegionIds.All.Contains))&&d.DisclosureGroups.Select(g=>g.Id).Distinct().Count()==d.DisclosureGroups.Length,"DisclosureGroups");
            Require(d.Contests!=null&&d.Contests.Length==2&&d.Contests.All(c=>c!=null)&&d.Contests[0].Kind==ContestKind.Bodybuilding&&d.Contests[1].Kind==ContestKind.ThinBuild,"Contests");
            Require(d.Contests.Select(c=>c.AnchorId).Distinct().Count()==2,"Contests.AnchorId");
            Require(d.Contests.Select(c=>c.Id).Distinct().Count()==2,"Contests.Id");
            Require(d.Judges!=null&&d.Judges.Length==8&&d.Judges.All(j=>j!=null)&&d.Judges.Select(j=>j.Id).Distinct().Count()==8,"Judges");
            Require(d.CommentaryProfiles!=null&&d.CommentaryProfiles.All(p=>p!=null)&&d.CommentaryProfiles.Select(p=>p.Id).Distinct().Count()==d.CommentaryProfiles.Length,"CommentaryProfiles");
            foreach(var c in d.Contests) {
                string path="Contests["+c.Kind+"]";
                Require(new[]{c.Id,c.Name,c.AnchorId}.All(x=>!string.IsNullOrWhiteSpace(x))&&c.DrawCount==3,path);
                Require(c.FormulaKind==(c.Kind==ContestKind.Bodybuilding?"MUGT":"LHCT"),path+".FormulaKind");
                Require(c.ParticipantRefs!=null&&c.ParticipantRefs.SequenceEqual(new[]{"player","rival"}),path+".ParticipantRefs");
                Require(c.JudgeIds!=null&&c.JudgeIds.Length==4&&c.JudgeIds.Distinct().Count()==4&&c.JudgeIds.All(id=>d.Judges.Any(j=>j.Id==id&&j.Kind==c.Kind)),path+".JudgeIds");
            }
            foreach(var j in d.Judges) {
                string path="Judges["+j.Id+"]";
                Require(new[]{j.Id,j.Name,j.Description,j.CommentaryProfileId}.All(x=>!string.IsNullOrWhiteSpace(x)),path);
                Require(d.Contests.Any(c=>c.Kind==j.Kind&&c.JudgeIds.Contains(j.Id)),path+".Kind");
                Require(j.WeightsPercent!=null&&j.WeightsPercent.Length==4&&j.WeightsPercent.All(w=>w>=0)&&j.WeightsPercent.Sum()==100,path+".WeightsPercent");
                Require(j.FatLower>d.FatMinimum&&j.FatUpper<d.FatMaximum&&j.FatLower<=j.FatUpper,path+".FatRange");
                Require(j.PreferredRegions!=null&&j.PreferredRegions.Distinct().Count()==j.PreferredRegions.Length&&j.PreferredRegions.All(RegionIds.All.Contains)&&(j.Balanced?j.PreferredRegions.Length==0:j.PreferredRegions.Length>0),path+".PreferredRegions");
                Require(d.CommentaryProfiles.Any(p=>p.Id==j.CommentaryProfileId),path+".CommentaryProfileId");
                var profile=d.CommentaryProfiles.Single(p=>p.Id==j.CommentaryProfileId);
                Require(profile.Rules!=null&&profile.Rules.All(r=>r!=null&&(j.Kind==ContestKind.Bodybuilding
                    ?new[]{"BalancedRatioAtLeast","PreferredAtLeastMean"}.Contains(r.Predicate)
                    :new[]{"ThinIndexAtLeast","PreferredStrengthBelow","PreferredStrengthAbove"}.Contains(r.Predicate))),path+".CommentaryPredicates");
            }
            foreach(var p in d.CommentaryProfiles) {
                Require(!string.IsNullOrWhiteSpace(p.Id)&&!string.IsNullOrWhiteSpace(p.FallbackTextRef)&&p.Rules!=null,"CommentaryProfiles["+p.Id+"]");
                foreach(var r in p.Rules)Require(r!=null&&!string.IsNullOrWhiteSpace(r.TextRef)&&new[]{"BalancedRatioAtLeast","PreferredAtLeastMean","ThinIndexAtLeast","PreferredStrengthBelow","PreferredStrengthAbove"}.Contains(r.Predicate)&&r.ThresholdMicro>=0&&r.ThresholdMicro<=(r.Predicate.StartsWith("PreferredStrength",StringComparison.Ordinal)?100000000:1000000),"CommentaryProfiles["+p.Id+"].Rules");
            }
        }
        private static void Require(bool ok,string path) { if(!ok)throw new ArgumentException("competition."+path); }
    }
}
