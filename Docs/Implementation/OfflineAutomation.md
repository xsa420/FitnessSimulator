# [FRAME] 脱机自动运行实施

[COMPUTED｜HIGH] 首入口为 Tests/Production/Automation/OfflineM1Tests.cs，沿现有正式测试程序集编译，架构师串行执行 `Tools/Game.ps1 -Action test`。不另起Unity、不改共享命令壳。

[COMPUTED｜HIGH] 当前实现三个固定种子1/7/42，各运行三日日结，策略只读FrontendView选择首个合法餐食/入口/手动组，卧推根据公开Balance生成左右输入，每10ms推进真实核。没有注入训练结果或自动资格。Test/ScriptReplay来源明确保留。

[COMPUTED｜HIGH] 每日日结重开并逐字比较完整持久Root；所有存档在系统临时目录Fitness-Offline随机子目录。trace.jsonl保留命令、回执、Advance及每持久revision完整根（包含随机与收据），final-state.json、report.txt分别保留末态与退出信息。诊断快照不用于策略。预算100000循环/180秒，非训练无revision变化50次则失败。

[FRAME] 待串行编译执行。当前只实现日结重开完整根比较，未实现基准分叉连续运行对照、Playing/ResultReady等全断点、批外部参数或终局。三日到达称M1ThreeDaysReached，绝不称整局成功。下一步接验收策划D8、1..20种子、足额餐食、覆盖要求，并扩展断点分叉。

[RULES I BROKE]: 无。

[COMPUTED｜HIGH] 首轮实跑test-20261023-023151-864.json：exit0/sourceDrift0、205用例/正式201，三种子各三日三重开通过。种子1/7/42结算组9/3/9，真实核tick19800/6600/19800。原始报告和轨迹已复制TestResults/Automation/Baseline，保留原件。

[FRAME] 新增Tools/Automation/Run.ps1调度既有串行Game.ps1：默认D8、seed1..20、每seed1200秒、40000×天数步预算；现有三个基线测试也随参数运行。新增视图全字段轨迹供交互统计。此扩展尚未运行，仍不签全断点恢复或终局。

[COMPUTED｜HIGH] 后续入口已改为显式Seeds/Days/Source/Strategy，默认三个基线种子、三日；不再默认20seed长批。Run.ps1执行前清空相关环境并在finally恢复，策略按独立名称记录。replay/baseline保留原跳餐真实核策略；trusted/paid-rest-auto走OpenAutomation，公开菜单非skip餐、每组90秒休息、只选已启用自动容量，另有付费餐/休息/自动组三项覆盖断言。此新策略待串行执行，不沿用旧基线通过。

[FRAME] 小批命令（架构师串行执行）：`./Tools/Automation/Run.ps1 -Seeds 1,7,42 -Days 3 -Source replay -Strategy baseline`；独立生活与资格路线：`./Tools/Automation/Run.ps1 -Seeds 1 -Days 3 -Source trusted -Strategy paid-rest-auto`。D8扩展显式设Days8，批种子显式给Seeds，不隐式重抽。

[FRAME] 新经济用例自身策略预算600秒，框架超时须显式660000ms以留收尾余量。已仅在Tools/Automation暂存源码加Timeout(660000)，正式Assets仍冻结，待架构释放后只同步此单行；旧World预算不动。未将此配置当执行成功。

[COMPUTED｜HIGH] 两真实路线证据已复制至TestResults/Automation/20261023-040355-532；copy-sha256-manifest.json逐文件核对25/25相同。含两完整保存目录及本轮XML/JSON/源码清单，原件未修改。经济report为seed1、D7、41次恢复、89业务命令、144.2598275秒、PurchaseUseStudyReached；原报告仍保留四项Missing。教学属于使用训练快照分支的应用集成策略，不称公开信息策略；其原始route.txt/diagnostic.txt原样保留，不捏造trace/report文件。

[FRAME] 已在TEMP准备Run.filtered.ps1，默认filter仅OfflineM1Tests，显式-TestFilter方可选择其他路线。正式Tools仍冻结未改，待释放窗口同步；不能继续默认全跑。

[COMPUTED｜HIGH] 20261023冠军稿按架构单文件窗口接入并修独立评分审计：旧READY哈希7E849AAC1ECB6F814E92F03DFED40C178394A0998047955C8FFD2AA43EF45D50；新正式/TEMP哈希2BD2F51BB6F60B4B9546201ACB69FC39E04DADB466D6503CA7045B0907D09DE7。新稿以独立decimal将U/M量化Micro后排序、核三评委固定panel及无重复、核均分分币；待编译运行，不沿用旧证据认证。

[COMPUTED｜HIGH] 045200-920八玩法原夹具及执行XML/JSON/source已复制TestResults/Automation/20261023-045200-920，83/83文件SHA256一致；044120-839的42日失败现场及执行元数据复制相邻stamp目录，25/25一致。每目录copy-sha256-manifest.json记录原件路径/目标/校验，原件未改。失败现场保留为失败证据，不标通过。

[COMPUTED｜HIGH] 按架构窗口正式替换双冠军统一稿NaturalChampionRouteTests.cs，SHA256 0862FD66B2CBF0B8E4941044A4A37A48B1C19B767FE4E2A701A0FD5B21521D51；新增NaturalRomanceRouteTests.cs，SHA256 5DF5003962001FE4FC15DE42DA34589ED5729E0FED8500F176707FEF836EDBBE。两稿已完成TEMP托管编译，正式Unity执行待架构串行，不能将静态/托管检查算自然冠军或恋爱达成。默认均短预检，薄肌入口Explicit，长局须显式预算。

[COMPUTED｜HIGH] 051312-449的42日真实双报名双弃赛终止成功验收现场已完整复制TestResults/Automation/20261023-051312-449，原件未改；45/45文件与源SHA256一致，包含原始保存目录/trace/关键根/保留成就及XML/包装JSON/source manifest。复制一致不扩为自然冠军/恋爱证据。

[COMPUTED｜HIGH] 20261023运行空隙已归档052934-678、053645-898、054157-428至TestResults/Automation对应日期目录：171/171、75/75、31/31文件源/目标SHA256一致，总277文件，无遗漏，完整原件保留。各copy-sha256-manifest.json哈希依次B66083D7CA520FBA298837CAA25F32132E56479336FCC20B2F001B44DE84CBDE、AFE44250B4442C6CDC1965CAA4CF7441F21C01CAFB2F0BBA3BE2FF75B5BAC9B4、F26D7E2D8E6ECAD9449ED12EC5A72C9503A0B203AF14B45A51341ABB24707589。IR01/02不重复搬运；归档不将短预检扩大为自然长局成功。

[COMPUTED｜HIGH] 20261023-061453-988自然健美长局已封存：XML1/1 Passed、1183.2140027秒，wrapper exit0/sourceDrift0；report outcome=NaturalChampionD43、continued/D43/freezeVerified=true。完整现场及XML/包装/source manifest/Unity日志共36文件126,283,898字节，逐件复制前源/副本/复制后源SHA256一致，原件保留。归档目录[TestResults/Automation/20261023-061453-988](../../TestResults/Automation/20261023-061453-988/)，copy-sha256-manifest.json SHA9378A37ABCE6EA3F09E9280594BD9317C89B3989E0508FA21937D75E97FAFECD。

[COMPUTED｜HIGH] 实际[sealed-d43-proof.json](../../TestResults/Automation/20261023-061453-988/sealed-d43-proof.json) SHA F04EDE027589050CF35A9BFE277A0FE582E9F8D00CA0B7D02CCC3FA5E90717CD；绑定批次清单、真实final/report及完整save inventory，不使用D8替代。该封存长局证据对应原运行source manifest，不能签后续新源码。

[COMPUTED｜HIGH] 架构限定窗口已接入NaturalContinuationTests.cs（AA25DB3CC82BC03A2B7D9F68CA56604B84C8546F8C203CB8F432DB84FED8387A）及meta；既有NaturalChampionRouteTests.cs仅3行AuditScores接口差异：internal、requireChampion=true、最后两条胜者断言条件化。旧调用默认行为不变，续局直接false审计真实胜/败，不反射吞异常。D85出口等待实际第三届提交，入日中间Ordinal2继续正常推进。

[FRAME] 待架构串行运行filter FitnessSimulator.Game.Tests.Automation.NaturalContinuationTests.SealedD43ToD50AndD84ActualAttendance；FITNESS_CONTINUATION_PROOF为上述proof绝对路径，FITNESS_CONTINUATION_SECONDS显式1..3600；框架3660秒/外层500000步/D85/证据256MiB。尚无续届实际执行结果；源码接入不等于D50/D84通过。源码即刻冻结。

### [FRAME] 073422-328自然恋爱封存后义务分支接入

[COMPUTED｜HIGH] 仅静态核对：正式RomanceCompetitionObligationTests.cs SHA1F1FD3B057F9B8F950444EF68E8476A70FB95B9C08F886064FA9F3CFA0866710；Prepare-RomanceProof.ps1要求的完整save/final.json/report.txt由NaturalRomanceRouteTests finally产出，NaturalRomanceContinued在真实成功继续、根重开和continue-game后设置。Proof字段与接收DTO一致：Batch/SourceDirectory/RunId/ContentVersion/Files{Path,Sha256}/FinalRootPath+Sha256/ReportPath+Sha256/BatchManifestPath+Sha256。未读取活跃45ad495fd27e44feac71e417560d1e0f存档，没有生成本批proof。

[FRAME] 以下命令只供架构师在运行完成、Unity退出且完整现场按原规范封存为本批目录后执行；不是当前通过声明。封存应含save全部文件、final/report、原trace/policy-memory/关键根、XML/wrapper/source manifest，并逐文件复制SHA核对。该目录布局沿现有归档约定，若实际归档名变化须使用实际值，不能改造源以适配。

```powershell
$romanceBatch = 'D:\FitnessSimulator\TestResults\Automation\20261023-073422-328'
$romanceFixture = Join-Path $romanceBatch '45ad495fd27e44feac71e417560d1e0f'
$romanceWrapper = Get-Content (Join-Path $romanceBatch 'test-20261023-073422-328.json') -Raw | ConvertFrom-Json
if ($romanceWrapper.exitCode -ne 0 -or $romanceWrapper.sourceDrift -ne 0 -or $romanceWrapper.reportResult -ne 'Passed') { throw 'Natural route batch is not verified successful' }
$romanceProof = Join-Path $romanceBatch 'sealed-romance-proof.json'
if (Test-Path -LiteralPath $romanceProof) { throw 'Proof already exists; verify existing artifact rather than overwrite' }
& "$env:TEMP\Fitness-Automation-RomanceObligations\Prepare-RomanceProof.ps1" `
  -SourceDirectory (Join-Path $romanceFixture 'save') `
  -FinalRootJson (Join-Path $romanceFixture 'final.json') `
  -RouteReport (Join-Path $romanceFixture 'report.txt') `
  -BatchManifest (Join-Path $romanceBatch 'copy-sha256-manifest.json') `
  -Batch '20261023-073422-328' -Output $romanceProof
Get-FileHash -LiteralPath $romanceProof -Algorithm SHA256
```

[FRAME] 两个精确case filter分别为下列True（实际薄肌出场）及False（真实零出场）入口。当前工程同版NUnit已有XML使用(True)/(False)参数名；架构按例串行选择，不默认整个自动化suite。初次显式1800秒/支，测试硬范围1..3600、框架3660秒；不会继承主恋爱路线预算。两个分支各完整clone同proof源，原件不写。

```powershell
$priorObligationProof = [Environment]::GetEnvironmentVariable('FITNESS_ROMANCE_OBLIGATION_PROOF','Process')
$priorObligationSeconds = [Environment]::GetEnvironmentVariable('FITNESS_ROMANCE_OBLIGATION_SECONDS','Process')
try {
  $env:FITNESS_ROMANCE_OBLIGATION_PROOF = $romanceProof
  $env:FITNESS_ROMANCE_OBLIGATION_SECONDS = '1800'
  # 架构师可分别执行；两条保持串行，验后保留各自实际报告。
  & 'D:\FitnessSimulator\Tools\Game.ps1' -Action test -TestFilter 'FitnessSimulator.Game.Tests.Automation.RomanceCompetitionObligationTests.NaturalRomanceStillRequiresCompetition(True)'
  & 'D:\FitnessSimulator\Tools\Game.ps1' -Action test -TestFilter 'FitnessSimulator.Game.Tests.Automation.RomanceCompetitionObligationTests.NaturalRomanceStillRequiresCompetition(False)'
} finally {
  [Environment]::SetEnvironmentVariable('FITNESS_ROMANCE_OBLIGATION_PROOF',$priorObligationProof,'Process')
  [Environment]::SetEnvironmentVariable('FITNESS_ROMANCE_OBLIGATION_SECONDS',$priorObligationSeconds,'Process')
}
```

[COMPUTED｜HIGH] 接收测试明示边界：源须真实RomanceEstablished、原run唯一romance_success完整历史/已定备注/正确lean_social历史来源；仍第一届、Day<=42、0出场/无事实/所有窗口未关闭无比分、薄肌实际报名。源若已冻结，因无法亲见之前已提交ready根，本候选报CoverageMissing；不倒改日期/报名/人物/随机。Proof不满足hash/根一致属证据错误；合法来源但目标条件已错过属覆盖缺失，不冒称产品故障。

[COMPUTED｜HIGH] True分支实际失利应完整走competition_failed终局，仍可通过正常参赛义务；实际胜利走公开演出/空备注/继续到D43实际新届。False必须有下午IntValue1弃赛Accepted见证，此前无Unfulfilled/TerminalLock，之后唯一Unfulfilled/0出场/无比分/NonChampion，终局不能继续。两支保留原恋爱完整Snapshot/NoteState/Note及原lean_social资格来源、不重复授予；实际失败/缺入口/超预算保留完整现场。未获得真实NaturalRomanceContinued前不能生成proof或执行这些义务场景。

## 2026-10-24 配置驱动有限应用与表现验收

[COMPUTED｜HIGH] 本轮自动化唯一测试目录为 `Tests/Production/ConfigurationAcceptance`；架构同时移交 Presentation/Narrative及其表现测试、presentation/narrative/audio/knowledge内容。生产Application、全局内容校验、其他领域、Tools与Unity串行执行仍属架构。自动化负责同源码真实磁盘A/B、公开操作驾驶、预算、断言、失败定位和证据；不自行启动Unity。

[COMPUTED｜HIGH] `ConfigurationP1PublicRouteTests` 原四例在 `TestResults/M1/editmode-20261023-231927-538.xml` 全通过；当时测试源SHA1885C98F6E26F873C7A15D7A3EF9FA8694EE96531AEDE63B01BF06D9B015AD9F，未留直接复制源文本，后改测试不得倒签。后续严格ContentJson配置codec及12层条件roundtrip/求值纳入原第四例，在 `TestResults/ConfigR1/editmode-20261023-233712-722.xml` 复跑通过。

[COMPUTED｜HIGH] `ConfigurationP2PublicRouteTests` 三例在 `TestResults/ConfigR1/editmode-20261023-233157-632.xml` 通过：noCheck/check文件A/B真实钱/K/负费用/随机消费及幂等重开；剧情条件资格/费用显示与实扣、listen/ask后继、已读跨重开与新局；Monday显式占用与next-anchor不执行旧后继。固定seed1、每例两日以内、180秒策略预算/210秒框架门槛，复用P1驾驶器，仅快照用于断言。现场在测试输出记录的TEMP/Fitness-Configuration-P1独立目录，含源清单、程序集hash、配置raw/resolved/source/version、逐命令/回复、最终根及冻结内容包。

[COMPUTED｜HIGH] `ConfiguredPresentationTests` 的8音频资源hash/双AudioSource生命周期在233157通过；组合配置名与显式预填、知识目录/音频偏好两例首跑因编辑态Destroy报错失败。只修UI子节点逆序清理，运行态Destroy、编辑态DestroyImmediate；两例在233712复跑通过，保留首失败。音频继续复用既有项目原创WAV，不重复合成；此证据不等于真实听感或图形审签。知识内容来自已冻结策划三张现实卡，来源与游戏区别可见，目录关闭后仍可访问；偏好独立于游戏根，测试显式TEMP路径。

[FRAME] 图形与实际声音观感由架构后续有限P3验收；本记录不把旧存档迁移其他所有者的失败或未跑项签为完成，不扩展42—126日路线。
