# 界关兴张苞单武将批次

当前普通 OL 界关兴张苞（蜀势力、4 体力、双人将、史诗）以官方页当前文本接入：父魂（两张牌当【杀】使用或打出；转化【杀】的目标只能用同色手牌响应；出牌阶段用【杀】造成伤害后本回合获得武圣、咆哮）。官方立绘 69000 已离线入库并登记图鉴/映射/同步脚本。来源见[来源档案](../content/sources/boundary-guan-xing-zhang-bao-2026-10-04.json)。

| 技能 | 实现口径 |
| --- | --- |
| 父魂 | 转化体与激活体逐字复用已验收的 classic:fuhun 程序形状（inputCount 2、forPlay+forResponse、useSelectedCardsAs），仅换 boundary id。界升级点一：新增引擎卡牌策略 `convertedSlashSameColorResponseOnly`（枚举 786），当攻击的用牌来源是该技能实例时在人类与 AI 两处闪避选择构建点按转化实体牌颜色过滤【闪】（全红要求红闪、全黑要求黑闪、红黑混合不施加单色限制）。界升级点二：afterDamageApplied（subject damageSource、杀族 damageCardKinds、turnOwnerScope own）触发 grantTurnSkills 授予 classic:wusheng 与 classic:paoxiao，回合结束随 turn: 前缀授予清理。 |

## 共享能力扩展

- `SkillProgramCardPolicyKind` 新增 `ConvertedSlashSameColorResponseOnly = 786`：按来源技能实例绑定的目标响应颜色限制；解析走既有杀族 cardKinds 门控，无数值/花色附加字段。
- 闪避选择的两处构建点（人类窗口与 AI 决策）统一按策略颜色过滤；`ProgramDodgeResponseChoices` 的技能转化响应不在本批过滤范围（来源档案 notes 已记录）。

## 验证

- 定向 4/4：定义与元数据、转化杀响应颜色自适应断言（同色闪可答/异色闪被拒并致伤+授予两技能）、自然杀不受限且被任意色闪回答、回合限技能随回合结束清离；关键步骤间冷恢复一致性。
- 无过滤日常范围：Core 249/249、WPF 18/18，wrapper 实测 102.8 秒（含增量构建）。
- Full 全量：Core 678/678、WPF 57/57，wrapper 实测 301.0 秒（基底 17d36b26）。

## 边界说明

- 官方文案"使用【杀】造成伤害"未限定转化【杀】，授予触发不按 sourceSkillId 过滤。
- 本批开发期间 main（c0cf5dfe/cb0418e5）存在并行会话的半截快照提交，本批附带两处最小构建修复：GameEngine.PrepDiscardReceipts.cs 多余右括号、CardMovementPrograms.cs 一处未全限定 JsonIgnore 特性；缺失的引擎枚举成员由持有会话补提交，本批不代写。
- 界郭皇后（726）由并行批次认领；界于吉（449）质疑交互流暂无人认领；本批在独立 worktree（batch/guan-xing-zhang-bao）开发后合并回 main。
