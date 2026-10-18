# 开发门禁边界

## 格式与内容边界

| 数据 | 版本来源 | 调整条件 |
| --- | --- | --- |
| Checkpoint | [GameCheckpoint.CurrentSchemaVersion](../src/CardGame.Core/Replay.cs) | 持久化结构发生需要区别处理的变化 |
| 技能规则 JSON | [SkillProgramCatalog.RulesSchemaVersion](../src/CardGame.Core/SkillPrograms.cs) | 不兼容的规则格式变化 |
| 桌面存档 | [GameSaveFile.CurrentFormatVersion](../src/CardGame.Wpf/Persistence/GameSaveStore.cs) | 存档外层格式变化 |
| 内容包 | 对应包的 `CurrentVersion` | 明确的内容发布批次 |
| AI 决策 | [AiDecisionPolicy](../src/CardGame.Core/Replay.cs) | 需要保留回放决策语义的策略变化 |

开发指南和逐人物测试引用这些来源，避免另写一份容易失效的“当前数值”。Checkpoint 检查结构、内容包签名、内容哈希和逐条命令合法性，再按当前实现回放。技能定义中的 `minimumRulesVersion` 参与既有内容指纹，不作为运行门禁；内容哈希区分开发中的定义变化。

复用已有节点的组合和兼容的可选能力可以沿用技能 schema；普通人物内容改动沿用内容包版本。桌面读取、校验或回放失败时初始化新牌局、停止自动推进，并保护失败的原存档，流程见 [本地存档与恢复](SAVE_AND_RESUME.md)。

## 内容开发

人物文件放在 `src/Characters/<character>/<variant>/`：`Content/*.character.json` 声明基础资料与注册，`Skills/` 保存规则及展示，`Runtime/` 保存独立规则能力；共享配置放在 `_shared/Skills/`。运行模块链接编译进 Core，可选 `Wpf/` 只编译进 WPF。注册细节见 [人物模块与注册](CHARACTER_MODULES.md)。

普通人物优先修改所属目录的内容和技能定义。纯配置由共享加载、注册及已有机制检查覆盖；引入尚未覆盖的行为或修复缺陷时，增加针对性的 Core 检查。UI 行为变化增加 WPF 检查。共享能力需验证节点参数、非法上下文、资源消费和真实规则行为，并按影响补充隐私、牌区守恒、暂停恢复与回放边界。

## 验证方式

[Test-Changed.ps1](../tools/Test-Changed.ps1) 需要 Windows、PowerShell 7 和 PATH 中可用的 Python 3.10 或更新版本；Python 仅使用标准库登记临时输出。脚本在一次调用内构建并复用产物。开发时运行相关名称过滤；批次结束运行 [VerificationScopes.psd1](../tools/VerificationScopes.psd1) 定义的常规范围：

```powershell
pwsh -File .\tools\Test-Changed.ps1
pwsh -File .\tools\Test-Changed.ps1 -CoreFilter 'program interaction '
pwsh -File .\tools\Test-Changed.ps1 -WpfFilter 'selection, target toggle'
```

常规范围覆盖命令与故障、快照和事件冻结、内容指纹、牌区移动、技能与生命周期，以及代表性的用牌、响应和界面保存恢复。具体名称以清单和测试入口为准。新增人物按名称验证受影响行为；默认清单只在共享边界或代表场景需要调整时更新，并测量包含增量构建的实际耗时，目标约一分钟。

同组过滤在一个进程中取并集，每项只执行一次。显式空过滤或零匹配会失败；全部登记检查由 `-Full` 或直接无过滤运行测试程序集触发，仅在用户明确要求手动完整验证时执行。失败后仍保留实际失败计数、进程退出码和构建结果。

[Inspect-SkillProgram.ps1](../tools/Inspect-SkillProgram.ps1) 可先检查内容装载，加载成功只说明配置通过相应检查。提交前运行 `git diff --check`，并按本次范围检查暂存内容。

## 临时输出与清理

默认验证目录按仓库路径固定为 `%TEMP%\CardGame-TestChanged-<仓库哈希>`，同仓库运行通过锁互斥。优先复用兼容产物；脚本将 `bin`、`obj` 和 `renders` 登记到 [cleanup_queue.json](../tools/cleanup_queue.json)，运行结果保存在该目录的 `summary.json` 与日志中。显式 `-ArtifactsPath` 遵循相同登记流程，登记失败会报告路径并返回失败。

登记只记录待清理路径。任务完成查看后，代理按 [AGENTS.md](../AGENTS.md) 清理本任务可丢弃产物，保留必要日志和交付物；清理前验证绝对路径、归属、链接和消费者状态，并遵守验证锁。遇到策略阻止或仍占用的目录，保留精确记录与原因，不能绕过拒绝或清理其他任务的历史目录。

用户可双击 [clean.cmd](../clean.cmd) 重试队列，也可先查看或预览：

```powershell
python -B tools/cleanup_temp.py --list
python -B tools/cleanup_temp.py --dry-run
```

任务内保留目录使用 `--add <绝对目录> --root <已验证的临时根目录> --reason <原因>` 登记；验证产物同时指定 `--lock-root <验证目录>`。清理成功或确认已不存在时只移除相应记录。长久说明保留规则依据与源码入口，截图、基准日志和恢复档案等临时材料需要仓库内位置时放在忽略的 `.artifacts/benchmarks/`，不纳入文档引用或 Git。

## 测试维护

合并旧分支时按当前保留覆盖范围处理测试，只引入未覆盖行为或回归所需的新检查。删除过时检查时同步处理注册、方法和无人使用的辅助代码；保留 checkpoint、回放、指纹、输入、隐私和牌区边界。清理辅助代码需检查特性发现、反射、扩展方法和 partial 跨文件引用。

优先使用小型场景和已验证种子，保留代表性集成场景及关键断言，避免重复搜索种子或完整对局模拟。普通开发与验证使用构建及检查流程；发布、安装器和分发包按明确的打包请求执行。
