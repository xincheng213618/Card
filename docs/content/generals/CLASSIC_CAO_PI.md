# 经典曹丕

`classic:cao-pi` 已注册到经典身份当前武将池，内容位于 `src/Characters/cao-pi/classic/`。基础资料为魏、男性、3 体力，技能按行殇、放逐顺序展示；主公体力加成由身份模式统一处理。

## 行殇

`classic:xingshang` 在 `characterDied` 窗口提供可选触发。死亡帧保留此次清理的实体牌 ID，`claimDeathCleanupCards` 取得其中仍在弃牌堆的牌并移入曹丕手牌；已经移走的牌不会重复取得。无可取得牌时不产生空效果选择，曹丕自己的死亡不能作为自己的行殇对象。

行殇复用死亡流程的清理结果与父子续接，死亡奖惩和胜负继续由统一死亡结算负责。实现入口是 `GameEngine.DeathPrograms.ClaimProgramDeathCleanupCards`。

## 放逐

`classic:fangzhu` 在一次伤害的 `afterDamageApplied` 窗口可选发动，按整次伤害询问。效果依次执行：

1. 选择一名其他存活角色。
2. 对该角色执行 `turnOver`，切换正面与背面状态。
3. 令同一角色摸 X 张牌，数量使用 `ownerLostHp` 表达式。

伤害触发中的已损失体力由所属窗口的冻结事实提供，含义是曹丕的体力上限减当前体力，最小为零。效果与子步骤保存在程序帧中，完成后返回原伤害结算；翻面和摸牌均由通用能力执行。

## 配置与来源

- [人物注册](../../../src/Characters/cao-pi/classic/Content/classic-cao-pi.character.json)定义当前池和两项技能。
- [规则程序](../../../src/Characters/cao-pi/classic/Skills/classic-cao-pi.rules.json)定义死亡取牌与先翻面后摸牌的顺序。
- [来源档案](../sources/cao-pi-2026-09-28.json)保存采用的官方文本、版本依据与独立立绘的地址和哈希。

规则、展示与资源分别由人物内容、技能程序和素材目录维护。通用程序说明见 [运行时规范](../skill-composition/RUNTIME.md)。
