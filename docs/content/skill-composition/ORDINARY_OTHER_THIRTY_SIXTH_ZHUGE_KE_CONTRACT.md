# 诸葛恪：当前规则与执行入口

当前人物 ID 为 `ol:zhuge-ke`，势力 `wu`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/zhuge-ke/ol/Content/ol-zhuge-ke.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `ol:aocai` | 傲才 | 当你于回合外需要使用或打出基本牌时，若你有/没有手牌，你可以观看牌堆顶两/四张牌，然后可以使用或打出其中的基本牌。 |
| `ol:duwu` | 黩武 | 出牌阶段，你可以弃置X张牌对你攻击范围内一名其他角色造成1点伤害（X为其体力值）。若其因此进入濒死状态且于濒死结算后存活，你失去1点体力且本回合本技能失效。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `ol:aocai` | `cardPolicies` | 规则文件中的显式过滤、状态与策略 |
| `ol:duwu` | `active / target-hp-cost` | `discardTargetHpCardsAndDamage` |

正式规则文件：

- [ordinary-zhuge-ke.rules.json](../../../src/Characters/zhuge-ke/ol/Skills/ordinary-zhuge-ke.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](ORDINARY_OTHER_THIRTY_SIXTH_ZHUGE_KE_ENGINEERING_DEFAULTS.md)；这些解释与官方原文分别标明。
