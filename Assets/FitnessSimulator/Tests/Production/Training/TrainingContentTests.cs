using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Training;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Training
{
    public sealed class TrainingContentTests
    {
        private static ContentPackage Variant(TrainingContentDefinition training,BodyContentDefinition body)
        {
            var documents=ContentRuntime.Current.Export().Documents.ToDictionary(d=>d.Id,d=>d.Json);
            documents["training"]=JsonUtility.ToJson(training);documents["body"]=JsonUtility.ToJson(body);
            return ContentPackage.FromDocuments(documents);
        }
        private static BodyState Body()=>BodyState.Create("actor",Enumerable.Repeat(10000000L,21).ToArray(),25000000,50000000,0);
        private static TrainingSettlement RealCurl()
        {
            var body=Body();var progress=TrainingProgress.CreateInitial("actor");
            var quota=DayTrainingQuota.Freeze("actor",1,"plan",new[]{new TrainingPlanEntry { EntryId="curl",ExerciseId="dumbbell_curl",Sets=2,Reps=8 }},progress);
            var playing=TrainingSession.BeginSet(TrainingSession.Begin("content-test",quota),body,progress,TrainingSetType.Volume,TrainingProducer.TrustedAutomationInput);
            var kernel=TrainingInputFactory.Begin(playing.PendingRequest,17);var p=TrainingInputFactory.Describe("dumbbell_curl");
            for(int i=0;i<8;i++)
            {
                kernel.Advance(p.PreparationMilliseconds+p.Milliseconds("raise_ms"));
                kernel.Submit(TrainingInput.ButtonEvent(TrainingButton.Raise,true));kernel.Submit(TrainingInput.ButtonEvent(TrainingButton.Raise,false));
                kernel.Advance(p.Milliseconds("lower_ms")-p.Milliseconds("raise_ms"));kernel.Submit(TrainingInput.ButtonEvent(TrainingButton.Lower,true));
            }
            Assert.That(kernel.Result.Success,Is.True,"real configurable input kernel");
            return TrainingSession.Settle(TrainingSession.CompleteResult(playing,kernel.Result),body,progress,1,new Dictionary<string,uint>());
        }
        [Test] public void InstalledDefinitionsValidateAndCopiesNeverBecomeGlobalCaches()
        {
            BodyContent.ValidateContent();TrainingContent.ValidateContent();int seconds=ExerciseCatalog.Get("dumbbell_curl").SecondsPerRep;
            var training=TrainingContent.Current;var body=BodyContent.Current;training.Exercises.Single(e=>e.Id=="dumbbell_curl").SecondsPerRep=seconds+1;
            Assert.That(ExerciseCatalog.Get("dumbbell_curl").SecondsPerRep,Is.EqualTo(seconds));
            using(ContentRuntime.Push(Variant(training,body)))Assert.That(ExerciseCatalog.Get("dumbbell_curl").SecondsPerRep,Is.EqualTo(seconds+1));
            Assert.That(ExerciseCatalog.Get("dumbbell_curl").SecondsPerRep,Is.EqualTo(seconds));
        }
        [Test] public void JsonOnlyPoolWeightSecondsAndRestChangesReachRealSettlement()
        {
            var baseline=RealCurl();var t=TrainingContent.Current;var b=BodyContent.Current;
            t.TierStrengthPoolMicro[0]*=2;var exercise=t.Exercises.Single(e=>e.Id=="dumbbell_curl");exercise.WeightBasisPoints=new[]{6000,4000};exercise.SecondsPerRep++;
            t.Fatigue.RestRecoveryPerSecondMicro*=2;
            using(ContentRuntime.Push(Variant(t,b)))
            {
                TrainingContent.ValidateContent();var changed=RealCurl();
                Assert.That(changed.Calculation.GameSeconds,Is.EqualTo(baseline.Calculation.GameSeconds+8));
                Assert.That(changed.Body.Region("biceps_long").StrengthMicro,Is.GreaterThan(baseline.Body.Region("biceps_long").StrengthMicro));
                Assert.That(changed.Body.Region("biceps_long").ExactTemporary.CompareTo((Rational)40),Is.Zero,"quota remains 80 over two sets");
                var rested=BodyCalculator.Rest(changed.Body,30);Assert.That(rested.Region("biceps_long").ExactTemporary.CompareTo((Rational)34),Is.Zero,"configured .2 points/sec");
                Assert.That(changed.Workout.Quota.StandardGameSeconds,Is.EqualTo(2*8*exercise.SecondsPerRep+t.Fatigue.StandardRestSeconds));
            }
        }
        [TestCase("bench_press")][TestCase("dumbbell_curl")][TestCase("lat_pulldown")][TestCase("triceps_pushdown")]
        [TestCase("squat")][TestCase("hip_thrust")][TestCase("shoulder_press")][TestCase("lateral_raise")]
        public void EveryKernelConsumesWindowAndExistingSessionFreezesItsParameters(string id)
        {
            var request=new TrainingSetRequest { SetId="window",ActorId="actor",ExerciseId=id,Tier=1,TargetReps=2,DifficultyMicro=420000,Producer=TrainingProducer.TrustedAutomationInput };
            var original=TrainingInputFactory.Begin(request,7);double window=original.View.Window;
            var t=TrainingContent.Current;var b=BodyContent.Current;t.Kernels.Single(k=>k.Id==id).Parameters.Single(p=>p.Key=="window_base").Value+=.01;
            using(ContentRuntime.Push(Variant(t,b)))
            {
                TrainingContent.ValidateContent();var changed=TrainingInputFactory.Begin(request,7);
                Assert.That(changed.View.Window,Is.EqualTo(window+.01).Within(1e-10));
                Assert.That(original.View.Window,Is.EqualTo(window).Within(1e-10),"old live session must keep old kernel parameters");
            }
        }
        [Test] public void IndependentCharmPressureAndStudyParametersAreConsumed()
        {
            var body=Body();long oldCharm=BodyMetrics.GetEffectiveCharmMicro(body);var oldPressure=BodyCalculator.BodyFatPressureReduction(body.BodyFatMicro);
            var t=TrainingContent.Current;var b=BodyContent.Current;b.CharmCurve.ValueMicro[1]=100000;b.CharmCurve.ValueMicro[2]=100000;t.Knowledge.EveningStudyMicro=4000000;t.Knowledge.FullDayStudyMicro=13000000;
            using(ContentRuntime.Push(Variant(t,b)))
            {
                BodyContent.ValidateContent();TrainingContent.ValidateContent();
                Assert.That(BodyMetrics.GetEffectiveCharmMicro(body),Is.LessThan(oldCharm));Assert.That(BodyCalculator.BodyFatPressureReduction(body.BodyFatMicro).CompareTo(oldPressure),Is.Zero,"independent pressure curve");
                var evening=TrainingKnowledgeRules.Evening(body,new[]{"bench_press"},false);Assert.That(evening.NextBody.Exercise("bench_press").KnowledgeMicro,Is.EqualTo(4000000));Assert.That(evening.NextBody.Exercise("squat").KnowledgeMicro,Is.Zero);
                Assert.That(TrainingKnowledgeRules.FullDay(body,true,3).NextBody.Exercises.All(e=>e.KnowledgeMicro==13000000),Is.True);
                Assert.Throws<InvalidOperationException>(()=>TrainingKnowledgeRules.Evening(body,Array.Empty<string>(),false));Assert.Throws<InvalidOperationException>(()=>TrainingKnowledgeRules.Evening(body,new[]{"bench_press"},true));Assert.Throws<InvalidOperationException>(()=>TrainingKnowledgeRules.FullDay(body,false,3));
            }
        }
        [Test] public void InvalidDefinitionsRejectWithDocumentLocation()
        {
            var b=BodyContent.Current;var t=TrainingContent.Current;t.Fatigue.MandatoryInjuryMicro=149000000;
            Assert.That(Assert.Throws<ArgumentException>(()=>TrainingContent.Validate(t,b)).Message,Does.Contain("training.Fatigue"));
            t=TrainingContent.Current;t.Kernels.Single(k=>k.Id=="shoulder_press").Parameters.Single(p=>p.Key=="block_width").Value=.5;
            Assert.That(Assert.Throws<ArgumentException>(()=>TrainingContent.Validate(t,b)).Message,Does.Contain("shoulder_press"));
            t=TrainingContent.Current;t.Exercises[0].WeightBasisPoints[0]++;
            Assert.That(Assert.Throws<ArgumentException>(()=>TrainingContent.Validate(t,b)).Message,Does.Contain("training.Exercises"));
            b.Regions[0].Id="missing-region";Assert.That(Assert.Throws<ArgumentException>(()=>BodyContent.Validate(b)).Message,Does.Contain("body.Regions"));
        }
        [Test] public void CompleteInitializationRejectsMissingZeroAndDoesNotShareArrays()
        {
            Func<long,InitialValue> value=x=>new InitialValue { Value=x };
            var d=new BodyInitialDefinition { Regions=RegionIds.All.Select(id=>new InitialRegionDefinition { RegionId=id,StrengthMicro=value(10000000),LocalFatigueMicro=value(0),TemporaryFatigueMicro=value(0),InjuredDay=value(0),ExpiresAtStartOfDay=value(0) }).ToArray(),Exercises=ExerciseCatalog.All.Select(e=>new InitialExerciseDefinition { ExerciseId=e.Id,KnowledgeMicro=value(0),ProficiencyMicro=value(0) }).ToArray(),StressMicro=value(0),BodyFatMicro=value(25000000),BaseCharmMicro=value(50000000),MentalStressMicro=value(0) };
            var initialized=BodyState.CreateFromDefinition("actor",d);d.Regions[0].StrengthMicro.Value=20000000;Assert.That(initialized.Regions[0].StrengthMicro,Is.EqualTo(10000000));
            d.Regions[0].LocalFatigueMicro=null;Assert.Throws<ArgumentException>(()=>BodyState.CreateFromDefinition("actor",d));
            var training=new TrainingInitialDefinition { Exercises=ExerciseCatalog.All.Select(e=>new InitialTrainingExerciseDefinition { ExerciseId=e.Id,HighestUnlockedTier=value(1),AutomaticTiers=new bool[4] }).ToArray() };
            var progress=TrainingProgress.CreateFromDefinition("actor",training);training.Exercises[0].AutomaticTiers[0]=true;Assert.That(progress.Exercises[0].AutomaticTiers[0],Is.False);
        }
    }
}
