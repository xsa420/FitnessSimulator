# [FRAME] M2人物计划与当日安排

[COMPUTED｜HIGH] 2026-10-23，按父确认的最小接口实施；只改Planning及自有测试/记录，未改Application、Life、M1手机包，未启动Unity。现有PlanState及PlanPersistence Schema2逻辑未修改；DayPlan仅增加只读PlanId/ExerciseCatalogVersion，其他M1改动是内部冻结值复制构造器及新增重载。

## [COMPUTED] 文件与接口

- [COMPUTED] `PlanCatalog.CreateNpcDefault(NpcPlanPreset)`返回独立可编辑PlanDraft。四预设按生活稿§4：Rival一胸二/二背三/四腿臀/六肩下午均衡；Beginner一胸二/三腿臀/六背三下午少量；Veteran二背三/四腿臀/六肩晚上均衡；Coach一胸二/三背三/五腿臀/日肩晚上均衡。组合动作直接引用现有完整默认计划，训练量只读训练目录。角色到预设映射由Life/Application负责，不以显示姓名作ID；版本标识`ActorDayPlans.PresetVersion=LC.npc-week-plans.v1`。
- [COMPUTED] `ActorDayPlans.ResolveBase(PlanState basePlan,int day,DayOfWeek weekday,AnchorEligibility eligibility,IEnumerable<string> knownTrainingAnchorIds)`返回本人的不可变ActorDayArrangement。
- [COMPUTED] `ActorDayPlans.FollowPlayer(PlanState npcBasePlan,DayPlan playerDay,string bindingId,AnchorEligibility targetEligibility,IEnumerable<string> knownTrainingAnchorIds)`复制完整冻结安排，校验同run/day、不同actor、非空绑定标识、同动作目录版本及目标当前锚点资格。不会SavePending或改NPC基础计划。
- [COMPUTED] `ActorDayArrangementPersistence.Export(value)`/`Restore(record,expectedRunId,expectedActorId,expectedDay,catalog,knownTrainingAnchorIds)`使用独立schema1字段记录。HasCompanionSource明确来源，禁止用inline null猜来源；目标身份、溯源、动作版本、完整组次及锚点引用均校验。恢复只按已知目录校验历史引用，不拿今日解锁重判过去资格。
- [COMPUTED] `TrainingCatalogAdapter.ToTrainingEntries(ActorDayArrangement)`返回独立训练DTO数组；修改此数组不影响安排。训练按目标NPC自身TrainingProgress冻结梯度/配额，复制内容不包含玩家身体、结果、自动资格、随机状态或训练会话。

## [FRAME] 所有权和集成顺序

[FRAME] Life拥有CompanionBinding邀请、生效/解绑、关系与唯一当前搭子。Planning不读取Life实体，也不自证绑定合法；父只把已批准有效绑定的bindingId和玩家DayPlan传入FollowPlayer。生活稿§5及赛事稿§12已澄清当前搭子采用共同世界目标当日已生效的锚点资格：N01激活后，N02向玩家和搭子传同一资格快照，不另套NPC个人成长解锁。接口仍显式校验，未知、不合法或只有“明日将解锁”的锚点在今天拒绝；漏传资格须在入日提交前修正，不授解锁、不替换为下午、不自动解绑。

[COMPUTED｜HIGH] 已只读核对上述两份策划文本，额外锚点的产品规则歧义关闭；本轮仅同步本文与父任务，不改冻结源码。世界集成仍需验证早/晚在N01生效后可用于双方，以及未生效/漏传时不提交错误日切；不能将规则对齐视作测试通过。

[FRAME] N02先ApplyDay玩家pending及Life绑定，再ApplyDay所有NPC基础计划；对当前搭子调用FollowPlayer，其余ResolveBase。每个NPC即使跟随期间也持续推进自身basePlan的日期，不覆盖它；解绑/更换生效日直接解析原NPC当日基础计划。玩家休息则同步休息，TrainingEntries为空，父不得为休息日调用要求非空的DayTrainingQuota.Freeze。

[FRAME] 父/world以(run,actor,day)保存唯一不可变安排，重复步骤相同内容复用、不同内容冲突；Planning是纯函数，没有自造ledger。配额PlanRevision引用应使用父稳定arrangement引用，不单独用玩家revision，以免不同actor碰撞。历史binding/sourcePlan引用和同日片段唯一性由父根校验；记录自身不能代替有效关系授权。训练claim、入口抽取、到场、伤病及结果均归原模块，同安排不保证实际训练。

[COMPUTED] ActorDayArrangement保留目标RunId/ActorId/Day/BasePlanId/BaseRevision及SourceActorId/SourcePlanId/SourceRevision/SourceEffectiveDay/BindingId，Slot为复制的不可变值，包含来源slot index、休息、锚点、组合、档位、entryId、有序动作、动作版本、确切组数次数。运行时复制不重新读取目录计算量；恢复用冻结目录版本校验记录量一致，构造仍使用记录值。entryId是安排内部条目身份，不可单独充当跨角色workout/set身份。

## [COMPUTED] 验证与冻结交付

[COMPUTED｜HIGH] 实际运行`dotnet run --project Assets/FitnessSimulator/Tests/Production/Planning/Standalone/Planning.Verify.csproj --configuration Release`，退出0，22 passed/0 failed：原14项加新增8项。独立运行器编译真实计划/训练目录/训练配额与真实NUnit，不含Unity序列化测试。

| 新测试 | 已执行断言 |
|---|---|
| [COMPUTED] FourPresetWeeksMatchLifeScheduleAndReuseAllActionDefinitions | 四周日序/组合/时间/量匹配，正式目录验证合法，竞争者完整八动作保留。 |
| [COMPUTED] FollowCopiesFrozenContentsButKeepsTargetIdentityAndBaseState | NPC目标身份与玩家溯源分离，base引用/pending不变，副本及训练DTO不串改。 |
| [COMPUTED] N02PendingActivationPrecedesFollowAndFutureEditsDoNotMutateSnapshot | 玩家pending生效后复制最新4×10/晚上，之后编辑不改今日安排。 |
| [COMPUTED] UnbindingResolvesUntouchedBaseOnCorrectWeekdayAfterMultipleDays | 连续跟随后解绑，周四恢复老手腿臀/晚上，而非跟随计划。 |
| [COMPUTED] PlayerRestProducesRestArrangementAndNoTrainingEntries | 休息同步且无训练条目。 |
| [COMPUTED] FollowRejectsWrongOwnerDateRunAndUnavailableOrUnknownAnchor | 跨局/日期/相同actor/无绑定/缺资格/仅明日解锁/非目录锚点拒绝。 |
| [COMPUTED] SameArrangementUsesNpcOwnTrainingProgressForQuota | 用NPC自身梯度2生成配额；传玩家进度被训练服务拒绝。 |
| [COMPUTED] ArrangementRecordCopiesValuesAndRejectsTamperedOwnerAnchorAndWorkload | 往返无可变别名，组次/版本/身份/来源位及餐食锚点篡改拒绝。 |

[COMPUTED｜HIGH] 已只读核对父串行Unity报告`TestResults/M1/editmode-20261023-013440-172.xml`及同号JSON：197/197通过（正式193、历史4），0失败/跳过、exit0、sourceDrift0。新增8项ActorDayPlansTests和`ActorDayArrangementJsonTests.BaseAndCompanionRecordsRoundTripWithActualJsonUtility`均逐项Passed，实际引擎序列化待测项现关闭。覆盖清单为`TestResults/M1/M2-first-regression-coverage.json`；本任务未另启动Unity，较早013234编译失败不替代本次最新通过证据。

[FRAME] 全世界N02重入/唯一日片段、绑定应用、解除、同日人物自主执行与实际UI均待父集成；当前M2未接主流程，不凭模块回归声称阶段完成或完整S09/AC10通过。

[FRAME] 本交付源码冻结，等待父测试反馈再修；没有实施比赛、关系持久化或扩大计划UI。

[COMPUTED｜HIGH] 2026-10-23 新训练时段默认调整：按制作人最新修正，新玩家默认 `noon`；竞争者、初学者的新预设为 `noon`，老手、教练保持 `evening`。此前“统一晚上”口径已撤回。本次只修改新建默认值，不改变训练日序、动作、组次或训练量。

[COMPUTED｜HIGH] 存档结构、内容版本、全量可信锚点目录、恢复与搭子接口未改。新增有限域测试验证旧 `morning` / `afternoon` 的 Active、Pending、冻结搭子安排保留原时段，Pending 次日仍按原文生效；向仅允许中午/晚上的编辑资格重新提交旧时段会拒绝，既有安排不变。新默认中午可保存，改选晚上只作用于次日。

[COMPUTED｜HIGH] 最新默认版本执行 `dotnet run --project .utmp/planning-anchor-defaults/Verify.csproj --configuration Release`：24 passed、0 failed，退出0。仅执行计划及搭子有限域用例，不包含赛事或引擎。自有引擎序列化测试的构造资格已适配中午默认，本轮未执行这些引擎测试。

[FRAME] 新编辑资格与运行冻结资格分离、旧草稿 legacy 提示及主动改选、仅晚上开放编辑、上班与收入事件仍由对应应用/界面负责人集成；本交付不代表这些行为已完成。源码交付后冻结。

[RULES I BROKE]: 无。
