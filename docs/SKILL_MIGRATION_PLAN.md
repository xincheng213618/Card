# 技能能力与开发入口

当前技能以人物目录资料、规则 JSON、展示 JSON 及必要的注册型运行能力接入。开发按机制选择入口；已经支持的内容复用同一加载、合法性、选择、执行、投影和重放边界。

| 需要处理的内容 | 当前入口 |
| --- | --- |
| 人物、版本、技能和选将池注册 | [人物模块与注册](CHARACTER_MODULES.md) |
| 定义字段、操作与入口能力 | [当前技能配置](content/skill-composition/RUNTIME.md)、`tools/Inspect-SkillProgram.ps1` |
| 组合加载、资源校验和执行 | [技能组合定义与执行](SKILL_COMPOSITION_DESIGN.md) |
| 时机与规则查询的整体职责 | [组合式规则引擎](COMPOSABLE_RULE_ENGINE.md) |
| 用牌身份、目标、响应和完成 | [公共时机](CARD_LIFECYCLE_CONTRACT.md)、[用牌生命周期](CARD_USE_LIFECYCLE_DESIGN.md) |
| 材料被领取后的续接 | [使用中实体牌](CARD_USE_MATERIALS.md) |
| 帧、已支付状态、运行钩子和返回 | [运行状态与调度](RUNTIME_REFACTOR_IMPLEMENTATION.md)、[结算协议](RESOLUTION_PROTOCOLS.md) |
| 技能授予、牌区和状态可见性 | [区域与状态归属](REGION_STATE_AUDIT.md) |
| 技能升级、布尔升级状态与技能替换 | [公共技能变更入口](content/skill-composition/RUNTIME.md#技能升级与替换) |
| 当前未覆盖的具体能力需求 | [Core 能力需求](content/CORE_API_REQUESTS.md) |

能力判断同时核对 `ProgramEntryCapabilities`、操作 descriptor、`ProgramCompositionValidator`、具体宿主和正式消费者。窗口枚举、操作名称或单项加载成功不足以说明任意组合可执行。

普通内容开发先明确规则版本和来源，再添加资料、规则和展示。纯配置复用现有加载、注册和机制检查；新行为或缺陷修复增加聚焦场景，新增 UI 交互才补 WPF 检查。缺少机制时，先定义上下文、状态拥有者、选择、费用、期限和精确父返回，再完成可复用能力及内容接入。

格式版本来源保留在 `SkillPrograms.cs`、`Replay.cs` 和相关包的 `CurrentVersion`。普通人物配置由内容 hash 区分。开发使用现有名称过滤及 `tools/Test-Changed.ps1` 常规范围；完整验证只按用户明确要求执行。提交、推送与发布按用户对这些动作的授权分别处理。
