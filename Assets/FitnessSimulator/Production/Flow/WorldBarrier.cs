using System;
using System.Collections.Generic;
using System.Globalization;

namespace FitnessSimulator.Game.Flow
{
    public enum BarrierStage { Arriving, Interacting, Complete }
    public enum ArrivalKind { Pending, Arrived, Skipped }

    [Serializable]
    public sealed class ActorCursor
    {
        public string ActorId;
        public ArrivalKind Arrival;
        public string ArrivalReference;
        public bool HasWorkout;
        public string WorkoutId;
        public bool HasPendingResult;
        public string PendingResultId;
        public int NextStepIndex;
        public bool BarrierReached;
        public string CompletionReference;
    }

    public sealed class BarrierTransition
    {
        public WorldBarrierState State;
        public bool Changed;
    }

    [Serializable]
    public sealed class WorldBarrierState
    {
        public string RunId;
        public int Day;
        public string Phase;
        public string AnchorOccurrenceId;
        public string BarrierId;
        public string PlayerActorId;
        public long ScheduleRevision;
        public BarrierStage Stage;
        public List<string> StepIds = new List<string>();
        public List<ActorCursor> Actors = new List<ActorCursor>();
    }

    public static class WorldBarrier
    {
        public static WorldBarrierState Create(string runId, int day, string phase, string anchorOccurrenceId,
            IEnumerable<string> participants, string playerActorId, long scheduleRevision, IEnumerable<string> stepIds)
        {
            var state = new WorldBarrierState { RunId = runId, Day = day, Phase = phase,
                AnchorOccurrenceId = anchorOccurrenceId, PlayerActorId = playerActorId, ScheduleRevision = scheduleRevision,
                BarrierId = Key(runId, day, phase, anchorOccurrenceId), StepIds = new List<string>(stepIds) };
            foreach (string actor in participants) state.Actors.Add(new ActorCursor { ActorId = actor });
            Validate(state); return state;
        }

        public static string Key(string runId, int day, string phase, string anchorOccurrenceId)
        {
            Require(runId); Require(phase); Require(anchorOccurrenceId);
            return Field(runId) + Field(day.ToString(CultureInfo.InvariantCulture)) + Field(phase) + Field(anchorOccurrenceId);
        }

        public static void Validate(WorldBarrierState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Day < 1 || state.ScheduleRevision < 0 || state.BarrierId != Key(state.RunId, state.Day, state.Phase, state.AnchorOccurrenceId)
                || state.Actors == null || state.Actors.Count == 0 || state.StepIds == null || !Enum.IsDefined(typeof(BarrierStage), state.Stage))
                throw new InvalidOperationException("Invalid barrier identity/state.");
            Require(state.PlayerActorId);
            var steps = new HashSet<string>(StringComparer.Ordinal);
            foreach (string step in state.StepIds) { Require(step); if (!steps.Add(step)) throw new InvalidOperationException("Duplicate step."); }
            var actors = new HashSet<string>(StringComparer.Ordinal);
            int minimum = state.StepIds.Count, maximum = 0;
            bool allReached = true;
            foreach (var actor in state.Actors)
            {
                if (actor == null) throw new InvalidOperationException("Missing actor cursor.");
                Require(actor.ActorId);
                if (!actors.Add(actor.ActorId) || actor.NextStepIndex < 0 || actor.NextStepIndex > state.StepIds.Count)
                    throw new InvalidOperationException("Invalid actor cursor.");
                if (!Enum.IsDefined(typeof(ArrivalKind), actor.Arrival)) throw new InvalidOperationException("Unknown arrival kind.");
                if (actor.Arrival != ArrivalKind.Pending) Require(actor.ArrivalReference);
                if (actor.HasWorkout) Require(actor.WorkoutId);
                if (actor.HasPendingResult) Require(actor.PendingResultId);
                if (actor.BarrierReached) Require(actor.CompletionReference);
                if ((state.Stage != BarrierStage.Arriving && actor.Arrival == ArrivalKind.Pending)
                    || (state.Stage == BarrierStage.Arriving && (actor.NextStepIndex != 0 || actor.BarrierReached))
                    || (actor.BarrierReached && (actor.NextStepIndex != state.StepIds.Count || actor.HasPendingResult)))
                    throw new InvalidOperationException("Actor phase disagrees with barrier.");
                minimum = Math.Min(minimum, actor.NextStepIndex); maximum = Math.Max(maximum, actor.NextStepIndex);
                allReached &= actor.BarrierReached;
            }
            if (!actors.Contains(state.PlayerActorId) || maximum - minimum > 1 || (state.Stage == BarrierStage.Complete) != allReached)
                throw new InvalidOperationException("Invalid participant barrier.");
        }

        public static BarrierTransition Arrive(WorldBarrierState state, string barrierId, long scheduleRevision,
            string actorId, ArrivalKind kind, string actualEntryReference)
        {
            state = Candidate(state, barrierId, scheduleRevision); Require(actualEntryReference);
            if (kind != ArrivalKind.Arrived && kind != ArrivalKind.Skipped) throw new ArgumentException("Explicit arrival or skip required.");
            var actor = Find(state, actorId);
            if (actor.Arrival != ArrivalKind.Pending) {
                Same(actor.ArrivalReference, actualEntryReference);
                if (actor.Arrival != kind) throw new InvalidOperationException("Conflicting arrival kind.");
                return Result(state, false);
            }
            if (state.Stage != BarrierStage.Arriving) throw new InvalidOperationException("Arrival phase closed.");
            actor.ArrivalReference = actualEntryReference; actor.Arrival = kind; return Result(state, true);
        }

        public static BarrierTransition BeginInteraction(WorldBarrierState state, string barrierId, long scheduleRevision)
        {
            state = Candidate(state, barrierId, scheduleRevision);
            if (state.Stage != BarrierStage.Arriving) return Result(state, false);
            foreach (var actor in state.Actors) if (actor.Arrival == ArrivalKind.Pending) throw new InvalidOperationException("Still awaiting arrivals.");
            state.Stage = BarrierStage.Interacting; return Result(state, true);
        }

        public static BarrierTransition CompleteStep(WorldBarrierState state, string barrierId, long scheduleRevision, string actorId, string stepId)
        {
            state = Candidate(state, barrierId, scheduleRevision); var actor = Find(state, actorId);
            int index = state.StepIds.IndexOf(stepId);
            if (index < 0) throw new InvalidOperationException("Unknown step.");
            if (index < actor.NextStepIndex) return Result(state, false);
            if (state.Stage != BarrierStage.Interacting || index != actor.NextStepIndex) throw new InvalidOperationException("Out of order step.");
            foreach (var other in state.Actors) if (other.NextStepIndex < index) throw new InvalidOperationException("Previous step barrier incomplete.");
            actor.NextStepIndex++; return Result(state, true);
        }

        public static BarrierTransition CompleteActor(WorldBarrierState state, string barrierId, long scheduleRevision, string actorId, string completionReference)
        {
            state = Candidate(state, barrierId, scheduleRevision); Require(completionReference); var actor = Find(state, actorId);
            if (actor.BarrierReached) { Same(actor.CompletionReference, completionReference); return Result(state, false); }
            if (state.Stage != BarrierStage.Interacting || actor.NextStepIndex != state.StepIds.Count || actor.HasPendingResult)
                throw new InvalidOperationException("Actor has unfinished work.");
            actor.BarrierReached = true; actor.CompletionReference = completionReference;
            bool all = true; foreach (var other in state.Actors) all &= other.BarrierReached;
            if (all) state.Stage = BarrierStage.Complete;
            return Result(state, true);
        }

        public static bool CanAdvance(WorldBarrierState state) { Validate(state); return state.Stage == BarrierStage.Complete; }
        private static BarrierTransition Result(WorldBarrierState state, bool changed)
        { Validate(state); return new BarrierTransition { State = state, Changed = changed }; }
        private static WorldBarrierState Candidate(WorldBarrierState state, string barrierId, long scheduleRevision)
        {
            Validate(state);
            if (state.BarrierId != barrierId || state.ScheduleRevision != scheduleRevision)
                throw new InvalidOperationException("Stale barrier or schedule.");
            var copy = new WorldBarrierState { RunId = state.RunId, Day = state.Day, Phase = state.Phase,
                AnchorOccurrenceId = state.AnchorOccurrenceId, BarrierId = state.BarrierId, PlayerActorId = state.PlayerActorId,
                ScheduleRevision = state.ScheduleRevision, Stage = state.Stage, StepIds = new List<string>(state.StepIds) };
            foreach (var actor in state.Actors) copy.Actors.Add(new ActorCursor {
                ActorId = actor.ActorId, Arrival = actor.Arrival, ArrivalReference = actor.ArrivalReference,
                HasWorkout = actor.HasWorkout, WorkoutId = actor.WorkoutId, HasPendingResult = actor.HasPendingResult,
                PendingResultId = actor.PendingResultId, NextStepIndex = actor.NextStepIndex,
                BarrierReached = actor.BarrierReached, CompletionReference = actor.CompletionReference });
            return copy;
        }
        private static ActorCursor Find(WorldBarrierState state, string actorId)
        {
            foreach (var actor in state.Actors) if (string.Equals(actor.ActorId, actorId, StringComparison.Ordinal)) return actor;
            throw new InvalidOperationException("Actor is not a participant.");
        }
        private static void Same(string saved, string requested) { if (saved != requested) throw new InvalidOperationException("Conflicting duplicate."); }
        private static string Field(string value) { return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }
        private static void Require(string value) { if (string.IsNullOrEmpty(value)) throw new ArgumentException("Required reference is empty."); }
    }
}
