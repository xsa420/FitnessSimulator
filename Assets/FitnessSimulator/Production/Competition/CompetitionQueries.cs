using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Competition
{
    public sealed class PublicJudgeSeat
    {
        public int Seat;
        public bool Revealed;
        public string Name, Description;
    }
    public sealed class PublicContestView
    {
        public string ContestId, Name, AnchorId, Status;
        public ContestKind Kind;
        public int ContestDay, DaysRemaining;
        public bool EntryVisible, Enrolled, CanEnroll, CanWithdraw, CanAttend;
        public PublicJudgeSeat[] Judges;
        public CompetitionResult[] Results;
    }
    public sealed class RivalDisclosureView
    {
        public string ActorId, Phase, SourceCommitId, StrengthBucket, BodyFatBucket, BalanceDescription;
        public int Day, Level;
        public long ActorRevision;
        public string[] HighestGroups=Array.Empty<string>(), LowestGroups=Array.Empty<string>();
        public string[] RegionIds=Array.Empty<string>();
        public long[] StrengthMicro=Array.Empty<long>();
        public long BodyFatMicro, BaseCharmMicro;
    }
    public static class CompetitionQueries
    {
        public static PublicContestView[] Entries(CompetitionState state,int day)
        {
            CompetitionValidation.Validate(state); var e=state.Current;
            int offset=CompetitionContent.Offset(e.Ordinal);
            if(day<Math.Max(1,offset-CompetitionContent.CycleDays+CompetitionContent.ContestDay)||day>e.ContestDay)throw new ArgumentOutOfRangeException(nameof(day));
            int relative=day-offset;
            int disclosedByDay=CompetitionContent.DisclosureCount(relative);
            return e.Contests.Select(c=>new PublicContestView {
                ContestId=c.ContestId,Name=CompetitionContent.Contest(c.Kind).Name,Kind=c.Kind,AnchorId=CompetitionContent.Anchor(c.Kind),ContestDay=e.ContestDay,DaysRemaining=Math.Max(0,e.ContestDay-day),
                EntryVisible=e.Opened||e.Ordinal>1,Enrolled=c.Enrolled,
                CanEnroll=e.Opened&&!e.RegistrationClosed&&!c.Enrolled&&relative>=CompetitionContent.OpenDay&&relative<=CompetitionContent.RegistrationCloseDay&&day<e.ContestDay,
                CanWithdraw=e.Opened&&!e.RegistrationClosed&&c.Enrolled&&relative>=CompetitionContent.OpenDay&&relative<=CompetitionContent.RegistrationCloseDay&&day<e.ContestDay,
                CanAttend=day==e.ContestDay&&e.FrozenActors.Length==2&&c.Enrolled&&!c.WindowClosed&&!e.Failed&&
                    (c.Kind==ContestKind.Bodybuilding||e.Contests[0].WindowClosed),
                Status=c.Results.Length>0?"Settled":c.Declined?"Declined":c.WindowClosed?"Closed":e.Failed?"Stopped":
                    e.RegistrationClosed?(c.Enrolled?"AwaitingAttendance":"RegistrationClosed"):c.Enrolled?"Enrolled":e.Opened?"RegistrationOpen":"Upcoming",
                Judges=Enumerable.Range(0,3).Select(i=> {
                    bool visible=e.Opened&&i<Math.Min(e.RevealedJudges,disclosedByDay);
                    var judge=visible?CompetitionContent.Judge(c.JudgeIds[i]):null;
                    return new PublicJudgeSeat { Seat=i+1,Revealed=visible,Name=visible?judge.Name:"",Description=visible?judge.Description:"" };
                }).ToArray(), Results=c.Results.Select(r=>r.Clone()).ToArray()
            }).ToArray();
        }
        // Caller supplies the rival's current committed body, not a competition-only forecast or stale cache.
        public static RivalDisclosureView Rival(CompetitionState state,BodyState liveRival,int day,string phase,string sourceCommitId)
        {
            CompetitionValidation.Validate(state); var e=state.Current; liveRival.Validate();
            if(liveRival.ActorId!=state.RivalId||string.IsNullOrWhiteSpace(phase)||string.IsNullOrWhiteSpace(sourceCommitId)||
                day<Math.Max(1,CompetitionContent.Offset(e.Ordinal)-CompetitionContent.CycleDays+CompetitionContent.ContestDay)||day>e.ContestDay)throw new ArgumentException("InvalidRivalDisclosureSource");
            int relative=day-CompetitionContent.Offset(e.Ordinal);
            int byDay=CompetitionContent.DisclosureCount(relative);
            int level=Math.Min(e.RevealedJudges,byDay);
            var view=new RivalDisclosureView { ActorId=state.RivalId,Day=day,Phase=phase,SourceCommitId=sourceCommitId,ActorRevision=liveRival.Revision,
                Level=level,StrengthBucket="",BodyFatBucket="",BalanceDescription="" };
            if(CompetitionContent.Discloses("StrengthBucket",level)) {
                long sum=liveRival.Regions.Sum(x=>x.StrengthMicro);
                view.StrengthBucket=Bucket(sum/21,CompetitionConfiguration.Read().StrengthBucketWidth,CompetitionConfiguration.Read().StrengthNormalizer);
            }
            if(new[]{"HighestGroups","LowestGroups","BalanceDescription"}.Any(f=>CompetitionContent.Discloses(f,level))) {
                var groups=CompetitionConfiguration.Read().DisclosureGroups;
                var means=groups.Select(g=> { var regions=liveRival.Regions.Where(r=>g.RegionIds.Contains(r.RegionId)).ToArray();
                    return new { Group=g.Id,Mean=new Rational(regions.Sum(r=>r.StrengthMicro),regions.Length) }; }).ToArray();
                var min=means.Select(x=>x.Mean).Aggregate((a,b)=>a<b?a:b); var max=means.Select(x=>x.Mean).Aggregate((a,b)=>a>b?a:b);
                if(CompetitionContent.Discloses("HighestGroups",level))view.HighestGroups=means.Where(x=>x.Mean.CompareTo(max)==0).Select(x=>x.Group).ToArray();
                if(CompetitionContent.Discloses("LowestGroups",level))view.LowestGroups=means.Where(x=>x.Mean.CompareTo(min)==0).Select(x=>x.Group).ToArray();
                if(CompetitionContent.Discloses("BalanceDescription",level))view.BalanceDescription=min.CompareTo(max)==0?"各肌群均值相同":"肌群均值存在差异";
            }
            if(CompetitionContent.Discloses("BodyFatBucket",level))view.BodyFatBucket=Bucket(liveRival.BodyFatMicro,CompetitionConfiguration.Read().BodyFatBucketWidth,CompetitionConfiguration.Read().FatMaximum);
            if(CompetitionContent.Discloses("Regions",level)) {
                view.RegionIds=Training.RegionIds.All.ToArray(); view.StrengthMicro=view.RegionIds.Select(r=>liveRival.Region(r).StrengthMicro).ToArray();
            }
            if(CompetitionContent.Discloses("BodyFat",level))view.BodyFatMicro=liveRival.BodyFatMicro;
            if(CompetitionContent.Discloses("BaseCharm",level))view.BaseCharmMicro=liveRival.BaseCharmMicro;
            return view;
        }
        private static string Bucket(long micro,int width,int max)
        {
            if(micro==max*NumericKernel.Micro)return max.ToString(System.Globalization.CultureInfo.InvariantCulture);
            long low=micro/(width*NumericKernel.Micro)*width; return "["+low+","+(low+width)+")";
        }
    }
}
