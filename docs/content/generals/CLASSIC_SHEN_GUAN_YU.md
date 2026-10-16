# 经典神关羽

`classic:shen-guan-yu` 使用正式服 I 版技能定义，已进入经典身份当前武将池。印刷势力为神，男性，基础体力 5，技能为 `classic:wushen` 武神与 `classic:wuhun` 武魂。身份开局通过通用选择流程确定本局有效势力。

## 武神

武神是带锁定标签的持续状态能力：自己的红桃手牌视为普通杀，使用这些杀没有距离限制。牌名修正只作用于手牌区，离开该区后保留实体牌的原始身份；出杀次数、响应、防具和普通伤害仍由当前合法动作及结算链处理。

[武神程序](../../../src/Characters/guan-yu/classic/Skills/classic-shen-guan-yu.rules.json)以 `cardIdentities` 定义红桃手牌身份，并通过绑定该身份的 `slashDistanceLimit` 修正距离。强制身份替代原手牌用途，不是可选转换按钮。

## 武魂

武魂由两组强制触发组成：

- 实际受到伤害后、进入濒死前，按伤害点数给实际伤害来源增加归属于该神关羽的梦魇标记。自伤和没有有效来源的伤害不会向自己或虚构来源加标记。
- 神关羽死亡且牌局尚未确定胜负时，从存活角色中选择其所属梦魇标记的正数最大持有者；并列时由技能拥有者选择，全部为零时没有目标。目标进行一次可改判的普通判定，结果不是桃或桃园结义时直接死亡。

直接死亡复用 `DeathFrame`，不产生伤害、濒死求桃或击杀归因；嵌套死亡完成后返回父结算。相关来源标记随拥有者死亡流程完成清理。公开快照显示标记合计，来源归属由可信运行时保存。

[武魂程序](../../../src/Characters/guan-yu/Skills/nightmare-death-skills-v53.rules.json)使用 `changeAttributedMarker`、`maximumAttributedMarker`、`startJudgment` 和 `causeDeathUnlessBoundCardKind`；运行时实现位于 `GameEngine.DeathPrograms`，界面使用通用选择与判定展示。

## 来源与内容入口

[人物注册](../../../src/Characters/guan-yu/classic/Content/classic-shen-guan-yu.character.json)定义技能顺序与当前池。采用的文本、判定与死亡顺序依据见 [内容来源](../sources/god-guan-yu-a53b2-2026-09-20.json)，立绘来源与哈希见 [素材档案](../sources/god-guan-yu-c21-2026-09-21.json)。BWIKI 的不同版本记录与官方不同产品线文本需要按版本身份分别使用。

通用标记、判定与生命周期能力见 [运行时规范](../skill-composition/RUNTIME.md)，玩家视图与可信材料边界见 [架构说明](../../ARCHITECTURE.md)。
