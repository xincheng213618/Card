# RUNTIME V70 —— 规则 193 / 经典包 1.164.0

本版本号为张春华批次（绝情/伤逝）使用。Checkpoint schema 仍为 3；规则版本 192 → 193，经典包 1.163.0 → 1.164.0。技能 JSON schema 62 不变（本批零新增节点类型，仅追加枚举成员）。旧规则的检查点按现有版本边界拒绝恢复。

## 张春华批次：绝情 / 伤逝

本批为单武将交付：张春华（绝情/伤逝，一将成名2011，魏 3 体力，最低规则 193，十周年官方武将库 gid 295 现行页面为唯一文本口径）。共享引擎语汇新增一个策略消费点与两个枚举成员，无新增触发窗口、无新增 EffectOp——五点注册清单与 ProgramCompositionDefinitionChecks fixture 均不适用。

### 消费方

- 绝情（锁定技）：技能级 `cardPolicies: [{kind: "convertOutgoingDamageToHpLoss"}]`（cardKinds 空=全部牌类、condition always）。`ApplyAttackDamage`（全部伤害唯一漏斗）在无前防护、before-damage 窗口、寒冰剑、麒麟弓之后、铁索传导之前按来源策略转化：不产生伤害事件、不开伤害触发窗、不传导铁索、不置伤害类事实；以 `ProgramSkillHpLostEvent`（skillId=绝情）+ `RecordHpChange(Loss)` 记账，`AfterHpLost` 窗口照常可见；清空体力走 `BeginHpLossDying`（killerSeat=null 的 Damage 续接濒死——流失致死无来源、无奖惩，攻击牌照常收尾进弃牌堆）。
- 伤逝：三触发（subject owner、optional、condition compare(`currentHandCount` < `currentLostHp`)、effect draw `ownerLostHpMinusHandCount`）——
  - `afterDamageApplied`（damageOccurrence perDamage）：受到伤害后 X 增；
  - `afterHpLost`：非伤害体力流失后 X 增（崩坏类主动流失、对方绝情转化）；
  - `cardsMoved`（sourceZones [hand]、perBatch、无排除原因、无 ignoreOwnSkillMovements）：手牌离开后手牌数减（弃牌阶段、被顺被拆被换均属之）。
  - 获得/摸牌方向不触发（实装惯例边界，见批次记录诚实归因）。

### 新共享语汇（三成员，一消费点）

- `SkillProgramCardPolicyKind.ConvertOutgoingDamageToHpLoss = 18`：来源侧策略，`ParseCardPolicy` 为其放行空 `cardKinds`。
- `SkillProgramTriggerValueKind.CurrentLostHp = 27`：触发值 = CurrentMaxHp − CurrentHp，纯事实推导。
- `SkillProgramNumberExpression.OwnerLostHpMinusHandCount = 16`：draw 数量 = max(0, 持有者已损失体力 − 当前手牌数)，与既有 `targetMaxHpMinusHandCount` 同构；draw 描述符解析白名单同步放行。

### 引擎改动（一处消费臂 + 濒死变体）

- `GameEngine.ApplyAttackDamage`：新增 `TryConvertOutgoingDamageToHpLoss(attack, amount, nature, out awaitingDying)` 转化臂（out 双信号：转化命中必返回 true，awaitingDying 表达是否濒死续接；首版单返回值穿透缺陷由行为检查切片断言当场拦截修正）。
- 新私有 `BeginHpLossDying`：复用 `DyingContinuation.Damage` 保留攻击收尾（FinishAttack 弃牌/闪电移动等），仅 killerSeat=null；`CompleteDamageAfterDying` 对 `DamageConvertedToHpLoss` 攻击跳过 AfterDamageApplied 窗口与 `AfterDamageEvent`，其余路径逐字保留。
- `AttackResolution.DamageConvertedToHpLoss` 旗标：转化攻击不置 `DamageWasApplied`/`CausedDamage`、不计 `_playPhaseDamageDealtByCurrentPlayer`——依赖伤害事实的既有技能按官方口径"看不到"该结算。

### 内容与注册

- `src/CardGame.Content.Standard/SkillPrograms/classic-zhang-chun-hua.rules.json`（schema 62，minimumRulesVersion 193）+ presentation（schema 3）
- 注册：`StandardClassicGeneralPackage.cs` 资源常量 + 懒加载目录 + `ZhangChunHuaProgram` + 技能块（绝情 Locked+State、伤逝 WithOptionalTriggerMetadata）+ `AddGeneral`（魏，3 体力）+ `CurrentGeneralIds`；包版本 1.163.0 → 1.164.0；`Replay.cs` 规则 192 → 193
- 立绘：`official-zhang-chun-hua.png`（gid 295 经典 129501，574×761）；`tools/sync_general_art.py` CLASSIC_HEROES 加 `"zhang-chun-hua": 295`；`docs/content/general-art-catalog.json` entries 按字典序新增；`GeneralGalleryCatalog.cs` fame-1（一将成名2011 官方名单）新增

## 验证口径

定向检查 `ZhangChunHuaChecks`（4 项）全部通过：定义与 schema + 拒收面十项（policy 带 factionId / 带花色改写 / 缺 policy；afterDamage 缺 damageOccurrence / 带 hpChangeOccurrence；cardsMoved 带 turnOwnerScope / 带 destinationZones；currentLostHp 右目带 value；draw 常量+表达式并存 / 表达式越白名单 / 目标越 owner）；绝情杀转化（切片内单条流失事件、零伤害事件、HP−1）+回放一致；绝情决斗转化（全决斗牌堆银行无杀可响应、owner 来源零伤害事件、决斗牌照常进弃牌堆）+回放一致；伤逝受伤后补牌至恰为 X（refill 移动条数=差值）+回放一致。种子扫描动态停止，无静态白名单。Debug 全量 Core **664/664 全绿**（基线 660+4，无入池位移失败）；Release 构建 0 error；WPF 过滤器：缺立绘名单为既有 8 名（不含 zhang-chun-hua），"general gallery combines registered series" 通过。`sync_general_art.py --phase verify`：106 武将 / 887 PNG 全过。批次记录见 [2026-09-30-zhang-chun-hua](../../benchmarks/2026-09-30-zhang-chun-hua.md)。
