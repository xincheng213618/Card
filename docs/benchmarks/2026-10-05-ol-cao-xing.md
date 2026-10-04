# 曹性单武将批次

当前 OL 曹性（群势力、4 体力）以官方页当前文本接入：流矢（出牌阶段，你可以将一张红桃牌置于牌堆顶，视为对一名角色使用一张无距离和次数限制的【杀】，若此【杀】造成伤害，其手牌上限-1）+ 斩腕（锁定技，当受到“流矢”效果影响的角色于弃牌阶段弃牌后，你摸等量张牌并移除其“流矢”效果）。官方立绘 47600 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-cao-xing-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 流矢 | 出牌阶段：playPhaseStarting 可选触发（沿用界夏侯渊神速/神黄忠模板）——selectTarget otherLivingVirtualSlashTarget → selectAndMoveOwnedCard（zones hand、suits [heart] 本批开放、destination drawPileTop、awaitMovementTriggers）→ useVirtualCard slash（useCardActionWindows + distanceUnlimitedAgainstTarget）。伤害追责：cardUseCompleted（ownerRelation actor、cardKinds slash）+ cardUseCausedDamage + cardUseConversionSkillIs（虚拟用牌本批开始记录自身 CardConversionSource）→ changeParticipantMarker liuShi +1 + adjustPersistentHandLimit -1（均 targetRef eventTarget）。 |
| 斩腕 | discardPhaseEnded（turnOwnerScope otherLiving、optional false）+ compare eventTargetMarkerCount liuShi ≥ 1 → draw（numberExpression turnOwnerDiscardPhaseHandDiscardCount 本批新增表达式）→ changeParticipantMarker -1 + adjustPersistentHandLimit +1（还原）。 |

## 共享能力扩展

- `selectAndMoveOwnedCard` 开放 `suits` 花色过滤（沿 selectAndMoveOwnedCard → handler → host → BuildOwnedCardPaymentChoices 既有类别的过滤链），牌堆顶支付路径本就要求 owner 手牌单张 + resultBind + awaitMovementTriggers。
- `changeParticipantMarker` 与 `adjustPersistentHandLimit` 支持 owner 占位 + `targetRef`（仅 eventTarget）：executor 对这两个效果按 TargetReference 解析目标座位；`ProgramPersistentHandLimitChangedEvent` 追加可选 `TargetSeat`（缺省 -1 = 旧的所有者口径，旧档重放行为不变），手牌上限贡献按生效座位归属（技能持有者的运行时实例校验保留）。
- `adjustPersistentHandLimit` 金额从 ±1 放宽为有界非零整数（现有使用者仍为 ±1）。
- 弃牌阶段结束窗口的候选收集在启用标记操作时叠加阶段拥有者的 `EventTargetMarkerCounts`，`eventTargetMarkerCount` 校验白名单补 DiscardPhaseEnded（eventTarget 引用白名单补 CardUseCompleted 与 DiscardPhaseEnded）；`HasAttributedEventOperations` 识别 `ChangeParticipantMarker`。
- 标记变更的运行时窗体白名单补 AfterDamageApplied / CardUseCompleted / DiscardPhaseEnded（组合校验器同步），CardUseCompleted 的父帧校验走 ProgramCardTriggerWindowFrame、AfterDamageApplied 走 ActiveDamageTrigger。
- 虚拟用牌（useCardActionWindows 时）记录自身技能的 CardConversionSource——此前转换链恒为空，cardUseConversionSkillIs 无法匹配程序虚拟用牌。

## 边界口径

- 多层“流矢”标记按弃牌阶段逐层移除（一次弃牌阶段结束移除一层并还原 1 点手牌上限）；斩腕的摸牌数取阶段拥有者本回合弃牌阶段的手牌弃牌数（既有账本值）。
- 虚拟【杀】不可被响应的场面由牌堆构成决定（夹具证明路径）；AI 摸牌后不主动打出无懈可击，弃牌阶段必然触发斩腕。

## 验证

- 定向 `--filter="Ol Cao Xing"`：2/2 通过（定义元数据；激活支付→牌堆顶→无距离虚拟【杀】→伤害→标记+手牌上限-1→弃牌阶段结束摸等量+标记清除+上限还原→第二回合不再触发，含冷恢复重放比对）。
- 例行 `tools/Test-Changed.ps1`（分支）：Core 260 通过 / 27 失败 + WPF 18/18；与 main（ee8e8522）同口径例行的失败集合逐项 diff **完全一致（零新增失败）**，27 项均为并行批既有债（SP曹仁/陈琳/SP蔡文姬、Tiered Round、程普、吴国太、李儒、鲁肃、马岱、不恋世、吕蒙、杨修、SP马超、关银屏、诸葛恪、伏完、刘协、SP庞德等）。
- 本批在独立 worktree（batch/ol-cao-xing）开发后合并回 main。
