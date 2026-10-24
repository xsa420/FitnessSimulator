# 保存体验修订 SaveR1

[COMPUTED｜HIGH] 用户反馈的“保存很久”包含真实存储和内部自动日程推进。原GameSession.View在DayEnd/EnterDay、未启动游标、非PreSet阶段以整页“正在保存”覆盖业务正文；每个Advance只执行一条玩家/NPC/屏障命令。没有人为最低展示时间，但多次内部提交被表现为连续保存页。

[COMPUTED｜HIGH] 基线012343-697复制ConfigR1已验两日存档（TEMP/Fitness-Usability-TwoDay/688544e9df6c4d80ab4a035810d01258/save），只执行D2日结到D3早餐。31次Advance、32个已提交revision；Execute+Advance+ReadView累计6404.2506ms，测试区间墙钟6484.9198ms另含审计与输出，不把墙钟全部当用户等待。store.commit累计3002.2769ms，JSON encode/decode累计2892.8503ms嵌套在其他计时内，不能再相加。

[COMPUTED｜HIGH] 修订：RootEnvelope.DetachedCopy复制每个可变列表和记录，片段JSON/标识是不可变string，可共享；避免反复转义/解析约777KB根。LocalCheckpointStore仍逐命令执行原日志flush、根写flush、回读逐字验证、ledger提交与确认顺序；没有合并事务、减少持久化边界或改异步写入。默认关闭的RuntimeTiming仅提供线程局部观察。

[COMPUTED｜HIGH] Application用IsTransitioning明确内部自动业务推进，轻量投影当前日程作为冷恢复回顾，真实训练输入不误标。Presentation保留同局上一业务页，禁旧业务控件与同帧重发；过渡仅中性小点，确认同局持久revision递增才短闪勾号0.55秒且不阻止已可用操作。菜单/新局/换局清缓存，存储错误立即优先呈现；没有主屏保存页、额外确认或人为最低推进时间。

[COMPUTED｜HIGH] 012827-782实际15/15通过、sourceDrift=0：同一实际短链1例；原CommitCopyIsolationTests 11例（含8处断电/未确认边界与候选/返回值/永久片段隔离）；CommitFeedbackPresentationTests 3例（保页/禁重复/继续Advance、确认revision才闪且不延迟、错误优先/换局冷回顾）。根编码计数从“候选复制+写盘”2次收紧为仅持久写盘1次；故障前后发布预期未放宽。

[COMPUTED｜HIGH] 对照仍31次Advance/32提交，5人物从D2到D3、同operation幂等、重开一致；完整最终root逐字等于基线（包含身体/余额/计划/树/时间/随机/收据/冻结配置）。新调用累计3301.6697ms，较6404.2506ms减少48.45%；Execute86.9421ms、Advance2796.4469ms、ReadView418.2807ms。JSON嵌套成本505.9405ms；真实store.commit仍2283.5698ms，单次最大125.1408ms。此为本机编辑器同源短段对照，不外推所有设备的存储速度。

[COMPUTED｜HIGH] 原始证据：基线TEMP/Fitness-SaveExperience/d94400592fab459caa4a26517167d370；新版818a7c01e00f417fb360afec4dd245a3保留source-copy/source-hashes.txt、实际save、timing.txt、final.json。测试源码及DLL对应以各批Tools清单/source-copy为准。

[COMPUTED｜HIGH] 单图012925-294 Passed/exit0/sourceDrift0，实际D2归档复制后正式continue-game/next-day提交；保留日结业务页、禁用旧按钮、角落已确认revision勾号可见。路径TestResults/SaveR1/Captures/20261024-012938-6430b7，旁存business-before/transition-after。640×320实际uGUI，只作为布局证据，不外推设备触控。随后同冻结源码构建SaveR1独立包，结果如下；ConfigR1保留。

[COMPUTED｜HIGH] SaveR1构建013030-152成功，exit0/sourceDrift0/configurationDrift0。Builds/SaveR1/Android/FitnessSimulator-SaveR1.apk为43,871,785bytes，SHA256 CB5A50E47205FC79161BFEC3DF33D5A4FC74FA242E605EA51FA64AD8F5A9E506。220项源码对15例测试与单图各零差异，final-gates.json及原签名/权限/badging输出旁存。apksigner v2通过，沿用现有Android Debug证书db282bd457fd001c3dab20dbf3b944523ac5f231d48ab49517df3e053c43a581；无INTERNET权限。原ConfigR1 APK仍SHA256 235C15F643F87186F5E996085F4FB2C32E11A272826D3AFF27B5BB8B9A29BDA8未改变。本次有限保存体验修订完成，不追加性能工程或长线。

[RULES I BROKE]: 无。


