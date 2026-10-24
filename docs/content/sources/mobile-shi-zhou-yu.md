# 势·周瑜（mobile-shi-zhou-yu）来源与实现说明

## 官方来源

- 武将：势·周瑜（移动版“兵势篇”，吴，男，4 体力，编号 662）。
- 技能文本核对（2026-10-24）：
  - BWIKI 页面 `https://wiki.biligame.com/msgs/势周瑜`
  - 官网详情页 `https://www.sanguosha.cn/pc/hero-detail-662.html`
  - 双源名称与技能文本一致。
- 技能原文（双源一致）：
  - **炽沄**：当你于每个阶段首次获得牌后，你可以将任意张手牌交给一名其他角色。若如此做，你令其选择一项：【1】展示所有与这些牌颜色相同的手牌，然后你对其造成1点火焰伤害；【2】你摸两张牌，然后令其横置。
  - **焰洄**：当你使用牌指定其他角色为目标后，你可以展示目标角色的一张手牌，然后若此牌不为本回合第一次展示，则你弃置之。出牌阶段结束时，你选择一项：【1】对一名因此技能弃置过牌的角色造成1点火焰伤害；【2】摸X张牌（X为本回合展示过牌的角色数）。
  - **焚涛**：锁定技，当一名角色受到火焰伤害时，若该角色不为你且其处于横置状态，则其选择一项：【1】令本次传导的伤害值+1；【2】弃置一半的牌（向上取整），然后此伤害结算完毕后，令其横置。
  - **雄姿**：限定技，准备阶段，你可以令本局游戏的“炽沄”、“焰洄”、“焚涛”均只能于你的回合内发动，且仅保留其中全部的选项一或者选项二（仅存在两个选项的技能方可执行），然后你摸两张牌。

## 立绘

- BWIKI 文件页原图
  `https://patchwiki.biligame.com/images/msgs/5/51/osstb0c0n3m8u8749670ero5nxwz9lt.png`
  （530×750 PNG），转存为 530×750 JPEG 存至
  `src/CardGame.Wpf/Assets/mobile-shi-zhou-yu.jpg`
  （sha256 `066e38377c65fd53a926dec12492482e429b22f8685660023e1dbef7851c724e`）。

## 实现说明

- registrationOrder：炽沄 51102、焰洄 51103、焚涛 51104（锁定）、雄姿 51105（限定）、
  武将 51106；程序操作 `ResolveShiZhouYu* = 63360..63366`；运行时钩子优先级 67800。
- 炽沄：`cardsGained` 触发（subject owner，destinationZones hand，`movementOccurrence:
  perBatch`，`ignoreOwnSkillMovements` 屏蔽自身摸牌/给牌的回环）。每阶段首次以
  `ShiZhouYuChiyunPhaseSeenEvent`（回合、阶段、座位）去重。给出批记
  `ShiZhouYuChiyunGivenEvent`（含各牌颜色）后由接收者选择：选项一以
  `CommitProgramHandReveal` 展示同色手牌并 `BeginProgramSkillDamage`（火焰 1）；
  选项二 `DrawProgramCards(2)` 加 `SetProgramChainedState` 横置接收者，记
  `ShiZhouYuChiyunOptionEvent`。
- 焰洄：`cardUseTargetsFinalized` 触发（ownerRelation actor）进入展示阶段，展示目标真实
  手牌（观看即技能效果，选项携带真实卡牌 id），记 `ShiZhouYuYanhuiShownEvent`（含是否
  本回合已展示过）；重复展示经 `MoveProgramCardsFromMultipleSources` 以
  `skill-program.mobile:shi-yanhui.discard` 弃置。`playEnding` 触发结算收尾选择：
  对弃置过牌者火焰 1（`ShiZhouYuYanhuiEndingChosenEvent`）或摸 X（X=本回合展示过牌的
  角色数账本）。
- 焚涛：`beforeDamageApplied`（subject any）门槛校验火焰伤害、目标非本人且横置；
  受害者选择增幅记 `ShiZhouYuFentaoChosenEvent`，选项一由
  `ProgramDamageHook(AdditiveAmount)` 按攻击结算 id 匹配 +1；选项二按
  （手牌+装备+1）/2 弃置并在 `afterDamageApplied`（damageOccurrence perDamage）以
  `ShiZhouYuFentaoRechain` 计划横置，结算完毕记 `ShiZhouYuFentaoRechainedEvent` 后
  `SetProgramChainedState` 复横置。
- 雄姿：`turnStartBeforeNormalFlow` 触发（仅本人回合、每局限一次），选择仅保留选项一或
  选项二并记 `ShiZhouYuXiongziActivatedEvent`，然后摸两张。三种技能的资格门槛
  `CanOfferShiZhouYuUnderXiongzi` 依据该账本把发动限制在本人回合并裁剪对应选项。
- 程序在活跃卡牌结算（`CardUseTargetsFinalized`/`BeforeDamageApplied` 窗口）内发出
  弃牌移动后同步驱动延续（无触发候选的移动批不会自行归还控制权）；伤害子结算经
  `CompleteProgramAttack` 恢复后于 `ChiYunStageResolveDamage`/`YanhuiStageEndingDamage`
  阶段收尾。移动证明 `ProgramMovementProof(NativeCost)` 覆盖炽沄给牌、焰洄/焚涛弃置三个
  批次。
- 覆盖：炽沄给牌与接收者选项二（摸牌+横置）全流程、焰洄重复展示弃置与结束火焰伤害
  （含焚涛增幅联动）、雄姿保留选项二与焚涛弃置复横置各有专项检查
  （`ZhouYuChecks`，含冷恢复与短命令重放校验）。
