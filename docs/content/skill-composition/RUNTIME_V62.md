# RUNTIME V62 —— 张郃"巧变"与阶段跳过公共能力（规则 186 / 经典包 1.156.0）

规则版本 185 → 186：把 SkipTurnPhases 从"零消费者的管道能力"落为可用公共能力——新增弃牌阶段跳过与弃牌阶段开始窗口、有序双人组目标、跨角色对应区域移牌目的地四项公共能力（并行姜维批落 185，本批分层 186）。

## 新增公共能力

1. **阶段值 `discard` 与窗口 `discardPhaseStarting`**：
   - 语义：`SkillProgramTurnPhase` 增 `Discard`；`SkillProgramTriggerWindow` 增 `DiscardPhaseStarting`（弃牌阶段开始、弃牌结算前）。
   - SkipTurnPhases 白名单：`discardPhaseStarting` 仅允许 `[discard]`；`turnStartBeforeNormalFlow` 保持 ⊆ {judgment, draw}；`afterNormalDraw` 保持 `[play]`。
   - 引擎：`DelayedTurnEffects` 增 `SkipDiscardPhase`；`BeginDiscardPhase` 入口镜像摸牌阶段模式（先查跳过标志，再开 discardPhaseStarting 程序窗口，窗口内声明本回合弃牌跳过）——该入口同时覆盖"跳出牌阶段后直接进弃牌"路径（乐不思蜀/巧变出牌分支 + 弃牌分支连招）。
2. **有序双人组目标 `livingPairDistinct`**：
   - 语义：任意两名存活角色的有序组合（含技能拥有者自身，两名互异）；`SelectedFirst`/`SelectedSecond` 引用按选择顺序解析——首位为牌来源角色，次位为置入角色。
   - 解析器约束：minTargets = maxTargets = 2（镜像 `otherLivingPair`）。
3. **对应区域目的地 `selectedTargetCorrespondingZone`**：
   - 语义：`selectAndMoveOwnedCard` 的 `destination` 新值——将判定区/装备区牌置入 `targetRef` 指向角色的对应区域：判定区牌→其判定区，装备牌→其对应装备槽（槽占用时原装备入弃牌堆，镜像用牌装备替换语义）。
   - 解析约束：`targetRef` 必填；`zones` ⊆ {judgment, equipment}。
4. **（口径）阶段跳过日志**：`因【兵粮寸断】跳过…` 硬编码文案的处理见批次记录（若测试锁字符串则记录边界不改）。

## 消费方：张郃"巧变"

- 单技能四触发器，每分支各自"弃一张手牌 → skipTurnPhases"，每回合各限一次（官方允许一回合多次发动跳过不同阶段）：
  - 跳过判定：`turnStartBeforeNormalFlow` + `skipTurnPhases([judgment])`（引擎无判定阶段开始窗口；回合开始与判定阶段之间无其他效果时机，语义等价）。
  - 跳过摸牌+获得：`turnStartBeforeNormalFlow` + `skipTurnPhases([draw])` → `selectTargets(otherLivingWithHand, 1..2)` → `takeRandomHandCardFromSelectedTargets(1)`（每名各一张）。跳过声明落在回合开始——引擎 `SkipDrawPhase` 标志在 `CompleteTurnStart` 消费，先于摸牌阶段窗口，与兵粮寸断同机制；与官方"摸牌阶段开始时决策"的时点差仅影响判定结果可见性，不影响可观测牌面。
  - 跳过出牌+移牌：`afterNormalDraw` + `skipTurnPhases([play])` → `selectTargets(livingPairDistinct, 2..2)` → `selectAndMoveOwnedCard(cardOwnerRef selectedFirst, zones [judgment, equipment], destination selectedTargetCorrespondingZone, targetRef selectedSecond, skipIfNoCards true)`（移动为官方"你可以"，无可移牌时仅跳过阶段）。
  - 跳过弃牌：`discardPhaseStarting` + `skipTurnPhases([discard])`。
- 文本口径：一将成名站现行文本为主（"一至两名有手牌的其他角色"）；移动版详情页"至多两名"为实现超集，见来源 JSON versionBoundary。

## 验证口径

（待回填：定向检查项、构建与全量回归数字、提交态复核，以批次记录为准）

## 边界与未覆盖

- 判定/摸牌分支的决策时点在回合开始公共窗口（引擎阶段跳过标志的统一消费点所致），非官方的各阶段开始时点；效果等价性论证见上。
- 出牌分支的"对应位置"实现为判定区↔判定区、装备↔对应装备槽；跨槽转移（如武器置入防具槽）官方语义不存在，不适用。
- 准备阶段与结束阶段不可跳过（官方文本排除），白名单天然拒绝。
