# 开发门禁边界

当前规则 epoch 以 [GameCheckpoint](../src/CardGame.Core/Replay.cs) 的 `CurrentRulesVersion` 为准；技能 JSON 格式以 [SkillProgramCatalog](../src/CardGame.Core/SkillPrograms.cs) 的 `RulesSchemaVersion` 为准；内容包发布版本以对应包的 `CurrentVersion` 为准。不要在开发指南或逐人物测试里复制“当前”数值。

开发期 Checkpoint 同时精确检查 schema、rules epoch、内容包签名和内容哈希。新增普通武将、技能、卡牌或素材时，内容哈希已经能隔离旧存档；不逐人物提升全局 rules/schema 或内容包版本。只有同一内容指纹下既有命令的结算、事件顺序、随机数消费或暂停恢复语义改变，才提升 rules epoch。只有规则 JSON 出现不兼容格式变化，才提升 rules schema；已有节点的新组合和向后兼容的可选能力不提升 schema。内容包版本用于明确的发布批次。Checkpoint 自身结构变化才提升 Checkpoint schema。

## 内容开发

普通人物默认只改内容定义和武将池/绑定。复用现有机制的纯配置由通用加载、注册及已有机制行为检查覆盖；只有引入未覆盖的行为或修复缺陷时，才新增针对性的 Core 场景。UI 行为变化才新增 WPF 检查。共享能力变化应验证节点参数、非法上下文、资源消费和真实行为；需要时补充隐私、牌区守恒、暂停恢复与回放边界。不要逐人复制整份 JSON 到测试，也不要增加人物专用测试入口或维护多份架构“当前版本”流水账。

测试统一使用名称过滤，零匹配会失败：

```powershell
.\tools\Test-Changed.ps1
.\tools\Test-Changed.ps1 -CoreFilter 'program interaction '
.\tools\Test-Changed.ps1 -WpfFilter 'selection, target toggle'
.\tools\Test-Changed.ps1 -Full
```

[Test-Changed.ps1](../tools/Test-Changed.ps1) 在一次调用内构建一次并复用产物。开发中运行相关现有检查；批次结束运行常规验证，完整验证只在用户明确要求时执行。内容 JSON 可先用 [Inspect-SkillProgram.ps1](../tools/Inspect-SkillProgram.ps1) 加载检查，但加载成功不能代替真实行为检查。提交前运行 `git diff --check`，仅修复本次引入的问题。

无参调用运行 [VerificationScopes.psd1](../tools/VerificationScopes.psd1) 定义的常规范围：命令与故障边界、快照隐私和冻结、内容指纹、牌区移动与守恒、技能执行与生命周期，以及代表性的用牌、响应和 UI 保存恢复。受影响的机制仍需名称过滤检查。`-Full` 和直接无参运行 Core/WPF 程序集会执行全部登记检查，均只在用户明确要求完整验证时运行。

常规范围使用明确的检查名称，新增人物时通过名称过滤验证受影响的行为，不把每个人物的全部场景持续追加到默认范围。只有新增共享边界或替换已有代表场景时才调整常规清单，并重新测量包含增量构建的耗时。

每组名称过滤在一个测试进程中取并集，同一检查只执行一次；单个空过滤器或零匹配仍失败。脚本只有真正未提供过滤器时才进入常规范围，显式提供的过滤器若全部为空则拒绝。所有检查的失败计数、进程退出码和构建结果保留在 `summary.json` 与日志中；WPF 单项失败会被记录并继续其余检查，最终仍返回失败。

默认输出目录按仓库路径固定为 `%TEMP%\CardGame-TestChanged-<仓库哈希>`，同一仓库的运行通过锁互斥。不要每个功能新建一份数 GB 的验证目录。成功、构建失败或检查失败后，将该目录内的 `bin`、`obj`、`renders` 登记到 [cleanup_queue.json](../tools/cleanup_queue.json) 文本清单；保留产物供查看和增量构建。登记失败会给出具体路径和原因，并返回非零退出码。

代理只登记目录，不执行删除。用户完成查看后，双击仓库根目录的 `clean.cmd` 即可清理，窗口会保留执行结果；也可以手动运行以下命令。`clean.cmd --list` 查看清单，`clean.cmd --dry-run` 只预览。脚本仅处理清单中的精确目录，删除成功或目录已不存在才移除记录；占用、路径校验或删除失败时保留记录及错误原因。清单使用 UTF-8 JSON，可直接查看；优先使用 `--add` 自动去重并安全更新。`-KeepArtifacts` 保持兼容，已不再需要。

```powershell
python tools/cleanup_temp.py --list       # 查看待清理目录
python tools/cleanup_temp.py --dry-run    # 只预览，不删除或修改清单
python tools/cleanup_temp.py              # 用户手动执行清理
python tools/cleanup_temp.py --add 'C:\Temp\Card-task\outputs' --root 'C:\Temp\Card-task' --reason '本任务验证结束'
```

显式指定 `-ArtifactsPath` 也遵循登记流程。原始目录、截图和日志如需长期保留，应在登记前保存必要证据。登记不代表磁盘空间已经释放。

合并旧分支或工作树时保留主线已完成的测试裁剪，只带入未覆盖行为或缺陷回归所需的新检查。不能为了恢复历史测试数量或沿用旧分支约定而整套回取已删除测试。测试冲突按当前保留的覆盖范围逐项处理；删除过时用例时同步删除注册、方法和无人使用的辅助代码，不改动历史兼容实现或存储数据来凑数量。

清理辅助代码时保留特性发现、反射调用、扩展方法和 partial 类型跨文件使用的入口；不能仅凭单个文件中的名称引用次数判定这些成员无用。

默认验证以增量构建加测试约一分钟为目标。优先复用小型场景或已验证能触发目标行为的固定种子，避免每个用例重新遍历大量种子或重复跑完整对局。保留代表性的集成场景及其关键断言；一批改动结束后实测耗时，不能只凭测试数量估算速度。

历史版本决策和已完成批次保存在 [技能迁移计划](SKILL_MIGRATION_PLAN.md) 与专门的历史记录中。本页只维护当前开发规则。
