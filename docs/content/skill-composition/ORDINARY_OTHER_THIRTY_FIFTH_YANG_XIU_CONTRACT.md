# 杨修：当前规则与执行入口

当前人物 ID 为 `ol:yang-xiu`，势力 `wei`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/yang-xiu/ol/Content/ol-yang-xiu.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `ol:danlao` | 啖酪 | 当你成为锦囊牌的目标后，若你不是此牌的唯一目标，你可以摸一张牌，然后此牌对你无效。 |
| `ol:jilei` | 鸡肋 | 当你受到伤害后，你可以声明一种牌的类型，伤害来源本回合不能使用、打出或弃置你声明的此类手牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `ol:danlao` | `otherActualUseTargeted / own-multi-trick` | `drawThenNullifyOwnMultiTargetTrick` |
| `ol:jilei` | `afterDamageApplied / declare-source-category / perDamage` | `chooseOption`、`restrictDamageSourceHandCategory` |

正式规则文件：

- [ordinary-yang-xiu.rules.json](../../../src/Characters/yang-xiu/ol/Skills/ordinary-yang-xiu.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](ORDINARY_OTHER_THIRTY_FIFTH_YANG_XIU_ENGINEERING-DEFAULTS.md)；这些解释与官方原文分别标明。
