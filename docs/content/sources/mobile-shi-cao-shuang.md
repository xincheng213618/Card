# 势·曹爽（mobile-shi-cao-shuang）来源与实现说明

## 官方来源

- 武将：势·曹爽（移动版“兵势篇”，魏，男，4 体力）。
- 技能文本核对（2026-10-24）：
  - 官网详情页：未收录。对 `https://www.sanguosha.cn/pc/hero-detail-N.html` 的 N=663..685 逐一探测
    （官网当前最大编号 675），均无势曹爽详情页（404），结论为 **BWIKI 单源**。
  - BWIKI 页面 `https://wiki.biligame.com/msgs/势曹爽`；页面注明该武将“仅存在爆料版”
    （2 月爆料、未上线），技能文本取其**当前版本**面板。
- 技能原文（BWIKI 当前版本面板）：
  - **奢权**：锁定技，每轮各限一次，【当你造成伤害后/当你受到伤害后/当一名角色进入濒死阶段后】，你令所有其他角色同时选择一张手牌，并标记为“奢权”牌。
  - **典易**：锁定技，每轮各限一次，【当你造成伤害后/当你受到伤害后/当一名角色进入濒死阶段后】，你随机获得X张“奢权”牌（X为此技能本轮的发动次数）；然后若你本轮以此法获得牌数大于你的体力上限，你失去1点体力。
  - **渐专**：锁定技，①你的“奢权”手牌点数为K，其他角色的“奢权”手牌点数为A。②当你本局游戏首次：【造成伤害后/受到伤害后/当一名角色进入濒死状态时】，你为“典易”或者“奢权”增加一个每轮限一次的对应触发时机。③当你增加过所有的时机后，你失去“典易”或者“奢权”，然后你获得“溺尊”并增加X点体力上限回复X点体力（X为你失去对应技能的时机数）。
  - **溺尊**：①你使用过牌的回合结束时，你将本回合弃牌堆中的“奢权”牌置于你的武将牌上。②每回合限一次，你可以如手牌般使用这些牌。若如此做，当你下一次受到伤害时，此伤害值+X（X为你本轮以此法使用牌的数量）。

## 立绘

- BWIKI 文件页 `File:势曹爽-经典形象.png`，原图
  `https://patchwiki.biligame.com/images/msgs/2/26/h18w43bt6fdwfsbwez7mt70ywo56xj9.png`
  （600×600 PNG），缩放至 750×750 后居中裁剪为 530×750 JPEG 存至
  `src/CardGame.Wpf/Assets/mobile-shi-cao-shuang.jpg`
  （sha256 `ff23885389b2539c6e1043cbe59a379d753a4d5164fdb97eef5d5897c4a01e18`）。

## 实现说明

- registrationOrder：奢权 51202、典易 51203、武将 51204、渐专 51205、溺尊 51206
  （51200/51201 已被全琮 boundary-mobile 占用）；程序操作 `ResolveShiCaoShuang* = 63400..63404`；
  标记 `PlayerMarkerKind.ShiCaoShuangSheQuan = 33400`（显示名“奢权”）；运行时钩子优先级 67900；
  回合计数经 `UsesRoundTracking`（`ResolveShiCaoShuangSheQuan`）启用。
- 奢权：`afterDamageApplied`（subject any、`damageOccurrence: perDamage`、priority 100，
  在触发资格中按伤害来源/目标是否为曹爽过滤且伤害值大于 0）与 `dyingEntering`（subject any）
  三类锁定触发，每轮每窗口各限一次（`ShiCaoShuangSheQuanRoundTriggerEvent` 账本）。
  结算时令所有其他存活角色依次（逐个提示，AI 默认首张）选择一张手牌标记为“奢权”牌
  （按持有人计数的玩家标记，`PlayerMarkerChangedEvent` 同步）；实现为顺序提示而非原文的同时刻选择。
- 典易：同三类窗口的锁定触发（默认优先级，排在奢权之后），每轮每窗口各限一次；随机获得
  X 张“奢权”牌（X 为该窗口本轮发动次数，`ShiCaoShuangDianYiGainEvent` 账本），移入持有者
  手牌并解除标记；每次结算后若本轮以此法累计获得牌数大于体力上限，失去 1 点体力。
- 渐专：①“奢权”牌点数投影（`ShiCaoShuangSheQuanRank`：持有者有奢权视为 K(13)，其他角色视为
  A(1)），当前仅作规则元数据辅助，不参与比较结算。②本局首次【造成伤害后/受到伤害后/濒死阶段】
  时（priority -100，排在三技能之后）记 `ShiCaoShuangJianZhuanTimingEvent` 并发布
  “增加给典易/奢权”选择，为对应技能的每轮窗口上限 +1。③集满 3 个时机后发布
  “失去典易/奢权”选择；失去对应技能（禁用授予并记 `CharacterSkillsLostEvent`）后经
  `GrantProgramSkills` 获得“溺尊”，体力上限 +X（`MaximumHpChangedEvent`）并回复 X 点体力，
  记 `ShiCaoShuangJianZhuanAwakenedEvent`（含游戏性哈希）。
- 溺尊：①`turnEnding` 触发（`turnOwnerScope: anyLiving`，任意角色回合结束结算）：本回合
  曹爽使用过牌（`ShiCaoShuangOwnerCardUsedEvent` 账本）且弃牌堆存在“奢权”牌时，把弃牌堆中的
  “奢权”牌全部置于武将牌上（命名私有持久堆 `mobile-shi-cao-shuang.nizun`），记
  `ShiCaoShuangNiZunStashedEvent`。②出牌阶段激活（usesPerTurn 1，堆为空时不算合法动作）：
  选择一张置上的牌——实现为取回至手牌后由玩家正常使用（“如手牌般使用”的近似），记
  `ShiCaoShuangNiZunUsedEvent`；本轮每次使用使下一次受到的伤害 +X
  （`ProgramDamageHook(AdditiveAmount)`，按 `ShiCaoShuangNiZunPenaltyConsumedEvent` 账本在
  伤害落地时消耗一次）。
- 覆盖：`ShiCaoShuangChecks` 三项检查（含冷恢复与短命令重放）：标记与随机获得、溢出失去
  体力、三时机觉醒与上限回复；渐专觉醒路线与溺尊回收/取回/惩罚消耗；失去技能后各账本冻结
  与每轮门槛保持。
