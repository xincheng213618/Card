# 游戏音频导入与目录映射

在仓库根目录使用 PowerShell 运行：

```powershell
pwsh -File .\tools\Import-GameAudio.ps1
```

脚本扫描指定 Chrome `Cache_Data` 目录中的公开游戏音频地址，限定为 `web.sanguosha.com/220/h5_2/res/runtime/pc/voice/` 下的 MP3、OGG 和 WAV；不关闭浏览器。地址按移除查询参数后的路径去重，已收录素材跳过下载，相同 SHA-256 的新地址记为来源别名。

## 输入与映射

默认读取 Chrome Default 缓存，其他 Profile 可以显式指定：

```powershell
pwsh -File .\tools\Import-GameAudio.ps1 -CacheDir 'C:\Users\17917\AppData\Local\Google\Chrome\User Data\Profile 2\Cache\Cache_Data'
pwsh -File .\tools\Import-GameAudio.ps1 -RefreshMapping
pwsh -File .\tools\Import-GameAudio.ps1 -RetryFailures
```

`-RefreshMapping` 重新取得并解析公开客户端配置；`-RetryFailures` 重新尝试已记录失败的地址。无新地址且映射已存在时快速退出，保留现有目录。当前增量入口要求仓库中已有 `game-audio-catalog.json`。

`import_game_audio.py` 从公开客户端解码逻辑取得资源配置，按完整资源路径关联 `sys_h5_music`、`character` 和皮肤信息；解码结构变化时报错。外部数字 ID 与版本类独立保存，本地绑定使用明确的 `generalKey`、`skinId` 和技能标识，不按同名武将或文件名猜测。

公开配置用于确认音频来源、用途和台词，不能代替本项目的技能规则。尚无本地武将或皮肤绑定的资源可以保留来源资料，但不参与自动配音。

## 文件与恢复

- [game-audio-catalog.json](game-audio-catalog.json) 保存资源路径、原始与交付哈希、格式、来源及 bindings。
- [game-audio-index.csv](game-audio-index.csv) 是从目录派生的 UTF-8 BOM 表，按本地皮肤绑定展开，也列出未绑定素材。
- 音频交付到 `src/CardGame.Wpf/Assets/Audio/Official/`，MP3 保留原格式，OGG 转为 PCM WAV。
- 扫描状态、下载和失败清单属于工具缓存，运行报告用于检查本次新增、别名和失败数，不属于游戏资源或规则依据。

导入前验证公开配置，下载后检查 SHA-256、ffprobe 格式与完整解码。失败不登记空白音轨；已存在目标必须与来源哈希一致，冲突会报错。中断后重新运行可接纳已验证且相同的文件，目录 JSON 与派生 CSV 使用临时文件替换。

依赖 Python、`requests`、`pycryptodome` 以及 PATH 中的 ffmpeg／ffprobe。Python 依赖可用 `python -m pip install requests pycryptodome` 安装，直接入口为 `python -B .\tools\import_game_audio.py --sync-cache`。本地导入回归入口为 `python -B .\tools\tests\test_game_audio_import.py`。

导入修改源码资源和目录，构建时目录嵌入程序集、音频文件复制到输出。需要分发包时按明确的打包任务执行 [本地打包流程](../LOCAL_PLAY.md)。播放绑定、隐私与本机设置见 [资源结构](../RESOURCE_CATALOG_DESIGN.md) 和 [声音说明](../BATTLE_AUDIO.md)。
