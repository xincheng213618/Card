# 人物模块与注册

新增人物优先复用已有技能 JSON 能力。人物资料、技能定义与专属运行代码放在 `src/Characters` 的同一人物目录，由主程序的内容和运行注册表接入。

## 内容声明

目录先按人物身份分组，再按版本分组。例如：

```text
src/Characters/wang-can/
  ol/
    Content/ol-wang-can.character.json
    Skills/ol-wang-can.rules.json
    Skills/ol-wang-can.presentation.json
    Runtime/GameEngine.WangCanPrograms.cs
```

`Content/*.character.json` 是人物的基础资料和技能元信息声明，包含 `packageId`、人物身份、武将版本、技能名称与说明、标签、执行形式、AI 权重以及注册顺序。技能通过 `programBundle` 引用同目录 `Skills` 中的规则和展示文件；多个角色复用的规则放在 `_shared/Skills`。授予或升级后才获得的技能也要声明，不能只注册武将的初始技能。

同一人物跨版本共用的专属运行代码可放在人物直属的 `Runtime` 中；多人复用的通用机制继续由 Core 管理。人物肖像和音频仍通过现有资源目录、目录表和 `portraitKey` 引用，保留素材工具与 WPF 的资源约定。

`CharacterContentCatalog` 发现编入内容程序集的资料，校验后按各包的 `registrationOrder` 顺序注册技能和武将。内容包只调用目录注册器，并声明共用卡牌、牌堆、模式和依赖。人物自己的 `pools` 指定其选将池及位置，经典包的当前人物池和标准演示池从这些声明生成，不再另维护人物列表。

新增人物在自己的文件夹添加资料和技能文件；复用现有能力时无需修改 `GameEngine`、项目文件或中央人物清单。选择该包未占用的注册顺序值和选将池顺序值。顺序会影响确定性的选将结果，不能为了整理目录而重排。加载器拒绝重复身份、重复顺序、错误格式和缺失的规则引用；最终内容注册器继续校验技能依赖和模式约束。

装备栏费用沿用原生容量和实体移动。预检废栏后的距离、攻击范围及【杀】次数时，`excludedEquipmentSlot` 排除该栏内全部实际装备；【杀】次数查询通过 `GetEquipmentRuleCards` 重建废栏后的武器能力，保留独立继承的能力。这类查询绕过普通动作查询缓存，避免不同废栏方案共用结果。复原单栏遇到历史整区废除标志时，先把各栏转换为零容量，再启用所选栏；全部复原的效果按本次实际由废除变为可用的栏数计算收益。

文件物理位置与程序集职责分开：`Runtime/**/*.cs` 链接编译进 Core；内容 JSON 嵌入 Content.Standard；可选 `Wpf/**/*.cs` 链接进 WPF。专属 partial 和私有适配器仍在原程序集内，不开放引擎可变状态。技能 JSON 保留原文件名和逻辑资源名，目录变动不会改变历史规则资源身份。

## 运行能力

通用 `ProgramSkillHost` 只实现 `ISkillProgramExecutionHost` 与 `ISkillProgramEffectHost`。人物或共享机制的专用接口由独立的 `[ProgramCapability]` 适配器实现，效果处理器通过 `host.GetCapability<IExampleProgramHost>()` 获取能力；可选能力使用 `TryGetCapability`。

每个适配器有且仅有一个接收 `GameEngine` 的构造函数。注册元数据只保存类型与工厂；实际适配器按对局惰性创建、缓存，不跨对局共享。注册只扫描引擎显式标记的私有嵌套适配器，不扫描外部程序集或文件。适配器保留在能力实现附近，利用私有嵌套类型访问受信任的引擎操作，避免开放玩家、牌区和结算栈的可变状态。

这是运行能力接口的组合边界。结算状态仍属于引擎与对应帧，适配器不保存另一套 pending 状态，不自行提交命令或通知 UI。已有实现专用接口的测试宿主仍可通过通用宿主的默认能力解析使用自身实现。

## 规则观察与资格检查

事件规则在实现方法旁使用 `[ProgramRuleObserver(phase, order)]` 注册。`BeforeEvent` 在事实进入输出队列前运行，`AfterEvent` 在入队后运行，`BeforeStatePublish` 在打开原有状态窗口前运行。无参数方法用于同步状态；单个事件参数会按事件类型过滤。递归产生的事实仍先完整处理后返回父调用。

激活与触发资格分别通过 `[ProgramActivationLegality]` 和 `[ProgramTriggerLegality]` 声明阶段、顺序和适用操作。资格方法与原能力放在一起；多个阶段对应通用条件、次数限制和支付检查的原有位置，不能随意合并或重排。未限定操作的检查对该阶段全部程序适用。

私有选择与 AI 策略继续使用已有 `[ProgramAnswer]`、`[ProgramAiChoice]`，子帧返回继续使用 `[ProgramContinuation]`。新增能力应在自己的模块声明相应入口，不修改集中人物分派表。

需要观察响应窗口中的实际「使用」时，人物模块可提供无状态、无参的 `IProgramCommittedUseContract`。注册表按技能身份发现唯一契约，技能 JSON 加载与内容注册均校验其完整触发绑定；未注册的格式继续受原校验约束。契约只校验声明，实际使用与单纯打出的区分仍由原生动作上下文和人物资格检查负责。

## 卡牌、目标与动作筛选

人物选择规则由独立的无状态类实现，使用 `[ProgramSelectionModule(stage, order, operations)]` 自行注册。注册表只扫描 Core 自身程序集中的显式声明，拒绝重复阶段顺序、错误接口、泛型类型和携带实例字段的模块。共享缓存只保存不可变的模块清单；候选结果不缓存，每次查询重新读取当前事实。

模块按职责实现 `IProgramSelectionCardFilter`、`IProgramSelectionTargetFilter`、`IProgramSelectionSourceProvider`、`IProgramSelectionCandidateFilter`、`IProgramSelectionSuitProvider`、`IProgramSelectionPhaseLimitProvider` 或 `IProgramSelectionAiDecorator`。仅为现有能力增加人物时，仍只修改内容和技能 JSON；新增机制才需要新选择模块。

选择模块只接收 `ProgramSelectionContext`、本次动作的 `ProgramSelectionCandidates` 草稿和 `IProgramSelectionQueries`。查询接口必须继承 `IProgramSelectionQuery`，由所属能力的私有适配器提供只读事实或单个通用合法性查询；选择模块不能取得引擎、可变角色、效果宿主或提交命令的入口。卡牌事实连同授予技能列表一起冻结，避免只读外层仍携带可变内容。

阶段保留原有求值位置：卡牌来源先生成卡牌，`AfterCards` 后才生成来源对应的目标；普通来源才运行 `OrdinaryTarget`。来源返回已处理且结果为空时，不回退到普通来源。`AfterTargets` 支持目标重建、卡牌和目标双向过滤；通用数量检查之后才运行最终排序和目标调整，最后生成关联关系、次数及 AI 提示。

不能把有序目标组合或卡牌目标关系简化成两个独立集合。`TargetSequences` 表示有顺序的合法目标组合，`CardTargets` 表示实际允许的卡牌目标配对；发布时仍使用现有不可变合法动作边界。人物模块不能支付代价、移动牌、压入结算帧或发布事件。

提交重验通过 `[ProgramSelectionValidator(stage, order, operations)]` 独立注册，保留通用数量、重复项、候选成员、花色及公开条件校验之间的原有位置，以及最先返回的错误代码和文案。带人物状态的目标域通过 `[ProgramTargetResolver]` 自行提供，通用目标选择继续负责生命周期、隐藏信息和选择提交边界。

额外动作来源使用 `[ProgramActionSource]`，特殊动作的提交、执行及贡献入口使用 `[ProgramActionHandler]`，声明仍放在所属能力实现旁。动作发布和命令处理有各自的显式顺序；命令处理只由可信命令会话调用，选择查询模块不能调用这些入口。

修改已有物理用牌动作的能力使用 `[ProgramCardAction]`，分别在程序动作生成之前和之后接入。它复用已有可信动作构建器参数，不把这些可变参数开放给只读选择模块；新增能力不再向通用动作方法追加人物函数调用。

## 子结算推进与帧检查

子结算推进和续接权限在所属机制的方法旁使用 `[ProgramRuntimeHook(phase, order)]` 注册。推进方法无参数并返回 `bool`，按阶段内的显式顺序运行，首个返回 `true` 的方法结束本轮分派。`BeforeDeclaration`、`BeforeResponseHealth`、`AfterResponseHealth` 保留原调度位置，不能跨过声明、响应体力子帧或其他原生结算分支移动。

`BeforeJudgmentPhase` 位于全部准备阶段子结算之后、原生跳判判断之前，用于消费已经签发的下次判定阶段义务。它在翻面跳过整个回合时不会经过，也不会为已跳过的判定阶段虚构阶段开始窗口；同步完成义务的钩子返回 `false`，由原生回合流程继续推进。

`BeforeRecoveryApply` 位于恢复替代选择之后、实际体力变化之前。最终来源、对象、请求量和替代候选保存在恢复帧的 `PendingApplication`，支付子结算返回时继续使用该凭据，不重新询问替代选择。`BeforeDamageApply` 在原生伤害前窗口推进时处理已经签发的数值义务。两者的支付状态属于各自的原生帧，体力子窗通过 `AmountSupport` 和严格的父帧凭据返回，已完成的代价不能再次支付。

`AfterOwnerDeath` 和 `AfterSkillLoss` 分别判断所属帧能否继续，方法接收 `ProgramSkillFrame` 并返回 `bool`。两种权限独立注册；已支付代价、来源身份、原生完成凭据等原条件仍由所属机制核对，不能统一放行，也不能让恢复后的子帧重复支付。

帧断言使用 `[ProgramFrameInvariant(phase, order)]`，接收原帧，必要时再接收本轮已经解析的 `ProgramExecutionPlan`。`BeforeSelectionState` 和 `AfterSelectionState` 分处通用选择状态检查两侧，保留通用游标、选择域和已支付状态检查的位置。机制原有条件放在自身包装方法中；无实例状态的断言可以保持静态。

这些注册只保存不可变方法元数据，实际委托按对局绑定到引擎，禁止跨对局保存绑定实例。注册表校验阶段、顺序与签名，不新增可序列化状态或另一套 pending 对象。新增机制只在自身模块声明入口，无需向通用调度、死亡与失技权限、帧断言方法追加人物名称。

## 验证与兼容

纯人物配置复用内容加载和注册验证；新增机制或缺陷修复增加实际行为检查。开发时使用 `tools/Test-Changed.ps1 -CoreFilter <名称>`，批次结束运行无过滤器的常规范围。完整验证仍需用户明确要求。

注册重构必须保留声明顺序、资格短路顺序、事件时机、技能实例与帧身份、支付次数、隐私投影、牌移动和回放结果。仅调整接口组织不提升规则版本、技能 JSON schema 或内容包版本；实际行为或格式变化仍按现有兼容规则处理。

## 其他角色发动与阶段获得技能

主动配置可声明 `otherCharactersOnly: true`，将入口提供给其他当前出牌角色。当前共用契约接受零选牌、零选目标的阶段限次入口，`activator` 指发动者，所属帧的 owner 仍是技能拥有者。合法动作、命令和技能栏均保留准确的来源座位；原有献牌、陷嗣等入口继续使用各自的费用选择流程。

`offerRandomPlayPhaseSkill` 接受明确的 `skillIds` 列表和候选数 `amount`。候选只在执行时抽取一次，保存在所属帧中，由技能拥有者选择；`target` 决定获得者。候选提示仅投影给选择者，最终获得的技能公开进入获得者的技能区。阶段来源复用现有 `SkillGrantPhaseExpiry`，正常结束、插入新出牌阶段、阶段角色死亡和终局均按原有边界移除，其他永久来源不受影响。代价子窗返回后不重新付费；获得者已死亡或原阶段已结束时不授予技能。

钟琰按[官方技能说明](https://www.sanguosha.com/hero/7014)接入此流程，仪法复用指定目标后的实际使用者手牌上限贡献。博览采用[2021-09-15 官方公告](https://www.sanguosha.com/news/20210903_6756_2218)公布的 18 项名单，并在 `ol-zhong-yan-2026-10-17.json` 逐项固定所用版本；这不宣称已经核验当前线上服务器的完整技能池。借用沿用所选版本的技能效果，技能的移除不撤销它已经产生且另有期限的规则贡献。
