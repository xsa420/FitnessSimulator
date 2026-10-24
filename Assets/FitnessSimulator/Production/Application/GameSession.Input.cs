using System;
using System.Linq;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Flow;
using FitnessSimulator.Game.Planning;
using FitnessSimulator.Game.Training;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        public void Advance(double unscaledSeconds)
        {
            using (ContentScope()) AdvanceBound(unscaledSeconds);
        }
        private void AdvanceBound(double unscaledSeconds)
        {
            if (disposed || suspended || atMenu || IsHistoryOpen || error != null || dispatcher == null) return;
            if (double.IsNaN(unscaledSeconds) || double.IsInfinity(unscaledSeconds) || unscaledSeconds < 0 || unscaledSeconds > 60)
                throw new ArgumentOutOfRangeException(nameof(unscaledSeconds));
            if (IsProgressionPaused(CurrentSnapshot) || IsCompetitionMenuOpen(CurrentSnapshot)) return;
            if (AdvanceWorld()) return;
            // No business state can change while only the live input clock advances.
            // Persist exactly once when the real kernel produces its result.
            if (game != null && game.Result == null) {
                if (game.View.IsSuspended) return;
                AdvanceKernel(unscaledSeconds);
                if (game.Result == null) return;
                var completedRoot = CurrentSnapshot;
                var completedReply = Execute(new FrontendAction { ActionId = "result-ready", ExpectedRevision = completedRoot.Revision,
                    OperationId = completedRoot.RunId + "/system/" + completedRoot.Revision + "/result-ready" });
                if (!completedReply.Accepted && error == null) error = "流程已停在最近保存点：" + completedReply.Message;
                return;
            }
            var root = CurrentSnapshot;
            if (root.FindFragment("session", Actor) == null) return;
            var state = Read<SessionState>(root, "session");
            if (state.Phase == "InitialPlan" || state.Phase == "AwaitNextDay") return;
            string command = null;
            if (state.Phase == "DayEnd" || state.Phase == "EnterDay") command = "day-step";
            else {
                var flow = Read<FlowState>(root, "flow");
                var anchors = SessionContent.Anchors(state);
                if (state.AnchorIndex >= anchors.Length || flow.Tree == null || flow.Day != state.Day || flow.AnchorId != anchors[state.AnchorIndex]) command = "resolve-anchor";
                else if (flow.EntryPhase == EntryPhase.Finished) command = "finish-anchor";
                else if (flow.EntryPhase != EntryPhase.ChoicePending) {
                    var cursor = FlowEngine.Current(flow);
                    if (cursor?.Phase == NodePhase.NotStarted) command = "start-node";
                    else {
                        var training = Read<TrainingDayState>(root, "training");
                        var node = cursor == null ? null : flow.Tree.Nodes.Single(n => n.Id == cursor.NodeId);
                        if (cursor?.Phase == NodePhase.Started && SessionContent.IsAutomaticPostureHint(flow, training, node))
                            command = "continue";
                        else if (training.Workout?.Phase == WorkoutPhase.Playing) {
                            if (training.Workout.PendingRequest.Producer == TrainingProducer.AutoCapacity) command = "result-ready";
                            else {
                                SyncKernel();
                                if (game.View.IsSuspended) return;
                                AdvanceKernel(unscaledSeconds);
                                if (game.Result != null) command = "result-ready";
                            }
                        }
                    }
                }
            }
            if (command == null) return;
            if (!PlayerCanAct(root, command)) return;
            var reply = Execute(new FrontendAction { ActionId = command, ExpectedRevision = root.Revision,
                OperationId = root.RunId + "/system/" + root.Revision + "/" + command });
            if (!reply.Accepted && error == null) error = "流程已停在最近保存点：" + reply.Message;
        }

        private void AdvanceKernel(double seconds)
        {
            double milliseconds = seconds * 1000 + fractionalMilliseconds;
            int whole = (int)Math.Floor(milliseconds + 1e-9);
            fractionalMilliseconds = Math.Max(0, milliseconds - whole);
            game.Advance(whole); liveMilliseconds += whole;
        }

        private void SyncKernel()
        {
            if (dispatcher == null || error != null) return;
            var root = CurrentSnapshot;
            if (IsProgressionPaused(root)) { game = null; liveSetId = null; edges.Clear(); return; }
            if (root.FindFragment("training", Actor) == null) return;
            var workout = Read<TrainingDayState>(root, "training").Workout;
            if (workout?.Phase != WorkoutPhase.Playing || workout.PendingRequest.Producer == TrainingProducer.AutoCapacity) {
                game = null; liveSetId = null; edges.Clear(); liveMilliseconds = 0; fractionalMilliseconds = 0; return;
            }
            if (liveSetId == workout.PendingRequest.SetId && game != null) return;
            game = TrainingInputFactory.Begin(workout.PendingRequest, Read<SessionState>(root, "session").SetSeed);
            liveSetId = workout.PendingRequest.SetId; edges.Clear(); liveMilliseconds = 0; fractionalMilliseconds = 0;
            if (suspended) game.Suspend();
        }

        private FrontendReply Input(FrontendAction action)
        {
            if (IsProgressionPaused(CurrentSnapshot)) return Reply(false, "当前结局尚未完成。");
            if (game == null || game.Result != null) return Reply(false, "当前没有可操作的训练组。");
            if (action.ExpectedRevision != CurrentSnapshot.Revision || (!string.IsNullOrEmpty(action.RunId) && action.RunId != CurrentSnapshot.RunId)) return Reply(false, "界面已更新，请使用当前操作。");
            try {
                switch (action.ActionId) {
                    case "bench-left-down": SubmitBench(TrainingButton.Left, true); break;
                    case "bench-left-up": SubmitBench(TrainingButton.Left, false); break;
                    case "bench-right-down": SubmitBench(TrainingButton.Right, true); break;
                    case "bench-right-up": SubmitBench(TrainingButton.Right, false); break;
                    case "bench-rebind":
                        if (game.View.ExerciseId != PlanCatalog.BenchPressId || suspended || action.IntValue < 0 || action.IntValue > 3) throw new InvalidOperationException("尚未恢复前台或按键状态非法。");
                        game.Resume(new[] { TrainingButton.Left, TrainingButton.Right }.Where((b, i) => (action.IntValue & (1 << i)) != 0).Select(b => TrainingInput.ButtonEvent(b, true)).ToArray()); break;
                    case "training-input": game.Submit(ParseInput(codec.Decode<FrontendTrainingInput>(action.StringValue))); break;
                    case "training-rebind":
                        if (suspended) throw new InvalidOperationException("尚未恢复前台。");
                        var rebuilt = codec.Decode<FrontendTrainingRebind>(action.StringValue);
                        if (rebuilt?.Inputs == null) throw new ArgumentException("缺少重绑输入。");
                        game.Resume(rebuilt.Inputs.Select(ParseInput).ToArray()); break;
                    default: return Reply(false, "未知输入。");
                }
                edges.Add(new InputEdge { Milliseconds = liveMilliseconds, Sequence = edges.Count, Action = action.ActionId, Value = action.IntValue, Payload = action.StringValue });
                return Reply(true, null);
            } catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { return Reply(false, ex.Message); }
        }

        private void SubmitBench(TrainingButton button, bool held)
        {
            if (game.View.ExerciseId != PlanCatalog.BenchPressId) throw new ArgumentException("当前动作不接受卧推输入。");
            game.Submit(TrainingInput.ButtonEvent(button, held));
        }

        private static TrainingInput ParseInput(FrontendTrainingInput input)
        {
            if (input == null || !Enum.TryParse(input.Kind, false, out TrainingInputKind kind) || !Enum.IsDefined(typeof(TrainingInputKind), kind)
                || input.Kind != kind.ToString() || input.PointerId < 0 || double.IsNaN(input.X) || double.IsInfinity(input.X)
                || double.IsNaN(input.Y) || double.IsInfinity(input.Y) || input.X < 0 || input.X > 1 || input.Y < 0 || input.Y > 1)
                throw new ArgumentException("非法训练输入。");
            if (kind == TrainingInputKind.ButtonDown || kind == TrainingInputKind.ButtonUp) {
                if (!Enum.TryParse(input.Button, false, out TrainingButton button) || !Enum.IsDefined(typeof(TrainingButton), button) || input.Button != button.ToString()) throw new ArgumentException("未知训练按键。");
                return TrainingInput.ButtonEvent(button, kind == TrainingInputKind.ButtonDown);
            }
            if (!string.IsNullOrEmpty(input.Button)) throw new ArgumentException("触点不能同时指定按键。");
            return TrainingInput.Pointer(kind, input.PointerId, input.X, input.Y);
        }

        public void SetSuspended(bool value)
        {
            if (disposed || suspended == value) return;
            suspended = value;
            if (value && !atMenu) {
                game?.Suspend();
                if (game != null) edges.Add(new InputEdge { Milliseconds = liveMilliseconds, Sequence = edges.Count, Action = "suspend" });
            }
            // A live kernel stays frozen until the UI explicitly rebuilds its held inputs.
        }
    }
}
