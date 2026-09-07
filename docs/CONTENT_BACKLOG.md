# 内容扩充清单

这份清单是简要入口；按阶段维护的动态队列见 [`LUNA_CONTENT_BACKLOG.md`](LUNA_CONTENT_BACKLOG.md)。Core、内容、WPF 和测试现在由同一个长期 goal 统一推进，但每批内容仍必须满足：规则核心不引用 WPF、AI 只读取玩家视角快照、固定种子可复现、普通快照不泄露暗信息，并通过 Release 构建、Console 自测和格式检查。

## 当前阶段：K4 开局切片 + K5 基础结算切片已落地，继续扩大内容池并完善完整结算入口

已完成：

- `CardCatalog` 注册杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻的显示文案、牌类别、AI 价值和弃牌保留价值；WPF 卡面从 Core 内容目录读取描述。
- `StandardDeckCatalog.BasicDemo` 注册 18 张杀、2 张火杀、2 张雷杀、18 张闪、10 张桃、2 张酒、4 张决斗、2 张无中生有、2 张南蛮入侵、2 张万箭齐发、2 张桃园结义、2 张五谷丰登、2 张过河拆桥、2 张顺手牵羊、2 张火攻，共 72 张牌。
- 初始每人 4 张、摸牌阶段 2 张也属于牌堆配置，`GameEngine` 不再拥有这些内容常量。
- 回归检查覆盖内容注册、牌堆总数与分布、id/花色/点数稳定性、武将技能注册和 AI 使用桃的策略。
- `CardGame.Content.Standard` 已注册 `standard:*` 基本牌、技能、武将、牌堆和 `identity:standard-8` 模式元数据；`ContentRegistry` 校验依赖并冻结独立只读投影。
- WPF 已通过 Standard Registry 创建对局；`Content.cs` 只保留兼容投影，不再作为新增内容的长期落点。
- `ContentModeDefinition` 已描述 `DeckId`、候选数量和 `GeneralPoolIds`；WPF 通过 `UseInteractiveSetup` 使用私有选将、共享池去重和逐轮发牌。
- 现有杀/火杀/雷杀/闪/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/火攻/伤害/桃链路、属性伤害类型、决斗交替杀响应、群体牌逐目标杀/闪响应、桃园结义逐目标恢复、五谷丰登公开翻牌与私有逐人选牌、过河拆桥确定性盲弃目标手牌、顺手牵羊距离一盲取目标手牌、火攻私有展示与同花色弃牌及基础濒死求桃已通过 `ResolutionFrame` 栈和类型化事件接入 Core；属性抗性、铁索连环和其他锦囊、装备、判定、通用触发时机及复杂濒死响应仍按后续阶段开放。
- K4 Console 场景覆盖私有候选不泄漏、非法武将零状态变化、共享池不重复、5 人 AI 选将终止和同 seed 开局确定性。
- `standard:duel` 已接入目标选择、重复 `RespondSlash` Prompt、响应牌处理区生命周期和 Duel 类型化事件；WPF 响应按钮根据当前 Prompt 切换为“打出杀”。
- `standard:draw_two` 已接入无目标 `CardUseFrame`、牌堆摸牌、处理区生命周期和 `CardUseFinishedEvent`；WPF 卡面直接消费 Registry 描述。
- `standard:barbarian_assault` 已接入 `CardUseFrame.TargetIndex`、所有其他存活角色的顺序目标、逐目标私有 `RespondSlash`、`GroupCardUsedEvent`/`GroupResponseEvent` 和单次伤害/濒死续接；同一张南蛮入侵在所有目标完成前保持于 `Processing`。
- `standard:arrow_barrage` 复用同一组群体结算入口，将逐目标私有响应切换为 `RespondDodge`；`GroupResponseEvent` 记录必需响应牌种类，父牌同样在全部目标完成前保持于 `Processing`。
- `standard:peach_garden` 复用 `CardUseFrame.TargetIndex`，锁定使用时仍存活的全部角色（含使用者），每次 `AdvanceOneStep()` 只处理一个目标；受伤角色进入 `RecoveryFrame` 并提交 `RecoveryAppliedEvent`，满血角色跳过，父牌在全部目标完成前保持于 `Processing`。
- `standard:five_grains` 复用 `CardUseFrame.TargetIndex`，公开翻出等同于锁定存活角色数的牌；当前 picker 通过私有 `SelectHarvestCard` prompt 从公共展示区选择一张，AI 只接收自己的脱敏快照和公开选项，选中的牌进入 picker 手牌，其余公开牌按统一移动理由清理，父牌在全部 picker 完成前保持于 `Processing`。
- `standard:dismantlement` 已接入单目标 `CardUseFrame`：只允许选择有手牌的其他存活角色，核心使用确定性随机数盲弃置目标一张手牌；普通快照和公共 `TargetCardDiscardedEvent` 不包含被弃牌的 ID/牌面，可信宿主移动账本保留完整来源和 reason。装备区、判定区以及真正的目标私有候选仍未开放。
- `standard:snatch` 已接入单目标 `CardUseFrame`：只允许选择距离为 1 且有手牌的其他存活角色，核心使用确定性随机数盲取目标一张手牌并转入使用者手牌；普通快照和公共 `TargetCardTakenEvent` 不包含被取牌的 ID/牌面，只有使用者私有快照能看到取得的牌。当前距离只按座位环计算，不考虑坐骑或死亡角色缩圈。
- `standard:fire_attack` 已接入两段私有选牌的单目标 `CardUseFrame`：目标只在自己的快照中选择展示牌，展示后牌面通过 `FireAttackCardRevealedEvent` 和 `PublicRevealedCards` 对所有观察者公开；攻击者再从自己的同花色手牌中选择弃牌或跳过，成功弃牌才进入 `DamageNature.Fire` 的 1 点伤害链。AI 只接收本座私有快照和宿主发布的候选 ID，WPF 复用通用 Prompt 按钮，普通快照不暴露未展示牌面。
- `standard:fire_slash` / `standard:thunder_slash` 已接入普通杀的合法性、闪响应和处理区生命周期；命中时通过 `DamageNature.Fire` / `DamageNature.Thunder` 写入伤害帧、伤害事件和 AI/日志可解释结果。属性抗性、属性转化和铁索连环仍未开放。
- `standard:alcohol` 已接入出牌阶段无目标使用、`Hand → Processing → DiscardPile` 生命周期和公开一次性酒效；本回合下一张直接杀在声明时获得 +1 伤害，金额贯穿 `DamageFrame`、伤害事件、技能上下文和濒死收尾，未消费的酒效在回合结束失效；濒死时仅由濒死者使用酒自救 1 点体力，不能用酒救援其他角色。
- `standard:feedback` 已接入存活伤害后的可选触发：伤害牌仍在 `Processing` 时收集当前存活拥有者的 `DamageTriggerCandidate`，经 `DamageTriggerOrdering` 稳定排序后冻结在带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前候选压入 `DamageSkillFrame`，只向反馈者发布私有 `Feedback` Choice；帧和请求/结果事件保留 `CandidateId`/`Priority`，发动后按 `skill.feedback.claim-damage-card` 将同一实体牌移入技能拥有者手牌，并发布 `DamageSkillResolvedEvent`/`DamageCardClaimedEvent`；不发动则正常弃牌，群体父结算帧仍可继续推进，普通快照不泄漏取得牌 ID。现有内置技能默认仍只参与受伤者窗口。
- `standard:yiji` 已接入受伤后的可选触发：当前候选压入 `DamageSkillFrame` 后，先通过 `skill.yiji.draw` 从牌堆私有摸两张牌，再向郭嘉发布精确的牌/其他存活目标组合和跳过选项；发动时按 `skill.yiji.give-card` 将一张牌由拥有者手牌移给目标并提交 `DamageSkillCardGivenEvent`，跳过时两张牌留在拥有者手牌。摸牌、跨手牌移动和候选牌面只属于可信宿主或对应私有视图；通用非受伤者触发条件仍未开放。
- `standard:jieming` 已接入同一伤害触发窗口：荀彧受伤后发布私有 `DecisionKind.Jieming`，从存活且公开手牌数低于体力上限的角色中选择一名目标；发动后按 `skill.jieming.draw` 从牌堆摸至目标上限，并由 `DamageSkillCardsDrawnEvent.TargetSeat` 和移动账本记录目标，跳过仍沿候选游标继续。目标选择不携带目标隐藏牌面；通用非受伤者触发条件和更复杂的跨层效果仍未开放。
- `standard:yuanhu` 已接入同一伤害触发窗口：援护者在其他角色受到正伤害后发布私有 `DecisionKind.Yuanhu` 弃牌 Choice；发动时按 `skill.yuanhu.discard` 将自己的一张手牌移入弃牌堆，并通过 `RecoveryFrame` 令固定受伤目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 和 `DamageSkillResolvedEvent.EffectTargetSeat` 记录可信宿主结果，普通视图不泄漏弃牌 ID；这是当前唯一显式 opt-in 的非受伤者触发示例。
- `standard:wusheng` 已接入红色非杀实体牌按 `Slash` 使用：合法动作通过 `PlayedCardKind` 区分物理牌与有效牌型，`PlayCardCommand`/WPF 均可明确选择转化；事件和伤害链记录有效 `Slash`，移动账本保留原始实体牌，不复制牌或绕过来源校验。

当前实现十五种牌：六种基本牌和九张最小锦囊（含一张无目标即时牌、两张共用响应机制的群体牌、一张多目标恢复牌、一张公开 draft 牌、一张隐藏手牌盲弃牌、一张距离一隐藏手牌盲取牌和一张私有展示/同花色弃牌牌）。酒支持出牌阶段的一次性直接杀加伤和濒死者自救 1 点体力，但不能救援其他角色。这里的“标准”指本 Demo 的标准演示牌堆，不声称是商业游戏的官方卡表。

## 等待核心扩展点后接入

以下内容先记录需求，不把未实现的牌塞进现有 `CardKind` 或在窗口里写分支。

### 锦囊

- 过河拆桥：已完成最小单目标盲弃手牌切片；完整的目标可见/不可见区域选择、装备/判定区和目标私有候选仍需安全的目标视角与牌移动事件。
- 顺手牵羊：已完成最小距离一盲取目标手牌切片；当前只按座位环计算距离，装备区选择、马匹修正和目标私有候选仍需后续安全的视角与区域入口。
- 无中生有：已接入无目标“使用后摸两张”的类型化卡牌效果；重洗和牌堆耗尽仍沿用 Core 的公共摸牌入口。
- 决斗：已接入交替 `RespondSlash` 响应；群体/多伤害嵌套和无懈链仍待后续 K5/K7。
- 南蛮入侵：已接入所有其他存活角色按座次逐个 `RespondSlash`；每个未响应目标独立进入伤害/濒死链，同一使用牌保持在 `Processing`，更复杂的群体多伤害与无懈链仍待后续 K5/K7。
- 万箭齐发：已复用群体目标游标和处理区生命周期，所有其他存活角色按座次逐个 `RespondDodge`；`GroupResponseEvent` 使用通用 `RequiredCardKind`/`UsedResponse`/`ResponseCardId` 字段，后续群体牌可继续接入同一入口。
- 桃园结义：已接入全部存活角色（含使用者）的多目标恢复；`TargetIndex` 跨公开单步边界推进，受伤目标使用 `RecoveryFrame` 和 `RecoveryAppliedEvent`，满血目标不生成伪恢复事件，使用牌直到全部目标完成才进入弃牌堆。
- 五谷丰登：已接入公共展示区与逐 picker 私有 `SelectHarvestCard` prompt；展示牌对所有快照可见，当前选牌、其他玩家手牌和 AI 思考仍按 viewer 隔离，选牌事件和 `card.harvest-pick` 移动账本可回放，父牌直到所有 picker 完成才离开 `Processing`。
- 无懈可击：需要对卡牌效果建立可插入的响应时机和链式抵消。

### 装备

- 武器：需要装备区、攻击范围和持久化修正；后续可加入诸葛连弩、青釭剑等不含商业素材的纯文字内容。
- 防具：需要防具区和响应/伤害修正时机；八卦阵应在类型化判定入口开放后实现。
- 坐骑：需要距离计算和上下马的牌移动语义；`+1`/`-1` 坐骑不能仅作为 UI 标签。

### 武将与技能

现有奸雄、反馈、咆哮、英姿、空城、武圣、龙胆、遗计、节命、援护覆盖当前十个被动查询入口。反馈目前是目标存活且伤害牌仍在 `Processing` 时冻结并稳定排序候选、通过 `DamageTriggerWindowFrame` 游标发布私有选择的最小触发切片；遗计已经复用同一游标和私有 Prompt，完成受伤后摸两张牌并向其他存活角色交一张牌的跨手牌效果；节命复用同一游标和私有 Prompt，按公开手牌数量选择目标并补牌至其体力上限；援护是当前唯一显式跨座位的伤害后技能，使用拥有者手牌换取固定受伤目标的 1 点恢复；武圣是红牌按杀的最小牌转化切片；龙胆复用有效/物理牌型分离的出牌与响应入口；通用非受伤者候选的窗口骨架已开放，但通用跨座位触发的可配置语义和复杂伤害后技能仍需后续入口。新增武将技能前需要核心任务开放至少一项对应能力：

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
