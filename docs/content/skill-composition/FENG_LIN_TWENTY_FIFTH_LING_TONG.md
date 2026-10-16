# 界凌统：当前规则与执行入口

当前人物 ID 为 `boundary:ling-tong`，势力 `wu`，基础体力 4。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/ling-tong/boundary/Content/boundary-ling-tong.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:xuanfeng-current` | 旋风 | 当你失去装备区里的牌后，或一次性失去至少两张牌后，你可以依次弃置至多两名其他角色共计至多两张牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:xuanfeng-current` | `cardsMoved / owner-batch-loss-discard` | `chooseOtherOwnedCardDiscard` |

正式规则文件：

- [boundary-ling-tong.rules.json](../../../src/Characters/ling-tong/boundary/Skills/boundary-ling-tong.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

## 规则来源

- [fenglin-twenty-fifth-588-source-2026-10-04.json](../sources/fenglin-twenty-fifth-588-source-2026-10-04.json)
