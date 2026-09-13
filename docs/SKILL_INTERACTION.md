# 主动技能操作收尾 · 2026-09-12

点击技能入口后，选择所需手牌和亮起的目标；主操作按钮变为「发动技能名」，条件满足后才可点击或按 Enter 确认。操作区显示已选数量与所需范围，Esc 或「取消」退出选择，不支付代价。无需选牌、目标的苦肉仍直接发动。

技能选择期间，手牌亮暗只表示能否用于该技能，战局指南显示真实技能规则、所需数量、已选目标和确认方式。打开指南或取消新对局面板保留选择；进入新手演练再返回，也恢复原局的技能牌、目标、手牌顺序及可用状态，修复教程高亮残留。

确认继续提交既有 `UseSkillCommand`，不改变规则版本、内容指纹或存档格式。尚未确认的选择仅在同次运行中的教程往返保留，不加入磁盘存档。

## 验证

- Release 构建：0 警告、0 错误；Core 119/119，WPF 25/25。
- 制衡、仁德、回春使用真实牌局到达技能边界，覆盖空选、缺目标、撤销重选、Esc 取消、指南输入隔离、新局取消和教程恢复；按钮及 Enter 均提交恰好一条包含所选牌和目标的命令，重复确认不再提交。
- 原有五场界面对局全部结算，共出牌 77 次、回答 42 次响应。
- 1120×740 实际 WPF 控件离屏渲染检查，未进行原生桌面输入或多 DPI 验收。

构建输出：`.artifacts/skill-experience-20260912/`。日志：[Core](../artifacts/skill-experience-review/core.txt)、[WPF](../artifacts/skill-experience-review/wpf.txt)。

截图：[技能入口](../artifacts/skill-experience-review/37-制衡-entry.png)、[技能指南](../artifacts/skill-experience-review/38-仁德-guide.png)、[多目标确认](../artifacts/skill-experience-review/39-回春-ready.png)。
