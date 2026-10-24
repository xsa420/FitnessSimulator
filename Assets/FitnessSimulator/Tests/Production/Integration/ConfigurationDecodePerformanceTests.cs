using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using FitnessSimulator.Game.Configuration;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Actors;
using FitnessSimulator.Game.Body;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.Integration
{
    public sealed class ConfigurationDecodePerformanceTests
    {
        [Test, Timeout(30000)]
        public void CachedDecoderKeepsDefensiveCopiesAndMeasuresExistingContentPath()
        {
            var package = ContentRuntime.Current;
            Measure<BodyContentDefinition>(package, "body");
            Measure<ActorContentDocument>(package, "actors");
            Measure<FlowContentDocument>(package, "flow");
            var original = package.Get<BodyContentDefinition>("body");
            string expected = ContentJson.Serialize(original);
            original.Attributes[0].Name = "mutated";
            original.Growth.StressThresholdsMicro[0] = -1;
            Assert.That(ContentJson.Serialize(package.Get<BodyContentDefinition>("body")), Is.EqualTo(expected));
            var altered = package.Export().Documents.ToDictionary(x => x.Id, x => x.Json);
            original.Version = "isolated-copy";
            altered["body"] = ContentJson.Serialize(original);
            var other = ContentPackage.FromDocuments(altered);
            using (ContentRuntime.Push(other)) Assert.That(BodyContent.Current.Attributes[0].Name, Is.EqualTo("mutated"));
            using (ContentRuntime.Push(package)) Assert.That(ContentJson.Serialize(BodyContent.Current), Is.EqualTo(expected));
        }
        private static void Measure<T>(ContentPackage package, string id) where T : class
        {
            var type = typeof(ContentPackage).Assembly.GetType("FitnessSimulator.Game.Configuration.StrictContentJson");
            var parse = type.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
            var decode = type.GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static);
            var parsed = parse.Invoke(null, new object[] { id, package.Export().Documents.Single(x => x.Id == id).Json });
            var args = new object[] { parsed, typeof(T) };
            Assert.That(ContentJson.Serialize(package.Get<T>(id)), Is.EqualTo(ContentJson.Serialize((T)decode.Invoke(null, args))));
            const int count = 300;
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) decode.Invoke(null, args);
            double before = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            for (int i = 0; i < count; i++) package.Get<T>(id);
            double after = timer.Elapsed.TotalMilliseconds;
            TestContext.WriteLine(id + " count=" + count + " oldDecodeMs=" + before + " cachedGetMs=" + after + " ratio=" + after / before);
        }
    }
}
