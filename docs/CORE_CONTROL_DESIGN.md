# 核心控制模块

`GameEngine` 是对局的命令入口与规则协调者。内容注册表提供本局使用的牌、武将、技能和模式；角色状态、实体牌位置、结算帧和命令会话分别持有各自的可变数据。核心同步推进，遇到选择时保留数据帧并返回，等待下一条命令。

## 命令与提交

正常宿主与桌面操作通过 `Submit(GameCommand)` 推进状态。命令使用 `ActorSeat`、`ExpectedRevision`，回答选择时同时携带 `PromptId` 和完整选项身份。`PromptChoice` 将牌、目标、参数及内容引用绑定为一个合法选择，客户端不能把不同选项的字段重新组合。

`Commands.cs` 定义开局、推进、选将、用牌、重铸、结束阶段、弃牌、回答提示、装备效果和程序技能命令。玩家输入错误返回 `CommandError`；过期 revision、错误操作者、未知提示、未发布选项和非法材料都在正式执行前拒绝。输入中的集合由命令边界复制，接受日志不依赖调用者随后可变的对象。

成功操作的顺序由 `GameEngine.CommandSession.cs` 保证：

1. 执行规则，更新角色、牌区及所属结算帧。
2. 同步派生技能授予并检查核心不变量。
3. 准备下一 revision 的玩家视图及事件封装；嵌套集合同时冻结。
4. 预留命令、事件和通知队列容量，然后提交命令与事件。
5. 向观察者交付已经准备的结果，调用者收到同一份结果。

`CommandSessionState` 唯一持有 revision、接受日志、待提交命令、故障和可信恢复元数据。规则或准备阶段异常会将对局标记为故障；若此前由 `Submit` 完整驱动，可通过 `LastTrustedCheckpoint` 取得异常前的命令前缀。该机制不回滚异常前已经修改的所有领域对象，继续游戏需要从可信前缀重建。交付阶段的基础设施异常保留已经提交的 revision、命令及事件；普通观察者异常分别隔离。交付期间拒绝重入推进。

## 实体牌与移动

`CardZoneStore` 唯一记录实体牌位置。一张牌只能位于一个 `CardLocation`；角色所属区域带 `OwnerSeat`，摸牌堆、弃牌堆、处理区和游戏外区不带拥有者。命名持久牌区及私有暂存还具有精确来源身份。

移动通过 `MoveBatch` 校验整批原位置、重复实体和目标区域后提交，移动账本保留实体、前后位置与 namespaced 原因。使用和响应的实体材料通常经过 `Processing`；效果结束只清理本次结算仍持有的材料。被合法领取的使用材料依据本次使用凭据处理，见 [使用中实体牌](CARD_USE_MATERIALS.md)。公开展示和私下观看表示查看权限，不为实体牌建立第二个位置。

装备容量、废除状态与装备实体分开保存；判定区废除也属于角色状态。更换武将绑定不会重新建立手牌、装备、身份或体力。特殊牌区按自身契约处理失源、到期、转移与死亡，见 [区域与状态归属](REGION_STATE_AUDIT.md)。

## 结算帧与选择

`FrameStore` 持有唯一结算栈。`GameEngine.Runtime.cs` 的 push、replace、complete 操作检查唯一 ID、存活父帧、类型及栈顶：替换保持身份和类型，完成只能移除预期栈顶。子结算挂起父帧，完成后凭类型化结果返回。

用牌、响应、无懈、目标牌选择、判定、伤害、回复、濒死、死亡及技能程序均由对应帧保存游标、冻结事实和待决状态。程序帧保存技能、绑定、拥有者、实例、当前指令及已支付状态。处理器或适配器不另持一份 pending 状态；已完成的费用不会在子结算恢复时再次支付。

`PendingDecision` 和 `SkillPrompt` 由规则发布。UI 草稿只帮助构造回答；实际提交仍校验提示身份、权限及完整选项。对他人暗手牌的选择使用不透明牌位，公开装备和判定牌可以按明确实体选择。玩家只能看到自己的私有提示。

## 规则时机与输出

规则事实经 `AdvanceEventRulesAndQueueFact` 进入内部观察与窗口逻辑，再排入提交队列。状态推进经 `AdvanceRulesAndPublishState` 完成规则清理和待决窗口后准备发布。`QueueGameEvent` 与 `PublishState` 负责输出。

内部规则观察器以 `[ProgramRuleObserver]` 声明 `BeforeEvent`、`AfterEvent`、`BeforeStatePublish` 及显式顺序。嵌套事实同步完成自己的观察后返回父调用。提交后的 `EventCommitted`、日志和界面通知只观察结果，不能作为修改规则状态的触发总线。

事件在提交准备阶段经 `CommittedEventProjection` 冻结。包含集合的 record、只读列表外壳以及嵌套政策对象均需要处理，不能只依赖顶层只读属性。

## 人物、技能与规则查询

`CharacterState` 持有本局角色属性、武将绑定和 `CharacterSkillSet`。主副将的初始技能分别来自 `template:primary`、`template:secondary`；规则授予保留技能实例、来源和到期条件。移除某个授予来源不等于移除其他来源的同名技能。

`SkillProgram` 描述修正、转换、主动和触发效果。`ProgramOperationCatalog`、`ProgramCompositionValidator`、`ProgramEntryCapabilities` 与 `SkillProgramExecutor` 负责加载、能力/资源校验和执行。人物注册及扩展入口见 [人物模块与注册](CHARACTER_MODULES.md)。

持续数值规则由 `GameEngine.RuleQueries.cs` 与 `RuleQueryReducer` 聚合模式、装备、技能及有期限贡献。距离、攻击范围、次数上限和是否计入次数分别计算；目标禁令和动作限制另有明确查询。Reducer 校验来源，处理最高优先级 `Set`、`Add`、上下界和显式 `Unlimited`；同优先级的冲突 `Set` 会失败。查询返回贡献明细，查询本身不支付费用或发布选择。

## 玩家视图与恢复

普通玩家读取 `CreateSnapshot(viewerSeat)`。`Projection/PlayerViewProjector.cs` 按观察座位过滤身份、主副将、手牌、私有牌区和提示；暴露的集合冻结。`ResolutionStack`、牌区诊断、完整命令日志和可信上下文可能包含隐藏信息，只供受信任宿主使用。

Checkpoint 保存初始化选项、已接受命令前缀、revision、模式、内容包签名及内容 hash。`GameReplay.Restore` 校验格式、元数据、内容一致性，再使用当前实现逐条 `Submit`，核对恢复的 revision 和模式。格式来源为 `Replay.cs`，技能 JSON 格式来源为 `SkillPrograms.cs`，包版本由各包自己的 `CurrentVersion` 提供。

引擎仍提供兼容适配 API；混用适配调用与命令可能使 revision 和接受命令数量不一致。`CreateCheckpoint` 要求完整命令日志，并明确拒绝这种混合状态。宿主需要保存与恢复时应持续使用命令路径。

新增规则的定向检查覆盖实际变化的输入、帧返回、材料、隐私及命令重放。纯内容配置复用已有加载和机制检查；批次使用 `tools/Test-Changed.ps1` 的常规范围。完整验证与发布按 [开发门禁](DEVELOPMENT_GATES.md) 执行。

## 源码入口

- [命令](../src/CardGame.Core/Commands.cs)、[命令会话](../src/CardGame.Core/GameEngine.CommandSession.cs)、[故障边界](../src/CardGame.Core/GameEngine.FaultBoundary.cs)
- [帧存储](../src/CardGame.Core/FrameStore.cs)、[运行调度](../src/CardGame.Core/GameEngine.Runtime.cs)、[结算数据](../src/CardGame.Core/Resolution.cs)
- [牌区](../src/CardGame.Core/CardZones.cs)、[角色](../src/CardGame.Core/CharacterState.cs)、[技能授予](../src/CardGame.Core/CharacterSkillSet.cs)
- [事件冻结](../src/CardGame.Core/Projection/CommittedEventProjection.cs)、[玩家投影](../src/CardGame.Core/Projection/PlayerViewProjector.cs)、[回放](../src/CardGame.Core/Replay.cs)
