# 界马岱：当前规则与执行入口

当前人物 ID 为 `boundary:ma-dai`，势力 `shu`，基础体力 4。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/ma-dai/boundary/Content/boundary-ma-dai.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `classic:mashu` | 马术 | 锁定技，你计算与其他角色的距离始终 -1。 |
| `boundary:qianxi` | 潜袭 | 出牌阶段开始时，你可以展示一张牌。若如此做，距离为1的其他角色本回合不能使用或打出与“潜袭”牌颜色相同的手牌，你本回合使用“潜袭”牌造成的伤害+1。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `classic:mashu` | `modifiers` | 规则文件中的显式过滤、状态与策略 |
| `boundary:qianxi` | `playPhaseStarting / shown-entity-turn-policy` | `selectOwnedCards`、`revealOwnedBoundCardAppearance`、`issueShownEntityTurnPolicy` |

正式规则文件：

- [rule-query-skills.rules.json](../../../src/Characters/_shared/Skills/rule-query-skills.rules.json)
- [boundary-ma-dai.rules.json](../../../src/Characters/ma-dai/boundary/Skills/boundary-ma-dai.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_THIRTIETH_MA_DAI_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-thirtieth-696-source-2026-10-04.json](../sources/fenglin-thirtieth-696-source-2026-10-04.json)
