# 界吴国太：当前规则与执行入口

当前人物 ID 为 `boundary:wu-guo-tai`，势力 `wu`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/wu-guo-tai/boundary/Content/boundary-wu-guo-tai.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:ganlu-current` | 甘露 | 出牌阶段限一次，你可以令两名角色交换装备区里的牌。若X大于你已损失的体力值，你须在选择角色时弃置X张牌（X为其装备区里的牌数之差）。 |
| `boundary:buyi-current` | 补益 | 当一名角色进入濒死状态时，你可以选择其一张牌，若此牌不为基本牌，则其弃置此牌，然后回复1点体力。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:ganlu-current` | `active / exchange-equipment` | `selectEquipmentPairAndPayment`、`moveBoundCards`、`awaitBoundCardMovements`、`exchangeSelectedTargetEquipment` |
| `boundary:buyi-current` | `dyingEntering / support-dying-card` | `selectDyingOwnedCard`、`moveBoundCards`、`awaitBoundCardMovements`、`recover` |

正式规则文件：

- [boundary-wu-guo-tai.rules.json](../../../src/Characters/wu-guo-tai/boundary/Skills/boundary-wu-guo-tai.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_TWENTY_SIXTH_WU_GUO_TAI_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-twenty-sixth-640-source-2026-10-04.json](../sources/fenglin-twenty-sixth-640-source-2026-10-04.json)
