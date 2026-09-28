# Runtime v57：用牌观察距离、目标自指条件与受赠者用牌

本批消费方为经典曹昂（星火燎原-天府，慷忾）。统一 schema62 最低规则176、经典包1.146.0；Checkpoint格式3不变。历史内容不改写，新入口只对声明了最低规则176及以上的内容开放。

## 观察用牌目标时的距离事实

用牌类触发窗口新增冻结触发值 `ownerEventTargetDistance`（触发事实 `OwnerEventTargetDistance`）：对每个 (持有者, eventTarget) 候选，在 `CollectSharedCardActionCandidates` 捕获持有者到 eventTarget 的实际战斗距离（`GetCombatDistance`，含马匹修正）；eventTarget 缺席时为 `int.MaxValue`。仅允许声明在带目标的用牌触发窗口，解析器对其他窗口直接拒绝。与其他触发事实一致，条件求值读取冻结值，不重算当前局面。

新增触发条件 `cardActionTargetIsOwner`（事实 `CardActionTargetIsOwner`）：该次用牌的目标集合是否包含持有者本人，供“持有者自己成为目标”的分支与观察者分支互斥。窗口限制同上。

## 受赠者立即使用赠出的装备

新增效果操作 `useBoundCardByTarget`：让 `selectedTarget` 以标准用牌流程使用其手牌中一张已公开的绑定单牌。约束：

- 绑定卡集必须恰好1张且可见性为 Public（由 `selectAndMoveOwnedCard.destination: "selectedTargetHand"` 加 `resultBind` 产出）；
- 用牌者存活、该牌仍在其手牌区且为装备牌，否则取消技能剩余结算并清理挂起决策；
- 效果条件（如 `choiceIs`）先行筛除“保留”分支；
- 使用走 `BeginCardUse` + `MoveCard` + `CompleteEquipmentUse` 标准装备流程，并挂 `PendingMovementContinuation`：若有候选移动触发窗口则等待后代响应，没有则立即 `CompleteAwaitedProgramMovement` 兜底，防止帧悬死。

解析仅接受 `op`、`target`（必须 selectedTarget）、`sourceBind`、`condition`；资源声明为读 selectedTarget 与该卡集。AI 语义 `UseBoundCardByTarget` 对受赠者加正向估值（+8），使其倾向立即装备而非留手。

## 消费方：经典曹昂“慷忾”

两个触发共享 `cardUseTargetsFinalized` 窗口、【杀】系牌种与 optional：

- `self-targeted-by-slash`（ownerRelation=target，条件 `cardActionTargetIsOwner`）：只摸一张牌。对应官方FAQ“自己与自己距离为0、但不能交给自己牌”。
- `nearby-slash-target`（ownerRelation=observer，条件 `not[cardActionTargetIsOwner]` 且 `ownerEventTargetDistance <= 1`）：摸一张牌 → 公开转交一张手牌给 eventTarget（`revealBeforeMove` + `awaitMovementTriggers`）→ 受赠者二选一（装备牌时出现“使用”选项）→ `useBoundCardByTarget` 按选择执行。

## 验证口径

定向检查 `CaoAngChecks`（5项）：注册与schema拒收、距离1内赠八卦阵且受赠者AI选择装备并回放一致（Checkpoint 还原后事件与状态全等）、非装备赠牌不开使用选项、距离2不触发、自己被【杀】只摸牌。当时冻结 Full：Core 545/545、0警告0错误。WPF 立绘核对：`classic:cao-ang` 已由 general-art-catalog 的 cao-ang 条目解析（默认 official-cao-ang.png，8个官方皮肤），图鉴断言不再报告缺立绘；同断言中的 xu-sheng 属另一并行批次，未在本批处理。

未覆盖：慷忾与借刀杀人转移目标、多目标【杀】下多名观察者顺序、赠出的装备被【乐不思蜀】等时序干扰的组合未逐一做人物专测；这些走既有通用移动/用牌路径，未新增人物专用分支。AI 估值不宣称最优。
