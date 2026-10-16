# 技能组合定义与执行

技能规则由 `SkillProgramCatalog` 加载为不可变 `SkillProgram`，展示由独立文件提供。人物资料引用已注册技能，程序运行使用技能实例和具体绑定身份。整体职责见 [组合式规则引擎](COMPOSABLE_RULE_ENGINE.md)，目录和扩展注册见 [人物模块与注册](CHARACTER_MODULES.md)。

## 规则定义

| 定义部分 | 当前含义 |
| --- | --- |
| `id`、`revision` | 稳定技能身份及定义修订 |
| `modifiers` | 摸牌数、手牌上限、距离、范围、次数等纯数值规则 |
| `viewAs` | 来源牌区、材料筛选、有效牌型、动作上下文及转换来源 |
| `activations` | 主动入口、限次、选牌/选人边界和效果步骤 |
| `triggers` | 窗口、主体、条件、可选性、优先级、触发粒度及效果 |
| `contributions` | 向已有动作或响应提供材料的受控入口 |
| `cardIdentities`、`cardPolicies` | 持续牌身份及已实现的牌规则政策 |
| `states` | 声明式技能状态及使用记录 |

具体字段和参数以 [当前技能配置](content/skill-composition/RUNTIME.md) 及加载器为准。资料中的 `SkillTag`、`SkillExecutionForm`、`SkillActionForm` 分别描述主公/锁定/限定/觉醒/转换标签、状态/触发形态和主动操作入口。标签本身不支付限次、不自动产生触发优先级，也不从中文说明推导。

## 加载与组合校验

`SkillProgramCatalog` 严格校验当前格式和字段。`ProgramOperationCatalog` 按操作解析参数，由 descriptor 提供 handler、资源关系、上下文能力、交互分类和 AI 策略。未知操作、错误字段、重复身份和缺失依赖在加载或注册阶段失败。

`ProgramCompositionValidator` 顺序跟踪牌集、目标集、拼点结果、选择结果及其他绑定。读取必须有合法生产者；集合过滤保留根和别名关系；移动子集与补集时检查消费，避免同一临时实体被两个步骤重复处理。条件性步骤和特定操作还有专门校验，不是任意 JSON 节点的自由脚本系统。

`ProgramEntryCapabilities` 声明每个入口实际提供的上下文。普通主动、伤害窗口、用牌窗口、摸牌计划和濒死响应具有不同能力；需要 `Damage`、`DrawPlan`、`CardAction` 等上下文的操作不能放到不提供该能力的入口。工具 `tools/Inspect-SkillProgram.ps1` 可以检查实际操作、入口和候选定义。

## 运行和续接

`ProgramInstructionResolver` 从不可变定义解析可复用的 `ProgramExecutionPlan`，缓存指令和静态特征。`ProgramSkillFrame` 保存本局技能、绑定、拥有者、技能实例、选牌/选人结果、冻结事实、步骤游标及操作专属草稿。

`SkillProgramExecutor` 根据当前帧运行步骤，返回 `Continue`、`AwaitChoice` 或 `AwaitChild`。提交指令游标后才挂起选择或原生子结算；返回凭据指向已提交指令，防止重放费用。父帧的替换使用当前帧，保留子结算已经写回的绑定和结果。

选项由服务端生成并冻结。`SkillPrompt` 提供技能名、说明和选择形态；命令仍以 `PromptId`、revision、决定者及精确 `ChoiceId` 校验。手牌、目标组合、牌目标配对和有序目标序列保留完整关联，不能拆成各自独立的可选集合。

公共候选推进使用 `ProgramTriggerWindowDriver`。完成触发程序通过 `[ProgramWindowReturn]` 返回所属窗口；内部步骤和移动返回通过 `[ProgramContinuation]` 处理。所属机制继续核对候选、父帧、阶段和费用，见 [触发窗口与结算返回](RESOLUTION_PROTOCOLS.md)。

## 牌动作与规则事实

`CardActionContext` 冻结使用者、提供者、请求者、响应者、对手、有效牌身份、实体原位置、转换链和动作因果身份。有效牌名与实体原牌型分别保留。`Use`、`Response` 以及响应场景中的真实使用由原生边界与能力契约共同判断，不能只看弹窗类型。

触发窗口保存原事件的事实与候选；暂停后实时资格检查不会重新采样原事实。整张使用、真实指定目标、逐目标效果、实际伤害和牌移动分别使用各自上下文。用牌窗口及材料规则见 [公共时机](CARD_LIFECYCLE_CONTRACT.md)、[用牌生命周期](CARD_USE_LIFECYCLE_DESIGN.md) 和 [使用中实体牌](CARD_USE_MATERIALS.md)。

## 状态、隐私与 AI

`CharacterSkillSet` 按来源保存授予；`SkillRuntimeStateStore` 保存事件、阶段、回合、轮、整局额度和转换面。回合规则贡献、临时技能与持久牌区各有明确期限和清理规则。失技、抑制、死亡和已支付步骤是否继续由具体机制检查。

玩家视图通过 `CreateSnapshot(viewerSeat)` 过滤并冻结。公开亮牌、本人查看、他人暗牌槽位和可信诊断使用不同权限。AI 消费合法动作及程序公开上下文，按操作策略估值；未知手牌或牌堆顺序不作为可读取事实。

规则定义及运行元数据参与当前内容 hash，展示文本独立维护。`minimumRulesVersion` 保留为加载和指纹元数据。Checkpoint 使用当前实现重放接受命令，依据格式、包签名、内容 hash 及命令校验恢复。

纯配置复用已有加载与机制检查；新行为补聚焦场景，新交互补必要 WPF 检查。开发运行名称过滤及 `tools/Test-Changed.ps1` 常规范围；完整验证需用户明确要求。

## 源码入口

- [加载器和模型](../src/CardGame.Core/SkillPrograms.cs)、[嵌入资源](../src/CardGame.Content.Standard/EmbeddedSkillProgramCatalog.cs)
- [操作目录](../src/CardGame.Core/ProgramOperationDefinitions.cs)、[组合校验](../src/CardGame.Core/ProgramCompositionValidator.cs)、[入口能力](../src/CardGame.Core/ProgramEntryCapabilities.cs)
- [可执行计划](../src/CardGame.Core/ProgramExecutionPlan.cs)、[执行器](../src/CardGame.Core/SkillProgramExecutor.cs)、[技能状态](../src/CardGame.Core/SkillRuntimeState.cs)
