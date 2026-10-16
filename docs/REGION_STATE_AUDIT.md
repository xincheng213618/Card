# 牌桌区域与状态归属审查

审查日期：2026-10-17。范围为当前工作区的 Core 状态、初始化、玩家快照、生命周期入口及 WPF 区域投影。工作区存在并行开发改动；本文描述本次查阅的实现，不代表某个已发布版本。

本次是静态代码审查和文档整理，没有修改规则、快照协议、历史兼容、版本号或 UI 行为，没有运行行为测试或前台交互验收。下文分别标明已确认的展示缺口、维护问题及需要进一步验证的场景。

## 1. 归属原则与当前结论

- 本局牌桌拥有公共牌区、轮次、当前行动者、阶段、结算栈及等待中的决定。
- 场上角色实例拥有当前身份、武将绑定、体力、公开状态、标记及技能授予关系。
- 武将定义提供姓名、立绘、默认势力、基础体力上限、可选初始体力和初始技能引用，不保存对局过程。
- 技能定义和公共机制负责规则；技能授予记录负责谁拥有哪个实例、来自哪里、何时失效。
- 实体牌的位置由 CardZoneStore 唯一记录。手牌、装备和判定等区域虽由牌桌的统一账本管理，规则归属仍通过 OwnerSeat 指向角色。
- UI 区域是玩家可见状态的投影。放在头像附近不表示数据属于武将模板；同时显示一张牌也不意味着该牌真的处于多个牌区。

现有 Core 已具备这些主要边界，不能归纳成“人物与所有区域混在一起”。主要缺口集中在 UI 未消费已有区域状态、不同生命周期共用文案、来源与期限信息被简化，以及特殊牌堆的展示入口不统一。依据：[角色实例][E1]、[技能授予][E2]、[卡牌账本][E3]、[玩家投影][E4]。

## 2. 区域和信息总表

| 区域或信息 | 实际归属 | 初始来源 | 正常玩家视图的可见性 | 变化、失效或清理条件 |
| --- | --- | --- | --- | --- |
| 摸牌堆、弃牌堆、处理区、游戏外区 | 本局 CardZoneStore，共享位置不带 OwnerSeat | 牌区构造和本局牌堆配置；发牌使用正式移动 | 快照提供牌堆数量；处理区数量受私密结算过滤。牌面公开必须走明确的公开展示投影 | 出牌、响应、效果完成、弃置、洗牌等移动；不会随换将重建 |
| 公共展示牌、私人观看牌 | 规则指定的展示权限，不是新的实体牌存放区域 | 当前帧、公开手牌或私有观看记录 | PublicRevealedCards 与 PrivateRevealedCards 分开；私人决定仅给对应 viewer | 跟随具体结算窗口和观看权限消失；显示结束不自动等于弃牌 |
| 轮次、回合、阶段、当前行动者 | 本局流程 | 模式与开局流程 | 快照公开当前流程；具体私有选择内容单独过滤 | 按实际流程边界推进，不能按 UI 计时或人物刷新重置 |
| 座次、显示座号、屏幕位置 | 本局座位关系及 UI 布局 | Seat 开局分配；DisplaySeatNumber 按模式和主公座位转换 | 座位公开；显示号与内部 Seat 分开 | 当前 Seat 是不可变身份索引；行动者变化和观察视角变化不改变牌区拥有者 |
| 身份、队伍 | CharacterState | 模式开局分配；不由武将推导 | 身份根据本人、主公、公开状态和模式过滤；国战用势力关系 | 揭示、实际身份交换和模式规则改变；换将 setter 不重置身份 |
| 主副将、姓名、立绘、性别 | 武将定义与角色的当前绑定；性别允许运行时覆盖 | ContentGeneralDefinition / GeneralDefinition，选将流程 | 主副将分别检查已选、明置、本人权限；隐藏身份机制另有投影 | 换将或明置更新对应绑定；主副将技能来源分别处理 |
| 势力 | 默认资料与本局有效势力共同决定 | 武将 FactionId、国战配置、选定势力等 | GetEffectiveFactionId 的结果仍经过公开条件过滤；UI 的印刷势力回退来自可见武将 | 按模式、选择、明置或特定机制变化；不能只读静态人物势力 |
| 体力、体力上限、血条 | CharacterState.Hp / MaxHp | 基础上限与 InitialHp 经模式、身份或双将初始化规则计算 | 通常公开；HpUndefined 时不以零血角色呈现；诊断模式另论 | 伤害、失去/回复体力、增减上限、死亡等；换将 setter 本身不重新初始化体力 |
| 技能区 | CharacterSkillSet 的当前授予与技能运行时状态 | 主副将初始技能，加上规则授予、阶段授予、回合授予等 | 已知武将的当前技能经过 OwnedRuntimeSkills 投影；暗将与禁用条件另外判断 | 按 SourceId 删除对应授予；临时技能按精确回合/阶段结束清理，永久来源应保留 |
| 状态区 | 受影响角色的状态；来源可能属于另一角色 | 技能、卡牌、模式及规则事件 | Markers、连环、翻面、酒效等公开状态进入快照；不应反推暗牌 | 各自独立的消耗、到期、来源条件或死亡条件；不存在统一“回合结束全清”规则 |
| 手牌区 | CardLocation.Hand(Seat) | 正式开局发牌，以及后续移动 | 本人看牌面，其他人通常只看数量；规则明确公开的牌走单独展示 | 使用、响应、弃置、转移及死亡清理 |
| 装备区与槽位 | 角色所属 Equipment 区，以及角色的槽位容量/废除状态 | 公共装备槽默认容量；装备由实际卡牌移动产生 | 装备牌面、区域废除和显式槽位容量已进入快照 | 装备替换、转移、废除、恢复、死亡；离开装备区时处理附属效果和牌堆 |
| 判定区／延时锦囊区 | 角色所属 Judgment 区；保留顺序和有效牌身份 | 使用、转化或技能放置延时牌 | 判定区牌面公开；IsJudgmentAreaAbolished 也已投影 | 判定、无懈后处理、转移、区域废除和死亡；判定阶段不是一个永久存牌区 |
| 特殊牌区 | 具体牌区拥有者，加上技能实例、装备或寄存来源 | 规则建立或往已有空区放牌；不是每个武将创建全部特殊牌区 | 按每类牌堆的权限投影，不能统一公开或统一隐藏 | 依具体牌堆的清理契约，见下一表 |
| 操作区、响应区、目标选择、确认/取消、倒计时 | 本地交互状态与引擎发布的决定 | 当前合法动作、PromptId、Revision 和拥有帧 | 私有 Prompt 只给决定者；公共等待提示不包含选择明细 | 接受命令、新快照、Prompt 更换或帧闭合时更新；UI 草稿不能自行支付代价 |

定位入口：[E1]–[E7]、[主窗口][E8]、[牌桌投影][E9]、[技能展示][E10]。

## 3. 特殊牌区的归属与生命周期

“技能创建了牌堆”“牌堆归某角色所有”“失去技能后清空”是三个独立判断。

| 牌区/机制 | 拥有者和初始来源 | 可见性 | 已查到的清理或续接边界 |
| --- | --- | --- | --- |
| WoodenOxGrain（木牛粮） | 角色所属区域，来源关联木牛装备 | 数量公开，牌面依本人权限；本方操作区允许以带来源标记的牌显示 | 木牛从装备区转给他人时随之转移；其他离开装备区的情况弃置粮。不能简单按技能丢失清理 |
| BuquWound、Authority、Chunlao | 各自拥有者的专用牌区，技能效果放入 | 当前投影公开牌面/数量 | 有取用与死亡清理；不同专用机制的全部失技分支未在本次逐一复现，不作统一清理承诺 |
| PrivateReserve | 角色所属的私有储备区，InitializePrivatePile 放牌 | 数量公开；本人可见牌面，其他人无牌面 | 已确认交换/消耗和死亡清理；本次未确认统一的“来源技能丢失即清空”入口，需按具体规则决定是否需要 |
| PublicPersistentPile | OwnerSeat + SkillId + SkillInstanceId，支持命名位置与多个来源 | 牌面公开；单来源兼容标量与多来源集合共存 | 死亡流程移动实体牌；失源检查在 ContinueTurnAfterForeignActualContests 调用，存在 IsDurableZengdaoPublicPile 例外；禁用与失去授予不等价 |
| PublicDeferredPile | 角色所属公共寄存区；各份 deposit 记录来源和实体牌 | 当前公开牌面、数量；名称从 deposit 的来源技能展示读取 | 指定结算时消费；死亡清理，失源检查在 ContinueTurnAfterForeignActualContests 调用；不能丢掉来源账本只保留牌数 |
| PojunHold | 被扣牌角色所属牌区，技能效果放入 | 当前快照公开扣牌数量与牌面 | 实际回合末归还，死亡弃置 |
| RoundHold | 被扣牌角色所属牌区 | 当前快照主要提供数量 | 本轮对应边界归还，死亡弃置；不能套用回合末文案 |
| PrivateTurnHold：普通暂存 | 所在位置 OwnerSeat，精确 HoldId、SourceId、技能实例和 ExpiresTurnNumber | 按投影权限显示数量与本人牌面 | ReturnPrivateTurnHoldsAtTurnEnd 处理到期普通暂存；拥有者死亡弃置 |
| PrivateTurnHold：DeferredOffer | 实体仍在来源方的暂存位置，另有关联 TargetSeat；快照按接收处理方归组并携带 DeferredSourceSeat | 来源方可知牌面；目标方只在正式查看窗口获得权限；其他人只看摘要 | 明确排除普通回合末返还；进入目标后续实际回合开始前的专用结算，交换或移去；相关角色死亡有清理 |
| PrivatePersistentPile：伏兵、旋 | 每份 deposit 记录来源技能实例和独立位置 | 数量公开；牌面/记录牌名仅给拥有者，诊断投影另论 | 实例来源丢失或死亡清理；伏兵按使用消耗，旋另有后续出牌阶段结束的到期逻辑 |
| SourceCurses（咒） | 实体牌属于目标角色，保留另一角色的来源记录 | 牌面公开，WPF 作为目标所属公共牌堆显示 | 被用作判定、移去或目标死亡等对应流程；不按“来源在别人身上，所以牌堆归别人”处理 |

依据：[卡牌移动与死亡][E5]、[公共来源][E11]、[公共清理][E12]、[公共寄存][E13]、[私有暂存][E14]、[延后暗置处理][E15]、[伏兵][E16]、[旋][E17]、[咒][E18]、[破军扣牌][E19]。

## 4. 已确认缺口和仍混合的地方

### F1 · 优先：区域可用状态已在 Core，WPF 没有完整消费

PlayerSnapshot 已提供 IsEquipmentAreaAbolished、IsJudgmentAreaAbolished 和 EquipmentSlotCapacities。当前 WPF 中未找到前两个字段的消费；EquipmentSlotViewModel 也没有“废除/不可用”状态。

HumanEquipmentSlots 只用容量和现有牌数生成项目：整区废除时可能继续显示普通空槽，某类槽容量变为零时可能直接没有该槽的项目。空判定区也没有对应的废除提示。这是展示缺口；本次没有据此认定引擎允许非法装备或延时牌。

建议：保留区域/槽位自己的状态，明确区分空、占用、废除和多槽；不要以隐藏整个区域代替不可用状态。

定位：[玩家投影][E4] 的区域字段、[槽位投影][E9] 的 HumanEquipmentSlots / EquipmentSlotViewModel、[座位模型][E20]、[区域模板][E21]。

### F2 · 优先：普通暂存与延后暗置共用错误的到期提示

ReturnPrivateTurnHoldsAtTurnEnd 明确跳过 DeferredOffer；DeferredOffer 以专门标记保存，并在目标后续实际回合开始前进入专用结算。ProjectPrivateTurnHolds 却将两类对象投进同一个列表，而 RebuildTurnHandHoldViews 对所有对象写死 ReturnHint 为“回合结束时返还”。

因此，对于延后暗置对象，该提示与实际生命周期不符。这是静态可确认的条件分支与文案不一致；本次未运行完整对局复现。

建议：在玩家可见投影中提供暂存种类、到期语义及来源/处理目标的关系，再分别生成文案。不得从某个特殊数值推断文案，也不能为了迁就提示而改动规则期限。

定位：[E14] 的 ReturnPrivateTurnHoldsAtTurnEnd / ProjectPrivateTurnHolds、[E15] 的 TryBeginDeferredPrivateOffers、[暂存展示][E22] 的 RebuildTurnHandHoldViews。

### F3 · 技能来源与期限在规则层完整，在展示层被简化

SkillGrant 已记录 GrantId、SkillId、SkillInstanceId、SourceId、TurnExpiry、PhaseExpiry。角色初始技能分别来自 template:primary 与 template:secondary。

但 SkillRuntimeStateSnapshot 主要提供 SkillId、IsAcquired、次数和转换状态，没有通用的授予来源/期限列表。BuildOwnedHumanSkillCards 多数只能显示人物名或“获得技能”，并存在父魂来源的专用判定。同一个技能由永久和临时来源同时持有时，UI 无法完整解释每一份来源及其期限；这不代表底层会把永久授予一起删掉。

建议：新增经过权限过滤的技能授予展示摘要，让 UI 按技能归组，仍保留各份来源和失效条件。不要直接把完整内部授予对象交给普通客户端。

定位：[E2]、[运行时快照][E6] 的 SkillRuntimeStateSnapshot、[E10] 的 BuildOwnedHumanSkillCards、[回合授予][E23]、[阶段授予][E24]。

### F4 · 状态徽标与特殊牌堆共用列表，通用来源/期限不足

ProjectSeatStatuses 将普通标记、连环、翻面、酒效、创牌、权、醇、扣牌和公共/私有牌堆摘要放入同一 StatusBadges 列表。超过五个项目时，座位以四个项目加“+N”摘要显示。

这只是 UI 合并，不是实体牌区混乱；公共牌堆、伏兵/旋和暂存手牌已经有独立详情面板。实际缺口是通用 SeatStatusViewModel 仅有 Label、Tooltip、Count 等展示字段，没有统一的状态/牌堆类别、来源、期限或详情目标。PlayerMarkerSnapshot 也只有种类、名称、数量，无法通用展示内部 MarkerSourceCounts 的来源差异；部分机制已有专用快照，应复用。

建议：状态、牌堆入口按类别分组；摘要允许合并，但来源和期限应在详情可查，并为重要限制保留可辨识的入口。

定位：[状态投影][E25]、[状态模型][E26]、[E20]、[公共牌详情][E27]、[私有牌详情][E28]、[E22]。

### F5 · 特殊牌堆展示协议分散，不能通过删除兼容字段解决

当前同时存在 Authority、Chunlao、PrivateReserve、公共寄存、单/多公共持久牌堆、伏兵、旋和暂存等专用字段。公共持久牌堆内部有精确实例身份，但各类 DTO 的展示字段和生命周期摘要不一致。

例如单来源公共牌堆仍使用兼容标量；多来源时提供 PublicPersistentPiles，WPF 已通过集合优先、标量回退避免重复。不能把这些旧字段误判为重复状态并直接删除。

建议：先在玩家视图/WPF 适配层统一牌堆展示描述，包括拥有者、来源、显示名、数量、允许查看的牌、查看权限和生命周期提示；底层保留有语义差异的区域与现有兼容投影。

定位：[E4]、[E11]、[E27]、[E28]、[E14]。

### F6 · 私有储备区的通用名称与七星专用展示耦合

Core 的 InitializePrivatePile / PrivateReserve 是通用名称；SeatViewModel.PrivateReserveText 则固定为“七星[数量]”，区域模板提示也固定称“星”。当前七星用途能对应上，不能据此判定现有规则错误；新增其他私有储备用途时容易出现错误标签和查看说明。

建议：明确将它保留为七星专用适配，或从合法来源的展示元数据取得名称与查看说明；不要求为了名称而改历史区域枚举或存档。

定位：[私有储备与来源标记][E29]、[E20] 的 PrivateReserveText、[E21] 的 PrivateReserveBadge。

### F7 · 需核实：不同牌堆的失源清理发生在不同流程边界

CleanupLostPublicPersistentPiles 和 CleanupLostDeferredPileSources 当前仅在 ContinueTurnAfterForeignActualContests 的回合开始续接中被调用；伏兵和旋的失源清理则注册在 BeforeEvent / BeforeStatePublish。死亡流程另有直接移动实体牌的清理，不能与失去技能的分支混为一谈。

因此，不能在文档或 UI 中把所有牌堆承诺为“失去技能后立即清空”。公共牌堆在回合中失去来源后是否可能暂留到后续回合、这种保留是否符合具体技能规则，需要沿真实失技命令和暂停窗口验证。本次确认的是清理入口时点不同，尚未据此断定某个技能发生结算错误。

建议：先补充具体来源丢失场景的边界证据，再决定是否需要即时清理；改变清理时点可能影响事件次序、卡牌移动触发和回放语义，不能仅为统一 UI 而改动。

定位：[E5] 的 ContinueTurnAfterForeignActualContests、[E12] 的 CleanupLostPublicPersistentPiles、[E13] 的 CleanupLostDeferredPileSources、[E16] / [E17] 的清理观察器。

### F8 · 架构文档仍使用旧状态名和不准确的手牌归属说明（本次已修正）

ARCHITECTURE.md 原先仍称 PlayerRuntime 保存身份和手牌。当前是 CharacterState 保存角色属性与技能授予，CardZoneStore 记录实体牌位置。本次已更新名称和说明，并增加本文入口；未改运行时代码。

## 5. 已存在的边界与检查，不应重复建设

- 武将绑定 setter 只更新对应初始技能来源；体力、身份和实体牌区域不因此整体重建。
- 初始体力和当前体力已经分开；普通开局、交互选将和国战双将有不同初始化入口，不能仅修改一个入口后宣称全部覆盖。
- 玩家快照已经按 viewerSeat 过滤手牌、身份、主副将及私有牌堆，并冻结暴露的集合；诊断 revealAll 是另一种权限，不能作为普通玩家权限依据。
- 公共展示区、私人观看区、公共牌堆详情、私有牌堆详情和暂存详情已经存在，不能统称“特殊牌区完全缺失”。
- 临时授予以真实回合/阶段身份到期；效果帧有自己的续接与已支付状态，不能用 UI 生命周期替代。
- 技能被禁用、技能授予被移除、来源方死亡和接收方死亡是不同条件。清理入口必须遵守各机制的契约。

静态查阅到的现有检查入口如下。本次未执行，不能作为本轮通过结果：

| 边界 | 已有检查入口 |
| --- | --- |
| 同技能多份来源与独立授予 | CharacterSkillSetChecks、SkillOwnershipChecks |
| 实际回合和出牌阶段授予到期 | ActualTurnTemporarySkillChecks、PlayPhaseTemporarySkillChecks |
| 公共牌堆投影、冻结和多来源 | PublicPileProjectionChecks、PublicPileSkillUiChecks |
| 私有牌堆牌面和后续清理 | PrivateReserveOrderChecks、PrivateWhirlChecks、NamedAmbushAndSourceHandChecks |
| 延后暗置在目标后续实际回合处理 | OrdinaryChenLinChecks.DeferredPrivateOfferTargetsNextActualStartAndExchangeReturnsOnce |
| 状态跟随接收者、计数、溢出和消失 | SeatStatusUiChecks.PublicStatusesFollowRecipientsAndClear |
| 公私有详情、暂存、木牛来源展示 | DeferredCardsUiChecks、RuleInformationUiChecks、PrivateReserveUiChecks |
| 装备槽数量和操作区 | TableSurfaceChecks.ActionDockSelection；已查阅的该检查覆盖多槽，不能据此认为废除状态展示已覆盖 |

## 6. 建议实施顺序及验收范围

1. 先修 F1、F2：明确区域不可用状态和两种暂存的到期提示，增加对应缺口的 WPF 行为检查。
2. 再补 F3、F4：经权限过滤的来源、期限、类别和详情入口；覆盖同名多来源、临时来源到期后永久来源仍在，以及他人暗信息不外泄。
3. 核实 F7 的真实失技流程，再处理 F5、F6 的展示适配。覆盖多牌堆、同名不同来源、空区、来源失去、死亡和相关装备转移；不要为统一展示而合并所有底层牌区或更改清理时点。

未来修复使用现有按名称过滤的检查和 tools/Test-Changed.ps1 的例行范围；不因本文或审查完成自动运行全量验证，不给每个武将增加重复定义检查。静态问题不等于所有技能已逐一对局验证，也不构成鼠标、键盘、多 DPI 或前台视觉验收。

## 代码依据

[E1]: ../src/CardGame.Core/CharacterState.cs
[E2]: ../src/CardGame.Core/CharacterSkillSet.cs
[E3]: ../src/CardGame.Core/CardZones.cs
[E4]: ../src/CardGame.Core/Projection/PlayerViewProjector.cs
[E5]: ../src/CardGame.Core/GameEngine.cs
[E6]: ../src/CardGame.Core/Domain.cs
[E7]: ../src/CardGame.Core/ContentRegistry.cs
[E8]: ../src/CardGame.Wpf/MainWindow.xaml
[E9]: ../src/CardGame.Wpf/ViewModels/MainViewModel.TableSurface.cs
[E10]: ../src/CardGame.Wpf/ViewModels/MainViewModel.Presentation.cs
[E11]: ../src/CardGame.Core/GameEngine.PublicPileSources.cs
[E12]: ../src/CardGame.Core/GameEngine.PublicPersistentPiles.cs
[E13]: ../src/CardGame.Core/GameEngine.DeferredCards.cs
[E14]: ../src/CardGame.Core/GameEngine.PrivateTurnHolds.cs
[E15]: ../src/CardGame.Core/GameEngine.PrivateCardOffers.cs
[E16]: ../src/CardGame.Core/GameEngine.NamedAmbush.cs
[E17]: ../src/CardGame.Core/GameEngine.PrivateWhirl.cs
[E18]: ../src/CardGame.Core/GameEngine.SourceCurses.cs
[E19]: ../src/CardGame.Core/GameEngine.ProgramHoldCards.cs
[E20]: ../src/CardGame.Wpf/ViewModels/SeatViewModel.cs
[E21]: ../src/CardGame.Wpf/Themes/TableTheme.xaml
[E22]: ../src/CardGame.Wpf/ViewModels/MainViewModel.TurnHandHolds.cs
[E23]: ../src/CardGame.Core/GameEngine.ActualTurnSkillGrants.cs
[E24]: ../src/CardGame.Core/GameEngine.PlayPhaseSkillGrants.cs
[E25]: ../src/CardGame.Wpf/ViewModels/MainViewModel.SeatStatuses.cs
[E26]: ../src/CardGame.Wpf/ViewModels/SeatStatusViewModel.cs
[E27]: ../src/CardGame.Wpf/ViewModels/MainViewModel.DeferredCards.cs
[E28]: ../src/CardGame.Wpf/ViewModels/MainViewModel.NamedPrivatePiles.cs
[E29]: ../src/CardGame.Core/GameEngine.ParticipantAndReservePrograms.cs
