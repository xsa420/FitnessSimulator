using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Actors
{
    [Serializable] public sealed class ActorInputStep
    {
        public int AdvanceMilliseconds;
        public bool HasInput;
        public TrainingInputKind InputKind;
        public TrainingButton Button;
        public int PointerId;
        public double X,Y;
        public TrainingInput Input { get=>new TrainingInput { Kind=InputKind,Button=Button,PointerId=PointerId,X=X,Y=Y }; set { InputKind=value.Kind;Button=value.Button;PointerId=value.PointerId;X=value.X;Y=value.Y; } }
    }
    [Serializable] public sealed class ActorInputTrace
    {
        public string PolicyVersion,CharacterId,SetId;
        public uint KernelSeed;
        public int SamplesConsumed;
        public ActorInputStep[] Steps;
        public TrainingSetResult Result;
    }
    // One set only. The coordinator owns the anchor barrier, random cursor and atomic commit.
    // The trace can be replayed from the request and seed after an interrupted preparation.
    public static class ActorInputPolicy
    {
        public static string Version=>ActorContent.Definition.Version;
        public static ActorInputTrace Execute(string characterId,TrainingSetRequest request,uint seed,uint[] samples)
        {
            ActorPolicies.Index(characterId);
            if(request==null||request.ActorId!=characterId||request.Producer!=TrainingProducer.NpcPolicy||samples==null)throw new ArgumentException("NPC input scope mismatch");
            var kernel=TrainingInputFactory.Begin(request,seed);var trace=new List<ActorInputStep>();int cursor=0;
            var policy=ActorContent.Input(characterId,request.ExerciseId);
            double amplitude=policy.ErrorAmplitude,mistakeRate=policy.MistakeProbability;
            Func<double> draw=()=>{if(cursor>=samples.Length)throw new InvalidOperationException("NeedNpcInputSamples");return samples[cursor++]/4294967296d;};
            Action<TrainingInput> send=input=>{kernel.Submit(input);trace.Add(new ActorInputStep { HasInput=true,Input=input });};
            Action<int> advance=ms=>{kernel.Advance(ms);trace.Add(new ActorInputStep { AdvanceMilliseconds=ms });};
            int attempt=-1,greenAt=-1,heldAt=-1,lateralAt=-1,waypoint=0;
            int[] memory={-1,-1,-1};double error=0;bool mistake=false;
            // Every kernel has an attempt timeout; this guards a broken adapter, never creates a result.
            int limit=checked((request.TargetReps+3)*policy.MaximumTicksPerAttempt);
            for(int tick=0;tick<limit&&!kernel.View.IsComplete;tick++)
            {
                var v=kernel.View;int number=v.EffectiveReps+v.FailedAttempts;
                if(number!=attempt)
                {
                    attempt=number;greenAt=heldAt=lateralAt=-1;waypoint=0;memory=new[]{-1,-1,-1};
                    error=(2*draw()-1)*amplitude;mistake=draw()<mistakeRate;
                }
                if(v.IsPreparing){advance(policy.TickMilliseconds);continue;}
                switch(v.ExerciseId)
                {
                    case "bench_press":
                        // Current visible displacement only; an error delays or reverses correction.
                        if(v.AttemptMilliseconds%policy.ControlIntervalMilliseconds==0)
                        {
                            double e=(2*draw()-1)*amplitude*policy.PositionError;bool miss=draw()<mistakeRate;
                            int direction=v.Balance+e>.025?-1:v.Balance+e<-.025?1:0;if(miss)direction=-direction;
                            SetButton(v,TrainingButton.Left,direction<0,send);SetButton(v,TrainingButton.Right,direction>0,send);
                        }
                        break;
                    case "dumbbell_curl":
                        int beat=v.Step==0?700:1500;
                        if(v.AttemptMilliseconds>=beat+(int)(error*policy.TimingErrorMilliseconds))
                        {
                            var button=v.Step==0?TrainingButton.Raise:TrainingButton.Lower;
                            if(mistake)button=button==TrainingButton.Raise?TrainingButton.Lower:TrainingButton.Raise;
                            send(TrainingInput.ButtonEvent(button,true));
                        }
                        break;
                    case "triceps_pushdown":
                        if(v.Stage=="Start")send(TrainingInput.ButtonEvent(TrainingButton.Press,true));
                        else if(v.Stage=="Held")
                        {
                            if(v.SignalGreen&&greenAt<0)greenAt=v.AttemptMilliseconds;
                            if((mistake&&v.AttemptMilliseconds>=policy.FailureTimingMilliseconds)||(greenAt>=0&&v.AttemptMilliseconds>=greenAt+Math.Max(0,policy.ReactionMilliseconds+(int)(error*policy.TimingErrorMilliseconds))))send(TrainingInput.ButtonEvent(TrainingButton.Press,false));
                        }
                        break;
                    case "hip_thrust":
                        if(v.Stage=="Start"){heldAt=v.AttemptMilliseconds;send(TrainingInput.ButtonEvent(TrainingButton.Press,true));}
                        else if(v.Stage=="Held"&&v.AttemptMilliseconds-heldAt>=v.TargetSeconds*1000+error*policy.TimingErrorMilliseconds-(mistake?policy.FailureTimingMilliseconds:0))send(TrainingInput.ButtonEvent(TrainingButton.Press,false));
                        break;
                    case "squat":
                        if(v.VisibleDirection>=0&&v.AttemptMilliseconds<1350)memory[Math.Min(2,v.AttemptMilliseconds/450)]=v.VisibleDirection;
                        if(v.Stage=="Respond")
                        {
                            if(v.HeldInputs.Length==0)send(TrainingInput.Pointer(TrainingInputKind.PointerDown,0,.5,.85));
                            else
                            {
                                var p=v.HeldInputs[0];
                                if(p.Y<.6)send(TrainingInput.Pointer(TrainingInputKind.PointerMove,0,.5,.85));
                                else
                                {
                                    int direction=memory[v.Step];if(direction<0)throw new InvalidOperationException("Missing observed memory cue");
                                    if(mistake)direction=(direction+1)%3;
                                    send(TrainingInput.Pointer(TrainingInputKind.PointerMove,0,new[]{.15,.5,.85}[direction],.3));
                                }
                            }
                        }
                        break;
                    case "lat_pulldown":
                        if(v.HeldInputs.Length==0)send(TrainingInput.Pointer(TrainingInputKind.PointerDown,0,.5,.1));
                        else
                        {
                            double[] xs={.5,.35,.5,.65,.5},ys={.1,.4,.8,.4,.1};
                            double e=(2*draw()-1)*amplitude*policy.PositionError;if(draw()<mistakeRate)e+=policy.FailurePositionOffset;
                            Move(v.HeldInputs[0],xs[v.Step+1],ys[v.Step+1],.07,e,send);
                        }
                        break;
                    case "shoulder_press":
                        if(v.HeldInputs.Length==0)send(TrainingInput.Pointer(TrainingInputKind.PointerDown,0,.5,.9));
                        else
                        {
                            // Approach each visible opening horizontally outside its obstacle band.
                            double[] xs={v.FirstOpening,v.FirstOpening,v.SecondOpening,v.SecondOpening,.5,.5,v.SecondOpening,v.SecondOpening,v.FirstOpening,v.FirstOpening,.5};
                            double[] ys={.72,.58,.42,.28,.1,.2,.28,.42,.58,.72,.9};
                            var p=v.HeldInputs[0];
                            if(waypoint<xs.Length-1&&Math.Abs(p.X-xs[waypoint])<.015&&Math.Abs(p.Y-ys[waypoint])<.015)waypoint++;
                            double e=(2*draw()-1)*amplitude*policy.PositionError;if(draw()<mistakeRate)e+=policy.FailurePositionOffset;
                            Move(p,xs[waypoint],ys[waypoint],.05,e,send);
                        }
                        break;
                    case "lateral_raise":
                        if(v.HeldInputs.Length==0) { send(TrainingInput.Pointer(TrainingInputKind.PointerDown,0,.4,.7));send(TrainingInput.Pointer(TrainingInputKind.PointerDown,1,.6,.7)); }
                        else
                        {
                            var left=v.HeldInputs.FirstOrDefault(p=>p.PointerId==0);var right=v.HeldInputs.FirstOrDefault(p=>p.PointerId==1);
                            bool leftAt=left.Y<=.31,rightAt=right.Y<=.31;
                            if(leftAt&&rightAt)
                            {
                                if(lateralAt<0)lateralAt=v.AttemptMilliseconds;
                                if(v.AttemptMilliseconds-lateralAt>=350)
                                {
                                    send(TrainingInput.Pointer(TrainingInputKind.PointerUp,0,.15,.3));
                                    if(!kernel.View.IsComplete&&kernel.View.EffectiveReps+kernel.View.FailedAttempts==attempt)send(TrainingInput.Pointer(TrainingInputKind.PointerUp,1,.85,.3));
                                }
                            }
                            else
                            {
                                if(!leftAt)Move(left,.15,.3,.06,0,send);
                                if(v.AttemptMilliseconds>=Math.Max(0,error*policy.TimingErrorMilliseconds)+(mistake?policy.FailureTimingMilliseconds:0)&&!rightAt)Move(right,.85,.3,.06,0,send);
                            }
                        }
                        break;
                    default:throw new ArgumentException("Unsupported NPC input exercise");
                }
                if(!kernel.View.IsComplete)advance(policy.TickMilliseconds);
            }
            if(!kernel.View.IsComplete)throw new InvalidOperationException("NPC input step budget exhausted");
            return new ActorInputTrace { PolicyVersion=Version,CharacterId=characterId,SetId=request.SetId,KernelSeed=seed,SamplesConsumed=cursor,Steps=trace.ToArray(),Result=kernel.Result };
        }
        static void SetButton(TrainingInputView view,TrainingButton button,bool down,Action<TrainingInput> send)
        {
            bool held=view.HeldInputs.Any(p=>p.Kind==TrainingInputKind.ButtonDown&&p.Button==button);
            if(held!=down)send(TrainingInput.ButtonEvent(button,down));
        }
        static void Move(TrainingInput p,double x,double y,double distance,double xError,Action<TrainingInput> send)
        {
            double dx=x-p.X,dy=y-p.Y,length=Math.Sqrt(dx*dx+dy*dy),scale=length<=distance||length==0?1:distance/length;
            send(TrainingInput.Pointer(TrainingInputKind.PointerMove,p.PointerId,Math.Max(0,Math.Min(1,p.X+dx*scale+xError)),Math.Max(0,Math.Min(1,p.Y+dy*scale))));
        }
    }
}
