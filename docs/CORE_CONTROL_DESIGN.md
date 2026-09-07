# 核心控制模块设计

本文件定义长期稳定的控制层。具体牌和技能只能通过这里公开的契约参与游戏。

当前实现等级：**K4 开局切片 + K5 基础响应/恢复/公开选牌/隐藏手牌/伤害后技能效果与触发游标切片**。已经落地唯一牌区、`Processing`、原子 `MoveBatch`、移动账本、可信宿主诊断、公共操作后的通知队列、逐订阅者异常隔离、派发期重入拒绝、统一 Command/Revision/PromptId/Choice、不可变内容 Registry、模式化身份开局/私有选将/共享池去重/逐轮发牌、杀/火杀/雷杀/闪/桃/酒/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊链路的数据型结算帧、酒的一次性直接杀加伤与濒死者自救、武圣红牌按杀和龙胆杀/闪响应转化的有效牌型分离、反馈的存活伤害后私有可选触发、遗计受伤后私有摸两张牌并向其他存活角色交一张、伤害触发候选的稳定排序与可序列化游标、`DamageNature` 属性伤害类型、座位环最小距离查询、基础濒死求桃，以及提交后的宿主类型化事件投影。遗计已经验证跨座位牌移动效果，但通用非受伤者触发语义、多伤害嵌套濒死和完整结算编排仍属于后续 K5/K6/K7；用酒救援他人仍未开放。

援护补充了一个受约束的跨座位示例：候选拥有者可以在其他角色受到正伤害后，通过私有弃牌 Choice 将自己的手牌移入弃牌堆，并由 `RecoveryFrame` 令固定受伤目标回复 1 点体力；这不是对所有技能开放的通用非受伤者触发系统。

## 1. 总流程

已接受的 `GameCommand` 可由可信宿主记录为不可变命令日志，并通过 `CommandJson` 与 `GameReplay` 在相同 seed 和内容 Registry 下重放；拒绝命令与旧兼容适配器调用不进入该日志，日志不能下发给普通玩家。

```text
Human / AI / Replay
        │
        ▼
GameCommand + ExpectedRevision
        │
        ▼
CommandDispatcher ──校验操作者、版本、Prompt 和完整选项
        │
        ▼
ResolutionEngine ──推进数据型结算帧，直到完成或产生询问
        │
        ├── RuleQueryService：距离、次数、目标、手牌上限等 modifier
        ├── TriggerDispatcher：收集并稳定排序触发技能
        ├── CardZoneStore：唯一卡牌移动入口
        └── GameModeRuntime：胜负、奖惩和模式事件
        │
        ▼
Commit：校验不变量、Revision + 1、生成 EventBatch
        │
        ├── PlayerViewProjector
        ├── GameLogProjector
        ├── ReplayRecorder
        └── 宿主通知（提交后、异常隔离）
```

核心保持同步和单线程。暂停使用数据状态表示，不阻塞线程。

## 2. 标识符

核心对象使用强类型 ID，内容使用带命名空间的稳定字符串 ID：

```csharp
readonly record struct PlayerId(int Seat);
readonly record struct CardInstanceId(int Value);
readonly record struct PromptId(long Value);
readonly record struct ChoiceId(string Value);
readonly record struct EventId(long Value);

readonly record struct CardDefinitionId(string Value);   // standard:slash
readonly record struct SkillId(string Value);            // standard:jianxiong
readonly record struct GeneralId(string Value);          // standard:cao_cao
readonly record struct ModeId(string Value);              // identity:standard_8
```

定义 ID 在存档和录像中稳定；实例 ID 只标识本局对象。禁止依赖枚举顺序生成网络/存档语义。

## 3. 命令与原子提交

统一入口：

```csharp
CommandResult Submit(GameCommand command);

abstract record GameCommand(
    PlayerId? Actor,
    long ExpectedRevision,
    PromptId? PromptId);

record PlayCardCommand(
    PlayerId Actor,
    CardInstanceId Card,
    IReadOnlyList<PlayerId> Targets,
    long ExpectedRevision,
    CardKind? PlayedCardKind = null) : GameCommand(Actor, ExpectedRevision, null);

record EndPlayPhaseCommand(...) : GameCommand(...);
record AnswerPromptCommand(
    PlayerId Actor,
    PromptId Prompt,
    ChoiceId Choice,
    long ExpectedRevision) : GameCommand(Actor, ExpectedRevision, Prompt);
record SelectGeneralCommand(
    int ActorSeat,
    string GeneralId,
    long ExpectedRevision,
    PromptId? PromptId = null) : GameCommand(...);
```

`CommandResult` 不用异常表达玩家操作错误：

```csharp
record CommandResult(
    bool Accepted,
    RuleError? Error,
    long Revision,
    IReadOnlyList<EventEnvelope> CommittedEvents,
    PlayerGameView View);
```

错误目标、过期 Revision、重复回答、不属于自己的手牌、未发布的选项等都返回稳定错误码。异常仅用于核心编程错误或不变量破坏。

当前兼容实现对应为 `int ActorSeat`、`int CardId`、`IReadOnlyList<int> TargetSeats`，并额外支持 `AnswerPromptCommand`/`RespondCommand`。`PlayCardCommand` 可以带可选 `PromptId` 和 `PlayedCardKind`；如果提供则必须匹配当前出牌询问和完整动作，后者用于武圣、龙胆这类把物理牌转成有效牌型的选择。`PendingDecision.Choices` 已经发布每个完整牌/目标组合，回答响应询问时必须匹配其中一个 `ChoiceId`。`CommandResult` 总是附带当前脱敏 `GameSnapshot`，便于本地宿主在拒绝后重新渲染。

一次成功提交的顺序固定：

```text
验证全部输入
→ Begin transition
→ 修改状态并累计事件/日志/通知
→ ValidateInvariants
→ Revision + 1
→ Commit event batch
→ 逐订阅者隔离派发通知
```

规则执行过程中不得调用外部委托。订阅者抛异常只能进入诊断列表，不能回滚或中断已经提交的游戏状态。派发期间再次调用 `Submit/Advance` 必须被拒绝。

## 4. 牌区与移动

```csharp
enum CardZoneKind
{
    DrawPile,
    Hand,
    Processing,
    DiscardPile,
    Equipment,
    Judgment,
    OutsideGame
}

record CardLocation(
    CardZoneKind Zone,
    PlayerId? Owner = null,
    string? Slot = null);

readonly record struct CardMoveReason(string Value);

CardMoveReasons.Use;          // card.use
CardMoveReasons.DeathDiscard; // rule.death-discard
```

`CardZoneStore` 是卡牌位置的唯一真相来源。所有移动必须通过 `Move`/`MoveBatch`，批量移动先整体校验再整体生效。

移动原因采用强类型、带命名空间的稳定 ID；内容包以后可以声明自己的原因，而不需要扩大核心枚举。K1 的 `CardMovementRecord` 与 `CardMoved` 用于可信宿主观察和回归诊断，在提交后才派发，不能作为技能回调修改状态。K5 会在内部事件批次中加入可排序的 `CardsMoving/CardsMoved` 规则时机。

标准移动：

```text
发牌/摸牌：DrawPile → Hand
使用杀：Hand → Processing → DiscardPile
武圣转化：红色非杀实体牌 Hand → Processing → DiscardPile；有效牌型记录为 Slash
龙胆转化：物理闪可在出牌阶段当杀；物理杀/火杀/雷杀可在需要闪的响应窗口当闪；响应 Prompt 逐张发布有效牌型，实体牌仍只经过 Hand → Processing → DiscardPile。
奸雄：   Hand → Processing → 受伤者 Hand
反馈：   目标存活且伤害牌在 Processing → DamageTriggerWindowFrame → DamageSkillFrame → 私有选择；发动后 Processing → 技能拥有者 Hand，否则进入弃牌堆
遗计：   目标存活且伤害结算完成 → DamageTriggerWindowFrame → DamageSkillFrame → 私有摸牌/牌与目标选择；发动时 DrawPile → Hand(拥有者)，再 Hand(拥有者) → Hand(其他存活目标)，跳过时两张牌留在拥有者手牌
援护：   其他角色受到正伤害且目标未满体力 → DamageTriggerWindowFrame → DamageSkillFrame → 私有弃牌；发动时 Hand(拥有者) → Processing → DiscardPile，再由 RecoveryFrame 令固定受伤目标回复 1 点体力
打出闪：Hand → Processing → DiscardPile
使用桃：Hand → Processing → DiscardPile
装备：   Hand → Processing → Equipment
替换装备：旧 Equipment → DiscardPile，新 Processing → Equipment
判定：   DrawPile → Processing → DiscardPile
阵亡：   Hand/Equipment/Judgment → DiscardPile（原子批次）
重洗：   DiscardPile → DrawPile（批量移动后确定性洗牌）
```

每个稳定边界必须满足：

- 每个实例 ID 恰好存在一次；
- Location 与区域顺序一致；
- 总牌数等于开局内容清单；
- Pending 结算引用的牌位于符合要求的区域；
- 游戏结束时 `Processing` 为空。

## 5. 询问与恢复

禁止继续使用“合法卡牌集合 × 合法目标集合”的笛卡尔积。询问必须包含完整、精确的选择：

```csharp
record PendingPrompt(
    PromptId Id,
    long Revision,
    PlayerId Responder,
    PromptKind Kind,
    IReadOnlyList<PromptChoice> Choices,
    bool IsPrivate);

record PromptChoice(
    ChoiceId Id,
    string Description,
    IReadOnlyList<CardInstanceId> Cards,
    IReadOnlyList<PlayerId> Targets,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> ContentIds);
```

回答必须同时匹配 Responder、PromptId、Revision 和 ChoiceId。同一回答只能成功一次。AI 与真人使用同一种 Prompt；`GameRunner` 只是替 AI 自动选择并提交，核心不再维护 `_pendingAiDodgeDecision` 一类专用布尔状态。

## 6. 结算栈

结算帧必须是可序列化的数据，不保存委托、闭包或 WPF 对象：

```text
TurnFrame(phaseCursor)
UseCardFrame(card, targets, targetCursor, step)
ResponseWindowFrame(responderCursor, incomingCard, requiredCard, step)
DamageFrame(source, target, amount, nature, step)
DyingFrame(victim, responderSeats, responderCursor, step)
TriggerWindowFrame(eventId, candidateCursor, step)
EffectSequenceFrame(effectCursor, step)
```

`ResolutionEngine.Pump()` 反复推进栈顶：

1. 帧完成则弹出；
2. 帧产生子事件则压栈；
3. 帧需要玩家输入则写入 `PendingPrompt` 并返回；
4. 收到回答后记录选择、清除 Prompt，继续原帧游标；
5. 游戏结束时只保留明确允许的收尾帧。

`AdvanceOneStep()` 继续保持当前 UI 语义：一次状态阶段或一次 AI 决策，而不是暴露每个内部微事件。

当前 K5 切片已经把现有杀/火杀/雷杀/闪/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/伤害/桃路径接入 `ResolutionStack`：`CardUseFrame` 可以拥有 `ResponseWindowFrame`，并通过 `TargetIndex` 保存群体牌当前目标；伤害、恢复、基础濒死和死亡作为子帧，`DamageFrame` 保存 `DamageNature`；伤害后候选先进入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，可选技能再压入其子 `DamageSkillFrame`，回答后沿同一窗口继续；武圣的 `LegalAction.PlayedCardKind`/`PlayCardCommand.PlayedCardKind` 区分物理牌和本次有效牌型，龙胆的出牌与响应分别通过 `PlayedCardKind`、`response-card-kind` 和 `CardRespondedEvent.EffectiveCardKind` 区分有效牌型，事件与伤害链记录有效牌型，移动账本仍引用同一实体牌；无中生有使用空目标列表并在同一提交内完成摸牌，南蛮入侵和万箭齐发按固定座次逐个发布私有 `RespondSlash`/`RespondDodge`，桃园结义按固定座次逐个推进 `RecoveryFrame`，五谷丰登按固定座次公开翻牌并逐个发布私有 `SelectHarvestCard`，过河拆桥按目标选择使用确定性随机数盲弃一张隐藏手牌，顺手牵羊按座位环距离一盲取一张隐藏手牌，`GroupResponseEvent` 携带 `RequiredCardKind`，`CardsRevealedEvent`/`HarvestCardSelectedEvent`/`TargetCardDiscardedEvent`/`TargetCardTakenEvent` 记录公开效果和脱敏结果，每个目标完成后才推进父帧，濒死窗口按固定座次逐一发布私有桃选项，反馈/遗计/节命通过同一伤害技能子帧提供取得伤害牌、私有摸牌/跨座位分配和按公开手牌数补牌至目标上限的最小效果。每个帧只保存可序列化数据，不保存委托、闭包或 WPF 对象。它是可信宿主诊断和回放基础，尚未宣称已经实现通用跨座位技能、多伤害嵌套、完整技能濒死响应或所有牌型。

## 7. 类型化事件与时机

反馈、遗计、节命与援护是本批在该帧栈上的四条可选伤害后技能切片：目标存活且伤害牌仍在 `Processing` 时，Core 先冻结排序后的 `DamageTriggerWindowFrame`，再压入 `DamageSkillFrame`。反馈只向反馈者发布私有 `DecisionKind.Feedback`；发动后才将同一实体牌移入技能拥有者手牌并提交 `DamageSkillResolvedEvent`/`DamageCardClaimedEvent`，跳过则让伤害牌进入弃牌堆。遗计只向受伤者发布私有 `DecisionKind.Yiji`，先通过 `DamageSkillCardsDrawnEvent` 记录两张牌从牌堆进入拥有者手牌，再以精确的牌/目标组合选择将其中一张经 `skill.yiji.give-card` 移给其他存活角色并提交 `DamageSkillCardGivenEvent`，跳过则两张牌留在拥有者手牌。节命只向受伤者发布私有 `DecisionKind.Jieming`，按公开存活状态、手牌数量和体力上限生成目标 Choice，发动后经 `skill.jieming.draw` 摸牌并在 `DamageSkillCardsDrawnEvent.TargetSeat` 中记录目标。援护只向非受伤的技能拥有者发布私有 `DecisionKind.Yuanhu` 弃牌 Choice，发动后按 `skill.yuanhu.discard` 将拥有者的一张手牌经过 `Processing` 移入弃牌堆，再压入 `RecoveryFrame` 令固定受伤目标回复 1 点体力，并以 `DamageSkillCardDiscardedEvent`/`RecoveryAppliedEvent` 记录结果。四条路径都沿候选游标继续，群体父帧保持可推进，普通视图不携带私有取得牌 ID。

事件信封：

```csharp
record EventEnvelope(
    EventId Id,
    EventId? ParentId,
    long Sequence,
    long Revision,
    string CorrelationId,
    IGameEvent Payload);
```

首批事件（当前已提交的观测事件；并非内容技能回调）：

```text
 GameStarted
SetupStarted / GeneralSelectionRequested / GeneralSelected / SetupCompleted
TurnStarted / TurnEnded
PhaseChanging / PhaseStarted / PhaseEnded
CardUseDeclared / TargetsConfirmed / CardUseFinished
ResponseRequested / CardResponded / DuelResponse / GroupCardUsed / GroupResponse
CardsMoving / CardsMoved
DamageRequested / DamageApplied / AfterDamage（携带 DamageNature）
DamageSkillRequested / DamageSkillResolved / DamageCardClaimed / DamageSkillCardsDrawn / DamageSkillCardGiven / DamageSkillCardDiscarded
AlcoholApplied / AlcoholExpired
RecoveryApplied
  PlayerDying / DyingResponse / DyingResolved / PlayerDied
RoleRevealed
WinnerDetermined / GameEnded
```

日志是事件投影，不再由规则分支直接写字符串 `"CardUsed"`。

触发技能排序键固定为：

```text
priority（高到低）
→ 相对当前行动者的座次顺序
→ skillId（序数排序）
→ candidateId
```

相同 seed、内容清单和命令流必须产生逐事件相同的录像。

## 8. 规则查询与技能

持续修改规则值的能力使用 modifier：

```csharp
interface IRuleModifier<TQuery>
{
    void Modify(ref TQuery query, RuleContext context);
}
```

例子：摸牌数、杀次数、距离、攻击范围、目标禁止、手牌上限、伤害值。

在事件时机主动执行的能力使用 trigger：

```csharp
interface ITriggerSkill<in TEvent> where TEvent : IGameEvent
{
    IEnumerable<TriggerCandidate> Collect(TEvent gameEvent, TriggerContext context);
}
```

例子：奸雄、反馈、武圣、刚烈、遗计、节命、援护、武器命中后效果。技能只能返回类型化 Effect/候选，不直接取得可变 `GameEngine`。

限定技次数、回合标记和装备授予技能全部存于 `GameState`，技能定义对象必须无局内可变字段。

## 9. 内容注册表

```csharp
interface IGameContentPackage
{
    PackageManifest Manifest { get; }
    void Register(IContentRegistryBuilder builder);
}

record PackageManifest(
    string Id,
    Version Version,
    IReadOnlyList<PackageDependency> Dependencies);
```

注册表包含 CardDefinition、CardBehavior、SkillDefinition、GeneralDefinition、DeckRecipe 和 GameModeDefinition。`Build()` 时必须：

- 拒绝包内/跨包重复 ID；
- 拒绝未知卡牌、技能、武将、模式引用；
- 检查依赖缺失和依赖环；
- 以稳定顺序冻结为不可变结构；
- 保证不同 Registry 创建的引擎互不污染。

短期保留当前 `CardKind`/`SkillKind` 作为兼容投影；内容项目迁移完成后再弃用，不能让 Luna 继续无限添加枚举成员。

K3 当前实现为 `ContentRegistry.Build(params IGameContentPackage[])` 与 `ContentRegistry`：注册过程按包依赖拓扑排序，冻结包清单、卡牌、技能、武将、牌堆和模式元数据，并校验重复 ID、未知引用、版本不足和依赖环。`CardGame.Content.Standard` 是首个正式包；`GameEngine.CreateStandard(options, registry)` 消费其 `standard:basic-demo` 牌堆配方，旧的无 Registry 构造仍作为兼容入口。当前还提供可信宿主 `Events`/`EventCommitted`，用 `EventEnvelope` 携带 `EventId`、Revision、CorrelationId 和类型化 payload；它不是内容技能回调，也不进入玩家视图。

K4 当前实现把 `ContentModeDefinition` 的角色分布、牌堆 ID、候选数量和武将池接入 `GameEngine`。启用 `GameOptions.UseInteractiveSetup` 后，`Start`/`AdvanceOneStep` 会依次执行身份分配、私有单将候选、AI 选择、共享池移除、公开武将、洗牌和 round-robin 发牌；真人通过 `SelectGeneralCommand` 暂停和恢复。候选使用 `PendingDecision.ValidContentIds` 与 `PromptChoice.ContentIds`，只投影给 responder；`GeneralSelectionRequestedEvent`、`GeneralSelectedEvent` 和 AI 选将思考只属于可信宿主。WPF 已启用该路径，旧构造式 Demo 保留用于迁移。

K5 当前实现把现有 `standard:slash`、`standard:fire_slash`、`standard:thunder_slash`、`standard:dodge`、`standard:peach`、`standard:alcohol`、`standard:duel`、`standard:draw_two`、`standard:barbarian_assault`、`standard:arrow_barrage`、`standard:peach_garden`、`standard:five_grains`、`standard:dismantlement`、`standard:snatch` 路径包进可序列化 `CardUseFrame`、`ResponseWindowFrame`、`DamageFrame`、`DamageTriggerWindowFrame`、`DamageSkillFrame`、`RecoveryFrame`、`DyingFrame` 和 `DeathFrame`；同时提交 `CardUseDeclared`、`TargetsConfirmed`、`GroupCardUsed`、携带 `DamageNature` 和实际金额的 `DamageRequested`/`DamageApplied`/`AfterDamage`、伤害触发窗口打开/游标推进、`DamageSkillCardsDrawn`、`DamageSkillCardGiven`、`AlcoholApplied`、`AlcoholExpired`、`RecoveryApplied`、`PlayerDying`、`DyingResponse`、`DyingResolved`、`DuelResponse`、`GroupResponse`、`CardsRevealed`、`HarvestCardSelected`、`TargetCardDiscarded`、`TargetCardTaken`、`RoleRevealed`、`WinnerDetermined` 等类型化事件。酒作为无目标即时牌使用，公开设置 `HasAlcoholEffect`，下一张直接杀在声明时消费 +1 伤害；未消费效果在回合结束清除。濒死时只有持有者可通过私有 `RescueDying` Prompt 使用酒，酒经独立 `CardUseFrame`/`RecoveryFrame` 回复 1 点体力并标记 `DyingResponseEvent.UsedAlcohol`。决斗按双方交替的 `RespondSlash` 窗口可暂停、恢复，响应牌各自经过 `Processing`；无中生有使用无目标的 `CardUseFrame`，摸牌移动仍经统一区域入口；南蛮入侵/万箭齐发/桃园结义使用 `TargetIndex` 按座次逐目标响应或恢复，五谷丰登按同一游标公开翻牌并逐个发布私有 `SelectHarvestCard`，过河拆桥按单目标 `CardUseFrame` 用确定性随机数盲弃目标手牌，顺手牵羊按单目标 `CardUseFrame` 用座位环距离一确定性盲取目标手牌并转入使用者手牌，遗计在伤害后先将两张牌私有摸入受伤者手牌，再支持一张牌向其他存活角色的受限跨手牌移动，父牌在各自效果完成前保持于 `Processing`；基础濒死窗口按固定 responder 顺序可暂停、可恢复，私有桃/酒候选只投影给当前 responder；通用非受伤者触发语义、多伤害嵌套和用酒救援他人仍留待后续切片。

其中 `standard:yuanhu` 是一个明确受限的跨座位触发：伤害目标以外的援护者收到私有 `DecisionKind.Yuanhu`，选择自己的手牌后由 Core 依次执行 `Hand(owner) → Processing → DiscardPile` 和固定目标的 `RecoveryFrame`；它验证了非受伤者候选的最小闭环，但没有宣称通用跨座位触发已完成。

## 10. 视图、录像和存档

本批新增 `FeedbackSkill` 与 `YijiSkill` 两条最小可选伤害后触发：Core 收集当前存活拥有者的 `DamageTriggerCandidate`，通过 `DamageTriggerOrdering` 稳定排序后压入带游标的 `DamageTriggerWindowFrame`，再为当前候选压入 `DamageSkillFrame`。反馈的私有 `Feedback` Choice 可发动并将处理区中的同一实体牌移入技能拥有者手牌，或跳过并弃牌；遗计的私有 `Yiji` Choice 先把两张牌从 `DrawPile` 摸入受伤者手牌，再从合法的牌/其他存活目标组合中选择一项，将一张牌由拥有者手牌移给目标，或跳过并保留两张牌。`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留候选标识，遗计的 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 与两条 `skill.*` 移动原因只属于可信宿主，普通玩家快照不携带私有取得牌 ID。现有内置技能默认只参与受伤者窗口；遗计验证的是跨座位效果，不代表通用非受伤者触发语义已经完成。

`standard:yuanhu` 在同一窗口上验证了受约束的非受伤者触发：其他角色受到正伤害后，援护者收到私有 `Yuanhu` 弃牌 Choice；发动时由可信核心将援护者的一张手牌按 `skill.yuanhu.discard` 移入弃牌堆，创建 `RecoveryFrame` 令固定受伤目标回复 1 点体力，并发布 `DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 与带 `EffectTargetSeat` 的 `DamageSkillResolvedEvent`。当前只有援护显式 opt-in，通用跨座位触发条件仍未完成。

三类数据必须分离：

- `PlayerGameView`：可下发给指定玩家的脱敏状态；
- `SpectatorView`：遵守模式观战规则；
- `EngineCheckpoint`：可信宿主专用的完整恢复数据。

Checkpoint 至少包含：

```text
SchemaVersion
CoreApiVersion
ContentManifest（包 ID + 版本 + 内容哈希）
完整 GameState 和全部卡牌区域顺序
确定性随机数状态
ResolutionStack + PendingPrompt
Revision / EventSequence
AI 公开知识模型和 AI 随机数状态
```

录像保存初始 Checkpoint/种子、内容清单和已接受命令流。普通玩家 JSON 中不得出现暗身份、他人牌 ID/定义、牌堆顺序、seed、他人 Prompt 或完整 AI 候选。

## 11. 开局控制流水线

模式通过数据型步骤组织开局：

```text
ValidatePlayers
→ AssignSeats
→ AssignRolesOrTeams
→ BuildGeneralPool
→ OfferGeneralChoices
→ ResolveGeneralChoices
→ InitializeCharacters
→ BuildDeck
→ ShuffleDeck
→ DealInitialHandsRoundRobin
→ EmitGameStartEvents
→ SelectFirstPlayer
→ BeginFirstTurn
```

选将和其他询问复用同一个 Prompt/Choice 机制。AI 通过 `IGeneralSelectionPolicy` 给候选评分，不能看别人的私有候选。

## 12. 兼容迁移

迁移期间保留当前公开方法：

```text
HumanPlay        → PlayCardCommand
HumanEndPlay     → EndPlayPhaseCommand
HumanRespond     → AnswerPromptCommand
Advance          → GameRunner.PumpToHumanBoundary
AdvanceOneStep   → GameRunner.PumpOneUiStep
```

旧 WPF 先继续消费兼容 `GameSnapshot`。新 `PlayerGameView` 稳定后再切换 UI。任何阶段不得同时替换状态、命令、事件、内容格式和 UI。
