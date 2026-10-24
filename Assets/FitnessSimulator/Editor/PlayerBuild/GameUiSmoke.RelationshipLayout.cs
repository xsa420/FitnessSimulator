// Layout-only fixture; does not validate relationship business rules.
using System;
using System.Linq;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Planning;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Editor
{
    public static partial class GameUiSmoke
    {
        private static RelationshipLayoutFrontend relationshipLayout;
        private static int relationshipLayoutStep;
        private static readonly string[] RelationshipCaptures = {
            "fixed-navigation-role-top", "fixed-navigation-role-bottom",
            "fixed-navigation-training-disabled", "meal-breakfast-preferred", "meal-lunch-preferred", "meal-dinner-preferred",
            "meal-feedback-after-auto-advance", "plan-legacy-anchor-readonly", "plan-noon-evening-options", "confirmation-cancel-left-confirm-right"
        };

        private sealed class RelationshipLayoutFrontend : IGameFrontend
        {
            public readonly FrontendView View = new FrontendView {
                RunId = "layout-fixture-only", Revision = 1, ScreenId = "relationship-layout-fixture",
                Title = "关系长文布局样本", Status = "布局夹具 · 非业务公开规则证据",
                Body = "打开角色查看长文。此样本只验证固定默认字号与导航布局。",
                CanOpenAuxiliary = true,
                Actions = new[] { new FrontendOption { Id = "open-menu", Label = "返回主菜单" } },
                BodyRows = Enumerable.Range(1, 4).SelectMany(index => new[] {
                    "人物布局样本 " + index + " · 身份说明占位",
                    "好感数值／当前搭子／次日搭子／生效日期：长文本排版样本",
                    "已知周计划：训练与休息安排；计划不保证实际到场。",
                    "书签库存／冷却最早日期／待表白状态：仅长度占位，不用于业务断言。"
                }).Concat(Enumerable.Range(1, 21).Select(index => "身体区域样本 " + index + "：力量、疲劳及状态说明。"))
                  .Concat(new[] { "【布局样本末尾】应能完整滚动至此，并点击返回。" }).ToArray()
            };
            public int MealSubmissions;
            private bool pendingAdvance;
            public FrontendView ReadView() => View;
            public FrontendReply Execute(FrontendAction action)
            {
                if (action.ActionId != "meal") throw new InvalidOperationException("Unexpected layout fixture command: " + action.ActionId);
                MealSubmissions++; pendingAdvance = true;
                inputs.Add("synthetic-layout-command:meal:no-payment-or-persistence");
                return new FrontendReply { Accepted = true, Message = "布局回执：已进食豆浆餐，支出12元，摄入350千卡。" };
            }
            public void Advance(double seconds)
            {
                if (!pendingAdvance) return;
                pendingAdvance = false; View.Revision++; View.ScreenId = "synthetic-auto-next";
                View.Title = "进食后的自动推进布局样本"; View.Body = "本页由布局夹具自动推进，不涉及支付或持久化。";
                View.Actions = new[] { new FrontendOption { Id = "open-menu", Label = "返回主菜单" } };
                inputs.Add("synthetic-layout-auto-advance:screen-and-revision-changed");
            }
            public void SetSuspended(bool value) { }
        }

        private static void TickRelationshipLayout(double now)
        {
            if (now < nextAction) return;
            nextAction = now + .7;
            var view = relationshipLayout.View;
            switch (relationshipLayoutStep++)
            {
                case 0:
                    var navigation = host.GetComponentsInChildren<Button>(false);
                    if (navigation.Any(x => x.name == "字号" || x.name == "当天安排")) throw new InvalidOperationException("Removed navigation entry returned.");
                    if (navigation.Single(x => x.name == "返回主菜单").transform.parent.name != "Navigation") throw new InvalidOperationException("Menu is not fixed navigation.");
                    Click("角色"); break;
                case 1: Capture(RelationshipCaptures[0], view); ScrollRelationshipToBottom(); break;
                case 2:
                    Capture(RelationshipCaptures[1], view);
                    Click("返回当前事件");
                    view.Revision++; view.ScreenId = "training-layout-fixture";
                    // Deliberately true: verify the UI's own active-training guard.
                    view.CanOpenAuxiliary = true;
                    view.TrainingInput = new FrontendTrainingInputView {
                        ExerciseId = "bench_press", Stage = "Balance", TargetReps = 8, Window = .2
                    };
                    inputs.Add("fixture:active-training-view-with-no-kernel-or-business-progression");
                    break;
                case 3:
                    foreach (string caption in new[] { "角色", "计划", "返回主菜单" })
                    {
                        var button = host.GetComponentsInChildren<Button>(false).Single(x => x.name == caption);
                        if (button.interactable) throw new InvalidOperationException("Auxiliary entry enabled during training: " + caption);
                        // Even an unsolicited callback must not open the auxiliary modal.
                        inputs.Add("synthetic-callback:disabled-auxiliary-guard:" + caption);
                        button.onClick.Invoke();
                    }
                    if (host.GetComponentsInChildren<RectTransform>(false).Any(x => x.name == "Modal"))
                        throw new InvalidOperationException("Auxiliary modal opened during training.");
                    AssertTrainingLayout();
                    Capture(RelationshipCaptures[2], view);
                    view.TrainingInput = null; view.Revision++; break;
                case 4:
                    foreach (string name in new[] { "Page", "Actions" })
                        if (!host.GetComponentsInChildren<RectTransform>(true).Single(x => x.name == name).gameObject.activeInHierarchy)
                            throw new InvalidOperationException("Page did not return after training: " + name);
                    inputs.Add("visibility:page-and-actions-restored-after-training");
                    SetMealLayout("breakfast", "早餐", "豆浆餐 · 12元 · 350千卡 · 上次选择"); break;
                case 5:
                    Capture(RelationshipCaptures[3], view); ClickOption(view, "meal"); break;
                case 6:
                    if (relationshipLayout.MealSubmissions != 1 || view.ScreenId != "synthetic-auto-next") throw new InvalidOperationException("Meal fixture did not submit once and advance.");
                    if (FindButton("确认") != null) throw new InvalidOperationException("Meal unexpectedly requires confirmation.");
                    if (!host.GetComponentsInChildren<Text>(false).Single(x => x.name == "Feedback").text.Contains("支出12元")) throw new InvalidOperationException("Meal feedback vanished on automatic advance.");
                    Capture(RelationshipCaptures[6], view);
                    SetMealLayout("lunch", "午餐", "米饭套餐 · 20元 · 650千卡 · 上次选择"); break;
                case 7:
                    Capture(RelationshipCaptures[4], view);
                    SetMealLayout("dinner", "晚餐", "不吃 · 0元 · 0千卡 · 上次选择"); break;
                case 8:
                    Capture(RelationshipCaptures[5], view);
                    var draft = PlanCatalog.CreateBenchPressSlice(); draft.Slots[0].AnchorId = "morning";
                    view.Revision++; view.ScreenId = "plan-layout"; view.Title = "计划布局样本"; view.Body = "旧上午值保留，主动选择后才能保存。";
                    view.PlanAnchorOptions = new[] { new FrontendOption { Id = "noon", Label = "中午" }, new FrontendOption { Id = "evening", Label = "晚上" } };
                    view.Actions = new[] { new FrontendOption { Id = "save-plan", Label = "编辑计划布局样本", StringValue = JsonUtility.ToJson(draft) } }; break;
                case 9: ClickOption(view, "save-plan"); break;
                case 10: Click("动作、组合、时段与排序"); break;
                case 11: ScrollLayoutLabelIntoView("LegacyAnchor"); Capture(RelationshipCaptures[7], view); Click("选择训练时段"); break;
                case 12: Capture(RelationshipCaptures[8], view); Click("返回本项训练"); break;
                case 13: Click("返回计划总览"); break;
                case 14:
                    Click("关闭编辑");
                    view.Revision++; view.ScreenId = "confirmation-layout"; view.Title = "确认布局样本"; view.Body = "仅检查左右顺序，不执行业务。";
                    view.Actions = new[] { new FrontendOption { Id = "fixture-confirm", Label = "打开确认布局", RequiresConfirmation = true, ConfirmationText = "布局示例：返回在左，确认在右。" } }; break;
                case 15: Click("打开确认布局"); break;
                case 16:
                    Canvas.ForceUpdateCanvases();
                    var cancel = FindButton("返回选择"); var confirm = FindButton("确认");
                    if (cancel.transform.position.x >= confirm.transform.position.x) throw new InvalidOperationException("Confirmation row order is reversed.");
                    VisiblePointer(cancel.gameObject, 70, false); VisiblePointer(confirm.gameObject, 70, false);
                    Capture(RelationshipCaptures[9], view); Click("返回选择"); Finish(true, null); break;
            }
        }

        private static void SetMealLayout(string id, string title, string label)
        {
            var view = relationshipLayout.View;
            view.Revision++; view.ScreenId = "meal/" + id; view.Title = title + " · 布局样本";
            view.Body = "按该餐次上次成功选择标记；单击立即提交。此处仅为布局占位。";
            view.Actions = new[] { new FrontendOption { Id = "meal", Label = label, IsPreferred = true, StringValue = id }, new FrontendOption { Id = "open-menu", Label = "返回主菜单" } };
        }

        private static void ScrollLayoutLabelIntoView(string name)
        {
            var label = host.GetComponentsInChildren<Text>(false).Single(x => x.name == name);
            var scroll = label.GetComponentInParent<ScrollRect>();
            for (int step = 0; step < 120; step++)
            {
                Canvas.ForceUpdateCanvases();
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, label.rectTransform);
                if (bounds.min.y >= scroll.viewport.rect.yMin && bounds.max.y <= scroll.viewport.rect.yMax) return;
                scroll.scrollSensitivity = 12;
                ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, bounds.min.y < scroll.viewport.rect.yMin ? -1 : 1) }, ExecuteEvents.scrollHandler);
            }
            throw new InvalidOperationException("Layout label cannot be made visible: " + name);
        }

        private static void AssertTrainingLayout()
        {
            Canvas.ForceUpdateCanvases();
            var canvas = host.GetComponentInChildren<Canvas>().GetComponent<RectTransform>();
            var rects = host.GetComponentsInChildren<RectTransform>(false);
            var allRects = host.GetComponentsInChildren<RectTransform>(true);
            var cover = CanvasBounds(rects.Single(x => x.name == "TrainingInput"), canvas);
            foreach (string name in new[] { "Page", "Actions" })
            {
                var panel = allRects.Single(x => x.name == name);
                if (panel.gameObject.activeInHierarchy) throw new InvalidOperationException("Underlying panel remains active during training: " + name);
                inputs.Add("visibility:" + name + ":inactive-during-training");
                var covered = CanvasBounds(panel, canvas);
                if (Mathf.Abs(cover.yMin - covered.yMin) > .01f || cover.yMax + .01f < covered.yMax
                    || cover.xMin - .01f > covered.xMin || cover.xMax + .01f < covered.xMax)
                    throw new InvalidOperationException("Training panel does not cover page geometry: " + name);
                inputs.Add(FormattableString.Invariant($"geometry:{name}:bottom={covered.yMin:F3}:training-bottom={cover.yMin:F3}"));
            }
            foreach (string name in new[] { "LeftHold", "RightHold" })
            {
                var button = rects.Single(x => x.name == name);
                var caption = button.GetComponentsInChildren<Text>().Single(x => x.name == "Caption");
                if (caption.preferredHeight > caption.rectTransform.rect.height + .01f)
                    throw new InvalidOperationException("Training caption overflows its height: " + name);
                inputs.Add(FormattableString.Invariant($"geometry:{name}:caption-preferred={caption.preferredHeight:F3}:caption-height={caption.rectTransform.rect.height:F3}:font={caption.fontSize}"));
            }
        }

        private static Rect CanvasBounds(RectTransform rect, RectTransform canvas)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(canvas.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(x => x.x), points.Min(x => x.y), points.Max(x => x.x), points.Max(x => x.y));
        }

        private static void ScrollRelationshipToBottom()
        {
            var scroll = host.GetComponentsInChildren<ScrollRect>(false).Single(x => x.name == "ModalScroll");
            Canvas.ForceUpdateCanvases();
            if (scroll.content.rect.height <= scroll.viewport.rect.height)
                throw new InvalidOperationException("Fixture does not overflow; scrolling has not been exercised.");
            for (int count = 0; count < 200 && scroll.verticalNormalizedPosition > .001f; count++)
            {
                scroll.scrollSensitivity = 24;
                ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) }, ExecuteEvents.scrollHandler);
                Canvas.ForceUpdateCanvases();
            }
            if (scroll.verticalNormalizedPosition > .001f) throw new InvalidOperationException("Could not reach relationship text bottom.");
            // The existing click guard checks viewport clipping and top-most raycast coverage.
            VisiblePointer(FindButton("返回当前事件").gameObject, 70, false);
            inputs.Add("scroll:relationship-layout-bottom-and-return-button-visible");
        }
    }
}
