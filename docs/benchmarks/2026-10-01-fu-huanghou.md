# 伏皇后批次：求援 / 惴恐（一将成名 2013）

- 日期：2026-10-01
- 批次：classic:fu-huanghou（伏皇后，群，女，3 勾玉）单武将
- 运行时：schema 62，规则版本 190（本批不上抬），经典包 1.164.0
- 状态：内容、注册、立绘、图鉴、三项引擎增量与定向检查已落地

## 官方文本与口径

来源：`docs/content/sources/fu-huanghou-2026-10-01.json`（官方武将库 gid 319 页面逐字提取，UTF-8 原文；客户端目录 `heroes.json` 二手核对群/女/3 勾玉与默认皮肤 131901；BWIKI 经典条目作二手旁证）。

- 惴恐：其他角色的准备阶段，若你已受伤，你可以与该角色拼点。若你赢，直到回合结束，该角色使用的牌不能指定除该角色外的角色为目标；若你没赢，直到回合结束，该角色与你的距离视为1。
- 求援：当你成为【杀】的目标时，你可以令另一名其他角色(不能是此【杀】的使用者)选择一项：交给你一张【闪】；或成为此【杀】的额外目标。

三处口径差异如实记录并给出取舍：

1. 惴恐时点——官方页「准备阶段」、BWIKI「回合开始时」，本批落 `playPhaseStarting` + `turnOwnerScope = otherLiving`。引擎的 `turnOwnerScope` 白名单（`SkillPrograms.cs:2277`）不含 `turnStartBeforeNormalFlow`，而该窗口的候选只按回合拥有者生成；两口径的效果域都是「直到回合结束」，准备阶段本身不产生「指定目标」的用牌行为，因此差异只在拼点提示的时点与判定阶段中途阵亡/翻面的边角。详见 `RUNTIME_V70.md`。
2. 求援选择权——BWIKI 把选择写在伏皇后一侧（「你可以选择另一名其他角色，除非其交给你一张【闪」），官方页明确由被求援角色二选一。本批按官方页用 `chooseOption(target = selectedTarget)` 承载，且支付选项只在对方手里确有【闪】时出现。
3. 距离口径——官方页「该角色与你的距离视为1」以 `DirectedTurnCardPolicyEffect.ignoreDistance`（actor=回合角色、target=拥有者）落地；目标资格上两者收敛（杀/顺手/过河/兵粮的距离门槛均 ≤1 即可）。

## 实现映射（最低规则 190）

| 技能 | 结构 |
| --- | --- |
| 求援 | `cardUseTargetsFinalized` + `ownerRelation = target` + 杀系三 kinds → `selectTarget(otherLegalCurrentCardTarget)` → `chooseOption(selectedTarget)`：`give-dodge`（条件 `hasOwnedCardCategory{zones:[hand], cardKinds:[dodge]}`）/ `become-target` → `selectAndMoveOwnedCard(chooser=cardOwner=selectedTarget, cardKinds:[dodge], destination=ownerHand, awaitMovementTriggers)` 或 `addCurrentCardUseTarget(selectedTarget)` |
| 惴恐 | `playPhaseStarting` + `subject = owner` + `turnOwnerScope = otherLiving`，条件 `all(ownerLostHp ≥ 1, currentHandCount ≥ 1)` → `selectTarget(eventSource)` → `startPindian(opponentRef = selectedTarget, public)` → 赢面 `grantTurnCardTargetRestriction(target = selectedTarget, selfOnly)`；没赢面 `grantDirectedTurnCardPolicy(actorRef = selectedTarget, targetRef = owner, [ignoreDistance])` |

## 引擎增量（三项，全部可选放宽）

1. `TurnCardTargetRestriction.SubjectSeat`：selfOnly 限定可绑定被点名的主体（惴恐钉住回合角色），历史授予回落 `Source.OwnerSeat`，行为不变；`GrantProgramTurnCardTargetRestriction` 对 selfOnly 允许 `targetSeat != frame.OwnerSeat`，非 selfOnly 的旧不变量保留。匹配点 `HasTargetRestriction` 与自检同步。
2. `hasOwnedCardCategory.cardKinds`：选项条件可按精确牌面过滤（求援要【闪】，类别词表 basic/trick/equipment 表达不了）；`cardCategories` 与 `cardKinds` 至少其一，`cardKinds` 仅 `boundCardsMatchKinds`/`hasOwnedCardCategory` 可用且须去重。求值委托统一走 `MatchesProgramCardFilter`。**拒绝文案沿用历史措辞**，因为既有定义检查按文案片段断言失败原因。
3. 卡牌角色变更放宽：`IsProgramCardUseRoleOwner` 允许「本牌的唯一指定目标」驱动 `addCurrentCardUseTarget`（牌须属杀系），`ReplaceCurrentCardUseActor` 仍仅用牌者可用；候选集合同步放宽，因此 `otherLegalCurrentCardTarget` 对被盯住的目标角色也返回「这张【杀】还能合法吃下的角色」。

## 检查

`tests/CardGame.Core.Tests/FuHuanghouChecks.cs`，runner 名称 `fuhuanghou*` 三条：

- 定义与拒绝面：注册/池/势力性别、求援四段效果与选项条件、惴恐四段效果与主体引用，外加三条组合期拒绝（cardKinds 落到非牌面条件、hasOwnedCardCategory 无过滤、牌面重复）。
- 求援两支：交【闪】→ 伏皇后手牌 +1 且无 `ProgramCardUseTargetAddedEvent`；拒绝→ 同一张【杀】的 `CardUseFrame.TargetSeats` 同时含伏皇后与被求援角色，并产生带 TargetSeat 的审计事件。
- 惴恐两支：赢面 `CardTargetRestrictionGrantedEvent` 的 `Restriction.SelfOnly`、`TargetSeat is null`、`SubjectSeat == 回合角色`、`Source.OwnerSeat == 伏皇后`；没赢面只有 `DirectedTurnCardPolicyGrantedEvent(actor=回合角色, target=伏皇后, IgnoreDistance)`。两支均在拼点结果处与回合收尾处各做一次 checkpoint↔replay 等值。

## 验证

（待填：Core 全量、WPF 全量、并行态归因。）

## 并行态与归因

（待填。）
