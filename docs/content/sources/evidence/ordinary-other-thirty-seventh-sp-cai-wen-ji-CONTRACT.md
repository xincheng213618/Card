# SP蔡文姬：当前规则与来源边界

人物 ID `ol:sp-cai-wen-ji`，势力 `wei`，基础体力 3。当前人物属性、包与将池绑定见[人物资料](../../../../src/Characters/cai-wen-ji/ol-sp/Content/ol-sp-cai-wen-ji.character.json)。

| 技能 ID | 名称 | 当前规则 |
| --- | --- | --- |
| `ol:chenqing` | 陈情 | 每轮限一次，当一名角色进入濒死状态时，你可以令另一名其他角色摸四张牌，然后弃置四张牌，若弃置牌包含四种花色，其视为对处于濒死状态的角色使用一张【桃】。 |
| `ol:moshi` | 默识 | 结束阶段，你可以将一张手牌当你出牌阶段内使用过的第一张基本牌或普通锦囊牌使用，然后你可以将一张手牌当你出牌阶段使用过的第二张基本牌或普通锦囊牌使用。 |

## 执行入口

- [ordinary-sp-cai-wen-ji.rules.json](../../../../src/Characters/cai-wen-ji/ol-sp/Skills/ordinary-sp-cai-wen-ji.rules.json)

规则执行保留真实材料、费用和动作/窗口身份；暂停状态属于其拥有帧，原生移牌、回复、伤害、濒死等子链完成后沿类型化返回继续。已发行成本不重复支付，尚未发行的后续按其来源与死亡/终局合同判断。

私有候选通过对应玩家快照投影；玩家快照与提交事件的嵌套集合在准备阶段冻结，帧内集合按对应机制合同管理。人物性别、派生技能版本等未由 API 字段直接提供的资料，按保留来源与明确工程解释区分。

具体解释见[工程默认](ordinary-other-thirty-seventh-sp-cai-wen-ji-ENGINEERING-DEFAULTS.md)。

源码格式、提交与恢复边界见[技能程序运行时](../../skill-composition/RUNTIME.md)，来源规范见[版本核对](../../BWIKI_CONTENT_BACKLOG.md)。
