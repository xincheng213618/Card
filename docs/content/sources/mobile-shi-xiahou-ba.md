# 势·夏侯霸（mobile-shi-xiahou-ba）来源与实现说明

## 官方来源

- 武将：势·夏侯霸（移动版“兵势篇”，魏，男，4 体力）。
- 来源核对（2026-10-24）：
  - BWIKI 页面 `https://wiki.biligame.com/msgs/势夏侯霸`（URL 编码 `%E5%8A%BF%E5%A4%8F%E4%BE%AF%E9%9C%B8`）。
  - 官网：`https://www.sanguosha.cn/pc/hero-list.html` 中没有“势夏侯霸”条目（普通版夏侯霸为 hero-detail-78），官网亦无对应 hero-detail 页。BWIKI 标注该武将“仅存在爆料版”。本武将为 **BWIKI 单源**。
- 技能原文（BWIKI 当前版本，逐字）：
  - **厉兵**：锁定技，当你使用非伤害类卡牌结算完毕后，你执行上次执行的选项的下一项：【1】攻击范围+1；【2】摸X张牌（X为执行过此选项的次数+1）；【3】使用的下一张伤害牌伤害+1。当你使用伤害类卡牌结算后，重置此技能。
  - **迫袭**：锁定技，当你的牌因弃置而进入弃牌堆时，若此牌为【杀】，则你改为使用之（此牌不计入次数且无距离次数限制）。你以此法使用的【杀】不能被手牌数不大于你的角色响应。
- 台词：BWIKI 全部标注“未知”。

## 立绘

- BWIKI 上不存在任何势夏侯霸原画：“经典形象”栏位是上传占位，全站唯一的武将图片是 88×88 的通用“将”字头像
  `https://patchwiki.biligame.com/images/msgs/6/6a/irswi4ywz6nor0vke3nfp2n8sk4le46.png`（`File:势夏侯霸-头像.png`）。
- 因此 `src/CardGame.Wpf/Assets/mobile-shi-xiahou-ba.jpg`（530×750 JPEG，SHA-256
  `643ab156791e245932d73629dfec976bf68d283365734a966b8f13aaf16b67df`）为**临时占位图**：以该头像原图为素材居中排版、渐变底色，非角色原画。待官方发布经典形象后应由集成者替换并更新美术目录条目。

## 实现说明

- registrationOrder：厉兵 51300、迫袭 51301、武将 51304；程序操作 `ResolveShiXbLibbing = 63440`、
  `ResolveShiXbPoxi = 63441`；运行时钩子优先级 68000。无新增玩家标记。
- 厉兵：`cardUseCompleted`（ownerRelation actor）锁定触发。伤害类卡牌判定沿用雷铜移动版
  `IsDamageCardKind` 清单（杀系+决斗/南蛮/万箭/火攻/霧妙/奇正相生，不含闪电）。全部进度（上次选项、各选项执行次数）
  由事件账本推导（`ShiXbLibbingAdvancedEvent` / `ShiXbLibbingResetEvent`），无独立持久字段：
  选项 1 的攻击范围加成经 `EvaluateAttackRange` 的 `issued:<seat>:mobile:shi-libing:attack-range` 有限贡献项按
  “自上次重置以来选项 1 的执行次数”累计；选项 2 立即摸（次数+1）张；选项 3 的待决伤害加成由
  `ProgramDamageHook(AdditiveAmount)` 按同口径次数对使用者后续伤害类卡牌伤害+1，重置事件即消费点
  （重置只会在该伤害牌结算完毕时发生）。选项按 1→2→3→1 循环；“重置此技能”实现为清零选项指针与全部计数。
- 迫袭：`discardPileReceived` 锁定触发（`movementDiscardOnly`+`discardOwnerScope: own`+来源手牌/装备区+
  四种【杀】卡牌种类，逐张触发）。触发后校验原牌仍在弃牌堆，枚举其他存活角色中
  `CanUseSlashTarget(ignoreDistance: true)` 的合法目标；无目标则牌留在弃牌堆，有目标则向持有者发布目标选择。
  确认后按黄祖断鞅同款原实体用牌流程结算：`BeginCardUse` 校验动作成本来源为弃牌堆，将牌以
  `skill-program.mobile-shi-xiahou-ba.use` 移入处理区，`ShiXiahouBaPoxi` 凭据绑定 `CardUseFrame`
  （`UnlimitedUse` + `ProgramCardUsePaymentStage.SlashCountExemption` 保证不计入次数），随后进入原生杀目标结算。
  原用牌整场返回后记账 `ShiXbPoxiNativeReturnedEvent` 并关闭程序帧。
- 响应限制：`IsNearbyTargetResponseProhibited` 中央调度新增 `ShiXbPoxiCannotRespond` 一行——仅在当前最内层
  用牌恰为迫袭原实体【杀】时，手牌数不大于使用者的目标角色不能响应。
- 覆盖：厉兵三选项循环与伤害牌重置（含范围加成随重置清零）、迫袭弃杀改用（远距离原实体杀、不计次数）与
  手牌数响应门槛、非【杀】弃置不触发各有专项检查（`XiahouBaChecks`，含冷恢复与短命令重放校验）。
