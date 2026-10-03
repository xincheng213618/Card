# 界孟获单武将批次

当前 OL 界孟获（蜀势力、4 体力、男性、史诗，gid 492）以官方页当前文本接入：祸首（锁定技，【南蛮入侵】对你无效；当其他角色使用【南蛮入侵】指定目标后，你代替其成为此牌的伤害来源）、再起（结束阶段，你可以令至多X名角色各选择一项（X为本回合置入弃牌堆的红色牌数量）：1.摸一张牌；2.令你回复1点体力）。官方立绘 49200 已离线入库并登记图鉴/映射/同步脚本。来源见[来源档案](../content/sources/boundary-meng-huo-2026-10-04.json)。

| 技能 | 实现口径 |
| --- | --- |
| 祸首 | 逐字复用已验收的 classic:huoshou 程序形状：cardPolicies `excludeGlobalTarget`（南蛮入侵排除拥有者）+ `attributeGlobalDamage`（伤害来源改为拥有者），均仅作用于 `barbarianAssault`。官方界版完整句式与 classic 语义一致，无新引擎能力。 |
| 再起 | 结束阶段强制触发（optional=false、usageScope turn、usageLimit 1），效果为新引擎操作 `offerRedDiscardRecoveryChoice`：X=本回合置入弃牌堆的红色牌数量，X=0 静默跳过。拥有者逐个点选至多 X 名其他角色（可“不发动”，选择去重），确认后经 `ProgramRedDiscardRecoveryCommittedEvent` 公开承诺，再逐个询问每名被选角色摸一张牌或令界孟获回复1点体力（拥有者满体力只提供摸牌分支），每名角色的选择记录为 `ProgramRedDiscardRecoveryChosenEvent`。 |

## 共享能力扩展

- 新增引擎操作 `offerRedDiscardRecoveryChoice`（枚举 2340）：照 classic:kuizhu 弃牌预算（`resolveDiscardBudgetParticipants`）的参与者迭代先例实现——typed 草稿 `ProgramRedDiscardRecoveryDraft` 挂在拥有帧上，`ResumeRedDiscardRecovery` 复用 runtime 帧续接点逐个询问，冻结断言 `AssertRedDiscardRecoveryDraft` 校验草稿与公开承诺，AI 按“拥有者缺失体力 ≥2 优先回复”的公开信息策略选择。
- 新增回合内红色置弃计数：照 `_fullDiscardPhaseSuits` 的回合内标量先例，在卡牌移动批量提交点按 `HasTriggerOperation(offerRedDiscardRecoveryChoice)` 选择性跟踪（`CaptureTurnRedDiscardCount`），回放按命令重放确定性重建，检查点/回放格式零变化。
- 解析门控：该操作仅允许出现在拥有者自己的强制 turnEnding 触发器的唯一效果位；`ProgramRedDiscardRecoveryCommittedEvent` 的 Seats 集合已纳入 `CommittedEventProjection` 冻结。
- 为什么既有原语不够：触发器不支持多目标选择，`selectTargets` 数量为静态整数，`chooseOption` 仅支持单一选择者，且没有“本回合置入弃牌堆的红色牌数量”值源；按参与者迭代先例新增一个自带询问循环的操作。

## 验证

- 界孟获定向 6/6：定义与元数据（含 cardPolicies 形状、turnEnding 强制单触发、文案）、南蛮免疫与伤害来源归属（其他角色使用南蛮入侵时拥有者不受害、其余角色伤害来源=拥有者）、再起主路径（X=3、选择去重、承诺事件、依次询问 recovery/draw/draw、回复事件恰好一次）、满体力只摸牌分支、拒绝发动零事件、黑色置弃全程沉默；关键步骤间做冷恢复与四视角一致性。
- 无过滤日常范围：Core 166/166（新增 6 项计入）、WPF 17/17，wrapper 实测 90.4 秒（含增量构建 13.3 秒、Core 48.4 秒、WPF 28.6 秒）。
- Full 全量：Core 555/555、WPF 56/56，wrapper 实测 299.6 秒（Core 246.3 秒、WPF 51.2 秒；本 worktree）。

## 边界说明

- 主公体力按身份模式惯例 +1（4+1=5）；测试按该口径断言。
- 官方“你可以令至多X名角色”实现为操作内“逐个点选 + 不发动”提示（触发器本体强制、X=0 静默），语义与官方一致且避免 X=0 时的空询问。
- “至多X名”按每名角色至多被选一次实现；同一角色不会被询问两次。
- 本批为单武将批次，在独立 worktree（batch/meng-huo）开发后合并回 main，未触碰主区并行工作。
