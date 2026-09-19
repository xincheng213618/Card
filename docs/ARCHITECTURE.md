# 架构说明

## 1. 边界

```text
┌──────────────────────── CardGame.Wpf ────────────────────────┐
│ MainWindow.xaml                                               │
│      ↓ Binding / ICommand                                     │
│ MainViewModel ───── 提交 GameCommand（旧 Human* 为适配器） ─┐   │
└───────────────────────────────────────────────────────────│───┘
                                                            ↓
┌──────────────────────── CardGame.Core ─────────────────────────┐
│ GameEngine                                                     │
│   ├─ PlayerRuntime（体力、身份、武将等私有真实状态）           │
│   ├─ CardZoneStore（所有实体牌位置的唯一真相）                 │
│   ├─ ContentRegistry（每局独立、冻结的内容输入）              │
│   ├─ CardCatalog / StandardDeckCatalog（兼容投影）             │
│   ├─ IPassiveSkill（规则查询）                                 │
│   ├─ IActiveSkill（主动技能效果数据）                          │
│   ├─ SimpleAiBrain（读取过滤后的 GameSnapshot）                │
│   └─ Log / AiThought / GameSnapshot（提交后的只读输出）        │
└────────────────────────────────────────────────────────────────┘
                ▲
                │ 注册
┌──────────── CardGame.Content.Standard ────────────┐
│ StandardContentPackage → StandardContentRegistry  │
└───────────────────────────────────────────────────┘
```

`CardGame.Core` 不引用 WPF、线程、计时器或网络库。窗口不能直接改体力、手牌或身份；它只能调用引擎公开命令。这个边界以后可以把 WPF 换成网页、Unity 或真正的服务器，而不用重写规则。

`GetHumanHandGuidance()` 从当前玩家手牌、私有询问和既有合法动作产生只读的使用条件说明，不提交规则动作或泄漏其他玩家的信息。`MainViewModel.PlayerGuide` 结合玩家快照、选牌状态和 `CardCatalog` 呈现当前指南与图鉴；打开指南暂停 AI 计时推进并隔离后方控件，关闭保留选择。详见 [`PLAYER_GUIDE.md`](PLAYER_GUIDE.md)。

`TutorialScenario` 只用固定种子和正式 `GameCommand` 准备杀、闪、桃、弃牌四个练习位置。`MainViewModel.Tutorial` 暂存并脱离正式引擎，按类型化规则事件判断完成，教学期间屏蔽替换牌局和存档的命令；退出后重新挂接原引擎及界面选择。详见 [`NEW_PLAYER_TUTORIAL.md`](NEW_PLAYER_TUTORIAL.md)。

声音由 `MainViewModel.Audio` 在已接受命令返回后发布，复用公开战斗提示并单独比较当前玩家 PromptId，因此没有新战斗事件的出牌阶段也能提示。`GameAudioController` 处理前台、音量、静音、节流和故障隔离，`MediaPlayerAudioOutput` 只播放应用自带 WAV。声音失败不会中断已提交的规则动作，读取或新开对局停止旧声音。详细生命周期与本机原生播放证据见 [`BATTLE_AUDIO.md`](BATTLE_AUDIO.md)。

`BattleCueProjector` 从每次提交完成后的事件增量中提取公开出牌、响应、伤害、回复和回合提示。私有手牌移动与选将候选不进入动画。`BattleFeedbackLayer` 根据实际武将与公开牌控件的位置绘制，不提交命令、不接收输入，闲置或卸载后停止计时。中央最近出牌也来自有效牌型事件，存档恢复只重建静态列表，不重播历史动画。详见 [`BATTLE_FEEDBACK.md`](BATTLE_FEEDBACK.md)。

## 2. 状态与命令

引擎内部的 `PlayerRuntime` 保存完整身份和手牌。外部只能取得 `GameSnapshot`：

- 自己：可见身份和完整手牌；
- 主公：身份公开；
- 其他存活角色：只有武将、体力和手牌数量；
- 选将阶段：自己的候选和已选武将可见，其他座位的候选和未公开武将均隐藏；全部完成后武将一次公开；
- 阵亡角色：身份公开；
- 普通快照不携带随机种子，也不会携带其他玩家正在处理的私有决定；
- `ProcessingCardCount` 只公开处理区数量，不公开牌堆顺序或他人暗牌 ID；已装备的牌面通过 `PlayerSnapshot.Equipment` 公开，延时牌通过 `PlayerSnapshot.Judgment` 公开，但装备前的手牌仍只对持有者可见；酒效是公开的 `HasAlcoholEffect` 状态，不扩大任何手牌可见性；五谷丰登的 `PublicRevealedCards` 是规则明确公开的牌面例外，当前 `SelectHarvestCard` prompt 仍只给当前 picker；过河拆桥和顺手牵羊对隐藏手牌继续使用不带牌面/牌 ID 的脱敏事件，对公开装备或判定区目标则在 `TargetCardDiscardedEvent`/`TargetCardTakenEvent` 中携带已公开的实体身份，顺手牵羊取得公开区域牌后只在使用者私有手牌快照中出现；
- 反馈、遗计与节命的 `DamageTriggerWindowFrame`、`DamageSkillFrame`、窗口生命周期事件和 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 只属于可信宿主结算/事件投影；其中保留稳定的 `CandidateId`/`Priority`，`Feedback`/`Yiji`/`Jieming` Prompt 只投影给对应拥有者，遗计的摸牌与跨手牌转移通过 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 记录，节命的目标和补牌通过 `DamageSkillCardsDrawnEvent.TargetSeat` 与 `skill.jieming.draw` 移动记录，普通快照不泄漏其他玩家的候选牌 ID；
- 援护的 `Yuanhu` Prompt 只投影给非受伤的技能拥有者；弃牌 ID 只属于可信宿主的 `DamageSkillCardDiscardedEvent`/移动账本，固定受伤目标和 1 点恢复属于公开结算结果，普通快照不泄漏被弃牌的实体 ID。
- 刚烈的 `Ganglie` Prompt 只投影给受伤者；判定牌和红/黑结果公开，红色后的精确两牌组合或承受伤害 Choice 只投影给伤害来源，`GangliePunishmentResolvedEvent` 与技能结果事件属于可信宿主记录。
 - 主动技能的 `UseSkillCommand`、`ActiveSkillFrame` 和 `ActiveSkillRequestedEvent`/`ActiveSkillResolvedEvent` 只在当前拥有者的合法出牌动作中产生；`苦肉` 的体力扣除和摸牌结果公开，具体摸入牌 ID 只属于可信宿主事件/移动账本；`制衡` 的精确弃牌集合只属于当前拥有者与可信宿主，普通快照不泄漏暗牌；青囊的精确弃牌 ID 和目标选择同样只在拥有者 Prompt/可信事件中出现；回春的两张弃牌 ID、2–3 个目标和逐目标恢复证据也只属于拥有者 Prompt/可信宿主。苦肉在 1 点体力发动时保留主动技能帧，复用私有 `RescueDying` Prompt；获救后才继续摸牌，未获救则先完成死亡清理再闭合主动技能帧。
- 鬼才的 `Guicai` Prompt 只投影给当前判定前候选拥有者；判定牌和最终红/黑结果公开，替换牌只从拥有者自己的私有手牌 Choice 进入 `Processing`/`Judgment`，`JudgmentReplacementRequestedEvent`/`JudgmentReplacementResolvedEvent` 属于可信宿主记录。
- 乐不思蜀和兵粮寸断的使用牌进入目标公开 `Judgment` 区；目标在下回合摸牌前进入同一 `JudgmentFrame`。规则 v11 起，乐不思蜀非红桃跳过出牌阶段，兵粮寸断非梅花跳过摸牌阶段；规则 v18 起兵粮寸断通过 `GetCombatDistance` 限定距离 1，`IgnoresTrickDistance` 允许奇才豁免，目标即使在无懈链中打空手牌仍正常置入；v1–v17 保留“目标有手牌”和空手时跳过效果的历史语义，v1–v10 另保留历史红黑判定。延时牌和判定牌的移动、无懈窗口、鬼才改判、多个延时效果累计及死亡清理均由 Core 统一提交，普通视图不携带其他玩家的私有手牌候选。
- 规则 v20 将出牌阶段饮酒记录为当前行动者的回合内标记；直接杀声明仍消费公开酒效，但不会清除该限次标记，直到该角色下一回合 `BeginTurn` 才重置。濒死自救不写入该出牌阶段标记。v1–v19 只检查当前是否仍有酒效，因此酒效被杀消费后可在同回合再次饮酒，供旧命令前缀确定性回放。
- 规则 v21 在经典身份局把英姿从自动摸牌 modifier 提升为摸牌阶段的私有发动/跳过 Prompt；`DrawSkillResolvedEvent` 公开是否发动及最终摸牌数，Checkpoint 仍由命令前缀重建暂停点。v1–v20 与非经典模式继续沿用自动多摸一张的历史语义。
- 规则 v22 在经典身份局的公开判定结果与判定牌收尾之间加入天妒 Prompt；发动时同一实体牌从 `Judgment(owner)` 进入手牌，跳过时按原路径进入弃牌堆，随后恢复八卦、延时牌或刚烈的父结算。`JudgmentCardClaimedEvent`、移动账本和命令前缀共同重建暂停点；v1–v21 不创建该窗口。
- 规则 v23 在经典身份局为反间增加目标侧四花色 Prompt；选花色前不投影周瑜手牌，答复后才从稳定排序的源手牌中确定性随机选择一张，经 `Processing` 转入目标手牌并公开。不同花色以普通伤害复用既有受伤触发、濒死和胜负链；v1–v22 不发布该主动技动作。
- 规则 v24 在经典身份局为观星增加准备阶段的私有多步 Prompt；先冻结牌堆顶至多五张实体牌，再分别记录“顶端先取”和“底端最深优先”的有序选择。完成时用 `CardZoneStore.ReorderDrawPileTop` 原子验证并重排同一区域，不生成跨区移动记录；`GuanxingResolvedEvent` 只公开数量。v1–v23 不发布该选择。
- 规则 v25 在经典身份局把护驾嵌入既有 `RespondDodge`/`ResponseWindowFrame`：曹操先发布发动 Choice，其他魏势力角色按行动顺序获得各自私有的闪、八卦阵或拒绝 Choice。实体响应牌从提供者手牌进入处理区再弃置，但 `CardRespondedEvent` 的有效 responder 为曹操；盟友八卦失败只推进护驾游标，不提前伤害曹操。全部失败后恢复同一个响应窗里的曹操自有闪、八卦阵与放弃选项。
- 规则 v26 在经典身份局把激将接入主动技能与既有 `RespondSlash`/`ResponseWindowFrame`：刘备出牌阶段选择目标后，或在决斗/南蛮入侵要求杀时，按行动顺序向其他蜀势力角色发布私有杀 Choice。提供者支付实体牌，`CardUsedEvent`/`CardRespondedEvent` 的有效 source/responder 仍为刘备；主动使用按刘备的距离、目标禁止与出杀次数校验，成功保留实体杀的属性牌型，全部失败不消耗次数并恢复原出牌/响应边界。
- 规则 v27 在经典身份局把救援作为濒死桃恢复量修正接入统一 `CardUseFrame → RecoveryFrame`：目标必须是拥有救援的主公孙权，来源必须是另一名吴势力角色，且调用来自濒死桃入口。命中时同一实体桃仍由提供者从手牌进入处理区和弃牌堆，恢复帧及 `RecoveryAppliedEvent` 的数量为 2，并额外发布 `JiuyuanAppliedEvent`；自救、非吴、酒和非濒死桃保持 1 点。
- 规则 v28 在经典身份局通过 `IPassiveSkill.CanUseAsDismantlement` 接入奇袭。合法动作可从拥有者手牌或装备区取得黑色实体牌，`CardUseFrame`、`NullificationWindowFrame`、`TargetCardSelectionFrame` 与用牌事件携带有效牌型 Dismantlement；`CardZoneStore` 仍按实际 Hand/Equipment 来源移动物理牌，避免把装备成本伪装成手牌或改写实体牌种。
- 规则 v29 在经典身份局通过 `IPassiveSkill.CanSkipDiscardPhase` 接入克己。引擎在回合开始清空杀标记，并只在拥有者自己的 Play 阶段记录直接使用、决斗/南蛮入侵响应及激将代出的有效杀；进入 Discard 后若标记仍为空，则发布私有 `DecisionKind.Keji` Choice。发动会发布 `PhaseSkillResolvedEvent` 并结束回合，跳过则继续普通手牌上限弃置。
- 规则 v30 在经典身份局通过 `IPassiveSkill.CanReplaceDrawPhase` 接入突袭。Draw 阶段冻结所有有手牌的其他存活角色，私有 Prompt 枚举一人/两人完整组合与普通摸牌；发动后从每名目标手牌中由确定性随机源选一张，经 `Hand(target) → Processing → Hand(source)` 移动。`HandCardsGainedBySkillEvent` 只公开目标座位和数量，牌面留在私有手牌与可信移动账本。
- 规则 v31 在经典身份局通过 `IPassiveSkill.CanReduceDrawPhase` 接入裸衣。Draw 阶段私有选择发动时少摸一张并设置仅本回合有效的标记；`AttackResolution` 分离原始 `CardUserSeat` 与本次实际 `SourceSeat`，使许褚自己使用的杀/决斗伤害通过 `DamageModifiedBySkillEvent` 从基础值 +1，而许褚在自己发起的决斗中应战失败时由对方造成的伤害不加成。决斗伤害双方修正同样以 rules v31 为界，v1–v30 保留旧回放。
- 规则 v32 在经典身份局通过 `IActiveSkill` 接入强袭。主动技草稿冻结 0–1 张手牌/装备区武器牌和一名攻击范围内的其他存活角色；空牌分支先失去 1 点体力，武器分支按 `来源区 → Processing → DiscardPile` 移动。`AttackResolution` 的来源牌/有效牌型允许为空，但 `SourceSkill=Qiangxi`，因此无实体牌来源的 1 点伤害仍统一进入 `DamageFrame`、伤害后技能和濒死链；若体力成本先令典韦濒死，主动技帧保留在濒死帧下方，获救后继续原目标伤害。rules v31 保留未开放强袭的旧回放。
- 规则 v33 在经典身份局通过 `IPassiveSkill.CanUseAsSupplyShortage` 和 `ModifySupplyShortageDistanceLimit` 接入断粮。徐晃自己的黑色基本牌或黑色装备牌可从手牌/装备区进入既有延时锦囊与无懈链，原生及转化兵粮寸断的距离上限为 2。`_judgmentEffectiveCardKinds` 只为判定区内的转化实体牌持久记录有效牌型：公开快照、同名延时牌去重、观星目的和判定结算读取兵粮寸断，牌区诊断、移动账本与最终弃置仍读取原实体牌；离开判定区即清理映射。rules v32 保留未开放断粮的旧回放。
- 规则 v34 在经典身份局接入甄姬的洛神与倾国。准备阶段先发布仅本人可见的 `DecisionKind.Luoshen`；每次选择发动都进入既有 `JudgmentFrame`，可由鬼才替换，黑色生效牌通过 `skill.luoshen.claim-judgment` 进入甄姬手牌并再次询问，红色牌进入弃牌堆并结束链。倾国复用 `IPassiveSkill.CanUseAsResponse`，只把甄姬自己的黑色手牌作为有效闪，移动账本继续记录原实体牌；rules v33 保留无甄姬、无洛神 Choice、无倾国转化的旧回放。
- 规则 v35 在经典身份局接入黄月英的集智。普通锦囊完成声明后，引擎先保存原 `CardUseFrame` 或正在进行的 `NullificationWindowFrame` 游标，再向拥有者发布私有 `DecisionKind.Jizhi`；发动时以 `skill.jizhi.draw` 从牌堆摸一张并发布 `DrawSkillResolvedEvent`，随后从原处进入无懈询问或继续反制链。无懈可击本身属于普通锦囊，延时锦囊不触发；rules v34 保留无黄月英、无集智 Choice 的旧回放。
- 规则 v36 在经典身份局接入马超的铁骑。杀完成目标声明后、仁王盾与闪响应前，引擎保留当前 `CardUseFrame` 并向攻击者发布私有 `DecisionKind.Tieqi`；发动时以攻击者为判定目标压入共享 `JudgmentFrame`，因此鬼才可在结果生效前替换。红色结果在当前 `AttackResolution` 上记录禁止闪响应，实体闪、倾国、八卦阵和护驾均不再发布；黑色结果或跳过从同一杀帧继续普通响应。rules v35 保留无马超、无铁骑 Choice 的旧回放。
- 规则 v37 在经典身份局接入黄忠的烈弓。仅在出牌阶段用杀指定目标后，引擎读取公开目标手牌数、黄忠当前体力与统一攻击范围；目标手牌数不小于黄忠体力值或不大于攻击范围时，保留当前 `CardUseFrame` 并向黄忠发布私有 `DecisionKind.Liegong`。发动在同一 `AttackResolution` 上记录禁止闪响应，实体闪、倾国、八卦阵和护驾均不再发布；跳过或双条件均不满足时继续普通响应。rules v36 保留无黄忠、无烈弓 Choice 的旧回放。
- 规则 v38 在经典身份局接入魏延的旧版狂骨。统一伤害后候选以 `DamageTriggerScope.DamageSource` 找到实际伤害来源，再读取来源到受伤目标的公开战斗距离；距离不大于 1 且魏延已受伤时，以高优先级锁定候选在同一伤害窗内按实际伤害点数压入 `RecoveryFrame`，并以体力上限截断。`KuangguRecoveredEvent` 记录伤害帧、来源、目标、伤害量与实际回复量；rules v37 保留无魏延、无狂骨恢复的旧回放。
- 规则 v39 在经典身份局接入吕布的无双。`IPassiveSkill.ModifyRequiredResponseCount` 只修改公开的连续响应数量；杀来源拥有无双时，目标的每张闪分别进入独立 `ResponseWindowFrame`，决斗则按当前对手是否拥有无双决定本轮 responder 需依次打出一张或两张杀。实体响应、倾国/龙胆、八卦阵、护驾与激将仍各自复用原移动和续接链，`RequiredResponseProgressEvent` 只公开已完成/所需次数；rules v38 保留单次闪/杀响应。
- 规则 v40 在经典身份局补齐关羽武圣的“红色牌”区域语义。合法动作和杀响应在原手牌候选之外，仅为武圣追加自己的红色装备牌；主动杀、决斗/南蛮响应与激将提供牌都通过 `FindOwnedCardLocation` 从实际 `Hand` 或 `Equipment` 进入 `Processing`，事件继续发布有效 `Slash` 而移动账本保留实体牌型。rules v39 保留仅手牌候选。
- 规则 v41 在经典身份局补齐华佗急救的“红色牌”区域语义。濒死桃候选在原手牌之外，仅为回合外的急救拥有者追加自己的红色装备；统一恢复牌结算通过 `FindOwnedCardLocation` 从实际 `Hand` 或 `Equipment` 进入 `Processing`，`DyingResponseEvent` 继续记录有效桃与原物理牌型。rules v40 与非经典演示模式保留仅手牌候选。
- 规则 v42 在经典身份局接入借刀杀人。父 `CardUseFrame` 冻结“持武器者、其攻击范围内被杀目标”的有序座位对，并在集智/无懈完成后向持武器者发布私有 `DecisionKind.BorrowedSword`；选择实体杀、武圣转化牌或激将时，`BorrowedSwordResolution` 保留父帧上下文并压入一条真实的子杀结算链，闪、防具、伤害、濒死及伤害后技能全部完成后才恢复父锦囊。拒绝或无法出杀时，当前武器按 `Equipment(owner) → Processing → Hand(source)` 交给使用者。装备武器自身被武圣当杀支付时不再贡献该杀的攻击范围；强制杀不消耗当前回合角色的通常出杀次数，烈弓也只在技能拥有者自己的出牌阶段开放。rules v41 保留不发布借刀杀人动作的旧回放。
- `revealAll: true` 只用于本地开发者视图和测试，宿主可通过独立的 `GameEngine.Seed` 记录回放种子。

完整牌区位置只能通过明确标为可信宿主诊断的 `CreateCardZoneDiagnostics()` 取得。它不能进入玩家网络 DTO。

Core 保留三个基础响应兼容入口；当前 WPF 已全部迁移到命令边界：

```csharp
game.HumanPlay(cardId, targetSeat);
game.HumanEndPlay();
game.HumanRespond(useDodge: true);
```

K2 的统一入口使用单一 Revision 和完整的 Prompt Choice：

```csharp
game.Submit(new PlayCardCommand(
    ActorSeat: 0,
    CardId: cardId,
    TargetSeats: [targetSeat],
    ExpectedRevision: game.Revision,
    PromptId: game.PendingDecision!.PromptId));

game.Submit(new SelectGeneralCommand(
    ActorSeat: 0,
    GeneralId: game.PendingDecision!.Choices[0].ContentIds[0],
    ExpectedRevision: game.Revision,
    PromptId: game.PendingDecision!.PromptId));
```

主动技能也通过同一命令边界提交；当前内置主动技能中，`苦肉` 使用无牌、无目标动作，`制衡` 在规则 v17 的经典身份局使用当前拥有者私有手牌与公开装备 ID 的多选动作（v1–v16 和演示模式仍只选手牌），`仁德` 使用私有手牌多选和其他存活角色目标选择，`青囊` 使用私有一张手牌和受伤存活角色目标选择，`回春` 使用私有两张手牌和 2–3 名受伤存活角色目标选择。牌/目标集合、数量边界和 Prompt 由 Core 发布并在提交前统一校验；苦肉的单体濒死续接已复用既有救援窗口，更复杂多目标和多效果主动技能仍沿同一契约预留：

```csharp
game.Submit(new UseSkillCommand(
    ActorSeat: 0,
    Skill: SkillKind.Kujin,
    CardIds: [],
    TargetSeats: [],
    ExpectedRevision: game.Revision,
    PromptId: game.PendingDecision!.PromptId));
```

`CommandResult` 对玩家输入错误返回稳定 `CommandErrorCode`。出牌询问中的每个 Choice 已经把牌和目标绑定，回答询问还必须匹配 Responder、PromptId、ChoiceId 和 Revision；被拒绝的命令不改变快照、日志、牌区或随机数。

回合末 `DiscardCards` 是准确子集选择：私有 Prompt 发布当前手牌 ID 和 `RequiredCardCount`，不枚举 `Choices` 组合。`DiscardCardsCommand` 携带准确张数的不同实体牌 ID、ActorSeat、PromptId 和 Revision；Core 在任何修改前验证所有权、候选集合及当前手牌上限，再以卡牌 ID 顺序统一移入弃牌堆。默认 `UseInteractiveDiscard = true`，只有活着的人类超限时暂停；AI 和显式关闭该选项的演示局保留自动弃牌。成功的手动弃牌停在下一回合，等待宿主继续推进。

`HandLimitDiscardedEvent` 的准确牌 ID 仅属于可信宿主，普通玩家日志只显示数量。命令日志复制并冻结传入的选牌集合和目标集合。为拒绝旧回合末自动推进语义，Checkpoint SchemaVersion 为 3。

WPF 使用 `AdvanceOneStepCommand` 记录每个 AI/结算步骤，并以 `AdvanceAfterHumanCommands = false` 保持逐步行动。响应 `ChoiceId` 显式通过 JSON 构造函数恢复，避免只读结构体使用默认值丢失候选 ID。全部界面动作都经过命令边界后，由独立 `FileGameSaveStore` 保存自动/手动两个槽位；关闭窗口前刷新待保存操作。恢复在独立引擎上完成全部验证后再替换当前引擎，详见 [`SAVE_AND_RESUME.md`](SAVE_AND_RESUME.md)。

`GetHumanLegalActions()` 是服务端式合法性检查。界面高亮仅提供提示；`HumanPlay` 会重新从当前状态生成合法动作并再次验证，因此不能靠伪造按钮参数越过规则。

## 3. 推进和暂停

`GameEngine` 是同步、显式状态机：

```text
NotStarted
    ↓ Start
Setup → Pending General Selection → Reveal Generals → Round-robin Deal
    ↓
Draw → Play ──────────────┐
        │                 │ HumanPlay 后仍可继续出牌
        ├─ Pending Play ──┘
        ├─ Slash / FireSlash / ThunderSlash → Pending Dodge → Damage / Dodged
        ├─ Duel → 交替 Pending Slash → Damage
       ├─ DrawTwo → 无目标 CardUse → Draw
       ├─ Alcohol → 无目标 CardUse → 公开酒效（下一张直接杀 +1）
       ├─ Equip → Hand → Processing → Equipment（同槽旧装备进入弃牌堆）
        ├─ DamageSkill → Feedback Prompt → 发动：DamageCardClaimedEvent / Processing → Hand；跳过：Processing → DiscardPile
       ├─ DamageSkill → Yiji Prompt → 摸两张：DrawPile → Hand(owner)；交牌：Hand(owner) → Hand(other target)；跳过：两张牌留在 owner Hand
       ├─ DamageSkill → Yuanhu Prompt → 发动：Hand(owner) → Processing → DiscardPile；RecoveryFrame → 固定受伤目标回复 1 点；跳过：不移动牌
       ├─ ActiveSkill → ActiveSkillFrame → 类型化体力变化/技能摸牌 → close
       ├─ BarbarianAssault / ArrowBarrage → TargetIndex → 逐目标 Pending Slash / Dodge → Damage / Dying
        ├─ PeachGarden → TargetIndex → 逐目标 RecoveryFrame → RecoveryApplied
        ├─ FiveGrains → TargetIndex → PublicRevealedCards → 私有 SelectHarvestCard → Hand
        ├─ Indulgence/SupplyShortage → 目标 Judgment → 下回合摸牌前 JudgmentFrame → 乐不思蜀非红桃跳过出牌 / 兵粮寸断非梅花跳过摸牌
        └─ EndPlay
             ↓
          Discard → Finished → 下一存活玩家的 Draw
```

`Advance()` 连续运行 AI，直到遇到真人输入或游戏结束。`AdvanceOneStep()` 只处理一个阶段变化或一次 AI 决策，供调试界面观察。两者和旧人类 API 都通过公共提交边界产生 Revision。

这与旧式引擎“阻塞一个房间线程等待客户端”不同：暂停由 `PendingDecision` 表达，进程中没有锁住的等待线程。若未来联网，服务器可以序列化当前快照，稍后收到回复再恢复。

可信宿主的持久化边界由 `GameCheckpoint` 单独承担：它保存选项、已接受命令前缀、Revision、模式 ID、内容包版本签名、规范化 SHA-256 内容指纹和规则行为版本，`GameReplay.Restore` 仍逐条经过普通命令校验。它可以恢复处于私有 Prompt 的命令驱动暂停点，并拒绝相同包版本下的内容定义漂移；缺少规则版本字段的旧 JSON 按 v1 恢复，以保留既有事件语义。它不是玩家快照；若状态曾由旧兼容 API 直接推进，捕获会显式失败。

## 4. 卡牌结算

一次杀/决斗的主要调用链：

```text
HumanPlay / AI ChoosePlay
      → BuildLegalActions（当前状态下重新验证）
      → ExecuteAction（普通牌或主动技能）
      → ResolveSlash
      → 杀从使用者 Hand 移入 Processing
      → 记录 CardUsed，通知所有 AI 观察公开行为
      → 有闪：真人生成 PendingDecision；AI 进入下一步 ChooseDodge
          → 闪从 Hand 移入 Processing，再进入 DiscardPile
      → 无闪或放弃：ApplySlashDamage
           → 直接杀：若有酒效则体力 -2，并在伤害声明时消费酒效；其他攻击仍按自身伤害规则
          → 普通杀/火杀/雷杀分别标记 DamageNature.Normal/Fire/Thunder
           → DamageSkillContext 与 DamageRequested/Applied/AfterDamage 保持同一伤害类型和实际金额
          → DamageSkillContext 询问被动技能
              → 存在候选：压入 DamageTriggerWindowFrame，CandidateIndex 可在候选间推进；规则版本 5 即使目标降至 0 点体力也先完成该窗口，v1–v4 回放保留历史顺序
           → 可选技能：压入 DamageSkillFrame，拥有者私有选择发动/跳过
              → Feedback 发动：取得同一实体牌并发布 DamageSkillResolvedEvent / DamageCardClaimedEvent
              → Yiji 发动：先摸两张牌，再将一张合法牌移给其他存活目标并发布 DamageSkillCardGivenEvent
              → Guicai 判定前发动：旧判定牌结束；新手牌经 Hand → Processing → Judgment(target)，再按同一 JudgmentFrame 继续
              → 跳过：Feedback 的伤害牌进入 DiscardPile；Yiji 的两张牌留在拥有者手牌；父攻击/群体帧继续
         → 体力为 0：DyingFrame
              → 按固定座次逐一发布私有桃候选
              → AI 或真人提交救援/放弃
              → 救援成功：恢复至 1 点体力，继续父结算
              → 全部放弃：DeathFrame、公开身份、奖惩、EvaluateWinner
     → FinishAttack：杀从 Processing 进入 DiscardPile，或被奸雄移入手牌
                     反馈取得时，父攻击/群体帧继续引用已转入目标手牌的同一实体牌；遗计赠牌完成后，父攻击/群体帧沿触发游标继续
```

桃园结义沿用同一父级 `CardUseFrame`，在使用时锁定所有仍存活的座位（含使用者），然后由 `TargetIndex` 逐步推进。每次 `AdvanceOneStep()` 最多处理一个目标：受伤目标短暂压入 `RecoveryFrame` 并发布 `RecoveryAppliedEvent`，满血目标只推进游标；父牌在所有目标完成前停留于 `Processing`，普通玩家快照只看到处理区数量和已经公开的体力结果。

五谷丰登复用同一父级和目标游标，但把牌面可见性显式建模为 `GameSnapshot.PublicRevealedCards`。使用时每个锁定的存活角色对应一张 `DrawPile → Processing` 的公开牌；当前 picker 收到私有 `SelectHarvestCard` prompt，选中牌从 `Processing` 进入自己的手牌，完成全部 picker 后父牌才进入弃牌堆。AI 只调用 `ChooseHarvestCard(view, options, ...)`，其中 `view` 是本座玩家视角、`options` 是公共展示牌，不读取引擎牌区或其他玩家手牌。

火攻使用独立的两段私有选择而不是复用五谷丰登的公共 draft：目标先在自己的快照中选择一张手牌。规则 v19 起实体牌继续位于 `Hand(target)`，但通过 `FireAttackCardRevealedEvent` 和 `PublicRevealedCards` 临时公开牌面；攻击者再从自己的同花色手牌中选择弃牌或跳过，自选目标时展示牌本身也可支付该弃牌成本。成功弃牌时同一 `CardUseFrame` 转为 `AttackResolution`，由 `DamageNature.Fire` 进入既有伤害/技能/濒死链；跳过或无同花色牌只清除公开展示状态并结束父牌。v1–v18 保留只能选择其他角色、展示牌 `Hand(target) → Processing → DiscardPile` 的历史路径。两个 AI 选择器只接收本座快照与宿主已经发布的候选 ID，普通观察者既看不到未展示牌面，也看不到私有候选 Prompt。

过河拆桥和顺手牵羊复用单目标 `CardUseFrame` 与分层目标牌选择：规则版本 4 的隐藏手牌分支先压入 `TargetCardSelectionFrame`，只向来源玩家发布带不透明 ordinal 牌位的私有 `DecisionKind.SelectTargetCard` Prompt；目标和普通观察者看不到牌面或实体 ID。来源玩家确认后，前者把目标牌从 `Hand(target)`、`Equipment(target)` 或 `Judgment(target)` 经过 `Processing` 后送入弃牌堆，后者把目标牌从这三个来源之一经过 `Processing` 后送入 `Hand(source)`；规则版本 1–3 回放保留历史确定性盲选语义。顺手牵羊的合法性由 `GetCombatDistance(sourceSeat, targetSeat) == 1`、存活、非自身和目标有手牌、装备或判定区牌共同决定；战斗距离按存活座位环计算，并叠加进攻/防御坐骑 modifier。隐藏手牌效果只投影脱敏的类型化结果事件，公开装备/判定区事件可携带其已公开实体 ID/牌种，可信移动账本始终保留物理牌 ID。

`CardZoneStore` 维护 DrawPile、Hand、Processing、DiscardPile、Equipment、Judgment 和 OutsideGame。单张和批量移动都先验证真实来源、目标区和重复 ID，再统一生效；引擎在每次公共操作返回前校验 90 张实体牌唯一且守恒。移动账本使用类型化 `CardMoveReason`，可供测试和后续录像使用。武圣与龙胆的牌转化不创建副本：`LegalAction.PlayedCardKind`/`PlayCardCommand.PlayedCardKind` 描述出牌时的有效牌型，响应 Prompt 的 `response-card-kind` 与 `CardRespondedEvent.EffectiveCardKind` 描述响应有效牌型，`CardMovementRecord.CardKind` 仍描述同一实体牌的物理牌面。

引擎保持所有规则状态修改集中在受控入口。技能只回答“小型规则问题”，例如摸牌数是否增加、能否成为杀的目标、伤害牌是否被取得。这样不会让某个技能脚本悄悄绕过死亡和胜负检查。当前 `DamageNature` 只表达普通、火焰和雷电三种伤害来源；铁索连环已通过公开状态标记和类型化传导事件接入，属性抗性、属性转化和更复杂的多伤害嵌套仍未开放。

## 5. 内容注册与阶段边界

当前内容同时提供兼容投影和正式 Registry：

- `CardCatalog` 为杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻、乐不思蜀、兵粮寸断、闪电、无懈可击、铁索连环、借刀杀人和七种装备牌（含仁王盾）提供显示文案、类别、AI 使用价值和自动弃牌保留价值；`Card.DisplayName` 与 WPF 卡面都从这里读取，避免 UI 再维护一份牌面语义。
- `StandardDeckCatalog.BasicDemo` 描述 18 张杀、2 张火杀、2 张雷杀、18 张闪、10 张桃、2 张酒、4 张决斗、2 张无中生有、2 张南蛮入侵、2 张万箭齐发、2 张桃园结义、2 张五谷丰登、2 张过河拆桥、2 张顺手牵羊、2 张火攻、2 张乐不思蜀、2 张兵粮寸断、2 张闪电、2 张无懈可击、2 张铁索连环、2 张诸葛连弩、1 张八卦阵、1 张青釭剑、1 张赤兔、1 张绝影和 1 张玉玺、1 张仁王盾组成的 90 张演示牌堆、初始手牌数 4 和每回合摸牌数 2；玉玺可使持有者额外摸一张，青釭剑使直接杀无视目标防具，规则 v14 的仁王盾在黑色杀指定目标后令其无效，`GameEngine` 只消费这些配置并负责洗牌、移动和结算。
- `classic:standard-deck` 从经典包 1.23.0 起在 `BasicDemo` 配方上增加两张 `classic:borrowed-sword`，共 92 张；经典模式定义显式引用该牌堆。恢复包 1.22.0 及更早内容时，模式仍引用原 90 张 `standard:basic-demo`，因此旧内容指纹、发牌和同种子回放不被新牌静默改变。
- 牌的顺序生成仍然使用稳定的 id、花色和点数规则，再交给现有确定性随机数洗牌，因此相同种子仍可复现。
- `CardGame.Content.Standard/StandardContentPackage.cs` 注册 `standard:*` 卡牌、技能、武将、牌堆和 `identity:standard-8` 模式定义；`ContentRegistry.Build()` 会按依赖拓扑顺序注册，并拒绝重复 ID、缺失引用、版本不足和依赖环。
- `GameEngine.CreateStandard(options, registry)` 消费 Registry 中的 `standard:basic-demo` 牌堆配方，运行时仍通过 `CardKind`/`SkillKind` 兼容映射，避免一次性破坏旧 WPF API；不传 Registry 时保留 Core 内置兼容路径。
- `ContentModeDefinition` 还提供角色分布、牌堆 ID、候选数量和武将池；开启 `UseInteractiveSetup` 后，模式开局通过私有 `SelectGeneralCommand` 暂停，所有候选均由 Core 校验，完成后才洗牌并逐轮发牌。
- `standard-active-skills@1.0.0` 是依赖 `standard@1.11.0` 的可选扩展包，增加 `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、`standard:mashu`、`standard:qicai`、七个技能演示武将及 `identity:active-skills-8/5`；其上另有依赖主动技能包的 `standard-rescue-skills@1.0.0`，增加 `standard:jijiu`/急救者并扩展同一模式武将池；`StandardContentRegistry.Create()` 和不含救援层的 `CreateWithActiveSkills()` 保持原内容指纹不变，WPF 默认窗口显式选择包含救援层的 Registry。
- `standard-classic-generals@1.1.0` 在 1.0.0 的正式多技能武将层上增加 `classic:guo-jia` 与 `classic:tiandu`，并保留遗计作为第二技能；恢复 1.0.0 存档时仍构造旧武将池和旧包指纹，不把新版郭嘉静默写入历史命令前缀。
- `standard-classic-generals@1.23.0` 在既有正式武将层中注册 `classic:borrowed-sword` 与 `classic:standard-deck`；1.22.0 仍用 `classic:guan-yu` / `classic:wusheng` 替换经典池的 Standard 占位关羽但保留 90 张牌堆，1.21.0 保留 `standard:guan-yu`，1.20.0 保留 `standard:zhao-yun`，1.19.0 保留 `standard:zhang-fei` 且不注册对应新经典 ID，1.18.0 保留不含吕布的武将池，1.17.0 保留不含魏延的武将池，1.16.0 保留不含黄忠的武将池，1.15.0 保留不含马超的武将池，1.14.0 保留不含黄月英的武将池，1.13.0 保留不含甄姬的武将池，1.12.0 保留不含徐晃的武将池，1.11.0 保留不含典韦的武将池，1.10.0 保留不含许褚的武将池，1.9.0 保留不含张辽的武将池，1.8.0 保留不含吕蒙的武将池，1.7.0 保留不含甘宁的武将池，1.6.0 保留不含黄盖的武将池，1.5.0 保留不含救援的孙权，1.4.0 再保留不含激将的刘备，1.3.0 继续使用旧 `standard:cao-cao`，更早版本按历史边界恢复诸葛亮、周瑜与郭嘉；二十四个版本分别保留原武将池、卡牌/牌堆定义和内容指纹。
- K5 切片把决斗的交替 `RespondSlash` 响应、无中生有的无目标摸牌、酒的一次性直接杀 +1 伤害、濒死窗口所有 responder 的桃救援与 victim 的酒自救、急救红牌当桃、南蛮入侵/万箭齐发的群体逐目标 `RespondSlash`/`RespondDodge`、桃园结义的群体逐目标恢复、普通/火/雷杀的 `DamageNature`、反馈/遗计/节命伤害后技能和单次杀/决斗/群体牌伤害后的基础濒死窗口接入同一帧栈：`CardUseFrame.TargetIndex` 保存当前群体目标，`GroupResponseEvent` 记录必需响应牌种类，`DamageFrame` 保存伤害类型和实际金额，`AlcoholAppliedEvent`/`AlcoholExpiredEvent` 表示酒效生效/回合结束失效，`RecoveryFrame` 记录桃园结义当前恢复子帧，`DyingFrame` 逐个询问可用桃/酒/红牌的 responder；桃、酒和转化牌通常从 responder 自己的手牌移动到 `Processing`，rules v41 的经典急救转化牌也可从自己的装备区支付。规则 v12 起酒仅允许 victim 自救，v3–v11 保留跨座位救援，v1/v2 仍按原仅自救语义，急救事件同时记录有效 Peach 与物理牌型，节命通过私有目标 Choice 和 `DamageSkillCardsDrawnEvent.TargetSeat` 补牌至目标体力上限，其他视角只看到公开结算结果。

当前已开放十五张最小锦囊 `standard:duel`、`standard:draw_two`、`standard:barbarian_assault`、`standard:arrow_barrage`、`standard:peach_garden`、`standard:five_grains`、`standard:dismantlement`、`standard:snatch`、`standard:fire_attack`、`standard:indulgence`、`standard:supply_shortage`、`standard:lightning`、`standard:nullification`、`standard:iron_chain` 与 `classic:borrowed-sword`，酒作为第六种基本牌复用无目标 `CardUseFrame` 并提供一次性直接杀加伤，四张群体/多目标牌共用目标游标，其中两张响应牌共用通用响应事件，五谷丰登复用公开翻牌和私有选牌事件，过河拆桥和顺手牵羊复用单目标 `CardUseFrame` 与“隐藏手牌不透明牌位选择/公开装备或判定区牌精确选择”的目标牌移动入口；规则版本 4 使用 `TargetCardSelectionFrame` 固化私有暂停点，`TargetCardSelectionRequestedEvent` 只公开候选数量，规则版本 1–3 回放保留历史盲选语义，火攻复用单目标父帧并增加私有展示/同花色弃牌两段 Prompt，乐不思蜀、兵粮寸断和闪电把延时牌置入目标公开判定区并在其下回合摸牌前复用 `JudgmentFrame`，规则 v11 起前者非红桃跳过出牌阶段、兵粮寸断非梅花跳过摸牌阶段，v1–v10 保留历史红黑语义，闪电黑桃 2 至 9 命中 3 点雷电伤害否则转移；无懈可击在锦囊效果前复用固定座次私有响应和有限多层 `NullificationWindowFrame`，铁索连环通过精确一/二目标和公开 `IsChained` 标记接入火/雷伤害同额传导；借刀杀人通过有序双目标与 `BorrowedSwordResolution` 将持武器者的私有出杀/交武器选择接入真实子杀链；普通/火/雷杀共用杀路径，属性类型和实际伤害金额写入伤害帧与事件；K6 已开放五个装备槽位、七种装备牌（含仁王盾）的生命周期、同槽替换、死亡清理、基础战斗距离/攻击范围查询以及诸葛连弩、青釭剑、赤兔、绝影、玉玺、仁王盾和马术的基础 modifier；规则 v13 起诸葛连弩/青釭剑分别使用牌面攻击范围 1/2，v1–v12 保留旧 +1/默认 1 的回放数值；规则 v14 起八卦阵覆盖直接杀及万箭齐发的闪响应，仁王盾在黑色杀指定目标后通过 `ArmorEffectAppliedEvent` 令其无效，v1–v13 保留旧时机；奇才通过 `GetLegalActions` 统一放宽距离型锦囊目标，K7 已为八卦阵、乐不思蜀、兵粮寸断和闪电接入 `JudgmentFrame`、判定区移动和对应结果，鬼才复用同一判定帧。其余锦囊和复杂装备效果仍需各自的目标、响应窗口和处理区语义。明确扩展需求记录在 [`CONTENT_BACKLOG.md`](CONTENT_BACKLOG.md)，在对应类型化入口开放前不通过 UI 或 `GameEngine` 特判接入。

## 6. 技能扩展

当前最小接口：

```csharp
public interface IPassiveSkill
{
    int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => currentCount;
    int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => currentLimit;
    int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) => currentDistance;
    bool ProhibitsSlashTarget(PlayerSkillContext owner) => false;
    bool CanUseAsSlash(PlayerSkillContext owner, Card card) => false;
    bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) => false;
    bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => false;
    bool CanClaimResolvedJudgment(JudgmentSkillContext context) => false;
    bool OffersDamageCardChoice(DamageSkillContext context) => false;
    bool ClaimsDamageCard(DamageSkillContext context) => false;
    DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) => DamageSkillEffectKind.None;
}
```

主动技能使用独立的数据型接口，不让内容实现直接触碰引擎私有状态：

```csharp
public interface IActiveSkill
{
    SkillKind Kind { get; }
    string Name { get; }
    bool CanUse(ActiveSkillContext context) => false;
    ActiveSkillEffect GetEffect(ActiveSkillContext context) => new(ActiveSkillEffectKind.None);
}
```

三十二个被动技能通过查询、修正或触发扩展点接入；主动技能另有独立入口：

- 英姿：旧规则直接修改摸牌数；规则 v21 的经典身份局在摸牌阶段发布可暂停选择，并在确认后继续同一回合；
- 天妒：规则 v22 的经典身份局在拥有者自己的判定牌生效后发布可暂停选择，决定该牌进入手牌还是按通常流程弃置；
- 反间：规则 v23 的经典身份局通过目标型主动技能选择一名其他角色，再由目标先选花色；随机牌转移并公开后，花色不同时进入 1 点普通伤害链；
- 观星：规则 v24 的经典身份局在延时牌判定和摸牌之前发布发动/跳过 Choice；发动后牌面、花色、点数和排序步骤仅对技能拥有者可见，完成后只公开观看/置顶/置底数量；
- 护驾：规则 v25 的经典主公曹操在闪响应窗中可发起有序魏势力询问；候选只看到自己的实体闪/转化牌与公开八卦阵，成功牌的物理拥有者和有效响应者分开记录；
- 激将：规则 v26 的经典主公刘备可在出牌阶段或杀响应窗发起有序蜀势力询问；每个候选只看到自己的实体杀/转化牌，提供者与有效使用者/响应者分开记录，主动失败保留人类重试能力；
- 救援：规则 v27 的经典主公孙权在濒死时，由另一名吴势力角色使用的桃回复量为 2；技能无新 Prompt，复用既有私有救援选择、实体牌移动和恢复事件，并用专用事件公开加成已生效；
- 奇袭：规则 v28 的经典甘宁可把手牌或装备区的黑色实体牌按过河拆桥使用；合法目标、无懈与目标牌选择复用有效牌型，移动账本保留原牌型和来源区；
- 克己：规则 v29 的经典吕蒙在 Play 阶段未使用或打出杀时，于 Discard 阶段得到私有发动/跳过 Choice；发动后跳过弃牌并结束回合；
- 突袭：规则 v30 的经典张辽可在 Draw 阶段选择一至两名有手牌的其他角色，以随机暗手牌转移替代整段普通摸牌；
- 裸衣：规则 v31 的经典许褚可在 Draw 阶段少摸一张，使本回合由自己使用的杀或决斗伤害 +1；决斗反向伤害按实际来源结算且不受裸衣加成；
- 强袭：规则 v32 的经典典韦每个出牌阶段限一次，可失去 1 点体力或弃置一张手牌/装备区武器牌，对攻击范围内一名其他角色造成 1 点无实体牌来源的技能伤害；体力成本与目标伤害之间可暂停于共享濒死续接；
- 断粮：规则 v33 的经典徐晃可将手牌或装备区的黑色基本牌/装备牌当兵粮寸断使用，并把兵粮寸断距离上限修正为 2；判定区有效牌型与实体牌身份分别持久化；
- 洛神：规则 v34 的经典甄姬在准备阶段可反复选择进行判定，获得生效后的黑色判定牌，红色结果自动停止并弃置；
- 倾国：规则 v34 的经典甄姬可将一张黑色手牌作为闪使用或打出，实体牌型和有效响应牌型分别记录；
- 集智：规则 v35 的经典黄月英使用普通锦囊牌后可私有选择摸一张牌；选择完成后恢复同一锦囊的无懈询问或反制游标，无懈可击自身也触发，延时锦囊不触发；
- 铁骑：规则 v36 的经典马超使用杀指定目标后可私有选择进行判定；共享判定帧允许鬼才替换，红色结果在当前杀结算上禁止目标使用闪，黑色结果或跳过恢复普通闪响应；
- 烈弓：规则 v37 的经典黄忠于出牌阶段使用杀指定目标后，若目标公开手牌数不小于黄忠当前体力值或不大于黄忠攻击范围，可私有选择令目标不能使用闪；跳过或双条件均不满足时恢复普通闪响应；
- 狂骨：规则 v38 的经典魏延作为实际伤害来源对距离 1 以内角色造成伤害后，锁定按伤害点数回复体力；满体力不创建空恢复，rules v37 不触发；
- 无双：规则 v39 的经典吕布使用杀时目标需依次完成两次闪响应；吕布使用或成为目标的决斗中，另一方每轮需依次完成两次杀响应。第一张不能单独抵消或交替回合，rules v38 保留单次响应；
- 咆哮：修改一回合使用杀的上限；
- 空城：添加目标禁止条件；
- 奸雄：在伤害完成后改变造成伤害的卡牌去向。
- 武圣：规则 v40 的经典关羽允许手牌或装备区红色非杀实体牌生成有效牌型为杀的主动动作与响应 Choice，并从实际来源区支付；rules v39 保留仅手牌候选；
- 急救：规则 v41 的经典华佗在回合外允许手牌或装备区红色非桃实体牌生成有效桃救援 Choice，并从实际来源区支付；rules v40 与演示模式保留仅手牌候选；
- 龙胆：允许闪生成有效杀，杀/火杀/雷杀生成有效闪，并复用精确响应询问；
- 马术：将拥有者计算与其他角色的公开出攻距离减少 1，并由 Core 统一钳制最小距离；
- 奇才：让拥有者使用的锦囊牌忽略距离限制，由 `GetLegalActions` 统一发布扩大后的公开合法目标；
- 急救：仅在濒死响应窗口把红色非桃实体牌映射为有效桃，物理牌仍由引擎按原牌区生命周期移动并在事件中保留牌型；
- 反馈：目标存活且伤害牌仍在处理区时进入私有可选触发；发动后取得同一实体牌，并以类型化事件记录。
- 遗计：受伤后私有摸两张牌，从合法牌/目标组合中将一张牌交给其他存活角色，并以类型化事件和受限跨手牌移动记录。
- 节命：受伤后按公开手牌数量选择一名合法角色补牌至体力上限，并以私有 Prompt、类型化事件和牌堆到目标手牌的移动记录。
- 援护：其他角色受到正伤害后，援护者从自己的私有手牌中选择一张弃置，并以 `RecoveryFrame` 令固定受伤目标回复 1 点体力；弃牌、恢复和效果目标通过类型化事件记录。
- 刚烈：受伤后私有选择是否发动并公开判定；判定为红色时，向伤害来源发布仅其可见的精确两牌弃置/承受 1 点伤害 Choice，若反制伤害进入濒死则在救援完成后回到原触发游标。
- 鬼才：判定牌生效前由当前候选拥有者私有选择一张手牌替换或跳过；替换牌进入同一 `JudgmentFrame`，最终结果公开，AI 只使用自己的过滤快照。
- 苦肉：出牌阶段且体力大于 0 时发动，沿 `ActiveSkillFrame` 失去 1 点体力并摸两张牌；若降至 0 点，先在同一帧下压入私有 `DyingFrame`，救援成功后继续摸牌，失败后完成死亡清理再闭合主动技能。
- 制衡：规则 v17 的经典身份局每个出牌阶段限一次，从自己的手牌或公开装备区选择至少一张并逐张移入 `Processing`，弃置后摸等量牌；v1–v16 与演示模式保留手牌限定和可重复发动。暗手牌集合只在当前拥有者的私有 Prompt 与可信宿主事件中出现，装备候选保持公开。
- 乐不思蜀/兵粮寸断：将使用牌置入其他存活角色的公开判定区，在其下回合摸牌前按顺序进入 `JudgmentFrame`；规则 v11 起，乐不思蜀非红桃跳过出牌阶段，兵粮寸断非梅花跳过摸牌阶段，v1–v10 保留历史红黑语义；多个延时牌通过位标记累计效果，延时牌和判定牌均沿统一移动账本收尾。

下一步若要加入更复杂技能，建议在现有 `AfterDamage`、`PlayerDying` 和 `DyingResponse` 类型化事件及 `DamageTriggerWindowFrame` 游标上扩展完整状态 Checkpoint；当前 `DamageTriggerScope` 已把受伤者、其他存活角色和任意存活角色的座位关系变成可复用契约，`DamageTriggerOrdering` 仍固定优先级、相对行动者座次、技能序号和候选 ID 的排序键，而不是把顺序交给任意字符串或可变字典。遗计已经完成一条真实的跨座位牌效果，援护已复用通用的其他存活角色范围，刚烈已经完成一条受伤者触发后定向伤害来源询问的效果，苦肉也已完成单体主动技能濒死续接；多目标主动技能、多伤害嵌套和复杂技能濒死响应尚未宣称完成。

## 7. 提交边界与宿主通知

规则执行过程中不再直接调用 WPF 或其他外部订阅者。一次公共操作的顺序是：

```text
执行状态转换
→ 累计 Log / AiThought / CardMoved
→ 校验牌区与 pending resolution 不变量
→ 固化返回结果
→ 仍在重入保护内逐订阅者派发
→ 返回宿主
```

日志、AI 思考和卡牌移动按产生顺序派发；同一公共操作内多次 `PublishState` 合并为最终 `StateChanged` 快照。某个订阅者抛异常时，异常被写入最多保留 128 条的 `ObserverFailures` 可信诊断列表，后续订阅者仍会收到通知，对局状态、随机数、日志和 AI 选择均不受影响。通知派发期间尝试再次调用改变状态的 API 会被统一拒绝。

这一步解决的是“外部观察者打断半次规则操作”。K2 已把 Revision、PromptId 和精确 Choice 纳入统一命令边界；K3 又把内容注册与依赖校验隔离出来；K4 增加了可暂停选将和开局事件；K5 已把现有杀/火杀/雷杀/闪/伤害/桃链路接入可序列化的 `ResolutionStack` 与类型化结算事件，伤害类型在帧和事件之间保持一致；K7 又把可抵消锦囊的有限多层响应接入独立的 `NullificationWindowFrame` 和私有 Prompt。当前事件仍主要用于可信宿主观察/录像投影，完整 EventBatch、通用触发时机、多伤害嵌套和更复杂的响应链仍待后续 K5/K6/K7 切片。

## 8. AI

新 WPF 牌局显式使用 `GameOptions.AiPolicyVersion = 3`，存档记录该版本；旧 JSON 缺少字段时为 1，读取和再次保存均不自动升级。v3 保留 v2 的评分和随机调用顺序，并把身份局火攻接入公开攻击观察入口；v1/v2 的历史命令日志和 Checkpoint 继续按原行为重放。升级前的三场完整牌局与一个 WPF 私有询问样本逐事件校验兼容性。未知版本在创建引擎时拒绝。

v2/v3 的战术覆盖位于 `SimpleAi.Tactical.cs`：群伤/回复净收益、合法酒杀接续、保命牌转化成本、基于公开行为的有限阵营置信度、已公开阵亡身份推断、救援与辅助技能目标；`PublicAttackEvidence` 为公开攻击观察提供类型化且不含暗信息的输入，v3 另覆盖身份局火攻。无懈同时区分有益和有害效果。AI 仍只消费本座快照，具体行为、实战比较及边界见 [`TACTICAL_AI.md`](TACTICAL_AI.md)。

`SimpleAiBrain` 有意采用容易读懂的启发式评分，而不是搜索整棵博弈树：

```text
候选动作 = 与真人相同的 LegalAction 列表

桃分数 = 已损失体力 × 38 + 濒危奖励
杀分数 = 目标敌对值 + 低体力击杀奖励 + 受伤压力奖励
结束出牌 = 0
```

身份策略：

- 反贼优先攻击公开主公；
- 主公/忠臣优先攻击已公开反贼或反贼嫌疑高的人；
- 内奸在多人阶段保护主公并平衡阵营，最终单挑时才攻击主公；
- 攻击主公会提高行为者的“反贼嫌疑”，攻击已有反贼嫌疑的人会小幅降低嫌疑；角色阵亡并公开身份后，击杀反贼会降低击杀者嫌疑，击杀忠臣则会提高嫌疑。

关键约束是：AI 收到的是 `CreateSnapshot(aiSeat)`，不是内部 `PlayerRuntime`。它能看自己的身份和手牌，但看不到其他暗身份或暗牌。

每次选择都会生成 `AiThoughtRecord`，保存所有候选动作、得分、理由和最终选择。WPF 右侧的“AI 思考”面板在开发者视图中展示这些记录，因此算法不是黑盒，同时普通游玩模式不会被评分反向剧透身份或暗牌。

选将阶段使用独立的 `AiGeneralThought` 记录。AI 只获得当前座位的 `GameSnapshot` 和私有候选数组；候选 ID、评分和其他座位的选择只保留在可信宿主诊断中。

使用固定种子的小幅抖动只用于打破完全同分，既避免每次总选同一个座位，又能保证录像和测试可以复现。

## 9. 测试覆盖

Console 自测覆盖：

- 1/2/4/1 身份数量；
- 玩家视角不会泄露暗身份和手牌；
- 三个阵营的胜负条件；
- 三十二种被动技能钩子及八个主动技能（含反馈伤害后取牌、遗计跨手牌分配、节命目标补牌、援护跨座位弃牌恢复、刚烈判定与来源反制、鬼才判定替换、天妒取得判定牌、观星私有牌堆排序、护驾跨座位闪响应、激将跨座位杀使用/响应、救援恢复量修正、奇袭黑牌转化、克己跳过弃牌、突袭摸牌替换、裸衣少摸牌/伤害归因修正、断粮黑色基本牌/装备转化与兵粮距离修正、洛神连续判定/取得、倾国黑手牌响应转化、集智普通锦囊后摸牌/父结算续接、铁骑杀目标后判定/闪响应禁止、烈弓手牌条件/闪响应禁止、狂骨伤害后回复、无双连续响应、武圣牌转化、龙胆响应转化、马术距离修正、奇才锦囊距离豁免、急救红牌濒死转化、苦肉主动命令、制衡多选弃牌、仁德目标交牌、青囊弃牌恢复、回春多目标弃牌恢复、反间目标选花色/随机交牌与强袭可选武器成本/无牌伤害）；
- 相同种子得到相同初始状态；
- 真人出牌和打闪暂停点；
- 初始发牌、处理区生命周期、单张/批量移动原子性和每个公共边界的卡牌守恒；
- 提交后通知、订阅者异常隔离、重入拒绝和观察者不影响确定性结果；
- AI 单步只执行一个决策；
- 全 AI 对局可以结束并产生解释记录；
- 快照可以序列化为 JSON。
  - 当前 Core 自测共 175 项、WPF 自测共 53 项；除既有牌、技能、Checkpoint、装备、判定和视图回归外，还覆盖借刀杀人的精确有序目标、持武器者私有响应、真实子杀/交武器分支、激将提供者、在途与完成回放、rules v41 兼容，以及集智、护驾、观星、天妒、反间、急救、国战和完整 UI 命令对局等既有场景；完整 Release 构建保持零警告、零错误。
- Revision、PromptId、精确 Choice 和过期/伪造命令的零状态变化；
- Standard 内容包 Registry 的隔离、不可变投影、重复 ID、未知引用和依赖环校验。
- K4 私有选将、共享池无重复、5 人 AI 开局终止以及同 seed + 同选择命令的快照/事件确定性。
- K5 杀链路的结算帧栈、响应窗口父子关系、JSON 序列化和伤害/濒死/胜负事件。

测试项目不用 MSTest/xUnit，是为了让这个教学 Demo 在离线环境中也能一条命令运行。
