# 邓忠单武将批次

当前 OL 邓忠（魏势力、4 体力）以官方页当前文本接入：勘破（当你使用【杀】对目标角色造成伤害后，你可以观看其手牌并获得其中一张与此【杀】花色相同的牌。每回合限一次，你可以将一张手牌当【杀】使用）+ 更战（其他角色出牌阶段限一次，当一张【杀】因弃置置入弃牌堆后，你可以获得之。其他角色的结束阶段，若其本回合未使用过【杀】，你下个出牌阶段使用【杀】的限制次数+1）。官方立绘 51200 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-deng-zhong-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 勘破 | 拿牌：afterDamageApplied 可选触发（subject damageSource——伤害窗口的 owner 主体是受害者，沿奸雄；damageCardKinds 杀三态；damageSourceIsOwner/damageTargetIsOther/directCardUseDamage 条件）→ selectAndMoveOwnedCard 携带本批新增 `suitFrom: damageCard`：按伤害牌花色过滤事件目标手牌（执行期从窗口事实 DamageCardSuit 解析，skipIfNoCards）。视作：单牌 viewAs 规则（任意手牌当【杀】，allowSameKind，usesPerPhase 1 + usageGroup）——仅出牌阶段可用，故“每回合限一次”与“每出牌阶段限一次”等价，经 ConsumeProgramViewAsUsage 计数。 |
| 更战 | 领取：discardPileReceived 可选触发（movementDiscardOnly + usageScope turn/usageLimit 1——每名其他角色回合各一次；cardKinds 杀三态；phaseIsPlay + not ownerIsTurnPlayer）→ claimDiscardedEntityWithProvenance/offerFaceUpForOutsideClaims 成对（曹植落英先例，无背面政策时 offer 空转）。记账：turnEnding 强制触发（otherLiving）+ 新触发值 `turnOwnerSlashUseCount`（回合开始以来的杀类使用数）==0 → 己方 gengZhan 标记 +1。兑现：playPhaseStarting 强制触发（ownerAttributedMarkerCount ≥1）→ grantTurnRuleModifier(slashLimit, add, 本批新增 `amountFromMarker`：按当前标记数授予) + changeParticipantMarker `clearMarker`——累计标记一次性兑换为本出牌阶段的杀次数上限。 |

## 共享能力扩展

- `selectAndMoveOwnedCard` 新增 `suitFrom: damageCard`：解析器限定“事件目标单张手牌入己方手牌、无静态 suits”；执行器从 `frame.WindowContext.Facts.DamageCardSuit`（CreateAfterDamageProgramContext 新事实）解析动态花色并复用既有 suits 过滤管道。
- 触发值 `turnOwnerSlashUseCount`（3102）：CaptureProgramTriggerFacts 按 UsesTriggerValue 门控，扫描本回合开始的 CardUsedEvent（杀三态、回合拥有者）。
- 触发条件 `phaseIsPlay`（1025）：基础事实新增 `PhaseIsPlay`（区别于 action 作用域的 cardActionPhaseIsPlay，供移动窗口判定出牌阶段）。
- `grantTurnRuleModifier` 新增 `amountFromMarker`（仅 slashLimit add，与常量 amount 互斥）：引擎执行时读取拥有者当前标记数。
- `changeParticipantMarker` 新增 `clearMarker`（target owner、amount -1 占位）：执行时按持有量整额扣除。
- 组合校验器允许 provenance 领取触发器携带 `cardKinds`（运行时候选收集本就支持，界邓艾先例）；生命周期标记门的 owner-self（无 targetRef）放宽，TurnEnding 窗口接受 `TurnEndingBoundaryFrame` 父帧（自然回合结束路径）。
- `useVirtualCard` 解析放宽为支持 `NormalSlashTarget`（普通距离），激活式虚拟用牌放行 UseVirtualCard op；本批最终改用 viewAs 转化路径，该扩展保留为共享能力。

## 边界口径

- “观看其手牌”以按花色过滤后的候选选择呈现（规则实质为“获得其中一张同花色牌”）；无同花色牌时 skipIfNoCards 静默跳过。
- 更战领取以触发器 usageScope turn 计数（_declined_ 不消耗次数，比“限一次”更宽松的既有口径）。
- 兑现把全部累计标记一次性转换为本出牌阶段的杀上限（各合格回合的 +1 叠加到下个出牌阶段）；标记清零后不影响再下回合。

## 验证

- 定向 `--filter="Ol Deng Zhong"`：5/5 通过（定义元数据；勘破同花色拿牌；勘破视作杀转化与本阶段不再提供；更战领取其他角色出牌阶段弃置的杀；更战记账→标记→下出牌阶段整额兑换，含冷恢复重放比对）。
- 例行 `tools/Test-Changed.ps1`（分支）：失败集合与 main（7280ce51）基线逐项 diff **完全一致（零新增失败）**（实测数字见最终回复）。
- 本批在独立 worktree（batch/ol-deng-zhong）开发后合并回 main。
