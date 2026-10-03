# 界孙策单武将批次

当前普通 OL 界孙策（吴势力、4 体力、男性、稀有）以官方页当前文本接入：激昂（使用/成为【决斗】或红色【杀】目标摸一张牌；每回合首次【决斗】或红色【杀】因弃置入弃牌堆后可失去 1 点体力获得之）、魂姿（准备阶段体力值为 1 觉醒：减 1 体力上限，获得英姿、英魂，本回合结束阶段摸两张牌或回复 1 点体力）、制霸（主公技：出牌阶段限一次与一名其他吴势力角色拼点，若其没赢可获得两张拼点牌）。官方立绘 45200 已离线入库并登记图鉴/映射/同步脚本。来源见[来源档案](../content/sources/boundary-sun-ce-2026-10-04.json)。

| 技能 | 实现口径 |
| --- | --- |
| 激昂 | 前半句与 classic:jiang 逐字同文，四个触发体（cardUseBeforeTargetEffects × ownerRelation actor/target × 决斗 / 红杀 cardActionCardIsRed）逐字复用已验收形状。界升级点是弃牌回收：两个 discardPileReceived 触发体（决斗任意、红杀用 cardKinds slash/fireSlash + suits heart/diamond），排除清单复用 classic:luoying 的因使用/结算完毕清单；效果链 loseHp 1 → claimMovedCards；每回合首次用 usageScope turn + usageLimit 1 的共享 namedUsageGroup "jiang-reclaim" 表达（两种牌型合计每回合一次）。 |
| 魂姿 | 觉醒体（turnStartBeforeNormalFlow、currentHp==1、changeMaximumHp -1、grantSkills classic:yingzi + classic:yinghun）与 classic:hunzi 一致。界升级点是觉醒当回合结束阶段的摸二/回一：turnEnding 触发体用技能级 boolean state "woke"（states 声明照 classic:xingshuai）关联觉醒回合，觉醒置 true、结算后置 false，另挂 usageScope game + usageLimit 1 作一次性护栏；chooseOption 提供摸二/回一两个恒可选项（官方无“可以”，锁定二选一）。 |
| 制霸 | 主公技资格由 Tags=SkillTag.Lord 表达（引擎按非主公位排除授予，与界激将口径一致）。本批实现拥有者发起半句：激活体 challenge-wu（一张手牌 + `targetKind=otherLivingWuFactionWithHand`、usesPerPhase=1）+ op pindian，形状照 classic:tianyi。拼点牌回收走引擎既有拼点 claim 流程：新增 cardPolicy kind `pindianClaimAllWhenSourceWins`（枚举 785），发起方没赢（含平局）时拥有者可一次获得两张拼点牌。 |
| 英姿/英魂 | 觉醒获得技，`grantSkills` 直接授予已验收的 classic:yingzi、classic:yinghun，不复制定义。 |

## 共享能力扩展

- 新增目标集 `otherLivingWuFactionWithHand`（`SkillProgramTargetKind` 2001）：纯静态过滤（存活其他角色 + 有手牌 + `GetEffectiveFactionId==wu`），在 `GetProgramTargetSeats` 与 `BuildProgramActions` 两处目标枚举同步添加，照 OtherLivingMale 等既有静态目标集先例；无运行时状态写回。
- 新增 cardPolicy kind `pindianClaimAllWhenSourceWins`（枚举 785）：复用 `BeginPindianClaims` 既有 claim 流程，仅按 kind 分支领取集合（来源方没赢时领两张，否则按既有单张语义）；`ResolvePindianClaimChoice` 把单张 `MoveCard` 推广为按选择牌列表循环（既有 PindianClaim 路径每次仍只携带一张，行为不变）；解析门控要求该 kind 不携带 cardKinds 等附加字段。
- 既有回放/检查点格式零变化：拼点帧结构未变，claim 选择仍走既有 PromptChoice/AnswerPrompt 路径。

## 验证

- 界孙策定向 8/8：定义与元数据（势力/体力/技能序/变体、六个触发体形状、共享组、woke 状态、制霸目标集与 claim policy、主公/觉醒标签、文案）、决斗与红杀的己方使用摸牌、黑杀不触发、被红杀指定目标摸牌（driver 请求杀路径）、每回合首次弃牌回收（回收一次、同批后续不重复、跳过后不回收、下回合恢复）、魂姿觉醒（减上限/得英姿英魂/觉醒无即席选择、结束阶段回一为当回合唯一选择、之后保持沉默）、制霸（非吴目标拒绝、吴目标可选中、平局没赢领两张、拒绝后两张入弃牌堆）；关键步骤间做冷恢复与四视角一致性。
- 无过滤日常范围：Core 164/164（新增 8 项计入）、WPF 17/17，wrapper 实测 95 秒（含增量构建）。
- Full 全量：Core 557/557、WPF 56/56，wrapper 实测 233 秒（本 worktree）。

## 边界说明

- 主公体力按身份模式惯例 +1（4+1=5），魂姿觉醒后为 4；测试按该口径断言。fixture 通用 AI 武将体力在 `identity:sc`（非 classic-* 模式）下不套用 BaseHp，按占位 4 断言。
- 激昂回收沿用 `claimMovedCards` 的既有牌移动边界：只回收其他角色弃置入堆的牌，拥有者自己弃置的决斗/红杀不触发；可选触发被拒绝时不计次，本回合后续符合条件的牌会再次询问（引擎契约：触发次数在接受时消耗）。
- 制霸后半句“其他吴势力角色出牌阶段限一次，其可以与你拼点（你可以拒绝）”需要由他人主动发起拥有者技能的跨玩家交互流（含拥有者拒绝门），既有 DSL 原语（contributions 仅移牌、激活仅拥有者发起）无法表达，也不符合“typed 标量 + 解析门控”的最小扩展模式，本批未实现，留待后续批次。
- 本批为单武将批次：在独立 worktree（batch/sun-ce）开发，未触碰主区并行未提交工作。
