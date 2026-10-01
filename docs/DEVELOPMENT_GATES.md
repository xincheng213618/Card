# 开发门禁边界

当前规则 epoch 以 [GameCheckpoint](../src/CardGame.Core/Replay.cs) 的 `CurrentRulesVersion` 为准；技能 JSON 格式以 [SkillProgramCatalog](../src/CardGame.Core/SkillPrograms.cs) 的 `RulesSchemaVersion` 为准；内容包发布版本以对应包的 `CurrentVersion` 为准。不要在开发指南或逐人物测试里复制“当前”数值。

开发期 Checkpoint 同时精确检查 schema、rules epoch、内容包签名和内容哈希。新增普通武将、技能、卡牌或素材时，内容哈希已经能隔离旧存档；不逐人物提升全局 rules/schema 或内容包版本。只有同一内容指纹下既有命令的结算、事件顺序、随机数消费或暂停恢复语义改变，才提升 rules epoch。只有规则 JSON 出现不兼容格式变化，才提升 rules schema；已有节点的新组合和向后兼容的可选能力不提升 schema。内容包版本用于明确的发布批次。Checkpoint 自身结构变化才提升 Checkpoint schema。

## 内容开发

普通人物默认只改内容定义和武将池/绑定。复用现有机制的纯配置由通用加载、注册及已有机制行为检查覆盖；只有引入未覆盖的行为或修复缺陷时，才新增针对性的 Core 场景。UI 行为变化才新增 WPF 检查。共享能力变化应验证节点参数、非法上下文、资源消费和真实行为；需要时补充隐私、牌区守恒、暂停恢复与回放边界。不要逐人复制整份 JSON 到测试，也不要增加人物专用测试入口或维护多份架构“当前版本”流水账。

测试统一使用名称过滤，零匹配会失败：

```powershell
.\tools\Test-Changed.ps1
.\tools\Test-Changed.ps1 -CoreFilter 'Jiangchi'
.\tools\Test-Changed.ps1 -WpfFilter 'selection, target toggle'
.\tools\Test-Changed.ps1 -Full
```

[Test-Changed.ps1](../tools/Test-Changed.ps1) 在一次调用内构建一次并复用产物。开发中运行相关现有检查；一批共享机制完成后做一次适当的完整验证。内容 JSON 可先用 [Inspect-SkillProgram.ps1](../tools/Inspect-SkillProgram.ps1) 加载检查，但加载成功不能代替真实行为检查。提交前运行 `git diff --check`，仅修复本次引入的问题。

无参调用运行 [VerificationScopes.psd1](../tools/VerificationScopes.psd1) 定义的常规范围：命令与故障边界、快照隐私和冻结、内容指纹、牌区移动与守恒、技能执行与生命周期，以及代表性的用牌、响应和 UI 保存恢复。它不是全部武将场景、完整 AI 对局或发布验收；受影响的机制仍需名称过滤检查，批次结束运行 `-Full`。直接无参运行 Core/WPF 程序集仍执行该程序全部登记。

每组名称过滤在一个测试进程中取并集，同一检查只执行一次；单个空过滤器或零匹配仍失败。脚本只有真正未提供过滤器时才进入常规范围，显式提供的过滤器若全部为空则拒绝。所有检查的失败计数、进程退出码和构建结果保留在 `summary.json` 与日志中；WPF 单项失败会被记录并继续其余检查，最终仍返回失败。

合并旧分支或工作树时保留主线已完成的测试裁剪，只带入未覆盖行为或缺陷回归所需的新检查。不能为了恢复历史测试数量或沿用旧分支约定而整套回取已删除测试。测试冲突按当前保留的覆盖范围逐项处理；删除过时用例时同步删除注册、方法和无人使用的辅助代码，不改动历史兼容实现或存储数据来凑数量。

默认验证以增量构建加测试约一分钟为目标。优先复用小型场景或已验证能触发目标行为的固定种子，避免每个用例重新遍历大量种子或重复跑完整对局。保留代表性的集成场景及其关键断言；一批改动结束后实测耗时，不能只凭测试数量估算速度。

历史版本决策和已完成批次保存在 [技能迁移计划](SKILL_MIGRATION_PLAN.md) 与专门的历史记录中。本页只维护当前开发规则。
