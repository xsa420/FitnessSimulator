using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Competition
{
    // Pure transitions. The caller atomically commits State, reservations, facts and its random-stream receipt.
    public static class CompetitionRules
    {
        public static CompetitionState Create(string runId,string playerId,string rivalId)
        {
            if(string.IsNullOrWhiteSpace(runId)||string.IsNullOrWhiteSpace(playerId)||string.IsNullOrWhiteSpace(rivalId)||playerId==rivalId)
                throw new ArgumentException("InvalidCompetitionIdentity");
            return new CompetitionState { RunId=runId,PlayerId=playerId,RivalId=rivalId,Editions=new[]{NewEdition(runId,1)} };
        }
        public static CompetitionChange Open(CompetitionState state,int day,int[] bodyJudgeDrawIndices,int[] thinJudgeDrawIndices)
        {
            var next=Copy(state); var e=next.Current; RequireDay(e,day,CompetitionContent.OpenDay);
            var body=Draw(ContestKind.Bodybuilding,bodyJudgeDrawIndices); var thin=Draw(ContestKind.ThinBuild,thinJudgeDrawIndices);
            if(e.Opened) {
                if(!e.Contest(ContestKind.Bodybuilding).JudgeIds.SequenceEqual(body)||!e.Contest(ContestKind.ThinBuild).JudgeIds.SequenceEqual(thin))
                    throw new InvalidOperationException("JudgePanelAlreadyFixed");
                return Change(next);
            }
            e.Opened=true; e.Contest(ContestKind.Bodybuilding).JudgeIds=body; e.Contest(ContestKind.ThinBuild).JudgeIds=thin;
            return Change(next);
        }
        public static CompetitionChange AdvanceDisclosure(CompetitionState state,int day)
        {
            var next=Copy(state); var e=next.Current; int relative=day-CompetitionContent.Offset(e.Ordinal);
            if(!e.Opened||relative<CompetitionContent.OpenDay||relative>CompetitionContent.ContestDay)throw new InvalidOperationException("InvalidDisclosureStage");
            int count=CompetitionContent.DisclosureCount(relative);
            if(count<e.RevealedJudges)throw new InvalidOperationException("DisclosureCannotRewind");
            e.RevealedJudges=count; return Change(next);
        }
        public static CompetitionChange SetEnrollment(CompetitionState state,ContestKind kind,int day,bool enrolled,bool slotAvailable)
        {
            CompetitionContent.CheckKind(kind); var next=Copy(state); var e=next.Current;
            int relative=day-CompetitionContent.Offset(e.Ordinal);
            if(!e.Opened||e.RegistrationClosed||relative<CompetitionContent.OpenDay||relative>CompetitionContent.RegistrationCloseDay)throw new InvalidOperationException("RegistrationNotOpen");
            var contest=e.Contest(kind); if(contest.Enrolled==enrolled)return Change(next);
            if(enrolled&&!slotAvailable)throw new InvalidOperationException("CompetitionReservationConflict");
            contest.Enrolled=enrolled;
            return Change(next,new[]{Reservation(e,contest,enrolled)});
        }
        // The phase is an explicit assertion by the trusted time coordinator, never inferred from wall time.
        public static CompetitionChange CloseRegistration(CompetitionState state,int day,string phase)
        {
            var next=Copy(state); var e=next.Current; RequireDay(e,day,CompetitionContent.RegistrationCloseDay);
            if(phase!=CompetitionContent.ClosePhase||!e.Opened)throw new InvalidOperationException("InvalidRegistrationCloseBarrier");
            e.RegistrationClosed=true; return Change(next);
        }
        public static CompetitionChange Freeze(CompetitionState state,CompetitionFreezeInput input)
        {
            var next=Copy(state); var e=next.Current;
            if(input==null)throw new ArgumentNullException(nameof(input)); RequireDay(e,input.Day,CompetitionContent.ContestDay);
            if(!e.RegistrationClosed||e.RevealedJudges!=3||input.Phase!=CompetitionContent.FreezePhase||string.IsNullOrWhiteSpace(input.BarrierId))
                throw new InvalidOperationException("InvalidCompetitionFreezeBarrier");
            CompetitionValidation.Snapshot(input.Player,next.PlayerId,CompetitionSourceKind.PlayerProduction,input.Day);
            CompetitionValidation.Snapshot(input.Rival,next.RivalId,CompetitionSourceKind.NpcProduction,input.Day);
            if(e.FrozenActors.Length>0) {
                if(e.BarrierId!=input.BarrierId||!SameSnapshot(e.FrozenActors[0],input.Player)||!SameSnapshot(e.FrozenActors[1],input.Rival))
                    throw new InvalidOperationException("FrozenInputsCannotChange");
                return Change(next);
            }
            e.BarrierId=input.BarrierId; e.FrozenActors=new[]{input.Player.Clone(),input.Rival.Clone()}; return Change(next);
        }
        public static CompetitionChange Attend(CompetitionState state,ContestKind kind,int day,string anchorId)
        {
            var next=Copy(state); var e=next.Current; var contest=Window(e,kind,day,anchorId);
            if(contest.Results.Length==1)return Change(next);
            RequireLiveWindow(e,contest);
            if(!contest.Enrolled)throw new InvalidOperationException("NotEnrolled");
            var score=CompetitionScoring.Score(kind,e.FrozenActors[0].Body,e.FrozenActors[1].Body,contest.JudgeIds);
            var result=new CompetitionResult { ResultId=contest.ContestId+"/result",BarrierId=e.BarrierId,Score=score };
            contest.Results=new[]{result}; contest.WindowClosed=true;
            var fact=Fact(next,contest,score.Participants.Single(x=>x.ActorId==next.PlayerId).Rank==1?CompetitionFactKind.Champion:CompetitionFactKind.NonChampion,result.ResultId,"ActualAttendance");
            AddFact(e,fact);
            return Change(next,new[]{Reservation(e,contest,false)},new[]{fact});
        }
        public static CompetitionChange Decline(CompetitionState state,ContestKind kind,int day,string anchorId)
        {
            var next=Copy(state); var e=next.Current; var contest=Window(e,kind,day,anchorId);
            if(contest.Declined)return Change(next);
            RequireLiveWindow(e,contest);
            if(!contest.Enrolled)throw new InvalidOperationException("NotEnrolled");
            contest.Declined=true; contest.WindowClosed=true;
            return CloseChange(next,contest);
        }
        public static CompetitionChange CloseWindow(CompetitionState state,ContestKind kind,int day,string anchorId)
        {
            var next=Copy(state); var e=next.Current; var contest=Window(e,kind,day,anchorId);
            if(contest.WindowClosed)return Change(next);
            RequireLiveWindow(e,contest);
            // A pending registered entrant requires an explicit decision; leaving the app never calls this as a decline.
            if(contest.Enrolled)throw new InvalidOperationException("ExplicitAttendanceDecisionRequired");
            contest.WindowClosed=true; return CloseChange(next,contest);
        }
        public static CompetitionChange NextEdition(CompetitionState state)
        {
            var next=Copy(state); var e=next.Current;
            if(e.Failed||!e.Contests.All(x=>x.WindowClosed)||e.ActualAttendanceCount==0)
                throw new InvalidOperationException("EditionCannotContinue");
            next.Editions=next.Editions.Concat(new[]{NewEdition(next.RunId,checked(e.Ordinal+1))}).ToArray(); return Change(next);
        }
        public static CompetitionState Restore(CompetitionState persisted,string expectedRunId,string expectedPlayerId,string expectedRivalId)
        {
            CompetitionValidation.Validate(persisted);
            if(persisted.RunId!=expectedRunId||persisted.PlayerId!=expectedPlayerId||persisted.RivalId!=expectedRivalId)
                throw new ArgumentException("CompetitionSaveIdentityMismatch");
            return persisted.Clone();
        }
        public static CompetitionState Export(CompetitionState state) => Copy(state);
        private static CompetitionChange CloseChange(CompetitionState state,ContestState contest)
        {
            var e=state.Current; var facts=Array.Empty<CompetitionFact>();
            if(contest.Kind==ContestKind.ThinBuild&&e.ActualAttendanceCount==0) {
                var fact=Fact(state,null,CompetitionFactKind.Unfulfilled,"",e.Contests.Any(x=>x.Enrolled)?"NoActualAttendance":"NotEnrolled");
                AddFact(e,fact); facts=new[]{fact};
            }
            return Change(state,contest.Enrolled?new[]{Reservation(e,contest,false)}:null,facts);
        }
        private static ContestState Window(CompetitionEdition e,ContestKind kind,int day,string anchorId)
        {
            CompetitionContent.CheckKind(kind); RequireDay(e,day,CompetitionContent.ContestDay);
            if(anchorId!=CompetitionContent.Anchor(kind)||e.FrozenActors.Length!=2)throw new InvalidOperationException("InvalidContestWindow");
            return e.Contest(kind);
        }
        private static void RequireLiveWindow(CompetitionEdition e,ContestState c)
        {
            if(e.Failed)throw new InvalidOperationException("CompetitionFailureAlreadyCommitted");
            if(c.WindowClosed)throw new InvalidOperationException("ContestWindowClosed");
            if(c.Kind==ContestKind.ThinBuild&&!e.Contest(ContestKind.Bodybuilding).WindowClosed)
                throw new InvalidOperationException("MorningWindowNotProcessed");
        }
        private static CompetitionFact Fact(CompetitionState state,ContestState contest,CompetitionFactKind kind,string resultId,string reason)
        {
            var e=state.Current; string id=contest==null?e.EditionId:contest.ContestId;
            return new CompetitionFact { FactId=id+"/"+kind,RunId=state.RunId,EditionId=e.EditionId,ContestId=contest==null?"":contest.ContestId,
                ActorId=state.PlayerId,ResultId=resultId,Kind=kind,OccurredDay=e.ContestDay,ActualAttendanceCount=e.ActualAttendanceCount,Reason=reason };
        }
        private static void AddFact(CompetitionEdition e,CompetitionFact fact) { e.Facts=e.Facts.Concat(new[]{fact}).ToArray(); }
        private static CompetitionReservationIntent Reservation(CompetitionEdition e,ContestState contest,bool reserve) => new CompetitionReservationIntent {
            ReservationId=contest.ContestId+"/reservation",ContestId=contest.ContestId,AnchorId=CompetitionContent.Anchor(contest.Kind),Day=e.ContestDay,Reserve=reserve };
        private static CompetitionState Copy(CompetitionState state) { CompetitionValidation.Validate(state); return state.Clone(); }
        private static CompetitionChange Change(CompetitionState state,CompetitionReservationIntent[] reservations=null,CompetitionFact[] facts=null) =>
            new CompetitionChange { State=state,ReservationIntents=reservations??Array.Empty<CompetitionReservationIntent>(),Facts=(facts??Array.Empty<CompetitionFact>()).Select(x=>x.Clone()).ToArray() };
        private static CompetitionEdition NewEdition(string runId,int ordinal)
        {
            string id=CompetitionContent.EditionId(runId,ordinal); CompetitionContent.Offset(ordinal);
            return new CompetitionEdition { EditionId=id,Ordinal=ordinal,BarrierId="",Contests=new[]{
                new ContestState { ContestId=id+"/"+CompetitionContent.Contest(ContestKind.Bodybuilding).Id,Kind=ContestKind.Bodybuilding },
                new ContestState { ContestId=id+"/"+CompetitionContent.Contest(ContestKind.ThinBuild).Id,Kind=ContestKind.ThinBuild }} };
        }
        private static void RequireDay(CompetitionEdition e,int day,int relative) { if(day!=checked(CompetitionContent.Offset(e.Ordinal)+relative))throw new InvalidOperationException("InvalidCompetitionDay"); }
        private static string[] Draw(ContestKind kind,int[] indices)
        {
            if(indices==null||indices.Length!=3)throw new ArgumentException("ThreeDrawIndicesRequired");
            var pool=CompetitionContent.Contest(kind).JudgeIds.ToList(); var output=new List<string>();
            foreach(int index in indices) { if(index<0||index>=pool.Count)throw new ArgumentOutOfRangeException(nameof(indices)); output.Add(pool[index]); pool.RemoveAt(index); }
            return output.ToArray();
        }
        private static bool SameSnapshot(CompetitionActorSnapshot a,CompetitionActorSnapshot b) => a.ActorId==b.ActorId&&a.SourceCommitId==b.SourceCommitId&&
            a.ActorRevision==b.ActorRevision&&a.Day==b.Day&&a.Phase==b.Phase&&a.SourceKind==b.SourceKind&&a.Body.BodyFatMicro==b.Body.BodyFatMicro&&
            a.Body.BaseCharmMicro==b.Body.BaseCharmMicro&&a.Body.Regions.All(r=>r.StrengthMicro==b.Body.Region(r.RegionId).StrengthMicro);
    }
}
