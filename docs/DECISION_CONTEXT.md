# 响应上下文 · 2026-09-12

中央响应区持续显示当前效果、来源和受影响角色，使用座位与武将名替换上下文中的 AI 编号；滚动候选时，上方的完整询问仍可见。角色卡增加「效果来源」「受影响」「等待救援」「正在选牌」标记，标记不参与目标选择。

- 杀、南蛮与决斗显示当前询问的来源与响应者。决斗中的对手取自当前询问，不假定为当前回合角色。
- 救援显示濒死角色与体力；该询问的 SourceSeat 实际代表濒死者，界面不会把它误标为攻击者。
- 无懈保留原询问中抵消／恢复效果的说明。无中生有显示其使用者为受益者，群体效果不假定只影响当前响应玩家；询问没有给出的多目标名单不会被猜测补齐。
- 火攻、目标牌和技能窗口保留完整询问说明，选将、普通出牌、弃牌与无询问时清除上下文。

`Presentation/DecisionContext.cs` 只接收玩家快照，不读取命令日志、引擎私有结算字段或其他玩家手牌。不会修改规则、保存格式或当前选择。

## 验证

Release 构建 0 警告、0 错误；Core 120/120、WPF 27/27。新增语义回归覆盖救援来源、无中生有受益者、群体无懈、决斗对手与私有询问隔离；真实响应控件检查绑定，完整对局持续检查上下文和角色标记跟随状态更新、显示不依赖隐藏身份／手牌、读取不改变牌局。五场界面对局出牌 77 次、响应 42 次并全部结束。

1120×740 与 1440×860 使用真实 WPF 控件离屏检查，小窗口保留公开选牌所需的滚动区域；这不等同于原生鼠标、键盘或多 DPI 验收。

构建输出 `.artifacts/decision-context-20260912/`；日志：[Core](../artifacts/decision-context-review/core.txt)、[WPF](../artifacts/decision-context-review/wpf.txt)。

截图：[攻击来源与目标](../artifacts/decision-context-review/40-response-RespondDodge.png)、[濒死救援](../artifacts/decision-context-review/40-response-Jijiu.png)、[群体无懈](../artifacts/decision-context-review/40-response-Nullification.png)。
