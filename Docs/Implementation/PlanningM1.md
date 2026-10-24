# [FRAME] M1计划与模板实现记录

[COMPUTED｜HIGH] 2026-10-23，收到父架构明确实际开发授权后实现。拥有区域仅`Production/Planning`、`Tests/Production/Planning`及本文；没有启动编辑器、运行setup、改旧实验场、提交git或实现比赛。读取现行README、Architecture、角色比赛规格、计划比赛技术稿；工作区及父级未发现AGENTS.md文件，沿用会话规则。

## [COMPUTED] 实际入口

| 文件 | 已实现能力 |
|---|---|
| [COMPUTED] `Assets/FitnessSimulator/Production/Planning/PlanModels.cs` | 可编辑PlanDraft；不可变PlanState/PlanRevision/DayPlan；锚点资格；领域错误定位；训练目录投影接口。 |
| [COMPUTED] `PlanRules.cs` | Validate、SaveInitial、SavePending、ApplyDay、ResolveDay；无整体游戏状态、时间推进、持久化或界面依赖。所有保存返回新对象。 |
| [COMPUTED] `PlanCatalog.cs` | 完整自然周四练/六日循环、明确独立的卧推单动作自定义切片；三模板目录、资格查询及初始化意图；未集成动作报告。 |
| [COMPUTED] `TrainingCatalogAdapter.cs` | 从训练唯一ExerciseCatalog/RegionIds读取8动作目标与三档组次；组合按区域交集；ToTrainingEntries输出训练拥有的DTO。 |
| [COMPUTED] `PlanPersistence.cs` | 导出显式字段PlanStateRecord，恢复时校验schema/content版本、日切边界和合法计划，再复制为不可变状态；不读写磁盘。 |

[FRAME] 根命名空间为`FitnessSimulator.Game.Planning`，通过父维护的单一Game程序集集成。没有复制训练贡献比例或动作量表；组合区域由训练RegionIds的既定组前缀投影，不按动作名称匹配。

## [FRAME] 父Application最短接入

```csharp
var catalog = new TrainingCatalogAdapter();
var intent = TemplateCatalog.PrepareInitialization(
    TemplateCatalog.DefaultId, unlockedTemplateIds, runId, actorId);
var state = new PlanState(runId, actorId, "player-plan");
// M1明确选择此自定义切片；产品预设使用CreateDefault，不删预设中的其他动作。
var draft = PlanCatalog.CreateBenchPressSlice();
state = PlanRules.SaveInitial(state, draft, 1, 0,
    new AnchorEligibility(1, new[] { "morning", "noon", "afternoon", "evening" },
        Array.Empty<string>()), catalog);
var dayPlan = PlanRules.ResolveDay(state, 1, DayOfWeek.Monday);
var trainingEntries = TrainingCatalogAdapter.ToTrainingEntries(dayPlan);
var record = PlanPersistence.Export(state);
// Application把record及其他领域初始化候选一起提交，才发布新局。
```

[FRAME] 初始化意图的Template字段提供UniformStrength/BodyFatPercent/BaseCharm/MentalStress/Affluence/Balance及版本引用。父调用身体`BodyState.Create(actorId, 21个显式力量微单位, 体脂微单位, 魅力微单位, 心理微单位)`、训练`TrainingProgress.CreateInitial(actorId)`、生活`LifeRules.Initialize(runId,actorId,balance,affluence,day)`；三域全部成功才提交。关系/库存初始引用已明确，但M1生活实现尚无完整关系/库存域，不能声称已全部初始化。

[FRAME] 修改调用`SavePending(state,draft,currentDay,state.Revision,anchors,catalog)`；每次仅替换待生效版本。日切只调`ApplyDay(state,nextDay)`，同日重复返回原状态，跨越多日拒绝，避免跳过业务日结；再由世界传真实星期给`ResolveDay`。训练未出现不调用任何计划推进计数，因此漏训自然跳过。状态Revision与Active.Revision不同：辅助保存/日切影响状态版本，而当天动作仍引用原active内容版本。

[FRAME] 操作幂等由父公共OperationId收据负责；本服务只检查expectedRevision，不能以重复调用SavePending代替公共重放。JsonUtility应序列化`PlanPersistence.Export(state)`的public-field Record，不直接序列化只读PlanState；`Restore(record,catalog,knownTrainingAnchorIds)`只接受同内容版本，不热替换历史组次。第三参数必须由Application已加载内容目录提供全部已知且可训练的锚点，不能从存档或今日解锁集推导；餐食不属于此目录。辅助保存只合并这个计划片段，禁止整体复制当前未完成事件工作对象。

[FRAME] UI载荷为PlanDraft：Mode=NaturalWeek(0)/DayCycle(1)，Slots的Index周一0..周日6或循环0..13，IsRest，AnchorId，Combination=Free(0)/ChestBiceps(1)/BackTriceps(2)/LegsGlutes(3)/Shoulders(4)，Volume=Low(0)/Balanced(1)/High(2)，Entries的EntryId/ExerciseId。所有字段由Application反序列化后Validate，界面不得自报Sets/Repetitions。Rest用空Entries且无AnchorId；训练一个slot只有一个AnchorId，重复slot Index拒绝。

[FRAME] M1训练启动前调用`GetIntegrationIssues(dayPlan,new[]{"bench_press"})`；返回缺失玩法则说明未集成，不截断默认清单、不自动成功。完整默认计划可查看和合法保存，但其首日弯举尚未集成，不能冒充可完整执行；卧推切片是单独明确选择的自定义计划。

## [COMPUTED] 验证证据

[COMPUTED｜HIGH] 实际执行命令：`dotnet run --project Assets/FitnessSimulator/Tests/Production/Planning/Standalone/Planning.Verify.csproj --configuration Release`，结果12 passed / 0 failed，退出码0。它编译实际计划源码、训练目录/DTO和有理数基础，链接工程已有的真实NUnit程序集，并调用本任务12个无setup/参数化的Test方法。没有Unity编辑器、完整游戏集成、触摸或磁盘故障测试。

[COMPUTED｜HIGH] 调整构建输出到隐藏`.build`后再次执行，仍12/12通过。旧bin/obj移动保留在`.build/legacy-*`，未删除用户文件；`.build`被本目录gitignore排除。父可在统一编辑器测试时直接运行`PlanningTests`，不使用此独立运行器替代正式集成测试。

[COMPUTED｜HIGH] 策划复审发现并复现恢复锚点自我授权：旧Restore取存档中的AnchorId当合法目录。已修复为独立注入`knownTrainingAnchorIds`，旧两参数重载删除，不能静默把未知锚点改成下午。新增active/pending未知与餐食锚点拒绝、合法历史已解锁清晨及待生效清晨恢复、缺目录拒绝测试；补上UnknownTemplate实际断言。相同独立命令复跑14 passed / 0 failed、退出码0。之前12测试未覆盖该缺陷，不能据旧通过结果否认复审问题。

[COMPUTED｜HIGH] 指定策划再次审阅修复并独立复跑14/14通过，确认领域恢复缺陷关闭，证据记录于其规格§11。本次随后只读核对发现Application/GameSession.cs第71行仍调用旧两参数Restore，已通知父所有者迁移到固定训练锚点目录；该调用点不在独立运行器编译范围，领域测试不能证明应用已编译或整局恢复通过。应用迁移完成证据由父补交。

[COMPUTED｜HIGH] 父随后确认已完成可信目录签名迁移，但首轮实际Unity集成的7条GameSession用例在新局读取计划失败：JsonUtility将inline null恢复为默认对象，旧Restore误认其为有效计划。已将片段SchemaVersion提升为2，并增加HasActive/HasPending存在位；Restore忽略不存在计划的空默认对象，拒绝存在位与真实数据矛盾，schema1明确拒载，不静默猜迁移。

[COMPUTED｜HIGH] 已新增`PlanJsonUtilityTests.cs`四个使用实际`UnityEngine.JsonUtility`的NUnit测试：空计划往返、有效计划且无pending、有pending以及日切后重新保存、存在位矛盾/旧schema拒绝。该文件不纳入独立.NET运行器；待父串行编辑器测试，本任务没有宣称其通过。修复后原独立14项仍全部通过。这个缺陷证明普通对象Export/Restore断言不足以验证实际序列化行为。

### [COMPUTED] 后续引擎证据：关闭上述待测项

[COMPUTED｜HIGH] 已只读核对父运行的`TestResults/M1/test-20261023-004235-506.json`与对应`editmode-20261023-004235-506.xml`：exitCode=0，总115/115通过（正式111、旧4），失败/跳过均0。四项PlanJsonUtilityTests均明确Passed，因此上述实际序列化缺陷及四项引擎待测状态关闭；本任务未另启动编辑器。

[COMPUTED｜HIGH] 同份报告的`LastSavedPendingPlanSurvivesUnfinishedEventAndActivatesOnlyNextDay`与`MenuCancelPreservesRunAndConfirmedReplacementIsAtomicAndRetryable`均Passed，提供父Application层计划恢复/次日生效和取消/确认重开证据。指定策划也已独立核对这些报告并记录复审。先前矩阵里的“未测”是最初领域交付时状态，以上两个命名用例覆盖的集成部分以此后续证据为准；未覆盖的界面、故障组合、8玩法和真机门槛仍保留，不扩成完整AC02/AC04/AC14通过。

[COMPUTED｜HIGH] 父随后要求复核CS02确认边界，静态检查当前Application显示：new-game先初始化并替换旧局进入持久InitialPlan，save-plan才提交首次计划。此前Replacement测试通过仅证明该实现边界的事务行为，不能证明“未选/非法计划不得清旧局”的产品要求。已向指定策划请求确认，父倾向把模板与初始计划均保留草稿，最后合法计划确认时一次BeginNewRun；本任务不越界修改Application。

[COMPUTED｜HIGH] 指定策划已明确确认上述为既有规则：模板选择与初始计划编辑均是未提交草稿；合法模板与合法初始计划全部确认后才能原子发布/替换。取消、未选、非法计划或初始化失败保留原局；无旧局时也不发布未完成开局为可玩局。策划撤回此前依据Replacement测试作出的该产品子集认可；本文亦不再把该用例的旧通过结果作为CS02满足证据。序列化四项和已有次日计划测试证据不受影响，修复/新回归由父负责。

[COMPUTED｜HIGH] 后续只读复核父修复`GameSession.NewGame.cs`：newGameTemplate为本地草稿，CreateRunWithPlan先在fresh根Initialize、Prepare首次计划及配额，全部合法后才BeginNewRun；坏档Reset同样在验证之后。`test-20261023-010559-275.json`记录exit0/sourceDrift0，总136、正式132；对应XML为136/136、0失败/跳过。已逐项核对新测试断言：NewGameDraftCancelInvalidPlanAndExitNeverReplaceExistingRun比较旧根在草稿、非法计划、取消及重启后完全不变；FirstGameIsNotPublishedWithoutAValidInitialPlan确认无计划/非法计划不发布，合法后同根有Active与Quota；NewRunFault的BeforeRootWrite与BeforeAcknowledgement分别恢复完整旧局或已有有效计划的新局，后者同命令重试成功。上述CS02新局确认边界缺陷在这些集成路径关闭，不引用旧Replacement测试代替。

[COMPUTED｜HIGH] 新草稿页增加明确的卧推自定义自然周入口（周一卧推、其余休息），与完整产品自然周四练不同。此静态入口变化不等于完整默认计划、组合动作编辑或CS01—12全链通过；未执行界面触摸验收。本轮仅修改本文证据，不修改冻结中的应用源码或启动编辑器。

[COMPUTED｜HIGH] 同次静态检查当前GameFrontendView：无初始JSON时仅CreateBenchPressSlice，编辑器可改卧推/休息、三档量及循环增删末日，虽能显示传入自然周数据，但没有自然周/循环或两种产品预设切换入口。因此CS01默认自然周选择、CS03全休息周、CS04循环切周、CS08组合/8动作选择等只能引用领域测试，不能签收完整UI流程；CS05/06循环部分和其余用例也仅按实际命名集成测试范围认可，CS01—12不存在整批全链通过声明。

| 策划用例 | 已执行的领域证据 | 尚未集成/未测 |
|---|---|---|
| [COMPUTED] M1-CS01 | 默认意图10/25/50/10/0/300；默认周一卧推→弯举、3×8及完整8动作；DefaultTemplate…/DefaultPlan…测试通过。 | [FRAME] 身体/钱包等领域整局提交和UI确认，正式真实训练。 |
| [COMPUTED] M1-CS02 | 锁定/未知模板、缺计划拒绝；父公共重试前不变更原state。 | [FRAME] 父新局幂等及失败不覆盖旧局故障验证。 |
| [COMPUTED] M1-CS03 | 全休息周、1/14日循环合法、无隐藏动作。 | [FRAME] UI预览与赛事压力说明。 |
| [COMPUTED] M1-CS04 | D3多次保存、草稿后改不影响已存，D4按周四执行。 | [FRAME] 界面放弃流程/保存通知时机。 |
| [COMPUTED] M1-CS05 | D4循环第1、D5休息、D10第1，同日ApplyDay幂等。 | [FRAME] 正式日期、训练事件随机未出现的联动。 |
| [COMPUTED] M1-CS06 | 0/15循环拒绝、1/14接受；重复日期键、空训练拒绝。 | [FRAME] UI错误定位展示。 |
| [COMPUTED] M1-CS07 | 不同entryId重复动作拒绝，跨日同动作合法。 | [FRAME] UI增删排序实际操作。 |
| [COMPUTED] M1-CS08 | 实际训练映射：胸二头下拉合法、深蹲非法、自由8动作合法。 | [FRAME] 7个未集成小游戏不能计正式玩法通过。 |
| [COMPUTED] M1-CS09 | 已提示明日清晨仅次日合法，当天/未解锁拒绝。 | [FRAME] 世界解锁提示与锚点序列集成。 |
| [COMPUTED] M1-CS10 | 计划无伤病输入、不重排；ToTrainingEntries保留顺序和量。 | [FRAME] 训练伤病/门槛/配额实际消费测试由训练与父集成。 |
| [COMPUTED] M1-CS11 | 独立片段与不可变输出已实现。 | [FRAME] 未完成事件暂态、未知写入结果及磁盘中断集成未测，不能据纯测试标通过。 |
| [COMPUTED] M1-CS12 | 草稿隔离、过期版本拒绝、持久片段复制、内容版本漂移/非法pending日拒绝；复审后补测独立目录拒绝active/pending未知或不可训练锚点，保留合法历史锚点。 | [FRAME] 全局新局重开/永久资格保留/训练期间辅助保存集成。 |

[FRAME] 以上只确认已执行领域断言。完整AC02/AC04/AC14、三成功解锁链、8小游戏、赛事M3、安卓真机均未以本结果验收。

## [COMPUTED] 对齐与待集成

- [COMPUTED｜HIGH] 父已接受纯服务签名和卧推切片独立于默认计划；实际调用示例、Record恢复方式、12测试结果已发送。
- [COMPUTED｜HIGH] 训练已提供实际目录/初始化签名，本适配直接消费；生活提供LifeM1.v1和显式余额/富裕初始化，尚无库存/关系完整域已保留为待集成。
- [COMPUTED｜HIGH] 已向界面任务发送可编辑JSON字段、枚举、切片与缺失玩法提示方式。
- [FRAME] 本实现供父Application统一提交、调用领域初始化、实际计划选择和运行测试；没有另建公共封套/持久化器。等待父集成差异及指定策划代码复审，发现问题在拥有区域继续修复。

[RULES I BROKE]: 无。
