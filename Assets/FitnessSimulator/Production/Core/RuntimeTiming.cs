using System;
using System.Diagnostics;

namespace FitnessSimulator.Game.Core
{
    // Opt-in diagnostics, scoped to the caller thread. No file IO or timers when disabled.
    public static class RuntimeTiming
    {
        [ThreadStatic] public static Action<string, double> Observe;
        public static IDisposable Measure(string name) => Observe == null ? null : new Measurement(name, Observe);
        private sealed class Measurement : IDisposable
        {
            private readonly string name;
            private readonly Action<string, double> observe;
            private readonly Stopwatch clock = Stopwatch.StartNew();
            public Measurement(string name, Action<string, double> observe) { this.name = name; this.observe = observe; }
            public void Dispose() { observe(name, clock.Elapsed.TotalMilliseconds); }
        }
    }
}
