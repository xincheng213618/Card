# 对局声音与胜负反馈

手牌右侧新增音效开关与音量滑块，Ctrl+M 切换静音。默认开启、音量 40%，与动画开关独立。游戏窗口切到后台立即停声，后台行动不积累播放队列；回到前台只播放之后的新行动。关闭窗口释放音频资源。

## 声音与界面

13 个原创短音效覆盖落牌、打出响应、普通/火焰/雷电伤害、回复、轮到你出牌、新的响应或选牌提示、濒死、阵亡、胜利、战败和平局。AI 的每个回合切换不会反复发出提示音。

“轮到你”来自实际的玩家出牌决策，其他等待来自新的本地 PromptId。即使该步骤没有新增战斗事件，也会检查决策变化；单纯刷新、整理、悬停和选中尚未提交的牌不会发声。

胜负按玩家阵营判断，阵亡的同阵营玩家也能共同获胜。界面显示“胜利 / 败北 / 平局”，下方说明获胜阵营。结果音只播放一次，并替代此前的战斗音；平局不再显示为获胜。载入终局只恢复画面，不播放旧结果。

## 原创素材与实现

`tools/generate_game_audio.py` 使用 Python 标准库合成全部 WAV：确定性噪声、鼓音、泛音拨弦和金属共振，无第三方采样、既有游戏语音或音乐。格式为 44.1 kHz / 16-bit / 单声道 PCM，单个最长 1.35 秒，首尾淡化、峰值约 0.68。素材与参数记录在 `src/CardGame.Wpf/Assets/Audio/`，随构建和发布复制，无需运行时下载或 ffmpeg。

`MainViewModel.Audio` 复用公开战斗提示，并单独识别当前玩家决策和终局；不读取暗手牌或隐藏身份来制造提示。`GameAudioController` 每次提交至多选择两个短音效，优先保留等待玩家与重大结果；相同普通音效间隔至少 120 ms，重大提示至少 350 ms。输出至多保留四个同时播放的声部，取消未加载完的请求后不会在异步打开时又响起来。

播放采用 WPF `MediaPlayer` 的本地文件、单独音量、打开与结束事件；每个声音资源在需要时加载，超过 750 ms 才打开的过期请求被丢弃。使用方式参考微软的 [MediaPlayer API](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.mediaplayer?view=windowsdesktop-8.0) 和 [多媒体概览](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/multimedia-overview)。不修改系统音量。

播放或设备错误会停用当前输出，在音效提示中说明可关闭后重新开启重试。错误不会向规则提交链抛出，也不会改变对局；静音、音量归零、后台、新对局、读取和关闭均清理旧播放请求。

## 存档兼容

当前声音与动画采用独立本机偏好，重启后保留，读档不会覆盖已有设置；仅在没有本机偏好时导入旧档字段。首次开局面板也可调整设置，详见 [`PLAYER_PREFERENCES.md`](PLAYER_PREFERENCES.md)。下述字段继续保留以兼容旧文件。

FormatVersion 1 / Checkpoint SchemaVersion 3 保持不变。可选 `SoundEnabled` 与 `SoundVolume` 在首次迁移时使用；旧文件缺少字段时采用开启和 40%。已开始或已恢复牌局中的设置变化仍会写入兼容字段；首次启动尚未开始游戏时更改设置只保存本机偏好，不覆盖旧存档。

音量字段校验 0–1 的有限数。实际文件测试暴露了原先 `InvalidDataException` 未进入统一读取错误分支的问题，本轮已修复；非法文件设置会显示读取失败并保留当前局面。

## 验证与预览

```powershell
dotnet build .\CardGame.sln -c Release --artifacts-path .artifacts/audio-final
& '.\.artifacts\audio-final\bin\CardGame.Wpf.Tests\release\CardGame.Wpf.Tests.exe' "$PWD\artifacts\audio-review" '--record-motion' '--verify-native-audio'
python .\tools\generate_game_audio.py --preview .\artifacts\audio-review\sound-preview.wav
python .\tools\generate_game_audio.py --timeline .\artifacts\audio-review\audio-preview-events.json --output .\artifacts\audio-review\battle-soundtrack.wav
$audioFrames = (Get-Content -LiteralPath '.\artifacts\audio-review\record-frame-directory.txt' -Raw).Trim()
$audioFrameCount = [int](Get-Content -LiteralPath '.\artifacts\audio-review\record-frame-count.txt' -Raw)
$audioDuration = ($audioFrameCount / 24.0).ToString('0.000000', [Globalization.CultureInfo]::InvariantCulture)
& 'C:\Users\17917\OneDrive\Path\ffmpeg.exe' -y -thread_queue_size 128 -framerate 24 -i (Join-Path $audioFrames 'frame-%04d.png') -i '.\artifacts\audio-review\battle-soundtrack.wav' -t $audioDuration -c:v libx264 -crf 18 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart '.\artifacts\audio-review\battle-audio.mp4'
```

Core 92/92，WPF 17/17，Release 构建零警告、零错误。默认 WPF 检查为 16 项；可选原生验证在本机以零音量逐一播放全部 13 个 WAV，确认每个均打开并到达 `MediaEnded`，最后没有活动声部；另确认加载中取消不会重播，损坏 WAV 通过异步失败回调退出且未开始播放。常规检查不创建原生播放设备。

回归覆盖：真实提交与玩家提示边界、动画关闭时声音仍可独立使用、后台与重新激活不追播、静音和零音量、失败输出与正常引擎的完整状态一致、资源格式与无削波、真实声音控件与 JSON 来回恢复、缺少新增字段、非法音量拒绝、五场完整对局的唯一结果音，以及真实的单回合平局。

预览由实际 WPF 控件离屏帧和该次命令发布的声音事件合成，素材试听另有时间标记。它不是桌面录制，也不是扬声器或耳机实听验收。真实鼠标、键盘、多 DPI 与扬声器听感仍待后续验收。
