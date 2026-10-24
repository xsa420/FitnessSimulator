using System;
using System.Linq;

namespace FitnessSimulator.Game.Competition
{
    internal static class CompetitionValidation
    {
        public static void Validate(CompetitionState state)
        {
            if(state==null||state.SchemaVersion!=1||state.ContentVersion!=CompetitionContent.Version||state.ScoringVersion!=CompetitionContent.ScoringVersion||
                string.IsNullOrWhiteSpace(state.RunId)||string.IsNullOrWhiteSpace(state.PlayerId)||string.IsNullOrWhiteSpace(state.RivalId)||state.PlayerId==state.RivalId||
                state.Editions==null||state.Editions.Length==0)throw new ArgumentException("InvalidCompetitionSchema");
            for(int i=0;i<state.Editions.Length;i++) {
                var e=state.Editions[i];
                if(e==null||e.Ordinal!=i+1||e.EditionId!=CompetitionContent.EditionId(state.RunId,e.Ordinal)||e.RevealedJudges<0||e.RevealedJudges>3||
                    e.Contests==null||e.Contests.Length!=2||e.Contests.Any(x=>x==null)||e.Contests[0].Kind!=ContestKind.Bodybuilding||e.Contests[1].Kind!=ContestKind.ThinBuild||
                    e.FrozenActors==null||(e.FrozenActors.Length!=0&&e.FrozenActors.Length!=2)||e.Facts==null||e.Facts.Any(x=>x==null))Invalid("InvalidEdition");
                if(!e.Opened&&(e.RevealedJudges!=0||e.RegistrationClosed))Invalid("UnopenedEditionAdvanced");
                if(e.FrozenActors.Length==2) {
                    if(!e.RegistrationClosed||e.RevealedJudges!=3||string.IsNullOrWhiteSpace(e.BarrierId))Invalid("InvalidFrozenEdition");
                    Snapshot(e.FrozenActors[0],state.PlayerId,CompetitionSourceKind.PlayerProduction,e.ContestDay);
                    Snapshot(e.FrozenActors[1],state.RivalId,CompetitionSourceKind.NpcProduction,e.ContestDay);
                } else if(!string.IsNullOrEmpty(e.BarrierId))Invalid("BarrierWithoutSnapshots");
                foreach(var c in e.Contests) {
                    string suffix="/"+CompetitionContent.Contest(c.Kind).Id;
                    if(c.ContestId!=e.EditionId+suffix||c.JudgeIds==null||c.Results==null||c.Results.Length>1||c.Results.Any(x=>x==null))Invalid("InvalidContest");
                    if(e.Opened)CompetitionScoring.ValidateJudges(c.Kind,c.JudgeIds);
                    else if(c.JudgeIds.Length!=0||c.Enrolled)Invalid("UnopenedContestChanged");
                    if((c.WindowClosed||c.Declined||c.Results.Length>0)&&e.FrozenActors.Length!=2)Invalid("ContestBeforeFreeze");
                    if(c.Declined&&(!c.Enrolled||!c.WindowClosed||c.Results.Length!=0))Invalid("InvalidDecline");
                    if(c.WindowClosed&&c.Enrolled&&!c.Declined&&c.Results.Length!=1)Invalid("MissingAttendanceDecision");
                    if(c.Results.Length==1) {
                        if(!c.Enrolled||!c.WindowClosed||c.Declined)Invalid("InvalidAttendance");
                        var r=c.Results[0];
                        if(r.ResultId!=c.ContestId+"/result"||r.BarrierId!=e.BarrierId)Invalid("ResultIdentityMismatch");
                        var expected=CompetitionScoring.Score(c.Kind,e.FrozenActors[0].Body,e.FrozenActors[1].Body,c.JudgeIds);
                        EqualScore(r.Score,expected);
                    }
                }
                var morning=e.Contests[0]; var afternoon=e.Contests[1];
                if(afternoon.WindowClosed&&!morning.WindowClosed)Invalid("AfternoonBeforeMorning");
                if(morning.Results.Length>0&&morning.Results[0].Score.Participants[0].Rank!=1&&afternoon.WindowClosed)Invalid("ContinuedAfterFailure");
                ValidateFacts(state,e);
                if(i<state.Editions.Length-1&&(e.Failed||e.ActualAttendanceCount==0||!e.Contests.All(x=>x.WindowClosed)))Invalid("InvalidEditionContinuation");
            }
        }
        public static void Snapshot(CompetitionActorSnapshot value,string actorId,CompetitionSourceKind kind,int day)
        {
            if(value==null||value.Body==null||value.ActorId!=actorId||value.Body.ActorId!=actorId||value.SourceKind!=kind||
                string.IsNullOrWhiteSpace(value.SourceCommitId)||value.ActorRevision!=value.Body.Revision||value.Day!=day||value.Phase!=CompetitionContent.FreezePhase)
                Invalid("InvalidProductionSnapshotProvenance");
            value.Body.Validate();
        }
        private static void ValidateFacts(CompetitionState state,CompetitionEdition e)
        {
            int expectedCount=e.ActualAttendanceCount+(e.Contests[1].WindowClosed&&e.ActualAttendanceCount==0?1:0);
            if(e.Facts.Length!=expectedCount||e.Facts.Select(x=>x.FactId).Distinct(StringComparer.Ordinal).Count()!=expectedCount)Invalid("InvalidFactCount");
            foreach(var fact in e.Facts) {
                if(fact.RunId!=state.RunId||fact.EditionId!=e.EditionId||fact.ActorId!=state.PlayerId||fact.OccurredDay!=e.ContestDay)Invalid("InvalidFactIdentity");
                if(fact.Kind==CompetitionFactKind.Unfulfilled) {
                    string reason=e.Contests.Any(x=>x.Enrolled)?"NoActualAttendance":"NotEnrolled";
                    if(!e.Contests[1].WindowClosed||e.ActualAttendanceCount!=0||fact.FactId!=e.EditionId+"/Unfulfilled"||fact.ActualAttendanceCount!=0||
                        fact.ContestId!=""||fact.ResultId!=""||fact.Reason!=reason)Invalid("InvalidUnfulfilledFact");
                } else {
                    var c=e.Contests.SingleOrDefault(x=>x.ContestId==fact.ContestId);
                    if(c==null||c.Results.Length!=1)Invalid("FactWithoutResult");
                    var kind=c.Results[0].Score.Participants[0].Rank==1?CompetitionFactKind.Champion:CompetitionFactKind.NonChampion;
                    int count=c.Kind==ContestKind.Bodybuilding?1:e.ActualAttendanceCount;
                    if(fact.Kind!=kind||fact.FactId!=c.ContestId+"/"+kind||fact.ResultId!=c.Results[0].ResultId||fact.ActualAttendanceCount!=count||fact.Reason!="ActualAttendance")Invalid("InvalidResultFact");
                }
            }
        }
        private static void EqualScore(ContestScoreResult actual,ContestScoreResult expected)
        {
            if(actual==null||actual.Kind!=expected.Kind||actual.ScoringVersion!=expected.ScoringVersion||actual.SharedChampionship!=expected.SharedChampionship||
                actual.Participants==null||actual.Participants.Length!=2)Invalid("InvalidResultScore");
            for(int p=0;p<2;p++) {
                var a=actual.Participants[p]; var b=expected.Participants[p];
                if(a==null||a.ActorId!=b.ActorId||a.TotalScoreUnits4!=b.TotalScoreUnits4||a.DisplayMeanCents!=b.DisplayMeanCents||a.Rank!=b.Rank||
                    a.TieKeysMicro==null||!a.TieKeysMicro.SequenceEqual(b.TieKeysMicro)||a.Judges==null||a.Judges.Length!=3)Invalid("InvalidParticipantScore");
                for(int j=0;j<3;j++) {
                    var x=a.Judges[j]; var y=b.Judges[j];
                    if(x==null||x.JudgeId!=y.JudgeId||x.ScoreUnits4!=y.ScoreUnits4||x.ComponentMicro==null||!x.ComponentMicro.SequenceEqual(y.ComponentMicro))Invalid("InvalidJudgeScore");
                    // Historical results may predate commentary; never overwrite their saved fields.
                    if(!string.IsNullOrEmpty(x.CommentaryTextRef)&&x.CommentaryTextRef!=y.CommentaryTextRef)Invalid("InvalidJudgeCommentary");
                }
            }
        }
        private static void Invalid(string reason) { throw new ArgumentException(reason); }
    }
}
