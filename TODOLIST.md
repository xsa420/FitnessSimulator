# [FRAME] 唯一批次待办与验收清单

[FRAME] 协作规则见[AGENTS.md](AGENTS.md)。制作建批／收口、架构实施期间单写，禁止并发编辑。代理按任务ID回传摘要；原始证据只存一份并在此链接。

## [FRAME] 当前批次 LITE-20261024：轻量测试与历史副本清理

- [FRAME] 用户反馈：取消逐次源码复制与重复封存；限制日志和快照；清理不必要副本且保留有效验收证据；验证后推送 GitHub。
- [FRAME] 范围：测试工具、证据策略、历史副本清理和对应文档；不改游戏业务，不出包。查看入口为 Tools、Docs/Verification.md 与本清单；当前架构唯一维护清单。
- [FRAME] 有限验收：版本追溯、引用式封存、容量边界、清理安全及证据完整性；必要的短测试，不重跑历史长线或构建。

| ID | 验收条件／反馈映射 | 实施负责人／唯一文件范围 | 依赖 | 独立验收人 | 状态 | 证据与结论 |
| --- | --- | --- | --- | --- | --- | --- |
| LITE-01 | 取消每次源码复制、提供引用式封存与可追溯版本记录；运行日志/证据预算有明确失败语义 | 工具程序：Tools/Game.ps1、Tools/Automation/Run.ps1、Tools/Testing下新增工具与测试（Test-MaintainEvidence.ps1归独立验收人） | 无 | 独立测试代理 | 已验收 | [独立验收](TestResults/Lightweight/LITE-20261024/acceptance.json)：版本追溯、引用封存、预算与实际入口通过 |
| LITE-02 | 自动化追踪与快照输出受限，不静默截断有效证据、保留业务断言 | 测试设施程序：Assets/FitnessSimulator/Tests/Production/Automation、Assets/FitnessSimulator/Editor/Automation、GameUiSmoke产证、两个测试/编辑器asmdef引用 | 与LITE-01约定预算 | 独立测试代理 | 已验收 | 同一独立验收：预算边界与两日业务回归通过；未跑长线、图形烟测或设备 |
| LITE-03 | 清理缓存与可验证冗余副本，历史证据可定位、可核验；更新流程说明 | 架构：Tools/Testing/Maintain-Evidence.ps1、README.md、Docs/Verification.md、TODOLIST.md、TestResults历史迁移 | LITE-01、02；清理先审查 | 独立测试代理 | 已验收 | 同一独立验收及[清理回执](TestResults/Lightweight/LITE-20261024/maintenance/applied.json)：历史证据完整；本轮编译缓存约19MB因自动审批拒绝删除而保留，不上传 |
| LITE-04 | 有限验收通过后提交推送，远端与本地一致 | 架构：Git提交推送、本清单 | LITE-01至03 | 独立测试代理 | 待验收 | 代码已独立验收；待远端一致性核验，证据TestResults/Lightweight/LITE-20261024/push-verification.json |

## [FRAME] 后续批次填写约定

[FRAME] 在本文件替换“当前批次”为新批次，先保留用户每条反馈到任务ID的映射；补齐目标／范围外、查看场景与必要前置、出包授权、清单写者及上表字段。完成批次移到下方一行历史索引，详细证据继续使用原路径；不另开批次待办文件。

## [FRAME] 历史索引

- [COMPUTED｜HIGH] SYNC-20261024已验收：工程已推送 origin/main；证据见TestResults/RepositorySync/SYNC-20261024/review.txt。

- [COMPUTED｜HIGH] UIEXT-20261024已验收：界面扩展区与设计聊天交接；证据见TestResults/Documentation/UIEXT-20261024/publishing-receipt.json。

- [COMPUTED｜HIGH] REQ-20261024已完成：Notion首版交付清单创建并独立审查／读回通过；证据见TestResults/Documentation/REQ-20261024/publishing-receipt.json。

- [COMPUTED｜HIGH] COOP-20261024已完成：AGENTS／TODOLIST／README入口静态验收通过，架构师、自动化程序与脱机验收策划均确认采用；未改游戏／测试／构建。

- [COMPUTED｜HIGH] 此流程启用前的最近游戏交付为SaveR1：[保存体验修订](Docs/Implementation/SaveExperienceRevision.md)。保留原有证据，不重新验收或出包。


