# 群雄逐鹿 · 本地卡牌对战

Windows WPF 本地卡牌游戏。规则核心负责合法动作、结算、AI 和命令回放；界面负责玩家视图、操作、音画反馈与本地持久化。

全部功能说明与开发资料见 [文档目录](docs/README.md)。

大厅另有[山河图剧情征程](docs/SHANHE_CAMPAIGN.md)：首批“诸侯伐董·本地起步版”提供三章分路、战法与额外技能组合、奇遇商店、真实战斗和独立征程存档。

## 运行

需要 Windows 和 .NET 8 SDK，SDK 选择见 [global.json](global.json)。在仓库根目录执行：

```powershell
dotnet run --project .\src\CardGame.Wpf\CardGame.Wpf.csproj
```

也可以双击 [Play.cmd](Play.cmd)：有本地发行包时启动该包，否则构建并启动开发版本。

## 玩法与操作

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

验证脚本需要 PowerShell 7。无参运行选择常规范围，开发时按检查名称过滤：

```powershell
pwsh -File .\tools\Test-Changed.ps1
pwsh -File .\tools\Test-Changed.ps1 -CoreFilter 'program interaction '
pwsh -File .\tools\Test-Changed.ps1 -WpfFilter 'selection, target toggle'
```

常规清单在 [VerificationScopes.psd1](tools/VerificationScopes.psd1)，全部登记名称在 Core/WPF 测试入口中。完整验证使用 `-Full`，仅在明确要求手动完整验证时执行。日志和临时构建目录的生命周期见 [开发门禁](docs/DEVELOPMENT_GATES.md)。
