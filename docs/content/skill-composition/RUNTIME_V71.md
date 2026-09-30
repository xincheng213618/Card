# RUNTIME V71 —— 规则 194 / 经典包 1.165.0

本版本号为马谡批次（散谣/制蛮）使用。Checkpoint schema 仍为 3；规则版本 192 → **194**（193 由并行张春华批预留），经典包 1.163.0 → **1.165.0**（1.164 由并行吴国太批预留）。技能 JSON schema 62 不变（本批零新增节点类型、零新增操作、零新增窗口）。旧规则的检查点按现有版本边界拒绝恢复。

## 马谡批次：散谣 / 制蛮

本批为单武将交付：马谡（一将成名2011，蜀 3 体力，最低规则 194，gid 289）。官方现行文本（x 站 gid 289 现行页面，逐字）：

> 【散谣】出牌阶段限一次，你可以弃置一张牌并指定一名体力最多（或之一）的角色，你对其造成1点伤害
> 【制蛮】当你对一名其他角色造成伤害时，你可以防止此伤害，然后获得其装备区或判定区的一张牌

BWIKI 二手核对：蜀、男、经典版 3 勾玉、一将成名2011；BWIKI 页未收录两技能的裁定细则。

### 消费方

- 散谣：trigger 无（激活承载）——activation `spread-rumor`：minCards/maxCards 1、sourceZones [hand, equipment]（"弃置一张牌"）、targetKind **livingMaxHp**（本批新增目标种类，枚举尾值 29）、usesPerTurn 1、condition ownTurn；effects = `discardSelected`(owner, amount 1) + `damage`(selectedTarget, 1)。目标过滤"体力最多（或之一）"= 存活角色中体力等于全场最大者（并列全部合法）；经典文本不排除自身（界版"除你以外"反证），livingMaxHp 按 AnyLiving 同款允许自指进激活目标集，AI 估值下不会自选。
- 制蛮：trigger `prevent-and-take`，window `beforeDamageApplied`、subject owner、optional、condition `damageSourceIsOwner`（既有条件，枚举 25）→ effects = `preventCurrentDamage` + `selectAndMoveOwnedCard`（chooser owner、cardOwner **eventTarget**、zones [equipment, judgment]、destination ownerHand、skipIfNoCards）。仁心（damage-prevention-skills）是 before-damage 窗口 + preventCurrentDamage 的既有样例；"其装备区或判定区的一张牌"落在 selectAndMoveOwnedCard 的可见区段（chooser≠cardOwner 时禁 cardCategories，天然只做逐张选择）。

### 引擎扩展清单（四处，均为既有语汇的收窄或延伸，无新窗口/无新操作/无新条件种类）

1. **`SkillProgramTargetKind.LivingMaxHp = 29`（枚举尾追加）** + 两处候选收集臂：`GetProgramTargetSeats`（GameEngine.ProgramLifecycle.cs，selectTarget 效果路径）与 `BuildProgramActions` 激活目标 switch（GameEngine.SkillPrograms.cs，activation 声明路径）各一臂 `target.Hp == 存活体力最大值`；激活路径的自指守卫（原 `AnyLiving or AnyWounded || seat != owner`）扩入 LivingMaxHp。
2. **`damageSourceIsOwner` 事实扩展到 before-damage 窗口**：`TryBeginBeforeDamageProgramWindow`（GameEngine.DamagePreventionPrograms.cs）的逐候选事实捕获从 `with { EventTargetHp }` 扩为再冻结 `DamageSourceIsOwner = owner.Seat == sourceSeat`；解析门（SkillPrograms.cs）将该条件的窗口白名单从 `AfterDamageApplied | DamageAppliedBeforeDying` 扩为再含 `BeforeDamageApplied`——`DamageSourceFactionIs` 拆出独立 gate 保持原白名单不变（其 faction 事实未捕获，不在本批扩展）。
3. **eventTarget 参与者引用窗口白名单纳入 before-damage 窗口**（SkillPrograms.cs effects gate）：eventTarget 在 after-damage 语义是"伤害目标"，before-damage 窗口同语义（context.TargetSeat），故与 AfterDamageApplied 同列白名单。
4. **ProgramCompositionAi.SelectAndMoveOwnedCard 增 eventTarget→ownerHand 估值臂**：与 selectedTarget→ownerHand 臂同值（+1 持牌估计、-1 目标牌），共享操作无死估值组合。

### 内容与注册

- `src/CardGame.Content.Standard/SkillPrograms/classic-ma-su.rules.json`（schema 62，minimumRulesVersion 194）
- `src/CardGame.Content.Standard/SkillPrograms/classic-ma-su.presentation.json`（schema 3，名称+描述，无 chooseOption 故无 optionLabels）
- 注册：`StandardClassicGeneralPackage.cs` 资源常量 + 懒加载目录 + `MaSuProgram` + 技能块（散谣 WithActiveActionMetadata、制蛮 WithOptionalTriggerMetadata）+ `AddGeneral`（蜀，3 体力）+ `CurrentGeneralIds`；包版本 1.163.0 → 1.165.0
- `Replay.cs`：规则 192 → 194（193 并行张春华批预留，见版本源注释）
- 立绘：`official-ma-su.png`（gid 289 经典 128901，574×761，sha256 a0470fdf…3a0d5d）；`tools/sync_general_art.py` CLASSIC_HEROES 加 `"ma-su": 289`；`general-art-catalog.json` entries 新增；`GeneralGalleryCatalog.cs` fame-1（一将成名2011 官方名单）新增

## 验证口径

定向检查 `MaSuChecks`（4 项）全部通过：定义与 schema + 解析拒收面九项（activation sourceZones 含 judgment、缺 usesPerTurn、discardSelected 目标非 owner、discardSelected 带 condition、ownerHand 目的地带 targetRef、zones 含 discardPile、count≠1、cardCategories 与 chooser≠cardOwner、damageSourceIsOwner 用于 turnEnding）；散谣"弃一张手牌→对体力最多目标 1 伤害"且可选目标恰为存活体力最大者+回放一致；制蛮"防伤+取装备区牌"零伤害落地+回放一致；拒定制蛮伤害照常+回放一致。种子扫描动态停止（首个命中即验），无静态白名单。

批次记录见 [2026-09-30-ma-su](../../benchmarks/2026-09-30-ma-su.md)。
