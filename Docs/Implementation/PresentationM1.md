# [FRAME] M1 文本界面与正式入口实施记录

[FRAME] 2026-10-23，进入实际开发，原M0只文档限制已由父聊天核验的用户授权解除。本任务只修改分配的表现、启动、正式构建、表现测试、本文；父额外授权单文件 `Production/Application/FrontendContracts.cs`。不运行Unity、不提交git、不运行原型setup。

## [COMPUTED] 已写代码

- [COMPUTED｜HIGH] `Production/Application/FrontendContracts.cs`：IGameFrontend.ReadView/Execute/Advance/SetSuspended；FrontendView/Option/Action/Reply及独立FrontendBenchPressView；父Application实现GameSession。UI发操作与参数，不调用身体Reducer或存储。
- [COMPUTED｜HIGH] `Production/Presentation/GameFrontendView.cs`：uGUI Canvas、640×320扩展逻辑布局、安全区、可滚动正文与合法动作、选择确认、身体/安排/计划只读、1.25字号；save-plan编辑PlanDraft的槽训练量、训练/休息及循环长度，保存/放弃/继续。首次/次日生效与合法性由父控制。
- [COMPUTED｜HIGH] `HoldInputButton.cs`和`PointerHoldState.cs`：左右持续按住、多指身份集合，最后一指抬起才发up；系统取消只清物理触点，不生成玩法release。
- [COMPUTED｜HIGH] 卧推页显示安全窗、平衡、进度、有效量和错误数，无成功按钮。左右通过bench-left-down/up、bench-right-down/up；重绑bench-rebind位掩码左1/右2。每帧Advance不决定游戏时间，父转给确定性玩法核。
- [COMPUTED｜HIGH] `Production/Bootstrap/GameBootstrap.cs`：正式字体和许可证直接引用；new GameSession(Path.Combine(persistentDataPath,"PlayerM1"))。与实验DevelopmentProbe分离。
- [COMPUTED｜HIGH] `Editor/PlayerBuild/GamePlayerBuild.cs`与独立Editor asmdef：CreateScene/ValidateContent/BuildAndroid/BuildWindowsProxy；正式场景路径`Assets/FitnessSimulator/Scenes/Player/GameBootstrap.unity`由父执行CreateScene生成，不碰旧Bootstrap。

## [FRAME] 生命周期与边界

[COMPUTED｜HIGH] 失焦先通知父冻结核，再禁用输入模块并清物理触点；回前台重新启用输入模块，按原逻辑左右状态重建触点；无按住状态时显式点击继续。UI发送bench-rebind后由核负责350ms准备，不重置失误。重绑拒绝保持遮罩；进程死亡由父存档恢复，UI无独立恢复槽。

[FRAME] M1页面由父投影实际状态和Actions，包括模板选择、事件、组结算、休息、日结次日、保存错误和继续。此文件记录的是已写实现，只有父运行后才能标实际可操作；未接七玩法不能被成功注入替代。

[FRAME] 字号开关当前为本次UI实例偏好，尚未持久化；正式跨局偏好/已读跳过/音频/知识卡在后续接入。M1不据此称AC13全量通过。计划编辑当前为有限操作面板，不宣称完整首版动作增删排序编辑已做完。全休息合法草稿不会被UI强行加训练。

## [COMPUTED] 字体与隔离

[COMPUTED｜HIGH] 检查系统存在中文字体，但未复制系统字体。已从[官方字体仓库](https://github.com/notofonts/noto-cjk/blob/main/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf)下载NotoSansCJKsc-Regular.otf（16,437,364字节）及同仓库Sans/LICENSE，放在Presentation/Fonts；SHA-256 `2C76254F6FC379FDDFCE0A7E84FB5385BB135D3E399294F6EEB6680D0365B74B`。OFL许可证随正式场景TextAsset引用入包。ValidateContent检查健/卧/疲字形，但导入与画面仍待父执行，不以文件存在称可读通过。

[COMPUTED｜HIGH] 父批准构建期间临时隔离ExerciseCatalog.asset。方法验证源/目标绝对路径在工作区、目标不存在、源GUID及hash；记录Prepared/Moved/Restored journal，再以MoveAsset保持GUID移动至Editor/PlayerBuild/Quarantine。try/finally恢复，无删除或覆盖；重启可调用RecoverIsolation。源/目的同时存在、身份变化即停，不猜测覆盖。

[FRAME] 父负责旧Runtime asmdef增加!FITNESS_PLAYER约束，正式Build使用FITNESS_PLAYER额外define并在调用前检查约束；详细构建报告检查打包内容无旧Resources/Generated/Runtime/实验场景。构建过程临时设置安卓标识与平台参数，finally恢复原设置。只有父执行与最终包检查才能证明隔离生效。

## [FRAME] 父串行验证入口

1. [FRAME] 编译并调用 `FitnessSimulator.Game.Editor.GamePlayerBuild.CreateScene`，生成正式场景和资源引用；若场景已在则验证，不重置手工场景。
2. [FRAME] 调用ValidateContent；跑Tests/Production/Presentation三项指针状态测试、一项实际中文字体/许可证测试、一项动作载荷序列化测试及父集成测试。
3. [FRAME] 调用BuildWindowsProxy或BuildAndroid。输出分别为Builds/M1/Windows/FitnessSimulator-M1.exe、Builds/M1/Android/FitnessSimulator-M1.apk；报告TestResults/M1/build-StandaloneWindows64.json、build-Android.json，包含结果/错误/路径/hash/时间。
4. [FRAME] 离线打开正式入口，选择默认角色及明确单卧推自定义计划，按实际事件路由进入训练，不保证当天随机抽中。输入和保存证据由父采集。
5. [FRAME] 存档位于各平台persistentDataPath/PlayerM1；测试需独立目录。清空/重置必须经应用新局确认或父指定测试目录操作，不删实验或永久记录。设备未连接时只记桌面代理/编辑器，不能填真机通过。

## [COMPUTED] 当前验证与待集成

[COMPUTED｜HIGH] PowerShell Add-Type直接编译PointerHoldState，并断言双指保持、最后一指释放、系统取消不产生up、旧指针up不影响新指针，输出POINTER_HOLD_CHECKS_PASS。此为独立纯状态检查，不是Unity测试或真实触摸证据。

[COMPUTED｜HIGH] 父要求会话目录锁生命周期后，Bootstrap改为持有IGameFrontend并在OnDestroy对IDisposable.Dispose；界面借用，不重复释放。动作DTO已标Serializable，避免父JsonUtility指纹遗漏StringValue/IntValue。

## [FRAME] 真实界面图形烟测（父调用）

[COMPUTED｜HIGH] 已新增Editor-only `GameUiSmoke.Run`。命令参数为 `-executeMethod FitnessSimulator.Game.Editor.GameUiSmoke.Run -fitnessUiSmokeExit`；必须有图形设备，不能用`-nographics`或`-quit`。可先尝试带图形的batchmode；父负责进程串行与超时。方法进入PlayMode后自动结束，完成标识FITNESS_UI_SMOKE_OK，失败标识FITNESS_UI_SMOKE_FAILED；内部240秒上限。

[COMPUTED｜HIGH] 运行创建空测试场景与独立GameSession.OpenTest种子17，真正调用GameFrontendView及uGUI ExecuteEvents点击/左右按压；自动反馈控制通过同一个卧推核产结果，没有结果注入。输出`TestResults/M1/Captures/<UTC-random>/`中的menu/initial-plan/meal/bench-preset/bench-running/day-end六图、ui-evidence.json及独立FixtureSave。检查图像非单色、六图hash不同；画面可读性仍需人工逐图检查。

[FRAME] 图像为实际uGUI经Camera/RenderTexture的桌面图形代理采集，非真机屏幕截图；证据标source=Test、inputSource=ScriptReplay、injectedResult=false、androidTouchVerified=false。编辑器专用OS失焦旁路仅供隐藏图形采证，报告明确此轮不提供生命周期证据；正式Player编译不含旁路属性。人工和真实设备后台恢复另验。此烟测新增代码尚待父执行，不因具备入口声明通过。

[COMPUTED｜HIGH] 父已执行正式导入与CreateScene：`TestResults/M1/import-20261023-003306-384.json`记录exitCode=0，正式GameBootstrap.unity已生成且直接引用中文字体和OFL资源。父测试报告`TestResults/M1/editmode-20261023-003355-474.xml`中本模块5项全部Passed：三指针、一中文字形/OFL、一动作指纹载荷。该报告全局80项、正式76项，数量仅引用父报告，不由本模块冒领全部实现。

[COMPUTED｜HIGH] 首测后08:34:24仍有生命周期小修（回前台跳过首帧delta、恢复计划草稿），已通知父下一轮重新编译；不能将首测报告视为最后源码快照的完整验证。

[COMPUTED｜HIGH] 后续父报告`TestResults/M1/test-20261023-004235-506.json`及对应XML记录exitCode=0、115项总测试/111项正式测试，完成北京时间08:43:13；晚于本轮GameUiSmoke及RunId载荷修改，当前源码已通过这轮编译/测试。新增截图工具本身的实际图形运行仍待执行，未混入已通过测试数。

[FRAME] 仍待可见中文排版/实际输入、正式包资源隔离及安卓触摸性能验证；字形存在不等于画面排版通过。当前本任务没有启动Unity；ADB设备0沿父记录。父Application只读投影曾暴露任意end-action入口及缺少本动作最高T/100/150提示，已发父修其拥有文件，等待复验。

## [COMPUTED] 首轮视觉驳回与修订

[COMPUTED｜HIGH] 首轮图形报告 `TestResults/M1/Captures/20261023-004829-99c0b2/ui-evidence.json` 虽为Passed，但人工截图检查发现弹窗底层文字透出、卧推按键无标题、正文挤占动作区；旧runner可直接点击裁切按钮。因此该报告不能作为视觉或可操作验收通过证据。

[COMPUTED｜HIGH] 修订将正文与动作拆为独立滚动区，加滚动提示/滚动条；弹窗全屏不透明并拦截底层；补卧推标题和计划中文项目名，训练量横排。换页将滚动位置复位。新runner只在按钮完整进入viewport、中心raycast最上层命中目标后发送点击，并通过scroll事件将裁切按钮滚入视口。

[FRAME] 下一轮要求13图：原六图，加group-result、existing-new-confirm、existing-cancel、existing-continued、corrupt-exit、corrupt-confirm、corrupt-recovered。已有局取消后断言RunId不变，继续回原日结；损坏测试仅改新生成FixtureSave当前generation文件，然后实际点击确认新建并检查InitialPlan。此修订尚未经父重新编译/图形验证，不沿用前一轮通过状态。

## [COMPUTED] 最新代理记录与真机人工操作清单

[COMPUTED｜HIGH] `test-20261023-010559-275.json`记录exitCode=0、sourceDrift=0、136总测试/132正式测试。`Captures/20261023-010723-87c664/ui-evidence.json`为Passed，包含14图（增加默认/1.25字号组前对照），输入仍为ScriptReplay、平台EditorGraphicsProxy、androidTouchVerified=false。父任务转达主策划已复看并关闭D07代理缺陷；此结论不是手机验收。源码继续冻结。

[FRAME] 前置条件：父确认正式APK构建成功并记录SHA256；手机所有者在设备上确认USB调试授权，`adb devices -l`显示device而非unauthorized；安装并人工启动同一APK，记录设备型号、系统版本、分辨率、导航模式和包版本。使用明确允许替换的测试局；不清数据或覆盖用户存档。新游戏需保存合法计划才提交新局。测试计划选择卧推，在实际抽中训练时进入手动容量组。

1. [FRAME] **人工多指**：外部相机同时拍屏幕和手指。一指按左键，再第二指按同一左键；抬第一指时左键应保持高亮，最后一指抬起才熄灭。右键重复；再左右同时按住、分别松开，检查独立高亮与平衡响应。录像记录有效次数/失败次数；不要把自然扰动误认作按键失效。普通adb input tap/swipe不是多指证据。
2. [FRAME] **后台存活**：先记录有效次数、失败次数、平衡、进度和左右高亮，分别测试无按住、仅左、仅右、双侧按住时回桌面后返回。用前后pidof核对进程相同。无按住时应出现继续本组确认；有按住时重新按住原来的左右逻辑组合才继续，无需恢复原物理手指数；未匹配时保持暂停。匹配后应显示准备提示约0.35秒，再推进；后台时间不能导致组内进度跳跃或额外失败。录像只能观察准备时段，无法单凭人眼证明精确350ms。
3. [FRAME] **进程死亡与未完组**：当前组已有部分次数且未完成时记录屏幕，回桌面后由父在指定测试包执行force-stop或设备设置强停，记录旧PID退出，再人工重开/继续。验收当前未完成组重做，已结算的较早组不回滚；不能把同PID后台返回当进程死亡。强停不是卸载/清数据，也不等同系统低内存杀进程的完整覆盖。
4. [FRAME] **完成组保留**：完成真实手动组，在结算本组按钮出现后强停/重开，确认结果仍可结算且不重玩；另在结算后结果页强停/重开，核对已显示的身体变化、余额、组号只结算一次，再继续下一组。截图只能支持可见值对照，不能证明全部内部字段和事务次数。
5. [FRAME] **日结恢复**：在日结记录天数、余额、摄入及可见变化，强停/重开/继续后仍回该日结；人工点击进入次日一次，核对只前进一天。长正文上下滑动，截图保留顶部与末尾，避免只记录当前可视部分。
6. [FRAME] **横屏、安全区与字号**：左右横屏均检查刘海/挖孔和系统导航区；正文、动作分别滑动，确认关键继续/退出可发现。组前切默认与1.25字号，核对禁用原因不重叠；检查计划编辑、确认弹窗、日结。弹窗外点按不触发底层；卧推左右键标题完整、触摸无死区。记录屏幕录像与方向，不能由桌面1280×720图代替手机安全区验收。

[FRAME] 可由父采集的只读材料：`adb devices -l`、`adb shell getprop ro.product.model`、`adb shell getprop ro.build.version.release`、`adb shell wm size`、`adb shell dumpsys package com.fitnesssimulator.game`、`adb shell pidof com.fitnesssimulator.game`及对应时段logcat；截图/屏幕录像属于输出采集，不是游戏输入。日志可查崩溃和生命周期线索，但未实现逐指事件审计，日志缺失不证明事件没发生。外部相机才同时展示人工手指；纯屏幕录像无法区分人工和脚本输入。

[COMPUTED｜HIGH] 正式构建未设置Development选项；现有UI只展示平衡、进度、有效次数/失败次数、准备提示和按键高亮，没有完整指针ID、350ms内部时钟、RunId、输入摘要或事务审计面板。PlayerM1位于persistentDataPath下，当前清单不假定正式包可用run-as读私有目录。无法取得的内部证据记为未取得，不能补写成已通过。ADB输入应另标ScriptReplay或ADB-script；人工手指记录Human-touch；强停标ADB lifecycle control，不能冒充人工操作或系统自然杀进程。

[COMPUTED｜HIGH] D09单项追加：图形runner保留14图，并在实际view.Body包含“本次训练没有开始”时采集必需图missed-training，共15图；仍沿seed17自然事件路径，不修改事件树或注入漏训状态。此新增图待父执行，手机原安装包及人工试验独立，不将新源码效果归入旧包证据。

## [COMPUTED] 后续文本页面与完整计划编辑接入

[COMPUTED｜HIGH] 架构师确认店铺、物品点、学习、休息互动、比赛报名／结果、结局演出及历史继续使用Title/Body/BodyRows和Actions。三模板也由应用层投影通用选项及Enabled/DisabledReason，UI不判断解锁、不编造购买或比赛规则。结局备注文本输入等待正式应用API后再对齐，本轮未猜造字段。

[COMPUTED｜HIGH] 唯一新视图字段为可选PlanAnchorOptions：Id为训练锚点，Label为中文，Enabled/DisabledReason由应用层按本次计划生效日投影。为空时保留原时段，不推断解锁。新增GameFrontendView.PlanEditor.cs实现目录中八动作增删、上下排序、五组合选择、时段选择；排序保留EntryId，新条目使用独立GUID，切换组合不偷偷删除动作，最终合法性由save-plan校验。

[COMPUTED｜HIGH] 自然星期／按天循环默认计划通过二次确认替换未保存草稿；既有休息日、训练量、循环长度编辑保留。保存失败在弹窗中显示原因，返回修改保留草稿。模态中的长按钮说明也按文字实际高度自适应。本轮仅静态diff检查，未运行编辑器或图形测试；不能沿用旧15图证明新增编辑页面已验收。

## [FRAME] 八核输入与系统中断重绑契约（已确认，待解冻实施）

[FRAME] FrontendView.TrainingInput使用独立Serializable DTO镜像公开TrainingInputView，不引用训练领域类型。输入Kind/Button为大小写严格的枚举名称，PointerId为非负逻辑标识，X/Y归一化至0..1且Y向上。training-input发送单输入对象；training-rebind发送对象封套`{Inputs:[...]}`。旧卧推字段和动作兼容保留，新字段优先。

[FRAME] 冻结HeldInputs靶点仅用于操作系统中断后的接触重建：用户实际按回对应靶点后，UI才将新的物理指针映射到冻结逻辑PointerId及冻结坐标；按钮也必须重新实际按住原逻辑按钮。没有实际接触，不得发送rebind或自动恢复。重绑本身不推进训练核、不生成新的点击成功；后续移动继续使用实际触摸归一坐标。无held接触时须用户明确点击继续。该约束由架构师确认，已请求训练负责人核对；源码冻结期间仅记录，不声明八核界面已实现。

[FRAME] 训练负责人补正：七新核普通准备期不登记down，UI须过滤该触点后续move/up，直到实际释放并重新down；提示“准备，松开后重新操作”，不能把物理held冒充核已登记held。恢复350ms准备期存在release被忽略后留下逻辑held的风险；训练负责人拟解冻后改为恢复准备期任意输入触发重新Suspend，再经实际接触重绑。此核修订尚未确认落盘，UI接入时必须核对最终行为。卧推原持键语义保留，以HeldInputs为准。

[FRAME] 训练负责人确认靶点重绑方案，建议命中容差0.03面板单位，仅用于实际接触重建，不修改玩法窗口或冻结核坐标。坐标按核公开值原样绘制：下拉起点(.5,.1)、肩推(.5,.9)、侧平举(.4/.6,.7)；深蹲中心(.5,.85)半径.1，方向目标y<=.45且x分三段。不得因动作名称静默反转Y轴；视觉朝向另与设计核对。

[COMPUTED｜HIGH] 解冻后已实现FrontendTrainingInputView/FrontendTrainingInput/FrontendTrainingRebind、GameFrontendView.Training.cs及TrainingPointerSurface。公开八核状态驱动按钮或触点面板，指针输入Button为空，屏幕坐标归一化Y向上，物理负ID映射为非负逻辑ID。普通准备未登记接触过滤到释放，恢复准备期间已登记接触的实际变化送核；训练owner已确认其恢复准备输入重新Suspend修订落盘。重绑仅实际接触冻结靶点后发送，空held须明确继续。提示图使用公开坐标及状态，不产生训练结果。新增TrainingInputAdapterTests三例待父验证；源码实现不等于八核图形／真机验收通过。

[COMPUTED｜HIGH] 结局新增EndingNoteDraft/EndingCanEditNote及多行InputField，明确区分ending-draft、ending-empty、ending-note。只替换投影载荷Text，保留AchievementId/SegmentId；按StringInfo文本元素计数，超过200提示并阻止提交，完整输入保留、不截断。同结局修订变化不覆盖本地未保存正文。演出和历史沿通用Actions/Body呈现，不在UI判断结局资格。本轮未运行编辑器，新增测试及控件显示待父统一验证。

## [COMPUTED] 用户七项修改：固定导航与直接进食

[COMPUTED｜HIGH] 最新实现覆盖旧字号／安排要求：固定导航为返回主菜单、角色、计划；主菜单仅复用应用投影open-menu并从事件选项区去重，训练时三入口保持禁用。字号按钮及状态/切换逻辑、当天安排入口与分支已移除，固定默认字号。通用确认、计划保存、未保存退出和默认计划替换均按取消左／确认右排列；结局确认复用同一确认行。

[COMPUTED｜HIGH] 进食单击沿应用meal选项提交，应用RequiresConfirmation=false；上次成功选择与真实餐食/费用/摄入反馈由应用投影及回复提供。成功meal反馈允许同局自动推进后保留最多4秒，下一次手动操作、回菜单、换局或错误页清除；失败不持久化界面偏好，也不新增继续节点。晚间编辑资格只消费应用save-plan选项。旧morning/afternoon值保留只读，编辑页及总览提示改选，未改选不提交保存；可选时段仍仅取PlanAnchorOptions。

[COMPUTED｜HIGH] 本轮新增FrontendNavigationTests五例，待根执行。原默认截图流程移除大字号截图后为14图；专用短模式沿既有flag改为3图fixed-navigation-role-top/bottom及fixed-navigation-training-disabled，固定640×320，保留滚动、菜单定位、删除入口、训练禁用及遮罩/退出恢复断言。它仅为布局夹具，未覆盖三餐记忆、夜间资格和真实保存业务；这些由根应用测试负责，旧15/5图仍是旧需求历史证据。未启动编辑器、构建或长线。

[RULES I BROKE]: 无。
