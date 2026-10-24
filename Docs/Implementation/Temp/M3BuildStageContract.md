# [FRAME] M3独立开发包阶段参数（TEMP，未接正式源）

[FRAME] 保持原executeMethod `FitnessSimulator.Game.Editor.GamePlayerBuild.BuildAndroid`；根脚本仅需在显式M3开发包请求时传`-fitnessBuildStage M3 -fitnessDevelopmentBuild`。阶段大小写严格，只允许M1/M3，重复或缺值拒绝；不传阶段默认M1。不传开发开关保持原非Development选项。开发开关只添加BuildOptions.Development，不添加AutoRunPlayer/调试器连接或设备安装。

[FRAME] 输出`Builds/M3/Android/FitnessSimulator-M3.apk`及同目录build-manifest.json；报告`TestResults/M3/build-Android.json`。默认M1输出及报告路径不变。Windows代理同样按阶段定目录，但本次目标为安卓开发包。应用标识、Bootstrap、persistentDataPath及存档目录均不改，因此不能把“输出目录隔离”冒称“安装后存档隔离”；本任务明确不安装设备，不会触碰现存局。

[FRAME] 构建清单记录APK SHA256、阶段、开发标志、实际BuildOptions、编辑器版本、场景及define、安卓包名/后端/架构/minSDK、packedAssets验收标记。原正式场景验证、旧Runtime排除、资源隔离GUID/hash校验、finally恢复和IsolationJournal位置完全保留。源文件/配置文件hash清单继续由根Tools/Game.ps1现有source manifest扩展/归档负责；请根将本次源码与Packages/ProjectSettings配置manifest连同源漂移结果放入M3产物目录，必要在根汇总manifest记录各manifest哈希，不能仅凭builder配置摘要宣称有完整源码清单。我不写Tools脚本。

[COMPUTED｜HIGH] 对照正式构建源SHA256为71401A5E0AFEF7C2421402CBD2870D33A0F4ACE45DD11F00564F0367FB3A5DE7，完整临时副本GamePlayerBuild.M3.cs.utmp及最小差异GamePlayerBuild.M3.diff在本目录。已独立引用编译，结果见GamePlayerBuild.M3.Compile.log；此为引用闭合检查，未运行编辑器、未构建APK、未移动资源。正式接入和构建必须待全部M3路线／组合／续届验收完成及根开放窗口，不挤当前长线。

[RULES I BROKE]: 无。
