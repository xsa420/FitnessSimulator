using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FitnessSimulator.Game.Core
{
    [Serializable]
    public sealed class DomainRandomState
    {
        public string RunId;
        public string ActorId;
        public string Purpose;
        public int AlgorithmVersion = 1;
        public ulong State;
        public long DrawIndex;
    }

    public sealed class DomainRandomDraw
    {
        public DomainRandomState State;
        public uint Value;
    }

    public static class DomainRandom
    {
        public static DomainRandomState Create(string runId, string actorId, string purpose, string runSeed)
        {
            Require(runId); Require(actorId); Require(purpose); Require(runSeed);
            byte[] hash;
            using (var bytes = new MemoryStream())
            {
                foreach (string field in new[] { "domain-random-splitmix64-v1", runId, actorId, purpose, runSeed })
                {
                    byte[] encoded = new UTF8Encoding(false, true).GetBytes(field);
                    uint length = (uint)encoded.Length;
                    for (int i = 0; i < 4; i++) bytes.WriteByte((byte)(length >> (8 * i)));
                    bytes.Write(encoded, 0, encoded.Length);
                }
                using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes.ToArray());
            }
            ulong seed = 0;
            for (int i = 0; i < 8; i++) seed |= (ulong)hash[i] << (8 * i);
            return new DomainRandomState { RunId = runId, ActorId = actorId, Purpose = purpose, State = seed };
        }

        public static void Validate(DomainRandomState stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            Require(stream.RunId); Require(stream.ActorId); Require(stream.Purpose);
            if (stream.AlgorithmVersion != 1 || stream.DrawIndex < 0)
                throw new InvalidOperationException("Invalid or unsupported random stream.");
        }

        public static uint PeekUInt(DomainRandomState stream)
        {
            Validate(stream);
            unchecked
            {
                ulong value = stream.State + 0x9e3779b97f4a7c15UL;
                value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return (uint)((value ^ (value >> 31)) >> 32);
            }
        }

        public static DomainRandomDraw NextUInt(DomainRandomState stream)
        {
            uint value = PeekUInt(stream);
            long nextIndex = checked(stream.DrawIndex + 1);
            return new DomainRandomDraw { Value = value, State = new DomainRandomState {
                RunId = stream.RunId, ActorId = stream.ActorId, Purpose = stream.Purpose,
                AlgorithmVersion = stream.AlgorithmVersion, DrawIndex = nextIndex,
                State = unchecked(stream.State + 0x9e3779b97f4a7c15UL) } };
        }

        public static DomainRandomDraw NextInt(DomainRandomState stream, int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
            Validate(stream);
            uint bound = (uint)exclusiveMaximum;
            ulong limit = (1UL << 32) - (1UL << 32) % bound;
            DomainRandomDraw draw;
            do { draw = NextUInt(stream); stream = draw.State; } while (draw.Value >= limit);
            draw.Value %= bound;
            return draw;
        }

        private static void Require(string value) { if (string.IsNullOrEmpty(value)) throw new ArgumentException("Identity/seed must be nonempty."); }
    }
}
