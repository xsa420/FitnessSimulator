using System;
using FitnessSimulator.Game.Body;
using FitnessSimulator.Game.Core;
using FitnessSimulator.Game.Life;
using FitnessSimulator.Game.Numeric;
using FitnessSimulator.Game.Planning;

namespace FitnessSimulator.Game.Application
{
    public sealed partial class GameSession
    {
        private PreparedOutcome PrepareDayStep(RootEnvelope root, SessionState state, TrainingDayState training, ref BodyState body, ref LifeActorState life, string actorId = Actor)
        {
            if (state.Phase != "DayEnd" && state.Phase != "EnterDay") throw new InvalidOperationException("Not in day transition");
            string step = (state.Phase == "DayEnd" ? "E" : "N") + state.DayStep.ToString("00");
            var outcome = Outcome(root.RunId + "/" + actorId + "/d" + state.Day + "/" + step, "日切步骤已保存：" + step);
            if (state.Phase == "DayEnd") {
                switch (FlowContent.Current.DayEndSteps[state.DayStep]) {
                    case "close":
                        state.BeforeDayEndBody = body.Clone(); state.BeforeDayEndBalance = life.Wallet.Balance;
                        if (actorId == Actor) PrepareCompetitionDeadline(root, state, outcome);
                        break; // M1 closes entry dispatch; competition deadline integration belongs to M3.
                    case "income-summary":
                        // Preserve the saved day-step numbering. Income is now paid only by
                        // completed work events; old recorded daily income remains historical.
                        break;
                    case "nutrition": state.Nutrition = LifeRules.EvaluateNutrition(life.Nutrition); CompleteDayBusiness(outcome); break;
                    case "recover-local":
                        body = BodyCalculator.RecoverLocalFatigue(body, new Rational(state.Nutrition.LocalRecoveryMultiplierNumerator, state.Nutrition.LocalRecoveryMultiplierDenominator));
                        CompleteDayBusiness(outcome); break;
                    case "recover-stress":
                        body = BodyCalculator.RecoverStress(body, new Rational(state.Nutrition.BodyRecoveryMultiplierNumerator, state.Nutrition.BodyRecoveryMultiplierDenominator));
                        CompleteDayBusiness(outcome); break;
                    case "deficit-stress":
                        body = BodyCalculator.AddStress(body, new Rational(state.Nutrition.DeficitStressNumerator, state.Nutrition.DeficitStressDenominator));
                        CompleteDayBusiness(outcome); break;
                    case "clear-temporary": body = BodyCalculator.ClearTemporaryFatigue(body); CompleteDayBusiness(outcome); break;
                    case "expiry": break; // Expiry dates remain explicit; activation is N01, never during this day.
                    case "unlock": break; // Reached unlock dates/pending plan were already saved by their owner.
                    case "summary":
                        state.DaySummary = DescribeDay(state, body, life);
                        outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "DaySummary", Json = codec.Encode(new DayRecord { Day = state.Day, Text = state.DaySummary, Body = body, Nutrition = life.Nutrition }) });
                        break;
                    case "await": state.Phase = "AwaitNextDay"; break;
                    default: throw new InvalidOperationException("Invalid day-end step");
                }
                state.DayStep++;
            } else {
                switch (FlowContent.Current.EnterDaySteps[state.DayStep - 1]) {
                    case "new-day":
                        outcome.Appends.Add(new LogAppend { StreamId = "run/" + root.RunId + "/records", Kind = "NutritionArchive", Json = codec.Encode(life.Nutrition) });
                        state.Day++; state.AnchorIndex = 0; state.LastOvertime = 0; state.IncomingOvertime = 0;
                        if (actorId != Actor) {
                            var shared = Read<SessionState>(root, "session");
                            state.EarlyAvailableDay = shared.EarlyAvailableDay; state.LateAvailableDay = shared.LateAvailableDay;
                        }
                        state.AnchorGameSeconds = 0; state.Nutrition = null;
                        life = LifeRules.StartNutritionDay(life, state.Day);
                        body = BodyCalculator.ExpireInjuries(body, state.Day);
                        state.DayStartBody = body.Clone(); state.DayStartBalance = life.Wallet.Balance;
                        state.BeforeDayEndBody = null; state.ChangedFields.Clear();
                        training.Workout = null;
                        Write(root, "activity", new ActivityState { Day = state.Day }, actorId);
                        AdvanceRelationships(root, state.Day, actorId);
                        state.DayStep = 2; CompleteDayBusiness(outcome); break;
                    case "plans-quota":
                        var plan = PlanRules.ApplyDay(Plan(root, actorId), state.Day);
                        Write(root, "plan", PlanPersistence.Export(plan), actorId);
                        FreezeQuota(root, state, training, actorId);
                        // M1 deliberately has no shop/NPC/competition initialization; these are M2/M3 gates.
                        state.DayStep = 3; break;
                    case "shops":
                        RefreshShops(root, state, actorId, outcome); state.DayStep = 4; break;
                    case "publish": state.Phase = "Anchors"; state.DayStep = 0; break;
                    default: throw new InvalidOperationException("Invalid enter-day step");
                }
            }
            return outcome;
        }
        private static void CompleteDayBusiness(PreparedOutcome outcome)
        { outcome.CheckKind = CheckKind.NormalCompletion; outcome.EventDisposition = EventDisposition.CompleteNode; }
        [Serializable] private sealed class DayRecord { public int Day; public string Text; public BodyState Body; public NutritionDay Nutrition; }
    }
}
