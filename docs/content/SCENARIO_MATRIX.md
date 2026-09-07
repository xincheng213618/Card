# 内容场景矩阵（C0 / C0-K1 / C0-K2 / C0-K3 / C0-K4 / C0-K5）

场景 ID 作为内容测试契约；正式 Content/Scenario 测试项目在对应核心入口开放后承载这些场景。当前已实现的基础牌、命令和 Registry 场景由 `tests/CardGame.Core.Tests/Program.cs` 的 Console 自测覆盖。K1 的完整移动契约见 [`CARD_MOVEMENT_CONTRACT.md`](./CARD_MOVEMENT_CONTRACT.md)。

| 场景 ID | 内容 | 正向断言 | 拒绝/边界断言 | 视图与确定性 |
| --- | --- | --- | --- | --- |
| `basic.slash.target` | `standard:slash` | 合法目标受到 1 点伤害或进入闪响应 | 自身、死亡角色、空城角色不可选 | 相同 seed/命令结果一致 |
| `basic.elemental-slash.nature` | `standard:fire_slash` / `standard:thunder_slash` | 共用杀的目标与闪响应；未被闪避时分别产生 `DamageNature.Fire` / `DamageNature.Thunder`，伤害帧和三类伤害事件保持一致 | 闪避后不得产生伤害；自身、死亡角色、空城角色或伪造属性类型被拒绝；属性抗性尚未实现 | AI 只消费自己的手牌和公开目标信息；相同 seed/命令结果一致 |
| `basic.alcohol.slash_boost` | `standard:alcohol` | 出牌阶段无目标使用，公开设置一次性酒效；下一张直接杀声明时消费并造成 2 点伤害，未消费时回合结束失效 | 同回合重复使用、群体牌/决斗消费、用酒救援他人和非法阶段使用必须拒绝；酒牌与杀牌均完整经过 `Processing` | AI 只读取本座手牌中的杀数量和公开状态；固定 seed/命令流下事件、金额和牌区一致 |
| `basic.alcohol.dying_rescue` | `standard:alcohol` | 濒死者在私有 `RescueDying` prompt 使用酒，自身恢复 1 点体力；酒牌经过 `Hand → Processing → DiscardPile`，`DyingResponseEvent.UsedAlcohol` 为真并完成濒死帧 | 非濒死者不能用酒救援他人；同一响应不能同时使用桃和酒；伪造 Choice、错误牌区来源或把酒用于群体牌/决斗必须拒绝 | 其他 viewer 看不到 prompt 和酒牌 ID；AI 只读取自己的快照/手牌；固定 seed/命令流可复现 |
| `basic.dodge.response` | `standard:dodge` | 闪抵消杀并进入弃牌区 | 非响应窗口不能打闪；无闪不能伪造响应 | 他人看不到手牌牌 ID |
| `basic.peach.recovery` | `standard:peach` | 受伤角色回复 1 点体力 | 满血不能使用；当前 Demo 只允许自救 | AI 只能看到自身手牌 |
| `trick.draw_two.immediate` | `standard:draw_two` | 无目标使用后摸两张牌，使用牌进入弃牌区 | 伪造目标、非出牌阶段或错误牌区来源被拒绝 | 其他玩家只看到手牌数量变化 |
| `trick.barbarian_assault.group_response` | `standard:barbarian_assault` | 所有其他存活角色按座次逐一收到私有 `RespondSlash`；未响应者各自进入 1 点伤害/濒死链；父牌全程保持在 `Processing` | 伪造目标、错误响应者、未发布牌 ID 或跳过当前目标被拒绝 | 目标只能看到自己的 Prompt/手牌；固定事件顺序和 `TargetIndex` 可回放 |
| `trick.arrow_barrage.group_response` | `standard:arrow_barrage` | 所有其他存活角色按座次逐一收到私有 `RespondDodge`；未响应者各自进入 1 点伤害/濒死链；父牌全程保持在 `Processing` | 伪造目标、错误响应者、未发布牌 ID 或跳过当前目标被拒绝 | 目标只能看到自己的 Prompt/手牌；固定事件顺序和 `TargetIndex` 可回放 |
| `trick.peach_garden.group_recovery` | `standard:peach_garden` | 使用时所有存活角色（含使用者）按座次逐一处理；受伤者回复 1 点，满血者跳过；父牌全程保持在 `Processing` | 伪造目标、非出牌阶段、错误牌区来源或在结算中插入目标被拒绝 | 普通玩家只看到公开恢复结果；`TargetIndex`、`RecoveryFrame` 和同 seed 事件顺序可回放 |
| `trick.five_grains.public_draft` | `standard:five_grains` | 公开展示等同于存活人数的牌，所有存活角色按座次各选一张并进入自己的手牌；父牌和未选展示牌保持在 `Processing` 直到 draft 完成 | 伪造卡牌、错误 picker、旧 prompt、非展示区牌或跳过当前选牌者被拒绝 | 所有人看到公共展示牌，只有当前 picker 看到私有 `SelectHarvestCard`；固定事件/移动顺序可回放 |
| `basic.deal.round_robin` | `identity_8_basic_demo` | 每人初始 4 张，总牌数守恒 | 不足牌堆不能半程开局 | 其他玩家只看到数量 |
| `basic.deck.repeatability` | `identity_8_basic_demo` | 同一配方和 seed 得到相同开局 | 不同 seed 不应依赖固定历史快照 | 普通快照不含 seed |
| `skill.jianxiong.after_damage` | `standard:jianxiong` | 伤害完成且牌仍在 processing 时获得杀 | 闪避、非杀伤害或牌已离开 processing 不触发 | 触发原因不泄露其他暗牌 |
| `skill.feedback.claim_damage` | `standard:feedback` | 角色存活并受到伤害，伤害牌仍在 `Processing` 时收到私有 `Feedback` Prompt；发动后获得该牌，不发动则进入弃牌堆，随后攻击帧完成 | 目标死亡、伤害牌已离开 `Processing`、未发布 Choice 或同一张牌重复取牌不能发生 | Prompt 只投影给反馈者；伤害牌 ID 只出现在可信宿主事件和反馈者私有快照；普通视图不泄漏；固定 seed 可复现 |
| `skill.yiji.gift_card` | `standard:yiji` | 郭嘉受到伤害后私有摸两张牌，从精确牌/其他存活目标组合中选择一张交给目标，或保留两张；伤害帧完成后继续 | 目标死亡、自身、未摸到的牌、旧 Prompt、伪造牌/目标或重复移动必须拒绝；本切片每次只分配一张 | 摸牌与赠牌只在可信宿主事件/移动账本中带实体 ID；只有遗计拥有者看到候选牌，目标收到后才在其私有手牌视图出现；固定 seed/命令流可复现 |
| `skill.jieming.draw_to_max_hand` | `standard:jieming` | 荀彧受到正伤害后获得私有目标 Choice；从存活且公开手牌数低于体力上限的角色中选择一名，按缺口从牌堆摸至目标上限，伤害帧完成后继续 | 死亡/满手目标、旧 Prompt、伪造目标、负伤害或重复摸牌必须拒绝；跳过时不发生补牌 | 目标合法性只使用公开手牌数量、体力上限和存活状态；摸牌 ID 只在可信宿主事件/移动账本中出现，普通视图不泄漏；固定 seed/命令流可复现 |
| `skill.yuanhu.cross_seat_recovery` | `standard:yuanhu` | 其他角色受到正伤害且仍存活、未满体力时，援护者获得私有弃牌 Choice；发动后弃置一张自己的手牌并令固定受伤目标回复 1 点体力，伤害帧完成后继续 | 伤害目标与技能拥有者相同、目标死亡/满血、拥有者无手牌、旧 Prompt、伪造牌 ID/目标或非正伤害必须拒绝；跳过时不发生弃牌或恢复 | 候选拥有者和目标座位来自可信触发帧；弃牌 ID 只在可信事件/移动账本出现，普通视图不泄漏；AI 只读取自己的手牌和目标公开体力；固定 seed/命令流可复现 |
| `skill.wusheng.red_card_slash` | `standard:wusheng` | 红色非杀实体牌可作为对合法目标使用的 `Slash`；`LegalAction.PlayedCardKind` 与 `PlayCardCommand.PlayedCardKind` 明确记录实际牌型 | 黑色牌、原生杀/火杀/雷杀或非法目标不能伪造转化；同一物理牌不能复制为另一张实体牌 | `CardUseDeclared`、伤害事件和 `CardUseFinished` 使用有效牌型 `Slash`；移动账本保留原牌面和同一实体 ID；固定 seed/命令流可复现 |
| `skill.longdan.slash_dodge_conversion` | `standard:longdan` | 物理闪可在出牌阶段作为 `Slash`，物理杀/火杀/雷杀可在需要闪的响应窗口作为 `Dodge`；响应 Choice 明确有效牌型 | 桃、锦囊和无关阶段不能伪造转化；错误 responder、旧 Prompt、未发布牌 ID 或错误有效牌型必须拒绝；同一物理牌不能复制 | `PlayedCardKind`、`response-card-kind` 和 `CardRespondedEvent.EffectiveCardKind` 记录有效牌型；移动账本保留原牌面和同一实体 ID；普通视图不泄漏他人手牌；固定 seed/命令流可复现 |
| `skill.paoxiao.slash_limit` | `standard:paoxiao` | 出牌阶段杀次数不受 1 次限制 | 其他阶段不改变合法动作 | AI 只消费自己的视角 |
| `skill.yingzi.draw` | `standard:yingzi` | 摸牌阶段额外摸 1 张 | 非摸牌阶段不改变摸牌 | 发牌顺序可复现 |
| `skill.kongcheng.target_lock` | `standard:kongcheng` | 空手牌时不能成为杀目标 | 有手牌后恢复可选 | 只公开技能，不公开暗牌 |
| `trick.dismantlement.blind_hand_discard` | `standard:dismantlement` | 选择一名有手牌的其他存活角色，核心用确定性随机数盲弃一张手牌 | 目标无牌、死亡、自身或伪造目标被拒绝 | 普通快照和 `TargetCardDiscardedEvent` 不含被弃牌 ID/牌面；可信账本可回放 |
| `trick.snatch.distance_one_blind_take` | `standard:snatch` | 选择一名座位环距离为 1 且有手牌的其他存活角色，核心用确定性随机数盲取一张手牌并转入使用者手牌 | 距离大于 1、目标无牌、死亡、自身或伪造目标被拒绝 | 普通快照和 `TargetCardTakenEvent` 不含被取牌 ID/牌面；只有使用者私有快照获得牌面，可信账本可回放 |
| `trick.duel.response_chain` | `standard:duel` | 多轮杀响应可暂停、恢复并结束 | 旧 prompt、错误 responder 被拒绝 | 固定事件流可回放 |
| `trick.nullification.chain` | `standard:nullification` | 多层无懈按稳定顺序抵消 | 链外打牌、重复回答被拒绝 | 他人私有 prompt 不泄露 |
| `equipment.replace` | `standard:crossbow` / `standard:bagua` | 装备进入槽位，替换旧装备并弃置 | 错误槽位、死亡后装备残留被拒绝 | 装备状态在 checkpoint 可恢复 |
| `equipment.distance` | 坐骑 | 距离/范围查询影响目标合法性 | 不能由 UI 直接改距离 | AI 只读公开距离结果 |

## C0-K1 强制移动场景

以下场景是 K1 通知要求的最小覆盖集。每项正式实现时都必须同时验证实体牌守恒、非法来源/目标不产生部分提交、固定 seed 可重放，以及普通玩家视图不泄漏隐藏信息；详细 Given/When/Then 见 `CARD_MOVEMENT_CONTRACT.md`。

| 场景 ID | 对应内容/规则 | 移动重点 |
| --- | --- | --- |
| `k1.slash.hit` | `standard:slash` 命中 | `Hand → Processing → DiscardPile` |
| `k1.slash.dodge` | `standard:dodge` 闪避 | 响应牌独立经过 `Processing` |
| `k1.jianxiong.claim` | `standard:jianxiong` 奸雄取得伤害牌 | `Processing → Hand`，不复制实体牌 |
| `k1.peach` | `standard:peach` | 使用牌经过 `Processing` 后弃置 |
| `k1.deal.draw` | 发牌与摸牌 | `DrawPile → Hand`，顺序可复现 |
| `k1.hand-limit-discard` | 手牌上限弃置 | `Hand → DiscardPile`，不得部分提交 |
| `k1.death-cleanup` | 阵亡清区 | K1 清理手牌；装备/判定区分别等待 K6/K7 |
| `k1.lord-penalty` | 主公误杀忠臣惩罚 | 使用 `mode.identity.lord-killed-loyalist` |
| `k1.reshuffle` | 弃牌区重洗 | `DiscardPile → DrawPile`，牌数守恒 |

## C0-K2/K3 强制控制与内容场景

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k2.revision.stale` | `GameCommand.ExpectedRevision` | 过期命令返回 `StaleRevision`，快照、日志、牌区和随机数不变 |
| `k2.prompt.exact-choice` | `PromptId` / `PromptChoice` | 卡牌和目标绑定为一个完整 Choice，不接受伪造组合 |
| `k2.prompt.private` | 玩家视图 | 非 responder 看不到 PromptId、Choice、他人手牌或 AI 候选 |
| `k2.prompt.answer-once` | `AnswerPromptCommand` | 错误 responder、旧 Prompt、未发布 Choice 和重复回答均拒绝 |
| `k3.registry.isolated` | `ContentRegistry` | 两个 Registry 不共享可变注册状态，公开集合为只读投影 |
| `k3.registry.references` | 包依赖和引用校验 | 重复 ID、未知引用、版本不足和依赖环在 Build 时失败 |

## C0-K4 模式开局与选将

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k4.mode.registry` | `ContentModeDefinition` | 模式选择角色分布、牌堆、候选数量和武将池，不复制另一套状态机 |
| `k4.general.private-offer` | `SelectGeneralCommand` | responder 只能看到自己的候选，其他 viewer 看不到 Prompt、候选 ID 或未公开武将 |
| `k4.general.shared-pool` | 共享池去重 | 每次选中后从池移除，任何已选武将不能再次选择 |
| `k4.general.invalid-choice` | Revision/Choice 校验 | 未发布武将返回 `InvalidGeneral`，状态、日志、牌区和随机数不变 |
| `k4.setup.pause-resume` | 可暂停开局 | `Start`/`AdvanceOneStep` 可停在真人选将，回答后继续 AI 选将、公开、洗牌和逐轮发牌 |
| `k4.setup.repeatability` | 固定 seed + 命令流 | 相同模式、seed 和选将命令产生相同公开快照、事件顺序和武将分配 |
| `k4.setup.five-player` | 5 人身份模式 | 1/1/2/1 分布、全 AI 选将和整局终止均成立 |

## C0-K5 结算帧与类型化事件切片

| 场景 ID | 对应能力 | 必须成立 |
| --- | --- | --- |
| `k5.resolution.stack` | `ResolutionStack` | 杀/属性杀的卡牌帧、响应窗口帧和伤害/死亡子帧保持父子关系，完成后栈清空 |
| `k5.resolution.serializable` | 数据型结算帧 | 可信宿主可将当前帧栈序列化为 JSON；帧中不包含委托、WPF 对象或玩家视图数据 |
| `k5.events.card-damage` | 类型化事件 | 提交事件包含出牌声明、目标确认、携带 `DamageNature` 的伤害请求/应用/AfterDamage 和结算完成信息 |
| `k5.events.damage-skill` | 伤害后技能触发 | 存活目标收到私有 `Feedback`、`Yiji` 或 `Jieming` Choice；非受伤者的援护者收到私有 `Yuanhu` 弃牌 Choice；`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 记录请求与发动/跳过，反馈发动时 `DamageCardClaimedEvent` 记录取得伤害牌，遗计发动时 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 记录私有摸牌与跨座位赠牌，节命发动时 `DamageSkillCardsDrawnEvent.TargetSeat` 记录补牌目标，援护发动时 `DamageSkillCardDiscardedEvent`/`RecoveryAppliedEvent` 记录弃牌和恢复，并与对应移动 reason 一致 |
| `k5.events.damage-trigger-order` | 伤害触发候选排序 | `DamageTriggerCandidate` 按优先级、相对当前行动者座次、技能序号和 `CandidateId` 稳定排序；`DamageTriggerWindowFrame.CandidateIndex` 冻结并推进当前候选，收集逐个检查存活拥有者的 `CanTriggerAfterDamage`，候选身份写入触发/技能帧和请求/结果事件；现有内置技能的触发条件默认仍只匹配受伤者，遗计的效果可把牌交给其他座位，节命的效果可把牌补给公开合法目标 |
| `k5.events.alcohol` | 酒的一次性状态、实际伤害金额与濒死自救 | `AlcoholAppliedEvent` 公开酒效设置，直接杀声明时消费；未消费时发布 `AlcoholExpiredEvent`；加伤后的实际金额在 `DamageFrame`、伤害请求/应用/AfterDamage 和技能上下文中保持为 2；濒死者使用酒生成 `DyingResponseEvent.UsedAlcohol` 与 1 点 `RecoveryAppliedEvent`，群体牌/决斗不消费 |
| `k5.trick.group-response` | 群体逐目标结算 | `GroupCardUsedEvent` 声明完整目标列表，`GroupResponseEvent` 携带 `RequiredCardKind` 并按座次逐个提交；南蛮入侵要求杀，万箭齐发要求闪；桃园结义按同一 `TargetIndex` 逐目标恢复并使用 `RecoveryFrame`；每个目标的伤害/濒死/恢复结束后才推进父帧 |
| `k5.trick.public-draft` | 公共展示与私有逐人选牌 | `CardsRevealedEvent` 只包含显式公开牌；当前 picker 获得自己的 `SelectHarvestCard` prompt，`HarvestCardSelectedEvent` 推进 `TargetIndex`，选中牌移动到 picker 手牌 |
| `k5.events.dying-winner` | 濒死/胜负事件 | 现有 Demo 的基础濒死响应、死亡、身份公开和胜负判定产生稳定事件；多伤害嵌套和技能濒死询问仍属于后续切片 |

## 当前执行顺序

1. 先用现有 Core 契约维护基础 `basic.*`、十个 `skill.*` 和伤害后技能事件的回归。
2. K1 已开放：`k1.*` 移动契约已完成并审阅；运行时覆盖保留在 Core Console 自测。
3. K2 已开放：`k2.*` 命令/Prompt 场景由同一 Console 自测覆盖。
4. K3 已开放：`k3.*` Registry 场景已覆盖，`standard:*` ID 在 Standard 包中冻结。
5. K4 已开放：`k4.*` 私有选将和模式开局场景由同一 Console 自测覆盖；更大规模 seed 矩阵仍是扩展验证。
6. K5 已开放结算帧/事件切片：`k5.resolution.*`、`k5.events.*`、`basic.elemental-slash.nature`、`basic.alcohol.slash_boost`、`basic.alcohol.dying_rescue`、`skill.feedback.claim_damage`、`skill.yiji.gift_card`、`skill.jieming.draw_to_max_hand`、`skill.yuanhu.cross_seat_recovery`、`skill.longdan.slash_dodge_conversion`、`k5.events.damage-trigger-order`、`trick.barbarian_assault.group_response`、`trick.arrow_barrage.group_response`、`trick.peach_garden.group_recovery`、`trick.five_grains.public_draft`、`trick.dismantlement.blind_hand_discard` 和 `trick.snatch.distance_one_blind_take` 由 Console 自测覆盖；普通/火/雷杀响应和类型化伤害、反馈存活伤害取牌、遗计跨座位分配、节命目标补牌至上限、援护跨座位弃牌恢复、龙胆闪/杀互转、伤害触发候选稳定排序与游标暂停/恢复、酒的一次性直接杀 +1 伤害与回合结束失效、濒死者酒自救、决斗多轮杀响应、无中生有无目标摸牌、南蛮入侵逐目标杀响应、万箭齐发逐目标闪响应、桃园结义逐目标恢复、五谷丰登逐人选牌、过河拆桥隐藏手牌盲弃、顺手牵羊距离一隐藏手牌盲取和基础单次伤害濒死求桃已运行；通用可配置的非受伤者触发条件、属性抗性、多伤害嵌套和复杂牌型仍未开放。
7. 后续 K5/K6/K7 按清单顺序实现锦囊、装备、判定和技能，不把多个阶段合并成一次大重写。
