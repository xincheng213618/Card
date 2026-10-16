# 界蔡夫人：当前规则与执行入口

当前人物 ID 为 `boundary:cai-fu-ren`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/cai-fu-ren/boundary/Content/boundary-cai-fu-ren.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:xianzhou-current` | 献州 | 限定技，出牌阶段，你可以将你装备区里的所有牌交给一名其他角色，然后其选择一项：1.令你回复X点体力；2.对其攻击范围内至多X名角色各造成1点伤害。（X为你给出的牌数） |
| `boundary:qieting-current` | 窃听 | 其他角色的回合结束后，若其本回合未对其他角色造成伤害，你可以选择一项；若其本回合未对其他角色使用过牌，你可以选择两项；1.将其装备区里的一张牌置入你的装备区；2.摸一张牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:xianzhou-current` | `active / all-equipment-donation` | `donateAllEquipmentAndOfferRecipientBenefits` |
| `boundary:qieting-current` | `afterTurnEnded / other-actual-turn-equipment-options` | `chooseEquipmentOrDrawAfterOtherActualTurn` |

正式规则文件：

- [boundary-cai-fu-ren.rules.json](../../../src/Characters/cai-fu-ren/boundary/Skills/boundary-cai-fu-ren.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_SEVENTH_CAI_FU_REN_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-twenty-seventh-627-source-2026-10-04.json](../sources/fenglin-twenty-seventh-627-source-2026-10-04.json)
