# 武将图鉴分类与布局

图鉴采用顶部系列页签、系列子分类、分组卡墙、底部势力筛选与搜索，以及点击卡牌打开的技能详情。分类属于 WPF 展示数据，不修改武将 ID、规则包、技能、存档或回放格式。

## 系列

| 页签 | 子分类 |
| --- | --- |
| 全部 | 按系列分组展示已注册武将 |
| 标准 | 标准版 |
| 神话再临 | 风、火、林、山、阴、雷 |
| 一将成名 | 一、二、三、四、五、六、七 |
| 界限突破 | 标准及神话再临的界限突破版本 |
| 界一将 | 一将成名的界限突破版本 |
| 神武将、SP、谋 | 各自单独展示 |
| 其他扩展 | 尚未指定系列的正式扩展武将 |
| 机制演示、国战试验 | 保留项目已有的演示与独立国战版本 |

只展示内容注册表中的真实条目。进入具体系列时保留其全部子分类；当前没有条目的分组显示“当前版本暂无此分组武将”。“全部”页不插入空分组；搜索或势力筛选无结果时提供清除筛选入口。保留分类入口不代表对应武将及技能已经实现。

## 武将归属与依据

`src/CardGame.Wpf/Presentation/GeneralGalleryCatalog.cs` 按完整武将 ID 指定归属。不能把所有 `classic:` 条目都视为标准版，也不能把演示包 `standard:` 当作正式标准版。未来未显式归类的正式条目进入“其他扩展”，避免根据同名武将猜测版本。

- 标准、风、火、林的基础归属参考官方[国战资料中的武将扩展标记](https://sanguosha.com/news/20211019_5172_0210)，结合项目冻结的具体技能版本。公孙瓒独立归入 SP。
- 一将成名一至三参考官方[2011 武将名单](https://www.sanguosha.com/news/20140418_2046_5959)、[2012 武将名单](https://www.sanguosha.com/news/20150723_5319_3515)、[2013 武将名单](https://www.sanguosha.com/news/20150928_5653_3409)。当前华雄为“耀武”版本，按标准展示，不把历史 2012 版本作为第二个条目。
- 一将成名六、七的界面编号遵循用户参考图，分别衔接官方[原创设计 2016](https://www.sanguosha.com/news/20160721_8948_3717)与[原创设计 2017](https://www.sanguosha.com/news/20170503_9728_3716)系列；当前没有注册条目时仅展示空分组。
- 阴、雷按用户确认一起加入。官方[阴雷扩展公告](https://sanguosha.com/news/20180913_4065_2911)及[阴包武将说明](https://www.sanguosha.com/news/20220510_3965_4310)用于核对归属；当前严颜归入阴包。
- 神关羽归入神武将，界张角归入界限突破。神势力筛选仅规范图鉴显示，不改变引擎的势力数据。
- 项目具体版本证据继续以 `docs/content/sources/` 中的现有记录为准。本次没有改动这些冻结记录。

## 交互与验证

系列、子分类、势力与武将名 / 技能搜索叠加生效；切换筛选回到卡墙顶部。小窗口下顶部系列可用箭头、滚轮及键盘焦点横向浏览。点击武将显示完整技能；Escape 先收起详情，再关闭图鉴。沿用已有立绘，缺图时明确显示“暂无立绘”。

`tests/CardGame.Wpf.Tests/GeneralGalleryChecks.cs` 覆盖代表武将归类、注册表无重复遗漏、六包和七期、组合筛选、空状态、选中高亮、详情与 Escape、滚动重置、小窗口控件范围，以及浏览不推进对局状态。`Program.CheckGeneralGallery` 保留模态暂停和恢复检查。

PowerShell 验证命令：

```powershell
dotnet build tests\CardGame.Wpf.Tests\CardGame.Wpf.Tests.csproj -c Release --artifacts-path .artifacts\gallery-redesign
dotnet .artifacts\gallery-redesign\bin\CardGame.Wpf.Tests\release\CardGame.Wpf.Tests.dll .artifacts\gallery-redesign\screenshots '--filter=general gallery'
```

截图由实际 WPF 控件离屏渲染，用于检查 1440×880 和 1120×740 的排版；不代表桌面端人工试玩验收。
