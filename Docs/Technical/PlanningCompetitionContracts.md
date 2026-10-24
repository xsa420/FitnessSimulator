# [FRAME] 开局、计划与比赛实施契约

[FRAME｜定位] 本文件仅为可实施技术方案；唯一写入范围为本文，未实现玩法。安卓离线横屏文本首版保留8种独立可操作训练小游戏、21区和4名自主人物。产品参数、候选业务规则、技术决定分别标识；技术方案不把候选赛日或候选失败条件变成产品决定。

## [COMPUTED] 阅读证据与差异

- [COMPUTED｜HIGH] 2026-10-23读取在线主文档 `2bc3e8eb2dfd80bcb359f603fc989244`、`Docs/ProgramModulesAndDependencies.md`、`Docs/Design/ProductionAlignment.md`、`README.md`、`Docs/Architecture.md`、`Docs/Verification.md`及训练、存档原型源码。
- [COMPUTED｜HIGH] `TrainingContracts.cs`仍含historicalPerformanceSourceId和consecutiveStrengthSets；`SaveContracts.cs`仅有activePlanJson/pendingPlanJson等外壳。没有据此认定正式计划、赛事或跨领域初始化已实现。
- [FRAME｜既定] 在线主文档优先：自动容量组按当前目标量执行、保留完成组。旧架构整动作回退、历史成绩复用和旧人员分工不作为本方案依据。
- [INFERRED｜HIGH] 展示用动作目录不是正式21区贡献配置；全部区域可训练必须由新动作数据校验，不能以8个展示场景存在作为证据。

## [FRAME] 规则分层与状态所有权

| 类别 | 契约 |
|---|---|
| [FRAME｜既定] 开局 | 1默认+2永久解锁模板；选模板后选预设或局内自定义计划；不跨局继承身体、钱、关系、物品。 |
| [FRAME｜既定] 计划 | 首次当天生效；修改次日生效；同日保存最后版；自然星期/生效日起天数循环；每天最多一个训练锚点，漏训不补不顺延。 |
| [FRAME｜既定] 比赛 | 两赛入口与倒计时、每场3不重复评委稳定抽取、三人平均、与真实竞争者比分；必须至少参加一种、参赛未夺冠终止，恋爱成功不豁免。 |
| [FRAME｜候选参数] 赛程 | D8开放、D14/28/35披露、D41末截止、D42上午健美/下午薄肌、42日后续周期均来自专项策划候选，不能作为用户已定。 |
| [FRAME｜既定解释] 同届义务 | 主策划确认本次两赛窗口实际出场≥1；实际参赛非冠军终止。双报放弃一项而另一项冠军不失败；零出场只在最后窗口关闭判定。未来每届重复义务仍为候选。 |
| [FRAME｜技术决定] 身份 | RunId/ActorId/ConfigVersion引用公共封套；EditionId=(RunId,ScheduleId,CycleOrdinal)，ContestId=(EditionId,ContestKind)，不用当前天数或界面顺序单独充当实例ID。 |
| [FRAME｜技术决定] 写权 | 模板/计划模块仅写选择、初始化状态、计划版本；比赛仅写赛程、报名、评委、冻结、比分及履约事实。身体、钱包、关系由各领域写；日期/流程由流程写；永久资格与历史/终止锁由进度结局写；持久化不重算评分。 |

## [FRAME] 开局跨领域初始化

| 数据 | 字段与语义 |
|---|---|
| [FRAME] TemplateDefinition | templateId/version、displayText、eligibilityConditionRef、bodyInitRef、economyInitRef、relationshipInitRef、actionProgressInitRef、actorRosterInitRef；每个引用版本固定，不复制身体公式。 |
| [FRAME] NewRunDraft | draftId、selectedTemplateId/version、selectedPlanDraft、validatedEligibilityRevision；未确认草稿不覆盖当前局。 |
| [FRAME] RunInitialization | newRunId、initOperationId、templateRef、configBundleVersion、initialDate、actorIds、domainInitIntentRefs、phase=Prepared/Committed；所有领域同一逻辑提交完成前不可进入事件。 |
| [FRAME] PermanentEligibilityView | profileRevision、eligibleTemplateIds、grantEvidenceIds；仅只读资格，不含上局数值。 |
| [FRAME] PlanOwner | runId+actorId；玩家与四人物使用相同计划模型和隔离命名空间，不共享可变数组。 |

[FRAME｜技术决定] `PrepareNewRun(template, plan, eligibilityRevision)`重新校验资格与配置，调用身体/经济/关系/人物领域准备初始化意图，返回完整诊断或PreparedInitialization。`CommitNewRun`由公共协调器原子发布新局与首计划；失败仍保留原局。永久档只读取已提交资格，初始化不得替代GrantTemplateEligibility。新局计划循环基准取初始日，星期由世界日期提供；不存在依机器时区计算计划星期。

[FRAME｜候选参数] 策划目前提出全21区力量10/13/8、体脂25/29/20、基础魅力50/40/65、心理压力10/20/15、富裕0/0/10及余额300/300/360；仅作为待审配置引用，不是本文件第二套初值权威。最终模板完整字段须由各领域验证，缺值报MissingTemplateField，不静默补0。

[FRAME｜技术决定] 重试同initOperationId且同payload返回原提交结果；同ID不同payload报IdempotencyConflict。新局覆盖是一次根切换，不能先清空当前局再逐领域初始化；实际落盘/故障恢复由公共存档契约负责。

## [FRAME] 计划结构、资格与日切

| 数据 | 字段与语义 |
|---|---|
| [FRAME] PlanRevision | planId、revision、owner、configVersion、mode=Week/Cycle、effectiveDay、cycleLength、slots、contentHash；已保存版本不可变。 |
| [FRAME] PlanSlot | slotKey=weekday或cycleIndex、kind=Rest/Training、anchorId、combinationId或Free、volumePreset、orderedEntries；Rest不带训练动作。 |
| [FRAME] PlanEntry | entryId稳定条目身份、exerciseId、exerciseDefinitionVersion、targetSets、targetRepetitions、restPresetRef；同动作若允许重复仍必须不同entryId。 |
| [FRAME] PlanState | activeRevisionRef、pendingRevisionRef?、pendingEffectiveDay?、stateRevision；编辑草稿由界面持有，保存后才替换pending。 |
| [FRAME] DayPlanSnapshot | runId/actorId/day、planRevision、effectiveDay、anchorId?、orderedEntries、source=Base/PartnerOverride、sourceBindingId?、snapshotHash；休息日明确空训练，不以缺计划表示休息。 |

[FRAME｜既定] Week按当天实际星期查表；Cycle索引为(day-effectiveDay) mod cycleLength，首次日为0。按次编辑只是同一slots投影，不能另建“完成一次才推进”的队列。

[FRAME｜技术决定] `ValidatePlan`检查：周期长度正数、星期键完整、每天最多一训练锚点；动作引用属于正式8动作；组合通过动作目标区域与组合肌群有交集判断，不凭动作名称；每动作三档量引用存在且目标组数/次数合法；锚点在effectiveDay已解锁或已承诺次日解锁。错误返回字段路径与原因，不写入任何版本。

[FRAME｜既定] 受伤属于运行训练资格；计划保存不通过删除受伤动作重写今天顺序。`GetExerciseEligibility(actor,entry,stateRevision)`由训练/身体返回Allowed或Blocked及受伤区域，执行器按既定伤病规则结束/跳过不可训练动作，不能临场加动作补齐。力量门槛/梯度挑战资格不是计划编辑器自行计算。

[FRAME｜技术决定] `SaveInitialPlan`仅无有效计划时允许，effectiveDay=today；`SavePlanRevision`只接受expectedPlanRevision，effectiveDay=tomorrow，同日后存覆盖pending引用但不删除审计依据。`ApplyPendingPlan(day,worldStepKey)`仅由日切协调器调用，原子切换active并清pending；重复步骤返回原结果。今日DayPlanSnapshot及训练已冻结配额不变。

[FRAME｜技术决定] 查询`ResolveDayPlan(actor,day)`返回指定已生效版本及不可变安排；未来预览明确preview标记，不能作为执行依据。训练拥有(run,actor,day)唯一session claim、动态树及当天配额，计划不拥有“训练已完成”计数。未进入训练事件，记漏训事实由事件/记录处理，明日直接解析明日slot。

[FRAME｜候选基线已对齐] 同一训练内禁止重复动作，训练日至少1条，循环长度1..14；全休息计划合法。跨日组合可重复，独立entryId仍保留。restPreset仅默认建议，不覆盖每次实际休息选择。搭子复制锚点、有序动作、档位组次；自身梯度、自动资格和伤病独立，basePlan不被覆盖，解绑次日恢复自有预设计划。

[FRAME｜已对齐技术] SavePlan只基于最新committed根修改白名单计划片段，不序列化当前未完成事件工作副本；其他事务版本变化需重新Prepare，不能覆写最新根。打开草稿/放弃草稿不写业务状态。已保存pending在普通事件回滚时仍保留，由公共恢复协议重建。

## [FRAME] 赛事结构与状态机

| 数据 | 字段与语义 |
|---|---|
| [FRAME] ScheduleDefinition | scheduleId/version、openOffset、enrollmentDeadline、contestSlots[2]、disclosureMilestones、nextEditionRule、obligationPolicyRef、tiePolicyRef、scoringPolicyRef；时间全部逻辑日/锚点/阶段，不用现实钟。 |
| [FRAME] EditionState | editionId、originDay、resolvedCalendar、configVersion、phase=Scheduled/Open/Closed/FreezePending/Frozen/Resolving/Completed、obligationStatus、freezeTokenRef、nextEditionId?。 |
| [FRAME] ContestState | contestId、kind、registration=NotEnrolled/Enrolled/Withdrawn、attendance=NotDue/Pending/Attended/Declined/WindowClosedAbsent、resolution=Unscored/Scored/Published、judges[3]、judgeDrawReceipt、disclosureStage、snapshotRef、scoreResultRef；注册/出席/评分不可压成一个bool。 |
| [FRAME] FreezeToken | editionId、worldBarrierId、day、phase、actorRevisions[]、participantSnapshotIds[]、configVersion；公共封套由流程定义，赛事仅消费。 |
| [FRAME] ParticipantSnapshot | snapshotId、actorId、sourceCommitId、bodyRevision、regionSchemaVersion、strengthByRegion[21]、bodyFatPercent、baseCharm、source=Player/AutonomousActor、growthProvenanceRef；完整不可变，缺字段不等于0。 |
| [FRAME] ContestResult | resultId、contestId、snapshotHash、judgePolicyVersions、perJudgeComponents、perJudgeScore、meanScore、rank、coWinnerIds、tieResolution、scoringVersion、resultHash；展示精度与排名精度明确分开。 |
| [FRAME] ParticipationFact | factId、editionId、contestId?、actorId、kind=EnrollmentClosedWithoutEntry/Attended/Champion/NonChampion/MissedEnrolledContest/EditionWindowClosed/ObligationSatisfied/Unfulfilled、actualAttendanceCount、absenceReason、sourceCommitId、policyVersion、occurredDay；未报名是记录事实，只有最后窗口0出场才Unfulfilled。不含可自行写入的RunTerminated。 |

[FRAME｜技术决定] 赛事入口开放操作一次建立届及两场；评委按场次专用随机用途抽取3个不同ID并随场次保存。池不足3个报配置错误；查询/重载/披露不抽随机。披露阶段单调推进，显示只读投影不改名单。

[FRAME｜候选流程] Scheduled→Open允许Enroll/Unenroll；D41全部锚点结束且日结前Closed拒绝报名/退报；日初屏障后FreezePending→Frozen；固定出场节点接受Attend或DeclineAttendance。Attend进入Attended→Scored→Published；放弃关闭本场资格不算失败，退出应用仍Pending。D42下午最后窗口关闭产生EditionWindowClosed，此时actualAttendanceCount=0才Unfulfilled；出场≥1且没有NonChampion则已履约。Completed后才准备下一届。失败事实送结局策略，本模块不写终止锁。

[FRAME｜技术决定] `Enroll/Unenroll(contestId,expectedRevision)`仅开放窗口可用，不耗锚点；重复同状态返回AlreadyApplied，退报更新保留时段意图。`Attend/DeclineAttendance(contestId,choiceToken)`仅当前固定事件可用，放弃预览实际出场数及剩余机会；消费同一选择令牌后不可反悔。关闭页面/退出应用不推定弃赛。报名遇已提交未来占用冲突时拒绝该场并展示冲突，不能撤销旧成本。

[FRAME｜技术决定] `EvaluateSchedule(worldStep)`只返回DueIntents与事实意图，不推进日期。`FreezeEdition(token)`验证同届所有参赛者同一阶段、版本与真实来源；返回WaitingForBarrier可恢复等待，不能先冻玩家再让对手成长。`ScoreContest`是无随机纯计算；`PublishContestResult`才写公开结果和Champion/NonChampion事实，结局不重新评分。

## [FRAME] 同日双赛屏障与解耦

[FRAME｜候选基线已对齐] DayOpenReady：D-1所有人物推进完成，D日计划/搭子切换和日初初始化完成，D日任何事件（包括清晨/用餐）尚未执行，冻结全参赛名单快照。两赛复用同一cohort。赛事策划已接受该精确时点，替换此前歧义的“首普通锚点前”；具体世界游标仍引用公共流程契约。

[FRAME｜技术决定] 玩家与竞争者必须同day/phase，不能拿玩家D日开始与竞争者D日训练结束相比；四人物自主驱动保留，比赛仅要求实际参赛者输入。最小两人名单是策划候选，其他人物不因实现方便新增参赛义务。比赛不能调用人物私有策略直接加力量来填快照。

[FRAME｜技术决定] 同届两赛只预先冻结输入，不预生成下午比分；Attend、实际出场计数、评分结果及发布事实在该场同一逻辑提交。第一赛奖励、关系变化、成功历史不会重写已冻身体。第一赛成功只中断流程到结局演出，恢复ContinuationToken继续第二赛正常后继；第一赛失败后结局封局，下午不冒充已经参加/失败。独立评分夹具覆盖两类失败分支，但正式流程只计算发布实际出场比赛。

[FRAME｜技术决定] `GetContestView`只返回已披露评委、带日期的对手信息、报名合法操作、倒计时及已发布比分；正式完整快照供受限评分/验证端口。`GetThinBuildIndex`独立于评委、场次及抽样，不要求创建赛事；返回value、metricVersion、bodyRevision、regionSchemaVersion、componentExplanation，不含体脂或魅力。公式产品定义由赛事策划维护，计算契约/配置放身体域的中性BodyMetrics，比赛与关系都依赖BodyMetrics；身体不得引用比赛实例模块，避免双向依赖。

[FRAME｜技术决定] 必达介绍、披露、保留时段、报名截止和出场截止全部注册到统一事件/时间能力：EvaluateSchedule只提交固定节点/保留意图，统一执行器用稳定nodeInstanceId执行并记统计、保存。保留赛事锚点在入口解析前排除冲突普通候选，固定赛事入口只解析一次；恢复沿已选入口继续，不再抽普通入口。没有报名时仍走普通池及无限兜底。日界/窗口检查挂世界步骤，不依赖玩家进入赛事页面。

[FRAME｜候选验收] D42已报上午健美且计划休息：全天学习因消耗保留时段而在候选过滤中非法，显示冲突，不执行半天后截断；午餐独立锚点仍可执行。未报名者可按普通规则全天学习，但即使其显式消耗下午，世界推进也必须先后执行下午窗口关闭检查，0出场产生Unfulfilled。D8介绍与赛事固定节点都沿公共事件提交，不能另开绕过统计/恢复的赛程执行器。

## [FRAME] 评分与全区覆盖

[FRAME｜既定] 身体接口必须分开BaseCharm与SocialEffectiveCharm；比赛/表白只读前者。BodyFatPercent只进入独立体脂适配项，不能先修正魅力或ThinBuildIndex再叠加脂肪分。健美每区力量参与，薄肌适中而非力量线性越大越好。

[FRAME｜候选公式] 当前策划共享薄肌指标为L=mean(h(S_i))×(0.8+0.2×(1-clamp(populationStd(S)/14,0,1)))；h在S=0/18/26/48取0/1/1/0，段间线性，48以上为0。精确定义/权重版本最终引用专项策划稿，不能在关系模块复制另一份L。体脂与基础魅力不输入该函数。

[COMPUTED｜HIGH] 按上述候选：21区全0时L=0，全10时L=10/18≈0.555556，全22时L=1，全48时L=0；同力量分布改体脂或魅力，L保持不变。这是游戏候选函数验算，不是现实身体评价。

[FRAME｜技术决定] 评分服务输入完整StrengthMap和版本，不接受只有总力量/均值的替代；缺一个区域、重复区域ID、非有限数、越界、缺评委参数、名单不足、不同屏障都返回InvalidScoreInput。允许完整合法的全0，并按公式计算，不能把合法零与缺数据混淆。

[FRAME｜验收门槛] 区域目录唯一：胸3、背4、腿4、肩3、二头2、三头3、腹1、臀1，共21；动作唯一：卧推、哑铃弯举、高位下拉、绳索下压、深蹲、臀推、肩推、侧平举。动作贡献矩阵每行比例合计1、正贡献引用合法，每列至少一正式动作正贡献。评分偏好组亦引用同一RegionId集合。不得新增第9动作补覆盖或给未覆盖区自动成长，缺口由训练策划修订8动作候选映射。

[FRAME｜候选基线已对齐] 完整评分定义引用`CompetitionAndStartSpec.md`§5—7：健美M=mean(S)/100、U=min(S)/100、G=偏好均值/100；薄肌L及H=偏好h均值、c=C/100；各评委按其4项权重与独立T体脂适配求分。配置字段为judgeId/kind/preferredRegionIds/weights[4]/bodyFatOptimalLower/Upper，权重非负且和1、5<lower≤upper<50，区域集合非空合法；平衡评委G=M、H=L。两池各4名的具体内容不复制到技术稿。

[FRAME｜技术决定，策划已认可] 每评委按十进制四舍五入4位后转换为ScoreUnits=score×10000整数，比较三人ScoreUnits之和；显示均分2位，不拿显示值排序。候选平分键：健美U→M；薄肌L→mean(T)→c；全部一致为并列冠军，禁止ActorId/随机破平。次级键统一归一为0..1后十进制四舍五入到1e-6整数比较；无策略报MissingTiePolicy。运算顺序、总体标准差及舍入模式必须随scoringVersion固定。

[FRAME｜技术决定，训练程序接口对齐] 标准差调用身体域唯一SqrtRational12V1，不调用平台Math.Sqrt：总体方差以非负精确有理a/b表示（b>0，单位为力量单位平方）；令k=isqrt(floor(a×10^24/b))，若4a×10^24≥b(2k+1)^2则返回(k+1)/10^12，否则k/10^12。L后续按有理公式计算、最终指标按1e-6非负half-up；禁止评分模块自行换平方根或中间截断。scoringVersion绑定metricVersion+sqrtKernelVersion，并写入结果证据；整数中间量需任意精度或有证明的足够宽度，溢出报错，不退回浮点。

[COMPUTED｜HIGH] 已用独立整数验算核对该核：输入0→0、1→1、2→1.414213562373、1/4→0.5；输入方差1/(4×10^24)恰处0.5个输出量子，half-up得到10^-12；1/(5×10^24)得到0。比较式来自sqrt(a/b)×10^12与k+1/2的非负平方比较，整除只用于寻找下界k，半量子比较使用原分子分母，不丢余数。上述为算法验算，未来实际目标运行时仍需相同金样本和比分边界测试。

[FRAME｜技术决定，最终消费边界] 身体微单位整数s_i下，总体方差V=(21Σs_i²−(Σs_i)²)/(441×10^12)，只由BodyMetrics调用平方根核。metricVersion固定为BodyMetricsL_V1_Sqrt12_Micro6，numericKernelVersion为NumericRationalV1，sqrtKernelVersion为SqrtRational12V1；scoringVersion同时绑定以上指标/核及ScoreUnits4/TieUnits6版本。比赛与关系均消费LUnits/10^6，评分不得使用解释字段中的量化前L，L平分键直接取LUnits，不再次量化。平衡评委H=L亦使用此量化值；其他偏好H继续按其配置公式计算。冻结输入保存指标版本与BodyRevision，缺旧版本不能用新核重算历史。

[COMPUTED｜HIGH] 新增金样本独立复算：20区S20、1区S21的总体方差为20/441；以身体核给出的Std12=212958855000计算，LUnits=996958。根后纯函数夹具h均值1、Std12为6999965000001/6999965000000/6999964999999时，LUnits分别900000/900001/900001；该夹具不是可达身体向量。后续跨运行时测试共用身体稿BM01/BM02期望整数，不维护赛事专属平方根实现。

[COMPUTED｜HIGH] 已独立复算策划§7四个全区等值夹具：健美S20/b20的三分33.6471、32.6667、38.4375，均34.9171；S18/b20的三分32.0471、31.0667、36.9375，均33.3504333。薄肌S20/b20/C50的三分90、90.9375、85，均88.6458333；S30/b20/C50为80、80.0284、76.8182，均78.9488667，均与策划显示精度一致。

[COMPUTED｜HIGH] 已读取`TrainingAndGrowthSpec.md`§1并独立验算8行贡献合计均100%，正贡献区域并集恰为21。腹部由深蹲5%/臀推10%，小腿由深蹲10%，下背由深蹲5%/臀推10%覆盖；这是候选配置的静态覆盖通过，不证明低贡献部位在42日内可达到冠军或薄肌平台。完整映射只由身体/训练配置维护，程序ID引用其RegionSchema。

## [FRAME] 提交、恢复与错误

| 提交边界 | 必须一起保存 / 中断结果 |
|---|---|
| [FRAME] 开局 | 新Run根、各领域初始化、首计划、日期与配置版本；发布前失败原局保留，发布后重试返回同局。 |
| [FRAME] 计划保存 | 仅active/pending计划白名单与操作账本；不夹带未完成事件扣费/抽取；恢复已保存最后版。 |
| [FRAME] 开届/评委 | 届/两场身份、日历、随机receipt/位置、抽定评委与记录；中断不改抽样。 |
| [FRAME] 报名/截止 | 报名修订或截止状态、事实、世界步骤标志；重放不重复报名或漏赛事实。 |
| [FRAME] 冻结 | FreezeToken、全部参赛者快照和hash同一根；只有完整token后才能评分，半快照不发布。 |
| [FRAME] 发布比分 | 出场计数、immutable result、Published标志、赛事事实、结局检查候选及正常后继同逻辑提交；历史/资格由结局准备，公共持久化保存同一根封套。 |
| [FRAME] 续玩/下届 | 恢复正常后继，不重算/重发首赛；CreateNextEdition键=(run,schedule,nextOrdinal)，只在产品定义的本届收尾边界创建一次。 |

[FRAME｜技术决定] 领域命令沿用公共CommandEnvelope的operationId/source/expectedRevision/payloadHash；领域唯一键为初始化、计划修订、报名、Freeze、Publish、下届编号。先验证身份权限并查操作收据，同操作同hash直接返回原收据；未见操作才检查revision，旧revision报StaleState并返回新查询令牌。相同operationId异hash报OperationConflict；不同操作却命中同业务结算键返回AlreadyApplied。缺参数/引用错误是配置失败，暂缺人物屏障是等待，逾期报名是业务拒绝，冠军失败是有效业务结果，四者不得混成回滚或默认成功。

[FRAME｜技术决定] 在赛前快照与比分均已提交后，不因新加载配置改变旧结果；引用的旧配置不可加载时必须走迁移/版本错误而非重新抽评委。迁移决定由存档与配置模块承担，本文仅要求保留旧结果证据。

### [FRAME] 公共v0.2结果语义接入

[FRAME｜技术决定] PreparedOutcome分别填写outcomeKind、eventDisposition、checkKind、opportunityPolicy；不能从BusinessFailure推断节点完成、机会消耗或结局检查。具体枚举约束引用公共稿§3，赛事不得自行扩大合法组合。

| 本模块操作 | 公共结果映射 |
|---|---|
| [FRAME] SavePlan、开放期Enroll/Unenroll | [FRAME] Success / AuxiliaryOnly / None / Keep；保存计划或报名与保留意图，不推进当前事件、不额外扫描结局。 |
| [FRAME] 等待选手屏障/出场选择 | [FRAME] WaitingInput / KeepOpen / None / Keep；不算已经参赛，不生成比分。 |
| [FRAME] Attend并发布冠军或非冠军 | [FRAME] 处理成功为Success / CompleteNode / NormalCompletion / Consume；非冠军通过NonChampion事实表达，不把成功计算出的落败结果误当技术失败。出场机会及出场计数同提交。 |
| [FRAME] DeclineAttendance | [FRAME] Success / CompleteNode / NormalCompletion / Consume；消耗本场出场机会，未履约仅由最后窗口事实判断，不直接造败绩。 |
| [FRAME] 最后窗口关闭 | [FRAME] 由统一时间推进必经的玩家固定检查节点CompleteNode / NormalCompletion执行，事实含实际出场数；不在容器收尾或辅助查询隐式扫描。流程负责在进入后续节点前完成该检查，即使普通锚点被消耗。 |
| [FRAME] 过期报名/非法计划/旧选择令牌 | [FRAME] Rejected或ConfigError按错误类别返回，不完成当前节点、不消耗出场机会；若需保存拒绝回执，使用无效果检查点且checkKind=None。 |

[FRAME｜技术决定] 日初N02完成计划/搭子生效后，由训练准备PrepareDayQuota；计划仅提供不可变输入，不自行结算疲劳。历史、完整旧回执和有效记录使用公共v0.2追加日志及根的已提交前缀，领域不另存文件；最近回执缓存未命中必须查询持久账本，不能因淘汰而再次报名、发布比分或生成同届。根/块/分页预算引用公共稿§9，不在本模块复制独立阈值。

## [FRAME] 状态表与验收样例

| 情境 | 输入/过程 | 可验收输出 |
|---|---|---|
| [FRAME] 单赛冠军 | 玩家三评委80/82/84，对手79/81/83；合法已参加 | [COMPUTED] 平均82对81；[FRAME] Champion事实，结局成功，继续后不重复历史/资格。该算例验证平均与比较，不替代实际属性评分。 |
| [FRAME] 真平分 | 两人三评委均70/80/90且次级键也同 | [COMPUTED] 两者平均80；[FRAME] 候选策略为并列冠军；无tiePolicy拒绝发布。若只是总分同，先按量化次级键比较。 |
| [FRAME] 完全未报名 | D41截止两项NotEnrolled，D42下午最后窗口关闭 | [FRAME] D41只记EnrollmentClosedWithoutEntry；D42窗口关闭actualAttendanceCount=0才Unfulfilled并交流程终止。 |
| [FRAME] 已报名错过 | 单报健美但明确放弃，下午无报名 | [FRAME] 上午MissedEnrolledContest不伪造比分；下午最后窗口0出场才未履约。关应用恢复待出场，不算错过。 |
| [FRAME] 双报放弃一项 | 上午冠军下午放弃，或上午放弃下午冠军 | [FRAME] actualAttendanceCount=1且无NonChampion，均可续玩；无未履约/败绩。 |
| [FRAME] 双赛失败 | 两项报名，上午非冠军，下午潜在非冠军 | [FRAME] 上午非冠军触发终止；下午仍未发布/未参加，不能双写失败历史。另独立评分测试两赛失败分支均覆盖。 |
| [FRAME] 首赛成功次赛失败 | 上午冠军，完成记录后续玩，下午非冠军 | [FRAME] 相同冻结快照、保留上午历史、下午失败终止；成功不可覆盖失败终止。 |
| [FRAME] 恋爱后仍参赛 | 恋爱成功继续，赛程/报名不满足 | [FRAME] 赛程义务仍在，按比赛事实判定，不授免赛。 |
| [FRAME] 续玩新周期 | 本届所需赛事处理完、局仍可继续 | [FRAME] 依候选nextEditionRule生成新ordinal和新场次/新评委，旧结果不可变；42日仅测试配置样例。 |
| [FRAME] 重开 | 已永久解锁模板，新局选择该模板 | [FRAME] 新runId、cycleOrdinal重置、重新初始化全部领域；保留资格/历史，不继承旧报名、评委、计划、身体或钱。 |
| [FRAME] 计划日切 | D3保存v2再保存v3；D3未抽训练 | [FRAME] D3仍v1且漏训；D4切v3，循环第1项；v2不执行。自然星期模式D4读当天星期而非循环第1项。 |
| [FRAME] 计划中断 | 未完成事件已临时扣费时另存pending，再退出 | [FRAME] pending保留、事件成本依普通恢复规则回退，不因计划保存偷渡扣费。 |
| [FRAME] 双赛公平 | 两赛同一token，首赛后奖励改魅力；尝试对手D日训练后快照 | [FRAME] 两赛仍用冻结基础魅力，日期阶段不同快照拒绝；第二赛信息未到发布点不可见。 |
| [FRAME] 全区/薄肌 | 缺腹部输入、全0、全22、全48四组 | [FRAME] 缺区报错；合法零正常评分；按候选L为0/1/0；更改体脂不改变L。 |

## [FRAME] 分阶段实施包与S/AC映射

| 包 | 可独立交付与依赖 | 验证/覆盖 |
|---|---|---|
| [FRAME] P0 契约与校验 | 模板引用、计划字段、场次身份、RegionSchema；依赖公共提交和配置版本 | [FRAME] S02/S04/S10/S14；AC02/04/11/15，缺字段/权重/重复ID/21区覆盖诊断。 |
| [FRAME] P1 开局计划 | 初始化意图+独立计划保存+日切+当天只读安排；接身体/钱包/关系最小领域 | [FRAME] S02/S03/S04/S13；AC02/03/04/14，三模板资格、次日版本、漏训、不跨局继承及故障恢复。 |
| [FRAME] P2 纯评分与视图 | 共享L、两类评委、精度/平分、披露投影；先人工固定输入，不能称真实人物验收 | [FRAME] S05/S10/S12；AC05/11/13，实际公式可复算、体脂单计、真实零/缺值和隐藏信息。 |
| [FRAME] P3 人物与赛事整合 | 日初屏障、真实竞争者来源、报名/固定赛事入口、发布事实；依赖4人物真实推进 | [FRAME] S09/S10/S11；AC10/11/12，同日双赛与漏赛/非冠军/恋爱后参赛完整矩阵。 |
| [FRAME] P4 持久化和连续周期 | 演出/后继、历史/资格、次届、重开；接公共存档 | [FRAME] S11/S13/S14；AC12/14/15，每一提交前后故障注入，评委/结果/事实/解锁均一次；长期续玩不同届不串身份。 |

[FRAME] 文本界面和脚本调用同一命令；小游戏期间是否禁计划入口由表现/训练合法操作视图决定，禁止绕过限制直接写计划。后续真实安卓触摸与节奏验证由验收任务负责，本文不把纯评分样例称为2—4小时体验已达成。

## [FRAME] 对齐记录与未关闭事项

- [COMPUTED｜HIGH] 已全文审阅指定赛事策划 `01a16465-f833-7410-8cd7-d20fb2dc6dfa`的`CompetitionAndStartSpec.md`。其消息明确确认：最小实际出场义务、D41截止/D42冻结与最后窗口检查、候选禁止同日重复动作、搭子同步、共享L、4位单分及1e-6次级键；已纳入并请求其全文复审。
- [COMPUTED｜HIGH] 该策划随后完成修订稿复审，明确回复“与我稿产品语义一致”“可记录策划复审通过（规格层，不是AC实现通过）”；覆盖报名/履约分离、日初冻结、出场与比分同提交、不提前算下午、L中性查询、精度及组量同步。赛事D8/14/28/35介绍披露执行于其与流程统一的N04固定步骤，仍由统一事件保存/统计。
- [COMPUTED｜HIGH] 事件程序 `01a16468-f8be-7cd2-bfb2-871b7a166e0e`确认计划白名单独立提交、幂等ApplyPendingPlan、FreezeToken字段、ContinuationToken及赛事只提交事实不写终止锁；公共定义以其`FlowPersistenceContracts.md`为准。
- [COMPUTED｜HIGH] 事件程序追加确认：只在报名时原子保留时段，退报原子释放；未报名不提前预占。普通入口被显式消耗不消除世界固定窗口到期检查。公共持久化若改为追加记录+根引用，本文“同根”均指同一逻辑提交，不要求每次复制全部历史正文。
- [COMPUTED｜HIGH] 训练程序 `01a16469-00b1-7fa2-babd-e93860245256`确认不可变DayPlan、稳定entryId、量和动作版本、今日配额不被次日编辑改变；WorkoutClaimKey由训练拥有，真实零与缺区域区分，GetCompetitionBodySnapshot提供完整21区/体脂/基础魅力与版本；静态覆盖验算已通过。
- [COMPUTED｜HIGH] 训练程序追加确认BodyMetrics位于身体域、不依赖比赛实例/日期/体脂魅力；身体采用1e-6微单位及非负half-up，评分读取其已提交值并遵循相同明确舍入语义。全精度样例与未来微单位实现的末位差异应作为金样本校准，不改变赛事参数归属。
- [COMPUTED｜HIGH] 生活自主程序 `01a16469-0929-7700-9251-5dd684ac142a`确认领域初始化隔离、真实来源修订、共享L/BaseCharm；指出D日NPC不得先练完再与玩家D日初相比，已纳入。
- [COMPUTED｜HIGH] 已阅读公共`FlowPersistenceContracts.md`§2—7，本文沿用CommandEnvelope/PreparedOutcome/QueryView及同根提交；FreezeToken的configVersion在公共传输中映射contentVersion，内部scoringVersion另记，禁止复制第二套提交器。父审查的统一事件入口与BodyMetrics单向依赖要求已落实到本方案。
- [COMPUTED｜HIGH] 公共v0.2发布后已核对其§3新封套并增加操作映射；接入追加日志前缀与持久回执去重，逻辑同根原子要求保持。此次仅更新指定技术文档，没有修改产品规则或业务实现。
- [FRAME｜实施状态] 先前精度/同分、冻结阶段、最小义务和21区静态覆盖问题已获得候选协议或既定解释，可据此继续实施；没有需用户裁决的范围阻塞。实际配置落地、跨运行时精度边界、真实人物来源验证、完整恢复测试与路线平衡仍是实施验收门槛，本文不宣称已通过。
- [FRAME｜可调候选] D42/42日、模板初值、L适配段、评委权重与具体披露日属于策划可调参数；标候选即可继续结构实施，正式内容发布前需版本化认可与验证。
- [COMPUTED｜HIGH] 策划稿§8.1后续增加42日长路线静态算术，生活稿将竞争者初始全区S24修订为候选S11。本文没有独立复核整条路线，不将其纳入已通过验证；该初值只由生活配置维护，不成为赛前补分。后续训练/平衡验证需复算长路线并做真实操作与随机分支长跑，不阻塞已认可的契约。

[RULES I BROKE]: 无。
