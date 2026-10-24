# [FRAME] 计划、开局、赛事配置交接

[COMPUTED｜HIGH] 2026-10-23 实际配置位于 `Assets/FitnessSimulator/Production/Resources/GameContent/templates.json`、`plans.json`、`competition.json`。读取合同为 `ContentRuntime.Current.Get<T>(documentId)`，每次取得防御副本；本领域没有静态配置结果缓存。三个文档顶层 Version 必填，公共加载器负责严格字段、类型和包身份。本轮没有执行测试、构建或引擎；以下为源码交接，不是验收通过报告。

| 覆盖 | 配置及实际消费者 | 启动验证 |
|---|---|---|
| [COMPUTED] C01 玩家模板 | templates：3模板名称/说明/优势/短板/排序/启用/条件/具名初值。TemplateCatalog.All/IsAvailable/PrepareInitialization；TemplateInitializationIntent.CreateBody/CreateTraining 展开全部初值，应用层调用完整工厂。 | TemplateCatalog.ValidateContent；TemplateContent.ValidateReferences 注入生活目录引用检查。 |
| [COMPUTED] C01 完整初值 | 每模板21区 S/F/T/无伤病、身体压力/心理压力/体脂/魅力、8动作K/X及梯度/4自动资格、富裕/余额、库存与关系引用。必填 InitialValue 包装区分缺失与显式0；调用身体/训练所有者工厂。 | 完整目录/范围及无开局累积权益；逐动作首梯度力量门槛。 |
| [COMPUTED] C03 计划 | plans：周历、循环、卧推切片与4人物基础计划；日槽稳定ID、锚点、动作顺序、组合、训练量。PlanCatalog.CreateDefault/CreateFromTemplate/CreateNpcDefault 直接生成配置草稿。工作量仍来自训练动作目录。 | PlanningContent.ValidateContent(可信锚点目录)：日键/数量/动作/组合/引用；不自建锚点常量。 |
| [COMPUTED] C04 组合 | plans.Combinations：排序/名称、正式区域集合、预填动作、固定区域交集编辑规则。TrainingCatalogAdapter.GetCombinationRegions 实际约束计划；PlanCatalog.Combinations/PrefillCombination供界面显式选择/预填。 | 唯一组合、合法区域、自由组合全部区域、预填实际目标交集。 |
| [COMPUTED] C21 日程/披露 | competition：周期、开放/截止/比赛日、阶段、两赛锚点/角色引用、池与稳定抽取顺序、披露日与字段白名单、分桶/肌群集合。CompetitionRules/Queries/Edition及Content接口实际消费。 | CompetitionContent.ValidateContent；CompetitionConfiguration.ValidateReferences注入有序锚点、选手目录和文本目录。 |
| [COMPUTED] C22 评分/点评 | competition：两核身份、8评委权重/偏好/体脂区间、归一参数、精度策略引用、有序点评规则/fallback。CompetitionScoring产出比分和JudgeScore.CommentaryTextRef；薄肌指标仍调用BodyMetrics唯一实现。 | 两类各4池/3人不重复、权重和100、合法体脂、已注册规则、固定数值策略；点评引用交呈现层解析。 |

[FRAME] 公共集成顺序：先加载包并验证身体/训练/生活/锚点定义，再执行上述领域校验及跨引用校验；最后建立会话。默认模板无条件True，锁定模板条件仅可查询 `templateGrant:<已知模板ID>`，由正式永久资格映射为小写布尔字符串，不读当前身体猜解锁。

[COMPUTED｜HIGH] 新玩家训练默认中午；竞争者/初学者中午，老手/教练晚上。编辑资格与运行资格仍由应用层分离；配置只生成新草稿/新实例，当前与待生效计划、人物基础计划、搭子冻结安排不会从新默认重建。新结构不扩为一天多个训练场次。旧上午/下午计划是否能重新保存仍由新编辑资格校验，读取执行保留原文。

[FRAME] 旧局必须绑定其存档包快照：恢复仍检查原域版本并使用绑定包校验比分，不能拿新安装包重算旧冻结结果。旧结果可能没有点评字段，恢复不填补旧字段；已有非空点评引用必须与原包规则一致。原封存包/报告未改，旧快照兼容与实际序列化由根统一验收。

[COMPUTED｜HIGH] 新增 PlanningContentTests 六个有限域测试源码，覆盖防御读取、新模板单区配置变化与旧身体不变、计划包作用域、新草稿与旧草稿分离、缺区/非法资格拒绝、隔离5日赛历与真实规则入口、评委权重和点评fallback变化。隔离短日历与固定身体是明确的领域夹具，不作为自然42日路线证据；本轮未执行这些测试。

[FRAME] 待根验收与集成核对：应用层所有赛历hook改用OpenDay/RegistrationCloseDay/ContestDay/DisclosureDays/ClosePhase，初始化采用完整工厂和生活引用；界面消费模板优势/短板、组合名称/显式预填、赛事Name及点评文本引用；启动调用两个ValidateReferences。跨模块消费者由唯一owner交付，本领域不越权修改。

[FRAME] 仅改配置验收例：改变默认模板一个合法区域力量并新建局，观察该区独立变化；把一个计划槽改到晚上/调换合法动作顺序，既存计划不动；隔离包改开放/披露/比赛日，使用公开日序验证触发；改评委合法权重/体脂区间/点评阈值，复算生产结果与点评差异。负例为缺区域、缺显式0、重复动作、未知组合/锚点、池不足/重复、权重不合100、非法阶段顺序和缺文本引用，均须拒绝并定位。域fixture之外的公开短链、配置包Hash及源码无差异证据由根串行测试提供。

[RULES I BROKE]: 无。
