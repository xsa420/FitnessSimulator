# [FRAME] 八玩法真实输入核接入记录

[COMPUTED｜HIGH] 2026-10-23，新增TrainingInputFactory与ITrainingInputSession，保留原BenchPressSession及M1存档字段。KernelVersion为InputKernelsV1；规则版本仍由请求显式携带并校验。暂不持久化核内部进行中状态；进程内Suspend/Resume冻结并重建逻辑输入，进程死亡仍按既有未完成组重做约定处理。

[COMPUTED｜HIGH] 输入入口为Submit(TrainingInput)、Advance(int milliseconds)。输入种类ButtonDown/Up与PointerDown/Move/Up；按钮Left/Right/Raise/Lower/Press，触点含稳定PointerId与面板0—1坐标。Result只返回核生成结果的克隆，不接受有效次数或成功标志。

[COMPUTED｜HIGH] View包含ExerciseId、Stage、Feedback、AttemptMilliseconds、EffectiveReps、FailedAttempts、TargetReps、Step、VisibleDirection、IsPreparing、IsSuspended、IsComplete、SignalGreen、Balance、Window、TargetSeconds、FirstOpening、SecondOpening、HeldInputs。调用方不可用界面状态替代核判定。深蹲只显示当前展示项，隐藏后VisibleDirection=-1；下压不公开未来绿灯时间。返回对象和HeldInputs均为查询副本。

[FRAME] 深蹲逻辑输入坐标约定：中心(.5,.85)半径.1，目标y≤.45且x分左/中/右三段；VisibleDirection用0/1/2。界面应显示对应区域，NPC应记住展示提示后逐项操作。其他坐标遵循训练规格。七个新核的准备遮罩期间不受理输入；下一尝试清空逻辑触点，调用方重新按下。卧推沿用既有持键语义，调用方读取HeldInputs。

| 核 | 输入与公开提示 | Window单位 |
|---|---|---|
| [COMPUTED] bench_press | Left/Right按钮，Balance；Stage=Balance | 归一平衡半宽 |
| [COMPUTED] dumbbell_curl | Raise/Lower按钮；Stage=Raise/Lower；活动700/1500ms对应节拍 | 秒半窗 |
| [COMPUTED] lat_pulldown | 单稳定触点依次起点、四检查点；Step为当前段索引 | 归一通道半径 |
| [COMPUTED] triceps_pushdown | Press按下/松开；Stage=Start/Held/Return；SignalGreen当前灯 | 秒释放窗 |
| [COMPUTED] squat | Watch阶段VisibleDirection当前提示，Respond阶段依记忆滑动并回中心；Step为已答项数 | 秒单项响应窗 |
| [COMPUTED] hip_thrust | Press按下/松开；Stage=Start/Held/Hold；TargetSeconds目标时长 | 秒误差半窗 |
| [COMPUTED] shoulder_press | 稳定触点出发/返回；FirstOpening/SecondOpening对应y=.65/.35；Stage=Outward/Return | 归一开口全宽 |
| [COMPUTED] lateral_raise | 两个稳定触点分半屏；Stage=Synchronize；HeldInputs保留逻辑身份 | 秒到达/释放差窗 |

[FRAME] 活动时间是核诊断/驱动时钟；实际界面不得因此在臀推隐藏阶段显示蓄力进度。所有坐标按数值直接映射统一面板；如表现层采用Y向上，仍须按上述数值画目标，不根据动作名称静默翻转轴。

[COMPUTED｜HIGH] TrainingProducer追加TrustedAutomationInput=5，原0—4值不动。正常真实输入成功沿既有结算授自动资格；ScriptReplay不授自动资格，TestInjected不可从正式开组或输入factory进入。核/DTO本身不是权限边界。

[FRAME] 父负责受信无人驱动入口鉴权与隔离存档：只在开发构建入口设置私有可信标志，外部命令不得自报该来源；ResultReady只接父私有session.Result。此记录不宣称父接入已实现。

[COMPUTED｜HIGH] BodyMetrics新增GetThinRegionAdaptation(long strengthMicro)，校验0..100e6，返回精确Rational。原GetThinBuildIndexMicro复用该函数；BodyMetrics.Version与NumericKernel.SqrtVersion保持既有值。赛事复制合法BodyState并自行记录run/actor/day/phase/revision/commit/provenance。

[COMPUTED｜HIGH] 首次闭合编译后运行TrainingM2Standalone：既有13组与M2新增6组全部PASS，退出0。此时新七核自身轨迹测试尚未运行；该结果不能作为八玩法完成证据。

## [COMPUTED｜HIGH] 随后独立轨迹验证

[COMPUTED｜HIGH] 解冻后运行 `dotnet run --project Assets/FitnessSimulator/Tests/Production/Training/TrainingInputStandalone.csproj`，退出0。原13组、M2的6组、以下新输入检查均PASS。八动作各3难度（0/.42/1）×3种子（1/17/991）×成功/部分/零完成，共216条终止结果轨迹；全部由按钮或触点输入产生，不写v/success。另有各核相同事件在毫秒拆分/批量推进及暂停恢复下的结果摘要一致检查。

[COMPUTED｜HIGH] 新边界覆盖弯举窗口等号、下拉跳点/逆序点/偏离120与121ms、肩推低采样线段扫掠障碍、双触点身份替换、双点180ms同步等号及181ms失败、深蹲隐藏题目和下压未来灯时不泄漏。真实弯举输入接原锁结果/身体结算，证明TrustedAutomationInput与NpcPolicy授自动资格、ScriptReplay不授；TestInjected及AutoCapacity不能进输入factory。区域薄肌函数18/26/48边界及越界拒绝已测。

[COMPUTED｜HIGH] 发现并修正恢复准备期吞release可能保留失效held的问题：七新核在Resume后的350ms提示期间收到任何输入会重新Suspend，活动时间/逻辑held不变，必须再次实际重绑；检查确认重绑后继续原蓄力时长。普通尝试准备期仍是掩码；前端须过滤未送入核的物理触点后续move/up，等待释放后新down。已同步父与表现程序。卧推保留原有持键行为。

[COMPUTED｜HIGH] 新增TrainingInputDomainTests共19个引擎测试入口，调用同一套纯检查。已读取父串行第二短批报告TestResults/M1/editmode-20261023-025530-087.xml：总计278/278通过，其中本类19/19通过；对应test-20261023-025530-087.json记录exitCode=0、sourceDrift=0。本批按Integration、Training、FlowPersistence、Life、Planning、Presentation测试域筛选，共278个正式案例，不是包含长无人轨迹的全量测试。本域没有启动编辑器。

[COMPUTED｜HIGH] 首次独立编译曾因测试将Rational使用未定义的==失败，已改CompareTo后重跑通过；不把首次失败隐藏为通过。

[COMPUTED｜HIGH] 已读取后续TestResults/M1/editmode-20261023-025759-448.xml及同名运行JSON：仅Automation范围3/3通过，exitCode=0、sourceDrift=0。PostInputCache下三份report.txt记录种子1/7/42均运行3日、重开3次，完成组分别9/3/9；策略仍为first-enabled-real-bench-v1，来源Test/ScriptReplay，明确未覆盖NPC、其他动作、结局和组中断比较。父随后解除冻结。

[COMPUTED｜HIGH] 已读取TestResults/Automation/PostInputCache/comparison.json：各种子6个业务fragment及其版本、run/revision/generation一致。原始输入/时钟对比不完全一致，每个种子均有初始revision0空存档随机RunId的两行差异；仅规范化该两行后完全一致。比较排除了新增KernelVersion/Payload审计与公开投影日志等差异，不能描述为所有原始文件逐字节一致。

## [COMPUTED｜HIGH] 可重放核输入报告

[COMPUTED｜HIGH] 独立runner追加可选新输出目录参数，实际执行后生成TestResults/Training/InputKernelsV1-20261023/report.json及24份jsonl：8动作×成功/部分/零，种子17、D=.42、N=2的隔离核夹具。报告保存请求/来源/规则版本/核版本/输入文件/结果与摘要；记录每次Submit与Advance。生成后重新读取实际jsonl逐份重放，24/24结果全部字段一致，进程退出0；同次运行旧13、M2的6组及新核检查也通过。旁附source-hashes.json保存本域生产源及输入检查源的SHA256，便于识别对应代码。

[FRAME] 这些轨迹只证明核输入与文件重放，不包含身体结算、生产NPC策略、应用鉴权、存档恢复或整局结局。请求的TrustedAutomationInput标签在隔离测试中不具有父应用授权含义。报告中的N=2用于最小核样例，不替代正式动作训练量；生产仍由冻结计划产生请求。核协议没有因此改变。

[FRAME] 尚未证明八核已接父正式主流程、NPC策略/真实触摸/整局终局、文件恢复与自动化存档隔离全部通过。输入对象是纯协议，父私有核与鉴权才是生产可信边界；输入摘要只是重放诊断，不是鉴权签名。未完成组不新增核内部存档，旧M1字段语义未变。

[RULES I BROKE]: 无。
