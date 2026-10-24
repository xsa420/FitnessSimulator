using System;

namespace FitnessSimulator.Training
{
    public enum TrainingSetType { Volume, StrengthChallenge }
    public enum TrainingExecutionMode { Manual, Automatic }
    public enum TrainingSetOutcome { Completed, Failed, Interrupted }

    [Serializable]
    public sealed class TrainingSetRequest
    {
        public string exerciseId;
        public string[] targetMuscleIds;
        public int attemptedLevel;
        public float attemptedWeight;
        public string weightUnit;
        public int targetRepetitions;
        public TrainingSetType setType;
        public float baseDifficulty;
    }

    [Serializable]
    public sealed class TrainingSetResult
    {
        public TrainingSetRequest request;
        public int actualRepetitions;
        public TrainingSetOutcome outcome;
        public TrainingExecutionMode executionMode;
        public string performanceSchema;
        public string performancePayload;
        public string historicalPerformanceSourceId;
        public double gameDuration;
        public string durationUnit;
        public int consecutiveStrengthSets;
    }

    // Each approved minigame supplies its own input, scoring and completion implementation.
    // No default score/growth formula is inferred from a presentation clip.
    public interface ITrainingMinigame
    {
        bool IsRunning { get; }
        void Begin(TrainingSetRequest request, Action<TrainingSetResult> onFinished);
    }
}
