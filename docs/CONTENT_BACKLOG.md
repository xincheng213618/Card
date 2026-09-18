# 内容扩充清单

这份清单是简要入口；按阶段维护的动态队列见 [`LUNA_CONTENT_BACKLOG.md`](LUNA_CONTENT_BACKLOG.md)。Core、内容、WPF 和测试现在由同一个长期 goal 统一推进，但每批内容仍必须满足：规则核心不引用 WPF、AI 只读取玩家视角快照、固定种子可复现、普通快照不泄露暗信息，并通过 Release 构建、Console 自测和格式检查。

## 当前阶段：K4 开局切片 + K5 基础结算切片/伤害触发范围切片 + K6 装备距离基础切片 + K7 无懈/铁索连环/鬼才/乐不思蜀/兵粮寸断/闪电判定切片 + K8 可信命令 Checkpoint 兼容切片 + 主动技能目标/濒死续接、被动距离修正和急救红牌转化切片已落地，继续扩大内容池并完善完整结算入口

M1 当前状态：公开阵营 2v2 已作为独立 `standard-team-modes@1.0.0` 扩展包落地；`team:standard-2v2` 复用 Core 的选将、AI、结算、事件、视图脱敏和回放边界，WPF 新局设置已可选择。M2 Lite 也已落地为独立 `standard-national-war-lite@1.1.0` 扩展包；M3 已开放国战 AI 明置策略、六人独立势力试验（`standard-national-war-ambitious@1.0.0` / `national:ambitious-6`）、受约束的公开攻击证据推断、隐藏目标救援评分和公开攻击后的中途 Checkpoint/Replay 校验；AI policy v3 另把身份局火攻接入公开攻击观察入口，并保留 v1/v2 的历史回放语义。当前全量回归为 Core 171/171、WPF 51/51；完整国战仍待 M3 后续切片。

- `standard-national-war-ambitious@1.0.0` 注册魏 3、蜀 2、野心家 1 的六人国战实验；`ContentModeDefinition.SoloFactionIds` 只表达人数/内容元数据，尚未宣称珠联璧合、阵法、围攻、变更副将或完整野心家胜利规则。Core、WPF、私有快照、AI 明置和 Checkpoint/Replay 均有对应固定场景覆盖。

已完成：

- `CardCatalog` 注册杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻、乐不思蜀、兵粮寸断、闪电、无懈可击、铁索连环和七种装备牌（含仁王盾）的显示文案、牌类别、AI 价值和弃牌保留价值；WPF 卡面从 Core 内容目录读取描述。
- `StandardDeckCatalog.BasicDemo` 注册 18 张杀、2 张火杀、2 张雷杀、2 张兵粮寸断、18 张闪、10 张桃、2 张酒、4 张决斗、2 张无中生有、2 张南蛮入侵、2 张万箭齐发、2 张桃园结义、2 张五谷丰登、2 张过河拆桥、2 张顺手牵羊、2 张火攻、2 张乐不思蜀、2 张闪电、2 张无懈可击、2 张铁索连环、2 张诸葛连弩、1 张八卦阵、1 张青釭剑、1 张赤兔、1 张绝影和 1 张玉玺、1 张仁王盾，共 90 张牌。
- 初始每人 4 张、摸牌阶段 2 张也属于牌堆配置，`GameEngine` 不再拥有这些内容常量。
- 回归检查覆盖内容注册、牌堆总数与分布、id/花色/点数稳定性、武将技能注册和 AI 使用桃的策略。
- `CardGame.Content.Standard` 已注册 `standard:*` 基本牌、技能、武将、牌堆和 `identity:standard-8` 模式元数据；`ContentRegistry` 校验依赖并冻结独立只读投影。
- WPF 已通过 Standard Registry 创建对局；`Content.cs` 只保留兼容投影，不再作为新增内容的长期落点。
- `ContentModeDefinition` 已描述 `DeckId`、候选数量和 `GeneralPoolIds`；WPF 通过 `UseInteractiveSetup` 使用私有选将、共享池去重和逐轮发牌。
- `standard-active-skills@1.0.0` 作为依赖 Standard 的可选扩展包已落地：注册 `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、`standard:mashu`、`standard:qicai`、七个技能演示武将和 5/8 人主动技能演示模式；`UseSkillCommand`、`LegalActionKind.UseSkill`、`ActiveSkillFrame` 及类型化技能事件沿统一提交/回放边界接入。苦肉允许出牌阶段且体力大于 0 时失去 1 点体力并摸两张牌，降至 0 点时复用私有 `RescueDying` 窗口，获救后继续摸牌；制衡的演示模式保留至少一张私有手牌、经过 `Processing` 弃置后摸等量牌的历史行为，规则 v17 的经典孙权则可混选手牌与公开装备且每阶段限一次；仁德允许私有选择一至若干张手牌交给一名其他存活角色，一次交给至少两张时按目标伤势回复 1 点体力，并按回合限制重复使用；青囊允许私有选择一张手牌和一名受伤存活角色，经过 `Processing` 弃置后令目标回复 1 点体力，同样按回合限制重复使用；回春允许私有选择精确两张手牌和 2–3 名受伤存活角色，逐目标回复 1 点体力并按回合限制重复使用；马术通过公开距离 modifier 统一影响杀与顺手牵羊的合法性；奇才通过统一合法性查询使距离型锦囊不受距离限制。暗牌 ID 只属于对应私有 Prompt 或可信宿主事件/账本；标准 `Create()` 与旧内容指纹保持不变，WPF 默认窗口显式使用扩展包。
- `standard-rescue-skills@1.0.0` 作为依赖主动技能扩展的独立救援层已落地：注册 `standard:jijiu` 和 `standard:demo-jijiu`，并由 `StandardContentRegistry.CreateWithRescueSkills()` 将急救者加入 5/8 人扩展模式；`IPassiveSkill.CanUseAsDyingRescue` 只把红色非桃实体牌映射为濒死窗口的有效桃，物理牌仍按 `Hand → Processing → DiscardPile` 移动，`DyingResponseEvent` 同时保留有效牌型和物理牌型。AI 与 WPF 复用同一私有候选和命令边界，普通快照不泄漏候选牌 ID；默认 `CreateWithActiveSkills()` 及旧内容指纹保持不变。
- `standard-team-modes@1.0.0` 作为独立公开阵营扩展包已落地：注册 `team:standard-2v2`、青/赤两队分配、公开 `TeamAssignedEvent`、公开阵营快照、队伍胜负、Team AI 敌我关系和固定 seed/Checkpoint/Replay 回归；普通视图继续隐藏手牌、seed 和宿主内部状态，基础 Standard Registry 的内容哈希保持不变。WPF 默认新局设置已加入 2v2 选项、阵营说明、座位标签、目标文案和胜负音效。
- 现有杀/火杀/雷杀/闪/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/火攻/乐不思蜀/兵粮寸断/闪电/无懈可击/铁索连环/伤害/桃链路、属性伤害类型、决斗交替杀响应、群体牌逐目标杀/闪响应、桃园结义逐目标恢复、五谷丰登公开翻牌与私有逐人选牌、过河拆桥私有不透明牌位选择目标手牌或精确弃置公开装备/判定区牌、顺手牵羊战斗距离一私有不透明牌位选择目标手牌或精确取得公开装备/判定区牌、火攻私有展示与同花色弃牌、无懈效果前固定座次有限多层响应、铁索连环精确一/二目标与公开标记/火雷传导、乐不思蜀/兵粮寸断/闪电判定区延时结算、基础濒死求桃、青釭剑直接杀无视防具和 K6 装备生命周期已通过 `ResolutionFrame` 栈、类型化事件与移动账本接入 Core；属性抗性、属性转化和其他锦囊、复杂改判、通用触发时机及复杂濒死响应仍按后续阶段开放。
- K4 Console 场景覆盖私有候选不泄漏、非法武将零状态变化、共享池不重复、5 人 AI 选将终止和同 seed 开局确定性。
- 可信宿主可从纯 `Submit` 命令边界保存带 SchemaVersion、Revision、模式、内容包签名、规范化内容指纹和 `RulesVersion` 的 Checkpoint，并在私有 Prompt 暂停点通过 `GameReplay.Restore` 恢复；缺少规则版本的旧 JSON 保留 v1 事件语义，同版本内容定义漂移和旧兼容 API 直接推进的混合状态均显式拒绝。
- `standard:duel` 已接入目标选择、重复 `RespondSlash` Prompt、响应牌处理区生命周期和 Duel 类型化事件；WPF 响应按钮根据当前 Prompt 切换为“打出杀”。
- `standard:draw_two` 已接入无目标 `CardUseFrame`、牌堆摸牌、处理区生命周期和 `CardUseFinishedEvent`；WPF 卡面直接消费 Registry 描述。
- `standard:barbarian_assault` 已接入 `CardUseFrame.TargetIndex`、所有其他存活角色的顺序目标、逐目标私有 `RespondSlash`、`GroupCardUsedEvent`/`GroupResponseEvent` 和单次伤害/濒死续接；同一张南蛮入侵在所有目标完成前保持于 `Processing`。
- `standard:arrow_barrage` 复用同一组群体结算入口，将逐目标私有响应切换为 `RespondDodge`；`GroupResponseEvent` 记录必需响应牌种类，父牌同样在全部目标完成前保持于 `Processing`。
- `standard:peach_garden` 复用 `CardUseFrame.TargetIndex`，锁定使用时仍存活的全部角色（含使用者），每次 `AdvanceOneStep()` 只处理一个目标；受伤角色进入 `RecoveryFrame` 并提交 `RecoveryAppliedEvent`，满血角色跳过，父牌在全部目标完成前保持于 `Processing`。
- `standard:five_grains` 复用 `CardUseFrame.TargetIndex`，公开翻出等同于锁定存活角色数的牌；当前 picker 通过私有 `SelectHarvestCard` prompt 从公共展示区选择一张，AI 只接收自己的脱敏快照和公开选项，选中的牌进入 picker 手牌，其余公开牌按统一移动理由清理，父牌在全部 picker 完成前保持于 `Processing`。
- `standard:dismantlement` 已接入单目标 `CardUseFrame`：允许选择有手牌、公开装备或公开判定区牌的其他存活角色；规则版本 4 的手牌由来源玩家选择不透明牌位（v1–v3 回放保留确定性随机数盲弃），公开装备/判定区牌通过带 `TargetCardId` 的精确 Choice 逐张选择；手牌事件继续脱敏，公开区域事件可携带已公开的实体身份，可信宿主移动账本保留完整来源和 reason。判定区牌仅开放已公开实体，目标来源的私有牌位候选已开放，普通观察者只见候选数量。
- `standard:snatch` 已接入单目标 `CardUseFrame`：允许选择战斗距离为 1 且有手牌、公开装备或公开判定区牌的其他存活角色；规则版本 4 的手牌由来源玩家选择不透明牌位（v1–v3 回放保留确定性随机数盲取），公开装备/判定区牌通过带 `TargetCardId` 的精确 Choice 逐张选择并转入使用者手牌；手牌事件继续脱敏，取得的公开区域牌面只在使用者私有快照中出现，目标手牌牌面始终不公开。当前战斗距离按存活座位环计算并消费赤兔/绝影/马术修正；奇才通过技能查询放宽距离型顺手牵羊的合法目标。
- `standard:fire_attack` 已接入两段私有选牌的单目标 `CardUseFrame`：规则 v19 起可选择自己，目标只在自己的快照中选择展示牌，展示后实体仍位于目标手牌，牌面通过 `FireAttackCardRevealedEvent` 和 `PublicRevealedCards` 对所有观察者公开；攻击者再从自己的同花色手牌中选择弃牌或跳过，自选目标时展示牌本身也可作为弃牌成本，成功弃牌才进入 `DamageNature.Fire` 的 1 点伤害链。v1–v18 保留他人目标及展示牌进入处理区后弃置的历史路径。AI 只接收本座私有快照和宿主发布的候选 ID，WPF 复用通用 Prompt 按钮，普通快照不暴露未展示牌面。
- `standard:fire_slash` / `standard:thunder_slash` 已接入普通杀的合法性、闪响应和处理区生命周期；命中时通过 `DamageNature.Fire` / `DamageNature.Thunder` 写入伤害帧、伤害事件和 AI/日志可解释结果，并在连环目标上按固定顺序传导同额属性伤害。属性抗性、属性转化和更复杂的多伤害嵌套仍未开放。
- `standard:alcohol` 已接入出牌阶段无目标使用、`Hand → Processing → DiscardPile` 生命周期和公开一次性酒效；本回合下一张直接杀在声明时获得 +1 伤害，金额贯穿 `DamageFrame`、伤害事件、技能上下文和濒死收尾，未消费的酒效在回合结束失效；规则 v20 起出牌阶段每回合限使用一次，即使增伤已被杀消费也保留限次标记至下回合，v1–v19 保留历史重复使用路径；规则 v12 的濒死窗口只允许 victim 使用自己的酒自救 1 点体力，v3–v11 保留跨座位兼容回放。
- `standard:feedback` 已接入存活伤害后的可选触发：伤害牌仍在 `Processing` 时收集当前存活拥有者的 `DamageTriggerCandidate`，经 `DamageTriggerOrdering` 稳定排序后冻结在带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前候选压入 `DamageSkillFrame`，只向反馈者发布私有 `Feedback` Choice；帧和请求/结果事件保留 `CandidateId`/`Priority`，发动后按 `skill.feedback.claim-damage-card` 将同一实体牌移入技能拥有者手牌，并发布 `DamageSkillResolvedEvent`/`DamageCardClaimedEvent`；不发动则正常弃牌，群体父结算帧仍可继续推进，普通快照不泄漏取得牌 ID。现有内置技能默认仍只参与受伤者窗口。
- `standard:jianxiong` 在规则 v16 的经典身份局中复用同一伤害候选游标：正伤害完成后，只要造成伤害的实体牌仍在 `Processing`，就向曹操发布私有取得/跳过 Choice；发动后按 `skill.jianxiong.claim-damage-card` 将同一实体牌移入手牌并发布 `DamageCardClaimedEvent`，跳过则由父牌正常收尾。v1–v15 与演示模式保留自动取得杀类伤害牌的历史语义；延时锦囊等其他牌区来源仍待统一牌区时机。
- `standard:yiji` 已接入受伤后的可选触发：当前候选压入 `DamageSkillFrame` 后，先通过 `skill.yiji.draw` 从牌堆私有摸两张牌，再向郭嘉发布精确的牌/其他存活目标组合和跳过选项；发动时按 `skill.yiji.give-card` 将一张牌由拥有者手牌移给目标并提交 `DamageSkillCardGivenEvent`，跳过时两张牌留在拥有者手牌。摸牌、跨手牌移动和候选牌面只属于可信宿主或对应私有视图；通用非受伤者触发条件仍未开放。
- `standard:jieming` 已接入同一伤害触发窗口：荀彧受伤后发布私有 `DecisionKind.Jieming`，从存活且公开手牌数低于体力上限的角色中选择一名目标；发动后按 `skill.jieming.draw` 从牌堆摸至目标上限，并由 `DamageSkillCardsDrawnEvent.TargetSeat` 和移动账本记录目标，跳过仍沿候选游标继续。目标选择不携带目标隐藏牌面；通用非受伤者触发条件和更复杂的跨层效果仍未开放。
- `standard:yuanhu` 已接入同一伤害触发窗口：援护者在其他角色受到正伤害后发布私有 `DecisionKind.Yuanhu` 弃牌 Choice；发动时按 `skill.yuanhu.discard` 将自己的一张手牌移入弃牌堆，并通过 `RecoveryFrame` 令固定受伤目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 和 `DamageSkillResolvedEvent.EffectTargetSeat` 记录可信宿主结果，普通视图不泄漏弃牌 ID；这是当前唯一显式 opt-in 的非受伤者触发示例。
- `standard:ganglie` 已接入同一伤害触发窗口：受伤者发布私有 `DecisionKind.Ganglie` 发动/跳过 Choice；发动后将牌堆顶移入公开 `Judgment` 区并记录判定事件，红色结果向伤害来源发布私有 `DecisionKind.GangliePunish`，由其选择精确两张手牌弃置或承受 1 点伤害。弃牌、判定和结果事件沿统一移动账本记录，来源视角之外不泄漏牌 ID；当前 Registry 规则路径把反制伤害复用为嵌套 `DamageFrame`、`DamageRequestedEvent`、`DamageAppliedEvent` 和 `AfterDamageEvent`，旧 Checkpoint 缺失规则版本字段时保留历史事件形状；若反制伤害致死，濒死/救援完成后回到原伤害触发游标。
- `standard:guicai` 已接入判定前改判窗口：判定牌翻入公开 `Judgment(target)` 后，Core 按 `JudgmentTriggerOrdering` 冻结候选并向当前鬼才拥有者发布私有 `DecisionKind.Guicai` 替换/跳过 Choice；替换牌按 `Hand(owner) → Processing → Judgment(target)` 移动，旧判定牌先结束，最终 `JudgmentResolvedEvent` 公开结果，普通视图不泄漏拥有者手牌。
- `standard:indulgence` 与 `standard:supply_shortage` 已接入延时判定窗口：使用牌进入目标公开 `Judgment` 区，在目标下回合摸牌前复用 `JudgmentFrame`；规则 v11 起，乐不思蜀非红桃跳过出牌阶段，兵粮寸断非梅花跳过摸牌阶段，v1–v10 保留历史红黑语义。判定牌和延时牌分别沿 `DrawPile → Judgment → DiscardPile`、`Hand → Processing → Judgment → DiscardPile` 移动，目标判定区公开且不允许重复放置；无懈可击、鬼才、多个延时效果累计和死亡清理共用既有边界。
- `standard:lightning` 已接入自用延时判定窗口：闪电进入自己的公开 `Judgment` 区，下个回合判定为黑桃 2 至 9 时造成 3 点雷电伤害并沿伤害/濒死链续接，其他判定牌则转移到下一名存活角色；判定结果、命中/转移事件、牌区移动和普通视图脱敏均沿统一入口记录。
- `standard:wusheng` 已接入红色非杀实体牌按 `Slash` 使用：合法动作通过 `PlayedCardKind` 区分物理牌与有效牌型，`PlayCardCommand`/WPF 均可明确选择转化；事件和伤害链记录有效 `Slash`，移动账本保留原始实体牌，不复制牌或绕过来源校验。
- `standard:huichun` 已接入出牌阶段主动技能：当前拥有者私有选择精确两张手牌和 2–3 名受伤存活角色，牌按 `Hand → Processing → DiscardPile` 使用 `skill.huichun.discard` 移动，随后为每个目标建立独立 `RecoveryFrame` 并发布 `RecoveryAppliedEvent`；非法数量或重复目标原子拒绝，普通视图不泄漏暗牌 ID。
- `standard:mashu` 已接入被动技能距离切片：马术通过 `IPassiveSkill.ModifyOutgoingDistance` 将拥有者到其他角色的公开战斗距离减少 1，最终距离由 Core 统一钳制为至少 1；顺手牵羊与杀的合法动作、AI 输入和 WPF 座位距离文本都复用 `GetCombatDistance`，基础 Standard Registry 和 90 张牌堆保持不变。
- `standard:qicai` 已接入被动技能锦囊距离切片：奇才通过 `IPassiveSkill.IgnoresTrickDistance` 为距离型锦囊提供统一合法性查询；当前顺手牵羊在 Core 中允许距离大于 1 的公开合法目标，AI 和 WPF 都消费同一结果，基础 Standard Registry、90 张牌堆和旧内容指纹保持不变。
- 伤害后触发范围已抽为 `DamageTriggerScope`：默认技能匹配受伤者，援护声明 `OtherLivingPlayer`，并保留 `AnyLivingPlayer` 供后续技能复用；`CanTriggerAfterDamage` 统一拒绝非正伤害和缺少目标的跨座位上下文，候选窗口、AI、WPF 和回放仍共享同一 Core 结果。
- 本轮增量验证为 Core `116/116`、WPF `23/23`；新增覆盖规则 4 目标手牌不透明牌位选择、规则版本 5 致命伤害后触发窗口、急救扩展包隔离、红牌筛选、AI 选择、私有濒死 Choice、有效/物理牌型事件、处理区移动、Checkpoint/Replay 和 WPF 目标牌位/急救按钮渲染与提交；既有 Registry、距离、伤害触发范围、跨座位酒救援、普通快照脱敏和 UI 观察仍通过。

当前实现二十七种牌：六种基本牌、十四张最小锦囊和七种装备牌（含仁王盾；含一张无目标即时牌、两张共用响应机制的群体牌、一张多目标恢复牌、一张公开 draft 牌、一张隐藏手牌不透明牌位选择弃置、一张距离一隐藏手牌不透明牌位选择取得、一张私有展示/同花色弃牌牌、一张可在效果前插入有限多层响应的抵消牌、一张精确一/二目标并传导火雷伤害的状态牌、三张进入公开判定区并延后至目标回合判定的延时牌、五类装备槽位）。酒支持出牌阶段的一次性直接杀加伤和规则 v12 的濒死者酒自救 1 点体力；规则 v13 的诸葛连弩/青釭剑牌面攻击范围 1/2、连弩无限杀及赤兔/绝影/玉玺基础 modifier 已接入，v1–v12 保留旧武器范围；规则 v14 的八卦阵直接杀/万箭闪响应和仁王盾黑色杀无效已接入，v1–v13 保留旧防具时机。乐不思蜀、兵粮寸断和闪电已接入延时判定（安全花色分别为红桃/梅花；闪电仅以黑桃 2 至 9 命中并造成 3 点雷电伤害，否则转移）。这里的“标准”指本 Demo 的标准演示牌堆，不声称是商业游戏的官方卡表。

## 等待核心扩展点后接入

以下内容先记录需求，不把未实现的牌塞进现有 `CardKind` 或在窗口里写分支。

### 锦囊

- 过河拆桥：已完成目标手牌的不透明牌位选择与公开装备/判定区牌逐张选择；规则版本 4 通过 `TargetCardSelectionFrame`、私有 `DecisionKind.SelectTargetCard` 和脱敏 `TargetCardSelectionRequestedEvent` 固化暂停、回放和视角边界，公开区域仍使用 `LegalAction.TargetCardId` 与 `Equipment(target) → Processing → DiscardPile` / `Judgment(target) → Processing → DiscardPile` 移动；规则版本 1–3 回放保留历史盲弃语义。
- 顺手牵羊：已完成距离一目标手牌的不透明牌位选择与公开装备/判定区牌逐张取得；公开区域沿 `Equipment(target) → Processing → Hand(source)` / `Judgment(target) → Processing → Hand(source)` 进入使用者私有手牌，目标手牌牌面不进入来源以外的视图；规则版本 1–3 回放保留历史盲取语义。
- 无中生有：已接入无目标“使用后摸两张”的类型化卡牌效果；重洗和牌堆耗尽仍沿用 Core 的公共摸牌入口。
- 决斗：已接入交替 `RespondSlash` 响应和效果前有限无懈窗口；群体/多伤害嵌套仍待后续 K5/K7。
- 南蛮入侵：已接入所有其他存活角色按座次逐个 `RespondSlash`；每个未响应目标独立进入伤害/濒死链，同一使用牌保持在 `Processing`，更复杂的群体多伤害仍待后续 K5/K7；效果前有限无懈窗口已由通用抵消入口覆盖。
- 万箭齐发：已复用群体目标游标和处理区生命周期，所有其他存活角色按座次逐个 `RespondDodge`；`GroupResponseEvent` 使用通用 `RequiredCardKind`/`UsedResponse`/`ResponseCardId` 字段，后续群体牌可继续接入同一入口。
- 桃园结义：已接入全部存活角色（含使用者）的多目标恢复；`TargetIndex` 跨公开单步边界推进，受伤目标使用 `RecoveryFrame` 和 `RecoveryAppliedEvent`，满血目标不生成伪恢复事件，使用牌直到全部目标完成才进入弃牌堆。
- 五谷丰登：已接入公共展示区与逐 picker 私有 `SelectHarvestCard` prompt；展示牌对所有快照可见，当前选牌、其他玩家手牌和 AI 思考仍按 viewer 隔离，选牌事件和 `card.harvest-pick` 移动账本可回放，父牌直到所有 picker 完成才离开 `Processing`。
- 无懈可击：已完成有限切片；锦囊效果在进入实际结算前压入 `NullificationWindowFrame`，按固定座次向当前 responder 发布私有 `DecisionKind.Nullification`，无懈之间可以继续互相抵消，链结束后才恢复或清理原效果。复杂响应时机、多伤害嵌套和复杂改判仍待后续；鬼才基础判定替换已完成。
- 铁索连环：已完成有限切片；只允许精确一名或两名其他存活角色，目标以公开 `IsChained` 标记记录，使用牌经过 `Processing` 和有限无懈窗口；火杀/雷杀对连环目标造成同额、同属性传导，普通杀和更复杂的多伤害嵌套仍不触发。
- 乐不思蜀：已完成正式花色延时判定切片；使用者选择一名其他存活角色，牌公开进入其判定区，在其下回合摸牌前判定，非红桃跳过出牌阶段、红桃正常出牌；目标重复禁止、判定区移动、无懈窗口、鬼才替换和阵亡清理均沿统一结算入口。规则 v1–v10 保留历史红黑行为。
- 兵粮寸断：已完成正式花色延时判定切片；使用者选择一名有公开手牌的其他存活角色，牌公开进入其判定区，在其下回合摸牌前判定，非梅花跳过摸牌阶段、梅花正常摸牌；目标重复禁止、无手牌目标过滤、判定区移动、无懈窗口、鬼才替换和阵亡清理均复用乐不思蜀的统一结算入口。规则 v1–v10 保留历史红黑行为。
- 闪电：已完成基础延时判定切片；只能对自己使用，进入自己的公开判定区；下个回合黑桃 2 至 9 命中并造成 3 点雷电伤害，否则移至下一名存活角色的判定区；转移、伤害续接、无懈窗口、鬼才替换和阵亡清理均复用统一结算入口。

### 装备

- K6 基础切片已开放五类 `EquipmentSlot`、`LegalActionKind.Equip`、同槽替换、`EquipmentChangedEvent`、装备公开快照和阵亡清理；规则 v13 的诸葛连弩/青釭剑牌面攻击范围、连弩无限杀、赤兔、绝影、玉玺、仁王盾、马术和奇才的基础合法性 modifier 已进入 Core；Standard Registry 的冻结描述由 WPF 按规则版本投影。
- 八卦阵已实现防具槽、公开生命周期和 K7 判定防御；规则 v14 覆盖直接杀及万箭齐发的闪响应，判定牌通过 `JudgmentFrame` 与类型化事件进入判定区并公开结果。青釭剑已实现武器槽生命周期和直接杀无视防具；仁王盾在 v14 以公开类型化事件令已指定目标的黑色杀无效，v1–v13 保留目标过滤。过河拆桥/顺手牵羊的公开装备与判定区牌目标选择已通过独立精确入口接入。
- 后续装备仍需失效/卸载和更复杂的持续 modifier；所有新增装备必须沿统一移动账本并保持 AI 只读取自己的玩家视图。

### 武将与技能

- 规则 v21 的经典英姿在摸牌阶段发布私有发动/跳过 Prompt；发动后多摸一张，跳过则按通常数量摸牌，AI 默认发动。v1–v20 与演示模式继续自动多摸一张；非法 Choice、Checkpoint/Replay 和 WPF 两项选择均有回归。
- `standard-classic-generals@1.1.0` 用 `classic:guo-jia` 替换经典池中的旧单技能郭嘉，并按天妒、遗计的稳定顺序注册双技能。规则 v22 在其自己的判定牌生效后、离开公开判定区前发布私有发动/跳过 Prompt；发动时按 `skill.tiandu.claim-judgment` 获得同一实体牌，AI 默认发动，答复后恢复原判定父结算。v1–v21 不创建该窗口；恢复 `standard-classic-generals@1.0.0` 时仍使用旧武将池和内容指纹。非法 Choice、牌区守恒、Checkpoint/Replay 与 WPF 两项选择均有回归。
- `standard-classic-generals@1.2.0` 用 `classic:zhou-yu` 替换经典池中的旧单技能周瑜，并按英姿、反间的稳定顺序注册双技能。规则 v23 的反间每个出牌阶段限一次，目标先从四种花色中选择，再获得并展示一张确定性随机源手牌；花色不同时造成 1 点普通伤害。花色 Prompt 不泄漏周瑜手牌，AI 只用公开敌对关系选目标并以固定花色处理未知牌面。v1–v22 不发布动作，包 1.1.0/1.0.0 保留旧周瑜；非法 Choice、匹配/不匹配、牌区守恒、Checkpoint/Replay 与 WPF 目标/花色选择均有回归。
- `standard-classic-generals@1.3.0` 用 `classic:zhuge-liang` 替换经典池中的旧单技能诸葛亮，并按观星、空城的稳定顺序注册双技能。规则 v24 的观星在准备阶段私有选择发动，冻结牌堆顶 `min(5, 存活角色数)` 张牌，先排列牌堆顶、再从最深处排列牌堆底；公开结果只含数量。AI 只读取私有 Choice 元数据和公开判定区，优先为乐不思蜀、兵粮寸断、闪电选择安全判定，再安排预计摸牌。v1–v23 不发布 Prompt，包 1.2.0/1.1.0/1.0.0 保留旧诸葛亮；伪造 Choice、顺序、同区重排、隐私、Checkpoint/Replay 与 WPF 保存恢复均有回归。
- `standard-classic-generals@1.4.0` 用 `classic:cao-cao` 替换经典池中的旧单技能曹操，并按奸雄、护驾的稳定顺序注册双技能。规则 v25 在曹操需要闪时发布护驾 Choice，再按当前行动顺序私有询问其他存活魏势力角色；提供者可使用自己的实体闪/转化牌或八卦阵，成功视为曹操响应，失败继续游标，全部失败再恢复曹操自有响应。v1–v24 不发布护驾，包 1.3.0 继续使用旧曹操；伪造 Choice、隐私、实体移动、盟友八卦失败、Checkpoint/Replay 与 WPF 恢复均有回归。
- `standard-classic-generals@1.5.0` 为 `classic:liu-bei` 增加激将，并按仁德、激将的稳定顺序注册双技能。规则 v26 同时覆盖出牌阶段目标杀和决斗/南蛮入侵杀响应：其他存活蜀势力角色按行动顺序获得自己的私有实体杀/转化牌 Choice，成功时提供者支付实体牌而刘备作为有效 source/responder；主动目标、攻击范围与出杀次数均按刘备计算，失败不消耗次数且人类可重试。v1–v25 与包 1.4.0 保留不含激将的刘备；伪造 Choice、隐私、属性杀、牌区、AI 防循环、暂停/完成 Checkpoint/Replay 与 WPF 双主动技入口均有回归。
- `standard-classic-generals@1.6.0` 为 `classic:sun-quan` 增加救援，并按制衡、救援的稳定顺序注册双技能。规则 v27 只在其他吴势力角色对濒死主公孙权使用桃时把统一恢复帧改为 2 点；实体桃、私有救援 Choice 和牌区移动不变，专用事件记录技能拥有者、提供者、实体牌及回复量。v1–v26 与包 1.5.0 保留不含救援的孙权；自救、非吴桃、酒和非濒死回复边界明确排除。
- `standard-classic-generals@1.7.0` 将 `classic:huang-gai` 以吴势力、4 点基础体力和 `standard:kujin` 加入正式经典池；既有主动技能帧已覆盖同一出牌阶段重复发动、每次失去 1 点体力并摸两张牌，以及 1 点体力发动后的濒死救援续接。包 1.6.0 保留不含黄盖的历史武将池和内容指纹；本切片不新增规则版本。
- `standard-classic-generals@1.8.0` 将 `classic:gan-ning` 以吴势力、4 点基础体力和 `classic:qixi` 加入正式经典池；rules v28 让黑色手牌或已装备牌成为过河拆桥的物理成本，复用其目标与无懈链，帧/事件保存有效牌型，牌区账本保存实际来源和实体牌型。包 1.7.0 保留不含甘宁的历史池，rules v27 不发布转换动作。
- `standard-classic-generals@1.9.0` 将 `classic:lu-meng` 以吴势力、4 点基础体力和 `classic:keji` 加入正式经典池；rules v29 在弃牌阶段按本回合 Play 阶段的有效杀使用/打出标记发布私有 Choice，发动后保留超上限手牌并结束回合，跳过则继续普通弃牌。包 1.8.0 保留不含吕蒙的历史池，rules v28 不发布克己选择。
- `standard-classic-generals@1.10.0` 将 `classic:zhang-liao` 以魏势力、4 点基础体力和 `classic:tuxi` 加入正式经典池；rules v30 在摸牌阶段枚举有手牌的其他角色一人/两人组合，发动以每名目标一张随机暗手牌替代普通摸牌，跳过则按通常数量摸牌。包 1.9.0 保留不含张辽的历史池，rules v29 不发布突袭选择。
- `standard-classic-generals@1.11.0` 将 `classic:xu-chu` 以魏势力、4 点基础体力和 `classic:luoyi` 加入正式经典池；rules v31 在摸牌阶段发布少摸一张/普通摸牌 Choice，发动后仅让许褚本回合由自己使用的杀或决斗伤害 +1。决斗响应失败时按实际攻击来源结算，因此对方反向造成的伤害不受裸衣加成；包 1.10.0 与 rules v30 保留旧池、普通摸牌和旧决斗归因。
- `standard-classic-generals@1.12.0` 将 `classic:dian-wei` 以魏势力、4 点基础体力和 `classic:qiangxi` 加入正式经典池；rules v32 每个出牌阶段限一次，发布 0–1 张手牌/装备区武器牌和攻击范围内一名其他角色的草稿。空牌分支失去 1 点体力，武器分支走统一弃置链，随后造成无实体牌来源的 1 点技能伤害并复用伤害后/濒死链；包 1.11.0 与 rules v31 保留旧池且不发布强袭。
- `standard-classic-generals@1.13.0` 将 `classic:xu-huang` 以魏势力、4 点基础体力和 `classic:duanliang` 加入正式经典池；rules v33 将自己的黑色基本牌或黑色装备牌从手牌/装备区当兵粮寸断使用，并把原生及转化兵粮寸断的目标距离上限改为 2。转化牌在判定区持久保存有效兵粮寸断身份，公开快照、去重、无懈和结算读取有效牌型，移动账本与弃置仍保留原实体牌；包 1.12.0 与 rules v32 保留旧池且不发布断粮。
- `standard-classic-generals@1.14.0` 将 `classic:zhen-ji` 以魏势力、3 点基础体力和 `classic:luoshen`+`classic:qingguo` 加入正式经典池；rules v34 在准备阶段以私有 Choice 驱动洛神的发动、重复与停止，黑色判定牌进入手牌，红色判定牌进入弃牌堆；倾国把黑色手牌作为有效闪接入杀与万箭齐发响应。包 1.13.0 与 rules v33 保留旧池且不发布两项技能。
- `standard-classic-generals@1.15.0` 将 `classic:huang-yueying` 以蜀势力、3 点基础体力和 `classic:jizhi`+`standard:qicai` 加入正式经典池；rules v35 在普通锦囊声明后、无懈询问或效果前发布私有集智 Choice，发动摸一张后恢复同一父结算游标；无懈可击自身触发，延时锦囊不触发。包 1.14.0 与 rules v34 保留旧池且不发布集智触发。
- `standard-classic-generals@1.16.0` 将 `classic:ma-chao` 以蜀势力、4 点基础体力和 `classic:tieqi`+`standard:mashu` 加入正式经典池；rules v36 在杀指定目标后、仁王盾与所有闪响应前发布私有铁骑 Choice，发动后复用公开判定与鬼才替换；红色结果禁止目标以实体闪、倾国、八卦阵或护驾响应当前杀，黑色结果或跳过继续普通响应。包 1.15.0 与 rules v35 保留旧池且不发布铁骑。
- `standard-classic-generals@1.17.0` 将 `classic:huang-zhong` 以蜀势力、4 点基础体力和 `classic:liegong` 加入正式经典池；rules v37 仅在出牌阶段的杀指定目标后，按目标公开手牌数是否不小于黄忠当前体力值或不大于黄忠攻击范围发布私有烈弓 Choice，发动后禁止全部闪响应入口，跳过或双条件均不满足继续普通响应。包 1.16.0 与 rules v36 保留旧池且不发布烈弓。
- `standard-classic-generals@1.18.0` 将 `classic:wei-yan` 以蜀势力、4 点基础体力和旧版锁定技 `classic:kuanggu` 加入正式经典池；rules v38 在伤害后按实际伤害来源到目标的公开战斗距离筛选，距离不大于 1 时按伤害点数自动回复，满体力不创建空恢复。包 1.17.0 与 rules v37 保留旧池且不触发狂骨。
- `standard-classic-generals@1.19.0` 将 `classic:lu-bu` 以群势力、4 点基础体力和锁定技 `classic:wushuang` 加入正式经典池；rules v39 让吕布的杀需要目标依次完成两次闪响应，并让与吕布决斗的另一方每轮依次完成两次杀响应。每一次实体牌、八卦阵、护驾、激将与转换牌仍复用独立原响应链；包 1.18.0 与 rules v38 保留旧池和单次响应。
- `standard-classic-generals@1.20.0` 将经典池中的 `standard:zhang-fei` 占位替换为蜀势力、4 点基础体力的 `classic:zhang-fei`，并以 `classic:paoxiao` 描述正式锁定技；运行时继续复用已有 `ModifySlashLimit` 和 AI 杀价值查询。包 1.19.0 保留 Standard 张飞的原池和内容指纹，不新增规则版本。
- `standard-classic-generals@1.21.0` 将经典池中的 `standard:zhao-yun` 占位替换为蜀势力、4 点基础体力的 `classic:zhao-yun`，并以 `classic:longdan` 描述正式杀闪双向转化；运行时继续复用已有有效牌型/物理实体分离、私有响应与 AI 转化成本查询。包 1.20.0 保留 Standard 赵云的原池和内容指纹，不新增规则版本。

无双是当前第三十二个被动查询入口：它通过 `ModifyRequiredResponseCount` 只把公开的杀/决斗响应次数从一改为二，不直接读取或移动暗牌；每张响应牌仍由原私有 Prompt 与牌区链支付，公开 `RequiredResponseProgressEvent` 只记录次数。其余三十一个既有被动入口保持原契约。

现有无双、狂骨、烈弓、铁骑、集智、奸雄、护驾、激将、救援、奇袭、克己、突袭、裸衣、断粮、洛神、倾国、反馈、咆哮、英姿、天妒、观星、空城、武圣、龙胆、马术、奇才、遗计、节命、援护、刚烈、鬼才、急救覆盖当前三十二个被动查询入口；另有独立的 `IActiveSkill` 主动技能入口。规则 v39 的无双只把杀/决斗的公开响应次数改为二，每次仍走独立响应窗；规则 v38 的旧版狂骨由实际伤害来源触发，只读取公开伤害量、体力与战斗距离，锁定按伤害点数回复并以体力上限截断；规则 v37 的烈弓只在出牌阶段的杀指定目标后按公开手牌数、当前体力与攻击范围发布私有发动/跳过 Choice，发动只对当前杀禁止全部闪响应入口，跳过或双条件均不满足恢复普通响应；规则 v36 的铁骑在杀指定目标后发布私有发动/跳过 Choice，发动复用公开判定与鬼才替换，红色结果只对当前杀禁止全部闪响应入口，黑色结果或跳过恢复普通响应；规则 v35 的集智在普通锦囊声明后发布私有发动/跳过 Choice，发动摸一张后恢复原无懈或效果游标，无懈可击自身触发而延时锦囊不触发；规则 v15 的经典身份局已让空城通过 `ProhibitsCardTarget` 同时禁止空手拥有者成为杀或决斗目标，v1–v14 和演示模式仍沿旧的杀专用入口；规则 v16 又让经典奸雄通过伤害技能效果解析取得任意仍在处理区的伤害牌，并提供私有取得/跳过 Choice，v1–v15 与演示模式继续自动取得杀类牌；规则 v17 的经典制衡把手牌与自己的公开装备合并为候选并限制每阶段一次，v1–v16 与演示模式继续手牌限定、可重复发动。反馈目前是目标存活且伤害牌仍在 `Processing` 时冻结并稳定排序候选、通过 `DamageTriggerWindowFrame` 游标发布私有选择的最小触发切片；遗计已经复用同一游标和私有 Prompt，完成受伤后摸两张牌并向其他存活角色交一张牌的跨手牌效果；节命复用同一游标和私有 Prompt，按公开手牌数量选择目标并补牌至其体力上限；援护通过 `DamageTriggerScope.OtherLivingPlayer` 参与其他角色受伤后的候选，使用拥有者手牌换取固定受伤目标的 1 点恢复；刚烈由受伤者触发公开判定，并在红色结果后向伤害来源发布私有弃牌/受伤选择；鬼才复用 JudgmentFrame 候选游标，在判定结果生效前发布私有替换/跳过 Choice，并将替换牌按统一牌区契约送入同一判定帧；天妒复用同一判定帧的结果后暂停点，在自己的判定牌生效后决定进入手牌或弃牌堆；观星在延时判定与摸牌前冻结私有牌堆顶切片，按顶端先取和底端最深优先两阶段排序；洛神复用公开判定与鬼才替换，在黑色结果后取得判定牌并重新发布私有继续/停止 Choice；护驾复用闪响应窗，激将复用杀的主动使用和响应续接，两者都按行动顺序发布私有势力候选并分开记录实体提供者与有效使用/响应者；救援复用濒死桃的私有响应、实体牌区和恢复帧，仅把另一名吴势力角色对主公孙权使用桃的回复量修正为 2；奇袭把黑色手牌或装备牌按过河拆桥使用，克己按回合内杀历史跳过弃牌，突袭以随机暗手牌转移替代普通摸牌，裸衣以少摸一张换取本回合由本人使用的杀/决斗伤害 +1，断粮把黑色基本牌/装备牌按兵粮寸断使用并将其距离上限修正为 2，倾国把黑色手牌按闪响应，集智把普通锦囊使用后的可选摸牌接回原父结算，铁骑把杀指定目标后的可选判定接回原响应链，烈弓把满足公开手牌条件的杀接回同一闪响应禁止链，狂骨把距离 1 内的来源伤害接入统一恢复帧，无双把杀/决斗接入连续响应计数；武圣是红牌按杀的最小牌转化切片；龙胆复用有效/物理牌型分离的出牌与响应入口；苦肉覆盖无牌/无目标主动效果，并已在 1 点体力发动时接入共享濒死/救援续接；制衡覆盖私有手牌/公开装备多选、混合来源弃置与等量摸牌，仁德覆盖私有手牌多选、其他存活角色目标白名单、跨手牌移动和按数量回复，青囊覆盖私有一张手牌/受伤目标选择、弃牌与恢复，回春覆盖私有双牌/多目标选择与逐目标恢复，反间覆盖无牌成本的其他角色目标、目标侧花色 Prompt、随机源手牌转移和普通伤害续接，激将覆盖无手牌成本的目标草稿、跨座位实体杀与原响应恢复，强袭覆盖体力或手牌/装备区武器二选一成本、攻击范围目标、无实体牌来源伤害和自损濒死后续接；八者均保留体力/回合边界、类型化事件和普通视图脱敏。更复杂的主动技能多目标选择和多效果结算仍需后续入口。通用伤害触发范围已由 `DamageTriggerScope` 开放，狂骨明确使用其中的 `DamageSource` 作用域，后续技能仍需在该范围上声明具体合法条件和类型化效果。新增武将技能前需要核心任务开放至少一项对应能力：

- 牌转化：武圣红牌按杀和龙胆杀/闪互转均已完成最小切片；后续牌转化仍需复用有效/物理牌型分离的入口；
- 主动选牌、弃牌和多目标选择；
- 通用伤害前后、复杂濒死救援和死亡奖励时机；基础单次伤害求桃已进入 K5 切片；
- 装备区/距离/攻击范围修正；
- 触发技能的优先级、询问和可中断流程。

没有这些入口时，宁可保留“暂无技能”的明确内容，也不复用语义不相符的现有技能名。

## 每批内容的验收

1. 内容定义有唯一 id/枚举映射、中文名、规则描述和必要的 AI 策略数据。
2. 牌堆数量、发牌参数、牌面生成顺序和确定性种子都有回归检查。
3. 普通快照、AI 快照和开发者视图的可见性不因内容字段而扩大。
4. `dotnet build .\CardGame.sln -c Release`、Console 自测、`dotnet format .\CardGame.sln --verify-no-changes` 均通过；若运行中的 WPF 锁住输出，需使用不结束用户进程的隔离构建并如实记录。
5. 不引入来源不明或商业受限的卡面、插画、音频、商标文案；内容仅使用代码和自绘文字牌面。
