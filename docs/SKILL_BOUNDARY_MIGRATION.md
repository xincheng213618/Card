# 技能执行边界

技能内容通过 `SkillProgram` 和人物注册声明，执行由公共程序及原生规则能力共同完成。内容字段、状态、动作、选择和返回各有独立契约。

| 边界 | 当前实现 |
| --- | --- |
| 内容 | `CharacterContentCatalog` 注册人物与技能；`ContentRegistry` 校验依赖并冻结当前定义 |
| 效果 | 主动与触发均使用 `SkillProgramEffect`、`ProgramExecutionPlan` 和 `SkillProgramExecutor` |
| 规则查询 | 当前 Program 修正和政策参与数值、转换、牌身份及合法性查询 |
| 动作 | 合法选项保留程序、绑定、技能实例、材料和目标身份；正式变化经 `Submit` |
| 拼点 | 原生拼点帧保存参与者、实体、结果和游标；程序消费同一结果绑定 |
| 技能拥有 | `CharacterSkillSet` 保存来源及期限，角色查询当前有效实例 |
| 暂停与恢复 | 对应原生/程序帧保存待决数据，类型化子结果返回精确父帧 |
| 玩家投影 | `CreateSnapshot(viewerSeat)` 过滤并冻结；WPF 提交完整合法选择 |

能力扩展在所属机制附近注册。专用效果接口使用 `[ProgramCapability]`；选择、动作、资格、规则观察、续接和窗口返回使用相应注册目录。属性发现只提供分派元数据，处理器仍负责来源、阶段、技能实例、费用和父帧凭据检查，见 [人物模块与注册](CHARACTER_MODULES.md)。

`tools/Test-SkillBoundaries.ps1` 检查明确禁止的接口和拼点人物分派入口。它是静态边界检查，不证明所有内容已经能任意组合，也不替代行为验证。

运行格式由 `SkillPrograms.cs` 和 `Replay.cs` 定义，内容 hash 由当前注册表产生。`minimumRulesVersion` 保留在规则元数据和指纹中，Checkpoint 根据 schema、内容包签名、内容 hash 和命令重放恢复。

已有能力的新人物维护资料、规则和展示。机制变化补共享行为检查，内容只补尚未覆盖的条件和交互；WPF 检查用于界面行为。开发运行相关名称过滤及常规范围，完整验证需用户明确要求。

相关说明：[内容职责](CONTENT_MODULE_ARCHITECTURE.md)、[技能组合](SKILL_COMPOSITION_DESIGN.md)、[运行状态与调度](RUNTIME_REFACTOR_IMPLEMENTATION.md)、[结算返回](RESOLUTION_PROTOCOLS.md)。
