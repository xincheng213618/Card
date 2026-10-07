# 清河公主单武将批次

当前 OL 清河公主（魏势力、3 体力、女、称号"魏长公主"，官网 498 号）接入：谮构（当你攻击范围内一名角色使用【闪】时，你可以弃置一张非基本牌或失去1点体力，令此【闪】无效，然后你获得之。）、长姬（一名角色的结束阶段，若你本回合：造成过伤害，你可以令其摸两张牌；受到过伤害，你可以令其弃置两张牌。）。官方立绘 49800 已离线入库并登记图鉴/映射。技能文本以 OL 专站 [sgsol 清河公主词条](https://wiki.biligame.com/sgsol/清河公主)（oldid=31193）为准，并与官网 hero/info 接口（gid=498）和官网英雄页正文三方交叉一致；sgs 主站 BWIKI 词条经核对为十周年经典版（大魏长公主/荟萃-皇家贵胄/2024-10-24，技能同名不同文），未采用。来源与两站差异取证见[来源档案](../content/sources/ol-qinghe-gongzhu-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 谮构 | zengou-nullify：slashFullyDodged、ownerRelation observer、可选；触发条件 compare(ownerEventTargetDistance ≤ currentAttackRange) 预过滤攻击范围，op 内 IsWithinAttackRange 复核 → 新 op `zengouNullifyDodge`（7253）：要求冻结卡牌行动 + 单目标攻击 + 最近一条 card.response-finished 进弃牌堆且高级牌面为【闪】的实体牌，弹出代价选择（逐张非基本牌——手牌+装备区、CardCatalog 类别非"基本牌"，或失去1点体力）；支付后对攻击 CardUseFrame 置 `CurrentCardEnhancement.Uncancelable`（红杀政策同款机制），随后 ContinueSlashAfterDodgePrograms 的免抵消检查读到它，【杀】照常造成伤害；【闪】实体经弃牌堆→手牌移动获得，`ProgramZengouNullifiedEvent` 记录代价/无效/获得证据。 |
| 长姬 | changji-ending-own（turnEnding、turnOwnerScope own）+ changji-ending-others（turnEnding、turnOwnerScope otherLiving）：强制触发，选择在 op 内 → 新 op `changjiEndingDamageChoice`（7252）：从本回合 TurnStartedEvent 边界以来的已提交 `DamageAppliedEvent` 重建伤害史（造成=SourceSeat==owner 且 !SourceLess 且 Amount>0；受到=TargetSeat==owner 且 Amount>0），有任一时向拥有者提供"令其摸两张牌 / 令其弃置两张牌 / 不发动"选择（其=结束阶段角色）；摸牌分支 DrawCards(结束角色,2)；弃牌分支按闭境惩罚同款两段提示（帧载 `ChangjiDiscardState`），由结束角色逐张弃置 min(2, 手牌数) 张手牌，`ProgramChangjiEndingEvent` 记录分支/弃牌/摸数。 |

## 共享能力扩展

- 新增 EffectOp 7252–7253（只占用分配的 7252–7259 段内两枚）；描述符按反射目录自动注册；未新增 ProgramOperationAiSemantic、ConditionKind、ValueKind、PlayerMarkerKind、触发窗口或 schema 节点。
- 唯一共享白名单扩展：`SkillPrograms.cs` 的 CurrentAttackRange 比较窗口校验追加 `SlashFullyDodged`（additive 校验放宽；该卡牌行动窗口的冻结事实本就无条件计算 CurrentAttackRange，OwnerEventTargetDistance 此前已允许该窗口）。其余指纹不受影响（无技能使用该组合时行为不变）。
- 冷恢复：两个 op 的证据均为已提交事件；谮构依赖的结算事实（弃牌堆实体、攻击帧增强）在重放中按命令确定性重建，无新增序列化运行时状态。

## 边界口径

- 谮构触发时点是【闪】结算完成（slashFullyDodged）之后、伤害判定之前——官方文本"使用【闪】时"的理想时点更早；本实现为追认式无效，与"使用瞬间"之间若有其他技能结算，先后顺序可能可见（如实记录）。
- 谮构仅对单目标【杀】生效：多目标攻击的 Uncancelable 标志按整个 CardUseFrame 读取，无法按目标定界，为避免对其他目标的【闪】泄漏而选择不触发（记录缺口）。
- 谮构"获得之"只认高级牌面为【闪】的已结算实体；转化/虚拟【闪】（如龙胆）只无效、无牌可获得。
- 长姬伤害史按本回合任意 `DamageAppliedEvent`（Amount>0；造成侧排除 SourceLess）判定，不区分伤害对象是否为结束阶段角色；体力流失、 FurnaceDelay 等非 DamageAppliedEvent 的扣除不计入"造成/受到伤害"。
- 长姬弃牌分支只弃手牌（"弃置两张牌"标准读法）；手牌不足两张时弃全部，无手牌则该分支落空；两个分支都满足时可任选其一或都不发动。
- AI 策略：长姬结束阶段目标是自己优先摸 2、否则优先令其弃 2，再次跳过；谮构代价优先弃牌、否则失去体力；谮构的可选发动走通用估算（标签估值为 0，AI 实际上会保持不发动），不写人物专用 AI 分支。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案）：0 error；Core 工程全量重编 warning 仅存在于既有文件（SkillProgramExecutor.cs:1112、GameEngine.ActualHandGainPrograms.cs:121、GameEngine.EndingHistoricalUses.cs:99、GameEngine.LiangXingPrograms.cs:27、GameEngine.OwnedDeathBenefitReturns.cs:66/74/109、GameEngine.PublicPilePreparation.cs:75、GameEngine.SameTypeActualUseAid.cs:178），本批新增/改动的文件无任何 warning，均为基线既有、非本批引入。
- 内容装载：`tools/Inspect-SkillProgram.ps1` 对本批 rules/presentation 校验通过（装载成功不等于行为验证）；`--ai-inspect` 三个种子（11/4998/77）全 AI 完整对局正常完成，覆盖内容注册与整局稳定性的冒烟观察（非行为断言）。
- 例行 `tools/Test-Changed.ps1`（无过滤，工作树含本批全部内容）：Core 266 通过 / 57 失败 / 0 跳过（323 项），WPF 18/18 通过，用时约 129 秒（含增量构建）。失败数量与严畯批基线（dee76fef/1fc7b1d6 实测 Core 266/57 + WPF 18/0）一致；57 项失败名单已随最终汇报提供给协调者逐项核对，名单中不含任何 qinghe/changji/zengou 相关项。
- 版本纪律：未提升 `GameCheckpoint.CurrentRulesVersion`、`SkillProgramCatalog.RulesSchemaVersion` 或内容包版本；无同指纹重放差异（共享白名单扩展只在技能使用新窗口组合时放宽校验，不改变任何已装载技能的运行行为）。
- 取证更正说明：本批首个实现（a7374e03）误用 sgs 主站 BWIKI 的十周年版本文本（长姬=锁定技多目标优先、谮构=标记惩罚），按协调员交叉核对提醒后于 0a614ffc 重做为 OL 专站现行普通版；两个提交均保留在分支历史以供核对。
- 本批在独立 worktree（batch/ol-qinghe-gongzhu）开发，与蒋干、刘辩、陈登等九路并行。
