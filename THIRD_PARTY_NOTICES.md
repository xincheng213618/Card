# Licensing scope and third-party notices / 许可范围与第三方资料

## Original software / 原创软件

The [MIT License](LICENSE) applies to this project's original software implementation, including original C#/XAML code, tests, scripts, machine-readable rule implementations, examples and project-authored documentation. The exceptions below apply regardless of the file containing the material.

[MIT](LICENSE) 授权覆盖本项目原创软件实现，包括原创 C#/XAML 代码、测试、脚本、机器规则实现、示例和自写文档。以下例外不因内容所在的文件类型或目录而改变。

## Material outside that grant / 不在此授权范围内的资料

The software license does not grant rights to third-party artwork, audio, branding, trademarks, copied website or client resources, reference snapshots, or quoted game and skill descriptions. This includes third-party text or code embedded in source, JSON content or documentation. Those materials remain subject to their respective rights holders' terms.

软件许可不授予第三方图片、音频、品牌、商标、复制的网页或客户端资源、参考快照及游戏和技能原文的权利。嵌入源码、JSON 内容或文档中的第三方文字及代码也属于此范围，仍适用相应权利人的条款。

| Material / 资料 | Provenance records / 来源记录 |
| --- | --- |
| Desktop assets, including mixed third-party and generated resources / 桌面素材目录（含混合来源资源） | [src/CardGame.Wpf/Assets](src/CardGame.Wpf/Assets) is excluded from the blanket software grant; retain any resource-specific notices. / 此目录不随软件统一授权，保留各资源自己的说明。 |
| Character portraits / 武将图片 | [General artwork catalog](docs/content/general-art-catalog.json) |
| Card images / 卡牌图片 | [Card artwork catalog](docs/content/card-art-catalog.json) and [artwork notes](docs/content/card-artwork.md) |
| Table backgrounds and gallery UI resources / 桌面背景与图鉴界面素材 | [Table catalog](docs/content/table-art-catalog.json) and [gallery UI catalog](docs/content/gallery-ui-assets.json) |
| Game audio and official recordings / 游戏音频与官方录音 | [Audio catalog](docs/content/game-audio-catalog.json), [sync notes](docs/content/game-audio-sync.md), and [local audio notes](src/CardGame.Wpf/Assets/Audio/README.md) |
| Scraped rule references / 抓取规则参考 | [BWIKI reference notes](docs/content/BWIKI_REFERENCE_DATA.md) and [data/reference/bwiki](data/reference/bwiki) |
| External pages, scripts and client/API evidence / 外部页面、脚本及客户端/API 证据 | [docs/content/sources](docs/content/sources), including preserved HTML, JavaScript, text and JSON responses |
| Quoted character, card and skill descriptions / 引用的人物、卡牌与技能文字 | Source records linked from [the content manifest](docs/content/CONTENT_MANIFEST.md), character content, presentation JSON and related documentation |

Source URLs, download dates and checksums document provenance; they do not establish redistribution permission. Preserve existing source records, copyright statements and any specific licenses. The resource directories and catalogs are not an exhaustive rights-clearance record, and this notice does not claim that every resource has been licensed for redistribution.

来源网址、下载日期和校验值用于记录出处，不代表已获得再分发许可。请保留已有来源、版权声明及具体许可。素材目录与清单并非完整的权利审查记录；本说明不声称所有资源都已获准再分发。

For an integration that does not require desktop assets, start with [CardGame.Core](src/CardGame.Core) and the [headless example](samples/CardGame.Headless/README.md). If you package external tools or dependencies, include their own applicable licenses; the project's MIT grant does not replace them.

无需桌面素材的接入可以从 [Core](src/CardGame.Core) 和[无界面示例](samples/CardGame.Headless/README.md)开始。分发外部工具或依赖时，应另行保留其适用许可；项目 MIT 授权不替代这些许可。

Sanguosha.NET is an independent community project and is not affiliated with or endorsed by the Sanguosha rights holders. Names used to identify compatible game rules do not imply ownership of the game, its branding or its assets.

Sanguosha.NET 为社区独立实现，与三国杀权利人无隶属或背书关系。用于说明对应玩法的名称不表示拥有游戏、品牌或素材的权利。
