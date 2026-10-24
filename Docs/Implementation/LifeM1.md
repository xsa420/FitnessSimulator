# [FRAME] M1生活经济与营养实现交接

[FRAME] 2026-10-23；产品候选版本LifeM1.v1，来源LifeAndCharactersSpec §2；代码命名空间FitnessSimulator.Game.Life。此次为实际编码阶段，替代此前仅方案任务的写入限制；未扩展商店、关系或NPC时间循环。

## [COMPUTED｜HIGH] 已实现文件和路径

| 文件 | 实际能力 |
|---|---|
| [COMPUTED｜HIGH] Assets/FitnessSimulator/Production/Life/LifeContracts.cs | 可序列化钱包、当日营养、餐食/收入收据与营养有理输出DTO；准备结果不是公共命令封套 |
| [COMPUTED｜HIGH] Assets/FitnessSimulator/Production/Life/LifeContent.cs | 版本化三餐各4项含不吃、固定价格/摄入，查询返回新DTO；收入100+2W、最低摄入2000 |
| [COMPUTED｜HIGH] Assets/FitnessSimulator/Production/Life/LifeRules.cs | 显式初始化、改变W候选、餐食支付即食、日收入、营养评估、新日营养候选；深拷贝，不修改输入、不保存、不推进世界日期、不写身体 |
| [COMPUTED｜HIGH] Assets/FitnessSimulator/Tests/Production/Life/LifeRulesTests.cs | 参数化边界、付款不足/跳餐、三餐即食、重复/冲突、执行时W、精确营养对、跨日与作用域隔离、拷贝隔离、非法配置及溢出测试 |
| [COMPUTED｜HIGH] Assets/FitnessSimulator/Tests/Production/Life/LifeJsonRoundTripTests.cs | 实际UnityEngine.JsonUtility往返：新局首次餐食、收入收据与去重、跨日、存在位矛盾、必需对象缺失；测试已编写，修复后运行结果待父串行测试 |

## [FRAME] 实际调用接口与集成责任

| 实际C#签名 | 调用方与提交约束 |
|---|---|
| [COMPUTED｜HIGH] LifeRules.Initialize(string runId, string actorId, long initialBalance, int affluence, int day = 1) → LifeActorState | [FRAME] 父Application读取Planning.TemplateInitializationIntent显式余额/W；初始化候选300/0、300/0、360/10由计划模板定义。Life不猜模板、不发库存。 |
| [COMPUTED｜HIGH] LifeRules.PrepareAffluence(LifeActorState current, int affluence) → LifeActorState | [FRAME] 事件经权限与领域验证后调用；W整数0..100，消费不调用此方法。 |
| [COMPUTED｜HIGH] LifeContent.Menu(MealSlot slot) → MealOption[]；Find(slot, optionId) → MealOption | [FRAME] 展示选项；未知ID/餐点拒绝，不默认不吃；确认时父校验事件/角色/根revision，服务重取权威价格。 |
| [COMPUTED｜HIGH] LifeRules.PrepareMeal(LifeActorState current, MealSlot slot, string optionId, string eventInstanceId) → MealPreparation | [FRAME] 父将Candidate、Receipt、餐点完成/记录同一公共事务提交；Prepared成功，InsufficientFunds不扣款/不记餐点、KeepOpen+Keep+None；MealAlreadyChosen拒绝重选；AlreadyApplied用原收据而不再次发放。 |
| [COMPUTED｜HIGH] LifeRules.PrepareIncome(LifeActorState current, int day) → IncomePreparation | [FRAME] 父E01调用；读执行时W，Amount与AffluenceAtExecution同收据。按稳定SemanticKey去重，不能仅靠OperationId。 |
| [COMPUTED｜HIGH] LifeRules.EvaluateNutrition(NutritionDay day) → NutritionEvaluation | [FRAME] 父E02保存输入摄入和评估结果；Body消费有理对，唯一计算恢复。 |
| [COMPUTED｜HIGH] LifeRules.StartNutritionDay(LifeActorState current, int nextDay) → LifeActorState | [FRAME] 仅允许当前日+1，由父N01调用并将旧NutritionDay归档到可见记录前缀后原子发布新日0；方法不保存旧日档案、不推进世界日期。输入旧日对象保持完整。 |

[FRAME] LifePreparationStatus仅是领域准备状态，不替代Core.CommandStatus或PreparedOutcome。所有字符串ID均需有效；稳定键使用长度前缀编码run/actor避免分隔符歧义。IncomeKey(run,actor,day)固定E01.Income；MealKey(run,actor,day,slot)按餐点去重。LastIncome在片段中保留最近收据，跨日完整回执查询由公共持久日志拥有；旧日命令不能重发收入。父在调用服务前完成RunId/ActorId权限校验和事务根版本校验。

[FRAME] 只有Candidate可以进入统一提交；Receipt与Candidate中的收据互不共享可变对象。准备成功不是持久成功，父不得提前向UI确认。异常参数/未知版本/作用域不匹配/整数溢出映射技术拒绝或配置错误，不能把异常当“默认成功”。候选状态的Revision是领域修订号，不替代根Revision。

## [FRAME] 身体恢复输入及次序

[COMPUTED｜HIGH] NutritionEvaluation带RunId/ActorId/Day/ConfigVersion/IntakeKcal，输出LocalRecoveryMultiplierNumerator/Denominator、BodyRecoveryMultiplierNumerator/Denominator与DeficitStressNumerator/Denominator，均为long。摄入C先夹到2000；倍率有理对为(2000+3C)/8000，不足压力为8(2000−C)/2000。保留原始IntakeKcal，不把显示小数写回状态。

[FRAME] 身体将有理对转其精确有理类型，执行E03局部F恢复20×mF→E04身体B恢复10×mB并裁0→E05不足追加并裁100→E06临时T清零；未到期伤病保留、S不变。Life无F/B/T/S字段，也不施加第二份恢复。候选菜单仅摄入，无即时身体效果/体脂变化/餐食库存。

[COMPUTED｜HIGH] 已核对Production/Body/BodyCalculator.cs实际接入签名：静态BodyCalculator.RecoverLocalFatigue(BodyState, Numeric.Rational multiplier)、RecoverStress(BodyState, Numeric.Rational multiplier)、AddStress(BodyState, Numeric.Rational amount)、ClearTemporaryFatigue(BodyState)、ExpireInjuries(BodyState, int enteringDay)，返回类型均为BodyState。父用new Numeric.Rational(numerator, denominator)转换本领域输入；Body返回克隆状态并增加领域Revision，不自行保存或幂等。此处核对接口，不表示生活与身体的完整集成测试已通过。

[FRAME] E02评估输入必须与本Actor当日摄入一致；M1若使用直接NutritionDay输入夹具，Source必须是Test，不冒充生产餐食链或M2自主人物。E09仍保留旧日摄入记录，N01新建下一日摄入0；不能先清摄入再评估。

## [FRAME] 交接验收与尚未验证

| 预期场景 | 代码/集成证据边界 |
|---|---|
| [FRAME] 初始300/W0→收入前W10→420；同日换OperationId不能再+120 | [COMPUTED｜HIGH] 领域测试已写；父需实际公共提交与持久回执集成测试 |
| [FRAME] 早餐余额9购买10失败→仍9且未消费→可选不吃；未知option不默认skip | [COMPUTED｜HIGH] 领域测试已写 |
| [FRAME] 三份最低餐共50/C2000；原输入不变、候选即食无物品发放阶段 | [COMPUTED｜HIGH] 领域测试已写 |
| [FRAME] C0/1000/2000/2500/极大摄入的精确倍率、不足压力 | [COMPUTED｜HIGH] 领域测试已写；Body最终F/B/T数值由训练测试/父集成验证 |
| [FRAME] 跨Actor钱包/摄入不串、相同事件重放不二扣、旧日保留 | [COMPUTED｜HIGH] 领域测试已写；不代表四人自主已实现 |
| [FRAME] E01后/E03后/持久化后通知前退出、日结等待重开 | [FRAME] 需要父GameSession与存档故障注入；Life纯规则无法证明恢复链通过 |

[COMPUTED｜HIGH] 本任务已执行只读文件/指引检查及源码静态复核，未启动Unity、未执行构建、未运行本测试类；没有可报告的测试通过数。父聊天拥有统一编辑器/测试运行权，建议纳入过滤FitnessSimulator.Game.Tests.Life.LifeRulesTests的EditMode测试并报告实际命令、结果路径和失败。

[COMPUTED｜HIGH] 生活策划已完整审阅本轮3个源码文件、测试文件与本记录，签收M1纯领域静态产品语义，未发现需改代码的产品缺陷；明确测试未运行、不能宣称通过。Body实际恢复接口已取得对接回执。

[FRAME] 父集成必须在PrepareMeal调用前校验事件所属day/slot/actor及当前阶段，拒绝旧日事件在新日变成新餐；Life服务的eventInstanceId是收据引用，不自行解码流程身份。旧日收入完整回执查公共日志；N01旧日归档与新候选同事务；E02输入冻结及E03—E06身体次序/低B裁剪/中断恢复均需集成验证，Test夹具不能冒充生产链。

[FRAME] 当前尚待：父程序集/统一事务/流程接入与串行测试、持久恢复和真机证据。静态审阅已完成，无领域产品阻塞。没有修改旧Runtime、Generated、Bootstrap或他人文件，没有运行setup，没有提交git。M2商店/关系仍按父排期，不创建另一套人物日期循环。

## [COMPUTED｜HIGH] 集成反馈修复：可空内联收入收据

[COMPUTED｜HIGH] 父聊天报告第三轮7条GameSession集成测试首次用餐均因Invalid income receipt失败：JsonUtility将LastIncome=null物化为默认对象。本轮已增加LifeActorState.HasLastIncome存在位；初始化false、收入成功true、复制与验证/重放均按存在位。新增LifeRules.NormalizeAfterLoad(LifeActorState)返回已验证的深拷贝，可在父Read之后调用；领域操作自身也能处理false+空默认对象，不依赖适配器才能工作。

[FRAME] false仅允许null或全默认收入载荷，归一为null；false+实际收据或true+缺失/非法收据拒绝，防止静默忘记已经支付的收入。旧版缺存在位但带已发收入的存档必须显式迁移或拒绝，不能凭缺字段当新局。Wallet是必需对象，另加ConfigVersion标记，防null被物化为零余额有效钱包；Nutrition有必需run/actor/day可拒绝默认对象。Meals空数组表示合法无餐点；数组内默认收据仍拒绝。准备结果不是持久化DTO，失败Receipt=null不进入根存档，不需要收入存在位复用。

[COMPUTED｜HIGH] 修复新增5条真实JsonUtility测试源码，未自行启动Unity；已向父提交NormalizeAfterLoad接口与格式变化，修复有效性等待父下一轮运行，不能将之前纯领域静态签收当成本次序列化验收。

## [COMPUTED｜HIGH] 当前验证状态：2026-10-23父串行回归

[COMPUTED｜HIGH] 本节更新此前“尚未运行/等待父验证”的历史状态。已只读核对父执行的[测试XML](../../TestResults/M1/editmode-20261023-013440-172.xml)与[运行报告](../../TestResults/M1/test-20261023-013440-172.json)：197/197通过，失败0、跳过0，其中正式测试193；exitCode=0、sourceDrift=0。LifeRulesTests为17/17，LifeJsonRoundTripTests为5/5，收入存在位修复的真实序列化测试已通过。

[COMPUTED｜HIGH] [回归覆盖记录](../../TestResults/M1/M2-first-regression-coverage.json)记录原正式测试132、当前正式测试193、missingOldTests为空。本聊天未另启编辑器或测试，仅更新实施记录；源码继续冻结。

[INFERRED｜HIGH] 此证据确认本轮自动化回归通过，不足以确认真机验收或整个阶段通过；主流程及真机门槛由父继续汇总。

[RULES I BROKE]: 无。
