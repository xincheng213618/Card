# 陈琳：当前规则与来源边界

人物 ID `ol:chen-lin`，势力 `wei`，基础体力 3。当前人物属性、包与将池绑定见[人物资料](../../../../src/Characters/chen-lin/ol/Content/ol-chen-lin.character.json)。

| 技能 ID | 名称 | 当前规则 |
| --- | --- | --- |
| `ol:bifa` | 笔伐 | 结束阶段，你可以将一张手牌暗置于一名其他角色的武将牌上，其下回合开始时观看之并选择一项：1.交给你一张类型相同的手牌并获得此牌；2.移去此牌并失去1点体力。 |
| `ol:songci` | 颂词 | 每名角色限一次，出牌阶段，你可以选择一项：1.令一名手牌数小于体力值的角色摸两张牌；2.令一名手牌数大于体力值的角色弃置两张牌。 |

## 执行入口

- [ordinary-chen-lin.rules.json](../../../../src/Characters/chen-lin/ol/Skills/ordinary-chen-lin.rules.json)

规则执行保留真实材料、费用和动作/窗口身份；暂停状态属于其拥有帧，原生移牌、回复、伤害、濒死等子链完成后沿类型化返回继续。已发行成本不重复支付，尚未发行的后续按其来源与死亡/终局合同判断。

私有候选通过对应玩家快照投影；玩家快照与提交事件的嵌套集合在准备阶段冻结，帧内集合按对应机制合同管理。人物性别、派生技能版本等未由 API 字段直接提供的资料，按保留来源与明确工程解释区分。

具体解释见[工程默认](ordinary-other-thirty-seventh-chen-lin-ENGINEERING-DEFAULTS.md)。

源码格式、提交与恢复边界见[技能程序运行时](../../skill-composition/RUNTIME.md)，来源规范见[版本核对](../../BWIKI_CONTENT_BACKLOG.md)。
