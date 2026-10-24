using System;
using System.Linq;
using System.Collections.Generic;
using FitnessSimulator.Game.Training;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Numeric;

namespace FitnessSimulator.Game.Tests.Training
{
    public static class TrainingInputChecks
    {
        private static void Check(bool ok,string label) { if(!ok)throw new Exception(label); }
        private static void Reject(Action f) { bool rejected=false;try { f(); }catch(ArgumentException) { rejected=true; }catch(InvalidOperationException) { rejected=true; }Check(rejected,"Expected rejection"); }
        private static TrainingSetRequest Request(string id,long difficulty=420000,int reps=2)=>new TrainingSetRequest { SetId="input-set",ActorId="actor",ExerciseId=id,Tier=1,TargetReps=reps,Producer=TrainingProducer.TrustedAutomationInput,DifficultyMicro=difficulty };
        private static void Button(ITrainingInputSession s,TrainingButton b,bool down=true)=>s.Submit(TrainingInput.ButtonEvent(b,down));
        private static void Point(ITrainingInputSession s,TrainingInputKind kind,int id,double x,double y)=>s.Submit(TrainingInput.Pointer(kind,id,x,y));
        private static void Advance(ITrainingInputSession s,int n,bool split,bool pause)
        {
            if(pause&&n>1&&!s.View.IsComplete)
            {
                Advance(s,n/2,split,false);var v=s.View;s.Suspend();s.Advance(10000);
                Check(s.View.AttemptMilliseconds==v.AttemptMilliseconds,"Suspension freezes clock");
                if(v.HeldInputs.Length>0)Reject(()=>s.Resume(Array.Empty<TrainingInput>()));
                s.Resume(v.HeldInputs);s.Advance(350);Advance(s,n-n/2,split,false);return;
            }
            if(split)for(int i=0;i<n;i++)s.Advance(1);else s.Advance(n);
        }
        public static void OneAttempt(ITrainingInputSession s,bool success,bool split=false,bool pause=false)
        {
            Action<int> advance=n=>Advance(s,n,split,pause);
            advance(350);
            string id=s.View.ExerciseId;
            if(id=="bench_press")
            {
                int previous=s.View.EffectiveReps+s.View.FailedAttempts;
                for(int i=0;i<3000&&!s.View.IsComplete&&s.View.EffectiveReps+s.View.FailedAttempts==previous;i++)
                {
                    var v=s.View;
                    bool left=success&&v.Balance>.02,right=!success||v.Balance<-.02;
                    Button(s,TrainingButton.Left,left);Button(s,TrainingButton.Right,right);s.Advance(1);
                }
                return;
            }
            switch(id)
            {
                case "dumbbell_curl":
                    advance(700);Button(s,success?TrainingButton.Raise:TrainingButton.Lower);
                    if(success) { Button(s,TrainingButton.Raise,false);advance(800);Button(s,TrainingButton.Lower); }break;
                case "lat_pulldown":
                    Point(s,TrainingInputKind.PointerDown,7,.5,.1);
                    if(!success) { Point(s,TrainingInputKind.PointerUp,7,.5,.1);break; }
                    advance(100);Point(s,TrainingInputKind.PointerMove,7,.35,.4);advance(100);Point(s,TrainingInputKind.PointerMove,7,.5,.8);advance(100);Point(s,TrainingInputKind.PointerMove,7,.65,.4);advance(100);Point(s,TrainingInputKind.PointerMove,7,.5,.1);break;
                case "triceps_pushdown":
                    Button(s,TrainingButton.Press);
                    if(!success) { Button(s,TrainingButton.Press,false);break; }
                    for(int i=0;i<1600&&!s.View.SignalGreen;i++)advance(1);
                    Button(s,TrainingButton.Press,false);advance(600);break;
                case "squat":
                    int[] cues=new int[3];for(int i=0;i<3;i++) { cues[i]=s.View.VisibleDirection;advance(450); }
                    Point(s,TrainingInputKind.PointerDown,7,.5,.85);
                    if(!success) { Point(s,TrainingInputKind.PointerMove,7,.15+.35*((cues[0]+1)%3),.3);break; }
                    for(int i=0;i<3;i++) { Point(s,TrainingInputKind.PointerMove,7,.15+.35*cues[i],.3);if(i<2)Point(s,TrainingInputKind.PointerMove,7,.5,.85); }break;
                case "hip_thrust":
                    Button(s,TrainingButton.Press);advance(success?(int)Math.Round(s.View.TargetSeconds*1000):1);Button(s,TrainingButton.Press,false);if(success)advance(700);break;
                case "shoulder_press":
                    double a=s.View.FirstOpening,b=s.View.SecondOpening;Point(s,TrainingInputKind.PointerDown,7,.5,.9);
                    if(!success) { Point(s,TrainingInputKind.PointerMove,7,0,.65);break; }
                    Point(s,TrainingInputKind.PointerMove,7,a,.75);Point(s,TrainingInputKind.PointerMove,7,a,.55);Point(s,TrainingInputKind.PointerMove,7,b,.45);advance(100);Point(s,TrainingInputKind.PointerMove,7,b,.25);Point(s,TrainingInputKind.PointerMove,7,.5,.1);Point(s,TrainingInputKind.PointerMove,7,b,.25);Point(s,TrainingInputKind.PointerMove,7,b,.45);Point(s,TrainingInputKind.PointerMove,7,a,.55);Point(s,TrainingInputKind.PointerMove,7,a,.75);Point(s,TrainingInputKind.PointerMove,7,.5,.9);break;
                case "lateral_raise":
                    Point(s,TrainingInputKind.PointerDown,7,.4,.7);
                    if(!success) { Point(s,TrainingInputKind.PointerMove,7,.15,.3);advance(401);break; }
                    Point(s,TrainingInputKind.PointerDown,19,.6,.7);Point(s,TrainingInputKind.PointerMove,7,.15,.3);Point(s,TrainingInputKind.PointerMove,19,.85,.3);advance(300);Point(s,TrainingInputKind.PointerUp,7,.15,.3);Point(s,TrainingInputKind.PointerUp,19,.85,.3);break;
            }
        }
        private static ITrainingInputSession Play(string id,int wanted,long difficulty,uint seed,bool split=false,bool pause=false)
        {
            var s=TrainingInputFactory.Begin(Request(id,difficulty),seed);
            for(int attempt=0;attempt<5&&!s.View.IsComplete;attempt++)OneAttempt(s,s.View.EffectiveReps<wanted,split,pause);
            Check(s.Result!=null,id+" terminal");Check(s.Result.EffectiveReps==wanted,id+" effective expected "+wanted+" got "+s.Result.EffectiveReps+" feedback "+s.View.Feedback);
            Check(s.Result.Success==(wanted==2),id+" success");Check(s.Result.FailedAttempts==(wanted==2?0:3),id+" errors");return s;
        }
        public static void Outcomes(string id)
        { foreach(long d in new long[]{0,420000,1000000})foreach(uint seed in new uint[]{1,17,991})foreach(int v in new[]{0,1,2})Play(id,v,d,seed); }
        public static void PauseAndBatching(string id)
        {
            var a=Play(id,2,1000000,91);var b=Play(id,2,1000000,91,true);var c=Play(id,2,1000000,91,false,true);
            Check(a.Result.InputDigest==b.Result.InputDigest,id+" chunk-invariant digest");Check(a.Result.InputDigest==c.Result.InputDigest,id+" pause-invariant digest");
            var copy=a.Result;copy.EffectiveReps=0;Check(a.Result.EffectiveReps==2,"result clone");
        }
        public static void DistinctRuleBoundaries()
        {
            var curl=TrainingInputFactory.Begin(Request("dumbbell_curl",1000000),1);curl.Advance(350+580);Button(curl,TrainingButton.Raise);Check(curl.View.FailedAttempts==0,"curl inclusive early edge");Button(curl,TrainingButton.Raise,false);curl.Advance(1040);Button(curl,TrainingButton.Lower);Check(curl.View.EffectiveReps==1,"curl inclusive late edge");
            var path=TrainingInputFactory.Begin(Request("lat_pulldown"),1);path.Advance(350);Point(path,TrainingInputKind.PointerDown,7,.5,.1);Point(path,TrainingInputKind.PointerMove,7,.5,.8);path.Advance(121);Check(path.View.FailedAttempts==1,"path cannot skip checkpoints");
            var shoulder=TrainingInputFactory.Begin(Request("shoulder_press",1000000),3);shoulder.Advance(350);Point(shoulder,TrainingInputKind.PointerDown,7,.5,.9);Point(shoulder,TrainingInputKind.PointerMove,7,0,.1);Check(shoulder.View.FailedAttempts==1,"continuous obstacle sweep");
            var lateral=TrainingInputFactory.Begin(Request("lateral_raise"),1);lateral.Advance(350);Point(lateral,TrainingInputKind.PointerDown,7,.4,.7);Point(lateral,TrainingInputKind.PointerMove,19,.15,.3);Check(lateral.View.FailedAttempts==1,"identity replacement rejected");
            var memory=TrainingInputFactory.Begin(Request("squat"),1);memory.Advance(350+1350);Check(memory.View.VisibleDirection==-1,"hidden sequence not leaked");
            var signal=TrainingInputFactory.Begin(Request("triceps_pushdown"),1);signal.Advance(350);Check(!signal.View.SignalGreen&&signal.View.TargetSeconds==0,"future signal not leaked");
        }
        public static void SourceEligibilityAndMetrics()
        {
            Check((int)TrainingProducer.TestInjected==4&&(int)TrainingProducer.TrustedAutomationInput==5,"stable producer values");
            foreach(var source in new[]{TrainingProducer.TrustedAutomationInput,TrainingProducer.NpcPolicy,TrainingProducer.ScriptReplay})
            {
                var body=BodyState.Create("actor",Enumerable.Repeat(10000000L,21).ToArray(),25000000,50000000,0);var progress=TrainingProgress.CreateInitial("actor");
                var quota=DayTrainingQuota.Freeze("actor",1,"plan",new[]{new TrainingPlanEntry { EntryId="curl",ExerciseId="dumbbell_curl",Sets=2,Reps=8 }},progress);
                var playing=TrainingSession.BeginSet(TrainingSession.Begin("workout",quota),body,progress,TrainingSetType.Volume,source);
                var kernel=TrainingInputFactory.Begin(playing.PendingRequest,1);for(int attempt=0;attempt<10&&!kernel.View.IsComplete;attempt++)OneAttempt(kernel,true);Check(kernel.Result!=null,"bounded input driver completed");
                var settled=TrainingSession.Settle(TrainingSession.CompleteResult(playing,kernel.Result),body,progress,1,new Dictionary<string,uint>());
                Check(settled.Progress.Exercise("dumbbell_curl").AutomaticTiers[0]==(source!=TrainingProducer.ScriptReplay),"real input eligibility "+source);
            }
            var bad=Request("dumbbell_curl");bad.Producer=TrainingProducer.TestInjected;Reject(()=>TrainingInputFactory.Begin(bad,1));bad.Producer=TrainingProducer.AutoCapacity;Reject(()=>TrainingInputFactory.Begin(bad,1));
            Check(BodyMetrics.GetThinRegionAdaptation(18000000).CompareTo((Rational)1)==0,"h18");Check(BodyMetrics.GetThinRegionAdaptation(26000000).CompareTo((Rational)1)==0,"h26");Check(BodyMetrics.GetThinRegionAdaptation(48000000).CompareTo((Rational)0)==0,"h48");Reject(()=>BodyMetrics.GetThinRegionAdaptation(-1));
        }
        public static void RunAll(Action<string> report)
        {
            foreach(var d in ExerciseCatalog.All) { Outcomes(d.Id);PauseAndBatching(d.Id);report("PASS inputs "+d.Id+" outcomes/pause/batching"); }
            DistinctRuleBoundaries();SourceEligibilityAndMetrics();RecoveryMaskAndToleranceBoundaries();report("PASS inputs boundaries/eligibility/metrics/recovery");
        }
        public static void RecoveryMaskAndToleranceBoundaries()
        {
            var hip=TrainingInputFactory.Begin(Request("hip_thrust"),4);hip.Advance(350);Button(hip,TrainingButton.Press);hip.Advance(200);hip.Suspend();var held=hip.View.HeldInputs;
            hip.Resume(held);Button(hip,TrainingButton.Press,false);Check(hip.View.IsSuspended&&hip.View.AttemptMilliseconds==200,"resume release requires fresh rebind");Check(hip.View.FailedAttempts==0,"rebuild interruption is not gameplay error");
            hip.Resume(held);hip.Advance(350);hip.Advance((int)Math.Round(hip.View.TargetSeconds*1000)-200);Button(hip,TrainingButton.Press,false);hip.Advance(700);Check(hip.View.EffectiveReps==1,"continued original hold duration");
            var path=TrainingInputFactory.Begin(Request("lat_pulldown",1000000),1);path.Advance(350);Point(path,TrainingInputKind.PointerDown,7,.5,.1);Point(path,TrainingInputKind.PointerMove,7,0,0);path.Advance(120);Check(path.View.FailedAttempts==0,"channel 120ms inclusive");path.Advance(1);Check(path.View.FailedAttempts==1,"channel 121ms fails");
            path=TrainingInputFactory.Begin(Request("lat_pulldown"),1);path.Advance(350);Point(path,TrainingInputKind.PointerDown,7,.5,.1);Point(path,TrainingInputKind.PointerMove,7,.35,.4);Point(path,TrainingInputKind.PointerMove,7,.5,.1);Check(path.View.FailedAttempts==1,"reverse checkpoint immediate failure");
            foreach(int difference in new[]{180,181})
            {
                var lateral=TrainingInputFactory.Begin(Request("lateral_raise",1000000),1);lateral.Advance(350);Point(lateral,TrainingInputKind.PointerDown,7,.4,.7);Point(lateral,TrainingInputKind.PointerDown,19,.6,.7);Point(lateral,TrainingInputKind.PointerMove,7,.15,.3);lateral.Advance(difference);
                if(difference==181) { Check(lateral.View.FailedAttempts==1,"arrival strict over limit");continue; }
                Point(lateral,TrainingInputKind.PointerMove,19,.85,.3);lateral.Advance(300);Point(lateral,TrainingInputKind.PointerUp,7,.15,.3);lateral.Advance(180);Point(lateral,TrainingInputKind.PointerUp,19,.85,.3);Check(lateral.View.EffectiveReps==1,"arrival/release inclusive equal limit");
            }
        }
#if TRAINING_INPUT_STANDALONE
        public static int Main(string[] args) { try { TrainingChecks.RunAll(Console.WriteLine);TrainingM2Checks.RunAll(Console.WriteLine);RunAll(Console.WriteLine);if(args.Length==1)TrainingInputTraceReport.Write(args[0]);return 0; }catch(Exception e) { Console.Error.WriteLine(e);return 1; } }
#endif
    }
}
