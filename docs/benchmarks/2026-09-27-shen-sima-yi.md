# 神司马懿批：归属标记、击杀挂起与额外回合

状态：神司马懿（神话再临-山，2010，神4体力）单武将交付完成。schema62 / 最低规则177 / 经典包1.147.0 / Checkpoint3；新内容只在1.147注册、声明最低规则177。本批为单对话交付；工作区内另一对话的被动伤害/体力程序批次（HpChangePrograms 等）与徐盛、张松改动未纳入本批验证，源码文件互不覆盖。

## 技能与官方口径

- 忍戒：锁定技，当你受到伤害后，或于弃牌阶段弃置手牌后，你获得X枚“忍”标记（X为伤害值或弃置的手牌数）。
- 拜印：觉醒技，准备阶段开始时，若你的“忍”标记数不小于4，你减1点体力上限，然后获得技能“连破”（适配：官方奖励为“极略”，见下文边界）。
- 连破：当你杀死一名角色后，你可以于此回合结束后获得一个额外的回合。

官方FAQ（bwiki 转录官方规则集问答）：额外回合内杀死角色可继续发动连破；一回合内杀死两名角色只获得一个额外回合；额外回合推迟到当前回合结束才开始；额外回合规则与正常回合相同。四条结论分别编码为挂起槽位的写入/消费语义，来源记录见 [shen-sima-yi-2026-09-27](../content/sources/shen-sima-yi-2026-09-27.json)。

## 公共能力

- `ownerAttributedMarkerCount`（触发值/事实）：持有者指定归属标记的数量，作为觉醒条件 `compare` 的左值；`marker` 必填，`changeAttributedMarker` 仅允许写向持有者自身（归属不变量）。
- `deathKillerIsOwner`（触发条件/事实）：characterDied 窗口冻结的击杀归属事实，仅限该窗口。
- `pendExtraTurn`（效果操作）：活动帧内为持有者挂起一个额外回合；`FinalizeEndTurn` 消费单一挂起槽位；持有者死亡或已分胜负时忽略；AI 语义 +20。
- 修复：`GetPendingProgramTriggerCandidate` 回填 `ProgramKillTriggerWindowFrame` 候选，使可选击杀触发的人类“激活/跳过”询问可作答。
- 神司马懿按既有觉醒窗口（`turnStartBeforeNormalFlow` + `changeMaximumHp` + `grantSkills`）与 `afterDamageApplied`/`cardsMoved` 观察窗口组合消费上述能力，无人物专用引擎分支。

## 内容与验证

内容：`classic-shen-sima-yi.rules.json`（忍戒/拜印/连破，revision 1，最低规则177）、`classic-shen-sima-yi.presentation.json`；`StandardClassicGeneralPackage` 注册武将（faction god、portraitKey shen_sima_yi、BaseHp 4，身份模式下主公 +1），版本 1.147.0，`CurrentGeneralIds` 追加 classic:shen-sima-yi。神势力武将开局走既有 `DecisionKind.SelectFaction` 私有势力选择询问。

定向检查 `tests/CardGame.Core.Tests/ShenSimaYiChecks.cs`（7项，自然命令与真实决策应答）：

1. 定义与击杀窗口schema：注册表、神4体力、identity:classic-5 池、忍戒锁定/拜印觉醒/连破可选的关键触发属性；四类schema拒收样例。
2. 受伤与弃牌都记“忍”：标记增量与伤害增量一致。
3. 手牌上限弃牌记“忍”：逐张 `PlayerMarkerChangedEvent` 且移动原因为 `rule.hand-limit-discard`。
4. 四枚“忍”觉醒：`SkillAwakenedEvent`（上限-1、获得连破）与快照一致。
5. 击杀获得恰好一个额外回合并回放：击杀回合 → 额外回合（同一玩家）→ 恢复正常轮转；Checkpoint 还原后事件与状态全等。
6. 拒绝连破保持正常轮转。
7. 额外回合内再击杀可连锁：500种子批内连弩两连杀，额外回合序列逐项断言。

验证结果：定向7/7通过；当时冻结 Core 全量 551/552，唯一失败为经典庞德“孟津”系并行批次的既有回归（在其 HEAD 上即失败），非本批引入。WPF：`GeneralGalleryCatalog` god 组新增 shen-sima-yi，缺立绘断言不再报告 `classic:shen-sima-yi`（官方 gid 208，默认皮肤120801为 `official-shen-sima-yi.png`，另2皮肤可切）；同断言剩余 xu-sheng 属另一并行批次。`tools/sync_general_art.py --phase verify` 对全目录校验会命中既有 zhang-song JPG 条目（本批之前的遗留），非本批引入。

## 边界与未覆盖

- 官方“拜印”奖励“极略”（鬼才/放逐/完杀/制衡/集智）因完杀、放逐、集智等借用药技未收录而未实现；连破并入觉醒奖励，详见来源记录的 versionBoundary。
- 连破“杀死即挂起”与官方现行“回合结束时检查”的时点在常见结算下一致；回合内击杀归属转移组合未逐一专测。
- “忍”标记当前仅供觉醒条件消费；极略类“移去标记换技能”入口暂不提供。
- 额外回合中拜印可继续觉醒（FAQ4）由通用觉醒窗口保证，未单列人物专测。
- 立绘经官方页面下载并记SHA-256；未做实机多DPI人工验收（与既有各批一致）。
