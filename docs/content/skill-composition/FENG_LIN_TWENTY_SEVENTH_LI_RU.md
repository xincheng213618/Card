# 界李儒：当前规则与执行入口

当前人物 ID 为 `boundary:li-ru`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/li-ru/boundary/Content/boundary-li-ru.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:mieji-current` | 灭计 | 出牌阶段限一次，你可以将一张锦囊牌置于牌堆顶并令一名有手牌的其他角色选择一项：1.弃置一张锦囊牌；2.依次弃置两张牌。 |
| `boundary:fencheng-current` | 焚城 | 限定技，出牌阶段，你可以选择一名其他角色开始，令所有其他角色依次选择一项：1.弃置任意张牌（须比上家弃置的牌多）；2.受到你造成的2点火焰伤害。 |
| `boundary:juece-current` | 绝策 | 结束阶段，你可以对一名手牌数小于等于你的其他角色造成一点伤害。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:mieji-current` | `active / topdeck-trick` | `captureSelectedCards`、`moveBoundCards`、`chooseCategoryOrSequentialDiscard` |
| `boundary:fencheng-current` | `active / chosen-start-fire-walk` | `escalatingDiscardOrDamageFromSelected` |
| `boundary:juece-current` | `turnEnding / damage-not-more-hand` | `selectTarget`、`damage` |

正式规则文件：

- [boundary-li-ru.rules.json](../../../src/Characters/li-ru/boundary/Skills/boundary-li-ru.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_SEVENTH_LI_RU_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-twenty-seventh-642-source-2026-10-04.json](../sources/fenglin-twenty-seventh-642-source-2026-10-04.json)
