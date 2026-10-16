# 技能程序运行时

技能规则由 `SkillProgramCatalog` 严格解析，按已注册能力执行。它是一组受控的规则节点及类型化原生流程，不从技能中文描述推断效果。

## 文件、版本与身份

人物资料和技能路径见[人物模块说明](../../CHARACTER_MODULES.md)。规则文件的根对象是 `schemaVersion` 与 `skills` 数组；展示文件的 `skills` 是以 SkillId 为键的对象。规则与展示的 ID 必须对应。JSON 随项目嵌入构建，修改后需要重新编译。

规则和展示格式版本、运行时标识分别取 `SkillProgramCatalog.RulesSchemaVersion`、`PresentationSchemaVersion` 和 `RuntimeVersion`。加载器只接受当前格式；不要把能力主题或文档编号当成可选择的运行时版本。当前可用的完整文件示例是 [standard-active-cutover.rules.json](../../../src/Characters/_shared/Skills/standard-active-cutover.rules.json) 及人物目录中的对应展示文件。

每项技能有稳定的 `id` 和正整数 `revision`。规则节点、顺序及语义进入玩法指纹；空白、属性顺序和展示文字不改变玩法指纹。可选新增字段保持明确默认值与规范化省略。`minimumRulesVersion` 是加载器保留的指纹元数据，不控制当前运行或 Checkpoint 恢复，也不作为普通新人物的版本门禁。

未知字段、重复属性/ID、错误类型、越界数值、无效绑定、能力资源冲突及含糊的字段组合在加载时拒绝。数量、深度、绑定引用和每种操作允许的字段由当前解析器及操作描述符约束。

## 配置入口

| 技能字段 | 职责 | 详细说明 |
| --- | --- | --- |
| `modifiers`、`damageModifiers` | 纯规则查询、持续伤害修正 | [状态、阶段与规则修正](RUNTIME_STATE_AND_TURNS.md) |
| `viewAs`、`cardIdentities` | 可选转换、持续牌名身份 | [牌动作与选择](RUNTIME_CARD_ACTIONS.md) |
| `activations` | 主动选牌/选目标及顺序效果 | [基础配置与步骤](RUNTIME_CONFIGURATION.md) |
| `triggers` | 显式规则窗口、条件、选择及顺序效果 | [判定与生命流程](RUNTIME_JUDGMENT_AND_HEALTH.md) |
| `contributions` | 由符合资格的其他角色向拥有者贡献实体手牌 | [牌动作与选择](RUNTIME_CARD_ACTIONS.md) |
| `states`、`cardPolicies` | 结构化状态与特定牌策略 | [状态、阶段与规则修正](RUNTIME_STATE_AND_TURNS.md) |
| 操作中的牌绑定/移动字段 | 私有与公开牌集合、实际来源、实体消费 | [移动与拥有牌区](RUNTIME_MOVEMENT_AND_ZONES.md) |

人物专属操作也必须具备描述符、解析校验、资源声明、执行与 AI/交互适配，并通过现有注册协议接入。完整操作集合以 `SkillProgramEffectOp` 及当前操作目录为准；本组说明聚焦可复用合同。

## 提交、暂停与恢复

命令接受前重新校验 Revision、Prompt、操作者、技能实例、材料和目标。玩家视图在提交前准备，并冻结所有嵌套集合；公开事件的集合由 `CommittedEventProjection` 冻结。玩家及 AI 使用 `CreateSnapshot(viewerSeat)`；`ResolutionStack` 是可信诊断。

程序帧保存来源、技能/绑定、玩法指纹、指令位置、选定对象、支付收据和原生子流程返回。子流程完成后沿类型化父返回继续；已经支付的费用不再次扣除，已经完成的效果不再次发行。失源、死亡或终局对后续步骤的处理依赖该操作的发行合同，不能用一个全局条件取消所有已支付义务。

Checkpoint 只保存不可变设置与已接受命令前缀等恢复元数据，通过当前实现重放。schema、内容签名/指纹或命令不符合时拒绝恢复。格式源见 `src/CardGame.Core/Replay.cs`，桌面失败处理见[存档说明](../../SAVE_AND_RESUME.md)。

## 来源与验证

技能标签、状态/触发形态、主动入口分别显式声明，见[技能元数据](SKILL_METADATA_V1.md)。规则版本、产品线和裁定依据从[来源规范](../BWIKI_CONTENT_BACKLOG.md)、人物来源档案及本目录的裁定/合同读取。

纯配置沿现有加载器、Registry 和机制检查；新行为或修复增加名称过滤检查。开发使用 `tools/Test-Changed.ps1` 的相关过滤，批次运行规定例行范围。完整验证只在用户明确请求时执行。普通新增人物不逐项提高格式或包发布版本。
