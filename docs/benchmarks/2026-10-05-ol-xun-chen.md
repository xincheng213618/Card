# 荀谌单武将批次

当前 OL 荀谌（群势力、3 体力）以官方页当前文本接入：锋略（出牌阶段开始时，你可以拼点：若你赢，被拼点者将其每个区域各一张牌交给你；若你没赢，你交给被拼点者一张牌。拼点结算后你可以令其获得你的拼点牌）+ 谋识（出牌阶段限一次，你可以交给一名角色一张手牌，然后当其于其下回合出牌阶段对一名角色首次造成伤害后，你摸一张牌）。官方立绘 46000 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-xun-chen-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 锋略 | playPhaseStarting 可选触发：selectTarget otherLivingWithHand → startPindian（resultBind，触发式拼点沿简庸/伏皇后）→ 赢：三段 selectAndMoveOwnedCard（hand/equipment/judgment，chooser=被拼点者，destination ownerHand，skipIfNoCards，pindianWon+sourceBind）→ 输：一张手牌给 selectedTargetHand（pindianNotWon）→ chooseOption(give/keep) + givePindianCard（本批新增 op：从弃牌堆领取己方拼点牌交予对方，事件 PindianResultDeterminedEvent 定位牌 ID）。 |
| 谋识 | 赠牌：playPhaseStarting 可选触发（turn 级 booleanState 账本 + 手牌非空 compare 门控）→ selectAndMoveOwnedCard 至 selectedTargetHand → changeParticipantMarker mouShi +1。汲取：afterDamageApplied 强制触发（subject any）+ cardActionPhaseIsPlay + eventSourceMarkerCount（本批新增触发值，伤害事实携带来源标记）→ 摸一张 + targetRef eventSource 消耗标记。过期：turnEnding（otherLiving）eventTargetMarkerCount ≥1 → 移除。 |

## 共享能力扩展

- 触发值 `eventSourceMarkerCount`：SkillProgramTriggerFacts 增 `EventSourceMarkerCounts`（after-damage 上下文按攻击来源座位填充），usesMarker 校验、afterDamageApplied 窗口校验同步。
- 标记操作 `targetRef` 支持 `eventSource`（解析走窗口 SourceSeat）；标记变更白名单（组合校验器 + 运行时生命周期门）补 TurnEnding，eventTargetMarkerCount/eventTarget 引用白名单补 TurnEnding。
- 新效果 `givePindianCard`（op 7161）：descriptor/handler/host 反射注册，引擎方法按最后一条本技能拼点结果事件定位己方拼点牌并从弃牌堆移动到对方手牌（awaitMovementTriggers 沿曹仁据守口径）。
- `selectAndMoveOwnedCard` 验证了跨玩家区域拿牌（cardOwnerRef selectedTarget + chooserRef selectedTarget/owner + destination ownerHand）与既有的 lose-branch 付牌路径。

## 边界口径

- 触发式拼点的双方拼点牌由引擎随机选取（既有 startPindian 口径）；平点按“没赢”处理（平点非赢）。
- “每个区域各一张”按手牌/装备区/判定区各一段独立拿牌实现，空区域 skipIfNoCards 跳过。
- 谋识标记在其“下回合”的出牌阶段伤害时消耗一次（首次），若未触发则在该回合结束时效移除；期间其他角色回合结束不满足条件（eventTargetMarkerCount 按回合拥有者读取）。

## 验证

- 定向 `--filter="Ol Xun Chen"`：3/3 通过（定义元数据；锋略拼点结算与平点输分支付牌；谋识赠牌标记→标记玩家出牌阶段伤害→摸一张+标记一次性消耗→过期不再触发，含冷恢复重放比对）。
- 例行 `tools/Test-Changed.ps1`（分支）：Core 260 通过 / 27 失败 + WPF 18/18；失败集合与 main（f928a513）基线逐项 diff **完全一致（零新增失败）**。
- 本批在独立 worktree（batch/ol-xun-chen）开发后合并回 main。
