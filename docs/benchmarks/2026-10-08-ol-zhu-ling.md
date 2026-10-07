# 朱灵单武将批次

当前 OL 朱灵（魏势力、4 体力、男、称号"良将之亚"，官网 489 号，史诗）接入：战意（出牌阶段开始时，你可以弃置一种类别的所有牌，然后直到你的下个回合开始，另外两种类别的牌：基本牌，你使用时无距离限制且回复值或伤害值+1；锦囊牌，不计入你的手牌上限且你使用时摸一张牌；装备牌，置入你的装备区时可以选择一名其他角色的一张牌并令其弃置之）。官方立绘 48900 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-zhu-ling-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 子效果 | 实现口径 |
| --- | --- |
| 发动（弃置一种类别的所有牌） | zhanyi-launch：playPhaseStarting 可选触发 → 新 op `zhuLingZhanyiChooseCategory`（7228）。只为手牌+装备区合计至少一张牌的类别出选项（选项展示张数），选定后把该类别实体牌按实际位置分两批移入弃牌堆（`skill-program.ol:zhanyi.launch`），`ProgramZhanyiCategoryChosenEvent` 记录证据。一个类别都没有则触发自然落空。 |
| 基本牌（被选类别为另两类时） | 共享回合效果授权：伤害修正（slash/fireSlash/thunderSlash，+1，`expires: nextOwnerTurnStart`，sourceScope ownerUsed——与既有裸衣同一存储，生命周期与文本"直到你的下个回合开始"完全一致）+ 新共享能力"按牌种过滤的 cardUseDistanceLimit=unlimited 规则修正"（种类限定为三种杀）。 |
| 锦囊牌（被选类别为另两类时） | 手牌上限：共享 `grantTurnHandLimitCardKindExemption` 等价授权，牌种覆盖全部非装备锦囊（决斗、无中生有、南蛮、万箭、桃园、五谷、拆、顺、火攻、借刀、无懈、铁索、乐、兵粮、闪电），只在发动回合的弃牌阶段生效（该共享存储本就是回合级）。摸牌：内容触发 zhanyi-trick-draw（cardUseCommitted、ownerRelation actor、条件 cardActionCategoryIs[trick] + booleanState zhanyi-trick）→ 共享 draw op 摸一张。 |
| 装备牌（被选类别为另两类时） | 内容触发 zhanyi-equipment-punish（cardUseCommitted、ownerRelation actor、条件 cardActionCategoryIs[equipment] + booleanState zhanyi-equipment、可选）→ 新 op `zhuLingZhanyiEquipmentPunish`（7229）：先选一名持有可弃牌的其他角色，再选其一张明置装备或按牌位盲选一张暗牌；被选实体牌按统一移动契约弃置（尊重既有装备弃置保护），`ProgramZhanyiEquipmentPunishEvent` 记录证据。 |
| 到期 | zhanyi-expire：turnStartBeforeNormalFlow 强制触发，用内容层 setBooleanState 把三个声明状态（zhanyi-basic/zhanyi-trick/zhanyi-equipment，private、game 作用域）复位为 false——即"直到你的下个回合开始"；伤害修正由共享 nextOwnerTurnStart 到期、规则修正与手牌上限豁免由回合级存储自行到期。 |

## 共享能力扩展

- 新增 EffectOp 7228–7229（只占用分配的 7228–7235 段）；描述符按反射目录自动注册，AI 估值复用既有语义（GrantTurnRuleModifier/ChooseOtherOwnedCardDiscard）并新增 `ZhanyiLaunch` 公开估值（+6 场面、按一张手牌计成本）。未新增 PlayerMarkerKind、ConditionKind、触发窗口或 schema 节点。
- **cardUseDistanceLimit=unlimited 规则修正支持按牌种过滤**（新增）：此前该查询只允许无种类（全局无距离限制）；现在允许携带非空去重 cardKinds，存储层既有 `CardKinds.Contains(effectiveCardKind)` 过滤直接生效。接线三处：引擎授权校验放行（GameEngine.CardUseModules.cs）、回合效果不变量放行并校验去重（CardUseModules.cs）、内容描述符 `grantTurnRuleModifier` 在 cardUseDistanceLimit 下接受可选 cardKinds（ProgramLifecycleOperationDefinitions.cs），另把 `HasUnlimitedTurnRuleModifier` 增加可选牌种参数并在 `HasCardDistanceExemption` 传入当前牌种（空种类行为不变，既有内容零影响——此前该组合会被校验拒绝，没有任何现存内容使用）。

## 边界口径

- 发动：只提供"至少持有一张该类别牌"的选项；"弃置所有牌"覆盖手牌区与装备区，不含判定区。发动次数由触发窗口本身决定（每个出牌阶段开始时一次；同回合额外出牌阶段会再次给出窗口，每次发动覆盖此前状态）。
- 基本牌 +1 伤害只在三类杀的用牌伤害结算时生效（sourceScope=ownerUsed：使用者即伤害来源）；**回复值+1 未表达**——普通（非濒死）用桃的回复量没有共享修正入口（既有 RecoveryBonus 只挂在濒死救援策略上），该子项如实记入 `docs/content/CORE_API_REQUESTS.md`，不静默简化。
- 无距离限制按种类限定为三种杀；其生命周期是回合级共享存储（发动当回合有效）。文本上"直到下个回合开始"期间在其他角色回合中使用基本牌的场景（极罕见）不会被回合级距离修正覆盖；伤害+1（nextOwnerTurnStart）、锦囊摸牌与装备惩罚（boolean 状态）则严格覆盖整个窗口。差别如实记录。
- 锦囊摸牌：触发条件只认 ownerRelation=actor 的 cardUseCommitted（"你使用时"）。本引擎无懈可击走响应路径，不经过 cardUseCommitted，故无懈不摸（记录为引擎口径差异）；延时锦囊的使用照常摸。
- 装备惩罚：触发窗口是 cardUseCommitted（用牌已承诺、实体即将置入装备区时），非置入动作完成时点；只有自己用装备牌时触发，其他角色效果把装备置入朱灵装备区的场景不在窗口内（文本为"置入你的装备区时"，此处收窄为"你使用装备牌置入时"，如实记录）。暗牌按牌位盲选（与基础过河拆桥的暗牌牌位选择同口径），牌位→实体映射按牌 ID 升序。
- 三个 boolean 状态为私有运行时状态，普通快照不含其值；冷恢复按接受命令前缀确定性重建（与既有"闭境"标记同一口径）。
- 本批未提升 `GameCheckpoint.CurrentRulesVersion`、`SkillProgramCatalog.RulesSchemaVersion` 或任何包版本：规则 JSON 为加性可选节点（states 节点既有），内容指纹哈希已区分本批内容。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案，--no-incremental 复核）：0 error；20 条 warning 全部位于既有文件（9 条 src/Core 来自 aed24e70 批在制文件，11 条 tests/ 未触碰文件），无本批引入。
- 例行 `tools/Test-Changed.ps1`（无过滤）：Core 266 通过 / 57 失败（323 项），WPF 18 通过 / 0 失败。57 项失败名单与严畯批基线（1fc7b1d6 实测 266/57、WPF 18/0）一致，无新增失败、无朱灵相关条目（名单含 "Zhuge Ke" 字样的是既有条目的子串巧合）。本批收尾复跑于共享能力扩展提交（26662b28）之后，结果与扩展前完全一致。
- 本批在独立 worktree（batch/ol-zhu-ling）开发，与蒋干、潘淑、杨仪等路并行。基线说明同严畯批：57 项失败属 aed24e70 批次自身的在制状态，非本批引入。
