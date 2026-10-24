using System;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Training
{
    public enum BenchButton { Left,Right }
    public sealed class BenchPressView
    {
        public double Balance { get; internal set; }
        public double WindowHalfWidth { get; internal set; }
        public int AttemptMilliseconds { get; internal set; }
        public int StableMilliseconds { get; internal set; }
        public int EffectiveReps { get; internal set; }
        public int FailedAttempts { get; internal set; }
        public int TargetReps { get; internal set; }
        public bool IsPreparing { get; internal set; }
        public bool IsSuspended { get; internal set; }
        public bool IsComplete { get; internal set; }
        public bool LeftHeld { get; internal set; }
        public bool RightHeld { get; internal set; }
        public string LastFeedback { get; internal set; }
    }
    /// <summary>1 ms deterministic control simulation; no body writes, scene, wall clock, or success shortcut.</summary>
    public sealed class BenchPressSession
    {
        private readonly TrainingSetRequest request;
        private readonly long windowPico,driftNanoPerSecond,forceNanoPerSecond,limitPico;
        private readonly KernelDefinition parameters;
        private readonly int failureLimit;
        private uint randomState;
        private readonly uint initialSeed;
        private ulong inputHash=14695981039346656037UL;
        private int inputEvents;
        private long activeTick;
        private long balancePico;
        private int elapsed,stable,tailStable,preparation,resumePreparation,valid,failed;
        private readonly int[] signs;
        private bool left,right,suspended;
        private string feedback="Prepare";
        private TrainingSetResult result;
        private BenchPressSession(TrainingSetRequest request,uint seed)
        {
            this.request=request.Clone(); randomState=seed; initialSeed=seed;
            parameters=TrainingContent.Kernel(ExerciseCatalog.Get(request.ExerciseId).KernelId);failureLimit=TrainingContent.Current.FailureLimit;
            preparation=parameters.PreparationMilliseconds;balancePico=checked((long)Math.Round(parameters.Number("initial_balance")*1000000000000d,MidpointRounding.AwayFromZero));
            windowPico=checked((long)Math.Round((parameters.Number("window_base")-parameters.Number("window_reduction")*request.DifficultyMicro/1000000d)*1000000000000d,MidpointRounding.AwayFromZero));
            driftNanoPerSecond=checked((long)Math.Round((parameters.Number("drift_base")+parameters.Number("drift_gain")*request.DifficultyMicro/1000000d)*1000000000d,MidpointRounding.AwayFromZero));
            forceNanoPerSecond=checked((long)Math.Round(parameters.Number("force")*1000000000d,MidpointRounding.AwayFromZero));limitPico=checked((long)Math.Round(parameters.Number("balance_limit")*1000000000000d,MidpointRounding.AwayFromZero));
            signs=new int[(parameters.Milliseconds("attempt_ms")+parameters.Milliseconds("drift_ms")-1)/parameters.Milliseconds("drift_ms")];
            GenerateAttempt();
        }
        public static BenchPressSession Begin(TrainingSetRequest request,uint seed)
        {
            request.Validate();
            if(request.ExerciseId!="bench_press"||request.Producer==TrainingProducer.AutoCapacity||request.Producer==TrainingProducer.TestInjected)throw new ArgumentException("Bench manual input required");
            return new BenchPressSession(request,seed);
        }
        public TrainingSetResult Result=>result?.Clone();
        public BenchPressView View=>new BenchPressView { Balance=balancePico/1000000000000d,WindowHalfWidth=windowPico/1000000000000d,AttemptMilliseconds=elapsed,StableMilliseconds=stable,EffectiveReps=valid,FailedAttempts=failed,TargetReps=request.TargetReps,IsPreparing=preparation>0||resumePreparation>0,IsSuspended=suspended,IsComplete=result!=null,LeftHeld=left,RightHeld=right,LastFeedback=feedback };
        private void GenerateAttempt()
        {
            for(int i=0;i<signs.Length;i++) { randomState=unchecked(randomState*1664525u+1013904223u); signs[i]=(randomState&0x80000000u)==0?-1:1; }
        }
        public void Press(BenchButton button)=>SetButton(button,true);
        public void Release(BenchButton button)=>SetButton(button,false);
        private void SetButton(BenchButton button,bool held)
        {
            if(suspended||result!=null)throw new InvalidOperationException("Input unavailable");
            if(button!=BenchButton.Left&&button!=BenchButton.Right)throw new ArgumentOutOfRangeException(nameof(button));
            // Repeated UI held-state updates are idempotent, not new physical input evidence.
            if((button==BenchButton.Left?left:right)==held)return;
            inputEvents++;
            unchecked { inputHash^=(ulong)activeTick; inputHash*=1099511628211UL; inputHash^=(ulong)((int)button*2+(held?1:0)); inputHash*=1099511628211UL; }
            if(button==BenchButton.Left)left=held; else if(button==BenchButton.Right)right=held; else throw new ArgumentOutOfRangeException(nameof(button));
        }
        public void Suspend() { if(result==null)suspended=true; }
        public void Resume(bool leftHeld,bool rightHeld)
        {
            if(!suspended)throw new InvalidOperationException("Not suspended");
            if(leftHeld!=left||rightHeld!=right)throw new InvalidOperationException("Rebuild held logical inputs first");
            suspended=false; resumePreparation=parameters.ResumeMilliseconds;
        }
        public void Advance(int elapsedMilliseconds)
        {
            if(elapsedMilliseconds<0)throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            if(suspended||result!=null)return;
            for(int i=0;i<elapsedMilliseconds&&result==null;i++)
            {
                if(resumePreparation>0) { resumePreparation--; continue; }
                activeTick++;
                if(preparation>0) { preparation--; continue; }
                // Pico-position integrates micro-quantized difficulty exactly per millisecond.
                balancePico+=( (right?forceNanoPerSecond:0)-(left?forceNanoPerSecond:0)+signs[elapsed/parameters.Milliseconds("drift_ms")]*driftNanoPerSecond);
                elapsed++;
                bool inWindow=Math.Abs(balancePico)<=windowPico;
                if(inWindow) { stable++; tailStable++; } else tailStable=0;
                if(Math.Abs(balancePico)>limitPico)FinishAttempt(false,"BalanceLimit");
                else if(elapsed==parameters.Milliseconds("attempt_ms"))FinishAttempt(stable>=parameters.Milliseconds("attempt_ms")*parameters.Number("stable_ratio")&&tailStable>=parameters.Milliseconds("tail_ms"),"StabilityWindow");
            }
        }
        private void FinishAttempt(bool success,string reason)
        {
            if(success)valid++; else failed++;
            feedback=success?"ValidRepetition":reason;
            if(valid==request.TargetReps||failed==failureLimit)
            {
                result=new TrainingSetResult { SetId=request.SetId,RulesVersion=request.RulesVersion,Producer=request.Producer,EffectiveReps=valid,FailedAttempts=failed,Success=valid==request.TargetReps,TerminalReason=valid==request.TargetReps?"TargetReached":reason,InputDigest=inputHash.ToString("x16"),InputEventCount=inputEvents,MinigameSeed=initialSeed };
                result.Validate(request); return;
            }
            elapsed=stable=tailStable=0; preparation=parameters.PreparationMilliseconds;balancePico=checked((long)Math.Round(parameters.Number("initial_balance")*1000000000000d,MidpointRounding.AwayFromZero)); GenerateAttempt();
        }
    }
}

