using System.Collections.Generic;
using System.Reflection;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Presentation
{
    public sealed class TrainingInputAdapterTests
    {
        private GameObject host;
        private GameFrontendView ui;
        private FakeFrontend frontend;
        private sealed class FakeFrontend : IGameFrontend
        {
            public readonly List<FrontendAction> Commands = new List<FrontendAction>();
            public readonly FrontendView View = new FrontendView { RunId = "test", Revision = 1,
                TrainingInput = new FrontendTrainingInputView { ExerciseId = "lat_pulldown" } };
            public FrontendView ReadView() => View;
            public FrontendReply Execute(FrontendAction action) { Commands.Add(action); return new FrontendReply { Accepted = true }; }
            public void Advance(double seconds) { }
            public void SetSuspended(bool value) { }
        }
        [SetUp] public void SetUp()
        {
            host = new GameObject("InputAdapterTest"); ui = host.AddComponent<GameFrontendView>(); frontend = new FakeFrontend();
            Set("frontend", frontend); Set("view", frontend.View);
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(host); }
        private void Set(string name, object value) => typeof(GameFrontendView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, value);
        private void Call(string name, params object[] args) => typeof(GameFrontendView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, args);
        private void Pointer(string kind, int id, float x = .5f, float y = .1f) => Call("OnTrainingPointer", kind, id, new Vector2(x, y));

        [Test] public void OrdinaryPreparationContactMustReleaseBeforeNewDown()
        {
            frontend.View.TrainingInput.IsPreparing = true;
            Pointer("PointerDown", -1);
            frontend.View.TrainingInput.IsPreparing = false;
            Pointer("PointerMove", -1); Pointer("PointerUp", -1);
            Assert.That(frontend.Commands, Is.Empty);
            Pointer("PointerDown", -1);
            var sent = JsonUtility.FromJson<FrontendTrainingInput>(frontend.Commands[0].StringValue);
            Assert.That(sent.Kind, Is.EqualTo("PointerDown"));
            Assert.That(sent.PointerId, Is.GreaterThanOrEqualTo(0));
            Assert.That(string.IsNullOrEmpty(sent.Button), Is.True);
        }

        [Test] public void FrozenPointerRequiresActualTargetContactAndPreservesLogicalCoordinates()
        {
            frontend.View.TrainingInput.IsSuspended = true;
            frontend.View.TrainingInput.HeldInputs = new[] { new FrontendTrainingInput { Kind = "PointerDown", PointerId = 17, X = .5, Y = .1 } };
            Call("BeginTrainingRebind", frontend.View.TrainingInput);
            Pointer("PointerDown", 3, .8f, .8f);
            Assert.That(frontend.Commands, Is.Empty);
            Pointer("PointerDown", 4, .51f, .1f);
            Assert.That(frontend.Commands.Count, Is.EqualTo(1));
            Assert.That(frontend.Commands[0].ActionId, Is.EqualTo("training-rebind"));
            var sent = JsonUtility.FromJson<FrontendTrainingRebind>(frontend.Commands[0].StringValue).Inputs[0];
            Assert.That(sent.PointerId, Is.EqualTo(17)); Assert.That(sent.X, Is.EqualTo(.5)); Assert.That(sent.Y, Is.EqualTo(.1));
            frontend.View.TrainingInput.IsSuspended = false; frontend.View.TrainingInput.IsPreparing = true;
            Pointer("PointerUp", 4, .51f, .1f);
            Assert.That(frontend.Commands.Count, Is.EqualTo(2), "A real release during recovery preparation must reach the kernel.");
            Assert.That(JsonUtility.FromJson<FrontendTrainingInput>(frontend.Commands[1].StringValue).Kind, Is.EqualTo("PointerUp"));
        }

        [Test] public void ReleaseAfterKernelClearsAttemptContactsIsNotSubmitted()
        {
            Pointer("PointerDown", 9);
            frontend.View.TrainingInput.IsPreparing = true;
            Pointer("PointerUp", 9);
            Assert.That(frontend.Commands.Count, Is.EqualTo(1));
        }
    }
}
