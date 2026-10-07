# 黄承彦单武将批次

当前 OL 黄承彦（群势力、3 体力、称号捧月共明，官网 481 号）接入：解阵（出牌阶段限一次，你可以令一名其他角色将其所有技能替换为"八阵"（锁定技、限定技、主公技除外），你的下个回合开始时或当其发动【八卦阵】进行判定后，其失去"八阵"并获得原技能，然后你获得其区域里的一张牌）、择才（限定技，一轮游戏结束时，你可以令一名其他角色获得技能"集智"直到下一轮游戏结束，然后若其是本轮使用锦囊牌数唯一最多的角色，其执行一个额外的回合）、隐世（锁定技，①当你每回合首次受到无色牌或非游戏牌造成的伤害时，防止此伤害；②当一名角色发动【八卦阵】进行判定时，你获得其生效的判定牌）。官方立绘 48100（750×950）已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-huang-cheng-yan-2026-10-08.json)。

| 技能 | 实现口径 |
| --- | --- |
| 解阵 | playPhaseStarting 可选触发（窗口每出牌阶段一次）→ selectTarget otherLiving → 新 op `jiezhenReplaceSkills`（7212）：枚举目标当前全部已启用内容技能，按注册表 `SkillTag` 排除锁定技/限定技/主公技，逐 grant 置为停用，再以 `acquire` 运行时授权授予既有内容技能 `classic:bazhen`（"八阵"：锁定技，若你的装备区里没有防具牌，视为你装备着【八卦阵】，与 BWIKI 八阵词条逐字一致）；转换记录保存在引擎内注册表（目标座位 → 拥有者/被替换技能/八阵 grant id），以提交事件 `ProgramJiezhenConvertedEvent` 为审计事实，冷恢复随命令重放重建。恢复用同一 op `jiezhenRestoreSkills`（7213）挂两个触发：turnStartBeforeNormalFlow（subject owner，即拥有者下个回合开始）与 judgmentFinalized（subject any + judgmentReasons `equipment.bagua-defense`，即其发动【八卦阵】判定后）：重新启用被替换技能的 grant、移除八阵 grant、清空注册表，然后按既有跨区域随机口径获得其区域里的一张牌（手牌/装备/判定区全体均匀随机，走处理区两跳并开放 cards-moved 窗口）。 |
| 择才 | turnStartBeforeNormalFlow 强制触发 → 新 op `zecaiRoundSettlement`（7214）：以"上一轮编号 = 当前轮 − 1 且 ≥1、且本轮尚未结算"为门（引擎轮推进 `BeginRoundForTurn` 在回合开始时发布 `RoundStartedEvent`），先清理过期轮授权（`acquired:round:{N}:…` 前缀 grant，N 小于当前轮即移除，对应"直到下一轮游戏结束"），再以提交事件判 限定技 未用后弹出自定义决策：逐存活其他角色 + 放弃；选中即以 `acquired:round:{当前轮}:…` 授权既有内容技能 `classic:jizhi`（"集智"：当你使用普通锦囊牌时，你可以摸一张牌）并记 `ProgramZecaiSkillGrantedEvent`（限定技消耗的唯一事实）；随后按上一轮锦囊使用统计（见共享能力）判定唯一最多者，若恰为所选角色则以既有通用 `PendProgramExtraTurn` 令其执行额外回合。 |
| 隐世 | 锁定技，两个强制触发：①beforeDamageApplied（subject damageTarget）→ 新 op `yinshiPreventSourcelessDamage`（7215）：伤害尝试无来源游戏牌（`IsSourceless` 或 `Card == null`）且本回合尚无该技能防止事实（提交事件 `ProgramYinshiDamagePreventedEvent` 携带回合号）时，走既有 `PreventProgramCurrentDamage` 防止路径（含标准 `ProgramDamagePreventedEvent` 与日志）；②judgmentFinalized（subject any + judgmentReasons `equipment.bagua-defense`）→ 新 op `yinshiClaimBaguaJudgmentCard`（7216）：生效判定牌仍在判定者判定区时移入拥有者手牌并记 `ProgramYinshiJudgmentClaimedEvent`，与既有 `claimJudgmentCard` 同构但解除"判定对象必须是自己"的限制。 |

## 共享能力扩展

- `SkillProgramDependencies.UsesRoundTracking` 增加 `zecaiRoundSettlement` 触发 op：内容包出现该 op 时即开启引擎轮推进（`BeginRoundForTurn`/`RoundStartedEvent`/`_skillRuntimeState.ResetRound`），不依赖谋攻篇正式模式。
- 新增每轮锦囊使用统计：`ProgramRoundTrickUsedEvent`（轮号/座位/动作 id/牌种）由 `ObserveRoundTrickUse` 在事件推进管线中记录（与既有 `ActualTurnTrickUseRecordedEvent` 同构，含无懈可击响应使用、按动作 id 去重），仅当内容包包含 `zecaiRoundSettlement` 时启用。
- `AI` 挂起决策分派增加 `ZecaiRoundSettlement` 用例与 `zecai-target`/`zecai-decline` 决策路由；AI 优先选择标记为上一轮锦囊唯一最多者的目标，否则放弃（计数为公开信息，随选项参数携带）。

## 边界口径

- 解阵"其所有技能"按注册表元数据执行：`SkillTag.Locked/Limited/Lord` 之外的全部已启用内容技能（含运行时获得的技能）停用并恢复；觉醒技按文本不在排除列（会被替换）；元数据缺标注的技能按无标签处理。
- 解阵恢复时按"被替换技能 id"重新启用该角色当前停用的对应 grant；若替换期间第三方停用/移除了同一技能的 grant，恢复会一并重新启用（以解阵记录为准，不追溯第三方意图）；转换期间目标死亡时恢复仍执行（技能 grant 恢复，取牌自然跳过）。
- "获得其区域里的一张牌"沿用跨区域随机口径：手牌（暗置，随机）+ 装备区 + 判定区全体候选均匀随机，同突袭/冲阵隐私口径；区域为空时该步自然跳过。该取牌与隐世②发生在同一次【八卦阵】判定结算时可能先后都发生（两张技能各自成立），取牌若先取走判定牌，隐世②对该判定自然跳过。
- 择才"一轮游戏结束时"实现为"新一轮开始后的拥有者首个回合开始时"结算（引擎无轮结束钩子；轮推进发生在开启新一轮的回合开始处）：结算数据为刚结束一整轮的完整统计，时机最迟延至拥有者回合开始；额外回合插入在拥有者当前回合之后。限定技"未用"以 `ProgramZecaiSkillGrantedEvent` 存在性为准，放弃不消耗。
- 择才"直到下一轮游戏结束"：授权在结算所在轮授予（源 `acquired:round:{N}:…`），在 N+1 轮开始后的首次结算点移除；与文本的"授予于轮结束、存续至下一轮结束"在存续区间上等价。
- 择才"本轮使用锦囊牌数唯一最多"按普通锦囊与延时锦囊的全部锦囊使用计数（含无懈可击响应使用，按动作去重）；全体 0 计或并列最多时无唯一最多者，不执行额外回合。
- 隐世①"无色牌或非游戏牌"：本规则集所有游戏牌均带花色（无无色牌正实例），可表达口径为"伤害无来源游戏牌"（`IsSourceless` 或无源牌，含技能伤害等）；【闪电】等延时锦囊伤害携带源牌，不防止；每回合首次以携带回合号的提交事实计数。
- 隐世②覆盖任意角色（含黄承彦自己）的【八卦阵】判定（护驾/普通八卦阵共用 `equipment.bagua-defense` 判定原因）；判定牌已被改判/移走时静默跳过。

## 验证

- Release 全解决方案构建 0 error；20 条 warning 全部位于既有文件（`SkillProgramExecutor.cs`、`GameEngine.ActualHandGainPrograms.cs`、`GameEngine.EndingHistoricalUses.cs`、`GameEngine.LiangXingPrograms.cs`、`GameEngine.OwnedDeathBenefitReturns.cs`、`GameEngine.PublicPilePreparation.cs`、`GameEngine.SameTypeActualUseAid.cs` 与既有测试文件），本批新增文件无 warning。
- 内容包静态加载冒烟：定向 `--filter` 跑既有 "classic identity applies base HP, multiple skills and legacy replay boundaries" 1/1 通过（该检查完整构建 StandardClassicGeneralPackage，覆盖本批 rules/presentation JSON 的 schema 校验与注册）。
- 例行 `tools/Test-Changed.ps1`（无过滤，分支 5b47fd15+本批内容，实测耗时 145 秒）：Core 266 通过 / 57 失败 / 0 跳过（323 项），WPF 18/18 通过。失败名单与 yan-jun 批在同源修复基线上实测的 57 项失败逐项 diff 完全一致（57=57，通过集合亦一致），无新增失败；基线失败属 aed24e70 批次自身在制状态，非本批引入，逐项核对留待协调者合并时处理。
- 按用户指令本批不含新增行为检查：未新建测试文件、未修改 tests/、未注册检查。
