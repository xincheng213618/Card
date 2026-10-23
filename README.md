# Sanguosha.NET · 群雄逐鹿

[English](README.en.md) · [开发起步](docs/GETTING_STARTED.md) · [文档目录](docs/README.md) · [参与贡献](CONTRIBUTING.md)

[![CI](https://github.com/xincheng213618/sanguosha-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/xincheng213618/sanguosha-dotnet/actions/workflows/ci.yml)

**使用 C# / .NET 8 实现的三国杀（Sanguosha）规则引擎与 Windows WPF 单机客户端。** 希望为卡牌规则研究、自定义武将、AI 实验和其他界面实现提供可以运行、阅读和扩展的基础。

本项目为社区独立实现，与三国杀官方无隶属或背书关系。玩法以各模式和武将明确记录的版本为准；实现范围和缺口见[模式与规则](docs/MODES_AND_RULESETS.md)、[活动场清单](docs/ACTIVITY_MODES.md)及[能力缺口](docs/SANGUOSHA_GAP_PLAN.md)。

## 这个项目能提供什么

| 你想做的事 | 已有基础与入口 |
| --- | --- |
| 运行本地游戏 | WPF 牌桌、AI 对手、身份场、2v2、国战及活动场；见[玩家指南](docs/PLAYER_GUIDE.md) |
| 局域网开房 | 五人/八人标准身份局，真人与 AI 混合入座、准备、开始；见[联机原型](docs/LAN_PROTOTYPE.md) |
| 接入自己的界面 | Core 不依赖 WPF；用命令提交操作，用当前座位的快照展示信息；见[可运行示例](samples/CardGame.Headless/README.md) |
| 扩展武将和技能 | 按人物、版本组织的内容声明、技能 JSON 和运行模块；见[开发起步](docs/GETTING_STARTED.md)和[人物模块](docs/CHARACTER_MODULES.md) |
| 研究结算和回放 | 命令校验、嵌套结算、原子牌区移动、玩家隐私投影、checkpoint 与命令回放；见[架构说明](docs/ARCHITECTURE.md) |
| 尝试 AI 策略 | 可读的规则查询与现有决策实现；见[AI 说明](docs/TACTICAL_AI.md)和 [SimpleAi.cs](src/CardGame.Core/SimpleAi.cs) |

当前以源码方式使用，扩展内容随项目编译；尚未承诺稳定的外部 SDK、二进制插件协议或公网联机服务。局域网房间处于原型阶段。桌面界面需要 Windows；Core、联机服务端和无界面示例以 `net8.0` 为目标，CI 在 Windows 上验证。

## 快速运行

安装 Git、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。SDK 选择以 [global.json](global.json) 为准。以下命令使用 PowerShell：

```powershell
git clone https://github.com/xincheng213618/sanguosha-dotnet.git
Set-Location sanguosha-dotnet

# 无界面：提交真实命令、读取玩家快照、序列化 checkpoint 并恢复回放
dotnet run --project .\samples\CardGame.Headless\CardGame.Headless.csproj

# Windows：启动 WPF 游戏
dotnet run --project .\src\CardGame.Wpf\CardGame.Wpf.csproj
```

Windows 用户也可以双击 [Play.cmd](Play.cmd)：源码目录会构建并启动当前开发版本；只有分发目录才默认启动已有游戏包。源码目录要打开旧包，可运行 `tools\Start-Game.ps1 -UsePackage`。无界面示例不读取桌面存档，不需要图片、音频或 WPF。

接入自己的界面时请使用 `CreateSnapshot(viewerSeat)` 生成玩家视图。结算栈与 checkpoint 属于可信宿主数据，可能包含私有信息，不应直接发送给其他玩家。

## 玩法与操作

大厅另有[山河图剧情征程](docs/SHANHE_CAMPAIGN.md)：首批“诸侯伐董·本地起步版”提供三章分路、战法与额外技能组合、奇遇商店、真实战斗和独立征程存档。

开局面板提供经典八人身份、经典五人身份、2v2、国战及活动场入口，可选择身份或队伍、选将方式和弃牌设置。剑阁决战默认五阶武将与人机搭档，提供普通、进阶、困难三档；驱鬼逐邪提供四关挑战；虎牢关采用OL 2022双阶段神吕布；神武再世采用OL 2017六席单关，提供共享神将、专属装备与四类凶兽首领；挑战年兽采用OL 2016四席版，包含四态轮换、共享三次复活和30分钟时限；十殿阎罗采用OL 2020四关版，包含孟婆失技、回魂与隐藏地藏王；文和乱武采用十周年2019八人生存版，包含七种轮次规则、四张活动锦囊、毒与个人排名，其余七人由AI控制；龙舟争渡采用OL 2018四队两轮版，包含共享至宝、同船选将、五张活动牌及四名专将。合作活动默认五阶玩家和人机搭档，各活动采用自己明确记录的等阶规则。实际模式范围见 [模式与规则](docs/MODES_AND_RULESETS.md)，其他官方活动的版本与实现进度见 [活动场清单](docs/ACTIVITY_MODES.md)。

1. 开始对局后选择武将；私有候选只向当前选择者显示。
2. 出牌时点选可用手牌，再点选合法目标；操作区展示实际牌名、转化效果和所需目标。
3. 点击主按钮或按 Enter 确认；Esc 取消当前选择，1–9 选择手牌，Ctrl+Enter 结束出牌。
4. 响应时选择亮起的手牌并确认；多牌组合、装备效果、技能与放弃选项按中央公布的精确候选执行。
5. 自动推进在需要玩家操作时暂停；可以调节速度、暂停或单步观察。F5 独立保存手动存档。

牌桌显示公开身份与武将信息、装备、判定区、连环和翻面状态。隐藏手牌、身份及私有选择按玩家视角投影；开发者视图属于可信本机诊断。

当前规则覆盖基础牌、普通与延时锦囊、装备、伤害与回复、濒死救援、死亡及模式胜负。技能通过人物内容、技能 JSON 与独立运行模块注册；具体人物规则以所属版本的说明和实现为准。

| 功能 | 说明 |
| --- | --- |
| 选牌、目标与响应 | [目标选择](docs/CARD_TARGET_SELECTION.md)、[手牌响应](docs/HAND_RESPONSES.md)、[技能交互](docs/SKILL_INTERACTION.md) |
| 铁索与重铸 | [铁索连环](docs/IRON_CHAIN_RECAST.md) |
| 教学与指南 | [新手演练](docs/NEW_PLAYER_TUTORIAL.md)、[玩家指南](docs/PLAYER_GUIDE.md) |
| 保存与继续 | [本地存档与恢复](docs/SAVE_AND_RESUME.md) |
| 设置与反馈 | [本机偏好](docs/PLAYER_PREFERENCES.md)、[战斗反馈](docs/BATTLE_FEEDBACK.md)、[音频](docs/BATTLE_AUDIO.md) |
| 战绩与观战 | [战绩](docs/MATCH_HISTORY.md)、[对局速度与观战](docs/PLAYBACK_AND_SPECTATING.md) |
| 武将与素材 | [武将图鉴](docs/GENERAL_GALLERY.md)、[素材目录](docs/RESOURCE_CATALOG_DESIGN.md) |

## 开发入口

| 职责 | 入口 |
| --- | --- |
| 领域模型与玩家快照 | [Domain.cs](src/CardGame.Core/Domain.cs) |
| 命令、询问与精确选择 | [Commands.cs](src/CardGame.Core/Commands.cs) |
| 引擎与提交会话 | [GameEngine.cs](src/CardGame.Core/GameEngine.cs)、[GameEngine.CommandSession.cs](src/CardGame.Core/GameEngine.CommandSession.cs) |
| 结算帧与事件 | [Resolution.cs](src/CardGame.Core/Resolution.cs)、[Events.cs](src/CardGame.Core/Events.cs) |
| 卡牌区域与移动 | [CardZones.cs](src/CardGame.Core/CardZones.cs) |
| 内容注册与指纹 | [ContentRegistry.cs](src/CardGame.Core/ContentRegistry.cs) |
| 人物资料、技能与运行能力 | [人物目录](src/Characters/README.md)、[注册约定](docs/CHARACTER_MODULES.md) |
| 技能配置与执行 | [SkillPrograms.cs](src/CardGame.Core/SkillPrograms.cs)、[配置说明](docs/content/skill-composition/RUNTIME.md) |
| AI 策略 | [SimpleAi.cs](src/CardGame.Core/SimpleAi.cs)、[AI 说明](docs/TACTICAL_AI.md) |
| Checkpoint 与回放 | [Replay.cs](src/CardGame.Core/Replay.cs) |
| WPF 视图模型与牌桌 | [MainViewModel.cs](src/CardGame.Wpf/ViewModels/MainViewModel.cs)、[MainWindow.xaml](src/CardGame.Wpf/MainWindow.xaml) |

[架构说明](docs/ARCHITECTURE.md) 介绍当前调用链；[核心控制契约](docs/CORE_CONTROL_DESIGN.md) 与 [卡牌生命周期](docs/CARD_LIFECYCLE_CONTRACT.md) 说明提交、结算和隐私边界。内容职责与开发检查见 [开发门禁](docs/DEVELOPMENT_GATES.md)；规则来源及内容资料见 [参考数据](docs/content/BWIKI_REFERENCE_DATA.md)。

## 验证

验证脚本需要 Windows、PowerShell 7，以及 PATH 中可用的 Python 3.10 或更新版本（登记临时输出使用，不需要额外 Python 包）。无参运行选择常规范围，开发时按检查名称过滤：

```powershell
pwsh -File .\tools\Test-Changed.ps1
pwsh -File .\tools\Test-Changed.ps1 -CoreFilter 'program interaction '
pwsh -File .\tools\Test-Changed.ps1 -WpfFilter 'selection, target toggle'
```

常规清单在 [VerificationScopes.psd1](tools/VerificationScopes.psd1)，全部登记名称在 Core/WPF 测试入口中。完整验证使用 `-Full`，仅在明确要求手动完整验证时执行。日志和临时构建目录的生命周期见 [开发门禁](docs/DEVELOPMENT_GATES.md)。

CI 运行同一常规范围，并构建、运行无界面示例。欢迎提交可复现的问题、规则依据、文档修正和小型扩展。参与方式见[贡献指南](CONTRIBUTING.md)；通过 [Issues](https://github.com/xincheng213618/sanguosha-dotnet/issues) 提问或报告问题，第一次参与可以从 [good first issue](https://github.com/xincheng213618/sanguosha-dotnet/issues?q=is%3Aissue%20is%3Aopen%20label%3A%22good%20first%20issue%22) 开始。

## 许可与素材

本项目原创软件代码、脚本与自写文档采用 [MIT License](LICENSE)。第三方图片、音频、商标、抓取页面及引文（包括嵌入源码或内容文件的原文）不在此授权范围内；来源与具体边界见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。复用规则引擎时，可以从不依赖桌面素材的 Core 和无界面示例开始。
