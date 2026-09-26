# 原始牌面接入

45 种已实现卡牌使用 `Assets/Cards/<CardKind>.png`。来源是已观察到的官方 CDN `cards.webp` 与配套 `cards.atlas`；具体网址、输入 SHA256、原始精灵名称、裁切矩形及输出 SHA256 记录在 `card-art-catalog.json`。

牌面按 `CardKind` 绑定并缓存为冻结图片，不按中文牌名推断。花色、点数、技能转化和当前规则仍由游戏状态提供，图片不写入存档或 Core 内容注册表。手牌、开局发牌及公开亮牌共用 `CardFaceTemplate`，指南按当前选中牌种显示同一图片。未指定牌种或文件缺失时显示文字卡牌，不能推断其他玩家的暗牌。

## 重建素材

在仓库根目录使用 PowerShell 和 PATH 中的 ffmpeg：

```powershell
& tools/Import-CardArtwork.ps1
```

脚本下载清单指定的两个输入，校验固定 SHA256，解码 WebP 后按 atlas 坐标导出 45 张 186×260 牌面及 54 个组件。组件包括 26 个红黑点数、4 个花色和 24 条 284×50 装备条。也可以用 `-SourceDirectory` 指定已有 `cards.webp`、`cards.atlas` 的目录，或用 `-Ffmpeg` 指定可执行文件。输入版本、尺寸、裁切方式不匹配时停止，避免静默导入错牌。

## 牌桌、装备与技能

牌桌采用 `Assets/Table/Board.jpg` 的原始「天下牌局」背景和 `gameBase`、`seatbottom`、`selfseat` 中的边框、装备空槽、操作按钮及技能按钮。18 个资源的来源网址、atlas 帧名、裁切坐标和 SHA256 在 `table-art-catalog.json` 中。装备空槽只使用 `self_seat_bg` 的空白首行，不使用含「废除」字样的 `self_equip_*` 图片。

布局适配现有八人牌局：左下五类装备槽，手牌从左侧连续排列，右下技能按钮与本方武将，右侧常驻牌局记录。中央最近出牌也绑定公开事件中的牌种。这里不从底层牌堆补充事件未公开的花色、点数。

`HumanEquipmentSlots` 从当前快照投影装备及其真实花色、点数；原始牌面不会写死这些值。技能按钮通过内容技能 ID（旧技能使用 `SkillKind`）重新查找当前合法动作，然后调用原有技能选择命令。主动技能可以点击，自动/锁定技能保持只读，完整说明在悬停提示中。多技能时区域可滚动，不挤占手牌或本方武将。

## 验证

WPF 检查 `--filter=original card artwork` 覆盖所有牌面的加载、真实手牌的花色点数/选择、禁用状态及指南；`--filter=reference table layout` 覆盖真实装备动作、装备条映射、三个窗口尺寸和技能按钮发动。`--filter=skill rail` 检查主动/锁定技能状态及说明。渲染图保存在测试输出目录。

发布包验证牌面 SHA256、尺寸和已实现牌种覆盖率，以及组件、嵌入牌桌资源的 SHA256，分别记录 `CardAssets`、`CardComponents`、`TableAssets`。牌面内印刷文字保留原素材，工具提示和指南规则正文以当前游戏规则为准。
