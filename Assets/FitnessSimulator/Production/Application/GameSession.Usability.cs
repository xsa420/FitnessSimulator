using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Planning;
using System.Linq;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        private static string[] EditableTrainingAnchors => FlowContent.Current.Anchors.Where(a => a.EditableTraining).OrderBy(a => a.Order).Select(a => a.Id).ToArray();

        private AnchorEligibility EditingEligibility(SessionState state)
        {
            var today = SessionContent.Anchors(state).Where(EditableTrainingAnchors.Contains).ToArray();
            var tomorrow = codec.Decode<SessionState>(codec.Encode(state));
            tomorrow.Day++;
            return new AnchorEligibility(state.Day, today,
                SessionContent.Anchors(tomorrow).Where(EditableTrainingAnchors.Contains).Except(today));
        }

        private static bool IsEveningPlanWindow(SessionState state)
        {
            var anchors = SessionContent.Anchors(state);
            return state.Phase == "Anchors" && state.AnchorIndex >= 0 && state.AnchorIndex < anchors.Length
                && FlowContent.Anchor(anchors[state.AnchorIndex]).PlanEditing;
        }

        private static string LastMeal(SessionState state, MealSlot slot)
            => slot == MealSlot.Breakfast ? state.LastBreakfastOption
                : slot == MealSlot.Lunch ? state.LastLunchOption : state.LastDinnerOption;

        private static void RememberMeal(SessionState state, MealSlot slot, string option)
        {
            if (slot == MealSlot.Breakfast) state.LastBreakfastOption = option;
            else if (slot == MealSlot.Lunch) state.LastLunchOption = option;
            else state.LastDinnerOption = option;
        }
    }
}
