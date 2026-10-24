using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Training
{
    public enum TrainingInputKind { ButtonDown, ButtonUp, PointerDown, PointerMove, PointerUp }
    public enum TrainingButton { Left, Right, Raise, Lower, Press }
    public struct TrainingInput
    {
        public TrainingInputKind Kind;
        public TrainingButton Button;
        public int PointerId;
        public double X,Y;
        public static TrainingInput ButtonEvent(TrainingButton button,bool held) => new TrainingInput { Kind=held?TrainingInputKind.ButtonDown:TrainingInputKind.ButtonUp,Button=button };
        public static TrainingInput Pointer(TrainingInputKind kind,int id,double x,double y) => new TrainingInput { Kind=kind,PointerId=id,X=x,Y=y };
    }
    public sealed class TrainingInputView
    {
        public string ExerciseId,Stage,Feedback;
        public int AttemptMilliseconds,EffectiveReps,FailedAttempts,TargetReps,Step,VisibleDirection=-1;
        public bool IsPreparing,IsSuspended,IsComplete,SignalGreen,IsProgressVisible;
        public double Balance,Window,TargetSeconds,FirstOpening,SecondOpening,BalanceMinimum,BalanceMaximum;
        public TrainingInput[] HeldInputs;
    }
    public interface ITrainingInputSession
    {
        string KernelVersion { get; }
        TrainingInputView View { get; }
        TrainingSetResult Result { get; }
        void Submit(TrainingInput input);
        void Advance(int milliseconds);
        void Suspend();
        void Resume(TrainingInput[] rebuiltHeldInputs);
    }
    public static class TrainingInputFactory
    {
        public const string Version="InputKernelsV1";
        // Static public instructions/geometry only; this never exposes a generated hidden sequence.
        public static KernelDefinition Describe(string exerciseId)=>TrainingContent.Kernel(ExerciseCatalog.Get(exerciseId).KernelId);
        public static ITrainingInputSession Begin(TrainingSetRequest request,uint seed)
        {
            request.Validate();
            if(request.Producer==TrainingProducer.AutoCapacity||request.Producer==TrainingProducer.TestInjected)throw new ArgumentException("Real input source required");
            return request.ExerciseId=="bench_press"?(ITrainingInputSession)new BenchInputAdapter(request,seed):new ExerciseInputSession(request,seed);
        }
    }
    internal sealed class BenchInputAdapter : ITrainingInputSession
    {
        private readonly BenchPressSession session;
        private readonly KernelDefinition parameters;
        public BenchInputAdapter(TrainingSetRequest request,uint seed) { parameters=TrainingInputFactory.Describe(request.ExerciseId);session=BenchPressSession.Begin(request,seed); }
        public string KernelVersion=>TrainingInputFactory.Version;
        public TrainingSetResult Result=>session.Result;
        public TrainingInputView View { get { var v=session.View; var held=new List<TrainingInput>(); if(v.LeftHeld)held.Add(TrainingInput.ButtonEvent(TrainingButton.Left,true)); if(v.RightHeld)held.Add(TrainingInput.ButtonEvent(TrainingButton.Right,true)); return new TrainingInputView { ExerciseId="bench_press",Stage="Balance",Feedback=v.LastFeedback,AttemptMilliseconds=v.AttemptMilliseconds,EffectiveReps=v.EffectiveReps,FailedAttempts=v.FailedAttempts,TargetReps=v.TargetReps,IsPreparing=v.IsPreparing,IsSuspended=v.IsSuspended,IsComplete=v.IsComplete,Balance=v.Balance,BalanceMinimum=parameters.Number("range_min"),BalanceMaximum=parameters.Number("range_max"),Window=v.WindowHalfWidth,HeldInputs=held.ToArray() }; } }
        public void Submit(TrainingInput input)
        {
            if((input.Kind!=TrainingInputKind.ButtonDown&&input.Kind!=TrainingInputKind.ButtonUp)||(input.Button!=TrainingButton.Left&&input.Button!=TrainingButton.Right))throw new ArgumentException("Bench requires left/right buttons");
            var b=input.Button==TrainingButton.Left?BenchButton.Left:BenchButton.Right;
            if(input.Kind==TrainingInputKind.ButtonDown)session.Press(b);else session.Release(b);
        }
        public void Advance(int milliseconds)=>session.Advance(milliseconds);
        public void Suspend()=>session.Suspend();
        public void Resume(TrainingInput[] held)
        {
            if(held==null||held.Any(x=>x.Kind!=TrainingInputKind.ButtonDown||(x.Button!=TrainingButton.Left&&x.Button!=TrainingButton.Right))||held.Select(x=>x.Button).Distinct().Count()!=held.Length)throw new ArgumentException("Invalid rebuilt buttons");
            session.Resume(held.Any(x=>x.Button==TrainingButton.Left),held.Any(x=>x.Button==TrainingButton.Right));
        }
    }
}
