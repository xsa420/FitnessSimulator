# [FRAME] M2训练占用与组前权益接口

[COMPUTED｜HIGH] 2026-10-23，按父确认的纯领域范围实现；没有修改M1 WorkoutSnapshot、TrainingSession签名/Normalize/序列化、Body或小游戏核，没有修改Application/关系模块，没有启动编辑器。此次新增TrainingDayClaim.cs、TrainingBenefits.cs、TrainingSessionM2.cs及自身测试。既有M1包与存档不自动升级。

## [FRAME] 最小接入与唯一状态

[FRAME] 父接入M2后，每人物当日只持久化一份M2WorkoutContext.Workout，不再并存可独立写入的M1 Workout副本。旧M1入口保留作旧版本/回归，不在M2调用链中混用自由实时教学/风险参数。根内容版本识别、全局迁移、日历推进、提交发布均由父负责；本实现不解析WorkoutId推断RunId，不提供隐式迁移。

| 实际API | 输入/返回及职责 |
|---|---|
| [COMPUTED] TrainingClaims.CreateDay(run,actor,day,scheduleRevision) | 显式Available；无训练实例。 |
| [COMPUTED] TrainingClaims.TryClaim(current,run,actor,day,scheduleRevision,workoutId,arrivalAnchor) | 返回克隆NextClaim及Created/AlreadyClaimed/Conflict/Closed。run/actor/day/schedule及已有workout/anchor全比；同ID不同请求不是幂等。 |
| [COMPUTED] TrainingClaims.Close(claim,reason) | 克隆关闭；Missed只可无workout，Completed/EndedEarly须已占用，Terminated可两者；重复同reason不改，异reason拒绝。关闭后不补练。 |
| [COMPUTED] TrainingSessionM2.Create(run,actor,day,workoutId,quota,claimedDay) | 要求身份匹配且claim已占用；复制ScheduleRevision/ArrivalAnchor，生成唯一内层Workout。scheduleRevision和quota.PlanRevision是不同概念，不强制相等；父负责计划与安排关联。 |
| [COMPUTED] TrainingSessionM2.BeginSet(context,body,progress,type,producer,benefits) | 校验run/actor/day/workout/即将分配的全训练sequence，深拷贝快照及数组，后续改传入快照不影响本组。 |
| [COMPUTED] CompleteResult / CompleteAutomatic | 沿M1核结果契约；保留冻结权益至结算与后继阶段。生产输入仍由父私有核获取，不新增结果注入入口。 |
| [COMPUTED] PrepareTeachingClaim(context) | 仅ResultReady；读冻结来源及有效次数，满足来源sequence下界才纳入；名义目标取max，零有效目标0/空来源。 |
| [COMPUTED] Settle(context,body,progress,injuryRolls,resolution) | 无自由实时教学/减伤参数。只用冻结减伤、校验后的名义delta调用M1纯结算；返回M2TrainingSettlement{Context,Settlement}。 |
| [COMPUTED] ContinueAfterFollowers / EndCurrentAction / SkipUnavailableActions / EndWorkout / Rest | 封装既有生命周期；清理过期权益/本组回执，不改旧规则。Rest返回M2TrainingRest{Context,Body}。 |
| [COMPUTED] Clone / NormalizeAfterLoad / Restore | 均返回新M2WorkoutContext，不修改输入。调用方必须使用返回值。Restore另做既有Workout完整恢复检查。 |

## [FRAME] 版本化字段与提交顺序

[COMPUTED｜HIGH] TrainingDayClaim持SchemaVersion1、完整身份、scheduleRevision、arrivalAnchor、HasWorkoutId、状态和关闭原因。M2WorkoutContext持SchemaVersion2、显式RunId/ActorId/Day/WorkoutId、ScheduleRevision/ArrivalAnchor、唯一Workout、HasSetBenefits/Benefits、HasTeachingResolution/AppliedTeachingResolution。

[COMPUTED｜HIGH] SetBenefitsSnapshot持SchemaVersion1、SnapshotId、完整组上下文、RelationshipRevision、RuleVersion、RiskReductionBasisPoints/RiskSourceIds、TeachingSources。每教学来源含EntitlementId、SourceActorId/Kind、FirstEligibleWorkoutSetSequence、NominalTargetMicro、KnowledgeRuleId、ActivatedAtCommitId；当前候选来源额度为1或3知识。重复EntitlementId拒绝，源合并取max，不相加。规则ID/关系资格来源由关系查询和父适配校验，本域不读取关系状态。

[FRAME] 无权益使用显式SetBenefitsSnapshot.None(snapshotId,run,actor,day,workout,sequence)，不是空引用/缺省0。旧M1未来升级需父明确识别旧根版本并验证无权益事实后另行提供显式升级入口；本轮未实现升级。M2活跃阶段缺HasSetBenefits或有效快照直接拒绝，不能Normalize补LegacyNone。

[COMPUTED｜HIGH] TeachingClaim键按逐段转义的(run,actor,workout,exercise,setId,TeachingDelta)编码，载荷含完整身份、SnapshotId/组序号、快照关系版本、有效次数、合格来源ID与EligibleTargetMicro。TeachingResolution携对应身份/键、AwardLedgerExpectedRevision、PreviousNominalMicro、TargetNominalMicro、DeltaMicro；核target等于冻结claim目标且delta=max(0,target−previous)。组前权益版本与提交时教学账本前置版本分开，不能拿最新权益替换冻结输入。

[FRAME] 关系模块唯一拥有(run,actor,workout,exercise)名义额度账及去重；本域不更新该账，也不把resolution数据视作真实账本授权。父顺序：保存ResultReady→训练产claim→关系准备ledger patch及resolution→训练Settle准备身体/训练候选→父核账本ExpectedRevision及其他领域前置→同一提交发布ledger/Body/Training/随机/后继。任一步失败均不部分发布。v0不占额度；K封顶仍记名义额度；X先读旧K，再加失败K和教学delta。

[COMPUTED｜HIGH] Settled保存AppliedTeachingResolution作本组重放核对；同resolution可返回AlreadySettled，变更revision/金额等拒绝；跨后继旧请求依旧由父OperationReceipt处理。PreSet/Ended清Benefits/Resolution；Playing与ResultReady必有Benefits、无AppliedResolution；Settled两者均必填。所有清理都发生在新克隆上，输入对象不变。

## [COMPUTED｜HIGH] 当前验证与后续门槛

[COMPUTED｜HIGH] 已执行 `dotnet run --project Assets/FitnessSimulator/Tests/Production/Training/TrainingM2Standalone.csproj`，原13组M1检查及新增6组M2纯规则检查全部PASS，退出0。独立输出使用本测试目录隐藏.standaloneM2，与M1输出隔离。

| 新增测试组 | 实际覆盖 |
|---|---|
| [COMPUTED] ClaimsUseFullIdentityAndNeverReopen | 六维冲突、相同请求复用、漏训/完成关闭后不可重开、无workout不伪造完成。 |
| [COMPUTED] FrozenSnapshotIsDeepAndExactContext | 数组/来源深拷贝，run/actor/day/workout/sequence错配拒绝，缺快照不变None。 |
| [COMPUTED] TeachingUsesMaxBoundaryZeroAndNominalDelta | 1与3取max3、先1后补2、v0不领、付款sequence边界、重复来源拒绝。 |
| [COMPUTED] SettlementReadsOldKnowledgeAndStoredRisk | K29/X99挑战失败，X先封100后K32；冻结10百分点减免；重放一致/不同resolution冲突。 |
| [COMPUTED] FullKnowledgeStillConsumesResolutionAndNoneIsExplicit | K100实际增0但名义delta3保留给关系提交；显式None；活跃缺标记拒绝。 |
| [COMPUTED] NormalizationIsPureAndRequiredPayloadsStayRequired | Normalize/Clone不改输入/不共享来源对象；不接受未知M2版本或猜run。 |

[COMPUTED｜HIGH] 已读取父串行运行的 TestResults/M1/editmode-20261023-013440-172.xml：197/197通过、失败0；其中本域TrainingM2DomainTests 6/6、TrainingM2JsonUtilityTests 8/8通过（五阶段参数化、缺失标记/载荷、Settled回执重放、漏训claim）。对应 test-20261023-013440-172.json 记录正式案例193、退出0、sourceDrift0；M2-first-regression-coverage.json 记录原132个正式案例无遗漏。本次实际引擎验证已完成，不再标记为待运行。上述证据仅覆盖该次运行。

[KNOWN｜HIGH] 父反馈此前013234轮因集成测试使用不受支持的Is.AnyOf编译失败，并已修复；该历史失败不替代013440轮的实际通过结果。当前保持源码冻结，等待父完成D09图形单项与下一批集成安排。

[FRAME] M2尚未接入主流程，本轮回归通过不代表M2阶段全通过。训练域尚待关系真实账本、Application原子提交、到场/自主屏障及M2文件恢复的后续接入验证；上述教学结果是纯结算夹具，不能当生产人物操作证据。NPC只允许后续接现有卧推核输入，本轮未扩其他七核。

[RULES I BROKE]: 无。
