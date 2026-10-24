using System.Linq;
using NUnit.Framework;
using FitnessSimulator.Game.Actors;
using FitnessSimulator.Game.Life.Relationships;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Tests.Life
{
    public sealed class ActorPolicyTests
    {
        [Test] public void FourActorsHaveIndependentResourcesAndNoAutomaticQualification()
        {
            var actors=ActorPolicies.InitializeFour("r");
            Assert.That(actors.Select(a=>a.Life.Wallet.Balance).ToArray(),Is.EqualTo(new long[]{480,450,540,600}));
            actors[0].Body.Exercises[0].KnowledgeMicro=99000000;
            Assert.That(actors[1].Body.Exercises[0].KnowledgeMicro,Is.Zero);
            Assert.That(actors.SelectMany(a=>a.Shops).Select(s=>s.ActorId).Distinct().Count(),Is.EqualTo(4));
            Assert.That(actors.All(a=>a.Inventory.Items.All(i=>i.Quantity==0)),Is.True);
        }
        [Test] public void DecisionsUseOnlyLegalCandidatesAndStopAfterTwoExtraRests()
        {
            var legal=new[]{new ActorEventOption { EventId="ordinary",Kind=ActorEventKind.Ordinary },new ActorEventOption { EventId="training",Kind=ActorEventKind.Training },new ActorEventOption { EventId="rest",Kind=ActorEventKind.Rest }};
            Assert.That(ActorPolicies.ChooseEvent(RelationshipRules.Rival,legal,75000000,false,0).EventId,Is.EqualTo("rest"));
            Assert.That(ActorPolicies.ChooseEvent(RelationshipRules.Rival,legal,0,false,0).EventId,Is.EqualTo("training"));
            Assert.That(ActorPolicies.ChooseSet(RelationshipRules.Beginner,0,60000000,100000000,false,true,false,true,false,2),Is.EqualTo(ActorSetDecision.EndWorkout));
            Assert.Throws<System.ArgumentException>(()=>ActorPolicies.ChooseEvent(RelationshipRules.Coach,new ActorEventOption[0],0,false,0));
        }
        [TestCase("bench_press")]
        [TestCase("dumbbell_curl")]
        [TestCase("lat_pulldown")]
        [TestCase("triceps_pushdown")]
        [TestCase("squat")]
        [TestCase("hip_thrust")]
        [TestCase("shoulder_press")]
        [TestCase("lateral_raise")]
        public void RealNpcTraceReplaysThroughSameKernel(string exercise)
        {
            var request=new TrainingSetRequest { ActorId=RelationshipRules.Coach,SetId="set",ExerciseId=exercise,TargetReps=1,Tier=1,Producer=TrainingProducer.NpcPolicy };
            var trace=ActorInputPolicy.Execute(RelationshipRules.Coach,request,7,Enumerable.Repeat(2147483648u,20000).ToArray());
            var replay=TrainingInputFactory.Begin(request,7);
            foreach(var step in trace.Steps) { if(step.HasInput)replay.Submit(step.Input);else replay.Advance(step.AdvanceMilliseconds); }
            Assert.That(replay.Result.InputDigest,Is.EqualTo(trace.Result.InputDigest));
            Assert.That(replay.Result.EffectiveReps,Is.EqualTo(trace.Result.EffectiveReps));
            Assert.That(replay.Result.TerminalReason,Is.EqualTo(trace.Result.TerminalReason));
            Assert.That(trace.SamplesConsumed,Is.GreaterThan(0));Assert.That(trace.Steps.Any(s=>s.HasInput),Is.True);
            trace.Result.Validate(request);
        }
    }
}
