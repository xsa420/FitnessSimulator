using System;
using System.Globalization;
using System.Linq;
using FitnessSimulator.Game.Application;
using UnityEngine;
using UnityEngine.UI;

namespace FitnessSimulator.Game.Presentation
{
    public sealed partial class GameFrontendView
    {
        [Serializable]
        private sealed class EndingPayload
        {
            public string AchievementId, SegmentId, Text;
        }
        private RectTransform endingNoteRoot;
        private InputField endingNoteInput;
        private Text endingNoteCount;
        private string endingNoteIdentity;

        private void BuildEndingNote()
        {
            endingNoteRoot = Panel("EndingNote", content, new Color(.10f, .17f, .22f));
            LayoutHeight(endingNoteRoot.gameObject, 150);
            var field = Panel("NoteInput", endingNoteRoot, new Color(.07f, .12f, .16f));
            Stretch(field, Vector2.zero, Vector2.one, new Vector2(6, 38), new Vector2(-6, -6));
            var text = Label("NoteText", field, 18);
            text.supportRichText = false;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4));
            var placeholder = Label("Placeholder", field, 16); placeholder.text = "填写结局备注；也可选择明确留空。";
            placeholder.color = new Color(.6f, .7f, .75f);
            Stretch(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4));
            endingNoteInput = field.gameObject.AddComponent<InputField>();
            endingNoteInput.textComponent = text; endingNoteInput.placeholder = placeholder;
            endingNoteInput.lineType = InputField.LineType.MultiLineNewline;
            endingNoteInput.characterLimit = 0;
            endingNoteInput.onValueChanged.AddListener(_ => RefreshEndingCount());
            endingNoteCount = Label("NoteCount", endingNoteRoot, 13);
            Stretch(endingNoteCount.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(8, 2), new Vector2(-8, 36));
            endingNoteRoot.gameObject.SetActive(false);
        }

        private void RenderEndingNote()
        {
            endingNoteRoot.gameObject.SetActive(view.EndingCanEditNote);
            if (!view.EndingCanEditNote) { endingNoteIdentity = null; return; }
            var action = (view.Actions ?? Array.Empty<FrontendOption>()).FirstOrDefault(x => x.Id == "ending-draft" || x.Id == "ending-note");
            string achievement = null;
            try { achievement = string.IsNullOrEmpty(action?.StringValue) ? null : JsonUtility.FromJson<EndingPayload>(action.StringValue)?.AchievementId; }
            catch (ArgumentException) { /* Keep input available; submitting rejects malformed payload. */ }
            string identity = view.RunId + "/" + view.ScreenId + "/" + achievement;
            if (identity == endingNoteIdentity) return;
            endingNoteIdentity = identity;
            endingNoteInput.SetTextWithoutNotify(view.EndingNoteDraft ?? "");
            RefreshEndingCount();
        }

        private void RefreshEndingCount()
        {
            int count = StringInfo.ParseCombiningCharacters(endingNoteInput.text ?? "").Length;
            endingNoteCount.text = count + "/200 文本元素" + (count > 200 ? " · 超出限制，请缩短后保存；输入未截断。" : " · 草稿、留空、最终提交分别操作。");
            endingNoteCount.color = count > 200 ? new Color(1, .65f, .45f) : new Color(.7f, .85f, .9f);
        }

        private bool PrepareEndingOption(ref FrontendOption option)
        {
            if (option.Id != "ending-draft" && option.Id != "ending-note" && option.Id != "ending-empty") return true;
            if (!view.EndingCanEditNote) { ShowFeedback("当前备注不可编辑。", 5); return false; }
            try
            {
                var payload = JsonUtility.FromJson<EndingPayload>(option.StringValue);
                if (payload == null || string.IsNullOrEmpty(payload.AchievementId)) throw new ArgumentException("缺少结局标识。");
                payload.Text = option.Id == "ending-empty" ? "" : endingNoteInput.text;
                if (StringInfo.ParseCombiningCharacters(payload.Text ?? "").Length > 200)
                { ShowFeedback("备注超过200文本元素，请缩短后再保存；原输入已保留。", 6); return false; }
                option = new FrontendOption { Id = option.Id, Label = option.Label, Enabled = option.Enabled, DisabledReason = option.DisabledReason,
                    RequiresConfirmation = option.RequiresConfirmation, ConfirmationText = option.ConfirmationText,
                    IntValue = option.IntValue, StringValue = JsonUtility.ToJson(payload) };
                return true;
            }
            catch (ArgumentException) { ShowFeedback("备注操作缺少合法载荷，请返回后重试。", 6); return false; }
        }
    }
}
