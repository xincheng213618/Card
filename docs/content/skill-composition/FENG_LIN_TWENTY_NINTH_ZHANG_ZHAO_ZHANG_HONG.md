# 界张昭张纮：当前规则与执行入口

当前人物 ID 为 `boundary:zhang-zhao-zhang-hong`，势力 `wu`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/zhang-zhao-zhang-hong/boundary/Content/boundary-zhang-zhao-zhang-hong.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:zhijian` | 直谏 | 出牌阶段，你可以将一张装备牌置于其他角色的装备区（替换原装备），然后摸一张牌。 |
| `boundary:guzheng` | 固政 | 每阶段限一次，当其他角色的至少两张牌因弃置置入弃牌堆后，你可以令其获得其中一张牌，然后你可以获得剩余牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:zhijian` | `active / place-equipment` | `captureSelectedCards`、`placeCapturedEquipmentAndDraw` |
| `boundary:guzheng` | `discardPileReceived / restore-actual-discard-batch` | `restoreActualDiscardBatch` |

正式规则文件：

- [boundary-zhang-zhao-zhang-hong.rules.json](../../../src/Characters/zhang-zhao-zhang-hong/boundary/Skills/boundary-zhang-zhao-zhang-hong.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_NINTH_ZHANG_ZHAO_ZHANG_HONG_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-twenty-ninth-563-source-2026-10-04.json](../sources/fenglin-twenty-ninth-563-source-2026-10-04.json)
