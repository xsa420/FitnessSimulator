# [FRAME] 事件提交、公共状态与保存恢复契约

[FRAME｜状态] 2026-10-23；契约修订 v0.2；公共协议及存储方案已通过父聊天本次总审，实际测试未执行。只做实施方案，未修改业务源码或正式配置。产品顺序与参数仅采用策划确认的候选基线，不升级为用户既定规则。v0.1全历史重写已因写放大审查撤回，采用下述追加数据/有界工作根方案。

## [COMPUTED] 来源与现状

- [COMPUTED｜HIGH] 已读在线[项目主文档](https://app.notion.com/p/2bc3e8eb2dfd80bcb359f603fc989244)、ProgramModulesAndDependencies、ProductionAlignment、README、Architecture、Verification，以及 Runtime/Persistence/LocalJsonStore.cs、SaveContracts.cs、Runtime/Training/TrainingContracts.cs。
- [COMPUTED｜HIGH] LocalJsonStore.Save 用同目录 tmp、Flush(true)、File.Replace/Move；无提交账本、校验摘要、跨文件协调或恢复状态。TryLoad 把部分解析/IO错误合并为 false；不能区分首次无档、损坏和版本不兼容。
- [COMPUTED｜HIGH] RunSaveEnvelope 仅有单后继ID、日期、单随机状态和若干JSON；历史独立外壳无法表达动态树栈、待结算组、NPC屏障和结局正常后继/演出双游标。旧训练接口仍带历史表现、连续力量组字段。
- [INFERRED｜HIGH] 旧 Architecture 的整动作回退、历史成绩复用不能继续作为实现依据；新方案保留完成组、自动容量按当前目标执行。底层读写和原型测试不等于已通过恢复验收。

## [FRAME] 1. 决定层级与硬约束

| 分类 | 本稿约束 |
|---|---|
| [FRAME｜既定] 产品 | 每个普通完成子事件保存并有序检查首个未触发结局；容器收尾不额外检查；演出完成子事件保存不递归检查。开始才记次数；相同配置共享次数；恢复同实例不重复开始。默认事件无限/间隔0，仅无合法其他候选时用。 |
| [FRAME｜既定] 恢复 | 已完成组保留；未完成组重做；已存小游戏结果只结算一次；成功继续正常后继；终止型不能继续；一次备注含明确留空；当前局覆盖但历史/模板资格保留。 |
| [FRAME｜技术决定，方案总审通过] 实施 | 单写者、一个根封套、双数据槽+追加可见前缀+提交收据账本；领域只能准备变更；确定性随机按角色/用途分流；实例身份与配置身份分离；UI与脚本共用命令门。 |
| [FRAME｜候选参数] 产品时序 | 锚点排序、日结顺序、NPC到场/结束屏障、赛事冻结、目录结局优先级由 FlowAndRecoverySpec 给出；本文存结构与引用，不私自定产品优先级。 |
| [FRAME｜真实实施门槛] 尚须验证 | 目标安卓设备对持久化刷盘/目录同步的能力；领域版本迁移、内容版本可用性、全量配置合法性；所有故障注入未执行，不称已可靠落地。 |

## [FRAME] 2. 公共身份与权威状态封套

[FRAME｜技术决定] 所有ID为稳定字符串，显示名不参与身份。RunId新局生成；ActorId固定内容身份；ResourceScope至少为RunId/ActorId，可加ShopId；实例ID不使用帧号、对象地址或场景索引。Revision为根提交递增64位整数，Generation为磁盘提交代际，业务查询返回Revision。

| 字段/记录 | 类型与语义；唯一语义写者 |
|---|---|
| [FRAME] RootEnvelope | schemaVersion、contentVersion/hash、generation、revision、previousGeneration、lastCommitId、currentRun?、permanentCore、logPrefixes[]、recentReceipts[最多64]、preferences。存档模块序列化，提交器唯一发布。logPrefixes声明可见追加数据的长度/末帧hash。 |
| [FRAME] RunState | runId、runStatus、templateId、logicalDay、weekday、dayScheduleVersion、dayPlanRef、activePlan/pendingPlan版本引用。会话管状态/日期；计划模块管计划语义。没有现实时间推进游戏日。 |
| [FRAME] ActorState[] | actorId/resourceScope、各领域versioned fragments。身体/经济/关系/训练/计划/比赛分别唯一拥有字段；NPC不复制另一套身体公式或钱包。 |
| [FRAME] FlowState | anchorCursor、entryResolution、treeInstances[]、executionStack、activeNode、continuation、triggerStats[]、occupancies[]、pendingUnlocks、dayEnd、worldBarrier、actorCursors[]。流程唯一拥有。 |
| [FRAME] NodeInstance | instanceId、definitionId、definitionVersion、treeInstanceId、parentId、callOrdinal、phase、startCommitId?、completionCommitId?、startRandomRefs、startedDay、recoveryInputRef。phase=NotStarted/Started/AwaitingInput/Completed/CancelledByControl。 |
| [FRAME] TreeInstance | treeId、kind Fixed/Dynamic、rootDefinitionRef、immutable node/edge manifest 或可验证生成输入+manifestHash、buildVersion、planRevision。动态实例由训练生成，流程校验存储。恢复禁止拿新计划重生成旧树。 |
| [FRAME] EntryResolution | anchorOccurrenceId、status Unresolved/ChoicePending/Resolved、candidateIds、selectedEntry、choiceToken、randomReceipt、resolutionCommitId。候选显示不记触发次数；同一锚点首层解析最多一次。 |
| [FRAME] TriggerStat | ActorScope+definitionId → startedCount/lastStartedDay。同配置跨树共享；玩家/NPC各私有计数，NPC不消耗玩家机会。 |
| [FRAME] Occupancy | day/anchorId、ownerEventInstanceId、source、state Reserved/Consumed。同owner重放不冲突，不同owner冲突；外层默认当前锚点，子事件默认无额外占用。 |
| [FRAME] RandomStreamState[] | streamId=(actorScope,purpose)、algorithmVersion、seed/state、drawIndex；入口/伤病/商店/评委/策略分流；随机记录含streamId、前后index和结果摘要。 |
| [FRAME] TrainingFragment | workoutId、frozenPlanRevision、definitionsVersion、planEntryId、setId、setOrdinal、setPhase、preSetSnapshotRef/hash、pendingResult、settlementKey、settlementCommitId、elapsedGameTime。详细输入输出引用 TrainingGrowthContracts。 |
| [FRAME] DayEnd | day、orderedStepDefinitionIds、stepCursor、perActorProgress、state NotStarted/Running/AwaitingNextDay/EnteringNextDay/Done、summaryBaseline、dailyDelta、dayEndDelta、changedFieldIds。日结贡献属于全天差值子集，不再次相加。 |
| [FRAME] WorldBarrier | barrierId=(run,day,phase,anchorOccurrenceId)、participantActorIds、targetPhase、arrivals[]、completed[]、scheduleRevision。阶段至少入日/锚点到场/锚点结束/日结/次日初始化；顺序由配置版本决定。 |
| [FRAME] ActorCursor | actorId、day、phase、anchorId、eventCursor、workoutId?、pendingResultId?、arrivalReady、barrierReached、lastCommitId、expectedActorRevision。actorIndex只用于稳定调度顺序，不可当身份。 |
| [FRAME] EndingState | triggeredEndingIds、activeAchievementId?、terminalLock、normalContinuation、presentationCursor、noteState、noteDraft。结局模块唯一决定锁与继续资格。 |
| [FRAME] PermanentState | core持有有限unlockedTemplateIds、最近资格证据引用、活动achievement引用与备注态；全部immutableAchievements/最终备注正文在永久追加日志，每历史键=(runId,endingId)。正文可见前缀和局内标记同根事务。新局不重置永久日志前缀，不携带旧局身体/钱/物品/关系/个人计划。 |
| [FRAME] OperationReceipt | operationId、payloadHash、commitId/revision、outcomeCode、resultSummary、eventInstanceId、settlementKey?。完整已提交回执写run receipt追加日志，根仅缓存最近64条。缓存未命中查已提交前缀的持久索引或扫描，不能因缓存淘汰允许重执行。新局后旧run命令拒绝，永久历史命令按其日志语义键查询。 |
| [FRAME] EffectiveRecord | recordId、run/day、actor、commitId、sourceNode、seenTextIds、selectedOption、publicDeltas。只记录有效事实，不含未选分支/隐藏判定。诊断另含attempt/sessionId、作废原因、恢复generation。 |

[FRAME｜技术决定] 序列化采用显式DTO/数组，禁止依赖现有JSON工具隐式序列化字典/接口/多态；每片段注册版本、序列化器、合法性校验器。跨片段引用用稳定ID。缺字段必须报迁移/配置错误，不以0、空数组或成功填补关键状态。

## [FRAME] 3. 命令、准备结果、查询

| 契约 | 字段及行为 |
|---|---|
| [FRAME] CommandEnvelope | operationId、expectedRevision、runId、actorId、resourceScope、eventInstanceId?、setId?、actionKind、payload、payloadHash、source Player/NpcProduction/Test、capabilityToken、choiceToken?。source/capability由入口授予，外部不能自报Test获得权限。 |
| [FRAME] PreparedOutcome | outcomeKind Success/BusinessFailure/WaitingInput/ControlOnly；eventDisposition KeepOpen/CompleteNode/AuxiliaryOnly；checkKind None/NormalCompletion/EndingPresentation；opportunityPolicy Keep/Consume及domain理由；domainPatches[]（owner、fragmentVersion、preconditions、变更）、randomAdvances、timeIntent、occupancyIntent、facts[]、publicRecords、controlIntent、normalContinuation。无磁盘/网络/全局副作用；枚举组合必须校验。 |
| [FRAME] CommandResult | Committed、AlreadyCommitted、Rejected、ConfigError、StorageUnavailable、StorageOutcomeUnknown；附operationId、receipt、currentRevision、错误定位与允许重试动作。BusinessFailure是已执行业务结果，可Committed，不等于技术Rejected。 |
| [FRAME] QueryView | snapshotRevision、permittedActions、disabledReason、disclosureStage、公开领域视图、pendingResolution。查询不抽随机、不计次数、不发奖励。动作与选择Token绑定版本；过期仅刷新，不拿旧报价继续扣钱。 |
| [FRAME] EvaluateCondition | conditionRef、readContext Committed/Candidate、revision、actorScope → bool或TypedError。Candidate仅提交器内部；UI不可访问未提交候选和隐藏依据。 |
| [FRAME] CommitCheckpoint | expectedGeneration、rootCandidate、operationId → DurableReceipt/Unknown/Error。仅提交器可调用；领域/表现不得直接调用 LocalJsonStore。 |
| [FRAME] LoadCheckpoint | Empty/Loaded/CorruptCommitted/IncompatibleSchema/MissingContent/IoUnavailable；返回generation和修复入口，不把损坏等同新局。 |

[FRAME｜幂等顺序] 验证运行身份/权限后先查OperationId：同hash返回原Receipt（即使expectedRevision已旧）；异hash报OperationConflict。未见ID才检验版本与合法操作。业务还有稳定SettlementKey，例如(run,actor,setId,Settle)，防不同operationId重复结算。启动、日结步骤、NPC步骤、入日、FreezeContest、NoteCommit分别使用语义键。

[FRAME｜失败与完成显式判定] checkKind=NormalCompletion仅允许玩家普通节点CompleteNode；EndingPresentation的CompleteNode保存不扫描；NPC、Start、辅助提交、KeepOpen均None。业务失败不自动决定机会：余额不足教导/餐食为BusinessFailure+KeepOpen+Keep+None，只留可改选结果，不扣款或占机会；无效道具结束机会为BusinessFailure+CompleteNode+Consume+NormalCompletion（依生活已定语义）；力量组失败的Settle仍CompleteNode+NormalCompletion，按真实结果结算并消耗名额。ControlOnly若为正式NextAnchor/EndCurrentAction子事件则CompleteNode+NormalCompletion；容器栈收尾/纯查询不是节点完成，None。非法组合报配置/契约错误。KeepOpen若只返回拒绝不产生业务提交；若须保存选择/回执则为无效果检查点，不把它当普通完成。

[FRAME｜并发] 首版提交器串行。准备计算若读取版本被辅助保存改变，则从最新已提交根重建候选并重验领域前置；禁止覆盖辅助修改。小游戏/训练保存不可用根Revision作为唯一组前锁：冻结preSetSnapshot+领域版本/输入hash，SavePlan改变根版本不使已完成结果失效。执行中不允许独立身体变更命令；NPC/其他片段变化也须重新验证依赖。

## [FRAME] 4. 状态转移与单提交顺序

[FRAME｜技术决定] 普通流程：UnresolvedAnchor → EntryResolved或ChoicePending → StartCheckpoint → Started/AwaitingInput → PrepareCompletion → CandidateValidated → DurableCheckpoint → 发布Committed → 后继。容器只推进结构，不假造完成奖励；等待表现期间的成本与效果仅暂存工作副本。

1. [FRAME] ResolveAnchor固定候选/抽取/选择Token及随机位置；必须开始的入口可与Start在一个根提交，先选未确认不计数。
2. [FRAME] Start验证配置、入口资格和占用；新实例把startedCount/lastStartedDay、占用、immutable恢复输入及随机起点一同持久化，然后开放交互。恢复Started复用实例，不再检验次数导致自我拒绝，不重增统计、不再占一次。开始不是普通“完成”，不扫描结局。
3. [FRAME] 完成命令从Committed根创建候选。领域按声明顺序准备并验证Patch；汇入时间、有效记录、随机游标、完成标记与正常后继。普通未完成事件的临时输入/成本不混入根。
4. [FRAME] 在该候选的最新状态上按配置顺序扫描结局，仅首个本局未触发项。加入触发标记、不可变历史、模板资格证据、终止锁或成功继续资格、正常后继与演出起点。此时无磁盘发布过业务半状态。
5. [FRAME] 校验整个根的引用/所有者/版本/不变量，执行可靠提交；只有成功才更新内存Committed指针并通知UI。未知结果冻结命令队列，先按operationId读盘核对，不盲重试更换ID。
6. [FRAME] 无结局按已存后继走；有结局切演出，保存正常后继。演出子事件完成仍提交但不扫描；容器退出、菜单进入、继续按钮、计划/偏好保存均不触发额外结局扫描。

[FRAME｜开始语义] 开始统计/占用是显式流程检查点，不是未完成业务成本。事件未完恢复保留开始事实，回到该事件开头；若当前为复合树，保留已完成子节点和当前未完成节点的Started，而不是重置整个树。配置错误保留最后有效根，不以释放占用+重抽躲过错误。

[FRAME｜继承身份] 固定树节点实例=(treeInstanceId,definitionPath,callOrdinal)。动态训练节点=(workoutId,planEntryId,setOrdinal,phase,callOrdinal)。同配置重复引用有不同instanceId，共享definition统计。动态插入突发链分配并保存callOrdinal/nextInstanceSequence；恢复不得再抽一次并分配另一个树。

[FRAME｜控制] EndCurrentAction只能由训练结算后合法节点发出，标记该动作剩余组CancelledByControl并移至下一合法动作；NextAnchor终止本锚点剩余栈和训练，不额外消耗夹在目标间的锚点；正常无后继表示树结束，RequiredNext引用缺失是ConfigError。

## [FRAME] 5. 条件、配置与入口算法

[FRAME｜技术决定] Condition为All(children)、Any(children)、Not(child)、Compare(fieldRef,operator,typedValue)、HistoryPredicate(kind,id,count/window)。组合不空；日期/数值/枚举/标记类型检查；Not严格1子项；历史只读有效账本与领域事实，诊断不可作为达成条件。字段注册声明owner、类型、可用ActorScope、可公开等级；未知字段或缺参数报错。

[FRAME｜既定/算法] 候选按enabled→条件→次数→冷却→占用过滤；互斥组按配置顺序取首合法成员；definitionId去重；选择事件合并成组再加权；选项唯一自动执行，多项必须确认。默认入口不参与普通权重，只有普通合法候选为空才执行，必须无限次数/冷却0/可执行且非一次性奖励漏洞。

[FRAME｜技术决定] 配置EventDefinition至少含id/version、handlerKind、entryCondition、enabled、positiveWeight、maxStarts?（null才无限）、cooldownDays、exclusiveGroup?、selectionGroup?、consumeTargets、successors及required标志、controlIntent约束。DateTarget为RelativeDay或CurrentWeekday并显式anchorId；过滤过去目标不顺延；剩余未解锁/被别人占用则候选不合法；显式列表过滤后为空为配置错误。当前owner已占用恢复例外不能变成新事件偷占许可。

[COMPUTED｜HIGH] 冷却例：D3开始且cooldownDays=2，D4/D5禁用、D6可用；判断为currentDay-lastStartedDay>2。cooldown=0允许当天其他合法锚点，不误用严格大于0阻止同日。

[FRAME｜技术决定] 加载校验：重复ID/引用/不支持handler、默认入口、正权重、互斥归属唯一、日期类型、时间单位、必需后继、无交互无限循环、动作结束位置、领域比例/区间/人数与迁移兼容；动态校验：当前目标占用、实际资格、图manifest、全部参赛者屏障。错误带configId/fieldPath/definitionVersion/run/actor/node/revision，不默默跳过。

## [FRAME] 6. 训练、时间与辅助保存边界

| 阶段 | 落盘/恢复契约 |
|---|---|
| [FRAME] SetStarted | 保存请求、组前身体/训练上下文、随机起点和固定组身份；未完成退出重做本组；完成组不回滚。实时输入不写权威成长。 |
| [FRAME] ResultReady | 小游戏完成是普通子事件：保存不可变结果、结果来源、输入摘要、实际梯度/量/成功失败、组前引用；结局可在此触发，normalContinuation仍为Settle。恢复不重玩。 |
| [FRAME] SetSettled | 以SetId/Settle去重；原子应用身体/成长/疲劳/知识/资格/判伤随机/时间/名额/组记录/后继；完成组以此为准。结果阶段不得提前计同一时间或名额，容器汇总不得再加。 |
| [FRAME] PostSet | 固定剧情、结束动作、休息、互动均单独实例；未完成剧情只恢复剧情；受伤判定已固定，不再抽；动作结束保留本组既得效果。 |
| [FRAME] Rest | 确认休息提交时间/恢复；同期普通对话为0额外时长后继，完成才发好感；教导支付/权益/机会另一个原子业务子事件。 |

[FRAME｜技术决定] 游戏时间字段统一为整数gameSeconds+timeUnitVersion；训练片段elapsedGameSeconds用同单位。现实操作时戳单独属于输入轨迹，不能计入游戏秒。动作/休息配置须同单位，转换只在边界且明确舍入，不能用渲染帧生成收益。实际次数×配置单次耗时与休息分别记来源。超时输出为来源锚点、耗时、标准时长、超时量及目标入口Token；依流程策划候选传下一实际未消耗锚点一次，不累旧债、跨日清零，不隐式跳锚点。

[FRAME｜训练程序对齐] SetId为结构化(run,actor,workout,planEntryId,setOrdinal)的唯一编码；SettlementKey附phase=settle与contractVersion=2，ResultKey附phase=result，RestKey另附单调RestOrdinal。不得拼未转义自由文本。preSetContext包括BodyRevision、TrainingRevision、RelationshipEntitlementRevision/权益快照、contentVersion和QuotaSnapshot；相关输入已变则报冲突，不按新身体偷偷重算。训练片段另含practicedExerciseSet与grantedAutoKeys/tier；成功结局恢复Settle已双方确认。

[FRAME｜计划隔离] SavePlan只从最新Committed根复制，更新计划模块批准的active/pending字段与操作Receipt；禁止序列化运行中对象图或工作副本。第一次创建按开局门生效；后续版本次日生效，同日最后一版覆盖pending；当前动态树冻结版本不变。SaveNoteDraft、SaveReadPreference同样白名单且不推进节点/随机/次数/时间/结局。辅助命令不读本次未完成业务效果。

## [FRAME] 7. 世界屏障、日结与赛事

[FRAME｜结构已对齐] 一个世界日期由会话写。每人物自己的资源、计划、事件游标、训练会话及random streams；同actor/day的已存在训练会话用于到场/搭子读取，禁止为互动再启动一套自主训练。ActorAdvance以(actor,barrierId,stepId)去重；多人串行提交，每次记录其游标。NPC业务提交不检查玩家结局；玩家公开节点的检查仍消费最新有效事实。

[FRAME｜已与策划对齐的候选顺序] 入日初始化完成 → 必要赛事整届冻结 → 各锚点按排序：NPC先解析本锚点真实入口/选择并持久化到场 → 玩家相遇与各角色本锚点执行 → 全员到结束屏障 → 下一锚点。不是先跑完NPC全天再让玩家相遇；中断只继续未达屏障人物/步骤；全部到齐才前进。

[FRAME｜候选日结结构] 当天全部锚点后，按策划给定orderedStepIds逐项执行收入/饮食评估/恢复/清理等；每步和每人物进度分别落盘，当前步成功才递增cursor。最后显示日结AwaitingNextDay，点击EnterNextDay才进入可恢复入日步骤；待生效计划/搭子、商店刷新、解锁锚点采用相同StepKey。日结中成功结局保存精确nextStep，继续不重算前步；不得仅保存“已日结”一个bool。

[FRAME｜消费流程稿候选，不另定产品顺序] FlowAndRecoverySpec §2锚点为清晨(锁)/早餐/上午/午餐/中午/下午/晚餐/晚上/后半夜(锁)。§5日结E00封闭入口/截止事实→E01收入→E02摄入评估→E03局部疲劳恢复→E04身体压力恢复→E05不足压力追加→E06临时疲劳清零→E07标记次日到期解除→E08收集解锁/待生效→E09只读汇总→E10等点击。点击后N01日期/解锁/到期/新日账本→N02计划/搭子切换及同步→N03各私域店刷新→N04赛程/冻结→N05开放首锚点。每一业务步骤读取前项结果；E09/E10不追加结局扫描。四人逐步骤达同阶段屏障才共同前进，任一失败不跳人。伤病E07只准备到期，N01才激活。

[FRAME｜训练日初补充] N02计划及搭子同步完成后、N03之前调用训练PrepareDayQuota(plan,trainingProgress)，按日初已解锁梯度生成DayTrainingQuotaSnapshot及冻结L_std；首局有效计划建立后执行同样步骤。包含入场已伤动作，归训练片段，不创建workout或消耗名额。进入训练只引用，不因当天资格变化重归一。训练外层超时唯一标准L_std，H含训练及明确后继加时，只算一次max(0,H−L_std)；普通1800秒候选不覆盖训练，不叠加参考超时。

[FRAME｜固定时间边界] 报名/退报同时申请/释放对应赛事Reserved占用，报名碰已提交占用即拒绝该场，不预占全部未报名比赛。任何路径越过赛事窗口（包括被消耗、全天学习、NextAnchor）仍先执行配置的赛程截止业务子事件并检查结局；只有普通入口被跳过，世界边界不得漏执行。N04冻结身体时引用D8等正式节点已固定的评委，不重新抽。

[COMPUTED｜HIGH] 交叉阅读发现训练稿曾写压力不足追加先于恢复，生活稿/流程稿写恢复先于追加；对B=99、追加6、恢复4且每步裁剪0..100，分别得到96和100。已直接要求流程/训练方修订；本文采用流程稿E04→E05候选，不以可交换算例掩盖此冲突。

[FRAME｜赛事契约] FreezeToken={editionId,barrierId,day,phase,actorRevisions[],participantSnapshotIds[],contentVersion}。赛事模块准备不可变快照与评委，提交器检查所有参与角色达到同一冻结屏障后一次提交；同届双赛引用同token。首赛成功后的Continuation仍含第二赛出场选择，不能提前发布第二赛成绩；实际参加第二赛且非冠军可封局。未报名/放弃出场/实际非冠军分别记录，不把未出场伪造为败绩。赛程后续由比赛模块产生，生成键=(run,cycleOrdinal)。D42及42日周期均候选，不能硬编码。

[FRAME｜主策划澄清，2026-10-23经父聊天转达] 同一两赛窗口只要求实际参加至少一场；双报后只参加一场且冠军不因放弃另一场终止；实际参赛任一非冠军立即终止；末出场窗口关闭后实际出场数0才产生未履约。成功结局不清除尚未关闭窗口的义务；所有窗口处理结束再由赛事产生下一届。

[FRAME｜与赛事程序已对齐候选] D41全部锚点完成后、日结开始前关闭报名；D42入日初始化结束且任何当天事件（包括清晨/早餐）前冻结同届双赛；D42下午最后出场窗口关闭后检查实际出场数。此语义替换含糊的“首普通锚点前”，不允许早餐先改变参赛状态。

## [FRAME] 8. 结局与永久历史状态机

[FRAME｜技术决定] Running → AchievementTriggered → Presenting → NotePending → MainMenu；成功可Continue → normalContinuation，终止锁永不开放Continue。Triggered提交同时保存endingId/run唯一标记、history immutableSnapshot、真实UTC达成时间（仅展示）、模板资格、normalContinuation、presentationCursor、terminalLock。游戏条件不依赖现实时间。

[FRAME｜状态语义] normalContinuation包含node/stack、ChoiceToken或待选择描述、dayEndCursor、worldBarrier、pendingSetResult引用；演出cursor单独存。每已确认文本子事件推进演出cursor；未完成段从段首恢复。演出结束不等于已填备注；NoteState=Pending/CommittedEmpty/CommittedText，Draft独立。CommitNote或LeaveEmpty只允许从Pending一次提交；同OperationId重发返回原结果，异新ID不能改写已提交内容。

[FRAME｜成功恢复] ResultReady触发成功时，演出/备注/菜单后继续Settle，原结果不丢；若同点终止，pending结果可留存诊断但不允许结算。日结中触发成功只继续剩余步骤，不再次检查同一触发提交。多个结局同时满足只触发配置首个，其余等后续普通完成子事件；Continue自身不触发。

[FRAME｜历史不可变] 训练计划、角色/21区状态、游戏天数、结局名及内容版本快照在触发时复制，不保留可变领域对象引用。仅一次备注为该历史条目的允许补全字段；永久模板资格增量与history/局内trigger标记同根提交。新局替换currentRun且保留Permanent，历史不可加载成旧局。

[FRAME｜界面程序已对齐] 跨局已读键=(contentId,textRevision)，完整段阅读且推进后登记；大量已读条目归永久追加流而非无限扩张根。教学/知识卡/音频偏好跨局，有限开关在preferences；保存已读或偏好仍为辅助白名单提交，不完成当前未完业务。后台但进程存活时冻结小游戏阶段/错误/逻辑触点，重建输入遮罩后按策划0.35秒恢复，不重揭隐藏序列；进程死亡重载才按未锁结果重做/ResultReady不重玩。后台暂停不是CompleteNode，不推进随机或游戏时间。

## [FRAME] 9. 首版具体原子存储方案

[FRAME｜技术决定] 不建立泛化跨文件事务框架：逻辑上当前局/永久档分区，由同一个RootEnvelope发布可见性。目录使用state-a.bin、state-b.bin、commit-ledger.bin，加run记录/回执/轨迹和permanent历史/已读的追加数据流。每数据槽是完整工作根，不含历史正文；禁用各领域独立写文件。现有LocalJsonStore保留实验场用途，正式存储适配器扩展。

[FRAME｜有界工作根] 根只含当前角色状态、正在执行的树/组及下一步、当日差值、有限定义计数/资格、当前/确定未来占用、最近64回执、日志前缀。已完成旧树/旧组/旧日汇总/历史正文/完整回执/已读(contentId,textRevision)集合写追加流；不因日数累积这些对象进根。工作根预算≤256KiB（含5角色与当前流程；预算是工程目标，超出要优化/阻断验收，不能截断真实数据）；单组轨迹按引用外置，当前待结算结果保留必要小字段。未来占用仅保留尚未过去目标，完成后归档；配置产生的真实活动状态不可为达预算删除。

[FRAME｜追加流格式] 每流以1MiB目标大小滚动块，单帧不跨块；超大单帧独占并计预算异常。帧含kind/version、run/operation/semanticKey、sequence、length、payload、previousFrameHash、frameHash与trailer。Root.logPrefixes每流仅保存最后segmentOrdinal/byteOffset/frameHash及首块身份；块按序hash链接，段名可计算，根不列举全部块。只允许读取该前缀内的完整帧，未提交尾永不可见。UI按64条分页；历史/有效记录按日志读，不加载整局JSON。永久历史一次备注为独立NoteFinal帧，归并到原achievement，禁止覆盖正文；Pending草稿仅活动记录在根。

[FRAME｜跨局有界性] Root.logPrefixes仅容纳活动局固定种类流与永久固定种类流，不按历次RunId累积条目。替换当前局时，将旧局各流最终已提交前缀及校验引用写入归档manifest；manifest作为永久固定种类追加流管理，其候选前缀与新局根同次提交。旧局流无需常驻运行引用，按归档manifest定位，不加载全部旧局清单进根，也不开放旧局回读为游戏存档。本条只规定引用归档，不增加删除或清理政策。

[FRAME｜完整回执查询] 根64条只是热缓存。完整回执流保留operationId/hash/结果；磁盘派生索引存operationId→segment/offset，可用分块hash索引且只缓存有限页；索引命中必须回验前缀与帧hash。索引记录自己的已索引prefix；新提交后追赶，缺失或损坏时扫描权威已提交前缀重建，追赶完成前缓存未命中不能当作未执行。旧完成语义键由已完成游标和回执流联合拒绝。派生索引不参与事务，不因索引写失败回滚业务；大日志扫描恢复耗时需实测但不会迫使每次提交重写历史。

1. [FRAME] 启动获取单写者锁；扫描提交账本中连续完整记录（magic、格式version、recordLength、generation、slot、payloadLength、payloadHash、previousReceiptHash、operationId、recordChecksum、trailer）。最后完整有效收据指向唯一权威槽；不以槽mtime判断新旧。
2. [FRAME] 先向相关追加流写本次有效记录/回执/历史/轨迹帧，刷盘并校验，取得候选prefix。再选择非当前权威槽，生成含这些prefix的完整工作根UTF8载荷；槽头/尾包含generation、长度和SHA-256。覆盖非当前槽，Flush(true)，关闭重读校验DTO/长度/hash/根不变量。当前槽不动，旧root指向的已提交日志字节不可修改。
3. [FRAME] 校验通过后追加一条固定有界长度收据到账本并Flush(true)。此收据是线性化点；在支持的平台完成必要目录/元数据持久化（首次创建尤其需要），之后返回DurableReceipt。成功前不向玩家声明已保存。
4. [FRAME] 进程被杀时：日志新尾/完整新槽无收据=未提交，旧槽及其prefix仍权威；完整收据存在=采用新槽及新prefix，即使UI没收到ACK。尾部不完整收据仅视为未完成提交，恢复锁内截去该尾部后才追加；日志超出权威prefix的尾巴同样先隔离诊断再截去，不能被下次前缀意外吸收。不能跨坏收据继续扫描。完整记录校验失败为CorruptCommitted，不能假定只是未提交。
5. [FRAME] 最新有效收据引用槽损坏/丢失，或schema/content不兼容，停止加载并保留文件。不能用另一槽静默回退：它可能撤销已完成组、封局、备注或资格。未曾存在有效收据且存在异常文件也不是“首次无档”。
6. [FRAME] I/O返回异常后提交结果可能未知：暂停所有写入，重新扫描账本。若operationId已完整提交则发布原回执；否则保留旧权威状态并可用同ID重试；无法确认则StorageOutcomeUnknown，等待恢复入口而非继续改状态。

[FRAME｜文件寿命] 首版账本/数据流只追加，不自动删除有效记录或永久历史；旧局记录不再通过产品UI浏览，原文件保留到明确清理政策，诊断按用户主动清理。达到空间不足时保留当前权威槽及prefix，停止提交并可重试。所有已提交前缀在读取时校验；缺帧/坏hash停止对应读取及需该状态的恢复，不退旧根。诊断日志独立非权威，写失败不改变业务效果。

[FRAME｜损坏局与永久历史只读] 根槽内currentRun与permanentCore（含永久流prefix）独立长度/offset/摘要，整根有总hash；提交收据登记permanentCore段hash与offset/length。最新完整收据、该段和对应永久日志前缀全可验证时，允许CurrentCorruptPermanentReadable只读浏览历史；不能拿旧代际冒充最新历史。显式新局可基于验证后永久core/prefix构造新根并保留损坏原文件诊断副本；若永久段/日志/收据无法验证，阻止覆盖它的新局创建，重试/菜单/诊断，不把未知历史当空。最终失档处置交主策划。

[FRAME｜尺寸夹具而非玩法频率] 采用UI验证方建议：每角色每天200提交、5角色合1000提交/日；每提交Receipt512B+有效记录1024B=1536B；根256KiB；额外提交收据按256B估算。真实完整输入轨迹/历史快照另加，不藏在1536B内。生产允许更少/更多提交，此夹具用于观察随日数增长趋势。

| 天数 | 追加回执/记录总量（十进制MB） | v0.1全历史重写累计量（只算1536B累积正文） | v0.2累计根写量（256KiB/次） |
|---|---:|---:|
| [COMPUTED｜HIGH] 1 | 1.536 | 0.768768 GB | 0.262144 GB |
| [COMPUTED｜HIGH] 42 | 64.512 | 1354.784256 GB | 11.010048 GB |
| [COMPUTED｜HIGH] 84 | 129.024 | 5419.072512 GB | 22.020096 GB |
| [COMPUTED｜HIGH] 420 | 645.12 | 135475.52256 GB | 110.10048 GB |

[COMPUTED｜HIGH] 上表公式：提交数n=1000×天数；旧正文累计写=1536×n(n+1)/2，新根累计写=262144×n；这是累计写入量而非占用空间。420日两根约0.524288MB，回执/记录645.12MB，提交账本约107.52MB，合约753.164288MB再加段头/索引/真实轨迹/历史正文。单次常规提交约根262144+追加1536+收据256=263936B，不随旧日数增加。256KiB仍是偏保守压力根，实测应报告实际p50/p95及端上刷盘延迟，不把累积110GB误报成磁盘占用。

[FRAME｜新增故障要求] 日志已刷但根未提交尾不可见；根已提交却日志缺帧视损坏不得发半历史；滚动块创建/目录同步中断；索引落后/损坏重建后同operation不能再执行；64条缓存淘汰后重发仍返回原回执。首版结构不再让每步序列化整局历史，但这些测试仍须实施。

[INFERRED｜HIGH] 双槽能把写半个候选与有效检查点隔离；账本提供提交状态判断，但校验摘要不能修复损坏。Flush(true)与进程杀死测试不能独自证明所有安卓设备突然断电耐久性。

[FRAME｜平台验收门] 实施时校验目标安卓文件系统/运行时刷盘与必要父目录同步；必要时用小平台适配层完成fsync，不能凭File.Replace存在便宣称断电安全。首建文件、扩展账本、低存储、进程杀死、设备重启需分别验。暂不承诺未执行的硬断电测试结果。

[FRAME｜迁移] schemaVersion比程序新则只读阻断；旧版按显式version→version迁移函数转换完整根，在内存验所有片段和引用后以新generation提交，旧权威槽在确认前保留。contentHash缺失或动态树生成版本不兼容则阻断并定位，不拿新配置重新抽入口/伤病/评委。现有原型v1没有足够正式状态，不能伪造补齐成正式局；实验probe与正式存档目录分开。正式版本支持范围须按真实迁移测试登记。

## [FRAME] 10. 故障点与恢复验收表

| 故障点 | 唯一合法恢复/断言 |
|---|---|
| [FRAME] F01 候选显示后退出 | 不计次数；若ChoicePending已存，保留候选/Token，不能刷入口。 |
| [FRAME] F02 Start已存、普通事件未完 | 同实例重入；次数/占用一次；临时成本/效果都未提交。 |
| [FRAME] F03 小游戏中途退出 | 本组重做，组前输入/随机固定，之前完成组不动。 |
| [FRAME] F04 ResultReady已存、Settle未执行 | 恢复结算，不开小游戏；结果与来源/摘要不变。 |
| [FRAME] F05 Settle准备后、槽写入一半 | 权威旧根仍ResultReady；重新准备相同结算，判伤随机相同。 |
| [FRAME] F06 数据槽完整、收据未写 | 不采纳新槽，旧根恢复；不得展示已完成组。 |
| [FRAME] F07 收据已刷盘、ACK前退出 | 新根生效；同ID返回回执；成长/次数/时间不再加。 |
| [FRAME] F08 组已结算、组后剧情未完 | 只重读该剧情；收益、判伤、名额、时间保留。 |
| [FRAME] F09 休息已存、同期对话未完 | 休息效果保留；对话好感尚未发；重读后发一次。 |
| [FRAME] F10 教导/交易提交中断 | 成本+物品/权益+机会全有或全无；余额不足路径按领域业务结果处理。 |
| [FRAME] F11 保存计划时事件有暂存效果 | 仅计划和Receipt变；重开事件不获得暂存收益或扣款。 |
| [FRAME] F12 日结收入后恢复前退出 | 从恢复步骤继续，收入不重发；日结差值与全天值不重复。 |
| [FRAME] F13 2个NPC已到屏障、另2未到 | 已到者不重跑，未到者从各自cursor继续；世界不能越过屏障。 |
| [FRAME] F14 比赛快照提交后评分前退出 | 同届双赛同snapshotId/评委；日期或成长不重新冻结。 |
| [FRAME] F15 结局触发写盘中断 | history、局内标记、模板资格、正常后继和终止锁同有同无。 |
| [FRAME] F16 演出中退出/备注草稿退出 | 从未完成段恢复；草稿不等于已提交备注；再次空提交后不可补写。 |
| [FRAME] F17 Continue后ACK丢失 | 续玩语义键去重；不重复触发事件或立即扫描结局。 |
| [FRAME] F18 最新收据对应槽损坏 | CorruptCommitted停载，保留现场，不静默退旧/新局/解封终止。 |
| [FRAME] F19 入次日计划切换后刷新前退出 | 继续刷新步骤，旧计划不恢复；其余人物待生效变更按cursor继续。 |
| [FRAME] F20 迁移/新局写入中断 | 旧根或完整新根择一；永久历史不半丢；新局不复制旧局数值。 |

[FRAME｜验证方法] 每个故障点注入一次进程结束/写入异常，再重复原operationId与不同ID相同语义键；对照不间断基线的领域快照hash、结算键集合、随机drawIndex、历史数、占用、日期、游标、有效记录。另测同ID异载荷、旧视图、损坏头/正文/收据尾、未知schema、缺配置。尚未运行，不能把此表写成测试通过。

## [FRAME] 11. 分阶段实施包与S/AC覆盖

| 实施包 | 输入/出口与测试 |
|---|---|
| [FRAME] M0 公共协议 | 冻结本文封套、条件字段注册、所有者/错误枚举；领域提供合法与非法片段样例；不需要正式数值就能验证结构，但不能填0冒充配置。 |
| [FRAME] M1 保存与事件样本 | 双槽+账本/单写者、固定树/默认/选择/Start、真实首小游戏ResultReady→Settle、辅助计划隔离；F01—09/11/18基础故障通过才扩量。 |
| [FRAME] M2 世界与生活 | 交易跨领域、单人物真实自主→四人物屏障、日结/入日步骤；F10/12/13/19；验证同角色训练会话复用和资源隔离。 |
| [FRAME] M3 结局/赛事 | 真实双赛快照、三成功/终止、历史/备注/成功后继与剩余日结；F14—17/20。不能注入冠军替代全流程产品验收。 |
| [FRAME] M4—M6 全量/平台 | 8独立小游戏/4人物/3模板接同协议，全量内容校验、长局大小/保存延迟、安卓真机进程中断与离线测试；明确设备数据后定容量和性能阈值。 |

| S/AC | 本模块证据边界 |
|---|---|
| [FRAME] S03/AC03 | 固定/动态树共路、默认无限/冷却、一次入口、占用/解锁、日结与点击次日；F01/02/12/19。 |
| [FRAME] S11/AC12 | 配置有序首结局、每局一次、演出无递归、成功继续/终止锁；领域提供真实胜败/关系事实；F15—17。 |
| [FRAME] S13/AC14 | 完成组/待结算、计划隔离、永久历史/备注/模板、新局/迁移/损坏；F03—20。 |
| [FRAME] S14/AC15 | 配置错误定位、命令驱动/注入标记、故障日志和hash对照；构建/真机由验证任务交付。 |
| [FRAME] S01/AC01 | 离线本机文件和耐久门，真机证据尚缺；不是安卓验收已通过。 |
| [FRAME] S02/04—10、AC02/04—11 | 提供保存/身份/时间/世界/交易/快照边界，不代替计划、8小游戏、身体、经济、人物和比赛规则验收。 |
| [FRAME] S12/AC13 | 查询脱敏/公开反馈/只读历史/恢复页面接口，UI任务负责触摸与阅读验证。 |

## [FRAME] 12. 对齐记录与待办

- [COMPUTED｜HIGH] 父聊天 01a1629c-9160-7251-a24e-7e4341eb9d61 已映射本任务与4程序、指定策划；授权读取及职责内互发。本文唯一写入路径为 Docs/Technical/FlowPersistenceContracts.md。
- [COMPUTED｜HIGH] 流程策划 01a16465-e99b-78d3-8e32-9bb27d0393f1 已完整审阅并明确接受Start不扫描、占用恢复、ResultReady扫描且Settle后继、损坏不静默退旧、一次备注三态、演出游标及E/N精确次序；其§6/F07/§10已修末窗口0出场和占用冲突，本文已回读核对。新增训练超时/进程存活后台语义也已收到。
- [COMPUTED｜HIGH] 训练程序 01a16469-00b1-7fa2-babd-e93860245256 已提供动态身份/冻结计划/ResultReady与Settle需求；我已确认ResultReady可触发结局并保存Settle后继。
- [COMPUTED｜HIGH] 生活自主程序 01a16469-0929-7700-9251-5dd684ac142a 提供Actor私有域、逐人物cursor与同actor/day训练会话复用；我已反馈arrivalReady/barrierReached及单写者结构。
- [COMPUTED｜HIGH] 计划赛事程序 01a16469-12e2-75d0-8383-b83108d7dd45 提出计划隔离、ApplyPendingPlan重入、同届双赛同FreezeToken、首胜后第二赛可失败；我已确认结构。
- [COMPUTED｜HIGH] UI验证程序 01a16469-1cf0-7361-aa0f-264a36dd13a8 提出同ID回执/异载荷冲突、持久化后通知、查询版本和能力来源；我已确认契约名称。
- [COMPUTED｜HIGH] 四程序均已回读签收公共字段/命令/恢复边界；生活确认每笔购买独立保存，训练确认整数gameSeconds与日初配额，赛事确认固定时间窗口不可被消耗漏检，界面确认查询与反馈分离。训练策划TG-0.2已修为先恢复裁剪、再追加不足压力，流程稿补饱和算例；此冲突关闭。
- [COMPUTED｜HIGH] 赛事程序随后签收v0.2四维结果：SavePlan/Enroll/Unenroll为AuxiliaryOnly/None；Attend把实际出场计数与比分同提交，非冠军为处理成功及NonChampion事实；Decline只消费本场机会、不造败绩；末窗口为玩家固定检查节点CompleteNode/NormalCompletion，不在容器收尾隐式检查。其稿已同步日志前缀回执查询和N02后训练配额归属。
- [COMPUTED｜HIGH] 父聊天已全文审阅v0.2，接受有界工作根+追加可见前缀+commit收据作为首版技术决定，四维PreparedOutcome通过方案审查。总审要求的压力顺序措辞修正与跨局logPrefixes有界说明已补入本文；实际故障/平台测试未执行。
- [FRAME｜待办] 本文产品恢复协议已获指定策划确认，技术方案本次总审通过；常规参数仍为候选，非用户最终数值批准。剩余实施门槛是真实迁移范围与目标安卓耐久/容量/延迟测试；无尚待策划决定才能表达的恢复状态，未宣称业务已实现或测试通过。

[RULES I BROKE]: 无。
