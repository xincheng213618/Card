# 徐盛与界徐盛的破军

经典徐盛 `classic:xu-sheng` 与界徐盛 `boundary:xu-sheng` 分别配置破军。规则见[经典文件](../../src/Characters/xu-sheng/classic/Skills/classic-xu-sheng.rules.json)与[界版文件](../../src/Characters/xu-sheng/boundary/Skills/boundary-xu-sheng.rules.json)，采用的官方文本及差别见[来源档案](sources/xu-sheng-2026-09-27.json)。

| 版本 | 当前实现 |
| --- | --- |
| 经典 | 在自己的出牌阶段使用杀，对每个目标的 `slashBeforeResponse` 窗口可扣置其手牌/装备牌，上限由触发时体力决定 |
| 界版 | 扣置不要求自己的出牌阶段；杀造成伤害时，自己的手牌数和装备数分别不小于目标时伤害加一 |

`holdTargetCards` 冻结真实来源、精确实体与私有选择草稿。扣置牌进入 `PojunHold`；当前回合结束返还到原持有者手牌，装备牌也回手牌。持有者死亡时由原生清理移入弃牌堆。

多目标杀逐个处理实际目标，转换或被请求使用的杀保留其原动作、材料和时点资格。扣置及返还通过统一移动和对应子流程，不复制实体，也不从暗手牌推断未公开牌面。
