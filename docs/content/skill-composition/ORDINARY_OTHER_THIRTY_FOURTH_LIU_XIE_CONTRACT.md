# 刘协：当前规则与执行入口

当前人物 ID 为 `ol:liu-xie`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/liu-xie/ol/Content/ol-liu-xie.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `ol:tianming` | 天命 | 当你成为【杀】的目标后，你可以：弃置两张牌（不足则全弃），摸两张牌。若全场体力值唯一最大的角色不为你，其可以如此做。 |
| `ol:mizhao` | 密诏 | 出牌阶段限一次，你可以交给一名其他角色所有手牌，然后令其与另一名其他角色拼点：拼点赢的角色视为对没赢的角色使用一张【杀】。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `ol:tianming` | `otherActualUseTargeted / actual-slash-target` | `discardDrawAndOfferUniqueHpPeer` |
| `ol:mizhao` | `active / recipient-contest` | `giveAllHandAndStartRecipientPindian`、`usePindianWinnerSlash` |

正式规则文件：

- [ordinary-liu-xie.rules.json](../../../src/Characters/liu-xie/ol/Skills/ordinary-liu-xie.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](ORDINARY_OTHER_THIRTY_FOURTH_LIU_XIE_ENGINEERING_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [ordinary-other-thirty-third-73-source-2026-10-04.json](../sources/ordinary-other-thirty-third-73-source-2026-10-04.json)
