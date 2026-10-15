# 内容任务的核心 API 请求

这些请求记录内容和核心共同需要的契约。当前由同一个长期 goal 统一推进；每个核心等级开放后，把对应的 manifest 项从 `planned` 改为可实现，并补齐场景测试。

2026-09-08 当前状态补充：规则行为版本为 5；致命伤害在存在伤害后触发候选时，会先完成可序列化的 `DamageTriggerWindowFrame`/`DamageSkillFrame` 游标，再进入 `DyingFrame`，而 v1–v4 回放保留旧事件顺序。最新验证为 Core 116/116、WPF 23/23。

M1 已开放：`ContentModeKind.Team`、`TeamCounts`、公开 `TeamId`/`TeamAssignedEvent`、队伍胜负和 AI 队友关系均已接入既有 Core 状态机；`standard-team-modes@1.0.0` 注册 `team:standard-2v2`，WPF 新局设置已消费同一模式元数据。普通玩家视图公开阵营但继续隐藏手牌、seed、私有 Prompt 和可信宿主字段；固定 seed 全 AI 对局、事件签名和 Checkpoint/Replay 已回归，当前验证为 Core 119/119、WPF 23/23。

M2 Lite 已开放：`ContentModeKind.NationalWarLite`、`FactionCounts`、双将选择/明置事件、按势力胜负和规则版本化双将体力已接入既有 Core 状态机；`standard-national-war-lite@1.1.0` 注册四人魏蜀 Lite 模式，WPF 新局设置、座位双肖像、指南、存档和历史结果消费同一模式元数据。M3 已开放国战 AI 明置评分、受约束的公开攻击证据推断和隐藏目标救援评分，以及 `standard-national-war-ambitious@1.0.0` / `national:ambitious-6` 的魏 3、蜀 2、野心家 1 六人独立势力试验；`SoloFactionIds` 仅是可验证的模式元数据，完整野心家规则仍未开放。AI policy v3 还把身份局火攻接入统一的公开攻击观察入口，并保持 v1/v2 的历史回放语义。普通玩家视图继续隐藏他人的暗将、技能、势力和手牌，内容注册要求国战显式声明带势力标签的专属武将池；固定 seed 全 AI 对局、三方隐私、AI 明置选择、公开攻击证据、隐藏目标救援评分、公开攻击中途暂停点 Checkpoint/Replay、终局嵌套结算收口、旧规则/旧内容包已回归，当前验证为 Core 135/135、WPF 45/45。完整国战 M3 仍待推进。

## K1：牌区与原子移动

内容需要：

- 内容规范层将稳定 `definitionId` 与本局唯一 `instanceId` 分离；K1 当前宿主仍以 `CardId`/`CardKind` 记录，正式运行时类型在 K3 Registry 冻结；
- `DrawPile`、`Hand`、`Processing`、`DiscardPile`、`Equipment`、`Judgment` 等强类型牌区；
- 单张/批量移动、替换装备、死亡清理和重洗的统一入口；
- 提交后可观察的 `CardMoved` 宿主通知；批量 `CardsMoving`/`CardsMoved` 触发时机属于 K5 类型化结算，不作为 K1 的内容消费前提。

该等级历史上阻塞了奸雄、反馈、遗计、过河拆桥、顺手牵羊、装备替换和所有“从目标区域取牌”的内容；当前过河拆桥/顺手牵羊已开放规则版本 4 的手牌不透明牌位选择与公开装备、公开判定区牌的精确目标，装备槽的 K6 基础入口、鬼才判定区替换和三种基础延时牌的最小入口也已开放，更复杂的目标区域仍需后续入口。临时 `CardGame.Core/Content.cs` 不继续扩大为正式 Registry。

## K2：命令、Revision 与完整 Prompt

内容需要：

- 主动技能、目标选择、私有候选和多牌选择的 `GameCommand`/`PendingPrompt`；
- `PromptId`、`Revision`、Responder 和精确 Choice 的验证；
- AI 与真人共享同一询问形状。

该等级历史上阻塞了仁德、制衡、苦肉、过河拆桥、顺手牵羊、装备主动效果和选将；当前过河拆桥/顺手牵羊已经消费精确目标 Choice，公开装备和公开判定区牌分支进一步消费带 `TargetCardId` 的精确 Choice，装备使用也消费精确的无目标 Choice，复杂装备主动效果仍未开放。UI 不能通过按钮参数拼接“合法卡牌集合 × 合法目标集合”来替代完整 Choice。

## K3：内容 Registry 与包清单

内容需要：

- `standard:*` 命名空间 ID 的不可变注册表；
- Card、Skill、General、DeckRecipe 的定义、行为和依赖引用；
- 重复 ID、未知引用、依赖环和内容哈希校验；
- 同一内容包在不同 Registry/对局之间不共享可变状态。

阻塞内容：正式 Standard 包、稳定录像/存档兼容和新 `CardKind`/`SkillKind` 的迁移。当前 manifest 可以先审阅，但不应在 Core 闭合枚举上继续堆成员。

## K4：模式开局与选将

内容需要：

- 8 人/5 人身份的 `ModeDefinition`、RoleDistribution 和 `DeckRecipe`；
- 选将候选的私有 Prompt、共享池冲突策略和 AI 选择策略；
- 首行动者、发牌和角色初始化的模式策略。

K4 已开放：`ContentModeDefinition` 提供 `DeckId`、候选数量和 `GeneralPoolIds`；`GameEngine` 在 `UseInteractiveSetup = true` 时执行可暂停的私有单将选将、共享池去重、公开结果、确定性洗牌和逐轮发牌。旧八人固定 Demo 继续作为兼容模式，不在内容线程复制状态机。

仍阻塞内容：国战双将；5/8 人更大规模固定 seed 矩阵、同时选将和模式策略对象属于 K4 扩展验证，2v2 的更复杂赛制和多队变体仍由后续 M1 扩展处理。

## K5：类型化卡牌结算与响应链

内容需要：

- `CardUseDeclared`、目标确认、效果序列、响应窗口和 `CardUseFinished`；
- 伤害、属性伤害、回复、基础濒死求桃、死亡和奖惩时机；
- 可暂停恢复的嵌套结算帧与稳定事件顺序。

当前已开放的 K5 基础切片：`standard:slash`、`standard:fire_slash`、`standard:thunder_slash`、`standard:dodge`、`standard:peach`、`standard:alcohol`、`standard:duel`、`standard:draw_two`、`standard:barbarian_assault`、`standard:arrow_barrage`、`standard:peach_garden`、`standard:five_grains`、`standard:dismantlement`、`standard:snatch`、`standard:fire_attack`、`standard:indulgence`、`standard:supply_shortage`、`standard:lightning`、`standard:iron_chain` 已接入 `CardUseFrame`、`ResponseWindowFrame`、`DamageFrame`、`DamageTriggerWindowFrame`、`DamageSkillFrame`、`RecoveryFrame`、`DyingFrame` 和 `DeathFrame`；决斗通过 `RespondSlash` 让双方交替响应，无中生有通过空目标 `CardUseFrame` 摸两张牌，南蛮入侵/万箭齐发通过 `CardUseFrame.TargetIndex` 按座次逐目标发布私有 `RespondSlash`/`RespondDodge`，桃园结义通过同一目标游标按座次逐目标恢复，五谷丰登通过同一目标游标公开翻牌并发布私有 `SelectHarvestCard`，过河拆桥通过目标 `CardUseFrame` 在规则版本 4 压入 `TargetCardSelectionFrame`，发布只含 slot index 的私有不透明牌位 Prompt（v1–v3 回放仍按确定性随机数盲弃），或通过 `TargetCardId` 精确弃置目标公开装备/判定区牌，顺手牵羊通过 `GetCombatDistance` 限制战斗距离一，并在规则版本 4 发布同一不透明牌位 Prompt（v1–v3 回放仍按确定性盲取），或通过 `TargetCardId` 精确取得目标公开装备/判定区牌；火杀/雷杀共用杀的闪响应，实际牌种分别映射为 `DamageNature.Fire`/`DamageNature.Thunder` 并贯穿 `DamageFrame`、`DamageRequestedEvent`、`DamageAppliedEvent`、`AfterDamage` 和 `DamageSkillContext`；铁索连环通过精确一/二目标和公开 `IsChained` 标记接入效果前有限无懈窗口，火/雷伤害命中连环目标时按固定顺序传导同额属性伤害；酒通过公开 `HasAlcoholEffect`、`AlcoholAppliedEvent`/`AlcoholExpiredEvent` 和实际伤害金额接入直接杀链路，直接杀声明时消费酒效并使 `DamageFrame`、伤害请求/应用/AfterDamage 与 `DamageSkillContext.Amount` 为 2；规则 v12 起只有濒死者本人可通过自己的私有 `RescueDying` Prompt 使用酒自救 1 点体力，并生成标记 `DyingResponseEvent.UsedAlcohol` 的响应和 `RecoveryAppliedEvent`；v3–v11 保留跨座位兼容回放，v1/v2 也保持原仅自救语义；`GroupResponseEvent` 记录必需响应牌种类，`TargetCardDiscardedEvent`/`TargetCardTakenEvent` 对隐藏手牌只发布脱敏效果，对公开装备/判定区牌携带已经公开的实体 ID/牌型，反馈通过 `DamageTriggerCandidate`/`DamageTriggerOrdering` 收集并稳定排序，在伤害目标的伤害牌仍位于 `Processing` 时先压入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`；当前规则版本即使目标因此降至 0 点体力，也先完成这条有序伤害后触发窗口，再进入可恢复的 `DyingFrame`，历史 v1–v3 回放仍保留原事件顺序。随后为当前候选发布私有 `Feedback` 选择并压入 `DamageSkillFrame` 与 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留 `CandidateId`/`Priority`，发动才通过 `DamageCardClaimedEvent` 将同一实体牌移入技能拥有者手牌；遗计在同一窗口内发布私有 `Yiji` 牌/目标组合，先将两张牌从 `DrawPile` 移入拥有者手牌，再按 `skill.yiji.give-card` 将其中一张移至其他存活角色手牌，每张伤害牌至多一次，每个未响应目标独立续接单次伤害/濒死链，单次伤害后的基础濒死窗口通过 `AnswerPromptCommand` / `DecisionKind.RescueDying` 逐座询问，桃只从当前 responder 的私有手牌移动；酒在 v12 还要求当前 responder 就是 victim，两者均经 `Processing` 再弃置，救援完成后恢复原结算帧。武圣允许红色非杀实体牌生成有效牌型为 `Slash` 的动作；`LegalAction.PlayedCardKind` 与 `PlayCardCommand.PlayedCardKind` 明确本次有效牌型，事件记录 `Slash` 而移动账本继续记录原始物理牌；龙胆使用 `PlayedCardKind`、`response-card-kind` 和 `CardRespondedEvent.EffectiveCardKind` 完成闪/杀互转；刚烈通过 `DamageTriggerCandidate`/`DamageSkillFrame` 支持受伤者私有发动、公开判定、红色结果后的来源私有两牌反制和濒死续接；青釭剑通过装备 modifier 在直接杀声明时设置 `CardUseFrame.IgnoresArmor`，同步写入声明/使用事件并禁止目标生成八卦阵选项；回春通过精确两张私有手牌与 2–3 名受伤存活目标选择，逐目标压入 `RecoveryFrame` 并记录 `skill.huichun.discard`；马术通过公开 `GetCombatDistance` 修正出攻距离并复用杀/顺手牵羊合法动作。Console 自测当前为 115 项，WPF 已显示酒效状态、濒死私有选择、v12 酒仅自救与 v3–v11 跨座位兼容回放、普通/属性杀响应、武圣转化按钮、苦肉无牌主动技能、苦肉 1 点体力濒死后复用私有救援窗口并在获救后摸牌、制衡私有多选弃牌与等量摸牌、反馈/遗计/援护/刚烈/鬼才私有触发、遗计牌/目标组合、群体牌响应、桃园结义恢复游标、五谷丰登公开牌/私有选牌、过河拆桥目标选择和顺手牵羊战斗距离一目标选择、过河拆桥/顺手牵羊公开装备/判定区牌目标选择、火攻两段私有选牌和装备/距离状态、八卦阵判定成功/失败、青釭剑无视防具、判定区移动和红色判定视为闪、铁索连环精确目标/公开标记/火雷传导、乐不思蜀/兵粮寸断/闪电红黑分支、闪电命中/转移/雷电伤害、延时牌收尾；反馈、遗计、武圣、龙胆、青釭剑、铁索连环、鬼才与闪电结果由 AI 思考、技能状态文本和事件流可观察；可信宿主可记录已接受命令并用 CommandJson/GameReplay 确定性重放。

本轮补充的急救层消费同一濒死入口：`IPassiveSkill.CanUseAsDyingRescue` 只负责回答物理牌是否可转化，Core 仍负责 `CardUseFrame`、`RecoveryFrame`、死亡和胜负；红色实体牌以有效 `Peach` 进入声明/完成事件，`DyingResponseEvent.UsedPeachPhysicalCardKind` 和移动账本保留原始物理牌型。rules v41 的经典身份从手牌与自己的装备区枚举候选并按实际来源支付，rules v40 与非经典演示模式仅枚举手牌。`standard-rescue-skills@1.0.0` 单独依赖主动技能包，旧 `CreateWithActiveSkills()` Registry、旧事件形状和旧 checkpoint 签名不被静默改写。

阻塞内容：多伤害/技能型濒死响应和更复杂的主动技能；当前伤害候选排序、可序列化触发窗口游标、可信宿主窗口生命周期事件以及 `DamageTriggerScope` 座位范围契约已开放，反馈与遗计已分别覆盖处理区取牌和跨座位赠牌，援护复用 `OtherLivingPlayer` 范围；规则 v3–v11 的跨座位酒救援只作为版本兼容保留，v12 已按正式规则关闭。火攻已覆盖目标私有展示、同花色弃牌和火焰伤害，铁索连环已覆盖精确一/二目标、公开状态标记、有限无懈窗口和火/雷同额传导，K6 基础装备已覆盖五类槽位、替换、死亡清理、战斗距离、攻击范围、摸牌和青釭剑无视防具、仁王盾阻挡黑色杀 modifier，K7 已覆盖无懈可击的有限多层响应；属性抗性/转化、南蛮入侵/万箭齐发的多伤害嵌套、复杂跨座位效果和复杂变体仍属于后续扩展。

本批已开放 `standard:feedback`、`standard:yiji` 与 `standard:ganglie` 的最小可选伤害后触发：目标存活且伤害牌仍在 `Processing` 时，Core 收集当前存活拥有者的 `DamageTriggerCandidate`，通过 `DamageTriggerOrdering` 稳定排序后压入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前候选压入 `DamageSkillFrame`；反馈通过私有 `Feedback` Choice 决定是否按 `skill.feedback.claim-damage-card` 取得伤害牌，遗计先发布私有两张摸牌，再通过 `Yiji` 牌/目标 Choice 按 `skill.yiji.give-card` 将其中一张移给其他存活角色。帧和 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留 `CandidateId`/`Priority`，遗计额外发布 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent`，刚烈发布判定/反制结果事件，普通视图不泄漏隐藏牌 ID，群体父帧仍可继续推进。`DamageTriggerScope`/`AfterDamageTriggerScope` 现已把受伤者、其他存活角色和任意存活角色的座位关系变成可复用规则，援护使用该通用范围；更复杂伤害后效果仍等待后续 K5。

本批同时开放 `standard:wusheng` 的最小牌转化：红色且不是原生杀/火杀/雷杀的实体牌可以作为杀使用或打出。合法动作、`PlayCardCommand` 和兼容 `HumanPlay` 均能通过可选 `PlayedCardKind` 选择有效牌型；规则 v40 的经典身份另为 `classic:wusheng` 枚举自己的红色装备，并让主动/响应支付从实际 `Hand` 或 `Equipment` 进入处理区。事件/伤害链记录有效 `Slash`，`CardMovementRecord` 保留同一物理牌的原始 `CardKind`、实体 ID 和来源区，避免复制牌或让 UI 绕过完整合法性检查。
本批同时开放 `standard:longdan` 的最小响应型牌转化：物理闪可在出牌阶段产生有效 `Slash`，物理杀/火杀/雷杀可在需要 `Dodge` 的响应窗口产生有效 `Dodge`；`PendingDecision.Choices` 为每张物理牌发布 `response-card-kind`，`CardRespondedEvent.EffectiveCardKind` 记录有效响应牌型，实体牌继续通过统一 `Hand → Processing → DiscardPile` 路径。AI 只读取自己的玩家视图和合法响应列表，WPF 通过精确响应选择展示原生/转化差异。

本轮新增 `standard:jijiu` 的最小濒死牌转化：急救者在私有 `RescueDying` Prompt 中将红色非桃实体牌当作桃使用，`physical-card-kind` 参数、`DyingResponseEvent.UsedPeachPhysicalCardKind`、有效 `CardUseDeclaredEvent/CardUseFinishedEvent` 和 `Hand/Equipment → Processing → DiscardPile` 移动共同表达“同一实体、不同有效牌型”。rules v41 只为经典华佗开放自己的装备区，旧规则与演示模式仍限手牌。AI 与 WPF 只消费本座私有候选，普通快照不泄漏牌 ID；内容通过独立 `standard-rescue-skills@1.0.0` 包接入，旧主动技能包仍可单独恢复。

本轮继续在 K5 开放 `standard:yuanhu` 的受约束跨座位触发，并将座位关系抽为通用契约：`DamageTriggerScope.OtherLivingPlayer` 使其他角色受到正伤害且仍存活、未满体力时，援护者获得私有 `DecisionKind.Yuanhu` 弃牌 Choice；发动时按 `skill.yuanhu.discard` 将一张自己的手牌经过 `Hand → Processing → DiscardPile`，再由 `RecoveryFrame` 令固定受伤目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 和 `DamageSkillResolvedEvent.EffectTargetSeat` 记录可信宿主证据，普通视图不泄漏弃牌 ID；AI 只读取援护者自己的手牌和公开目标状态。更复杂跨座位效果、多伤害嵌套和完整状态 Checkpoint 仍待后续 K5/K8。

本轮新增 `standard:ganglie`：刚烈者在受到正伤害后收到私有 `DecisionKind.Ganglie` 发动/跳过 Choice；发动后公开判定，红色结果向伤害来源发布私有 `DecisionKind.GangliePunish`，由其选择精确两张手牌弃置或承受 1 点伤害。判定区移动、结果事件和 `skill.ganglie.discard` reason 沿 Core 账本提交；反制伤害若触发濒死，救援完成后回到原伤害技能帧和候选游标，AI 与普通视图都不会获得对方的隐藏牌 ID。

本轮新增 `standard:guicai`：判定牌翻入 `Judgment(target)` 后、判定生效前，Core 通过 `JudgmentFrame` 冻结当前鬼才候选和游标，并为拥有者发布私有 `DecisionKind.Guicai` 替换/跳过 Choice。替换牌按 `Hand(owner) → Processing → Judgment(target)` 移动，旧判定牌先按 `judgment.finish` 结束；最终结果由 `JudgmentResolvedEvent` 公开，`JudgmentReplacementRequestedEvent`/`JudgmentReplacementResolvedEvent` 和 `skill.guicai.replace` 提供可信宿主回放证据，AI 只读取自己的过滤快照。

本轮新增 `standard:indulgence` 与 `standard:supply_shortage`：使用者选择一名其他存活角色后，乐不思蜀或兵粮寸断从 `Processing` 进入目标公开 `Judgment` 区；目标下回合摸牌前按顺序复用 `JudgmentFrame` 判定。规则 v11 起，乐不思蜀非红桃跳过出牌阶段，兵粮寸断非梅花跳过摸牌阶段，多个延时效果按位累计；v1–v10 保留历史红黑语义。`DelayedCardPlacedEvent`/`DelayedCardResolvedEvent`、`card.effect.delayed-place`/`card.effect.delayed-finish` 和死亡清理共同保证延时牌不残留；无懈可击与鬼才沿同一效果前/判定前窗口接入。

当前 Console 自测为 113 项、WPF 自测为 22 项，完整解决方案 Release 构建为 0 warning / 0 error，`dotnet format --verify-no-changes --no-restore` 通过；可信命令前缀 Checkpoint 可恢复私有 Prompt 暂停点，并通过内容指纹拒绝同版本定义漂移；新增覆盖急救扩展包隔离、红牌筛选、AI/人类选择、有效/物理牌型事件、私有视图脱敏、处理区移动和 checkpoint/replay，与既有援护、刚烈、鬼才、v12 酒仅自救与 v3–v11 跨座位兼容回放、牌区移动、装备/距离和延时牌场景一起保持通过。

## K5：主动技能选择切片已开放（2026-09-08）

- `IActiveSkill` 只返回可序列化的合法性和效果数据；`UseSkillCommand` 既支持苦肉的空牌/空目标，也支持制衡的私有手牌/公开装备多选集合和回春的精确两牌/多目标集合，统一经过 Revision、PromptId、数量、重复项和所有权校验；
- `LegalActionKind.UseSkill`、`ActiveSkillFrame` 与 `ActiveSkillRequestedEvent`、`SkillHpLostEvent`、`SkillCardsDiscardedEvent`、`SkillCardsGivenEvent`、`SkillCardsDrawnEvent`、`ActiveSkillResolvedEvent` 接入同一提交/回放链；`skill.kujin.draw`、`skill.zhiheng.discard`、`skill.zhiheng.draw`、`skill.rende.give-card`、`skill.qingnang.discard`、`skill.huichun.discard` 记录可信移动账本；
- `standard-active-skills@1.0.0` 依赖 `standard@1.11.0`，注册五项主动技能、`standard:mashu`/`standard:qicai` 两项被动技能和七个演示武将，WPF 默认窗口显式选择扩展 Registry；普通 Standard Registry、旧命令回放和普通玩家快照不被扩展牌 ID 污染或泄密；
- 苦肉在出牌阶段且体力大于 0 时失去 1 点体力并摸两张牌；若正好降至 0，Core 保留 `ActiveSkillFrame`，沿共享私有 `RescueDying` 窗口完成救援或死亡清理，获救后才继续摸牌。规则 v17 的经典制衡在每个出牌阶段限一次，可弃置至少一张自己的手牌或公开装备并摸等量牌；v1–v16 和演示模式保留手牌限定与可重复发动。`standard-active-skills@1.0.0` 的演示仁德仍按回合限一次并沿历史目标回复路径；rules v100 + 经典包 1.78.0 的正式仁德则可在同一出牌阶段跨目标重复交牌，以阶段账本累计实体牌，首次达到两张时只令技能拥有者回复一次。青囊在出牌阶段私有选择一张手牌和一名受伤存活角色，经过 `Processing` 弃置并令目标恢复 1 点体力，当前回合只能发动一次。回春在出牌阶段私有选择精确两张手牌和 2–3 名受伤存活角色，逐目标恢复 1 点体力，当前回合只能发动一次。更复杂的多目标选择和多效果结算仍属于后续 Core API。
- 本批实测为 Core `115/115`、WPF `23/23`；Debug/Release 构建均为 0 warning / 0 error，`dotnet format --verify-no-changes` 和 `git diff --check` 通过。

## K6：距离与装备（基础切片已开放）

内容需要：

- `EquipmentSlot` 五类槽位（武器、防具、进攻坐骑、防御坐骑、宝物），以及装备/替换/死亡清理；
- `LegalActionKind.Equip`、`EquipmentChangedEvent` 和 `equipment.use`/`equipment.enter`/`equipment.replace`/`rule.death-equipment-discard` 移动与事件语义；
- `GetCombatDistance`、`GetLegalActions`、`GetAttackRange` 和数据型装备 modifier 查询；
- `PlayerSnapshot.Equipment` 的公开投影，供普通玩家、AI 和 WPF 观察，不扩大任何手牌可见性。

当前已开放：规则 v13 的诸葛连弩（攻击范围 1、杀次数不受限）与青釭剑（攻击范围 2、直接杀无视目标防具），v1–v12 保留旧范围；赤兔（出攻距离 -1）、绝影（受攻距离 +1）、玉玺（摸牌 +1）、仁王盾（阻挡黑色杀）和八卦阵的槽位、公开判定防御生命周期也已接入。过河拆桥/顺手牵羊可对公开装备或判定区牌进行精确目标选择。阻塞内容：装备失效/卸载效果和依赖范围的后续武将技能；不能把装备当成一次性 `Basic` 卡处理。

## K7：判定和多层触发（基础切片已开放）

内容需要：

- 判定区、判定牌移动、复杂改判窗口和延时锦囊；
- 跨事件 Trigger 收集、优先级、询问和稳定排序；K5 当前只开放伤害候选的收集/排序边界；
- 事件取消/替换而不破坏牌区守恒。

当前 K7 已开放八卦阵判定闭环：规则 v14 起直接杀与万箭齐发需要闪时均可选择，公开判定牌进入 `Judgment(seat)`，以 `JudgmentFrame` 和 `JudgmentRequestedEvent`/`JudgmentResolvedEvent` 记录结果，红色判定视为闪并按固定移动 reason 进入弃牌堆；群体结果写入不带物理牌 ID 的 `GroupResponseEvent`，v1–v13 只保留直接杀响应。仁王盾在 v14 于黑色杀声明和目标确定后提交 `ArmorEffectAppliedEvent` 并令其无效，青釭剑可绕过，旧规则仍在合法目标层过滤。同时可抵消锦囊在效果结算前进入 `NullificationWindowFrame`，按固定座次发布私有 `DecisionKind.Nullification`；铁索连环通过精确一/二目标和公开 `IsChained` 状态接入效果前无懈窗口；`standard:guicai` 已接入判定翻牌后的私有替换；乐不思蜀/兵粮寸断/闪电复用同一判定帧和延时牌收尾。复杂改判、重复判定和更复杂的多层响应仍待后续。
阻塞内容：复杂改判和其他复杂多层响应；乐不思蜀/兵粮寸断/闪电、无懈可击与鬼才的当前基础切片已开放。

## K9：内容级 AI 策略

内容需要：

- 卡牌/技能/武将定义携带可解释的 AI 标签或策略对象；
- AI 只能从自己的 `PlayerGameView` 和公开事件知识中评分；
- 策略随机数独立且可记录、可复现；
- AI 候选在开发者诊断视图之外不泄露。

当前 `CardCatalog` 的数值字段是兼容过渡；正式策略挂载应在 Registry 和通用 AI 行动入口稳定后迁移。

## Core API Level: K1 已开放（2026-09-07）

核心线程已按长期 goal 通知 K1 已开放。契约文件为 `docs/MASTER_PLAN.md`、`docs/CORE_CONTROL_DESIGN.md` 和 `docs/ARCHITECTURE.md`；内容侧新增 READY 项当时仅为 `C0-K1 卡牌移动契约包`，正式内容 Registry 已在后续 K3 开放，类型化结算/技能触发已在后续 K5 基础切片开放。

- K1 可消费类型包括 `CardZoneKind`、`CardLocation`、`CardMoveReason`/`CardMoveReasons`、`CardMovementRecord`、`CardZoneDiagnostic`，以及 `GameEngine.CardMovements`、`CardMoved`、`ObserverFailures` 和 `CreateCardZoneDiagnostics()`；
- 稳定语义是所有实体牌恰处一个区域，单张/同源批量/通用批量移动先整体校验再生效，公共操作提交且不变量通过后才派发宿主通知；
- `CardMoved` 是提交后的宿主诊断通知，不是内容触发钩子；内部 `CardsMoving`/`CardsMoved`、EventBatch 和技能触发留待 K5，Revision/PromptId 留待 K2；
- K1 当时实测中 `CardGame.Core` 与 `CardGame.Core.Tests` Release 编译通过，Console 自测 30/30，`dotnet format --verify-no-changes` 通过；WPF 在隔离输出中 Release 编译为 0 警告/0 错误，整套解决方案的原始 WPF 输出仍受当时的 PID 81656 文件锁影响。

该历史批次只允许更新 `docs/content/**`，并要求补充牌移动生命周期、reason ID、可见性和 K1 强制场景；后续 K3/K5 已分别开放正式 Standard 包、奸雄/反馈/遗计的最小切片以及目标手牌取放的受限效果，K6 现已开放装备区基础槽位与生命周期，判定区在该历史批次仍保持 BLOCKED，已由后续 K7 基础切片开放。内容任务不会在 Core 中补缺失类型，也不会用反射、全局状态或 UI 分支绕过上述状态。

## Core API Level: K2 已开放（2026-09-07）

- 新增 `GameCommand`、`Submit`、`CommandResult` 和稳定 `CommandErrorCode`；`Revision` 是每次成功公共操作的提交版本，过期命令不改变状态；
- `PendingDecision` 增加 `PromptId`、发布 `Revision` 和完整 `PromptChoice`。出牌的牌/目标组合、结束出牌、打闪和不响应都由核心逐项发布；
- 旧 `HumanPlay`、`HumanEndPlay`、`HumanRespond` 作为适配器继续有效。内容侧可以编写主动技能和多目标选择的场景契约，但完整主动技能效果仍等待 K5/K6/K7；
- K2 强制验证：响应者、PromptId、ChoiceId、Revision、卡牌所有权和精确目标列表；拒绝结果不使用玩家可触发的异常。

## Core API Level: K3 已开放（2026-09-07）

- 新增 `ContentRegistry`、`IGameContentPackage`、`PackageManifest`、包依赖校验和 `Content*Definition` 类型；内容 ID 必须使用命名空间，例如 `standard:slash`；
- 新增 `src/CardGame.Content.Standard/StandardContentPackage.cs`，正式注册标准基本牌、兼容技能、武将、基础演示牌堆和八人身份模式元数据；
- Registry 构建会拒绝重复 ID、未知卡牌/技能引用、版本不足和依赖环，并把字典、列表和包清单冻结为独立只读投影；
- `GameEngine.CreateStandard(options, registry)` 已消费 `standard:basic-demo` 牌堆配方；未传 Registry 时仍走 Core 兼容内容，避免一次性破坏旧 WPF 调用；Standard 包现在包含 `standard:five_grains` 的公开 draft 元数据；
- 本阶段可实现：标准内容定义、牌堆配方、内容级 AI 标签、Registry 隔离和依赖测试；复杂技能、装备、锦囊仍等待 K5-K7，选将已在后续 K4 切片开放。

## Core API Level: K4 开局切片已开放（2026-09-07）

- `GameOptions` 可选择 `ModeId`、`DeckId` 和 `UseInteractiveSetup`；Standard Registry 的 5/8 人身份模式提供角色分布、牌堆、候选数量和武将池；
- 新增 `SelectGeneralCommand`、`DecisionKind.SelectGeneral`、`EngineStatus.AwaitingHumanGeneralSelection`、`PendingDecision.ValidContentIds` 和 `PromptChoice.ContentIds`；
- `Start`、`Advance` 和 `AdvanceOneStep` 可在 AI 选将或真人选将处暂停，所有选择完成后公开武将、确定性洗牌并逐轮发牌；
- 选将候选、`GeneralSelectionRequestedEvent`、`GeneralSelectedEvent` 和 `AiGeneralThought` 属于可信宿主/当前 responder，普通玩家视图不包含他人的候选或未公开武将；
- WPF 已接入新流程，旧 `HumanPlay` 等兼容入口与 `UseInteractiveSetup = false` 仍可用；Console 当前覆盖 111 项场景，另有 K5 结算帧/事件栈、普通/属性杀伤害、武圣红牌按杀、反馈/遗计/节命/援护/刚烈/鬼才 AI 与人类可选触发、遗计跨手牌分配、节命目标补牌与刚烈判定/来源反制、鬼才判定替换、苦肉无牌主动技能、苦肉 1 点体力濒死后的私有救援续接、制衡私有多选弃牌与等量摸牌、青囊私有弃牌/受伤目标选择与恢复、回春私有双牌/多目标选择与逐目标恢复、命令日志 JSON 编解码与确定性重放、可信命令 Checkpoint 私有 Prompt 恢复和同版本内容漂移拒绝、群体父帧续接、伤害触发候选稳定排序、酒的一次性直接杀 +1 伤害与回合结束失效、v12 酒仅自救与 v3–v11 跨座位兼容回放、决斗响应、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、无懈可击有限多层响应和铁索连环精确目标/火雷传导回归，并覆盖 K6 装备槽替换、距离/范围 modifier（含马术公开距离修正）及锦囊距离豁免、玉玺摸牌、青釭剑无视防具、仁王盾阻挡黑色杀、过河拆桥/顺手牵羊公开装备或判定区牌目标和阵亡清理，以及 K7 八卦阵判定成功/失败、判定区移动、鬼才私有改判、乐不思蜀/兵粮寸断/闪电红黑分支、命中/转移/雷电伤害和累计延时收尾与视图脱敏。

## Core API Level: K5 基础结算切片已开放（2026-09-07）

- `ResolutionStack` 当前支持杀/闪响应、无中生有无目标效果、伤害、恢复、基础濒死和死亡帧；`DyingResponseEvent`、`DyingResolvedEvent` 等类型化事件在提交后发布；
- `ResolutionStack` 当前支持普通/火/雷杀的统一闪响应；`DamageNature` 与实际牌种一同进入 `DamageFrame`、伤害请求/应用/后置事件和奸雄伤害上下文，属性抗性和属性转化仍未开放；
- 濒死阶段新增 `EngineStatus.AwaitingHumanDying`、`PendingDecision.TargetSeat` 和 `DecisionKind.RescueDying`，普通玩家视图只获得自己作为当前 responder 的桃选项；
- 非法 `ChoiceId`、过期 Revision 和非当前 responder 的回答保持拒绝且零状态变化；AI 濒死决策只使用脱敏快照与自己的桃，不读取其他玩家手牌；
- `standard:barbarian_assault`、`standard:arrow_barrage` 与 `standard:peach_garden` 已验证无目标群体牌的完整目标列表、`CardUseFrame.TargetIndex`、逐目标响应/恢复、由 `GroupCardUsedEvent` 声明目标列表且由 `GroupResponseEvent` 携带 `RequiredCardKind`、处理区驻留和每个目标的伤害/濒死/恢复续接；`standard:dismantlement` 另验证单目标选择、目标隐藏手牌的不透明牌位选择、公开装备/判定区牌的 `TargetCardId` 精确选择、`TargetCardDiscardedEvent` 按来源区域脱敏与双牌处理区移动账本；`standard:snatch` 验证战斗距离一合法性、目标隐藏手牌不透明牌位选择、公开装备/判定区牌的 `TargetCardId` 精确选择、`TargetCardTakenEvent` 按来源区域投影、取得牌进入使用者私有快照以及跨玩家处理区移动账本；AI 响应、恢复、不透明牌位选择和公开目标牌评分只使用当前目标/全局公开体力/手牌数量/公开装备/判定区的脱敏快照；
- 反馈、遗计和节命共享 `DamageTriggerWindowFrame`/`DamageSkillFrame`；其中反馈取得处理区伤害牌，遗计私有摸两张并交一张，节命在受伤者的私有 `Jieming` Prompt 中按公开手牌数选择目标并从牌堆补至体力上限，AI 不读取目标隐藏牌面，`DamageSkillCardsDrawnEvent.TargetSeat` 与 `skill.jieming.draw` 记录可信宿主结果；
- rules v90 提供 `PlayerMarkerKind`、`PlayerMarkerChangedEvent` 与公开正数 `PlayerSnapshot.Markers`；D4a 的合成武魂消费者在 `DamageAppliedEvent` 后、`PlayerDyingEvent` 前按每点实际伤害增加梦魇，Checkpoint 由命令前缀重建，rules v89 保留无标记历史路径。死亡拥有者候选、并列选择、死亡判定与直接死亡仍属于 D4b；
- rules v91 在可信引擎状态中以 `(PlayerMarkerKind, SkillOwnerSeat)` 归属同名标记，普通快照仍只公开合计；`GameRules.GetMaximumMarkerCandidates` 对单一来源排除死亡/零计数并返回全部正数最大并列者。可暂停的死亡拥有者选择、判定、直接死亡和来源标记清理仍属于 D4b2；
- rules v92 新增可序列化 `DeathSkillFrame`、已死 responder 的 `DecisionKind.WuhunTarget`、死亡技能开始／选定／完成事件和 `DirectDeathDeclaredEvent`。武魂复用普通判定与既有改判候选游标；直接死亡走嵌套 `DeathFrame`，不进入 `DyingFrame`，完成后只清理该技能拥有者来源的梦魇。胜负已确定时短路；rules v91 不启动此链；
- rules v93／skill-program schema 9 新增判定后通用 `causeDeath`：`ProgramCauseDeathDeclaredEvent` 保存 cause、父窗口、判定、技能绑定、来源和目标，统一死亡生命周期不生成 Damage、濒死、killer 或击杀奖惩；目标自己的死亡技能可嵌套，返回后继续外层候选，终局则完成必要判定收尾并跳过新动作；rules v92 明确拒绝 schema 9 内容；
- rules v94／skill-program schema 10 新增强制 `cardIdentities` 与来源限定的 `slashDistanceLimit`：只在拥有者手牌区替换普通使用／响应身份，接受动作时冻结物理牌、有效牌名和绑定来源，距离修正不外溢到次数、目标、响应、防具或属性；rules v93 明确拒绝 schema 10 内容；
- rules v95 新增通用身份模式神势力设置：`DecisionKind.SelectFaction` 复用 `AnswerPromptCommand`，`PlayerSnapshot.FactionId` 仅在本人私有可见或随神将公开后投影本局有效势力，`GodFactionSelectedEvent` 供可信宿主审计；内容武将的印刷 `FactionId: god` 不变，rules v94 不生成此设置；
- rules v96 新增结构化 `SkillTag`／`SkillExecutionForm` 和 `SkillRuntimeStateStore`：内容标签进入 hash schema 9 扩展但不从中文推断、不改变优先级；阶段／回合／轮／整局限次及阳／阴状态可按稳定技能 ID 记录和重置，正式涅槃先迁移整局账本，rules v95 保留旧限定技能集合；
 - 当前边界仍是单次伤害的基础求桃；多伤害嵌套、通用触发器和复杂技能仍待后续 K5；可信命令前缀 Checkpoint 已开放，可恢复命令驱动的私有 Prompt 暂停点，规范化内容指纹也已用于拒绝同版本定义漂移；完整内部状态存档、可执行规则实现签名和兼容迁移仍等待后续 K8。

## Core API Level: K6 基础切片已开放（2026-09-07）

- `EquipmentSlot` 提供武器、防具、进攻坐骑、防御坐骑、宝物五类槽位；`PlayerSnapshot.Equipment` 是公开投影，不把其他玩家手牌或装备候选泄漏给普通视图。
- 装备使用通过 `LegalActionKind.Equip` 和精确无目标 Choice 提交；装备实体按 `Hand → Processing → Equipment` 移动，同槽替换按 `Equipment → DiscardPile` 后进入新装备，并发布 `EquipmentChangedEvent`。
- `GetCombatDistance` 计算存活座位环距离并应用赤兔/绝影及 `standard:mashu` 的出攻距离 modifier；`GetLegalActions` 应用 `standard:qicai` 的锦囊距离豁免；`GetAttackRange` 应用诸葛连弩范围 modifier；玉玺接入回合摸牌数量，死亡清理装备区并保持牌数守恒。
- K6 基础场景已由 `equipment.replace`、`equipment.distance`、青釭剑无视防具、公开装备目标选择和 Standard Console 自测覆盖；K7 八卦阵基础判定场景以及公开判定区牌目标选择已由独立回归覆盖；装备失效/卸载语义和复杂装备效果留待后续 K6/K7。

## 内容请求：2026-10-08 OL 黄承彦批（择才/隐世）能力缺口

- 轮结束触发窗口：引擎只有回合开始/结束窗口与 `RoundStartedEvent`，没有"一轮游戏结束时"钩子；本批择才以"新一轮开始后的拥有者首个回合开始"近似结算并如实记录。若后续提供 `roundEnded`（或轮边界生命周期窗口），择才可直接改挂而无需定制 op。
- 伤害颜色维度：`BeforeDamageApplied` 触发只有伤害来源牌存在性等事实，没有"无色牌/牌色"条件或事实字段；本规则集所有游戏牌带花色，本批把"无色牌或非游戏牌"表达为"无来源游戏牌"。若引入无色牌或伤害颜色维度，需要伤害来源牌色的触发事实与条件。
- `claimJudgmentCard` 的对象限制：既有 op 只允许认领判定对象为技能拥有者本人的判定牌；隐世②需要认领"任意角色"的判定牌，本批以定制 op（7216）绕开。若把对象放宽为触发 subject，可直接改用通用 op。
- 目标角色技能替换/停用的内容级 op：运行时技能授权已有 `grantSkills`（拥有者），但"替换另一角色全部技能（按标签排除）"没有内容 op；本批以定制 op（7212/7213）+ 引擎内注册表实现。若提供带标签排除的目标级 grant/替换 op 与对应恢复语义，可消除该定制层。

## 2026-10-08 补充：可被无懈响应的无实体牌普通锦囊使用

OL 杨仪"狷狭"需要"视为依次使用至多三张牌名各不相同的仅指定唯一目标的普通锦囊牌"。当前引擎的无实体牌虚拟使用（无牌虚拟决斗、无牌虚拟【杀】）按既有保真度结算：真实伤害、濒死、防具与统一 cards-moved 窗口照常开放，但不进入无懈可击响应窗口，也不生成普通用牌的 `CardUseFrame`/`CardActionAcceptedEvent` 用牌历史。狷狭按该既有口径实现（四种单目标普通锦囊：决斗走无牌决斗管线，顺手牵羊/过河拆桥走牌区移动，火攻走展示+同色支付+程序火焰伤害），缺口如实记录：内容后续若需要"可被无懈响应、可进入用牌历史的无牌普通锦囊虚拟使用"，需要共享的无实体牌 `CardUseFrame` 生命周期（虚拟代表牌、无懈窗口、用牌事件与 Checkpoint 契约）。

## 2026-10-08 朱灵批记录的能力缺口

- 2026-10-13 装备置入修正：`cardsGained` 增加显式 `destinationZones: [equipment]`，支持逐牌或逐批的真实装备区进入。战意已改为逐牌的装备置入触发，覆盖技能直接置入与正常使用装备；手牌获得专属操作仍限制手牌目的区。装备置入、各选择点冷恢复、一次弃牌和错误目的区由 `Zhanyi equipment entry` 检查覆盖。下述两项保留为历史缺口记录，修复情况见本节末。
- **普通用牌的回复值修正入口**：战意"基本牌……回复值或伤害值+1"的伤害侧已由共享回合伤害修正表达，但回复侧没有共享入口——现有 `RecoveryBonus` 只挂在濒死救援（`SkillProgramCardPolicyKind.RescueRecoveryBonus`，按目标/势力策略、仅在 allowDying 分支求值），普通（非濒死、含自救）用桃的回复量在 `ResolvePeach`/`ProgramSimpleCardContinuation.RecoveryAmount` 处直接取 1。需要一条"按使用者的种类/条件修正回复值"的共享策略（描述符 + 查询点），本批未表达该子项。
- **规则修正的跨回合生命周期**：`cardUseDistanceLimit` 等规则修正存储按（回合号、回合座位）索引，只覆盖当回合；战意"无距离限制"的文本生命周期是"直到你的下个回合开始"。本批按回合级表达（伤害侧已用 `SkillProgramDamageModifierExpiration.NextOwnerTurnStart` 覆盖精确生命周期），跨回合规则修正（带到期时点的通用 rule-modifier 作用域）留作后续共享能力。
- 本批已落地的对应共享能力：`cardUseDistanceLimit=unlimited` 规则修正支持按牌种过滤（存储过滤既有、校验与描述符放行为本批新增），战意以此把"无距离限制"限定在三种杀上。
- 2026-10-13 只读复核：回复加成须统一覆盖普通桃、转换桃、零实体虚拟桃与额外目标桃，并在回复替代子窗前冻结实际回复量；不能仅修改 `ResolvePeach`。规则修正的精确到期点应在 `BeginTurn` 的实际回合开始，额外回合和翻面跳过的回合也到期，仅插入额外阶段则保持。现有 `zhanyi-expire` 准备阶段触发晚于翻面跳过分支，布尔增益可能残留至再下一回合，这是尚待命令级复现的静态疑点。后续新增到期参数应默认保持旧行为，并由新内容显式启用，避免无条件改变相同内容指纹下的回放。
- 2026-10-13 修复验证：新增按真实用牌者冻结的回复修正；普通、转换、零实体桃及额外目标分别保留拥有帧内的回复收据，回复替代和 HP 子窗返回不重复叠加。濒死酒按实际饮酒者归属，供应实体牌的其他角色不传递其加成。规则修正、锦囊手限豁免与布尔增益支持精确的下个本人实际回合开始到期，额外回合与翻面跳过均覆盖。旧配置默认字段省略、旧到期行为保留，由战意新 revision 显式启用。`Zhanyi benefits:` 四项聚焦检查通过；证据为 `docs/benchmarks/2026-10-13-zhanyi-benefits-focused-summary.json`。

## 2026-10-13 韩遂与夏侯霸的共享能力

- 韩遂的逆乱使用冻结的回合角色体力、实际用杀次数与真实黑色实体牌转换；装备作为材料离开时排空装备子结算，支付武器不得借用该武器的攻击范围。骁袭复用轮账本的首个普通回合条件，拒绝或额外回合不会重复打开本轮机会；新增轮首虚拟杀能力与阶段跳过能力分离。
- 夏侯霸按所选官网经典豹变版本复用挑衅、咆哮、神速。`hpSkillGrants` 根据当前 HP 和精确来源可逆授予叶技能，保留同源实例的次数记录，独立同名来源不被误删。正式发动的派生程序带帧内来源收据；白银狮子成本回血导致神速资格消失后，本次已发动效果继续，新发动资格立即撤回。该组合按技能完整成本与效果解释，未声称存在专门的官方组合 FAQ。
- 同批纠正经典挑衅的限次配置为每出牌阶段一次、神速装备成本允许手牌或装备区；仅修改各自内容 revision。额外阶段的公共返回修复另按回放规则提升 `GameCheckpoint.CurrentRulesVersion`：回合开始插入阶段结束后恢复正常流程阶段状态，使真实摸牌结束窗口可以继续。版本数值只保存在其源文件，不在本文复制；规则 JSON 格式和包版本不因此升级。
## 内容程序表达力缺口：陈登批记录（2026-10-08）

- 轮级、跨座位、随事件账本变动的公共数值规则调整（如丰积的"本轮 摸牌阶段摸牌数 -1 / 出牌阶段使用【杀】的次数上限 -1，对应其他角色 +2，选择否 +1"）目前没有内容侧声明通道：rules.json 无法表达"作用于任意座位、按轮号生效、由技能事件推导数值"的规则查询贡献。本批以 bespoke op（7244）+ 引擎侧门控贡献（EvaluateDrawCount / EvaluateSlashUseLimit 内按依赖开关读取事件账本）实现；若后续出现第二个同类技能，建议抽象出"事件账本驱动的轮级规则修改器"共享描述符。
- "此技能失效直到<条件>"（旋回：失效直到一名角色死亡）没有通用失效/恢复原语。本批以"失效事件与 PlayerDiedEvent 的历史顺序比较"推导（无序列化运行时状态）；通用化需要内容可声明的失效条件与自动恢复账本。
- "每轮开始时"触发窗口为本批新增共享能力（roundStarting，复用 ProgramLifecycleTriggerWindowFrame 与续接枚举尾部值），非缺口；列出仅为说明该窗口自此可用。

## 2026-10-08 孙邵批共享能力与同协整合边界

- `drawTwoThenDiscardTwoIfOverMaxHp`：严格拥有者摸牌结束入口，先选择一名其他存活角色、真实摸两张并排空子结算，在同一时点冻结双方手牌/体力上限资格，再依次支付各自至多两张合法 HE 牌。付款与资格保存在拥有帧收据中，复用原生回复、濒死和移动返回；不是重复执行的普通 draw/discard 宏。
- `addOneDistanceFreeCurrentUseTarget`：严格实际使用人指定目标入口，保留同一 Use 的费用、ActionId 与完整目标前缀；追加一个目标，借刀追加一个完整配对。弃牌堆按有效同名牌判断，只忽略距离，保留其他合法性、隐私、牌移动和完成证明。
- 同协整合修复的 `TongxieSettledSlashUseEvent` 在真实完成捕获点绑定本次动作与移动，区分链内使用、回收后的新使用和响应；不按实体 ID 设永久黑名单。赵俨现有弃牌完成入口仍要求单实体，零实体虚拟杀和多材料杀的完整使用完成合同尚未在本批补齐；不得把该修复报告为这些分支的验收。
- 来源解释、相关检查与常规测量记录在 [第十一轮记录](../benchmarks/2026-10-08-content-continuation-round11.md)。

## 内容程序表达力记录：刘宏批（2026-10-08）

- **全局时长伤害加成（已落地为共享能力）**：图兴②"你本局造成的伤害+1"需要"本局剩余时间对来源座位一切伤害 +N"的表达。此前引擎只有回合级 `grantTurnCardDamageModifier`（`CurrentTurnEnd`/`NextOwnerTurnStart` 两种过期）与声明式 `damageModifiers`（OwnerUsed 路径排除传导伤害且要求具体牌种；空牌种条件都是受击侧 `damageParticipant` 语义），没有全局时长条目。本批新增共享武装通道：通用证据事件 `ProgramGameDamageBonusArmedEvent`（内容技能经 bespoke 流程发布，committed-history 推导、幂等防重）+ `FinalizeAttackDamageAmount` 一处通用接入 `GetArmedGameDamageBonuses`（含传导、不限牌种、`ProgramCardDamageModifiedEvent` 照常发布）。后续"至游戏结束/至某条件为止"的全局数值修正可复用该事件通道或抽象为声明式描述符。
- **"装备栏被废除"触发窗口（未落地，如实记录）**：图兴①"当你废除一个装备栏时"没有对应触发窗口，当前由鬻爵的废除流程内联结算（当前内容中刘宏装备栏废除仅来自鬻爵，覆盖全部可观察行为），并为武装子句留了回合开始幂等补账。若后续出现第二个"当你废除装备栏时"或"其他角色的装备栏被废除时"技能，需要共享的 equipment-slot-abolished 触发窗口（窗口枚举尾部新值 + `EquipmentSlotCapacityChangedEvent`/`PlayerAreasAbolishedEvent` 的窗口发布点），届时图兴①即可改为纯内容触发。
- 本批已落地的其余口径全部为既有共享能力复用（装备栏容量废除、`ChangeProgramMaximumHp`、共享 Recover、`AcquireRuntimeSkills` 自定义源前缀授予与择时清除、耀冥"对其他角色造成伤害后"条件口径、usageScope/usageLimit 限次）。

## 2026-10-08 协同合入修复边界

- 抗歌全量进手牌收集器限制为专用、必选的拥有者 `cardsGained` / `perThirdPartyHandGain` / `hand` 目标单操作；未实现的 `ignoreOwnSkillMovements` 不再接受。濒死回复节点精确限定现行回复至 1。抗歌死亡代价为真正弃牌生产来源，节烈致命代价通过拥有帧已付收据与精确 before-damage / own-dying 关系返回，不泛允许任意程序子窗。
- 鬻爵改为真正出牌阶段零输入主动入口，未付款取消保留次数，装备栏付款与赠牌移动的后续子结算在同一拥有收据内排空；AI 只选择实际可用栏。图兴上限变化及全局加伤标注实际图兴来源，真实无来源伤害不计为拥有者造成的伤害。鬻爵以外的废栏即时图兴合同仍保留为上述共享触发窗口缺口。

## 2026-10-08 第十三轮烈节来源付款修复

- 原分支将“弃置伤害来源至多 X 张牌”收缩为随机弃来源手牌。本轮复用私有 HE 付款选项：拥有者可选不超过冻结红色费用数的来源手牌槽位和公开装备，也可完成零张；手牌身份不进入发布选项，外来防具／宝物保护在发布及提交时重验。
- 首笔混合 HE 费用和来源侧选择各沿真实原子移动结算；有效红色在首笔移动前冻结，将灵武器按既有规则离场到场外仍记作实际弃牌。帧收据与精确父窗支持装备移除、摸牌和付款引起的原生子结算，已付尾部不重复支付。验收范围以[第十三轮记录](../benchmarks/2026-10-08-content-continuation-round13.md)为准。

## 2026-10-08 第十七轮双人真实手牌重铸

- `pairedHandRecast` 是可选启用的通用主动单操作：真实出牌阶段每阶段一次，拥有者一张真实 Hand 牌及一名有手牌的其他存活目标；另一方私下选择其真实 Hand 实体。双方材料先冻结，再按拥有者 cost／draw、另一方 cost／draw 的顺序排空精确原生移动子窗。该顺序与 Hand 区限制是此通用操作的明确合同，尚不作为崇信完整官方 FAQ 验收。
- `ProgramPairedHandRecastReceipt` 留在原 ProgramSkillFrame，各参与者分别记成本／摸牌发票、序列区间和原批次。已付后失源不能撤销摸牌；后续原材料失效或参与者死亡只跳过原付款，不替换其他牌。重铸走真实 RecastDiscard／RecastDraw，不能冒充弃置；公开开始／跳过事实没有私有牌 ID，新摸牌实体不公开。
- 四项共享 focused 检查已经通过；相关回归与常规验收以[第十七轮记录](../benchmarks/2026-10-08-content-continuation-round17.md)为准。羊祜仍是完整候选，本轮没有注册缺技能的武将；怀远初始实体与继承、德彰门槛、卫戍真实摸／弃来源仍待补齐。[原生来源审计](../benchmarks/2026-10-08-native-draw-discard-provenance-audit.md)仅为静态证据，其三项旧弃置分类缺口尚未修复。

### R18：初始物理手牌、永久收益与真实阶段外摸弃牌

- 新增 `initializeOriginalHandEntities` / `offerOriginalHandLossBenefit` / `inheritOriginalHandBonuses` / `awakenWhenOriginalHandEmpty`。初始牌只来自原生 InitialDeal，物理实体第一次离开本人 Hand 后耗尽，重复返回不重建；未耗尽牌号仅在本人安全快照可见。收益、继承和觉醒各用所属类型帧与精确原生父窗，不增加 pending sidecar。
- 新增 `drawAfterActualOutsideDraw` / `discardAfterActualOutsideDiscard`。前者以正数真实 DrawCards 调用证明，一次调用只在末次实际移动批次上触发，排除 InitialDeal、普通获得与本技能直接生产的摸牌；后者只认本人 HEJ 实际弃置进入 DiscardPile。阶段判断使用实际阶段 token，包含插入阶段与返回；玩家选择为强制目标／暗置手牌牌位。
- 初始牌快照集合和嵌套牌号防御冻结；公开摸牌证明仅含标量，不携带私有摸牌牌号。手牌角标使用内容展示名，和现有粮角标独立。
- 修复三陈、奸回和凶竖的既有真实弃置支付漏分类。这些修复改变相同内容指纹下既有观察技能的行为，故提升 Replay 规则版本；新增角色本身不提升技能 JSON 格式或内容包版本。实际验证范围和结果以 R18 benchmark 为准。

## 2026-10-13 何太后与经典诸葛诞

- `healthChangeKindIs` 读取真实 `HpChangeContext.Kind`，用 `hpChangeKind: maximumHp` 表达上限变化，不能从 HP 前后相同推断。标准健康子窗、响应完成成本及供牌完成成本三个事实构造入口均按内容依赖填充可选类型字段。经典威重每次非零上限增减摸一张，幅度不改变次数；损伤、失血、回复和零上限变化分别排除。`livingPlayerCount` 在准备边界冻结存活人数，经典举义复用真实摸至上限、游戏限次和派生技能授予。五项聚焦证据为 `docs/benchmarks/2026-10-13-population-health-kinds-focused-summary.json`。
- `usePhaseOwnerVirtualAlcohol` 是显式新内容链：真实出牌阶段开始、拥有者一张手牌弃置、排空付款子结算，再由实际阶段角色使用零实体酒。酒的使用者与供应技能者分别记录；真实使用确认即消耗饮酒者的次数。帧内已付收据关联费用实体、真实移动序列、原阶段父窗与精确技能实例，允许已付效果在失去源技能后完成，死亡及游戏结束仍停止。旧虚拟酒路径不因此扩展。
- `actualTurnDeathBenefits` 仅接受实际回合结束后、冻结死亡数大于零的可选拥有者摸牌。数量为本实际回合总死亡数加本人击杀数的两倍；死去的回合玩家不会阻断存活拥有者的该项奖励，回合结束后其他子结算中的死亡不回计。原始回合开始与结束事实界定计数范围，AI 只读取公开冻结计数。
- 酒目前不开放 `cardUseBeforeTargetEffects` 取消窗口，本组未新增该窗口或相关返回类型，因此不把“在该窗口取消酒”作为运行验收。已付款后新增禁酒时，酒不能使用，对其他角色的独立尾伤害仍继续；这是本版本顺序文本的解释，未声称查得专门官方组合 FAQ。
- 版本选择和来源记录：`ol-he-tai-hou-2026-10-13.json` 为当前官网版；`classic-zhuge-dan-2026-10-13.json` 为官方 2016 完整经典版，界面明确标注经典，不拼接当前举义与未知派生技能。新内容及可选事实由内容指纹区分，不按人物提高规则 epoch、规则 JSON schema 或包版本。
- 文聘需要第三方唯一目标替换、在用实体材料隔离和实际回合后归还；孙鲁育仍缺当前魅步所赠止息的同版本明文。来源与已有能力的真实缺口保存在 `ol-wen-pin-sun-lu-yu-capability-assessment-2026-10-13.json`，本组没有注册这两名未完整表达的武将。
- 收尾修复了新数值表达式插入枚举首部导致旧隐式编号偏移的问题；新增项移至尾部，合同检查保留旧编号断言，并对照原有 44 个公开枚举确认既有值不变。最终常规 Core 519 项通过；WPF 73 项通过、1 项因 `Keyboard.Modifiers=Control` 前置条件失败，该原有手牌响应检查单独复查通过。常规约 186 秒，未达到约一分钟目标；本批完整证据见 `docs/benchmarks/2026-10-13-he-tai-hou-zhuge-dan-verification.json`。

## 2026-10-13 文聘与董允

- 本组补齐前述文聘能力缺口，采用官网当前完整镇卫；董允采用当前完整秉正、舍宴。人物、版本、官方原文及立绘证据分别保存在 `ol-wen-pin-2026-10-13.json` 和 `ol-dong-yun-2026-10-13.json`。董允目录没有锁定官方精确将包组，归其他扩展，不按年份猜测。
- `protectOtherSoleCardTarget` 使用实际用牌者、唯一存活主目标与 HP 资格，真实支付一张本人 HE 牌并排空成本子结算。替换保留 Use/action/provider/materials，更新实际攻击、预备攻击和新目标候选，跳过被移走的旧目标；独立来源可连续保护，同一次 Use 已付精确来源不重开。
- 黑色延时牌 placement 增加仅新能力启用的 typed finalized 返回桥。先完成原生费用和实际使用确认的观察技能，再开放保护，返回真实原生放置；实体牌和已有转换生产者均接入。隔离材料由所属 Use 收据证明，完成后在实际 `TurnEnded` 的拥有游标归还真实使用者并排空获得子窗；技能丢失不撤销付款，使用者死亡或游戏结束时弃置材料。
- 新目标流离保存已递增的 finalized 候选游标和实际目标槽，暂时复用原生重定向窗口，支付真实费用后恢复游标；不重跑原先的候选或镇卫费用。界疠火目标重建消费精确新替换/重定向事实。该时机的明确公告来自官方 2015 手游规则，适用于当前 OL 仍注明推断，没有宣称找到当前 OL 同组合 FAQ。
- `resolveUnequalHandHpEnding` 复用实际摸弃、私有目标自选手牌及付款子窗，返回后重新判断手牌与 HP 是否相等，再一次奖励并可赠本人 HE。已支付后失源或濒死沿原拥有帧继续，不能从新的 live source 重造费用。
- `adjustTargetedOrdinaryTrick` 保留原目标，只追加合法目标或使某个效果无效；选择时至少保留一个有效主目标。借刀追加完整 pair，锦囊无效只按主目标，副目标由后续真实杀规则裁定；五谷仍按原目标数亮牌。独立目录也显式捕获实际牌面，AI 只按安全快照的公开数量、HP 和牌效估值发动。
- 本任务的 11 项新行为检查与 6 项相关检查共 17 项通过；常规 Core 535、WPF 74 项全部通过，约 187 秒，未达到约一分钟目标。53 个旧公开枚举的 968 个旧值保持；52 个相关文件在常规前后哈希一致。并行 amount-cache 会话另修复旧虚拟派发和 Benxi/流离回放并提升规则 epoch，本任务保留并复验，未将其修复归为独立工作。完整证据为 `docs/benchmarks/2026-10-13-wen-pin-dong-yun-verification.json`，没有运行 manual Full 或发布。

## 2026-10-13 审配、孙鲁育与程昱

- 采用当前完整 OL 审配、孙鲁育与程昱。此前孙鲁育派生止息的来源缺口已由当前官方公开客户端补齐：角色 135 的魅步 246 明确关联止息 258，与官网完整主技能文本绑定；程昱角色 130 对应设伏 234、贲育 235。版本与组合解释分别记录于 `ol-shen-pei-2026-10-13.json`、`ol-sun-lu-yu-2026-10-13.json`、`ol-cheng-yu-2026-10-13.json`，不拼接历史版或十周年版限制。
- `refillAndScheduleNextTurnSelfOnly` 在真实回合结束后摸至上限、至多五张，并按已发布事实在目标的下一个真实回合开始消费；额外回合与跳过阶段都按真实回合界定。手牌并列最多也限制本回合只能对自己用牌，已签发效果不随原技能失去而消失。`otherParticipantDamageBecomesHpLoss` 在伤害专属增减、防具与连环传导前转为真实失血，保留实际来源和技能拥有者的区别；自伤与无来源伤害不触发。
- `grantOtherPlayUseRestriction` 支付本人真实 HE，排空付款子结算后向实际出牌阶段角色授予纯限制叶技能；来源实例与实际回合精确绑定。`stopUsingAfterTrickOrHpCount` 使用同一真实阶段的去重 Action 记录和当前 HP，锦囊、延时锦囊及实际使用的无懈均计入，纯打出响应不计入。普通及触发技能生成的零实体牌也走真实用牌计数。`chooseStartingEquipmentControl` 区分弃其他角色装备与获得任意角色防具，外来装备弃置保护按真实操作者判断；获得防具后签发实际回合的杀使用及响应禁令。
- `recordNamedPrivateAmbush` 暗置本人真实手牌并记录非装备牌名；`nullifyMatchingHandUse` 支付精确存活的记录实体，给实际已确认用牌添加无效状态，保留真实材料支付、观察窗口和使用完成。延时牌不放入判定区，酒不产生饮酒效果，闪与无懈沿原生响应所属帧返回。无懈仅有实际可参与的匹配设伏时延后翻转原链状态。消耗以 DepositId 和来源实例界定，同一物理牌回收后再次暗置不会复活旧记录。暗置牌面与所记牌名仅在拥有者快照和只读界面配对展示，他人仅见数量。
- `resolveDamageSourceHandChoice` 以一次真实有来源伤害冻结来源手牌数 X，选择摸至 min(X,5)，或实际弃至少 X+1 张手牌后对来源造成一次伤害；已付费用和子结算留在所属帧。当前版不增加旧手牌比较前提，摸零张仍可选择并完成发动。混合区域、木牛流马、其他供应者材料、暗置者付款后死亡等组合的解释与未实测边界须以来源记录为准，不能当作同版官方组合 FAQ。
- 另修复镇卫替换后流离真实 A→B→A 重新成为目标时跳过新询问的问题，拥有帧记录新目标事件的候选起点，按实际目标槽更新攻击；一次已付镇卫来源不重开。此项会改变相同内容指纹的回放，因此按项目规则调整 Replay 规则版本；普通武将新增不提高规则 JSON 格式或将包版本。红绿行为证据为 `2026-10-13-sole-target-episode-verification.json`。
- 收尾实际复现并修复伏兵多实体失技清理的重入重复移动、设伏与贲育已付费用后非主公拥有者死亡的尾部漏结算，以及设伏无效状态遇真实 finalized 使用者更换时误判付款证明失效。原始用牌资格冻结在不可变 Action 收据中，后续角色变化须由真实已接受动作与修改事实重建证明；不忽略操作者或材料身份，不重付费用。
- 聚焦 Core 33、WPF 2 项通过。常规首轮 Core 558 项通过、WPF 74 项通过与 1 项失败；同源单项复查通过后，修复原有响应夹具跨窗口复用已释放模型的问题，保留全部原生按键路径和断言并增加诊断。该夹具缺口不冒充已证实的首轮失败根因。之后保留并行公共能力缓存修改、重建当前源码，最终规定常规 Core 558、WPF 75 项全部通过，约 108 秒，仍超过约一分钟目标。
- 最终 80 个本批相关源码与支持依赖在常规前后哈希一致；对照此前已验证 Core 的 206 个公开枚举，1,961 个旧值均保持。来源与立绘的 16 项本地哈希一致，实际图鉴、拥有者牌面与记录牌名配对、他人仅数量和清空界面已核对。完整范围、组合解释、首轮失败、当前源码刷新及清理保留证据见 `docs/benchmarks/2026-10-13-shen-pei-sun-lu-yu-cheng-yu-verification.json`。没有运行 manual Full、发布或提交，持续增加武将的目标保持 active。
- 收尾期间共享代码继续发生并行修改，保留原工作并重建最新源码，再次通过本组及相关共享合同的 Core 33、WPF 2 项；此次 80 个相关路径在运行期间未变化。558/75 常规通过记录属于其当时的源码快照，后续共享修改由最新聚焦证据覆盖，未再称为最新源码的全常规通过，也没有扩大为 Full。

## 2026-10-13 张鲁、王荣与士燮

- 新增完整当前 OL 张鲁（义舍、布施、米道）、王荣（丰姿、吉占、赋颂）与士燮（避乱、礼下）。官网角色 343／344／345 与当前公开客户端技能绑定分别核对；立绘、图鉴和内容入口一并注册。来源与组合解释分别保存在 `ol-zhang-lu-2026-10-13.json`、`ol-wang-rong-2026-10-13.json`、`ol-shi-xie-2026-10-13.json`。
- 米是实际有来源实例的公开实体牌堆。义舍用真实结束阶段摸牌与 HE 自选存牌；布施逐伤害点由受伤者决定是否获得米；米道替换真实判定牌并保留原生移动、判定和最后一张米的回复窗口。付款及移动子结算留在所属帧，已付后失源或死亡不重付。
- 避乱按付款子结算返回时的存活势力数调整其他角色到拥有者的距离；礼下在其他角色真实结束阶段提供一至两项互异收益，每项分别减少其到拥有者的距离。定向永久数值与已有固定距离优先级共存，失去或重新获得来源不会重建已签发收益。公开距离、回复、摸牌和私有选择均使用实际事件与安全快照。
- 丰姿支付一张同类型真实手牌，在同一实际 Use／Action 中多结算一次效果，保留一次原材料成本、用牌确认和使用完成；第二遍重新进入原生响应与无懈。多目标按完整第一轮再完整第二轮执行，该次序是当前实现解释，尚未取得同版官方组合 FAQ。酒效通过可选公开数值显示，旧布尔酒效仍等价于加一伤害。
- 吉占以真实摸牌阶段开始窗口替换当前普通摸牌计划，保留该窗口后续独立调整。公开展示牌堆顶实体后严格比较点数，相等使两种猜测都失败，最终获得牌包括失败比较牌；猜对后可停猜由同一普通 OL 的官方 2021 发行文字支持，不冒称当前组合 FAQ。AI 使用公开先验估值。
- 赋颂在真实死亡窗口由死者决定是否选择体力上限更大的存活受赠者；选定后由受赠者本人强制二选一获得丰姿或吉占，没有额外的受赠者拒绝选项。永久授予及返回过程使用实际所属帧与冻结事实。
- 补齐新丰姿目录下普通锦囊实际确认事件的入口，并将新赋颂操作纳入原执行器的精确死亡目标许可。真实奸雄在第一遍伤害后合法领取原材料时，额外结算用所属 Use 中不可变领取收据证明原 Action、伤害窗候选、生产者指令、领取事实与 Processing→手牌移动；第二遍响应和无懈不再要求已合法领取的牌仍留在 Processing。保留原领取资格与牌的实际归属，不宽放任意区域。原生杀、群体锦囊与第二无懈冷恢复通过，无新能力的原流程对照也通过；没有改动历史 Damage 弹栈路径或按普通武将提升版本。
- 最终聚焦 Core 29、WPF 2 项通过；规定常规 Core 586、WPF 76 项全部通过，总计 88.356 秒（增量构建 1.205 秒，Core 81.770 秒、WPF 86.808 秒并行），仍高于约一分钟目标。最终 2,365 个仓库源码、规则、XAML、构建及验证配置和本组立绘输入在验证前后哈希一致；211 个公开枚举的 1,988 个旧值保持。已注册武将由 378 增至 381，原有 ID 未删。来源本地 14 文件已哈希，其中 11 文件与记录的预期哈希比较一致；未保留的上游完整原始包仅作为来源记录。
- 真实图鉴和酒效控件已核对，酒效图片使用公开 DTO，原生双酒与下一次杀另有真实引擎证据；没有冒称人工前台完整对局、全部锦囊组合或全部多材料领取实测。完整范围、规则解释、初期失败、领取组合红绿与保留输出见 `docs/benchmarks/2026-10-13-zhang-lu-wang-rong-shi-xie-verification.json`。未运行 manual Full、发布或提交，持续增加武将的目标保持 active。
- 常规之后共享锦囊目标继续处理及 WPF 技能控件缓存又有修改。保留修改、重建最新源码，本组和相关顺序锦囊／技能界面的 Core 40、WPF 5 项全部通过；此次 2,365 个输入在运行期间未变化，旧枚举值再次与新编译结果核对保持。586/76 常规通过记录属于此前的稳定源码快照；当前共享修改由最新聚焦证据覆盖，没有再称为当前源码的全常规通过，也没有扩大为 Full。

## 2026-10-14 普通 OL 马云騄、蹋顿、严白虎完整接入

- 当前官网、API 与客户端绑定确认普通 OL 346／347／348 及技能版本，注册马术／凤魄、乱战、雉盗／寄篱和独立官方立绘。新能力显式启用，不因普通武将提升全局规则、JSON 格式或包版本。
- 凤魄以实际回合首次杀／决斗的共同声明序号判定，并在最终唯一目标窗口冻结有效方片手牌数；公开选择不泄露私有数值。真实 Action 与旧式 Action-null 虚拟杀各保留原生返回，付款后失源／死亡只完成一次。杀变火杀只匹配既有同次转换证明，原序号仍为杀；实际连环传播沿已确定伤害，不再次加 X。
- 乱战每次造成或受到真实伤害获得一个标记，自伤只记一次、两点伤害不按点数记。追加目标额度冻结，最终减半按当时实际标记向上取整；原生借刀完整目标对、铁索连环、零实体杀和合法使用者替换均有真实命令及冷恢复证据。
- 雉盗实际获得受伤目标 H／E／J 各一张，手牌仅发布私有槽位；空目标也消耗当前回合次数并施加本回合只能指定自己的限制。原伤害帧收据不再重读嵌套子伤害的活动攻击，真实装备回复、濒死、失源和非主公死亡继续结算通过。寄篱计算与已有目标的距离，保留实际使用者、目标前缀和借刀辅助目标；杀、决斗、救援桃及普通酒的原生游标只接受／付款／完成一次。
- 最终聚焦 Core 30、WPF 1 项通过，包括 22 项新行为及相关共享回归。规定常规 Core 608、WPF 76 项通过，用时 85.254 秒，增量构建 1.295 秒；仍高于约一分钟目标，没有为降低时长删去行为断言。验证期间 2,387 个仓库输入哈希一致，216 个公开枚举的 2,015 个旧值保持，武将由 381 增至 384。已有武将完整定义未改。
- 实际图鉴图片已核对并保留。多拥有者寄篱追加目标后的候选重评目前仅有静态审计线索，全部多来源组合、人工前台完整对局和 manual Full 尚未验收。完整范围、初期失败修复与政策阻止的输出清理见 [本组验证记录](../benchmarks/2026-10-13-ma-yun-lu-ta-dun-yan-bai-hu-verification.json)。持续目标保持 active，直接开始关索／王朗／鲍三娘下一组。

## 2026-10-14 普通 OL 关索、王朗、鲍三娘完整接入

- 当前普通 OL 349／350／351、官网完整技能文字及精确客户端绑定已经核对。关索征南按其他角色真实死亡摸三张后选缺失技能，使用当前界武圣／当先／制蛮；撷芳随当前存活女性数调整有向距离。支付后原死亡／濒死子树采用所属帧的冻结凭据，嵌套失去体力、失源或死亡不重复摸牌与授予。
- 王朗鼓舌在私下选定后共同亮牌、按顺序比较；当前激词 3307 对实际拼点牌点数不大于 X 的可选加点与精确鼓舌次数恢复同时生效。七饶舌直接进入原生死亡，保留正体力、无伤害／濒死／虚构凶手的边界。实际拼点领取、白银狮子付款回复、天辩与随机策略均接入原生收据；天辩顶牌私下实际移入 Processing，耗尽后真实重洗，四座安全视图和公开移动回调在共同亮牌前不泄露顶牌。
- 鲍三娘按当前 3796／3797／3798 接入。非基本牌转换杀的独立不可响应与距离凭据和旧来源策略共存，真实火杀／使用者替换保留原转换者的收益。镇南以真实两张牌付款发起指定目标的原生南蛮，包括空目标；无懈、实际伤害来源、随机弃置、当回合目标次数额度继续使用原机制。获得非基本牌后的体力支付恢复所有当前对应限定技实例；原有与实际外来实例均有消耗、恢复和下一准备阶段再次发动证据。许身由受赠者本人选择替换技能或授予镇南，再回复至一点；当前客户端摘录缺少末句，使用当前官网完整文字，未混入旧版变关索规则。
- 最终聚焦 Core 32、WPF 1 项通过，包括新行为 23 项和相关共享回归。规定常规 Core 631、WPF 76 项全部通过，用时 89.716 秒，增量构建 1.439 秒；仍高于约一分钟目标。验证期间 2,413 个仓库输入哈希一致，217 个公开枚举的 2,024 个旧值保持，武将从 384 增至 387，原有完整武将定义不变。版本仍由原来源维护，未按普通武将提升规则／schema／包版本。
- 三张实际图鉴渲染已核对并保留。本地来源及立绘 17 文件已哈希，其中 16 个与预期记录比较一致，无不符；完整上游原始包仍仅有来源哈希和摘录，不冒称本地完整闭包。多随机策略争夺共同发起牌采用每目标首个已支付凭据一次的原生不变量解释，未取得同版官方组合 FAQ；没有人工前台完整对局或 manual Full 验收。初期失败、实际修复和保留清理输出见 [本组验证记录](../benchmarks/2026-10-14-guan-suo-wang-lang-bao-san-niang-verification.json)。继续下一组前已保留 387 武将和所有当前公开枚举基线。

## 2026-10-14 普通 OL 邓芝、曹婴、董昭完整接入

- 邓芝修好按双方真实伤害窗口共用每实际回合次数，防止伤害后令原伤害来源实际摸两张牌。素俭在原生弃牌阶段替换窗口依据当下手牌实体的真实取得流水，分配全部旧牌，或实际弃旧牌后对一名其他角色执行不超过真实弃牌数的私有槽位弃牌。费用、失源、死亡及子伤害返回均保存精确所属帧收据。
- 曹婴官网静态页面仍是旧版文字，当前普通 OL 客户端绑定已为 3964／3965。按当前绑定实施每回合猜牌、当回合下一次对目标的真实伤害加一、精确本次牌次数退款与摸牌、直到下次本人回合开始的临时双技；防止原牌伤害不会消耗下一次真实伤害额度。伏间按准备和结束阶段随机观看非全场唯一最多角色的完整手牌，所有牌面仍只给拥有者。关联 717 奸雄的当前文字是摸牌并获得伤害牌，与仓库保留的旧版界奸雄二选一不同；独立新增 `ol:jianxiong`，没有改动旧定义，真实摸牌子流程后的原决斗材料领取通过。
- 董昭先略复用已有 own／otherLiving 回合开始与 Observer 使用完成入口，只在真实主公回合记录，并按实际其他角色使用完成支付一次摸牌、分配真实新牌及重记。造王的永久身份授予保存在目标私有规则状态，公开事实不泄露原隐藏身份；真实死亡时进行忠臣继位或原凶手为主公／忠臣的反贼特殊胜利，仍使用原生身份判胜。非主公拥有者、公开队伍对照、原生回复付款后的失源及死亡均有真实命令和冷恢复证据。
- 最终聚焦 Core 47、WPF 1 项通过，其中 22 项为新增行为，旧三将和追还共用入口回归同时通过。规定常规 Core 653、WPF 76 项全部通过，用时 104.474 秒，增量构建 1.578 秒；高于约一分钟目标，没有删去机制断言。验证期间 2,435 个仓库输入一致，220 个公开枚举的 2,064 个旧值保持，武将由 387 增至 390；原有全部武将定义及 292 个既有立绘目录条目保持。
- 三张实际图鉴和立绘已核对；11 个本地来源／立绘文件与记录预期哈希一致，完整上游包没有保留，仅有来源哈希及有界摘录。多造王继位按座次首个合格者处理，尚无同版官方组合 FAQ；全部使用者替换、多目标和连环组合、人工前台完整对局以及 manual Full 未验收。准确初期失败、修复及保留清理输出见 [本组验证记录](../benchmarks/2026-10-14-deng-zhi-cao-ying-dong-zhao-verification.json)。完成后已开始吾彦／SP 张郃／宣公主的当前来源和机制预查，尚未声称下一组已可玩或通过行为检查。

## 2026-10-14 吾彦、SP张郃、宣公主

- 吾彦采用当前澜疆：结束阶段冻结原始手牌数合格角色，包括本人；每个真实角色自行选择是否令拥有者摸牌。伤害与尾部摸牌资格按当时手牌重新计算，但限定原名单。付款、伤害及摸牌子流程由所属类型帧续接，失源、死亡与冷恢复不重复支付。
- SP张郃采用群势力当前周旋，与魏势力巧变版本分开。真实手牌暗置为至多五张旋；使用牌时移去一张、摸一张，排空子流程后按当时手牌比较追加剩余旋数量。下一次实际出牌阶段结束清除，跳过阶段则保留。本人可见牌面，其他角色仅见名称和数量。
- 宣公主采用当前高陵、齐眉、追姬。身份隐匿是未定义体力状态，真实回合开始或扣减入口用所属登场帧续接；其他实际回合登场才能发动高陵。齐眉记录真实双方手牌或体力变化、分项共享回合次数；真实上限降低的体力截断也纳入。追姬在实际阶段开始开出债务，冻结阶段实例，插入阶段与返回仍各结算自己的欠账；失源保留已经开出的义务。
- 追加检查修复了原生桃园、白银狮子及跨角色回复读取隐匿内部体力的问题。真实桃园对可见受伤角色仍正常回复，对隐匿角色与狮子移出不产生非法回复；冷恢复保持。追姬使用独立 ol:zhuiji-tj，保留旧马超追击定义。
- 最终聚焦 Core 68、WPF 5 项通过；规定例行 Core 679、WPF 80 项通过，用时 92.042 秒，仍高于约一分钟目标。2,464 个仓库验证输入保持一致，原 390 名完整定义、2079 个旧枚举值及 295 个既有立绘登记保持；当前名单 393，立绘登记 298。没有执行 manual Full。
- 三张图鉴、实际隐匿前后、私有旋显示和原名单尾部选择已进行 WPF 渲染核对。隐匿初始体力设定及防止首次扣减、首次不发布伤害事实，以及澜疆跳过可选伤害后的摸牌尾部，是明确的实现解释，尚缺对应官方 FAQ／官方前台对局确认；全部组合未穷举。具体通过范围、解释、源码哈希和保留输出见 [验证记录](../benchmarks/2026-10-14-wu-yan-sp-zhang-he-xuan-gong-zhu-verification.json)。
- 两篇隐匿公告保存与历史预查文档更新曾被自动审批以 blocked by policy 拒绝，未换工具重试；历史原件保留，当前来源记录明确取代旧推断。已开始下一组 SP黄月英、步骘、董白的当前官方来源核对，跳过不可用 359 和已登记的重复版本。

## 2026-10-14 普通 OL SP黄月英、步骘、董白完整接入

- 当前官网/API及精确客户端361/363/366和六项技能已核对；玲珑使用当前坐骑空缺手牌上限和完整871奇才，不混入旧马术或只距离版。弘德按一次真实原子移动或严格原生摸牌/整区弃牌调用证明触发，不合并历史批次；定叛按实际存活反贼数计算原阶段次数，由真实受益者决定后续。
- 连诛黑牌由受赠者选择，非黑牌没有其他版本奖励。黠慧移动前冻结实际拥有区域的黑牌和精确来源，原生获得黑色装备也产生实体限制；自己的付款与他人强制移牌区分，真实体力减少/上限钳制或实体离手正确解禁。机巧装备成本、亮牌、领取、清理及白银狮子/濒死子流程保持帧内付款记录，失源或死亡后的完成必须成功返回一次。虚拟八卦AI与青釭剑原生忽略防具已有实际证据。
- 最终聚焦Core65、WPF5通过，规定例行Core701、WPF80通过，用时90.172秒，仍高于约一分钟目标。首次例行暴露的不相关武将查询2560字节分配已修，保留零分配断言；没有扩大为Full。2488个验证输入前后不变，原393名完整武将定义、2106个旧枚举值和298条原立绘登记不变，名单396、立绘301。三个真实图鉴渲染已核对。
- 定叛伤害来源、黠慧实体离手及失源后的权利解释明确记录；全部多来源、私有牌堆和前台完整对局未穷举。版本源不因普通武将提升。验证与保留输出见[本组记录](../benchmarks/2026-10-14-sp-huang-yueying-bu-zhi-dong-bai-verification.json)。接受源码恢复档逐项哈希/CRC已验证，属于必要恢复证据，未运行发布、打包、提交或推送。持续目标保持active，进入下一组来源预查。

### OL batch 11: Zhao Xiang, He Qi and Kan Ze

Implemented native conversion cost/provider reward, cumulative marker replacement, equipment-suit conditional grants with stable instances, joint hand reveal/discard/gain, whole-hand private reveal/claim, and final-damage prevention with next draw debt. Pending work stays on typed owning frames and exact source/material/action invoices. Additive optional grants retain old gameplay hashes. Focused 82 Core / 5 WPF and measured routine 718 Core / 80 WPF passed. Full and foreground acceptance were not performed. Evidence and interpretation limits: docs/benchmarks/2026-10-14-zhao-xiang-he-qi-kan-ze-verification.json.

## 2026-10-15 王允、诸葛果、戏志才

- 当前普通OL官网与公开客户端372/373/375核对，诸葛果使用当前11001祈禳、11002羽化。王允连计用真实牌堆武器和实际受命者的杀；谋逞按该杀的真实伤害觉醒，矜功一次抽样三种原生主动锦囊并支付真实材料，包含原生闪电及其后续判定。杀的父结算等待原生攻击完成收据，费用与返回证明保存在所属帧。
- 祈禳按真实装备使用取得实体锦囊，实体权利与原Action、拥有区域和真实付款绑定；铁索连环使用原生IronChainUse，借刀追加完整有序目标对。羽化私有牌堆顶选择、延时锦囊开出的下次实际回合观察额度、手牌上限豁免保留原生移动与牌堆顺序。死亡装备放置疑点已撤回：原生流程原已有存活门控，检查强化但没有新增生产补丁。
- 先辅按每次实际伤害和实际回复量结算；目标在付款后死亡不丢掉已冻结资格，互相先辅通过不同原生伤害实例继续到自然终止。筹策逐点真实判定，在天妒领取前冻结有效花色；黑牌费用使用真实HEJ移动。原生天妒已付子树及拥有者死亡后的筹策尾部按精确帧证明完成一次，不重付或增加未付效果。
- 最终聚焦Core100、WPF5通过，其中18项新增行为；规定例行Core736、WPF80通过，用时100.475秒，仍高于约一分钟目标。2537个仓库验证输入前后一致，原399名完整武将定义、2138个旧枚举值、770个旧技能哈希及304条原立绘登记保持；当前名单402，立绘307。三个真实图鉴渲染已核对，戏志才详情使用可滚动面板，单张图不覆盖全文。
- 普通新增未提升规则、格式或包版本；没有manual Full、人工前台完整对局、提交、推送或发布。组合解释、初次失败、修复、哈希及保留清理输出见[本组验收记录](../benchmarks/2026-10-14-wang-yun-zhuge-guo-xi-zhi-cai-verification.json)。接受源码恢复档逐项哈希/CRC已验证，旧11组验收记录与恢复档保持。持续目标active，已并行预查卑弥呼、刘琦及下一位未登记普通OL武将。


## 2026-10-15 卑弥呼、刘琦、鲁芝

- 当前普通 OL 官网、API 与有界客户端 377/378/379 核对；魏鲁芝与原群卢植保持不同身份。纵傀接真实回合开始之前及轮开始，骨疽按真实伤害与实际摸到牌累计，拜假通过原生回复和授予蚕食完成觉醒。蚕食实际支付全局傀标记，保留杀、无中生有隐式自身及借刀完整有序目标对；零追加结束不会伪造付款或目标变更。
- 问计由真实其他角色自主交给 HE 牌，按实际印刷牌名限制本回合其他角色响应，保留原生无懈反制链的自己例外。屯江记录真实进入、跳过及插入出牌阶段和实际主要目标。清忠记录所属实际阶段的已付债务，失源后的对应结束仍偿付，最低手牌由当前状态确定并原子交换；死亡停止未付后续。
- 卫境真实零材料杀或闪共用本轮额度，仅原生“使用”入口提供选择，决斗、南蛮、万箭的“打出”不提供。连计与五类既有请求杀保留各自请求者、实际使用者及独立卫境付款；子窗口造成主公死亡后，已付卫境杀通过原生完成返回，跳过游戏结束后的未来响应和未付尾部。额外最后目标不会覆盖此前对请求者造成伤害的结果。
- 最终聚焦 Core125、WPF8 通过，其中21项新增Core行为方法；常规 Core757、WPF81 通过，用时110.164秒，增量构建1.331秒，仍高于约一分钟目标。旧无轮次配置夹具补剥离新增显式选择配置，原断言保留；隐藏消息窗口按键前置条件一次失败仅补完整实值诊断，原因不能从原聚合日志确定，没有产品改动、断言放宽或人为延时。
- 2569个验证输入前后一致，402名旧完整武将定义、2157个旧枚举值、777个旧技能哈希及307条旧立绘登记保持；名单405、立绘310。三个实际图鉴渲染已核对，卑弥呼详情可滚动，单图不覆盖全文。普通新增未提升规则、格式或包版本，没有manual Full、人工前台完整对局、提交、推送或发布。限制、历次失败和保留输出见[本组验收记录](../benchmarks/2026-10-15-bei-mi-hu-liu-qi-lu-zhi-wei-verification.json)。源码恢复档重新打开逐项哈希/CRC通过，上一组验收原件保持。持续目标active，继续下一组来源预查。


## 2026-10-15 司马徽、徐荣、李傕

- 按当前普通 OL 416/417/418 官网、API 与有界客户端接入；司马徽专用火计、连环、业炎使用对应 SP 条目，映射推论和缺少服务器授予表的边界另记。新隐士 ID 为 ol:yinshi-sima-hui，原黄承彦“隐世”ol:yinshi 完整保留。首个实际回合按顺序授印，来源标记与稳定派生实例、每实际回合三次使用/重铸额度、原生连环解除前的称好资格、真实四花成本与费用致死后的业炎尾伤均已接线。
- 徐荣暴戾实际来源池、随机阶段债务及当前 3447 杀绝按原生事件执行。原生伤害扣减前的可选所属帧凭据保存真实体力与最终伤害，推导原始救援需求；旧体力截零与存档边界保留。因果实体支持双材料杀、领取后实际装备及同实体后续再用、再领取，校验限定到本次付款区间。
- 李傕初始体力4、上限6；狼袭使用真实准备阶段和一次原生随机伤害。亦算以实际使用及处理区至弃牌堆的实体发票判定，原生无懈、设伏延迟无懈和多材料无懈保留真实行动、链深和已付完成证明。上限费用与取牌移动使用各自正确续接，失源后续一次完成；上限降至零执行真实死亡，已死亡者不取得未来牌。
- 最终聚焦 Core142、WPF8 通过，其中17项新增行为方法；规定例行 Core774、WPF81 通过，用时103.053秒，增量构建1.177秒，仍高于约一分钟目标。2594个源输入前后一致；405名旧完整武将定义、2179个旧枚举值、785个旧技能哈希和310条旧立绘登记均保持。名单408、立绘313，三个实际图鉴渲染已查看，司马徽和徐荣详情使用滚动面板。
- 版本未提升；没有 manual Full、人工前台完整对局、提交、推送或发布。一次隐藏窗口 Space 前置条件观察到 AsyncLeftDown=true，保留原保护和失败实值，没有人为延时或放宽；后续必要最终聚焦和例行均通过。全部修复、历次失败、解释限制、恢复源码逐项哈希/CRC及保留清理输出见[本组验收记录](../benchmarks/2026-10-15-sima-hui-xu-rong-li-jue-verification.json)。上一组验收原件保持。持续目标active，继续张济、苏飞、黄权。


## 2026-10-15 张济、苏飞、黄权

- 按当前普通 OL 419/420/421 官网、API 与有界客户端条目接入。掠命由真实目标选点数，最终原生判定决定两点伤害或取得实际随机区域牌；结果加入既有绑定清理。屯军按真实历史发动次数依次使用不同副类型牌堆装备，已支付的限定序列在发起者死亡后仍向存活受益者结算。
- 联翩记录真实出牌阶段连续主目标与整实际回合次数；无中生有自身目标、借刀完整配对及伏绵四位目标均覆盖。共同目标后来死亡不取消已成立的摸牌资格，交牌仍要求存活目标与原摸实体；公开付款事实不暴露未知新手牌。
- 点虎使用真实游戏开始的指定关系、单次伤害及实际外角色回复事件。谏计复用原生合法动作与执行流程，保实际受赠者/provider、精确实体和父返回，不借当前角色的杀次数。普通酒按实际回合记录且救命酒排除，外回合酒效按当前回合结束失效；声明转换保原生质疑、付款及成功/失败返回。
- 最终聚焦 Core165、WPF9 通过，其中16项新增行为方法；规定常规 Core790、WPF86 通过，用时130.917秒，增量构建1.405秒，仍高于约一分钟目标。2615个源输入跨最终聚焦及常规保持；408名旧完整武将定义、2198个旧枚举值、795个旧技能哈希及313条旧立绘登记不变。名单411、立绘316，三张实际图鉴渲染已逐张核对，文本完整可见。
- 保留并发维护与界面改动；最小修复动画时钟返回类型，以及关闭动效时原生按钮两DIP按下反馈，保留外层动画与原测试断言。一次共享验证锁忙未启动构建/检查，不混用另一运行的证据；全部失败、解释边界、恢复源码逐项哈希/CRC及策略阻止清理的输出见[本组验收记录](../benchmarks/2026-10-15-zhang-ji-su-fei-huang-quan-verification.json)。版本未提升，无manual Full、人工前台整局、提交、推送或发布。持续目标active，继续下一组来源预查。

## 2026-10-15 唐咨、樊稠、郭汜

- 按当前普通 OL 422/423/424 官网、API 与保留的有界客户端条目接入。唐咨为魏势力；兴棹按存活且有实际体力值的受伤人数授予既有恂恂、在真实装备使用时摸牌，并于弃牌阶段入口重判是否跳过。恂恂保持私有四牌两顶两底整理及后续正常摸牌。
- 樊稠兴乱连接真实出牌阶段使用完成、场上六点牌取得、私有牌堆选择或无牌摸一，以及被指定角色自主弃六点牌或交牌。实付续接保来源、窗口、实体和移动凭据；白银狮子回复及木牛流马粮清理子批按原生路径返回，后者必须精确匹配收据记录的真实支付批 ID。
- 郭汜贪狈由目标自主选择，已付约定按真实回合与双方限定，杀仍支付原生次数。伺盗按接受使用的发生顺序记录连续主目标；真实一手牌顺手牵羊经过原生合法性、无懈及私有区域槽位，所属完成窗口和父行动凭据不被外来窗口替代。
- 保留并发用户授权的测试裁剪，未恢复旧套件。最终聚焦 Core31、WPF1 通过，其中19项新增行为方法；当前规定常规 Core119、WPF20 全过，含增量构建31.098秒。2607个源输入跨最终两轮保持，411名旧完整武将定义、2207个旧枚举值、800个旧技能哈希及316条旧立绘登记不变；名单414、立绘319，三张实际图鉴渲染均已核对。
- 早期扩展常规的输入环境失败、独立非前台桌面实验及并发裁剪后反射夹具恢复均分别记录，不冒充当时全部通过。原断言保留，无延时或设备状态篡改。没有 manual Full、人工前台整局、提交、推送或发布；版本未提升。完整边界、逐项哈希/CRC核验的源码恢复档和策略阻止清理的输出见[本组验收记录](../benchmarks/2026-10-15-tang-zi-fan-chou-guo-si-verification.json)。持续目标active，继续下一组来源预查。


## 2026-10-15 张琪瑛、曹纯、王粲

- 按当前普通/对应 OL 425/426/428 精确版本接入；紫微在独立判定生效前窗口修改有效花色与点数，真实八卦阵结果、原判定实体和冷恢复均通过。法箓严格区分实际弃置、处理中转凭证和木牛粮自动清理；后土在真实回合外救援使用支付，玉清支持实际自伤。
- 缮甲接真实装备失去、公开派生损标记、完整初始杀之后冻结 X、后续实际手牌使用/无懈等原生支付、失源已付义务及结束杀。摸牌付费、解横置、真实阵营提供者和火雷杀来源保各自费用与类型化返回。
- 散文按原生整次摸牌凭证归并实际新牌，排除全部本次新牌作为旧手牌见证；七哀逐存活角色自主交牌；登楼保原私有顶牌租约、先非基本牌入手、真实基本牌使用及精确失效/死亡清理。
- 新增20项行为方法，最终聚焦 Core34/WPF1 通过；既定常规 Core119/WPF20 全过，含增量构建30.240秒。2636个输入跨最终检查保持；414名旧完整武将、2223个旧枚举值、804个旧技能哈希、319条旧立绘登记不变。名单417，立绘322。图鉴三张首屏与两张长技能滚动底部渲染已实际核对。
- 第一次常规保留真实 AsyncLeftDown=true 输入前置失败；只读空闲状态复核和独立输入检查通过后，同一常规无代码修改重跑全过。未扩大默认范围，未运行 manual Full、人工前台完整对局、提交、推送或发布；版本不变。完整失败记录、解释边界、逐项哈希/CRC源码恢复档及策略阻止清理的保留目录见[本组验收记录](../benchmarks/2026-10-15-zhang-qi-ying-cao-chun-wang-can-verification.json)。持续目标 active，继续下一组。


## 2026-10-15 SP庞统、袁谭袁尚、吕虔

- 按当前普通/对应 OL 429/430/432 官网、API 与有界客户端精确条目接入。过论真实亮牌与可选原子交换，颂丧真实他人死亡后的限定回复/上限与永久授予；展骥按原生整次摸牌开始时冻结技能来源，修复先摸牌、后获得技能再误追溯触发的问题。
- 内伐连接真实阶段入口、领取后原生子流程与真实弃牌，类别禁用、动态 X、完整借刀目标对及首两次实际装备使用共享真实回合。移除最后目标保留真实付款并结束原生使用，修复无中生有错误回落为自身摸牌和零借刀对异常。
- 围虏按真实伤害来源累计下次实际出牌阶段债务，只在所属阶段结束回复实付体力；阶段拥有者死亡后，仍偿还给存活受益者。赠刀交出真实装备，原赠予者失技/死亡保留已给实体，由实际受赠者自主支付一张公开赠牌强化真实来源伤害。
- 新增18项行为方法，最终聚焦 Core29/WPF1 全过；规定例行 Core119/WPF20 全过，含增量构建29.662秒。2663个输入跨最终聚焦/例行保持；417名旧完整武将、2261个旧枚举值、811个旧技能哈希及322条旧立绘登记均未变。当前名单420、立绘325，三个实际图鉴渲染已逐张核对。
- 历次失败和修复、规则解释、覆盖边界、逐项哈希/CRC核验的源码恢复档及策略阻止清理的保留目录见[本组验收记录](../benchmarks/2026-10-15-sp-pang-tong-yuan-tan-yuan-shang-lv-qian-verification.json)。版本不变，没有 manual Full、人工前台整局、提交、推送或发布。持续目标 active，下一组吕旷吕翔、辛毗、花鬘已完成来源预查并分派契约；433孙邵已有同版完整定义，跳过重复。
