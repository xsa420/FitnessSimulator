using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Competition
{
    [Serializable] public sealed class JudgeScore
    {
        public string JudgeId;
        public string CommentaryTextRef;
        public long ScoreUnits4;
        public long[] ComponentMicro;
        public JudgeScore Clone()=>new JudgeScore { JudgeId=JudgeId,CommentaryTextRef=CommentaryTextRef,ScoreUnits4=ScoreUnits4,ComponentMicro=(long[])ComponentMicro.Clone() };
    }
    [Serializable] public sealed class ParticipantScore
    {
        public string ActorId;
        public JudgeScore[] Judges;
        public long TotalScoreUnits4;
        public long DisplayMeanCents;
        public long[] TieKeysMicro;
        public int Rank;
        public ParticipantScore Clone()=>new ParticipantScore { ActorId=ActorId,Judges=Judges.Select(x=>x.Clone()).ToArray(),TotalScoreUnits4=TotalScoreUnits4,
            DisplayMeanCents=DisplayMeanCents,TieKeysMicro=(long[])TieKeysMicro.Clone(),Rank=Rank };
    }
    [Serializable] public sealed class ContestScoreResult
    {
        public string ScoringVersion;
        public ContestKind Kind;
        public ParticipantScore[] Participants;
        public bool SharedChampionship;
        public ContestScoreResult Clone()=>new ContestScoreResult { ScoringVersion=ScoringVersion,Kind=Kind,
            Participants=Participants.Select(x=>x.Clone()).ToArray(),SharedChampionship=SharedChampionship };
    }
    public static class CompetitionScoring
    {
        // Pure formula entry: fixed bodies here are a legitimate test fixture, not production provenance.
        public static ContestScoreResult Score(ContestKind kind,BodyState player,BodyState rival,string[] judgeIds)
        {
            CompetitionContent.CheckKind(kind); player.Validate(); rival.Validate();
            if(player.ActorId==rival.ActorId)throw new ArgumentException("DuplicateParticipant");
            ValidateJudges(kind,judgeIds);
            var a=ScoreOne(kind,player,judgeIds); var b=ScoreOne(kind,rival,judgeIds);
            int comparison=Compare(a,b); a.Rank=comparison>=0?1:2; b.Rank=comparison<=0?1:2;
            return new ContestScoreResult { ScoringVersion=CompetitionContent.ScoringVersion,Kind=kind,Participants=new[]{a,b},SharedChampionship=comparison==0 };
        }
        internal static void ValidateJudges(ContestKind kind,string[] ids)
        {
            if(ids==null||ids.Length!=3||ids.Distinct(StringComparer.Ordinal).Count()!=3||ids.Any(id=>!CompetitionContent.Judges.Any(j=>j.Id==id&&j.Kind==kind)))
                throw new ArgumentException("InvalidJudgePanel");
        }
        public static Rational FatAdaptation(long bodyFatMicro,int lower,int upper)
        {
            var content=CompetitionConfiguration.Read(); int min=content.FatMinimum,max=content.FatMaximum;
            if(bodyFatMicro<min*NumericKernel.Micro||bodyFatMicro>max*NumericKernel.Micro||lower<=min||upper>=max||lower>upper)throw new ArgumentOutOfRangeException();
            var b=NumericKernel.FromMicro(bodyFatMicro);
            return b<lower?(b-min)/(lower-min):b<=upper?(Rational)1:(max-b)/(max-upper);
        }
        private static ParticipantScore ScoreOne(ContestKind kind,BodyState body,string[] ids)
        {
            var content=CompetitionConfiguration.Read();
            Rational m=new Rational(body.Regions.Sum(x=>x.StrengthMicro),21*content.StrengthNormalizer*NumericKernel.Micro);
            Rational u=new Rational(body.Regions.Min(x=>x.StrengthMicro),content.StrengthNormalizer*NumericKernel.Micro);
            long lUnits=BodyMetrics.GetThinBuildIndexMicro(body); Rational l=NumericKernel.FromMicro(lUnits);
            Rational c=new Rational(body.BaseCharmMicro,content.CharmNormalizer*NumericKernel.Micro); Rational totalT=0;
            var scores=ids.Select(id=> {
                var j=CompetitionContent.Judge(id); var t=FatAdaptation(body.BodyFatMicro,j.FatLower,j.FatUpper); totalT+=t;
                Rational[] parts;
                if(kind==ContestKind.Bodybuilding) {
                    var g=j.Balanced?m:new Rational(j.PreferredRegions.Sum(r=>body.Region(r).StrengthMicro),j.PreferredRegions.Count*content.StrengthNormalizer*NumericKernel.Micro);
                    parts=new[]{m,u,g,t};
                } else {
                    Rational h=l;
                    if(!j.Balanced) { h=0; foreach(var region in j.PreferredRegions)h+=BodyMetrics.GetThinRegionAdaptation(body.Region(region).StrengthMicro); h/=j.PreferredRegions.Count; }
                    parts=new[]{l,h,c,t};
                }
                Rational points=0; for(int i=0;i<4;i++)points+=parts[i]*j.WeightsPercent[i];
                return new JudgeScore { JudgeId=id,CommentaryTextRef=Commentary(j,body,m,u,l,parts),ScoreUnits4=(points*10000).RoundNonNegative(),ComponentMicro=parts.Select(NumericKernel.ToMicro).ToArray() };
            }).ToArray();
            long sum=scores.Sum(x=>x.ScoreUnits4);
            return new ParticipantScore { ActorId=body.ActorId,Judges=scores,TotalScoreUnits4=sum,DisplayMeanCents=new Rational(sum,300).RoundNonNegative(),
                TieKeysMicro=kind==ContestKind.Bodybuilding?new[]{NumericKernel.ToMicro(u),NumericKernel.ToMicro(m)}:new[]{lUnits,NumericKernel.ToMicro(totalT/3),NumericKernel.ToMicro(c)} };
        }
        private static string Commentary(JudgeDefinition judge,BodyState body,Rational m,Rational u,Rational l,Rational[] parts)
        {
            var profile=CompetitionConfiguration.Read().CommentaryProfiles.Single(p=>p.Id==judge.CommentaryProfileId);
            var mean=new Rational(judge.PreferredRegions.Sum(id=>body.Region(id).StrengthMicro),judge.PreferredRegions.Count*NumericKernel.Micro);
            foreach(var rule in profile.Rules) {
                var threshold=NumericKernel.FromMicro(rule.ThresholdMicro); bool match;
                switch(rule.Predicate) {
                    case "BalancedRatioAtLeast": match=m>0&&u>=threshold*m; break;
                    case "PreferredAtLeastMean": match=parts[2]>=m; break;
                    case "ThinIndexAtLeast": match=l>=threshold; break;
                    case "PreferredStrengthBelow": match=mean<threshold; break;
                    case "PreferredStrengthAbove": match=mean>threshold; break;
                    default: throw new ArgumentException("competition.CommentaryProfiles["+profile.Id+"].Predicate");
                }
                if(match)return rule.TextRef;
            }
            return profile.FallbackTextRef;
        }
        private static int Compare(ParticipantScore a,ParticipantScore b)
        {
            int c=a.TotalScoreUnits4.CompareTo(b.TotalScoreUnits4); if(c!=0)return c;
            for(int i=0;i<a.TieKeysMicro.Length;i++) { c=a.TieKeysMicro[i].CompareTo(b.TieKeysMicro[i]); if(c!=0)return c; }
            return 0;
        }
    }
}
