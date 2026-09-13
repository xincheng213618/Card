# 2v2 玩家体验 · 2026-09-12

开局可选择青队或赤队。三种对局模式横向排列，扩展技能模式长标题可以换行。牌桌显示双方存活人数、公开队伍和相对玩家的队友／对手标识；选将提示正确区分一名队友与两名对手。

新对局面板的模式选择只影响开局预览，当前牌桌的胜利目标始终来自真实玩家快照。取消切换、退出新手演练、读取存档和再战一局均保留正确队伍。选队通过既有 `GameOptions.HumanTeamId` 交给 Core；读档先完整验证，再恢复真实分配的队伍，没有修改规则版本或存档格式。

战局指南按所查看的模式切换：2v2 介绍公开阵营、队伍胜负、先手首轮少摸一张及没有身份局击杀奖惩；身份局继续提供四种身份的说明。武将势力和合法目标高亮都不作为敌我判断依据。

## 验证

- Release 解决方案构建：0 警告、0 错误。
- Core 119/119；WPF 24/24。
- 新回归通过实际绑定控件选择赤队，覆盖双向取消模式切换、指南控件显隐、教程进入退出、真实文件存档恢复、赤队完整对局终局与再战改选青队。
- 原有五场 UI 对局出牌 77 次、回答 42 次响应，全部完成；新增赤队场景采用结束出牌和响应命令验证流程及结算，不作为玩家策略质量评价。
- 1120×740 离屏检查开局、有存档的再战、扩展模式长标题、牌桌、指南和终局；没有进行原生桌面输入或多 DPI 验收。

构建输出为 `.artifacts/playability-20260912/`。日志：[Core](../artifacts/team-experience-review/core.txt)、[WPF](../artifacts/team-experience-review/wpf.txt)。

截图：[选队](../artifacts/team-experience-review/31-team-setup.png)、[队友与对手](../artifacts/team-experience-review/32-team-table.png)、[阵营指南](../artifacts/team-experience-review/33-team-guide.png)、[真实终局](../artifacts/team-experience-review/34-team-result.png)、[存档与再战](../artifacts/team-experience-review/35-team-rematch.png)、[扩展模式](../artifacts/team-experience-review/36-expanded-setup.png)。
