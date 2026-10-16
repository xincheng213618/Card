# 牌桌区域与状态归属

本文件说明当前 Core 状态、区域生命周期与 WPF 投影。静态查阅可以确认数据归属和展示差异，具体对局行为及前台交互仍以相应场景验证为准。

## 归属

牌桌拥有公共牌区、轮次、当前行动者、阶段、结算栈和待决提示。`CharacterState` 保存本局角色的身份、武将绑定、当前体力、状态、标记及技能授予。武将定义提供姓名、默认属性、立绘和初始技能引用。

实体牌位置由 `CardZoneStore` 唯一记录。手牌、装备、判定及特殊区域通过 `OwnerSeat` 指向角色；公开展示和私下观看表示查看权限。头像附近的区域布局属于 UI 投影，不改变实体牌区归属。

## 区域与信息

| 区域或信息 | 当前归属与可见性 | 变化边界 |
| --- | --- | --- |
| 摸牌堆、弃牌堆、处理区、游戏外区 | 本局共享位置；普通视图按协议提供数量和公开材料 | 正式移动、洗牌、使用/响应及效果完成 |
| 公共展示、私人观看 | 当前帧或规则授予的查看权限；`PublicRevealedCards` 与 `PrivateRevealedCards` 分开 | 具体窗口及权限结束；关闭展示本身不移动牌 |
| 轮、回合、阶段、当前行动者 | 本局流程；公开摘要与私有选择分开 | 实际流程边界推进 |
| 座位、显示座号、屏幕位置 | 本局 `Seat` 与 UI 显示转换分别保存 | 行动者、视角和布局变化不重写区域拥有者 |
| 身份、队伍 | 角色状态，经模式和观察者权限过滤 | 揭示、身份交换和模式规则 |
| 主副将、姓名、立绘、性别 | 武将定义与当前绑定；性别有运行时覆盖 | 换将、显隐及属性规则；主副将分别过滤 |
| 势力 | 默认资料、选择、模式及当前有效势力共同决定 | 按实际势力与公开条件查询 |
| 体力、体力上限 | `CharacterState.Hp / MaxHp`；无体力值的角色使用相应展示协议 | 伤害、失去/回复体力、上限变更及初始化 |
| 技能区 | `CharacterSkillSet` 当前授予与运行状态 | 精确来源移除、禁用、实际阶段/回合到期 |
| 状态区 | 受影响角色的连环、翻面、酒效及标记 | 各机制的消费、期限、失源或死亡规则 |
| 手牌 | `CardLocation.Hand(Seat)`；本人牌面、他人通常只看数量 | 使用、响应、弃置、转移和死亡 |
| 装备区及槽位 | 角色装备实体与容量/废除状态分别保存 | 装备替换、转移、废除、恢复及死亡 |
| 判定区 | 角色的有序延时牌位置及区域废除状态 | 使用/放置、判定、转移、区域废除及死亡 |
| 操作、响应、目标选择和确认草稿 | 规则提示与本地交互状态 | 提交命令、PromptId/revision 变化及帧闭合 |

换将 setter 更新对应模板技能来源，当前身份、体力和实体牌区保留自身状态。普通开局、交互选将和国战双将各有自己的初始化流程。

## 特殊牌区生命周期

区域拥有者、效果来源和失去来源后的清理条件分别记录。

| 牌区或机制 | 当前拥有者与权限 | 清理与续接 |
| --- | --- | --- |
| `WoodenOxGrain` | 角色所属，关联木牛装备；数量公开，本人可看牌面 | 木牛装备转移时随之转移；其他离开装备区的情况清理粮 |
| `BuquWound`、`Authority`、`Chunlao` | 各自拥有者的专用区域；当前投影公开牌面/数量 | 按各自取用及死亡流程处理，失技政策由具体机制决定 |
| `PrivateReserve` | 角色私有储备；数量公开，本人看牌面 | 支持交换、消耗和死亡清理；失源条件按实际能力处理 |
| `PublicPersistentPile` | 拥有者、技能、实例及命名位置；牌面公开 | 死亡移动实体；失源清理在回合开始续接，有持久来源例外 |
| `PublicDeferredPile` | 角色公共寄存区；deposit 保存来源和实体 | 指定结算时消费；死亡和失源按对应入口清理 |
| `PojunHold` | 被扣牌角色所属，当前数量与牌面公开 | 实际回合末归还，死亡弃置 |
| `RoundHold` | 被扣牌角色所属，快照主要提供数量 | 本轮对应边界归还，死亡弃置 |
| 普通 `PrivateTurnHold` | 位置拥有者与精确 `HoldId`、来源、实例、到期回合；按权限看牌 | 到期实际回合末返还，拥有者死亡弃置 |
| `PrivateTurnHold.DeferredOffer` | 实体位于来源方，另记录处理目标；来源方知道牌面，目标在正式查看窗口获得权限 | 目标后续实际回合开始前专门处理；普通回合末返还明确跳过此类 |
| `PrivatePersistentPile`：伏兵、旋 | 每份 deposit 保留实例和独立位置；数量公开，牌面/记录牌名按权限 | 失源或死亡清理；使用消耗和后续阶段到期各按机制处理 |
| 咒 | 实体位于目标角色的公共牌堆，保留另一角色的来源 | 判定、移去和目标死亡等对应流程 |

公共持久牌堆目前同时提供单来源标量和多来源集合。WPF 优先读取集合，再回退到标量，避免同一牌堆重复呈现。这些是现用投影字段，实体身份仍由底层精确位置和来源记录管理。

## 当前展示边界

- Core 玩家快照提供 `IsEquipmentAreaAbolished`、`IsJudgmentAreaAbolished` 和 `EquipmentSlotCapacities`。当前 WPF 未消费前两个字段，槽位模型也缺少完整的废除/不可用状态。整区废除时可能显示普通空槽，零容量类别可能直接没有槽位项目，空判定区缺少废除提示。
- `RebuildTurnHandHoldViews` 对所有暂存写入“回合结束时返还”。普通暂存与 `DeferredOffer` 共用该文案，但后者实际在目标后续回合开始前处理；当前提示不能准确表达两类生命周期。
- `SkillGrant` 保存技能实例、来源、回合/阶段到期。技能运行快照主要提供技能、获得标志、次数和转换状态，技能栏通常只显示人物名或“获得技能”，尚不能完整说明同技能多来源和各自期限。
- `ProjectSeatStatuses` 将状态标记和特殊牌堆摘要合入徽标列表，超过展示数量后使用“+N”摘要。详情面板已经存在，但通用徽标模型没有完整的类别、来源、期限及详情目标，部分机制仍依靠专用快照。
- 特殊牌堆各 DTO 的展示名、权限及生命周期摘要并不统一。`SeatViewModel.PrivateReserveText` 当前固定显示“七星”，区域模板沿用“星”的说明。
- 公共持久/寄存牌堆的失源清理在 `ContinueTurnAfterForeignActualContests` 的回合开始续接执行，伏兵和旋的失源清理通过规则观察器执行。死亡另有直接实体清理。文档和 UI 不能把所有牌堆概括为同一失源清理时点。

这些静态展示差异不等同于实体牌位置错误。失源后暂留是否符合某项具体技能，需要按其规则与真实失技/暂停路径核对。

## 已有检查入口

| 边界 | 检查类 |
| --- | --- |
| 同技能多来源、独立授予 | `CharacterSkillSetChecks`、`SkillOwnershipChecks` |
| 实际回合和出牌阶段授予到期 | `ActualTurnTemporarySkillChecks`、`PlayPhaseTemporarySkillChecks` |
| 公共牌堆投影、冻结和多来源 | `PublicPileProjectionChecks`、`PublicPileSkillUiChecks` |
| 私有牌面、顺序和清理 | `PrivateReserveOrderChecks`、`PrivateWhirlChecks`、`NamedAmbushAndSourceHandChecks` |
| 延后暗置 | `OrdinaryChenLinChecks` |
| 状态接收者、数量、溢出及消失 | `SeatStatusUiChecks` |
| 公私有详情、暂存与木牛来源 | `DeferredCardsUiChecks`、`RuleInformationUiChecks`、`PrivateReserveUiChecks` |

新增检查使用现有名称过滤和 `tools/Test-Changed.ps1` 常规范围，聚焦实际未覆盖的行为。完整验证需用户明确要求。

## 代码依据

- [角色](../src/CardGame.Core/CharacterState.cs)、[技能授予](../src/CardGame.Core/CharacterSkillSet.cs)、[牌区账本](../src/CardGame.Core/CardZones.cs)、[玩家投影](../src/CardGame.Core/Projection/PlayerViewProjector.cs)
- [公共持久牌堆](../src/CardGame.Core/GameEngine.PublicPersistentPiles.cs)、[公共寄存](../src/CardGame.Core/GameEngine.DeferredCards.cs)、[公共来源](../src/CardGame.Core/GameEngine.PublicPileSources.cs)
- [私有暂存](../src/CardGame.Core/GameEngine.PrivateTurnHolds.cs)、[延后暗置](../src/CardGame.Core/GameEngine.PrivateCardOffers.cs)、[伏兵](../src/CardGame.Core/GameEngine.NamedAmbush.cs)、[旋](../src/CardGame.Core/GameEngine.PrivateWhirl.cs)
- [槽位投影](../src/CardGame.Wpf/ViewModels/MainViewModel.TableSurface.cs)、[座位模型](../src/CardGame.Wpf/ViewModels/SeatViewModel.cs)、[区域模板](../src/CardGame.Wpf/Themes/TableTheme.xaml)
- [暂存展示](../src/CardGame.Wpf/ViewModels/MainViewModel.TurnHandHolds.cs)、[技能展示](../src/CardGame.Wpf/ViewModels/MainViewModel.Presentation.cs)、[状态投影](../src/CardGame.Wpf/ViewModels/MainViewModel.SeatStatuses.cs)
