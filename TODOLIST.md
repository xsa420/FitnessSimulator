# [FRAME] 唯一批次待办与验收清单

[FRAME] 协作规则见[AGENTS.md](AGENTS.md)。制作建批／收口、架构实施期间单写，禁止并发编辑。代理按任务ID回传摘要；原始证据只存一份并在此链接。

## [FRAME] 当前批次 SYNC-20261024：工程推送到 GitHub

- [FRAME] 用户原话：“把工程推送到github”。目标：将当前工程提交并推送到已有 origin/main。
- [FRAME] 范围：源码、资源、项目配置、文档与测试源码；排除本地缓存、编译产物、临时构建记录。游戏实现与功能验收不在本批范围，出包未请求。
- [FRAME] 查看：https://github.com/xsa420/FitnessSimulator 。本轮由当前执行者登记批次并负责清单单写。

| ID | 验收条件／反馈映射 | 实施负责人／唯一文件范围 | 依赖 | 独立验收人 | 状态 | 证据与结论 |
| --- | --- | --- | --- | --- | --- | --- |
| SYNC-01 | 工程文件进入现有远端 main；远端与本地提交一致；排除缓存与临时构建文件，保留测试工程 | 架构：.gitignore、本清单、Git 暂存／提交／推送 | 已有 origin 与 SSH 连接 | 独立同步审查代理 | 已验收 | 独立同步审查通过，工程提交已推送且远端与本地一致；证据见 TestResults/RepositorySync/SYNC-20261024/review.txt。源码与4个测试工程保留，临时编译产物排除；不代表游戏功能验收 |

## [FRAME] 后续批次填写约定

[FRAME] 在本文件替换“当前批次”为新批次，先保留用户每条反馈到任务ID的映射；补齐目标／范围外、查看场景与必要前置、出包授权、清单写者及上表字段。完成批次移到下方一行历史索引，详细证据继续使用原路径；不另开批次待办文件。

## [FRAME] 历史索引

- [COMPUTED｜HIGH] UIEXT-20261024已验收：界面扩展区与设计聊天交接；证据见TestResults/Documentation/UIEXT-20261024/publishing-receipt.json。

- [COMPUTED｜HIGH] REQ-20261024已完成：Notion首版交付清单创建并独立审查／读回通过；证据见TestResults/Documentation/REQ-20261024/publishing-receipt.json。

- [COMPUTED｜HIGH] COOP-20261024已完成：AGENTS／TODOLIST／README入口静态验收通过，架构师、自动化程序与脱机验收策划均确认采用；未改游戏／测试／构建。

- [COMPUTED｜HIGH] 此流程启用前的最近游戏交付为SaveR1：[保存体验修订](Docs/Implementation/SaveExperienceRevision.md)。保留原有证据，不重新验收或出包。


