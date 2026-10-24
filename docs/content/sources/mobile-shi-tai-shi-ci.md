# 势·太史慈（mobile-shi-tai-shi-ci）来源与实现说明

## 官方来源

- 武将：势·太史慈（移动版“兵势篇”，吴，男，4 体力，编号 600）。
- 技能文本核对（2026-10-24）：
  - 官网详情页 `https://www.sanguosha.cn/pc/hero-detail-600.html`
  - BWIKI 页面 `https://wiki.biligame.com/msgs/势太史慈`
- 技能原文（双源一致）：
  - **酣战**：出牌阶段限一次，你可以选择一名其他角色，你与其依次将手牌摸至体力上限（每次摸牌至多三张），然后你视为对其使用一张【决斗】。
  - **战烈**：①每名角色的回合开始时，你记录X（为你当前的攻击范围）；②本回合前X张进入弃牌堆的【杀】，若仍在弃牌堆，你各获得1枚“烈”（至多为6）；③出牌阶段结束时，你可以移去全部“烈”，视为使用一张无次数限制的【杀】，并至多选择Y项（Y为“烈”数除以3向下取整）：1.目标数+1；2.基础伤害+1；3.此杀需目标角色额外弃置一张手牌才能响应；4.此杀结算后你摸两张牌。
  - **振锋**：限定技，出牌阶段，你可以选择一项：1.回复2点体力；2.将“酣战”和“战烈”描述中的X分别固定为当前体力值、已损失体力值或存活角色数中的某一个数值。

## 立绘

- BWIKI 文件页 `File:势太史慈-经典形象.png`，原图
  `https://patchwiki.biligame.com/images/msgs/9/93/fvf5tctjeasnvia4vk6xvo9nv03q3ws.png`
  （2896×4096 PNG），缩放为 530×750 JPEG 存至 `src/CardGame.Wpf/Assets/mobile-shi-tai-shi-ci.jpg`。

## 实现说明

- registrationOrder：酣战 48800、战烈 48801、振锋 48802、武将 48803；程序操作
  `ResolveShiTz* = 63340..63344`；运行时钩子优先级 67600。
- 酣战：激活以任意两名存活角色为有序参与者（首目标=太史慈、次目标=对手），
  运行时按光标轮流为双方补牌至上限（振锋固定值优先，否则各自体力上限），每步至多
  3 张；随后由既有 `startVirtualDuel` 指令发起与对手的虚拟决斗。
- 战烈：范围记录与“烈”均走事件账本（`ShiTzRangeRecordedEvent` / `ShiTzLieGainedEvent`）。
  每个回合开始记录太史慈当时的攻击范围；运行时观察者扫描本回合进入弃牌堆的【杀】，
  对前 X 张（未重复、总数≤6）各记 1 枚“烈”。杀被移出弃牌堆不回收“烈”。
  出牌阶段结束（可选触发）发布选项组合（至多 Y 项）与目标选择，逐目标以
  `BeginProgramVirtualCardUse` 发起无次数限制的虚拟【杀】。
- 选项实现：
  1. 目标数+1 —— 目标选择允许 2 名（按座次依次结算为两次虚拟杀）；
  2. 基础伤害+1 —— `ProgramDamageHook(AdditiveAmount)` 按战烈帧命中 +1；
  3. 响应需弃牌 —— `slashBeforeResponse` 程序窗口（铁骑同款）接管：目标须弃置
     一张手牌获得响应资格后进入正常响应；目标无手牌时视为无法获得资格，
     此杀对该目标无效（`MarkCardEffectIneffective`），即“未获得资格则不结算”。
  4. 结算后摸两张 —— 最后一次虚拟杀结算完成后原生摸 2 张。
- 振锋：限定技（usesPerGame 1）激活，发布回复 2 点体力（`BeginRecovery`）或
  固定 X（`ShiTzZhenfengFixedEvent` 记账，酣战补牌上限与战烈攻击范围读取该值）。
- “烈”计数与固定值在玩家视图中通过 `ProgramPublicRuleStateSnapshot` 公开
  （`zhanlie-lies` / `zhanlie-y`）。
