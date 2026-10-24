using System;
using System.Diagnostics;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Training;
using UnityEngine;
namespace FitnessSimulator.Game.Tests.Automation {
// TEMP DRAFT ONLY. Policy public-ui-input-v1; geometry/timing source: Production/Presentation/GameFrontendView.Training.cs TrainingHint/DrawTrainingDiagram, lines 147-154 and 197-211. Owner mapping confirmed via architect; not yet compiled.
public static class PublicEightInputDriver {
 public static string DriveActiveSet(IGameFrontend frontend,string expectedExercise,Action<string> trace) {
  string[] supported={"bench_press","dumbbell_curl","lat_pulldown","triceps_pushdown","squat","hip_thrust","shoulder_press","lateral_raise"};
  if(Array.IndexOf(supported,expectedExercise)<0)throw new InvalidOperationException("MissingInputHandler: "+expectedExercise);
  var first=frontend.ReadView().TrainingInput;
  if(first==null||first.ExerciseId!=expectedExercise)throw new InvalidOperationException("NoMatchingActiveSet");
  var port=new PublicPort(frontend,trace);
  try {for(int attempt=0;attempt<100;attempt++) {var v=frontend.ReadView().TrainingInput;if(v==null||v.IsComplete)return RequireResultTransition(frontend);if(v.IsSuspended)throw new InvalidOperationException("SuspendedInputRequiresRebind");try{OneAttempt(port);}catch(InvalidOperationException ex){if(ex.Message!="AttemptEnded")throw;}}}
  catch(InvalidOperationException ex){if(ex.Message=="KernelLeftPublicView" && frontend.ReadView().TrainingInput==null)return RequireResultTransition(frontend);throw;}
  throw new InvalidOperationException("AttemptBudgetExceeded");
 }
 // Does not settle or claim successful reps. The orchestrator must execute the published settle option and verify its accepted reply.
 private static string RequireResultTransition(IGameFrontend frontend) {
  var view=frontend.ReadView(); if(view.TrainingInput!=null && view.TrainingInput.IsComplete){frontend.Advance(0);view=frontend.ReadView();}
  if(view.ScreenId=="error"||view.ScreenId=="menu")throw new InvalidOperationException("UnexpectedKernelExit: "+view.ScreenId);
  return "AwaitProductionResult";
 }
 public sealed class PublicPort {
  private readonly IGameFrontend frontend; private readonly Action<string> trace; private readonly Stopwatch clock=Stopwatch.StartNew();
  private readonly string operationPrefix=Guid.NewGuid().ToString("N"); private int sequence,ticks; private int attemptOrdinal=-1;
  public void BeginAttempt(){attemptOrdinal=View.EffectiveReps+View.FailedAttempts;}
  public void EndAttempt(){attemptOrdinal=-1;}
  private void CheckAttempt(){var v=View;if(attemptOrdinal>=0 && (v.IsComplete || v.EffectiveReps+v.FailedAttempts!=attemptOrdinal))throw new InvalidOperationException("AttemptEnded");}
  public int InputCommands {get;private set;} public int ClockCalls=>ticks;
  public PublicPort(IGameFrontend frontend,Action<string> trace){this.frontend=frontend;this.trace=trace;}
  public FrontendTrainingInputView View { get { var v=frontend.ReadView().TrainingInput;if(v==null)throw new InvalidOperationException("KernelLeftPublicView");return v; } }
  public void Advance(int milliseconds){CheckAttempt();if(milliseconds<0||milliseconds>5000)throw new ArgumentOutOfRangeException();if(++ticks>100000||clock.Elapsed.TotalSeconds>120)throw new InvalidOperationException("InputBudgetExceeded");trace("advance-ms="+milliseconds);frontend.Advance(milliseconds/1000d);}
  public void Submit(TrainingInput input){CheckAttempt();if(View.IsPreparing)throw new InvalidOperationException("InputDuringPreparation");if(++InputCommands>100000)throw new InvalidOperationException("InputCommandBudgetExceeded");var v=frontend.ReadView();var payload=new FrontendTrainingInput {Kind=input.Kind.ToString(),Button=(input.Kind==TrainingInputKind.ButtonDown||input.Kind==TrainingInputKind.ButtonUp)?input.Button.ToString():null,PointerId=input.PointerId,X=input.X,Y=input.Y};var a=new FrontendAction {OperationId=operationPrefix+"/"+(++sequence),RunId=v.RunId,ExpectedRevision=v.Revision,ActionId="training-input",StringValue=JsonUtility.ToJson(payload)};trace(JsonUtility.ToJson(v));trace(JsonUtility.ToJson(a));var r=frontend.Execute(a);trace(JsonUtility.ToJson(r));if(!r.Accepted)throw new InvalidOperationException("InputRejected: "+r.Message);}
 }
 private static void Button(PublicPort s,TrainingButton b,bool down=true)=>s.Submit(TrainingInput.ButtonEvent(b,down));
 private static void Point(PublicPort s,TrainingInputKind kind,int id,double x,double y)=>s.Submit(TrainingInput.Pointer(kind,id,x,y));
        public static void OneAttempt(PublicPort s)
        {
            s.EndAttempt(); bool success=true; Action<int> advance=n=>s.Advance(n);
            for(int preparation=0;s.View.IsPreparing;preparation++){if(preparation>=100)throw new InvalidOperationException("PreparationStalled");advance(10);}
            s.BeginAttempt(); string id=s.View.ExerciseId;
            if(id=="bench_press")
            {
                int previous=s.View.EffectiveReps+s.View.FailedAttempts;
                for(int i=0;i<300&&!s.View.IsComplete&&s.View.EffectiveReps+s.View.FailedAttempts==previous;i++)
                {
                    var v=s.View;
                    bool left=success&&v.Balance>.02,right=!success||v.Balance<-.02;
                    bool heldLeft=v.HeldInputs.Any(x=>x.Kind=="ButtonDown"&&x.Button=="Left");
                    bool heldRight=v.HeldInputs.Any(x=>x.Kind=="ButtonDown"&&x.Button=="Right");
                    if(left!=heldLeft)Button(s,TrainingButton.Left,left);if(right!=heldRight)Button(s,TrainingButton.Right,right);s.Advance(10);
                }
                return;
            }
            switch(id)
            {
                case "dumbbell_curl":
                    if(s.View.Step==0){while(s.View.AttemptMilliseconds<700)advance(Math.Min(10,700-s.View.AttemptMilliseconds));Button(s,TrainingButton.Raise);Button(s,TrainingButton.Raise,false);}
                    if(s.View.Step==1){while(s.View.AttemptMilliseconds<1500)advance(Math.Min(10,1500-s.View.AttemptMilliseconds));Button(s,TrainingButton.Lower);}break;
                case "lat_pulldown":
                    Point(s,TrainingInputKind.PointerDown,7,.5,.1);
                    if(!success) { Point(s,TrainingInputKind.PointerUp,7,.5,.1);break; }
                    advance(100);Point(s,TrainingInputKind.PointerMove,7,.35,.4);advance(100);Point(s,TrainingInputKind.PointerMove,7,.5,.8);advance(100);Point(s,TrainingInputKind.PointerMove,7,.65,.4);advance(100);Point(s,TrainingInputKind.PointerMove,7,.5,.1);break;
                case "triceps_pushdown":
                    Button(s,TrainingButton.Press);
                    if(!success) { Button(s,TrainingButton.Press,false);break; }
                    for(int i=0;i<160&&!s.View.SignalGreen;i++)advance(10);
                    if(!s.View.SignalGreen)throw new InvalidOperationException("SignalNotObserved");
                    Button(s,TrainingButton.Press,false);advance(600);break;
                case "squat":
                    int[] cues={-1,-1,-1};while(s.View.AttemptMilliseconds<1350){var cue=s.View;int index=cue.AttemptMilliseconds/450;if(cue.VisibleDirection>=0)cues[index]=cue.VisibleDirection;advance(10);}
                    if(cues.Any(c=>c<0))throw new InvalidOperationException("PublicCueMissing");
                    Point(s,TrainingInputKind.PointerDown,7,.5,.85);
                    if(!success) { Point(s,TrainingInputKind.PointerMove,7,.15+.35*((cues[0]+1)%3),.3);break; }
                    for(int i=0;i<3;i++) { Point(s,TrainingInputKind.PointerMove,7,.15+.35*cues[i],.3);if(i<2)Point(s,TrainingInputKind.PointerMove,7,.5,.85); }break;
                case "hip_thrust":
                    Button(s,TrainingButton.Press);advance(success?(int)Math.Round(s.View.TargetSeconds*1000):1);Button(s,TrainingButton.Press,false);if(success)advance(700);break;
                case "shoulder_press":
                    double a=s.View.FirstOpening,b=s.View.SecondOpening;Point(s,TrainingInputKind.PointerDown,7,.5,.9);
                    if(!success) { Point(s,TrainingInputKind.PointerMove,7,0,.65);break; }
                    Point(s,TrainingInputKind.PointerMove,7,a,.75);advance(10);Point(s,TrainingInputKind.PointerMove,7,a,.55);advance(10);Point(s,TrainingInputKind.PointerMove,7,b,.45);advance(10);Point(s,TrainingInputKind.PointerMove,7,b,.25);advance(10);Point(s,TrainingInputKind.PointerMove,7,.5,.1);advance(10);Point(s,TrainingInputKind.PointerMove,7,b,.25);advance(10);Point(s,TrainingInputKind.PointerMove,7,b,.45);advance(10);Point(s,TrainingInputKind.PointerMove,7,a,.55);advance(10);Point(s,TrainingInputKind.PointerMove,7,a,.75);advance(10);Point(s,TrainingInputKind.PointerMove,7,.5,.9);break;
                case "lateral_raise":
                    Point(s,TrainingInputKind.PointerDown,7,.4,.7);
                    if(!success) { Point(s,TrainingInputKind.PointerMove,7,.15,.3);advance(401);break; }
                    Point(s,TrainingInputKind.PointerDown,19,.6,.7);Point(s,TrainingInputKind.PointerMove,7,.15,.3);Point(s,TrainingInputKind.PointerMove,19,.85,.3);advance(300);Point(s,TrainingInputKind.PointerUp,7,.15,.3);Point(s,TrainingInputKind.PointerUp,19,.85,.3);break;
            }
        }

}}





