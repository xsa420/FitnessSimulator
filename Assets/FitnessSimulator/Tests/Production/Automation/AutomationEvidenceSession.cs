using FitnessSimulator.Automation;
using System.Runtime.CompilerServices;
using FitnessSimulator.Game.Application;

namespace FitnessSimulator.Game.Tests.Automation
{
    // Checks real test saves after commands; never changes production persistence behavior.
    internal static class AutomationEvidenceSession
    {
        private sealed class Location { public string Directory; }
        private static readonly ConditionalWeakTable<IGameFrontend, Location> Locations = new ConditionalWeakTable<IGameFrontend, Location>();

        public static GameSession OpenAutomation(string directory, uint seed) => Track(GameSession.OpenAutomation(directory, seed), directory);
        public static GameSession OpenTest(string directory, uint seed) => Track(GameSession.OpenTest(directory, seed), directory);
        private static GameSession Track(GameSession session, string directory)
        {
            try { AutomationEvidence.ValidateFor(directory); }
            catch { session.Dispose(); throw; }
            Locations.Add(session, new Location { Directory = directory });
            return session;
        }

        public static FrontendReply Execute(IGameFrontend session, FrontendAction action)
        {
            try { return session.Execute(action); }
            finally { if (Locations.TryGetValue(session, out var location)) AutomationEvidence.ValidateFor(location.Directory); }
        }
    }
}
