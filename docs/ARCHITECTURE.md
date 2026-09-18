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

- `CardCatalog` 为杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻、乐不思蜀、兵粮寸断、无懈可击、铁索连环和七种装备牌（含仁王盾）提供显示文案、类别、AI 使用价值和自动弃牌保留价值；`Card.DisplayName` 与 WPF 卡面都从这里读取，避免 UI 再维护一份牌面语义。
- `StandardDeckCatalog.BasicDemo` 描述 18 张杀、2 张火杀、2 张雷杀、18 张闪、10 张桃、2 张酒、4 张决斗、2 张无中生有、2 张南蛮入侵、2 张万箭齐发、2 张桃园结义、2 张五谷丰登、2 张过河拆桥、2 张顺手牵羊、2 张火攻、2 张乐不思蜀、2 张兵粮寸断、2 张闪电、2 张无懈可击、2 张铁索连环、2 张诸葛连弩、1 张八卦阵、1 张青釭剑、1 张赤兔、1 张绝影和 1 张玉玺、1 张仁王盾组成的 90 张演示牌堆、初始手牌数 4 和每回合摸牌数 2；玉玺可使持有者额外摸一张，青釭剑使直接杀无视目标防具，规则 v14 的仁王盾在黑色杀指定目标后令其无效，`GameEngine` 只消费这些配置并负责洗牌、移动和结算。
- 牌的顺序生成仍然使用稳定的 id、花色和点数规则，再交给现有确定性随机数洗牌，因此相同种子仍可复现。
- `CardGame.Content.Standard/StandardContentPackage.cs` 注册 `standard:*` 卡牌、技能、武将、牌堆和 `identity:standard-8` 模式定义；`ContentRegistry.Build()` 会按依赖拓扑顺序注册，并拒绝重复 ID、缺失引用、版本不足和依赖环。
- `GameEngine.CreateStandard(options, registry)` 消费 Registry 中的 `standard:basic-demo` 牌堆配方，运行时仍通过 `CardKind`/`SkillKind` 兼容映射，避免一次性破坏旧 WPF API；不传 Registry 时保留 Core 内置兼容路径。
- `ContentModeDefinition` 还提供角色分布、牌堆 ID、候选数量和武将池；开启 `UseInteractiveSetup` 后，模式开局通过私有 `SelectGeneralCommand` 暂停，所有候选均由 Core 校验，完成后才洗牌并逐轮发牌。
- `standard-active-skills@1.0.0` 是依赖 `standard@1.11.0` 的可选扩展包，增加 `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、`standard:mashu`、`standard:qicai`、七个技能演示武将及 `identity:active-skills-8/5`；其上另有依赖主动技能包的 `standard-rescue-skills@1.0.0`，增加 `standard:jijiu`/急救者并扩展同一模式武将池；`StandardContentRegistry.Create()` 和不含救援层的 `CreateWithActiveSkills()` 保持原内容指纹不变，WPF 默认窗口显式选择包含救援层的 Registry。
- `standard-classic-generals@1.1.0` 在 1.0.0 的正式多技能武将层上增加 `classic:guo-jia` 与 `classic:tiandu`，并保留遗计作为第二技能；恢复 1.0.0 存档时仍构造旧武将池和旧包指纹，不把新版郭嘉静默写入历史命令前缀。
- `standard-classic-generals@1.7.0` 在既有正式武将层中加入 `classic:huang-gai`，复用 `standard:kujin`；1.6.0 保留不含黄盖的武将池，1.5.0 保留不含救援的孙权，1.4.0 再保留不含激将的刘备，1.3.0 继续使用旧 `standard:cao-cao`，更早版本按历史边界恢复诸葛亮、周瑜与郭嘉，八个版本分别保留原武将池、定义和内容指纹。
- K5 切片把决斗的交替 `RespondSlash` 响应、无中生有的无目标摸牌、酒的一次性直接杀 +1 伤害、濒死窗口所有 responder 的桃救援与 victim 的酒自救、急救红牌当桃、南蛮入侵/万箭齐发的群体逐目标 `RespondSlash`/`RespondDodge`、桃园结义的群体逐目标恢复、普通/火/雷杀的 `DamageNature`、反馈/遗计/节命伤害后技能和单次杀/决斗/群体牌伤害后的基础濒死窗口接入同一帧栈：`CardUseFrame.TargetIndex` 保存当前群体目标，`GroupResponseEvent` 记录必需响应牌种类，`DamageFrame` 保存伤害类型和实际金额，`AlcoholAppliedEvent`/`AlcoholExpiredEvent` 表示酒效生效/回合结束失效，`RecoveryFrame` 记录桃园结义当前恢复子帧，`DyingFrame` 逐个询问可用桃/酒/红牌的 responder，桃、酒和转化牌只从 responder 自己的手牌移动到 `Processing`，规则 v12 起酒仅允许 victim 自救，v3–v11 保留跨座位救援，v1/v2 仍按原仅自救语义，急救事件同时记录有效 Peach 与物理牌型，节命通过私有目标 Choice 和 `DamageSkillCardsDrawnEvent.TargetSeat` 补牌至目标体力上限，其他视角只看到公开结算结果。

当前已开放十四张最小锦囊 `standard:duel`、`standard:draw_two`、`standard:barbarian_assault`、`standard:arrow_barrage`、`standard:peach_garden`、`standard:five_grains`、`standard:dismantlement`、`standard:snatch`、`standard:fire_attack`、`standard:indulgence`、`standard:supply_shortage`、`standard:lightning`、`standard:nullification` 与 `standard:iron_chain`，酒作为第六种基本牌复用无目标 `CardUseFrame` 并提供一次性直接杀加伤，四张群体/多目标牌共用目标游标，其中两张响应牌共用通用响应事件，五谷丰登复用公开翻牌和私有选牌事件，过河拆桥和顺手牵羊复用单目标 `CardUseFrame` 与“隐藏手牌不透明牌位选择/公开装备或判定区牌精确选择”的目标牌移动入口；规则版本 4 使用 `TargetCardSelectionFrame` 固化私有暂停点，`TargetCardSelectionRequestedEvent` 只公开候选数量，规则版本 1–3 回放保留历史盲选语义，火攻复用单目标父帧并增加私有展示/同花色弃牌两段 Prompt，乐不思蜀、兵粮寸断和闪电把延时牌置入目标公开判定区并在其下回合摸牌前复用 `JudgmentFrame`，规则 v11 起前者非红桃跳过出牌阶段、兵粮寸断非梅花跳过摸牌阶段，v1–v10 保留历史红黑语义，闪电黑桃 2 至 9 命中 3 点雷电伤害否则转移；无懈可击在锦囊效果前复用固定座次私有响应和有限多层 `NullificationWindowFrame`，铁索连环通过精确一/二目标和公开 `IsChained` 标记接入火/雷伤害同额传导；普通/火/雷杀共用杀路径，属性类型和实际伤害金额写入伤害帧与事件；K6 已开放五个装备槽位、七种装备牌（含仁王盾）的生命周期、同槽替换、死亡清理、基础战斗距离/攻击范围查询以及诸葛连弩、青釭剑、赤兔、绝影、玉玺、仁王盾和马术的基础 modifier；规则 v13 起诸葛连弩/青釭剑分别使用牌面攻击范围 1/2，v1–v12 保留旧 +1/默认 1 的回放数值；规则 v14 起八卦阵覆盖直接杀及万箭齐发的闪响应，仁王盾在黑色杀指定目标后通过 `ArmorEffectAppliedEvent` 令其无效，v1–v13 保留旧时机；奇才通过 `GetLegalActions` 统一放宽距离型锦囊目标，K7 已为八卦阵、乐不思蜀、兵粮寸断和闪电接入 `JudgmentFrame`、判定区移动和对应结果，鬼才复用同一判定帧。其余锦囊和复杂装备效果仍需各自的目标、响应窗口和处理区语义。明确扩展需求记录在 [`CONTENT_BACKLOG.md`](CONTENT_BACKLOG.md)，在对应类型化入口开放前不通过 UI 或 `GameEngine` 特判接入。

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

十六个被动技能例子覆盖十六类查询/触发扩展点；主动技能另有独立入口：

- 英姿：旧规则直接修改摸牌数；规则 v21 的经典身份局在摸牌阶段发布可暂停选择，并在确认后继续同一回合；
- 天妒：规则 v22 的经典身份局在拥有者自己的判定牌生效后发布可暂停选择，决定该牌进入手牌还是按通常流程弃置；
- 反间：规则 v23 的经典身份局通过目标型主动技能选择一名其他角色，再由目标先选花色；随机牌转移并公开后，花色不同时进入 1 点普通伤害链；
- 观星：规则 v24 的经典身份局在延时牌判定和摸牌之前发布发动/跳过 Choice；发动后牌面、花色、点数和排序步骤仅对技能拥有者可见，完成后只公开观看/置顶/置底数量；
- 护驾：规则 v25 的经典主公曹操在闪响应窗中可发起有序魏势力询问；候选只看到自己的实体闪/转化牌与公开八卦阵，成功牌的物理拥有者和有效响应者分开记录；
- 激将：规则 v26 的经典主公刘备可在出牌阶段或杀响应窗发起有序蜀势力询问；每个候选只看到自己的实体杀/转化牌，提供者与有效使用者/响应者分开记录，主动失败保留人类重试能力；
- 救援：规则 v27 的经典主公孙权在濒死时，由另一名吴势力角色使用的桃回复量为 2；技能无新 Prompt，复用既有私有救援选择、实体牌移动和恢复事件，并用专用事件公开加成已生效；
- 咆哮：修改一回合使用杀的上限；
- 空城：添加目标禁止条件；
- 奸雄：在伤害完成后改变造成伤害的卡牌去向。
- 武圣：允许红色非杀实体牌生成有效牌型为杀的动作，保留原物理牌实例；
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
- 十九种被动技能钩子及七个主动技能（含反馈伤害后取牌、遗计跨手牌分配、节命目标补牌、援护跨座位弃牌恢复、刚烈判定与来源反制、鬼才判定替换、天妒取得判定牌、观星私有牌堆排序、护驾跨座位闪响应、激将跨座位杀使用/响应、武圣牌转化、龙胆响应转化、马术距离修正、奇才锦囊距离豁免、急救红牌濒死转化、苦肉主动命令、制衡多选弃牌、仁德目标交牌、青囊弃牌恢复、回春多目标弃牌恢复与反间目标选花色/随机交牌）；
- 相同种子得到相同初始状态；
- 真人出牌和打闪暂停点；
- 初始发牌、处理区生命周期、单张/批量移动原子性和每个公共边界的卡牌守恒；
- 提交后通知、订阅者异常隔离、重入拒绝和观察者不影响确定性结果；
- AI 单步只执行一个决策；
- 全 AI 对局可以结束并产生解释记录；
- 快照可以序列化为 JSON。
  - 当前 Core 自测共 155 项、WPF 自测共 51 项；除既有牌、技能、Checkpoint、装备、判定和视图回归外，还覆盖激将的主动/响应双入口、私有有序询问、实体杀归属、属性牌型、失败重试、AI、暂停/完成恢复、1.4.0 兼容与 WPF 双主动技入口，以及护驾、观星、天妒、反间、急救、国战和完整 UI 命令对局等既有场景；完整 Release 构建保持零警告、零错误。
- Revision、PromptId、精确 Choice 和过期/伪造命令的零状态变化；
- Standard 内容包 Registry 的隔离、不可变投影、重复 ID、未知引用和依赖环校验。
- K4 私有选将、共享池无重复、5 人 AI 开局终止以及同 seed + 同选择命令的快照/事件确定性。
- K5 杀链路的结算帧栈、响应窗口父子关系、JSON 序列化和伤害/濒死/胜负事件。

测试项目不用 MSTest/xUnit，是为了让这个教学 Demo 在离线环境中也能一条命令运行。
