# RUNTIME V70 —— 规则 190（不变）/ 经典包 1.164.0

本版本号为伏皇后批次（求援 / 惴恐，一将成名 2013）使用。规则版本保持 `GameCheckpoint.CurrentRulesVersion = 190`，Checkpoint schema 仍为 3，技能 JSON schema 62 与 presentation schema 3 均不变——本批三项引擎增量都是**可选节点/可选主体**的放宽，同一内容指纹的命令回放语义对既有武将会一致，只有新内容才会走到新分支。经典包 `1.163.0 → 1.164.0`。

## 增量一：selfOnly 目标限定可以点名主体

`TurnCardTargetRestriction`（`src/CardGame.Core/CardUseModules.cs:102`）在既有 `TargetSeat` 之后追加可选 `SubjectSeat`：

- 匹配点 `HasTargetRestriction` 由 `item.Source.OwnerSeat == actorSeat` 改为 `(item.SubjectSeat ?? item.Source.OwnerSeat) == actorSeat`，即所有历史授予（`SubjectSeat` 为 null）继续绑定授予帧的拥有者。
- `GrantTargetRestriction` 记录并比较 `SubjectSeat`，键冲突语义不变；状态自检把「selfOnly 不得携带 TargetSeat」与「SubjectSeat 非负」并列。
- `GameEngine.CardUseModules.cs` 的 `GrantProgramTurnCardTargetRestriction`：非 selfOnly 仍要求 `targetSeat != frame.OwnerSeat`（旧不变量保留），selfOnly 现在允许 `targetSeat` 是他人座位，此时把该座位写入 `SubjectSeat` 而不写 `TargetSeat`。
- AI 侧 `ProgramCompositionAi` 原先只在 `target == Owner` 时把「本回合不能再杀他人」计入评估，现保持保守：被点名主体的分支由内容自身承担，不猜测他人座位。

用法：惴恐赢面把「只能指定自己为目标」钉在**回合角色**身上（`grantTurnCardTargetRestriction(target=selectedTarget, selfOnly)`），而不是技能拥有者。

## 增量二：hasOwnedCardCategory 支持精确牌面过滤

选项条件 `hasOwnedCardCategory` 原本只接受 `cardCategories`（basic/trick/equipment），无法表达「手里有一张【闪】」。本批把已存在的 `cardKinds` 节点接入该条件：

- 解析（`SkillPrograms.cs` 条件校验段）：`hasOwnedCardCategory` 现在要求 `cardCategories` 与 `cardKinds` 至少其一；`cardKinds` 仅 `boundCardsMatchKinds`/`hasOwnedCardCategory` 可用且必须去重；`cardCategories` 仍只归类别条件。
- 求值：`SkillProgramCondition.EvaluateOption` 的 `hasOwnedCardCategory` 委托增加第三个参数（牌面列表），判定收敛到新的 `MatchesProgramCardFilter(kind, categories, kinds)`——类别为空即不按类别筛，牌面为空即不按牌面筛，两者同时给出时取交集。
- 消费点全部同步：`GameEngine.ProgramChoices.cs`（人类/AI 选项提示与挂起后重算共 4 处）、`GameEngine.ProgramLifecycle.cs` 目标评分上下文、`ProgramCompositionAi.ProgramAiPublicContext` 字段类型。
- 支付面不需要改动：`selectAndMoveOwnedCard` 早已支持 `cardKinds`，`BuildOwnedCardPaymentChoices` 会按牌面过滤。

用法：求援的「交给你一张【闪】」选项仅在被求援角色手里真有【闪】时出现，且支付步只允许选【闪】。

## 增量三：卡牌角色变更可由「唯一指定目标」驱动

`addCurrentCardUseTarget` 原先要求帧拥有者是用牌者（赞绘口径：`action.ActorSeat == frame.OwnerSeat` 且唯一指定目标）。本批在 `GameEngine.ProgramCardUseRoles.cs` 抽出 `IsProgramCardUseRoleOwner(ownerSeat, action, ownerMayBeTarget)`：

- 用牌者本人继续可用（赞绘不变）。
- `ownerMayBeTarget: true` 时，允许帧拥有者是这张牌的**唯一指定目标**且该牌属于【杀】系（slash/fireSlash/thunderSlash）。该开关只对 `AddCurrentProgramCardUseTarget` 打开；`ReplaceCurrentCardUseActor` 仍走严格 actor 分支，目标不能篡改成用牌者。
- 候选集合 `GetProgramCardUseRoleTargets(ownerSeat, context)` 同步放宽，`otherLegalCurrentCardTarget` 因此对被盯住的目标角色也会返回「这张【杀】还能合法吃下的其他角色」（仍排除已指定目标、仍受 `CanBeProgramCardUseRoleTarget` 的杀资格与距离约束）。

## 惴恐时点取舍

官方页文本为「其他角色的**准备阶段**」，BWIKI 经典条目作「回合开始时」。引擎的 `turnOwnerScope` 白名单（`SkillPrograms.cs:2277`）只接受 turnEnding / playEnding / playPhaseStarting / discardPhaseEnded / judgmentPhaseStarting，`turnStartBeforeNormalFlow` 不在其中（该窗口的候选只按回合拥有者生成）。本批取 `playPhaseStarting` + `turnOwnerScope = otherLiving`：

- 效果域一致——两支授予都是回合域（`grantTurnCardTargetRestriction` / `grantDirectedTurnCardPolicy` 均为「直到回合结束」）。
- 差在拼点提示的时点：判定阶段的中途死亡/翻面等边角不再先拼点后判定。准备阶段本身不产生「指定目标」的用牌行为，因此「只能指定自己为目标」的实际约束区间不变。
- 若后续要把 `turnOwnerScope` 开放到回合开始窗口，需要引擎补该窗口的他回合候选生成，属独立增量。

「该角色与你的距离视为1」以 `DirectedTurnCardPolicyEffect.IgnoreDistance`（actor=回合角色、target=拥有者）落地：目标资格上「距离恒为 1」与「对你无距离限制」收敛（杀/顺手/过河/兵粮的距离门槛都 ≤1 即可），差异只在以距离数值本身为收益的结算，本批记录该口径而不新增距离覆写原语。

## 验证入口

`tests/CardGame.Core.Tests/FuHuanghouChecks.cs`（runner 名称 `fuhuanghou*`）：定义与拒绝面、求援两支（交【闪】/成为额外目标）、惴恐两支（赢面钉人／没赢开距离），均含暂停点与整回合的 checkpoint↔replay 等值断言。
