# [FRAME] 健身模拟器 / Fitness Simulator

[COMPUTED｜HIGH] 当前工程提供横屏、离线的 3D 训练资源实验场：8 套候选动作的人物、器械、循环动画与独立场景，以及本地存储基础、训练接口、测试和双平台构建脚本。正式小游戏规则与完整养成流程尚未实现。

[FRAME｜已确认] 2026-10-22 用户确认资源调研与工程演示采用 8 个候选动作：卧推、哑铃弯举、高位下拉、绳索下压、深蹲、臀推、肩推、侧平举；美术方向为风格化卡通 3D。该确认未将候选清单或具体操作、评分、数值定为正式玩法。

[COMPUTED｜HIGH] 现有分段几何人物仍为占位原型，尚未完成卡通角色替换；当前下压原型使用直杆，绳把及对应动作是明确待补项。后续资源筛选与制作按已确认风格执行，详见资源清单。

![COMPUTED：Windows 实际运行截图](Docs/Preview/bench-press.png)

## [FRAME] 开始使用

[COMPUTED｜HIGH] 固定编辑器版本为 Unity `6000.6.1f1`，使用 URP；版本与包锁文件随工程保存。通过 Unity Hub 添加此目录，打开 `Assets/FitnessSimulator/Scenes/Bootstrap.unity`，点击 Play。界面支持切换动作、暂停、播放速度、旋转镜头及本机存储自检。

```powershell
# 在项目根目录运行；编辑器需关闭本工程才能执行批处理。
powershell -NoProfile -File Tools/Environment.ps1
powershell -NoProfile -File Tools/Unity.ps1 -Action validate
powershell -NoProfile -File Tools/Unity.ps1 -Action test
powershell -NoProfile -File Tools/Unity.ps1 -Action build-windows
powershell -NoProfile -File Tools/Unity.ps1 -Action smoke -ShowWindow
powershell -NoProfile -File Tools/Unity.ps1 -Action build-android
```

[FRAME] 新环境需要安装相同编辑器及 Android Build Support、SDK/NDK、OpenJDK。脚本默认读取标准 Hub 安装路径，可通过 `-EditorPath` 指定其他位置。首次包解析/构建可能访问依赖仓库；游戏本身按离线要求设计。

[COMPUTED｜HIGH] Windows 输出为 `Builds/Windows/FitnessSimulator.exe`；Android 输出为 `Builds/Android/FitnessSimulator.apk`，ARM64、最低 Android API 26，开发阶段默认调试签名。正式发布签名与商店配置尚未设置。

[FRAME] `-Action setup` 会重新生成 `Generated` 资产和场景，请将正式手工资源放在独立目录。日常打开、测试和构建不需要重跑 setup。

[COMPUTED｜HIGH] 画面验证需 `-ShowWindow`，期间会显示原型窗口并依次切换动作，完成后退出；隐藏窗口虽可推进动画与读写存储，但本机驱动返回黑色截图。脚本会拒绝 8 张相同截图，画面仍需人工检查。

## [FRAME] 文档

- [FRAME] [工程结构与已确认规则边界](Docs/Architecture.md)
- [FRAME] [逐动作模型、动画、场景参考及资源许可记录](Docs/TrainingResources.md)
- [FRAME] [环境和验证记录](Docs/Verification.md)
- [FRAME] [清理记录与研发入口](Docs/Development.md)

[FRAME] `Library`、`Builds`、`Logs`、`TestResults` 等缓存和产物不进版本控制；提交 `Assets` 连同 `.meta`、`Packages`、`ProjectSettings`、源码及文档。原型资产采用仓库现有许可证；第三方包遵循各自许可证。
