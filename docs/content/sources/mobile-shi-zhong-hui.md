# 势·钟会（mobile-shi-zhong-hui）来源与实现说明

## 官方来源

- 武将：势·钟会（移动版“兵势篇”，魏（迂难后变更为群），男，4 体力，编号 640）。
- 技能文本核对（2026-10-24）：
  - 官网详情页 `https://www.sanguosha.cn/pc/hero-detail-640.html`
  - BWIKI 页面 `https://wiki.biligame.com/msgs/势钟会`
- 技能原文（双源一致）：
  - **肆恣**：蓄力技（4 点蓄力）。出牌阶段限一次，你可以减少任意点蓄力点（X），然后执行以下效果，直至 X 个回合结束后或你的回合开始时：1. 所有角色使用【杀】造成的伤害+1；2. 一名角色的回合结束时，你摸两张牌，且本回合内使用过【杀】的角色各失去 1 点体力；若 X 大于你的体力值，追加效果 3：一名角色的回合结束时，若该回合内没有角色使用过【杀】，当前回合角色失去 1 点体力。
  - **挟志**：锁定技，当你的体力值变化后，你获得 X 点蓄力点（X 为本次变化的值）；若你会因此获得超额蓄力点，你的手牌上限与使用【杀】的次数上限永久+1。
  - **迂难**：觉醒技，当你令一名角色进入濒死状态时，若本轮已有角色死亡，你将势力变更为“群”，然后获得技能“克昌”（若已有则升级）。
  - **克昌**：主公技，锁定技，群势力角色使用【杀】无距离限制；二级（升级后）新增：你使用的【杀】不可被响应。

## 立绘

- BWIKI 文件页 `File:势钟会-经典形象.jpg`，原图
  `https://patchwiki.biligame.com/images/msgs/0/09/grp7bjp0zdjzc7197ily414ge9estej.jpg`
  （1749×2475 JPEG），缩放为 530×750 JPEG 存至 `src/CardGame.Wpf/Assets/mobile-shi-zhong-hui.jpg`。

## 实现说明

- registrationOrder：肆恣 50600、挟志 50601、迂难 50602、克昌 50603、武将 50604；程序操作
  `ResolveShiZhHui* = 63350..63353`；蓄力标记 `PlayerMarkerKind.ShiZhHuiCharge = 33250`（显示名“蓄力”）；
  运行时钩子优先级 67700。
- 肆恣：出牌阶段激活（usesPerPhase 1，无蓄力点时不入合法动作），发布消耗 1..min(蓄力,4) 的
  数值选择；消耗后记 `ShiZhHuiSiziActivatedEvent`（序数、X、激活时体力）。效果 1 由
  `ProgramDamageHook(AdditiveAmount)` 实现（效果存续期间所有角色【杀】伤害+1）。
  效果 2/3 挂在 `turnEnding` 触发（`turnOwnerScope: anyLiving`，任意角色回合结束结算）：
  先摸 2 张，再按本回合【杀】使用账本（`ShiZhHuiSlashUsedEvent`）对每名使用过【杀】的存活
  角色各失去 1 点体力；若无人用杀且 X 大于激活时体力，则当前回合角色失去 1 点体力。
  每次结算记 `ShiZhHuiSiziElapsedEvent`，耗满 X 或钟会回合开始（观察者）记
  `ShiZhHuiSiziExpiredEvent`；效果存续查询一律由事件账本推导。
- 挟志：`afterHealthChanged` 锁定触发，按变化绝对值经公共标记加蓄力（上限 4）。
  触顶溢出记 `ShiZhHuiXiezhiOverflowEvent`（每次溢出=1 次）；手牌上限与【杀】次数上限由
  `EvaluateHandLimit` / `EvaluateSlashUseLimit` 的 `issued:*:mobile:shi-xiezhi:*` 贡献项按
  溢出次数永久+1。
- 迂难：`dyingEntering` 触发（subject any），资格门槛在 `ProgramTriggerLegality` 中校验
  “濒死 KillerSeat==钟会 且本轮已有角色死亡”（死亡账本 `ShiZhHuiDeathRecordedEvent`）。
  觉醒时按 `ApplyCaoTengFactionChange` 同款写入 `NativeEffectiveFactionOverrideId` 变更为
  群势力（`TracksNativeEffectiveFactionChanges` 已纳入迂难操作），再授予“克昌”
  （`GrantProgramSkills`），等级（1/2）记入 `ShiZhHuiYunanAwakenedEvent`。
- 克昌：无激活/触发的锁定主公技（`lordSkillProjection: true`），运行时行为：
  1 级——`ProgramDistanceRule(CardUseExemption)` 对群势力角色使用的【杀】豁免距离限制
  （克昌持有者存活期间生效）；2 级——`IsNearbyTargetResponseProhibited` 中央调度新增
  `ShiZhHuiKeChangCannotRespond` 一行，钟会使用的【杀】不可被响应。
- 覆盖：肆恣全流程（消耗/伤害+1/回合结束摸牌与惩罚/计满过期）、挟志溢出永久加成、
  迂难觉醒（势力变更+授予+升级）与克昌距离豁免、二级不可响应各有专项检查
  （`ShiZhongHuiChecks`，含冷恢复与短命令重放校验）。
