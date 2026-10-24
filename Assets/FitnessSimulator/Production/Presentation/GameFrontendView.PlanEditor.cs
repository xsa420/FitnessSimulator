using System;
using System.Collections.Generic;
using System.Linq;
using FitnessSimulator.Game.Planning;
using UnityEngine;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Presentation
{
    public sealed partial class GameFrontendView
    {
        private readonly IExercisePlanCatalog planCatalog = new TrainingCatalogAdapter();

        private void ShowPlanModes()
        {
            OpenModal("plan-mode", "选择默认计划将替换当前未保存草稿。原已保存计划不受影响，直到保存成功。\n自然星期按周一至周日；按天循环按列表顺序重复。");
            foreach (PlanMode mode in ReadUiContent().Get<PlansDocument>("plans").Defaults.Select(x=>x.Mode))
            {
                var chosen = mode;
                Button(mode == PlanMode.NaturalWeek ? "载入自然星期默认计划" : "载入按天循环默认计划", modalContent, () =>
                {
                    OpenModal("plan-mode-confirm", "确认替换未保存草稿为" + (chosen == PlanMode.NaturalWeek ? "自然星期" : "按天循环") + "默认计划？");
                    var row = ModalButtons();
                    Button("保留草稿", row, RenderPlanEditor);
                    Button("替换草稿", row, () => { using (FitnessSimulator.Game.Configuration.ContentRuntime.Push(ReadUiContent())) planDraft = PlanCatalog.CreateDefault(chosen); draftDirty = true; RenderPlanEditor(); });
                });
            }
            Button("返回计划", ModalButtons(), RenderPlanEditor);
        }

        private void EditPlanSlot(PlanSlotDraft slot)
        {
            OpenModal("plan-slot", "编辑第" + (slot.Index + 1) + "项训练\n动作按列表顺序执行；组合、时段与保存资格由应用层检查。");
            var summary = Label("Selection", modalContent, 18); AutoHeight(summary.gameObject);
            summary.text = "时段：" + DisplayId(slot.AnchorId) + "　组合：" + CombinationName(slot.Combination);
            Button("选择组合", modalContent, () => ShowCombinations(slot));
            Button("载入组合预填动作", modalContent, () => ConfirmPrefill(slot));
            AddPlanAnchorControls(slot);
            var entries = slot.Entries ?? Array.Empty<PlanEntryDraft>();
            for (int index = 0; index < entries.Length; index++)
            {
                int position = index;
                var label = Label("Exercise", modalContent, 18); AutoHeight(label.gameObject);
                label.text = (index + 1) + "．" + DisplayId(entries[index].ExerciseId);
                var row = PlanEditorRow();
                Button("上移", row, () => MovePlanEntry(slot, position, -1)).interactable = index > 0;
                Button("下移", row, () => MovePlanEntry(slot, position, 1)).interactable = index + 1 < entries.Length;
                Button("移除", row, () =>
                {
                    var list = new List<PlanEntryDraft>(slot.Entries); list.RemoveAt(position);
                    slot.Entries = list.ToArray(); draftDirty = true; EditPlanSlot(slot);
                });
            }
            var hint = Label("AddExercise", modalContent, 18); AutoHeight(hint.gameObject);
            hint.text = entries.Length == 0 ? "尚无动作；添加动作或返回将本日改为休息。" : "添加动作（同日不重复）";
            string[] exerciseIds;
            using (FitnessSimulator.Game.Configuration.ContentRuntime.Push(ReadUiContent())) exerciseIds = planCatalog.ExerciseIds.ToArray();
            foreach (string id in exerciseIds)
            {
                string exerciseId = id;
                bool exists = Array.Exists(entries, entry => entry.ExerciseId == exerciseId);
                Button((exists ? "已添加 · " : "添加 · ") + DisplayId(exerciseId), modalContent, () =>
                {
                    var list = new List<PlanEntryDraft>(slot.Entries ?? Array.Empty<PlanEntryDraft>());
                    list.Add(new PlanEntryDraft { EntryId = "draft-" + Guid.NewGuid().ToString("N"), ExerciseId = exerciseId });
                    slot.Entries = list.ToArray(); draftDirty = true; EditPlanSlot(slot);
                }).interactable = !exists;
            }
            Button("返回计划总览", ModalButtons(), RenderPlanEditor);
        }

        private Transform PlanEditorRow()
        {
            var row = Panel("EntryOrder", modalContent, Color.clear);
            LayoutHeight(row.gameObject, 56);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8; layout.childForceExpandWidth = true;
            return row;
        }

        private void MovePlanEntry(PlanSlotDraft slot, int index, int delta)
        {
            int destination = index + delta;
            if (destination < 0 || destination >= slot.Entries.Length) return;
            var item = slot.Entries[index]; slot.Entries[index] = slot.Entries[destination]; slot.Entries[destination] = item;
            draftDirty = true; EditPlanSlot(slot);
        }

        private void ShowCombinations(PlanSlotDraft slot)
        {
            OpenModal("plan-combination", "选择训练组合；保留现有动作与顺序，保存时检查动作目标区域与组合是否相容。");
            foreach (var definition in ReadUiContent().Get<PlansDocument>("plans").Combinations.OrderBy(x => x.DisplayOrder))
            {
                var chosen = definition.Kind;
                Button((slot.Combination == chosen ? "✓ " : "") + CombinationName(chosen), modalContent,
                    () => { slot.Combination = chosen; draftDirty = true; EditPlanSlot(slot); });
            }
            Button("返回本项训练", ModalButtons(), () => EditPlanSlot(slot));
        }

        private string CombinationName(TrainingCombination combination) => ReadUiContent().Get<PlansDocument>("plans").Combinations.Single(x => x.Kind == combination).Name;

        private void ConfirmPrefill(PlanSlotDraft slot)
        {
            var definition = ReadUiContent().Get<PlansDocument>("plans").Combinations.Single(x => x.Kind == slot.Combination);
            OpenModal("plan-prefill", "替换本项动作列表为「" + definition.Name + "」配置的预填动作？尚未保存的本项动作与顺序将被替换。");
            var row = ModalButtons();
            Button("保留动作", row, () => EditPlanSlot(slot));
            Button("载入预填", row, () => {
                slot.Entries = definition.PrefillExerciseIds.Select(id => new PlanEntryDraft { EntryId = "draft-" + Guid.NewGuid().ToString("N"), ExerciseId = id }).ToArray();
                draftDirty = true; EditPlanSlot(slot);
            });
        }

        private void AddPlanAnchorControls(PlanSlotDraft slot)
        {
            // Availability belongs to the application projection, not this draft editor.
            var options = view.PlanAnchorOptions ?? Array.Empty<FitnessSimulator.Game.Application.FrontendOption>();
            if (options.Length > 0 && !Array.Exists(options, option => option.Id == slot.AnchorId))
            {
                var legacy = Label("LegacyAnchor", modalContent, 16); AutoHeight(legacy.gameObject);
                legacy.text = "原时段「" + DisplayId(slot.AnchorId) + "」仅供旧计划执行，已不可新选；请改选可用时段后再保存。";
            }
            if (options.Length == 0)
            {
                var hint = Label("AnchorUnavailable", modalContent, 16); AutoHeight(hint.gameObject);
                hint.text = "当前未提供可选时段，保留原时段。";
                return;
            }
            Button("选择训练时段", modalContent, () =>
            {
                OpenModal("plan-anchor", "选择本项训练的时段；可用状态以本次计划生效日为准。");
                foreach (var option in options)
                {
                    var chosen = option;
                    Button((slot.AnchorId == chosen.Id ? "✓ " : "") + chosen.Label + (chosen.Enabled ? "" : "\n" + chosen.DisabledReason), modalContent,
                        () => { slot.AnchorId = chosen.Id; draftDirty = true; EditPlanSlot(slot); }).interactable = chosen.Enabled;
                }
                Button("返回本项训练", ModalButtons(), () => EditPlanSlot(slot));
            });
        }
    }
}
