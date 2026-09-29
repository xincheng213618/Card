# RUNTIME V69 —— 规则 192 / 经典包 1.163.0

本版本号为凌统批次（旋风）使用。Checkpoint schema 仍为 3；规则版本 189 → 192（190/191 由并行批预留），经典包 1.160.0 → 1.163.0（1.161/1.162 由并行批预留）。技能 JSON schema 62 不变（本批零新增节点类型）。旧规则的检查点按现有版本边界拒绝恢复。

## 凌统批次：旋风

本批为单武将交付：凌统（旋风，一将成名2011，吴 4 体力，最低规则 192）。**共享引擎语汇零新增操作、零新增窗口、零新增枚举**；唯一引擎改动是既有 `DiscardPhaseEnded` 生命周期窗口的 `CanRunProgramTrigger` 臂从"仅他人回合"放宽为按 `turnOwnerScope` 分支——与 `turnEnding`/`playEnding`/`playPhaseStarting` 三臂既有形状完全同构。

### 版本选型（口径冻结）

旋风有两版：一将成名2011 原版两分支（弃牌阶段弃置过至少两张手牌 ∪ 失去装备区里的牌）与 2014 修订版单分支（仅失去装备区里的牌）。以十周年官方武将库 gid 292 现行页面为唯一口径：页面展示**保留弃牌阶段分支的两分支文本**（官方润色措辞），故按 2011 原版实现，2014 修订版不在本批范围。BWIKI 与官方 2011 名单公告双源核对包归属（一将成名2011）。

### 消费方

- 旋风·弃牌阶段分支：trigger `discard-phase-gust`，window `discardPhaseEnded`、subject owner、**turnOwnerScope own**（本批解锁的臂分支）、optional、condition compare(`turnOwnerDiscardPhaseHandDiscardCount` ≥ 2)——该触发值（枚举 26）与弃牌阶段台账（`_discardPhaseHandDiscardIds`，仅统计当前回合拥有者在弃牌阶段的手牌→弃牌堆移动）由张昭张纮批（固政）落地，本批是它的第一个弃牌阶段触发条件消费方；effects = `chooseOtherOwnedCardDiscard` × 2（zones hand+equipment，chooserRef owner）。
- 旋风·装备区分支：trigger `equipment-loss-gust`，window `cardsMoved`、subject owner、sourceZones [equipment]、movementOccurrence perBatch、optional、**无 excludedMovementReasons、无 ignoreOwnSkillMovements**——按 BWIKI 细则，装备变更、被拆、被顺走、贯石斧/技能弃置交换均属"失去装备区里的牌"，每次失去（一批）触发一次；effects 同上两步。
- "依次弃置一名有牌的其他角色的两张牌，或选择两名有牌的其他角色，弃置这些角色的各一张牌"：两步自由的 `chooseOtherOwnedCardDiscard`（弓骑既有操作：手牌暗置槽位/装备区明置、逐张选择、步内可拒绝）的组合可达集与官方两分支划分完全一致（共两张、至多两名目标）；弃置区域沿用弓骑先例取手牌+装备区（不含判定区）。

### 引擎改动（一处）

`GameEngine.ProgramLifecycle.cs` 的 `CanRunProgramTrigger` `DiscardPhaseEnded` 臂：原 `owner.Seat != _currentSeat && owner.IsAlive && …`（只服务固政类 otherLiving 观察者）改为 `owner.IsAlive && … && (TurnOwnerScope == Own ? owner.Seat == _currentSeat : owner.Seat != _currentSeat)`。otherLiving 语义逐字保留；own 分支允许回合拥有者本人在自己的弃牌阶段结束时被收集（收集侧 `TryBeginDiscardPhaseEndedProgramWindow` 的 turnOwnerScope 过滤早已双向支持，仅此臂单边拦截）。新窗口/新操作为零——五点注册清单不适用。

### 内容与注册

- `src/CardGame.Content.Standard/SkillPrograms/classic-ling-tong.rules.json`（schema 62，minimumRulesVersion 192）
- `src/CardGame.Content.Standard/SkillPrograms/classic-ling-tong.presentation.json`（schema 3，名称+描述）
- 注册：`StandardClassicGeneralPackage.cs` 资源常量 + 懒加载目录 + `LingTongProgram` + 技能块（WithOptionalTriggerMetadata）+ `AddGeneral`（吴，4 体力）+ `CurrentGeneralIds`；包版本 1.160.0 → 1.163.0；`Replay.cs` 规则 189 → 192（190/191 并行批预留，见版本源注释）
- 立绘：`official-ling-tong.png`（gid 292 经典 129201，574×761）；`tools/sync_general_art.py` CLASSIC_HEROES 加 `"ling-tong": 292`；`docs/content/general-art-catalog.json` entries 新增；`GeneralGalleryCatalog.cs` fame-1（一将成名2011 官方名单）新增

## 验证口径

定向检查 `LingTongChecks`（4 项）全部通过：定义与 schema + 解析拒收面八项（弃牌阶段分支带 sourceZones、装备分支带 turnOwnerScope、装备分支带 suits、比较值左目带 value、弃置目标非 owner、zones 含 discardPile、缺 chooserRef、movementReasons 与 excluded 重叠）；弃牌阶段分支"弃三张手牌→弃两名其他角色各一张"+回放一致；放弃分支（拒绝两步）零移动+回放一致；装备分支"二连弩替换→旧弩离装备区→弃两名其他角色各一张"+回放一致。种子扫描为动态停止（1..120 首个命中即验），无静态白名单。Debug 全量 Core **654/654 全绿**（无入池位移失败）；Release 构建 0 error；WPF 过滤器验证：缺立绘名单为既有 8 名（不含 ling-tong），"general gallery combines registered series" 通过。批次记录见 [2026-09-30-ling-tong](../../benchmarks/2026-09-30-ling-tong.md)。
