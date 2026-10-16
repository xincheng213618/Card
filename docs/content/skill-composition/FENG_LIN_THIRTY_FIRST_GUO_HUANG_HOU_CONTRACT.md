# 界郭皇后：当前规则与执行入口

当前人物 ID 为 `boundary:guo-huang-hou`，势力 `wei`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/guo-huang-hou/boundary/Content/boundary-guo-huang-hou.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:jiaozhao-round-current` | 矫诏 | 出牌阶段限一次，你可将一张牌当本轮未有角色使用过的基本或普通锦囊牌使用。修改一：每轮限一次，你可将一张牌当任意基本牌或普通锦囊牌使用。修改二：每轮限一次，你可视为使用一张基本牌或普通锦囊。 |
| `boundary:danxin-capped-current` | 殚心 | 当你受到伤害后，你可摸X张牌，然后修改“矫诏”（X为你修改“矫诏”的次数）。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:jiaozhao-round-current` | `viewAs` | 规则文件中的显式过滤、状态与策略 |
| `boundary:danxin-capped-current` | `afterDamageApplied / draw-before-modification / perDamage` | `drawBeforeCappedConversionTierUpgrade` |

正式规则文件：

- [boundary-guo-huang-hou.rules.json](../../../src/Characters/guo-huang-hou/boundary/Skills/boundary-guo-huang-hou.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_THIRTY_FIRST_GUO_HUANG_HOU_DEFAULTS.md)；这些解释与官方原文分别标明。
