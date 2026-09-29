# RUNTIME V67 —— 规则 190 / 经典包 1.161.0

本版本号为法正批次（恩怨 / 眩惑）使用。Checkpoint schema 仍为 3；规则版本 189 → 190，经典包 1.160.0 → 1.161.0。技能 JSON schema 62 不变（本批全部为加值可选节点，`minimumRulesVersion: 190`）。旧规则的检查点按现有版本边界拒绝恢复。

## 法正批次：恩怨 / 眩惑

本批为单武将交付：法正（恩怨/眩惑，一将成名2011，蜀 3 体力，最低规则 190）。新增公共能力四项，另有一处共享描述符正确性修正与一处检查点不变式对齐。

### 新增公共能力

1. **冻结事实 `DominantForeignGainSourceSeat` + 触发条件 `gainedTwoPlusFromSingleOther`**：`SkillProgramTriggerConditionKind` 枚举尾加值 28；`SkillProgramTriggerFacts` 追加可空冻结事实；`CaptureCardsMovedTriggerFacts` 仅在 cardsGained 窗口捕获。语义：一次获得批次内，向拥有者手牌贡献最多匹配移牌的**单个他人来源**（并列取最小座位；须 ≥2 张才成立，否则为 null）。解析侧门槛：声明该条件的触发必须使用 cardsGained 窗口；eventSource 参与者白名单同批放开 cardsGained（其 SourceSeat 即该支配来源）。这是恩怨①"一次性获得一名其他角色的至少两张牌"的精确固化——非他人来源（摸牌）、多来源混批、单张获得均不成立。
2. **目标种类 `otherLivingRangeOrderedPair`（有序距离对）**：`SkillProgramTargetKind` 枚举尾加值 28。selectTargets 允许目标集白名单加入该值并要求恰好两个目标；候选枚举暴露全部有序方向对并按"第一位对第二位的可攻击距离"过滤（`GetCombatDistance(ordered[0], ordered[1]) <= GetAttackRange(ordered[0])`）；resolve 侧同步校验选中对仍合法（失配文案 "The selected range pair is no longer legal."）。既有 LivingPairDistinct/OtherLivingMale 的枚举语义逐分支保留。这是眩惑"令一名其他角色摸两张牌，并选择其攻击范围内的另一名其他角色"的承载——第二个选择依赖第一个，顺序选择会相互覆盖，故固化为一次有序对选择。
3. **`requestSlashByTarget` 的绑定响应者/受害者（responderRef/victimRef）**：描述符新增可选 `responderRef`/`victimRef` 参与者引用（victimRef 依赖 responderRef；responderRef 要求 owner 占位 + SelectedFirst/SelectedSecond；不声明时维持挑衅语义 target=selectedTarget 不变，完全向后兼容）。结算按 responderRef 解析"被迫用杀者"、按 victimRef 解析杀的目标；受害者死亡时技能剩余结算取消（沿用挑衅文案族）。提示语仅在受害者为拥有者本人时保留挑衅原文。choice 绑定不变（used-slash/declined）。这是眩惑第三步"令该角色选择：对你选择的角色使用一张【杀】"的承载。
4. **操作 `takeRandomCardsFromParticipant`**：新 `SkillProgramEffectOp`；描述符要求目标 owner、参与者引用必须是选中槽位（SelectedFirst/Second）、`zones` 白名单 hand/equipment、amount 1..20，条件自由（本批用 choiceIs 门控）。结算：从该参与者的声明区域内随机取牌**直接移入拥有者手牌**，全部所取牌跨一个原子移牌批次且保留来源区段归属（`ProgramRandomCardsTakenFromParticipantEvent` 仅发布座位/区域/数量，手牌不脱敏泄露）。组合 AI 估值 `TakeRandomCardsFromParticipant`（owner 摸入 + 他方调整）。"随机取牌"与神曹操批 `takeRandomCardFromEveryOtherCharacter`（每名固定一张）互补：本操作面向单个绑定参与者、固定多张。
5. **共享描述符修正（正确性，非新能力）**：`selectOwnedCards` 的 `CaptureSourceCard.OwnerHand` 静态标记此前忽略 `targetRef`——绑定 eventSource 选取的牌实为他人持有，误标 ownerHeld 会使后续 moveBoundCards 到 ownerHand 被组合校验拒绝（刚烈移向弃牌堆故未暴露）。修正为 `target == owner && targetRef is null && zones == [hand]` 才记 ownerHeld；运行时与组合 AI 路径本就按 targetRef 解析，既有内容零行为变化。
6. **检查点不变式对齐**：程序帧校验中 choice 绑定的 chooser 座位期望值此前按 `producer.Target` 解析；`requestSlashByTarget` 带 responderRef 时实际承诺者为响应者——校验臂按 TargetReference 优先解析，挑衅路径不变。

### 消费方

- 恩怨①：trigger `favor-for-gained-pair`，window cardsGained、subject owner、destinationZones [hand]、movementOccurrence perBatch、optional、condition gainedTwoPlusFromSingleOther；effects = draw owner 1 targetRef eventSource（来源摸一张）。
- 恩怨②：trigger `grudge-choice-after-damage`，window afterDamageApplied、subject owner、damageOccurrence perDamagePoint、optional、condition otherDamageParticipantAlive；effects = chooseOption（chooserRef eventSource、选项 give[handCountAtLeast 1]/loseHp[always]、optionLabels 必填）→ selectOwnedCards（targetRef eventSource、hand、amount 1、choiceIs give）→ moveBoundCards（sourceBind、ownerHand、choiceIs give）→ loseHp（owner、amount 1、targetRef eventSource、choiceIs loseHp）。
- 眩惑：trigger `replace-draw-with-ordered-pair`，window drawPhaseStarting、subject owner、optional、priority 100、drawPhaseMode replacement；effects = selectTargets（otherLivingRangeOrderedPair、2..2、targetAiOrder stable）→ draw owner 2 targetRef selectedFirst → requestSlashByTarget（owner、responderRef selectedFirst、victimRef selectedSecond、resultBind xuanhuo-answer）→ takeRandomCardsFromParticipant（participantRef selectedSecond、amount 2、zones [hand, equipment]、choiceIs declined）。

## 验证口径

定向检查 `FaZhengChecks`（3 项）全部通过；解析器拒收面以 raw-string 模板单行变换覆盖（favor 条件须 cardsGained、cardsGained 须 hand 目的地、afterDamage 缺 damageOccurrence、loseHp 参与者须事件参与者、chooseOption 选项须 optionLabels、有序距离对须恰两目标、绑定响应者须 owner 占位、绑定受害者须选中槽位、参与者取牌须选中槽位、区域白名单）。共享机制：skill executor 反射发现 5/5、composition kernel 13/13、刚烈回归 2/2。批次记录见 [2026-09-30-fa-zheng](../../benchmarks/2026-09-30-fa-zheng.md)。
