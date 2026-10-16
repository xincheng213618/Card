# SP 庞德的原生付款与决斗返回

技能先支付实际实体弃牌并结清移牌/HP/回复子链，再根据原受益者的真实弃牌结果决定后续。零实体决斗保留实际使用者、原目标、父程序和专门返回，不能重付原成本；私有选牌与类别禁弃读取同一合法性策略。

当前人物、程序绑定和规则来源见[规则合同](ORDINARY_OTHER_THIRTY_FIFTH_SP_PANG_DE_CONTRACT.md)；资料未明确的复杂口径见[工程默认](ORDINARY_OTHER_THIRTY_FIFTH_SP_PANG_DE_ENGINEERING_DEFAULTS.md)。

实现入口：[GameEngine.TieredRoundZeroUses.cs](../../../src/CardGame.Core/GameEngine.TieredRoundZeroUses.cs)。

通用字段、提交、隐私及命令恢复边界见[技能程序运行时](RUNTIME.md)。
