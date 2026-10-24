using System;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Competition;
using FitnessSimulator.Game.Numeric;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Planning
{
    public sealed class CompetitionTests
    {
        // Synthetic fixtures exercise formulas and coordinator-contract validation only.
        // Their source labels are assertions under test, never evidence of a real production run.
        private static readonly int[] Draws={0,0,0};
        private static string[] Judges(ContestKind kind)=>CompetitionContent.Judges.Where(x=>x.Kind==kind).Take(3).Select(x=>x.Id).ToArray();
        internal static BodyState Body(string id,int strength=20,int fat=20,int charm=50)=>BodyState.Create(id,Enumerable.Repeat(strength*NumericKernel.Micro,21).ToArray(),fat*NumericKernel.Micro,charm*NumericKernel.Micro,0);
        internal static CompetitionState Open()=>CompetitionRules.Open(CompetitionRules.Create("run","p","r"),8,Draws,Draws).State;
        internal static CompetitionActorSnapshot Snapshot(BodyState body,CompetitionSourceKind kind,int day=42)=>new CompetitionActorSnapshot {
            ActorId=body.ActorId,Body=body,ActorRevision=body.Revision,SourceCommitId="commit-"+body.ActorId,Day=day,Phase=CompetitionContent.FreezePhase,SourceKind=kind };
        internal static CompetitionFreezeInput Input(int playerStrength=20,int rivalStrength=20,int day=42)=>new CompetitionFreezeInput {
            BarrierId="barrier-"+day,Day=day,Phase=CompetitionContent.FreezePhase,Player=Snapshot(Body("p",playerStrength),CompetitionSourceKind.PlayerProduction,day),
            Rival=Snapshot(Body("r",rivalStrength),CompetitionSourceKind.NpcProduction,day) };
        internal static CompetitionState Ready(bool body=true,bool thin=true,int playerStrength=20,int rivalStrength=20)
        {
            var s=Open(); s=CompetitionRules.SetEnrollment(s,ContestKind.Bodybuilding,8,body,true).State;
            s=CompetitionRules.SetEnrollment(s,ContestKind.ThinBuild,8,thin,true).State;
            s=CompetitionRules.AdvanceDisclosure(s,35).State;
            s=CompetitionRules.CloseRegistration(s,41,"AllAnchorsFinishedBeforeDayEnd").State;
            return CompetitionRules.Freeze(s,Input(playerStrength,rivalStrength)).State;
        }
        [Test] public void PublishedFormulaExamplesUseFourDecimalComparisonAndTwoDecimalDisplay()
        {
            var body=CompetitionScoring.Score(ContestKind.Bodybuilding,Body("p",20),Body("r",18),Judges(ContestKind.Bodybuilding));
            Assert.That(body.Participants[0].Judges.Select(x=>x.ScoreUnits4),Is.EqualTo(new long[]{336471,326667,384375}));
            Assert.That(body.Participants[0].DisplayMeanCents,Is.EqualTo(3492)); Assert.That(body.Participants[0].Rank,Is.EqualTo(1));
            var thin=CompetitionScoring.Score(ContestKind.ThinBuild,Body("p",20),Body("r",30),Judges(ContestKind.ThinBuild));
            Assert.That(thin.Participants[0].Judges.Select(x=>x.ScoreUnits4),Is.EqualTo(new long[]{900000,909375,850000}));
            Assert.That(thin.Participants[1].Judges.Select(x=>x.ScoreUnits4),Is.EqualTo(new long[]{800000,800284,768182}));
            Assert.That(thin.Participants[0].DisplayMeanCents,Is.EqualTo(8865)); Assert.That(thin.Participants[1].DisplayMeanCents,Is.EqualTo(7895));
        }
        [Test] public void EqualInputsProduceSharedChampionshipWithoutIdentityTieBreak()
        {
            foreach(var kind in new[]{ContestKind.Bodybuilding,ContestKind.ThinBuild}) {
                var result=CompetitionScoring.Score(kind,Body("z"),Body("a"),Judges(kind));
                Assert.That(result.SharedChampionship,Is.True); Assert.That(result.Participants.Select(x=>x.Rank),Is.EqualTo(new[]{1,1}));
            }
        }
        [Test] public void QuantizedScoreTiesUseShortfallThenMeanBeforeDeclaringCoChampions()
        {
            var a=Body("p",20,13); var b=Body("r",20,13);
            foreach(var region in a.Regions)region.StrengthMicro+=60;
            var score=CompetitionScoring.Score(ContestKind.Bodybuilding,a,b,Judges(ContestKind.Bodybuilding));
            Assert.That(score.Participants[0].TotalScoreUnits4,Is.EqualTo(score.Participants[1].TotalScoreUnits4));
            Assert.That(score.Participants[0].TieKeysMicro[0],Is.GreaterThan(score.Participants[1].TieKeysMicro[0]));
            Assert.That(score.SharedChampionship,Is.False); Assert.That(score.Participants[0].Rank,Is.EqualTo(1));
            a=Body("p",20,13); foreach(var region in a.Regions.Skip(1))region.StrengthMicro+=70;
            score=CompetitionScoring.Score(ContestKind.Bodybuilding,a,b,Judges(ContestKind.Bodybuilding));
            Assert.That(score.Participants[0].TotalScoreUnits4,Is.EqualTo(score.Participants[1].TotalScoreUnits4));
            Assert.That(score.Participants[0].TieKeysMicro[0],Is.EqualTo(score.Participants[1].TieKeysMicro[0]));
            Assert.That(score.Participants[0].TieKeysMicro[1],Is.GreaterThan(score.Participants[1].TieKeysMicro[1]));
            Assert.That(score.Participants[0].Rank,Is.EqualTo(1));
        }
        [Test] public void FatEndpointsAndThinStrengthPlateauFollowSharedBodyFormula()
        {
            foreach(var j in CompetitionContent.Judges) {
                Assert.That(CompetitionScoring.FatAdaptation(5000000,j.FatLower,j.FatUpper).CompareTo(0),Is.Zero);
                Assert.That(CompetitionScoring.FatAdaptation(50000000,j.FatLower,j.FatUpper).CompareTo(0),Is.Zero);
                Assert.That(CompetitionScoring.FatAdaptation(j.FatLower*NumericKernel.Micro,j.FatLower,j.FatUpper).CompareTo(1),Is.Zero);
                Assert.That(CompetitionScoring.FatAdaptation(j.FatUpper*NumericKernel.Micro,j.FatLower,j.FatUpper).CompareTo(1),Is.Zero);
            }
            foreach(int value in new[]{0,48,100})Assert.That(BodyMetrics.GetThinBuildIndexMicro(Body("p",value)),Is.Zero);
            foreach(int value in new[]{18,26})Assert.That(BodyMetrics.GetThinBuildIndexMicro(Body("p",value)),Is.EqualTo(1000000));
        }
        [Test] public void ThinScoreTiesUseQuantizedLThenMeanFatAdaptationThenBaseCharm()
        {
            var panel=CompetitionContent.Judges.Where(x=>x.Kind==ContestKind.ThinBuild).Skip(1).Select(x=>x.Id).ToArray();
            var p=Body("p",26,18); var r=Body("r",26,18); r.Region("abs").StrengthMicro+=150;
            var score=CompetitionScoring.Score(ContestKind.ThinBuild,p,r,panel);
            Assert.That(score.Participants[0].TotalScoreUnits4,Is.EqualTo(score.Participants[1].TotalScoreUnits4));
            Assert.That(score.Participants[0].TieKeysMicro[0],Is.GreaterThan(score.Participants[1].TieKeysMicro[0]));
            Assert.That(score.Participants[0].Rank,Is.EqualTo(1));
            p=Body("p"); r=Body("r"); r.BodyFatMicro+=49;
            score=CompetitionScoring.Score(ContestKind.ThinBuild,p,r,Judges(ContestKind.ThinBuild));
            Assert.That(score.Participants[0].TotalScoreUnits4,Is.EqualTo(score.Participants[1].TotalScoreUnits4));
            Assert.That(score.Participants[0].TieKeysMicro[0],Is.EqualTo(score.Participants[1].TieKeysMicro[0]));
            Assert.That(score.Participants[0].TieKeysMicro[1],Is.GreaterThan(score.Participants[1].TieKeysMicro[1]));
            Assert.That(score.Participants[0].Rank,Is.EqualTo(1));
            p=Body("p"); r=Body("r"); p.BaseCharmMicro+=60;
            score=CompetitionScoring.Score(ContestKind.ThinBuild,p,r,Judges(ContestKind.ThinBuild));
            Assert.That(score.Participants[0].TotalScoreUnits4,Is.EqualTo(score.Participants[1].TotalScoreUnits4));
            Assert.That(score.Participants[0].TieKeysMicro.Take(2),Is.EqualTo(score.Participants[1].TieKeysMicro.Take(2)));
            Assert.That(score.Participants[0].TieKeysMicro[2],Is.GreaterThan(score.Participants[1].TieKeysMicro[2]));
            Assert.That(score.Participants[0].Rank,Is.EqualTo(1));
        }
        [Test] public void BaseCharmIsCountedOnceAndFatigueDoesNotAddCompetitionPenalty()
        {
            var p=Body("p"); var r=Body("r"); var baseScore=CompetitionScoring.Score(ContestKind.ThinBuild,p,r,Judges(ContestKind.ThinBuild));
            p.BaseCharmMicro+=10000000;
            var charmScore=CompetitionScoring.Score(ContestKind.ThinBuild,p,r,Judges(ContestKind.ThinBuild));
            Assert.That(charmScore.Participants[0].Judges.Zip(baseScore.Participants[0].Judges,(a,b)=>a.ScoreUnits4-b.ScoreUnits4),Is.EqualTo(new long[]{20000,15000,30000}));
            p.StressMicro=100000000; p.MentalStressMicro=100000000;
            foreach(var region in p.Regions) { region.LocalFatigueMicro=100000000; region.SetTemporary(140); region.InjuredDay=41; region.ExpiresAtStartOfDay=45; }
            var fatigueScore=CompetitionScoring.Score(ContestKind.ThinBuild,p,r,Judges(ContestKind.ThinBuild));
            Assert.That(fatigueScore.Participants[0].TotalScoreUnits4,Is.EqualTo(charmScore.Participants[0].TotalScoreUnits4));
            var bodyScore=CompetitionScoring.Score(ContestKind.Bodybuilding,p,r,Judges(ContestKind.Bodybuilding));
            Assert.That(bodyScore.SharedChampionship,Is.True);
        }
        [Test] public void ChangedSnapshotFixtureChangesOutcomeInsteadOfUsingOpponentCurve()
        {
            var weak=CompetitionRules.Attend(Ready(true,false,20,10),ContestKind.Bodybuilding,42,"morning");
            var strong=CompetitionRules.Attend(Ready(true,false,20,30),ContestKind.Bodybuilding,42,"morning");
            Assert.That(weak.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Champion));
            Assert.That(strong.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.NonChampion));
            Assert.That(strong.State.Current.FrozenActors[1].Body.Regions.All(r=>r.StrengthMicro==30000000),Is.True);
        }
        [Test] public void DrawIsWithoutReplacementAndOpeningCannotRedraw()
        {
            var s=Open(); Assert.That(s.Current.Contests.All(x=>x.JudgeIds.Distinct().Count()==3),Is.True);
            Assert.That(CompetitionRules.Open(s,8,Draws,Draws).State.Current.Contests[0].JudgeIds,Is.EqualTo(s.Current.Contests[0].JudgeIds));
            Assert.Throws<InvalidOperationException>(()=>CompetitionRules.Open(s,8,new[]{1,0,0},Draws));
            Assert.Throws<ArgumentOutOfRangeException>(()=>CompetitionRules.Open(CompetitionRules.Create("run","p","r"),8,new[]{0,3,0},Draws));
        }
        [Test] public void EnrollmentReservesAndWithdrawReleasesWithoutFacts()
        {
            var s=Open(); Assert.Throws<InvalidOperationException>(()=>CompetitionRules.SetEnrollment(s,ContestKind.Bodybuilding,8,true,false));
            var added=CompetitionRules.SetEnrollment(s,ContestKind.Bodybuilding,8,true,true);
            Assert.That(s.Current.Contests[0].Enrolled,Is.False); Assert.That(added.ReservationIntents[0].AnchorId,Is.EqualTo("morning"));
            Assert.That(added.ReservationIntents[0].Day,Is.EqualTo(42)); Assert.That(added.ReservationIntents[0].Reserve,Is.True);
            Assert.That(CompetitionRules.SetEnrollment(added.State,ContestKind.Bodybuilding,9,true,false).ReservationIntents,Is.Empty);
            var removed=CompetitionRules.SetEnrollment(added.State,ContestKind.Bodybuilding,41,false,false);
            Assert.That(removed.ReservationIntents[0].Reserve,Is.False); Assert.That(removed.Facts,Is.Empty);
        }
        [Test] public void DeadlineIsBeforeDayEndAndDoesNotFailUnregisteredPlayer()
        {
            var s=Open(); Assert.Throws<InvalidOperationException>(()=>CompetitionRules.CloseRegistration(s,41,"DayEnd"));
            var closed=CompetitionRules.CloseRegistration(s,41,"AllAnchorsFinishedBeforeDayEnd");
            Assert.That(closed.Facts,Is.Empty);
            Assert.Throws<InvalidOperationException>(()=>CompetitionRules.SetEnrollment(closed.State,ContestKind.ThinBuild,41,true,true));
        }
        [Test] public void PublicDisclosureNeverReturnsUnrevealedNamesWeightsOrPrematureScores()
        {
            var s=Open(); Assert.That(CompetitionQueries.Entries(s,8).All(c=>c.Judges.All(j=>!j.Revealed&&j.Name=="")&&c.Results.Length==0),Is.True);
            s=CompetitionRules.AdvanceDisclosure(s,14).State;
            Assert.That(CompetitionQueries.Entries(s,14)[0].Judges.Count(x=>x.Revealed),Is.EqualTo(1));
            var rival=CompetitionQueries.Rival(s,Body("r",20),14,"DayOpenReady","commit");
            Assert.That(rival.StrengthBucket,Is.EqualTo("[20,30)")); Assert.That(rival.StrengthMicro,Is.Empty); Assert.That(rival.BodyFatBucket,Is.Empty);
            s=CompetitionRules.AdvanceDisclosure(s,28).State; rival=CompetitionQueries.Rival(s,Body("r",20),28,"DayOpenReady","commit");
            Assert.That(rival.BodyFatBucket,Is.EqualTo("[20,25)")); Assert.That(rival.HighestGroups.Length,Is.EqualTo(8)); Assert.That(rival.StrengthMicro,Is.Empty);
            s=CompetitionRules.AdvanceDisclosure(s,35).State; rival=CompetitionQueries.Rival(s,Body("r",20),35,"DayOpenReady","commit");
            Assert.That(rival.StrengthMicro.Length,Is.EqualTo(21)); Assert.That(rival.BaseCharmMicro,Is.EqualTo(50000000));
        }
        [Test] public void FreezeRejectsStaleRivalWrongIdentityRevisionAndFormulaProvenance()
        {
            var frozen=Ready(); var s=frozen.Clone(); s.Current.FrozenActors=Array.Empty<CompetitionActorSnapshot>(); s.Current.BarrierId="";
            var input=Input(); input.Rival.Day=41; Assert.Throws<ArgumentException>(()=>CompetitionRules.Freeze(s,input));
            input=Input(); input.Rival.ActorId="other"; Assert.Throws<ArgumentException>(()=>CompetitionRules.Freeze(s,input));
            input=Input(); input.Rival.ActorRevision=1; Assert.Throws<ArgumentException>(()=>CompetitionRules.Freeze(s,input));
            input=Input(); input.Rival.SourceKind=CompetitionSourceKind.FormulaFixture; Assert.Throws<ArgumentException>(()=>CompetitionRules.Freeze(s,input));
            input=Input(); input.Phase="Morning"; Assert.Throws<InvalidOperationException>(()=>CompetitionRules.Freeze(s,input));
        }
        [Test] public void FreezeClonesInputsAndConflictingRefreezeIsRejected()
        {
            var s=Ready(); var same=CompetitionRules.Freeze(s,Input()); Assert.That(same.Facts,Is.Empty);
            Assert.Throws<InvalidOperationException>(()=>CompetitionRules.Freeze(s,Input(21)));
            var exported=CompetitionRules.Export(s); exported.Current.FrozenActors[0].Body.Regions[0].StrengthMicro=0;
            Assert.That(s.Current.FrozenActors[0].Body.Regions[0].StrengthMicro,Is.EqualTo(20000000));
        }
        [Test] public void MorningAttendanceDoesNotCalculateAfternoonAndReplayEmitsNothing()
        {
            var s=Ready(); Assert.Throws<InvalidOperationException>(()=>CompetitionRules.Attend(s,ContestKind.ThinBuild,42,"afternoon"));
            var attended=CompetitionRules.Attend(s,ContestKind.Bodybuilding,42,"morning");
            Assert.That(attended.State.Current.ActualAttendanceCount,Is.EqualTo(1)); Assert.That(attended.State.Current.Contests[1].Results,Is.Empty);
            Assert.That(attended.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Champion)); Assert.That(attended.Facts[0].ResultId,Is.Not.Empty);
            Assert.That(CompetitionRules.Attend(attended.State,ContestKind.Bodybuilding,42,"morning").Facts,Is.Empty);
            Assert.That(s.Current.ActualAttendanceCount,Is.Zero);
        }
        [Test] public void NoEnrollmentFailsOnlyAtLastWindowWithoutInventedScore()
        {
            var s=Ready(false,false); var morning=CompetitionRules.CloseWindow(s,ContestKind.Bodybuilding,42,"morning"); Assert.That(morning.Facts,Is.Empty);
            var afternoon=CompetitionRules.CloseWindow(morning.State,ContestKind.ThinBuild,42,"afternoon");
            Assert.That(afternoon.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Unfulfilled)); Assert.That(afternoon.State.Current.ActualAttendanceCount,Is.Zero);
            Assert.That(afternoon.State.Current.Contests.All(x=>x.Results.Length==0),Is.True);
            Assert.That(CompetitionRules.CloseWindow(afternoon.State,ContestKind.ThinBuild,42,"afternoon").Facts,Is.Empty);
            Assert.Throws<InvalidOperationException>(()=>CompetitionRules.NextEdition(afternoon.State));
        }
        [Test] public void DeclineIsExplicitAndOnlyBothUnattendedFail()
        {
            var s=Ready(); Assert.Throws<InvalidOperationException>(()=>CompetitionRules.CloseWindow(s,ContestKind.Bodybuilding,42,"morning"));
            var morning=CompetitionRules.Decline(s,ContestKind.Bodybuilding,42,"morning"); Assert.That(morning.Facts,Is.Empty);
            var afternoon=CompetitionRules.Decline(morning.State,ContestKind.ThinBuild,42,"afternoon");
            Assert.That(afternoon.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Unfulfilled));
            Assert.That(CompetitionRules.Decline(afternoon.State,ContestKind.ThinBuild,42,"afternoon").Facts,Is.Empty);
        }
        [Test] public void MorningChampionThenDeclineSatisfiesEdition()
        {
            var morning=CompetitionRules.Attend(Ready(),ContestKind.Bodybuilding,42,"morning");
            var afternoon=CompetitionRules.Decline(morning.State,ContestKind.ThinBuild,42,"afternoon");
            Assert.That(afternoon.Facts,Is.Empty); Assert.That(afternoon.State.Current.Failed,Is.False);
            Assert.That(CompetitionRules.NextEdition(afternoon.State).State.Current.Ordinal,Is.EqualTo(2));
        }
        [Test] public void MorningDeclineAndAfternoonChampionSatisfiesEdition()
        {
            var morning=CompetitionRules.Decline(Ready(),ContestKind.Bodybuilding,42,"morning");
            var afternoon=CompetitionRules.Attend(morning.State,ContestKind.ThinBuild,42,"afternoon");
            Assert.That(afternoon.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Champion)); Assert.That(afternoon.State.Current.ActualAttendanceCount,Is.EqualTo(1));
        }
        [Test] public void MorningChampionThenAfternoonLossPreservesBothFactsAndBlocksContinuation()
        {
            var morning=CompetitionRules.Attend(Ready(true,true,30,20),ContestKind.Bodybuilding,42,"morning");
            Assert.That(morning.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Champion));
            var afternoon=CompetitionRules.Attend(morning.State,ContestKind.ThinBuild,42,"afternoon");
            Assert.That(afternoon.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.NonChampion));
            Assert.That(afternoon.State.Current.Facts.Select(x=>x.Kind),Is.EqualTo(new[]{CompetitionFactKind.Champion,CompetitionFactKind.NonChampion}));
            Assert.Throws<InvalidOperationException>(()=>CompetitionRules.NextEdition(afternoon.State));
        }
        [Test] public void MorningLossBlocksAfternoonWithoutFabricatingSecondResult()
        {
            var morning=CompetitionRules.Attend(Ready(true,true,10,20),ContestKind.Bodybuilding,42,"morning");
            Assert.That(morning.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.NonChampion));
            Assert.Throws<InvalidOperationException>(()=>CompetitionRules.Attend(morning.State,ContestKind.ThinBuild,42,"afternoon"));
            Assert.That(morning.State.Current.Contests[1].Results,Is.Empty);
        }
        [Test] public void SecondEditionPreservesHistoryAndRenewsJudgesDeadlineAndObligation()
        {
            var s=CompetitionRules.Attend(Ready(true,false),ContestKind.Bodybuilding,42,"morning").State;
            s=CompetitionRules.CloseWindow(s,ContestKind.ThinBuild,42,"afternoon").State;
            var next=CompetitionRules.NextEdition(s).State; Assert.That(next.Current.ContestDay,Is.EqualTo(84)); Assert.That(next.Current.ActualAttendanceCount,Is.Zero);
            Assert.That(CompetitionQueries.Entries(next,42).Length,Is.EqualTo(2));
            var sameDay=CompetitionQueries.Rival(next,Body("r"),42,"AfternoonClosed","commit");
            Assert.That(sameDay.Level,Is.Zero); Assert.That(sameDay.StrengthMicro,Is.Empty);
            Assert.That(next.Editions[0].Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Champion)); Assert.That(next.Current.Contests[0].JudgeIds,Is.Empty);
            next=CompetitionRules.Open(next,50,new[]{3,2,1},Draws).State; next=CompetitionRules.AdvanceDisclosure(next,77).State;
            next=CompetitionRules.CloseRegistration(next,83,"AllAnchorsFinishedBeforeDayEnd").State; next=CompetitionRules.Freeze(next,Input(20,20,84)).State;
            next=CompetitionRules.CloseWindow(next,ContestKind.Bodybuilding,84,"morning").State;
            var closed=CompetitionRules.CloseWindow(next,ContestKind.ThinBuild,84,"afternoon"); Assert.That(closed.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Unfulfilled));
            Assert.That(s.Current.Ordinal,Is.EqualTo(1));
        }
        [Test] public void RestoreRejectsAlteredScoresFactsSnapshotsAndOwner()
        {
            var s=CompetitionRules.Attend(Ready(),ContestKind.Bodybuilding,42,"morning").State;
            Assert.That(CompetitionRules.Restore(s,"run","p","r").Current.ActualAttendanceCount,Is.EqualTo(1));
            Assert.Throws<ArgumentException>(()=>CompetitionRules.Restore(s,"other","p","r"));
            var corrupt=s.Clone(); corrupt.Current.Contests[0].Results[0].Score.Participants[0].Rank=2;
            Assert.Throws<ArgumentException>(()=>CompetitionRules.Restore(corrupt,"run","p","r"));
            corrupt=s.Clone(); corrupt.Current.Facts[0].Kind=CompetitionFactKind.NonChampion;
            Assert.Throws<ArgumentException>(()=>CompetitionRules.Restore(corrupt,"run","p","r"));
            corrupt=s.Clone(); corrupt.Current.FrozenActors[1].Day=41;
            Assert.Throws<ArgumentException>(()=>CompetitionRules.Restore(corrupt,"run","p","r"));
        }
    }
}
