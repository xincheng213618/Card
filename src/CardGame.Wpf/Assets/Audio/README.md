# 原创短音效

这组素材由本项目的 `tools/generate_game_audio.py` 生成，不含第三方采样、已有游戏语音或音乐。合成采用确定性噪声、衰减鼓音、泛音拨弦与金属共振，目标是短促、可辨认的牌桌提示。

音频为 44.1 kHz、16-bit、单声道 PCM WAV，峰值约 0.68，首尾淡入淡出。每个文件不超过 1.35 秒。文件名与 `GameSound` 枚举对应，随 WPF 构建和发布复制到 `Assets/Audio`，无需下载素材。

`manifest.json` 记录持续时间、峰值、RMS 和文件哈希。运行 `python .\tools\generate_game_audio.py` 可重建；`--preview <wav路径>` 生成包含所有音效的试听文件。
