using System;
using System.IO;
using System.Reflection;
using FitnessSimulator.Automation;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Presentation;
using UnityEngine;

namespace FitnessSimulator.Game.Editor
{
    public static partial class GameUiSmoke
    {
        private static bool saveFeedbackSmoke;
        private static void CopySaveFeedbackSource(string targetDirectory)
        {
            string source = Path.Combine(Path.GetTempPath(), "Fitness-Usability-TwoDay", "688544e9df6c4d80ab4a035810d01258", "save");
            Directory.CreateDirectory(targetDirectory);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                string dest = Path.Combine(targetDirectory, file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)); AutomationEvidence.Copy(file, dest);
            }
        }
        private static void TickSaveFeedback()
        {
            var ui = host.GetComponent<GameFrontendView>();
            var render = typeof(GameFrontendView).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic);
            var view = session.ReadView();
            var action = new FrontendAction { ActionId = "continue-game", RunId = view.RunId, ExpectedRevision = view.Revision, OperationId = "save-ui/continue" };
            if (!session.Execute(action).Accepted) throw new InvalidOperationException("Could not continue copied archive");
            render.Invoke(ui, new object[] { true });
            view = session.ReadView();
            if (view.ScreenId != "AwaitNextDay") throw new InvalidOperationException("Expected actual archived day summary");
            AutomationEvidence.WriteAllText(Path.Combine(output, "business-before.json"), JsonUtility.ToJson(view, true));
            action = new FrontendAction { ActionId = "next-day", RunId = view.RunId, ExpectedRevision = view.Revision, OperationId = "save-ui/next" };
            if (!session.Execute(action).Accepted) throw new InvalidOperationException("Next day was not committed");
            view = session.ReadView();
            if (!view.IsTransitioning) throw new InvalidOperationException("Expected actual automatic transition");
            render.Invoke(ui, new object[] { false });
            AutomationEvidence.WriteAllText(Path.Combine(output, "transition-after.json"), JsonUtility.ToJson(view, true));
            inputs.Add("actual-copied-D2-summary:continue-game,next-day; retained business page; confirmed revision flash; no injected result");
            Capture("save-feedback-retained-business", view);
            Finish(true, null);
        }
    }
}
