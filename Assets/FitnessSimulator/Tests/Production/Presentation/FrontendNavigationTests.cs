using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Tests.Presentation
{
    public sealed class FrontendNavigationTests
    {
        private GameObject host;
        private GameFrontendView ui;
        private FakeFrontend frontend;
        private sealed class FakeFrontend : IGameFrontend
        {
            public readonly List<FrontendAction> Commands = new List<FrontendAction>();
            public readonly FrontendView View = new FrontendView { RunId = "run", Revision = 1, ScreenId = "meal/test", CanOpenAuxiliary = true,
                Actions = new[] { new FrontendOption { Id = "open-menu", Label = "返回主菜单" }, new FrontendOption { Id = "meal", Label = "测试餐" } } };
            public FrontendView ReadView() => View;
            public FrontendReply Execute(FrontendAction action)
            {
                Commands.Add(action);
                return new FrontendReply { Accepted = true, Message = action.ActionId == "meal" ? "已进食测试餐：费用10，摄入200。" : action.ActionId == "continue" ? "上午工作已完成，收入50元，余额350元。" : null };
            }
            public void Advance(double seconds) { }
            public void SetSuspended(bool value) { }
        }
        [SetUp] public void SetUp()
        {
            frontend = new FakeFrontend(); host = new GameObject("FrontendNavigationTest"); ui = host.AddComponent<GameFrontendView>();
            ui.PreferenceFileOverride = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Fitness-FrontendNavigation", System.Guid.NewGuid().ToString("N"), "preferences.json");
            ui.Initialize(frontend, AssetDatabase.LoadAssetAtPath<Font>("Assets/FitnessSimulator/Production/Presentation/Fonts/NotoSansCJKsc-Regular.otf"));
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(host);
        private Button Find(string caption) => host.GetComponentsInChildren<Button>(false).Single(x => x.name == caption);
        private void Render()
        {
            // Edit-mode fixture cleanup; production destroys retired controls at frame end.
            var actions = (Transform)typeof(GameFrontendView).GetField("actionRoot", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
            foreach (var child in actions.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            typeof(GameFrontendView).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, new object[] { false });
        }
        private Text Feedback => host.GetComponentsInChildren<Text>().Single(x => x.name == "Feedback");

        [Test] public void MenuIsFixedNavigationAndRemovedEntriesAreAbsent()
        {
            var menu = Find("返回主菜单"); Assert.That(menu.transform.parent.name, Is.EqualTo("Navigation"));
            Assert.That(host.GetComponentsInChildren<Button>(true).Any(x => x.name == "字号" || x.name == "当天安排"), Is.False);
            menu.onClick.Invoke(); Assert.That(frontend.Commands.Single().ActionId, Is.EqualTo("open-menu"));
        }

        [Test] public void ConfirmationOrdersCancelBeforeConfirm()
        {
            frontend.View.Actions = new[] { new FrontendOption { Id = "test", Label = "需确认操作", RequiresConfirmation = true } };
            frontend.View.Revision++; Render(); Find("需确认操作").onClick.Invoke();
            Assert.That(Find("返回选择").transform.GetSiblingIndex(), Is.LessThan(Find("确认").transform.GetSiblingIndex()));
            Find("返回选择").onClick.Invoke(); Assert.That(frontend.Commands, Is.Empty);
        }

        [Test] public void MealSubmitsOnceAndFeedbackSurvivesAutomaticAdvanceButClearsOnManualNavigation()
        {
            Find("测试餐").onClick.Invoke();
            Assert.That(frontend.Commands.Count, Is.EqualTo(1));
            Assert.That(host.GetComponentsInChildren<Button>(false).Any(x => x.name == "确认"), Is.False);
            frontend.View.ScreenId = "next-event"; frontend.View.Revision++; Render();
            Assert.That(Feedback.text, Does.Contain("费用10"));
            Find("角色").onClick.Invoke(); Assert.That(Feedback.text, Is.Empty);
        }

        [TestCase("menu", "run")]
        [TestCase("next-event", "different-run")]
        public void MealFeedbackDoesNotLeakIntoMenuOrAnotherRun(string screen, string run)
        {
            Find("测试餐").onClick.Invoke(); frontend.View.ScreenId = screen; frontend.View.RunId = run; frontend.View.Revision++;
            Render(); Assert.That(Feedback.text, Is.Empty);
        }

        [Test] public void WorkCompletionFeedbackSurvivesAutomaticAdvance()
        {
            frontend.View.ScreenId = "work/morning"; frontend.View.Revision++;
            frontend.View.Actions = new[] { new FrontendOption { Id = "continue", Label = "完成工作" } };
            Render(); Find("完成工作").onClick.Invoke();
            frontend.View.ScreenId = "next-event"; frontend.View.Revision++; Render();
            Assert.That(frontend.Commands.Count, Is.EqualTo(1));
            Assert.That(Feedback.text, Does.Contain("收入50元"));
            Find("角色").onClick.Invoke(); Assert.That(Feedback.text, Is.Empty);
        }
    }
}
