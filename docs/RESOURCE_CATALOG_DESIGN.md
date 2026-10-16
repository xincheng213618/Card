# 武将与卡牌资源目录

规则内容与静态素材分别维护：`ContentRegistry` 决定武将、技能、卡牌和模式的规则身份；资源目录保存图片、录音、来源与适用绑定。素材存在不表示对应武将或规则已经实现。

| 关系 | 当前表示 |
| --- | --- |
| 武将与技能 | `ContentGeneralDefinition.Id` → `SkillIds` → `ContentSkillDefinition.Id` |
| 武将与立绘 | `general-art-catalog.json` 按外观键保存默认皮肤和 skins |
| 卡牌与牌面 | `card-art-catalog.json` 关联牌种与本地图片，运行时叠加实体花色点数 |
| 短音效 | `GameSound` 与 `Assets/Audio/manifest.json` 对应本地合成 WAV |
| 音乐与录音 | `game-audio-catalog.json` 的 assets 保存文件，bindings 保存适用关系 |
| 图鉴装饰 | `gallery-ui-assets.json` 记录背景、卡框、徽章与 atlas 帧 |

## 运行关系

```text
内容武将 ID → 技能 ID → 技能定义／规则程序
        ↓ 玩家可见身份
武将外观键 → 本机选中的 skinId
        ↓
已提交技能／阵亡事件 → 音频 bindings → asset → 本地播放器
```

图鉴列出该武将已核实的台词，支持逐句试听。对局自动配音要求武将版本、当前皮肤与技能／阵亡用途匹配；尚未匹配本地皮肤的录音可以列出试听，不凭相似姓名自动套用。

`sourceGeneralIds`、`sourceSkinIds`、`sourceSkillIds` 与 `sourceGeneralClasses` 保留来源系统标识；本地 `generalKey` 和 `skinId` 用于素材关联。普通、界、神、SP、谋等版本独立对应，不能仅凭中文同名合并。

文件来源记录包括原始地址、来源证据、格式、字节数、时长和 SHA-256。OGG 导入为 PCM WAV 时分别记录原始与交付哈希，台词文字和绑定关系独立于文件保存。同一录音可供多个绑定引用，无需重复存储。

## 导入与表现边界

`tools/Import-GameAudio.ps1` 导入音频，立绘与界面素材工具见 [`GENERAL_GALLERY.md`](GENERAL_GALLERY.md)。JSON 目录随程序离线交付，运行时不需要外部资源数据库。新增素材通过目录和绑定表达，不为每个武将增加播放器特判。

`GeneralVoiceProjector` 只消费已提交事件与公开快照，资源配置不触发规则，也不泄漏暗置身份。技能程序和运行模块的关系见 [`CONTENT_MODULE_ARCHITECTURE.md`](CONTENT_MODULE_ARCHITECTURE.md)。

皮肤、音乐、配音、总声音和动画属于 `PlayerPreferences`。后台、静音和关闭会取消播放，回前台恢复当前场景音乐，不追播旧技能，详见 [`BATTLE_AUDIO.md`](BATTLE_AUDIO.md)。
