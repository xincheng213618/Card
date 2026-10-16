# SP曹仁：当前规则与来源边界

人物 ID `ol:sp-cao-ren`，势力 `wei`，基础体力 4。当前人物属性、包与将池绑定见[人物资料](../../../../src/Characters/cao-ren/ol-sp/Content/ol-sp-cao-ren.character.json)。

| 技能 ID | 名称 | 当前规则 |
| --- | --- | --- |
| `ol:weikui` | 伪溃 | 出牌阶段限一次，你可以失去1点体力并观看一名有手牌的其他角色的手牌，若其中没有【闪】，你弃置其中一张牌，否则你视为对其使用一张【杀】，且你本回合计算与其的距离视为1。 |
| `ol:lizhan` | 励战 | 结束阶段，你可以令任意名已受伤角色摸一张牌。 |

## 执行入口

- [ordinary-sp-cao-ren.rules.json](../../../../src/Characters/cao-ren/ol-sp/Skills/ordinary-sp-cao-ren.rules.json)

规则执行保留真实材料、费用和动作/窗口身份；暂停状态属于其拥有帧，原生移牌、回复、伤害、濒死等子链完成后沿类型化返回继续。已发行成本不重复支付，尚未发行的后续按其来源与死亡/终局合同判断。

私有候选通过对应玩家快照投影；玩家快照与提交事件的嵌套集合在准备阶段冻结，帧内集合按对应机制合同管理。人物性别、派生技能版本等未由 API 字段直接提供的资料，按保留来源与明确工程解释区分。

具体解释见[工程默认](ordinary-other-thirty-seventh-sp-cao-ren-ENGINEERING_DEFAULTS.md)。

源码格式、提交与恢复边界见[技能程序运行时](../../skill-composition/RUNTIME.md)，来源规范见[版本核对](../../BWIKI_CONTENT_BACKLOG.md)。
