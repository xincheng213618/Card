# RUNTIME V64 —— 规则 187 / 经典包 1.158.0

本版本号由两个并行批次共同使用：共享规则复核（I力场 / 对应区公示校验）与张昭张纮批次（直谏 / 固政）。Checkpoint schema 仍为 3；规则版本 186 → 187，经典包 1.157.0 → 1.158.0。旧规则的检查点按现有版本边界拒绝恢复。

## 共享规则复核（I力场 / 对应区公示）

复核界赵云、神赵云、高达一号、姜维和张郃后，修正两处共享规则语义。

- 高达一号的“I力场”使用新增 `preventIncomingTrickDamage` 策略，仅在拥有者是受伤目标时防止锦囊伤害。原有 `preventTrickDamage` 保持“无言”的来源方与目标方双侧语义。防伤事件携带实际技能 ID，战斗提示显示对应技能。
- `selectedTargetCorrespondingZone` 在公布可选牌时检查目标区域：判定区已废除、已有同种延时锦囊，或装备区已废除时，不提供无效移动选项；应答后再检查一次。判定区内转化牌按有效牌种判断，判定区到判定区的移动保留该牌种。

## 张昭张纮批次：直谏 / 固政

本批为单武将交付：张昭张纮（直谏/固政，神话再临·山 2011，吴 3 体力，最低规则 187）。新增公共能力三项，其余全部复用既有语汇。

### 新增公共能力

1. **生命周期窗口 `discardPhaseEnded`**：`SkillProgramTriggerWindow` 枚举加值；解析器 `isLifecycleWindow`/`supportsTriggerCondition` 双清单（SkillPrograms.cs）加值；`turnOwnerScope` 白名单（原仅 turnEnding/playEnding/playPhaseStarting）加值并更新拒收文案；`eventSource` 兼收（原文案 "damage-applied, judgment or discard-phase-ended trigger"）。引擎侧：边界帧校验要求留在弃牌阶段并入边界白名单；`CanRunProgramTrigger` 臂（拥有者非当前回合角色、存活、事件源=当前回合角色、阶段=弃牌）；`TryBeginDiscardPhaseEndedProgramWindow`——多拥有者候选按座位距弃牌角色最近优先，continuation `EndTurnAfterDiscardPhase`（弹出即 EndTurn，不落任何阶段替换白名单）；`ProgramEntryCapabilities` SupportsWindow/For（Common|Judgment）。挂接点恰两处：RunOneStep 弃牌分支 AutoDiscard 之后，与 SubmitDiscardCards 应答收尾（人类/AI 共用）；SkipDiscardPhase 直落 EndTurn 的路径不开窗（被跳过的弃牌阶段没有“结束时”）。
2. **操作 `restorePhaseHandDiscards`**：新 `SkillProgramEffectOp`，效果描述符要求 `chooserRef` 与 `phaseOwnerRef`（后者必须为 eventSource）。阶段弃牌集不引入水位线：按 `_cardMovements` 查询 `TurnNumber==当前 && From==Hand(弃牌角色) && To==弃牌堆 && Reason==rule.hand-limit-discard`，再交“仍在弃牌堆”过滤——既天然防跨回合误配，也防多名固政拥有者重复提供同一张牌。结算：提示逐张“交还给弃牌角色”选择+放弃；交还一张后其余该角色本阶段弃置的牌按 Id 序自动归拥有者（严格有利，官方“你可以获得其余的牌”固化为自动执行）；集合为空静默 Continue。AI 估值为 +4（保守中性），选择器在仅一张可交还时选放弃（纯送礼）。
3. **`selectAndMoveOwnedCard` 的 `prohibitReplacingEquipment`**：解析约束——仅允许“单一 hand 源 + cardCategories=[equipment] + targetRef=selectedTarget”形态；反向地，hand 源的对应区移动必须声明该标志。选择枚举过滤“目标对应槽已被同槽位装备占用”的牌（AI 路径天然安全）；执行期若公示牌的槽位已被占用，取消技能结算（文案“目标装备栏已有牌，不能替换原有装备”）而非替换。激活资格门 `HasFreeEquipmentSlotForOwnedHandEquipment`：拥有者手牌中存在任一“目标有空槽”的装备牌才放行激活。组合 AI 估值：owner→selectedTargetCorrespondingZone 形态计 `_targetDraw += 1`。与上一节的对应区公示校验合并落地：hand 源经 `CanMoveProgramCardToCorrespondingZone`/`ProgramCorrespondingZoneLocation` 放行，两种能力共用同一 publication 检查。

### 消费方

- 直谏：activation（无次数限制，usesPerTurn null）`equip-target`：minCards/maxCards 0、sourceZones [hand]、otherLiving 单目标；effects = selectAndMoveOwnedCard（chooser/cardOwner 均 owner，hand，equipment 类，selectedTargetCorrespondingZone，prohibitReplacingEquipment）+ draw(owner,1)。
- 固政：trigger `restore-at-discard-phase-end`：window discardPhaseEnded、subject owner、turnOwnerScope otherLiving、optional；effects = restorePhaseHandDiscards（chooser owner，phaseOwner eventSource）。

## 顺带根治（预存缺陷，非本批内容引入）

青龙偃月刀跟进杀的结算竞态：多目标杀（方天画戟/群体响应攻击）在先行目标被闪后发布青龙刀选择，外层 `CompleteAttack` 推进到下家目标并发布其响应窗口，跟进解析却无条件在其上再开一张新杀——曾以“Processing 滞留牌”不变量违规的形式在蔡文姬批后被观察到（诚实归因：彼时二分已确认与新技能无关）。修复两处：其一，`MultiTargetContinuationDefersQinglong`——后续还有存活目标时暂不提供青龙刀选择，刃链在最后一个目标上收口；其二，不变式通用尾部容忍“栈上活跃外层 CardUseFrame 所持的牌”（帧在栈上即证明持牌未泄漏；帧弹出后残留仍会失败）。该竞态曾致 Qingguo 检查 613/614 中唯一一败，修复后 614/614。

## 验证口径

定向检查 `ZhangZhaoZhangHongChecks`（3 项：定义与触发器 schema、直谏装槽交接+回放、固政还一张得其余+回放）全部通过；解析器拒收面以 raw-string 模板单行变换覆盖（禁替换标志的四条形态约束、phaseOwnerRef 必须 eventSource、turnOwnerScope 新窗口白名单文案）。批次记录见 [2026-09-29-zhang-zhao-zhang-hong](../../benchmarks/2026-09-29-zhang-zhao-zhang-hong.md)。
