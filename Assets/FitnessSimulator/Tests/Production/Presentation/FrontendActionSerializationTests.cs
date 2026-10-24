using FitnessSimulator.Game.Application;
using NUnit.Framework;
using UnityEngine;

namespace FitnessSimulator.Game.Tests.Presentation
{
    public sealed class FrontendActionSerializationTests
    {
        [Test] public void CommandFingerprintPayloadRetainsPlanAndPointerValues()
        {
            var action = new FrontendAction { OperationId = "same-operation", RunId = "original-run", ExpectedRevision = 7,
                ActionId = "save-plan", StringValue = "{\"说明\":\"卧推\"}", IntValue = 3 };
            string first = JsonUtility.ToJson(action);
            var restored = JsonUtility.FromJson<FrontendAction>(first);
            Assert.That(restored.StringValue, Is.EqualTo(action.StringValue));
            Assert.That(restored.IntValue, Is.EqualTo(3));
            Assert.That(restored.ExpectedRevision, Is.EqualTo(7));
            Assert.That(restored.RunId, Is.EqualTo("original-run"));
            action.StringValue = "{\"说明\":\"休息\"}";
            Assert.That(JsonUtility.ToJson(action), Is.Not.EqualTo(first), "Same operation ID with changed payload must not have the same serialized fingerprint input.");
        }
    }
}
