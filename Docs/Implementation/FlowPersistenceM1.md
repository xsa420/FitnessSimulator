# [FRAME] M1 公共协议、流程与存储实施记录

[FRAME] 2026-10-23。范围仅Production/Core、Flow、Persistence及Tests/Production/FlowPersistence；业务全局编排由父任务Application/GameSession负责。本记录不宣称M1整体验收通过。

[FRAME｜CS02签收纠正] 正常新局端到端产品认可撤回，应用提交时机待修待验：点击new-game、选择模板、编辑初始计划均只操作草稿；合法模板及合法初始计划最终确认后，才替换旧局，并在同一提交内保存有效计划和日初配额。坏档也必须到该最终确认点才移动备份并初始化。旧132/132及InitialPlan断言仍是执行事实，不证明该时序；FP-R03证明/保留备份/拒绝不明永久状态等底层通过事实保留。

## [COMPUTED] 已落盘接口

| 文件 | 实际接口/行为 |
|---|---|
| [COMPUTED] Production/Core/GameContracts.cs | CommandEnvelope、四维PreparedOutcome、OperationReceipt、RootEnvelope及versioned string fragments；ICheckpointStore.Load/Commit/FindReceipt/FindSemanticReceipt/ReadLog；IStateCodec。 |
| [COMPUTED] Production/Core/SerialCommandDispatcher.cs | 串行Execute/BeginNewRun、候选深克隆、授权/版本/命令hash/语义键检查、正常完成候选结局回调、存储确认后发布、未知结果冻结/Recover。Snapshot返回克隆；不持有领域对象。 |
| [COMPUTED] Production/Flow/FlowEngine.cs | 有限固定/动态manifest共用NodeCursor；候选筛选/互斥/去重/选择组权重/默认；选择不计数，Start存统计/占用，重入不重复；后继验证、冻结manifest、确定性入口随机；DayStepCursor重入。 |
| [COMPUTED] Production/Persistence/LocalCheckpointStore.cs | 持目录单写锁；双根帧、SHA-256/长度/尾标记；滚动追加日志、可见prefix、hash链、提交ledger；半收据忽略但完整坏帧停载；最新根或已提交日志损坏停载，不回旧根；64热回执外查询完整日志；新局旧prefix归档。 |
| [COMPUTED] Production/Persistence/UnityJsonStateCodec.cs | 实际生产序列化适配器；Core/Flow不引用Unity/UI。 |

[FRAME] 父接入：`new LocalCheckpointStore(path, codec, expectedContentVersion: "M1Baseline")`后Load；只有Empty或Loaded可建立会话，其余停载。初始Root需要RunId、ContentVersion，计数为0；Loaded使用返回完整根。构造SerialCommandDispatcher时必须提供authorize，生产仅授权真实Player来源；隔离测试目录可明确授权Test模拟玩家，同步检查结局。NpcProduction不能执行玩家NormalCompletion。

[FRAME] Execute的prepare委托只改所传候选，用SetFragment(owner,scope,schemaVersion,domainRevision,json)写领域结果并返回四维Outcome/SemanticKey。不得改现有会话对象后才序列化；辅助计划/偏好命令从候选读取并只改相应片段。NormalCompletion回调在保存前运行；若尚无结局内容，父也需提供显式无命中规则回调，不能缺失后静默跳过。

[FRAME] 日志流固定为run/{RunId}/records、inputs、内部receipts与permanent/history、read、archive；其他流名拒绝。ReceiptKind与receipt流不允许领域自行写入。ReadLog最多64条，仅已提交prefix可见。

[COMPUTED] 显式新局接口为dispatcher.BeginNewRun(command,initializedRun)：ActionKind固定BeginNewRun，命令携旧RunId/ExpectedRevision及可信来源；initializedRun是新RunId/ContentVersion/初始Fragments，Generation/Revision=0，不得自带prefix/回执/永久片段。提交器继承Root.PermanentFragments及旧Scope为permanent或permanent/...的片段，保留永久日志前缀；store归档旧局流。身份变化后同operation重试仍先查回执，异初始化载荷拒绝；旧归档RunId不得复用。普通Execute仍拒绝偷换RunId。

[FRAME｜CS02调用约束] BeginNewRun只用于最终确认，不用于打开新局草稿或首次进入计划页。initializedRun必须已经包含经领域验证的角色初值、有效初始计划及日初配额；领域合法性由父Application在候选构造时验证，公共存储不猜测计划字段。草稿取消/退出/非法计划不得改变旧RunId、数值或存档前缀；最终确认故障及重试应得到完整旧局或完整新局。对应应用回归与坏档最终确认时机需补验，本模块不以已有原子写测试替代它们。

[COMPUTED] 损坏范围接口为LocalCheckpointStore.ResetCorruptM1Store(bool userConfirmed)，返回StoreResetResult(Status=Reset/Refused/Unknown,BackupPath,Error)。只在全新空目录写入校验genesis；任何永久片段/历史/已读写入前，先持久化并验证permanent-touched单调标记。重建要求有效genesis且成功枚举目录证明标记不存在；最新完整收据若声明永久非空也拒绝。旧空收据不能覆盖更晚未知永久写入标记，标记部分/锁定/目录占位/读失败均不能当不存在。首笔M1无永久提交失败可据genesis确认重建；证明缺失/损坏则拒绝，不把损坏Loaded局改判Empty。接受后Directory.Move整数据目录到唯一.corrupt备份再建立空目录，不删除/覆盖现场。目录同级.writer-lock租约保持单写者；父负责确认UI与替换会话。重建故障返回Unknown/BackupPath，GetPreservedBackupPaths可只读定位所有保留现场；不自动回读较旧备份。此接口不是完整F08永久抢救。

[COMPUTED] FlowState新增HasTree；FlowEngine.NormalizeAfterLoad负责清理HasTree=false且无真实引用的默认物化Tree，已有引用或HasTree=true损坏则报错。父Read<FlowState>应调用；不能用Tree!=null判断是否已解析。

[COMPUTED] 错误菜单使用`bool LocalCheckpointStore.CanResetCorruptM1Store(out string reason)`判定是否启用确认重建；它与Reset共用证明逻辑，仅只读核验文件并恢复查询前内存缓存，不修复/截断/覆写日志。Reset确认时再次核验证据，先前true不是可复用许可。新增ResetEligibilityIsReadOnlyAndConfirmationRechecksChangedEvidence覆盖查询前后文件hash不变及随后marker出现时拒绝确认；已在005847正式报告通过。

## [COMPUTED] 测试代码与现有证据

- [COMPUTED｜HIGH] 已执行PowerShell `Add-Type -Path`，编译Core两个文件、FlowEngine和LocalCheckpointStore，输出“Core/Flow/Persistence pure C# compile PASS”。不含UnityJsonStateCodec，不是Unity编译。
- [COMPUTED｜HIGH] 另将上述源码与两份NUnit测试源码一起做内存编译，引用工程现有nunit.framework.dll，UnityJsonStateCodec仅用同签名抛异常替身用于编译；输出“Pure C# and NUnit test source compile PASS; codec stub compile-only, no tests executed.” 此步只验证测试源码可编译，没有执行测试，也不验证真实序列化适配器。
- [COMPUTED｜HIGH] 已用同一源码加内存System.Text.Json测试codec独立运行8个StorageFaultPoint：BeforeLogAppend、AfterLogFlush、BeforeRootWrite、AfterRootFlush、BeforeCommitReceipt、DuringCommitReceipt、AfterCommitReceipt、BeforeAcknowledgement。逐点基线提交→注入IOException→关闭并重开，验证前六点旧完整态、后两点新完整态；输出“PASS 8 persistence fault boundaries using System.Text.Json codec; Unity JsonUtility and NUnit pending parent run.” 此运行不启动Unity，也不证明安卓硬断电耐久。
- [COMPUTED｜HIGH] 可复核runner已保存Tests/Production/FlowPersistence/Run-PurePersistenceSmoke.ps1；工程根用PowerShell7执行`& Assets/FitnessSimulator/Tests/Production/FlowPersistence/Run-PurePersistenceSmoke.ps1`。同目录PureSmokeResult.json记录本次源码SHA256、8断点前后态、原operation重试及revision=2断言，passed=true；这是纯codec证据，不标Unity通过。
- [COMPUTED｜HIGH] 已直接读取父第四轮报告`TestResults/M1/editmode-20261023-004235-506.xml`：总115/115 Passed，按完整类名FlowPersistence过滤为38/38 Passed，覆盖FP-R01新增参数/相同副本、HasTree及正常新局/已证空永久坏根重建；源代码随后新增的genesis/永久标记严化测试不在该报告，不借此结果证明通过。执行入口由父维护，未由本任务启动Unity。
- [COMPUTED｜HIGH] 第五轮`TestResults/M1/editmode-20261023-005101-984.xml`为131项、129通过、2失败；已直接核对失败名为InvalidGenesisDoesNotProveFirstStoreEmpty和InvalidPermanentMarkerStopsThePermanentCommitBeforeDataAppend。两处损坏帧InvalidDataException未被helper/Commit边界捕获，现已分别改为证明无效及提交Unknown冻结；未改测试预期，原数据保留。后续005847报告已验证修复；第五轮129/131失败历史保留。
- [COMPUTED｜HIGH] 已直接核对`TestResults/M1/editmode-20261023-005847-710.xml`及对应`test-20261023-005847-710.json`：132/132通过，其中正式128、旧4；本模块FlowPersistence为54/54通过，sourceDrift=0。包含genesis首笔6故障、永久标记严化、只读资格/确认重验、重建4故障及前述修复。本轮组件测试通过，不扩展为真机、硬断电或完整永久救援通过。

| 测试文件/名称 | 覆盖 |
|---|---|
| [COMPUTED] CheckpointRecoveryTests.FaultBoundaryPublishesWholeOldOrNewState（8参数） | 半日志/候选槽/半提交收据/ACK丢失，未知结果冻结，重开可见前缀，原命令重试。 |
| [COMPUTED] ReceiptBeyondHotCacheRemainsIdempotentAfterReopen | 70操作越64缓存，重开旧ID不用prepare；新ID同语义键不重复状态。 |
| [COMPUTED] CommittedRootCorruptionNeverFallsBackToOlderSlot / MissingCommittedLogIsCorruptionNotEmptyState / MissingLedgerDoesNotMasqueradeAsFreshInstall | 已确认代际根/日志/账本缺失或损坏，阻止回旧/空档。 |
| [COMPUTED] RolloverNeverAbsorbsAnUncommittedTail | 700KB已存＋200KB未提交尾，再400KB滚块，旧尾不混入可见记录。 |
| [COMPUTED] PartialUncommittedLogTailIsInvisibleAndRemovedOnAppend | 已提交数据后追加5字节半帧，重开只读权威前缀，后续追加截去尾巴且仍可完整读取。 |
| [COMPUTED] NewRunArchivesOldPrefixesWithoutGrowingRootOrDroppingPermanentHistory | 多局替换，旧流manifest归档，根prefix数量稳定，永久历史保留。 |
| [COMPUTED] ConflictingPayloadAndStaleRevisionCannotMutateState / AuxiliaryPlanSaveDoesNotCaptureUncommittedWorkingCopy | 旧视图/同ID异载荷拒绝；独立暂存身体副本不随两次计划保存落盘。 |
| [COMPUTED] AuthorizedTestPlayerUsesSameEndingCheckButProductionRejectsTest | 测试目录授权Test走正常候选结局路径；生产授权拒Test。 |
| [COMPUTED] DirectoryHasExactlyOneWriter / MissingContentVersionBlocksLoadWithoutFallback | 单写者、内容版本不兼容。 |
| [COMPUTED] FlowEngineTests全部 | 展示/开始/完成分离；重入次数/占用一次；一次入口；冷却D3→D6；默认无限；显式消耗保留中间餐点；非法后继；动态manifest冻结；日结游标重入等点击。 |
| [COMPUTED] ConflictingSharedEntryDefinitionsAreRejectedBeforeSelection / IdenticalSharedEntriesDeduplicateAndStartTheSameCanonicalDefinition | FP-R01回归：同DefinitionId禁用/条件/消费差异在解析前拒绝；完全相同副本正常去重。策划已独立复跑原禁用复现和相同副本两条并确认修复，其余参数等正式测试。 |
| [COMPUTED] UnresolvedFlowJsonRoundTripDoesNotMaterializeAnActiveTree / NormalizeDoesNotHideConflictingOrMissingTreeReferences | 实际UnityJsonStateCodec未解析flow roundtrip的HasTree行为；损坏引用不被归空。 |
| [COMPUTED] BeginNewRunPreservesPermanentStateAndIsIdempotentAcrossRunIdentityChange / BeginNewRunRejectsUntrustedSourceAndStaleRevision | 正常新局初始化与永久保留原子发布；跨身份重试/载荷冲突/来源与版本权限。 |
| [COMPUTED] ConfirmedCorruptM1ResetMovesAllEvidenceAndAllowsFreshInitialization / CorruptStoreWithPermanentStateOrUnverifiableLedgerRefusesReset | 可证无永久的坏当前局显式确认后保留完整备份；有永久或无法证明时拒绝。 |
| [COMPUTED] FirstCommitFailureCanBeExplicitlyResetUsingUntouchedGenesis（6参数）/ FirstUncertainPermanentWriteRevokesGenesisOnlyReset / InvalidGenesisDoesNotProveFirstStoreEmpty | 首笔各提交前故障可据有效genesis显式重建；首次永久写前先撤销该证明；坏证明拒绝。已在005847正式报告通过。 |
| [COMPUTED] OldEmptyReceiptCannotOverrideANewerUnknownPermanentWrite / InvalidPermanentMarkerStopsThePermanentCommitBeforeDataAppend / UnreadableOrNonFileMarkerDoesNotCountAsAbsent | 旧空收据不能越过新永久未知；坏标记不继续写；标记锁定/目录占位不当不存在。已在005847正式报告通过。 |
| [COMPUTED] ResetInterruptionRetainsOriginalEvidenceOrLocatableBackup（4参数） | 取消无移动；移动前失败保留原目录，移动/初始化中断仍定位唯一备份，不宣称重建成功。已在005847正式报告通过。 |

## [FRAME] M1集成边界及未测项

- [FRAME] 流程引擎修改事务候选，不自行提交；父需把Resolve/Confirm/Start/Complete分别封为合法命令，并在普通Complete使用NormalCompletion。训练Handler接真实卧推，ResultReady与Settle来自训练/身体模块，不由本模块伪造成功。
- [FRAME] 当前FlowDefinition为有限DAG，共用固定/动态manifest；生产重复休息以新的runtime节点/身份表达，未实现通用嵌套调用栈/跨树热替换。普通条件用受控只读callback，不是完整配置DSL编辑器。
- [FRAME] 已完成组/休息/日结实际身体收益必须由父集成测试核对；本模块cursor测试不是成长、比赛或关系规则验收。四角色完整世界屏障与M3演出/备注UI尚未在本模块实现。
- [FRAME] 冷回执查询当前顺序验证日志，未实现派生磁盘索引；正确性不依赖64缓存，但长局读取延迟尚待优化/实测。工作根没有历史正文，仍须父验证领域片段不无限累积已完成树/组。
- [FRAME] 存储有Flush(true)和读回校验；安卓目录元数据同步适配、断电耐久、设备存储不足、长周期性能尚未验证。进程注入与实际硬断电不是同项证据。
- [FRAME] 尚无发布旧版迁移实现；未知schema返回IncompatibleSchema，内容缺失返回MissingContent；损坏永久部分的独立只读抢救尚未实现。首笔提交中断仍不自动冒称首次无档；有效genesis提供的是用户确认重建资格，不是恢复原进度。证明不成立时保守停载。
- [FRAME] 任意事后物理截断整个已确认ledger尾部与真正未提交半尾可能无法单凭剩余文件区分；当前故障模型验证追加中断/完整帧损坏，未承诺修复任意媒体丢失。

[COMPUTED] 已向父发送实际类型签名、最短接入方式、测试路径和独立编译/故障验算结果；已向指定策划确认A—H序列将由组件及父集成用例共同覆盖。没有运行setup、Unity编辑器/构建，也没有git提交。

[RULES I BROKE]: 无。

