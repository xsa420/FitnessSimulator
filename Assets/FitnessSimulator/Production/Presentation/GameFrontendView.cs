using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using FitnessSimulator.Game.Application;
using FitnessSimulator.Game.Planning;
using System.Collections.Generic;

namespace FitnessSimulator.Game.Presentation
{
    public sealed partial class GameFrontendView : MonoBehaviour
    {
        private IGameFrontend frontend;
        private PresentationDocument presentationContent;
        private string presentationContentHash;
        private PresentationDocument Presentation
        {
            get
            {
                var package = ReadUiContent();
                string hash = package.Hash;
                if (presentationContent == null || hash != presentationContentHash) { presentationContent = package.Get<PresentationDocument>("presentation"); presentationContentHash = hash; }
                return presentationContent;
            }
        }
        private string UiText(string id, params FitnessSimulator.Game.Narrative.NarrativeValue[] values) => PresentationContent.Text(Presentation, id, values);
        private Font font;
        private RectTransform safeRoot, content, modalRoot, benchRoot;
        private RectTransform pageRoot, actionsPanel;
        private ScrollRect pageScroll, actionsScroll;
        private Text status, heading, story, feedback, benchText, modalText;
        private RectTransform balanceMarker, safeWindow, progressFill;
        private Transform actionRoot;
        private HoldInputButton left, right;
        private Button bodyButton, planButton, menuButton;
        private FrontendView view;
        private string renderedScreen;
        private long renderedRevision = -1;
        private bool appPaused, focusLost, rebind, requiredLeft, requiredRight;
        private string modalKind;
        private FrontendOption selected;
        private long selectedRevision;
        private string selectedRunId;
        private float feedbackUntil;
        private string feedbackScreen, feedbackRunId;
        private long feedbackRevision;
        private bool committedFeedbackAcrossAdvance;
        private Rect lastSafeArea;
        private Vector2 lastScreen;
        private PlanDraft planDraft;
        private long planRevision;
        private string planRunId;
        private bool draftDirty;
        private RectTransform modalContent;
        private InputSystemUIInputModule inputModule;
        private bool skipAdvanceOnce;
#if UNITY_EDITOR
        // Only the editor capture runner may bypass OS focus. Never lifecycle evidence.
        public bool IgnoreHostFocusForEditorSmoke { get; set; }
#endif

        public void Initialize(IGameFrontend application, Font chineseFont)
        {
            frontend = application ?? throw new ArgumentNullException(nameof(application));
            font = chineseFont ? chineseFont : throw new ArgumentNullException(nameof(chineseFont));
            InitializeLocalPresentation();
            Build();
            Render(true);
        }

        private void Build()
        {
            var canvasObject = new GameObject("PlayerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(640, 320);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = Panel("Background", canvasObject.transform, new Color(.035f, .065f, .09f));
            Stretch(background, Vector2.zero, Vector2.one);
            safeRoot = Panel("SafeArea", canvasObject.transform, Color.clear);
            Stretch(safeRoot, Vector2.zero, Vector2.one);
            var header = Panel("Header", safeRoot, new Color(.07f, .12f, .16f));
            Stretch(header, new Vector2(0, 1), Vector2.one, new Vector2(8, -44), new Vector2(-8, -4));
            status = Label("Status", header, 16); Stretch(status.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-42, 0));
            BuildCommitIndicator(header);
            var footer = Panel("Navigation", safeRoot, new Color(.07f, .12f, .16f));
            Stretch(footer, Vector2.zero, new Vector2(1, 0), new Vector2(8, 4), new Vector2(-8, 60));
            var nav = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            nav.spacing = 8; nav.padding = new RectOffset(8, 8, 4, 4); nav.childForceExpandWidth = true;
            menuButton = Button(UiText("nav.menu"), footer, OpenMainMenu);
            bodyButton = Button(UiText("nav.body"), footer, () => ShowAuxiliary("body"));
            planButton = Button(UiText("nav.plan"), footer, () => ShowAuxiliary("plan"));
            var knowledgeButton = Button("知识与声音", footer, ShowKnowledgeDirectory);
            foreach (var navButton in new[] { menuButton, bodyButton, planButton, knowledgeButton }) LayoutHeight(navButton.gameObject, 48);
            var scrollRoot = Panel("Page", safeRoot, Color.clear);
            pageRoot = scrollRoot;
            Stretch(scrollRoot, Vector2.zero, new Vector2(.57f, 1), new Vector2(16, 88), new Vector2(-8, -48));
            content = ScrollContent(scrollRoot);
            pageScroll = scrollRoot.GetComponent<ScrollRect>();
            heading = Label("Heading", content, 22); LayoutHeight(heading.gameObject, 34);
            story = Label("Story", content, 18); AutoHeight(story.gameObject);
            BuildEndingNote();
            var actions = Panel("Actions", safeRoot, new Color(.07f, .12f, .16f));
            actionsPanel = actions;
            Stretch(actions, new Vector2(.57f, 0), Vector2.one, new Vector2(0, 88), new Vector2(-16, -48));
            actionRoot = ScrollContent(actions);
            actionsScroll = actions.GetComponent<ScrollRect>();
            feedback = Label("Feedback", safeRoot, 16); feedback.color = new Color(.65f, .95f, .83f);
            Stretch(feedback.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(20, 62), new Vector2(-20, 87));
            BuildBench();
            BuildTraining();
            modalRoot = Panel("Modal", safeRoot, new Color(.035f, .065f, .09f, 1));
            Stretch(modalRoot, Vector2.zero, Vector2.one);
            modalRoot.gameObject.SetActive(false);
            if (EventSystem.current == null)
            {
                var eventObject = new GameObject("PlayerEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventObject.transform.SetParent(transform, false);
                var input = eventObject.GetComponent<InputSystemUIInputModule>();
                input.AssignDefaultActions();
                input.pointerBehavior = UIPointerBehavior.AllPointersAsIs;
                inputModule = input;
            }
            else inputModule = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            ApplySafeArea();
        }

        private void BuildBench()
        {
            benchRoot = Panel("BenchPress", safeRoot, new Color(.035f, .065f, .09f));
            Stretch(benchRoot, Vector2.zero, Vector2.one, new Vector2(16, 88), new Vector2(-16, -48));
            benchText = Label("BenchStatus", benchRoot, 18);
            Stretch(benchText.rectTransform, new Vector2(0, .64f), Vector2.one);
            var bar = Panel("BalanceTrack", benchRoot, new Color(.15f, .22f, .28f));
            Stretch(bar, new Vector2(.08f, .50f), new Vector2(.92f, .62f));
            safeWindow = Panel("SafeWindow", bar, new Color(.12f, .45f, .34f));
            balanceMarker = Panel("BalanceMarker", bar, new Color(.98f, .8f, .3f));
            var progress = Panel("ProgressTrack", benchRoot, new Color(.15f, .22f, .28f));
            Stretch(progress, new Vector2(.08f, .40f), new Vector2(.92f, .46f));
            progressFill = Panel("Progress", progress, new Color(.18f, .75f, .6f));
            left = Hold("左侧修正\n按住", benchRoot, true);
            right = Hold("右侧修正\n按住", benchRoot, false);
            Stretch(left.GetComponent<RectTransform>(), new Vector2(.03f, .02f), new Vector2(.47f, .35f));
            Stretch(right.GetComponent<RectTransform>(), new Vector2(.53f, .02f), new Vector2(.97f, .35f));
            benchRoot.gameObject.SetActive(false);
        }

        private HoldInputButton Hold(string caption, Transform parent, bool isLeft)
        {
            var rect = Panel(isLeft ? "LeftHold" : "RightHold", parent, new Color(.15f, .27f, .36f));
            var text = Label("Caption", rect, 22); text.text = caption; text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one);
            var hold = rect.gameObject.AddComponent<HoldInputButton>();
            string action = isLeft ? "bench-left-" : "bench-right-";
            hold.Pressed = id => { if (!rebind) Send(action + "down", id); };
            hold.Released = id => { if (!rebind) Send(action + "up", id); };
            return hold;
        }

        private void Update()
        {
            if (frontend == null) return;
            ApplySafeArea();
            if (skipAdvanceOnce) skipAdvanceOnce = false;
            else if (!appPaused && !focusLost && !rebind && !trainingRebind && !LocalReading) frontend.Advance(Time.unscaledDeltaTime);
            Render(false);
            if (rebind && !appPaused && !focusLost && left.IsHeld == requiredLeft && right.IsHeld == requiredRight)
            {
                // No held logical buttons: explicit confirmation, not automatic resume.
                if (requiredLeft || requiredRight) ResumeRebound();
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !rebind && !trainingRebind)
                RequestClose();
            if (Time.unscaledTime > feedbackUntil && !rebind) { feedback.text = ""; committedFeedbackAcrossAdvance = false; }
        }

        private void LateUpdate()
        {
            ResizeActionCaptions(actionRoot);
            if (modalRoot != null && modalRoot.gameObject.activeSelf) ResizeActionCaptions(modalContent);
        }

        private void ResizeActionCaptions(Transform parent)
        {
            if (parent == null) return;
            // Width is supplied by the layout group. Re-evaluate after wrapping/font changes,
            // including disabled actions whose reason is part of the caption.
            foreach (Transform child in parent)
            {
                if (!child.gameObject.activeSelf || child.GetComponent<Button>() == null) continue;
                var label = child.GetComponentInChildren<Text>();
                var layout = child.GetComponent<LayoutElement>();
                if (label == null || layout == null || label.rectTransform.rect.width <= 0) continue;
                float height = Mathf.Max(56, Mathf.Ceil(label.preferredHeight) + 12);
                if (!Mathf.Approximately(layout.preferredHeight, height)) layout.preferredHeight = height;
            }
        }

        private void Render(bool force)
        {
            var candidate = frontend.ReadView();
            if (candidate == null || !PrepareTransitionPresentation(candidate, ref force)) return;
            view = candidate;
            RenderLocalPresentation();
            if (!KeepFeedbackFor(view))
            {
                feedback.text = "";
                committedFeedbackAcrossAdvance = false;
            }
            status.text = view.Status ?? "健身模拟器";
            RenderEndingNote();
            bool unifiedPlaying = view.TrainingInput != null && !view.TrainingInput.IsComplete;
            bool playing = unifiedPlaying || view.BenchPress != null && !view.BenchPress.IsComplete;
            pageRoot.gameObject.SetActive(!playing);
            actionsPanel.gameObject.SetActive(!playing);
            bodyButton.interactable = planButton.interactable = view.CanOpenAuxiliary && !playing;
            var menuOption = Array.Find(view.Actions ?? Array.Empty<FrontendOption>(), option => option.Id == "open-menu");
            menuButton.interactable = !playing && menuOption != null && menuOption.Enabled;
            if (force || view.ScreenId != renderedScreen || (!playing && view.Revision != renderedRevision))
            {
                if (modalKind != null && modalKind != "confirm" && !LocalReading && planDraft == null && !rebind && !trainingRebind) CloseModal();
                heading.text = view.Title ?? "";
                story.text = view.Body ?? "";
                ClearChildren(actionRoot);
                foreach (var option in view.Actions ?? Array.Empty<FrontendOption>())
                {
                    if (option.Id == "open-menu") continue;
                    var captured = option;
                    var button = Button(option.Label + (option.Enabled ? "" : "\n" + option.DisabledReason), actionRoot, () => Select(captured));
                    button.interactable = option.Enabled;
                    LayoutHeight(button.gameObject, 56);
                }
                renderedScreen = view.ScreenId; renderedRevision = view.Revision;
                Canvas.ForceUpdateCanvases();
                pageScroll.verticalNormalizedPosition = 1;
                actionsScroll.verticalNormalizedPosition = 1;
            }
            benchRoot.gameObject.SetActive(playing && !unifiedPlaying);
            RenderTraining(view.TrainingInput);
            if (unifiedPlaying) return;
            if (!playing) return;
            var bench = view.BenchPress;
            benchText.text = (rebind ? "恢复本组：按住原控制，再继续。" : bench.Instruction) +
                $"\n有效 {bench.EffectiveReps}/{bench.TargetReps}　失误 {bench.Failures}/3　平衡 {bench.Balance:+0.00;-0.00;0.00}";
            float position = Mathf.Clamp01((float)(bench.Balance + 1) / 2);
            Stretch(balanceMarker, new Vector2(position, 0), new Vector2(position, 1), new Vector2(-3, -2), new Vector2(3, 2));
            float half = Mathf.Clamp01((float)bench.WindowHalfWidth / 2);
            Stretch(safeWindow, new Vector2(.5f - half, 0), new Vector2(.5f + half, 1));
            Stretch(progressFill, Vector2.zero, new Vector2(Mathf.Clamp01((float)bench.Progress), 1));
            left.AcceptInput = right.AcceptInput = !appPaused && !focusLost;
        }

        private void Select(FrontendOption option)
        {
            if (!option.Enabled || BusinessSubmissionBlocked()) return;
            ClearCommittedFeedback();
            if (!PrepareEndingOption(ref option)) return;
            if (option.Id == "save-plan") { EditPlan(option.StringValue); return; }
            if (!option.RequiresConfirmation) { Send(option.Id, option.IntValue, stringValue: option.StringValue); return; }
            selected = option; selectedRevision = view.Revision; selectedRunId = view.RunId;
            OpenModal("confirm", option.ConfirmationText ?? option.Label);
            var row = ModalButtons();
            Button("返回选择", row, CloseModal);
            Button("确认", row, () => { var optionToSend = selected; var revision = selectedRevision; var runId = selectedRunId; CloseModal(); Send(optionToSend.Id, optionToSend.IntValue, revision, optionToSend.StringValue, runId); });
        }

        private bool Send(string actionId, int pointerId = 0, long? expectedRevision = null, string stringValue = null, string expectedRunId = null)
        {
            bool liveInput = actionId.StartsWith("training-", StringComparison.Ordinal) || actionId.StartsWith("bench-", StringComparison.Ordinal);
            if (frontend.ReadView()?.IsTransitioning == true || !liveInput && awaitingBusinessRender) return false;
            if (!liveInput) LockUntilNextBusinessRender();
            ClearCommittedFeedback();
            bool completedWork = actionId == "continue" && (frontend.ReadView().ScreenId ?? "").StartsWith("work/", StringComparison.Ordinal);
            var reply = frontend.Execute(new FrontendAction { OperationId = Guid.NewGuid().ToString("N"), RunId = expectedRunId ?? view.RunId, ExpectedRevision = expectedRevision ?? view.Revision, ActionId = actionId, IntValue = pointerId, StringValue = stringValue });
            if (reply != null && !string.IsNullOrEmpty(reply.Message))
            {
                ShowFeedback(reply.Message, Presentation.Style.CommittedFeedbackSeconds);
                committedFeedbackAcrossAdvance = reply.Accepted && (actionId == "meal" || completedWork);
            }
            bool accepted = reply != null && reply.Accepted;
            if (!actionId.StartsWith("training-") && !actionId.StartsWith("bench-")) audioPresentation.PlayCue(accepted ? "confirm" : "invalid");
            if (accepted && completedWork) QueueKnowledge("ordinary-complete");
            return accepted;
        }

        private void ShowFeedback(string message, float seconds)
        {
            committedFeedbackAcrossAdvance = false;
            // Execute may already have changed the screen/revision. Bind to its resulting view.
            var owner = frontend.ReadView();
            feedbackScreen = owner?.ScreenId;
            feedbackRunId = owner?.RunId;
            feedbackRevision = owner?.Revision ?? -1;
            feedback.text = message;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        private bool KeepFeedbackFor(FrontendView current)
        {
            if (current.RunId != feedbackRunId) return false;
            if (committedFeedbackAcrossAdvance)
                return current.ScreenId != "menu" && current.ScreenId != "error" && Time.unscaledTime <= feedbackUntil;
            return current.ScreenId == feedbackScreen && current.Revision == feedbackRevision;
        }

        private void ClearCommittedFeedback()
        {
            if (!committedFeedbackAcrossAdvance) return;
            feedback.text = ""; committedFeedbackAcrossAdvance = false;
        }

        private void OpenMainMenu()
        {
            if (view == null || view.TrainingInput != null && !view.TrainingInput.IsComplete || view.BenchPress != null && !view.BenchPress.IsComplete) return;
            var option = Array.Find(view.Actions ?? Array.Empty<FrontendOption>(), item => item.Id == "open-menu");
            if (option != null && option.Enabled) Select(option);
        }

        private void ShowAuxiliary(string kind)
        {
            if (kind != "body" && kind != "plan") return;
            if (view == null || !view.CanOpenAuxiliary || view.BenchPress != null && !view.BenchPress.IsComplete || view.TrainingInput != null && !view.TrainingInput.IsComplete) return;
            ClearCommittedFeedback();
            if (planDraft != null) { RequestClose(); return; }
            string text = kind == "body" ? string.Join("\n", view.BodyRows ?? Array.Empty<string>()) : view.PlanText;
            OpenModal(kind, text ?? "暂无可公开内容");
            Button("返回当前事件", ModalButtons(), CloseModal);
        }

        private void OpenModal(string kind, string text)
        {
            ClearChildren(modalRoot);
            modalKind = kind; modalRoot.gameObject.SetActive(true);
            var scroll = Panel("ModalScroll", modalRoot, Color.clear);
            Stretch(scroll, Vector2.zero, Vector2.one, new Vector2(12, 64), new Vector2(-12, -8));
            var scrollContent = ScrollContent(scroll);
            modalContent = scrollContent;
            modalText = Label("Details", scrollContent, 18); AutoHeight(modalText.gameObject); modalText.text = text;
        }

        private Transform ModalButtons()
        {
            var row = Panel("ModalButtons", modalRoot, Color.clear);
            Stretch(row, Vector2.zero, new Vector2(1, 0), new Vector2(12, 8), new Vector2(-12, 60));
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>(); group.spacing = 8; group.childForceExpandWidth = true;
            return row;
        }

        private void CloseModal() { bool bodyClosed = modalKind == "body"; if (modalRoot) modalRoot.gameObject.SetActive(false); modalKind = null; selected = null; if (bodyClosed) QueueKnowledge("body-exit"); }

        private void EditPlan(string json)
        {
            try
            {
                using (FitnessSimulator.Game.Configuration.ContentRuntime.Push(ReadUiContent()))
                    planDraft = string.IsNullOrEmpty(json) ? PlanCatalog.CreateBenchPressSlice() : JsonUtility.FromJson<PlanDraft>(json);
                if (planDraft == null || planDraft.Slots == null || planDraft.Slots.Length == 0) throw new ArgumentException("计划草稿缺少日期。");
                planRevision = view.Revision; planRunId = view.RunId; draftDirty = false; RenderPlanEditor();
            }
            catch (Exception exception) { planDraft = null; ShowFeedback("无法编辑计划：" + exception.Message, 6); }
        }

        private void RenderPlanEditor()
        {
            OpenModal("plan-edit", "编辑计划 · " + (planDraft.Mode == PlanMode.NaturalWeek ? "自然星期" : "按天循环") + "\n首次当天生效，修改次日生效；可全休息。动作可玩资格以训练页面为准。");
            Button("选择模式与默认计划", modalContent, ShowPlanModes);
            foreach (var slot in planDraft.Slots)
            {
                var captured = slot;
                string day = planDraft.Mode == PlanMode.NaturalWeek ? new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }[Mathf.Clamp(slot.Index, 0, 6)] : "第" + (slot.Index + 1) + "天";
                var caption = Label("Day", modalContent, 18); AutoHeight(caption.gameObject);
                caption.text = day + (slot.IsRest ? " · 休息" : " · " + DisplayId(slot.AnchorId) + " · " + string.Join("、", Array.ConvertAll(slot.Entries ?? Array.Empty<PlanEntryDraft>(), x => DisplayId(x.ExerciseId))));
                if (!slot.IsRest && view.PlanAnchorOptions != null && view.PlanAnchorOptions.Length > 0 && !Array.Exists(view.PlanAnchorOptions, option => option.Id == slot.AnchorId))
                    caption.text += "\n原时段现为只读；请改选可用时段后保存。";
                Button(slot.IsRest ? "改为卧推训练" : "改为休息日", modalContent, () =>
                {
                    if (captured.IsRest)
                    {
                        PlanSlotDraft training;
                        using (FitnessSimulator.Game.Configuration.ContentRuntime.Push(ReadUiContent())) training = PlanCatalog.Training(captured.Index, TrainingCombination.Free, PlanCatalog.BenchPressId);
                        captured.IsRest = false; captured.AnchorId = training.AnchorId; captured.Combination = training.Combination; captured.Entries = training.Entries;
                    }
                    else { captured.IsRest = true; captured.AnchorId = null; captured.Entries = Array.Empty<PlanEntryDraft>(); }
                    draftDirty = true; RenderPlanEditor();
                });
                if (!slot.IsRest)
                {
                    Button("动作、组合、时段与排序", modalContent, () => EditPlanSlot(captured));
                    var volumeRow = Panel("TrainingVolume", modalContent, Color.clear);
                    LayoutHeight(volumeRow.gameObject, 52);
                    var volumeLayout = volumeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
                    volumeLayout.spacing = 8; volumeLayout.childForceExpandWidth = true;
                    foreach (TrainingVolume volume in Enum.GetValues(typeof(TrainingVolume)))
                    {
                        var target = volume;
                        Button((slot.Volume == volume ? "✓ " : "") + new[] { "少组少次", "均衡", "多组多次" }[(int)volume], volumeRow,
                            () => { captured.Volume = target; draftDirty = true; RenderPlanEditor(); });
                    }
                }
            }
            if (planDraft.Mode == PlanMode.DayCycle && planDraft.Slots.Length < 14)
                Button("循环末尾增加休息日", modalContent, () => { var slots = new List<PlanSlotDraft>(planDraft.Slots); slots.Add(PlanCatalog.Rest(slots.Count)); planDraft.Slots = slots.ToArray(); draftDirty = true; RenderPlanEditor(); });
            if (planDraft.Mode == PlanMode.DayCycle && planDraft.Slots.Length > 1)
                Button("移除循环最后一天", modalContent, () => { var slots = new List<PlanSlotDraft>(planDraft.Slots); slots.RemoveAt(slots.Count - 1); planDraft.Slots = slots.ToArray(); draftDirty = true; RenderPlanEditor(); });
            var buttons = ModalButtons();
            Button("关闭编辑", buttons, RequestClose);
            Button("保存并退出", buttons, SaveDraft);
        }

        private void SaveDraft()
        {
            if (BusinessSubmissionBlocked()) return;
            ClearCommittedFeedback();
            if (view.PlanAnchorOptions != null && view.PlanAnchorOptions.Length > 0 && Array.Exists(planDraft.Slots,
                slot => !slot.IsRest && !Array.Exists(view.PlanAnchorOptions, option => option.Id == slot.AnchorId)))
            {
                OpenModal("plan-error", "计划含不可新选的旧时段。原值已保留，请在训练项内改选可用时段后再保存。");
                Button("返回修改计划", ModalButtons(), RenderPlanEditor); return;
            }
            LockUntilNextBusinessRender();
            var reply = frontend.Execute(new FrontendAction { OperationId = Guid.NewGuid().ToString("N"), RunId = planRunId, ExpectedRevision = planRevision, ActionId = "save-plan", StringValue = JsonUtility.ToJson(planDraft) });
            if (reply != null && reply.Accepted) { planDraft = null; draftDirty = false; CloseModal(); }
            else
            {
                OpenModal("plan-error", reply?.Message ?? "计划尚未保存");
                Button("返回修改计划", ModalButtons(), RenderPlanEditor);
            }
        }

        private void RequestClose()
        {
            if (planDraft == null) { CloseModal(); return; }
            if (!draftDirty) { planDraft = null; CloseModal(); return; }
            OpenModal("plan-exit", "计划有未保存修改。放弃不会撤销此前已保存版本。");
            var row = ModalButtons();
            Button("继续编辑", row, RenderPlanEditor);
            Button("放弃修改", row, () => { planDraft = null; draftDirty = false; CloseModal(); });
            Button("保存并退出", row, SaveDraft);
        }

        private void OnApplicationPause(bool paused)
        {
#if UNITY_EDITOR
            if (IgnoreHostFocusForEditorSmoke) return;
#endif
            appPaused = paused; ApplyLifecycle();
        }
        private void OnApplicationFocus(bool focused)
        {
#if UNITY_EDITOR
            if (IgnoreHostFocusForEditorSmoke) return;
#endif
            focusLost = !focused; ApplyLifecycle();
        }
        private bool wasSuspended;
        private void ApplyLifecycle()
        {
            if (frontend == null) return;
            bool suspended = appPaused || focusLost;
            if (suspended == wasSuspended) return;
            wasSuspended = suspended;
            if (audioPresentation) audioPresentation.SetSuspended(suspended);
            if (suspended)
            {
                var current = frontend.ReadView().BenchPress;
                requiredLeft = current != null && current.LeftHeld;
                requiredRight = current != null && current.RightHeld;
                frontend.SetSuspended(true);
                SuspendTrainingControls();
                left.AcceptInput = right.AcceptInput = false;
                if (inputModule) inputModule.enabled = false;
                left.CancelSystemPointers(); right.CancelSystemPointers();
                CloseModal();
            }
            else
            {
                frontend.SetSuspended(false);
                skipAdvanceOnce = true;
                if (inputModule) inputModule.enabled = true;
                var unified = frontend.ReadView().TrainingInput;
                if (unified != null && !unified.IsComplete)
                {
                    rebind = false; BeginTrainingRebind(unified); return;
                }
                var current = frontend.ReadView().BenchPress;
                rebind = current != null && !current.IsComplete;
                if (rebind && !requiredLeft && !requiredRight)
                {
                    OpenModal("resume", "本组已暂停，阶段和失误保留。点击继续后准备0.35秒恢复。");
                    Button("继续本组", ModalButtons(), ResumeRebound);
                }
                else if (!rebind && planDraft != null) RenderPlanEditor();
            }
        }

        private void ResumeRebound()
        {
            if (!Send("bench-rebind", (left.IsHeld ? 1 : 0) | (right.IsHeld ? 2 : 0))) return;
            rebind = false; CloseModal();
        }

        private void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                var child = parent.GetChild(index).gameObject;
                child.SetActive(false);
                if (UnityEngine.Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private void ApplySafeArea()
        {
            if (!safeRoot || Screen.width <= 0 || Screen.height <= 0) return;
            var size = new Vector2(Screen.width, Screen.height);
            var area = Screen.safeArea;
            if (area == lastSafeArea && size == lastScreen) return;
            safeRoot.anchorMin = area.position / size;
            safeRoot.anchorMax = (area.position + area.size) / size;
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
            lastSafeArea = area; lastScreen = size;
        }

        private RectTransform ScrollContent(RectTransform parent)
        {
            var hint = Label("ScrollHint", parent, 12); hint.text = "上下滑动查看更多 ↕";
            Stretch(hint.rectTransform, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(-14, 18));
            var viewport = Panel("Viewport", parent, Color.clear); Stretch(viewport, Vector2.zero, Vector2.one, new Vector2(0, 20), new Vector2(-14, 0));
            viewport.gameObject.AddComponent<RectMask2D>();
            var rect = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(viewport, false);
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = Vector2.zero;
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 10; layout.childForceExpandHeight = false; layout.childControlHeight = true;
            rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = parent.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = rect; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var track = Panel("Scrollbar", parent, new Color(.12f, .18f, .23f));
            Stretch(track, new Vector2(1, 0), Vector2.one, new Vector2(-10, 20), Vector2.zero);
            var handle = Panel("Handle", track, new Color(.45f, .65f, .72f));
            Stretch(handle, Vector2.zero, Vector2.one);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            return rect;
        }
        private string DisplayId(string id) => UiText("name." + id);
        private RectTransform Panel(string name, Transform parent, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.GetComponent<Image>().color = color; return rect;
        }
        private Text Label(string name, Transform parent, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>(); text.transform.SetParent(parent, false);
            var style = Presentation.Style;
            text.font = font; text.fontSize = size == 22 ? style.TitleSize : size == 18 ? style.BodySize : size == 16 ? style.SecondarySize : Math.Max(8, style.SecondarySize + size - 16); text.color = new Color(.91f, .95f, .97f); text.raycastTarget = false;
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.lineSpacing = style.LineHeight;
            return text;
        }
        private Button Button(string caption, Transform parent, UnityEngine.Events.UnityAction clicked)
        {
            var rect = Panel(caption, parent, new Color(.15f, .27f, .36f));
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(clicked);
            var label = Label("Label", rect, 18); label.text = caption; label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 2), new Vector2(-8, -2));
            LayoutHeight(rect.gameObject, Presentation.Style.ButtonMinHeight); return button;
        }
        private static void LayoutHeight(GameObject item, float value) { var layout = item.GetComponent<LayoutElement>() ?? item.AddComponent<LayoutElement>(); layout.minHeight = value; layout.preferredHeight = value; }
        private static void AutoHeight(GameObject item) { item.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin = default, Vector2 offsetMax = default)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}
