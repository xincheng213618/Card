# 日常验证耗时只读审查

状态：仅源码审查与方案，不含性能补丁、构建、测试或基准。用户要求休息期间停止 CPU 重验证，因此建议等当前第 24 批验收范围稳定后再实施、统一测量。本审查未修改 src、tests、tools 或注册范围。

## 证据范围

计时取自 `docs/benchmarks/2026-10-04-routine-current-profile.json`，这是第 23 批最终输入的实际日志剖析，启动于 `2026-10-04T05:42:12.4739892+08:00`。含构建 95.776 秒；Core 231 项合计 73.385 秒，WPF 18 项合计 18.613 秒。该输入摘要不是当前第 24 批的性能结果，不能据此声称第 24 批耗时或任何优化收益。

|现有检查|实际历史计时|方法|程序登记|routine 登记|
|---|---:|---|---|---|
|Unrespondable counterspell equipment payment completed cold|7.530993 秒|`BoundaryWolongZhugeLiangChecks.UnrespondableCounterspellEquipmentPaymentAndCompletedCold`|Program.cs:45|VerificationScopes.psd1:132|
|Damage judgment suit payment four suits and exact claims|5.371106 秒|`BoundaryCaiWenJiChecks.DamageJudgmentBeforePaymentFourSuitsAndExactClaims`|Program.cs:40|VerificationScopes.psd1:10|
|Discarded provenance real turn all materials no response|3.926948 秒|`BoundaryCaoZhiChecks.ProvenanceSurvivesRealTurnsAndAllMaterialUsesFreezeNoResponse`|Program.cs:53|VerificationScopes.psd1:135|

三项合计 16.829047 秒，占历史 Core 计时约 22.932%。源码与登记基线的 SHA、静态调用计数及未测边界见 `evidence.json`。这些是静态审阅数据，尚没有逐段 CPU/分配量计时。

## 优先方案：合并无关体力前置，不碰真实救援

无懈方法保留七个不同真实场景：装备支付、实物无懈、原生 AI 装备支付、青弦随机装备、涅槃、酒诗零实体酒、醇醪实体酒。每条生产链都有独立关键边界，不能通过移出 routine、合并支付产物或删去冷恢复缩减覆盖。

`BoundaryWolongZhugeLiangChecks.cs:225–230` 为涅槃场景确认目标初始 HP8、无装备/手牌，然后七次 `hurt-other` 失 HP1。`:337–339` 在 virtual/bound 两场景重复同一前置。此三条前置仅建立 HP1；真正需要覆盖的最后 HP1→0、Dying、救援付款和子帧都在后面的实际青弦/无懈链中。

最小修改提案：在现有 fixture driver 增加仅供这三个场景的 `prepare-one-hp` activation，复用 `loseHp`、`target:selectedTarget`、`amount:7`。每个场景以一次真实 `UseProgramSkillCommand` 加同样的 `ReachPlay` 替换七次 HP1 命令。现有 `hurt-other` amount1 保持原样，原生 AI 装备支付场景仍通过它制造受伤。

保留的断言：原始 HP8、目标没有装备/救援材料、真实降至 HP1、涅槃未提前消费、青弦真正造成 HP1→0、exact Dying/token/父帧、每类救援的实体/零实体事实、真实付款一次、所有冷恢复、四视角隐私和原无懈尾部。仅把 “Seven real loss commands” 诊断文字改为 “A real seven-HP loss”；不删除或弱化对应条件。

现有 `LoseHpProgramOperationDescriptor` 使用 `Amount(reader,20)`，7 在现行 loader 合同内（`ProgramOperationDefinitions.cs:531–535`）。三个目标在此阶段所配技能是 quiet，以及对应 Niepan/Jiushi/HP recovered/装备观察；这些前置既不受伤也不回复，未覆盖逐点失血行为。合并不涉及生产规则或新 opcode。

静态可确定少 18 条 live `UseProgramSkillCommand`（3×(7−1)）。其后分别有 5/5/6 次冷恢复，所以还能少重放 96 条相同前置 `UseProgramSkillCommand`（6×16）。bound 分支真实存醇的更早一次冷恢复不计入此数字。`GameReplay.Restore` 确实重放完整 checkpoint.Commands（Replay.cs:175–178）；这里没有按 checkpoint 当前帧直接跳过历史。Reach 引起的附加 Advance 数量和节省秒数尚未测，不推算。

不建议改为直接 InitialHp1：现有测试明确断言初始 HP8，也通过真实命令建立 HP1。一次 loseHp7 能保留这些前提，更窄且不用 HOST 写入。

## 三项共同方案：复用同一命令边界内已准备的 prompt

三文件的 P helper 都逐座调用 `CreateSnapshot(seat).PendingDecision`，直到取得非空 prompt；没有 prompt 时需要四个快照，有 prompt 时需要 1–4 个快照。它是惰性枚举，不能误报为每次固定生成四份。

当前 Reach 已取 P 检查停止条件，失败后 Step/Advance 又取同一状态的 P；随后 Answer/Continue 还可能再取一次。具体位置：Wolong :479、494–506；Cai :186、199–208；Cao :136、149–161。

最小提案：只在测试助手内部，把本次循环取得的 `PendingDecision?` 传给 `Step/Advance`，并让同一步的 Answer/Continue 使用该已准备 prompt。未经过 Submit 的同一边界可复用；每次真实 Submit 后重新取得，不跨 Revision 缓存。仍从 `CreateSnapshot(viewerSeat)` 取决策，保留 CommandJson round-trip、真实 command 接受/拒绝、既有循环上限与停止条件，以及全部 Private/Frozen/Cold 断言。

**不能直接改为 `game.PendingDecision`。** `GameEngine.cs:527–529` 的 State 是 HumanSeat 视图，该属性依赖 State，会漏掉这些检查中 AI participant 私有选择的暂停。也不以诊断帧私有集合直接代替公开选择来提交命令。

这是重复视图投影的精简，并未证明投影占了这三项的多少时间。暂不引入通用测试框架，也不修改生产 snapshot 或 command 边界。

## 冷恢复保留，消除紧邻重复 State 投影

三项静态展开各分支后分别调用 29、29、11 次 `Cold`。这些调用验证不同真正 owning 状态，全部保留。需要优化的是相同边界上重复构造源引擎的四视角完整 State，而非减少恢复或只比较帧。

提案一：`Cold` 在完成现有完整 restore 对比后返回其已序列化的源 State；紧邻且没有实际命令/写入的 `Reject` 使用这个不可变字符串作为 before，仍提交原非法 Answer、断言拒绝，并生成完整 after State 作相等比较。

适用位点：Cai 每花色三处 `Cold;Reject`（:27、36、43），共 12 处；Cao :68、75、85，共 3 处。仅源 State 少生成 15 次，等价于少做 60 次 CreateSnapshot 投影，以及同次数的完整帧/事件/命令/移动序列化。非法 mixed-material 提交的单独 State 前后比较（Cao :63–64）原样保留。

提案二：Wolong 的 9 处 `PrivateAndFrozen;Cold`，前者本来已经完整 State before/after 相等（:514、531）。它可返回已验证不变的 before 字符串，交紧邻 Cold 作源 State；仍保留两次完整 mutation-probe State、每视角隐藏手牌/私有输入、所有 nested collection mutation probes，以及完整 replay State 对比。少 9 次源 State，即 36 个快照投影；29 次 Restore 一个不少。

两项合计静态少生成 24 次完整源 State（96 个快照投影）；不把此数换算成秒。只能复用邻接且未变更状态的局部字符串，不跨真实命令、拒绝后、修改检查点或 HOST probe 共享缓存。

## 曹植第二优先方案：减少额外拒绝的落英候选

当前方法的两局都必须保持四人固定 roster、distance2、真实回合和自然翻回正面：`BoundaryCaoZhiChecks.cs:55–60`、`:78–86`。不能以 setFaceState 或跳到后续回合代替 natural face-up/skipTurn（GameEngine.cs:3377–3385），也不能改为三人局而失去 distance2 反例。

现有 quiet fixture 每个 AI `afterNormalDraw` 真实弃两张 Club，再跳过 Play（Cao :208）。在本方法中，主局仅领取三枚 tag，声明局仅领取一枚；后续实际落英候选被通用 Step decline。可增加仅此方法使用的 fixture 参数，例如 `quietDiscardAmount:1`，让这两局每个 AI 每次真实弃一张，其他方法维持 amount2。

主局仍从三个真实其他角色获取三枚实际 discarded Club，保留 per-card origin、gain child、四视角 Cold、跨回合持有、自然翻面、两实体全 tag 远距 Slash、混合材料原子拒绝、转换无懈、声明 Processing 支付一次和真实 no-response。声明局仍取得一枚实际 tag。已知两次完整 AI 圈的 quiet 弃置实体从每局 12 枚降到 6 枚，但实际命令、窗与耗时应由后续 targeted 计数核验，不能宣称净快了一半。

这比合并三枚 tag 一次原子获得更稳妥：保留三次真实 per-card Luoying 领取及三个原 `ClaimOne` 冷恢复。需要实测确认目标仍持有可用 Dodge 材料、原始 untagged 材料存在、source/declaration eligibility 不变；没有 seed 搜索、HOST 注入、scope 变更或 runner 新增。

## 蔡文姬不再重复缩减收益夹具

四花色各自包含实际 Slash→先判定→实际 HE 费用→收益/认领→原伤害 cleanup，Club 两张弃置是两个实际支付点。不能共享四花色一局来复用 paid/usage/history，不可只测最终认领而删除 Final/Movement/HP/gain/flip 子窗。

第五局必须保留首枚真实 claim→gain loseHp→DyingEntering→实物 Peach→HP observer→第二 claim 的原序与所有冷恢复。profile 已记录此局 draw 已从 20+20 改为 2+2；本审查不再把已实施改动记为新建议（Cai :72、fixture分支）。当前最窄提案只做前述 prompt/State 重用，保留五局与 29 次 Cold。

本次不建议缩短物理牌组：相同 deck fixture 被其他登记方法用于 draws、不同 rank/claim 或 native 场景，统一减牌可能改变洗牌、抽取和耗尽回收边界。若后续证明 deck/projection 是主要开销，应先计算每个实际夹具的实体预算，再单独参数化，不能凭总牌数做硬缩。

## 后续实施与验证边界

建议顺序：先合并 Wolong 三条无关 HP 前置，再做邻接 State/prompt 重用，最后考虑曹植 quietDiscardAmount1。每个修改都独立保存 before/after 原检查结果及 accepted-prefix/Cold 次数；待用户允许统一测试时再执行现有 name filters、完整实际 routine，并按实际日志报告秒数。

保持现有三 method 的全部登记、scope、七/五/两种场景和 29/29/11 Cold 次数。不得删除四视角、非法选择原子拒绝、CommandJson round-trip、实物成本一次、damage/judgment/history/source/typed-parent 断言，不新增人物 definition snapshot 或专属 runner。24 批当前未验收状态与 23 批既有通过结果仍分别记录，不因本只读审查扩大验收结论。
