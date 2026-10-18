# Headless Core sample / 无界面示例

Run from the repository root with the .NET 8 SDK selected by [global.json](../../global.json):

在仓库根目录使用 [global.json](../../global.json) 指定的 .NET 8 SDK 运行：

```powershell
dotnet run --project samples/CardGame.Headless
dotnet run --project samples/CardGame.Headless -- --custom-general
```

The sample references the actual Core and Content.Standard projects, with no WPF or third-party package dependency. It submits two commands through `Submit`, reads filtered player snapshots, serializes a checkpoint to JSON in memory, then verifies restoration and command replay for every seat.

示例直接引用 Core 与 Content.Standard，不依赖 WPF 或第三方包。它通过 `Submit` 提交开局与选将两条命令，读取玩家视图，在内存中往返序列化 checkpoint JSON，并验证恢复、命令回放后每个座位的快照一致。

Expected output for the standard path / 标准路径预期输出：

```text
Mode: identity:standard-5
Selected general: standard:guo-jia
Revision: 2; next decision: PlayCard
Player view: 6 own cards visible; opponents' hand cards hidden.
Checkpoint JSON round trip and command replay: 2 commands; all 5 viewer snapshots match.
```

`--custom-general` compiles and registers the original tutorial character in [TrainingGeneralPackage.cs](TrainingGeneralPackage.cs), reuses an existing skill, and selects it in an independent training mode. Its mode and selected general lines become `sample:training-5` and `sample:training-general`. The sample does not simulate a full match or exercise the skill's combat behavior.

`--custom-general` 编译并注册 [TrainingGeneralPackage.cs](TrainingGeneralPackage.cs) 中的原创练习武将，复用已有技能，在独立模式中选择它。模式与武将输出分别变为 `sample:training-5` 和 `sample:training-general`。示例没有模拟整场对局，也没有验证该技能的战斗效果。

Checkpoints and command journals are trusted-host data and can reveal private information. Send players only `CreateSnapshot(viewerSeat)`. This sample does not print or save checkpoint JSON. Restoration validates format, package signatures, content hash, and accepted commands; arbitrary future rule implementations may not replay the same save.

Checkpoint 与命令记录属于可信宿主数据，可能包含私有信息；玩家只接收 `CreateSnapshot(viewerSeat)`。示例不打印或保存 checkpoint JSON。恢复会校验格式、包签名、内容 hash 和命令，不能据此承诺未来任意规则版本都兼容旧存档。

See the [bilingual quick start](../../docs/GETTING_STARTED.md) and [general extension guide](../../docs/EXTENDING.md) for host boundaries, source-level extension steps, and verification scope.

宿主边界、源码扩展步骤与验证范围见 [双语开发入门](../../docs/GETTING_STARTED.md) 和 [新增武将指南](../../docs/EXTENDING.md)。
