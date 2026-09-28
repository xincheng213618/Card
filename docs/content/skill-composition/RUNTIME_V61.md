# RUNTIME V61 —— 邓艾"屯田 / 凿险 / 急袭"与沙摩柯"蒺藜"（规则 183 / 经典包 1.153.0）

规则版本 182 → 183：新增修正值表达式 `negatedOwnedZoneCount`、触发值/数值表达式 `cardsUsedOrRespondedThisTurn`（值 24）、数值表达式 `currentAttackRange`（值 11）、触发条件 `ownerIsTurnPlayer`（值 23）四项公共能力，并把 cardsMoved/cardsGained 窗口的能力位升级为 `Common | Judgment`（移动窗口内可声明判定效果）。本批另把 authority 区纳入 viewAs 可选来源区与"可打出的实体牌所在区"。

## 新增公共能力

1. **修正值表达式 `negatedOwnedZoneCount`（出向距离负修正）**：
   - 语义：修正值 = -（拥有者指定持久区当前牌数），用于"计算与其他角色的距离-X"。
   - 解析器约束：仅允许 `operation: add` 且 `query: outgoingDistance`；`valueZone` 必须是拥有者持久区（authority/woodenOxGrain/buquWound/chunlao），其余组合拒收。既有 `livingFactionCount` 的"仅主公技修正"限制对本表达式豁免。
2. **触发事实 `OwnerIsTurnPlayer` 与条件 `ownerIsTurnPlayer`**：
   - 语义：技能拥有者是否为当前回合玩家。"回合外"触发统一写 `{"kind":"not","children":[{"kind":"ownerIsTurnPlayer"}]}`（触发条件无独立 ownTurn/notOwnTurn 种类，与既有 All/Any/Not 组合子一致）。
   - 事实捕获：`CaptureProgramTriggerFacts` 统一填充（`owner.Seat == _currentSeat`）。
3. **触发值 `cardsUsedOrRespondedThisTurn`（值 24）**：
   - 语义：本回合开始以来，该玩家使用牌的次数与打出牌的次数之和（含当前这张）。使用按 `CardUseDeclaredEvent.SourceSeat` 计数，打出按 `CardActionAcceptedEvent`（`Action.Type == Response` 且 `ActorSeat` 匹配）计数，起点为本回合 `TurnStartedEvent`。
   - 计数入口：`GameEngine.SkillPrompts.CountCardsUsedOrRespondedByPlayerThisTurn`；配合 `currentAttackRange` 的比较承载"第X张牌"语义。
4. **数值表达式 `currentAttackRange`（值 11）**：
   - 语义：触发时点的实时攻击范围（基础1+武器+技能修正）；`draw` 的 `numberExpression` 白名单新增本表达式（`DrawProgramCards` 优先取窗口事实，缺省回退 `GetAttackRange`）；`compare` 条件亦可引用。
   - 窗口白名单：`currentAttackRange` 的比较仅允许 cardUseCommitted / cardResponseAccepted / slashBeforeResponse（既有 EventTargetHandCount 的"杀响应边界"限制保持不变，二者分叉校验）。
5. **cardsMoved / cardsGained 窗口能力位升级**：`ProgramEntryCapabilities` 由 `Common` 升为 `Common | Judgment`——移动窗口内可声明 startJudgment（屯田在移牌窗口内判定）。判定效果仍遵守既有"resultBind 必须在同一效果序列内被消费"的组合校验。
6. **viewAs 来源区收 authority；snatch 输出牌仅限主动使用**：`ParseViewAs` 的 sourceZones 接受 authority；`outputKind: snatch` 仅允许 `forPlay: true, forResponse: false` 且单张输入，双违皆拒收。`GameEngine.CardConversions` 把 authority 解析为 viewAs 来源区；`FindOwnedPlayableCard` 纳入 authority 区（合法性仍由 BuildLegalActions 的转化过滤决定，非转化牌依旧不可出）。

## 消费方：邓艾"屯田 / 凿险 / 急袭"、沙摩柯"蒺藜"

- 屯田（可选，三触发器同构）：`cardsMoved` × sourceZones [hand] / [equipment] / [judgment]，均 perBatch + `ignoreOwnSkillMovements` + 非 OwnTurn 条件；手牌触发器另带 `excludedMovementReasons: ["card.use","card.respond","card.respond.nullification"]`（自己用牌与打出的移动不算"失去"）。效果链 `startJudgment(skill.tuntian, public, resultBind tuntian-judgment)` → `filterBoundCards`（黑桃/梅花/方块 → tian-cards）→ `moveBoundCards(tian-cards → ownerPersistentZone/authority)` → `moveBoundCards(tuntian-judgment exceptBind tian-cards → discardPile)`（红桃判定牌入弃牌堆）。修正 `add(outgoingDistance, negatedOwnedZoneCount(authority))`。
- 凿险（觉醒，非可选，usageScope game / usageLimit 1）：`turnStartBeforeNormalFlow` + `compare(currentOwnedZoneCount(authority), greaterThanOrEqual, 3)`，效果 `changeMaximumHp(-1)` → `grantSkills([classic:jixi])`；零分支单受益，不使用 choiceGroup（choiceGroup 需 ≥2 分支，为钟会权等多分支觉醒保留）。
- 急袭：viewAs `{"sourceZones":["authority"],"inputSuits":["spade","club","diamond"],"outputKind":"snatch","forPlay":true,"forResponse":false}`；顺手牵羊的合法动作由引擎既有距离-1目标逻辑生成，实体牌为 authority 区的田。
- 蒺藜（可选，双触发器同构）：`cardUseCommitted` / `cardResponseAccepted` × `ownerRelation: actor` + `compare(cardsUsedOrRespondedThisTurn, equal, currentAttackRange)`，效果 `draw(owner, numberExpression currentAttackRange)`。

## 验证口径

定向检查 `tests/CardGame.Core.Tests/DengAiChecks.cs`（5项）与 `ShaMoKeChecks.cs`（3项），自然命令与真实决策应答、种子扫描 fixture（5人局、4名8体力标准none银行将、240张受控牌堆、回合内结束出牌以让 AI 拆顺触发屯田）：

邓艾：
1. 定义与触发schema：魏4体力、技能 [屯田,凿险]（急袭不在 AdditionalSkillIds，仅经凿险授予）、身份池；三移牌触发器窗口/来源区/perBatch/非OwnTurn断言、效果四步链断言、负距离修正断言、凿险觉醒属性断言、急袭 viewAs 断言；schema拒收样例（negatedOwnedZoneCount 配 hand 区、配 handLimit 查询、snatch viewAs 声明 forResponse）。
2. 屯田存田减距并回放：回合外 AI 拆/顺致失去 → 激活判定 → 非红桃判定牌入 authority 区且移动原因含技能 id；判定牌要么成田要么入弃牌堆（判定区→处理区链计数）；单田使基础距离2的对手实际距离变1；Checkpoint 还原后事件与状态全等。
3. 红桃判定不入田：扫描到屯田弃牌移动的种子，断言 authority 区为空且无田时距离不变。
4. 凿险觉醒并授予：3田后下一回合开始自动觉醒（上限4→3）、SkillAwakenedEvent 授予 [jixi]、田保留、回放全等。
5. 急袭转化出牌：觉醒后自己的出牌阶段出现 authority 牌的 Snatch 合法动作（conversionSource 技能断言）；提交后田离开 authority 区、目标牌数减少；回放全等。

沙摩柯：
1. 定义与触发schema：蜀4体力、单技能、身份池；双触发器窗口/ownerRelation actor/可选/条件（cardsUsedOrRespondedThisTurn equal currentAttackRange）/效果（draw currentAttackRange）断言；schema拒收样例（currentAttackRange 比较声明在 turnEnding、draw 同时给 amount 与 numberExpression）。
2. 首次使用摸1、同回合第二次不摸：无武器攻击范围1，第一张用牌（杀）后按技能原因恰一张入手，第二张用牌后不再增加；回放全等。
3. 回合外首次打出摸1：AI 回合对主公出杀，打出闪后蒺藜提示出现，激活后恰一张入手；回放全等。

验证结果：见批次记录（并行批次在制期间的最终复验数字以记录为准）。

## 边界与未覆盖

- 屯田的"失去牌"口径：以 cardsMoved 三触发器 + 排除自己用牌/打出/无懈响应承载；不覆盖"转换、交给、展示后收回"等特殊移动路径的逐一枚举（perBatch 语义一致处理）。
- 蒺藜计数含装备牌使用与虚拟牌打出；无懈响应计入打出计数，与官方"使用或打出"一致，未逐一专测。
- 急袭仅主动使用（官方文本"使用"）；不可作为打出响应（解析器拒收 forResponse）。
- 攻击范围为0的极端情形（无武器+减范围修正）下蒺藜永不触发（第0张不存在），语义自然成立，未专测。
