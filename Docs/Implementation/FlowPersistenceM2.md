# [FRAME] M2 世界屏障、分域随机与语义重放

[COMPUTED｜HIGH] 2026-10-23：新增独立纯规则已纳入父任务串行 Unity 回归，本轮197/197通过（正式193、历史4），sourceDrift=0、exitCode=0。本模块12个新增测试全部在报告中通过；源码保持冻结。应用世界编排、真实 NPC 行为与主流程接入尚不由此证明。

## [FRAME] 接口与所有权

[COMPUTED｜HIGH] `Production/Flow/WorldBarrier.cs`：`Create(runId, day, phase, anchorOccurrenceId, participants, playerActorId, scheduleRevision, stepIds)` 创建快照并复制名单/步骤。`WorldBarrierState`持有完整身份、阶段、共同步骤列表与逐角色`ActorCursor`。键为长度前缀编码的 run/day/phase/anchor；scheduleRevision 单独校验。

[COMPUTED｜HIGH] 转换统一返回 `BarrierTransition { State, Changed }`；输入、嵌套角色和列表不被修改。签名：`Arrive(state, barrierId, scheduleRevision, actorId, kind, reference)`；`BeginInteraction(state, barrierId, scheduleRevision)`；`CompleteStep(state, barrierId, scheduleRevision, actorId, stepId)`；`CompleteActor(state, barrierId, scheduleRevision, actorId, completionReference)`。`CanAdvance`、`Validate`只读。未知角色、错误屏障键/调度版本、冲突事实、超前步骤均拒绝；同一事实重放 Changed=false。

[FRAME] `ArrivalKind.Pending/Arrived/Skipped` 区分未完成到场解析、真实入口到场与显式消耗/跳过事实。reference 必须非空；纯规则不证明其业务合法性。应用从真实入口决策生成引用，不能以同计划冒充训练到场。全员到场或显式跳过后才开放交互；全员完成当前共同日切步骤才能推进下一步；全员完成且没有未结算结果才可跨屏障。StepIds只能是全员共同阶段，不能用不同角色的训练组数作共同步骤。

[COMPUTED｜HIGH] 可选字符串引用显式使用 HasWorkout/HasPendingResult；没有可空嵌套状态对象。HasPendingResult 指尚未结算结果，阻止 CompleteActor；Validate也拒绝 BarrierReached 与 HasPendingResult 同时为真。已结算回执不可继续标pending。日历不由本模块递增，下一屏障由应用创建。

[COMPUTED｜HIGH] `Production/Core/DomainRandom.cs`：`Create(runId, actorId, purpose, runSeed)`；`PeekUInt(state)`；`NextUInt(state)`、`NextInt(state, exclusiveMaximum)` 返回 `DomainRandomDraw { State, Value:uint }`。返回状态是新对象。协议字段 RunId/ActorId/Purpose/AlgorithmVersion/State:ulong/DrawIndex:long；版本1采用 SplitMix64 混合值高32位；零状态合法；未知算法和索引溢出拒绝，不自动播种。

[FRAME] v1播种字节契约：按顺序编码固定域标记 `domain-random-splitmix64-v1`、run、actor、purpose、seed；每字段严格UTF8字节前加4字节无符号小端字节长度。SHA256前8字节按小端解释为初始状态。每原始抽样状态加 `0x9e3779b97f4a7c15`，执行代码中固定混合常量，输出高32位；NextInt以2^32为样本域拒绝尾部不完整桶。拒绝抽样也计入DrawIndex，应用必须保存返回的完整State，不能自行只加1。

[COMPUTED｜HIGH] 既有 FlowState.RandomState 和 CommandEnvelope 未修改。经济调用方可直接消费NextUInt的完整uint样本。失败候选丢弃即不推进权威流；实际提交必须把领域变化、随机状态、步骤游标同一次写入；未知写结果由既有dispatcher冻结。

## [FRAME] 语义收据修复

[COMPUTED｜HIGH] `SerialCommandDispatcher.Execute`原先同SemanticKey无条件返回旧成功；现在比较既有PayloadHash，不同载荷返回 Rejected/SemanticConflict。现Fingerprint不含OperationId/ExpectedRevision，但包含run/actor/resource/event/set/action/payload/source/choice，适合同业务参数不同操作ID的重放；不会使Test与NPC来源等价。

[COMPUTED｜HIGH] 新增重载 `Execute(command, Func<CommandEnvelope,string> resolveSemanticKey, prepare)`：身份授权→旧OperationId→可信应用推导语义键并查账→版本检查→prepare→校验prepared键与推导键一致→提交。语义查账先于过期机会/报价检查，旧操作ID优先。原重载兼容保留，仍在prepare后查语义账；需要过期状态重放的调用方必须使用新重载。

[FRAME] resolver是应用内部可信函数，按认证命令业务身份计算，不能接受不可信用户任意指定查询键，也不能依赖当前报价是否仍有效。授权回调必须校验调用权限而不能只看用户传入Source。模块没有新增无授权收据查询端点。

## [FRAME] 实际验证与范围

[COMPUTED｜HIGH] 新增12个测试：DomainRandomTests 4个（黄金序列与实际Json恢复、UTF8播种黄金值/域隔离/丢弃候选、拒绝采样、未知版本/溢出）；WorldBarrierTests 6个（到场/跳过/候选隔离、逐角色共同步骤与重放、部分到场/部分步骤实际Json恢复等价、pending完成和损坏恢复拒绝、名单深拷贝和可选引用实际Json往返、旧身份/错误角色/冲突事实）；CheckpointRecoveryTests 2个（认证语义重放早于过期prepare、旧op优先/来源拒绝/异载荷冲突、旧重载冲突/推导键一致性）。具体方法名见对应测试文件。

[COMPUTED｜HIGH] 已核对 `TestResults/M1/editmode-20261023-013440-172.xml`：total=197、passed=197、failed=0、skipped=0；`test-20261023-013440-172.json`记录正式193项、sourceDrift=0、exitCode=0。`M2-first-regression-coverage.json`记录原正式132项全部保留（missingOldTests为空），并列出本模块12项新增测试及其他领域新增项。原136基线由正式132项与历史4项组成。

[COMPUTED｜HIGH] 本轮含 `PendingResultBlocksCompletionAndCorruptCompletedRestoreIsRejected`，证明 M2-FP01 的纯模块完成入口及损坏恢复防护回归通过。父任务另报告此前013234轮为集成测试使用不支持的Is.AnyOf导致编译失败，已由父修复；该旧失败不替代013440轮的实际通过结果。

[FRAME] 上述结果认证本轮被测源码与列出的用例，不代表正式NPC行为、全世界日切、主流程接入或M2阶段验收通过。父任务后续补D09图形单项；本模块保持源码冻结，本次只更新实施记录。

[RULES I BROKE]: 无。
