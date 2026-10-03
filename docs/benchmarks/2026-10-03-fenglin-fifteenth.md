# 当前OL界吕布、界袁绍

两名武将、官方原立绘、图鉴、同步映射与来源均已直接写入主区。利驭真实获得按打印类别分支，并保留移动children、受害者选择、真实Duel及外层杀的有限返回；乱击更新实际余集及后续observer，血裔冻结开局群人数并实时查询剩余裔。详见[来源](../content/sources/fenglin-fifteenth-2026-10-03.json)、[共享能力](../content/skill-composition/FENG_LIN_FIFTEENTH_CAPABILITIES.md)和[规则口径](../content/skill-composition/FENG_LIN_FIFTEENTH_RULINGS.md)。

本次删除后的恢复通过原会话补丁重建，31实际源码SHA冻结且审查前后稳定，五共享文件统一合并并保留界邓艾的新过滤。以下均为恢复后Main实际重新运行，未把已删除worker的历史结果当本次验收。

| 实际范围 | 结果 | wrapper耗时 |
| --- | --- | --- |
| Core定向12过滤器union | 35/35 | 27.719秒 |
| Full | Core 552/552；WPF 56/56 | 203.752秒 |
| 无过滤日常范围，含增量构建 | Core 159/159；WPF 17/17 | 59.846秒 |
| 离线立绘 | 182武将；979 PNG | 通过 |

两张750×950原PNG的编译资源SHA与来源相同。此范围不等同于用户运行窗口、设备或帧率验收。正式accepted命令/cold与source/Role/actor/GeneralWeapon生成等HOST审计分别标注。首次定向冷构建仅两个现有GaoDa nullable警告，无新增生产警告；Full/日常增量构建零警告。没有发布、push或版本提升。

本批只使用Main与一个固定verification-cache，未复制整棵工作区或冻结重复DLL。日志在验证期间持有，完成后仅保留结果与输入/日志摘要，清理临时脚本、补丁请求和构建输出。源码输入摘要：`a34259fc56e27f2a33fd323fd1844004e563819daef580348a0ff771d14d1225`。

- focused/core-selected.log: `14efa431be2c3a9da6296bb4ed35c4810357814209536a4aafee91decab11c6b`
- full/core-full.log: `03546d134e544c7bcdbd29275cb3cb284d17603b166724b1b268491c2366d72f`
- full/wpf-full.log: `e407358f7528def3eabf58032e2f2d989c9e482382c80ea6b6e1351e680d0689`
- verification-cache/core-selected.log: `044644ec5df0f4cd28d2e77926da1047c14ddb4c3ef3e7fd6b0735101daff918`
- verification-cache/wpf-selected.log: `e1224be6e3039f145f10ac48c3acf6fe82822d2f2b3aa44bbff4e0d29c6d3761`

清理已核验完成：本批固定缓存与恢复临时目录均不存在，共移除2707个自有文件（逻辑大小2.097 GB），没有保留构建树。只保留源码、官方原图与本验证摘要。[清理记录](2026-10-03-fenglin-fifteenth-cleanup.json)。
