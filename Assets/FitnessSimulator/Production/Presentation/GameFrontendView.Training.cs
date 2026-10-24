using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Application;
using UnityEngine;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Presentation
{
    public sealed partial class GameFrontendView
    {
        private RectTransform trainingRoot, inputArea, trainingDiagram;
        private RectTransform trainingBalanceMarker;
        private Text trainingCaption;
        private string trainingExercise, trainingDiagramKey;
        private readonly Dictionary<string, HoldInputButton> trainingButtons = new Dictionary<string, HoldInputButton>();
        private readonly Dictionary<int, FrontendTrainingInput> pointerBindings = new Dictionary<int, FrontendTrainingInput>();
        private readonly HashSet<int> ignoredPointers = new HashSet<int>();
        private FrontendTrainingInput[] frozenInputs = Array.Empty<FrontendTrainingInput>();
        private bool trainingRebind, recoveringPreparation;
        private int nextLogicalPointer;

        private void BuildTraining()
        {
            trainingRoot = Panel("TrainingInput", safeRoot, new Color(.035f, .065f, .09f));
            Stretch(trainingRoot, Vector2.zero, Vector2.one, new Vector2(16, 88), new Vector2(-16, -48));
            trainingCaption = Label("TrainingInstructions", trainingRoot, 14);
            Stretch(trainingCaption.rectTransform, new Vector2(0, .65f), Vector2.one);
            inputArea = Panel("InputArea", trainingRoot, new Color(.10f, .17f, .22f));
            Stretch(inputArea, Vector2.zero, new Vector2(1, .63f));
            inputArea.gameObject.AddComponent<TrainingPointerSurface>().Input = OnTrainingPointer;
            trainingDiagram = Panel("Diagram", inputArea, Color.clear);
            Stretch(trainingDiagram, Vector2.zero, Vector2.one);
            trainingDiagram.GetComponent<Image>().raycastTarget = false;
            trainingRoot.gameObject.SetActive(false);
        }

        private void RenderTraining(FrontendTrainingInputView current)
        {
            bool active = current != null && !current.IsComplete;
            trainingRoot.gameObject.SetActive(active);
            if (!active) { trainingExercise = null; trainingRebind = false; return; }
            if (!current.IsPreparing) recoveringPreparation = false;
            if (trainingExercise != current.ExerciseId)
            {
                foreach (var button in trainingButtons.Values) Destroy(button.gameObject);
                trainingButtons.Clear(); pointerBindings.Clear(); ignoredPointers.Clear();
                trainingExercise = current.ExerciseId; trainingDiagramKey = null;
                if (current.ExerciseId == "bench_press") { AddTrainingButton("Left", "左侧修正 · 按住", 0, 2); AddTrainingButton("Right", "右侧修正 · 按住", 1, 2); }
                else if (current.ExerciseId == "dumbbell_curl") { AddTrainingButton("Raise", "举起", 0, 2); AddTrainingButton("Lower", "放下", 1, 2); }
                else if (current.ExerciseId == "hip_thrust" || current.ExerciseId == "triceps_pushdown") AddTrainingButton("Press", "按住／松开", 0, 1);
            }
            if (current.IsSuspended && !trainingRebind && !appPaused && !focusLost) BeginTrainingRebind(current);
            foreach (var button in trainingButtons.Values) button.AcceptInput = !appPaused && !focusLost;
            string state = trainingRebind ? "已暂停：重新按住原按钮／黄色靶点。" : current.IsPreparing ? (recoveringPreparation || current.ExerciseId == "bench_press" ? "准备：保持当前接触状态。" : "准备：松开后重新操作。") : TrainingHint(current);
            trainingCaption.text = DisplayId(current.ExerciseId) + $"　有效 {current.EffectiveReps}/{current.TargetReps}　失误 {current.FailedAttempts}/3\n" + state + "\n" + TrainingFeedback(current.Feedback);
            DrawTrainingDiagram(current);
        }

        private void AddTrainingButton(string key, string caption, int index, int count)
        {
            var rect = Panel(key == "Left" ? "LeftHold" : key == "Right" ? "RightHold" : key + "Hold", inputArea, new Color(.15f, .27f, .36f));
            Stretch(rect, new Vector2((float)index / count, 0), new Vector2((float)(index + 1) / count, .48f), new Vector2(4, 0), new Vector2(-4, 0));
            var label = Label("Caption", rect, 20); label.text = caption; label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform, Vector2.zero, Vector2.one);
            var button = rect.gameObject.AddComponent<HoldInputButton>(); trainingButtons.Add(key, button);
            button.Pressed = _ => OnTrainingButton(key, true);
            button.Released = _ => OnTrainingButton(key, false);
        }

        private void OnTrainingButton(string key, bool down)
        {
            if (appPaused || focusLost) return;
            if (trainingRebind) { TryTrainingRebind(); return; }
            var current = frontend.ReadView().TrainingInput;
            if (current == null || current.IsComplete) return;
            bool held = (current.HeldInputs ?? Array.Empty<FrontendTrainingInput>()).Any(x => x.Kind == "ButtonDown" && x.Button == key);
            if (current.ExerciseId != "bench_press" && ((current.IsPreparing && !recoveringPreparation && !held) || (!down && !held))) return;
            SendTrainingInput(new FrontendTrainingInput { Kind = down ? "ButtonDown" : "ButtonUp", Button = key });
        }

        private void OnTrainingPointer(string kind, int physicalId, Vector2 position)
        {
            if (appPaused || focusLost || trainingButtons.Count != 0) return;
            var current = frontend.ReadView().TrainingInput;
            if (current == null || current.IsComplete) return;
            if (trainingRebind)
            {
                if (kind == "PointerDown")
                {
                    var target = frozenInputs.FirstOrDefault(x => x.Kind == "PointerDown" && !pointerBindings.Values.Any(p => p.PointerId == x.PointerId) && Vector2.Distance(position, new Vector2((float)x.X, (float)x.Y)) <= .03f);
                    if (target != null) pointerBindings[physicalId] = CopyInput(target);
                }
                else if (pointerBindings.TryGetValue(physicalId, out var bound) && (kind == "PointerUp" || Vector2.Distance(position, new Vector2((float)bound.X, (float)bound.Y)) > .03f)) pointerBindings.Remove(physicalId);
                TryTrainingRebind(); return;
            }
            if (kind == "PointerDown")
            {
                if (current.IsPreparing && !recoveringPreparation) { ignoredPointers.Add(physicalId); return; }
                pointerBindings[physicalId] = new FrontendTrainingInput { PointerId = nextLogicalPointer++ };
            }
            if (ignoredPointers.Contains(physicalId)) { if (kind == "PointerUp") ignoredPointers.Remove(physicalId); return; }
            if (!pointerBindings.TryGetValue(physicalId, out var logical)) return;
            if (kind != "PointerDown" && !(current.HeldInputs ?? Array.Empty<FrontendTrainingInput>()).Any(x => x.Kind == "PointerDown" && x.PointerId == logical.PointerId))
            { pointerBindings.Remove(physicalId); return; }
            var input = new FrontendTrainingInput { Kind = kind, PointerId = logical.PointerId, X = position.x, Y = position.y };
            SendTrainingInput(input);
            if (kind == "PointerUp") pointerBindings.Remove(physicalId);
        }

        private void SendTrainingInput(FrontendTrainingInput input) => Send("training-input", stringValue: JsonUtility.ToJson(input));
        private static FrontendTrainingInput CopyInput(FrontendTrainingInput input) => new FrontendTrainingInput { Kind = input.Kind, Button = input.Button, PointerId = input.PointerId, X = input.X, Y = input.Y };

        private void BeginTrainingRebind(FrontendTrainingInputView current)
        {
            trainingRebind = true; frozenInputs = (current.HeldInputs ?? Array.Empty<FrontendTrainingInput>()).Select(CopyInput).ToArray();
            pointerBindings.Clear(); ignoredPointers.Clear();
            foreach (var button in trainingButtons.Values) button.CancelSystemPointers();
            trainingDiagramKey = null;
            if (frozenInputs.Length == 0)
            {
                OpenModal("training-resume", "本组已暂停。确认继续后，准备0.35秒恢复。");
                Button("继续本组", ModalButtons(), () => TryTrainingRebind(true));
            }
        }

        private void TryTrainingRebind(bool explicitContinue = false)
        {
            if (!trainingRebind || appPaused || focusLost || (frozenInputs.Length == 0 && !explicitContinue)) return;
            foreach (var pair in trainingButtons)
                if (pair.Value.IsHeld != frozenInputs.Any(x => x.Kind == "ButtonDown" && x.Button == pair.Key)) return;
            if (pointerBindings.Count != frozenInputs.Count(x => x.Kind == "PointerDown")) return;
            if (!Send("training-rebind", stringValue: JsonUtility.ToJson(new FrontendTrainingRebind { Inputs = frozenInputs.Select(CopyInput).ToArray() }))) return;
            trainingRebind = false; recoveringPreparation = true; trainingDiagramKey = null; skipAdvanceOnce = true; CloseModal();
        }

        private void SuspendTrainingControls()
        {
            foreach (var button in trainingButtons.Values) { button.AcceptInput = false; button.CancelSystemPointers(); }
            pointerBindings.Clear(); ignoredPointers.Clear(); trainingRebind = false; recoveringPreparation = false;
        }

        private static string TrainingHint(FrontendTrainingInputView value)
        {
            switch (value.ExerciseId)
            {
                case "bench_press": return $"保持平衡在绿色区间　当前 {value.Balance:+0.00;-0.00;0.00}";
                case "dumbbell_curl": return $"节拍 {value.AttemptMilliseconds / 1000d:0.00} 秒：0.70秒点举起，1.50秒点放下";
                case "lat_pulldown": return "按住起点，沿折线依序经过标记，不要松手。";
                case "triceps_pushdown": return value.SignalGreen ? "绿灯：现在松开，再等待回位。" : value.Stage == "Return" ? "回位中，请等待。" : "按住按钮，等待绿灯后松开。";
                case "squat": return value.VisibleDirection >= 0 ? "记住方向：" + new[] { "左", "中", "右" }[Mathf.Clamp(value.VisibleDirection, 0, 2)] : "从圆心按住，按记忆滑向下方方向区，每次回圆心。";
                case "hip_thrust": return value.Stage == "Hold" ? "保持完成，等待判定。" : $"按住约 {value.TargetSeconds:0.0} 秒后松开。";
                case "shoulder_press": return "按住上方起点，穿过两处开口到下方，再沿开口返回。";
                case "lateral_raise": return "双指从两起点移至两终点，同步到达，保持0.3秒后同步松开。";
                default: return "按当前提示操作。";
            }
        }

        private string TrainingFeedback(string code) => string.IsNullOrEmpty(code) ? "" : UiText("training.feedback." + code);
        private void DrawTrainingDiagram(FrontendTrainingInputView current)
        {
            string key = current.ExerciseId + "/" + current.Step + "/" + current.FirstOpening + "/" + current.SecondOpening + "/" + current.Window + "/" + trainingRebind;
            if (key == trainingDiagramKey) { UpdateTrainingBalance(current); return; }
            trainingDiagramKey = key; ClearChildren(trainingDiagram); trainingBalanceMarker = null;
            if (trainingRebind)
            {
                foreach (var input in frozenInputs.Where(x => x.Kind == "PointerDown")) Dot((float)input.X, (float)input.Y, .03f, Color.yellow);
                return;
            }
            switch (current.ExerciseId)
            {
                case "bench_press":
                    Box(.5f - (float)current.Window / 2, .72f, .5f + (float)current.Window / 2, .9f, new Color(.12f, .6f, .35f));
                    trainingBalanceMarker = Panel("BalanceMarker", trainingDiagram, Color.yellow);
                    trainingBalanceMarker.GetComponent<Image>().raycastTarget = false;
                    UpdateTrainingBalance(current); break;
                case "lat_pulldown":
                    float[] xs = { .5f, .35f, .5f, .65f, .5f }, ys = { .1f, .4f, .8f, .4f, .1f };
                    for (int i = 0; i < 4; i++) Line(new Vector2(xs[i], ys[i]), new Vector2(xs[i + 1], ys[i + 1]));
                    for (int i = 0; i < 5; i++) Dot(xs[i], ys[i], .025f, i == current.Step + 1 ? Color.yellow : Color.cyan);
                    break;
                case "shoulder_press":
                    Barrier(.65f, (float)current.FirstOpening, (float)current.Window);
                    Barrier(.35f, (float)current.SecondOpening, (float)current.Window);
                    Dot(.5f, .9f, .04f, Color.cyan); Dot(.5f, .1f, .04f, Color.yellow); break;
                case "squat":
                    Dot(.5f, .85f, .1f, Color.cyan);
                    for (int i = 0; i < 3; i++) { Box(i / 3f + .01f, .02f, (i + 1) / 3f - .01f, .45f, new Color(.2f, .35f, .4f)); DiagramLabel(new[] { "左", "中", "右" }[i], (i + .5f) / 3, .2f); }
                    break;
                case "lateral_raise":
                    Dot(.4f, .7f, .06f, Color.cyan); Dot(.6f, .7f, .06f, Color.cyan);
                    Dot(.15f, .3f, .06f, Color.yellow); Dot(.85f, .3f, .06f, Color.yellow); break;
            }
        }

        private void UpdateTrainingBalance(FrontendTrainingInputView current)
        {
            if (!trainingBalanceMarker) return;
            float x = Mathf.Clamp01((float)(current.Balance + 1) / 2);
            Stretch(trainingBalanceMarker, new Vector2(x, .70f), new Vector2(x, .92f), new Vector2(-2, 0), new Vector2(2, 0));
        }

        private void Box(float x0, float y0, float x1, float y1, Color color)
        {
            var rect = Panel("Guide", trainingDiagram, color); rect.GetComponent<Image>().raycastTarget = false;
            Stretch(rect, new Vector2(x0, y0), new Vector2(x1, y1));
        }
        private void Dot(float x, float y, float radius, Color color) => Box(x - radius, y - radius, x + radius, y + radius, color);
        private void Barrier(float y, float center, float width)
        { Box(0, y - .03f, center - width / 2, y + .03f, Color.gray); Box(center + width / 2, y - .03f, 1, y + .03f, Color.gray); }
        private void DiagramLabel(string caption, float x, float y)
        { var label = Label("GuideLabel", trainingDiagram, 18); label.text = caption; label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform, new Vector2(x - .1f, y - .12f), new Vector2(x + .1f, y + .12f)); }
        private void Line(Vector2 start, Vector2 end)
        {
            // Small non-interactive marks display the public fixed path; no hit testing here.
            for (int i = 0; i <= 24; i++) { var point = Vector2.Lerp(start, end, i / 24f); Dot(point.x, point.y, .006f, new Color(.3f, .6f, .65f)); }
        }
    }
}
