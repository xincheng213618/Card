# 张昭张纮（直谏/固政）单武将交付批记录

日期：2026-09-29。武将：经典张昭张纮（吴，男，3 体力，神话再临·山 2011；官方 gid 56）。技能：直谏（出牌阶段主动技）、固政（其他角色回合结束时机可选触发技）。前序：刘禅批（规则 186→187 / 经典包 1.156.0→1.157.0）；本批在隔离 worktree（batch/zhang-zhao-zhang-hong，基线 09608d9c）完成。本批规则 187→188（GameCheckpoint.CurrentRulesVersion）/ 经典包 1.157.0→1.158.0。

## 官方口径

- 直谏：出牌阶段，你可以将手牌中的一张装备牌置于一名其他角色的装备区里，摸一张牌。
- 固政：其他角色的弃牌阶段结束时，你可以令该角色获得弃牌堆里的其于此阶段内弃置的一张手牌，若如此做，你可以获得弃牌堆里的其余于此阶段内弃置的牌。
- 来源与体力口径见 [zhang-zhao-zhang-hong 来源记录](../content/sources/zhang-zhao-zhang-hong-2026-09-29.json)（体力值官方页不渲染，3 勾玉取 BWIKI 口径；BWIKI 直谏补充"不可替换原装备"细则，按正式角色卡核对落地为"目标须有匹配空闲装备槽、赠送永不替换已装备牌"）。

## 公共能力与实现要点

1. **直谏为 0 牌激活 + 装备赠送目的地**：
   - 激活 `gift-equipment`：minCards=maxCards=0、minTargets=maxTargets=1、targetKind=otherLiving、`usesPerTurn=null`（官方措辞无次数限制）、usesPerPhase/usesPerGame 同为 null。
   - **新激活字段 `targetRequiresEmptyEquipmentSlot`**（SkillProgramActivation 构造参+属性）：BuildProgramActions 目标过滤末端追加"目标存活 && 装备区未废除（`EquipmentAreaAbolished`）&& 持有者手牌存在一张可放入目标空闲槽位的装备牌"（新私有 helper `HasEmptyEquipmentSlotForOwnerHandEquipment`，槽位经 `EquipmentCatalog.Get(kind).Slot` 比对）。Parse 侧校验：该字段为 true 时必须恰好 1 目标（min=max=1）且 maxCards=0。
   - **新卡牌目的地 `SelectedTargetEquipment`**（SkillProgramCardDestination 第 7 值）：selectAndMoveOwnedCard 目的地白名单收编；targetRef 配对校验（SelectedTargetHand/SelectedTargetEquipment 必须带 `targetRef={kind:selectedTarget}`）；装备赠送硬约束 zones 恰为 [hand] 且 cardCategories 恰为 [equipment]。结算期双保险：`SelectAndMoveProgramOwnedCard` 校验目的地为异于支付者的存活角色；`BuildOwnedCardPaymentChoices` 只列出能进入目标空闲槽的手牌装备（同槽占用即不可选）；`ResolveSelectAndMoveOwnedCardChoice` 在选牌后再验槽（期间被第三方装上同槽等竞态）→ 不可用即 `CancelProgramBindingAndCleanup`（"目标装备槽不可用，技能结算已取消。"），后置摸牌随之不结算。
   - 移牌成功后补发 `EquipmentChangedEvent(ReplacedCardId: null)` + EquipmentChanged 日志（对齐 `CompleteEquipmentUse` 三连）；移动→摸牌顺序，官方"置于…摸一张牌"。取消路径=无摸牌。
   - AI 估值：Owner→SelectedTargetEquipment 分支计入 _ownerDraw（-1 手牌 +1 摸牌）与 +2 otherAdjustment（资攻）。
2. **固政为回合内弃牌账本 + 新绑定原语 `bindDiscardPhaseDiscards`**：
   - **弃牌阶段手牌账本**（GameEngine 私有状态 `_discardPhaseHandDiscardIds` + `_discardPhaseHandDiscardTurnNumber`）：MoveCard/MoveCards/MoveAllCards 三处移动出口统一挂钩 `CollectDiscardPhaseHandDiscard`，仅在 `_phase==Discard && from=当前回合主人的手牌 && to=弃牌堆` 时记账（**语义边界：记录该阶段内全部手牌→弃牌堆移动，不区分弃牌原因**——弃牌阶段内进入弃牌堆的手牌只有弃牌流程一条常规路径，含超限弃置与结算弃置，取官方"于此阶段内弃置"的字面全集）。BeginDiscardPhase 重置账本；turn-number 守卫使死亡中断的回合无法向后继回合边界泄露陈旧池。账本为纯派生态，经命令日志重放自然恢复，checkpoint schema 零变更。
   - **新公共触发值 `turnOwnerDiscardPhaseHandDiscardCount`**（SkillProgramTriggerValueKind=26，SkillProgramTriggerFacts 新增可选参，默认 0）：CaptureProgramTriggerFacts 冻结 + ProgramAiPublicContext 注入（AI 条件评估同源）。
   - **新原语 `bindDiscardPhaseDiscards`**（描述符/handler/ISkillProgramEffectHost 成员/GameEngine host/引擎实现全链）：引擎侧校验活动帧+turnEnding 窗，将账本 id 过滤"仍居弃牌堆"（对齐行殇 `ClaimProgramDeathCleanupCards` 先例，中途离堆的牌不可领）后 `SetProgramCardSet(Public)`。**候选截断边界：`CardSubsetSelector.MaximumCandidateCount=8`——子集选择原语最多枚举 8 张候选，账本按弃置顺序截取前 8 张**（超 8 张的极端弃牌阶段，第 9 张起本回合不可被固政领取）。
   - **MoveBoundCards 条件规则放宽**：ChoiceIs 门控目的地在 DiscardPile 之外新增 OwnerHand（"命名选择的弃置或收ownhand 获得"分支，注释注明固政用途）；消息同步改为 discard or gain branch。
   - 组合目录夹具：ProgramCompositionDefinitionChecks nodes 表补 `[BindDiscardPhaseDiscards]`（SetEquals 全 op 覆盖不变式维持）；执行器分派检查补 `bind-discard-phase` 调用断言（fake host 记账 Continue）。
3. **固政程序形态**（classic:guzheng 单触发六效果）：`turnEnding` + subject owner + **turnOwnerScope=otherLiving** + optional、无 usage 上限（官方"每当"族语义，条件已 gate 空池）；条件 `compare(turnOwnerDiscardPhaseHandDiscardCount > 0)`；效果链：bindDiscardPhaseDiscards(pool) → selectTarget(eventSource 绑定结束回合的回合主人) → selectCardSubset(pool→returned，1..1，maximumRankSum=208/aiOrder=mostCardsThenRankSum) → moveBoundCards(returned→selectedTargetHand，还给回合主人) → chooseOption(take-rest/decline，presentation 补 optionLabels) → moveBoundCards(pool exceptBind returned→ownerHand，choiceIs(take-rest) 门控)。子集空余=空集移动 no-op（既有 Continue 短路）；池竞态为空=干净取消。
4. **版本面**：Replay.CurrentRulesVersion 187→188（epoch 注释：selectedTargetEquipment 目的地、空槽激活过滤、回合主人弃牌阶段手牌事实、命名选择获得分支）；经典包 CurrentVersion 1.158.0，嵌入资源 const + ClassicZhangZhaoZhangHongCatalog 惰性目录 + ZhangZhaoZhangHongProgram 工厂；技能注册块（直谏 WithActiveActionMetadata、固政 WithOptionalTriggerMetadata）；AddGeneral（classic:zhang-zhao-zhang-hong，"张昭张纮"，zhang_zhao_zhang_hong，wu，3 体力，主技能 classic:zhijian，AdditionalSkillIds=[guzheng]）；CurrentGeneralIds 追加（进入 identity 共享武将池）。官方立绘（gid 56，经典形象 105601，574×761）经 `--phase classic --key` 下载为 `src/CardGame.Wpf/Assets/official-zhang-zhao-zhang-hong.png` 并入 general-art-catalog；图鉴 myth-mountain 组追加；tools/sync_general_art.py CLASSIC_HEROES 补 zhang-zhao-zhang-hong:56；来源记录合并下载完成态（localPath/sha256/尺寸 + versionBoundary 结案说明）。

## 内容与验证

内容：`classic-zhang-zhao-zhang-hong.rules.json`（两技能 revision 1、最低规则 188、schemaVersion 62）+ `classic-zhang-zhao-zhang-hong.presentation.json`（schemaVersion 3，两技能官方逐字文本；固政补 optionLabels）。

定向检查 `tests/CardGame.Core.Tests/ZhangZhaoZhangHongChecks.cs`（6 项，真实命令与真实决策应答，注册于 Program.cs 刘禅之后；另在既有执行器检查内新增 bindDiscardPhaseDiscards 分派断言）：

1. 定义与技能 schema：吴 3 体力、身份双池；直谏 0 牌无限次激活形态 + 空槽过滤字段 + 装备目的地效果链；固政触发形态（otherLiving、可选、count>0 条件、六效果顺序、except 补集移动 choiceIs 门控）；schema 拒收样例（空槽过滤配多目标、zones 含 equipment、categories 非 equipment、缺 targetRef、其余牌获得分支改 wounded 条件、bindDiscardPhaseDiscards 未知键/非 owner 目标）。
2. 直谏手牌装备置入他人空槽并摸一张（含 EquipmentChangedEvent(ReplacedCardId=null)、技能理由摸牌、暂停决策点回放全等）。
3. 直谏占用槽位目标被剔除、无装备手牌不出技能（双负样）。
4. 固政取回一张并可选获得其余（take-rest 支，账本=回合主人本阶段手牌弃置全集，回放自触发提示点全等）。
5. 固政 decline 支其余留弃牌堆（回放全等）。
6. 固政对自身回合结束与空弃牌池保持沉默（负样：自己弃牌阶段有弃置也不开窗；首个未受伤银行回合无弃置不开窗）。
7. 执行器/组合目录：bindDiscardPhaseDiscards 分派（SkillProgramExecutorChecks）+ 组合夹具全集（ProgramCompositionDefinitionChecks）。

## 门禁（全部真实执行）

- Release 全解构建：0 警告 0 错误（--artifacts-path 独立目录 Card-zzz-artifacts）。
- 定向：Zhang Zhao Zhang Hong 6/6 全绿；执行器分派（active Program primitives dispatch）全绿。
- 邻接过滤（--filter 为子串匹配，逐一单独跑）：Liu Shan 9/9、Dong Zhuo 11/11、Jiang Wei 6/6、Lu Su 5/5 全绿。
- 组合目录 15/15、技能执行器 5/5 全绿。
- 全量 Core：**623/623 全绿**（0 跳过；基线 617 + 本批 6 项新增）。批内首两轮全量 621/623，2 失败均为本批入池漂移与夹具接线，无语义回归：
  1. **执行器分派夹具**（本批新断言自身接线）：bindDiscardPhaseDiscards 单操作样例被组合校验器以"revealed card binding 'guzhengPool' is not fully consumed"拒绝——NeedsCleanup 卡集必须被静态消费是既有不变式，本样例缺消费者。修复：样例补 `moveBoundCards(guzhengPool→discardPile)` 消费者（引擎真实固政链即"绑定→移动"消费，语义一致），断言未弱化。
  2. **经典典韦强袭夹具（FindDianWeiQiangxiFixture）**：张昭张纮入共享池改变候选枚举 RNG，扫描选中全新牌桌使 `classic:xiao-qiao[天香]` 成为强袭目标——伤害转移让"目标掉 1 体力"不变式落空（诊断实录 hpT=3/3）。修复照种子试验化排除谓词模式：damageTriggerSkills 排除集补 `classic:tianxiang`（诊断出的实际干扰技能，非照抄相邻批排除集），断言与夹具目标不变。
- 格式：本批新增/触碰文件 dotnet format whitespace 0 条（18 个 .cs 已 verify-no-changes；ClassicGeneralChecks.cs 存在基线预存的 3 条 format 违规于 5364-5366 行（黄月英集智注释换行），非本批引入，未修）。
- 立绘：新条目离线校验通过（key zhang-zhao-zhang-hong，105601，574×761，sha256 与本地 PNG 一致）。**预存问题（与本批无关、未修）**：① 全量 `--phase verify` 在预存条目 zhou-yu/201203 上报哈希漂移（HEAD 目录记录 3741da…，仓库内 PNG 实为 d3bd4a…，基线即如此）；② `--phase finalize` 在预存 `NEW_OL_DEFAULTS` NameError 上崩溃（基线即如此，任务口径明示不修）。

## 批次边界与语义裁决

- **固政账本语义**：官方"其于此阶段内弃置的手牌"落地为"弃牌阶段内、当前回合主人手牌→弃牌堆的全部移动"，不区分弃牌原因/理由串（该阶段内手牌进弃牌堆的常规路径只有弃牌流程）；阶段与回合双重作用域（BeginDiscardPhase 重置 + turn-number 守卫）保证跨回合不泄露、死亡中断回合不给后继边界留陈旧池。
- **8 张截断**：子集选择原语枚举上限 8（引擎既有约束，li-dian 星徐盛 4 张、行殇全取不选牌均在限内）；固政以弃置顺序截前 8 张并在引擎注释与来源记录中登记边界。剩余牌本回合不可领，次回合新账本重新开始。
- **selectTarget(eventSource) 的单选提示**：既有 selectTarget 原语无论 targetKind 一律出选择提示（董卓暴虐同型）；人类张昭张纮固政会先看到"选择目标"单选（回合主人）再选牌，与既有引擎行为一致，未新增静默绑定机制。
- **直谏摸牌时点**：置于装备区成功后才摸牌；选牌取消/槽位竞态取消均不摸牌，无部分结算。
- 直谏 usesPerTurn=null 依据官方措辞无次数限制；激活过滤与选牌双重空槽校验（激活期+结算期）覆盖选牌间隙的装备变动。
