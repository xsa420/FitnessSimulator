using System;
using NUnit.Framework;
using UnityEngine;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Training
{
    public sealed class TrainingM2JsonUtilityTests
    {
        private static T RoundTrip<T>(T value)=>JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        [TestCase(WorkoutPhase.PreSet)]
        [TestCase(WorkoutPhase.Playing)]
        [TestCase(WorkoutPhase.ResultReady)]
        [TestCase(WorkoutPhase.Settled)]
        [TestCase(WorkoutPhase.Ended)]
        public void AllM2PhasesRoundTripWithExplicitPresenceAndNoInputMutation(WorkoutPhase phase)
        {
            var body=TrainingM2Checks.Body(); var progress=TrainingM2Checks.Progress(); var state=TrainingM2Checks.Context(progress);
            if(phase==WorkoutPhase.Ended)state=TrainingSessionM2.EndWorkout(state);
            else if(phase!=WorkoutPhase.PreSet)
            {
                state=TrainingSessionM2.BeginSet(state,body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,TrainingM2Checks.Benefits());
                if(phase!=WorkoutPhase.Playing)
                {
                    state=TrainingSessionM2.CompleteResult(state,TrainingM2Checks.Result(state,1));
                    if(phase==WorkoutPhase.Settled)state=TrainingSessionM2.Settle(state,body,progress,null,TrainingM2Checks.Resolution(TrainingSessionM2.PrepareTeachingClaim(state))).Context;
                }
            }
            var loaded=RoundTrip(state); string before=JsonUtility.ToJson(loaded);
            var normalized=TrainingSessionM2.NormalizeAfterLoad(loaded);
            Assert.That(JsonUtility.ToJson(loaded),Is.EqualTo(before),"normalize must not mutate deserialized input");
            var restored=TrainingSessionM2.Restore(RoundTrip(normalized)); Assert.That(restored.Workout.Phase,Is.EqualTo(phase));
            bool active=phase==WorkoutPhase.Playing||phase==WorkoutPhase.ResultReady||phase==WorkoutPhase.Settled;
            Assert.That(restored.HasSetBenefits,Is.EqualTo(active));
            if(active) { Assert.That(restored.Benefits.SnapshotId,Is.EqualTo("benefits-1")); Assert.That(restored.Benefits.TeachingSources[0].NominalTargetMicro,Is.EqualTo(3000000L)); }
            else Assert.That(restored.Benefits,Is.Null);
            Assert.That(restored.HasTeachingResolution,Is.EqualTo(phase==WorkoutPhase.Settled));
            if(phase==WorkoutPhase.Settled)Assert.That(restored.AppliedTeachingResolution.DeltaMicro,Is.EqualTo(3000000L));
            else Assert.That(restored.AppliedTeachingResolution,Is.Null);
        }
        [Test] public void ActiveMissingFlagOrPayloadCannotBecomeLegacyNone()
        {
            var progress=TrainingM2Checks.Progress();
            var state=TrainingSessionM2.BeginSet(TrainingM2Checks.Context(progress),TrainingM2Checks.Body(),progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,TrainingM2Checks.Benefits());
            state.HasSetBenefits=false;
            Assert.Throws<ArgumentException>(()=>TrainingSessionM2.Restore(RoundTrip(state)));
            state.HasSetBenefits=true; state.Benefits=null;
            Assert.Throws<ArgumentException>(()=>TrainingSessionM2.Restore(RoundTrip(state)));
        }
        [Test] public void SettledRoundTripReplaysOnlyIdenticalTeachingResolution()
        {
            var body=TrainingM2Checks.Body(); var progress=TrainingM2Checks.Progress();
            var state=TrainingSessionM2.BeginSet(TrainingM2Checks.Context(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch,TrainingM2Checks.Benefits());
            state=TrainingSessionM2.CompleteResult(state,TrainingM2Checks.Result(state,1));
            var resolution=TrainingM2Checks.Resolution(TrainingSessionM2.PrepareTeachingClaim(state));
            var done=TrainingSessionM2.Settle(state,body,progress,null,resolution);
            var loaded=TrainingSessionM2.Restore(RoundTrip(done.Context));
            var again=TrainingSessionM2.Settle(loaded,RoundTrip(done.Settlement.Body),RoundTrip(done.Settlement.Progress),null,RoundTrip(resolution));
            Assert.That(again.Settlement.AlreadySettled,Is.True);
            Assert.That(again.Settlement.Body.Revision,Is.EqualTo(done.Settlement.Body.Revision));
            var next=TrainingSessionM2.ContinueAfterFollowers(loaded,done.Settlement.Body);
            Assert.That(TrainingSessionM2.Restore(RoundTrip(next)).HasSetBenefits,Is.False);
            loaded.HasTeachingResolution=false;
            Assert.Throws<ArgumentException>(()=>TrainingSessionM2.Restore(RoundTrip(loaded)));
        }
        [Test] public void ClosedMissedDaySerializesWithoutArtificialWorkout()
        {
            var claim=TrainingClaims.Close(TrainingClaims.CreateDay("run","actor",3,"schedule"),TrainingDayCloseReason.Missed);
            var restored=RoundTrip(claim); restored.Validate();
            Assert.That(restored.HasWorkoutId,Is.False);
            Assert.That(TrainingClaims.TryClaim(restored,"run","actor",3,"schedule","late","evening").Disposition,Is.EqualTo(TrainingClaimDisposition.Closed));
        }
    }
}
