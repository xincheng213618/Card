# 界伏皇后：当前规则与执行入口

当前人物 ID 为 `boundary:fu-huang-hou`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/fu-huang-hou/boundary/Content/boundary-fu-huang-hou.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:zhuikong-current` | 惴恐 | 其他角色的回合开始时，若你已受伤，你可以与其拼点：若你赢，其本回合不能对除其以外的角色使用牌；若你没赢，你获得其拼点牌，然后其视为对你使用一张【杀】。 |
| `boundary:qiuyuan-current` | 求援 | 当你成为【杀】或伤害锦囊的目标时，你可以令另一名其他角色选择一项：1.交给你一张与此牌同类型不同牌名的牌；2.成为此牌的额外目标。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:zhuikong-current` | `otherActualTurnStarted / foreign-actual-turn-contest` | `selectTarget`、`startPindian`、`resolveForeignTurnPindian` |
| `boundary:qiuyuan-current` | `cardUseTargetsFinalized / finalized-actual-use-aid` | `offerSameTypeDifferentNameOrExtraTarget` |
| `boundary:qiuyuan-current` | `otherActualUseTargeted / legacy-virtual-target-aid` | `offerSameTypeDifferentNameOrExtraTarget` |

正式规则文件：

- [boundary-fu-huang-hou.rules.json](../../../src/Characters/fu-huang-hou/boundary/Skills/boundary-fu-huang-hou.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_NINTH_FU_HUANG_HOU_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-twenty-ninth-671-damage-trick-classification-2026-10-04.json](../sources/fenglin-twenty-ninth-671-damage-trick-classification-2026-10-04.json)
