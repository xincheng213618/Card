# 张昌蒲单武将批次

当前 OL 张昌蒲（魏势力、3 体力）接入：严教（出牌阶段限一次，你可以选择一名其他角色并亮出牌堆顶的四张牌，令该角色将这些牌分成点数之和相等的两组并将这两组牌分配给你与其，然后将剩余未分组的牌置入弃牌堆。若未分组的牌数大于1，你本回合的手牌上限-1）、省身（当你受到伤害后，你可以摸一张牌（若你的手牌数为全场最少，改为摸两张牌），令下一次发动“严教”亮出的牌数+1（若你的体力值为全场最少，改为+2）且至多+4）。官方立绘 46600 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-zhang-chang-pu-2026-10-06.json)。

| 技能 | 实现口径 |
| --- | --- |
| 严教 | launch：playPhaseStarting 可选触发 → selectTarget otherLiving → 新 op `yanjiaoRevealTopCards`（7165，亮出 4+省身加成张，公开绑定）→ 新 op `yanjiaoSplitRevealedCards`（7166，被选目标两段式分组）→ 三条 moveBoundCards 分配（ownerHand / selectedTargetHand / 弃牌堆，弃牌 awaitMovementTriggers）→ changeParticipantMarker 清空 shenJiao 标记。 |
| 省身 | afterDamageApplied（perDamage）可选触发 → 新 op `shenShenDrawAndArmBonus`（7167）：按全场最少手牌摸 1/2，再按全场最少体力武装 shenJiao 标记 +1/+2（至多 4），严教亮牌时消费、收尾清空。 |

## 共享能力扩展

- 新资源 `PartitionCardSet`：把一个公开亮牌根绑定三路拆分为三组全新原子别名（原原子退役、兄弟绑定收齐三子），使三条移动指令各自恰好消费一次；组合校验器按别名原子追踪。
- 分组交互：第一段只列出存在等和补组的选项（引擎侧子集掩码枚举 + 等和伙伴校验），第二段只列出与第一组等和的补组；无可行分组时自动全落弃牌堆并结算手牌上限惩罚。
- 手牌上限惩罚走既有 `TurnRuleModifier` 管线（HandLimit Add -1，-20..20 已被描述符接受）；`boundCardCountAtLeast` 的组合资源契约不允许挂在 grantTurnRuleModifier 上，因此计数判定内联在 split op 中。
- `PlayerMarkerKind.ShenJiao`（3900，“省身”）参与者标记，沿用 `MutateParticipantMarker` 归因账本。

## 边界口径

- 分组者的两段选择均为私有决策（IsPrivate），选项文本明示每组点数和与剩余牌去向；对手给主公的分组按“牌数最多→点数和更高→选项序”AI 策略选择。
- 未分组的牌数大于 1 才惩罚（恰好 1 张不惩罚）；惩罚只影响本回合弃牌阶段的手牌上限。
- 亮牌期间牌面冻结在处理区（frozen source locations），分组前牌离开处理区立即失败。

## 验证

- 定向 `--filter="Zhang Changpu"`：5/5 通过（定义元数据；可行分组三路分配与弃牌去向；无可行分组全弃+手牌上限惩罚；省身摸 2/武装 +2、严教亮 6 张消费清零；三轮武装到 4 封顶，均含冷恢复重放比对）。
- 例行 `tools/Test-Changed.ps1`（分支，266 通过）：失败集合与 main（a1b2400e）基线逐项 diff 零新增（46=46）；WPF 18/18。
- 本批在独立 worktree（batch/ol-zhang-chang-pu）开发后合并回 main。
