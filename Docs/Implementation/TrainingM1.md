# [FRAME] M1训练、身体与卧推实现记录

[COMPUTED｜HIGH] 2026-10-23，候选规则版本TG-0.3。M1实际开发授权后实现；仅修改Production/Training、Body、Numeric、Tests/Production/Training及本记录。未运行编辑器、构建、setup或提交git。统一事务、真实设备与完整流程由父Application集成。

## [COMPUTED｜HIGH] 已实现代码与接口

| 路径（Assets/FitnessSimulator/下） | 内容 |
|---|---|
| Production/Numeric/Rational.cs | 精确有理四则、非负half-up微单位、整数平方根SqrtRational12V1、判伤精确平方比较。 |
| Production/Training/ExerciseCatalog.cs | 21区稳定ID、八动作TG-0.3贡献、三档工作量、秒/次、F/B/体脂系数、梯度门槛与池；不可变目录查询。 |
| Production/Body/BodyState.cs | 显式可序列化区域/动作数组、微单位、T精确余数字符串、伤病期限、克隆/校验；BodyMetrics薄肌指标。 |
| Production/Body/BodyCalculator.cs | 纯逐组计算、旧K→X→新K、F/B/H、计划配额、判伤、克隆输出；休息与独立日结步骤。 |
| Production/Training/TrainingModels.cs | 请求/结果/来源、输入摘要、计划条目、资格、冻结配额及配额重算校验。 |
| Production/Training/TrainingSession.cs | 纯快照转移，PreSet→Playing→ResultReady→Settled→后继；自动资格、重复结算、休息、结束动作/训练、伤病筛选、恢复校验、超时。 |
| Production/Training/BenchPressSession.cs | 首个正式卧推核，1ms固定步、按住左右施力、预生成分段扰动、80%稳定/最后300ms、越界/第三失误、暂停重绑；无自动成功入口。 |

[FRAME] 命名空间为FitnessSimulator.Game.Numeric/Body/Training。DTO字段用于由父提交候选与序列化，领域返回克隆，不自己发布全局状态或写盘；Application仍需检查公共Source/能力、事件游标及OperationId。测试ScriptReplay属于测试来源；训练核自身不把来源字符串当权限系统。

## [FRAME] Application最短接入顺序

1. [FRAME] 初始化：BodyState.Create(actorId,strengthMicro[21],bodyFatMicro,baseCharmMicro,mentalStressMicro)，TrainingProgress.CreateInitial(actorId)。父使用真实模板初值，不由训练固定统一角色。
2. [FRAME] 当日计划/资格生效后：DayTrainingQuota.Freeze(actor,day,planRevision,TrainingPlanEntry[],progress)，同入日步骤保存；休息日不调用非空训练配额。计划适配已由计划程序提供TrainingCatalogAdapter.ToTrainingEntries。
3. [FRAME] 实际进入训练时：TrainingSession.Begin(workoutId,quota)，父保存每日唯一训练claim；SkipUnavailableActions(workout,body)跳过已伤目标动作，配额不重算。
4. [FRAME] BeginSet(workout,body,progress,TrainingSetType,TrainingProducer)→Playing快照；保存PendingRequest/PreSetBody与父提供的小游戏seed。容量用当前最高梯度，挑战下一梯度且满足总力量，自动需当前梯度资格。
5. [FRAME] 手动：BenchPressSession.Begin(workout.PendingRequest,seed)，Advance(int elapsedMilliseconds)、Press/Release(BenchButton.Left/Right)，读View；完成Result不为空。自动：TrainingSession.CompleteAutomatic，仅AutoCapacity容量合法。
6. [FRAME] 手动结果：CompleteResult(workout,result)→ResultReady，先提交该子事件。再Settle(workout,currentBody,progress,day,injuryRolls,teachingKnowledgeMicro=0,injuryReductionBasisPoints=0)→TrainingSettlement{Workout,Body,Progress,Calculation,AlreadySettled}，三个片段、权益、随机回执、时间及事件游标由父原子保存。
7. [FRAME] 组后固定后继完成：ContinueAfterFollowers(workout,body)→PreSet或Ended。明确结束当前动作：EndCurrentAction(workout,body)，仅Settled合法，跳剩余组；EndWorkout仅PreSet合法。Rest(workout,body,30/90/180)返回新身体+新训练时间，每次由父独立事件去重。
8. [FRAME] 超时OvertimeSeconds(workout,explicitFollowerSeconds)=max(0,训练秒+明确后继秒−冻结Lstd)，只计算一次，训练不推进锚点。

[FRAME] 恢复：TrainingSession.Restore(snapshot)验证版本/配额/当前组上下文并克隆。Playing使用原PendingRequest、组前身体与父保存seed重启核，不能再BeginSet分配新组；ResultReady直接Settle；Settled后只恢复未完成固定后继。当前Settled重试同组返回AlreadySettled；跨更晚节点旧操作由父持久OperationReceipt返回，不由本域另建永久账本。

[FRAME] 日结必须分步：BodyCalculator.RecoverLocalFatigue(body,Rational mF)→RecoverStress(body,Rational mB)→AddStress(body,Rational deficit)→ClearTemporaryFatigue(body)；进入新日调用ExpireInjuries(body,enteringDay)。各返回Revision+1的克隆，父每步独立提交。RequiredInjuryRollRegions(preSetBody,request,result,quotaEntry)只返回结算后100<T<150的涉及区域，父只为该列表抽随机；T≤100/≥150不用随机。Life.NutritionEvaluation的分子/分母直接构造Rational，不改为浮点；B先恢复裁0再加不足。非训练显式休息可用CalculateRest(body,seconds)获取NextState/GameSeconds，仍只已定三档。

## [COMPUTED｜HIGH] 卧推输入与证据边界

[COMPUTED｜HIGH] 左右为持续按住，双按抵消；位置采用1e-12内部单位，D为1e-6，按1ms积分不截断漂移。每循环350ms准备，扰动每500ms固定一段，2.4秒至少80%稳定且最后300ms连续稳定有效；|b|>0.95立即失败。第三失败结束；已有效次数保留。

[FRAME] UI累计未缩放活动时间的余毫秒，再传整数Advance；同刻按输入序号调用Press/Release。渲染可随帧推进，但不可丢小数毫秒。Suspend保留逻辑左右状态且Advance无效；Resume(leftHeld,rightHeld)要求重建匹配状态，350ms提示后续原尝试；此提示不推进原判定。重载按组前恢复，不序列化正在运行的核。View仅显示当前状态，不泄露未来扰动。

[COMPUTED｜HIGH] 结果包含InputDigest、InputEventCount、MinigameSeed；摘要是确定性FNV样式审计摘要，不是安全签名。完整输入事件日志由Application操作记录保存，摘要不能单独重放。重复相同按住状态不计新物理输入，避免按帧刷新制造伪输入次数。

## [COMPUTED｜HIGH] 实际验证

[COMPUTED｜HIGH] 执行命令：`dotnet run --project Assets/FitnessSimulator/Tests/Production/Training/TrainingStandalone.csproj`。独立.NET 9纯源码编译及13组检查全部PASS、退出0；没有启动编辑器。相同TrainingChecks由TrainingDomainTests的13个NUnit测试调用，供父统一运行。独立项目只编译本域，不代表全Game程序集集成已通过。

| 检查组 | 本轮实际覆盖 |
|---|---|
| NumericAndCatalog | sqrt2/半量子，8动作权重和、21区覆盖、真实21区L金样本。 |
| RealBenchOutcomes | 同真实核输入：反馈控制6有效成功；3有效后持续偏向3失误；持续偏向零有效失败。没有通过写结果代替核。 |
| BenchPauseReplayAndTickBatching | 同输入100ms批量与1ms步进同结果；后台无进度；错误重绑拒绝、350ms后原循环继续；同刻左右顺序。 |
| CapacityAndPartialArithmetic | 单独身体夹具完整/部分/零量：总力量1.472/.736/0，X8/4/0，T40/20/0，36/18/0秒，输入不变。 |
| ChallengeKnowledgeAndPenalty | S14/Q98、K29/X99：v1挑战失败T72、总S+.3552、X先100后K30；v0失败T60且无K。 |
| QuotaRemainderAndRecovery | 三组精确80、余数归0、升级不改配额、清T不治伤、d3伤d6解、日结先恢复后不足。 |
| InjuryBoundaries | T100安全、T150搭子不能抵消、概率相等不伤、七目标同时伤且无关区不伤。 |
| LifecycleAutomaticAndIdempotence | 真核结果接ResultReady/Settle、结算重放无增量、双休息、自动当前N、禁止自动挑战、末组禁休息、结束动作/超时。 |
| ValidationAndHighFatigueAutomatic | 最高梯度力量加高不减难、高T仍有自动资格且必伤、配额篡改拒绝、冲突回调拒绝、相关身体版本冲突拒绝。 |
| AutomaticUsesNewDayTarget | 真实手动N6授资格，次日均衡N8自动v8/48秒，读取新配额80/3，不复用历史6。 |
| UpgradeLoadAndSharedQuota | 真实成功挑战升g2，后续容量按旧g0配额增加48T；卧推+肩推共享前肩q8/q32，卧推做完提前结束保留16T。 |
| InjuryRoutesToUnrelatedAction | 首卧推组使三头受伤，结束剩余卧推、跳相关下压，无关深蹲仍可开组。 |
| FatigueStressAndPartnerBoundaries | T125减10百分点，u约.60伤/.61不伤；F20/100和B24/25/50/75，第二组读取新S，非复用首组。 |

[FRAME] 身体公式夹具明确是下游计算隔离测试，未当作真实小游戏或人物生产结果；生产操作证据由RealBenchOutcomes单独提供。尚待父实际执行：统一程序集编译/NUnit、事务保存故障恢复、UI触摸、主流程和真机；其他7个小游戏核属M4，本轮只有完整定义表，不冒充已实现。

[COMPUTED｜HIGH] 初次独立项目配置产生一次MSBuild中间目录警告，已调整导入顺序，后续运行无该警告。后因统一编辑器运行期间工程Temp构建输出丢失，独立项目最终改为本测试目录隐藏.standalone/bin与obj，避免与编辑器清理冲突。遗留Tests/Production/Training/obj仅5个NuGet元数据文件，无源码；自动审批拒绝递归清理，故保留并通知父，不通过另一删除工具绕过拒绝。

[COMPUTED｜HIGH] 指定策划独立重跑首9组检查PASS并读代码，未发现确定公式偏差；其指出测试区分力不足后已新增上述后4组，全部13组再跑PASS。Restore目前验证对象快照，不是文件故障恢复；M1-T01～09不整体标通过。只读审查父Application发现首组前额外休息被禁止，已通知父且收到已修复回执；额外休息不自动制造人物互动机会。父确认生产结果只取private game.Result；首次统一编辑器编译已成功（父回执，非本人运行）。

[FRAME] 输入信任边界：Application仅向生产UI开放输入命令，由私有BenchPressSession.Result取得结果并核当前请求；不能开放任意v/success提交。摘要不构成授权证明，公共Source由入口授予。父当前源码CompleteResult来自私有game.Result，最终接口/持久恢复验证仍归父。

## [COMPUTED｜HIGH] 实际序列化边界修复

[COMPUTED｜HIGH] 父整局集成反馈内联null可被JsonUtility物化成默认对象。审查发现旧Clone会对物化PreSetBody调用BodyState.Clone，内部Regions/Exercises缺失时出错；Playing物化PendingResult也会暴露假“有结果”。此处不能以普通.NET对象克隆测试替代实际序列化验证。

[FRAME] WorkoutPhase作为已持久化的联合类型判别字段，足够确定可选载荷存在性，不另增加三个可能相互矛盾的Has位。NormalizeAfterLoad在PreSet/Ended清三个可选载荷；Playing保留并验证请求/组前身体、清结果；ResultReady/Settled三个载荷都必须有效，默认空对象与缺失一样拒绝。Clone也按阶段选择载荷，避免尚未显式Normalize时克隆幻影身体；Restore在克隆后规范化再执行原有完整检查。外层HasWorkout仍由Application负责，不能对不存在训练的默认空对象调用本域方法。

[COMPUTED｜HIGH] 新增TrainingJsonUtilityTests.cs七项NUnit，直接调用UnityEngine.JsonUtility.ToJson/FromJson：PreSet往返后休息/开组；Ended克隆/恢复；Playing原请求+组前状态且无结果；ResultReady直接结算；Settled重放及后继清空后再次保存；必填载荷缺失/非法阶段拒绝；身体T有理余数与伤病期限保持。父串行运行后，已只读核对TestResults/M1/editmode-20261023-004235-506.xml：测试运行总计115/115通过，TrainingJsonUtilityTests明确7/7通过、0失败，记录时间2026-10-23北京时间08:43:09。此为真实引擎序列化回归证据；独立13项纯域检查不代替它。该结果仍不代表安卓触摸或断电持久化验收。源码与测试在运行期间保持冻结。

[RULES I BROKE]: 无。



