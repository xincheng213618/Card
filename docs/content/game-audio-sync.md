# 游戏音频增量导入

在 Windows PowerShell 中，试听网页游戏后从仓库根目录运行：

```powershell
& .\tools\Import-GameAudio.ps1
```

脚本只读扫描当前 Chrome Default 的 `Cache\Cache_Data`，仅保留 `web.sanguosha.com/220/h5_2/res/runtime/pc/voice/` 中的公开 MP3/OGG/WAV 地址；不关闭浏览器，也不读取或导出账号、Cookie、令牌和其他网站数据。地址按去除 query 后的路径去重；已有目录项不会再次下载。新文件直接从公开 CDN 获取，校验 SHA-256、ffprobe 格式和 ffmpeg 完整解码，再复制到 `src/CardGame.Wpf/Assets/Audio/Official/OL/`。相同 SHA-256 的新 URL 记录为现有素材的来源别名，不产生无索引副本。OGG 转成 PCM WAV；MP3 保留原件。下载和解码失败不会写入目录或留下空白音轨。

若 Chrome 使用其他 Profile，可指定其缓存目录：

```powershell
& .\tools\Import-GameAudio.ps1 -CacheDir 'C:\Users\17917\AppData\Local\Google\Chrome\User Data\Profile 2\Cache\Cache_Data'
```

配置映射来自公开客户端 [`sgsGame_a.sgs`](https://web.sanguosha.com/220/h5_2/sgsGame_a.sgs) 的 `pR.WorkParse` / `aCt.InitOfb` 解码流程和公开 [`Config.sgs`](https://web.sanguosha.com/220/h5_2/res/config/Config.sgs) 中的 `sys_h5_music.sgs`、`character.sgs`、`cha_gs_dbs_fs_skininfo.sgs`。脚本从客户端代码提取当前 AES-CFB 密钥与 IV，解压资源并按 **完整资源路径** 匹配音频，不凭文件名猜技能或台词。客户端解码形式改变时会报错，不会猜测。需要主动复查更新后的公开配置时运行：

```powershell
& .\tools\Import-GameAudio.ps1 -RefreshMapping
```

`docs/content/game-audio-catalog.json` 的 `assets` 数组保存本地路径、原件与交付件 SHA-256、来源 URL、外部配置归属和可选 `bindings`。`generalKey`、`skinId`、`skillId` 是本地播放查询字段；外部 OL ID 另存于 `sourceGeneralIds`、`sourceSkinIds`、`sourceSkillIds`，绝不直接拿外部数字匹配本地皮肤。一个音频适用于多个已核实的本地皮肤时，`bindings` 给出附加映射；只有 `assignmentStatus=verified` 且本地武将键、皮肤 ID 与技能名匹配时才会自动播放。未实现于本地的外部武将仍保留来源名字、版本类、技能或阵亡类别和台词，但没有臆造的本地键。公开配置只用于确认音频来源和文案，**不是完整游戏规则源码**。

便于直接查看归属的 [game-audio-index.csv](game-audio-index.csv) 由脚本从目录派生，使用 UTF-8 BOM，可在 Excel 中打开。每个本地皮肤绑定单独列一行；无本地绑定的音频也列出其 OL 来源 ID、版本类、技能或阵亡和台词。增量没有变化时不重写 CSV 或 JSON。

扫描状态、缓存 URL 观察记录、下载失败清单及最后一次运行报告位于 `.artifacts/portrait-refresh/audio-sync/`；该目录是可重建的研究缓存，不是游戏发布资源。重复运行若没有新可下载地址，会快速退出：当前第二次无变化运行约 1.3 秒，输出 `0 new downloadable game audio URLs`。先前失败的 URL 保留失败记录，避免每次无变化运行重复请求；需要重新尝试时运行：

```powershell
& .\tools\Import-GameAudio.ps1 -RetryFailures
```

脚本先验证公开配置可获取和解码，再导入新资源。若在文件写入后、目录落盘前中断，下次运行会按来源 SHA-256 核对并接纳已经写好的相同文件；已有文件若与本次来源不符会报错，不会悄悄覆盖。目录 JSON 与派生 CSV 使用临时文件原子替换。

依赖：Python 3.10+、`requests`、`pycryptodome`，以及可从 `PATH` 调用的 `ffmpeg` / `ffprobe`。示例安装 Python 依赖：`python -m pip install requests pycryptodome`。如需直接运行 Python，使用 `python .\tools\import_game_audio.py --sync-cache`。首次已验证的 206 份素材可只靠仓库中的目录和音频文件保留；隐藏研究缓存缺失时脚本会重建扫描/配置证据。

导入新增素材或更新归属后，运行 `& .\tools\Build-GamePackage.ps1` 更新本地游戏包，再从根目录 `Play.cmd` 打开。音频目录在构建时嵌入程序，新增文件随发布复制；单独运行导入脚本更新的是源码资源目录。

交给 Sol 的后续任务可以直接写：“我又试听了一批，请运行音频增量导入，检查新增归属和失败报告，有新增或归属变化就验证并更新本地游戏包。”无变化时无需重新构建。

导入器的本地回归入口为 `python .\tools\tests\test_game_audio_import.py`。它在 `.artifacts/audio-import-tests` 中使用隔离目录和已有音频验证重复 URL 去重、无变化不下载/改目录、既有目标冲突保留文件并记失败；不会向外部网站发送测试请求。

截至 2026-09-26，目录有 206 份不同 SHA-256 的音频。OL 缓存中的 `voice/spell` 共 181 份，其中 167 份在公开 `heromusic` 中按路径匹配、13 份在 `cardmusic` 中匹配、1 份 `card/300.1` 未找到配置条目；另外此前单独采集的刘备 OL 样本也匹配 `heromusic`，所以脚本的全目录映射报告是 168 / 13 / 1。`game-audio-index.csv` 因多皮肤绑定展开为 208 行。这些数字是音频来源归属，不代表项目实现了 168 个武将技能；缺少本地武将或皮肤对应的条目仍不能自动播放。

对同一路径被多个 OL CardID 复用的录音，脚本仅依据公开 `character.sgs` 中**精确版本 Class** 映射可确认的本地键；例如 `JiaXu`、`SunCe`、`DaQiao` 分别指向 `jia-xu`、`sun-ce`、`da-qiao`，`JiaXuPo`、`SunCePo`、`DaQiaoPo` 不会并入普通版。其本地皮肤 ID 保持空值，只供图鉴按人物试听。当前目录记录了 9 个不同本地键，其中 6 个存在于本轮立绘目录；`jia-xu`、`sun-ce`、`cao-ang` 尚无该目录项，因此不会假装其本地皮肤已对应。
