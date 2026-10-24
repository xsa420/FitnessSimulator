using FitnessSimulator.Game.Core;
using UnityEngine;

namespace FitnessSimulator.Game.Persistence
{
    public sealed class UnityJsonStateCodec : IStateCodec
    {
        public string Encode<T>(T value) { using (RuntimeTiming.Measure("json.encode")) return JsonUtility.ToJson(value); }
        public T Decode<T>(string json) { using (RuntimeTiming.Measure("json.decode")) return JsonUtility.FromJson<T>(json); }
    }
}
