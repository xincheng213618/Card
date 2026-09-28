# 邓艾+沙摩柯批：屯田存田减距与蒺藜第X张摸牌

状态：邓艾（神话再临-山，2011，魏4体力，屯田/凿险/急袭）与沙摩柯（一将成名2015，蜀4体力，蒺藜）双武将交付完成。schema62 / 最低规则183 / 经典包1.153.0 / Checkpoint3。本批为单对话交付，基于并行批次提交 82d39a92 之后的工作区，未覆盖其文件。（如实记录：本批收尾期间工作区内另有鲁肃批并行在制并把规则版本续顶到184/经典包1.154.0；本批内容的 minimumRulesVersion 183 在其下继续有效，两批文件互不覆盖。）

## 技能与官方口径

- 屯田：当你于回合外失去牌后，你可以进行判定，然后将生效后的非红桃判定牌置于你的武将牌上，称为田；你计算与其他角色的距离-X（X为田的数量）。
- 凿险：觉醒技，准备阶段，若田的数量不小于3，你减1点体力上限，然后获得技能急袭。
- 急袭：你可以将一张田当【顺手牵羊】使用。
- 蒺藜：当你于一回合内使用或打出第X张牌时，你可以摸X张牌（X为你的攻击范围）。

官方现行文本取自 sanguosha.cn 武将详情页（hero-detail-52 / hero-detail-650），武将库 gid 52（邓艾，9款皮肤）与 gid 650（沙摩柯，5款皮肤）核验条目并下载立绘。体力值：官方页不渲染，两名 4 勾玉均取自两处独立二手资料（口径一致），如实标注。候选筛选时淘汰鲁肃（缔盟整手交换当时不可表达——随后并行批次以新增 exchangeSelectedTargetHands 落地，判定时点差异如实记录）、张郃（巧变多形态）、姜维（挑衅需含自己在内的攻击距离反向目标）、张昭张纮（固政需弃牌阶段检视语义）。来源记录见 [deng-ai-2026-09-28](../content/sources/deng-ai-2026-09-28.json) 与 [sha-mo-ke-2026-09-28](../content/sources/sha-mo-ke-2026-09-28.json)。

## 公共能力与实现要点（详见 [Runtime v61](../content/skill-composition/RUNTIME_V61.md)）

- `negatedOwnedZoneCount` 修正值表达式：出向距离 -（持久区牌数），仅 add×outgoingDistance×拥有者持久区组合可声明；屯田减距。
- 触发条件 `ownerIsTurnPlayer`（事实 `OwnerIsTurnPlayer`，CaptureProgramTriggerFacts 统一捕获）："回合外"触发以 not 组合子承载，触发条件无独立 notOwnTurn 种类。
- 触发值 `cardsUsedOrRespondedThisTurn`（24）：本回合开始以来使用+打出张数之和（含本牌），使用按 CardUseDeclaredEvent、打出按 CardActionAcceptedEvent(Response) 计数。
- 数值表达式 `currentAttackRange`（11）：实时攻击范围可作 draw 尺寸与 compare 比较项；比较窗白名单 cardUseCommitted/cardResponseAccepted/slashBeforeResponse（与 EventTargetHandCount 的杀响应边界分叉校验）。
- cardsMoved/cardsGained 窗口能力位升为 Common|Judgment：移动窗口内可 startJudgment（屯田），resultBind 仍须同序列消费。
- viewAs sourceZones 收 authority；outputKind snatch 仅 forPlay 且单张输入；`FindOwnedPlayableCard` 纳入 authority 区（合法性仍由转化过滤决定）。急袭实体牌为田，出牌阶段经既有距离-1顺手牵羊目标逻辑出牌。
- 凿险觉醒零分支单受益：`changeMaximumHp(-1)` → `grantSkills([classic:jixi])`，不使用 choiceGroup（choiceGroup 需 ≥2 分支）。急袭为授予型技能，不进 AdditionalSkillIds（与孙策仅登记 hunzi 同口径；本批曾误登记并返工摘除，授予后经非模板技能授予启用）。

## 内容与验证

内容：`classic-deng-ai.rules.json`（屯田三触发器/凿险/急袭 viewAs，revision 1，最低规则183）、`classic-sha-mo-ke.rules.json`（蒺藜双触发器）、两份 presentation（官方逐字现行文本）；`StandardClassicGeneralPackage` 注册两名武将（邓艾 wei/portraitKey deng_ai/BaseHp 4/技能 [tuntian, zaoxian]，沙摩柯 shu/portraitKey sha_mo_ke/BaseHp 4/技能 [jili]），版本 1.152.0 → 1.153.0，`CurrentGeneralIds` 追加两名。规则版本 182 → 183（Replay.CurrentRulesVersion；收尾时并行鲁肃批续顶为184，见状态行）。

官方立绘入 general-art-catalog（邓艾 gid52 经典形象105201，574×761；沙摩柯 gid650 经典形象165001，574×761；各含全部皮肤），WPF 资产 `official-deng-ai.png`、`official-sha-mo-ke.png`；图鉴神话再临·山组（myth-mountain）新增邓艾，一将成名2015组（fame-5）新增沙摩柯；`tools/sync_general_art.py` classic/skins/ol/finalize/verify 全流程通过（94 武将 867 PNG）。

定向检查 `tests/CardGame.Core.Tests/DengAiChecks.cs`（5项）与 `ShaMoKeChecks.cs`（3项），自然命令与真实决策应答、种子扫描 fixture（5人局、8体力标准none银行将、240张受控牌堆）：

1. 邓艾定义与schema：技能表 [屯田,凿险]、三移牌触发器/效果四步链/负距离修正/觉醒属性/急袭 viewAs 断言；拒收样例（negatedOwnedZoneCount 配非持久区、配非距离查询、snatch viewAs forResponse）。
2. 屯田存田减距并回放：AI 拆顺触发 → 非红桃判定牌入 authority、判定牌成田或入弃堆账目平衡、单田使基础距离2对手实际距离1、回放全等。
3. 红桃判定不入田：authority 为空、距离不变。
4. 凿险觉醒：3田后自动觉醒（上限4→3）、授予 [jixi]、田保留、回放全等。
5. 急袭转化：authority 牌的 Snatch 合法动作（conversionSource 断言），出牌后田离区、目标失牌。
6. 沙摩柯定义与schema：双触发器/条件与效果断言；拒收样例（currentAttackRange 比较声明在 turnEnding、draw 混用 amount 与 numberExpression）。
7. 蒺藜首次使用摸1、同回合第二次不摸，回放全等。
8. 蒺藜回合外首次打出（对 AI 杀出闪）摸1，回放全等。

验证结果：（待最终复验后回填）

## 边界与未覆盖

- 屯田"失去牌"以 cardsMoved 三触发器 + 排除自己用牌/打出/无懈承载；转换/交出/展示收回等特殊移动路径按 perBatch 语义一致处理，未逐一专测。
- 蒺藜计数含装备使用与无懈打出；攻击范围为0时第0张不存在、自然不触发，未专测。
- 急袭仅主动使用（官方"使用"），不可打出（解析器拒收 forResponse）。
- 并行在制说明：Core/WPF 全量在本批收尾时受工作区内鲁肃批在制面影响，最终验证以本批文件与当次复验为准，与在制面的差异按曹丕/孙策批先例归属记录。
