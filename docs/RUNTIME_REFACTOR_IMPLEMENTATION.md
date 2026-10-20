# 运行时状态与调度

本文件说明当前运行时的状态归属、提交及扩展边界。内容作者入口见 [人物模块与注册](CHARACTER_MODULES.md)，窗口与原生子结算返回见 [结算协议](RESOLUTION_PROTOCOLS.md)。

## 状态归属

| 数据 | 当前拥有者 |
| --- | --- |
| revision、接受命令、待提交命令、故障与可信恢复前缀 | `CommandSessionState` |
| 实体牌及精确位置 | `CardZoneStore` |
| 结算帧集合 | `FrameStore` |
| 角色属性、标记和技能授予 | `CharacterState` / `CharacterSkillSet` |
| 程序选择、绑定、已支付状态及指令游标 | `ProgramSkillFrame` |
| 用牌材料、目标游标、效果状态和使用完成标记 | `CardUseFrame` |
| 判定、无懈、伤害、回复、濒死及死亡的暂停信息 | 各自的类型化帧和返回凭据 |
| 已签发的有期限效果、技能使用及转换状态 | 对应规则存储及 `SkillRuntimeStateStore` |

定义、编译计划与共享注册元数据不持有对局。临时操作状态放在所属帧，长期政策由明确来源和期限标识。引擎中的轻量访问句柄只定位拥有帧，不复制一份结算状态。

## 命令提交与故障

`GameEngine.CommandSession.cs` 在提交之前运行规则、同步派生技能、验证不变量，并准备新 revision 的玩家视图和事件。`CommittedEventProjection` 冻结事件的嵌套集合，命令日志中的输入也经复制。预留容量后再追加命令和事件；外部观察者与调用者收到同一次准备的结果。

规则或准备异常将会话标记为故障。若此前拥有完整接受前缀，`LastTrustedCheckpoint` 提供异常前的可信恢复数据；异常后的可变状态不用于继续游戏。交付异常保留已经可见的提交，普通观察者异常逐个隔离。派发期间拒绝重入推进。

Checkpoint 保存初始化和接受命令，在当前实现中重放；恢复校验见 [核心控制模块](CORE_CONTROL_DESIGN.md)。当前格式来源为 `Replay.cs`，规则 JSON 来源为 `SkillPrograms.cs`，内容包版本来源为各包的 `CurrentVersion`。

## 帧调度

`GameEngine.Runtime.cs` 的兼容入口转到 `NativeFrameRoutingRuntime`，由它检查帧是否存在且位于栈顶；暂停中的父帧不分派。`NativeFrameRouteCatalog` 按处理器旁的 `[NativeFrameRoute]` 注册及显式顺序选择首个匹配项，普通程序仍先运行续接，再进入执行器。用牌的各个已支付分支保留原条件与优先序。新增原生续接在所属模块注册，无需扩充中央帧 `switch`。

主循环的普通原生帧准入也由处理器旁的 `[NativeFrameAutomaticStep]` 和 `NativeFrameAutomaticRuntime` 负责，四个固定阶段槽保留开局、声明质疑和判定决策前后的原顺序。待决检查的先后、不检查待决的原生步骤以及判定窗口无需额外发布的策略分别保存；自动准入仍调用完整显式路由，不改变用牌多重回执的优先序。

`PushRuntimeFrame` 检查父关系，replace 保留 ID 和运行类型，完成操作仅弹出预期栈顶。子帧完成后使用类型化返回，父帧保留原费用、目标、事实和游标。`DamageAdjacencyRuntime` 拥有伤害候选推进和观察者链遍历；`NativeHealthRecoveryRuntime` 拥有原生回复回执与回复后伤害邻接证明；`ResponseCompletionRuntime` 拥有响应支付账单、移动和健康子帧及返回。它们通过窄事实及操作口读取原帧，不缓存另一份待决状态。

程序先运行适用的 `[ProgramContinuation]`，再进入 `SkillProgramExecutor`。注册分为三个位置：

| 位置 | 当前协议 |
| --- | --- |
| `Resume` | 活跃程序继续执行前；接受帧 ID 或程序帧，首个处理成功者结束分派。每次回调解析当前帧 |
| `Movement` | 程序移动返回开始，位于选牌支付及保留支付分支之前；接收调用者捕获的返回凭据 |
| `MovementTail` | 前述支付分支未处理时，在通用移动返回之前运行；使用同一返回凭据 |

顺序在每类注册中显式声明且唯一。注册本身不证明费用、窗口或来源有效，这些检查由处理器完成。异常直接传播。

`ProgramTriggerWindowDriver` 复用候选推进；触发程序完成后由 `[ProgramWindowReturn]` 返回唯一所属窗口。原生用牌及攻击分别通过 `[NativeUseValidation]`、`[NativeUsePreparation]`、`[NativeUseReturn]` 和 `[NativeAttackReturn]` 注册返回边界。用牌准备在弹帧前捕获本次返回事实，之后才调用返回处理器。

## 规则与输出边界

规则事件经 `AdvanceEventRulesAndQueueFact` 运行内部观察，然后排队提交；状态推进经 `AdvanceRulesAndPublishState` 处理清理和窗口后发布。`QueueGameEvent`、`PublishState` 是输出操作。

`[ProgramRuleObserver]` 的 `BeforeEvent`、`AfterEvent`、`BeforeStatePublish` 保留明确位置和顺序；嵌套事实同步完成后返回。同步订阅者收到的是提交结果，不能承担规则步骤。

复杂观察者子树通过 `GameEngine.ObserverSubtrees.cs` 复用推进与遍历。调用者保留精确根、首子帧、边、支付及完成凭据检查；不能只因子帧类型相同就获得推进权限。共享执行器不提供默认放行策略。

## 扩展注册

专用效果接口由 `[ProgramCapability]` 私有嵌套适配器提供。共享目录只保存类型和工厂；适配器按对局惰性建立。`ProgramSkillHost` 只实现共同执行及效果 host，通过 `GetCapability<T>()` 获得专用能力。

`[ProgramRuntimeHook]` 在声明前、响应体力前后、判定阶段前、回复应用前和伤害应用前等规定位置推进所属机制；死亡后和失技后的继续权限独立声明。`[ProgramFrameInvariant]` 在通用选择状态检查两侧验证所属机制。阶段不是可以互换的优先级，不能跨过已支付体力或原生声明边界移动处理器。

选择来源、卡牌/目标过滤、提交验证、目标解析、AI 修饰、动作来源及动作处理分别使用自身注册目录。只读选择模块通过 `IProgramSelectionQueries` 查询，不能取得引擎可变状态或提交入口。

## 缓存与玩家投影

`ProgramInstructionResolver` 缓存不可变计划和指令特征，`MatchSkillBindingIndex` 缓存每席位当前绑定的能力索引。规则注册表和依赖目录缓存定义事实，操作候选和当前牌区、限制、费用仍在适当时机读取。计划/索引刷新不改变已经冻结的窗口事实。

`CreateSnapshot(viewerSeat)` 通过 `Projection/PlayerViewProjector.cs` 创建玩家投影，复制和冻结公开集合并过滤私有数据。`ResolutionStack` 可含隐藏候选、私有草稿和原生材料，是可信诊断接口。

WPF 使用提交后的人类动作视图、revision、当前引擎和规范目标列表维护选择预览。界面草稿不替代实际合法动作，最终命令仍携带转换来源、目标、PromptId 和 revision。

## 验证范围

当前约束由命令、帧身份、牌区、隐私、事件冻结、支付续接和命令重放的机制检查覆盖。纯内容复用已有检查；新增状态、交互或缺陷修复补聚焦行为。开发使用名称过滤，批次运行 `tools/Test-Changed.ps1` 常规范围。完整验证仅按用户明确要求执行。

## 源码入口

- [命令会话](../src/CardGame.Core/GameEngine.CommandSession.cs)、[帧存储](../src/CardGame.Core/FrameStore.cs)、[运行调度](../src/CardGame.Core/GameEngine.Runtime.cs)
- [程序续接](../src/CardGame.Core/GameEngine.ProgramContinuations.cs)、[窗口返回](../src/CardGame.Core/ProgramWindowReturns.cs)、[原生返回](../src/CardGame.Core/ProgramNativeReturns.cs)
- [运行钩子](../src/CardGame.Core/ProgramRuntimeHooks.cs)、[规则观察](../src/CardGame.Core/ProgramRuleObservers.cs)、[能力目录](../src/CardGame.Core/ProgramCapabilities.cs)
- [观察者子树](../src/CardGame.Core/GameEngine.ObserverSubtrees.cs)、[指令计划](../src/CardGame.Core/ProgramExecutionPlan.cs)、[绑定索引](../src/CardGame.Core/MatchSkillBindingIndex.cs)
