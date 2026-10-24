# [FRAME] M3赛事领域交接

[FRAME] 采用当前42日候选规则，版本`CS.competition-42day.v1`。本模块只输出状态、占用意图和事实；不写根存档、终局、奖励、解锁、时间或界面。父流程须将返回的三部分与命令回执、随机流回执在同一提交中落盘。

## [COMPUTED] 文件与接口

[COMPUTED] `Production/Competition/CompetitionContent.cs`定义两个项目、8名评委和日期；`CompetitionScoring.cs`实现精确评分；`CompetitionModels.cs`定义可序列化记录；`CompetitionRules.cs`实现迁移；`CompetitionValidation.cs`验证恢复；`CompetitionQueries.cs`只返回允许公开的视图。命名空间`FitnessSimulator.Game.Competition`。

| [COMPUTED] 接口 | [FRAME] 父流程责任与输出 |
|---|---|
| `Create(runId,playerId,rivalId)` | 仅创建第1届；既有竞争者身份必须由父传入。 |
| `Open(state,day,bodyJudgeDrawIndices,thinJudgeDrawIndices)` | 每届D8偏移调用。每池三个无放回索引范围依次`[0,4)、[0,3)、[0,2)`，父使用正式随机流均匀抽取并保存回执。重复同输入不重抽；不同抽签拒绝。 |
| `AdvanceDisclosure(state,day)` | N04执行，按D14/28/35披露1/2/3席，不能回退。 |
| `SetEnrollment(state,kind,day,enrolled,slotAvailable)` | D8至D41截止前；父先检查未来时段占用。返回稳定`ReservationId`的Reserve/Release意图；不消耗锚点，不产生比赛事实。 |
| `CloseRegistration(state,day,"AllAnchorsFinishedBeforeDayEnd")` | D41所有锚点完成且日结尚未开始的受信阶段；未报名不失败。 |
| `Freeze(state,input)` | D42初始化及计划/搭子切换完成，任何清晨/早餐/其他事件前，父先使双方实际角色到达同一屏障。 |
| `Attend(state,kind,day,anchorId)` | 玩家明确出场，上午/下午分别调用。比分、实际出场数和Champion/NonChampion事实同一返回变更。 |
| `Decline(state,kind,day,anchorId)` | 明确放弃；无比分、无NonChampion；下午0出场才返回Unfulfilled。退出程序不能调用此接口。 |
| `CloseWindow(state,kind,day,anchorId)` | 未报名窗口按上午、下午依次关闭，适用于跨锚点消费。已报名但未决时拒绝，必须恢复待出场选择。 |
| `NextEdition(state)` | 两窗口都关闭且至少出场一次、无失败后，保留旧届完整历史，建立下一42日周期。父以命令幂等保证只推进一次。 |
| `Export(state)` / `Restore(dto,expectedRunId,expectedPlayerId,expectedRivalId)` | 返回深拷贝；恢复严格核对身份、版本、顺序、冻结来源、比分和事实。不能以存档内身份充当受信期望身份。 |

[FRAME] `CompetitionChange.State`替换当前赛事记录；`ReservationIntents`和`Facts`仅包含本次新产生的操作。重复已提交出场返回无新事实，持久状态中的原事实仍保留。父不可将预览的变更提前应用一半，也不可在提交失败后保留出场计数。

## [FRAME] 真实来源与阶段边界

[COMPUTED] `CompetitionFreezeInput`包含`BarrierId,Day,Phase,Player,Rival`；每个`CompetitionActorSnapshot`包含`ActorId,SourceCommitId,ActorRevision,SourceKind,Day,Phase,Body`。接收的SourceKind分别必须为`PlayerProduction`、`NpcProduction`，拒绝`FormulaFixture`；检查身体身份、修订号、赛日、`DayOpenReady`和字段完整性，并克隆输入。

[FRAME] 这些字段表达父流程的受信断言，不能凭DTO证明来源。父必须从已提交的真实玩家及既有竞争者身体读取，绑定实际提交版本，检查双方实际时间进度后创建同一Barrier；禁止把人工向量、固定成长表或预测值包装成Production。测试驾驶的CommandSource.Test与身体来源不是同一个概念；经正式输入和领域结算得到的身体仍是正式来源。

[COMPUTED] 两赛共用同一组冻结身体；上午只生成上午结果，下午真正出场才生成下午结果。上午NonChampion阻止下午出场；上午Champion后下午NonChampion保留两条事实。Decline不生成比分。全部窗口0出场才生成Unfulfilled。事实包含稳定`FactId/ResultId`供父进度快照关联，不直接触发终局存储。

## [COMPUTED] 评分与公开查询

[COMPUTED] 单评委使用精确有理数，先舍入为四位比较分整数，三项求和排序，显示分币为总整数除300后四舍五入。健美同分键U→M，薄肌L→meanT→c，各归一为微单位；全部相同为并列冠军。薄肌L使用共享`BodyMetrics.GetThinBuildIndexMicro`的量化结果，偏好H复用`GetThinRegionAdaptation`；基础魅力只计一次，没有疲劳/伤病额外赛事扣分。

[COMPUTED] `CompetitionQueries.Entries(state,day)`返回公开席位、定性描述、入口/报名/出场状态和已成立结果，无未公开评委ID或权重，无预先计算的未来结果。`CompetitionQueries.Rival(state,liveBody,day,phase,sourceCommitId)`分级返回10点力量均值区间、5点体脂区间与肌群极值、D35后的完整实际属性及时间戳。父负责提供该时点实际身体。D28肌群极值使用每组区域均值；均衡描述仅说明相同/存在差异，未擅设“均衡/偏科”阈值。

## [COMPUTED] 验证范围

[COMPUTED] 独立领域测试22项已通过：规格算例、完全并列、四位总分相同后的U/M与L/meanT/c全部同分键、脂肪端点、共享L平台、魅力与疲劳、固定抽签、报名冲突/退报、截止、披露、冻结来源/重放、双赛顺序、先胜后败、末窗口履约、续届与存档篡改。正式落盘后执行`dotnet run --project Assets/FitnessSimulator/Tests/Production/Planning/Standalone/Planning.Verify.csproj --configuration Release`，含既有计划测试共44/44通过。测试向量仅是公式/协调器合同夹具，不是实际游戏来源或长期可达性证据。

[COMPUTED] 父第二窗口报告指定域及集成278/278通过，exit0/sourceDrift0，报告`TestResults/M1/editmode-20261023-025530-087.xml`；包含本模块3项实际引擎JsonUtility测试，验证空/已抽签/报名、共享冻结/上午结果后恢复/下一届、未履约无虚构比分。此报告不包含后续应用适配的6项新增测试。尚未据此声明主流程、真实NPC长跑、手动体验或全套CS验收通过。

## [COMPUTED] 应用适配新增文件

[COMPUTED] `Application/GameSession.Competition.cs`与`Tests/Production/Integration/GameSessionCompetitionTests.cs`已落盘；独立编译含应用和测试为0警告0错误，不执行引擎。新增6项应用测试尚待父串行执行；候选合同测试不冒充真实42日轨迹。未改既有应用文件，父负责接入以下hooks。

| [COMPUTED] 私有partial hook | [FRAME] 接入点 |
|---|---|
| `EnsureCompetition(root,rivalId="rival")` | 新局初始化。fragment `competition/player`直接存CompetitionState。 |
| `ReadCompetition(root)` | 进度模块及加载校验读取，严格核对受信run/player/rival及比分事实。 |
| `PrepareCompetitionDayOpen(root,state,outcome)` | N04，在共同入日屏障已提交后；D8抽签/披露/D42冻结；D43若尚未手动切届则自动建立下一届。 |
| `PrepareCompetitionDeadline(root,state,outcome)` | D41 E00，所有锚点结束后、日结业务前。 |
| `TryResolveCompetitionAnchor(root,state,flow,outcome)` | `resolve-anchor`内，在占用跳过及普通事件抽取之前；true表示已处理固定赛事或末窗失败。 |
| `PrepareCompetitionPassedWindows(root,state,flow,outcome)` | 上述hook内部已调用；额外跨锚点路径必须执行，按实际AnchorIndex关闭已越过的未报名窗口；越过已报名未决场次拒绝。返回是否产生失败。 |
| `TryPrepareCompetitionAction(root,action,out outcome)` | `Prepare`最前匹配`competition-*`；true时直接返回，避免后面的旧局部状态覆盖本适配写入。 |
| `IsCompetitionMenuOpen(root)` | `Advance`中暂停普通自动推进；返回菜单/关闭操作仍可执行。 |
| `TryProjectCompetition(root,state,view,actions)` | 旧节点handler switch之前；true表示已构建赛事全屏/菜单，跳过旧switch；false仍可能添加了入口。 |

[COMPUTED] 公开动作包括`competition-menu/close/enroll/withdraw/attend/decline/next`；项目通过IntValue 0健美、1薄肌选择，时段由实际session/flow再次核对，不能用菜单的CanAttend绕过锚点。固定比赛采用Handler=`competition`的单节点事件，沿用普通start-node/finish-anchor和NormalCompletion；事实与成绩日志随同一个父候选提交。

[COMPUTED] `competition-random/player`保存正式分域随机状态，purpose=`competition-judges`；`competition-ui/player`只保存菜单开关，报名与查看不耗锚点。保留时段写入flow.Occupancies，正式赛事解析时转为事件节点占用；退报释放对应reservation，不覆盖其他事件占用。菜单仅使用已有FrontendView Title/Body/Actions，披露内容从实际对手body/session读取；缺失或落后则明确未就绪，不显示旧数据。

[FRAME] 世界协议由父拥有：`world/shared` schema1至少含Day/Phase/BarrierId，Phase=`DayOpenReady`；`session/rival`同日同阶段；`body/player`及`body/rival`是实际已提交身体。本适配要求这些来源片段Revision不大于root.Revision，ActorRevision使用Body.Revision，SourceCommitId使用root.LastCommitId。缺失、日期落后、阶段不符、候选尚未提交一律拒绝，不构造替代NPC。后续真实世界屏障与持久化整局验证仍归父集成。

[COMPUTED] 策划指出下一届D42当日Rival查询下界不一致后，已统一为与Entries一致的`max(1,offset)`，同日新届仅Level0；已补回归，领域44/44再次通过。

[RULES I BROKE]: 无。
