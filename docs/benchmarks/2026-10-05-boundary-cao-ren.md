# 界曹仁单武将批次

当前普通 OL 界曹仁（魏势力、4 体力）以官方页当前文本接入：据守（结束阶段可翻面摸四张牌，然后弃置一张手牌或使用一张装备牌）+ 解围（装备区牌当【无懈可击】；翻至正面时可弃置一张牌，选一名其他角色，将其装备区里的一张牌移动到另一名角色的装备区）。官方立绘 2900 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/boundary-cao-ren-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 据守 | playEnding 可选触发：setFaceState(faceDown) + draw 4 + discardHandOrUseEquipment。 |
| 解围 | viewAs：equipment 源区 → nullification（forResponse）。characterTurnedFaceUp 可选触发：selectAndMoveOwnedCard(own hand+equipment 1→discardPile, skipIfNoCards) + selectTarget(otherLiving, skipIfNoTarget) + moveFieldEquipment。移动取“selectTarget=去向角色、候选=场上其余角色装备牌”的口径，可达配对与原文一致。 |

## 共享能力扩展

- `SkillProgramEffectOp` 新增 `DiscardHandOrUseEquipment = 7150` 与 `MoveFieldEquipment = 7151`，注册在 `ProgramCaoRenOperations.cs`（descriptor + public handler + `ICaoRenProgramHost`）：
  - `discardHandOrUseEquipment`：提示层复用 StrategicDraft 的“弃置或使用”二选一形态（卡候选=可弃手牌，含 decline 分支）；应答为手牌则走弃牌移动 + cardsMoved 窗口，应答为装备牌则走 BeginCardUse 装备使用路径后程序独立结算。
  - `moveFieldEquipment`：`ReadSelectedTarget` 提供移动来源（selectTarget 先行），候选=来源装备区非 IsGeneralWeapon 且能进入去向对应槽位的牌；移动走 BeginCardMovementBatch/Processing 暂存/RecordMovement/ResolveEquipmentSkillGrant/ResolveSilverLionRemoval 公共路径。
- 两操作均为独立标准形态，未改动既有窗口或收集器语义。

## 验证

- 定向 `--filter="Boundary Cao Ren"`：4/4 通过（定义元数据、据守翻面摸四弃手牌、据守费用改用装备、解围翻面弃牌后跨角色移动装备，含冷恢复重放比对）。
- 例行 `tools/Test-Changed.ps1`：Core 257 通过 / 26 失败 + WPF 18/18，约 118 秒。失败集合与干净基线 f1629581 完全一致（逐项 diff 为空），全部为并行批次既有债，本批零新增失败。
- 解围的 viewAs（装备牌当【无懈可击】应答）由定义解析检查覆盖，运行时走既有 viewAs 应答共享路径，未做独立应答集成。

## 边界说明

- 界于吉（211，蛊惑-重）、界沮授（749，矢北/渐营）暂无人认领。
- 本批在独立 worktree（batch/cao-ren）开发后合并回 main。
