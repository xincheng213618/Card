# 界姜维单武将批次

当前普通 OL 界姜维（蜀势力、4 体力、男性、史诗）以官方页当前文本接入：挑衅（每阶段限两次，选攻击范围内包含自己的角色，其除非对自己使用【杀】且此【杀】造成伤害，否则被弃置一张牌）、志继（准备阶段或结束阶段无手牌觉醒：回复 1 点体力或摸两张牌，减 1 体力上限，获得观星）。官方立绘 48000 已离线入库并登记图鉴/映射/同步脚本。来源见[来源档案](../content/sources/boundary-jiang-wei-2026-10-03.json)。

| 技能 | 实现口径 |
| --- | --- |
| 挑衅 | 激活体（minCards 0、`targetKind=otherLivingWhoseAttackRangeIncludesOwner`、`usesPerPhase=2`）逐字复用已验收的 classic:tiaoxin 程序形状，仅把每回合一次改为每阶段两次。效果链 `requestSlashByTarget(resultBind tiaoxin-answer)` + `selectAndMoveOwnedCard`（chooser=owner、cardOwner=selectedTarget、hand+equipment、弃入弃牌堆、awaitMovementTriggers）与 classic 一致。界升级点在弃牌分支条件 `not(all(choiceIs used-slash, requestedSlashDamagedOwner))`。 |
| 志继 | 觉醒体（currentHandCount==0、changeMaximumHp -1、grantSkills classic:guanxing、chooseOption recover/draw-two）与 classic:zhiji 一致；界升级点是新增结束阶段时机：turnStartBeforeNormalFlow 与 turnEnding 双触发窗口共用 `namedUsageGroup "zhiji-awakening"`（usageScope game、usageLimit 1），整局仅觉醒一次。 |
| 观星 | 觉醒获得技，`grantSkills` 直接授予已验收的 classic:guanxing，不复制定义。 |

## 共享能力扩展

- 新增引擎条件 `requestedSlashDamagedOwner`（枚举 1023）：请求杀子攻击（`ProgramSkillFrameId` 空且 `ProgramSkillCardUseFrameId` 非空）在 `CompleteAttackAfterCardResolution` 恢复点把 typed 标量 `DamageWasApplied && FinalTargetSeat == OwnerSeat` 写回拥有帧（`AttackCompletionReceipt` 增加 RequestedSlashChild/DamageWasApplied/FinalTargetSeat，仅同步消费，不入序列化/回放）。符合十四批“typed 标量事实归拥有帧”契约。
- 解析门控：`requestedSlashDamagedOwner` 仅允许出现在组合效果条件内（`allowRequestedSlashDamage` 旗标随子条件递归继承），且不得携带 value/children/sourceBind/stateId/optionId 等附加字段；简单求值路径与无帧求值路径按既有惯例抛错。
- `AttackCompletionReceipt` 回执为引擎内部记录类型；现有回放/检查点格式零变化。

## 验证

- 界姜维定向 7/7：定义与元数据（含 usesPerPhase=2、双窗口共享组、观星授予、文案）、未出杀弃其一张牌、出杀被闪避仍弃牌且不受伤、出杀造成伤害则免弃（且攻击者仅失去那张杀）、同阶段第二次挑衅与第三次拒绝、结束阶段觉醒（摸二/减上限/得观星/单次选择事实）、准备阶段觉醒与双时机共享一次（第二回合准备阶段醒一次，第三回合保持沉默）；关键步骤间做冷恢复与四视角一致性。
- 无过滤日常范围：Core 160/160（新增 7 项计入）、WPF 17/17，wrapper 实测 72.3 秒（含增量构建）。
- Full 全量：Core 549/549、WPF 56/56，wrapper 实测 208.3 秒（本 worktree，合并 main 前）。

## 边界说明

- 主公体力按身份模式惯例 +1（4+1=5），志继觉醒后为 4；测试按该口径断言。
- 官方挑衅文案保留“限一次……本阶段本技能限两次”的原文措辞，程序语义按“本阶段限两次”实现（usesPerPhase=2）。
- 挑衅的弃牌选项对拥有者隐藏目标手牌牌面（`program-action=select-and-move-owned-card` 隐藏槽位选择），测试按槽位驱动。
- 本批为单武将批次：界吕布/界袁绍由 FengLinFifteenth 批次、界夏侯渊/界小乔由 FengLinSixteenth 批次并行实施；本批在独立 worktree（batch/jiang-wei）开发后合并回 main，未触碰主区并行未提交工作。
