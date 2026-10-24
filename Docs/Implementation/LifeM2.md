# [FRAME] M2-A/B支付库存与私有商店交接

[FRAME] 范围仅A/B纯领域规则，候选版本LifeEconomyM2.v1；沿用LifeM1.v1钱包唯一所有权。未实现关系教学、自主人物循环、商店UI或Application接入，不代表完整M2通过。此次没有重建或认证M1安装包。

## [COMPUTED｜HIGH] 文件与实际接口

| 文件 | 已实现内容 |
|---|---|
| [COMPUTED｜HIGH] Production/Life/Economy/EconomyModels.cs | 独立库存/物品点/商店状态、调用上下文、报价/收据、固定效果意图、调用方随机样本及消费证据；没有新公共命令封套 |
| [COMPUTED｜HIGH] Production/Life/Economy/EconomyContent.cs | 两物品用途、两店6商品包规格/价格/每轮库存/3日与5日周期、版本化NotebookEffect定义 |
| [COMPUTED｜HIGH] Production/Life/Economy/InventoryRules.cs | Initialize、NormalizeAfterLoad、Quantity、PrepareGrant、PreparePayment、OpenPoint、PrepareUse、PrepareDelivery、PrepareMoneyLoss、PrepareItemLoss |
| [COMPUTED｜HIGH] Production/Life/Economy/ShopRules.cs | Initialize、NormalizeAfterLoad、ObserveUnlocks、PrepareRefresh、QuotePurchase、PreparePurchase |
| [COMPUTED｜HIGH] Tests/Production/Life/EconomyRulesTests.cs | 支付/整包/失败/旧报价/随机隔离/刷新/解锁/用途/损失/溢出及实际JsonUtility往返测试源码 |

[FRAME] 上表源码根为Assets/FitnessSimulator；命名空间FitnessSimulator.Game.Life.Economy。所有Prepare返回候选，不修改传入状态；所有查询返回新配置DTO或值，不抽随机。没有文件IO、UI依赖、独立PRNG、全局日期修改、身体字段写入。父Application组合候选与公共回执后一次提交。

| 实际API | 参数和结果要点 |
|---|---|
| [COMPUTED｜HIGH] Initialize(runId,actorId) → InventoryState | 显式笔记0/书签0，不靠缺字段推导空物资 |
| [COMPUTED｜HIGH] PreparePayment(life,context,purpose,amount) → PaymentPreparation | long整数金额非负，足额扣款；0费用仍有成功收据；不扣富裕值。父负责合法收费来源。 |
| [COMPUTED｜HIGH] OpenPoint(context,pointId) → ItemOperationPoint | 允许表为主动笔记；父只在既定学习操作点开放，不增加自由背包使用入口。 |
| [COMPUTED｜HIGH] PrepareUse(inventory,point,context,itemId,skip,conditionSatisfied,fixedTargetValid) → ItemPreparation | 无库存/条件失效/无效固定目标关闭点、不扣物；合法扣1并产固定8动作K2意图；跳过关闭点。未知配置ID是技术错误，不伪装成产品失败。 |
| [COMPUTED｜HIGH] PrepareDelivery(inventory,context,itemId,quantity,conditionSatisfied) → ItemPreparation | 仅剧情物品、正整数、全额交付；失败零扣、无成功效果。人物互动机会由未来关系模块处理，本方法不消费它。 |
| [COMPUTED｜HIGH] PrepareMoneyLoss(life,context,requestedLoss) / PrepareItemLoss(inventory,context,itemId,requestedLoss) → LossPreparation | 非自愿损失最多扣至0，返回ActualLoss；负请求拒绝，不发欠款或隐含惩罚。 |
| [COMPUTED｜HIGH] ShopRules.Initialize(runId,actorId,shopId) → ShopState | cycle0待D1初刷，默认单本预解锁eligibleCycle1。每run/actor/shop独立。 |
| [COMPUTED｜HIGH] ObserveUnlocks(shop,day,veteranAffection,beginnerAffection) → ShopState | 读取调用方权威条件快照，只添加黏性解锁，不改列表；首次命中eligible=currentGeneratedCycle+1；无变化不加revision。 |
| [COMPUTED｜HIGH] PrepareRefresh(shop,day,RandomSamples) → RefreshPreparation | 返回店候选、ExpectedShopRevision、稳定周期键、随机消费证据；到期前NotDue，样本不足NeedRandomSamples；均无变化。 |
| [COMPUTED｜HIGH] QuotePurchase(life,inventory,shop,productId,packageCount) → PurchaseQuote | 绑定三个领域revision、cycle、作用域及版本；记录总价/总件数，库存按包。 |
| [COMPUTED｜HIGH] PreparePurchase(life,inventory,shop,quote,context) → PurchasePreparation | 重验作用域/三revision/周期/价格/件数/当日刷新完成；返回新life/inventory/shop及收据。不足余额/包数不部分成交。 |

## [FRAME] 提交、幂等与序列化接入

[FRAME] EconomyContext含RunId/ActorId/EventInstanceId/Day/TransactionOrdinal。父先核事件归属、阶段、权限及公共OperationId/语义回执，再调用纯规则；消费或购买不能凭客户端自报context获得授权。每笔新交易分配新ordinal；相同事件/ordinal的重复请求（即使新OperationId）查公共回执，不重新执行Prepare。收入/餐食M1接口不变。

[FRAME] 领域SemanticKey对run/actor/event/action采用长度前缀；购买以事件+ordinal识别，商品/包数为负载，异负载复用键由公共账本报冲突。Prepare不是持久幂等提交器：对同输入重复准备得到等价候选，不构成两次效果；对提交后状态重复请求须公共回执先拦截，不能用新报价绕过原交易键。旧报价按三revision或cycle返回StaleQuote，明确不同于原回执AlreadyCommitted。

[COMPUTED｜HIGH] 已公开只读稳定键方法：ShopRules.PurchaseSemanticKey(context)、InventoryRules.PaymentSemanticKey(context,purpose)、DeliverySemanticKey(context)、ItemPointSemanticKey(context,pointId)。Prepare全部调用相应同一方法；父trustedSemanticResolver直接复用，不复制拼串，不依赖报价或当前revision即可先查询旧业务回执。测试PublicSemanticResolversMatchPreparedReceiptsBeforeQuoteValidation覆盖一致性与新ordinal区分。

[FRAME] 一次购买必须同时发布Life、Inventory、Shop、PurchaseReceipt、事件后继及公共回执；道具一次使用必须同时发布Inventory、ItemOperationPoint、Body效果、记录/后继。K已封顶实际变化0时依然扣物，Body负责上限，经济不退款。PrepareGrant用于开局/事件/商店的授权交付，调用方必须有来源与幂等键，不开放给UI任意赠物。

[FRAME] 新增持久状态没有可空内联分支：Inventory显式2项；Shop显式Offers/Unlocks数组及cycle；ItemOperationPoint有身份与Consumed。NormalizeAfterLoad检查必需身份/配置版本/形状后复制。各Preparation不是持久封套，其失败Receipt或Point为空不能作为业务存在位写入根；父只持久明确候选/收据。此前M1 HasLastIncome及Wallet版本校验保持不变。

## [FRAME] 刷新、解锁和随机资格边界

[FRAME] N03必须给每个角色的两店执行日期步，无论访问与否。馆内D1/4/7/10，街角D1/6/11；PrepareRefresh只接受确切到期日，不允许直接跳过多轮；后续周期应由父按逻辑日期恢复推进。购买到达NextRefreshDay而尚未刷新会拒绝，避免访问才刷新或消费旧轮。正常同日再次调用刷新返回NotDue，不重新抽取或补库存。

[FRAME｜生活策划明确确认] 资格由提交/快照边界决定，不按“同一天”额外延一轮：N03资格快照前已独立提交的解锁可进入即将生成轮；快照之后或刷新同提交新产生的解锁不能进入该轮。D7 cycle3、D8解锁三本→D10 cycle4；D10 N03快照前若确有合法已提交老手40解锁→D10可入；D10普通训练才达到40→D13 cycle5。开局默认单本预解锁cycle1。未实施ObservedDay等于刷新日就+2的提案。

[FRAME] 父从已提交Shop锁定刷新资格快照，校验ExpectedShopRevision后提交，不把未提交ObserveUnlocks候选临时并入同一刷新。串行协调器冻结期间不交错修改该店；若检测冲突停止错误顺序，不能丢弃已登记解锁或重新抽到有利商品。Shop候选是深拷贝，之后的解锁不能改已准备列表。库存刷新完全覆盖旧列表和剩余包数。

[COMPUTED｜HIGH] RandomSamples由调用方提供RunId/ActorId/ShopId/Purpose="ShopRefresh"/AlgorithmVersion/StartDrawIndex/uint Values[]；领域不生成样本。无放回依配置稳定顺序构建候选池，选择使用32位拒绝采样避免模偏差，返回全部实际消费值和起止索引；候选仅1项无需抽样。池不足商品位允许少位，无隐藏补货保底。

[FRAME] 随机样本不足返回原状态及0消费，调用方应保留同一输入前缀并补足后重试，不重新采样已经提供的前缀。最终店候选与公共随机后状态同提交；读取/报价不消费随机。随机作用域错误拒绝，不能挪用玩家入口随机、另一角色或另一店的流。

## [FRAME] 验收源码覆盖与未完成项

| 断言 | 测试方法 |
|---|---|
| [FRAME] 400买165三本包→235/+3/包−1；旧输入不变、重放旧报价拒绝、新交易无库存拒绝 | WholePackagePurchaseReturnsAtomicIndependentCandidates |
| [FRAME] 足额/不足/免费支付，不部分购买、篡改报价拒绝 | PaymentInsufficientKeepsWalletAndZeroPaymentIsExplicitSuccess；NoPartialPurchaseAndForgedQuoteCannotChangeAnything |
| [FRAME] D8解锁→D10、黏性/新局重置；同日快照前后不同轮、重复准备稳定 | UnlockWaitsForNextRefreshAndNeverRelocks；SameDayUnlockUsesCommittedSnapshotBoundaryNotCalendarDelay |
| [FRAME] uint拒绝样本留证、不足完全无消费、跳过到期步骤拒绝 | SuppliedRejectionSamplesAreAuditedAndExhaustionIsAtomic |
| [FRAME] 5角色×2店11日未访刷新/资源隔离；同轮不重刷、旧库存覆盖 | TwoShopsAndFiveActorsRefreshWithoutVisitsOrSharedStock；RefreshIsIdempotentInCycleAndReplacesUnsoldAndSoldStock |
| [FRAME] 无效道具关闭点且不扣、有效返回固定目标K、剧情足额、非自愿损失截0 | InvalidUseConsumesOnlyItsPointAndValidUseReturnsFixedAllExerciseIntent；InvalidConditionOrFixedTargetClosesPointWithoutItemLoss；StoryDeliveryRequiresFullQuantityAndDoesNotEmitKnowledge；InvoluntaryLossClampsAtZeroWithoutDebtOrPartialStorySuccess |
| [FRAME] 实际JsonUtility空/已用/已购状态往返，非法状态/溢出/非正数量拒绝 | ActualUnityJsonRoundTripsEmptyAndPopulatedOwnedStates；CorruptMissingStatesAndOverflowDoNotBecomeFreeGoods；NonPositiveQuantitiesAreRejected |
| [FRAME] 跨run/actor/shop随机和报价拒绝；三个revision分别过期 | SuppliedRandomScopeAndAllQuoteRevisionsMustMatch |

[COMPUTED｜HIGH] 已写测试代码并做静态复核，尚未自行启动Unity或执行测试，不报告新增测试通过数。父执行目标类FitnessSimulator.Game.Tests.Life.EconomyRulesTests。现有M1的136/136及代理图/双包由父回报，不属于本次M2代码验证证据。

[FRAME] 尚待父接入：公共随机载荷映射、跨片段提交/完整回执、N03逐日店推进、合法事件/道具点、UI成本与原因反馈、故障注入后恢复。须验证候选准备后/写盘中/提交成功通知前中断，及换OperationId同交易重放不二扣；当前纯域测试不能证明这些持久化场景。关系教学C、人物自主D均未实现，未新增第五人物或第二时间循环。

## [COMPUTED｜HIGH] A/B静态审修订

[COMPUTED｜HIGH] 指定生活策划已审四份源码、测试与交接，认可整包交易、私域/随机、刷新快照、道具机会和损失语义；指出剧情条件失效与库存不足混用。经父释放小修窗口，PrepareDelivery已区分ConditionNotMet与InsufficientItems：先判剧情条件，再判足额；两者均不扣物、不发效果、不消费人物机会。新枚举追加在末尾，保留已有枚举值。

[COMPUTED｜HIGH] 新增DeliveryConditionFailureIsDistinctFromMissingItemsAndNeitherConsumesAnything；CornerUnlockChecksBothDayAndAffectionAndKeepsCurrentOffers覆盖39/40、D27/80、D28/79、D28/80→D31及回落黏性；PreviousCycleQuoteIsRejectedAfterRefreshEvenForSameProduct覆盖同商品跨轮拒绝。此为测试源码补齐，尚未执行。完成后自有代码重新冻结，父统一运行；未修改父Integration/EconomyCommitTests。

## [COMPUTED｜HIGH] 当前验证状态：2026-10-23父串行回归

[COMPUTED｜HIGH] 本节更新此前“测试源码尚未执行”的历史状态。已只读核对父执行的[测试XML](../../TestResults/M1/editmode-20261023-013440-172.xml)与[运行报告](../../TestResults/M1/test-20261023-013440-172.json)：197/197通过，失败0、跳过0，其中正式测试193；exitCode=0、sourceDrift=0。本域EconomyRulesTests为21/21，包含静态审新增的条件失败区分、街角解锁边界和跨轮报价拒绝；父EconomyCommitTests为5/5。M1生活回归LifeRulesTests为17/17、LifeJsonRoundTripTests为5/5。

[COMPUTED｜HIGH] [回归覆盖记录](../../TestResults/M1/M2-first-regression-coverage.json)记录原正式测试132、当前正式测试193、missingOldTests为空。父已补跨领域交易集成测试，本节据此更新前文对应“待验证”状态；具体通过范围以该报告中的测试用例为准。

[INFERRED｜HIGH] 自动化通过不能推出M2已接入主流程或阶段全通过。N03生产流程、事件/道具入口、UI反馈及剩余图形验收仍由父推进；关系教学C、人物自主D等待下一批授权。本聊天只更新自有实施记录，未启动编辑器、构建或测试，未修改任何源码或父集成测试；源码继续冻结。

[RULES I BROKE]: 无。
