# 徐盛（经典与界限突破）破军

经典徐盛（`classic:xu-sheng`）与界徐盛（`boundary:xu-sheng`）以同一破军扣置程序加一条界版专属伤害修正实现，技能程序见 `classic-xu-sheng.rules.json` 与 `boundary-xu-sheng.rules.json`，官方文本出处与版本边界见 [`sources/xu-sheng-2026-09-27.json`](sources/xu-sheng-2026-09-27.json)。

| General | Faction / HP | Implemented program | Notes |
| --- | --- | --- | --- |
| `classic:xu-sheng` | Wu / 4 | `classic:pojun`: optional `slashBeforeResponse` hold of up to the target's HP in hand/equipment cards, returned to the holder's hand at turn end | Play-phase-only via `cardActionActorIsCurrentTurn` + `cardActionPhaseIsPlay`; re-triggers per Slash target |
| `boundary:xu-sheng` | Wu / 4 | same hold, without the play-phase restriction, plus damage modifier `not-fewer-hand-and-equipment` (+1 when both Xu Sheng's hand count and equipment count are no fewer than the target's) | The official boundary version allows an out-of-turn Slash to trigger Pojun and returns held cards at turn end |

实现要点：

- 新增持久区 `CardZoneKind.PojunHold`（扣置在目标武将牌旁）；回合结束统一返还到目标手牌（装备牌也回到手牌而非装备区），持有者死亡时直接进入弃牌堆。
- 新增程序操作 `holdTargetCards`（`ProgramHoldTargetCardsOperationDefinition`）：一次性批量扣置、私密选择草稿、最少 1 张、冻结来源位置；技能校验器同时放行 `slashBeforeResponse` 窗口中 actor 关系引用 `eventTarget`（该窗口按单目标逐个结算）。
- 引擎修复：`CanRunProgramTrigger` 的窗口门控缺失 `slashTargetRedirecting` / `slashBeforeResponse` / `slashFullyDodged` 三个分支（bb150217 引入杀窗口时的遗漏），导致所有杀窗口触发（烈弓、铁骑、流离、猛进、耀武等）一律被跳过；本批按 `CardUse*` 分支同款形状补齐，并绑定 `_pendingAttack.TargetSeat == context.TargetSeat`。
- 经典版仅出牌阶段内发动；界版官网文本没有这一限制，回合外实际使用【杀】（如借刀杀人要求使用的【杀】）也可发动。同一出牌阶段可反复发动（方天画戟/青龙偃月刀追杀再询问）；X 以触发时目标体力计（伤害结算前）。
- 官方立绘暂缺，UI 使用透明画刷回退；补充时按惯例记录 URL 与 SHA-256。
- 规则版本 172 → 173，经典包 1.143.0 → 1.144.0；旧检查点因版本不匹配不再续玩，与历次武将新增一致。
