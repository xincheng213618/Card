# C0-K1 卡牌移动契约包

更新时间：2026-09-07

本文件是内容侧对 K1 牌区能力的消费契约，并记录 K2/K3/K4/K5 对内容的影响。它描述稳定内容 ID、物理牌实例、移动原因、可见性和场景验收；正式 Registry、可暂停私有选将、杀/火杀/雷杀/决斗/酒/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊链路的类型化结算帧已由 `CardGame.Content.Standard`/Core 提供，通用 `CardsMoving/CardsMoved` 规则时机、复杂技能触发和完整响应链仍等待后续 K5/K6。

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

区域名称使用 K1 的 `CardLocation` 语义：`DrawPile`、`Hand(seat)`、`Processing`、`DiscardPile`、`Equipment(seat)`、`Judgment(seat)` 和 `OutsideGame`。K1 的 `Equipment(seat)` 只有所属玩家，没有装备槽；`Equipment(seat, slot)` 是 K6 的目标扩展，不能在当前内容契约中当作已实现 API。下表中的 reason ID 与现有 `CardMoveReasons` 对齐；标为“建议”的 ID 只用于后续内容包设计，不能假定当前 Core 已提供。

| reason ID | 用途 | 当前状态 |
| --- | --- | --- |
| `setup.initial-deal` | 选将完成后逐张发牌 | K1/K4 可用 |
| `rule.draw` | 摸牌进入手牌 | K1 可用 |
| `card.use` | 使用者把基本牌/锦囊送入处理区 | K1 可用 |
| `card.respond` | 响应者把响应牌送入处理区 | K1 可用 |
| `card.response-finished` | 响应牌结算完成进入弃牌区 | K1 可用 |
| `card.use-finished` | 使用牌结算完成进入弃牌区 | K1 可用 |
| `card.public-reveal` | 五谷丰登把公开牌从摸牌堆翻入处理区 | K5 可用 |
| `card.harvest-pick` | 当前 picker 把公开牌移入自己的手牌 | K5 可用 |
| `card.harvest-discard` | 五谷丰登结束时清理未选的公开牌 | K5 可用 |
| `card.effect.dismantlement` | 过河拆桥把目标一张隐藏手牌送入处理区 | K5 可用 |
| `card.effect.dismantlement-finished` | 过河拆桥把目标牌从处理区送入弃牌区 | K5 可用 |
| `card.effect.snatch` | 顺手牵羊把目标一张隐藏手牌送入处理区 | K5 可用 |
| `card.effect.snatch-finished` | 顺手牵羊把目标牌从处理区送入使用者手牌 | K5 可用 |
| `skill.jianxiong.claim-damage-card` | 奸雄从处理区取得伤害牌 | K1 可用 |
| `skill.feedback.claim-damage-card` | 反馈在目标存活且伤害牌仍在处理区时取得伤害牌 | K5 可用 |
| `rule.hand-limit-discard` | 手牌上限弃牌 | K1 可用 |
| `rule.death-discard` | 阵亡时清理当前可用的手牌；装备/判定区清理留待后续区域能力 | K1 手牌可用；装备 K6、判定 K7 目标 |
| `mode.identity.lord-killed-loyalist` | 主公误杀忠臣的惩罚弃牌 | K1 可用 |
| `deck.reshuffle` | 弃牌区重洗回摸牌堆 | K1 可用 |
| `skill.yiji.draw` | 遗计受伤后摸牌进入拥有者手牌 | K5 可用 |
| `skill.yiji.give-card` | 遗计把拥有者的一张摸牌交给其他存活角色 | K5 可用 |
| `skill.yuanhu.discard` | 援护在其他角色受伤后弃置拥有者的一张手牌 | K5 可用 |
| `equipment.equip` | 装备进入对应槽位 | K6 建议 |
| `equipment.replace` | 新装备替换旧装备 | K6 建议 |
| `equipment.remove` | 装备失去、失效或死亡清理 | K6 建议 |

建议 reason 必须继续遵循 `namespace.action[.qualifier]` 形式。内容包不能把 `CardMoved` 宿主通知当作触发技能的入口，也不能在通知回调中修改规则状态。

## 3. 标准内容生命周期

下表是每项当前清单内容的预期 `From → Processing → To`。`—` 表示该内容本身没有独立的牌移动；它只改变被修改牌的合法性或数值。没有 K5/K6 入口的行是契约预留，不是已实现声明。

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
| `standard:alcohol` | 出牌阶段无目标使用，准备本回合下一张直接杀 +1 伤害；濒死时由持有者自救 1 点体力 | `Hand(owner) → Processing → DiscardPile` | `card.use` → `card.use-finished`；濒死自救使用 `card.use` → `card.use-finished` | `HasAlcoholEffect` 作为公开状态；直接杀声明时消费，未消费则回合结束失效；濒死时仅由持有者自救，不支持救援他人 |
| `standard:duel` | 对单一合法目标使用，双方交替响应杀 | 使用牌与每张响应牌分别经过 `Processing` 后进入弃牌堆 | `card.use` / `card.respond` | `RespondSlash` 只向当前 responder 发布；无人响应的一方承受 1 点伤害 |
| `standard:draw_two` | 无目标使用后摸两张牌 | 使用牌 `Hand(owner) → Processing → DiscardPile`；效果牌 `DrawPile → Hand(owner)` | `card.use` / `rule.draw` / `card.use-finished` | 目标列表为空；牌堆耗尽时沿用公共重洗入口 |
| `standard:barbarian_assault` | 无目标使用，锁定所有其他存活角色并按座次逐一响应杀 | 使用牌 `Hand(source) → Processing`；每个目标的杀响应独立 `Hand → Processing → DiscardPile`；全部目标完成后使用牌进入 `DiscardPile`，若存活目标触发反馈则父牌可在剩余目标结算期间转入该目标手牌 | `card.use` / `card.respond` / `card.use-finished` / `skill.feedback.claim-damage-card` | `CardUseFrame.TargetIndex` 记录目标游标；每个未响应目标独立进入 1 点伤害/基础濒死链；父帧在整段效果完成前保留，伤害牌至多被反馈取得一次 |
| `standard:arrow_barrage` | 无目标使用，锁定所有其他存活角色并按座次逐一响应闪 | 使用牌 `Hand(source) → Processing`；每个目标的闪响应独立 `Hand → Processing → DiscardPile`；全部目标完成后使用牌进入 `DiscardPile`，若存活目标触发反馈则父牌可在剩余目标结算期间转入该目标手牌 | `card.use` / `card.respond` / `card.use-finished` / `skill.feedback.claim-damage-card` | 与南蛮入侵共用 `CardUseFrame.TargetIndex`；`GroupResponseEvent` 携带 `RequiredCardKind=Dodge`；每个未响应目标独立进入 1 点伤害/基础濒死链，伤害牌至多被反馈取得一次 |
| `standard:peach_garden` | 无目标使用，锁定使用时所有存活角色并按座次逐一恢复 | 使用牌 `Hand(source) → Processing`；恢复目标若受伤则生成 `RecoveryFrame`；全部目标完成后使用牌进入 `DiscardPile` | `card.use` / `card.use-finished` | 共用 `CardUseFrame.TargetIndex`；满血目标不生成恢复事件；`RecoveryAppliedEvent` 只公开已发生的恢复结果 |
| `standard:five_grains` | 无目标使用，锁定使用时所有存活角色并公开展示一张/人 | 使用牌 `Hand(source) → Processing`；展示牌 `DrawPile → Processing`；当前 picker 选择牌 `Processing → Hand(picker)`；若有未选牌则进入 `DiscardPile`；全部 picker 完成后使用牌进入 `DiscardPile` | `card.use` / `card.public-reveal` / `card.harvest-pick` / `card.harvest-discard` / `card.use-finished` | 展示牌进入所有玩家快照；`SelectHarvestCard` prompt 只投影给当前 picker；公共牌列表随选择实时缩减，其他玩家手牌仍隐藏 |
| `standard:dismantlement` | 对一名有手牌的其他存活角色使用，核心盲弃一张目标手牌 | 使用牌 `Hand(source) → Processing → DiscardPile`；目标牌 `Hand(target) → Processing → DiscardPile` | `card.use` / `card.effect.dismantlement` / `card.effect.dismantlement-finished` / `card.use-finished` | 使用者只选择目标；被弃牌 ID/牌面不进入普通快照或 `TargetCardDiscardedEvent`，可信宿主账本保留完整移动；当前不支持装备/判定区 |
| `standard:snatch` | 对一名距离为 1 且有手牌的其他存活角色使用，核心盲取一张目标手牌 | 使用牌 `Hand(source) → Processing → DiscardPile`；目标牌 `Hand(target) → Processing → Hand(source)` | `card.use` / `card.effect.snatch` / `card.effect.snatch-finished` / `card.use-finished` | 使用者只选择目标；被取牌 ID/牌面不进入普通快照或 `TargetCardTakenEvent`，取得后只在使用者私有快照中可见；距离只按座位环计算 |
| `standard:jianxiong` | 杀造成伤害且伤害牌仍在处理区 | `Processing → Hand(target)` | `skill.jianxiong.claim-damage-card` | 只公开技能结果允许公开的部分；不存在处理区牌时不得凭空生成牌 |
| `standard:feedback` | 目标存活且受到伤害、伤害牌仍在处理区时压入私有触发选择；发动后取得该牌，不发动则让父结算继续 | 发动：`Processing → Hand(target)`；不发动：`Processing → DiscardPile` | `skill.feedback.claim-damage-card` | 发布 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent`；发动时再发布 `DamageCardClaimedEvent`；每张伤害牌至多成功取一次；普通视图不泄漏取得牌 ID，群体父帧可在选择后继续推进 |
| `standard:yiji` | 郭嘉受到伤害后摸两张牌，并从私有候选中选择一张交给其他存活角色，或保留 | 两张牌分别 `DrawPile → Hand(owner)`；赠牌 `Hand(owner) → Hand(target)` | `skill.yiji.draw` / `skill.yiji.give-card` | 赠牌目标和候选牌通过私有 `Yiji` Choice 配对；`DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 只属于可信宿主；本切片每次只赠一张，普通视图不泄漏候选牌面 |
| `standard:yuanhu` | 其他角色受到正伤害后，援护者从自己的私有手牌中选择一张弃置，使固定受伤目标回复 1 点体力 | `Hand(owner) → Processing → DiscardPile`；恢复不移动牌 | `skill.yuanhu.discard` | `Yuanhu` Choice 只投影给技能拥有者；目标固定为本次伤害目标；可信宿主记录 `DamageSkillCardDiscardedEvent`/`RecoveryAppliedEvent`，普通视图不泄漏弃牌 ID |
| `standard:paoxiao` | 修改出杀次数 | — | — | 不创造或移动牌；合法性仍由 Core 判断 |
| `standard:yingzi` | 修改摸牌数量 | `DrawPile → Hand(owner)`（由摸牌动作产生） | `rule.draw` | 只改变数量 modifier，不公开牌堆顺序 |
| `standard:kongcheng` | 修改空手牌时的目标合法性 | — | — | 只读取目标手牌数量/规则公开信息，不读取目标牌面 |

### 3.2 K3/K5/K6 后续标准内容

| 内容 ID | 生命周期契约 | reason 建议 | 依赖与限制 |
| --- | --- | --- | --- |
| `standard:guicai` | 用手牌替换判定牌：`Hand(owner) → Processing → Judgment(target)`，旧判定牌按结算规则离开 | `card.respond` 或 K7 专用 reason | K5/K7；当前不实现 |
| `standard:rende` | `Hand(owner) → Processing → Hand(recipient)`；满足数量条件后恢复 | `skill.rende.give` | K2/K5；不得用 UI 直接改目标手牌 |
| `standard:zhiheng` | `Hand(owner) → Processing → DiscardPile`，随后等量 `DrawPile → Hand(owner)` | `skill.zhiheng.discard` / `rule.draw` | K2/K5；弃牌与摸牌必须同一可回放结算流 |
| `standard:dismantlement` | 使用牌 `Hand(source) → Processing → DiscardPile`；目标一张隐藏手牌 `Hand(target) → Processing → DiscardPile`（确定性盲选） | `card.effect.dismantlement` / `card.effect.dismantlement-finished` | K5 最小切片；装备/判定区和真正的目标私有候选仍待后续 |
| `standard:snatch` | 使用牌 `Hand(source) → Processing → DiscardPile`；目标一张隐藏手牌 `Hand(target) → Processing → Hand(source)`（距离一，确定性盲选） | `card.effect.snatch` / `card.effect.snatch-finished` | K5 最小切片；当前只支持座位环距离，装备区和真正的目标私有候选仍待后续 |
| `standard:nullification` | 每张无懈独立 `Hand → Processing → DiscardPile` | `card.respond` | K5/K7；响应链顺序固定 |
| `standard:peach_garden` | `Hand → Processing → DiscardPile`；效果按目标顺序回复，逐目标可暂停 | `card.use` / `card.use-finished` | K5；目标列表由 Core 固定，恢复子帧使用 `RecoveryFrame` |
| `standard:five_grains` | `Hand(source) → Processing`；公开牌 `DrawPile → Processing`，逐人选择后 `Processing → Hand(picker)` | `card.use` / `card.public-reveal` / `card.harvest-pick` / `card.harvest-discard` / `card.use-finished` | K5；公共展示牌是唯一新增牌面可见性，选牌 prompt 仍按 responder 隔离 |
| `standard:arrow_barrage` | 使用牌进入处理区；每张闪响应独立经过处理区后弃置 | `card.use` / `card.respond` | K5；群体响应不能泄漏未响应者手牌；已由通用群体入口实现 |
| `standard:iron_chain` | 使用牌 `Hand → Processing → DiscardPile`；目标状态标记由效果帧维护 | `card.use` / `card.use-finished` | K5/K7；标记不是一张可移动的牌 |
| `standard:crossbow` / `standard:qinggang_sword` / `standard:bagua` | K6 目标：`Hand(owner) → Equipment(owner, slot)`；替换旧装备 `Equipment → DiscardPile` | `equipment.equip` / `equipment.replace` | K6；K1 尚无 slot，装备不能伪装成普通弃牌 |
| `standard:offensive_horse` / `standard:defensive_horse` | K6 目标：`Hand(owner) → Equipment(owner, horse-slot)`；失去时 `Equipment → DiscardPile` | `equipment.equip` / `equipment.remove` | K6；距离 modifier 不由 UI 直接修改 |

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
| `k5.alcohol.slash-boost` | 合法来源在出牌阶段使用酒，再使用一张直接杀 | 酒牌和杀牌均按 `Hand → Processing → DiscardPile`；酒效公开设置、在杀声明时消费，`DamageFrame` 与三类伤害事件的金额为 2；未消费时回合结束清除并发布 `AlcoholExpiredEvent` | 群体牌/决斗不得消费酒效；同回合重复叠加、用酒救援他人和非法阶段使用必须拒绝；濒死自救由独立场景覆盖 |
| `k5.alcohol.dying-rescue` | 濒死者在私有濒死 Prompt 选择酒 | 酒按 `Hand → Processing → DiscardPile`；使用独立 `CardUseFrame`/`RecoveryFrame` 回复 1 点体力，`DyingResponseEvent` 标记 `UsedAlcohol`，濒死帧随后以 survived 完成 | 非濒死响应者、用酒救援他人、同一响应同时使用桃和酒、伪造 Choice 或错误牌区来源必须拒绝；普通视图不泄漏酒牌 ID |
| `k1.slash.dodge` | 目标在响应窗口持有闪并提交响应 | 闪按 `Hand → Processing → DiscardPile`，杀的处理区生命周期完整 | 非响应窗口、错误 responder、伪造牌 ID不得移动 |
| `k1.jianxiong.claim` | 杀造成伤害，伤害牌尚在处理区 | 奸雄取得同一实体牌到手牌，不复制、不凭空生成 | 闪避成功、牌已离开处理区、非伤害牌不得触发 |
| `k1.peach` | 受伤角色在允许阶段持有桃 | 桃经过处理区后弃置，回复结果与牌移动同一提交 | 满血、错误阶段、他人私有手牌不得被 UI 代打 |
| `k1.deal.draw` | 开局发牌或摸牌 | 每张牌从摸牌堆进入指定手牌，顺序和总数可复现 | 牌堆不足时不得部分开局或重复实体牌 |
| `k1.hand-limit-discard` | 回合结束手牌超过上限 | 选择/规则指定的牌逐张从手牌到弃牌区，提交后守恒 | 非本人手牌、错误阶段或超过合法数量不得弃置 |
| `k1.death-cleanup` | 角色死亡且仍有手牌 | K1 当前将手牌整体移入弃牌区；装备/判定区清理分别作为 K6/K7 扩展 | 已死亡角色再次清理、错误来源或重复清理不得部分提交 |
| `k1.lord-penalty` | 主公误杀忠臣 | 主公按规则弃置完整惩罚牌集，reason 为 `mode.identity.lord-killed-loyalist` | 非忠臣、非主公或已提交过的击杀不得触发 |
| `k1.reshuffle` | 摸牌堆耗尽且弃牌区非空 | 弃牌区整体重洗回摸牌堆，固定 seed 可复现，牌数守恒 | 空弃牌区不得伪造牌或产生负数牌堆 |

## 6. 验收和后续迁移

- 本批完成标准是本文件与 `CONTENT_MANIFEST.md`、`SCENARIO_MATRIX.md` 的 ID、reason、依赖和可见性描述一致；K3 的 Standard Registry 与 K4 的开局输入已是正式运行路径，复杂行为仍不提前声明完成。
- K4 的选将移动边界是：候选只改变角色初始化状态，不产生牌区移动；所有选将完成后才执行 `DrawPile → Hand` 的 round-robin 初始发牌。
- K3 已将 `definitionId`、牌堆配方和依赖引用迁入不可变 Registry，并覆盖重复 ID、未知引用、版本不足和依赖环测试；内容哈希和存档清单留待 K8。
- K5 当前切片已把已有杀/火杀/雷杀/闪/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/伤害/桃/酒路径接到 `CardUseFrame`、`ResponseWindowFrame`、伤害/恢复/死亡子帧和类型化事件；火杀/雷杀的 `DamageNature` 从实际 `CardKind` 派生并贯穿伤害帧与伤害事件，酒通过公开 `HasAlcoholEffect`、`AlcoholAppliedEvent`/`AlcoholExpiredEvent` 和实际伤害金额贯穿直接杀链路，并在私有濒死 Prompt 中支持濒死者自救 1 点体力（不支持用酒救援他人），五谷丰登的公开牌使用 `CardsRevealedEvent`，逐人选择使用 `HarvestCardSelectedEvent` 和专用移动 reason，过河拆桥/顺手牵羊使用 `TargetCardDiscardedEvent`/`TargetCardTakenEvent` 保持牌面脱敏；`CardMoved` 仍只作为提交后的宿主诊断。其他锦囊、装备、判定、多层响应和 `CardsMoving/CardsMoved` 规则时机继续按后续阶段开放。
- 本批新增 `standard:feedback` 与 `standard:yiji`：伤害牌仍在 `Processing` 时先压入带游标的 `DamageTriggerWindowFrame`，再为当前候选压入可序列化 `DamageSkillFrame`；反馈通过私有 `Feedback` Choice 决定是否按 `skill.feedback.claim-damage-card` 取得伤害牌，遗计先按 `skill.yiji.draw` 摸两张牌，再通过私有 `Yiji` Choice 按 `skill.yiji.give-card` 将一张牌交给其他存活角色；请求、决定和结果分别由 `DamageSkillRequestedEvent`、`DamageSkillResolvedEvent`、`DamageCardClaimedEvent`、`DamageSkillCardsDrawnEvent`、`DamageSkillCardGivenEvent` 表示，群体父帧可在选择后继续推进，普通快照不泄漏隐藏牌 ID。现有内置技能的触发条件默认仍只匹配受伤者，遗计的跨座位部分发生在效果移动阶段。
- `standard:jieming` 复用同一伤害触发窗口：荀彧受伤后通过私有 `Jieming` Choice 选择公开合法的手牌补足目标或跳过；摸牌使用 `skill.jieming.draw`，由 `DamageSkillCardsDrawnEvent.TargetSeat` 和移动账本记录效果目标，普通视图只看到公开目标条件，不看到抽到的牌面。
- `standard:yuanhu` 是当前唯一显式跨座位触发示例：其他角色受到正伤害且目标仍存活、未满体力时，援护者通过私有 `Yuanhu` Choice 选择自己的一张手牌，按 `skill.yuanhu.discard` 经过 `Processing` 后进入弃牌堆，再压入 `RecoveryFrame` 令固定受伤目标回复 1 点体力；`DamageSkillCardDiscardedEvent` 不向普通视图暴露实体牌 ID，`DamageSkillResolvedEvent.EffectTargetSeat` 记录恢复目标。
- K2 已补齐私有 Choice；K6/K7/K9 开放后继续补齐装备槽/距离、判定/触发和 AI 策略测试；在此之前不得用反射、全局状态、UI 分支或通知回调模拟缺失能力。
