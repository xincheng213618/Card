# 架构说明

## 程序边界

| 层 | 职责 | 入口 |
| --- | --- | --- |
| Core | 命令校验、规则状态、牌区移动、结算、玩家投影、AI 与回放 | [GameEngine.cs](../src/CardGame.Core/GameEngine.cs) |
| Content.Standard | 装载卡牌、人物、技能、牌堆和模式，生成不可变 Registry | [ContentRegistry.cs](../src/CardGame.Core/ContentRegistry.cs)、[人物注册](CHARACTER_MODULES.md) |
| Characters | 按人物与版本保存资料、技能 JSON、专属运行能力和可选界面适配 | [人物目录](../src/Characters/README.md) |
| WPF | 展示玩家快照、提交操作、调度自动推进、保存本机数据及播放反馈 | [MainViewModel.cs](../src/CardGame.Wpf/ViewModels/MainViewModel.cs) |

Core 不依赖 WPF。人物运行模块链接编译到 Core，内容 JSON 嵌入内容程序集，可选 WPF 模块编译到界面程序集。目录、注册顺序、内容 ID 和逻辑资源名共同遵守 [人物模块约定](CHARACTER_MODULES.md)。

## 命令与提交

WPF 的开局、选将、出牌、技能、响应、弃牌和推进通过 `Submit(GameCommand)` 进入同一会话。命令是可序列化数据，携带行动者和预期 Revision；回答询问时还需匹配 PromptId 与当前公布的精确候选。无效输入返回 `CommandError`，不推进牌局。

一次已接受操作的边界由 [GameEngine.CommandSession.cs](../src/CardGame.Core/GameEngine.CommandSession.cs) 管理：

1. 执行规则，累计待提交的事实与通知。
2. 同步技能投影，检查状态和牌区不变量。
3. 为新 Revision 准备玩家快照、返回结果和冻结后的事件载荷，并预留追加容量。
4. 提交命令记录与事件历史。
5. 派发提交后的通知，返回同一份已准备结果。

快照与事件中的嵌套集合在提交准备阶段冻结。[CommittedEventProjection](../src/CardGame.Core/Projection/CommittedEventProjection.cs) 负责带集合的内置事件；新增事件载荷也需在此覆盖。观察者无法通过返回对象修改已提交结果，派发期间的重入操作被拒绝。

普通订阅者异常进入 `ObserverFailures`，后续订阅者继续接收通知。提交后的基础设施派发异常保留已经可见的命令、Revision 和事件；规则执行或提交准备失败则将会话标为故障，并在可用时提供失败前的 `LastTrustedCheckpoint`。故障会话须从可信命令前缀重建，详见 [异常材料](EXCEPTION_REPORTS.md)。

## 推进与暂停

`AdvanceCommand` 推进到玩家输入或终局边界；`AdvanceOneStepCommand` 提交一个状态机步骤，供 WPF 计时器控制节奏。桌面将 `AdvanceAfterHumanCommands` 设为 false，玩家命令后由计时器继续推进。

`PendingDecision` 表达当前询问，合法动作和 Choice 由 Core 生成。界面提交已公布的牌、目标或候选，不自行拼接私有信息或推算规则结果。暂停不占用一个阻塞等待输入的线程。

进行中的结算保存在所属 `ResolutionFrame` 中，包括父子身份、冻结上下文、候选游标、指令位置、支付状态和类型化返回点。子帧通过运行时 push、replace、complete 操作推进；完成后返回相应父帧，已完成的代价不会再次支付。协议见 [结算协议](RESOLUTION_PROTOCOLS.md)。

## 规则事件与技能执行

规则事实通过 `AdvanceEventRulesAndQueueFact` 进入注册观察链：先执行 `BeforeEvent`，再入输出队列，最后执行 `AfterEvent`。状态推进通过 `AdvanceRulesAndPublishState` 先处理状态规则，再打开人物状态、体力和牌区移动窗口。`QueueGameEvent` 与 `PublishState` 负责输出。

技能规则与中文展示分别存储。加载器校验格式、节点参数和引用；Registry 校验内容依赖并编译执行计划。人物优先复用现有配置能力，需要共享机制时才增加运行模块。主要扩展点为：

- capability 适配器提供类型化效果接口，实例按对局创建。
- selection、legality 和 action 模块负责只读候选生成及提交重验。
- rule observer、runtime hook 与 trigger window 模块保留明确的执行阶段和顺序。
- continuation、window return 与 native return 协议把子结算结果交回正确的父帧。

共享注册元数据与不可变计划可以缓存，对局中的候选、支付和游标由当前会话与帧拥有。具体接口见 [人物注册](CHARACTER_MODULES.md) 和 [技能配置](content/skill-composition/RUNTIME.md)。

## 卡牌、伤害与阶段

[CardZoneStore](../src/CardGame.Core/CardZones.cs) 维护实体牌唯一位置与原子移动，覆盖牌堆、手牌、处理区、装备、判定、弃牌及专属区域。实体牌身份、有效牌型与技能转化来源分别记录；公开展示不必改变牌所在区域。用牌声明、成本、目标、效果及收尾见 [卡牌生命周期](CARD_LIFECYCLE_CONTRACT.md)。

用牌、响应、无懈、判定、伤害、回复、濒死和死亡通过嵌套结算帧衔接。伤害实际应用后，先处理受限的 `DamageAppliedBeforeDying` 窗口；体力不足时进入濒死，随后处理 `AfterDamageApplied` 窗口。触发候选、距离、实体材料及继续位置按所属帧冻结，多目标与连环结算使用各自游标。

`ActualPhaseEndFrame` 记录实际阶段结束的候选、子窗及返回位置；跳过的阶段不会生成实际结束窗口。插入摸牌属于原有摸牌义务，恢复原阶段不创建新阶段。`ActualTurnCursorReturnedEvent` 表示回合结束后的延后子结算已返回，实际回合游标即将推进。

`WholeActualTurnUseSequence` 在真实用牌声明时冻结顺序，防御杀所使用的闪和无懈可击也各占一次；打出、提供与重铸沿各自原生语义处理。屏忠读取本次声明及之前的记录，并沿原生使用者变更链核对实际使用者与提供者。防御闪与攻击用牌共享来源帧时，仍保持自己的响应身份。此计数延续到实际回合游标返回，覆盖回合末插入的出牌阶段；已有按 `TurnEnded` 截止的计数器保留原边界。

整次弃置在语义操作边界记录材料、原区域、发起者、执行者和实际移动范围；跨多个选牌提示的费用由所属程序帧保存付款凭据。普通用牌、响应、重铸和区域清理各自保持明确的移动原因，不能仅以逐张移动推断一次弃置。

有效势力由运行时状态和当前有效技能来源计算，印刷势力保留在内容定义中。开局神将选势力、回合内实际势力变更及国战明置分别遵守相应模式和公开信息边界。

清流的开局选择通过同一有效势力状态及查询缓存生效，开局声明不产生实际回合中的势力变更事实。首次濒死被救回后的变更复用真实濒死退出的类型化返回点；参与者、当前候选与已完成的濒死凭据必须一致。拥族的临时技能按精确授予来源在受益者下个真实 `TurnStarted` 到期，永久手牌上限加成由已经执行的选择事实累计。

归命通过单独的“已受伤”谓词影响条件、目标资格、人数统计和相关授技，不改写真实体力或已损失体力数量。旧程序中精确的“当前体力小于本人体力上限”条件也读取这一谓词；其他数值比较保持原值。触发窗口冻结其参与者的谓词结果。只有内容具备此能力时才添加可空事实字段，原生回复仍按真实回复空间结算。

悍勇的加伤资格在真实用牌声明时冻结并由用牌帧持有，伤害来源变更不改变这份用牌收益。燃殇按实际造成的火焰伤害点数累计公开标记，结束阶段由原技能帧发行一次等量体力流失，等待原生濒死与状态子结算后返回。残蚀的弃牌义务则属于本实际回合，发行后持续到游标返回；每次真正使用杀或普通锦囊时，经原生弃牌和装备移除子结算后再继续原牌。

整局用杀次数直接读取完整原生用牌声明，按真实用牌帧去重，再沿使用者变更事实确定最终归属。获得计数技能前的声明仍有效；决斗和南蛮中的单纯打出不形成新的用牌声明，借刀等真实用杀则形成独立声明。用牌接受、材料支付和追加效果轮次均不另计一次。

决生的零实体决斗保留原生多目标和追加效果游标，每名实际对手的伤害凭据归原技能帧持有。其真实响应付款必须先完成装备移除、回复和濒死子树，即使目录没有响应完成类技能，也按精确的决生父用牌进入既有响应完成协议。整张用牌返回后才执行授技；临时授予只移除自身来源，永久限定消耗不随技能来源更换而重置。

落雁的防御技能由真实星舞牌堆是否非空派生，并保留精确授予来源。派生资格和缓存依赖同时检查牌堆及来源，避免移牌与同步授技之间继续使用陈旧权利；失去派生来源只移除本次授予，不删除独立取得的同名技能。星舞的翻面、手牌或牌堆支付、目标装备移除和伤害依次返回同一个技能帧。

## 玩家投影与可信数据

玩家视图使用 `CreateSnapshot(viewerSeat)`。隐藏手牌、身份、未公开武将及私有询问只按该座位权限投影；其他座位只能得到相应公开信息或不透明牌位。

`Events`、`CardMovements`、`ResolutionStack`、命令记录、完整牌区诊断和 Checkpoint 都属于可信宿主材料，可能包含隐藏信息。向界面或玩家发布反馈时使用玩家投影和已公开事件，不把可信诊断直接当作玩家数据。

## 持久化与 AI

`GameCheckpoint` 保存规则选项、已接受命令前缀、Revision、模式、内容包签名和内容指纹。`GameReplay.Restore` 检查结构、签名及指纹，再按当前实现逐条 `Submit`，最后核对模式与 Revision。私有询问和中途结算由相同命令前缀重建。桌面存档失败保护与本机偏好见 [存档恢复](SAVE_AND_RESUME.md)。

AI 使用自己的玩家快照和公布的合法候选，在确定性随机序列上决策。策略版本由 `GameOptions.AiPolicyVersion` 记录，当前支持范围与新局选择以 [AiDecisionPolicy](../src/CardGame.Core/Replay.cs) 为准。详细评分和信息边界见 [AI 说明](TACTICAL_AI.md)。

## 验证入口

Core/WPF 检查按名称登记，常规范围由 [VerificationScopes.psd1](../tools/VerificationScopes.psd1) 选择。开发时验证相关规则与共享边界；完整验证仅在明确要求时执行。命令、版本来源和临时输出管理统一见 [开发门禁](DEVELOPMENT_GATES.md)。
