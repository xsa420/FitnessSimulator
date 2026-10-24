#if TRAINING_INPUT_STANDALONE
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Training
{
    // Evidence recorder belongs only to the standalone checks, never the production driver.
    internal static class TrainingInputTraceReport
    {
        private sealed class Recorder : ITrainingInputSession
        {
            private readonly ITrainingInputSession inner;
            public readonly List<TraceEvent> Events=new List<TraceEvent>();
            public Recorder(ITrainingInputSession inner) { this.inner=inner; }
            public string KernelVersion=>inner.KernelVersion;
            public TrainingInputView View=>inner.View;
            public TrainingSetResult Result=>inner.Result;
            public void Submit(TrainingInput input) { Events.Add(new TraceEvent { Operation="input",Input=input });inner.Submit(input); }
            public void Advance(int milliseconds) { Events.Add(new TraceEvent { Operation="advance",Milliseconds=milliseconds });inner.Advance(milliseconds); }
            public void Suspend() { Events.Add(new TraceEvent { Operation="suspend" });inner.Suspend(); }
            public void Resume(TrainingInput[] inputs) { Events.Add(new TraceEvent { Operation="resume",HeldInputs=inputs });inner.Resume(inputs); }
        }
        private sealed class TraceEvent
        {
            public string Operation;
            public int Milliseconds;
            public TrainingInput Input;
            public TrainingInput[] HeldInputs;
        }
        public static void Write(string destination)
        {
            destination=Path.GetFullPath(destination);
            if(Directory.Exists(destination))throw new InvalidOperationException("Report destination must be new to preserve previous evidence");
            Directory.CreateDirectory(destination);
            var rows=new List<object>();var options=new JsonSerializerOptions { IncludeFields=true };
            foreach(var exercise in ExerciseCatalog.All)foreach(int wanted in new[]{0,1,2})
            {
                var request=new TrainingSetRequest { SetId="trace/"+exercise.Id+"/"+wanted,ActorId="isolated-kernel-fixture",ExerciseId=exercise.Id,Tier=1,TargetReps=2,DifficultyMicro=420000,Producer=TrainingProducer.TrustedAutomationInput };
                const uint seed=17;
                var recorder=new Recorder(TrainingInputFactory.Begin(request,seed));
                for(int attempt=0;attempt<5&&!recorder.View.IsComplete;attempt++)TrainingInputChecks.OneAttempt(recorder,recorder.View.EffectiveReps<wanted);
                var result=recorder.Result;
                if(result==null||result.EffectiveReps!=wanted)throw new Exception("Trace did not produce expected result");
                string filename=exercise.Id+"-v"+wanted+".jsonl";
                using(var writer=new StreamWriter(Path.Combine(destination,filename)))foreach(var entry in recorder.Events)writer.WriteLine(JsonSerializer.Serialize(entry,options));
                // Replay from the written artifact, not the in-memory event list.
                var replay=TrainingInputFactory.Begin(request,seed);
                foreach(string line in File.ReadLines(Path.Combine(destination,filename)))
                {
                    var entry=JsonSerializer.Deserialize<TraceEvent>(line,options);
                    switch(entry.Operation)
                    {
                        case "input":replay.Submit(entry.Input);break;
                        case "advance":replay.Advance(entry.Milliseconds);break;
                        case "suspend":replay.Suspend();break;
                        case "resume":replay.Resume(entry.HeldInputs);break;
                        default:throw new Exception("Unknown replay operation");
                    }
                }
                bool identical=JsonSerializer.Serialize(result,options)==JsonSerializer.Serialize(replay.Result,options);
                if(!identical)throw new Exception("Written trace replay differs");
                rows.Add(new { kernelVersion=recorder.KernelVersion,request,seed,result,inputTrace=filename,recordedOperations=recorder.Events.Count,replayIdentical=identical });
            }
            File.WriteAllText(Path.Combine(destination,"report.json"),JsonSerializer.Serialize(new { scope="Isolated real-input kernel fixtures; no production authority, body settlement, NPC policy, save recovery or whole-game claim",source="Standalone tests; producer tag exercises the trusted source without granting application authority",cases=rows },new JsonSerializerOptions { IncludeFields=true,WriteIndented=true }));
            Console.WriteLine("PASS 24 persisted input traces and file replays: "+destination);
        }
    }
}
#endif
