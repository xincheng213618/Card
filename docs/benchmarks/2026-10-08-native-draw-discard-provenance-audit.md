# 原生摸牌与真实弃置来源审计（2026-10-08）

R17 只读审计结论：现有获得牌事件不能精确替代一次真实摸牌；现有真实弃置分类漏识别三个已存在的支付生产者。本报告记录缺口与后续最小接入方案，不修改旧规则、版本或分类结果。羊祜仍作为完整候选保留，本轮不注册缺少卫戍的半成品。

依据为当前源码及 [官方来源队列](C:/Users/17917/Desktop/Card/docs/content/sources/jin-round16-candidates-2026-10-08.json:185) 中 gid497 / skill3084 的当前普通身份文本。队列保存了完整卫戍原文与客户端提示，但未提供一次摸多张、一次弃多张、跨持有者同技能来源排除的专项 FAQ。下文建议的调用粒度与来源范围是待显式采用的引擎映射，不能标成官方 FAQ。

## 真实摸牌入口与批次边界

[DrawCards](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.cs:16345) 循环调用 `DrawOne`；每张 `DrawOne` 都经 `MoveCard` 从 DrawPile 移至玩家 Hand。一次摸 N 张因此通常产生 N 个独立 movement batch。`CardsDrawn` 是日志，当前没有可作为规则凭证的通用 actual-draw fact。

[CompleteCardMovementBatch](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.CardMovementPrograms.cs:62) 冻结每个物理批次，并放入后继移动窗口队列。[MatchingMovementIndexes](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.CardMovementPrograms.cs:348) 的 `CardsGained` 依目的 Hand 判断获得牌；它也涵盖赠牌、取得其他角色牌及其他移入手牌的行为。只设置 `cardsGained/perBatch` 既会把普通获得误作摸牌，也可能将一次摸三张重复触发三次。

最小后续 hook 建议：仅在存在相关 capability 时，在 `DrawCards` 调用前冻结原父 frame、实际 turn/phase/phase actor 和直接 producer；调用结束记录一个 `NativeDrawReceipt`。记录调用 ID、请求数、实际卡牌与对应 movement sequence、实际数量、最后实际 batch ID、原父身份及 producer source。只在该 receipt 的最后 batch 发布一次相关候选，沿用 `CardsMovedTriggerWindowFrame` 与原 typed return；不改变已有各张 gain observer 的数量或顺序。实际为零没有“摸牌后”触发。该方案实施时还必须验证最后 batch 确实属于本次调用，不能按最新任意移动推断。

边界：

- [初始发牌](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.cs:3345) 也调用 `DrawCards`，必须依据 setup 状态及 `InitialDeal` 明确排除。
- [DrawOneToProcessing](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.cs:16384) 用于翻牌等原生流程，不是上述真实摸牌入口，不能因来自 DrawPile 就纳入。
- 牌堆耗尽、洗牌后实际不足请求数，均以本次 receipt 的实际实体为准；不能制造 requested 数量的假材料。
- 不要求摸得实体在稍后仍位于手牌；后继观察者移动它，不抹去已经发生的真实摸牌。

## 实际阶段回流与 producer-self

[CaptureMovementTiming](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.CardMovementPrograms.cs:8) 已冻结 actual turn owner、phase 与 phase actor，并对 `_programPhaseSchedule` 的插入 Draw 作专门处理。但该捕获目前按已有 capability 启用；新增能力必须纳入启用条件，不能默认所有 batch 已有 timing。

现有 [OutsideOwnerDraw 判定](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.CardMovementPrograms.cs:14) 为 `Phase != Draw || PhaseActorSeat != owner`。它允许“别人摸牌阶段你摸牌”，与“当前实际阶段不是 Draw”不同。卫戍未来选择何种文本映射须明确记录，不能默默复用名称相近的 gate。弃牌分支同理：应使用支付发生时冻结的真实阶段，而非稍后 observer 运行时的全局阶段。

插入阶段需覆盖 [ScheduleProgramPhase / ResumeScheduledProgramPhase](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.ProgramLifecycle.cs:211) 的进入与回流，以及 [DrawPhaseObligation](C:/Users/17917/Desktop/Card/src/CardGame.Core/DrawPhaseObligations.cs:99) 的真实摸牌与额外 Draw 子阶段。现有 [RestoreActualDiscardRecoveryPhase](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.ActualDiscardRecoveryPhases.cs:44) 展示了原阶段 token 的精确恢复路径；不能把新子阶段误认成被冻结的原阶段，也不能从子阶段返回后沿用其 phase actor。

现有 `ignoreOwnSkillMovements` 要求 batch 的 owner、skill 与 instance 三者均相同，且 batch origin 来自最近的祖先 ProgramSkillFrame（[捕获处](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.CardMovementPrograms.cs:42)，[过滤处](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.CardMovementPrograms.cs:362)）。它不能证明某次摸牌的直接生产者。

建议新增摸牌 producer 参数，由卫戍自己的 Draw1 显式传入。按稳定 owner+skill 排自己的同技能摸牌，避免重授 instance 变化造成递归；其他来源范围仍需明确文本映射。不要把卫戍祖先栈内由另一观察者实际产生的摸牌一并排除。弃置半句没有“非因本技能”，不得顺手复制摸牌排除条件。

## 真实弃置分类的现存漏项

[GetProgramDiscardSource](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.ProgramDiscardOrigins.cs:6) 能识别实际进入 DiscardPile 的原 owned HEJ 来源，并恢复合法 `owned → Processing → DiscardPile` 支付。其 reason 最终交给 [IsDiscardMovementReason](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.ProgramDiscardTopPlacement.cs:29)。对于 `skill-program.*`，该分类器在精确后缀/操作白名单后直接返回，未命中者不会继续走底部 `.discard` fallback。

| 真实生产者 | 实际 reason 与证据 | 当前分类结果 |
| --- | --- | --- |
| 三陈强制支付 | `skill-program.{skill}.draw-discard-category.discard`；[reason](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.DrawDiscardCategoryRefund.cs:7)、[实际支付](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.DrawDiscardCategoryRefund.cs:115) | 不在 skill-program 白名单，返回 false |
| 奸回来源弃牌 | `skill-program.{skill}.last-damage-source.discard`；[reason](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.LastDamageSourceReciprocity.cs:7)、[实际支付](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.LastDamageSourceReciprocity.cs:175) | 不在 skill-program 白名单，返回 false |
| 凶竖轮成本 | `program.phase-name-prediction.cost`；[reason](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.PhaseNamePrediction.cs:16)、[实际支付](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.PhaseNamePrediction.cs:97) | 不匹配精确名单或 discard 后缀，返回 false |

这些是现有真实支付未被通用弃置分类识别的问题，影响依赖该分类的候选；不只是尚未实现的卫戍。报告未调整旧结果，也未声称已有检查覆盖这些组合。

最小修复方向是利用各 family 已存在的 paid receipt / issued fact，在真实支付 commit 处登记或验证精确 `ActualDiscardMaterial`（batch、sequence、原 source、实际 destination、card、producer）。新分类只接受有该 typed 证据的原生支付；不要宽认任意含 `cost` 的 reason，也不要把所有手牌损失视为弃置。历史精确白名单与 Processing 来源兼容应保留。

必须保持目的地区分：GeneralWeapon 离开 Equipment 被规范化到 OutsideGame，未“置入弃牌堆”，不能触发此条件；木牛流马粮的附带清理由现分类明确排除，不能因同一父支付而一起算弃置。使用/打出后的清理、重铸、死亡清理及装备替换也不属于这一支付条件。

## 后继、隐私与最小回归

[BuildOtherOwnedCardDiscardChoices](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.ProgramOtherOwnedCardDiscards.cs:55) 可复用 HEJ 合法性、手牌盲牌位及外国装备弃置保护。现 `ChooseProgramOtherOwnedCardDiscard` 自带 decline，不适合直接作为锁定强制动作。新 family 应无 decline；无人有合法牌时不发布空强制选择。

owning receipt 需冻结原 movement window / candidate / producer、所选目标与实体、实际支付区间。支付发行后即使来源资格失效，也沿精确 typed child 返回结清，不重付。银狮卸下的原生恢复主体是被弃装备的原主人；[银狮与木牛处理](C:/Users/17917/Desktop/Card/src/CardGame.Core/GameEngine.cs:16911) 可能产生不同参与者及附带移动，须分别验证。不要为此宽认所有 HP/Dying/private 子树。新集合 fact 必须在 `CommittedEventProjection` 冻结；外国手牌提示不得带真实实体 ID，仅 chooser 获得私有提示。

建议最小 focused regression：

1. 一次真实摸三张只触发一次；普通赠牌/取得牌、翻牌、InitialDeal 与实际零张不触发。
2. 自己被本技能摸牌不递归；其他技能在其子树中真实摸牌可触发。重授 source instance 不改变已选定的 producer-self 映射。
3. 普通、插入及额外 Draw/Discard 阶段，及返回原阶段后，均按冻结真实 phase/actor 判断；明确检验“别人 Draw 阶段自己摸牌”的选定映射。
4. 三陈、奸回、凶竖真实弃置命中；使用/打出清理、重铸、死亡、替换及木牛粮清理不命中；GeneralWeapon 的 OutsideGame 不命中。
5. 混合 HEJ、Processing 完成来源、一次多牌弃置只按选定批次映射发布一次；选中但未实际移走不计。
6. 外国手牌盲选、装备保护、无人有合法牌跳过，以及银狮恢复/HP/Dying、木牛附带移动、支付后失源与 cold/checkpoint/replay；每个真实动作仅支付一次并恢复原父 cursor。

验证边界：本报告来自静态源码审查；未运行 build/test，未改生产分类、规则版本、schema 或注册。
