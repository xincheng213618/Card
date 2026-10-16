# 曹洪：当前规则与来源边界

人物 ID `ol:cao-hong`，势力 `wei`，基础体力 4。当前人物属性、包与将池绑定见[人物资料](../../../../src/Characters/cao-hong/ol/Content/ol-cao-hong.character.json)。

| 技能 ID | 名称 | 当前规则 |
| --- | --- | --- |
| `ol:yuanhu` | 援护 | 结束阶段，你可以将一张装备牌置入一名角色的装备区，若为：武器牌，你弃置其距离为1的一名角色区域里的一张牌；防具牌，其摸一张牌；坐骑牌，其回复1点体力。 |

## 执行入口

- [ordinary-cao-hong.rules.json](../../../../src/Characters/cao-hong/ol/Skills/ordinary-cao-hong.rules.json)

规则执行保留真实材料、费用和动作/窗口身份；暂停状态属于其拥有帧，原生移牌、回复、伤害、濒死等子链完成后沿类型化返回继续。已发行成本不重复支付，尚未发行的后续按其来源与死亡/终局合同判断。

私有候选通过对应玩家快照投影；玩家快照与提交事件的嵌套集合在准备阶段冻结，帧内集合按对应机制合同管理。人物性别、派生技能版本等未由 API 字段直接提供的资料，按保留来源与明确工程解释区分。

具体解释见[工程默认](ordinary-other-thirty-sixth-cao-hong-ENGINEERING_DEFAULTS.md)。

源码格式、提交与恢复边界见[技能程序运行时](../../skill-composition/RUNTIME.md)，来源规范见[版本核对](../../BWIKI_CONTENT_BACKLOG.md)。
