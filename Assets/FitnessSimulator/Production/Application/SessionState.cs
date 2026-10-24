using System;
using System.Collections.Generic;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Training;
using FitnessSimulator.Game.Life;

namespace FitnessSimulator.Game.Application
{
    [Serializable] public sealed class AnchorUnlock { public string Id; public int Day; }
    [Serializable]
    public sealed class SessionState
    {
        public int Day = 1;
        public int AnchorIndex;
        public string Phase = "Anchors";
        public int DayStep;
        public int AnchorGameSeconds;
        public int AnchorStandardSeconds;
        public int IncomingOvertime;
        public int LastOvertime;
        public long TotalGameSeconds;
        public uint RandomState = 1;
        public uint InitialRandomSeed = 1;
        public string TemplateId;
        public uint SetSeed;
        public long CompletionChecks;
        public bool HasEffectiveTraining;
        public int EarlyAvailableDay;
        public int LateAvailableDay;
        public List<AnchorUnlock> AnchorUnlocks = new List<AnchorUnlock>();
        public string LastMessage;
        // Successful choices only; absent fields in an older save mean no remembered choice.
        public string LastBreakfastOption;
        public string LastLunchOption;
        public string LastDinnerOption;
        public string DaySummary;
        public NutritionEvaluation Nutrition;
        public BodyState DayStartBody;
        public BodyState BeforeDayEndBody;
        public long DayStartBalance;
        public long BeforeDayEndBalance;
        public List<string> ChangedFields = new List<string>();
    }

    [Serializable]
    public sealed class TrainingDayState
    {
        public bool HasQuota;
        public bool HasWorkout;
        public TrainingProgress Progress;
        public DayTrainingQuota Quota;
        public WorkoutSnapshot Workout;
    }

    [Serializable]
    public sealed class InputEdge
    {
        public long Milliseconds;
        public int Sequence;
        public string Action;
        public int Value;
        public string Payload;
    }
}
