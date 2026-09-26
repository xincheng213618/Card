# 原始牌面接入

45 种已实现卡牌使用 `Assets/Cards/<CardKind>.png`。来源是已观察到的官方 CDN `cards.webp` 与配套 `cards.atlas`；具体网址、输入 SHA256、原始精灵名称、裁切矩形及输出 SHA256 记录在 `card-art-catalog.json`。

牌面按 `CardKind` 绑定并缓存为冻结图片，不按中文牌名推断。花色、点数、技能转化和当前规则仍由游戏状态提供，图片不写入存档或 Core 内容注册表。手牌、开局发牌及公开亮牌共用 `CardFaceTemplate`，指南按当前选中牌种显示同一图片。未指定牌种或文件缺失时显示文字卡牌，不能推断其他玩家的暗牌。

## 重建素材

在仓库根目录使用 PowerShell 和 PATH 中的 ffmpeg：

```powershell
& tools/Import-CardArtwork.ps1
```

脚本下载清单指定的两个输入，校验固定 SHA256，解码 WebP 后按 atlas 坐标逐张导出 186×260 PNG。也可以用 `-SourceDirectory` 指定已有 `cards.webp`、`cards.atlas` 的目录，或用 `-Ffmpeg` 指定可执行文件。输入版本、尺寸、裁切方式不匹配时停止，避免静默导入错牌。

## 验证

WPF 检查 `--filter=original card artwork` 覆盖所有牌面的加载、真实手牌的花色点数/选择、禁用状态及指南；渲染图保存在测试输出目录。另运行手牌滚动和指南交互检查。发布包验证所有牌面 SHA256、尺寸和已实现牌种覆盖率，并记录 `CardAssets` 数量。牌面内印刷文字保留原素材，工具提示和指南规则正文以当前游戏规则为准。
