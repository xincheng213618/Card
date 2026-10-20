# 组合式规则引擎

当前技能以 `SkillProgram` 为规则定义，人物内容声明技能绑定，Core 提供加载、查询、选择、效果执行和原生结算能力。已有能力的新内容通常只增加人物资料、规则 JSON 和展示 JSON。需要新的状态变化或交互时，再扩展有明确上下文和返回契约的公共能力。

## 定义、实例与执行

武将模板提供默认属性和初始技能；本局角色由 `CharacterState` 保存当前体力、体力上限、身份、武将绑定和公开状态。技能持有关系由 `CharacterSkillSet` 的授予记录表达，包含 `SkillId`、`SkillInstanceId`、`SourceId` 和到期条件。相同技能可以有多个来源。

有数值的体力可以为负。普通伤害先扣护甲，剩余伤害与直接失去体力均按完整数额扣减，实际体力变化、已损失体力操作数、公开预测与界面数字使用同一有符号值；未定义体力仍先走原生显现／防止边界。濒死时每张桃或酒继续走真实用牌，已付后仍不高于0则允许同一响应者继续救援，弃救或资源耗尽才轮到下一位。程序救援以所属帧的实际付款与完成用牌事实判断，已付后失源不撤回付款；不以回复后的净体力变化冒充是否出过牌。

回复至某值按目标减去当前体力计算基础回复，再走原生回复修正与上限裁剪，不先把负体力置0。负体力沿原有状态与命令重放保留，不增加独立欠血字段或全局规则版本。[官方桃说明](https://guozhan.sanguosha.com/a/kapaiyilan/youxipai/jibenpai/2013/0130/149.html)的多桃例和[十周年杀绝正文](https://www.sanguosha.com/news/20240327_9515_2919)支持这一通用体力边界。

规则与文案分开：`SkillProgram` 描述 `modifiers`、`viewAs`、`activations`、`triggers`、`contributions`、`cardIdentities`、`cardPolicies` 和 `states` 等当前加载器支持的字段；展示按相同技能 ID 提供名称、说明和提示。标签、执行形态和主动入口分别声明，中文描述不参与规则推导。

`ProgramOperationCatalog` 发现并校验 Core 程序集中的操作 descriptor。descriptor 提供参数解析、handler、交互分类、上下文能力、资源关系、编译及 AI 策略。`ProgramCompositionValidator` 校验绑定的生产与读取、别名、消费、目标和临时材料；部分有专门前置条件的操作还具备自身的组合校验。枚举存在不等于任意入口均可执行。

`ProgramInstructionResolver` 按不可变定义缓存 `ProgramExecutionPlan` 与指令特征。计划不保存对局状态。`SkillProgramExecutor` 从 `ProgramSkillFrame` 的已提交游标执行，结果为继续、等待选择或等待子结算；宿主负责帧写入、原生状态操作与完成返回。

## 时机与查询

触发规则使用明确窗口。回合、阶段、移动、伤害、体力变化、用牌、响应、判定、濒死及死亡由所属原生机制提供冻结上下文。`ProgramEntryCapabilities` 声明入口提供的 `Damage`、`CardAction`、`Judgment`、`DrawPlan`、`Pindian` 等能力，加载时据此拒绝无上下文的条件与操作。

窗口先冻结候选身份及事实，随后按该窗口顺序推进。发动与暂停恢复时复查存活、技能实例、限次和当前支付/目标资格；窗口初始事实保持不变。`ProgramTriggerWindowDriver` 复用候选推进，完成程序经 `[ProgramWindowReturn]` 返回所属窗口。详细协议见 [触发窗口与结算返回](RESOLUTION_PROTOCOLS.md)。

`MatchSkillBindingIndex` 按席位缓存有效技能定义和绑定，提供触发、数值修正、转换、主动入口、牌身份及政策索引。缓存 stamp 包含技能授予和派生投影 revision、体力及武将显隐等资格；当前手牌、装备、条件结果和暂停候选由实际调用处读取或冻结。缓存重建不会向已打开的窗口添加候选。

持续规则使用无副作用的查询。`RuleQueryReducer` 处理有限基础值、带来源贡献、最高优先级 `Set`、加减、上下界和显式无限值。距离与攻击范围、次数上限与使用账本、距离豁免与目标禁令分别处理。合法动作、命令提交和 AI 以当前规则查询为依据。

## 状态与扩展边界

短期选择、已支付费用、子结算凭据和继续游标保存在所属帧；跨阶段、跨回合效果保存明确来源和期限。技能限次与转换状态由 `SkillRuntimeStateStore` 管理，其作用域包括事件、阶段、回合、轮和整局。临时技能授予与已签发规则贡献各按自己的期限处理。

通用执行 host 提供共同操作；专用窄接口由 `[ProgramCapability]` 私有适配器实现，通过 `GetCapability<T>()` 解析。注册元数据不可变，适配器按对局创建。选择模块只接收只读查询，不能取得引擎状态写入口。规则观察、动作来源、提交验证、续接、运行钩子和帧断言均在能力附近显式注册，见 [人物模块与注册](CHARACTER_MODULES.md)。

专用回复子结算的父帧凭据使用 `[ProgramRecoveryParent(phase, order)]` 注册在所属能力的判断方法上，由现有运行钩子注册器绑定。`RecoveryCursor` 和 `DamageCursor` 分别保留各自的准入集合与短路顺序；方法接收原 `ProgramSkillFrame` 和 `RecoveryReplacementFrame`，继续校验支付、来源及原生回执。新增此类能力无需修改通用游标的人物名单，也不新增存档状态。

伤害、移牌和濒死的专用子树凭据同样由所属模块注册：`ProgramObserverProof` 接收原窗口编号，`ProgramNestedDamageProof` 接收原程序帧及伤害参数，`ProgramMovementProof` 接收原帧、暂停效果和移牌续接凭据，`ProgramDyingProof` 保留独立的运行推进与帧校验范围。各枚举值对应既有调用位置，不能把派系债务前后的伤害许可、或不同付款位置的移牌许可相互替代。复用已有操作的人物只配置内容；只有新增子树机制才需要声明对应凭据入口，不向共享条件追加人物名称。

注册器按各位置内的显式顺序短路判断。濒死校验另保留原来的 `|=` 分组：组内短路，后续组仍执行；`ProgramDyingProof` 的 `group` 参数仅用于保留这些既有边界，默认组为人物专用续接所在的组。元数据只读共享，委托按对局、按实际经过的位置绑定，不进入存档。凭据方法继续负责来源、支付、帧身份及恢复条件；注册本身不授予任何额外许可。

原生用牌、响应、判定、伤害、回复、濒死与死亡继续拥有各自规则编排。用牌目标和效果依牌种适配，部分复杂操作仍有专门的验证和返回凭据；组合执行器不把任意操作开放到每个窗口。判断能力是否可用需要同时查看入口能力、descriptor、组合校验及真实宿主路径。

目录组合校验由模块的 `[ProgramCatalogValidation]` 注册，死亡目标资格由操作 descriptor 的 `AllowsDeadTarget` 提供；新增人物不再向加载器和通用执行器追加这两类名单。采集原生事实的需求由 descriptor 的 `FactRequirements` 区分触发和主动入口，经整个冻结内容目录聚合。公开阶段用量和玩家视图则分别通过 `[ProgramPhaseUsedProjection]`、`[ProgramViewContributor]` 注册只读投影；不同可见性渠道保持独立，返回值为零也可覆盖阶段用量。

原生用牌合法性、支付与实际伤害推进分别通过 `ProgramCardLegality`、`ProgramCardUsePayments` 和 `ProgramDamageHooks` 接入模块声明。各阶段对应原有调用位置，不能跨过共享资格、付款回执、伤害前窗口、装备选择或体力落地；查询短路、执行顺序和首个错误保持独立。伤害修正保留事实总和的检查溢出与后续加成的非检查左结合语义，带签发副作用的加成只在实际结算中执行一次，公开预测继续使用原查询路径。

这些扩展不改变命令提交、支付凭据、帧返回、原生事件产生时机或回放格式，也不新增存档中的 pending 状态。新增协议能力应补其实际行为检查，再供人物复用。操作枚举的稳定编号和加载格式仍是共享协议的一部分。

## 内容与回放契约

内容 hash 覆盖当前注册规则数据，展示文案有独立来源。JSON 中的 `minimumRulesVersion` 继续解析并参与内容指纹；它不决定运行时执行分支。Checkpoint 按格式、包签名、内容 hash 和逐条命令校验，在当前实现中重建对局。

格式来源保持在 `SkillPrograms.cs`、`Replay.cs` 与各包的 `CurrentVersion`。普通人物配置无需改格式或包版本。新增可选节点按格式兼容性决定 schema；公共机制变化补实际行为检查。开发运行名称过滤与常规范围，完整验证仅在用户明确要求时执行。

## 开发入口

- [人物模块与注册](CHARACTER_MODULES.md)：目录、注册表和各扩展属性。
- [技能组合定义与执行](SKILL_COMPOSITION_DESIGN.md)：作者需要的加载及运行契约。
- [当前技能配置](content/skill-composition/RUNTIME.md)：现用配置字段与能力。
- [运行时状态与调度](RUNTIME_REFACTOR_IMPLEMENTATION.md)：帧、提交及返回边界。
- [核心控制模块](CORE_CONTROL_DESIGN.md)：命令、牌区、投影和恢复。

`tools/Inspect-SkillProgram.ps1` 可查询操作、入口能力和正式定义中的样例，也可加载候选规则与展示文件。工具的格式/组合检查需要结合目标内容的真实行为场景使用。
