# C0-K1 卡牌移动契约包（延伸至 K7 基础判定）

规则行为版本 5 补充：致命伤害若产生伤害后技能候选，伤害牌仍保持在 `Processing`，先完成 `DamageTriggerWindowFrame`/`DamageSkillFrame` 的候选游标，再按现有死亡契约进入 `DyingFrame`；v1–v4 回放保留历史移动和事件顺序。

更新时间：2026-09-08

本文件是内容侧对 K1 牌区能力的消费契约，并记录 K2/K3/K4/K5/K6/K7 对内容的影响。它描述稳定内容 ID、物理牌实例、移动原因、可见性和场景验收；正式 Registry、可暂停私有选将、杀/火杀/雷杀/决斗/酒/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/无懈可击/铁索连环/闪电链路、刚烈判定/来源反制链路、鬼才判定替换链路、仁王盾黑色杀阻挡链路和 K6 基础装备链路、K7 八卦阵判定链路的类型化结算帧已由 `CardGame.Content.Standard`/Core 提供，通用 `CardsMoving/CardsMoved` 规则时机、复杂技能触发、复杂响应链和 K6 剩余装备效果仍等待后续阶段。

## 1. 统一不变量

每张实体牌在任意已提交的公共操作后必须满足：

1. 恰好位于一个牌区或 `OutsideGame`；
2. 物理实例 ID 不重复，牌区总数与开局注册总数相等；
3. 单张移动、同源批量移动和 `MoveBatch` 先验证完整来源、目标、槽位和重复 ID，再整体生效；非法来源、非法目标或中途失败不得产生部分移动；
4. `Processing` 保存当前结算仍在使用的实体牌；若伤害后技能按内容契约取得该实体牌，允许在父结算仍未结束时转入目标手牌，父帧必须继续引用同一实例并在最终边界完成；
5. `CardMovementRecord` 的 reason 使用命名空间 ID，不用 UI 文案、枚举序号或任意字符串；
6. 普通玩家视图只得到其有权知道的牌面和数量。可信宿主的完整移动账本、随机种子、牌堆顺序、其他玩家手牌和 AI 思考不进入普通视图。

内容 ID 与物理牌实例在规范层分开：`definitionId` 例如 `standard:slash`，`instanceId` 是本局唯一的实体牌 ID。K1/K2 当前运行记录仍使用宿主的 `CardId`/`CardKind`，内容不能依赖 `CardKind` 的数值顺序；两者的正式注册关系已由 K3 Standard Registry 冻结。

## 2. 区域与移动原因

区域名称使用 K1 的 `CardLocation` 语义：`DrawPile`、`Hand(seat)`、`Processing`、`DiscardPile`、`Equipment(seat)`、`Judgment(seat)` 和 `OutsideGame`。K6 基础切片继续使用 `Equipment(seat)` 作为物理牌区，并以 `EquipmentSlot` 在规则核心中保证每个玩家每槽至多一张；K7 八卦阵判定把公开判定牌放入对应 `Judgment(seat)`，再按结果移入弃牌堆；不把槽位塞进 UI 字符串或改变旧 `CardLocation` 构造。下表中的 reason ID 与现有 `CardMoveReasons` 对齐；标为“建议”的 ID 只用于后续内容包设计，不能假定当前 Core 已提供。

| reason ID | 用途 | 当前状态 |
| --- | --- | --- |
| `setup.initial-deal` | 选将完成后逐张发牌 | K1/K4 可用 |
| `rule.draw` | 摸牌进入手牌 | K1 可用 |
| `card.use` | 使用者把基本牌/锦囊送入处理区 | K1 可用 |
| `card.respond` | 响应者把响应牌送入处理区 | K1 可用 |
| `card.response-finished` | 响应牌结算完成进入弃牌区 | K1 可用 |
| `card.respond.nullification` | 无懈响应者把无懈可击送入处理区 | K7 可用 |
| `card.respond.nullification-finished` | 无懈响应完成后把无懈可击送入弃牌区 | K7 可用 |
| `card.use-finished` | 使用牌结算完成进入弃牌区 | K1 可用 |
| `card.public-reveal` | 五谷丰登把公开牌从摸牌堆翻入处理区 | K5 可用 |
| `card.harvest-pick` | 当前 picker 把公开牌移入自己的手牌 | K5 可用 |
| `card.harvest-discard` | 五谷丰登结束时清理未选的公开牌 | K5 可用 |
| `card.effect.dismantlement` | 过河拆桥把目标一张手牌或公开装备送入处理区 | K5 可用 |
| `card.effect.dismantlement-finished` | 过河拆桥把目标牌从处理区送入弃牌区 | K5 可用 |
| `card.effect.dismantlement-judgment` | 过河拆桥把目标公开判定区牌送入处理区 | K7 可用 |
| `card.effect.dismantlement-judgment-finished` | 过河拆桥把目标判定区牌从处理区送入弃牌区 | K7 可用 |
| `card.effect.snatch` | 顺手牵羊把目标一张手牌或公开装备送入处理区 | K5 可用 |
| `card.effect.snatch-finished` | 顺手牵羊把目标牌从处理区送入使用者手牌 | K5 可用 |
| `card.effect.snatch-judgment` | 顺手牵羊把目标公开判定区牌送入处理区 | K7 可用 |
| `card.effect.snatch-judgment-finished` | 顺手牵羊把目标判定区牌从处理区送入使用者手牌 | K7 可用 |
| `skill.jianxiong.claim-damage-card` | 奸雄从处理区取得伤害牌 | K1 可用 |
| `skill.feedback.claim-damage-card` | 反馈在目标存活且伤害牌仍在处理区时取得伤害牌 | K5 可用 |
| `rule.hand-limit-discard` | 手牌上限弃牌 | K1 可用 |
| `rule.death-discard` | 阵亡时清理手牌 | K1 可用 |
| `rule.death-equipment-discard` | 阵亡时清理装备区 | K6 可用 |
| `mode.identity.lord-killed-loyalist` | 主公误杀忠臣的惩罚弃牌 | K1 可用 |
| `deck.reshuffle` | 弃牌区重洗回摸牌堆 | K1 可用 |
| `skill.yiji.draw` | 遗计受伤后摸牌进入拥有者手牌 | K5 可用 |
| `skill.yiji.give-card` | 遗计把拥有者的一张摸牌交给其他存活角色 | K5 可用 |
| `skill.yuanhu.discard` | 援护在其他角色受伤后弃置拥有者的一张手牌 | K5 可用 |
| `skill.ganglie.discard` | 刚烈红色判定后由伤害来源弃置两张手牌 | K5 可用 |
| `skill.guicai.replace` | 鬼才将拥有者手牌作为新判定牌替换当前判定 | K7 可用 |
| `card.effect.delayed-place` | 乐不思蜀/兵粮寸断使用牌从处理区进入目标判定区 | K7 可用 |
| `card.effect.delayed-transfer` | 闪电未命中时从当前角色判定区转移到下一名存活角色判定区 | K7 可用 |
| `card.effect.delayed-finish` | 乐不思蜀/兵粮寸断延时牌从目标判定区进入弃牌堆 | K7 可用 |
| `judgment.reveal` | 八卦阵将摸牌堆顶判定牌公开移入目标判定区 | K7 可用 |
| `judgment.finish` | 八卦阵完成判定后将判定牌移入弃牌堆 | K7 可用 |
| `equipment.use` | 装备者把装备牌送入处理区 | K6 可用 |
| `equipment.enter` | 装备牌从处理区进入所属玩家装备区 | K6 可用 |
| `equipment.replace` | 新装备替换旧装备 | K6 可用 |
| `equipment.remove` | 装备失去或失效 | K6 预留 |
| `rule.death-equipment-discard` | 阵亡时把装备牌移入弃牌堆 | K6 可用 |

建议 reason 必须继续遵循 `namespace.action[.qualifier]` 形式。内容包不能把 `CardMoved` 宿主通知当作触发技能的入口，也不能在通知回调中修改规则状态。

## 3. 标准内容生命周期

下表是每项当前清单内容的预期 `From → Processing → To`。`—` 表示该内容本身没有独立的牌移动；它只改变被修改牌的合法性或数值。没有 K5/K6/K7 入口的行是契约预留，不是已实现声明。

### 3.1 当前可运行基本牌和核心技能

| 内容 ID | 使用/触发入口 | 生命周期 | reason | 牌面与视图要求 |
| --- | --- | --- | --- | --- |
| `standard:slash` | 对单一合法目标使用 | `Hand(source) → Processing → DiscardPile` | `card.use` → `card.use-finished` | 使用者看到自己的牌；其他玩家不因移动账本获知暗牌 ID |
| `standard:wusheng` | 将红色非杀实体牌按 `Slash` 对单一合法目标使用 | 原牌 `Hand(source) → Processing → DiscardPile` | `card.use` → `card.use-finished` | 事件中的有效牌型为 `Slash`，移动账本保留原始 `CardKind`/实体 ID；`PlayedCardKind` 只描述本次使用，不复制牌 |
| `standard:longdan` | 物理闪按 `Slash` 使用，或物理杀/火杀/雷杀按 `Dodge` 响应 | 原牌 `Hand(owner) → Processing → DiscardPile` | `card.use`/`card.respond` → 对应完成 reason | `PlayedCardKind`、`response-card-kind` 和 `CardRespondedEvent.EffectiveCardKind` 记录有效牌型；响应选择只向当前 responder 发布，不复制实体牌 |
| `standard:fire_slash` | 对单一合法目标使用，未被闪避时造成火焰伤害 | `Hand(source) → Processing → DiscardPile` | `card.use` → `card.use-finished` | 共用杀的合法性和闪响应；`DamageNature.Fire` 在伤害帧和伤害事件中保持一致 |
| `standard:thunder_slash` | 对单一合法目标使用，未被闪避时造成雷电伤害 | `Hand(source) → Processing → DiscardPile` | `card.use` → `card.use-finished` | 共用杀的合法性和闪响应；`DamageNature.Thunder` 在伤害帧和伤害事件中保持一致 |
| `standard:dodge` | 闪响应 | `Hand(responder) → Processing → DiscardPile` | `card.respond` → `card.response-finished` | 只有响应者能从手牌选择；旁观者只看到公开结算结果 |
| `standard:peach` | 自己受伤后的出牌阶段回复 | `Hand(owner) → Processing → DiscardPile` | `card.use` → `card.use-finished` | 不公开其他玩家手牌；满血/错误阶段不得移动 |
| `standard:alcohol` | 出牌阶段无目标使用，准备本回合下一张直接杀 +1 伤害；规则 v12 起濒死时仅持有者可用酒自救 1 点体力 | `Hand(owner) → Processing → DiscardPile` | `card.use` → `card.use-finished`；濒死自救使用 `card.use` → `card.use-finished` | `HasAlcoholEffect` 作为公开状态；直接杀声明时消费，未消费则回合结束失效；v3–v11 保留跨座位救援兼容回放，v1/v2 与 v12 起均仅允许持有者自救 |
| `standard:jijiu` | 急救者在濒死窗口将红色非桃实体牌当作桃使用 | `Hand(owner) → Processing → DiscardPile` | `card.use` / `card.use-finished` 的有效牌型为 `Peach`；`DyingResponseEvent.UsedPeachPhysicalCardKind` 保留物理牌型 | 红牌候选只进入当前 responder 的私有 Prompt；移动账本记录原始物理牌，普通快照不泄漏牌 ID；黑牌、非濒死窗口和伪造 Choice 必须拒绝 |
| `standard:duel` | 对单一合法目标使用，双方交替响应杀 | 使用牌与每张响应牌分别经过 `Processing` 后进入弃牌堆 | `card.use` / `card.respond` | `RespondSlash` 只向当前 responder 发布；无人响应的一方承受 1 点伤害；rules v31 起实际伤害来源为最后成功响应者、目标为失败响应者，原始用牌者另行保留，v1–v30 保留旧归因 |
| `standard:draw_two` | 无目标使用后摸两张牌 | 使用牌 `Hand(owner) → Processing → DiscardPile`；效果牌 `DrawPile → Hand(owner)` | `card.use` / `rule.draw` / `card.use-finished` | 目标列表为空；牌堆耗尽时沿用公共重洗入口 |
| `standard:barbarian_assault` | 无目标使用，锁定所有其他存活角色并按座次逐一响应杀 | 使用牌 `Hand(source) → Processing`；每个目标的杀响应独立 `Hand → Processing → DiscardPile`；全部目标完成后使用牌进入 `DiscardPile`，若存活目标触发反馈则父牌可在剩余目标结算期间转入该目标手牌 | `card.use` / `card.respond` / `card.use-finished` / `skill.feedback.claim-damage-card` | `CardUseFrame.TargetIndex` 记录目标游标；每个未响应目标独立进入 1 点伤害/基础濒死链；父帧在整段效果完成前保留，伤害牌至多被反馈取得一次 |
| `standard:arrow_barrage` | 无目标使用，锁定所有其他存活角色并按座次逐一响应闪 | 使用牌 `Hand(source) → Processing`；每个目标的闪响应独立 `Hand → Processing → DiscardPile`；全部目标完成后使用牌进入 `DiscardPile`，若存活目标触发反馈则父牌可在剩余目标结算期间转入该目标手牌 | `card.use` / `card.respond` / `card.use-finished` / `skill.feedback.claim-damage-card` | 与南蛮入侵共用 `CardUseFrame.TargetIndex`；`GroupResponseEvent` 携带 `RequiredCardKind=Dodge`；每个未响应目标独立进入 1 点伤害/基础濒死链，伤害牌至多被反馈取得一次 |
| `standard:peach_garden` | 无目标使用，锁定使用时所有存活角色并按座次逐一恢复 | 使用牌 `Hand(source) → Processing`；恢复目标若受伤则生成 `RecoveryFrame`；全部目标完成后使用牌进入 `DiscardPile` | `card.use` / `card.use-finished` | 共用 `CardUseFrame.TargetIndex`；满血目标不生成恢复事件；`RecoveryAppliedEvent` 只公开已发生的恢复结果 |
| `standard:five_grains` | 无目标使用，锁定使用时所有存活角色并公开展示一张/人 | 使用牌 `Hand(source) → Processing`；展示牌 `DrawPile → Processing`；当前 picker 选择牌 `Processing → Hand(picker)`；若有未选牌则进入 `DiscardPile`；全部 picker 完成后使用牌进入 `DiscardPile` | `card.use` / `card.public-reveal` / `card.harvest-pick` / `card.harvest-discard` / `card.use-finished` | 展示牌进入所有玩家快照；`SelectHarvestCard` prompt 只投影给当前 picker；公共牌列表随选择实时缩减，其他玩家手牌仍隐藏 |
| `standard:dismantlement` | 对一名有手牌、公开装备或公开判定区牌的其他存活角色使用；规则版本 4 手牌目标由来源玩家通过私有不透明牌位选择，公开装备/判定区牌由使用者逐张选择 | 使用牌 `Hand(source) → Processing → DiscardPile`；目标牌从 `Hand(target)`、`Equipment(target)` 或 `Judgment(target)` 经 `Processing → DiscardPile` | `card.use` / `card.effect.dismantlement` / `card.effect.dismantlement-finished` / `card.effect.dismantlement-judgment` / `card.effect.dismantlement-judgment-finished` / `card.use-finished` | 手牌只向来源玩家发布不透明牌位 Choice，公开装备和判定区牌消费带 `TargetCardId` 的精确 Choice；手牌事件保持牌面与实体 ID 脱敏，公开区域事件可带已公开的牌 ID/牌型，可信宿主账本保留完整移动 |
| `classic:qixi` | 规则 v28 中，甘宁将一张黑色手牌或已装备牌当过河拆桥使用 | 实体成本从 `Hand(source)` 或 `Equipment(source)` 进入 `Processing → DiscardPile`；目标牌完全复用过河拆桥移动 | `card.use` / `card.use-finished` 的有效牌型为 Dismantlement；移动记录保留实体牌型与真实来源，期间复用无懈及目标牌事件 | 不能把装备成本伪装成手牌；黑色实体牌校验、目标、公开/暗牌选择、无懈与回放必须一致；v27 不发布该动作 |
| `classic:tuxi` | 规则 v30 中，张辽以至多两名其他角色各一张随机手牌替代普通摸牌 | 每张牌分别 `Hand(target) → Processing → Hand(source)`，移动原因为 `skill.tuxi.gain-card` | `HandCardsGainedBySkillEvent` 只公开发动者、目标座位与总数；可信移动账本保留实体牌 | 目标组合只包含当前有手牌的其他存活角色；牌面不进入公共事件，张辽仅通过自己的私有手牌视图看到结果；v29 继续普通摸牌 |
| `classic:luoyi` | 规则 v31 中，许褚少摸一张并令本回合由自己使用的杀或决斗伤害 +1 | 不产生额外牌移动；Draw 阶段只把普通摸牌数从 2 减为 1，杀/决斗仍沿各自原移动链 | `DrawSkillResolvedEvent` 记录发动与实际摸牌数；`DamageModifiedBySkillEvent` 公开基础/修改后伤害、来源、目标和来源牌 | 私有 Prompt 只向许褚发布；加成要求原始用牌者与实际伤害来源均为许褚，反向决斗伤害不加成；v30 继续普通摸牌与旧决斗归因 |
| `classic:qiangxi` | 规则 v32 中，典韦每个出牌阶段限一次，失去 1 点体力或弃置一张武器牌，对攻击范围内一名其他角色造成 1 点伤害 | 体力分支不移动牌；武器分支将实体成本从 `Hand(source)` 或 `Equipment(source)` 移至 `Processing → DiscardPile`，原因为 `skill.qiangxi.discard`；目标伤害没有来源牌移动 | `SkillHpLostEvent` 或 `SkillCardsDiscardedEvent` 记录成本；`DamageRequestedEvent.SourceCard`、伤害触发事件的来源牌/牌型为空，`ActiveSkillResolvedEvent` 闭合主动技 | 草稿只发布自己的武器候选与攻击范围内目标；装备武器在选择时仍可建立攻击范围，支付后再离开装备区；无牌来源伤害仍进入共享伤害后技能/濒死链，自损先濒死时获救后继续；v31 不发布动作 |
| `classic:duanliang` | 规则 v33 中，徐晃将自己的黑色基本牌或黑色装备牌当兵粮寸断使用，并可选择距离 2 的目标 | 实体牌从 `Hand(source)` 或 `Equipment(source)` 进入 `Processing → Judgment(target)`；结算后仍以原实体牌型进入 `DiscardPile`，移动记录不伪造物理牌种 | 用牌、无懈、`DelayedCardPlacedEvent`/`DelayedCardResolvedEvent` 使用有效 SupplyShortage；判定区公开快照显示有效牌型，可信牌区诊断仍显示原实体牌型 | 判定区映射持久保存有效牌型，供同名去重、观星、拆顺公开选择与判定结算使用；离开判定区即清理。黑色/类别/所有权/距离 2 必须由已发布动作校验，v32 不发布转换或距离修正 |
| `standard:snatch` | 对一名战斗距离为 1 且有手牌、公开装备或公开判定区牌的其他存活角色使用；规则版本 4 手牌目标由来源玩家通过私有不透明牌位选择，公开装备/判定区牌由使用者逐张选择 | 使用牌 `Hand(source) → Processing → DiscardPile`；目标牌从 `Hand(target)`、`Equipment(target)` 或 `Judgment(target)` 经 `Processing → Hand(source)` | `card.use` / `card.effect.snatch` / `card.effect.snatch-finished` / `card.effect.snatch-judgment` / `card.effect.snatch-judgment-finished` / `card.use-finished` | 手牌只向来源玩家发布不透明牌位 Choice，公开装备和判定区牌消费带 `TargetCardId` 的精确 Choice；手牌事件保持牌面与实体 ID 脱敏，公开区域事件可带已公开的牌 ID/牌型，取得后只进入使用者私有手牌视图 |
| `standard:crossbow` / `standard:offensive_horse` / `standard:defensive_horse` / `standard:jade_seal` / `standard:renwang_shield` | 出牌阶段无目标装备到对应 `EquipmentSlot` | 新牌 `Hand(owner) → Processing → Equipment(owner)`；同槽旧牌 `Equipment(owner) → DiscardPile`；阵亡时装备 `Equipment → DiscardPile` | `equipment.use` / `equipment.enter` / `equipment.replace` / `rule.death-equipment-discard` | 装备区和装备牌面是公开状态；`EquipmentChangedEvent` 可带新旧实体 ID，不扩展他人手牌可见性 |
| `standard:bagua` | 规则 v14 起，在普通/火/雷杀或万箭齐发要求闪时可选择公开判定；红色判定视为闪 | 判定牌 `DrawPile → Judgment(owner) → DiscardPile`；来牌继续按原杀或群体牌链从 `Processing` 结算 | `judgment.reveal` / `judgment.finish` | 仅拥有八卦阵且当前需要闪的目标可选择；`JudgmentFrame`、判定事件和无实体牌 ID 的群体响应结果属于可信宿主记录；v1–v13 仅开放直接杀响应 |
| `standard:jianxiong` | 杀造成伤害且伤害牌仍在处理区 | `Processing → Hand(target)` | `skill.jianxiong.claim-damage-card` | 只公开技能结果允许公开的部分；不存在处理区牌时不得凭空生成牌 |
| `standard:feedback` | 目标存活且受到伤害、伤害牌仍在处理区时压入私有触发选择；发动后取得该牌，不发动则让父结算继续 | 发动：`Processing → Hand(target)`；不发动：`Processing → DiscardPile` | `skill.feedback.claim-damage-card` | 发布 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent`；发动时再发布 `DamageCardClaimedEvent`；每张伤害牌至多成功取一次；普通视图不泄漏取得牌 ID，群体父帧可在选择后继续推进 |
| `standard:yiji` | 郭嘉受到伤害后摸两张牌，并从私有候选中选择一张交给其他存活角色，或保留 | 两张牌分别 `DrawPile → Hand(owner)`；赠牌 `Hand(owner) → Hand(target)` | `skill.yiji.draw` / `skill.yiji.give-card` | 赠牌目标和候选牌通过私有 `Yiji` Choice 配对；`DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 只属于可信宿主；本切片每次只赠一张，普通视图不泄漏候选牌面 |
| `standard:yuanhu` | 其他角色受到正伤害后，援护者从自己的私有手牌中选择一张弃置，使固定受伤目标回复 1 点体力 | `Hand(owner) → Processing → DiscardPile`；恢复不移动牌 | `skill.yuanhu.discard` | `Yuanhu` Choice 只投影给技能拥有者；目标固定为本次伤害目标；可信宿主记录 `DamageSkillCardDiscardedEvent`/`RecoveryAppliedEvent`，普通视图不泄漏弃牌 ID |
| `standard:ganglie` | 受到正伤害后可判定；红色时伤害来源私有选择弃两张手牌或承受 1 点伤害 | 判定牌 `DrawPile → Judgment(owner) → DiscardPile`；弃牌分支 `Hand(source) → DiscardPile`；受伤分支进入 `DyingFrame` 后回到原伤害触发游标 | `judgment.reveal` / `judgment.finish` / `skill.ganglie.discard` | 判定结果公开；来源反制 Prompt 只投影给伤害来源；两牌组合只在可信宿主和来源私有视图中出现，`GangliePunishmentResolvedEvent` 记录结果 |
| `standard:guicai` | 判定牌生效前由当前候选拥有者选择替换或跳过 | 旧判定牌 `Judgment(target) → DiscardPile`；替换牌 `Hand(owner) → Processing → Judgment(target) → DiscardPile` | `judgment.finish` / `skill.guicai.replace` | 判定结果和替换后的牌面公开；鬼才 Prompt 只投影给拥有者，替换牌 ID 仅在其私有视图和可信宿主账本中出现；候选顺序冻结在 `JudgmentFrame` |
| `classic:tiandu` | 规则 v22 中，拥有者自己的判定牌生效后选择获得或跳过 | 发动：`Judgment(owner) → Hand(owner)`；跳过：`Judgment(owner) → DiscardPile` | `skill.tiandu.claim-judgment` / `judgment.finish` | 判定结果先公开，再向拥有者投影两项完整 Choice；`JudgmentCardClaimedEvent` 记录结果，答复后恢复八卦、延时牌或刚烈父结算；v1–v21 不创建该窗口 |
| `classic:fanjian` | 规则 v23 中，周瑜出牌阶段限一次选择其他存活角色；目标先选花色，再获得并展示随机源手牌 | `Hand(source) → Processing → Hand(target)` | `skill.fanjian.give-card` | 花色 Prompt 不含源牌 ID/牌面；答复后才确定性随机选择实体牌，`FanjianCardRevealedEvent` 公开结果；花色不同时进入 1 点普通伤害链；v1–v22 不发布动作 |
| `classic:guanxing` | 规则 v24 中，诸葛亮准备阶段可私有观看并排列至多五张牌堆顶牌 | `DrawPile → DrawPile` 同区顺序变化，不生成 `CardMovementRecord` | 无跨区 reason；`GuanxingResolvedEvent` 只公开数量 | Prompt 仅向拥有者投影牌 ID/牌面与剩余顺序；完成时原子验证冻结切片和顶/底精确分区，第一张顶牌最先离堆、第一张底牌最深；v1–v23 不发布选择 |
| `classic:hujia` | 规则 v25 中，主公曹操需要闪时可依次请求其他存活魏势力角色提供响应 | 实体闪/转化牌 `Hand(provider) → Processing → DiscardPile`；提供者八卦阵走自己的 `DrawPile → Judgment(provider) → DiscardPile` | `response.card` / `response.card-finished`；八卦沿 `judgment.reveal` / `judgment.finish` | `HujiaRequestedEvent`/`HujiaResolvedEvent` 记录公开请求与结果；私有 Choice 只含当前提供者自己的候选，实体牌提供者与 `CardRespondedEvent` 的有效 responder（曹操）分开；八卦失败继续候选游标，全部失败恢复曹操原响应窗；v1–v24 不发布选择 |
| `classic:jijiang` | 规则 v26 中，主公刘备在出牌阶段或决斗/南蛮入侵杀响应窗请求其他存活蜀势力角色提供杀 | 主动使用为 `Hand(provider) → Processing → DiscardPile`（`card.use` / `card.use-finished`）；响应为同路径但使用 `response.card` / `response.card-finished` | `JijiangRequestedEvent` / `JijiangResolvedEvent`；随后复用 `CardUsedEvent` 或 `CardRespondedEvent` | 私有 Choice 只含当前提供者自己的实体杀/转化牌；物理来源为 provider，有效 source/responder 为刘备，属性杀牌型保留。主动目标、范围和次数按刘备校验；全部失败不移动牌、不消耗次数，响应分支恢复刘备原候选；v1–v25 不发布动作 |
| `classic:jiuyuan` | 规则 v27 中，其他吴势力角色对濒死主公孙权使用桃 | `Hand(provider) → Processing → DiscardPile`，完全复用桃的 `card.use` / `card.use-finished` | `JiuyuanAppliedEvent` 后接 `RecoveryAppliedEvent(Amount=2)` | 不产生额外牌或 Prompt；提供者必须是另一名吴势力角色，目标必须是拥有救援的主公且正处于濒死桃入口。孙权自救、非吴桃、酒、非濒死桃和 v1–v26 均保持 1 点回复 |
| `standard:paoxiao` | 修改出杀次数 | — | — | 不创造或移动牌；合法性仍由 Core 判断 |
| `standard:yingzi` | 修改摸牌数量 | `DrawPile → Hand(owner)`（由摸牌动作产生） | `rule.draw` | 只改变数量 modifier，不公开牌堆顺序 |
| `standard:kongcheng` | 修改空手牌时的目标合法性 | — | — | 只读取目标手牌数量/规则公开信息，不读取目标牌面 |

### 3.2 K3/K5/K6 及后续标准内容

| 内容 ID | 生命周期契约 | reason 建议 | 依赖与限制 |
| --- | --- | --- | --- |
| `standard:rende` | `Hand(owner) → Processing → Hand(recipient)`；满足数量条件后恢复 | `skill.rende.give` | K2/K5；不得用 UI 直接改目标手牌 |
| `standard:qingnang` | `Hand(owner) → Processing → DiscardPile`；弃置一张手牌后令一名受伤角色回复 1 点体力 | `skill.qingnang.discard` | K5；精确手牌 ID 只属于拥有者 Prompt 与可信宿主，目标按公开存活/受伤状态筛选 |
| `standard:huichun` | `Hand(owner) → Processing → DiscardPile`；弃置两张手牌后令两至三名受伤角色各回复 1 点体力 | `skill.huichun.discard` | K5；精确手牌 ID 只属于拥有者 Prompt 与可信宿主，目标按公开存活/受伤状态筛选，每个目标使用独立 `RecoveryFrame` |
| `standard:zhiheng` | 规则 v17 经典身份为 `Hand(owner)` / `Equipment(owner) → Processing → DiscardPile`，随后等量 `DrawPile → Hand(owner)`；旧规则仅 `Hand(owner)` | `skill.zhiheng.discard` / `skill.zhiheng.draw` | K2/K5/K6；混合来源逐张校验，弃牌与摸牌属于同一可回放结算流；暗手牌选择只属于拥有者与可信宿主，装备来源公开 |
| `standard:dismantlement` | 使用牌 `Hand(source) → Processing → DiscardPile`；目标一张手牌由来源玩家选择不透明牌位，或由 `TargetCardId` 精确选择公开装备/判定区牌后经 `Processing → DiscardPile` | `card.effect.dismantlement` / `card.effect.dismantlement-finished` / `card.effect.dismantlement-judgment` / `card.effect.dismantlement-judgment-finished` | K5/K7 受限切片；只开放已公开区域，规则版本 4 私有牌位候选已开放，更复杂目标区域仍待后续 |
| `standard:snatch` | 使用牌 `Hand(source) → Processing → DiscardPile`；目标一张手牌由来源玩家选择不透明牌位，或由 `TargetCardId` 精确选择公开装备/判定区牌后经 `Processing → Hand(source)`（战斗距离一） | `card.effect.snatch` / `card.effect.snatch-finished` / `card.effect.snatch-judgment` / `card.effect.snatch-judgment-finished` | K5/K7 受限切片；只开放已公开区域，规则版本 4 私有牌位候选已开放，更复杂目标区域仍待后续 |
| `standard:nullification` | 每张无懈独立 `Hand → Processing → DiscardPile`；按固定座次进入有限多层响应窗口 | `card.respond.nullification` / `card.respond.nullification-finished` | K7 基础切片；当前效果牌与每张响应牌均保持 `Processing` 语义，私有 Prompt 不向其他 viewer 投影 |
| `standard:peach_garden` | `Hand → Processing → DiscardPile`；效果按目标顺序回复，逐目标可暂停 | `card.use` / `card.use-finished` | K5；目标列表由 Core 固定，恢复子帧使用 `RecoveryFrame` |
| `standard:five_grains` | `Hand(source) → Processing`；公开牌 `DrawPile → Processing`，逐人选择后 `Processing → Hand(picker)` | `card.use` / `card.public-reveal` / `card.harvest-pick` / `card.harvest-discard` / `card.use-finished` | K5；公共展示牌是唯一新增牌面可见性，选牌 prompt 仍按 responder 隔离 |
| `standard:arrow_barrage` | 使用牌进入处理区；每张闪响应独立经过处理区后弃置 | `card.use` / `card.respond` | K5；群体响应不能泄漏未响应者手牌；已由通用群体入口实现 |
| `standard:iron_chain` | 使用牌 `Hand → Processing → DiscardPile`；目标公开状态标记由效果帧维护，火/雷伤害按固定顺序传导 | `card.use` / `card.effect.iron-chain` / `card.effect.iron-chain-finished` / `card.use-finished` | K5/K7；标记不是一张可移动的牌，目标只允许精确一名或两名其他存活角色 |
| `standard:crossbow` / `standard:offensive_horse` / `standard:defensive_horse` / `standard:jade_seal` / `standard:renwang_shield` | K6：`Hand(owner) → Processing → Equipment(owner, slot)`；替换旧装备 `Equipment → DiscardPile`；阵亡时装备区整体清理 | `equipment.use` / `equipment.enter` / `equipment.replace` / `rule.death-equipment-discard` | K6 基础切片；装备区与五类槽位由 Core 校验，距离/范围/摸牌 modifier 不由 UI 直接修改 |
| `standard:bagua` | K7：直接杀响应中的公开判定牌 `DrawPile → Judgment(owner) → DiscardPile`；红色判定视为闪 | `judgment.reveal` / `judgment.finish` | K7 基础切片；`JudgmentFrame` 与判定事件属于可信宿主记录，复杂改判和复杂多层判定仍未开放 |
| `standard:guicai` | K7：判定生效前由当前鬼才候选替换或保留判定牌 | 旧牌 `Judgment(target) → DiscardPile`；替换牌 `Hand(owner) → Processing → Judgment(target) → DiscardPile` | `judgment.finish` / `skill.guicai.replace` | K7 基础改判切片；`JudgmentFrame` 保存候选座位和游标，Prompt 只给当前拥有者，最终判定结果公开 |
| `standard:indulgence` | K7 + rules v11：使用牌公开进入目标判定区，目标下回合摸牌前判定；非红桃跳过出牌阶段、红桃正常出牌 | 使用牌 `Hand(source) → Processing → Judgment(target)`；判定牌 `DrawPile → Judgment(target) → DiscardPile`；延时牌 `Judgment(target) → DiscardPile` | `card.effect.delayed-place` / `judgment.reveal` / `judgment.finish` / `card.effect.delayed-finish` | 正式花色延时判定切片；目标重复、无懈窗口、鬼才改判和死亡清理沿 Core 统一入口，判定区与结果公开；v1–v10 保留历史语义 |
| `standard:supply_shortage` | K7 + rules v11：使用牌公开进入目标判定区，目标下回合摸牌前判定；非梅花跳过摸牌阶段、梅花正常摸牌 | 使用牌 `Hand(source) → Processing → Judgment(target)`；判定牌 `DrawPile → Judgment(target) → DiscardPile`；延时牌 `Judgment(target) → DiscardPile` | `card.effect.delayed-place` / `judgment.reveal` / `judgment.finish` / `card.effect.delayed-finish` | 正式花色延时判定切片；只允许有公开手牌的目标，目标重复、无懈窗口、鬼才改判和死亡清理沿 Core 统一入口，判定区与结果公开；v1–v10 保留历史语义 |
| `standard:lightning` | K7：只能对自己使用，进入自己的公开判定区；下个回合判定为黑桃 2 至 9 时造成 3 点雷电伤害，否则转移到下一名存活角色的判定区 | 使用牌 `Hand(source) → Processing → Judgment(source)`；判定牌 `DrawPile → Judgment(source) → DiscardPile`；命中后延时牌 `Judgment(source) → DiscardPile`，未命中按 `Judgment(current) → Judgment(next)` 转移 | `card.effect.delayed-place` / `card.effect.delayed-transfer` / `judgment.reveal` / `judgment.finish` / `card.effect.delayed-finish` | K7 基础延时判定切片；自用、黑桃 2 至 9 命中、3 点雷电伤害和下一存活角色转移由 Core 校验，结果公开，鬼才/无懈 Prompt 仍按 responder 隔离 |
| `standard:qinggang_sword` | `Hand(owner) → Processing → Equipment(owner, Weapon)`；直接杀声明读取装备 modifier 并在 `CardUseFrame`/类型化事件记录无视防具 | `equipment.use` / `equipment.enter` / `equipment.replace` | K6 基础切片；复杂失效/卸载仍待后续 |
| `standard:renwang_shield` | `Hand(owner) → Processing → Equipment(owner, Armor)`；规则 v14 起黑色杀可指定装备者，随后由防具令效果无效；青釭剑直接杀可绕过 | `equipment.use` / `equipment.enter` / `equipment.replace` | 装备状态与 `ArmorEffectAppliedEvent` 公开；杀仍正常从手牌进入处理区并在无效后弃置；v1–v13 保留目标合法性过滤 |

## 4. 玩家视图和宿主诊断边界

| 信息 | 普通玩家视图 | 可信宿主/开发诊断 |
| --- | --- | --- |
| 自己的手牌牌面和实体 ID | 可见 | 可见 |
| 其他玩家手牌牌面、实体 ID、候选牌 | 不可见，只能看到规则允许的数量/公开结果 | 可见，但不得下发 |
| 摸牌堆顺序、随机种子 | 不可见 | 可见 |
| 处理区 | 按现有快照只公开 `ProcessingCardCount`；具体实体牌仅在规则明确公开时显示 | `CardZoneDiagnostic` 与完整移动账本可见 |
| `CardMovementRecord` | 不直接发送 | 可信宿主可记录、测试和录像使用 |
| `CardMoved` | 可作为宿主观察通知的输入投影；不能在回调中改变规则 | 可订阅；异常隔离，禁止重入 |
| AI 思考、隐藏身份推断 | 不可见 | 仅开发诊断可见，不能作为内容合法性输入 |
| 当前玩家 PromptId、完整 Choice | 仅 responder 可见 | 可信宿主可记录；不得下发给其他玩家 |
| 当前玩家 General candidates | 仅 responder 可见 | `PendingDecision.ValidContentIds`/`PromptChoice.ContentIds`；选将完成前不得进入公共快照 |

内容测试必须同时断言：公开快照没有多余字段、私有选择只发给授权交互者、相同 seed 和命令流下移动顺序可复现。完整隐藏信息策略仍需随 K5/K6/K9 扩展继续验证。

## 5. K1/K5 强制场景规格

每个场景都要检查“正向结果 + 非法来源/目标拒绝 + 牌区守恒 + 视图不泄漏 + 固定 seed 可重放”。

| 场景 ID | Given / When | 必须成立 | 必须拒绝 |
| --- | --- | --- | --- |
| `k1.slash.hit` | 合法来源持有杀，对合法目标使用 | 杀按 `Hand → Processing → DiscardPile`，结算顺序稳定，实体牌只出现一次 | 自身、死亡目标、错误阶段或不在手牌的牌不得移动 |
| `k5.attribute-slash.nature` | 合法来源使用火杀或雷杀，目标未打出闪 | 使用牌仍按 `Hand → Processing → DiscardPile`，`DamageNature` 在 `DamageFrame`、`DamageRequestedEvent`、`DamageAppliedEvent` 和 `AfterDamageEvent` 中与牌种一致 | 闪避后不得生成属性伤害；不得把属性类型只藏在 UI 文案或通过牌名字符串推断 |
| `k5.alcohol.slash-boost` | 合法来源在出牌阶段使用酒，再使用一张直接杀 | 酒牌和杀牌均按 `Hand → Processing → DiscardPile`；酒效公开设置、在杀声明时消费，`DamageFrame` 与三类伤害事件的金额为 2；未消费时回合结束清除并发布 `AlcoholExpiredEvent` | 群体牌/决斗不得消费酒效；同回合重复叠加和非法阶段使用必须拒绝；濒死救援由独立场景覆盖 |
| `k5.alcohol.dying-rescue` | 规则 v12 的濒死者在自己的私有 Prompt 选择酒；v3–v11 兼容 Prompt 可由其他 responder 选择 | 酒按 `Hand → Processing → DiscardPile`；使用独立 `CardUseFrame`/`RecoveryFrame` 回复濒死目标 1 点体力，`DyingResponseEvent` 标记 `UsedAlcohol`，濒死帧随后以 survived 完成 | 同一响应同时使用桃和酒、伪造 Choice、错误牌区来源、v12 非 victim 使用酒或把酒用于群体牌/决斗必须拒绝；普通视图不泄漏酒牌 ID |
| `k1.slash.dodge` | 目标在响应窗口持有闪并提交响应 | 闪按 `Hand → Processing → DiscardPile`，杀的处理区生命周期完整 | 非响应窗口、错误 responder、伪造牌 ID不得移动 |
| `k1.jianxiong.claim` | 杀造成伤害，伤害牌尚在处理区 | 奸雄取得同一实体牌到手牌，不复制、不凭空生成 | 闪避成功、牌已离开处理区、非伤害牌不得触发 |
| `k1.peach` | 受伤角色在允许阶段持有桃 | 桃经过处理区后弃置，回复结果与牌移动同一提交 | 满血、错误阶段、他人私有手牌不得被 UI 代打 |
| `k1.deal.draw` | 开局发牌或摸牌 | 每张牌从摸牌堆进入指定手牌，顺序和总数可复现 | 牌堆不足时不得部分开局或重复实体牌 |
| `k1.hand-limit-discard` | 回合结束手牌超过上限 | 选择/规则指定的牌逐张从手牌到弃牌区，提交后守恒 | 非本人手牌、错误阶段或超过合法数量不得弃置 |
| `k1.death-cleanup` | 角色死亡且仍有手牌 | K1 将手牌整体移入弃牌区；K6 同一死亡提交清理装备区 | 已死亡角色再次清理、错误来源或重复清理不得部分提交 |
| `k1.lord-penalty` | 主公误杀忠臣 | 主公按规则弃置完整惩罚牌集，reason 为 `mode.identity.lord-killed-loyalist` | 非忠臣、非主公或已提交过的击杀不得触发 |
| `k1.reshuffle` | 摸牌堆耗尽且弃牌区非空 | 弃牌区整体重洗回摸牌堆，固定 seed 可复现，牌数守恒 | 空弃牌区不得伪造牌或产生负数牌堆 |

## 6. 验收和后续迁移

- 本批完成标准是本文件与 `CONTENT_MANIFEST.md`、`SCENARIO_MATRIX.md` 的 ID、reason、依赖和可见性描述一致；K3 的 Standard Registry 与 K4 的开局输入已是正式运行路径，复杂行为仍不提前声明完成。
- K4 的选将移动边界是：候选只改变角色初始化状态，不产生牌区移动；所有选将完成后才执行 `DrawPile → Hand` 的 round-robin 初始发牌。
- K3 已将 `definitionId`、牌堆配方和依赖引用迁入不可变 Registry，并覆盖重复 ID、未知引用、版本不足和依赖环测试；可信命令前缀 Checkpoint 已开放，并在恢复前校验内容包签名与归一化内容哈希；完整存档清单和兼容迁移仍留待 K8 后续。
- K5 当前切片已把已有杀/火杀/雷杀/闪/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/无懈可击/铁索连环/伤害/桃/酒路径接到 `CardUseFrame`、`ResponseWindowFrame`、伤害/恢复/死亡子帧和类型化事件；规则 v14 的八卦阵判定可续接直接杀与万箭齐发，仁王盾让已进入 `Processing` 的黑色杀在目标确定后无效，v1–v13 保留旧防具时机。火杀/雷杀的 `DamageNature`、铁索连环、酒、公开 draft、不透明手牌位、无懈窗口和三种延时判定均沿统一帧栈与移动账本；`CardMoved` 仍只作为提交后的宿主诊断。其他复杂延时判定、复杂装备、复杂多层响应和 `CardsMoving/CardsMoved` 规则时机继续按后续阶段开放。
- 本批新增 `standard:feedback` 与 `standard:yiji`：伤害牌仍在 `Processing` 时先压入带游标的 `DamageTriggerWindowFrame`，再为当前候选压入可序列化 `DamageSkillFrame`；反馈通过私有 `Feedback` Choice 决定是否按 `skill.feedback.claim-damage-card` 取得伤害牌，遗计先按 `skill.yiji.draw` 摸两张牌，再通过私有 `Yiji` Choice 按 `skill.yiji.give-card` 将一张牌交给其他存活角色；请求、决定和结果分别由 `DamageSkillRequestedEvent`、`DamageSkillResolvedEvent`、`DamageCardClaimedEvent`、`DamageSkillCardsDrawnEvent`、`DamageSkillCardGivenEvent` 表示，群体父帧可在选择后继续推进，普通快照不泄漏隐藏牌 ID。默认触发范围仍匹配受伤者，但现在由 `DamageTriggerScope.DamagedPlayer` 显式表达；遗计的跨座位部分发生在效果移动阶段。
- `standard:jieming` 复用同一伤害触发窗口：荀彧受伤后通过私有 `Jieming` Choice 选择公开合法的手牌补足目标或跳过；摸牌使用 `skill.jieming.draw`，由 `DamageSkillCardsDrawnEvent.TargetSeat` 和移动账本记录效果目标，普通视图只看到公开目标条件，不看到抽到的牌面。
- `standard:yuanhu` 是当前受约束的非受伤者触发示例：其他角色受到正伤害且目标仍存活、未满体力时，援护者通过 `DamageTriggerScope.OtherLivingPlayer` 和私有 `Yuanhu` Choice 选择自己的一张手牌，按 `skill.yuanhu.discard` 经过 `Processing` 后进入弃牌堆，再压入 `RecoveryFrame` 令固定受伤目标回复 1 点体力；`DamageSkillCardDiscardedEvent` 不向普通视图暴露实体牌 ID，`DamageSkillResolvedEvent.EffectTargetSeat` 记录恢复目标。后续跨座位技能仍需复用该范围契约并自行声明具体合法条件。
- `standard:ganglie` 是受伤者触发后定向来源反制的示例：刚烈者先在私有 `Ganglie` Choice 中选择是否判定，判定牌公开进入 `Judgment(owner)`；红色时伤害来源通过私有 `GangliePunish` Choice 选择精确两张手牌按 `skill.ganglie.discard` 弃置或承受 1 点伤害。反制伤害的濒死处理完成后恢复原技能帧，普通视图不泄漏来源手牌 ID。
- `standard:guicai` 是判定前改判的最小闭环：Core 在 `JudgmentFrame` 中冻结当前存活鬼才拥有者和候选游标，向当前拥有者发布私有 `DecisionKind.Guicai`；替换时旧牌先按 `judgment.finish` 结束，新牌按 `skill.guicai.replace` 经过 `Processing` 进入同一 `Judgment(target)`，所有候选完成后再公开 `JudgmentResolvedEvent`。AI 只从自己的过滤快照和发布的有效手牌 ID 中选择，普通视图不泄漏候选手牌。
- K2 已补齐私有 Choice；K6/K7/K9 开放后继续补齐装备槽/距离、判定/触发和 AI 策略测试；在此之前不得用反射、全局状态、UI 分支或通知回调模拟缺失能力。
