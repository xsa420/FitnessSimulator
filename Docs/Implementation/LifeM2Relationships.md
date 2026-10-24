# [FRAME] 关系与人物自主实施记录

[COMPUTED｜HIGH] 首批已落盘：Production/Life/Relationships内TeachingLedgerRules、RelationshipRules、ConfessionRules；Tests/Production/Life/RelationshipRulesTests含5项测试源码。尚未取得编译或执行结果；父发起首个无人回归窗口后已回READY并冻结源码。

## [FRAME] 父接入契约

[FRAME] RelationshipState按run/actor独立持久化，WorkoutInteraction按整次训练独立持久化。OpenWorkout的在场名单必须来自双方实际进入训练的锁定快照；legalRest仅由父权威流程提供，不能来自客户端请求。每个训练只有一份机会，不能每个休息重新OpenWorkout。角色ID为rival/beginner/veteran/coach。

[FRAME] PrepareDialogue、PrepareInvite、PrepareTeaching、PrepareGift返回RelationshipPreparation；Status非Prepared不得发布成功后继。关系候选、互动机会及Payment.Life/Receipt或Delivery.Inventory/语义回执、事件后继必须整笔同提交。固定对话option 0/1/2/3分别+4/+1/+5/+6，后两项要求当前20/70。支付不足、赠礼不足、非法相遇不消费人物机会。免费教导仍有零金额收据并消费机会。

[FRAME] PrepareTeaching的nextSetSequence与commitId来自父已分配的真实后续组序号及提交身份；FreezeBenefits在组前调用并持久保存结果。当前搭子风险仅实际在场时发放，付费教导仅后续组有效。AdvanceDay只接受下一日；由父N02应用搭子计划，关系域不复制身体或资源。

[FRAME] TeachingLedger按run/actor/day/workout独立初始化。父从训练核取得TeachingClaim，调用Prepare，随后把Ledger和Resolution交给组结算，同身体、训练进度与公共回执提交。账记录名义额度，零完成不清旧账、老手1后教练仅补2；身体上限不影响额度。已提交组的重放由父公共语义回执先拦截，不能直接重复执行准备器代替恢复原始结果。

[FRAME] ConfessionRules.Freeze接受归一原始魅力micro、赛事共享薄肌指标micro、体脂百分比micro，冻结显示千分概率。Prepare接受同一次uint随机样本，输出关系与结果收据；随机后状态、收据、成功事实及后继同提交。Decline清待处理资格不退书签。已尝试后最早day+8再赠礼。概率确认必须持久保存Freeze输出，父不能重开时重读属性或重抽随机。

## [FRAME] 未完成与下一批

[COMPUTED｜HIGH] 尚未实现Actors初始化、逐锚点事件选择和真实输入策略；尚无本批生产接入或完整路线轨迹。训练线程已提供ITrainingInputSession当前提示接口，八玩法实现正在闭合。源码冻结期间仅核对规格与该接口。

[FRAME] 后续依次补关系序列化/身份和重放边界、四人物独立初始资源、单人物入口策略与真实输入轨迹，再扩四人物。世界屏障、生产命令调用及自动化由父拥有；不在本域推进世界时间，不预跑全天，不直接指定成功、有效次数或属性增长。

## [COMPUTED｜HIGH] 第二窗口交付状态

[COMPUTED｜HIGH] 已新增Production/Actors/ActorPolicies.cs与ActorInputPolicy.cs。Initialize/InitializeFour返回独立身体、训练资格、钱包摄入、库存、两店与PlanDraft；四人预设引用现有计划目录。ChooseEvent只选择父提供的合法候选，ChooseSet处理压力、伤病、挑战门槛及最多两次额外休息，ChooseMeal/ChooseNotebookPackage返回配置ID，不扣资源。

[FRAME] ActorInputPolicy.Execute只执行一个正式NPC组，request须NpcPolicy且ActorId匹配；使用调用方样本读取可见提示，生成按钮、坐标与虚拟时间输入，Result只能来自统一核。返回Steps、SamplesConsumed、KernelSeed与结果；父保存请求、策略版本、样本流起点、消费样本及轨迹摘要，用同请求/种子回放。样本不足抛NeedNpcInputSamples，未返回任何持久候选；重试保留原样本前缀。方法不推进世界锚点、不更新属性，父在世界屏障内分配组并提交结果。

[COMPUTED｜HIGH] 新增ActorPolicyTests的资源隔离、合法决策及8核真实输入逐步重放共10项源码；RelationshipBoundaryTests新增赠礼99/100与空背包/冷却/放弃、教练搭子与非法休息拒绝、p=.710相邻uint与.950上限、真实序列化与跨角色拒绝共4项。关系连同首批共9项源码，尚未获得新增执行结果。

[FRAME] 当前尚缺训练Claim—身体封顶—名义账的完整组合实证、四人逐锚点持久生产调用及正常新局到终局轨迹；不能把输入轨迹重放相同解释为自然路线可达或策略平衡通过。已向父回报第二窗口READY并暂停Assets改动。

## [COMPUTED｜HIGH] 应用接入包与已核测试

[COMPUTED｜HIGH] 已只读核对[第二窗口XML](../../TestResults/M1/editmode-20261023-025530-087.xml)：ActorPolicyTests10/10、RelationshipBoundaryTests4/4、RelationshipRulesTests5/5通过。该轮早于本节应用partial和后续测试，不能用来证明本节改动通过。

[COMPUTED｜HIGH] 父已新增授权并由本聊天落盘Application/GameSession.Relationships.cs、GameSession.NpcPolicy.cs与Integration/GameSessionActorsTests.cs。NPC初始化、逐节点合法命令选择、真实输入完成候选已接成应用hooks；经济命令复用父ShopCommand/ItemCommand。SelectNpcAction必须在dispatcher候选root内运行，其额外休息标记及选择随机消费与PrepareActor一起发布，不能选完丢弃该root。

[FRAME] 父负责在实际到场名单锁定后调用OpenPlayerInteraction；真实rest后MarkRelationshipRest；BeginSet前FreezePlayerBenefits（void，写set-benefits）；结算改用SettlePlayerTeaching（TrainingSettlement）。TryPrepareRelationship只拦玩家rel-*并提前返回；AppendRelationshipOptions显示已完成休息窗内的选项，N01统一AdvanceRelationships。关系请求携带不可变run/actor/day/workout/gift身份，父dispatcher用RelationshipSemanticKey先查公共业务回执，换OperationId也不能重复消费或授予。

[COMPUTED｜HIGH] 修复NPC输入trace的持久表示：TrainingInput本身未声明可序列化，ActorInputStep改为自有扁平字段并通过Input属性重建。新增应用测试覆盖新局四人私域/重开、深蹲及侧平举trace序列化后重放；另新增2项真实核→Claim→名义账→Body组合边界测试。以上新增尚未运行。

[FRAME] 本包尚待父编译、世界屏障接线、同锚点在场与完整日切、关系/经济故障恢复和新局至终局自然轨迹；当前不能宣称生产整局完成。

[RULES I BROKE]: 无。
