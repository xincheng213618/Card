# 贾诩（完杀/乱武/帷幕）单武将交付批记录

日期：2026-09-30。武将：经典贾诩（群，男，3 体力，神话再临·林 2011；官方 gid 49）。技能：完杀（锁定技）、乱武（限定技，出牌阶段）、帷幕（锁定技）。前序：张昭张纮批（规则 188/经典包 1.158.0，本批基线 4967d45 即其并入提交）。本批规则 188→189（Replay.CurrentRulesVersion）/ 经典包 1.158.0→1.159.0；SkillProgramCatalog.RulesSchemaVersion 保持 62（cardCategories/suits 为可加可选节点）。

## 官方口径

- 完杀：锁定技，不处于濒死状态的其他角色于你的回合内不能使用【桃】。
- 乱武：限定技，出牌阶段，你可以选择所有其他角色，这些角色各需对距离最近的另一名角色使用一张【杀】，否则失去1点体力。
- 帷幕：锁定技，你不能被选择为黑色锦囊牌的目标。
- 体力/势力/性别/包归属取 BWIKI 二手核对：群、男、经典版 3 勾玉、神话再临·林；图鉴入 myth-forest 组。来源与口径见 [jia-xu 来源记录](../content/sources/jia-xu-2026-09-29.json)（已合并官方立绘完成态：gid 49，经典形象 104901，574×761，sha256 bd512f2c…，localPath src/CardGame.Wpf/Assets/official-jia-xu.png）。

## 公共能力与实现要点

1. **完杀 = 濒死救援资格策略（零新增原语面）**：新卡牌策略 `SkillProgramCardPolicyKind.ProhibitDyingPeachByOthers = 18`，解析约束"恰好 [peach]、无其他过滤器"。引擎门 `GameEngine.GetDyingPeaches` 头部 `IsDyingPeachProhibitedFor(responder)`：`_pendingDying` 存在且 responder ≠ VictimSeat 且当前回合座有效时，查询**当前回合座**（`_players[_currentSeat]`）的该策略——回合归属归策略提供者（贾诩本人回合内全场除濒死者外均禁桃，含贾诩自己；濒死者自救不受限；`GetDyingAlcohols` 不受限，酒不在官方口径内）。对物理桃与全部 view-as 桃转化候选统一生效（GetDyingPeaches 整体返回空）。
2. **乱武 = 新共享原语 `requestNearestSlash`（审计建议的 op 内闭环）**：`SkillProgramEffectOp.RequestNearestSlash`（枚举唯一值，executor 接口 `ISkillProgramEffectHost.RequestNearestSlash(frame)` + `GameEngine.SkillProgramHost` 分派 + 引擎实现 + 执行器 FakeRuntime 记录 `nearest-slash-request:{ownerSeat}` 全链完整）。描述符 Parse 仅允许 op/target/condition，target 必须恰为 owner（响应者由 op 内部派生，`Resources => []`，AI 估值空 apply，沿 RequestAttackRangeAid 先例）。宿主 `GameEngine.ProgramNearestSlash.cs`：光标记录 `ProgramNearestSlashRequest(ResponderSeats, ResponderIndex)` 于 ProgramSkillFrame（进暂停前先提交光标，子结算经 `ContinueProgramNearestSlashIfResumable` 钩子续入循环，沿 attack-range-aid 模板）；响应者=激活时全部其他存活角色（seat 升序）；无杀在手或无距离最近目标 → 记账事件后直接失去 1 点体力（`ProgramSkillHpLostEvent`，体力归零走 `BeginProgramSkillDying` 程序技濒死续体）；否则对每张手杀×距离最近目标发布私有 ProgramTrigger 提示（`request-nearest-slash` / `request-nearest-slash-decline`），decline 同样失 1 体力。用杀结算 `ResolveSlashCore(..., countsTowardSlashLimit: false, programSkillCardUseFrameId: frame.Id)`，攻击链完成后回到循环（不经 ContinueProgramSkill）。**选择变体走共享 `CreateConversionChoiceVariants`**：带卡牌身份的手杀必须经其身份转化源使用（引擎从不提供身份卡的"素用"路径），选择参数携带 conversion-* 参数，结算回读 `TryReadConversionSource` 后以 `_selectedUseConversion` 发布所选变体（素用=显式发布原生选择）。答案以新事件 `ProgramNearestSlashAnsweredEvent`（含 TargetSeat/SlashCardId/LostHp）记账，不走 ChoiceBindings（断言器 producer 校验要求 chooser=owner，与逐响应者座次冲突，如实在此记录取舍）。AI 应答确定性取首个用杀选项（最低卡 id×最近座升序）。
3. **距离最近目标 = 新共享目标种类 `otherLivingNearest`（值 28）**：`GetProgramTargetSeats` 早臂——存活其他角色按 `GetCombatDistance(ownerSeat, target)` 取最小距离集合（并列全取，seat 升序）。供乱武循环与未来内容复用。
4. **帷幕 = 目标禁止策略加挂类别/花色过滤器（schema 62 増量可选节点）**：`SkillProgramCardPolicy` 增 `CardCategories`/`Suits` 只读属性（解析仅接受 `ProhibitTarget` 且要求"类别或花色至少其一"，无类别过滤的目标禁止仍必须给 kinds）。结算门两处：①`BuildLegalActions` 尾部目标过滤追加 `IsSuitFilteredCardTargetProhibited(target, effectiveKind, physicalSuit)`（物理牌取 `EffectiveSuit(actor, physical)`，转化候选无单一物理花色时 fail-open 不匹配花色过滤器）——该尾部同时是命令边界校验（伪目标提交被拒 InvalidTarget 且原子），覆盖人/AI/延时锦囊枚举一致面；②**既有 kind-only 门 `IsCardTargetProhibited` 改为跳过携带类别/花色过滤器的策略**（否则 kinds 为空的新形态策略会在枚举期按"任意牌型全禁"误伤——本批现场回归发现并修复，帷幕红决斗/黑杀误禁即此因）。`ProgramFilterCategoryOf` 映射 基本牌/锦囊牌/装备牌 → Basic/Trick/Equipment（CardCatalog.CategoryName，装备兜底）。全手牌转化路径（ProgramOrdinaryTrickUses）保持 kind-only（多卡混色无单一物理花色，属已知边界，见遗留）。
5. **乱武不用 selectTargets 的原因**：`SelectTargetsProgramOperationDescriptor` 对 OtherLiving 目标种类将 maximumTargets 截为 2（仅 AnyLiving/AnyWounded/OtherLivingHandAtLeastOwner 放行 8），与"所有其他角色"需求冲突；审计草图建议的形态不可行，改 op 内派生响应者（攻击辅助先例），如实在此记录。

## 内容与验证

内容：`classic-jia-xu.rules.json`（schemaVersion 62，三技能 revision 1、minimumRulesVersion 189；完杀/帷幕 cardPolicies、乱武 activation minCards 0/maxCards 0/targets 0/targetKind otherLiving/usesPerGame 1 + effects [requestNearestSlash target owner]）+ `classic-jia-xu.presentation.json`（schemaVersion 3，三技能官方逐字文本）。注册：经典包 1.159.0（CurrentVersion、两个嵌入资源 const、ClassicJiaXuCatalog 惰性目录、JiaXuProgram accessor 镜像张郃形态），wansha=`WithStructuredSkillMetadata(SkillTag.Locked, State)`、luanwu=`WithActiveActionMetadata`、weimu=`WithStructuredSkillMetadata(SkillTag.Locked, State)`；AddGeneral（classic:jia-xu，群，BaseHp 3，AdditionalSkillIds=[classic:luanwu, classic:weimu]，紧跟刘禅 AddGeneral 之后），CurrentGeneralIds 追加进入 identity:classic-5/8 共享武将池。规则版本 188→189。官方立绘（gid 49，104901，574×761，sha256 见目录）经 `sync_general_art.py --phase classic`（jia-xu: 1 skin(s)）下载入库，目录登记 sha256/尺寸/来源页；图鉴 myth-forest 组追加 jia-xu（GeneralGalleryCatalog.cs），WPF 资源经 csproj 通配自动收录。

定向检查 `tests/CardGame.Core.Tests/JiaXuChecks.cs`（4 项，注册于 Program.cs 张昭张纮之后；人机同场 fixture：人=seat0 主公贾诩、AI 为无技能 bank 牌堆，帷幕模式含专用帷幕 bank（首技能 classic:weimu），帷幕模式身份构成 1 主公 + 4 反贼——引擎从不提供盟友目标，忠臣座会令定向断言失真，故该模式不存在盟友座）：

1. 定义与策略 schema：群 3 体力三技能、双身份池；otherLivingNearest=28、prohibitDyingPeachByOthers=18 枚举钉扎；完杀纯桃禁策略、乱武 owner 型 requestNearestSlash 限定技、帷幕 Trick+黑花色目标禁止；官方文本逐字；schema 拒收样例（无 kinds/categories 的目标禁止、minimumResponseCount 挂类别/花色、濒死桃禁带 requiredCardKinds、濒死桃禁非桃 kinds）与正样例（帷幕形态可加载）。
2. 完杀濒死结算：1 体力 victim 被杀至濒死 → 自救用自己桃（Hp 回 1、该桃 Hand(1)→Use 结算）、旁观者桃零移动（仅计 Hand(2) 起源的移动，忽略发牌移动）、回合主自桃全程留存（阻断面覆盖含贾诩本人）、DriveUntilSettled 守卫"被禁桃不得出现于人类座任何提示选项"、checkpoint 重放全等。
3. 乱武闭环：激活（usesPerGame 1）→ 每个其他存活角色恰好一次 `ProgramNearestSlashAnsweredEvent`；用杀支 TargetSeat ∈ 该响应者最近集（公共 `GetCombatDistance` 复算）且 CardUsedEvent 实证、不掉血；无杀支 LostHp 且恰好 -1 体力；ProgramSkillResolvedEvent；结算后不再提供激活；checkpoint 重放全等。
4. 帷幕目标过滤：黑决斗不得指定帷幕主（合法行动面缺席）、红决斗可指定、黑决斗可指定无帷幕者、杀可指定帷幕主；伪黑决斗→帷幕主提交被拒 InvalidTarget 且状态/事件/命令计数原子；红决斗实发（CardUsedEvent 实证）并 checkpoint 重放全等。

执行器单测（`SkillProgramExecutorChecks`）：FakeRuntime 补 `RequestNearestSlash` 分派记录 `nearest-slash-request:{ownerSeat}`；目录解析 fixture（ProgramCompositionDefinitionChecks）按枚举逐一覆盖原则补 requestNearestSlash 唯一样例。

## 门禁

- Release 全解构建：0 警告 0 错误（--artifacts-path 独立目录 Card-jiaxu-artifacts）。基线两处既有 CS8602（GaoDaYiHaoChecks 81/85 行可空 action 断言后缀使用）按门禁 0 警告要求改为 `?? throw`（断言语义与报错文案不变，非行为修复，如实在此披露）。
- 定向：Jia Xu 4/4 全绿、Kongcheng（ProhibitTarget 邻接）全绿。
- 全量 Core：**641/641 全绿**（基线 637 + 本批 4 项；基线即本 worktree 基线提交 4967d45=张昭张纮并入态，纯基线曾复验 637/637 全绿）。本批两处入池/交互回归修复（断言原文未弱化）：
  1. **CaoRen Jushou（乱武×身份卡）**：贾诩入池后 AI 贾诩在 classic-5 种子夹具内激活乱武，身份卡（如武圣红牌）响应者按"素用"结算触发 `GetSelectedUseConversion` "A converted card use requires its published source choice"。修复：乱武用杀选择变体改走共享 `CreateConversionChoiceVariants`（身份/转化/素用与正常出牌同构），结算发布所选转化源。挑衅同构路径未动（既有夹具未覆盖该组合，留待工具属主/后续批）。
  2. **Weimu kinds 空策略×既有 kind-only 门**：`IsCardTargetProhibited`（HasCardPolicy 包装）对 kinds 为空的新形态策略按"任意牌型全禁"误伤（红决斗/黑杀全被禁）。修复：kind-only 门显式跳过携带类别/花色过滤器的策略（注释说明），花色敏感评估收敛于新增 `IsSuitFilteredCardTargetProhibited`。
- 格式：本批全部变更代码文件 dotnet format whitespace --verify-no-changes 0 条。
- 工作过程事故（如实记录）：开发中途外部进程删除了整个 worktree 目录（本 agent 未执行任何删除；分支无提交、全部未提交改动随目录丢失）。处置：`git worktree add` 原路径原分支重建（向共享 .git 写入 worktree 登记元数据，属恢复性管理操作），依据过程记录全量重放全部代码与内容改动后复跑三道门禁。为防再次丢失，交付前另存全部触碰文件副本至 Temp（Card-jiaxu-backup）。
- tools/sync_general_art.py 的 `--phase finalize` 在 HEAD 即存在 NEW_OL_DEFAULTS 未定义的 NameError（先于本批），本批以 --phase classic 完成入库、未修该既有缺陷（留待工具属主）。`--phase verify` 对全目录校验在既有 zhou-yu/201203 记录上失败（目录条目与本地 PNG 均为基线原样、非本批漂移）；jia-xu 条目按 classic 阶段产出的 sha256/尺寸登记。

## 批次边界与遗留

- 规则版本 189 仅本批使用；schema 62 未升版（cardCategories/suits 为可加可选节点）；RulesSchemaVersion 保持 62。
- 全部工作在隔离 worktree（batch/jia-xu，基线 4967d45）完成；共享主树（Desktop/Card）除 worktree 重建登记外零写操作；未触碰其他 worktree；未执行 push。
- 已知边界（如实）：①全手牌当普通锦囊（ProgramOrdinaryTrickUses）的目标判定未接花色/类别过滤（多卡混合无单一物理花色，语义未定义）；②挑衅（requestSlashByTarget）对"身份卡手杀"响应者存在与乱武同构的素用崩溃面，本批未动（非本批引入，既有测试未覆盖）；③帷幕对"转化候选无单一物理花色"fail-open（不匹配花色过滤器），与边界①同源；④--phase verify 的 zhou-yu 既有失配与 finalize NameError 均为先在缺陷。
