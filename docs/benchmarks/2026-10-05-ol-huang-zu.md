# 黄祖单武将批次

当前 OL 黄祖（群势力、4 体力）以官方页当前文本接入：挽弓（若你使用的上一张牌是基本牌，你使用【杀】无距离和次数限制且造成的伤害+1）。官方立绘 47700 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-huang-zu-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 挽弓 | 次数：modifier slashLimit unlimited（condition previousPlayCardIsBasic，咆哮查询口径）；距离：四种花色各一条 ignoreSlashUseDistanceBySuit policy（同条件，既有普通【杀】距离机制）；伤害：cardUseTargetsFinalized（ownerRelation actor）+ addCurrentTargetSlashDamage 1（comparison always 本批新增，烈弓操作面）。 |

## 共享能力扩展

- 新条件 `previousPlayCardIsBasic` 双枚举补齐：SkillProgramTriggerConditionKind（触发事实，沿用 CardActionMatchesPreviousPlayCard 的“排除当前牌取上一张使用牌”口径）+ SkillProgramConditionKind（PlayerSkillContext，供 modifier/policy 无帧评估）。
- `TracksPlayCardHistory` 依赖开关同时认可触发条件与 modifier/policy 条件两种出现方式。
- `ProgramFinalTargetComparison` 新增 `Always`（烈弓的 addCurrentTargetSlashDamage 原仅支持目标手牌/体力比较，挽弓的伤害加成只由触发条件门控）。
- 未改动既有窗口、查询与效果语义；slashLimit unlimited 沿用咆哮、按花色距离 policy 沿用既有普通【杀】距离机制。

## 验证

（合并前填写实测数字）

## 边界说明

- OL 界限突破仅剩 451 界刘禅、749 界沮授未实现（均需新共享机制）。
- 本批在独立 worktree（batch/huang-zu）开发后合并回 main。
