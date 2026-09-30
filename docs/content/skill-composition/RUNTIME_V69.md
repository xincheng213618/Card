# RUNTIME V69 —— 规则 190（不变）/ 经典包 1.163.0

本版本号为神吕布批次（狂暴 / 无谋 / 无前 / 神愤）使用。规则版本保持 190，Checkpoint schema 仍为 3，技能 JSON schema 62 与 presentation schema 3 均不变——本批为**零引擎增量**，只做内容与注册；经典包 `1.162.0 → 1.163.0`。

## 神吕布批次：狂暴 / 无谋 / 无前 / 神愤

单武将交付：神吕布（神，男，5 勾玉，神话再临·林 2010，四技能最低规则均为 187）。OL 线 `ol:shen-lu-bu` 早已用现行原语实现同一套语义，本批把经典文本与经典 id 入册，`docs/content/sources/shen-lu-bu-2026-09-30.json` 记录官方页与 BWIKI 两处印刷口径的差异与取舍。

### 消费的能力（无新增）

1. `changeAttributedMarker` + `damageOccurrence = perDamagePoint`：游戏开始 `gameStarting` 建 2 枚【暴怒】，`afterDamageApplied` 以 `subject = owner` 与 `subject = damageSource` 两支各 +1，正是经典文本「造成或受到 1 点伤害后」。
2. `spendMarkerOrLoseHp`：描述符 `Interaction = Choice`，因此同一操作同时承载「弃 1 枚标记」与「失去 1 点体力」两选一（失去体力走 `ProgramSkillHost.LoseHp`，保留濒死链）。触发面为 `cardUseCommitted`（11 种非延时锦囊：决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻、铁索连环、借刀杀人）与 `cardResponseAccepted`（无懈可击），即经典文本的「非延时类锦囊牌」。
3. `markerCost` + `grantTurnSkills` + `grantTurnCardTargetRestriction`：无前以 2 枚标记为价，本回合授予 `classic:wushuang` 并令所选角色的防具失效；官方页口径为「直到回合结束」，与 `grantTurnSkills` 的作用域一致。
4. 参与者操作族 `damageParticipants` / `discardParticipantCards` + `turnOver` + `usesPerPhase`：神愤以 6 枚标记为价，对所有其他角色各 1 点伤害，随后各自弃尽装备区、弃四张手牌（不足则全弃），最后自身翻面。参与者游标按座位序环绕，弃牌数取 `Math.Min(amount, 手牌数)`。

### 目标资格的一处硬约束

无前文本作「选择一名角色」，本批取 `otherLiving`：引擎在 `GameEngine.CardUseModules.cs` 的 `GrantProgramTurnCardTargetRestriction` 中对目标限定型限制（非 `SelfOnly`）设有不变量——`targetSeat == frame.OwnerSeat` 直接抛 `InvalidOperationException`。BWIKI 经典条目本就写作「一名其他角色」，两口径在该不变量下收敛为同一可选集合。

### 消费方

- 狂暴 `classic:kuangbao`、无谋 `classic:wumou`：`SkillTag.Locked` + `SkillExecutionForm.Trigger`。
- 无前 `classic:wuqian`、神愤 `classic:shenfen`：`WithActiveActionMetadata`（无 限定技 标签——神愤是「出牌阶段限一次」，由 `usesPerPhase` 表达）。
- 武将 `classic:shen-lu-bu`，势力 id `god`，5 勾玉，入 `identity:classic` 武将池；图鉴入 `god` 组。

## 验证口径

- 行为语义由既有共享检查 `OlClassicGodChecks` 覆盖（暴怒初始化与每点伤害增减、无前付费与授予无双、银狮甲对技能伤害封顶、神愤全体伤害后先弃装备再弃四手牌并翻面、出牌阶段限一次、检查点-重放等价），按 AGENTS.md「纯配置消费既有能力不重复加检查」口径本批不新增定向行为检查。
- 共享门禁：`current classic catalogue modes and skill references`、`composition kernel descriptor contracts`、全量 Core 与 Release 构建。
- 入池副作用：见批次记录 [2026-09-30-shen-lu-bu](../../benchmarks/2026-09-30-shen-lu-bu.md)——两处既有夹具的脆面被池位移暴露，已按「不放宽断言」原则加固（`XuShuChecks` 的举荐边界改为引擎自身可观测的 restore 探针；`WushengResponseScenario` 的非互动发牌路径放宽搜索边界并注明原因）。
