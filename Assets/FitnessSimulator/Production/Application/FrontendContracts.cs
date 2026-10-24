using System;

namespace FitnessSimulator.Game.Application
{
    public interface IFrontendContentProvider
    {
        FitnessSimulator.Game.Configuration.ContentPackage ReadContent();
    }
    // Projection-only boundary. The application owns validation, state and persistence.
    public interface IGameFrontend
    {
        FrontendView ReadView();
        FrontendReply Execute(FrontendAction action);
        void Advance(double unscaledSeconds);
        void SetSuspended(bool suspended);
    }

    [Serializable]
    public sealed class FrontendAction
    {
        public string OperationId;
        public string RunId;
        public long ExpectedRevision;
        public string ActionId;
        public string StringValue;
        public int IntValue;
    }

    [Serializable]
    public sealed class FrontendReply
    {
        public bool Accepted;
        public string Message;
    }

    [Serializable]
    public sealed class FrontendOption
    {
        public string Id;
        public string Label;
        public bool Enabled = true;
        public string DisabledReason;
        public bool RequiresConfirmation;
        public bool IsPreferred;
        public string ConfirmationText;
        public string StringValue;
        public int IntValue;
    }

    [Serializable]
    public sealed class FrontendView
    {
        public string RunId;
        public long Revision;
        public string ScreenId;
        public string Title;
        public string Body;
        public string Status;
        public string PlanText;
        public string ScheduleText;
        public string EndingNoteDraft;
        public bool EndingCanEditNote;
        public bool CanOpenAuxiliary;
        // Automatic business progression, not an assertion that disk IO is still in flight.
        public bool IsTransitioning;
        public FrontendOption[] Actions = Array.Empty<FrontendOption>();
        // Draft-only choices; Id is the anchor. The application projects effective-day eligibility.
        public FrontendOption[] PlanAnchorOptions = Array.Empty<FrontendOption>();
        public string[] BodyRows = Array.Empty<string>();
        public FrontendBenchPressView BenchPress;
        public FrontendTrainingInputView TrainingInput;
    }

    [Serializable]
    public sealed class FrontendTrainingInput
    {
        public string Kind;
        public string Button;
        public int PointerId;
        public double X, Y;
    }

    [Serializable]
    public sealed class FrontendTrainingRebind
    {
        public FrontendTrainingInput[] Inputs = Array.Empty<FrontendTrainingInput>();
    }

    [Serializable]
    public sealed class FrontendTrainingInputView
    {
        public string ExerciseId, Stage, Feedback;
        public int AttemptMilliseconds, EffectiveReps, FailedAttempts, TargetReps, Step, VisibleDirection = -1;
        public bool IsPreparing, IsSuspended, IsComplete, SignalGreen;
        public double Balance, Window, TargetSeconds, FirstOpening, SecondOpening;
        public FrontendTrainingInput[] HeldInputs = Array.Empty<FrontendTrainingInput>();
    }

    [Serializable]
    public sealed class FrontendBenchPressView
    {
        public string SetId;
        public double Balance;
        public double WindowHalfWidth;
        public double Progress;
        public int EffectiveReps;
        public int TargetReps;
        public int Failures;
        public string Instruction;
        public bool IsSuspended;
        public bool IsComplete;
        public bool LeftHeld;
        public bool RightHeld;
    }
}
