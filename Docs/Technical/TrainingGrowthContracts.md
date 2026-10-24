# [FRAME] 训练小游戏与身体成长：实施契约 v2

[FRAME｜交付状态] 2026-10-23；技术评审稿；已核对策划TG-0.3及逐项回执，最终审阅状态见末尾。仅方案，不包含实现、正式配置或验收通过声明。产品来源为[主文档](https://app.notion.com/p/2bc3e8eb2dfd80bcb359f603fc989244)3.3—3.5、3.10—3.11及本地模块分工；具体玩法参数以 `Docs/Design/TrainingAndGrowthSpec.md` 为唯一策划来源。本文技术字段为程序决定；标为候选的玩法仍不是用户已批准参数。

## [COMPUTED｜HIGH] 现状与替换策略

[COMPUTED｜HIGH] 已读 README、Architecture、Verification、ProgramModulesAndDependencies、ProductionAlignment、TrainingContracts.cs、SaveContracts.cs；源码引用检索仅找到训练接口声明，没有正式小游戏实现。旧 Architecture 的整动作回退和历史表现复用已被主文档替换。现有保存外壳没有本稿的组阶段、冻结配额和待结算结果结构。

| 旧字段/接口 | [FRAME｜程序决定] v2处理 |
|---|---|
| exerciseId、attemptedLevel、targetRepetitions、setType | 保留语义但绑定不可变 RequestId/SetId/内容版本，梯度改稳定 TierId，数值等级另给排序 |
| targetMuscleIds | 由动作定义读取带权贡献列表，UI不得自行传入肌群替换目标 |
| attemptedWeight/weightUnit | 由梯度定义派生，仅展示，收益不使用界面自报重量 |
| baseDifficulty | 替换为包含组前输入引用、公式版本、D与玩法参数的 DifficultySnapshot |
| actualRepetitions | 更名 effectiveReps，整数0..N，由玩法判定核产出 |
| Completed/Failed/Interrupted | 终局结果只有 Success/Failure；Partial是失败的完成量视图；Interrupted是运行生命周期，不发结算、不消耗新名额 |
| executionMode | 与 ProducerSource 分开，Manual/AutoCapacity，来源 PlayerTouch/ScriptReplay/NpcPolicy/AutoCapacity/TestInjected |
| performanceSchema/Payload | 可版本化保存判定证据；不输入成长倍率，不作为历史复用成绩 |
| historicalPerformanceSourceId、consecutiveStrengthSets | 删除；迁移时禁止用于推算当前收益或疲劳 |
| gameDuration/durationUnit | 删除小游戏权威写入口；结算从有效次数及动作定义计算整数游戏秒（公共timeIntent同单位） |
| ITrainingMinigame.Begin(request, callback) | 保留适配层概念；核心改 Begin/AdvanceInputTick/AcceptInput/Suspend/Resume/ReadView，结束回调只递交判定事实，不写身体、不保存、不自行跳后继 |

[FRAME｜程序决定] TrainingContractVersion=2、BodySchemaVersion=1、InputSchemaVersion按动作独立版本；内容锁定 ContentRevision、RulesVersion。版本号不参与业务唯一结算键，否则升级版本可重复发奖。运行中不热替换规则；加载不兼容版本返回明确 UnsupportedRulesVersion，不能补默认值或把旧成绩转换成新资格。现有原型无已验证正式训练存档；旧外壳仅允许已定义的机械字段迁移，不能宣称兼容旧业务进度。将来兼容迁移需显式映射21区、TierId、组结果和账本，先保留原文件再验迁移；无法映射交存档流程，不能静默新局。

## [FRAME｜程序决定] 数据结构与唯一写权限

| 结构 | 字段与语义 | 唯一语义写者 |
|---|---|---|
| BodyState | ActorId、BodyRevision；regions[21]的strength S 0..100/localFatigue F 0..100/temporaryFatigue T（候选持久上限150，判伤用截断前值）/injury；bodyStress B 0..100、bodyFatPct 5..50；exerciseProgress[8]的knowledge K 0..100/proficiency X 0..1000；baseCharm/mentalStress及各自配置上下界 | 身体Reducer；经济、关系仅提交合法效果请求 |
| InjuryState | regionId、sourceSetId、injuredDay、expiresAtStartOfDay；未伤为显式None，不能缺区域；各区独立 | 身体Reducer |
| TrainingProgress | actorId；每动作 highestUnlockedTierId、autoQualifiedTierIds；本局保存，不跨局继承 | 训练Reducer；手动成功或成功挑战授予 |
| FrozenDayPlan | planRevision、effectiveDay、orderedEntries[{PlanEntryId,exerciseId,targetSets,targetReps,restPreset}]；定义版本 | 计划模块产出；训练持不可变引用/副本 |
| WorkoutState | WorkoutId、actor/day/anchor、WorkoutClaimKey；FrozenDayPlan、QuotaSnapshot、actionCursor/setCursor、phase、nextWorkoutSetSequence、elapsedGameSeconds、practicedExerciseIds、PendingSet | 训练Reducer |
| QuotaSnapshot | quotaVersion、day、planRevision、frozenAtCommitId、standardGameSeconds；当日计划确定时冻结；每计划entry的基准容量TierId、N、组数、各区完整容量权重；各区denominator；排除/跳过原因另记，不改分母 | 训练服务构建，数值服务验证 |
| PreSetContext | RequestId/SetId、WorkoutSetSequence、BodyRevision、TrainingRevision、组前BodyState只读值、资格、实际TierId、SetType、N、配额引用、DifficultySnapshot、权益快照、随机流引用、内容版本 | 训练准备；协调器持久化其恢复边界 |
| SetResult | ResultId、RequestId、SetId、rules/inputVersion、executionMode、producerSource、effectiveReps、outcome、attempts/failedAttempts、terminalReason、inputDigest、policyVersion?、randomEvidenceRef? | 玩法核或授权自动容量服务；提交后不可变 |
| SetSettlement | SettlementKey、inputHash、区域前后值/变化及截断量、X/K变化来源、受伤抽样依据、资格变化、gameSeconds、practiced/teachingClaim、controlIntent、领域版本前置条件 | 纯计算器准备；各Reducer认领字段，统一提交 |

[FRAME｜程序决定] 21区稳定ID固定为 chest_upper/chest_middle/chest_lower；back_upper/back_middle/back_lats/back_lower；leg_quads/leg_hamstrings/leg_adductors/leg_calves；shoulder_front/shoulder_side/shoulder_rear；biceps_long/biceps_short；triceps_long/triceps_lateral/triceps_medial；abs/glutes。中文显示名可变；无左右双份。配置必须显式覆盖21区，零值与缺值区别处理。动作正贡献列表不得重复、负值或合计非1；全8动作覆盖所有区的正贡献需验证，不能由程序暗加贡献。

[FRAME｜程序决定] 身体scale=1,000,000（S/F/T/B/K/X及体脂百分点同为微单位），正贡献权重为整数基点合计10000；配置小数转精确有理数，不先转二进制浮点。每条公式中间用宽整数/有理运算（实现可用BigInteger作溢出安全基准），最终候选字段非负值最近微单位舍入，恰半向上；负变化先求未裁剪新值，再裁合法范围并舍入，避免负数舍入歧义。不得逐乘子舍入；相同输入/规则版本跨运行环境结果逐字节一致。显示1位小数不回写。

[FRAME｜程序决定] T额外保存每区精确有理余数accumulationRemainder（小于半微单位的技术精度状态，不是第二套可见疲劳），用于下一次增加/休息累积；q始终保存有理参考，不使用显示/差分舍入值计算力量倍率或失败罚。T的内部微单位持久值+余数构成下次精确输入，绝不读取UI一位小数，判伤读精确累积值，150必伤之后才截断持久T并清该区余数；休息到0同样清余数。其他身体字段每完成事件最终写入一次微单位，并在下一组读该已提交值。

[COMPUTED｜HIGH] q=80/3的三完整容量组，无休息/失败，累计舍入为26.666667、53.333333、80.000000；实际发布差分26.666667、26.666666、26.666667，总和80。单组理想份额仍80/3，最大量化误差不足1微单位，未改变配额比例定义；失败倍率永远乘精确q而不乘第二组差分。

[FRAME｜程序决定] 判伤避免平台sqrt差异：随机u为固定32位无符号整数除2^32（[0,1)），搭子减免d为百分点/100的精确有理数（配置必须验证0≤d≤1；负值拒绝）；100<T<150时u<max(0,sqrt((T−100)/50)−d)等价于(u+d)^2<(T−100)/50，以宽整数交叉相乘比较，等号不伤。T≤100直接不伤、T≥150直接必伤，不调用随机。规则实现仍按稳定区域顺序记录每个抽取/确定判定原因。精确概率仅诊断可计算显示，不用于UI。该比较与策划sqrt曲线同义，不引入近似曲线。

## [FRAME｜程序决定] 命令、结果、查询

[FRAME] 公共封套由 `FlowPersistenceContracts.md` 唯一定义；本域使用 OperationId、ExpectedRevision、ActorId、ResourceScope、Source、EventInstanceId。以下只定义业务载荷；全局修订变化不能直接否决已保存待结算结果，使用相关领域版本和冻结输入核验。

[FRAME｜程序决定] Source是公共权限来源Player/NpcProduction/Test；ProducerSource是事实证据来源，正交保存。Player+PlayerTouch用于真实触摸；NpcProduction+NpcPolicy用于生产策略；Player或NpcProduction+AutoCapacity只在各自资格满足时成立。ScriptReplay一律Source=Test、evidenceKind=Replay，不可冒充PlayerTouch/真机人工证据；TestInjected仅测试域且productionEligible=false，不能进入正式存档或资格。输入/结果来源由入口能力授予，不接受客户端自报升级权限。

| 命令/查询 | 输入 | 输出与前置条件 |
|---|---|---|
| PrepareDayTrainingQuota | 初始化/进入次日有效计划、日初TrainingProgress、动作定义 | 生成冻结g0/配额及标准时间，不创建Workout、不记训练已发生；空休息计划返回NotApplicable，不造0分母训练 |
| BuildTrainingTree | 实际进入训练事件、当日有效计划、actor/day | 原子占用WorkoutClaimKey=(run,actor,day)，引用当日QuotaSnapshot产生一次树；重复同操作返回原结果，另一入口不产生第二训练 |
| GetSetOptions | workout、最新已提交状态 | 可用容量/挑战/自动、门槛与伤病原因、组前休息/结束选项；不抽随机 |
| BeginSet | entry/组位、Manual或AutoCapacity、Volume或StrengthChallenge | 校验目标力量总和门槛、梯度、伤病、名额；分配全训练递增WorkoutSetSequence，冻结PreSetContext |
| SubmitInput/AdvanceInputTick | RequestId、InputSeq、activeTicks、pointerId、phase、归一坐标或buttonId | 核心接受/重复忽略/不合法拒绝；只更新未完成局部交互；完成后生成ResultReady |
| CompleteSetResult | 判定事实及证据 | 验证N/v/来源/请求关联；持久化ResultReady，不发成长、不扣第二次名额 |
| SettleSet | 已持久化ResultId | 准备纯计算变更，身体/资格/时间/名额/权益/随机/后继原子提交；幂等返回同SetSettlement |
| Rest | workout、Short/Medium/Long、RestOrdinal | 仅组前/休息选择合法；恢复T并加游戏时间，不恢复F/B、不抽伤、不耗组；允许反复 |
| EndWorkout | workout、当前可操作phase | 仅组前/休息选择；保留已结算收益，跳过剩余训练，不清T，不重归一 |
| ApplyBodyEffect | effectId、actor、origin、typed effect、前置版本 | 仅注册数值效果类型：知识/恢复/体脂/压力等；校验目标、范围、授权来源，无裸字段任意set |
| QuoteDifficulty / CalculateSet | 不可变快照、规则、结果、指定随机样本 | 纯返回报价/Delta，不读全局单例、不访问磁盘、不写状态；缺参数返回ConfigError |
| GetBodyView / GetTrainingView | actor、可见权限 | 文本体型、区域S/F/T/伤病、合法操作、组/动作/时间；风险定性，UI不读隐藏精确概率 |
| GetCompetitionBodySnapshot | actor、barrierId | bodyRevision、21区S、bodyFatPct、baseCharm及范围、configVersion；缺区InvalidBodySchema，合法0保留；不使用社交修正后魅力 |

[FRAME｜程序决定] 正常业务失败仍是一次有效完成组：v<N、不升级、保留比例收益、消费本组。StaleRequest、WrongActor、WrongPhase、InvalidResult、IneligibleAuto、InjuredTarget、MissingConfig、UnsupportedVersion、ConflictingDuplicate均为技术/前置拒绝，不伪造失败组。相同OperationId同输入重放返回原回执，不同输入同ID拒绝。ResultReady重复回调只接受同摘要；不同结果禁止覆盖。写盘失败不发布成功UI，恢复到最近有效提交并按账本重试。

## [FRAME｜程序决定] 状态机、稳定身份与恢复

[FRAME] 训练阶段：NotEntered → PreSet → Playing或AutoResolving → ResultReady → Settled → FixedFollowers → RestChoice/NextEntry/WorkoutEnded。FixedFollowers由统一事件执行器推进；受伤/配置结束动作返回EndCurrentAction意图，不能由小游戏跳锚点。每次开组只使用已提交最新状态；最高梯度不提供挑战。最后一组没有随后休息；下一动作切换自身0游戏耗时。

[FRAME] WorkoutId由执行器实际进入训练时生成；PlanEntryId不使用动作名或列表位置代替。SetId=(WorkoutId,PlanEntryId,SetOrdinal)；SetOrdinal是条目内稳定组位，WorkoutSetSequence是实际开组的全训练单调序号，两者不能混用。节点身份=(WorkoutId,PlanEntryId,SetOrdinal,Phase,CallOrdinal)，固定子树复用增加调用序号。重复休息用RestOrdinal。SettlementKey=(RunId,ActorId,SetId,Settle)，不含合同版本；结果键使用同SetId与Result阶段。

| 中断位置 | [FRAME] 恢复行为 |
|---|---|
| Playing/AutoResolving未结果提交 | 从同组PreSetContext重做；先前组、已完成休息/教导保留；Input未完成效果无权进入身体快照 |
| ResultReady已保存 | 原结果锁定；只执行未完成Settle，禁止重开小游戏或重抽表现 |
| Settle准备中未提交 | 从ResultReady按相同输入和随机位置重试；不能只保存力量而漏伤病/时间 |
| Settled、组后剧情/休息未完 | 完成组保留，只重做当前未完成子事件；组前恢复引用移到最新合法边界 |
| 休息已完成 | 恢复含T恢复和耗时，两者同提交，不能反复读档免费休息 |
| ResultReady触发成功结局 | 保存待Settle后继，演出/记录/菜单/继续后从Settle接续；结果仍不可改 |
| ResultReady触发终止结局 | 保存结果和历史；终止局不得执行Settle；未发生的身体变化不能写入历史 |
| 计划编辑保存/后继结局 | 仅更新计划已授权片段或结局片段，不能提交Playing局部输入；不因全局revision改变重算已固定结果 |

[FRAME] 身体相关状态在ResultReady与Settle之间不能被另一个业务步骤修改；若出现相关BodyRevision冲突，暴露调度/恢复错误，不能用新身体重报价。正常后继与结局演出游标并存由流程模块持有。随机抽样按actor/set/region固定用途与稳定区域顺序，状态和结果同提交，界面查询/休息不抽伤。

## [FRAME] 结果、双时钟与八个接入包

[FRAME｜候选基线，TG-0.2已对齐] N为正整数，v∈[0,N]。每有效动作循环v+1；第三无效循环终止；达到N立即成功，因此失败必v<N。Partial仅表示Failure且0<v<N；零量失败无力量/X，挑战失败知识仅v>=1发1。成功可含此前无效尝试，但最多N+2尝试；完成比例r=v/N仅应用一次。无效尝试是否增加游戏时间按既定有效完成口径为0，真实操作窗口超时可产生一次失败。

[FRAME｜程序决定] InputTick为确定性玩法逻辑步，activeTicks排除暂停。真实触摸带序号和活动时间戳，重采样进同一规则核；NPC策略用虚拟逻辑步产生轨迹，可高速无画面推进，不等待渲染或现实时间。渲染只插值显示，不负责是否命中。另一个GameSeconds仅由v×secondsPerRep加已完成休息计算，逻辑步、动画速度、UI停留不改变它。进程存活的后台打断冻结时钟、玩法、随机和逻辑触点，清除系统触点句柄；遮罩中无计时重建应按按钮/逻辑触点位置，玩家确认就绪及0.35秒提示后续玩，不清已有错误，不以释放事件免费完成。进程死亡或退出重载未完成组按组前恢复。

[FRAME｜程序决定] InputTick=1毫秒；输入采集先取得相对单调活动时间，最近1ms量化，恰0.5ms向上；策划窗口/周期也按同一规则量化一次，明确最大0.5ms时间量化误差。坐标按面板0..1量化到百万分之一，不能使用屏幕像素作判定。输入顺序为(tick,InputSeq)，同tick顺序由采集器或策略预先分配，指针身份不按到达顺序替换。先推进到该tick但不执行该tick超时，再处理该tick输入并核验合法完成，最后处理同tick超时；已有第三失误则不再消费后续输入。

[FRAME｜程序决定] 平衡控制以1ms固定积分步、按钮分段常量和预生成扰动推进；条件累计使用毫秒计数（80%按整数交叉乘）。轨迹不依渲染帧重新采样：按输入序号保留原始move事件，相邻有效采样点连线做连续扫掠/有序检查点穿越，沿段求边界交点及离通道时段；不得只检查端点，也不得把跨墙路径插值成合法绕行。逻辑显示可插值，只有原输入参与证据；同一输入在30/60/120渲染帧率下必须同结果。量化和同刻排序属于RulesVersion，变更须重验边界轨迹。

| 独立玩法包 | [FRAME｜候选] 输入/判定职责 | [FRAME] 独立交付及失败测试 |
|---|---|---|
| MG01卧推 | 左右按钮修正漂移并推进；平衡与推程同时合格完成循环 | 平衡积分核、漂移参数、双按钮触摸；持续偏向越界、边界帧、左右同时按处理 |
| MG02弯举 | 上/下两个节拍窗分别点中，完成升降循环 | 节拍状态机；提前/迟到/只点一半/连点重复无额外次数 |
| MG03下拉 | 单指沿曲线通道下拉并归位 | 路径投影与连续性；跳点穿越、出界、未归位、不同分辨率 |
| MG04下压 | 按住下压、信号变化时松开并回程 | 状态信号与边沿判定；提前松、晚松、重复down、丢失up |
| MG05深蹲 | 观察重心方向序列，再依序滑动 | 序列生成/回忆核；错序、漏项、观察阶段输入不能抢答 |
| MG06臀推 | 按住蓄力、时间窗释放、随后稳定保持 | 持续时长及稳定阶段；过早/过晚/保持中输入破坏 |
| MG07肩推 | 拖动方块穿过上行开口避障，再下行 | 路径碰撞/开口布局核；高速穿墙、边界接触、未完成返程 |
| MG08侧平举 | 双指分别抵达左右目标且时间差合格 | pointer身份和同步判定；单指冒充双指、交换ID、超时差、设备多点取消 |

[FRAME] 每包独立规则核、输入适配、只读View、教程提示、合法成功/部分/零量输入序列、NPC策略适配、安卓真机记录；共用结果封套不共用一套按钮计数玩法。难度D只能通过策划定义的窗宽/漂移/通道等参数作用，不允许实现者自行改变奖励。每包输入序列和规则版本可重放。测试注入仅测试程序集/专用构建，来源TestInjected，不能授予正式存档资格或作为八玩法/人物成长验收证据；生产包不注册该操作。

[FRAME｜共同NPC样例] N=8手动力量挑战，策略产实际输入，经同一核依次判有效、有效、无效、有效、无效、无效：attempts=6、failed=3、v=3、Failure/Partial；r=3/8，K失败+1，gameSeconds=3×该动作secondsPerRep；疲劳比例部分与完整失败罚分开。输入轨迹而非策略直接填写这个结果；具体触点时间须用策划参数和该规则核生成留证。零有效三失另一例不发K/X/力量，但全额失败疲劳仍存在。

[COMPUTED｜HIGH] 具体化为TG-0.2弯举：两目标S14，Q28恰g2门槛，K29/X99/T0，g0=1，合法少量计划两组N8。D=0.6、节拍半窗0.16秒。每次尝试准备结束作为t0，第1/2/4次分别up@700tick、down@1500tick；第3/5/6次down@700tick（错误按钮）立即失败。合法输入域内得到v3、三错误，时间12游戏秒；r0.375、单池0.9，S增0.439560/0.359640，X原增4.8375但只+1至100，再失败K+1至30；T每区增40×0.375×1.2×1.5+60=87，不抽伤。算术已独立计算，规则核尚未实现，不能声称该输入已经运行验收。

## [FRAME] 纯计算时序与数值边界

[FRAME｜候选组合，TG-0.2；其中单池、分配与保留曲线为既定] CalculateSet读取组前S/F/T/B/bodyFat/K/X，按实际梯度收益和r形成一份力量池，再按贡献权重给区域；区域递减/局部疲劳/全身压力相乘，最后封顶100。不能复制整池、再除肌群数、使用旧p，或重复乘v与r。G(S)=1−0.8S/100；局部疲劳F≤20保留1，以上线性至F100保留0.3；压力分段保留由策划明确定义边界。

[FRAME｜既定] X增长读取旧K倍率1+K/100及旧K开放上限100/300/600/1000（K界限30/60/90），溢出丢弃。之后才应用失败K和本组新教导K；新K只影响后续组。体脂变化在本组压力新增计算后生效，本组压力减免读取旧体脂。知识/熟练不直接加力量，T不额外直接扣力量。

[FRAME｜程序决定] 结算内部顺序：校验/去重 → 固定组前输入 → 计算力量与X → 本组局部疲劳、压力及体脂delta → 失败及教导知识 → 临时疲劳基础/力量倍率/失败追加 → 对本组正贡献区域判伤 → 训练资格/练过动作/名额/游戏时间/控制意图 → Reducer校验并原子提交。各计算步骤读组前或明确候选状态，不依赖字段遍历次序；判伤读取本组新增后的T，教导读组前权益。受伤保留已获收益并结束当前动作，无关动作仍可继续。

[FRAME｜候选基线，TG-0.2已对齐] 当日有效计划确定时用当天开始已解锁容量梯度g0冻结，不等到实际训练入口。u(j,m)=N_j×h(g0_j)×w(j,m)，U(m)=Σ全计划组u，q(j,m)=80u/U；实际容量T增量=q×r×h(g)/h(g0)，力量基础再乘1.5，失败力量另加1.5q（不乘r或实际梯度）。h=1/1.2/1.45/1.75。入场已伤、漏训、提前结束、中途升级都不改分母。U=0且无贡献则不参与；U=0却有正贡献是配置错误。日初快照和计划/训练进度初始化同事务，次日编辑不覆盖今天。

[FRAME｜候选基线，TG-0.2已对齐] 标准训练时间随同日配额冻结：Lstd=Σ(N×secondsPerRep)+90×max(计划总组数−1,0)，包括动作边界一次中休，不计最后组后休息。实际仅加有效次数与已执行休息；超时max(0,Lactual−Lstd)，不是禁止开组的条件；传流程后续锚点输入，不自动跳餐/跳锚点。锚点和训练标准不得重复扣算。

[FRAME｜既定] T≤100不抽伤；100<T<150按先陡后缓候选曲线减搭子百分点并截断；T≥150必伤不受搭子抵消。只对本组相关区逐区判定，允许同时多伤；判定在后续休息前，无关区不抽。压力不能另开通用受伤抽取。日结T清0不治伤；伤病到进入expiresAtStartOfDay解除，X完整天候选由策划给。

[FRAME｜技术边界] 身体服务另提供 PrepareRest、PrepareKnowledgeEffect、PrepareDailyRecovery、PrepareExpireInjury、PrepareBodyInitialization。日结按已对齐候选：摄入评估→F减20×mF→B减10×mB并裁0→另加不足压力→T清零，入次日解除到期伤病。q=clamp(intake/2000,0,1)、mF=mB=0.25+0.75q、不足B=8(1−q)引用生活规格；不复制菜单。不把省略摄入解释为倍率1。外部效果有origin/effectId/tags/target/delta或ruleId，不能让经济重复给一份自然恢复。

## [FRAME] 依赖、权益与解耦

[FRAME] 计划仅提供当日有序不可变安排，训练负责资格及伤病运行筛选和WorkoutClaim；事件负责树执行/锚点/结局，训练只返回意图；身体纯服务不引用UI或人物策略；小游戏仅读请求和产事实；存档只保存已验证片段；比赛读身体快照而不直接访问内部Reducer。

[FRAME｜程序决定，计划比赛程序已对齐] 身体域增加中性BodyMetrics查询，输入完整21区S与metricVersion，返回原始指标及微单位值；L等产品公式由赛事策划定义，身体仅持其版本化纯计算配置，禁止引用Competition实例/日期/赛程。比赛和关系同查该指标，避免身体与比赛循环依赖。比赛排序键按各归一指标×1e6取最近整数、恰半向上；不能拿显示数值排序。比赛快照另读取体脂和基础魅力，不混进L查询。

### [FRAME｜程序决定] BodyMetrics确定性平方根与金样本

[FRAME] “中间精确有理”只适用于有理四则运算；总体标准差含非完全平方根时按此唯一量化操作处理，禁止宣称无理数也精确保存。输入是21个已提交力量微单位s_i，scale=10^6；总体方差V=(21Σs_i²−(Σs_i)²)/(21²×10^12)，单位为力量平方，不用先舍入均值，也不采用样本方差除20。负分子为非法输入/实现错误，不用abs掩盖。

[FRAME] SqrtRational12V1(a,b)：要求整数a≥0、b>0，V=a/b；取A=a×10^24、k=isqrt(floor(A/b))。isqrt返回唯一满足k²≤floor(A/b)<(k+1)²的非负整数，使用宽整数牛顿迭代/二分并校验此不变量。再比较4A与b(2k+1)²：大于或等于取k+1，小于取k。在BodyMetrics调用场景返回整数Std12，单位为10^-12力量；中性核本身只返回缩放整数，其他调用的量纲由各自契约定义。这里相等为最近half-up；无需平台浮点sqrt，最大绝对量化误差0.5×10^-12力量单位。

[FRAME] BodyMetrics内部的唯一调用点是计算总体方差之后、E=1−clamp(std/14)之前。用std=Std12/10^12继续精确有理计算E与L=mean(h(S))×(0.8+0.2E)，按赛事产品定义的h分段；L最终保留归一微单位LUnits=roundHalfUp(L×10^6)。返回LUnits及有理componentExplanation（含Std12、V和量化前L），消费者禁止重复算sqrt或重新量化其输入。比赛和关系读取同一版本输出；评分中使用LUnits/10^6，排序L键直接使用LUnits，避免查询值与平分值两套口径。若未来更改精度/调用位置，应升版本并重跑评分金样本，不能改旧已冻结比赛结果。

[FRAME] 版本登记：numericKernelVersion=NumericRationalV1；sqrtKernelVersion=SqrtRational12V1；metricVersion=BodyMetricsL_V1_Sqrt12_Micro6，绑定产品L公式内容版本。scoringVersion必须绑定该metricVersion、sqrtKernelVersion及ScoreUnits4/TieUnits6舍入版本，冻结比赛快照保存这些版本和BodyRevision。旧版本缺失不得默认用新核重算历史。身体之外不得再维护一个Math.Sqrt标准差实现；判伤继续用本稿平方不等式，不改为SqrtRational12以免增加不必要量化。

[FRAME｜生活程序最终对齐] SqrtRational12V1实现放中性Numeric核，BodyMetrics与关系分别调用，不依赖Competition。一般互动输入归一非负有理x时返回无量纲根的12位half-up结果（不是力量单位）；单位由调用契约声明，算法不变。关系以该量化根继续精确有理计算1+0.5root及p0×魅力倍率×压力倍率，clamp后与随机u/2^32交叉乘比较，等号失败，不额外量化概率；表白既有pShown到0.001规则单独保留，不套用一般互动口径。kernelVersion随关系curveVersion持久化。该扩展不更改训练判伤的精确平方比较。

[COMPUTED｜HIGH] 一般互动金样本x=1/4时root=0.500000000000，魅力倍率1.25；p0=0.5、压力输入y=0且倍率1时p=0.625，随机整数u=2684354560恰对应0.625，等号失败；u=2684354559成功。此为算术期望，目标运行时测试待实施。

| 金样本任务 | [COMPUTED｜HIGH] 本轮整数运算核算值 / [FRAME] 后续实现断言 |
|---|---|
| [FRAME] SQ01平方根零/完全平方/非完全平方 | [COMPUTED｜HIGH] V=0→Std12=0；V=4→2000000000000；V=2→1414213562373。 |
| [FRAME] SQ02根的半单位与两侧 | [COMPUTED｜HIGH] V取24/(4×10^24)、25/(4×10^24)、26/(4×10^24)，Std12分别2、3、3；中间根恰2.5×10^-12，必须向上。此为数学核夹具，非角色配置。 |
| [FRAME] BM01真实21区非零方差 | [COMPUTED｜HIGH] 20区S20、1区S21：V=20/441，Std12=212958855000，h全为1，量化前L=1−0.212958855000/70，LUnits=996958。全区S20则V0、LUnits1000000。 |
| [FRAME] BM02 L半微单位两侧 | [COMPUTED｜HIGH] h均值1、Std12分别6999965000001/6999965000000/6999964999999，量化前L分别小于/等于/大于0.9000005，LUnits=900000/900001/900001。此为根之后指标纯函数夹具，不能冒充可达21区身体向量。 |
| [FRAME] SC01排序与评分交接 | [FRAME] BM02三值交比赛时L键分别900000/900001/900001；后两仅L并列，应按已配置后续键继续，禁止用未量化L再破平。每评委ScoreUnits4取原始score=12.345649999/12.34565/12.345650001应为123456/123457/123457；这是舍入核夹具。另重跑赛事§7所有真实身体评分样例，差异若在末位必须由版本解释，不能改金样本掩盖。 |

[FRAME] 实施包T2/P2共用一份金样本数据及期望整数：在编辑器、无画面运行及目标安卓运行比较Std12、LUnits、ScoreUnits、排名键、版本和结果摘要。覆盖区域排列置换不改变全区L、缺区拒绝、S微单位极值、std=14附近E裁剪及半单位上下邻值。当前只独立运行整数算术核验上表，不声称跨运行时实现或设备验收通过。

[FRAME｜候选基线，TG-0.3] 晚学对当日实际练过动作K+3；休息日全天学习对8动作K+12（替换TG-0.2的6），笔记K+2；全天学习只在首学习物品点一次机会，不另追加晚学/物品点。身体服务接受已验证StudyEffect的目标集合与来源键，事件/经济拥有资格和消耗，不由数值模块自行启动学习。K下降不追扣X，但X≥C(K)时暂停新增X，已有熟练减难仍有效。单动作故意失败最多4K的比较前提是同日不重复动作；不擅加知识日上限或错误时间费用。平衡尚需多日验证。

[FRAME｜公共封套v0.2接入] PreparedOutcome按outcomeKind、eventDisposition、checkKind、opportunityPolicy分别表达，不以BusinessFailure猜测完成/机会/结局。ResultReady与Settle完成各用CompleteNode+NormalCompletion；Playing等待输入用KeepOpen+None；测试拒绝无业务提交；计划保存AuxiliaryOnly+None。组失败是已完成Settle，消耗该组名额，仍做正常结局检查；名额消费与关系互动机会不是同一字段。持久日志/根prefix发布由存档独占，训练不另开文件。

### [COMPUTED｜HIGH] 本轮独立核算记录

[COMPUTED｜HIGH] 对策划8行发力权重求和均为100，按短编号映射集合核对覆盖21区；没有把缺失区补零成覆盖。弯举N8/v4、g1、S10夹具首组两区增0.4048/0.3312；下一组未量化中间理论值0.403375104/0.330246144，与策划算例一致，实施按微单位规则最终落0.403375/0.330246。K29/X99跨门槛先X封100后K30，逻辑不会补发被上限丢弃的X。上述为方案验算，不是运行源码测试。

[FRAME｜TG-0.2候选语义，技术账本同步生活程序] TeachingEntitlement由关系拥有：actorId、trainingSessionId(等于WorkoutId)、sourceActorId/sourceKind、activatedAtCommitId、firstEligibleWorkoutSetSequence、knowledgeRuleId。PreSetContext保存权益版本/快照；v>=1且WorkoutSetSequence≥下界才产生TeachingClaim。账本必须从awardedExerciseIds布尔集合改为teachingAwardByExercise{exerciseId,awardedNominalAmount,lastClaimCommitId}，按同来源族本次合格最高额度与已授额度的正差发放：老手1→付费教练3只补2；教练3后老手0。失败K+1是独立来源。关系账本与身体K同组原子提交，在X之后应用；付款前不追溯。K封顶也消费名义额度：K99教练3实际+1仍记3，之后负K不能补领；总账键=(WorkoutId,exerciseId)，每次差额effect键加SetId去重。主动/免费互动次数仍由关系模块写。

[FRAME] NPC、玩家按ActorId隔离身体、计划、钱包、配额和随机流；NPC到场/搭子/自主训练不重复创建同日会话。NPC策略仅选择合法操作和输入序列；没有默认成功路径。自动容量需本局同动作同梯度资格，当前N全完成且正常成长/疲劳/时间/伤病；高T不取消资格；力量挑战永远不走AutoCapacity。ScriptReplay只是输入来源，不能绕过相同判定。

## [FRAME] 分阶段实施、S/AC与验收情境

| 包/阶段 | 交付边界 | 验收要求与覆盖 |
|---|---|---|
| T0/M0 | v2字段、公共封套引用、动作/区域定义校验、冻结配额、状态机、兼容拒绝 | 配置缺字段非0；21区完整、权重和1；请求/来源篡改拒绝；S14/AC15 |
| T1/M1 | MG01真实输入→ResultReady→数值→后继→休息；一个模板有效计划 | 手机完成/部分/零量；逐阶段故障恢复，完成组不丢且结果不重玩；S03/04/05/13、AC03/04/05/14 |
| T2/M1 | 身体纯计算与Reducer全边界、学习/伤病到期 | 计划80、失败完整罚、K29/30/59/60/89/90、T100/150、多区伤、力量封顶；S05/AC05/06 |
| T3/M2 | 生活教导/学习/摄入恢复及首NPC输入策略 | 支付前后跨动作/跨组；X先K后；NPC三失部分与零量可重放；S06/08/09、AC07/09/10 |
| T4/M4 | MG02—MG08逐包接入、四人物同输入核策略、全量配置 | 各自独立成功/部分/失败轨迹与真机输入；不得以注入替代；S04/09/12、AC04/10/13 |
| T5/M5—M6 | 多日平衡与安卓触摸/恢复/性能证据 | 漏训、提前结束、受伤跳动作、连续失败知识、重复休息、自动后受伤；单段5—10分钟及首结局2—4小时测量，不能由公式推定通过；AC01/04/06/14/15 |

[FRAME] 必测矩阵补充：①两肌群权重0.7/0.3，只分一份池；②正式计划同日重复动作拒绝（候选已与计划策划对齐）；仅底层身份测试用同定义不同调用验证ID不冲突；③组间升级后分母不重算；④已伤动作/提前停后剩余配额不膨胀；⑤高T自动仍可选且150必伤；⑥本组K跨门槛但X仍按旧上限；⑦体脂变化不反改本组压力；⑧重复回调/重试同SettlementKey零二次效果；⑨计划编辑全局revision变化不丢待结算；⑩后台取消/帧率差异不改相同InputTick判定；⑪连续日结恢复、T清零、伤病到期分别只执行一次；⑫未进入训练当天晚学不凭计划清单发知识。

## [FRAME] 对齐记录与真实未闭项

| 对齐对象 | [COMPUTED｜HIGH] 已取得回执/证据 | [FRAME] 尚待事项 |
|---|---|---|
| 训练成长策划 01a16465-e320-77c3-8221-97e5ab19d096 | 已读TG-0.3；回执明确日初g0/全计划含伤病冻结、v=N立即成功、X后新K再判伤、教导名义额度封顶仍消费；后台重建口径已接入 | 已收到最终产品语义签收，未发现阻塞本模块方案的产品冲突；后续真机/平衡验证不是本轮已实现 |
| 流程保存程序 01a16468-f8be-7cd2-bfb2-871b7a166e0e | v0.2确认ResultReady/Settle、成功继续待Settle、终止无执行权、Started不重增；N02后日初PrepareDayQuota、首局同样调用 | 公共封套以其v0.2为准，实施需故障注入证据 |
| 生活人物程序 01a16469-0929-7700-9251-5dd684ac142a | LA-0.2确认名义额度max差额、K满消费、WorkoutSetSequence；输入轨迹/来源隔离与双时钟同意；发送具体弯举失败算例 | 八策略正式核重放与四人物长周期仍待实施 |
| 计划比赛程序 01a16469-12e2-75d0-8383-b83108d7dd45 | 接受FrozenDayPlan/PlanEntryId、restPreset默认、每日claim；同日重复动作候选禁止；21区覆盖核验；BodyMetrics中性归属已对齐；追加SqrtRational12V1获得比赛程序独立推导与整数验算回执，已同步其精度段 | 三模板与全计划实际初始化测试、比赛指标一致性待实施 |
| 文本验证程序 01a16469-1cf0-7361-aa0f-264a36dd13a8 | 输入字段/来源/API限制已纳入其稿；TG-0.2暂停重建冲突已回传唯一口径 | 真机八玩法、双点恢复、采样边界需其验收矩阵执行 |

[FRAME｜真实阻塞界定] 本轮配额分母/教导叠加/有效完成语义已取得候选协议，不再列为缺参阻塞；实现启动仍需父聊天登记统一合同/内容版本，技术精度由父架构师最终登记，指定训练策划已签收产品一致性。窗口、次数、倍率已有可执行候选，M5校准不能替代M1正确性。手机手感、双点暂停、总游玩时长与故意失败策略平衡尚无实测，是后续验证门槛。本文只写指定文件，没有修改业务源码、场景、预制体或共享总稿。

[RULES I BROKE]: 前版NPC样例未核弯举训练量，曾用三组N8；已公开纠正为合法少量两组N8、T87，并通知生活程序、训练策划及父聊天。






