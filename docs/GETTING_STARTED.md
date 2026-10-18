# 开发入门 / Developer quick start

[中文](#中文) · [English](#english) · [新增武将 / Add a general](EXTENDING.md)

## 中文

这个项目把规则引擎、内容和 WPF 客户端分开。想研究规则、接自己的界面或编写工具，可以先运行不依赖 WPF 的控制台示例。

### 准备与运行

安装仓库 [global.json](../global.json) 指定的 .NET 8 SDK；Windows 桌面客户端另需 Windows 和 .NET 桌面开发环境。以下命令在仓库根目录的 PowerShell 中执行：

```powershell
dotnet run --project samples/CardGame.Headless
dotnet run --project samples/CardGame.Headless -- --custom-general
```

第一个命令使用标准演示内容。第二个额外编译并注册一个原创练习武将，复用现有技能，并通过独立练习模式选择它。两个命令都只运行开局和选将这两条命令，不模拟完整对局，不下载美术或音频，也不生成发布包。

成功时会看到 `Checkpoint JSON round trip and command replay: 2 commands; all 5 viewer snapshots match.`。程序会实际校验命令是否被接受、对手手牌是否隐藏，以及恢复和回放后的每个座位快照是否一致；失败时返回非零退出码。

源码入口：

| 文件 | 内容 |
| --- | --- |
| [Program.cs](../samples/CardGame.Headless/Program.cs) | 创建对局、提交命令、玩家视图、checkpoint JSON 往返、恢复与回放 |
| [TrainingGeneralPackage.cs](../samples/CardGame.Headless/TrainingGeneralPackage.cs) | 以 `IGameContentPackage` 注册武将与独立模式 |
| [CardGame.Core](../src/CardGame.Core) | 命令、规则状态机、合法动作、隐私投影与回放 |
| [CardGame.Content.Standard](../src/CardGame.Content.Standard) | 内置内容包和资源加载 |

### 接入自己的宿主

最小创建流程使用真实公开 API：

```csharp
var registry = StandardContentRegistry.Create();
var engine = GameEngine.CreateStandard(new GameOptions
{
    PlayerCount = 5,
    HumanSeat = 0,
    HumanRole = Role.Lord,
    UseInteractiveSetup = true,
    AiPolicyVersion = AiDecisionPolicy.CurrentVersion
}, registry);

var result = engine.Submit(new StartGameCommand(engine.Revision));
if (!result.Accepted)
    throw new InvalidOperationException(result.Error?.Message);

var playerView = engine.CreateSnapshot(viewerSeat: 0);
```

命名空间为 `CardGame.Core` 与 `CardGame.Content.Standard`。后续输入应来自当前玩家快照中的提示或引擎发布的合法动作，携带当前 revision 和对应 prompt ID；提交后检查 `Accepted`，再重新获取视图。不要自行拼接牌与目标组合，也不要通过旧兼容方法改变状态后再创建 checkpoint。

`CreateSnapshot(viewerSeat)` 是玩家边界。`CreateCheckpoint()`、`AcceptedCommands`、事件、移动记录和 `ResolutionStack` 属于可信宿主，可能含种子、隐藏身份、暗牌或私有选择，不应作为网络玩家载荷发送。控制台示例只在内存中序列化 checkpoint，没有把私有 JSON 打印或写入磁盘。

`GameReplay.Restore` 使用 checkpoint 的格式、包签名、内容 hash 和命令校验恢复；它不保证任意未来规则实现都能重放旧存档。宿主应处理恢复失败，保留原存档，再提供新建对局入口。详见 [核心控制](CORE_CONTROL_DESIGN.md)、[存档恢复](SAVE_AND_RESUME.md) 与 [架构](ARCHITECTURE.md)。

### 开发范围与验证

示例通过项目引用从源码编译。当前扩展路径包括编译自己的内容包，以及向 `src/Characters` 增加编入程序集的 JSON 或必要的运行代码；这里没有承诺热插拔插件、稳定的 NuGet SDK、多真人联网宿主或跨版本规则兼容。

新增武将按 [扩展指南](EXTENDING.md) 操作。涉及新机制时增加对应行为检查；验证范围和命令见 [开发门禁](DEVELOPMENT_GATES.md)。常规验证与完整验证是不同范围，完整验证仅在用户明确要求时手动运行。控制台示例成功也不代表 WPF 界面、素材或整场对局已验收。

## English

The rules engine, content packages, and WPF client are separate projects. Start with the console sample to inspect rules, build a host, or try a content extension without WPF.

### Run the sample

Install the .NET 8 SDK selected by [global.json](../global.json). Run these commands from the repository root:

```powershell
dotnet run --project samples/CardGame.Headless
dotnet run --project samples/CardGame.Headless -- --custom-general
```

The first run uses the standard demo content. The second compiles an original tutorial general into a separate content package and adds a training mode that makes it selectable. Each run submits only two commands: start the match and select a general. It does not simulate a complete match, fetch media, or publish a distribution.

Success includes `Checkpoint JSON round trip and command replay: 2 commands; all 5 viewer snapshots match.` The program checks command acceptance, hidden opponent hands, and byte-identical serialized player snapshots after both checkpoint restoration and command replay. A failed check exits with an error.

Read [Program.cs](../samples/CardGame.Headless/Program.cs) for the complete host flow and [TrainingGeneralPackage.cs](../samples/CardGame.Headless/TrainingGeneralPackage.cs) for the content extension. Both reference the actual Core and Content.Standard projects and require no third-party packages.

### Host boundaries

Create a registry with `StandardContentRegistry.Create()`, create the engine with `GameEngine.CreateStandard(options, registry)`, and submit `StartGameCommand` through `Submit`. The C# example in the Chinese section above is the minimal flow; import `CardGame.Core` and `CardGame.Content.Standard`.

Use the current prompt or published legal action to construct subsequent commands, carrying the current revision and matching prompt ID. Check `Accepted` after every submission and refresh the player's view. Keep card/target combinations intact. A state reached through legacy compatibility calls cannot be inferred into a checkpoint journal.

Send players only `CreateSnapshot(viewerSeat)`. Checkpoints, accepted commands, events, movement records, and `ResolutionStack` are trusted-host data and may reveal private information. The sample serializes checkpoint JSON in memory and does not print or save it.

`GameReplay.Restore` validates checkpoint format, package signatures, content hash, and commands. This is not a promise of compatibility with every future rule implementation. Handle restore failures without overwriting the original save. See [core control](CORE_CONTROL_DESIGN.md), [persistence](SAVE_AND_RESUME.md), and [architecture](ARCHITECTURE.md).

### Extend and verify

These are source-level extension points. The sample uses project references; built-in character JSON is embedded during compilation, and new runtime capabilities may require Core code. This guide does not promise a stable NuGet SDK, hot-loaded plugins, or a multiplayer server. The headless sample does not depend on WPF; the desktop client requires Windows.

Continue with [Add a general](EXTENDING.md). Follow the repository's [development gates](DEVELOPMENT_GATES.md) for focused behavior checks and routine verification. Full verification requires an explicit manual request. Passing this sample verifies its short command prefix, not desktop UI behavior or a complete match.
