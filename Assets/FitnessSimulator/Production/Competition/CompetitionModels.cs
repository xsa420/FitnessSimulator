using System;
using System.Linq;
using FitnessSimulator.Game.Body;

namespace FitnessSimulator.Game.Competition
{
    public enum CompetitionFactKind { Champion, NonChampion, Unfulfilled }
    public enum CompetitionSourceKind { PlayerProduction, NpcProduction, FormulaFixture }
    [Serializable] public sealed class CompetitionActorSnapshot
    {
        public string ActorId, SourceCommitId, Phase;
        public int Day;
        public long ActorRevision;
        public CompetitionSourceKind SourceKind;
        public BodyState Body;
        public CompetitionActorSnapshot Clone() => new CompetitionActorSnapshot { ActorId=ActorId,SourceCommitId=SourceCommitId,
            Phase=Phase,Day=Day,ActorRevision=ActorRevision,SourceKind=SourceKind,Body=Body.Clone() };
    }
    public sealed class CompetitionFreezeInput
    {
        public string BarrierId;
        public int Day;
        public string Phase;
        public CompetitionActorSnapshot Player, Rival;
    }
    [Serializable] public sealed class CompetitionFact
    {
        public string FactId, RunId, EditionId, ContestId, ActorId, ResultId, Reason;
        public CompetitionFactKind Kind;
        public int OccurredDay, ActualAttendanceCount;
        public CompetitionFact Clone() => (CompetitionFact)MemberwiseClone();
    }
    [Serializable] public sealed class CompetitionResult
    {
        public string ResultId, BarrierId;
        public ContestScoreResult Score;
        public CompetitionResult Clone() => new CompetitionResult { ResultId=ResultId,BarrierId=BarrierId,Score=Score.Clone() };
    }
    [Serializable] public sealed class ContestState
    {
        public string ContestId;
        public ContestKind Kind;
        public string[] JudgeIds=Array.Empty<string>();
        public bool Enrolled, WindowClosed, Declined;
        // Arrays avoid engine serialization materializing an absent inline result.
        public CompetitionResult[] Results=Array.Empty<CompetitionResult>();
        public ContestState Clone() => new ContestState { ContestId=ContestId,Kind=Kind,JudgeIds=(string[])JudgeIds.Clone(),
            Enrolled=Enrolled,WindowClosed=WindowClosed,Declined=Declined,Results=Results.Select(x=>x.Clone()).ToArray() };
    }
    [Serializable] public sealed class CompetitionEdition
    {
        public string EditionId, BarrierId;
        public int Ordinal, RevealedJudges;
        public bool Opened, RegistrationClosed;
        public CompetitionActorSnapshot[] FrozenActors=Array.Empty<CompetitionActorSnapshot>();
        public ContestState[] Contests=Array.Empty<ContestState>();
        public CompetitionFact[] Facts=Array.Empty<CompetitionFact>();
        public int ContestDay => CompetitionContent.Offset(Ordinal)+CompetitionContent.ContestDay;
        public int ActualAttendanceCount => Contests.Sum(x=>x.Results.Length);
        public bool Failed => Facts.Any(x=>x.Kind!=CompetitionFactKind.Champion);
        public ContestState Contest(ContestKind kind) => Contests.Single(x=>x.Kind==kind);
        public CompetitionEdition Clone() => new CompetitionEdition { EditionId=EditionId,BarrierId=BarrierId,Ordinal=Ordinal,
            RevealedJudges=RevealedJudges,Opened=Opened,RegistrationClosed=RegistrationClosed,
            FrozenActors=FrozenActors.Select(x=>x.Clone()).ToArray(),Contests=Contests.Select(x=>x.Clone()).ToArray(),Facts=Facts.Select(x=>x.Clone()).ToArray() };
    }
    [Serializable] public sealed class CompetitionState
    {
        public int SchemaVersion=1;
        public string ContentVersion=CompetitionContent.Version, ScoringVersion=CompetitionContent.ScoringVersion;
        public string RunId, PlayerId, RivalId;
        public CompetitionEdition[] Editions=Array.Empty<CompetitionEdition>();
        public CompetitionEdition Current => Editions[Editions.Length-1];
        public CompetitionState Clone() => new CompetitionState { SchemaVersion=SchemaVersion,ContentVersion=ContentVersion,ScoringVersion=ScoringVersion,
            RunId=RunId,PlayerId=PlayerId,RivalId=RivalId,Editions=Editions.Select(x=>x.Clone()).ToArray() };
    }
    public sealed class CompetitionReservationIntent
    {
        public string ReservationId, ContestId, AnchorId;
        public int Day;
        public bool Reserve;
    }
    public sealed class CompetitionChange
    {
        public CompetitionState State;
        public CompetitionReservationIntent[] ReservationIntents=Array.Empty<CompetitionReservationIntent>();
        public CompetitionFact[] Facts=Array.Empty<CompetitionFact>();
    }
}
