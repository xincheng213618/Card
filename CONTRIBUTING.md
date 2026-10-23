# Contributing / 参与贡献

欢迎通过 [Issues](https://github.com/xincheng213618/sanguosha-dotnet/issues) 提交问题、规则依据、文档修正与小型扩展。中文和英文都可以。第一次参与可以从标记为 `good first issue` 的工作开始。

Questions, reproducible bug reports, sourced rule corrections and small contributions are welcome in Chinese or English. Start with the [developer quickstart](docs/GETTING_STARTED.md), [architecture](docs/ARCHITECTURE.md), and [character module conventions](docs/CHARACTER_MODULES.md).

## 开始开发 / Getting started

1. Fork 仓库，在自己的分支上开发；一个 PR 聚焦一个可说明的问题。
2. 按 [global.json](global.json) 安装 .NET SDK；常规验证另需 Windows、PowerShell 7 和 PATH 中可用的 Python 3.10 或更新版本（仅需标准库）。
3. 先运行无界面示例，再按修改范围运行已有名称过滤器。
4. 提交前检查差异和本次批次的常规范围；在 PR 中报告实际执行结果。

```powershell
dotnet run --project .\samples\CardGame.Headless\CardGame.Headless.csproj
pwsh -File .\tools\Test-Changed.ps1 -CoreFilter 'program interaction '
pwsh -File .\tools\Test-Changed.ps1 -WpfFilter 'selection, target toggle'
pwsh -File .\tools\Test-Changed.ps1
git diff --check
```

Work on a focused branch. Install the SDK selected by `global.json`. The routine smoke lane needs Windows, PowerShell 7 and Python 3.10 or newer on PATH; only the Python standard library is required. Its four Core checks target about one second of test-process time, excluding the build. Use the existing named filters for relevant engine and WPF regressions during development; run the routine lane after a batch and state exactly what you ran. CI uses the same routine lane and executes the headless example.

完整验证仅在明确要求手动完整运行时执行；不要在 CI、普通开发批次或常规失败后自动运行 `-Full` 或无过滤测试程序集。常规范围与全部登记检查是不同范围，不能把其中一种结果报告成另一种。

Full verification is manual only, on an explicit request. Do not automatically run `-Full` or an unfiltered test assembly in CI, after routine failures, or after a normal batch. Distinguish focused checks, routine checks, full verification and interactive desktop acceptance in your report.

## 武将与规则 / Characters and rules

- 优先在 `src/Characters/<character>/<variant>/` 中修改人物声明、技能规则与展示文件。复用现有能力的普通武将无需修改共享引擎或增加重复的定义快照测试。
- 对尚未覆盖的新行为和缺陷修复添加针对性的 Core 检查；UI 行为使用 WPF 检查；共享能力变化验证共享边界。沿用现有 runner 与 `--filter=<name>`。
- 提供规则来源和适用版本。实际支付、实体牌移动、隐私投影、暂停恢复及回放必须与行为一致。
- 普通人物改动不提高全局规则版本、技能 JSON schema 或内容包版本。版本来源和兼容边界见[开发门禁](docs/DEVELOPMENT_GATES.md)。

Prefer existing content capabilities. Add focused checks for uncovered behavior or regressions, WPF checks for UI changes, and shared mechanism checks when shared capabilities change. Cite the source and edition of a rule. Preserve command validation, real payment/card movement, seat-specific privacy, owning-frame continuation and replay. Do not raise format or package versions for each new character.

## 问题报告与 PR / Issues and pull requests

报告问题时请提供提交版本、模式、预期与实际行为、最短复现步骤，以及相关命令或检查结果。规则差异请附来源和版本。涉及存档时先移除个人路径与不打算公开的信息；checkpoint 可能包含整局私有状态。

A useful report includes the commit, game mode, expected/actual behavior and a short reproduction. For rule discrepancies, include a source and edition. Checkpoints can contain private game state; review them before sharing. PR descriptions should explain the problem, resulting behavior and validation, including any checks that could not run.

## 来源、许可与临时输出 / Sources, licensing and temporary files

本项目原创软件代码和自写文档采用 [MIT](LICENSE)。提交时应有权提供相应内容，并保留原有归属与第三方许可；第三方素材和抓取资料的范围见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

Original software and project-authored documentation use MIT. Contribute material you have the right to provide, preserve attribution and existing third-party licenses, and record sources. Artwork, audio and scraped reference text are not covered by the project's software license.

截图、验证日志、恢复档案和临时构建产物放在忽略的 `.artifacts/benchmarks/` 或验证脚本的临时目录，不提交 Git。任务完成后按 [AGENTS.md](AGENTS.md) 的归属、锁和路径边界清理本任务输出。长期规则解释与来源留在对应文档中。

Keep disposable screenshots, logs, archives and build outputs out of Git. Use the ignored artifact area or the verification runner's temporary directory, clean up task-owned outputs after inspection, and respect other contributors' files and locks.
