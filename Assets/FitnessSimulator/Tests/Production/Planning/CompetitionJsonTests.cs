using System.Linq;
using FitnessSimulator.Game.Competition;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Planning
{
    // Engine serializer checks are intentionally excluded from the standalone runner.
    public sealed class CompetitionJsonTests
    {
        private static CompetitionState RoundTrip(CompetitionState state)=>CompetitionRules.Restore(
            JsonUtility.FromJson<CompetitionState>(JsonUtility.ToJson(CompetitionRules.Export(state))),"run","p","r");
        [Test] public void EmptyOpenedAndEnrolledStatesPreserveAbsentResultsAndStablePanels()
        {
            var initial=RoundTrip(CompetitionRules.Create("run","p","r"));
            Assert.That(initial.Current.FrozenActors,Is.Empty); Assert.That(initial.Current.Contests.All(c=>c.Results.Length==0),Is.True);
            var opened=RoundTrip(CompetitionTests.Open());
            var enrolled=RoundTrip(CompetitionRules.SetEnrollment(opened,ContestKind.ThinBuild,14,true,true).State);
            Assert.That(enrolled.Current.Contests[1].Enrolled,Is.True);
            Assert.That(enrolled.Current.Contests[0].JudgeIds,Is.EqualTo(opened.Current.Contests[0].JudgeIds));
            Assert.That(enrolled.Current.FrozenActors,Is.Empty); Assert.That(enrolled.Current.Contests[1].Results,Is.Empty);
        }
        [Test] public void FrozenSharedInputsAndSettledMorningResumePendingAfternoon()
        {
            var frozen=RoundTrip(CompetitionTests.Ready());
            Assert.That(frozen.Current.FrozenActors.Length,Is.EqualTo(2));
            var morning=RoundTrip(CompetitionRules.Attend(frozen,ContestKind.Bodybuilding,42,"morning").State);
            Assert.That(morning.Current.ActualAttendanceCount,Is.EqualTo(1)); Assert.That(morning.Current.Contests[1].Results,Is.Empty);
            Assert.That(CompetitionRules.Attend(morning,ContestKind.Bodybuilding,42,"morning").Facts,Is.Empty);
            var afternoon=RoundTrip(CompetitionRules.Attend(morning,ContestKind.ThinBuild,42,"afternoon").State);
            Assert.That(afternoon.Current.ActualAttendanceCount,Is.EqualTo(2)); Assert.That(afternoon.Current.Facts.Length,Is.EqualTo(2));
            var next=RoundTrip(CompetitionRules.NextEdition(afternoon).State);
            Assert.That(next.Editions[0].Contests[0].Results[0].ResultId,Is.EqualTo(morning.Current.Contests[0].Results[0].ResultId));
            Assert.That(next.Current.ContestDay,Is.EqualTo(84)); Assert.That(next.Current.FrozenActors,Is.Empty);
        }
        [Test] public void LastWindowUnfulfilledFactRoundTripsWithoutInventedScore()
        {
            var s=CompetitionRules.CloseWindow(CompetitionTests.Ready(false,false),ContestKind.Bodybuilding,42,"morning").State;
            s=RoundTrip(CompetitionRules.CloseWindow(s,ContestKind.ThinBuild,42,"afternoon").State);
            Assert.That(s.Current.Facts.Single().Kind,Is.EqualTo(CompetitionFactKind.Unfulfilled));
            Assert.That(s.Current.Contests.All(c=>c.Results.Length==0),Is.True);
            Assert.That(CompetitionRules.CloseWindow(s,ContestKind.ThinBuild,42,"afternoon").Facts,Is.Empty);
        }
    }
}
