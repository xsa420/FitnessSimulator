# [FRAME] 唯一批次待办与验收清单

[FRAME] 协作规则见[AGENTS.md](AGENTS.md)。制作建批／收口、架构实施期间单写，禁止并发编辑。代理按任务ID回传摘要；原始证据只存一份并在此链接。

## [FRAME] 当前批次 HOME-20261025：家用提交快捷入口

- [FRAME] 用户反馈：“给我一个可以在我家提交本项目的快捷方式”。目标：克隆后双击提交当前工程，支持可选本机代理。
- [FRAME] 范围：根目录提交入口、Tools/Submit.ps1、README使用说明；不改业务、不运行游戏或构建。当前架构唯一维护清单。
- [FRAME] 验收：临时本地远端验证提交/推送、无改动重推、失败保留、路径含空格及代理参数；通过后将入口同步至现有仓库供家中获取。

| ID | 验收条件／反馈映射 | 实施负责人／唯一文件范围 | 依赖 | 独立验收人 | 状态 | 证据与结论 |
| --- | --- | --- | --- | --- | --- | --- |
| HOME-01 | 双击显示改动、输入说明后提交全部未忽略改动并推送当前分支；相对路径、代理可选、失败不覆盖远端 | 架构：Submit.cmd、Submit-WithProxy.cmd、Tools/Submit.ps1、.gitattributes、README、本清单；验收人独占临时测试与证据 | 已有Git与远端账号权限 | 复用独立测试代理 | 已验收 | [独立验收](TestResults/HomeSubmit/HOME-20261025/review.json)：16项有限检查通过；入口随本批提交发布，未测试真实家用代理或家用账号；出包未请求 |

## [FRAME] 后续批次填写约定

[FRAME] 在本文件替换“当前批次”为新批次，先保留用户每条反馈到任务ID的映射；补齐目标／范围外、查看场景与必要前置、出包授权、清单写者及上表字段。完成批次移到下方一行历史索引，详细证据继续使用原路径；不另开批次待办文件。

## [FRAME] 历史索引

- [COMPUTED｜HIGH] LITE-20261024已验收：轻量测试、历史副本去重及推送完成；证据见TestResults/Lightweight/LITE-20261024/acceptance.json与push-verification.json。

- [COMPUTED｜HIGH] SYNC-20261024已验收：工程已推送 origin/main；证据见TestResults/RepositorySync/SYNC-20261024/review.txt。

- [COMPUTED｜HIGH] UIEXT-20261024已验收：界面扩展区与设计聊天交接；证据见TestResults/Documentation/UIEXT-20261024/publishing-receipt.json。

- [COMPUTED｜HIGH] REQ-20261024已完成：Notion首版交付清单创建并独立审查／读回通过；证据见TestResults/Documentation/REQ-20261024/publishing-receipt.json。

- [COMPUTED｜HIGH] COOP-20261024已完成：AGENTS／TODOLIST／README入口静态验收通过，架构师、自动化程序与脱机验收策划均确认采用；未改游戏／测试／构建。

- [COMPUTED｜HIGH] 此流程启用前的最近游戏交付为SaveR1：[保存体验修订](Docs/Implementation/SaveExperienceRevision.md)。保留原有证据，不重新验收或出包。


