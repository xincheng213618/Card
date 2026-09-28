# Runtime v58：归属标记计数、击杀者事实与额外回合

本批消费方为神司马懿（神话再临-山，2010，神4体力）。统一 schema62、最低规则177（`Replay.CurrentRulesVersion`）、经典包1.147.0；Checkpoint格式3不变。历史内容不改写，新入口只对声明了最低规则177及以上的内容开放。工作区内另一对话的被动伤害/体力程序批次（HpChangePrograms 等）与徐盛、张松改动未纳入本批验证，源码文件互不覆盖。

## 归属标记计数

新增触发值 `ownerAttributedMarkerCount`（事实 `OwnerAttributedMarkerCount`，值23）：持有者身上某一种归属标记（marker 必填）的数量，供触发条件 `compare` 的左值使用。解析器要求：该值必须带 `marker`、不得带 `value`/`zone`；配套约束 `changeAttributedMarker` 的 `targetRef` 必须自指持有者（owner），对其他目标直接拒收，保证“归属给谁、只能谁读写”。

## 击杀者事实与可选击杀询问

新增触发条件 `deathKillerIsOwner`（事实 `DeathKillerIsOwner`，值19）：`characterDied` 窗口内冻结“死者是否死于持有者”的击杀归属事实，仅允许声明在 characterDied 窗口，解析器对其他窗口拒收。配套修复：`GetPendingProgramTriggerCandidate` 此前不识别 `ProgramKillTriggerWindowFrame`，人类玩家的可选击杀触发（激活/跳过询问）会因“无父触发候选”而无法作答；现按 `Candidates[CandidateIndex]` 直接回填触发候选与上下文。

## 额外回合

新增效果操作 `pendExtraTurn`（`ProgramExtraTurnOperations.cs`）：在活动技能帧内为持有者挂起一个额外回合。约束与语义：

- 仅在活动帧内有效（帧归属/技能不符直接抛错）；持有者死亡或已分胜负时静默忽略。
- 挂起即入日志与 `ProgramExtraTurnPendedEvent`，额外回合推迟到当前回合结束（`FinalizeEndTurn` 消费 `_pendingExtraTurnSeat`），对应官方FAQ“直到该回合结束额外回合才开始”。
- 挂起槽位单一：同一回合内重复挂起只保留一个，对应官方FAQ“一回合内杀死两名角色只获得一个额外回合”。
- 额外回合内再击杀可继续挂起（槽位在回合收尾清空后可重新写入），对应官方FAQ“额外回合内杀死角色还能继续发动连破”。

AI 语义 `PendExtraTurn` 计正向估值（+20），使 AI 倾向激活击杀后额外回合。

## 消费方：神司马懿“忍戒 / 拜印 / 连破”

- 忍戒（锁定）：`afterDamageApplied`（perDamagePoint）每点伤害 +1“忍”；`cardsMoved`（perCard，sourceZones 手牌、movementReasons 仅 `rule.hand-limit-discard`）每张弃置手牌 +1“忍”。归属标记经 `changeAttributedMarker` 写在持有者身上并随 `PlayerMarkerChangedEvent` 可观测。
- 拜印（觉醒，usageScope game / usageLimit 1）：`turnStartBeforeNormalFlow`，条件 `compare(ownerAttributedMarkerCount("ren") ≥ 4)`，先 `changeMaximumHp -1` 再 `grantSkills [classic:lianpo]`，伴随 `SkillAwakenedEvent`；正常回合与额外回合一视同仁。
- 连破（可选）：`characterDied` + `deathKillerIsOwner`，激活后 `pendExtraTurn`。

## 验证口径

定向检查 `tests/CardGame.Core.Tests/ShenSimaYiChecks.cs`（7项，自然命令与真实决策应答）：

1. 定义与击杀窗口schema：注册表存在 classic:shen-sima-yi / 三技能、神4体力、进入 identity:classic-5 池；忍戒/拜印/连破的关键触发属性断言；四类schema拒收样例（无 marker 的 ownerAttributedMarkerCount、归属标记改写指向非持有者、deathKillerIsOwner 声明在 characterDied 之外、pendExtraTurn 指向 selectedTarget）。
2. 受伤与弃牌都记“忍”：回合内挨打后标记增量与伤害增量一致。
3. 手牌上限弃牌记“忍”：弃牌询问逐张触发 `PlayerMarkerChangedEvent`（每张 +1）且移动原因均为 `rule.hand-limit-discard`。
4. 四枚“忍”觉醒：`SkillAwakenedEvent`（体力上限-1、获得连破）与快照技能一致。
5. 击杀获得恰好一个额外回合并回放：击杀 → 激活连破 → 次回合仍为击杀者、再下一位恢复正常轮转；Checkpoint 还原后事件与状态全等。
6. 拒绝连破保持正常轮转。
7. 额外回合内再击杀可连锁：500种子批内连弩+残血目标两连杀，额外回合数与轮转逐项断言。

验证结果：定向7/7通过；当时冻结 Core 全量 551/552——唯一失败为经典庞德“孟津”系并行批次的既有回归（在其 HEAD 上即失败），非本批引入。WPF：图鉴 god 组新增神司马懿，缺立绘断言不再报告 `classic:shen-sima-yi`（官方 gid 208，默认皮肤120801入 general-art-catalog，3皮肤可切）；同断言剩余 xu-sheng 属另一并行批次未交付。`tools/sync_general_art.py --phase verify` 对全目录校验会命中既有 zhang-song JPG 条目（本批之前的遗留），非本批引入。

## 边界与未覆盖

- 官方“拜印”觉醒奖励为“极略”及其五个借用药技（鬼才/放逐/完杀/制衡/集智），其中完杀、放逐、集智尚未收录于经典内容库；本批将连破并入觉醒奖励并在来源记录中注明适配，见 [shen-sima-yi-2026-09-27](../content/sources/shen-sima-yi-2026-09-27.json)。
- 连破采用“杀死即挂起”的时点（官方现行文本为“回合结束时检查本回合是否杀死过角色”）；常见单回合结算下行为一致，回合内转移击杀归属的组合未逐一专测。
- 忍戒的“忍”标记当前只被觉醒条件消费；极略类“移去标记换技能”入口随上述适配暂不提供。
- AI 估值（连破 +20）只为让 AI 击杀后稳定激活，不宣称最优。
