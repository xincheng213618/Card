# RUNTIME V68 —— 规则 191 / 经典包 1.162.0

本版本号为陈宫批次（明策 / 智迟）使用。Checkpoint schema 仍为 3；规则版本 189 → 191（190 段为并行批预留，本批在其基线之上独占递进），经典包 1.160.0 → 1.162.0（1.161.0 为并行批预留）。技能 JSON schema 62 不变（本批全部为加值节点）。旧规则的检查点按现有版本边界拒绝恢复。

## 陈宫批次：明策 / 智迟

本批为单武将交付：陈宫（明策/智迟，一将成名2011，群 3 体力，最低规则 191）。新增公共能力两项；智迟全部由既有能力承载，零引擎扩展。

### 新增公共能力

1. **操作 `useDesignatedVirtualSlash`**：新 `SkillProgramEffectOp`。语义：让**当前选中目标**（获牌角色）在其攻击范围内合法目标中被技能拥有者指定一名后，选择是否视为使用一张无实体牌的【杀】攻击该名角色，结果回填到 `resultBind` 绑定（`used-slash`/`declined`）。描述符约束：目标必须 `selectedTarget`（程序帧的单一选中目标即获牌者）、`resultBind` 必填、禁止效果条件（RequireAlways）；资源契约 `ReadSelectedTarget` + `CreateChoiceResult[used-slash, declined]`。引擎实现为三段交互：候选为空直接记 `declined` 并续接（兜底效果可达）；否则向拥有者发必答指定提示（program-action `designated-slash-victim`，选项携带 `victim-seat`），指定以 `{resultBind}-victim` 绑定（optionId `seat-{n}`，chooser=拥有者）回填后向获牌者发使用/放弃提示（`designated-slash-use`/`designated-slash-decline`）；使用走标准 AttackResolution 管线（CardUseFrame + CardUseDeclared + TargetsConfirmed + `AttackResolution(card: null, playedCardKind: Slash, programSkillCardUseFrameId)` + `ContinueSlashAfterResponsePrograms`），响应/无懈/伤害/续接与 `CompleteAttackAfterCardResolution` 的程序回接全部复用既有机制。程序帧一致性臂按双绑定形态扩展校验（生产者 op、选项形状、chooser 座位）。AI 臂：指定阶段敌意评分；应答阶段先以新增 `SimpleAi.ScoreHostility` 读公开敌意——敌意目标进入评分（使用恒优于放弃），盟友目标确定性放弃。
2. **激活级卡牌并集过滤 `cardCategories`/`cardKinds`**：`SkillProgramActivation` 新可选节点。语义：激活的可选牌在“任一声明类别或牌种”上匹配即可（效果级 selectAndMoveOwnedCard 的同名字段保持相交语义不变）。约束：声明即非空（空数组拒收）；仅接受朴素选牌——与 `selectedCardsSameSuit` 混用或 `maxCards=0` 时拒收。

### 消费方

- 明策：activation `advise`（minCards/maxCards 1、sourceZones [hand]、cardCategories [equipment]、cardKinds [slash, fireSlash, thunderSlash]、targetKind otherLiving、usesPerTurn 1、condition ownTurn）；effects = giveSelected(selectedTarget) → useDesignatedVirtualSlash(selectedTarget, resultBind `designated-slash`) → draw(selectedTarget, 1, choiceIs(designated-slash/declined))。官方“（若无则不选择）”的空候选路径由操作内 declined 直记承载，兜底摸牌随之可达。
- 智迟（锁定技，零引擎扩展）：state `delayed-guard`（public/game/preserveUntilGameEnd）；trigger `arm-after-out-of-turn-damage`（afterDamageApplied、subject owner、perDamage、必发、not[ownerIsTurnPlayer] → setBooleanState true）；trigger `nullify-targeted-card`（cardUseBeforeTargetEffects、ownerRelation target、15 种牌清单=【杀】/火杀/雷杀+普通锦囊全表、必发、booleanState delayed-guard==true → nullifyCurrentCardEffect owner，贞烈模板同款）；trigger `clear-at-turn-end`（turnEnding、subject owner、turnOwnerScope otherLiving、必发、priority -100 → setBooleanState false，张松 reset 模板）。

## 共享检查加固（诚实归因）

- 明策 AI 应答臂首版对盟友候选也必然选“使用”（敌意评分含 +14 基线，恒高于放弃的 0 分），放弃分支不可达且不合常理：修正为应答阶段先经 `SimpleAi.ScoreHostility` 公开敌意判定，盟友确定性放弃。共享 AI 能力为最小加值，公开信息承载、确定性可回放。
- 激活卡牌过滤校验初版以“两数组皆空”判定，未区分“未声明”与“声明为空”，误拒全部既有技能（曹植苦禁 fixture 撞出）；改为按属性存在性逐项校验后既有技能零感知。

## 验证口径

定向检查 `ChenGongChecks`（3 项）全部通过；解析器拒收面以 raw-string 模板单行变换覆盖八项（空 cardKinds、与花色约束混用、目标非 selectedTarget、缺 resultBind、加效果条件、nullify 目标非 owner、afterDamage 缺 damageOccurrence、触发组模板加载）。明策夹具为公开 2v2 队伍模式（快照 TeamId 公开、敌意 ±100 确定性），按队伍读数分流敌意/盟友两个分支并断言绑定选项；智迟夹具为主公对四反身份模式，覆盖武装→无效化→回合结束清除全弧并做武装点检查点回放对比。Debug 全量 Core 653/653 全绿；Release 构建 0 error（剩余 2 条 CS8602 位于并行高大义郝批的 GaoDaYiHaoChecks.cs，基线既有）；WPF 按过滤器验证（gallery 合并过滤项通过，缺立绘名单为基线既有 8 名、不含 chen-gong）。批次记录见 [2026-09-30-chen-gong](../../benchmarks/2026-09-30-chen-gong.md)。
