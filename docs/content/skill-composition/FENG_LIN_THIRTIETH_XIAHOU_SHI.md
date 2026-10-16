# 界夏侯氏：当前规则与执行入口

当前人物 ID 为 `boundary:xiahou-shi`，势力 `shu`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/xiahou-shi/boundary/Content/boundary-xiahou-shi.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:qiaoshi-current` | 樵拾 | 每个结束阶段，你可以与当前回合角色各摸一张牌。然后若其与你手牌数不相等，此技能本轮失效。 |
| `boundary:yanyu-current` | 燕语 | 出牌阶段，你可以重铸【杀】。出牌阶段结束时，若你本阶段失去过至少两张【杀】，你可以令一名男性角色摸两张牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:qiaoshi-current` | `turnEnding / own-ending-pair` | `drawEndingPairThenBlockRoundIfUnequal` |
| `boundary:qiaoshi-current` | `turnEnding / foreign-ending-pair` | `drawEndingPairThenBlockRoundIfUnequal` |
| `boundary:yanyu-current` | `active / recast-physical-slash` | `recastSelectedPhysicalSlash` |
| `boundary:yanyu-current` | `playEnding / lost-two-slashes-support` | `selectTarget`、`draw` |

正式规则文件：

- [boundary-xiahou-shi.rules.json](../../../src/Characters/xiahou-shi/boundary/Skills/boundary-xiahou-shi.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_THIRTIETH_XIAHOU_SHI_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [fenglin-thirtieth-752-source-2026-10-04.json](../sources/fenglin-thirtieth-752-source-2026-10-04.json)
