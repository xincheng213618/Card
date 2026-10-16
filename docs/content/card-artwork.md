# 卡牌与牌桌素材

[card-art-catalog.json](card-art-catalog.json) 维护卡牌和组件的来源、文件、裁切、适配与 SHA-256。牌面位于 `src/CardGame.Wpf/Assets/Cards/`，按 `CardKind` 绑定，当前覆盖范围由清单和 `CardCatalog.ImplementedCards` 核对。

## 素材来源

| 类型 | 处理方式 |
| --- | --- |
| 官方固定图集 | 校验 `cards.webp` 与 `cards.atlas` 输入哈希，按精灵帧裁切牌面、点数、花色与装备条 |
| 官方完整牌图 | 使用清单的 `officialCardFace` 来源及缩放参数，保留原印刷内容 |
| 官方插画 | 使用 `officialIllustration` 来源，保持宽高比缩放并在透明画布补边 |
| 项目生成素材 | `projectGenerated` 记录现有生成脚本、尺寸与哈希，导入器验证并保留文件 |

鬼龙斩月刀对应固定图集中的独立牌面与装备条。赤血青锋使用来源已记录的 500×500 官方插画，等比例缩为 186×186，置于 186×260 透明画布；刑天破军斧使用 93×130 官方完整牌图，以最近邻放大两倍。`GeneralWeapon` 和 `RedBloodBlade` 使用项目生成素材，原始脚本为 `tools/Build-GeneratedWeaponArtwork.ps1`。

牌面不烘焙运行时花色、点数或转化效果。手牌、起手牌、公开牌与指南共用相应模板；花色点数来自当前可见状态，未知或缺失素材显示文字卡牌。图片不进入规则存档或 Core 内容定义。

## 重建与校验

在仓库根目录运行 PowerShell，ffmpeg 需在 PATH 中：

```powershell
pwsh -File .\tools\Import-CardArtwork.ps1
pwsh -File .\tools\Import-CardArtwork.ps1 -ValidateOnly
pwsh -File .\tools\Import-CardArtwork.ps1 -Kinds ScarletBloodSword,XingtianAxe
```

`-SourceDirectory` 指定已有输入目录，`-Ffmpeg` 指定解码程序。导入器校验来源域名、输入哈希、尺寸、atlas 帧、缩放比例与输出尺寸，所有选中条目验证后才写交付图片与清单。`-ValidateOnly` 不改交付图片或清单；`-Kinds` 仅处理指定牌面，不改其他图和组件。

项目生成条目要求现有文件和记录哈希一致，缺图或冲突会明确报错。官方插画使用 Lanczos 等比例缩放与透明补边，完整牌图按记录的最近邻方式放大；输出哈希写回清单。

## 牌桌与技能

[table-art-catalog.json](table-art-catalog.json) 保存「天下牌局」背景、边框、装备空槽、操作与技能按钮的来源、atlas 帧和哈希。装备区从玩家快照显示真实牌面、花色和点数，中央公开牌使用已公开事件资料。

出牌操作区提供确定、取消、结束；没有对应操作时按钮禁用。本地对局没有操作时限。技能按钮按内容技能 ID 和当前技能拥有者重新查找合法动作，只有可执行动作才能进入选择；费用和目标选齐后由主按钮或 Enter 提交。当前不能主动使用的技能保留状态与说明。

装备效果、共享入口与额外激活按合法动作进入技能区或补充操作区；技能选择期间避免重复提交草稿。具体操作见 [主动技能](../SKILL_INTERACTION.md)、[响应](../HAND_RESPONSES.md) 与 [战斗反馈](../BATTLE_FEEDBACK.md)。

构建／包检查依据清单核对牌面、组件与牌桌资源的哈希和尺寸。原素材中的印刷文字属于图像，工具提示与指南正文使用当前内容规则。
