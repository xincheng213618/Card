# 零实体火攻的原生伤害边界

当前矫诏的满级零材料火攻具有真实原生 CardUse；材料为空，火攻同花色弃牌成本仍为真实正数实体。普通手牌成本及允许的颜色 HE 成本先支付并排空子窗口，成功付款事实成立后才发出火焰伤害。

伤害保留零材料用牌来源，使用 `card: null` 表达没有来源实体，实际费用实体不冒充火攻。原 CardUse 的来源、绑定、额度、目标、接受与完成记录保持，完成链先结清顺序目标、连环和类型化子返回，再发行一次用牌完成。

相关入口为 [GameEngine.TieredRoundZeroUses.cs](../../../src/CardGame.Core/GameEngine.TieredRoundZeroUses.cs) 与 [GameEngine.TieredRoundZeroReturns.cs](../../../src/CardGame.Core/GameEngine.TieredRoundZeroReturns.cs)。当前配置见[郭皇后规则](FENG_LIN_THIRTY_FIRST_GUO_HUANG_HOU_CONTRACT.md)，解释见[工程默认](FENG_LIN_THIRTY_FIRST_GUO_HUANG_HOU_DEFAULTS.md)。
