# [FRAME] 工程架构与首版接入约定

[COMPUTED｜HIGH] **最新实现为M3R1**，见[七项修改与交付](Implementation/M3Revision1.md)。工作事件按角色／日期／上午下午独立结算50＋当前富裕度，日结E01保位不支付；新生成事件树版本追加UsabilityWorkR1，旧冻结树和旧计划不静默改写。新计划编辑资格与历史运行资格分离，局内save-plan在视图及执行端均限evening安全状态。餐食成功偏好随事件同提交，界面只消费资格及已提交回执。67项短验收和独立新包已通过，旧长线证据不扩签本轮收入变化。

[FRAME｜现行入口，2026-10-23] 以[首版统一技术方案](Technical/FirstReleaseTechnicalPlan.md)为实施入口；模块、数据/代码所有权、IO、依赖和工程证据见[程序模块分工](ProgramModulesAndDependencies.md)，跨模块复核见[集成审查](Technical/IntegrationReview.md)。M0设计已完成，后续用户授权实际开发及无人验收；当前状态以[M2/M3实施记录](Implementation/M2Implementation.md)为准，旧包证据见[M1记录](Implementation/M1Implementation.md)。

[COMPUTED｜HIGH] 历史Runtime仍是3D资源实验场，正式实现位于Production。事件/DAG实例、双根追加存档、8种真实输入核、21区数值、计划、生活交易、人物世界屏障、关系、比赛与结局均已接入应用；代码接入不代表全部产品路线验收完成。两类冠军到D43、两类实际非冠军、双弃赛、从未报名、D84续届、双报名三种组合与D86自然恋爱成功已有路线证据；恋爱后的参赛/零出场义务已实际通过，M3开发包完成严格构建与签名核验，见[M3交付摘要](Implementation/M3VerificationSummary.md)。D84隔离工程的一项URP生成缓存配置漂移保留为明确限制，不能称全配置零差异。历史4项原型测试与正式测试分开统计，详见[验证记录](Verification.md)及上述实施记录。

## [FRAME] 产品及架构边界

[FRAME] 首版安卓文本优先完整可玩，保留全部已确认玩法；复杂模型、立绘、器械动画、体型插值后置。8个独立小游戏必须有真实可操作基础界面，不能以8动画、自动成功或结果注入替代。

[FRAME] 普通固定树与计划生成的训练动态树共用执行器；入口每个未消费锚点只解析一次，固定后继不重抽根池。锚点/三餐可配置且可明确消费；合法无其他候选使用无限次数、冷却0默认事件，错误必需引用不得伪装兜底成功。

[FRAME] 事件执行器只管生命周期、控制与协调。训练生成树并管理组，身体计算成长和疲劳，经济/关系/计划/比赛拥有各自字段，结局消费领域事实；所有业务变更准备成Patch由统一提交器发布，存档独占物理写入。查询/计划编辑/偏好/配置/纯公式/输入表现/构建验证是独立能力，不强制每个函数成为事件。

[FRAME] 公共身份和CommandEnvelope、四维PreparedOutcome、随机流/领域版本/语义键由[公共契约v0.2](Technical/FlowPersistenceContracts.md)唯一定义。Source为Player/NpcProduction/Test，训练ProducerSource与其正交；生产人物走同8玩法核合法输入，测试重放/注入必须留Test来源。

## [FRAME] 恢复与提交

[FRAME] 正常子事件完成后以候选最新状态有序检查首个合法结局，状态/正常后继/随机/历史/资格统一提交，再通知表现。容器不额外检查；开始统计/占用保存但不检查；结局演出子事件保存不递归检查。

[FRAME] 普通未完成事件从本节点起点恢复。训练已完成组保留，未完成组重做；ResultReady已存则直接Settle一次，结算后剧情未完只重读剧情。废止整动作回滚、历史成绩复用与旧连续力量组乘数。自动容量按当前目标，力量挑战仍手动。

[FRAME] 成功结局保存触发时数据与正常后继，演出/一次备注后从该后继继续；日结中成功只继续剩余步骤。非冠军或末窗未履约终止不能继续。永久历史与模板资格保留，新局不继承旧身体、钱、物品、关系或计划。

[FRAME] 正式存档采用有界双工作根+追加数据前缀+提交收据；当前局/永久进度逻辑一致，物理无需每步重写全历史。未知结果查原operation回执；最新已提交档损坏停载，不能静默回旧档。[COMPUTED｜HIGH] M1的LocalCheckpointStore已实现并进入故障回归；旧LocalJsonStore仍仅用于实验场。永久结局的产品集成属于M3，断电/真机验证未完成。

## [FRAME] 目录与实施顺序

[COMPUTED｜HIGH] 现有Runtime/Training含展示定义与旧接口，Runtime/Persistence含基础存储/DTO，Runtime/Presentation是ResourceLab，Editor含生成/构建，Generated为可重新生成资产。ProjectSetup当前构建显式指向实验Bootstrap；仅改BuildSettings不能得到正式包。

[COMPUTED｜HIGH] M1实际目录为Production/Core、Flow、Persistence、Training、Body、Numeric、Planning、Life、Application、Presentation、Bootstrap；当前共用一个正式Game程序集并保持目录所有权。Application编排、领域准备候选、存档物理写入。正式构建由Editor/PlayerBuild执行，旧Runtime使用FITNESS_PLAYER约束隔离、旧Resources构建时通过可恢复日志临时移出。[FRAME] 后续阶段按依赖增加Relationships、Actors/Autonomy、Competition、Progression等能力，不以目录存在冒称完成。

[FRAME] 实施顺序：公共协议→存档/固定树/首个真实训练切片→生活交易和同日人物→比赛结局→全8玩法/全内容→平衡耐久→安卓发布。纯公式、评分金样本、独立游戏核和只读视图可在协议稳定后并行；全局提交与日期保持单写。阶段任务、依赖、策划对接和验收入口均见统一技术方案。

## [FRAME] 历史文档与证据

[COMPUTED｜HIGH] 本次替换前的[Architecture原文](Audit/Architecture.before-2026-10-23-review.md)与[README原文](Audit/README.before-2026-10-23-review.md)已原样归档；模块审计中的旧规则引用指向归档对应行。归档只用于追溯，整动作回退、旧P编号和3D先行路线不再生效。

[FRAME] [资源清单](TrainingResources.md)仍记录历史实验资源与许可，不决定文本首版范围；[研发准备](Development.md)和[历史验证](Verification.md)中旧阶段记录不替代当前实施协议或新AC证据。安卓真实触摸、性能、恢复和长周期平衡均在后续执行，没有新正式包交付声明。

[RULES I BROKE]: 无。
