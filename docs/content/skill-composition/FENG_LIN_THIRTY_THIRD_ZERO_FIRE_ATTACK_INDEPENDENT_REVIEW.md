# 零实体火攻的材料与伤害归属

零实体火攻仍先实际展示、选择并支付同花色实体牌，再创建原生火焰伤害。无实体的火攻来源与真实正数费用分别保存；伤害不把牌 ID 零当作可查实体。完成返回验证原 CardUse、来源、额度、目标与真实成功付款事实，原材料只支付一次。

当前人物、程序绑定和规则来源见[规则合同](FENG_LIN_THIRTY_FIRST_GUO_HUANG_HOU_CONTRACT.md)；资料未明确的复杂口径见[工程默认](FENG_LIN_THIRTY_FIRST_GUO_HUANG_HOU_DEFAULTS.md)。

实现入口：[GameEngine.TieredRoundZeroReturns.cs](../../../src/CardGame.Core/GameEngine.TieredRoundZeroReturns.cs)。

通用字段、提交、隐私及命令恢复边界见[技能程序运行时](RUNTIME.md)。
