# 内容任务的核心 API 请求

这些请求记录内容和核心共同需要的契约。当前由同一个长期 goal 统一推进；每个核心等级开放后，把对应的 manifest 项从 `planned` 改为可实现，并补齐场景测试。

## K1：牌区与原子移动

内容需要：

- 内容规范层将稳定 `definitionId` 与本局唯一 `instanceId` 分离；K1 当前宿主仍以 `CardId`/`CardKind` 记录，正式运行时类型在 K3 Registry 冻结；
- `DrawPile`、`Hand`、`Processing`、`DiscardPile`、`Equipment`、`Judgment` 等强类型牌区；
- 单张/批量移动、替换装备、死亡清理和重洗的统一入口；
- 提交后可观察的 `CardMoved` 宿主通知；批量 `CardsMoving`/`CardsMoved` 触发时机属于 K5 类型化结算，不作为 K1 的内容消费前提。

该等级历史上阻塞了奸雄、反馈、遗计、过河拆桥、顺手牵羊、装备替换和所有“从目标区域取牌”的内容；当前过河拆桥/顺手牵羊的最小 K5 手牌效果已开放，但装备区和其他目标区域仍需后续入口。临时 `CardGame.Core/Content.cs` 不继续扩大为正式 Registry。

## K2：命令、Revision 与完整 Prompt

内容需要：

- 主动技能、目标选择、私有候选和多牌选择的 `GameCommand`/`PendingPrompt`；
- `PromptId`、`Revision`、Responder 和精确 Choice 的验证；
- AI 与真人共享同一询问形状。

该等级历史上阻塞了仁德、制衡、苦肉、过河拆桥、顺手牵羊、装备主动效果和选将；当前过河拆桥/顺手牵羊已经消费精确目标 Choice，装备主动效果仍未开放。UI 不能通过按钮参数拼接“合法卡牌集合 × 合法目标集合”来替代完整 Choice。

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

仍阻塞内容：国战双将和 2v2 开局；5/8 人更大规模固定 seed 矩阵、同时选将和模式策略对象属于 K4 扩展验证。

## K5：类型化卡牌结算与响应链

内容需要：

- `CardUseDeclared`、目标确认、效果序列、响应窗口和 `CardUseFinished`；
- 伤害、属性伤害、回复、基础濒死求桃、死亡和奖惩时机；
- 可暂停恢复的嵌套结算帧与稳定事件顺序。

当前已开放的 K5 基础切片：`standard:slash`、`standard:fire_slash`、`standard:thunder_slash`、`standard:dodge`、`standard:peach`、`standard:alcohol`、`standard:duel`、`standard:draw_two`、`standard:barbarian_assault`、`standard:arrow_barrage`、`standard:peach_garden`、`standard:five_grains`、`standard:dismantlement`、`standard:snatch`、`standard:fire_attack` 已接入 `CardUseFrame`、`ResponseWindowFrame`、`DamageFrame`、`DamageTriggerWindowFrame`、`DamageSkillFrame`、`RecoveryFrame`、`DyingFrame` 和 `DeathFrame`；决斗通过 `RespondSlash` 让双方交替响应，无中生有通过空目标 `CardUseFrame` 摸两张牌，南蛮入侵/万箭齐发通过 `CardUseFrame.TargetIndex` 按座次逐目标发布私有 `RespondSlash`/`RespondDodge`，桃园结义通过同一目标游标按座次逐目标恢复，五谷丰登通过同一目标游标公开翻牌并发布私有 `SelectHarvestCard`，过河拆桥通过目标 `CardUseFrame` 和确定性随机数盲弃目标一张隐藏手牌，顺手牵羊通过 `GetSeatDistance` 限制座位环距离一并确定性盲取目标一张隐藏手牌；火杀/雷杀共用杀的闪响应，实际牌种分别映射为 `DamageNature.Fire`/`DamageNature.Thunder` 并贯穿 `DamageFrame`、`DamageRequestedEvent`、`DamageAppliedEvent`、`AfterDamage` 和 `DamageSkillContext`；酒通过公开 `HasAlcoholEffect`、`AlcoholAppliedEvent`/`AlcoholExpiredEvent` 和实际伤害金额接入直接杀链路，直接杀声明时消费酒效并使 `DamageFrame`、伤害请求/应用/AfterDamage 与 `DamageSkillContext.Amount` 为 2；濒死者可在私有 `RescueDying` Prompt 使用酒自救 1 点体力，生成标记 `DyingResponseEvent.UsedAlcohol` 的响应和 `RecoveryAppliedEvent`，但不能用酒救援他人；`GroupResponseEvent` 记录必需响应牌种类，`TargetCardDiscardedEvent`/`TargetCardTakenEvent` 只发布脱敏效果，反馈通过 `DamageTriggerCandidate`/`DamageTriggerOrdering` 收集并稳定排序，在存活目标的伤害牌仍位于 `Processing` 时先压入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前候选发布私有 `Feedback` 选择并压入 `DamageSkillFrame` 与 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留 `CandidateId`/`Priority`，发动才通过 `DamageCardClaimedEvent` 将同一实体牌移入技能拥有者手牌；遗计在同一窗口内发布私有 `Yiji` 牌/目标组合，先将两张牌从 `DrawPile` 移入拥有者手牌，再按 `skill.yiji.give-card` 将其中一张移至其他存活角色手牌，每张伤害牌至多一次，每个未响应目标独立续接单次伤害/濒死链，单次伤害后的基础濒死窗口通过 `AnswerPromptCommand` / `DecisionKind.RescueDying` 逐座询问，桃和酒只从当前 responder 的私有手牌移动到 `Processing` 再弃置，救援完成后恢复原结算帧。武圣允许红色非杀实体牌生成有效牌型为 `Slash` 的动作；`LegalAction.PlayedCardKind` 与 `PlayCardCommand.PlayedCardKind` 明确本次有效牌型，事件记录 `Slash` 而移动账本继续记录原始物理牌；龙胆使用 `PlayedCardKind`、`response-card-kind` 和 `CardRespondedEvent.EffectiveCardKind` 完成闪/杀互转。Console 自测当前为 70 项，WPF 已显示酒效状态、濒死私有选择、普通/属性杀响应、武圣转化按钮、反馈/遗计/援护私有触发、遗计牌/目标组合、群体牌响应、桃园结义恢复游标、五谷丰登公开牌/私有选牌、过河拆桥目标选择和顺手牵羊距离一目标选择、火攻两段私有选牌；反馈、遗计、武圣与龙胆结果由 AI 思考、技能状态文本和事件流可观察；可信宿主可记录已接受命令并用 CommandJson/GameReplay 确定性重放。

阻塞内容：铁索连环、无懈可击、多伤害/技能型濒死响应和其他主动技能；当前伤害候选排序、可序列化触发窗口游标和可信宿主窗口生命周期事件已开放，反馈与遗计已分别覆盖处理区取牌和跨座位赠牌，火攻已覆盖目标私有展示、同花色弃牌和火焰伤害，但现有内置技能的触发条件默认仍只匹配受伤者，非受伤者本身触发的通用候选仍未开放；属性抗性/转化、南蛮入侵/万箭齐发的多伤害嵌套、无懈链、用酒救援他人和复杂变体仍属于后续扩展。

本批已开放 `standard:feedback` 与 `standard:yiji` 的最小可选伤害后触发：目标存活且伤害牌仍在 `Processing` 时，Core 收集当前存活拥有者的 `DamageTriggerCandidate`，通过 `DamageTriggerOrdering` 稳定排序后压入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前候选压入 `DamageSkillFrame`；反馈通过私有 `Feedback` Choice 决定是否按 `skill.feedback.claim-damage-card` 取得伤害牌，遗计先发布私有两张摸牌，再通过 `Yiji` 牌/目标 Choice 按 `skill.yiji.give-card` 将其中一张移给其他存活角色。帧和 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留 `CandidateId`/`Priority`，遗计额外发布 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent`，普通视图不泄漏隐藏牌 ID，群体父帧仍可继续推进。`CanTriggerAfterDamage` 已成为候选时机钩子，但现有内置技能的触发条件默认仍只匹配受伤者，非受伤者触发和更复杂伤害后技能仍等待后续 K5。

本批同时开放 `standard:wusheng` 的最小牌转化：红色且不是原生杀/火杀/雷杀的实体牌可以作为杀使用。合法动作、`PlayCardCommand` 和兼容 `HumanPlay` 均能通过可选 `PlayedCardKind` 选择有效牌型；事件/伤害链记录有效 `Slash`，`CardMovementRecord` 保留同一物理牌的原始 `CardKind` 和实体 ID，避免复制牌或让 UI 绕过完整合法性检查。
本批同时开放 `standard:longdan` 的最小响应型牌转化：物理闪可在出牌阶段产生有效 `Slash`，物理杀/火杀/雷杀可在需要 `Dodge` 的响应窗口产生有效 `Dodge`；`PendingDecision.Choices` 为每张物理牌发布 `response-card-kind`，`CardRespondedEvent.EffectiveCardKind` 记录有效响应牌型，实体牌继续通过统一 `Hand → Processing → DiscardPile` 路径。AI 只读取自己的玩家视图和合法响应列表，WPF 通过精确响应选择展示原生/转化差异。

本轮继续在 K5 开放 `standard:yuanhu` 的受约束跨座位触发：其他角色受到正伤害且仍存活、未满体力时，援护者获得私有 `DecisionKind.Yuanhu` 弃牌 Choice；发动时按 `skill.yuanhu.discard` 将一张自己的手牌经过 `Hand → Processing → DiscardPile`，再由 `RecoveryFrame` 令固定受伤目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 和 `DamageSkillResolvedEvent.EffectTargetSeat` 记录可信宿主证据，普通视图不泄漏弃牌 ID；AI 只读取援护者自己的手牌和公开目标状态。这个切片验证了一个显式 opt-in 的非受伤者候选，但通用可配置的跨座位触发、多伤害嵌套和完整 Checkpoint 仍待后续 K5/K6/K7。

当前 Console 自测为 70 项，完整解决方案 Release 构建为 0 warning / 0 error，`dotnet format --verify-no-changes` 通过；新增覆盖援护的 AI/人类选择、非法命令零状态变化、跨座位恢复事件、牌区移动和普通视图脱敏。

## K6：距离与装备

内容需要：

- 五类装备槽、装备/替换/失去/死亡清理；
- 距离、攻击范围和装备 modifier 查询；
- 装备授予/撤销技能的生命周期。

阻塞内容：诸葛连弩、青釭剑、八卦阵、+1/-1 马和依赖范围的武将技能。不能把装备当成一次性 `Basic` 卡处理。

## K7：判定和多层触发

内容需要：

- 判定区、判定牌移动、改判窗口和延时锦囊；
- 跨事件 Trigger 收集、优先级、询问和稳定排序；K5 当前只开放伤害候选的收集/排序边界；
- 事件取消/替换而不破坏牌区守恒。

阻塞内容：鬼才、刚烈、八卦阵、乐不思蜀、兵粮寸断、闪电、无懈可击的完整多层响应。

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

该历史批次只允许更新 `docs/content/**`，并要求补充牌移动生命周期、reason ID、可见性和 K1 强制场景；后续 K3/K5 已分别开放正式 Standard 包、奸雄/反馈/遗计的最小切片以及目标手牌取放的受限效果，装备区和其他目标区域仍按 K6 保持 BLOCKED。内容任务不会在 Core 中补缺失类型，也不会用反射、全局状态或 UI 分支绕过上述状态。

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
- WPF 已接入新流程，旧 `HumanPlay` 等兼容入口与 `UseInteractiveSetup = false` 仍可用；Console 当前覆盖 70 项场景，另有 K5 结算帧/事件栈、普通/属性杀伤害、武圣红牌按杀、反馈/遗计/节命/援护 AI 与人类可选触发、遗计跨手牌分配与节命目标补牌、命令日志 JSON 编解码与确定性重放、群体父帧续接、伤害触发候选稳定排序、酒的一次性直接杀 +1 伤害与回合结束失效、濒死者酒自救、决斗响应、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥和顺手牵羊回归。

## Core API Level: K5 基础结算切片已开放（2026-09-07）

- `ResolutionStack` 当前支持杀/闪响应、无中生有无目标效果、伤害、恢复、基础濒死和死亡帧；`DyingResponseEvent`、`DyingResolvedEvent` 等类型化事件在提交后发布；
- `ResolutionStack` 当前支持普通/火/雷杀的统一闪响应；`DamageNature` 与实际牌种一同进入 `DamageFrame`、伤害请求/应用/后置事件和奸雄伤害上下文，属性抗性和属性转化仍未开放；
- 濒死阶段新增 `EngineStatus.AwaitingHumanDying`、`PendingDecision.TargetSeat` 和 `DecisionKind.RescueDying`，普通玩家视图只获得自己作为当前 responder 的桃选项；
- 非法 `ChoiceId`、过期 Revision 和非当前 responder 的回答保持拒绝且零状态变化；AI 濒死决策只使用脱敏快照与自己的桃，不读取其他玩家手牌；
- `standard:barbarian_assault`、`standard:arrow_barrage` 与 `standard:peach_garden` 已验证无目标群体牌的完整目标列表、`CardUseFrame.TargetIndex`、逐目标响应/恢复、由 `GroupCardUsedEvent` 声明目标列表且由 `GroupResponseEvent` 携带 `RequiredCardKind`、处理区驻留和每个目标的伤害/濒死/恢复续接；`standard:dismantlement` 另验证单目标选择、目标隐藏手牌的确定性盲弃、`TargetCardDiscardedEvent` 的牌面脱敏和双牌处理区移动账本；`standard:snatch` 验证座位环距离一合法性、目标隐藏手牌确定性盲取、`TargetCardTakenEvent` 的牌面脱敏、取得牌进入使用者私有快照以及跨玩家处理区移动账本；AI 响应、恢复、盲弃和盲取评分只使用当前目标/全局公开体力/手牌数量的脱敏快照；
- 反馈、遗计和节命共享 `DamageTriggerWindowFrame`/`DamageSkillFrame`；其中反馈取得处理区伤害牌，遗计私有摸两张并交一张，节命在受伤者的私有 `Jieming` Prompt 中按公开手牌数选择目标并从牌堆补至体力上限，AI 不读取目标隐藏牌面，`DamageSkillCardsDrawnEvent.TargetSeat` 与 `skill.jieming.draw` 记录可信宿主结果；
- 当前边界仍是单次伤害的基础求桃；多伤害嵌套、通用触发器、复杂技能和完整回放 Checkpoint 继续等待后续 K5/K6/K7 切片。
