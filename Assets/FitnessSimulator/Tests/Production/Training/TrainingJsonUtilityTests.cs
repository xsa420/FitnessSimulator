using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Training
{
    /// <summary>Uses actual Unity serialization. Intentionally excluded from the standalone .NET project.</summary>
    public sealed class TrainingJsonUtilityTests
    {
        private static T RoundTrip<T>(T value)=>JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        private static BodyState Body()=>BodyState.Create("player",Enumerable.Repeat(10000000L,21).ToArray(),25000000,50000000,0);
        private static WorkoutSnapshot Begin(TrainingProgress progress)=>TrainingSession.Begin("json-workout",DayTrainingQuota.Freeze("player",3,"json-plan",new[]{new TrainingPlanEntry { EntryId="bench",ExerciseId="bench_press",Sets=2,Reps=6 }},progress));
        private static TrainingSetResult Play(TrainingSetRequest request)
        {
            var game=BenchPressSession.Begin(request,123456);
            for(int tick=0;tick<30000&&!game.View.IsComplete;tick++)
            {
                var balance=game.View.Balance;
                if(balance>0.02) { game.Press(BenchButton.Left); game.Release(BenchButton.Right); }
                else if(balance< -0.02) { game.Press(BenchButton.Right); game.Release(BenchButton.Left); }
                else { game.Release(BenchButton.Left); game.Release(BenchButton.Right); }
                game.Advance(1);
            }
            Assert.That(game.Result,Is.Not.Null); Assert.That(game.Result.Success,Is.True);
            return game.Result;
        }
        private static WorkoutSnapshot Ready(BodyState body,TrainingProgress progress)
        {
            var playing=TrainingSession.BeginSet(Begin(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
            return TrainingSession.CompleteResult(playing,Play(playing.PendingRequest));
        }
        private static void AssertNoPending(WorkoutSnapshot state)
        {
            Assert.That(state.PendingRequest,Is.Null);
            Assert.That(state.PendingResult,Is.Null);
            Assert.That(state.PreSetBody,Is.Null);
        }
        [Test] public void PreSetRoundTripClearsAbsentInlineObjectsAndCanRestAndBegin()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player");
            var raw=RoundTrip(Begin(progress));
            // Do not assume a particular serializer version returns null: normalize either representation.
            TrainingSession.NormalizeAfterLoad(raw); AssertNoPending(raw);
            var restored=TrainingSession.Restore(RoundTrip(raw)); AssertNoPending(restored);
            var rest=TrainingSession.Rest(restored,body,30);
            var playing=TrainingSession.BeginSet(rest.Workout,rest.Body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
            Assert.That(playing.Phase,Is.EqualTo(WorkoutPhase.Playing));
            Assert.That(playing.PendingResult,Is.Null);
        }
        [Test] public void EndedRoundTripDoesNotClonePhantomBodyAndRemainsEnded()
        {
            var ended=TrainingSession.EndWorkout(Begin(TrainingProgress.CreateInitial("player")));
            var raw=RoundTrip(ended);
            // Clone must also be safe before explicit application normalization.
            var copy=TrainingSession.Clone(raw); AssertNoPending(copy);
            var restored=TrainingSession.Restore(raw);
            Assert.That(restored.Phase,Is.EqualTo(WorkoutPhase.Ended)); AssertNoPending(restored);
            Assert.Throws<InvalidOperationException>(()=>TrainingSession.Rest(restored,Body(),30));
        }
        [Test] public void PlayingRoundTripPreservesPreSetContextButNoResult()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player");
            var playing=TrainingSession.BeginSet(Begin(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
            var raw=RoundTrip(playing); TrainingSession.NormalizeAfterLoad(raw);
            Assert.That(raw.PendingResult,Is.Null);
            var restored=TrainingSession.Restore(RoundTrip(raw));
            Assert.That(restored.PendingRequest.SetId,Is.EqualTo(playing.PendingRequest.SetId));
            Assert.That(restored.NextSetSequence,Is.EqualTo(playing.NextSetSequence));
            Assert.That(restored.PreSetBody.Region("chest_upper").StrengthMicro,Is.EqualTo(10000000L));
            var ready=TrainingSession.CompleteResult(restored,Play(restored.PendingRequest));
            Assert.That(ready.Phase,Is.EqualTo(WorkoutPhase.ResultReady));
        }
        [Test] public void ResultReadyRoundTripSettlesSavedResultWithoutNewPlay()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player"); var ready=Ready(body,progress);
            var restored=TrainingSession.Restore(RoundTrip(ready));
            Assert.That(restored.PendingResult.InputDigest,Is.EqualTo(ready.PendingResult.InputDigest));
            Assert.That(restored.PendingResult.MinigameSeed,Is.EqualTo(123456u));
            var result=TrainingSession.Settle(restored,RoundTrip(body),RoundTrip(progress),3,null);
            Assert.That(result.Workout.Phase,Is.EqualTo(WorkoutPhase.Settled));
            Assert.That(result.Calculation.GameSeconds,Is.EqualTo(36));
            Assert.That(result.Body.Exercise("bench_press").ProficiencyMicro,Is.EqualTo(8000000L));
        }
        [Test] public void SettledRoundTripDoesNotRepeatEffectsAndClearsPayloadAfterFollowers()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player");
            var settled=TrainingSession.Settle(Ready(body,progress),body,progress,3,null);
            var loadedBody=RoundTrip(settled.Body); var loadedProgress=RoundTrip(settled.Progress);
            var loaded=TrainingSession.Restore(RoundTrip(settled.Workout));
            var repeated=TrainingSession.Settle(loaded,loadedBody,loadedProgress,3,null);
            Assert.That(repeated.AlreadySettled,Is.True);
            Assert.That(repeated.Body.Revision,Is.EqualTo(loadedBody.Revision));
            Assert.That(repeated.Workout.ElapsedGameSeconds,Is.EqualTo(36));
            var next=TrainingSession.ContinueAfterFollowers(loaded,loadedBody);
            var nextLoaded=TrainingSession.Restore(RoundTrip(next));
            AssertNoPending(nextLoaded); Assert.That(nextLoaded.SetOrdinal,Is.EqualTo(2));
            Assert.That(nextLoaded.SettledSetIds,Has.Length.EqualTo(1));
            var ended=TrainingSession.EndWorkout(nextLoaded);
            AssertNoPending(TrainingSession.Restore(RoundTrip(ended)));
        }
        [Test] public void RequiredPayloadMissingIsRejectedNotSynthesized()
        {
            var body=Body(); var progress=TrainingProgress.CreateInitial("player");
            var playing=TrainingSession.BeginSet(Begin(progress),body,progress,TrainingSetType.Volume,TrainingProducer.PlayerTouch);
            playing.PreSetBody=null;
            Assert.Throws<ArgumentException>(()=>TrainingSession.NormalizeAfterLoad(RoundTrip(playing)));
            var ready=Ready(body,progress); ready.PendingResult=null;
            Assert.Throws<ArgumentException>(()=>TrainingSession.NormalizeAfterLoad(RoundTrip(ready)));
            var unknown=Begin(progress); unknown.Phase=(WorkoutPhase)999;
            Assert.Throws<ArgumentException>(()=>TrainingSession.NormalizeAfterLoad(RoundTrip(unknown)));
        }
        [Test] public void FatigueRemainderAndInjurySurviveActualSerializer()
        {
            var body=Body(); body.Region("chest_upper").SetTemporary(new FitnessSimulator.Game.Numeric.Rational(80,3));
            body.Region("chest_upper").InjuredDay=3; body.Region("chest_upper").ExpiresAtStartOfDay=6;
            var loaded=RoundTrip(body); loaded.Validate();
            Assert.That(loaded.Region("chest_upper").ExactTemporary.ToString(),Is.EqualTo("80/3"));
            Assert.That(loaded.Region("chest_upper").ExpiresAtStartOfDay,Is.EqualTo(6));
        }
    }
}
