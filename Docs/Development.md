# [FRAME] 研发准备与清理记录

[COMPUTED｜HIGH] 2026-10-22：从活动工程中移出模板说明代码及其资源、模板场景、空的顶层 Resources、临时 Android 构建目录 `.utmp`、旧 `build` 日志目录，以及失去源文件的两个 Assembly-CSharp 工程文件。77 个文件合计 2,468,658 字节，归档在 `TestResults/Cleanup/20261022-234304`，逐文件 SHA256 已核对。归档被版本控制忽略，可按 `removed-files.json` 的原路径恢复；这是可恢复归档，不是磁盘空间回收。

[COMPUTED｜HIGH] 移除未使用的包依赖：`com.unity.ai.navigation`、`com.unity.collab-proxy`、`com.unity.visualscripting`。移除前检查这三个包共 3,568 个资源 GUID，项目序列化资产中未发现引用；项目脚本中亦无相关 API 使用。默认模板场景路径改为 `Assets/FitnessSimulator/Scenes/Bootstrap.unity`。

[COMPUTED｜HIGH] 保留八套原型动作资产、原型生成器、训练和存储接口、四个现有测试。`SampleSceneProfile.asset` 名称虽来自模板，但仍被两个渲染管线配置引用，不能作为废文件删除。保留已配置的输入资源、渲染配置、暖缓存及既有运行截图证据。

## [FRAME] 日常入口

[FRAME] 使用固定版本 Unity `6000.6.1f1`；在编辑器中打开 `Assets/FitnessSimulator/Scenes/Bootstrap.unity`。脚本默认查找标准安装路径，其他位置用 `-EditorPath`。环境检查会校验编辑器版本、桌面构建支持、Android Java/ADB/NDK 与 APK 检查工具。

[FRAME] 编辑器关闭本工程后，在根目录执行以下命令。不要同时针对同一项目启动多个批处理；不要将 `setup` 当作日常初始化，它会重新生成原型资产和场景。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Environment.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity.ps1 -Action validate
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity.ps1 -Action test
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity.ps1 -Action build-windows
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Unity.ps1 -Action build-android
```

[COMPUTED｜HIGH] 编辑器任务入口见 `.vscode/tasks.json`：`Fitness: check` 串行执行环境、项目校验和测试；另有两个独立平台构建任务。批处理启动的编辑器窗口隐藏；日志和验证产物留在 `Logs` 与 `TestResults`。脚本拒绝旧测试报告、零通过测试以及本次未生成的构建报告。

[FRAME] 当前游戏逻辑仍是资源实验场。下一阶段按 `Architecture.md` 冻结正式接口，再做固定树闭环；本次没有实现事件系统或训练结算，也没有将待决规则硬编码到原型。

[COMPUTED｜HIGH] 清理开始时仓库只有初始化提交：绝大多数工程文件尚未纳入版本控制，README 与忽略文件已有用户改动。本次保留这些内容；未自动提交。后续提交应包含源码、资产及对应 `.meta`、包锁、项目设置和文档，排除缓存、构建包、归档与签名密钥。

## [FRAME] 本次验证

[FRAME] 清理后的验证结果见 `Verification.md` 的“研发准备后复验”段落。较早的运行截图只作为历史证据；本次不将它们标为新生成。
