# 界颜良文丑：当前规则与执行入口

当前人物 ID 为 `boundary:yan-liang-wen-chou`，势力 `qun`，基础体力 4。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/yan-liang-wen-chou/boundary/Content/boundary-yan-liang-wen-chou.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:shuangxiong-current` | 双雄 | 摸牌阶段结束时，你可以弃置一张牌，然后你本回合可以将一张与之颜色不同的牌当【决斗】使用。结束阶段，你获得本回合对你造成伤害的牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:shuangxiong-current` | `drawPhaseEnded / completed-draw-paid-color` | `selectOwnedCards`、`discardBoundCardForOppositeTurnDuel` |
| `boundary:shuangxiong-current` | `turnEnding / ending-causal-damage-entities` | `claimActualTurnDamageEntities` |

正式规则文件：

- [boundary-yan-liang-wen-chou.rules.json](../../../src/Characters/yan-liang-wen-chou/boundary/Skills/boundary-yan-liang-wen-chou.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_EIGHTH_YAN_LIANG_WEN_CHOU_DEFAULTS.md)；这些解释与官方原文分别标明。
