using System;
using System.Collections.Generic;
using System.Linq;

namespace FitnessSimulator.Game.Training
{
    // All durations use the active 1ms clock. No wall clock, scene or body mutations.
    internal sealed class ExerciseInputSession : ITrainingInputSession
    {
        private readonly TrainingSetRequest request;
        private readonly KernelDefinition parameters;
        private readonly int failureLimit;
        private readonly uint seed;
        private uint random;
        private ulong digest=14695981039346656037UL;
        private long ticks;
        private int events,elapsed,prepare,resumePrepare,valid,failed,step,start,signal,target,outside,holdStart=-1;
        private readonly int[] directions;
        private readonly Dictionary<int,TrainingInput> pointers=new Dictionary<int,TrainingInput>();
        private readonly HashSet<TrainingButton> buttons=new HashSet<TrainingButton>();
        private readonly int[] sideIds={-1,-1},arrived={-1,-1},released={-1,-1};
        private bool suspended,returnCenter;
        private string stage="Start",feedback="Prepare";
        private double opening1,opening2;
        private TrainingSetResult result;
        private double D=>request.DifficultyMicro/1000000d;
        private double Window=>parameters.Number("window_base")-parameters.Number("window_reduction")*D;
        private int CueMilliseconds=>parameters.Milliseconds("cue_ms");
        private int CueTotalMilliseconds=>checked(CueMilliseconds*directions.Length);
        public ExerciseInputSession(TrainingSetRequest request,uint seed)
        {
            this.request=request.Clone();this.seed=seed;random=seed;parameters=TrainingContent.Kernel(ExerciseCatalog.Get(request.ExerciseId).KernelId);
            failureLimit=TrainingContent.Current.FailureLimit;prepare=parameters.PreparationMilliseconds;
            directions=new int[request.ExerciseId=="squat"?checked((int)parameters.Number("sequence_length")):0];Generate();
        }
        public string KernelVersion=>TrainingInputFactory.Version;
        public TrainingSetResult Result=>result?.Clone();
        public TrainingInputView View=>new TrainingInputView { ExerciseId=request.ExerciseId,Stage=stage,Feedback=feedback,AttemptMilliseconds=elapsed,EffectiveReps=valid,FailedAttempts=failed,TargetReps=request.TargetReps,Step=step,VisibleDirection=request.ExerciseId=="squat"&&prepare==0&&elapsed<CueTotalMilliseconds?directions[elapsed/CueMilliseconds]:-1,SignalGreen=request.ExerciseId=="triceps_pushdown"&&elapsed>=signal&&stage=="Held",TargetSeconds=request.ExerciseId=="hip_thrust"?target/1000d:0,IsProgressVisible=request.ExerciseId=="hip_thrust"&&stage=="Held"&&elapsed-start<=parameters.Milliseconds("visible_progress_ms"),FirstOpening=opening1,SecondOpening=opening2,Window=Window,IsPreparing=prepare>0||resumePrepare>0,IsSuspended=suspended,IsComplete=result!=null,HeldInputs=Held() };
        private TrainingInput[] Held()=>buttons.OrderBy(x=>x).Select(x=>TrainingInput.ButtonEvent(x,true)).Concat(pointers.OrderBy(x=>x.Key).Select(x=>TrainingInput.Pointer(TrainingInputKind.PointerDown,x.Key,x.Value.X,x.Value.Y))).ToArray();
        private uint Next() { random=unchecked(random*1664525u+1013904223u);return random; }
        private void Generate()
        {
            switch(request.ExerciseId)
            {
                case "dumbbell_curl":stage="Raise";break;
                case "lat_pulldown":stage="Trace";break;
                case "triceps_pushdown":signal=parameters.Milliseconds("signal_min_ms")+(int)(Next()%(uint)(parameters.Milliseconds("signal_max_ms")-parameters.Milliseconds("signal_min_ms")+1));stage="Start";break;
                case "squat":directions[0]=(int)(Next()%3);for(int i=1;i<directions.Length;i++)directions[i]=(directions[i-1]+1+(int)(Next()%2))%3;stage="Watch";break;
                case "hip_thrust":target=checked((int)parameters.Choices[Next()%(uint)parameters.Choices.Length]);stage="Start";break;
                case "shoulder_press":opening1=parameters.Choices[Next()%(uint)parameters.Choices.Length];opening2=parameters.Choices[Next()%(uint)parameters.Choices.Length];stage="Outward";break;
                case "lateral_raise":stage="Synchronize";break;
                default:throw new ArgumentException("Unsupported input kernel");
            }
        }
        public void Suspend() { if(result==null)suspended=true; }
        public void Resume(TrainingInput[] rebuiltHeldInputs)
        {
            if(!suspended)throw new InvalidOperationException("Not suspended");
            var expected=Held();
            if(rebuiltHeldInputs==null||expected.Length!=rebuiltHeldInputs.Length)throw new ArgumentException("Rebuild logical contacts");
            var unmatched=new List<TrainingInput>(rebuiltHeldInputs);
            foreach(var e in expected) { int i=unmatched.FindIndex(x=>x.Kind==e.Kind&&(e.Kind==TrainingInputKind.ButtonDown?x.Button==e.Button:x.PointerId==e.PointerId&&x.X==e.X&&x.Y==e.Y));if(i<0)throw new ArgumentException("Rebuild logical contacts");unmatched.RemoveAt(i); }
            suspended=false;resumePrepare=parameters.ResumeMilliseconds;
        }
        private void Hash(long n) { unchecked { digest^=(ulong)n;digest*=1099511628211UL; } }
        public void Submit(TrainingInput input)
        {
            if(suspended||result!=null)throw new InvalidOperationException("Input unavailable");
            if(!Enum.IsDefined(typeof(TrainingInputKind),input.Kind)||!Enum.IsDefined(typeof(TrainingButton),input.Button)||double.IsNaN(input.X)||double.IsNaN(input.Y)||double.IsInfinity(input.X)||double.IsInfinity(input.Y)||input.X<0||input.X>1||input.Y<0||input.Y>1)throw new ArgumentException("Invalid logical input");
            if(input.Kind>=TrainingInputKind.PointerDown&&input.PointerId<0)throw new ArgumentException("Negative pointer identity");
            // Preparation is an input mask; no free completion by releasing while rebuilding.
            if(resumePrepare>0) { suspended=true;resumePrepare=0;return; }
            if(prepare>0)return;
            events++;Hash(ticks);Hash((int)input.Kind);Hash((int)input.Button);Hash(input.PointerId);Hash(BitConverter.DoubleToInt64Bits(input.X));Hash(BitConverter.DoubleToInt64Bits(input.Y));
            if(input.Kind==TrainingInputKind.ButtonDown||input.Kind==TrainingInputKind.ButtonUp)Button(input);
            else Pointer(input);
        }
        private void Button(TrainingInput input)
        {
            bool down=input.Kind==TrainingInputKind.ButtonDown;
            if(down) { if(!buttons.Add(input.Button)) { Finish(false,"RepeatedButton");return; } }
            else if(!buttons.Remove(input.Button)) { Finish(false,"UnheldButton");return; }
            switch(request.ExerciseId)
            {
                case "dumbbell_curl":
                    if(!down)return;
                    if(input.Button!=(step==0?TrainingButton.Raise:TrainingButton.Lower)) { Finish(false,"WrongDirection");return; }
                    if(Math.Abs(elapsed-(step==0?parameters.Milliseconds("raise_ms"):parameters.Milliseconds("lower_ms")))>Window*1000+1e-9) { Finish(false,"TimingWindow");return; }
                    if(++step==2)Finish(true,"ValidRepetition");else stage="Lower";
                    break;
                case "triceps_pushdown":
                    if(input.Button!=TrainingButton.Press) { Finish(false,"WrongButton");return; }
                    if(down) { if(stage!="Start"||elapsed>signal)Finish(false,"UnexpectedTouch");else stage="Held"; }
                    else if(stage!="Held"||elapsed<signal||elapsed>signal+Window*1000+1e-9)Finish(false,"ReleaseWindow");
                    else { stage="Return";start=elapsed; }
                    break;
                case "hip_thrust":
                    if(input.Button!=TrainingButton.Press) { Finish(false,"WrongButton");return; }
                    if(down) { if(stage!="Start")Finish(false,"UnexpectedTouch");else { stage="Held";start=elapsed; } }
                    else if(stage!="Held"||Math.Abs(elapsed-start-target)>Window*1000+1e-9)Finish(false,"ReleaseWindow");
                    else { stage="Hold";start=elapsed; }
                    break;
                default:Finish(false,"UnexpectedButton");break;
            }
        }
        private static double Distance(double x,double y,double a,double b)=>Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));
        private static double SegmentDistance(double x,double y,double ax,double ay,double bx,double by)
        { double dx=bx-ax,dy=by-ay,t=((x-ax)*dx+(y-ay)*dy)/(dx*dx+dy*dy);t=Math.Max(0,Math.Min(1,t));return Distance(x,y,ax+t*dx,ay+t*dy); }
        private double[] pathX=>parameters.Points.Select(p=>p.X).ToArray();
        private double[] pathY=>parameters.Points.Select(p=>p.Y).ToArray();
        private void Pointer(TrainingInput input)
        {
            bool down=input.Kind==TrainingInputKind.PointerDown,up=input.Kind==TrainingInputKind.PointerUp;
            if(input.PointerId<0)throw new ArgumentException("Negative pointer identity");
            if(down)
            {
                if(pointers.ContainsKey(input.PointerId)||pointers.Count>=(request.ExerciseId=="lateral_raise"?2:1)) { Finish(false,"ContactIdentity");return; }
                pointers.Add(input.PointerId,input);
            }
            else if(!pointers.ContainsKey(input.PointerId)) { Finish(false,"UnknownContact");return; }
            var old=pointers[input.PointerId];
            switch(request.ExerciseId)
            {
                case "lat_pulldown":
                    if(up) { Finish(false,"EarlyRelease");return; }
                    if(down) { if(Distance(input.X,input.Y,parameters.Points[0].X,parameters.Points[0].Y)>Window)Finish(false,"WrongStart");break; }
                    for(int checkpoint=0;checkpoint<parameters.Points.Length-1;checkpoint++)
                    {
                        if(checkpoint==step||checkpoint==step+1||(step==parameters.Points.Length-2&&checkpoint==0))continue;
                        if(Distance(input.X,input.Y,pathX[checkpoint],pathY[checkpoint])<=Window+1e-9) { Finish(false,"CheckpointOrder");return; }
                    }
                    // A linear sample segment must remain in the current convex segment capsule.
                    // Reaching a checkpoint changes the segment only after this sample is checked.
                    double a=SegmentDistance(old.X,old.Y,pathX[step],pathY[step],pathX[step+1],pathY[step+1]);
                    double b=SegmentDistance(input.X,input.Y,pathX[step],pathY[step],pathX[step+1],pathY[step+1]);
                    if(a>Window+1e-9||b>Window+1e-9) { outside=Math.Max(1,outside); }
                    else outside=0;
                    if(Distance(input.X,input.Y,pathX[step+1],pathY[step+1])<=Window+1e-9)
                    { if(a>Window+1e-9||b>Window+1e-9) { Finish(false,"SkippedPath");return; } if(++step==parameters.Points.Length-1) { Finish(true,"ValidRepetition");return; } }
                    break;
                case "shoulder_press":
                    if(up) { Finish(false,"EarlyRelease");return; }
                    if(down) { if(Distance(input.X,input.Y,parameters.Points[0].X,parameters.Points[0].Y)>parameters.Number("target_radius"))Finish(false,"WrongStart");break; }
                    if(CrossesObstacle(old,input,parameters.Number("first_barrier_y"),opening1)||CrossesObstacle(old,input,parameters.Number("second_barrier_y"),opening2)) { Finish(false,"ObstacleCollision");return; }
                    if(step==0&&old.Y>=parameters.Number("first_barrier_y")&&input.Y<parameters.Number("first_barrier_y"))step=1;
                    if(step==1&&old.Y>=parameters.Number("second_barrier_y")&&input.Y<parameters.Number("second_barrier_y"))step=2;
                    if(step==2&&Distance(input.X,input.Y,parameters.Points[1].X,parameters.Points[1].Y)<=parameters.Number("target_radius")) { step=3;stage="Return"; }
                    if(step==3&&old.Y<=parameters.Number("second_barrier_y")&&input.Y>parameters.Number("second_barrier_y"))step=4;
                    if(step==4&&old.Y<=parameters.Number("first_barrier_y")&&input.Y>parameters.Number("first_barrier_y"))step=5;
                    if(step==5&&Distance(input.X,input.Y,parameters.Points[0].X,parameters.Points[0].Y)<=parameters.Number("target_radius")) { Finish(true,"ValidRepetition");return; }
                    break;
                case "squat":
                    if(elapsed<CueTotalMilliseconds) { Finish(false,"WatchFirst");return; }
                    if(up) { Finish(false,"EarlyRelease");return; }
                    bool center=Distance(input.X,input.Y,parameters.Points[0].X,parameters.Points[0].Y)<=parameters.Number("center_radius");
                    if(down) { if(!center)Finish(false,"WrongStart");break; }
                    if(center) { returnCenter=false;break; }
                    int direction=input.Y<=parameters.Number("target_max_y")?(input.X<parameters.Number("left_boundary")?0:input.X>parameters.Number("right_boundary")?2:1):-1;
                    if(direction>=0)
                    {
                        if(returnCenter) { Finish(false,"ReturnToCenter");return; }
                        if(direction!=directions[step]) { Finish(false,"WrongDirection");return; }
                        if(++step==directions.Length) { Finish(true,"ValidRepetition");return; }
                        start=elapsed;returnCenter=true;
                    }
                    break;
                case "lateral_raise":
                    int side=Array.IndexOf(sideIds,input.PointerId);
                    if(down)
                    {
                        side=input.X<parameters.Number("half_boundary")?0:1;
                        if(sideIds[side]>=0||Distance(input.X,input.Y,parameters.Points[side].X,parameters.Points[side].Y)>parameters.Number("target_radius")) { Finish(false,"WrongStart");return; }
                        sideIds[side]=input.PointerId;
                    }
                    if(side<0||(side==0?input.X>parameters.Number("half_boundary"):input.X<parameters.Number("half_boundary"))) { Finish(false,"CrossedHalf");return; }
                    bool at=Distance(input.X,input.Y,parameters.Points[side+2].X,parameters.Points[side+2].Y)<=parameters.Number("target_radius")+1e-9;
                    if(up)
                    {
                        if(holdStart<0||elapsed-holdStart<parameters.Milliseconds("hold_ms")||!at) { Finish(false,"EarlyRelease");return; }
                        released[side]=elapsed;
                        if(released[1-side]>=0) { Finish(Math.Abs(released[0]-released[1])<=Window*1000+1e-9,"ReleaseSynchronization");return; }
                    }
                    else
                    {
                        if(at&&arrived[side]<0)arrived[side]=elapsed;
                        if(arrived[0]>=0&&arrived[1]>=0&&Math.Abs(arrived[0]-arrived[1])>Window*1000+1e-9) { Finish(false,"ArrivalSynchronization");return; }
                        var other=pointers.Values.FirstOrDefault(p=>p.PointerId==sideIds[1-side]);
                        bool otherAt=sideIds[1-side]>=0&&Distance(other.X,other.Y,parameters.Points[3-side].X,parameters.Points[3-side].Y)<=parameters.Number("target_radius")+1e-9;
                        if(at&&otherAt) { if(holdStart<0)holdStart=elapsed; } else holdStart=-1;
                    }
                    break;
                default:Finish(false,"UnexpectedTouch");return;
            }
            // Finish may have reset contacts. Never resurrect a finished attempt's contact.
            if(pointers.ContainsKey(input.PointerId)) { if(up)pointers.Remove(input.PointerId);else pointers[input.PointerId]=input; }
        }
        private bool CrossesObstacle(TrainingInput a,TrainingInput b,double y,double opening)
        {
            // Square block half-width/height .03: intersect the entire obstacle band,
            // and check both ends of the linear sample in that band.
            double half=parameters.Number("block_width")/2;double dy=b.Y-a.Y,lo=0,hi=1;
            if(Math.Abs(dy)<1e-15) { if(Math.Abs(a.Y-y)>half)return false; }
            else { double t1=(y-half-a.Y)/dy,t2=(y+half-a.Y)/dy;lo=Math.Max(0,Math.Min(t1,t2));hi=Math.Min(1,Math.Max(t1,t2));if(lo>hi)return false; }
            double min=opening-Window/2+half,max=opening+Window/2-half;
            double x1=a.X+(b.X-a.X)*lo,x2=a.X+(b.X-a.X)*hi;
            return x1<min-1e-9||x1>max+1e-9||x2<min-1e-9||x2>max+1e-9;
        }
        public void Advance(int milliseconds)
        {
            if(milliseconds<0)throw new ArgumentOutOfRangeException(nameof(milliseconds));
            if(suspended||result!=null)return;
            for(int i=0;i<milliseconds&&result==null;i++)
            {
                if(resumePrepare>0) { resumePrepare--;continue; }
                ticks++;
                if(prepare>0) { prepare--;continue; }
                elapsed++;
                switch(request.ExerciseId)
                {
                    case "dumbbell_curl":if(elapsed>(step==0?parameters.Milliseconds("raise_ms"):parameters.Milliseconds("lower_ms"))+Window*1000+1e-9)Finish(false,"MissedBeat");break;
                    case "lat_pulldown":
                        if(outside>0&&++outside>parameters.Milliseconds("outside_ms")+1)Finish(false,"OutsideChannel");else if(elapsed>parameters.Milliseconds("timeout_ms"))Finish(false,"Timeout");break;
                    case "triceps_pushdown":
                        if(stage=="Return"&&elapsed-start>=parameters.Milliseconds("return_ms"))Finish(true,"ValidRepetition");
                        else if(stage!="Return"&&elapsed>signal+Window*1000+1e-9)Finish(false,"ReleaseWindow");break;
                    case "squat":
                        if(elapsed==CueTotalMilliseconds) { stage="Respond";start=elapsed; }
                        if(elapsed>=CueTotalMilliseconds&&elapsed-start>Window*1000+1e-9)Finish(false,"ResponseTimeout");break;
                    case "hip_thrust":
                        if(stage=="Hold"&&elapsed-start>=parameters.Milliseconds("hold_ms"))Finish(true,"ValidRepetition");
                        else if(stage=="Held"&&elapsed-start>target+Window*1000+1e-9)Finish(false,"ReleaseWindow");break;
                    case "shoulder_press":if(elapsed>parameters.Milliseconds("timeout_ms"))Finish(false,"Timeout");break;
                    case "lateral_raise":
                        if(elapsed>parameters.Milliseconds("timeout_ms"))Finish(false,"Timeout");
                        else for(int side=0;side<2;side++)
                        { if((arrived[side]>=0&&arrived[1-side]<0&&elapsed-arrived[side]>Window*1000+1e-9)||(released[side]>=0&&released[1-side]<0&&elapsed-released[side]>Window*1000+1e-9)) { Finish(false,"SynchronizationTimeout");break; } }
                        break;
                }
            }
        }
        private void Finish(bool success,string reason)
        {
            if(success)valid++;else failed++;
            feedback=success?"ValidRepetition":reason;
            if(valid==request.TargetReps||failed==failureLimit)
            {
                result=new TrainingSetResult { SetId=request.SetId,RulesVersion=request.RulesVersion,Producer=request.Producer,EffectiveReps=valid,FailedAttempts=failed,Success=valid==request.TargetReps,TerminalReason=valid==request.TargetReps?"TargetReached":reason,InputDigest=digest.ToString("x16"),InputEventCount=events,MinigameSeed=seed };
                result.Validate(request);return;
            }
            elapsed=step=start=outside=0;holdStart=-1;returnCenter=false;prepare=parameters.PreparationMilliseconds;
            pointers.Clear();buttons.Clear();for(int i=0;i<2;i++)sideIds[i]=arrived[i]=released[i]=-1;
            Generate();
        }
    }
}
