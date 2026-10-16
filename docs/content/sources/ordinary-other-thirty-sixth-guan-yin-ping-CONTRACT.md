# 关银屏：当前规则与来源边界

人物 ID `ol:guan-yin-ping`，势力 `shu`，基础体力 3。当前人物属性、包与将池绑定见[人物资料](../../../src/Characters/guan-yin-ping/ol/Content/ol-guan-yin-ping.character.json)。

| 技能 ID | 名称 | 当前规则 |
| --- | --- | --- |
| `ol:xuehen` | 雪恨 | 出牌阶段限一次，你可以弃置一张红色牌并令至多X名角色（X为你已损失体力值且至少为1）进入连环状态，然后对其中一名角色造成1点火焰伤害。 |
| `ol:huxiao` | 虎啸 | 锁定技，当你对一名角色造成火焰伤害后，其摸一张牌，你本回合对其使用牌无次数限制。 |
| `ol:wuji` | 武继 | 觉醒技，结束阶段，若你本回合造成了至少3点伤害，你增加1点体力上限并回复1点体力，失去“虎啸”，然后从场上、牌堆或弃牌堆中获得【青龙偃月刀】。 |

## 执行入口

- [ordinary-guan-yin-ping.rules.json](../../../src/Characters/guan-yin-ping/ol/Skills/ordinary-guan-yin-ping.rules.json)

规则执行保留真实材料、费用和动作/窗口身份；暂停状态属于其拥有帧，原生移牌、回复、伤害、濒死等子链完成后沿类型化返回继续。已发行成本不重复支付，尚未发行的后续按其来源与死亡/终局合同判断。

私有候选通过对应玩家快照投影；玩家快照与提交事件的嵌套集合在准备阶段冻结，帧内集合按对应机制合同管理。人物性别、派生技能版本等未由 API 字段直接提供的资料，按保留来源与明确工程解释区分。

具体解释见[工程默认](ordinary-other-thirty-sixth-guan-yin-ping-ENGINEERING-DEFAULTS.md)。

源码格式、提交与恢复边界见[技能程序运行时](../skill-composition/RUNTIME.md)，来源规范见[版本核对](../BWIKI_CONTENT_BACKLOG.md)。
