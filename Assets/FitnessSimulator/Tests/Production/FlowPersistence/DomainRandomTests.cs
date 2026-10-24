using System;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Persistence;
using NUnit.Framework;

namespace FitnessSimulator.Game.Tests.FlowPersistence
{
    public sealed class DomainRandomTests
    {
        [Test] public void SplitMixGoldenSequenceAndJsonRecovery()
        {
            var state = new DomainRandomState { RunId = "r", ActorId = "p", Purpose = "entry", State = 0 };
            var codec = new UnityJsonStateCodec();
            uint[] expected = { 0xe220a839u, 0x6e789e6au, 0x06c45d18u, 0xf88bb8a8u };
            for (int i = 0; i < expected.Length; i++)
            {
                string before = codec.Encode(state);
                Assert.That(DomainRandom.PeekUInt(state), Is.EqualTo(expected[i]));
                var draw = DomainRandom.NextUInt(state);
                Assert.That(codec.Encode(state), Is.EqualTo(before));
                Assert.That(draw.Value, Is.EqualTo(expected[i]));
                state = codec.Decode<DomainRandomState>(codec.Encode(draw.State));
                Assert.That(state.DrawIndex, Is.EqualTo(i + 1));
                Assert.That(state.State, Is.EqualTo(draw.State.State));
            }
        }

        [Test] public void DomainIsolationAndDiscardedCandidateDoNotConsumeAuthority()
        {
            var original = DomainRandom.Create("run", "甲", "shop", "seed");
            var replay = DomainRandom.Create("run", "甲", "shop", "seed");
            Assert.That(original.State, Is.EqualTo(0xe8af51e6da9676daUL));
            Assert.That(original.State, Is.EqualTo(replay.State));
            Assert.That(original.State, Is.Not.EqualTo(DomainRandom.Create("run", "乙", "shop", "seed").State));
            Assert.That(original.State, Is.Not.EqualTo(DomainRandom.Create("run", "甲", "injury", "seed").State));
            Assert.That(original.State, Is.Not.EqualTo(DomainRandom.Create("other", "甲", "shop", "seed").State));
            var discarded = DomainRandom.NextUInt(original);
            for (int i = 0; i < 20; i++) discarded = DomainRandom.NextUInt(discarded.State);
            Assert.That(original.DrawIndex, Is.Zero);
            Assert.That(DomainRandom.NextUInt(original).Value, Is.EqualTo(DomainRandom.NextUInt(replay).Value));
            Assert.That(DomainRandom.Create("ab", "c", "d", "seed").State,
                Is.Not.EqualTo(DomainRandom.Create("a", "bc", "d", "seed").State));
        }

        [Test] public void BoundedDrawUsesFullUintDomainAndCountsRejectedSamples()
        {
            var state = new DomainRandomState { RunId = "r", ActorId = "p", Purpose = "shop", State = 0 };
            var draw = DomainRandom.NextInt(state, 1500000000);
            // First 0xe220a839 is above the 3,000,000,000 acceptance limit; second is accepted.
            Assert.That(draw.Value, Is.EqualTo(0x6e789e6au % 1500000000u));
            Assert.That(draw.State.DrawIndex, Is.EqualTo(2));
            Assert.That(state.DrawIndex, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => DomainRandom.NextInt(state, 0));
        }

        [Test] public void UnknownAlgorithmAndOverflowFailWithoutChangingState()
        {
            var state = DomainRandom.Create("r", "p", "entry", "seed");
            state.AlgorithmVersion = 2;
            Assert.Throws<InvalidOperationException>(() => DomainRandom.NextUInt(state));
            state.AlgorithmVersion = 1; state.DrawIndex = long.MaxValue;
            ulong original = state.State;
            Assert.Throws<OverflowException>(() => DomainRandom.NextUInt(state));
            Assert.That(state.State, Is.EqualTo(original));
        }
    }
}
