# [FRAME] 半日工作收入领域接入

[FRAME] 非休息日、有效计划、实际完成上午／下午 work 事件的资格由 Application 校验。错过事件不补发；休息日无日薪；玩家和 NPC 使用同一领域接口。餐食记忆由 Application 的 SessionState 实现，本改动不增加第二套记忆。

[COMPUTED｜HIGH] 接口为 `LifeRules.PrepareWorkIncome(LifeActorState current, int day, string anchorId, string eventInstanceId)`，返回 `WorkIncomePreparation`。时段只接受 `morning`／`afternoon`；day 必须等于当前 Nutrition.Day；每次实际入账 `50 + Wallet.Affluence`，收据冻结执行时财富值。

| [FRAME] 状态 | [FRAME] 应用处理 |
|---|---|
| Prepared | 原子提交 Candidate、记录 Receipt、完成事件及其时间；不可只发布部分效果。 |
| AlreadyApplied | 同日同槽同事件已支付，返回原收据；不再支付、不再记录收入。 |
| WorkAlreadyCompleted | 同日同槽已由不同事件支付，返回原收据；不再支付、不再记录收入。 |
| LegacyIncomeAlreadyApplied | 旧 LastIncome 已结清该日，Receipt 为 null；反馈已结清并正常完成事件，不发第二份日薪。 |

[COMPUTED｜HIGH] `WorkIncomeKey` 按 run／actor／day／anchor 去重，独立于旧 `E01.Income`。`WorkIncomeReceipt` 包含身份、日、时段、事件 ID、财富快照、金额、配置版本与语义键。`LifeActorState.WorkIncomeReceipts` 最多保留最近支付日的两个槽；新日首次工作替换旧日数组，完整历史由应用日志保留。日切本身不支付，也不把缺席转换为收入。

[COMPUTED｜HIGH] 原 `HasLastIncome`／`LastIncome`、旧公式及 `LifeContent.Version` 保留。工作收据使用独立 `LifeWorkIncome.v1`。旧存档缺少新数组时，Normalize 返回空数组；旧收据不会转换成半日收据。所有候选与回执均深拷贝，输入不被修改；损坏的非空工作收据不静默修复。

[FRAME] 应用必须移除 E01 的付款调用但保留旧游标步骤；旧冻结事件按原树走完，新解析事件使用 work，过去时段不回补。本领域不进行全档迁移，不判断计划是否休息，不改变 NPC 决策或界面确认方式。

[COMPUTED｜HIGH] 2026-10-23 纯领域检查执行新 WorkIncomeTests 的 16 个非引擎用例及既有 LifeRulesTests 的 17 个用例，共 33/33 通过。日志：`C:/Users/admin/AppData/Local/Temp/Fitness-WorkIncome-20261023/pure-domain.log`。含 UNITY_EDITOR JSON 用例的独立纯编译 exit 0、无诊断，日志同目录 `UnityCompile.log`；未启动 Unity，JSON 运行验证及应用工作事件路径由根线程统一执行。

[RULES I BROKE]: 无。
