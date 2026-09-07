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

## 2. 状态与命令

引擎内部的 `PlayerRuntime` 保存完整身份和手牌。外部只能取得 `GameSnapshot`：

- 自己：可见身份和完整手牌；
- 主公：身份公开；
- 其他存活角色：只有武将、体力和手牌数量；
- 选将阶段：自己的候选和已选武将可见，其他座位的候选和未公开武将均隐藏；全部完成后武将一次公开；
- 阵亡角色：身份公开；
- 普通快照不携带随机种子，也不会携带其他玩家正在处理的私有决定；
- `ProcessingCardCount` 只公开处理区数量，不公开牌堆顺序或他人暗牌 ID；酒效是公开的 `HasAlcoholEffect` 状态，不扩大任何手牌可见性；五谷丰登的 `PublicRevealedCards` 是规则明确公开的牌面例外，当前 `SelectHarvestCard` prompt 仍只给当前 picker；过河拆桥的 `TargetCardDiscardedEvent` 和顺手牵羊的 `TargetCardTakenEvent` 都不携带目标牌 ID/牌面，顺手牵羊取得的牌只在使用者私有快照中出现；
- 反馈、遗计与节命的 `DamageTriggerWindowFrame`、`DamageSkillFrame`、窗口生命周期事件和 `DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 只属于可信宿主结算/事件投影；其中保留稳定的 `CandidateId`/`Priority`，`Feedback`/`Yiji`/`Jieming` Prompt 只投影给对应拥有者，遗计的摸牌与跨手牌转移通过 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent` 记录，节命的目标和补牌通过 `DamageSkillCardsDrawnEvent.TargetSeat` 与 `skill.jieming.draw` 移动记录，普通快照不泄漏其他玩家的候选牌 ID；
- 援护的 `Yuanhu` Prompt 只投影给非受伤的技能拥有者；弃牌 ID 只属于可信宿主的 `DamageSkillCardDiscardedEvent`/移动账本，固定受伤目标和 1 点恢复属于公开结算结果，普通快照不泄漏被弃牌的实体 ID。
- `revealAll: true` 只用于本地开发者视图和测试，宿主可通过独立的 `GameEngine.Seed` 记录回放种子。

完整牌区位置只能通过明确标为可信宿主诊断的 `CreateCardZoneDiagnostics()` 取得。它不能进入玩家网络 DTO。

旧版 WPF 仍使用三个基础响应兼容入口：

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

`CommandResult` 对玩家输入错误返回稳定 `CommandErrorCode`。出牌询问中的每个 Choice 已经把牌和目标绑定，回答询问还必须匹配 Responder、PromptId、ChoiceId 和 Revision；被拒绝的命令不改变快照、日志、牌区或随机数。

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
        ├─ DamageSkill → Feedback Prompt → 发动：DamageCardClaimedEvent / Processing → Hand；跳过：Processing → DiscardPile
       ├─ DamageSkill → Yiji Prompt → 摸两张：DrawPile → Hand(owner)；交牌：Hand(owner) → Hand(other target)；跳过：两张牌留在 owner Hand
       ├─ DamageSkill → Yuanhu Prompt → 发动：Hand(owner) → Processing → DiscardPile；RecoveryFrame → 固定受伤目标回复 1 点；跳过：不移动牌
       ├─ BarbarianAssault / ArrowBarrage → TargetIndex → 逐目标 Pending Slash / Dodge → Damage / Dying
        ├─ PeachGarden → TargetIndex → 逐目标 RecoveryFrame → RecoveryApplied
        ├─ FiveGrains → TargetIndex → PublicRevealedCards → 私有 SelectHarvestCard → Hand
        └─ EndPlay
             ↓
          Discard → Finished → 下一存活玩家的 Draw
```

`Advance()` 连续运行 AI，直到遇到真人输入或游戏结束。`AdvanceOneStep()` 只处理一个阶段变化或一次 AI 决策，供调试界面观察。两者和旧人类 API 都通过公共提交边界产生 Revision。

这与旧式引擎“阻塞一个房间线程等待客户端”不同：暂停由 `PendingDecision` 表达，进程中没有锁住的等待线程。若未来联网，服务器可以序列化当前快照，稍后收到回复再恢复。

## 4. 卡牌结算

一次杀/决斗的主要调用链：

```text
HumanPlay / AI ChoosePlay
      → BuildLegalActions（当前状态下重新验证）
      → ExecutePlay
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
              → 目标存活且存在候选：压入 DamageTriggerWindowFrame，CandidateIndex 可在候选间推进
           → 可选技能：压入 DamageSkillFrame，拥有者私有选择发动/跳过
              → Feedback 发动：取得同一实体牌并发布 DamageSkillResolvedEvent / DamageCardClaimedEvent
              → Yiji 发动：先摸两张牌，再将一张合法牌移给其他存活目标并发布 DamageSkillCardGivenEvent
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

火攻使用独立的两段私有选择而不是复用五谷丰登的公共 draft：目标先在自己的快照中选择一张手牌，实体牌从 `Hand(target) → Processing` 后才通过 `FireAttackCardRevealedEvent` 和 `PublicRevealedCards` 公开；攻击者再从自己的同花色手牌中选择弃牌或跳过。成功弃牌时同一 `CardUseFrame` 转为 `AttackResolution`，由 `DamageNature.Fire` 进入既有伤害/技能/濒死链；跳过或无同花色牌则清理展示牌并结束父牌。两个 AI 选择器只接收本座快照与宿主已经发布的候选 ID，普通观察者既看不到未展示牌面，也看不到私有候选 Prompt。

过河拆桥和顺手牵羊复用单目标 `CardUseFrame` 与确定性隐藏手牌选择，但去向不同：前者把 `Hand(target) → Processing → DiscardPile`，后者把 `Hand(target) → Processing → Hand(source)`。顺手牵羊的合法性由 `GetSeatDistance(sourceSeat, targetSeat) == 1`、存活、非自身和目标有手牌共同决定；距离查询目前只计算固定座位环的最短边数，不调整坐骑或死亡座位。两张牌都只把脱敏的类型化效果事件投影给公共观察者，可信移动账本保留物理牌 ID。

`CardZoneStore` 维护 DrawPile、Hand、Processing、DiscardPile、Equipment、Judgment 和 OutsideGame。单张和批量移动都先验证真实来源、目标区和重复 ID，再统一生效；引擎在每次公共操作返回前校验 72 张实体牌唯一且守恒。移动账本使用类型化 `CardMoveReason`，可供测试和后续录像使用。武圣与龙胆的牌转化不创建副本：`LegalAction.PlayedCardKind`/`PlayCardCommand.PlayedCardKind` 描述出牌时的有效牌型，响应 Prompt 的 `response-card-kind` 与 `CardRespondedEvent.EffectiveCardKind` 描述响应有效牌型，`CardMovementRecord.CardKind` 仍描述同一实体牌的物理牌面。

引擎保持所有规则状态修改集中在受控入口。技能只回答“小型规则问题”，例如摸牌数是否增加、能否成为杀的目标、伤害牌是否被取得。这样不会让某个技能脚本悄悄绕过死亡和胜负检查。当前 `DamageNature` 只表达普通、火焰和雷电三种伤害来源；抗性、属性转化和铁索连环还没有接入。

## 5. 内容注册与阶段边界

当前内容同时提供兼容投影和正式 Registry：

- `CardCatalog` 为杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻提供显示文案、类别、AI 使用价值和自动弃牌保留价值；`Card.DisplayName` 与 WPF 卡面都从这里读取，避免 UI 再维护一份牌面语义。
- `StandardDeckCatalog.BasicDemo` 描述 18 张杀、2 张火杀、2 张雷杀、18 张闪、10 张桃、2 张酒、4 张决斗、2 张无中生有、2 张南蛮入侵、2 张万箭齐发、2 张桃园结义、2 张五谷丰登、2 张过河拆桥、2 张顺手牵羊、2 张火攻组成的 72 张演示牌堆、初始手牌数 4 和每回合摸牌数 2；`GameEngine` 只消费这些配置并负责洗牌、移动和结算。
- 牌的顺序生成仍然使用稳定的 id、花色和点数规则，再交给现有确定性随机数洗牌，因此相同种子仍可复现。
- `CardGame.Content.Standard/StandardContentPackage.cs` 注册 `standard:*` 卡牌、技能、武将、牌堆和 `identity:standard-8` 模式定义；`ContentRegistry.Build()` 会按依赖拓扑顺序注册，并拒绝重复 ID、缺失引用、版本不足和依赖环。
- `GameEngine.CreateStandard(options, registry)` 消费 Registry 中的 `standard:basic-demo` 牌堆配方，运行时仍通过 `CardKind`/`SkillKind` 兼容映射，避免一次性破坏旧 WPF API；不传 Registry 时保留 Core 内置兼容路径。
- `ContentModeDefinition` 还提供角色分布、牌堆 ID、候选数量和武将池；开启 `UseInteractiveSetup` 后，模式开局通过私有 `SelectGeneralCommand` 暂停，所有候选均由 Core 校验，完成后才洗牌并逐轮发牌。
- K5 切片把决斗的交替 `RespondSlash` 响应、无中生有的无目标摸牌、酒的一次性直接杀 +1 伤害、濒死者酒自救、南蛮入侵/万箭齐发的群体逐目标 `RespondSlash`/`RespondDodge`、桃园结义的群体逐目标恢复、普通/火/雷杀的 `DamageNature`、反馈/遗计/节命伤害后技能和单次杀/决斗/群体牌伤害后的基础濒死窗口接入同一帧栈：`CardUseFrame.TargetIndex` 保存当前群体目标，`GroupResponseEvent` 记录必需响应牌种类，`DamageFrame` 保存伤害类型和实际金额，`AlcoholAppliedEvent`/`AlcoholExpiredEvent` 表示酒效生效/回合结束失效，`RecoveryFrame` 记录桃园结义当前恢复子帧，`DyingFrame` 逐个询问可用桃/酒的 responder，桃和酒只从 responder 自己的手牌移动到 `Processing`，节命通过私有目标 Choice 和 `DamageSkillCardsDrawnEvent.TargetSeat` 补牌至目标体力上限，其他视角只看到公开结算结果。

当前已开放九张最小锦囊 `standard:duel`、`standard:draw_two`、`standard:barbarian_assault`、`standard:arrow_barrage`、`standard:peach_garden`、`standard:five_grains`、`standard:dismantlement`、`standard:snatch` 与 `standard:fire_attack`，酒作为第六种基本牌复用无目标 `CardUseFrame` 并提供一次性直接杀加伤，四张群体/多目标牌共用目标游标，其中两张响应牌共用通用响应事件，五谷丰登复用公开翻牌和私有选牌事件，过河拆桥和顺手牵羊复用单目标 `CardUseFrame` 与隐藏手牌移动入口，火攻复用单目标父帧并增加私有展示/同花色弃牌两段 Prompt；普通/火/雷杀共用杀路径，属性类型和实际伤害金额写入伤害帧与事件；其余锦囊仍需要各自的目标、响应窗口和处理区语义；装备需要装备区和持续修正。明确扩展需求记录在 [`CONTENT_BACKLOG.md`](CONTENT_BACKLOG.md)，在对应类型化入口开放前不通过 UI 或 `GameEngine` 特判接入。

## 6. 技能扩展

当前最小接口：

```csharp
public interface IPassiveSkill
{
    int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => currentCount;
    int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => currentLimit;
    bool ProhibitsSlashTarget(PlayerSkillContext owner) => false;
    bool CanUseAsSlash(PlayerSkillContext owner, Card card) => false;
    bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) => false;
    bool OffersDamageCardChoice(DamageSkillContext context) => false;
    bool ClaimsDamageCard(DamageSkillContext context) => false;
    DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) => DamageSkillEffectKind.None;
}
```

十个例子分别覆盖十类扩展点：

- 英姿：修改摸牌数；
- 咆哮：修改一回合使用杀的上限；
- 空城：添加目标禁止条件；
- 奸雄：在伤害完成后改变造成伤害的卡牌去向。
- 武圣：允许红色非杀实体牌生成有效牌型为杀的动作，保留原物理牌实例；
- 龙胆：允许闪生成有效杀，杀/火杀/雷杀生成有效闪，并复用精确响应询问；
- 反馈：目标存活且伤害牌仍在处理区时进入私有可选触发；发动后取得同一实体牌，并以类型化事件记录。
- 遗计：受伤后私有摸两张牌，从合法牌/目标组合中将一张牌交给其他存活角色，并以类型化事件和受限跨手牌移动记录。
- 节命：受伤后按公开手牌数量选择一名合法角色补牌至体力上限，并以私有 Prompt、类型化事件和牌堆到目标手牌的移动记录。
- 援护：其他角色受到正伤害后，援护者从自己的私有手牌中选择一张弃置，并以 `RecoveryFrame` 令固定受伤目标回复 1 点体力；弃牌、恢复和效果目标通过类型化事件记录。

下一步若要加入更复杂技能，建议在现有 `AfterDamage`、`PlayerDying` 和 `DyingResponse` 类型化事件及 `DamageTriggerWindowFrame` 游标上扩展通用非受伤者候选和可恢复 Checkpoint；当前 `DamageTriggerOrdering` 已固定优先级、相对行动者座次、技能序号和候选 ID 的排序键，而不是把顺序交给任意字符串或可变字典。遗计已经完成一条真实的跨座位牌效果，但现有内置技能仍默认只参与受伤者窗口，通用跨座位触发语义、多伤害嵌套和完整技能濒死响应尚未宣称完成。

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

这一步解决的是“外部观察者打断半次规则操作”。K2 已把 Revision、PromptId 和精确 Choice 纳入统一命令边界；K3 又把内容注册与依赖校验隔离出来；K4 增加了可暂停选将和开局事件；K5 已把现有杀/火杀/雷杀/闪/伤害/桃链路接入可序列化的 `ResolutionStack` 与类型化结算事件，伤害类型在帧和事件之间保持一致。当前事件仍主要用于可信宿主观察/录像投影，完整 EventBatch、通用触发时机、多伤害嵌套和更深的响应链仍待后续 K5/K6/K7 切片。

## 8. AI

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
- 十种技能钩子（含反馈伤害后取牌、遗计跨手牌分配、节命目标补牌、援护跨座位弃牌恢复、武圣牌转化和龙胆响应转化）；
- 相同种子得到相同初始状态；
- 真人出牌和打闪暂停点；
- 初始发牌、处理区生命周期、单张/批量移动原子性和每个公共边界的卡牌守恒；
- 提交后通知、订阅者异常隔离、重入拒绝和观察者不影响确定性结果；
- AI 单步只执行一个决策；
- 全 AI 对局可以结束并产生解释记录；
- 快照可以序列化为 JSON。
- 当前 Console 自测共 70 项；覆盖普通/火/雷杀的类型化伤害、武圣红牌按杀及物理/有效牌型分离、龙胆闪/杀响应转化、反馈伤害后取牌与跳过、遗计私有摸牌/牌与目标组合选择及跨手牌移动、节命公开合法目标筛选/补牌至上限、援护跨座位弃牌恢复与恢复事件、命令日志 JSON 编解码与确定性重放、`DamageTriggerWindowFrame` 候选游标暂停/恢复与自动候选续接、群体父帧续接与普通视图脱敏、伤害触发候选的优先级/相对座次/技能序号/候选 ID 稳定排序、酒效一次性直接杀加伤与回合结束失效、濒死者私有酒自救、决斗交替响应、南蛮入侵/万箭齐发逐目标响应、桃园结义逐目标恢复、五谷丰登公开 draft、过河拆桥隐藏手牌盲弃、顺手牵羊距离一隐藏手牌转移和处理区驻留；全解 Release 使用隔离 `--artifacts-path` 构建，避免正在运行的 WPF 进程锁定常规输出目录。
- Revision、PromptId、精确 Choice 和过期/伪造命令的零状态变化；
- Standard 内容包 Registry 的隔离、不可变投影、重复 ID、未知引用和依赖环校验。
- K4 私有选将、共享池无重复、5 人 AI 开局终止以及同 seed + 同选择命令的快照/事件确定性。
- K5 杀链路的结算帧栈、响应窗口父子关系、JSON 序列化和伤害/濒死/胜负事件。

测试项目不用 MSTest/xUnit，是为了让这个教学 Demo 在离线环境中也能一条命令运行。
