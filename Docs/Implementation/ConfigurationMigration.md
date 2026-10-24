# [FRAME] 配置驱动改造实施记录

[KNOWN｜HIGH] 2026-10-23 主策划聊天的人类授权“开始和架构师沟通并且派发任务”已核实。本轮落实 ConfigurationRequirements.md 的 C01—C29。旧 M3R1 证据和安装包保留；本记录不将派发、源码接入或历史测试当作新验收通过。

## [FRAME] 公共契约与单写

[FRAME] 基线文件为 Production/Resources/GameContent/*.json，文件名是稳定 documentId，顶层 Version 必填。ContentPackage 做严格解析、来源路径和哈希；领域提供 public-fields DTO 与 ValidateContent。所有数据从 ContentRuntime.Current.Get<T>(id) 取得，禁止跨内容包缓存。条件使用白名单事实和 All/Any/Not；效果只调用已登记领域能力。剧情返回局部选择，事件结算业务。

[FRAME] 每局保存完整内容包快照及哈希，恢复使用该快照；新局使用安装包内容。旧档首次接入保留原状态和冻结树，不改已提交结果。配置更新的运行中热替换不在范围。配置不允许通过脚本、反射写任意字段或测试结果注入绕过领域规则。

| 项 | 差距与唯一主责 | 文件／消费者 | 依赖与验收例 | 状态 |
|---|---|---|---|---|
| C01 | [FRAME] 完整模板/主要人物/评委原为代码；计划赛事、生活各拥有自己的对象 | templates/actors/competition；Planning/Actors/Competition→Application初始化 | 训练完整初值；改模板初值后真实新局首训变化，旧局不变 | P1模板实际首训通过；人物/评委领域配置已接 |
| C02 | [FRAME] 区域字典/单位：训练 | body；Body→Application只读 | 配置字典与21区完整性 | 已接，见下方运行证据与限制 |
| C03–04 | [FRAME] 计划/组合：计划赛事 | plans；Planning→计划编辑/冻结配额 | 动作、锚点注册；只改模板产生不同合法计划 | 已接，见下方运行证据与限制 |
| C05 | [FRAME] 锚点/开放/预算：架构 | flow；Application/SessionContent | 条件；新增锚点进入真实世界屏障与跨日 | 已接，见下方运行证据与限制 |
| C06–07 | [FRAME] 投放/调用/动态训练树：架构 | flow；SessionContent/GameSession | Flow、领域、剧情；换默认工作、配置后继与消费 | 已接，见下方运行证据与限制 |
| C08 | [FRAME] 条件基础：流程；效果编排：架构；计算归领域 | Configuration、Application | 白名单与错误路径；嵌套条件/效果只执行一次 | 已接；深层条件与公开效果短例通过 |
| C09 | [FRAME] 剧情内容/局部执行：表现；业务调用：架构 | narrative；Narrative→Application | 分支选择返回主事件，无二次确认/重复效果 | 已接，见下方运行证据与限制 |
| C10–12 | [FRAME] 八核参数/成长/疲劳恢复伤病：训练 | training/body；Training/Body/Numeric | 保持真实输入与150必伤；参数差异真正影响计算 | 已接，见下方运行证据与限制 |
| C13 | [FRAME] 学习计算：训练；物品：生活；投放：架构 | training/economy/flow | 学习合法作用集合/耗时，物品机会不重复 | 已接，见下方运行证据与限制 |
| C14 | [FRAME] 工作饮食：生活；事件调用：架构 | life/flow；LifeRules→Application | 改收入/菜价后实际账本变化 | 已接，见下方运行证据与限制 |
| C15–16 | [FRAME] 物品/商店：生活 | economy；Life/Economy→Application | 新池经事件打开，库存/支付/刷新真实消费 | 已接，见下方运行证据与限制 |
| C17–19 | [FRAME] 关系/社交/搭子教导：生活 | relationships/actors；Life/Relationships | 资格/成本展示与结算同源，机会与单搭子约束 | 已接，见下方运行证据与限制 |
| C20 | [FRAME] 人物策略：生活 | actors；Actors→Application/NpcPolicy | 合法输入，改策略参数影响实际操作轨迹 | 已接，见下方运行证据与限制 |
| C21–22 | [FRAME] 赛事披露/评委/评分：计划赛事 | competition；Competition→Application | 短赛程夹具显式标记；旧抽签与冻结成绩不变 | 已接，见下方运行证据与限制 |
| C23 | [FRAME] 恋爱：生活 | relationships；ConfessionRules→Application | 预览/执行同概率，冷却后继只结算一次 | 已接，见下方运行证据与限制 |
| C24 | [FRAME] 结局目录：流程；条件/演出接入：架构/表现 | endings/narrative；Progression→Application | 配置新增结局、继续保留正常后继 | 已接，见下方运行证据与限制 |
| C25 | [FRAME] 有序日切：架构 | flow；Application/Day/World | 必需步骤/依赖校验，恢复不重做 | 已接，见下方运行证据与限制 |
| C26–28 | [FRAME] 反馈/记录/知识卡/音频：表现；业务数据投影：架构 | presentation/knowledge/audio；Presentation/Narrative→Application | 实际显示/播放/已读/偏好；缺资源明确阻塞 | 已接，见下方运行证据与限制 |
| C29 | [FRAME] 加载/校验：流程；集成/命令/版本：架构；独立短验收：主策划所属自动化程序 | Configuration、Tools、Tests/Production/ConfigurationAcceptance | 同业务源码A/B绑定实际加载hash，错误定位，新包 | 已接，见下方运行证据与限制 |

[FRAME] 程序目录：流程=Configuration/Flow/Persistence/Progression；训练=Training/Body/Numeric；生活=Life/Actors；计划赛事=Planning/Competition；表现=Presentation/Narrative。架构唯一修改Application、公共前端契约、flow.json、统一加载注册及测试构建窗口。既有领域测试归领域；新ConfigurationAcceptance目录已移交主策划所属自动化程序，不并发运行工具链。

## [FRAME] 阶段退出

[FRAME] P0：逐项owner/接口/字段/来源清楚；P1：加载→锚点/工作→分支剧情→完整模板真实消费，以同源码配置A/B证明；P2：全部领域消费者和各项差异例；P3：短公开链、必要恢复夹具、配置错误、界面及独立新包。每次测试先冻结写入窗口，原始失败和限制保留。P1四项已实测并独立封存；P2分批证据如下，失败保留。

## [COMPUTED｜HIGH] 实际消费者与证据（2026-10-24）

[COMPUTED｜HIGH] P1批231927-538四项通过，完整封存于TestResults/Configuration/P1-20261023-231927-538；P2批233157-632为55项52通过3失败，完整封存于TestResults/Configuration/P2-20261023-233157-632。233712-722重验两个表现失败与新增12层条件均通过，旧档仍失败；233828-556旧档成功打开但旧收据原文保留断言失败。后续结果必须单列，不覆盖这些失败。

| 项 | 当前生产消费者 | 已运行证据及明确限制 |
|---|---|---|
| C01 | [COMPUTED｜HIGH] TemplateContent/ActorContent/CompetitionConfiguration→GameSession.Initialize；模板名称/说明/优势/短板/锁提示由View投影 | P1完整初始化、strength差异使真实首组难度420000→413571；未伪造训练完成 |
| C02 | [COMPUTED｜HIGH] GameSession.View/Feedback.AttributeValue读取body属性Name/Unit/DisplayDecimals | P2 DisplayDictionaryAndTemplateMetadataReachActualNewGameView通过；风险仍使用原值 |
| C03–04 | [COMPUTED｜HIGH] PlanningContent→PlanCatalog→GameFrontendView.PlanEditor与计划持久化 | PlanningContentTests通过；配置组合名/明确预填ConfiguredPresentationTests复验通过 |
| C05–07 | [COMPUTED｜HIGH] FlowContent→SessionContent.Build/AppendEvent→GameSession/World；相对日与Weekday占用、next-anchor/end-action控制 | P1真实新增锚点、工作默认；P2周一下午占用/next-anchor不执行999后继通过；end-action专用路径尚无独立差异例 |
| C08 | [COMPUTED｜HIGH] ConditionEvaluator/ContentJson严格DTO编解码；GameSession.ApplyConfiguredEffects | P2公开费用单次结算、社交收益通过；233712第4方法12层All写盘重读/实际Evaluate通过 |
| C09 | [COMPUTED｜HIGH] BeginNarrative/ProjectNarrative/TryNarrative；事件提供ChoiceAccess，返回后effects同事务结算 | P1双返回A/B、P2False资格/费用实扣/跨局已读通过；235455当前gift概率/过期拒绝通过（结构消费者） |
| C10–12 | [COMPUTED｜HIGH] TrainingContent/BodyContent→ExerciseCatalog/TrainingInputFactory/BodyCalculator/TrainingSession | TrainingContentTests通过；P1真实输入难度差异；235455旧锁结果恢复/续结算通过，不以旧长路线替代 |
| C13 | [COMPUTED｜HIGH] TrainingContent.Knowledge→BodyCalculator；Economy/Flow控制物品机会 | 现有领域配置测试通过，公开社交知识差异通过；未新增所有学习路径长路线 |
| C14 | [COMPUTED｜HIGH] LifeContent→LifeRules→GameSession工作/饮食 | P1真实工作50/平静0通过，保留此前工作收入修复 |
| C15–16 | [COMPUTED｜HIGH] EconomyContent→InventoryRules/ShopRules→Application商店 | LifeConfigurationConsumptionTests独立池与购买通过 |
| C17–19 | [COMPUTED｜HIGH] RelationshipContent→RelationshipRules/ConfessionRules；Application配置选项与EmitRelationshipResponse；SocialRules→ApplySocial | life8项与公开社交缩放/不缩成本/单次随机通过；235455配置关系名称/对白/门槛/教学价与回应消费者通过 |
| C20 | [COMPUTED｜HIGH] ActorContent→NpcPolicy；WantedNotebookUnits/NormalRestSeconds/ExtraRestSeconds实际读配置 | life人物初值/策略/非法休息参数通过；完整四人实际短链由P3验证 |
| C21–22 | [COMPUTED｜HIGH] CompetitionConfiguration→CompetitionRules/Scoring；Application显示配置比赛名/JudgeScore.CommentaryTextRef | 接线完成；本轮没有重新跑旧42–126日赛事路线，旧冻结成绩只用短恢复夹具 |
| C23 | [COMPUTED｜HIGH] RelationshipContent.Romance→ConfessionRules；Application读取冻结概率并派发回应 | 本轮life8项不含表白公式专例；235455概率消费者与非法gift拒绝通过（明确结构fixture），不宣称自然社交获取 |
| C24 | [COMPUTED｜HIGH] EndingProgression→GameSession.Progression按实际SegmentId读取Narrative | 234436真实早期schema演出段短恢复与旧自然grant通过，不冒称发布同源 |
| C25 | [COMPUTED｜HIGH] FlowContent.EnterDaySteps→Day/World现有5步屏障与competition开放 | 保留既有step4→DayOpenReady→step5顺序，无重复赛事初始化；P3两日验证 |
| C26–28 | [COMPUTED｜HIGH] GameFrontendView内容provider/LocalContent；PresentationContent体型映射；KnowledgeContent/AudioPresentation/已读存储 | 音频8资源hash/生命周期、知识目录偏好不提交游戏命令、provider作用域测试通过；声音听感与实际图形另属P3 |
| C29 | [COMPUTED｜HIGH] ContentPackage/StrictContentJson/global ValidateContentPackage/整局snapshot；Tools/Game.ps1 ConfigR1隔离输出与source-copy | ContentPackageTests、P1旧A恢复新B消费通过；235455具名旧schema迁移通过（具体范围见下）；最终APK未构建前不签 |

[COMPUTED｜HIGH] 旧档适配包位于Resources/LegacyContent/M3R1，属于本轮显式迁移目标而非虚构历史JSON。计划版本仍PlanningM1.CS-TG.v1，训练规则版本TG-0.3；新局使用安装包，迁移后使用整局快照。旧原始状态、树、锁请求/结果不重建；旧结局首次绑定先核验原延续hash，迁移记录保留SourceEndingContinuation，再仅纳入新增元数据重冻延续身份。

[COMPUTED｜HIGH] 收口批234436-926为7项2通过5失败：真实早期schema演出推进一段、旧自然终局模板grant菜单保留通过；它们不冒称M3R1发布同源。235455-453仅复跑剩余5项，5/5通过、sourceDrift=0：真实发布同源105355旧D2完整save复制后显式迁移，旧业务片段原文/永久片段不变；结构旧Playing请求恢复；旧ResultReady实际settle与归档g06-settled-once的BodyState/TrainingProgress完全相等，同operation重复提交及重开不重复结算；配置人物名/对白/邀请阈值/教学价与Application回应消费者；正式Freeze当前gift概率消费者和stale/empty gift拒绝。

[COMPUTED｜HIGH] 概率和关系回应两例是明确结构Application消费者夹具，非自然好感/赠礼获取路线，概率不绕待表白守卫发送普通剧情动作。旧Playing/ResultReady结构例来自带显式关系前置的旧测试快照，未冒充真实发布存档。真实D2迁移与早期结局存储均仅复制后操作，封存原件不变。固定适配目标语义hash为823f6155979d381c91f51148cd22c248703695dc263e7455a6ce67c5bbd78681，加载器拒绝同迁移ID改变目标。

[FRAME] 最终P3使用既有PublicTwoDayUsabilityTests有限两日链，新增界面必要采图与独立BuildStage ConfigR1 Android包；每批源码清单及原件副本绑定该批结果。不同批次不得冒称完全相同源码；打包批次将记录最终源码/配置与APK哈希。

[COMPUTED｜HIGH] P3首轮235601-182在首组真实输入触发InputBudgetExceeded，未提高240秒路线预算或120秒输入预算。定位同一内部已提交root每个输入tick重算全部静态配置投影；修复为仅活跃输入期间按root引用与content hash缓存序列化静态View，每次返回反序列化防御副本，kernel/按键/准备状态仍实时投影。非输入命令、提交、新根、重开均失效；没有改变游戏参数、人物数、种子或测试路线。

[COMPUTED｜HIGH] P3第二轮000115-120在184.68秒跨日触发DayNotApplied，已越过训练输入；N01新日完成检查读锚点解锁条件时，FlowFact无条件提前读取尚未Apply的D2计划。修复仅将DayPlan延迟到restDay/trainingHere事实分支，保留原五步入日及全部断言。第三轮000759-071跨日成功，但D2第5餐后240.419秒触原Wall budget（5餐/6工作收据/6重开/578steps），保持失败证据。

[COMPUTED｜HIGH] 001346-881最小性能/codec批2/2通过、sourceDrift=0。包内缓存仅字段元数据、已转换不可变标量及解码工厂，每次仍重建全部对象/数组，不用动态IL；同包body/actors/flow各300次旧Decode→新Get分别66.1212→17.4896ms、620.0054→162.1745ms、477.1809→144.151ms。序列化等价、嵌套副本修改隔离、跨包scope与12层条件/整局快照回归实际通过；随后原两日同预算复验。

[COMPUTED｜HIGH] P3两日001449-393实际Passed（231.242182秒、exit0/sourceDrift0），保留240秒预算、全部5人物、原种子/真实输入：6餐、1次晚间计划编辑且D2生效、6工作收据、8重开、39公开命令、791步；玩家收入50支出100。原始TEMP为Fitness-Usability-TwoDay/688544e9df6c4d80ab4a035810d01258。

[COMPUTED｜HIGH] 五屏001912-842实际Passed（exit0/sourceDrift0），TestResults/ConfigR1/Captures/20261024-001925-3ec87d：模板长文、组合预填确认、剧情资格费用概率、知识卡来源、声音开关；640×320真实uGUI RenderTexture，独立偏好目录。仅ConfigurationLayoutOnly，不签业务结果、Android触控或生命周期；制作和集成者逐图检查无布局阻塞。之后同冻结源码构建独立ConfigR1 APK，实际结果如下。

[COMPUTED｜HIGH] 最终构建002010-713 Succeeded/exit0/errors0，sourceDrift=0、configurationDrift=0、packedAssetsVerified=true；215项源码逐项hash与001449两日及001912五屏完全相同。产物Builds/ConfigR1/Android/FitnessSimulator-ConfigR1.apk，43,871,401bytes，SHA256 235C15F643F87186F5E996085F4FB2C32E11A272826D3AFF27B5BB8B9A29BDA8。IL2CPP/ARM64、非Development、最低SDK26/目标36；apksigner v2验证通过，沿用Android Debug证书SHA256 db282bd457fd001c3dab20dbf3b944523ac5f231d48ab49517df3e053c43a581，不称商店发布签名。aapt仅自身DYNAMIC_RECEIVER_NOT_EXPORTED权限，无INTERNET。source/configuration/release manifests、final-gates及badging/permissions/signature原文均在该独立目录；未覆盖M3R1产物。

[COMPUTED｜HIGH] 本轮有限配置改造交付至此收口。保留所有失败批及修复后的实际通过证据，不将结构前置、布局夹具、旧长线或源码接线扩大为未跑的自然路线/设备触控/声音听感证明。未新增后续阶段。

[RULES I BROKE]: 无。




