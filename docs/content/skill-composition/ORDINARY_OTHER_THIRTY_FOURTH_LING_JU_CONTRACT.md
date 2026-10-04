# 普通 OL 灵雎：完整静态实施稿

本稿实施当前官方 gid74 的全部竭缘、焚心，身份为 `ol:ling-ju`、VariantId=ordinary、RulesetId=sanguosha-ol。来源是 `docs/content/sources/ordinary-other-thirty-third-74-source-2026-10-04.json`，raw SHA256 `77409ab6db370f94fa2be0c2f679cfb4829919d571a833424e325c8d3f567413`。当前网页两技能完整，无未展开派生字体。API 群/3；API 没有 gender 字段，Female 是 biography 明示“女子”的工程判断，initial_hp=0 是占位，不作真实初始体力。

这是源码、窄接线与真实命令检查**草稿**，尚未编译、production loader、运行检查、native 游戏、UI 或 benchmark。原合同被冻结未改；本稿按随后批准的边界修正了其中“已死显式来源仍可未付款”的旧草案：当前只有明确来源和目标都存活才产生/支付未付款候选。

## 7100 声明式能力

唯一新增 `DiscardOwnedCardToAdjustCurrentDamage=7100`，7101 未使用。仅接受独立 BeforeDamageApplied、target=owner、amount=+1/-1，与 DamageSource/DamageTarget subject 精确对应。+1 基础 suits=Spade/Club，-1 基础 suits=Heart/Diamond；其他数值被新 descriptor 拒绝，旧 Amount 合同不放宽。

`PublicDeathDamageCost` 引用一个必须由 registry 实际登记的 qualifier skill，三种 public-dead-role 集合声明 HP、颜色、装备放宽。普通内容映射：亡反贼放宽增伤 HP，亡忠臣放宽减伤 HP，亡内奸两方向颜色任意且允许装备。只读有效 qualifier grant 与 Identity 模式真实已死且 RoleRevealed 的公开角色；不缓存永久解锁，不按人次叠加，不把国战占位 Role 当亡内奸。当前焚心登记为完整 Locked/State 技能，其实际能力是该明确引用的费用放宽，不是空程序占位。

## 精确拥有帧与真实费用

原 ProgramSkillFrame 上的 scalar receipt 锁 instruction、当前 BeforeDamage frame、原 attack owning frame、nature/source/recipient、原金额/方向、双方当下 HP、public role mask、qualifier instance 和冻结费用政策。首次 private cost prompt 只有付款者可见真实候选手牌 ID；公开 offered fact 不含候选/手牌 ID。选择前复验双方存活、owner exact skill instance、当前政策与实体位置/有效花色，失效取消未付款绑定，原伤害继续。

Stage=Paid 与 PendingMovementContinuation 在真实 MoveCard(Hand/Equipment→DiscardPile) **之前**写入；callback 记录一次实际 ledger sequence，再公开 cost 标量事实。标准 reason 为 `skill-program.{skillId}.DiscardOwnedCardToAdjustCurrentDamage`；现 `IsProgramDiscardOriginReason` 委托的 `IsDiscardMovementReason` 只新增这个准确 op，普通使用/重铸/Processing cleanup 不变成弃置。

费用只含 Hand，亡内奸允许 Equipment；沿成熟过滤排除 generated weapon、活动能力来源装备与真正 discard 禁止。颜色用付款实体 owner 的有效 Suit，不用任意转换结果伪装费用。

已付款后依成熟顺序先 drain RecoveryReplacement、CharacterState、HP、CardsMoved。首孩子锁确切单实体 ledger、Parent、origin skill/instance；movement AwaitingProgramFrameId 只允许 null 或 exact root。SilverLion HP/replacement 还锁原 Equipment→Discard、Amount1、source/recipient、producer/reason/typed AwaitedProgramMovement。此后只沿 `HalfHandPaidDamageObserverEdge` 及已有完整救援 suffix 证明连续子树，保留原 BeforeDamage/attack；不遮掉 ActiveDying，不放宽旧全局 guards。

Runtime 精确已付子树优先于旧原伤害续行；真正 Program AttackAttempt 子帧必须调用成熟 CompleteDamageAttack，不能让 executor 跑过待完成的 attack。RecoveryReplacement 保持其既有更早 dispatch。原外层 attack、BeforeDamage 和 Damage 游标只增加原 receipt+ledger+完整子树局部 union。付款后 source 失技或死亡仍完成一次冻结调整；目标死亡或 winner 已定则保留费用、drain 已有孩子、取消新调整并正常返回原 producer。没有补退款或重新付款。

全孩子返回后同时改变该原窗口和原 attack 的当前 recipient 金额；减至零标记 Prevented，不造 DamageApplied/HP 事件。Stage=Adjusted 与实际 fact 先落地，随后原 executor/typed window return 收束一次。nullable receipt 不建立旁路 pending 或 use-ID 侧表。

## 连环两种实际 producer

仅 registry 注册新能力时启用 recipient scope。`CaptureRecipientScopedDamageBase` 在成熟 ApplyAttackDamage 已冻结首个正实际 elemental amount 且确定真实传播名单后、SetChainedTargets 前签发基数，分别存在 CardAttackState / AttackAttemptState。首目标减至零不走传播。CardAttackHandle 与 ProgramAttackHandle 在成熟 TryAdvanceChainedTarget 修改 target 后、进入新 target armor/modifier 前恢复同一基数；本 recipient 的 ±1 不污染后面的 recipient。真实 RedirectCurrentProgramAttack 更新当前 scope 并由已有 ProgramDamageTransferredEvent 证明。aggregate DamageWasApplied/CausedDamage 从不清空。

后续 chain BeforeDamage 只为新 op 放开本窗口候选；不改变旧 Increase 的整个 attack 已造成伤害 guard，不改无新cap registry 的事件、窗口或金额路径。content hash/可选字段区分本能力；全局 rules/schema/package 常量未提升，不声称含新增成员的文件原始 hash 不变。

## 暴露与登记

所有新 event 均为标量（ID/seat/int/bool/Suit/CardLocation/string），无集合或嵌套政策 payload，因此无需增加 CommittedEventProjection case。definition 三个角色集合显式 readonly backing，init 克隆；with/JSON 也经过该 setter。private PromptChoices 沿既有 commit 准备冻结与 CreateSnapshot(viewerSeat) 隐藏边界。

四 Core 方法与一个 routine 前缀见 registration-baselines.json；不新增 runner/UI 检查框架。GeneralArt 现有 ol:→ol- normalization 可复用，portrait key=ol-ling-ju，图库明确 other；PNG 已单次取得但未安装主区。art catalog 交付单个 additive entry，root 与另外两份合并。实现、baseline、preview、注册建议和支持证据分别哈希；禁止包含他人 PrepDiscardReceipts dirty。
