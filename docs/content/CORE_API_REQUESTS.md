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
- 苦肉在出牌阶段且体力大于 0 时失去 1 点体力并摸两张牌；若正好降至 0，Core 保留 `ActiveSkillFrame`，沿共享私有 `RescueDying` 窗口完成救援或死亡清理，获救后才继续摸牌。规则 v17 的经典制衡在每个出牌阶段限一次，可弃置至少一张自己的手牌或公开装备并摸等量牌；v1–v16 和演示模式保留手牌限定与可重复发动。仁德在出牌阶段私有选择手牌和一名其他存活角色，经过 `Processing` 交牌并按数量恢复，当前回合只能发动一次。青囊在出牌阶段私有选择一张手牌和一名受伤存活角色，经过 `Processing` 弃置并令目标恢复 1 点体力，当前回合只能发动一次。回春在出牌阶段私有选择精确两张手牌和 2–3 名受伤存活角色，逐目标恢复 1 点体力，当前回合只能发动一次。更复杂的多目标选择和多效果结算仍属于后续 Core API。
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
 - 当前边界仍是单次伤害的基础求桃；多伤害嵌套、通用触发器和复杂技能仍待后续 K5；可信命令前缀 Checkpoint 已开放，可恢复命令驱动的私有 Prompt 暂停点，规范化内容指纹也已用于拒绝同版本定义漂移；完整内部状态存档、可执行规则实现签名和兼容迁移仍等待后续 K8。

## Core API Level: K6 基础切片已开放（2026-09-07）

- `EquipmentSlot` 提供武器、防具、进攻坐骑、防御坐骑、宝物五类槽位；`PlayerSnapshot.Equipment` 是公开投影，不把其他玩家手牌或装备候选泄漏给普通视图。
- 装备使用通过 `LegalActionKind.Equip` 和精确无目标 Choice 提交；装备实体按 `Hand → Processing → Equipment` 移动，同槽替换按 `Equipment → DiscardPile` 后进入新装备，并发布 `EquipmentChangedEvent`。
- `GetCombatDistance` 计算存活座位环距离并应用赤兔/绝影及 `standard:mashu` 的出攻距离 modifier；`GetLegalActions` 应用 `standard:qicai` 的锦囊距离豁免；`GetAttackRange` 应用诸葛连弩范围 modifier；玉玺接入回合摸牌数量，死亡清理装备区并保持牌数守恒。
- K6 基础场景已由 `equipment.replace`、`equipment.distance`、青釭剑无视防具、公开装备目标选择和 Standard Console 自测覆盖；K7 八卦阵基础判定场景以及公开判定区牌目标选择已由独立回归覆盖；装备失效/卸载语义和复杂装备效果留待后续 K6/K7。
