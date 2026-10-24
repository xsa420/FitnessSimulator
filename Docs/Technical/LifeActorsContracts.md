# [FRAME] 生活经济、关系与人物自主技术契约

[FRAME] 日期：2026-10-23。唯一交付：本文件。版本LA-0.3。状态：专项策划及跨程序协议已对齐，供父聊天整合；没有业务实现、配置落地或验收通过。产品规则仅指游戏，不转译成现实训练、饮食或关系判断。

[COMPUTED｜HIGH] 已读取在线主文档（返回最后编辑时间2026-10-22T16:19:20.567Z）、ProgramModulesAndDependencies、ProductionAlignment、README、Architecture、Verification，以及Runtime/Persistence/SaveContracts.cs、LocalJsonStore.cs、Runtime/Training/TrainingContracts.cs。现存存档只有日期、单后继、随机与JSON外壳；训练接口仍含historicalPerformanceSourceId、consecutiveStrengthSets旧字段。本方案不沿用旧动作回退、历史成绩自动执行或旧人员编号。

[INFERRED｜HIGH] 当前原型不能保证跨域交易或多人物日切恢复；需要下列领域协议接入公共提交器，不能凭单文件替换接口宣称事务已实现。

## [FRAME] 1. 决定分层与实现门槛

| 类别 | 内容 | 门槛 |
|---|---|---|
| [FRAME] 既定产品 | 安卓文本首版；四名自主人物；各自资源隔离；多店；同店ID跨事件共享；整包交易；新解锁下轮刷新；独立三餐即食；付费教导即时付费且不追溯；每训练每动作一次知识；满好感免费仍占机会；单搭子次日更换；体脂前魅力用于表白 | 不缩范围，不等待复杂美术 |
| [FRAME] 程序技术决定 | 领域只准备Patch；公共协调器一次持久化；资源复合键、固定精度金额、报价版本、业务幂等键、私有随机流、声明式合法操作 | 由父聊天合并公共契约，具体公共字段以FlowPersistenceContracts为准 |
| [FRAME] 已对齐候选产品细化 | 到场先于同锚点交互、完整训练安排同步、教学额度取max并按后续有效组补差、八动作NPC轨迹策略、具体收入菜单概率 | 生活策划已给值与算例；候选不等于用户确认或平衡通过 |
| [FRAME] 实施门槛 | 日期屏障/K额度结构已可设计；八动作轨迹合法性尚须实际判定器验证，正式NPC不能靠文档样例认定完成 | M2前首NPC轨迹与提交故障通过；M4四人物八动作全量 |

## [FRAME] 2. 数据结构、字段与唯一写权限

[FRAME] 所有运行数据处于RunId；ActorId是稳定身份，不是姓名/列表位置。玩家与四人物均有独立ActorId。配置ShopId可共用，运行商店键必须是(RunId,ActorId,ShopId)。ResourceScope沿用公共封套，调用者不能任意指定另一个角色的资源权限。

| 领域片段／主键 | 字段语义与约束 | 唯一语义写者 |
|---|---|---|
| [FRAME] Wallet(actor) | balance：最小货币单位非负整数；affluence：收入能力，与消费余额分开；revision；incomeReceiptKeys(day,step)。金额乘法与累计检查溢出；货币尺度及舍入策略配置版本固定 | 经济；身体/关系只可请求经验证效果 |
| [FRAME] Inventory(actor,itemId) | quantity：非负整数；ItemDefinition用途为ActiveUse或StoryCondition且互斥；无容量、装备、丢弃或累计获得玩法统计 | 经济 |
| [FRAME] NutritionDay(actor,day) | intakeKcal、已完成mealReceiptIds、mealChoice及来源；没有餐食库存；当天摄入账在日结恢复读取后封存/换日 | 经济记摄入；身体算恢复/压力/体脂效果 |
| [FRAME] ShopRuntime(actor,shop) | cycleId、generatedAtDay、nextRefreshDay、catalogRevision、slotEntries[offerId,productId,packagesRemaining]、unlockLedger[productId,unlockedAtCommit,eligibleFromCycle]、refreshReceipt | 经济；流程仅请求到期刷新 |
| [FRAME] ProductDefinition | productId、itemId、unitsPerPackage正整数、packagePrice、packagesPerRefresh、unlockCondition、hintVisibility；一包只能单种道具；同item不同product包规格可并存 | 配置加载只读；领域校验 |
| [FRAME] Relationship(owner,target) | affection、currentTier、claimedTierRewards集合、retainedUnlocks；有向关系键；退档不清一次奖励；固定休息对话收益类型独立于魅力倍率 | 关系 |
| [FRAME] InteractionOpportunity(actor,session) | opportunityId、Available/Consumed、consumedByEventId、reason；一次训练共享一个机会，不按NPC/动作/组重置 | 关系；流程负责事件操作点生命周期 |
| [FRAME] ItemUsePoint(actor,eventInstance,pointId) | Available/Consumed、resultCode；与训练互动机会不同的类型，不能共用布尔字段 | 经济定义消耗语义，事件保存游标 |
| [FRAME] CompanionBinding(player) | activeActorId或None、pendingActorId或None、pendingEffectiveDay、bindingRevision；区分无待修改与待解绑；不得让NPC策略调用玩家的绑定命令 | 关系；计划消费已生效只读绑定 |
| [FRAME] TeachingEntitlement(actor,workout) | entitlementId、sourceActorId、sourceKind、activatedAtCommitId、firstEligibleWorkoutSetSequence（含下界）、knowledgeRuleId、nominalTarget；付款收据引用；权益与互动机会分开保存 | 关系；身体写实际动作K |
| [FRAME] TeachingAwardLedger(actor,workout,exerciseId) | awardedNominalAmount、awardReceiptIds、lastAwardSetId；多个教学来源合并为同动作目标max，按后续合格组补差；不能只存每来源布尔值 | 关系；同组提交绑定身体KnowledgeEffect |
| [FRAME] RomanceProgress(owner,target) | eligibleRouteId、giftReceipt、specialEventInstanceId、attemptReceipt、successFact；礼物交付与后续表白是两个可恢复事件；资格不能只用UI布尔值 | 关系；结局只读成功事实 |
| [FRAME] ActorAutonomy(actor) | policyId/version、basePlanReference、policyMemory、currentDecisionReceipt、actionCursor、arrivalReady、barrierReached、expectedActorRevision；事件、训练游标通过ID引用，不复制 | 自主模块只写决策/调度片段；各领域写各自业务 |
| [FRAME] TrainingDayClaim(run,actor,day) | sessionId、claimStatus、scheduleRevision、arrivalAnchor；到场/陪练/自主训练共同引用一份，漏训/结束后不得新建第二份补练 | 训练模块拥有；自主模块请求/查询 |

[FRAME] 字段写权限与物理持久化分开：任何领域Patch只能改自己拥有的字段，提交器验证所有Patch后保存完整候选状态；UI、NPC策略、测试驱动、存档适配器均不得绕过领域改钱、好感、K、力量或日期。富裕值事件修改仍经经济；身体成长资源均在同Actor命名空间。

## [FRAME] 3. 命令、结果、查询契约

[FRAME] 公共CommandEnvelope、WorldBarrierId与ActorCursor引用FlowPersistenceContracts.md §2—4、§7；存储和未知提交引用§9—10，本稿只定义领域载荷，不维护第二版封套。公共上下文包括RunId、ActorId、ResourceScope、OperationId、Source、配置版本、ExpectedRevision、事件实例和随机流引用。Source是公共权限来源Player/NpcProduction/Test，producerSource是训练证据来源，两者正交，具体映射见§8。WorkoutId是训练会话正式名称，本稿session语义均映射到WorkoutId；SetId结构沿用训练稿，不能自行拼接自由文本。

| 命令／查询 | 领域输入 | 结果／前置约束 |
|---|---|---|
| [FRAME] QuotePurchase / Purchase | shopId、offerId、cycleId、packageCount、报价revision；确认时再验证 | TransactionReceipt；报价不锁货、不抽随机；允许多笔购买，单笔整包不拆卖 |
| [FRAME] ChooseMeal | mealEventId、optionId或Skip | 扣款+摄入+固定身体效果+餐点完成同提交；余额不足不完成选择、可改选 |
| [FRAME] UseItemAtPoint | pointId、itemId或Skip、固定目标引用 | 机会消耗和物品/效果结果；非法业务使用也结束机会；运行配置错误不冒充业务失败 |
| [FRAME] DeliverStoryItems | eventId、requirements、成功效果规则 | 足额全交付或零扣；不把HasItem条件查询当消耗 |
| [FRAME] ApplyMoneyLoss / ApplyItemLoss | 来源事件、非负损失量 | 最多扣到0，返回actualLoss；不生欠款，不附加猜测惩罚 |
| [FRAME] SettleIncome | actor/day/step、incomeRuleId | 读取执行时富裕值，收入+收据一次提交；同键复用 |
| [FRAME] RefreshDueShops / ObserveUnlocks | actor、逻辑day、已提交/候选条件视图 | 未访问仍刷新；随机无放回选productId；解锁只更新账本，不立刻改列表 |
| [FRAME] Interact / BeginTeaching | session、target、option、机会ID、预览revision | 可达/在场/机会/余额等重验；付款+权益+机会原子；后续剧情自有成本 |
| [FRAME] RequestCompanionChange | target或解绑、currentBindingRevision | pending绑定及生效日；当前日不改已冻结安排 |
| [FRAME] DeliverRomanceGift / ResolveConfession | route/event、礼物要求；表白预览token/状态revision | 礼物合法交付收据；表白返回显示概率、独立抽样凭证与成功/失败后继，不直接调用结局 |
| [FRAME] DriveActorToBarrier | actor、WorldBarrierId、expectedActorRevision | Progress/Waiting/Reached/ConfigurationError；返回下一合法操作请求，不直接产成长Patch |
| [FRAME] QueryLifeView / QueryLegalOperations | 观察者/操作者、事件/训练上下文、revision | 成本/余额/卡路里/机会/可操作性/原因；隐藏解锁按配置屏蔽；NPC私有详情不给玩家 |
| [FRAME] QueryNutrition / QueryEntitlement | actor/day或actor/session/setOrdinal | 身体计算只读快照；K资格读组前快照，结果后更新获奖标记 |
| [FRAME] QueryCompetitionActorSnapshot | 赛事授权、目标屏障、actor revision | 只在屏障达成后读取；属性披露由赛事视图过滤，不返回整套NPC事件日志 |

[FRAME] TransactionReceipt字段：transactionId、operationId、domainSettlementKey、actorId、eventInstanceId、configVersion、before/afterRevision、businessOutcome、actualMoneyDelta、itemDeltas、consumedOpportunities、entitlementIds、bodyEffectReceiptIds、randomDrawRefs、nextCursor、commitId。失败同样有明确结果；领域结果不等于是否持久化成功。

[FRAME] 错误分层：RejectedCommand表示权限/旧版本/未知报价/非法命令，不消耗业务机会；BusinessFailure是合法执行后的产品失败，可能消耗机会及费用；ConfigurationError保留最近有效检查点并停止错误步骤；PersistenceFailure未发布新状态，重试同OperationId。不得把前置条件不满足的所有情况统一映射为RejectedCommand：主动道具业务失败是明确的消耗机会结果。

[FRAME｜公共v0.2映射] 上段为领域错误语义；对外使用公共CommandResult的Rejected/ConfigError/StorageUnavailable/StorageOutcomeUnknown，不另增冲突枚举。PreparedOutcome必须显式填outcomeKind、eventDisposition、checkKind、opportunityPolicy，不能从失败猜测完成：余额不足教导/餐食=BusinessFailure+KeepOpen+None+Keep；无效道具=BusinessFailure+CompleteNode+NormalCompletion+Consume；购买成功=Success+CompleteNode+NormalCompletion（不消费训练互动）；合法表白失败=BusinessFailure+CompleteNode+NormalCompletion（已用赠礼机会不二扣）。所有NPC提交的checkKind=None，不扫描玩家结局。重放回执用AlreadyCommitted，旧回执不因缓存淘汰失效。购买容器继续下一交易节点，离店不再重算既有交易。

## [FRAME] 4. 交易状态机与恢复

[FRAME] 交易状态为Offered → Validating → Prepared → DurableCommitted → Published。Offered只是只读预览；Prepared包含经济/关系/身体/流程Patch、随机后状态及记录，全部仍在候选快照。只有统一持久化成功才发布。验证业务失败可生成仅消耗机会/推进游标的Prepared；配置/权限错误不能生成收益。

[FRAME] 业务幂等键不只依赖客户端OperationId：购买使用(eventInstance,transactionOrdinal)，教导使用(workout,interactionOpportunity)，餐食使用(mealEventInstance)，道具使用(pointId)，每次教学知识效果使用(workout,exerciseId,setId,TeachingDelta)，总额度账使用(workout,exerciseId)，日收入使用(actor,day,step)。同业务键同参数返回已存收据；同键不同参数返回Conflict且不覆盖。重复发送新的OperationId也不能绕开已消费机会或已完成事件。顺序遵循公共协议：验证身份/权限后先查原操作收据，再对新操作校验版本；根版本因独立计划保存变化不使已存组结果失效，按组前相关领域版本校验。

[FRAME｜已对齐候选] 每笔购买和教导确认是独立完成子事件，商店访问/对话是容器，已经提交的交易不因退出容器回滚；生活策划已确认，公共流程支持此边界。不允许“钱立即可见、离开商店才保存”的中间态。付费互动后续失败默认无退款；只有明确配置退款效果的后续子事件，按原收据为退款上限且只退款一次。退款不隐含恢复机会。提交结果未知时冻结操作队列并按公共账本核对，不能直接当保存失败换ID再买一次。

| 中断窗口 | 恢复行为 |
|---|---|
| [FRAME] 报价后退出 | 不扣钱/库存/机会；重新查询当前版本报价 |
| [FRAME] Prepare后、保存前 | 丢弃全部候选Patch，恢复事件起点与随机前状态；不保留局部扣款 |
| [FRAME] 临时文件写入失败 | 公共存档仅恢复最近已提交且校验有效的权威根；未提交候选不生效。最新已提交根或其可见日志前缀损坏必须报CorruptCommitted并停止加载，不能悄悄退旧档；未知提交先核对账本 |
| [FRAME] 保存成功、通知/动画前退出 | 从收据与正常后继恢复；不再次支付、不再次交付、不再次抽样 |
| [FRAME] 购买1完成、购买2未完成 | 保留第1笔，恢复第2笔起点；访问容器结束不再重复汇总 |
| [FRAME] 付费教导完成、下一组未完成 | 保留扣款/权益/机会，重做未完成组；没有有效训练不追溯退款或发K |
| [FRAME] 组结果已保存、结算前 | 复用训练结果ID和组前权益，身体与K记账只结算一次 |
| [FRAME] 表白已提交、结局演出前 | 保留概率/抽样/成功事实；结局模块恢复其已登记正常后继与历史 |

## [FRAME] 5. 机会与失败必须分型

| 操作与情况 | 钱/物品 | 机会与后继 |
|---|---|---|
| [FRAME] 教导余额不足 | 零扣款，无权益 | 唯一互动机会仍Available，可改选 |
| [FRAME] 满好感免费教导 | 价格由规则为0，无扣款；仍生成权益收据 | 消耗唯一互动机会；不能重复免费教导 |
| [FRAME] 正常付费判定互动失败 | 已提交费用保留；只执行配置失败效果 | 消耗机会；不默认减好感/加压力/退款 |
| [FRAME] 主动道具数量不足/条件不符/固定目标无效 | 不扣物、不生效 | 本操作点Consumed，结束；不能同点重试 |
| [FRAME] 合法主动道具实际变化为0 | 扣1件 | 消耗机会，标合法成功，不按无变化退款 |
| [FRAME] 剧情交付不足 | 零扣物、零成功收益 | 一般剧情走配置不足后继；恋爱赠礼保留训练互动机会，不套道具失败消耗规则 |
| [FRAME] 非法相遇/教导条件失效 | 零扣费、无权益 | 不消耗训练互动；成功对话/邀请/赠礼/支付共用一次机会；暂不互动可留到本次后续休息 |
| [FRAME] 购买余额/库存不足 | 零扣款、零发物、库存不变 | 可在访问期间改选/离开；购买不是唯一休息互动机会 |
| [FRAME] 用餐跳过 | 零费用、零摄入 | 完成当前餐食；被明确占用餐点直接跳过不补 |
| [FRAME] 点击前取消/未确认 | 没有操作、没有费用 | 仅在业务允许取消的界面成立；普通强制选择组不增取消入口 |
| [FRAME] 已确认操作中断 | 依完成子事件边界全有/全无 | 恢复相同实例，不通过退出重新获取已用机会 |
| [FRAME] 版本过期/越权/重复命令 | 无新增变化 | 返回新视图/原收据/拒绝原因；不是事件业务失败 |

## [FRAME] 6. 商店刷新与资源隔离

[FRAME] ShopDefinition共享商品池；ShopRuntime私有。玩家在事件A访问ShopX后，在事件B访问同ShopX读取同cycle与剩余包数；NPC在同ShopX读取其自己实例。ShopX与ShopY的周期、列表、解锁账及随机流相互独立。NPC不会消耗玩家钱、库存、商店包数、相遇或剧情机会，也不能占用玩家当前搭子。

[FRAME] 运行周期初始化与到期由游戏日期推进，不用现实钟或打开界面触发。每轮从当轮有资格的productId集合无放回抽取，候选不足允许少于商品位；刷新完整覆盖旧列表与包库存。OfferId携带cycleId，旧报价不能买到新库存。

[FRAME] 解锁首次满足后写入unlockLedger；eligibleFromCycle大于当前已生成cycle。刷新过程固定资格快照，刷新同提交新满足的条件不能偷偷进入该轮；需在流程确定“观测解锁→刷新”的精确顺序，并在验收中固定边界。如果解锁先于下一轮刷新完成则可进入下一轮候选，但不能保证抽到。

[FRAME] 到期跨越多个周期时不以一次随机覆盖掩盖中间日事件：正常游戏逐日推进；恢复读取已提交nextRefreshDay和cycle，不能把真实时钟差当跳天。若以后提供迁移/调试快进，必须按声明的日期步骤推进并标记测试来源。

## [FRAME] 7. 关系、教导与恋爱时序

[FRAME] 相遇查询只读当日有效安排和实际到场记录；仅“计划同锚点”不等于随机事件已进入训练。普通对话与已有休息并行，不额外增加时间；单独后继成本由流程计算，不能同时把休息时长和对话时长重复记入。

[FRAME] 固定休息好感直接应用配置固定值；一般判定互动读取普通EffectiveSocialCharm与心理压力倍率，概率截断；一般无判定互动只放大显式PositiveBenefit标记的正数值收益。代价、档位奖励、固定休息好感、物品、餐食不套此倍率。一般互动精确曲线由策划/身体契约给出。

[FRAME｜数值接入决定] 生活产品§5的一般判定曲线保持p=clamp(p0×(1+0.5√x)×(1−0.75y²),0,1)。x为有效社交魅力、y为心理压力按字段上下界归一后的精确有理数；上下界及曲线版本必须有效，归一后裁至[0,1]。√x复用TrainingGrowthContracts规定的SqrtRational12V1(a,b)，输入a/b=x、返回12位half-up定点根；此处根是无量纲，不能误用BodyMetrics力量单位或L微单位舍入。纯核放在中性Numeric模块，关系和BodyMetrics分别调用，关系不依赖Competition实现，也不另写Math.Sqrt。

[FRAME｜比较与版本] 根返回整数r后按r/10^12参与后续精确有理运算，乘p0与两个倍率并clamp得到p=n/d。随机取既定流的32位无符号整数U，使用宽整数比较U×d<n×2^32；相等失败。一般隐藏概率不追加微单位或.001舍入，.001量化仅属于表白pShown专门规则。持久化关系曲线版本、numericKernelVersion=NumericRationalV1、sqrtKernelVersion=SqrtRational12V1及输入状态版本/随机凭证；恢复按原版本执行，不用新核重算已提交结果。

[COMPUTED｜HIGH] 金样本：x=1/4、p0=1/2、y=0，根为500000000000/10^12=1/2，魅力倍率5/4、压力倍率1，因此p=5/8=.625。32位阈值为2684354560；U=2684354559成功，U=2684354560相等失败。该样本是独立整数算术核对，不是已实现规则核的运行验收。

[FRAME] 教导确认在休息操作点先完成扣款与权益。组前冻结权益；本组至少1次有效完成且WorkoutSetSequence达到权益下界才有该来源资格。WorkoutSetSequence从1起、整次训练跨动作单调递增，只在实际开组分配；SetOrdinal是单条计划动作局部序号，不能拿它比较付款边界；CommitId仅作审计。身体先用组前K算本组熟练度，再应用失败K和教学K；关系额度账与身体K同提交。付款前只有卧推有效组，付款后仅弯举有效组时，卧推不追溯；付款后卧推还有有效组才可获得新增教学额度；零完成不占教学额度。

[FRAME｜已对齐候选] TeachingEntitlement只负责资格，不直接给力量或熟练度。生活稿§5规定老手目标1、教练目标3，目标取max，不叠加为4。每个合格组求当时生效来源的target=max(nominalTarget)，教学请求delta=max(0,target−awardedNominalAmount)，同提交更新额度。老手已授1后支付教练，后续同动作有效组补2；无后续有效组保持1。失败力量K1独立来源，不能占教学额度。当前在场教练搭子自带3且隐藏重复付费选项；仍能普通对话。搭子风险按概率百分点减免，150必伤优先，无治疗能力。

[FRAME｜策划补充确认] 额度账记录已兑现的名义额度，不按实际ΔK记账。K99收到教练目标3时身体实际+1，教学账仍记3；后来外部事件降K不能重领。老手额度1因封顶实际+0也记1，随后付费且再次有效训练最多补2。上限裁剪由身体唯一处理，关系记录名义消费防止刷取；每动作总额一次的含义是同训练最多兑现最高教学目标，不禁止合法提升来源后的差额提交。

[FRAME｜已对齐候选] 搭子申请只写pending，首邀也次日生效。入日按流程N02先应用玩家有效计划和关系绑定，再由计划构造不可变TrainingScheduleSnapshot，包含planRevision/date/anchorId/orderedPlanEntries/volumePreset及每动作组次。生活策划确认同步完整锚点、动作顺序与训练量；解绑恢复NPC自身basePlan，不清好感。自身能力、梯度、伤病、自动资格、K与完成结果不复制玩家。计划同步不保证随机训练入口出现。

[FRAME｜已对齐候选] 满好感赠礼原子扣足剧情礼物、消费训练互动机会并产生giftReceipt，然后进入零额外时间特殊事件链。选择今天先不说清待表白，不退款且不保留免费再试；每次再试需满好感和新礼物，尝试冷却7完整日（D10后D18）。表白按最新已提交状态冻结BaseCharm（体脂修正前）、ThinBuildIndex（CompetitionAndStartSpec §5唯一公式、身体实现）与BodyFat独立值，计算并显示概率。输入带bodyRevision/metricVersion，不使用普通社交有效魅力或随机评委总分。生活稿§6给p=clamp(.15+.30c+.35L+.20b,.05,.95)，p先四舍五入至.001为pShown，显示至0.1%，判定也使用u<pShown。token携带revision，状态改变需重新预览/确认。抽样、尝试冷却、效果、收据与后继同提交；失败不退礼物、不隐含扣好感/加压力；成功只发事实，由结局模块处理，不能豁免参赛。

## [FRAME] 8. 自主人物执行与日期屏障

[FRAME] 自主驱动状态机：WaitingForBarrier → ChoosingEntry → ArrivalCommitted → ChoosingLegalAction → ExecutingEvent/Training → AwaitingCommit → ReachedBarrier；任何节点可进入BlockedOnConfiguration。它读取合法操作集合，按policyId/version及私有随机流选择并提交真实命令；身体、金钱、学习、饮食、道具和训练效果均由同一规则执行，不按日期套成长表。

[FRAME] 生产策略最小选择链：读取计划/入口候选→在执行器已抽中的选择组中选合法操作→根据余额/菜单选择吃或跳餐→根据伤病、自动资格和组前状态选择合法训练类型/休息→生成小游戏输入→同判定器产生结果→同身体规则结算→继续事件或日结。执行器才拥有入口抽取；策略优先训练不能保证抽到训练组。只有普通候选为空才允许默认入口；非空但策略不偏好仍须从当前合法候选中选，不得绕过入口去默认。策略权重/阈值/预算读取生活稿§7的版本化候选，不以内置0补齐。

[FRAME] 训练证据使用TrainingGrowthContracts的producerSource枚举PlayerTouch/ScriptReplay/NpcPolicy/AutoCapacity/TestInjected，executionMode另为Manual/AutoCapacity；它们不是第二套公共Source。映射为Player+PlayerTouch、NpcProduction+NpcPolicy、Player或NpcProduction+AutoCapacity；ScriptReplay只能Source=Test且evidenceKind=Replay，TestInjected只能测试域且productionEligible=false，不进入正式存档或授生产资格。来源由入口能力授予，调用者不可自报升级权限。NpcPolicy以八玩法合法输入序列和逻辑时钟驱动同判定器，保存policyVersion、输入摘要/必要可重放轨迹、随机引用与结果凭证，禁止直接填有效次数/结果。AutoCapacity只在该Actor持有同动作同梯度资格且为容量组时使用，完成当前目标量；挑战始终Manual，即使输入由NpcPolicy产生，也不能复制玩家成绩或默认成功。

[FRAME｜已对齐技术字段] NpcInputTrace沿用训练端口：InputSeq、InputTick/activeTicks、pointerId、phase、normalizedXY、buttonId、policyVersion、seedRef。输入域、时间合法性由训练规则核校验；人物策略只读当前可见提示，不能读未来随机。InputTick是确定性逻辑步，虚拟推进不等待现实帧；公共游戏耗时已统一为gameSeconds，只由有效完成量×secondsPerRep及已提交休息产生，不能把输入误差时长额外计入。

[FRAME｜已对齐候选] 策略配置引用LifeAndCharactersSpec §7：误差e=(2u−1)×a×W，四人物a为0.90/1.10/0.55/0.40，额外输入失误率0.08/0.12/0.03/0.02；W是D=0基准容差，不能跟随当前难度窗口收窄而抵消难度。各人阈值/休息/餐食预算按该稿读取；组前最多2次额外长休息仍过阈值则停止，挑战失败本动作不再挑战是NPC策略而非玩家规则。四人初始值/计划由该稿§4提供初始化意图，经济/身体/计划分别验证，不由自主模块直接写字段。

| 八动作适配 | 生产输入策略候选与失败来源 |
|---|---|
| [FRAME] 卧推 | 每0.1秒读取可见左右偏差反向控制；延迟反向输入可失败 |
| [FRAME] 哑铃弯举 | 升降提示两次时戳带固定基准窗误差；方向错/时刻错由判定器判失败 |
| [FRAME] 高位下拉 | 已展示检查点路径每0.05秒采样加横向误差；偏离通道失败 |
| [FRAME] 绳索下压 | 绿灯后0.15秒加时间误差释放，等待回程；提前释放失败 |
| [FRAME] 深蹲 | 记忆已经展示的三项后依次回中心输入；错误方向失败 |
| [FRAME] 臀推 | 按已展示目标时长释放后等待0.7秒顶端静止保持；提前释放失败 |
| [FRAME] 肩推 | 当前开口路径逐段控制，叠加横向误差；偏离通道失败 |
| [FRAME] 侧平举 | 两触点并行、分别施加到达误差、共同停留后释放；同步差超限失败 |

[FRAME] 八适配遵守训练稿最大尝试N+2、第三次失败终止及输入合法域；禁止超界坐标绕过核校验。相同合法轨迹通过玩家回放端与NpcPolicy端应产相同有效量/结果，来源标记不同；正式操作可错，协议数据不能畸形。训练给出的候选例使用合法弯举少量计划2×8、单组N=8，输入有效/有效/无效/有效/无效/无效，预期由规则核得到v=3且第三失终止；保留3/8量，力量失败K1独立，游戏耗时仅3×动作耗时，策略不能直接填写v=3。该例尚待规则核实现重放，不表示已经运行；不采用训练方已撤回的3×8计划及其疲劳58算例。

[FRAME] 同日身份：TrainingDayClaim由训练拥有，键(run,actor,day)。到场、被选为搭子、独立自主推进只引用sessionId；展示加入玩家训练不新造NPC会话。NPC身体独立，和玩家相同计划也用各自组前数据、梯度、输入与判伤；同步安排不等于同步结果。结束/漏训关闭当日机会，晚到或重新恢复不能补开。

[FRAME｜已对齐候选] 公共WorldBarrierId=(run,day,phase,anchorId)。各ActorCursor保存arrivalReady、actionCursor、barrierReached、expectedActorRevision及训练/事件结果引用；actorIndex只用于调度遍历，不能作幂等身份。流程先让各NPC完成当前锚点入口与到场意图提交，再允许玩家读取相遇；仅双方实际进入训练且没有明确剧情占用才进入在场名单，名单冻结至本次训练结束，不因NPC先完成组消失。其后各角色执行自身事件，在锚点结束汇合，禁止先算完NPC全天再给玩家同日交互。生活策划§4/7与流程程序已确认此候选结构。

[FRAME｜已对齐候选] 日结采用同phase逐角色推进：各人收入/恢复/清理由公共日结步骤驱动，ActorCursor记录已提交子步；单人失败阻止屏障完成，其他已完成者不重算。消费流程稿E01收入→E02摄入评估→E03局部恢复→E04压力恢复裁0→E05不足压力追加→E06临时疲劳清零→E07准备次日到期→E08解锁收集→E09汇总→E10等点击；伤病实际解除在N01。次日N01日期/新日账→N02计划/搭子→N03私域店刷新→N04赛程冻结→N05首锚点。赛事屏障为DayOpenReady后的BeforeAnyDailyEvent，必须早于清晨/早餐；原“首普通锚点前”已撤回，避免早餐先改变属性。具体步骤引用FlowPersistenceContracts §7，不在此重定义世界状态机。

[FRAME] 恢复算法：加载最近完整提交→按WorldBarrier与各ActorCursor定位→已到达者跳过→有已存组结果者继续原结算→未完成普通事件/组按公共恢复点重进→相同策略状态与随机状态产生相同选择→重新达到全员屏障后前进。若配置版本/策略版本不兼容则停止并报诊断，不能换策略悄悄重演已确认选择。

## [FRAME] 9. 依赖与解耦

| 依赖方向 | 必需输入／输出 | 禁止耦合 |
|---|---|---|
| [FRAME] 生活→流程/存档 | CommandEnvelope、提交Patch、事件实例、WorldBarrier、恢复结果 | 不自建文件、不直接NextDay、不自己判终局 |
| [FRAME] 经济→身体 | NutritionSnapshot、固定效果请求；身体返回效果Patch | 钱包不复制身体公式，身体不直接扣钱 |
| [FRAME] 关系→训练/身体 | 组前权益、相遇/搭子快照；组结果有效量、组序号、K收据 | 关系不写力量、不修改已结算组 |
| [FRAME] 自主→计划/事件/训练 | 当日不可变安排、合法操作集合、生产输入端口 | 不复制玩家成绩，不直接写成长，不从UI读状态 |
| [FRAME] 比赛→自主/身体 | 达屏障版本化快照、披露视图 | 比赛不触发NPC额外练一天，不访问隐藏行为全量 |
| [FRAME] 表现→生活查询/命令 | 确定成本、状态revision、表白概率、错误原因 | 视图不拥有库存/机会/随机抽取 |

## [FRAME] 10. 分阶段实施包与验证情境

[FRAME] 以下都是未来测试设计，未执行。每条证据记录config/policy版本、RunId/ActorId、操作序列、before/afterRevision、commitId、随机引用与实际断言；测试注入单独标记，不能证明小游戏手感或正式人物输入策略通过。

| 包 | 交付及准入 | S／AC |
|---|---|---|
| [FRAME] L0协议 | 冻结字段/机会失败矩阵/日期屏障/教导K来源合并；公共提交故障样例 | S03/S13/S14，AC03/14/15 |
| [FRAME] L1经济闭环 | 钱包、三餐、摄入、收入、固定物品、两店；联合身体恢复，无纯界面假支付 | S05/S06/S07，AC07/08/14 |
| [FRAME] L2关系闭环 | 首NPC对话/搭子/教导，真实付款权益同提交；各机会差异与K边界 | S08，AC09/14 |
| [FRAME] L3单人物多日 | 真实入口选择、输入轨迹、训练/饮食/休息/日结，逐锚点屏障可恢复 | S09，AC10/14/15 |
| [FRAME] L4终局及全量 | 恋爱完整链与赛事快照；扩四人物、八动作策略，安卓文本触摸 | S04/S08/S09/S10/S11/S12，AC04/09/10/11/12/13 |
| [FRAME] L5长周期与交付 | 多策略多日经济/失败/受伤/比赛组合；真机进退后台故障；离线当前版本 | S01/S13/S14，AC01/14/15；平衡不由文档替代 |

| 情境ID | 前置与操作 | 必须断言 |
|---|---|---|
| [FRAME] E01整包交易 | 测试夹具余额100、包价30、每包3件、库存2包；买2包 | 余额40、道具+6、包库存0；重发同业务键仍为40/+6/0；买3包则全零变化，不部分购买 |
| [FRAME] E02跨域中断 | 对E01在验证后/每Patch准备后/写临时文件/持久提交后/通知前逐点中断 | 恢复只允许100/0/2或40/6/0；绝无扣钱无货、发货无扣钱；后继与收据一致 |
| [FRAME] E03两店四人物 | 玩家P和四NPC A/B/C/D，各ShopX与ShopY；夹具X周期2日、Y周期3日，推进至少7日，期间P只访问X两次、A购买X、B从不访问 | 共10个独立运行实例；同Actor同Shop跨事件一致；A购买不减P或B库存；各自周期按逻辑日推进，Y不受X刷新影响；未访店也按期覆盖 |
| [FRAME] E04解锁边界 | 某商品在X已生成cycle1后解锁，再访问X；推进到cycle2 | cycle1不变；cycle2才进入候选；条件回落不撤销；本局账保留、新局清除；候选不足/空池合法性按配置验证 |
| [FRAME] E05机会区别 | 余额不足教导→改普通对话；空背包主动道具→同点再请求；合法零效果道具；免费教导重复请求 | 前者教导不耗机会、对话耗；空背包结束点且不扣物；零效果扣1；免费一次仍耗机会 |
| [FRAME] E06教导K | 动作A有效组→付费→A零组→B有效组→A有效组→A再次有效；跨各提交点退出 | 付款前不发；零组不获奖；B与后来的A各一次；X按各组前K，重放不重复K/费用；最后无后续组也不追溯 |
| [FRAME] E07餐食收入 | 选择付费餐、跳餐、餐点被占用、余额不足改选；富裕值在日结收入前改变 | 餐食即食无库存、摄入/效果/钱同提交；日结按执行时富裕值一次收入；不足影响两类恢复与压力，临时疲劳清零不等于治伤 |
| [FRAME] E08搭子去重 | D日请求替换，原搭子已练；D+1新搭子同锚点到场，自主驱动再次请求训练；各节点退出 | D日旧绑定，D+1新绑定；basePlan可恢复；同Actor/day同session；NPC不复制玩家有效量/自动资格/随机伤病 |
| [FRAME] E09四人物屏障 | A完成某日结子步、B保存前失败、C/D未运行；恢复后再推进 | A不重发收入；B从最后提交恢复；C/D继续；全员达标前世界日期与比赛冻结不推进 |
| [FRAME] E10真实生产输入 | 每动作策略分别给成功/部分/零完成及挑战失败输入；非法轨迹/缺策略版本 | 同判定器验证合法结果；非法输入停止并诊断；TestInjected不能作为生产凭证；自动容量仅合格者全N |
| [FRAME] E11恋爱 | 满好感足额礼物→特殊事件→显示概率后状态变化→确认→保存后中断 | 礼物一次；过期预览重算确认；基础魅力与独立体脂/薄肌一次入式；抽样不重抽；成功不免参赛，失败只走配置后继 |
| [FRAME] E12配置与安全 | 负价格/零包量/重复product/缺默认事件/跨Actor购买token/过期offer | 启动校验或明确拒绝；不写负库存、不用默认成功补错；错误有Actor/事件/配置/字段定位 |
| [FRAME] E13教学额度提升与封顶 | 老手先发1→付费→同动作有效再练；另例K99教练3、降K后再练；K100老手实际0后付费再练 | 前例只补2且累计3；第二例名义额度3已用，禁止再领；第三例名义老手1已用，补额最多2；每次差额与SetId同提交 |
| [FRAME] E14候选表算例与概率边界 | 读取生活候选：余额400买165三本包；C1000且B0日结；c=.5/L=.6/b=1表白 | 235/+3/包−1；压力先恢复裁0后追加4；pShown=.710，u=.710失败，.709成功；均为算例非实际玩法验收 |

[COMPUTED｜HIGH] E01的算术为100−2×30=40、2×3=6；这些数值仅是独立测试夹具，不是产品价格或初始余额。E03周期也是测试参数，不替代策划候选表。

## [FRAME] 11. 对齐证据与尚待事项

| 对齐对象 | 已取得协议/证据 | 尚待 |
|---|---|---|
| [COMPUTED｜HIGH] 流程程序01a16468-f8be-7cd2-bfb2-871b7a166e0e | 已读FlowPersistenceContracts §2—10；消息确认WorldBarrierId、ActorCursor、角色用途随机、纯Patch、同根保存、逐人同步日结；已纠正为BeforeAnyDailyEvent | [FRAME] 首版存储故障/真机刷盘验证属于实施，不能靠文档认定通过 |
| [COMPUTED｜HIGH] 训练程序01a16469-00b1-7fa2-babd-e93860245256 | 已读TrainingGrowthContracts相关段；双方确认WorkoutSetSequence含下界、CommitId仅审计、NpcPolicy输入轨迹、X后K、gameSeconds；收到其统一awardedNominalAmount回复，已发送生活封顶确认 | [FRAME] 虚拟轨迹逐动作实际验证；不阻塞本文领域字段设计 |
| [COMPUTED｜HIGH] 计划比赛程序01a16469-12e2-75d0-8383-b83108d7dd45 | 已核对CompetitionAndStartSpec §5；完整安排同步与basePlan恢复、训练拥有run/actor/day claim、任何当天事件前冻结、基础魅力/薄肌单一口径已互认 | [FRAME] 正式双赛与四人物成长集成验证 |
| [COMPUTED｜HIGH] 生活策划01a16465-f16f-73e3-b013-767be9ff84e1 | 已读LifeAndCharactersSpec全稿；消息确认机会差异、购买独立提交、同步完整安排、真实八轨迹、教学max/补差、封顶名义额度、pShown一致判定；其最终核对§7/8一致、无新增产品阻塞，仅要求清旧状态，本版§11已清除 | [FRAME] 参数为可实施候选，价格/输入误差/42日可达性仍需运行校准 |
| [COMPUTED｜HIGH] 父聊天01a1629c-9160-7251-a24e-7e4341eb9d61 | 已取得程序映射和同日去重、生产逻辑时钟要求 | [FRAME] 公共契约最终合并和实施准入 |

[FRAME] 本稿不修改业务源码、正式配置、场景、预制体、产品主文档或公共技术总稿。没有运行setup，没有把当前基础测试当本方案验收。后续只有取得对应证据才把“候选/待确认”改成已对齐。

[FRAME] 参数单写引用：经济/菜单/两店/四人物/教学与表白值来自生活稿§2—7；训练、晚学/全天学与道具作用的K/X执行顺序来自训练稿；薄肌L来自赛事稿§5；公共日期与恢复顺序来自流程稿。正文少量候选数值用于协议验算，不是新增配置源。当前全天学习K12替换旧6，晚学3、笔记2，实施时只读取相应定义的版本，不复制常量。

[FRAME｜交付结论] 本模块无尚待产品裁决的阻塞项；程序协议与可实施候选已闭合。仍有真实实施准入：首NPC八轨迹逐核重放、四人物42日多种子行为、两店资源隔离、故障注入及安卓验证，均未执行。父聊天负责公共契约版本整合；本文不以“已对齐”代替“已实现”。

[COMPUTED｜HIGH] 最后数值接入补记已与训练程序互认：中性Numeric核、调用者声明单位、一般互动精确有理比较、关系曲线绑定核版本及.625金样本一致；训练方确认已同步其稿。此轮仅补方案文档，至此停止方案任务。

[RULES I BROKE]: 无。
