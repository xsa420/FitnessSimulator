# [FRAME] M3 结局与永久进度实施记录

[COMPUTED｜HIGH] 2026-10-23：纯领域12项已核验在 `TestResults/M1/editmode-20261023-025530-087.xml` 全部Passed；同批JSON记录278/278、exitCode=0、sourceDrift=0，范围为所列正式域过滤，不是全量长无人流程。随后新增的应用适配及7项集成测试尚未执行，不沿用此前报告认证。父任务独占 Unity 执行。

## [FRAME] 接口及目录

[COMPUTED｜HIGH] `EndingProgression(IStateCodec)` 使用传入codec克隆候选，所有转换返回 `ProgressionTransition { Run, Permanent, Appends, Changed, HasResume, ResumeJson }`。不写磁盘、不读取系统时间、不计算比赛/关系事实。

[COMPUTED｜HIGH] `CreateRun(runId)` 新建局内状态；`DefaultCatalog()` 返回以下显式顺序，每次调用独立列表：

| [FRAME] Order | [FRAME] EndingId | [FRAME] ConditionKey | [FRAME] 类型/模板资格 |
|---|---|---|---|
| 10 | competition_failed | competition.non_champion | 终止 |
| 20 | competition_unfulfilled | competition.unfulfilled | 终止 |
| 30 | bodybuilding_champion | competition.bodybuilding_champion | 成功/strength_foundation |
| 40 | lean_champion | competition.lean_champion | 成功/lean_social |
| 50 | romance_success | relationship.romance_success | 成功/lean_social |

[FRAME] 条件由父应用连接真实已提交玩家领域事实；单场放弃不等于未履约，满好感不等于恋爱成功。模块不验证领域证据JSON的业务真实性，不提供强制冠军或关系成功入口。默认演出引用为 `<EndingId>.presentation.1`，应用负责对应可显示内容；引用不是额外奖励节点。

[COMPUTED｜HIGH] `Check(run, permanent, catalog, checkKind, source, checkpointId, fulfilled, snapshot, normalContinuationJson)`：仅 NormalCompletion 扫描，NPC来源拒绝；目录顺序唯一、结局ID唯一；跳过本局已触发条目，仅首个符合触发。LastCheckedCheckpoint 防止成功Continue后立即重复扫描原完成点；跨旧命令幂等仍由dispatcher语义收据保证。非正常检查不扫描。活动结局未完成时禁止新的普通完成点。

[COMPUTED｜HIGH] `ConfirmSegment(run, permanent, achievementId, segmentId)` 仅顺序确认冻结段落，已确认段重放不再推进；`SaveDraft(..., draft)` 只改待提交草稿；`CommitNote(..., text, leaveEmpty)` 一次文字或明确空，禁止仅空输入隐式完成、禁止后改；`Continue(..., achievementId)` 仅成功且备注已提交的菜单态开放，返回原正常后继JSON，并清活动结局，不扫描其他结局。

[FRAME] 后继JSON由应用编码完整恢复位置（结果待结算、组后剧情、日切下一步、选择点、世界屏障等），应用须验证其schema/业务引用。纯模块只保存原文并返回，不能从UI首页猜恢复点。终止结果仍保留后继诊断，但不返回可执行Resume。

## [FRAME] 保存与永久数据

[COMPUTED｜HIGH] 局内 `ProgressionRun` 保存触发ID集合、最后检查点、终止锁、HasActive和ActiveEnding。HasActive=false只允许空对象或null；恢复到有实际字段但flag=false报错，不能静默丢演出。活动游标、段落、备注三态一致性由Validate检查。

[COMPUTED｜HIGH] `ProgressionPermanent.TemplateGrants` 仅保存有限模板ID及首次achievement证据；整份历史不进入根。触发追加 `permanent/history` 的 `AchievementFrozen.v1`，备注追加 `NoteFinal.v1`。快照冻结run/ending/title/day/device ISO8601时间及偏移/contentVersion/角色肌肉JSON/计划JSON/证据JSON。备注不覆盖快照，草稿不成为最终备注。备注长度按最多200个文本元素验证，超限拒绝不截断。

[COMPUTED｜HIGH] `ReadHistory(store,skip,count)` count为1..64，从已提交日志分页扫描并归并所选快照的后续最终备注；内存保留所选页。查询不修改状态；日志扫描成本随历史长度增长，本轮没有新增磁盘索引。

[FRAME] 父应用将Run写局内片段，将Permanent写Root.PermanentFragments，并把Appends加入同一PreparedOutcome；普通完成的checkEndings回调可在该候选上合并。全部随同一次store.Commit提交，不能先发布历史再保存局内触发标记。演出/备注/Continue的CheckKind设None或EndingPresentation，不能请求NormalCompletion。

[COMPUTED｜HIGH] 本轮无需改Core/Flow/Persistence。既有dispatcher未知写冻结和Recover-before-retry覆盖永久写意图；既有带认证resolver的Execute可在prepare前回旧语义收据。备注语义键按achievement固定，payload包含text与leaveEmpty；不同参数返回SemanticConflict，不能另发ID改写已提交备注。段落语义键含achievement/segment，Continue语义键含achievement。SaveDraft按编辑修订单独命令，不能用同一业务键阻止合法后续编辑。

[FRAME] 新局最终确认仍走既有BeginNewRun；仅以CreateRun重置局内进度，永久片段和日志前缀保留。历史不是可加载旧局。必须由应用验证模板资格，默认模板无需永久解锁。

## [FRAME] 本轮待执行测试

[COMPUTED｜HIGH] 已写12个用例覆盖：默认目录顺序与资格映射；同中只取首项/本局一次/同检查点不追补；实际JsonUtility演出中途与备注草稿恢复；明确留空/文字一次提交与超长拒绝；成功返回原待结算后继、终止拒绝；NPC及非正常检查不授历史；HasActive丢失/游标矛盾/终止标记矛盾拒绝；快照深拷贝；永久日志最终备注归并；触发在BeforeRootWrite/BeforeAcknowledgement未知写恢复只有完整前态或后态且重试仅一次；最终备注语义重试和冲突；BeginNewRun后永久资格与历史保留。上述为已写测试，不是通过证据。

[FRAME] ActiveEnding保存冻结Terminal值，Validate要求它与Run.TerminalLock一致；与日志中冻结快照及实际领域事实的交叉核验仍由父应用恢复负责。测试中的比赛/关系predicate明确为纯模块夹具，不能替代真实比赛或关系路线。ResultReady后返回原Settle JSON的断言不证明应用已执行且只执行一次结算；该端到端断言由父集成补齐。

## [FRAME] 应用适配实施包

[COMPUTED｜HIGH] 父任务另行授权唯一新增 `Application/GameSession.Progression.cs` 与 `Integration/GameSessionProgressionTests.cs`。8个private hooks为 InitializeProgression、CheckProgression、PrepareProgressionAction、ReadProgressionView、IsProgressionPaused、UnlockedTemplates、ValidateProgression、ProgressionSemanticKey。既有main/NewGame/View/Input入口接线由父修改；FrontendContracts两备注字段由UI所有者修改。

[COMPUTED｜HIGH] 赛事读取调用另一partial的ReadCompetition严格恢复，再按本届ContestId匹配Contest.Kind映射冠军；非冠军/未履约读取正式Fact.Kind。关系读取NormalizeAfterLoad后的RomanceEstablished，不把好感100当成功；快照记录事实和关系修订。

[COMPUTED｜HIGH] 正常后继保存所有非progression局内片段的owner/scope/schema/revision/hash，以及session日期/phase/dayStep；恢复和Continue均比较当前片段未变。继续只清除活动结局，不覆盖任何旧Root、不重写正常业务、不改永久备注。CheckProgression必须在本次普通提交所有Write之后调用。永久资格只放永久片段，fresh新局只初始化run片段。

[FRAME] 父入口须在普通menu投影前读结局视图，在Prepare前分发结局动作，并给Advance/Input/SyncKernel/continue-game加暂停守卫；普通业务不能绕过演出/备注。ending-continue成功后atMenu=false；最终备注后显示ending-menu。newgame校验和Initialize从旧根资格传参，fresh本身不能携带PermanentFragments。

[COMPUTED｜HIGH] 动作为ending-segment/draft/note/empty/continue；StringValue是固定AchievementId/SegmentId/Text的JSON。语义键按run/achievement/动作阶段生成，note与empty共用一次备注槽；draft按operation区分编辑。备注201文本元素或空正文返回中文原因，明确留空要求空载荷。恢复读取活动草稿，成功菜单显示三模板及当前资格。

[COMPUTED｜HIGH] 新增7项应用集成用例：成功暂停与精确继续/恢复、正文200成功与201拒绝/语义冲突、备注BeforeRootWrite与BeforeAcknowledgement未知写两个用例、解锁模板草稿取消与最终新局永久保留、普通后继篡改恢复拒绝、无真实领域标记不造结局。前置成功由显式关系领域夹具调用ConfessionRules公式产生，不代表从初始赠礼资源到恋爱成功的整局路线。没有自行运行Unity，待父接线后执行。

## [FRAME] 收据定位索引与延迟快照优化

[COMPUTED｜HIGH] 父任务授权后，先在 `%TEMP%/Fitness-ReceiptCacheDraft` 准备副本，第三窗口结束后应用。仅修改 LocalCheckpointStore、GameSession.Progression、自有应用测试并新增 ReceiptIndexTests。没有持久化索引文件，没有减少保存频率，没有改变日志/根/账本的写入及刷盘顺序。

[COMPUTED｜HIGH] 内存索引为两组键→同一ReceiptLocation，位置保存segment/start/end及校验过的帧hash；不保留全量OperationReceipt/Result/PayloadHash。完整收据仅使用原根64条热缓存；冷命中按位置读单帧，校验帧格式、hash及身份。Load复用原ReadData完整前缀验证遍历构建临时索引，所有日志及最终hash检查通过才发布，没有第二遍构建扫描。索引绑定run/generation/receipt-prefix的segment/offset/count/hash；负查询只依据完整已验证索引。

[COMPUTED｜HIGH] 成功提交在账本写入及故障注入确认点通过后只加入本次位置；未知写立即失效索引并阻止读取，Load重新验证。新局丢旧索引，归档不扩大旧收据可查询范围；CanReset只读探测还原原根对应索引；返回收据防御复制，避免调用方修改内部热缓存。冷读发现损坏转为不可读并要求Load，不返回“未找到”。完整账本与日志仍为唯一权威。

[FRAME] 定位键本身仍随本局提交数O(N)增长，不宣称常量内存；420日预算的索引内存、构建耗时与磁盘写放大仍须父任务实测。当前没有全量历史实体常驻内存，也没有通过减少提交或放宽测试时限取得性能结果。运行中未被读取的媒体篡改仍以重载完整校验为界，不声称实时检测全部文件改写。

[COMPUTED｜HIGH] CheckProgression仅在存在本局未触发命中项时取设备时间、构造快照和冻结所有普通片段后继；无命中仍调用原Check并提交LastCheckedCheckpoint，保留原校验/次序。演出恢复校验没有缩减。自有StartAtMeal改为等待Enabled选项，最多3000次推进，耗尽保留诊断并失败，不绕过人物屏障。

[COMPUTED｜HIGH] 新增ReceiptIndexTests 7例，覆盖新键负查询与增量提交不解码旧帧、Load单遍/冷命中单帧、返回对象隔离、两个Unknown点后重载、跨局/CanReset缓存恢复、滚块定位与冷帧损坏拒绝。计数基于codec实际DataFrame解码，不用耗时阈值冒充复杂度证明。现有8故障点、语义重放、永久备注/历史测试仍需一并回归。

[COMPUTED｜HIGH] 静态检查：GameContracts、SerialCommandDispatcher、LocalCheckpointStore通过独立C#编译检查；未运行Unity或运行时测试。父此前033244-584 World现场为Timeout Failed，不能标通过；优化效果待同seed/策略正式重跑报告。

## [FRAME] 公开历史浏览接入

[COMPUTED｜HIGH] 父授权窗口后已从专属TEMP草稿落盘 `Application/GameSession.History.cs` 与 `Integration/GameSessionHistoryTests.cs`，新增5例待运行。共享Execute/View/Advance由父接线，本模块未修改共享文件。

[COMPUTED｜HIGH] hooks为TryExecuteHistory(action,out reply)、ReadHistoryView(root)、IsHistoryOpen、CanOpenHistory(root)。浏览操作只改变内存页面状态，不经过dispatcher、不改变atMenu、不推进正常事件或结局检查；历史打开时拒绝其他业务操作，关闭返回原页面。Playing、新局草稿、结局演出或备注未完成时拒绝打开，ending-menu可查询。

[COMPUTED｜HIGH] 目录只有有限默认目录标题和未知/已达成标记，不显示条件；记录每页16项，加1项探测下一页，详情读取单条冻结快照及最终备注。仅缓存当前投影JSON，按根generation和页面位置失效；目录摘要最多保存5个配置ID，不常驻完整历史。详情展示原设备时间及偏移、身体/肌肉/动作状态、冻结计划和三态备注，不提供旧局继续或修改备注操作。未知版本/坏历史显示读取失败，不冒充空历史。

[FRAME] 5例涵盖未知目录纯读与返回、有界分页/三备注态/原时区、跨新局和重开后冻结快照、演出备注防绕过、实际Playing禁止辅助浏览。历史达成前置为明确存储夹具，关系前置为领域公式夹具；不是自然终局路线证据。当前未运行此5例，待父接线后统一验证。

## [FRAME] 提交拷贝 A/B 限定优化

[COMPUTED｜HIGH] 父任务在八核045200-920批次结束后授权限定写入。应用前核对LocalCheckpointStore基线SHA256为 `8DE9398EE7A50DE16ED7B34C843A46BB998FAB88359B169E7A45ACD9D9410613`，仅落两处修改：A将整根Clone(current).RecentReceipts改为最多64项逐项CopyReceipt，新局直接空列表；B常规成功返回独立Root时复用已刷盘并读回核验的json进行Decode，避免再次Encode。所有候选隔离、返回对象隔离、刷盘、hash、读回、故障点、收据验证和保存频率保留；不实施字段拷贝器。

[COMPUTED｜HIGH] 新增 `CommitCopyIsolationTests.cs` 共11例，待父任务执行：编码计数1例（首笔及后续store.Commit各2次Root Encode/Decode，记录字节）；外部candidate/返回root各类列表项及receipt修改隔离1例；全部8个写入故障点保持调用者候选不变、未知写阻断及完整旧/新根恢复8例；新局独立收据列表与永久保留1例。计数证明编码次数，不代表实测CPU占比或世界路线提速。

[COMPUTED｜HIGH] 修改后LocalCheckpointStore SHA256：`6BCE61F86E92DD1F91FFE79AC7045D7243E4E09EFF651C901656E28A68ECAAE1`；测试SHA256：`6D29F7AEAB3BF71179DEEC77692E2F6C7F425B17F15AE001ABC7256BA490C8C2`。本次未自行编译或运行Unity，待父串行FlowPersistence及History/Progression回归后，再按原seed、同世界策略比较。

[RULES I BROKE]: 无。

## [FRAME] 工作收入提交恢复结构回归（2026-10-23）

[COMPUTED｜HIGH] 新增 Integration/WorkIncomeRecoveryTests.cs，参数展开7例，全部明确使用CommandSource.Test和D1存储结构夹具，不是自然工作路线、真实旧档迁移或长周期证据。证据目录与save目录隔离；夹具经过真实LocalCheckpointStore提交，后续公开continue/day-step通过真实GameSession调度和保存路径执行。未修改生产接口、领域允许集合或其他所有者测试。

[FRAME] WorkCompletionFaultRecoversMoneyFlowCheckAndReceiptAtomically：morning/afternoon各在BeforeRootWrite及BeforeAcknowledgement注入故障，核恢复后的金额、工作回执、Flow完成、正常检查数和WorkIncome日志全旧或全新；重开原operation重放只付款一次，工作时间1800秒。同payload换operation但带旧revision按现行应用协议拒绝且完整root不变，不冒称应用已支持稳定事件语义重放。没有重复新增已有调度器语义解析器模型测试。

[FRAME] StructuralLegacyDailyIncomeSuppressesNewWorkPaymentButCompletesEvent：显式构造已发旧日收入事实，实际工作完成不新增WorkIncome、不再次付款，但正常完成事件。StructuralOldE01CursorOnlyAdvancesWithoutIssuingOrReissuingIncome两例：显式构造D1/E01与旧日收入未发/已发状态，公开day-step推进至E02，life及正常检查数不变，重开重放不新增Income或WorkIncome。旧收入事实通过旧领域准备函数生成，仅为兼容边界夹具，不声称自然旧档恢复。

[COMPUTED｜HIGH] 当前生产源码副本及新增测试已在TEMP独立编译通过；未启动Unity、未运行7例。最终运行与结果由架构师统一执行，不能将静态编译记为行为通过。

[RULES I BROKE]: 无。
