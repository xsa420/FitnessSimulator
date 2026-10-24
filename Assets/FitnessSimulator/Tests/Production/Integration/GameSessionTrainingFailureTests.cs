using System;
using System.IO;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Persistence;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Integration
{
    // G01: production input/result/settle path. Only challenge pre-set S/K/X is a named fixture.
    // No result, quota, progress, flow cursor, day or completion count is injected.
    public sealed class GameSessionTrainingFailureTests
    {
        private GameSession session;
        private string directory;
        private string saveDirectory;
        private StreamWriter trace;
        private static readonly string[] Targets={"chest_upper","chest_middle","chest_lower","shoulder_front","triceps_long","triceps_lateral","triceps_medial"};
        private static readonly int[] Weights={20,35,15,10,8,7,5};
        private T State<T>(string owner)=>JsonUtility.FromJson<T>(session.InspectSnapshot().FindFragment(owner,"player").Json);
        private bool Has(string id)=>session.ReadView().Actions.Any(a=>a.Enabled&&a.Id==id);
        private FrontendAction Send(string id,string value=null,int number=0)
        {
            var view=session.ReadView();
            var action=new FrontendAction { ActionId=id,StringValue=value,IntValue=number,OperationId=Guid.NewGuid().ToString("N"),RunId=view.RunId,ExpectedRevision=view.Revision };
            trace.WriteLine(JsonUtility.ToJson(action));var reply=session.Execute(action);
            Assert.That(reply.Accepted,Is.True,id+": "+reply.Message);return action;
        }
        private void Advance(double seconds) { trace.WriteLine("advanceSeconds="+seconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture));session.Advance(seconds); }
        private void Restart()
        {
            session.Dispose();session=GameSession.OpenAutomation(saveDirectory,999);
            Assert.That(session.InspectSnapshot(),Is.Not.Null,"Restart failed: "+session.Diagnostic);
            Assert.That(session.ReadView().ScreenId,Is.Not.EqualTo("error"),session.ReadView().Body);Send("continue-game");
        }
        private void ReachPreSet()
        {
            for(int i=0;i<30000&&!Has("begin-set");i++)
            {
                var view=session.ReadView();Assert.That(view.ScreenId,Is.Not.EqualTo("error"),view.Body);
                if(Has("meal"))Send("meal","skip");
                else if(Has("continue"))Send("continue");
                else if(Has("choose-entry"))Send("choose-entry","training-choice");
                else Advance(0);
            }
            Assert.That(Has("begin-set"),Is.True,"Bounded route failed before first set");
        }
        private void ChallengePreSetFixture()
        {
            // Isolate the eligibility/old-K boundary without claiming natural tier-2 reachability.
            session.Dispose();session=null;
            using(var store=new LocalCheckpointStore(saveDirectory,new UnityJsonStateCodec()))
            {
                var loaded=store.Load();Assert.That(loaded.Status,Is.EqualTo(LoadStatus.Loaded));var root=loaded.Root;
                var training=JsonUtility.FromJson<TrainingDayState>(root.FindFragment("training","player").Json);
                Assert.That(training.Workout.Phase,Is.EqualTo(WorkoutPhase.PreSet));
                var body=JsonUtility.FromJson<BodyState>(root.FindFragment("body","player").Json);
                File.WriteAllText(Path.Combine(directory,"fixture-before-body.json"),JsonUtility.ToJson(body,true));
                foreach(string id in Targets)body.Region(id).StrengthMicro=14000000;
                body.Exercise("bench_press").KnowledgeMicro=29000000;body.Exercise("bench_press").ProficiencyMicro=99000000;
                body.Revision=checked(body.Revision+1);body.Validate();
                root.SetFragment("body","player",1,root.Revision+1,JsonUtility.ToJson(body));
                string idempotency=Guid.NewGuid().ToString("N");
                var commit=store.Commit(root.Generation,root,new OperationReceipt { RunId=root.RunId,OperationId=idempotency,CommitId=idempotency,SemanticKey="g01/pre-set-body/"+idempotency,PayloadHash="explicit-S14-K29-X99-boundary-fixture",Revision=root.Revision+1 },Array.Empty<LogAppend>());
                Assert.That(commit.Status,Is.EqualTo(CommitStatus.Committed));
                File.WriteAllText(Path.Combine(directory,"fixture-after-body.json"),JsonUtility.ToJson(body,true));
            }
            session=GameSession.OpenAutomation(saveDirectory,1);Assert.That(session.InspectSnapshot(),Is.Not.Null,"Fixture reopen failed: "+session.Diagnostic);Send("continue-game");
        }
        private void DriveRealBenchFailure(int desiredEffective)
        {
            // Decisions read only the public balance and effective-count view.
            for(int i=0;i<10000&&session.ReadView().TrainingInput!=null;i++)
            {
                var v=session.ReadView().TrainingInput;Assert.That(v.ExerciseId,Is.EqualTo("bench_press"));
                bool good=v.EffectiveReps<desiredEffective;
                bool left=good&&v.Balance>.02,right=!good||v.Balance<-.02;
                foreach(var button in new[]{"Left","Right"})
                {
                    bool wanted=button=="Left"?left:right;
                    bool held=v.HeldInputs.Any(h=>h.Kind=="ButtonDown"&&h.Button==button);
                    if(wanted!=held)Send("training-input",JsonUtility.ToJson(new FrontendTrainingInput { Kind=wanted?"ButtonDown":"ButtonUp",Button=button }));
                }
                Advance(.01);
            }
            Assert.That(session.ReadView().TrainingInput,Is.Null,"Bounded real input did not terminate");
            for(int i=0;i<30000&&!Has("settle");i++)Advance(0);
            Assert.That(Has("settle"),Is.True);
        }
        private static long Micro(Rational value)=>NumericKernel.ToMicro(Rational.Clamp(value,0,100));
        private static Rational Units(long value)=>new Rational(value,1000000);
        private static void AssertExactEffects(BodyState before,BodyState after,bool challenge,int v)
        {
            Rational ratio=new Rational(v,6),pool=(challenge?new Rational(12,5):new Rational(8,5))*ratio;
            var b=Units(before.StressMicro);Rational bRetention=b<25?(Rational)1:b<50?new Rational(9,10):b<75?new Rational(3,4):new Rational(3,5);
            foreach(var old in before.Regions)
            {
                var actual=after.Region(old.RegionId);int index=Array.IndexOf(Targets,old.RegionId);
                if(index<0) { Assert.That(JsonUtility.ToJson(actual),Is.EqualTo(JsonUtility.ToJson(old)),"unrelated "+old.RegionId);continue; }
                var s=Units(old.StrengthMicro);var f=Units(old.LocalFatigueMicro);
                var fRetention=f<=20?(Rational)1:1-new Rational(7,10)*(f-20)/80;
                long strength=Micro(s+pool*new Rational(Weights[index],100)*(1-new Rational(8,1000)*s)*fRetention*bRetention);
                Assert.That(actual.StrengthMicro,Is.EqualTo(strength),"S "+old.RegionId);
                // Single-action low plan has 2*6 reps; each target's frozen per-set q is exactly 40.
                Rational temporary=old.ExactTemporary+(challenge?60+72*ratio:40*ratio);
                Assert.That(actual.ExactTemporary.CompareTo(temporary),Is.Zero,"exact T/residual "+old.RegionId);
                Assert.That(actual.IsInjured,Is.False,"All four selected cases stay below T100");
            }
            var oldExercise=before.Exercise("bench_press");var actualExercise=after.Exercise("bench_press");
            var k=Units(oldExercise.KnowledgeMicro);var x=Units(oldExercise.ProficiencyMicro);
            int cap=k<30?100:k<60?300:k<90?600:1000;
            var room=(Rational)cap-x;if(room<0)room=0;
            var gain=(challenge?10:8)*ratio*(1+k/100);if(gain>room)gain=room;
            Assert.That(actualExercise.ProficiencyMicro,Is.EqualTo(NumericKernel.ToMicro(x+gain)),"X reads old K cap");
            Assert.That(actualExercise.KnowledgeMicro,Is.EqualTo(Micro(k+(challenge&&v>0?1:0))),"failure K only when v>0");
        }
        [TestCase(false,0)][TestCase(false,2)][TestCase(true,0)][TestCase(true,2)]
        [Timeout(300000)]
        public void RealFailureSurvivesReadyRestartSettlesExactEffectsAndRetries(bool challenge,int effective)
        {
            directory=Path.Combine(Path.GetTempPath(),"Fitness-G01-Training",(challenge?"challenge":"volume")+"-v"+effective,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            saveDirectory=Path.Combine(directory,"save");
            trace=new StreamWriter(Path.Combine(directory,"trace.jsonl"));
            try
            {
                session=GameSession.OpenAutomation(saveDirectory,1);Assert.That(session.InspectSnapshot(),Is.Not.Null,"Initial open failed: "+session.Diagnostic);Send("new-game",TemplateCatalog.DefaultId);
                var slot=PlanCatalog.Training(0,TrainingCombination.Free,"bench_press");slot.Volume=TrainingVolume.Low;
                Send("save-plan",JsonUtility.ToJson(new PlanDraft { Mode=PlanMode.DayCycle,Slots=new[]{slot} }));ReachPreSet();
                if(challenge)ChallengePreSetFixture();
                var initial=State<TrainingDayState>("training");
                Assert.That(initial.Workout.Quota.Entries.Length,Is.EqualTo(1));Assert.That(initial.Workout.Quota.Entries[0].FrozenTier,Is.EqualTo(1));
                Assert.That(initial.Workout.Quota.Entries[0].Plan.Sets,Is.EqualTo(2));Assert.That(initial.Workout.Quota.Entries[0].Plan.Reps,Is.EqualTo(6));
                Assert.That(initial.Progress.Exercise("bench_press").AutomaticTiers.All(x=>!x),Is.True);
                Send("begin-set","manual",challenge?1:0);var started=State<TrainingDayState>("training").Workout;
                Assert.That(started.PendingRequest.SetType,Is.EqualTo(challenge?TrainingSetType.StrengthChallenge:TrainingSetType.Volume));
                foreach(string id in Targets)Assert.That(started.PreSetBody.Region(id).ExactTemporary.CompareTo((Rational)0),Is.Zero);
                DriveRealBenchFailure(effective);
                var ready=State<TrainingDayState>("training").Workout;
                Assert.That(ready.Phase,Is.EqualTo(WorkoutPhase.ResultReady));Assert.That(ready.PendingResult.EffectiveReps,Is.EqualTo(effective));
                Assert.That(ready.PendingResult.Success,Is.False);Assert.That(ready.PendingResult.FailedAttempts,Is.EqualTo(3));
                Assert.That(ready.PendingResult.Producer,Is.EqualTo(TrainingProducer.TrustedAutomationInput));Assert.That(ready.PendingResult.InputEventCount,Is.GreaterThan(0));
                string resultJson=JsonUtility.ToJson(ready.PendingResult);string preBodyJson=JsonUtility.ToJson(State<BodyState>("body"));
                Restart();Assert.That(Has("settle"),Is.True);Assert.That(session.ReadView().TrainingInput,Is.Null);
                Assert.That(JsonUtility.ToJson(State<TrainingDayState>("training").Workout.PendingResult),Is.EqualTo(resultJson));
                Assert.That(JsonUtility.ToJson(State<BodyState>("body")),Is.EqualTo(preBodyJson),"Ready restart never awards growth");
                var beforeTime=State<SessionState>("session");var command=Send("settle");
                var afterBody=State<BodyState>("body");var afterTraining=State<TrainingDayState>("training");var afterTime=State<SessionState>("session");
                AssertExactEffects(ready.PreSetBody,afterBody,challenge,effective);
                Assert.That(afterTime.AnchorGameSeconds-beforeTime.AnchorGameSeconds,Is.EqualTo(effective*6));
                Assert.That(afterTime.TotalGameSeconds-beforeTime.TotalGameSeconds,Is.EqualTo(effective*6));
                Assert.That(afterTraining.Workout.ElapsedGameSeconds-ready.ElapsedGameSeconds,Is.EqualTo(effective*6));
                Assert.That(afterTraining.Progress.Exercise("bench_press").HighestUnlockedTier,Is.EqualTo(1));
                Assert.That(afterTraining.Progress.Exercise("bench_press").AutomaticTiers.All(x=>!x),Is.True,"failure never grants auto");
                Assert.That(afterTraining.Workout.SettledSetIds.Count(x=>x==ready.PendingRequest.SetId),Is.EqualTo(1));
                string committed=JsonUtility.ToJson(session.InspectSnapshot());Assert.That(session.Execute(command).Accepted,Is.True);
                Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(committed),"same-operation retry changes nothing");
                Restart();string reopened=JsonUtility.ToJson(session.InspectSnapshot());Assert.That(session.Execute(command).Accepted,Is.True);
                Assert.That(JsonUtility.ToJson(session.InspectSnapshot()),Is.EqualTo(reopened),"restart retry changes nothing");
                File.WriteAllText(Path.Combine(directory,"locked-result.json"),resultJson);File.WriteAllText(Path.Combine(directory,"body-before.json"),JsonUtility.ToJson(ready.PreSetBody,true));
                File.WriteAllText(Path.Combine(directory,"body-after.json"),JsonUtility.ToJson(afterBody,true));
                File.WriteAllText(Path.Combine(directory,"report.txt"),"scope=G01 application boundary\nchallenge="+challenge+"\neffective="+effective+"\nfixture="+(challenge?"pre-set target S14 K29 X99 only":"none")+"\nresultSource=real public bench input\nexit=Passed");
            }
            finally { if(session!=null) { File.WriteAllText(Path.Combine(directory,"final.json"),JsonUtility.ToJson(session.InspectSnapshot(),true));session.Dispose(); }trace.Dispose();TestContext.WriteLine(directory); }
        }
    }
}
