# 张松与界张松的强识、献图

当前规则分别位于[经典文件](../../src/Characters/zhang-song/classic/Skills/classic-zhang-song.rules.json)与[界版文件](../../src/Characters/zhang-song/boundary/Skills/boundary-zhang-song.rules.json)。版本参考[移动版张松](https://www.sanguosha.cn/pc/hero-detail-179.html)、[OL 界张松](https://www.sanguosha.com/hero/753)及[规则集使用流程](https://gltjk.com/sanguosha/rules/flow/use.html)；第三方规则重印与官方当前技能页分别作为依据。

| 技能 | 当前实现 |
| --- | --- |
| 经典强识 | 出牌阶段开始随机公开其他角色一张手牌，实体留在原手牌；本回合使用同类别牌时可摸一张，按 `cardUseCommitted` 每张牌一次 |
| 界强识 | 私密观看目标手牌并自选公开一张；本出牌阶段使用同类别牌时可摸一张，阶段结束清除类别 |
| 经典献图 | 其他角色出牌阶段开始可摸两张，再从自己的手牌/装备选择两张交给该角色；其本阶段没有击杀时，于该出牌阶段结束失去一点体力 |
| 界献图 | 可选摸一张或两张并选择等量牌赠予；按实际数量设置阈值，该出牌阶段造成伤害不足阈值时失去一点体力 |

普通基本牌、装备、即时及延时锦囊的使用窗口保留各自真实类型；装备实际进入装备区后打开相应提交窗口。重铸、装备主动效果和普通打出不冒充一次用牌。多目标用牌的同一次使用只提供一次强识收益机会。

私密观看只给技能拥有者，公开展示只暴露最终选中的一张。献图可以选择原有牌；选牌草稿不移动实体，确认后使用真实绑定与移动发票，原生子链完成后才继续。濒死桃/酒的父流程等待相应使用和回复窗口结束。

公共使用/打出与窗口边界见[卡牌生命周期合同](../CARD_LIFECYCLE_CONTRACT.md)，字段见[技能运行时](skill-composition/RUNTIME.md)。
