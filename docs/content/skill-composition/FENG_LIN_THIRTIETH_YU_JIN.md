# 界于禁：当前规则与执行入口

当前人物 ID 为 `boundary:yu-jin`，势力 `wei`，基础体力 4。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/yu-jin/boundary/Content/boundary-yu-jin.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `boundary:zhenjun-current` | 镇军 | 准备阶段，你可以弃置一名角色X张牌（X为其手牌数减体力值且至少为1)，然后选择一项： 1.你弃置与其中非装备牌数等量张牌； 2.结束阶段，其摸与其中非装备牌数等量张牌。 |
| `boundary:yizhong-current` | 毅重 | 锁定技，体力值大于等于你的角色的黑色【杀】对你无效；手牌数小于等于你的角色无法响应你的黑色【杀】。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `boundary:zhenjun-current` | `turnStartBeforeNormalFlow / actual-preparation-discard` | `resolvePrepDiscardOrEnding` |
| `boundary:zhenjun-current` | `turnEnding / paid-prep-ending` | `drawPrepDiscardEnding` |
| `boundary:yizhong-current` | `cardPolicies` | 规则文件中的显式过滤、状态与策略 |

正式规则文件：

- [boundary-yu-jin.rules.json](../../../src/Characters/yu-jin/boundary/Skills/boundary-yu-jin.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](FENG_LIN_THIRTIETH_YU_JIN_DEFAULTS.md)；这些解释与官方原文分别标明。
