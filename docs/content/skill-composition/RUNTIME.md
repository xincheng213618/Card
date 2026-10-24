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

一次弃置多张牌的原生收集器在原帧上保留未支付集合，选齐或确认后统一移动。玩家投影公布剩余上下限、允许张数及原帧的选牌阶段；桌面据此保留多选、反选和刷新后的高亮，并在确认时逐次查询当前视图提交原生命令。投影不改写命令身份或付款规则；规则明确要求依次弃置的分支仍逐次付款并等待各自子流程。

Checkpoint 只保存不可变设置与已接受命令前缀等恢复元数据，通过当前实现重放。schema、内容签名/指纹或命令不符合时拒绝恢复。格式源见 `src/CardGame.Core/Replay.cs`，桌面失败处理见[存档说明](../../SAVE_AND_RESUME.md)。

## 技能升级与替换

新增武将复用已有 `upgradeConversionTier`、`setBooleanState` / `toggleBooleanState`、`grantSkills`、`loseOwnerSkillsAndGrant` 等节点。准备阶段替换和觉醒选项也通过 `GameEngine.ProgramSkillChanges.cs` 提交；人物规则只描述触发、费用、选项和升级结果。

公共入口将每次变化绑定到当前程序帧的拥有者、技能实例、绑定、玩法指纹和已推进的指令位置。升级本身没有“必须已开始第1轮”的前提；伤害、濒死和技能获得等合法开局窗口可以在第0回合提交升级。只有规则明确要求实际阶段或轮次时，才在对应资格入口判断。执行器继续负责定义指纹、失源/死亡资格与已经发行的付费续接，公共提交不重新支付费用、不额外取消已付费义务。

两次修改的转换层级达到上限后再次申请升级会返回未变化，不发行第三层或重复变化事件。布尔状态不变和同来源重复授予也保留现有幂等语义。替换保留各节点的授予/失去顺序；主公、将库投影的本地禁用与模式来源保护同样由公共入口处理。

原生复合能力先完成自己帧上的费用、摸牌、回复及子流程，再调用帧感知入口：`TryUpgradeConfiguredConversionTier` 可带冻结的预期层级；`SetProgramBooleanState(frame, targetSkillId, instanceId, stateId, value)` 支持修改同一拥有者的另一项技能；`CommitProgramSkillChangeFact` 提交已有类型化升级事实。复合能力的待决状态仍放在原程序帧，不能另设全局升级队列或用 ID 旁表替代精确父返回。原生声明观察器校验来源后，通过公共 `CommitConfiguredConversionTier` 初始化层级；观察器的到期清理使用既有标量状态原语，两者都不伪造活动程序帧。

共享检查使用 `--filter=Skill changes:`，覆盖真实开局升级、封顶、重复授予、付款子窗口、升级时失去原技能与冷恢复。复用既有能力的新人物不需要复制这套夹具；新增行为和实际回归再补相应检查。

`tools/Test-SkillBoundaries.ps1` 同时阻止公共入口外直接改写转换层级，以及在公共技能变更入口引用回合、轮次或阶段字段。阶段资格留在具体操作入口，状态提交复用上述共享方法。

## 来源与验证

技能标签、状态/触发形态、主动入口分别显式声明，见[技能元数据](SKILL_METADATA_V1.md)。规则版本、产品线和裁定依据从[来源规范](../BWIKI_CONTENT_BACKLOG.md)、人物来源档案及本目录的裁定/合同读取。

纯配置沿现有加载器、Registry 和机制检查；新行为或修复增加名称过滤检查。开发使用 `tools/Test-Changed.ps1` 的相关过滤，批次运行规定例行范围。完整验证只在用户明确请求时执行。普通新增人物不逐项提高格式或包发布版本。
