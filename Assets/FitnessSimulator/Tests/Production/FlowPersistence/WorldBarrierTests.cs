using System;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class WorldBarrierTests
    {
        private static WorldBarrierState New(params string[] steps)
        { return WorldBarrier.Create("run", 1, "anchor", "morning-1", new[] { "player", "a", "b", "c" }, "player", 3, steps); }
        private static WorldBarrierState Arrive(WorldBarrierState s, string actor, ArrivalKind kind = ArrivalKind.Arrived)
        { return WorldBarrier.Arrive(s, s.BarrierId, s.ScheduleRevision, actor, kind, actor + "/fact").State; }
        private static WorldBarrierState Open(WorldBarrierState s)
        {
            foreach (string actor in new[] { "player", "a", "b", "c" }) s = Arrive(s, actor);
            return WorldBarrier.BeginInteraction(s, s.BarrierId, 3).State;
        }
        private static WorldBarrierState Step(WorldBarrierState s, string actor, string step)
        { return WorldBarrier.CompleteStep(s, s.BarrierId, 3, actor, step).State; }
        private static WorldBarrierState Finish(WorldBarrierState s)
        {
            foreach (string actor in new[] { "player", "a", "b", "c" })
                s = WorldBarrier.CompleteActor(s, s.BarrierId, 3, actor, actor + "/done").State;
            return s;
        }

        [Test] public void ArrivalGateSkipFactAndCandidateIsolation()
        {
            var s = New(); var candidate = Arrive(s, "player");
            Assert.That(s.Actors[0].Arrival, Is.EqualTo(ArrivalKind.Pending));
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.BeginInteraction(candidate, candidate.BarrierId, 3));
            candidate = Arrive(candidate, "a"); candidate = Arrive(candidate, "b"); candidate = Arrive(candidate, "c", ArrivalKind.Skipped);
            Assert.That(candidate.Actors[3].Arrival, Is.EqualTo(ArrivalKind.Skipped));
            candidate = WorldBarrier.BeginInteraction(candidate, candidate.BarrierId, 3).State;
            Assert.That(WorldBarrier.CanAdvance(candidate), Is.False);
            candidate = WorldBarrier.CompleteActor(candidate, candidate.BarrierId, 3, "a", "a/done").State;
            Assert.That(WorldBarrier.CanAdvance(candidate), Is.False);
            Assert.That(WorldBarrier.CanAdvance(Finish(candidate)), Is.True);
        }

        [Test] public void EveryActorMustJoinEachDayStepAndReplayIsIdempotent()
        {
            var s = Open(New("E01", "E02"));
            s = Step(s, "a", "E01");
            Assert.Throws<InvalidOperationException>(() => Step(s, "a", "E02"));
            Assert.That(WorldBarrier.CompleteStep(s, s.BarrierId, 3, "a", "E01").Changed, Is.False);
            foreach (string actor in new[] { "player", "b", "c" }) s = Step(s, actor, "E01");
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.CompleteActor(s, s.BarrierId, 3, "a", "done"));
            foreach (string actor in new[] { "player", "a", "b", "c" }) s = Step(s, actor, "E02");
            s = Finish(s);
            Assert.That(WorldBarrier.CompleteActor(s, s.BarrierId, 3, "a", "a/done").Changed, Is.False);
            Assert.That(s.Day, Is.EqualTo(1));
        }

        [Test] public void PartialArrivalAndPartialStepJsonRecoveryMatchUninterruptedBaseline()
        {
            var codec = new UnityJsonStateCodec();
            var partial = Arrive(Arrive(New("N01", "N02"), "a"), "b");
            var restored = codec.Decode<WorldBarrierState>(codec.Encode(partial));
            WorldBarrier.Validate(restored);
            foreach (string actor in new[] { "player", "c" }) { partial = Arrive(partial, actor); restored = Arrive(restored, actor); }
            partial = WorldBarrier.BeginInteraction(partial, partial.BarrierId, 3).State;
            restored = WorldBarrier.BeginInteraction(restored, restored.BarrierId, 3).State;
            foreach (string step in partial.StepIds)
                foreach (string actor in new[] { "a", "b", "player", "c" })
                {
                    partial = Step(partial, actor, step); restored = Step(restored, actor, step);
                    restored = codec.Decode<WorldBarrierState>(codec.Encode(restored));
                    WorldBarrier.Validate(restored);
                }
            Assert.That(codec.Encode(Finish(restored)), Is.EqualTo(codec.Encode(Finish(partial))));
            Assert.That(restored.Actors[0].HasWorkout, Is.False);
            Assert.That(restored.Actors[0].HasPendingResult, Is.False);
        }

        [Test] public void PendingResultBlocksCompletionAndCorruptCompletedRestoreIsRejected()
        {
            var codec = new UnityJsonStateCodec();
            var s = Open(New());
            s.Actors[0].HasPendingResult = true; s.Actors[0].PendingResultId = "unsettled";
            s = codec.Decode<WorldBarrierState>(codec.Encode(s));
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.CompleteActor(s, s.BarrierId, 3, "player", "done"));
            Assert.That(WorldBarrier.CanAdvance(s), Is.False);
            s.Actors[0].BarrierReached = true; s.Actors[0].CompletionReference = "invalid-done";
            var corrupt = codec.Decode<WorldBarrierState>(codec.Encode(s));
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.Validate(corrupt));
        }

        [Test] public void CreateCopiesRosterAndStepsAndOptionalReferencesRoundTrip()
        {
            string[] actors = { "player" }, steps = { "N01" };
            var s = WorldBarrier.Create("run", 1, "day", "day-1", actors, "player", 0, steps);
            actors[0] = "changed"; steps[0] = "changed";
            Assert.That(s.Actors[0].ActorId, Is.EqualTo("player"));
            Assert.That(s.StepIds[0], Is.EqualTo("N01"));
            s.Actors[0].HasWorkout = true; s.Actors[0].WorkoutId = "workout-1";
            s.Actors[0].HasPendingResult = true; s.Actors[0].PendingResultId = "result-1";
            var codec = new UnityJsonStateCodec();
            var restored = codec.Decode<WorldBarrierState>(codec.Encode(s));
            WorldBarrier.Validate(restored);
            Assert.That(restored.Actors[0].HasWorkout, Is.True);
            Assert.That(restored.Actors[0].WorkoutId, Is.EqualTo("workout-1"));
            Assert.That(restored.Actors[0].HasPendingResult, Is.True);
            Assert.That(restored.Actors[0].PendingResultId, Is.EqualTo("result-1"));
        }

        [Test] public void StaleIdentityUnknownActorsAndConflictingFactsAreRejected()
        {
            var s = Arrive(New(), "a");
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.Arrive(s, s.BarrierId, 4, "a", ArrivalKind.Arrived, "a/fact"));
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.Arrive(s, WorldBarrier.Key("run", 2, "anchor", "morning-1"), 3, "a", ArrivalKind.Arrived, "a/fact"));
            Assert.Throws<InvalidOperationException>(() => Arrive(s, "unknown"));
            Assert.Throws<InvalidOperationException>(() => Arrive(s, "a", ArrivalKind.Skipped));
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.Arrive(s, s.BarrierId, 3, "a", ArrivalKind.Arrived, "other"));
            Assert.Throws<ArgumentException>(() => WorldBarrier.Arrive(s, s.BarrierId, 3, "b", ArrivalKind.Skipped, ""));
            Assert.Throws<InvalidOperationException>(() => WorldBarrier.Create("run", 1, "day", "day-1", new[] { "a", "a" }, "a", 0, new string[0]));
            Assert.Throws<InvalidOperationException>(() => New("E01", "E01"));
        }
    }
}
