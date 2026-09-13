# 战斗反馈与手牌延续

本轮给公开行动加入短暂反馈：飞牌落桌、目标连线、伤害与回复数字、打出响应牌、回合切换、濒死与阵亡提示。玩家仍通过原有命令操作；动画没有等待规则，也不会自动确认响应。

## 表现约定

- 飞牌持续约 0.9 秒；回合提示约 0.95 秒；响应、伤害与回复约 1.15 秒；濒死和阵亡约 1.5 秒。
- 连线连接武将边缘，并避开底部操作栏；至多两个目标时显示连线，群体牌只描边对应目标。
- 飞牌终点取实际公开牌控件的坐标，跟随窗口、五/八人布局和最近牌列表变化。
- 中央有选择或公开牌时，牌名提示移到来源武将处，不在选择区飞牌或弹出回合提示。
- 小窗口出现选择内容时，中央优先保留完整牌面所需高度，并临时压缩上排武将；选择结束恢复原牌桌。内容较多时仍可滚动。
- “动画效果”首次使用遵循系统动画偏好，玩家设置随存档保留。关闭立即清空效果，重新开启不播放历史。

## 数据与生命周期

`MainViewModel.SubmitCommand` 返回后，`CaptureBattleFeedback` 才读取新增事件。这保证显示的是已提交动作，命令记录也已经完成。`BattleCueProjector` 明确列举允许的公开事件，不投影摸牌、暗手牌移动或私有选将和技能候选；响应按实体牌 ID 在一次提交内去重，输出不包含该 ID。

公开牌名使用 `CardUseDeclaredEvent.CardKind` 的有效类型。因此红牌经武圣当作杀、或正常使用桃，都同时显示在动画与最近三次出牌中；不再解析日志中的书名号。恢复时重建静态出牌列表并重置事件游标，避免重放旧动作。

`BattleFeedbackLayer` 不接收命中和焦点，只在加载且有活动效果时使用渲染计时器。单次提交最多投影最近 12 条提示，显示层最多保留 24 个活动效果，视图模型最多保留 32 条短期记录；到期、清空、关闭动画或卸载都会停止计时。重新加载只监听新加入的提示。

手牌按实体 ID 更新：仍在手中的 `CardViewModel` 和 WPF 内容容器被保留，只更新可用性与选中状态；整理顺序在刷新和弃牌后延续，新摸到的牌追加。替换整局引擎时清空手牌，避免新牌堆的相同 ID 错用旧对象。

## 可复现预览

```powershell
dotnet build .\CardGame.sln -c Release --artifacts-path .artifacts/feedback-final
& '.\.artifacts\feedback-final\bin\CardGame.Wpf.Tests\release\CardGame.Wpf.Tests.exe' "$PWD\artifacts\feedback-review" '--record-motion'
$frameDirectory = Get-Content -LiteralPath '.\artifacts\feedback-review\record-frame-directory.txt' -Raw
$frameCount = [int](Get-Content -LiteralPath '.\artifacts\feedback-review\record-frame-count.txt' -Raw)
& 'C:\Users\17917\OneDrive\Path\ffmpeg.exe' -y -framerate 24 -i (Join-Path $frameDirectory 'frame-%04d.png') -frames:v $frameCount -c:v libx264 -crf 18 -pix_fmt yuv420p -movflags +faststart -an '.\artifacts\feedback-review\battle-feedback.mp4'
```

预览由真实 WPF 窗口内容离屏渲染；使用固定种子的实际出牌、受伤、回复和回合事件，通过可控表现时钟取得 128 帧。各片段渲染期间比较完整规则状态和命令数量，确保没有因绘制而推进。它是效果片段，不是桌面操作录像，也不是自动推进速度的实测；真实鼠标、键盘、多 DPI 验收仍待后续完成。

`FeedbackChecks` 另覆盖私有事件过滤、响应去重、转化牌显示、手牌顺序和控件延续、动画开关保存、恢复游标、可见响应控件命中几何、停用与卸载时计时器清理。实际 JSON 存取检查同时比较动画偏好与静态最近出牌。
