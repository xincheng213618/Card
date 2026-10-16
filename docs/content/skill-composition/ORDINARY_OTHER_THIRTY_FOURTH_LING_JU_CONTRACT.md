# 灵雎：当前规则与执行入口

当前人物 ID 为 `ol:ling-ju`，势力 `qun`，基础体力 3。人物、技能绑定和将池注册取[人物资料](../../../src/Characters/ling-ju/ol/Content/ol-ling-ju.character.json)；主公加成和模式资格由实际模式处理。

## 技能定义

| 技能 ID | 名称 | 当前说明 |
| --- | --- | --- |
| `ol:jieyuan` | 竭缘 | 当你对其他角色造成伤害时，若其体力值不小于你，你可以弃置一张黑色手牌令此伤害+1；当其他角色对你造成伤害时，若其体力值不小于你，你可以弃置一张红色手牌令此伤害-1。 |
| `ol:fenxin` | 焚心 | 锁定技，若场上有已阵亡的：忠臣，“竭缘”减少伤害无体力值限制；反贼，“竭缘”增加伤害无体力值限制；内奸，“竭缘”弃置牌无颜色限制且可以弃置装备区里的牌。 |

## 程序与原生流程

| 技能 | 绑定入口 | 当前效果操作 |
| --- | --- | --- |
| `ol:jieyuan` | `beforeDamageApplied / increase-other-damage` | `discardOwnedCardToAdjustCurrentDamage` |
| `ol:jieyuan` | `beforeDamageApplied / reduce-other-source-damage` | `discardOwnedCardToAdjustCurrentDamage` |
| `ol:fenxin` | 共享规则/运行时资格 | 由技能定义与注册能力提供 |

正式规则文件：

- [ordinary-ling-ju.rules.json](../../../src/Characters/ling-ju/ol/Skills/ordinary-ling-ju.rules.json)

原生用牌、材料支付、伤害/回复/濒死、牌区移动及其子窗口在所属帧中保存真实来源和收据，完成后沿类型化父返回继续。动作接受前复验技能、材料和目标；已支付的子树不重新付款，尚未发行的效果按当前资格处理。

玩家和 AI 只消费其有权获得的快照与选择；私有手牌和拥有牌区不进入无权观察者的提示。玩家快照与提交事件的嵌套集合在投影/提交准备时冻结，帧内集合按对应机制合同管理。具体字段与允许组合见[运行时](RUNTIME.md)。

复杂时序与资料未裁定的口径见[工程默认](ORDINARY_OTHER_THIRTY_FOURTH_LING_JU_ENGINEERING_DEFAULTS.md)；这些解释与官方原文分别标明。

## 规则来源

- [ordinary-other-thirty-third-74-source-2026-10-04.json](../sources/ordinary-other-thirty-third-74-source-2026-10-04.json)
