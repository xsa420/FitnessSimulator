# [FRAME] 文本界面、自动化验证与安卓交付方案

> [FRAME｜正式职责移交｜2026-10-23] 本稿继续由表现模块单写，维护UI／文本／布局／触摸、图形截图、Bootstrap、Editor/PlayerBuild及已有表现测试。整局无画面运行器、自动路线、回放与恢复对照、批种子及停滞报告已移交 `/root/automation_engineering`，其独占 `Editor/Automation`、`Tests/Production/Automation`、`Tools/Automation` 与新增OfflineAutomation技术／实施稿。旧§6–8及其他涉及自动整局的段落仅保留为新所有者的只读历史输入，本稿不再维护第二套自动实现方案。职责冲突以本声明及新所有者正式方案为准；历史证据不搬移、不删除。共享文件与串行编辑器验证由架构师统筹，手机手感验证后置，不要求用户人工流程测试作为无人闭环前置。

[COMPUTED｜HIGH] 已有可复用入口为 `IGameFrontend.ReadView/Execute/Advance/SetSuspended` 与 `GameSession.OpenTest`。本模块 `GameUiSmoke.Run` 是有图形的实际uGUI烟测，保持归属表现模块；它使用测试会话、公开操作和可见按钮／滚动／指针事件，截图包含默认及大字号、卧推、漏训、结果及恢复流程。它不是整局无画面运行器，不能冒充真实多指、生命周期或全部结局覆盖。`GamePlayerBuild`负责正式场景、内容检查、构建和隔离恢复；既有指针、中文字形、载荷测试继续归属表现模块。下文“实施前方案”是历史状态，后续实施证据见 `Docs/Implementation/PresentationM1.md`。

[FRAME｜方案状态] 2026-10-23，实施前方案，本轮总审接受；唯一写入文件为本文件。没有修改业务代码、配置、场景或预制体，没有运行 setup、构建或设备测试。文中的端口、目录和测试命令是实施任务，不是现有能力或通过记录。

[COMPUTED｜HIGH] 本轮读取在线主文档 `2bc3e8eb2dfd80bcb359f603fc989244`、`Docs/ProgramModulesAndDependencies.md`、`Docs/Design/ProductionAlignment.md`、README、Architecture、Verification，以及 ResourceLab、ProjectSetup、OfflineAndroidManifest、TrainingContracts、SaveContracts、LocalJsonStore、FoundationTests、三个 asmdef、Tools 两个脚本及包清单。需求权威是在线主文档；旧 Architecture 的整动作回退、历史成绩复用及旧人员分工不进入本方案。

## [FRAME] 1．边界、状态所有权与依赖

[FRAME｜既定] 首版为安卓横屏、纯离线、文本完整可玩；8独立小游戏、21区、3模板、4自主人物、双比赛、三成功方向与失败终局不减范围。复杂3D不在交付门槛内。UI不得拥有第二套规则、随机、游戏时钟、保存或结局检查。

| 所有者 | 唯一写权限 | 本模块依赖／解耦 |
|---|---|---|
| [FRAME] 界面 | 草稿、焦点、滚动、临时选中、文本显现和反馈播放游标 | 只消费公开视图；销毁界面不改变领域状态 |
| [FRAME] 表现偏好服务 | 已读内容版本、教程和知识卡偏好、音量偏好语义 | 通过提交器白名单辅助提交；有限开关进core preferences，无界已读集合进永久追加日志并受根prefix约束，不附带未完成业务事务；跨局保留 |
| [FRAME] 会话／提交 | 流程实例、合法操作、状态版本、命令结果与提交顺序 | 使用 FlowPersistenceContracts 的公共封套，不在本文重新定义提交协议 |
| [FRAME] 各领域 | 身体、计划、训练、经济、人物、比赛、结局各自字段 | 各自投影公开字段、成本／风险、禁用原因；UI不反推隐藏资格 |
| [FRAME] 存档／记录 | 检查点、有效玩家记录、诊断链、永久历史的持久化 | UI只请求读取和操作；不直接调用 LocalJsonStore 改领域槽 |
| [FRAME] 工程验证 | 场景清单、断言、测试夹具、证据和构建报告 | 测试使用同一公共操作端口；测试注入需专用权限，不能成为生产人物策略 |

[FRAME｜程序技术决定] 依赖方向为触摸／脚本适配器→应用命令与只读查询端口→统一执行器→领域；领域不引用页面、音频或测试程序集。页面可以缺席，规则仍执行；反馈消费者失败不撤销已提交步骤。共享端口由公共协议主责维护，领域公开字段由该领域负责。

## [FRAME] 2．具体视图数据及操作契约

[FRAME｜程序技术决定] 以下为消费者需要的数据语义，类型名待公共协议整合。ID使用稳定业务ID，不能拿中文名称、按钮序号或场景索引作身份。所有集合是不可变快照；不返回内部对象引用。

| 数据 | 字段及语义 |
|---|---|
| [FRAME] ViewEnvelope | viewSchemaVersion；runId；committedRevision；eventInstanceId；screenKind；disclosureStage；availableActions；recoverability；读取时点。跨多个领域的一页必须来自同一已提交版本 |
| [FRAME] ActionDescriptor | actionId、目标实例ID、enabled、disabledReasonCode、参数范围、confirmationRequirement；禁用解释只含可公开原因。领域必须再次校验，按钮灰掉不是权限控制 |
| [FRAME] ChoicePreview | choiceId、textKey、确定成本列表(amount/unit)、余额／库存足额、定性收益标签、定性风险标签；仅表白的 probability 字段可含精确值，其他概率字段根本不序列化 |
| [FRAME] BodyView | 21项 regionId/name/strength/localFatigue/temporaryFatigue/injuryLabel/remainingGameDays；bodyDescriptionKey、fatPercent、bodyPressure；力量和两类疲劳单位明确，不把局部疲劳冒称临时疲劳 |
| [FRAME] TrainingView | workout/action/set实例；动作名、当前／挑战梯度、重量单位、组序／总组、目标／有效次数、自动资格及禁用原因、实际游戏耗时、超时标记、休息选项；目标肌群T列表和maxTRegionId |
| [FRAME] PlanView | activeVersion/effectiveDate、pendingVersion/effectiveDate、模式、锚点、动作有序ID及训练档；Draft是UI临时值，保存成功后才出现新的pending版本 |
| [FRAME] NarrativeView | contentId、contentRevision、segmentId、完整正文、已读标记、是否允许跳过、下一边界类型；未选分支正文不随视图传输 |
| [FRAME] DaySummaryView | current、dayNetDelta、dayEndContribution、hadAnyChange、trainedRegions；贡献是子集说明，不与全天净值相加；hadAnyChange=true且净值0也显示 |
| [FRAME] PublicRecordView | run/day/commit、已见文本、已选选项、实际变化、公开来源；未选分支、随机阈值和作废步骤仅在诊断中，不进玩家记录 |
| [FRAME] FeedbackItem | commitId、effectId、序号、sourceLabel、目标名、before/after/delta、unit、textKey、severity；只描述已提交事实，不能承载待执行效果 |
| [FRAME] KnowledgeCardDefinition | cardId、revision、正文、sourceTitle/sourceUrl、核实日期、reviewStatus、触发标签；未核实卡不能发布，已阅卡不改变动作K |
| [FRAME] PresentationPreferences | schemaVersion、readContentRevisionSet、tutorialStageSeen、tutorialEnabled、cardsEnabled、mutedCardIds、musicVolume/effectsVolume；偏好保存失败明确提示未保存，不伪称业务丢失 |

| 端口族 | 输入／结果 | 边界 |
|---|---|---|
| [FRAME] QueryView / QueryRecords / QueryHistory | 当前上下文、页游标→同版本只读投影或NotAvailable | 不推进时间、不随机、不补发奖励；查询记录无搜索筛选首版范围 |
| [FRAME] SubmitAction | 公共封套含operationId、expectedRevision、来源、run/actor/event/目标实例、命令及载荷→公共回执 | 选项确认、购买、餐食、计划保存、开组、休息、结束训练、进入次日、备注等走同一入口 |
| [FRAME] AdvanceText / CompletePresentation | 当前段实例和内容版本、operationId→下一段或事件完成回执 | 补全文字是本地表现；推进是有效操作。演出完成按执行器契约产生业务完成，不由反馈队列代发 |
| [FRAME] MinigameInput | set实例、玩法schema、inputSequence、输入类型／触点／相对时间→玩法反馈 | 训练模块解释输入、计算结果；UI不得直接设置完成量。真实输入和脚本输入接同一规则核 |
| [FRAME] SavePreferences | 偏好版本、patch→偏好回执 | 根revision随辅助提交递增，但不推进当前业务节点、随机或结局；是否读过仅对完整显示且推进的段生效 |
| [FRAME] RunScenario（测试） | scenarioId、fixtureHash、seed、commands、assertions→证据清单 | 只在独立测试环境运行；生产人物不能调用InjectTestResult |

[FRAME｜协议已对齐，本轮总审接受] 直接引用 `FlowPersistenceContracts.md` §2—4：CommandResult为Committed/AlreadyCommitted/Rejected/ConfigError/StorageUnavailable/StorageOutcomeUnknown；WaitingInput是PreparedOutcome完成类型，不能误当提交失败。QueryView使用snapshotRevision/permittedActions/disabledReason/disclosureStage/pendingResolution；本文ViewEnvelope的committedRevision即snapshotRevision，不产生第二个时钟。choiceToken绑定候选与版本；同operationId同payloadHash返回原Receipt，异载荷OperationConflict。StorageOutcomeUnknown冻结操作，先核对磁盘Receipt，不盲目重发新ID。旧报价过期仅刷新，不静默再确认。业务失败可以Committed，机会消耗由领域事实定义。

[FRAME｜协议已对齐] currentRun/permanent/preferences/receipts由提交器同一逻辑根一致提交；物理采用核心＋追加日志的已提交前缀，按流程v0.2规模修订，不要求全历史内嵌根，也没有表现独立JSON。辅助提交使根revision递增，训练结果仍按冻结组前输入hash/领域版本验证，不能因为读偏好或保存计划就丢掉真实完成结果。文本推进／演出完成与反馈播放ACK分离；只持久化确认发布反馈。当前最新已提交档损坏停载，不静默回退旧槽。

[FRAME｜公共v0.2最终同步] PreparedOutcome拆分outcomeKind（Success/BusinessFailure/WaitingInput/ControlOnly）、eventDisposition（KeepOpen/CompleteNode/AuxiliaryOnly）、checkKind（None/NormalCompletion/EndingPresentation）、opportunityPolicy（Keep/Consume及原因）。上一段WaitingInput具体属于outcomeKind。界面和测试不得从BusinessFailure推断节点已结束、机会已消费或应扫描结局；测试必须分别断言四个维度。教练不足与无效道具的区别按领域已批准组合消费，非法枚举组合报协议错误。

[FRAME｜训练协议已接入] 已读 `TrainingGrowthContracts.md` v2数据、命令、状态与恢复章节；输入使用RequestId/SetId/RulesVersion/InputSchemaVersion、递增InputSeq、activeTicks（系统暂停剔除）、pointerId、down/move/up/cancel和归一坐标／按钮ID；判定器输出effectiveReps=v、N、Success/Failure、attempts/failedAttempts、reason/evidence。部分完成由0<v<N派生，成功v=N；第三无效终止为当前策划候选。PlayerTouch/ScriptReplay/NpcPolicy/AutoCapacity/TestInjected分开，自动全N由训练服务生成。ResultReady已保存结果只进入结算；若该子事件触发结局则先演出，成功继续接Settle、终止无Settle权，不能用“处理中”绕过结局或再次Begin。

## [FRAME] 3．页面组织与呈现状态机

[FRAME｜程序技术决定] 正式单入口装配常驻顶栏（日期／锚点），中央事件区域，底部当天安排／角色／计划等辅助入口。使用现有界面依赖做响应式文本和列表；不复用实验场固定1600×900像素布局作为手机布局标准。安全区与系统返回键统一路由，返回键不得绕过必选项或组内限制。

| 页面／阶段 | 必备信息与操作 | 限制／退出 |
|---|---|---|
| [FRAME] 主菜单与新局 | 当前局状态、继续、新局覆盖提示、历史、偏好；三模板资格→预设／自定义计划→确认开局 | 终止局无继续；新局不清历史／永久资格；未提交草稿不创建半局 |
| [FRAME] 普通事件／选择 | 正文、先点选后确认、确定成本与定性风险；唯一合法选项按执行器直接执行 | 无取消／跳过必选项；未抽取事件不预告 |
| [FRAME] 当天安排／角色 | 当前与剩余锚点、有效训练；21区数值、两类疲劳、伤病与体型文字 | 无人物完整生活观察页；只展示已披露信息 |
| [FRAME] 计划编辑 | 当前／待生效明确日期；日／次视角；上下移动按钮；保存、放弃、继续编辑 | 修改次日生效；放弃不撤销此前已保存版本 |
| [FRAME] 组前／休息 | 操作说明、梯度、自动资格、目标次数、T最高肌群、休息、整次结束 | 禁止临场重排或任意跳动作；自动仅合资格容量组 |
| [FRAME] 小游戏 | 8种实际玩法各自输入区、目标／有效量、即时操作反馈、必要基础图形 | 本组期间辅助页面／主动结束训练不可达；系统退后台不是结束训练命令 |
| [FRAME] 已存结果待结算 | 上一组结果摘要、处理中或保存错误重试 | 不重玩、不领取按钮、不再次发结果；已有组保留 |
| [FRAME] 训练汇总 | 总摘要→按动作展开组记录；费用／成长／疲劳／伤病和来源 | 只查询历史提交，不再次计算或发放 |
| [FRAME] 三餐／商店／人物 | 热量／价格／余额／库存、整包购买；相遇与互动可用性、次日搭子标记、表白概率 | 确认时重校验；隐藏人物属性和评委按领域阶段投影 |
| [FRAME] 比赛 | 两赛倒计时、开放入口、已披露评委、比赛后三分项与平均／名次 | 读档不重抽；界面不能自行计算冠军或泄露未公开信息 |
| [FRAME] 日结 | 当前值、全天净变、日结贡献、零净值但发生变化项、已练肌群 | 完成日结后等点击进入次日，不用动画结束自动跨天 |
| [FRAME] 结局／记录／历史 | 结局文本、一次备注或留空确认、返回菜单、成功继续；目录→达成→只读快照 | 演出恢复与正常后继分开；空备注不等于未提交；历史不跟当前角色刷新 |
| [FRAME] 记录／教学／设置 | 按日浏览有效记录、阶段教程复看、知识卡目录、音乐和音效开关 | 无回退重选；知识卡阅读不奖励K；组内不开辅助页 |

[FRAME｜程序技术决定] 页面主状态为 Boot→Loading→Ready(只读快照/可选意图)→Submitting→Ready或AwaitingInput；保存失败进入CommitError并保留原操作重试；配置或损坏错误进入RecoveryError，只提供协议允许的恢复／菜单入口。MinigameRunning、ResultPendingSettlement、EndingPresentation、NotePending、DayEndAwaitAdvance均是流程状态的投影，UI不能自行转移为下一业务状态。

[FRAME｜后台候选已对齐] 系统失焦同进程：训练核Suspend保存内存中完整阶段、题目、有效量、错误数、activeTicks和逻辑触点；输入适配器递增inputEpoch、清系统pointer映射，系统cancel不作为玩家up。恢复进入RebindOverlay，按逻辑槽显示重建目标（不泄露题目答案），触点重建期间不推进计时或判定，完成后准备0.35秒再Resume。下压按住态重绑保持槽；侧平举左右槽固定身份、拒绝同一指针占两槽；下拉/肩推在原逻辑位置重新落指，不能瞬移穿障；深蹲记忆阶段保留剩余时间，答题阶段不重新展示序列。不是玩家可点的暂停入口。

[INFERRED｜MED] 该方案在接口层可用“玩法核显式SuspendSnapshot＋物理触点与逻辑槽分离”实现；可行性尚需八玩法原型证明，不能以接口设计代替真机恢复测试。[FRAME] 进程死亡无内存快照，未锁结果回PreSet；ResultReady接结算。Resume发现状态不完整则暴露错误并采用权威退出恢复协议，不猜阶段或默认成功。E02/E08对每玩法在按住、移动、记忆、作答、第三无效、结果回调交界分别测存活/死亡/已锁三类，记录inputEpoch和恢复前后状态摘要。

[FRAME｜既定] 文本第一击补全，第二击推进；已读跳过只处理已读文字，在未读、选项、小游戏停。没有自动播放。跳过不跳业务结算、不修改游戏耗时；逐段确认沿同一命令通路，旧段重复确认幂等。未完整显示或未确认的段不因一次“跳过全部”标成已读。

[FRAME｜策划候选已对齐] 已读以(contentId,textRevision)跨局保留，完整显示并推进才标；改版本重新未读。教学进度跨局，新局可重开；知识卡总开关/单条静音、音乐/音效偏好跨局，关闭卡片不隐藏主动目录。结局按已完成文本子事件游标恢复；备注草稿自动白名单保存，最终Pending/CommittedEmpty/CommittedText三态，后二者不可修改。确认休息独立提交，随后0额外时间对话中断不撤销休息。

[FRAME｜候选接入] 正式页面不增常驻商店入口；商店由事件进入。一般数值一位小数但风险按原始值判定，100.01显示“略高于100”或足够精度，非零小变化不写+0.0。所有真实知识卡正文与来源离线内置，不要求联网才能阅读；基础音频按文本稿两段背景及六类音效内容清单验收，受伤可复用结果类音色。

[FRAME｜领域候选接入] 表白由生活域产pShown=原p十进制四舍五入至0.001，显示0.1%精度，实际使用u<pShown；UI不保留另一套高精度阈值。E07验显示阈值一致。比赛GetContestView按阶段披露，单评委4位、平均2位可展开比较精度；以赛事域ScoreUnits与次级键判名次，不从2位显示均分反算。字段／算法唯一来源分别为LifeActorsContracts、PlanningCompetitionContracts，不由表现实现计算。

[FRAME｜程序技术决定] 数值反馈队列仅订阅持久化成功通知，以(commitId,effectId)去重，支持跨事件播放；页面始终显示最新已提交数值。队列积压时允许合并视觉摘要，但完整变化保留有效记录；断开重连从提交序号补通知或重建摘要，不重发业务。故障／重启不要求重播全部飘字，记录不丢才是验收重点。操作反馈与业务结果反馈分开，按下音效不能伪装购买成功。

[FRAME｜既定] 临时疲劳显示100危险分界、150必伤上限以及最高目标肌群名称；数值等于100不能显示“已超过100”，150显示必伤而非普通风险。可展开每个目标肌群；不能把最高值作为所有肌群判伤输入。显示上限不授权领域截断原始状态。局部疲劳单列，精确受伤率不出现在视图。

[FRAME｜程序技术决定] 基础音频事件表至少含菜单确认、业务拒绝、组结果、受伤、比赛／结局；背景音乐按日常与比赛上下文切换。通过内容ID与资源许可清单接入；缺失音频上报内容校验，播放失败不能阻塞业务。知识卡未核实来源列为发布阻塞，不用游戏伤病数值生成现实建议；本方案不撰写现实训练知识。

## [FRAME] 4．保存、恢复与错误用例

| 切断位置 | 恢复视图与允许操作 | 必须断言 |
|---|---|---|
| [FRAME] 普通事件未完成 | 从当前事件头恢复；此前已完成事件保留 | 未完成成本／效果同回退，开始统计按公共实例协议不重复 |
| [FRAME] 第n组操作中 | 恢复未完成组起点，之前组保留 | 无未完成收益；不能回退整个动作 |
| [FRAME] 结果已保存、结算未提交 | PendingSettlement，仅恢复结算 | 原结果内容与来源不变；不能再次操作或随机重算结果 |
| [FRAME] 组结算提交后、反馈前 | 显示已提交结果及后继 | 金钱／力量／伤病／随机／耗时只发生一次；未播反馈不妨碍完成 |
| [FRAME] 支付候选／文件替换／确认返回各处 | 成本效果全有或全无，回执可查询 | 不出现扣费无物或有物无扣费；同命令重试不多占机会 |
| [FRAME] 日结中途／完成待点击 | 未完成步骤继续；完成后停等待页 | 已做收入／清理／人物推进不重复，不能跳过剩余步骤 |
| [FRAME] 结局触发／演出／备注提交 | 先恢复演出游标，再记录页；继续恢复正常后继 | 历史／解锁只一条；空备注已提交不可补写；失败无继续 |
| [FRAME] 计划草稿／保存 | 未保存草稿丢弃依产品偏好处理；已保存pending仍在 | 计划保存不偷渡未完成事件；当日active不变 |
| [FRAME] 偏好保存／反馈播放 | 读取最近已保存偏好，重建已提交状态 | 不改变角色数值、随机位置或游戏时间 |
| [FRAME] 缺档／损坏／版本不支持／磁盘满 | 分开错误码，展示可重试或返回菜单，覆盖须明确新局意图 | 不静默创建新局或伪造成功；不将损坏当正常“无存档” |

[FRAME｜程序技术决定] 错误报告关联operationId、checkpointId、commitId、配置ID/字段和恢复点；玩家只显示简明原因与可用操作，内部堆栈只入诊断。测试对每个持久化阶段进行中断，并对恢复后的权威状态哈希、有效记录、历史条数、随机位置和下一合法操作一起比较；不能仅验证文件存在。

## [FRAME] 5．现工程隔离与目录／程序集实施任务

| 核查事实 | 拟实施工作与验收 |
|---|---|
| [COMPUTED｜HIGH] ResourceLab位于Runtime/Presentation，与存储和训练接口共用FitnessSimulator.Runtime；启动读取Resources/ExerciseCatalog，OnGUI显示英文实验场，SmokeRun验证动画位移与探针存储 | [FRAME] 将实验场运行与资源定义迁入独立Labs目录／程序集，原GUID随迁移保留；正式GameUI只依赖应用端口。实验场写DevelopmentProbe，测试使用独立数据根，正式存档不能共用 |
| [COMPUTED｜HIGH] ProjectSetup.Configure重建Generated和Bootstrap；Build方法显式scenes={Bootstrap}，不是单纯读取EditorBuildSettings | [FRAME] 拆出LabSetup与正式PlayerBuild入口，正式启动场景GameBootstrap另建；正式构建明确场景白名单，不调用生成器。禁止以改BuildSettings替代修复硬编码构建路径 |
| [COMPUTED｜HIGH] 实验目录由Resources.Load加载，且生成资产被目录引用 | [FRAME] 将实验资源移出正式Resources根或使用独立实验内容构建；报告依赖闭包，正式包不得含实验场及测试夹具。不能只排除场景却让Resources把模型打入包 |
| [COMPUTED｜HIGH] 现有三个asmdef分别为Runtime、Editor、EditModeTests；测试仅Editor平台 | [FRAME] 最小拆分Contracts、Domain/Application、Persistence、GamePresentation、PlayerBootstrap、Labs、Tests.EditMode、Tests.PlayMode、Tests.Harness；只增加需要的边界，不按页面拆程序集。Editor.Build与Editor.Labs分离，后者只编辑器／实验构建 |
| [COMPUTED｜HIGH] Tools/Unity.ps1 test仅跑EditMode；smoke启动Windows实验程序并验8张差异图；test判passed≥1 | [FRAME] 新增明确的validate-content、test-rules、test-flow、test-input、build-player-android与device-report任务契约；校验预期测试清单ID和缺失／跳过数，不允许只跑到一条就绿。旧smoke改名标注lab-smoke |
| [COMPUTED｜HIGH] Environment.ps1总是要求WindowsSupport；BuildAndroid输出固定APK，现脚本已有报告新鲜度、ARM64和INTERNET检查 | [FRAME] 按目标平台检查依赖，安卓交付不要求桌面发行能力。保留现检查，加产品入口、资源／程序集排除、签名／包ID／版本／APK哈希和配置manifest；产物目录按buildId隔离 |
| [COMPUTED｜HIGH] 四个FoundationTests验证局槽/历史分离、存储字段、路径穿越拒绝及8展示资源 | [FRAME] 存储基础回归保留；展示资源测试移入Labs，不能计入AC04。新增领域边界、流程恢复和真实玩法输入测试后才能谈AC验收 |
| [COMPUTED｜HIGH] TrainingContracts有historicalPerformanceSourceId与consecutiveStrengthSets，当前SaveContracts只是若干字段外壳 | [FRAME] 新正式协议由训练与存档主责迁移；测试专门拒绝旧历史成绩复用和旧连续失败乘数渗入新规则，不由UI兼容层偷偷保留 |

[FRAME｜程序技术决定] 正式程序集不能引用Tests/Harness/Labs；仅测试程序集注册结果注入端口与故障点。构建预检验证引用图、编译define、场景／资源依赖和路由注册表；正式包试发测试命令必须返回Unknown/Forbidden且无状态变化。仅用DevelopmentBuild布尔开关隐藏按钮不构成隔离。

## [FRAME] 6．验证层次、输入来源及证据结构

| 层 | 输入与目的 | 不得据此声称 |
|---|---|---|
| [FRAME] L0 配置静态验证 | 引用、ID、默认兜底、条件、合法控制上下文、比例、时间单位、三评委可抽人数、8游戏绑定、文本／来源／音频引用 | 规则能运行或体验合格 |
| [FRAME] L1 纯规则／领域测试 | 策划手算fixture、边界枚举、固定随机输入；断言输入输出而非复制生产公式作oracle | 真机触摸可用 |
| [FRAME] L2 无画面流程 | 同一命令端口从新局到结局，多日／多人物、恢复、注入边界 | 结果注入路径可证明8小游戏实际操作 |
| [FRAME] L3 玩法／界面集成 | 八玩法输入事件回放和实际规则核；文本／禁用／选择确认、UI与脚本等价回执 | 设备触摸、字号、性能或人类玩法难度已达标 |
| [FRAME] L4 安卓真机 | 安装包真实触摸、离线、前后台／进程中止、音频、可读性、性能、全路径 | 只凭一台设备结果覆盖未测试机型 |
| [FRAME] L5 体验／平衡 | 多策略样本和真实游玩计时，5—10分钟分段、首次主要结局2—4小时目标 | 用加速脚本时长代替真实体验时长 |

[FRAME｜程序技术决定] 来源分开记录：PlayerTouch、ScriptAction、ScriptMinigameInput、TestInjectedResult、NpcProductionResolver、FixtureSetup。公共source唯一权限枚举为Player/NpcProduction/Test；上述名称只是证据细分，不另定义权限枚举。PlayerTouch映射Player，NpcProductionResolver映射NpcProduction；ScriptAction、ScriptMinigameInput、TestInjectedResult、FixtureSetup均映射Test。能力由端口发放，调用者不能只改字符串冒充。脚本普通操作与触摸普通操作调用相同SubmitAction；脚本玩法输入与触摸进入同一玩法核；注入仅替代测试结果供应，不替代校验／结算规则。所有后代提交继承测试污染标记，防止混合路径结尾被标成纯真实输入。

[FRAME｜程序技术决定] 测试数据清单至少包含：完整合法最小闭环；8动作每个成／败／部分／零量轨迹；21区分配与T=100、略大于100、150；知识门槛两侧；三模板锁／解锁；两独立店及同ID共享；四人物隔离；两赛事三评委名单；三成功／两失败路线；故障点与非法配置。未定候选参数通过版本化fixture引用，缺值即Blocked，不填0、不自动成功。

[FRAME｜程序技术决定] 每份EvidenceRecord固定字段：schemaVersion、evidenceId、scenarioId/caseId、S/AC列表、specRevision、buildId、sourceCommit及dirtyDiffHash、configHash、fixtureHash、seed/randomStreamBeforeAfter、deviceId/system/分辨率/刷新率、inputSource/injectionReason/injectionPayloadHash、前置checkpoint、逐命令operationId与目标实例、expected、actual、assertionResult、commitIds/stateHashBeforeAfter、elapsed、日志／视频／截图路径及hash、执行时间（UTC及展示时区）、执行者、status、缺失证据理由。status使用NotRun/Blocked/Failed/Passed，未经执行不能写Passed。

[FRAME｜程序技术决定] 运行清单保存预期caseId全集；任何少跑、跳过、超时、断言无执行、文件旧于本次、证据哈希不符都不能通过该门槛。随机样本记录种子集合与样本数，概率分布测试的容差事先登记；边界100／150应做确定性断言，不靠统计碰到。失败保留可重放命令序列和最小前置，不以重跑成功覆盖首次失败。

## [FRAME] 7．全S／AC可执行覆盖表

[FRAME] 下表每行均为待执行；“前置→操作→断言”是测试设计，实际结果留空。每项套用上一节证据字段；涉及UI均需L3，涉及真实操作和手机体验均需L4。领域主责提供独立手算预期，工程负责执行与证据完整性。

| S／AC | 层；前置数据 | 操作与断言／专项证据 |
|---|---|---|
| [FRAME] S01／AC01 | L0/L4；冻结目标设备、签名包、新安装和已有存档 | 飞行模式安装→新局→真实触摸→退出续玩→历史；无需电脑或网络运行；记录系统、包hash、启动/保存/帧时/内存；ADB仅采证不代玩 |
| [FRAME] S02／AC02 | L1/L2/L4；3模板、锁条件、带旧金钱/物品的永久资格 | 默认开局→解锁→新局；只继承资格；各模板长短板按策划值；计划首次当天、修改次日、同日最后版、草稿放弃不撤旧保存 |
| [FRAME] S03／AC03 | L0/L1/L2；次数耗尽池、占用餐点、超时、日结多步骤 | 解析入口一次、无限兜底、冷却第3日触发间隔2则第6恢复、跳过明确占用但不连带夹中锚点、漏训不补；中断日结重进只做剩余且等点击 |
| [FRAME] S04／AC04 | L1/L2/L3/L4；8真实玩法轨迹、力量门槛上下、最高梯度、自动资格 | 每玩法实触成败/部分；挑战始终手动；自动当前目标；梯度内减难和最高梯度限制；提交当前真实结果不复用历史；八份输入视频+结果来源 |
| [FRAME] S04/S05／AC05 | L1/L2；全/部分/零量，非均匀肌群比例，K门槛两侧 | 逐组手算力量/熟练度；分配总量不复制不二次均分；零量无成长；本组新K不追溯；汇总重复打开不发收益 |
| [FRAME] S05／AC06 | L1/L2/L4；短/长计划、力量失败、100/150、多个目标、到期伤病 | 无失败恢复全容量基准80；零量失败额外罚仍有；先判伤后休息；150搭子不能免；无关肌群不抽；伤病挡相关动作、日结不提前治愈 |
| [FRAME] S05/S06／AC07 | L1/L2/L4；三餐足额/不足/占用、富裕值变动、有效训练列表 | 支付即食一致；摄入不足两类恢复/压力；收入当前富裕值一次；晚学习限定动作、全天保留餐食、道具全动作；失败力量≥1才给K |
| [FRAME] S07／AC08 | L0/L1/L2/L4；两ShopId、同ID两事件、未访刷新、整包库存/余额边界 | 购买/刷新/解锁后刷新/无效使用；跨店独立同店共享，新解锁不偷入旧库存；每支付阶段切断成本效果同生灭 |
| [FRAME] S08／AC09 | L1/L2/L4；在场计划、互动已用/未用、付费不足/足额/满好感 | 一次休息互动；教导不足不占、免费仍占；固定好感不二次放大；搭子替换次日；按实际练过动作一次且不追溯；150必伤 |
| [FRAME] S09／AC10 | L1/L2/L4；四正式策略、各私有钱包/物品/计划、跨日中断 | 同一世界日逐步推进四人并存档恢复；实际选择/成长诊断可追；玩家机会不变；公开页仅按阶段披露，无预写增长表冒充 |
| [FRAME] S10／AC11 | L0/L1/L2/L4；双赛日程、合法评委池、实际成长竞争者 | 指定日入口/倒计时、分期披露、冻结后重进3唯一评委不变；独立算例核单项及平均、两赛方向、体脂单计、实际对手冠军判定和平分策略 |
| [FRAME] S11／AC12 | L1/L2/L4；冠军双路线、满好感赠礼特殊事件、失败/漏赛 | 三成功真实到达；表白显示概率等于判定输入；成功继续、恋爱仍参赛；失败和未履约封局；历史一次，演出不嵌套结局 |
| [FRAME] S12／AC13 | L3/L4；长中文/长列表、全部页面、已读/未读/选择、无3D包 | 全流程可操作，21区/体型/T阈值清晰；跳过边界、组内禁辅助含返回键；确定成本、定性风险、音频、知识卡；反馈阻塞人为延长时提交仍完成 |
| [FRAME] S13／AC14 | L1/L2/L4；第4节全部断点、已完成组/待结算、历史/永久资格 | 每断点重启并比较权威状态/随机/记录/后继；计划独立保存；一局覆盖不动历史；备注空与未提交分开、已提交不可补；演出恢复后正常后继正确 |
| [FRAME] S14／AC15 | L0—L4；合法/非法配置、全case清单、测试与正式包 | 错误定位配置字段；脚本触摸同规则；注入来源全链；正式无注入入口；结构化报告完整且无历史产物混入；真实输入证据单独齐全 |

## [FRAME] 8．八条端到端执行包

[FRAME] 每包分别跑无画面规则链与真实触摸链；测试注入链只能支撑被标明的规则断言。完整通关路线使用正式配置和正常命令，fixture快速到达晚期状态不能证明从新局可达。

| 包 | 前置／操作 | 主要断言与证据 |
|---|---|---|
| [FRAME] E01 开局计划与随机漏训 | 新安装默认角色→首计划→当天训练或合法漏训→保存改计划→日结→次日 | AC01/02/03/13/14；主动／待生效版本、锚点序列和点击日志 |
| [FRAME] E02 八动作训练矩阵 | 每动作合法梯度→真实成/败/部分/零→挑战→自动→汇总 | AC04/05/13；8独立输入记录、实际量、自动资格、逐组和汇总差值 |
| [FRAME] E03 疲劳伤病与恢复 | 短长配额计划→力量失败→100/150→休息/换无关动作→跨日 | AC06/09/14；手算边界、随机记录、相关动作禁用与伤病到期 |
| [FRAME] E04 餐食学习商店 | 三餐→两店买整包→物品使用→未访问刷新→晚/全天学习→日结 | AC03/07/08；扣款库存摄入一致，餐点未被普通全天学习隐含消耗 |
| [FRAME] E05 四人物与教练 | 多日相遇→一次互动→不足/免费教导→换搭子→四人自主推进中断 | AC09/10/14；私人资源、教导时点、机会标记、每人推进证据 |
| [FRAME] E06 两赛胜负与漏赛 | 从新局分别走双冠军、非冠军、未履约→历史 | AC11/12/13；评委冻结、平均分独立核验、对手成长、失败不能继续 |
| [FRAME] E07 恋爱与成功续玩 | 满好感赠礼→表白成功/失败→成功演出→一次备注→继续→仍参赛 | AC09/12/14；概率显示、同存档同操作稳定、演出/备注/后继断点 |
| [FRAME] E08 交付耐久与错误 | 长中文/全部辅助页→长日程→每提交故障点→离线重启→版本/损坏样本→正式包测试路由探测 | AC01/03/13/14/15；内存/帧时/存档/日志、错误操作、无注入能力及无静默重开 |

## [FRAME] 9．安卓候选基线与测量协议

[FRAME｜候选基线，非通过] 目标设备不能仅写“中高端”。M1由制作人／工程登记两机目标矩阵和实际可用主验机，记录主验机厂商型号、SoC、内存、系统完整版本、分辨率／物理尺寸／系统缩放／刷新率；首切片先在主验机验证。第二台目标覆盖不同屏幕比例／系统版本，M5前补齐实际设备信息并冻结矩阵，M6双机以同一交付版本复验。当前未登记具体机型，不虚构设备。现工程API26和ARM64是构建设置，不是承诺所有API26设备体验合格。

[FRAME｜候选基线，与文本稿§6统一] 安全区640×320、800×360、960×540，至少两种比例，左右横屏安全区分别测。正文18／次要16／标题22逻辑字号，1.0/1.25缩放，行高1.4；普通触区≥48×48、间距8、主操作高52，小游戏避边缘24。输入样本为1200汉字正文、30字选项、21区、200条单日记录；正文无横滚、成本不裁切，滚动不触发确认。逻辑单位转换与实际密度需工程校准，不冒称平台标准。

[FRAME｜候选基线，与文本稿§6统一] 触摸可见反馈p95≤100ms，普通页可交互p95≤300ms，保存p95≤500ms且超500ms显示处理中；冷启动到菜单≤5s；小游戏目标60fps、帧时p95≤33.3ms且无操作期>100ms冻结；进程PSS峰值≤600MB（本方案明确十进制600,000,000字节口径），连续60分钟无崩溃／输入锁死／随日数无限增长。替换本稿初版更严的未协商预算，保留最大值、p99及趋势作诊断而非偷偷增加门槛。这些不是设备实测或产品既定值；M1校准、M5锁版本，不能测后偷偷放宽。

[FRAME｜程序技术决定，双方采样已对齐] 性能记录设备电量、温度、系统电源模式、刷新率和包类型；冷启动至少10次，安装耗时单列；每机连续60分钟混合文本/8小游戏/日结/记录滚动，每个小游戏至少连续3分钟，逐帧记录，每10分钟采内存趋势；触摸／页面每类至少30次，每档规模保存至少100次。分别量命令处理、文件写入、确认回执，报告样本数、最大值和最近秩分位数（排序后取ceil(p×n)位）。触摸延迟从系统输入时间到首个可见响应，业务提交耗时单列，不以按钮动画掩盖保存卡顿。

[FRAME｜程序技术决定] 真机包验收含首次安装、覆盖升级与同签名继续、飞行模式、锁屏/后台/进程被终止、电话等失焦模拟、低存储写失败、双横向安全区、中文字体缺字、长列表、音频焦点及静音偏好。运行时失焦／暂停的小游戏恢复粒度沿训练与恢复协议；不把系统暂停直接转为玩家主动结束本组。交付提供APK及hash、版本/配置manifest、签名标识、离线安装说明、已知问题和全部AC证据索引；个人离线交付不新增商店上架要求。

### [FRAME] 9.1 长局、四人物及写放大压力预算

[FRAME｜工程合成FixtureSetup，不是玩法频率] 建立1、42、84、420日四档（420只是10个候选42日周期的压力样本，不设全局上限）。每角色每日200次提交×玩家及4人物=1000提交/日；每提交逻辑Receipt 512字节、有效记录1024字节，合计1536字节。这些是测试负载输入，实际UTF-8序列化额外字节、索引、校验及诊断全部另计，不以理想负载冒称真实磁盘大小。另跑正式配置实际多日轨迹，合成数据只证明规模能力。

| 游戏日规模 | 提交/Receipt条数 | 有效记录条数 | 逻辑追加载荷（不含核心/索引/诊断） |
|---|---|---|---|
| [COMPUTED] 1 | 1,000 | 1,000 | 1,536,000 B（1.536 MB） |
| [COMPUTED] 42 | 42,000 | 42,000 | 64,512,000 B（64.512 MB） |
| [COMPUTED] 84 | 84,000 | 84,000 | 129,024,000 B（129.024 MB） |
| [COMPUTED] 420 | 420,000 | 420,000 | 645,120,000 B（645.120 MB） |

[COMPUTED｜HIGH] 若每次把累计1536字节记录全量重写，n次写入的记录部分为1536×n(n+1)/2；420日n=420000时为135,475,522,560,000字节，约135.476 TB（十进制），还不含核心与冗余。[INFERRED｜HIGH] 这种增长会使“每子事件全历史根重写”不适合作为长局方案；存档负责人需采用有界核心＋追加记录／可验证提交前缀或等效结构，语义同根一致不要求物理全量重写。

[FRAME｜测试接口需求，结构主责仍属存档] 存档应公开诊断计数：coreSerializedBytes、appendBytes、indexBytes、physicalWriteBytes、flushCount、readBytes、loadedRecordCount、residentPageBytes、tailReplayCount。工程按档位记录单次普通提交p95≤500ms、冷启动到菜单≤5s；另外测Continue到首个可操作页、随机旧日/末日浏览首屏、连续翻页、四人物跨日推进吞吐（报告实值，具体吞吐门槛在M1样本后冻结）。不得把全历史读入隐藏在菜单后而不记录Continue耗时。

[FRAME｜已与存档负责人对齐的技术预算] 核心目标≤256KiB，追加日志1MiB滚动块，查询页64条，根只缓存最近64 Receipt；完整Receipt日志及可重建磁盘offset索引保留，未命中不能重新执行。索引命中须回验记录hash与已提交prefix，索引丢失重建不能当“无回执”。有限偏好开关进核心，无界已读(contentId,revision)进永久追加日志。256KiB为优化预算，不允许超出时截断数据；实际越界须报告并修结构。

[FRAME｜候选有界内存预算] 玩家记录按存档64条分页、最多缓存4页；按fixture每条1024字节，[COMPUTED] 原始载荷262,144字节。[FRAME] 对象/字符串/布局放大后为这一路缓存预留≤4MiB并独立测量。四人物串行屏障只加载当前阶段必要片段，禁止同时加载全历史到每人物副本。总进程仍遵守600MB候选，记录缓存不随日数增加；分页不改变“当前局所有日期均可浏览”。历史与诊断磁盘可随有效内容增长；诊断保留到主动清理，不能自动删日志／历史达标。

[COMPUTED｜HIGH] 以每次核心恰为256KiB＋1536B追加估算，420000次写入为110,745,600,000B（110.746GB），未计commitReceipt、索引、校验和输入轨迹；比全历史重写低但仍是需要实测审查的累计写量。[FRAME] 报告必须列每次实际core大小与总物理写量，不能将256KiB最大预算当正常平均；若保存预算或设备写入负担不可接受，由存档主责继续减小核心／增量化，不能降低保存频率而丢已完成组。

[FRAME｜故障与索引一致性] 每档在追加正文、索引写入、核心提交前后切断；恢复只能读已提交前缀，不暴露孤儿尾记录，已完成记录不能漏。随机查询旧日与重发旧operationId必须仍可验证，Receipt冷索引不得以裁剪去重语义换内存。记录预计载荷与实际物理写量比、压实写量单列，验证一次普通提交不随历史长度线性增长；压实／迁移不能在交互线程制造>100ms冻结。另测删除非权威索引后重建的耗时／内存／进度可见性，不以冷索引缺失绕过去重。最终物理格式引用FlowPersistenceContracts §9，不在本稿复制第二套存储协议。

## [FRAME] 10．实施包与进入门槛

| 阶段 | 可独立交付包 | 依赖及退出门槛 |
|---|---|---|
| [FRAME] M0 | 视图字段/来源权限/错误码、组内禁用、恢复页面、证据schema、策划确认 | 公共提交封套与领域披露语义可引用；未知参数登记负责人，不用占位成功 |
| [FRAME] M1 | 正式入口隔离、菜单/计划/事件/单动作/日结、统一脚本、首次真机布局和恢复 | 登记两机目标矩阵及实际主验机，一个真实小游戏+L1/L2/L3+主验机L4；已完成组保留，提交不受反馈延迟；预算校准 |
| [FRAME] M2 | 三餐/商店/人物/知识页，单人物自主跨天、支付/教导/刷新故障包 | 同一操作入口和私人资源；钱物效果与机会原子边界可验 |
| [FRAME] M3 | 双赛/恋爱/结局/历史/备注/续玩，全终局路线包 | 三成功和两失败路径可达，演出与后继一致、日结恢复不漏 |
| [FRAME] M4 | 八真实小游戏、三模板、四自主人物、全部文字/教学/音频 | 内容清单全量、无未核实发布知识卡、真实输入全覆盖；非八结果注入替代 |
| [FRAME] M5 | E01—E08多策略/真实计时/设备预算与可读性调整 | 进入M5前补齐第二台实际设备并冻结矩阵；体验目标有真实记录，支配策略缺陷处理；冻结最终阈值 |
| [FRAME] M6 | 固定版本APK、所有AC证据与离线恢复回归 | 同一交付版本双机全量复验；无NotRun/Blocked冒充Passed；核心阻塞/存档破坏清零，非阻塞遗留列明 |

## [FRAME] 11．对齐记录、真正阻塞与证据

[COMPUTED｜HIGH] 已向指定文本策划 `01a16466-023a-72e2-a6a8-a36fedb7c7e5` 发送视图／命令、非阻断反馈、跳过、待结算恢复、偏好范围与设备候选问题；已向事件提交 `01a16468-f8be-7cd2-bfb2-871b7a166e0e` 请求公共封套与提交后通知；已向训练 `01a16469-00b1-7fa2-babd-e93860245256` 请求输入来源与组边界。父聊天 `01a1629c-9160-7251-a24e-7e4341eb9d61` 已收到现工程隔离发现。

[COMPUTED｜HIGH] 已读取文本策划0.2稿及后续更新、流程技术稿§2—4、训练v2数据/命令/状态与恢复章节，并收到双方直接确认：只读脱敏视图、合法操作版本／Token、提交后反馈、文本推进独立、待结算不重玩、跨局偏好、演出段游标及备注草稿已对齐。文本策划已完整审阅本技术稿并回复“产品语义主干接受”，要求统一设备门槛及更新旧待确认记录；第9节已按其门槛修订，并采用双方同意的10次冷启动/100次保存/两机60分钟。修订原因是避免两稿冲突，非实测后降低标准。

[FRAME｜已关闭的产品分歧] 文本策划撤回“后台一律重做”，按主策划经训练策划转达的候选采用存活/死亡分流，具体见§3；工程仍需M1原型验证触点重建。已读文本稿3张现实卡的正文/直接来源链接/核验记录，本技术任务仅检查内容交接字段，不冒称独立核验医学内容；M4入包复核及M6离线呈现仍待执行。体型轮廓映射以成长策划稿为准，不在UI补写。

[COMPUTED｜HIGH] 文本策划随后明确签收“除正在补齐长日志/4人物压力预算外未发现新产品不兼容点”，接受合成压力标FixtureSetup及不删历史；训练负责人确认同一后台分流。存档负责人确认§9.1规模输入、256KiB核心目标、1MiB块、64条分页及冷热Receipt索引。长期规模补充已落文，方案对齐不等于任何AC执行通过。

[FRAME｜赛事口径修订] 已收流程负责人澄清：至少实际参加一场，双报只参加一场且冠军不因放弃另一场终止；实际参赛非冠军立即终止；最后出场窗口关闭后0出场才未履约。候选D42入日初始化结束、任何事件前冻结同届双赛，D42下午末窗检查0出场。E06/E08必须分别覆盖未报名／报名未出场／实际非冠军，不能将返回或放弃单场误记败绩。

[FRAME｜真实实施门槛] 首动作输入/完成量/时间与训练候选参数在M1使用前必须有版本；领域披露与日切恢复协议在对应页面接入前必须确定。未知配置不能当0。M1登记两机目标矩阵与实际主验机并验证首切片，M5前补齐第二台实际设备并冻结矩阵，M5冻结性能阈值、M6双机复验；知识卡内容来源在M4发布前核实。没有设备实测、没有新AC通过、没有新包可交付；历史四测试和旧APK只列工程参考。

[COMPUTED｜HIGH] 父聊天总审已接受本方案的职责、入口隔离、AC证据和压力预算；按总审要求统一公共source为Player/NpcProduction/Test，脚本操作与脚本小游戏输入明确属于Test。实测门槛保留，方案接受不代表验收通过。

[COMPUTED｜HIGH] 已核读文本策划设备阶段修订，并同步§9、M1/M5/M6实施包及实施门槛：首切片可先主验机，第二台实际设备在M5前补齐冻结，M6双机复验；双机发布验收、性能预算和范围未变。

[RULES I BROKE]: 无。
