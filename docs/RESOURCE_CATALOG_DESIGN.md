# 武将资源结构对照

2026-09-26，基于当前 Card 源码与下载客户端公开资源表的检查。

| 关系 | 当前 Card | 已解析的外部资源 | 本次接入 |
| --- | --- | --- | --- |
| 武将与技能 | `ContentGeneralDefinition.Id` → `SkillIds` → `ContentSkillDefinition.Id`，由 `ContentRegistry` 注册 | `character`、`heromusic` 中的 CardID、skillID | 保持规则身份，导入器显式对应武将版本；外部数字 ID 单独记录 |
| 武将与立绘 | `general-art-catalog.json` 按 key 保存默认皮肤和 skins | `skininfo` 的 skinID、皮肤名，以及公开立绘目录 | 沿用本地皮肤选择，来源不同的 ID 不直接混用 |
| 音频文件与用途 | 原有 13 个短音效通过 `GameSound` 路由 | `sys_h5_music` 的 heromusic、cardmusic、背景音乐资源路径 | 新增 `game-audio-catalog.json`，每份文件保存哈希、路径、来源和用途 |
| 配音与武将、皮肤、技能 | 原先没有台词关联 | 一条配置可有多份录音，一份录音也可被多个皮肤引用 | `assets` 保存文件，`bindings` 保存适用关系；同录音无需重复存储 |
| 触发与播放 | Core 发布已提交事件，WPF 负责表现 | 配置表只说明资源关联，不能证明游戏全部逻辑实现 | `GeneralVoiceProjector` 从已完成技能事件和公开快照选配音，不让素材驱动规则 |

## 运行关系

```text
ContentRegistry.GeneralId → SkillIds → 技能定义/规则程序
             ↓ 公开快照
       GeneralArt key → 本机选中的 skinId
             ↓
已提交技能/阵亡事件 → GameAudioCatalog bindings → asset → 本地播放器
```

图鉴显示所属武将的已核实台词，允许逐句试听；对局播放要求武将版本、当前本地皮肤、技能或阵亡用途匹配。外部表已确认归属但尚未对应本地皮肤的录音可以列出试听，不会仅因名字相似自动套用。外部未实现武将的素材保留在目录和导入报告中。

`sourceGeneralIds`、`sourceSkinIds`、`sourceSkillIds`、`sourceGeneralClasses` 记录来源系统的标识；本地 `generalKey`、`skinId` 用于素材关联。普通、界、神、SP、谋属于不同武将版本。即使外部 `character` 表显示同名，也应依据版本类和已验证来源建立对应，不能只按中文姓名合并。

文件级证据包括 sourceUrl、sourceEvidence、原始/交付 SHA-256、格式、字节数和时长。OGG 转为 PCM WAV 时同时记录原始和交付哈希。台词文本与使用关系属于独立数据，可以在后续导入中补齐。

## 与规则架构的边界

当前规则仍处于 JSON SkillProgram 与已有模块共存的阶段，参考 `CONTENT_MODULE_ARCHITECTURE.md`。解析音频/武将/皮肤资源配置不等于取得全部服务端规则、AI、结算和联机代码。本次没有替换 Core 的稳定内容 ID、版本哈希、存档或回放格式。

无需为了静态素材再引入 SQL 数据库。现有 JSON 目录已经能表达这些关系，并可随包离线交付。后续素材增量通过 `tools/Import-GameAudio.ps1` 导入；需要更细的关联时先扩充目录和对应规则，无需为每名武将新增播放器分支。

新皮肤与配音是表现偏好，保存在 `PlayerPreferences`。音乐、技能配音和原有短音效共用总音量，音乐与配音另有独立开关；后台、静音和关闭取消播放，回到前台恢复当前场景音乐，不追播旧技能。
