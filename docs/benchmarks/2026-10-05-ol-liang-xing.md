# 梁兴单武将批次

当前 OL 梁兴（群势力、4 体力）接入：掳掠（出牌阶段开始时，你可以令一名有手牌且手牌数小于你的其他角色选择一项：1.将所有手牌交给你，然后你翻面；2.翻面，视为对你使用一张【杀】）+ 追袭（锁定技，当你对其他角色造成伤害时，或受到其他角色造成的伤害时，若其中一方武将牌背面朝上且另一方武将牌正面朝上，此伤害+1）。官方立绘 51400 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-liang-xing-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 掳掠 | playPhaseStarting 可选触发：selectTarget（本批新增 targetKind `otherLivingWithFewerHandCards`：有手牌且少于你；skipIfNoTarget）→ chooseOption（chooserRef selectedTarget，由对方选择）→ 分支一（本批新增 op `giveSelectedTargetHand` 7162：对方全部手牌移交己方，turnOver 翻己方）；分支二（turnOver 翻对方 + 本批升级的 op `selectedTargetVirtualSlashAgainstOwner` 7164：对方作为使用者对梁兴使用一张真实虚拟【杀】，含完整响应窗口与杀类联动）。 |
| 追袭 | 伤害修正器（damageParticipant 作用域，amount 1，任意伤害类别）+ 本批新增条件 `faceStatesDiffer`（伤害双方翻面状态相异且非自伤）；组合校验器允许该条件下省略 cardKinds。 |

## 共享能力扩展

- targetKind `otherLivingWithFewerHandCards`（1462）。
- op `giveSelectedTargetHand`（7162）：descriptor/handler/host 反射注册，引擎方法整手移动并挂起移动触发窗口（awaitMovementTriggers 口径）。
- 伤害修正器条件 `faceStatesDiffer`（1021）：damageParticipant 作用域，允许空 cardKinds。

## 边界口径

- “视为对你使用一张【杀】”已升级为第三方虚拟用牌（2026-10-06 批次 op 7164）：对方为其作用者，走正常距离/目标合法性与完整响应窗口；该强制使用不计入对方的出牌阶段使用账本与酒状态。
- 掳掠的目标选择在候选为空时不提示（skipIfNoTarget）。
- 追袭对任意类别伤害生效（含非牌伤害），源与目标为同一角色时不加成。

## 验证

- 定向 `--filter="Ol Liang Xing"`：2/2 通过（定义元数据与演示文案；掳掠选人→对方分支→分支效果与追袭 +1 的行为链，含冷恢复重放比对）。
- 例行 `tools/Test-Changed.ps1`（分支）：失败集合与 main（02c49d04）基线逐项 diff 零新增（实测数字见最终回复）。
- 本批在独立 worktree（batch/ol-liang-xing）开发后合并回 main。
