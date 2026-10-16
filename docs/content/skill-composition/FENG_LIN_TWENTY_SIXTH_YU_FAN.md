# 界虞翻：当前规则与执行入口

当前人物 ID 为 `boundary:yu-fan`，势力 `wu`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/yu-fan/boundary/Content/boundary-yu-fan.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:zongxuan-current` | 纵玄 | 当你的牌因弃置而置入弃牌堆后，或你上家的牌于每回合首次因弃置而置入弃牌堆后，你可以将其中任意张牌置于牌堆顶。 |
| `boundary:zhiyan-current` | 直言 | 你上家或你的结束阶段，你可以令一名角色摸一张牌并展示之，若为装备牌，其使用之并回复1点体力。若为非装备牌且其体力值不等于你，其失去1点体力。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:zongxuan-current` | `cardsMoved / own-or-previous-first-discard` | `putOwnOrPreviousFirstDiscardOnTop` |
| `boundary:zhiyan-current` | `turnEnding / own-or-previous-ending-reveal` | `selectTarget`、`draw`、`revealBoundCards`、`filterBoundCards`、`useBoundCardByTarget`、`recover`、`loseHpIfRevealedNonEquipmentDiffers` |

正式规则文件：

- [boundary-yu-fan.rules.json](../../../src/Characters/yu-fan/boundary/Skills/boundary-yu-fan.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

## 规则来源

- [fenglin-twenty-sixth-603-source-2026-10-04.json](../sources/fenglin-twenty-sixth-603-source-2026-10-04.json)
- [当前普通 OL 603 官方来源](https://www.sanguosha.com/hero/603)
