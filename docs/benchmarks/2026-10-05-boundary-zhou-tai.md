# 界周泰单武将批次

当前普通 OL 界周泰（吴势力、4 体力）以官方页当前文本接入：不屈（锁定技，濒死判定放“创”，点数唯一回复至 1，手牌上限为创数）+ 奋激（一名角色的手牌被弃置或获得后，可失去 1 点体力令其摸两张牌）。官方立绘 2100 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/boundary-zhou-tai-2026-10-05.json)。

| 技能 | 实现口径 |
| --- | --- |
| 不屈 | 触发体逐字复用已验收的 classic:buqu 程序形状（selfDyingResponse 必发、revealUniqueRankForDying、buquWound 伤口区、rescueHp 1），仅换 boundary id。手牌上限为创数：EvaluateHandLimit 同时识别 boundary:buqu-current。 |
| 奋激 | 弃置分支：discardPileReceived 逐张触发，sourceZones=[hand] 限手牌来源；按 other/own 两条触发覆盖“一名角色”含自己。获得分支：cardsGained 新增 movementOccurrence `perThirdPartyHandGain`（762），seat 解耦收集器观察任意角色手牌被他人获得，上下文 SourceSeat=牌原属者，selectTarget(eventSource)+loseHp+draw(selectedTarget) 全走既有操作面。 |

## 共享能力扩展

- `SkillProgramMovementOccurrence` 新增 `PerThirdPartyHandGain = 762`：cardsGained 边界的第三方观察语义，校验要求该边界。
- `discardPileReceived` 触发窗口补齐 `sourceZones` 字段支持（此前仅 cardsMoved 支持）：逐张匹配按 GetProgramDiscardSource 解析出的真实弃置来源区过滤，经 Processing 暂存的弃置同样可辨；未声明 sourceZones 的既有内容行为不变。
- EvaluateHandLimit 的伤口上限识别从 classic:buqu 单 id 扩展为同时识别 boundary:buqu-current。

## 验证

（合并前填写实测数字）

## 边界说明

- 界曹仁（29，据守/解围）、界于吉（211，蛊惑）、界沮授（749，矢北/渐营）暂无人认领；界曹仁需要“移动场上装备”与“弃置改用”两个新操作面，本批未采纳。
- 本批在独立 worktree（batch/zhou-tai）开发后合并回 main。
