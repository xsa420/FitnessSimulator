# [FRAME] M1 实施与集成记录

[COMPUTED｜HIGH] 2026-10-23，主策划聊天最新用户消息已直接核验：用户授权进入首版开发，由架构师沿用现有程序聊天从M1实施，按阶段验收推进。M0只文档限制属于已完成阶段。本轮以实际实现与验证为目标，不能将方案或派发当作完成。

## [FRAME] 文件所有权与并行边界

| 执行者 | 拥有区域 | 当前任务／状态 |
|---|---|---|
| [FRAME] 架构师父聊天 | Production/Application、正式Game与测试根asmdef、Tools正式命令、集成测试、本文 | 应用编排与统一提交接入；全局验证串行；开发中。 |
| [FRAME] 事件提交与保存程序 | Production/Core、Flow、Persistence；Tests/Production/FlowPersistence | 公共协议、固定/动态执行、双根追加日志和恢复/故障测试；已派发编码。 |
| [FRAME] 训练与身体程序 | Production/Training、Body、Numeric；Tests/Production/Training | 卧推真实核、组状态、21区结算/配额/伤病/休息；已派发编码。 |
| [FRAME] 计划比赛程序 | Production/Planning；Tests/Production/Planning | 模板/计划验证、首日/待生效/次日切换；已派发编码。 |
| [FRAME] 生活自主程序 | Production/Life；Tests/Production/Life | 钱包/三餐即食/摄入/日收入及营养恢复输入；已派发编码。 |
| [FRAME] 表现验证程序 | Production/Presentation、Bootstrap；Editor/PlayerBuild、Scenes/Player；Tests/Production/Presentation | 正式uGUI、真实多指卧推、文本流程、正式场景和构建方法；已派发编码。 |

[FRAME] 新正式代码统一放Assets/FitnessSimulator/Production，根命名空间FitnessSimulator.Game。现阶段一个正式Game程序集，按目录保持领域/应用/表现边界；不复制旧实验场逻辑。各领域纯服务准备数据，Application协调命令、事件与统一提交，存储独占物理写入。UI和领域不得再各起一次全局日切或保存。各程序只改自己区域，公共协议由核心程序单写并回传消费者；父负责集成。所有Unity批处理由父串行运行，不启动原型setup，不自动提交git。

## [FRAME] 切片范围与基线

[FRAME] 首动作卧推；使用合法自定义单卧推计划验证完整闭环，产品默认完整计划保留。未接入的七玩法不得自动成功冒充实现。M1包括新局/计划、固定事件/默认、卧推真实输入、ResultReady/逐组结算/休息/伤病、三餐与日结点击次日、计划次日切换、完成组保存及中断恢复。M2—M6按照既定方案后续推进；本阶段通过不等于全部首版AC通过。

[FRAME] 候选与技术文件启动哈希登记于M1Baseline.json；变更产品参数先经领域策划/主策划记录，技术普通缺陷自行修复。沿用M0全部恢复、来源与唯一写权约束，不因赶切片使用注入替代正式输入。

## [COMPUTED] 环境与设备

[COMPUTED｜HIGH] 启动检查：Unity 6000.6.1f1编辑器和其Android SDK/ADB路径存在；当前未发现该项目编辑器进程，仅Hub进程。adb devices -l返回空设备列表。原工作树README/.gitignore已修改、工程大量未跟踪，保留原状。

| 目标槽位 | 候选定位 | 实际型号／系统／屏幕与密度／连接状态 | 验收状态 |
|---|---|---|---|
| [FRAME] 主验机A | 目标下沿安卓横屏设备 | [COMPUTED] 未登记，ADB无连接；不虚构型号或参数。 | [FRAME] 真机NotRun；独立开发继续。 |
| [FRAME] 第二机B | 不同屏比／系统的代表机型 | [COMPUTED] 未登记、未连接。 | [FRAME] M5前补齐并冻结，M6双机复验。 |

[FRAME] 编辑器/桌面代理输入可以验证规则和界面连接，不能填写安卓真实触摸/性能通过。性能预算沿文本验收规格；实际设备可用后登记完整系统、分辨率、密度、安全区和采证状态。

## [FRAME] 集成与验收记录

[COMPUTED｜HIGH] 当前状态：实现与回归中，未完成M1阶段验收。GameSession已将事件、计划、训练、身体、餐食与日切接到统一提交；正常新局替换与坏档保留后重建已接界面。正式场景已生成。完整结局目录、NPC/商店/赛事仍属于后续阶段，不把当前完成检查回调计数冒充真实结局。

| 轮次 | 证据与结果 | 解释 |
|---|---|---|
| [COMPUTED] 首轮 | TestResults/M1/editmode-20261023-003355-474.xml，80/80，正式76 | 域/存储/界面基础测试，无整局接入证明。 |
| [COMPUTED] 第二、三轮 | 003632、003934测试报告：7条整局失败 | 实际JsonUtility将可空内联对象物化，引发空计划、空收入误判；已由计划/生活/流程存在位与训练Phase规范化修复。 |
| [COMPUTED] 第四轮 | editmode-20261023-004235-506.xml，115/115，正式111 | 原7条整局与新增新局确认/取消/替换幂等通过；真实核输入结果恢复、完成组保留、日切精确续接、生活/计划/训练真实序列化回归通过。 |
| [COMPUTED] 第五轮 | editmode-20261023-005101-984.xml，131项129过2败 | 新增坏Genesis/永久标记异常未转换；修复已落待复跑。应用坏档保留并显式新局用例通过。 |
| [COMPUTED] 首图形烟测 | Captures/20261023-004829-99c0b2，脚本Passed但视觉不合格 | 真正uGUI图有无字按键、模态重叠、主要动作离开视口。退回修布局，同时禁止脚本点击裁切不可见按钮。 |

[COMPUTED｜HIGH] Source/Producer隔离：OpenTest使用Test与ScriptReplay，菜单业务不提供v/success等客户端结果字段；ResultReady只读应用持有的BenchPressSession.Result，合法自动容量仍经资格校验。UI日志/截图是脚本重放证据，不是手指或手机性能证据。

[COMPUTED｜HIGH] 日切实际M1覆盖E01收入、E02冻结摄入、E03/F、E04/B裁剪、E05不足、E06/T清零、汇总与等点击、N01日期/摄入/到期、N02计划/配额、N05入口。N03店铺、N04比赛及人物屏障未接入，不以空步骤宣称完整世界日切。

[COMPUTED｜HIGH] 当前存档内容版本M1.3/TG-0.3/LifeM1.v1/PlanningSchema2，计划Record schema2；本轮开发期旧不兼容快照不静默迁移。源码执行清单由Tools/Game.ps1记录SHA-256；执行期任何已列源码变化即拒绝认证当前版。

## [COMPUTED] 最新复验与产品缺陷关闭（2026-10-23）

[COMPUTED｜HIGH] 当前有效回归为TestResults/M1/editmode-20261023-010559-275.xml：136/136通过，其中正式132、历史4，无跳过；对应test-20261023-010559-275.json记录exit0、sourceDrift=0。旧失败报告保留，不再作为当前未修复状态。

[COMPUTED｜HIGH] D08/CS02已纠正：新游戏仅创建内存草稿；合法初始计划确认后才一次BeginNewRun提交模板、计划、配额和新根。取消、退出、非法计划不替换旧根；首次非法计划也不发布空局。根写入前故障保留旧完整局，确认回执前故障恢复新完整局，重复原操作不重复创建。坏档备份/重建也延迟到合法计划最终确认。开局策划已复核相关断言与四文件哈希；此结论不覆盖全部CS01—12或AC02。

[COMPUTED｜HIGH] 当前图形证据为TestResults/M1/Captures/20261023-010723-87c664/ui-evidence.json：14图、Passed，真实uGUI及卧推核输入、无结果注入；对应ui-smoke-20261023-010715-322.json记录sourceDrift=0。默认及1.25字号的禁用原因不再溢出按钮；旧局取消/继续与坏档合法计划后恢复已采证。主策划已回看并关闭当前代理范围D07视觉缺陷。首轮脚本Passed但视觉失败、第二轮禁用文案重叠记录仍保留。

[COMPUTED｜HIGH] 上述图形证据来源Test/ScriptReplay，平台EditorGraphicsProxy，使用Camera/RenderTexture并显式绕过编辑器失焦；androidTouchVerified=false。不能将其用于手机触摸、后台生命周期或性能验收。

[COMPUTED｜HIGH] 正式安卓构建完成：TestResults/M1/build-android-20261023-010856-052.json记录exit0、sourceDrift=0；build-Android.json记录Succeeded/errors0。APK位于Builds/M1/Android/FitnessSimulator-M1.apk，43,017,629字节，SHA256为8F6D3BD45EB615E9F49E58458F7D0EB61B1E82DAA29BF4C20C636F789303A445。aapt确认arm64-v8a、minSDK26/target36、无INTERNET；apksigner验证通过。正式场景构建逐packedAssets拒绝实验资产；资源隔离完成恢复，ExerciseCatalog的GUID和SHA与移动前一致，见android-package-device-audit.json。

[COMPUTED｜HIGH] 当前实际主验设备已连接并授权：HONOR AAP-AN00，Android16/API36，物理1272×2800、560dpi。首次安装被设备拒绝，返回INSTALL_FAILED_ABORTED: User rejected permissions；已停止重试并交主策划协调设备确认。安全区尚未读取，真机启动/触摸/生命周期/性能均NotRun；连接成功不是验收通过。

[FRAME] M1总体仍待真机验证。仅源码/代理/构建子项关闭，不将M1或全部首版验收标为通过。

[COMPUTED｜HIGH] Windows正式代理构建也已Succeeded/errors0/sourceDrift=0，见build-proxy-20261023-011508-389.json。入口Builds/M1/Windows/FitnessSimulator-M1.exe，完整185文件哈希见TestResults/M1/windows-package-files.json；exe单体SHA256为07534CC5839D2C89C4FE45EC272C91B046FF3D64F14AB3F0A32221121E0FFFF2。构建完成不等于此独立exe已运行验收；此前图形运行来自编辑器代理。

[COMPUTED｜HIGH] release-source-consistency.json核对上述136测试、14图、Android、Windows四次执行清单，每次51个.cs/.asmdef均完全一致；各执行期间sourceDrift=0。此M1包作为独立待验版本保存，后续实现不能沿用其报告认证新源码。

[COMPUTED｜HIGH] 设备后续：用户明确允许重试后，第二次安装Success，正式Activity冷启动Status ok，PID10621；已验证前台属于本游戏后采集Device/HONOR-AAP-AN00/menu.png，2800×1272，中文与新游戏按钮可见。OS报告rotation90下左挖孔安全边距135物理像素；仅此方向菜单已查看，不能声称全页面/双横屏安全区通过。没有发送游戏点击/滑动、没有新建或覆盖局，保持菜单交主策划组织人手操作。首次拒绝保留历史，当前安装/启动子项通过；多指/后台/强停恢复/性能仍NotRun。设备清单evidence.json与android-package-device-audit.json已更新。

[COMPUTED｜HIGH] M1-D09设备反馈“没有出现训练”：已只读复制本游戏存档并校验104条提交链与最新根哈希。D3中午有效单卧推下午计划、冻结3×8配额；D1/D2下午实际记录为散步。原种子重放的两次入口抽取与当前第10抽状态精确匹配，支持按配置发生散步，未发现计划失效；未向设备注入或改档。证据Device/HONOR-AAP-AN00/no-training-diagnosis.json、no-training.png、NoTrainingSave。

[COMPUTED｜HIGH] 主策划认可D09为普通反馈缺陷。GameSession.View现已补当前计划训练时段被已选普通事件占用时的说明：显示计划动作/组次、当前发生事件及本次训练未开始；不预测未来、不暴露随机、不修改权重。此改动尚未重新运行或打包，手机上的8F6D…固定包仍是旧文案；不得沿用旧136/14图认证该改动。

[COMPUTED｜HIGH] 设备训练只读证据：AfterReadyReportSave的141条提交链/根已校验，D3下午实际产生3组并结算后进入晚餐。首组PlayerTouch，8/8、1失误、176逻辑边沿；后两组AutoCapacity各8/8，输入0。用户反馈“左右和双指响应正常，已到结果页”，作为人手报告单列；实际ready.png和current-result.png均为晚餐，不作为准备/结果停页证据。曾在查看前把ready.png称准备页，已向主策划和用户明确撤回更正。human-group.mp4录制起点也已晚餐，不能证明训练多指；未发送ADB游戏输入。详情training-observations.json。当前证据支持手动成功→自动资格→完成三组的真实设备业务链；物理同键多指、后台与强停仍需分别核验。

[RULES I BROKE]: 无。
