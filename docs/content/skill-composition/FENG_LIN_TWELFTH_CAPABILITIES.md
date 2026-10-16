# 界诸葛亮、界孙尚香共享能力

采用的普通 OL 文本、属性与工程默认见[来源档案](../sources/fenglin-twelfth-2026-10-02.json)及[规则口径](FENG_LIN_TWELFTH_RULINGS.md)。人物资料注册 boundary:zhuge-liang 和 boundary:sun-shangxiang；规则分别使用 boundary:guanxing-current、boundary:jieyin-current，并复用 classic:kongcheng、classic:xiaoji。

## 观星

ReorderTopCards 的 PopulationThresholdCount 在每次真正开始观看时按存活人数决定数量：至少四人看至多五张，否则至多三张，短堆使用实际可用数量。此模式与 numberExpression、exactTopCount 互斥。

TopReorder 固定一次 ViewedCardIds 及真实牌面，只给拥有者观看。逐张选择顶/底后，对真实顶片段和完整分区校验并一次重排；显示相同牌面不重新取看。

准备阶段的 AllBottomStateId 完成政策保存指令、实际回合和声明布尔状态。只有非空观看、全部实体实际置底并成功提交，才授予该实例的本回合资格。结束阶段另一个绑定只消费资格，不再次武装自身。新实例不继承；原状态按回合状态仓在下一实际回合开始重置。

排序提交前取消不产生奖励，提交后的真实牌序保留。结束阶段是否发动仍检查存活、有效技能与同一实例。AI 以 PublicControlValue(4) 提供公开控制先验，实际排序使用允许观看的信息，不以摸牌收据代替控制收益。

## 结姻

两个主动绑定共享一个实际出牌阶段额度：弃一张手牌，或把一张本人手牌/装备区的装备置入合法男性的装备区。男性按当前有效性别查询，符合条件的本人可选；原装备同区原地不构成费用。

PlaceSelectedEquipment 消费原始无条件 CaptureSelectedCards 的精确单实体绑定，保存原来源并按真实槽位容量、废除状态和替换规则移动；Transfer/Replacement 意图与外方弃置保护分别处理。装备/体力/移动子窗通过 SelectedCardPayment 返回，费用不重复。

FreezeSelectedHpPair 在所有付款子窗结束后，冻结两人的体力、存活与较大/较小方。先令较大者摸一，再令较小者回复一；相等或同一角色不产生这两项收益。后续摸牌引起体力变化不重算冻结分支。冻结后的尾序列只允许 Draw/Recover，不能追加费用或更换参与者。

程序状态属于原 ProgramSkillFrame，公开与私有选择由 CreateSnapshot(viewerSeat) 过滤冻结。配置见[当前技能配置](RUNTIME.md)，返回见[结算协议](../../RESOLUTION_PROTOCOLS.md)。
