# 孙策批：激昂红色杀摸牌与魂姿觉醒授技

状态：孙策（神话再临-山，2011年7月，吴4体力）单武将交付完成。schema62 / 最低规则181 / 经典包1.151.0 / Checkpoint3。本批为单对话交付，基于并行批次提交 82d39a92 之后的工作区，未覆盖其文件。

## 技能与官方口径

- 激昂：当你使用【决斗】或红色【杀】指定目标后，或成为【决斗】或红色【杀】的目标后，你可以摸一张牌。
- 魂姿：觉醒技，准备阶段，若你的体力值为1，你减1点体力上限，然后获得技能英姿（摸牌阶段，你可以额外摸一张牌）和英魂（准备阶段开始时，若你已受伤，你可以选择一项：1.令一名其他角色摸X张牌，然后其弃置一张牌；2.令一名其他角色摸一张牌，然后其弃置X张牌。（X为你已损失的体力值））。
- 制霸（主公技）：其他吴势力角色的出牌阶段限一次，该角色可以与你拼点（若你已觉醒，你可以拒绝此拼点），若其没赢，你可以获得拼点的两张牌。（本批未实现，见边界）

官方现行文本取自 sanguosha.cn 武将详情页（hero-detail-54）与官网武将库 gid 55，两轮核验一致。"指定目标后"以 cardUseBeforeTargetEffects 窗口（指定目标后、结算目标效果前）承载——cardUseTargetsFinalized 的 cardKinds 白名单只收杀系，决斗属锦囊牌须用前者。体力值：官方页不渲染，4勾玉取自两处独立二手资料（口径一致），如实标注。来源记录见 [sun-ce-2026-09-28](../content/sources/sun-ce-2026-09-28.json)。

## 公共能力与实现要点

- 新增触发条件 `cardActionCardIsRed`（事实字段 `CardActionCardIsRed`，值 21）：用牌动作实体牌全红花色（Heart/Diamond）为真；`CaptureProgramTriggerFacts(owner, action)` 统一捕获；解析器仅收用牌类窗口。与既有 `DamageCardIsRed`（伤害窗）及 AI 上下文 `CardUseIsRed` 口径一致。
- 激昂四触发器：`cardUseBeforeTargetEffects` × ownerRelation actor/target × cardKinds [duel]/[slash, fireSlash]，杀分支带红色条件，效果 `draw(owner, 1)`。
- 魂姿觉醒：`turnStartBeforeNormalFlow` + `compare(currentHp, equal, 1)`，`changeMaximumHp(-1)` → `grantSkills([classic:yingzi, classic:yinghun])`；非可选、usageScope game、usageLimit 1，与神司马懿授权连破同构。
- 授予零改动复用：classic:yingzi（周瑜英姿，draw-phase-skills bundle）与 classic:yinghun（孙坚英魂，owned-card-exchange-skills bundle）均为现行注册内容技能，结算语义与孙策官方授予版一致；英魂选择顺序沿用孙坚版（先选角色后选分支），与孙策文本表述顺序不同但结算等价，记录于来源 versionBoundary。

## 顺带修复（注册表池变化暴露的既有缺陷）

新增武将进入 CurrentGeneralIds 后，共享注册表测试的种子选将池变化，暴露两处既有问题；均以 HEAD 基线 worktree 复现归因（基线上两项通过、叠加本批内容后复现，Core 侧差异与本批内容注册无关）：

1. `AssertCoreInvariants` 两处伤害续接游标断言不认骑跨嵌套帧：御策 afterDamageApplied 效果移牌时，忍戒/连营的 cardsMoved 窗口按既有设计骑跨在待续接技能帧上，不变量误报游标丢失（烈弓 fixture 搜索中崩）。修复为 `DamageCursorEffectiveTop()` 走查（校验 Batch/HpChange 链接后向下），链接不成立的真正错序仍抛错。
2. `ConfiguredKujinDyingContinuation` 隐性依赖起手有桃：致死苦肉后救援询问仅在响应者有桃/酒/转化时同步暂停，池变化换种子后 14 张手牌无桃，`pending=none`。修复为 `SelectGeneral` 增加可选 `fixtureFilter`（发牌后评估），该测试要求起手有桃。

## 内容与验证

内容：`classic-sun-ce.rules.json`（激昂/魂姿，revision 1，最低规则181）、`classic-sun-ce.presentation.json`（官方逐字现行文本）；`StandardClassicGeneralPackage` 注册武将（faction wu、portraitKey sun_ce、BaseHp 4），版本 1.151.0，`CurrentGeneralIds` 追加 classic:sun-ce。规则版本 180 → 181（Replay.CurrentRulesVersion）。官方立绘（gid 55，经典形象105501，574×761）入 general-art-catalog，WPF 资产 `official-sun-ce.png`，图鉴神话再临·山组（myth-mountain，既有组）新增孙策；`tools/sync_general_art.py --phase verify` 全目录通过（91 武将 852 PNG）。

定向检查 `tests/CardGame.Core.Tests/SunCeChecks.cs`（4项，自然命令与真实决策应答）：

1. 定义与触发schema：吴4体力、身份池、激昂四触发器关键属性（窗口/归属/卡种/红色条件/效果）、魂姿觉醒属性（非可选、usageScope game、usageLimit 1、条件与效果）；schema拒收样例（cardActionCardIsRed 声明在 afterDamageApplied）。
2. 使用决斗摸牌并回放：提示处 Checkpoint 还原后事件与状态全等；激活后恰一张按技能原因入手。
3. 被指定目标摸牌且黑杀不摸：主公结束出牌后 AI 决斗/红杀指定主公触发目标分支，恰一张入手；使用黑杀断言无激昂绑定事件。
4. 魂姿觉醒并授予：体力1回合开始自动觉醒（主公上限5→4）、SkillAwakenedEvent 授予 [yingzi, yinghun]、回放全等；下一回合英魂提示可见、英姿提示激活后恰一张按原因入手。

验证结果：定向4/4通过。Release 全解构建0警告0错误。Core 全量 574/574 全绿（本批顺带修复的苦肉濒死/烈弓两项由失败转通过；中途在 HEAD 基线 worktree 见到的神司马懿定义检查失败为该 worktree 陈旧构建的假象，最终全量未复现）。WPF 过滤器验证：立绘断言的缺失名单仅含并行批次未交付立绘的4武将（classic:xu-sheng、boundary:xu-sheng、classic:zhang-xiu、ol:shen-guan-yu），sun-ce 不在名单；完整对局检查通过（5局93张牌、88次询问、命令可全程驱动）。

格式检查（本批新发现，如实记录）：全仓 `dotnet format --verify-no-changes` 报约 2741 条 WHITESPACE，覆盖大量本批未触碰的文件；在 HEAD（82d39a92）干净 worktree 复跑为 2749 条——即仓库在基线上本就从未全仓格式通过（无 .editorconfig、autocrlf 下工作区行尾混杂、历史漂移），既往批次的格式门槛实为范围化检查，全仓不过与本批无关。本批口径与执行：编辑过的既有文件逐文件报错条数与 HEAD 完全一致（零新增；行级差异经查均为工作区行尾状态差异，与内容无关），新文件 SunCeChecks.cs 的 10 条长行换行已按格式器自动修复并复验 0 条，修复后重建 Release 重跑定向 4/4 通过。

## 边界与未覆盖

- 主公技"制霸"未实现：需要拼点公共能力与"其他角色决策+主公技"族语义（若其没赢的"可获得"为孙策决策），与颂威同因；不在本批凑合。
- 体力值 4勾玉为二手资料口径；官方页不渲染体力，无法一手复核（与既往批次差异如实记录）。
- 英魂授予版的选择顺序沿用孙坚版实现（先角色后分支）；如需严格对齐孙策文本的"先选项后角色"，需选项前置的目标选择机制，另行立项。
- 多人拼点、连环下的激昂多次触发按每事件独立询问实现，未逐一专测。
