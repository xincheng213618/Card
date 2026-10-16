# 界小乔：当前规则与执行入口

当前人物 ID 为 `boundary:xiao-qiao`，势力 `wu`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/xiao-qiao/boundary/Content/boundary-xiao-qiao.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:hongyan` | 红颜 | 锁定技，你的黑桃牌和黑桃判定牌视为红桃牌。若你的装备区里有红桃牌，你的手牌上限等于体力上限。 |
| `boundary:tianxiang` | 天香 | 当你受到伤害时，你可以弃置一张红桃牌防止之并选择一项，令一名其他角色：1.受到伤害来源的1点伤害并摸X张牌（X为其已损失体力值且至多为5）；2.失去1点体力并获得你弃置的牌。 |
| `boundary:piaoling` | 飘零 | 结束阶段，你可以判定，若结果为红桃，你将判定牌置于牌堆顶或交给一名角色，若该角色是你，你弃置一张牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:hongyan` | `cardPolicies` | 规则文件中的显式过滤、状态与策略 |
| `boundary:tianxiang` | `beforeDamageApplied / prevent-and-benefit` | `discardSuitPreventDamageAndBenefit` |
| `boundary:piaoling` | `turnEnding / ending-heart-placement` | `startJudgment`、`placeMatchedJudgmentCard` |

正式规则文件：

- [boundary-xiao-qiao.rules.json](../../../src/Characters/xiao-qiao/boundary/Skills/boundary-xiao-qiao.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_EIGHTH_XIAO_QIAO_DEFAULTS.md)；这些解释与官方原文分别标明。
