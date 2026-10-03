# 界祝融单武将批次

当前普通 OL 界祝融（蜀势力、4 体力、女性、史诗）以官方页当前文本接入：巨象（锁定技，【南蛮入侵】对其无效，其他角色使用的结算结束后获得之）、烈刃（使用【杀】造成伤害后可拼点，赢则获得其一张牌）、长标（出牌阶段限一次，任意张手牌当无距离限制的【杀】使用且计入次数，若造成伤害本阶段结束时摸等量张牌）。官方立绘 47900 已离线入库并登记图鉴/映射/同步脚本。来源见[来源档案](../content/sources/boundary-zhu-rong-2026-10-04.json)。

| 技能 | 实现口径 |
| --- | --- |
| 巨象 | 逐字复用 classic:juxiang 的卡牌策略对（`excludeGlobalTarget` 免疫 + `claimResolvedGlobalCard` 结算后获得）；引擎获得逻辑本就排除使用者本人（`SourceSeat != claimant.Seat`），与界版“其他角色使用的”文案一致。锁定技注册。 |
| 烈刃 | 触发体与 classic:lieren 一致：`afterDamageApplied`（subject damageSource、直接用牌伤害）可选发动，`startPindian` 后仅获胜分支 `selectSourceCard`（目标手牌+装备、暗槽驱动）+ `moveBoundCards` 获得之。 |
| 长标 | 激活体任意张手牌（minCards 1、maxCards null）经 `useSelectedCardsAs` 变量转换结算一张无距离限制的【杀（默认计入杀次数）；转换张数由 `accumulateSelectedCardCount` 记入 Phase 用量账本，伤害经 `sourceSkillId`+`sourceViewAsId` 过滤的触发置 PlayPhase 布尔状态，`playEnding` 以新表达式 `phaseSkillUsage` 摸等量张牌并显式清除状态。 |

## 共享能力扩展

- `viewAs` 新增两个可选节点，均为纯增量、互为前置：
  - `"inputCount": null`（变量输入）：仅允许 `forPlay`、无 `forResponse`、`outputKind=slash`、纯手牌来源、无类别/同花色/链式/租约等附加机制的直接转换；变量规则不进入 `GetProgramMultiCardViewAsSelections` 组合枚举（避免指数爆炸），由 `FindProgramVariableMultiCardViewAsSelection` 按实际选牌直接校验（区属/花色/禁用过滤与枚举路径一致）。激活定义校验要求 minCards≥1、maxCards 为全部、单手牌区。既有固定数量转换行为零变化。
  - `"distanceUnlimited": true`：仅变量转换可用；候选目标枚举与 `UseProgramSelectedCardsAs` 执行校验在距离子句上按规则放行（`CanUseVirtualSlashTarget` 新增默认 false 的 `ignoreDistance` 参数）。选择该表达而非 `grantDirectedTurnCardPolicy` 的原因：后者是整回合对指定目标的定向策略，会令后续普通【杀】也无距离限制，超出“此【杀】无距离限制”的官方语义。
- `draw` 新增表达式 `phaseSkillUsage`（枚举 1022）：必须携带 `sourceBind` 作为用量 id，经新编译指令 `PhaseSkillUsageProgramAmount` 读取拥有者该技能 Phase 范围用量账本（`accumulateSelectedCardCount` 在 Phase 范围内已持久化计数，且 playEnding 窗口先于阶段重置执行）。`Resources` 契约仅对 boundCardCount 读取卡牌集，phaseSkillUsage 不读卡集。
- 版本纪律：以上均为可选附加节点，`SkillProgramCatalog.RulesSchemaVersion` 保持 62、`GameCheckpoint.CurrentRulesVersion` 保持 193，未新增事实事件类型（`accumulateSelectedCardCount` 沿用既有 `SkillUsageConsumedEvent`）；新内容以 `minimumRulesVersion: 193` 与内容指纹区分。

## 验证

- 界祝融定向 7/7：定义与元数据（三技能注册、巨象锁定策略对、烈刃触发形状、长标变量转换/一次性/账本/状态/文案与激活标签）、巨象免疫并获得他人结算后的【南蛮入侵】、烈刃拼点获胜取牌（13 对 2 定胜）、烈刃平点不赢不取牌、长标无距离限制杀伤害后阶段结束摸等量 2 张（含主公体力 +1 口径）、被闪避不摸、同阶段第二次激活拒绝；关键步骤间做冷恢复与四视角一致性；实测 2.2 秒。
- 无过滤日常范围：Core 166/166（新增 7 项计入）、WPF 17/17，wrapper 实测 74.9 秒（含增量构建）。
- Full 全量：Core 556/556、WPF 56/56，wrapper 实测 262.0 秒（本 worktree）。

## 边界说明

- 主公体力按身份模式惯例 +1（4+1=5）；测试按该口径断言。
- 烈刃取牌为暗槽选择（`program-action=select-source-card`+槽位驱动），且目标同时支付拼点牌，断言按“目标手牌-2（拼点支付+被取）”口径；关键步骤间做冷恢复。
- 长标对 AI 可用（原生激活枚举提供全部可选手牌与无距离目标，AI 默认选最少张数），完整对局模拟覆盖该路径。
- 本批为单武将批次，在独立 worktree（batch/zhu-rong）开发后合并回 main；未触碰主区并行未提交工作。
