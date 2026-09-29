# 刘禅（享乐/放权/若愚）单武将交付批记录

日期：2026-09-29。武将：经典刘禅（蜀，男，3体力，神话再临·山 2011；官方 gid 54）。技能：享乐（锁定技）、放权、若愚（主公技+觉醒技）。前序：姜维批（规则 185/经典包 1.155.0，本批基线 3c2cd47d 即姜维批交付提交）；并行董卓批在制（本批为其预留规则 186 与经典包 1.156.0，跳号使用）。本批规则 185→187（Replay.CurrentRulesVersion）/ 经典包 1.155.0→1.157.0。

## 官方口径

- 享乐：锁定技，当你成为一名角色使用【杀】的目标后，除非该角色弃置一张基本牌，否则此【杀】对你无效。
- 放权：你可以跳过出牌阶段，若如此做，此回合结束时，你可以弃置一张手牌并选择一名其他角色，令其获得一个额外的回合。
- 若愚：主公技，觉醒技，准备阶段开始时，若你是当前的体力值最小的角色（或之一），你加1点体力上限，回复1点体力，然后获得"激将"。
- 体力/势力/性别/包归属取 BWIKI 二手核对：蜀、男、经典版 3 勾玉、神话再临·山；图鉴入 myth-mountain 组。来源与口径见 [liu-shan 来源记录](../content/sources/liu-shan-2026-09-29.json)（已合并官方立绘完成态：gid 54，经典形象 105401，574×761，sha256 f5c65e3d…，localPath src/CardGame.Wpf/Assets/official-liu-shan.png）。

## 公共能力与实现要点

1. **共享公共能力 `LivingPlayersMinHp`（与董卓批约定一字不差，合并时去重）**：存活角色（含自己）当前体力最小值。落位为两枚枚举：`SkillProgramTriggerValueKind.LivingPlayersMinHp = 25`（compare 条件右值，触发期求值）与 `SkillProgramNumberExpression.LivingPlayersMinHp = 14`（效果数量表达式，按共享契约预留）；另补 `SkillProgramTriggerFacts.LivingPlayersMinHp` 事实位 + `GameEngine.ProgramLifecycle.CaptureProgramTriggerFacts` 捕获（`LivingPlayersMinimumHp`：`_players.Where(IsAlive).Select(Hp).DefaultIfEmpty(owner.Hp).Min()`）+ Resolve 臂。各既有 switch 均有 `_` 默认臂，新成员对旧内容零影响。
2. **`nullifyCurrentCardEffect` 解析放开 choiceIs**：`ProgramCardTargetEffectOperationDefinition` 的条件校验由"仅 Always"放开为 `Always | ChoiceIs`（注释说明：无效化可挂在具名选择分支之后；无条件程序保持历史形态）。结算路径零改动：`NullifyCurrentProgramCardEffect` 既有实现要求 owner 的 before-target-effects 窗口上下文并发布 `ProgramCardEffectNullifiedEvent` + `CardEffectSkippedEvent(SkillNullified)`。
3. **`pendExtraTurn` 参数化 targetRef（放权）**：`ProgramExtraTurnOperations` AllowOnly(op/target/targetRef/condition)，targetRef 必须恰为 `selectedTarget`（`target:"selectedTarget"` 仍被拒——额外回合帧始终属于技能宿主）；解析产出 `targetReference`，`Resources => ParticipantResources(effect.TargetReference)`；Handler `host.PendExtraTurn(frame, effect.TargetReference is { } r ? host.ResolveParticipant(frame, r) : null)`；`ISkillProgramExecutorHost.PendExtraTurn` 接口与引擎 `PendProgramExtraTurn(frame, int? targetSeat)` 加参，受益者 `targetSeat ?? active.OwnerSeat`，不存活即静默跳过，事件 `ProgramExtraTurnPendedEvent(..., Seat=受益者)`。
4. **享乐为纯组合（零新增原语）**：`cardUseBeforeTargetEffects` 触发（ownerRelation=target、cardKinds=[slash,fireSlash,thunderSlash]、optional=false、priority 0）+ `chooseOption(target=actor, resultBind=xianle-answer, options[pay=hasOwnedCardCategory(hand,basic), decline=always])` + `selectAndMoveOwnedCard(chooserRef=actor, cardOwnerRef=actor, zones=[hand], cardCategories=[basic], count 1, destination discardPile, condition choiceIs pay)` + `nullifyCurrentCardEffect(condition choiceIs decline)`。**审计偏差修正**：audit-2 建议的 `chooserRef eventSource` 被现场组合校验拒绝（eventSource 要求 damage/judgment 窗口），且该窗口能力面仅 CardAction|Judgment 无 Damage——按任务授权改用 `actor` 参与者（窗口上下文即"杀的使用者"，chooseOption target=actor 与 selectAndMoveOwnedCard actor/actor 均零解析改动现成支持）。
5. **放权为三触发器 + 公共布尔态**：audit-2 的 chooseOption 门控 skipTurnPhases 不可行（skipTurnPhases 描述符 RequireAlways 不接受条件）→ 改用 optional 触发器的接受/拒绝作为声明决策。states：`fangquan-pending`（public、initialValue false、resetScope game、reacquirePolicy preserveUntilGameEnd）。触发器：①`reset-pending`（turnStartBeforeNormalFlow、optional=false、priority 100、setBooleanState false——覆盖"回合结束拒绝"路径的跨回合残留）；②`declare-skip-play`（afterNormalDraw、optional=true：skipTurnPhases[play] + setBooleanState true，afterNormalDraw 窗口白名单仅 [play] 合规）；③`grant-extra-turn`（turnEnding、optional=true、usageScope turn、usageLimit 1、condition all[booleanState=true, compare currentHandCount>=1]：selectOwnedCards(1, hand, resultBind fangquan-cost) → moveBoundCards→discardPile → selectTarget otherLiving → pendExtraTurn(target owner, targetRef selectedTarget)）。
6. **若愚为纯组合（照抄志继模板）**：turnStartBeforeNormalFlow（subject owner、optional=false、usageScope game、usageLimit 1）、条件 `compare(currentHp, lessThanOrEqual, livingPlayersMinHp)`（"或之一"即 ≤ 语义）、效果 changeMaximumHp(+1) → recover(1) → grantSkills[classic:jijiang]。主公技门按既有机制验证：`CanOwnPrintedSkill` 仅 Role.Lord 持有 SkillTag.Lord 印刷技（定向检查含"非主公不拥有若愚"）；觉醒授予的技能不走印刷门，授予的激将在 identity:classic-* 模式出牌阶段可枚举为真实 UseProgramSkill。
7. **AI 攻击者付费估值**：`ProgramChoiceAi.Score` 对 `NullifyCurrentCardEffect` 效果计 -30（失去自己正在使用的杀即拒绝支付分支的真实代价）；否则选项 tie-break 按字母序 decline<pay 会系统性误选拒绝。
8. **额外回合单槽位裁定**：`_pendingExtraTurnSeat` 单槽位，**后挂覆盖前挂（latest declaration wins）**——同回合多次声明时以最后声明为准；`FinalizeEndTurn` 消费时带 `_players[seat].IsAlive` 守卫（受益者阵亡则作废）。执行器级单测覆盖 targetRef 分派（fake host 记录 `extra-turn:{ownerSeat}:{targetSeat|owner}`）；"后挂覆盖前挂"的集成级场景无法稳定播种（连破的挂起点是 characterDied 窗口即时结算、不在回合结束边界），单槽位覆盖语义以 host 分派单测 + 放权集成测试共同覆盖，如实在此记录。
9. **skipTurnPhases + booleanState 组合的复位不变式**：preserveUntilGameEnd 态跨回合残留，故 reset-pending 触发器为声明面完备性必需（拒绝声明后下回合标志必须为 false）；放权拒绝支定向检查断言拒绝后同回合出牌阶段保留且回合结束无 grant 提示。

## 内容与验证

内容：`classic-liu-shan.rules.json`（schemaVersion 62，三技能 revision 1、minimumRulesVersion 187）+ `classic-liu-shan.presentation.json`（schemaVersion 3，三技能官方逐字文本；享乐 optionLabels 弃置一张基本牌/不弃置此杀无效——校验器要求 chooseOption 每选项有标签）。注册：经典包 1.157.0（CurrentVersion、两个嵌入资源 const、ClassicLiuShanCatalog 惰性目录、LiuShanProgram accessor 镜像姜维形态），xiangle=`WithStructuredSkillMetadata(SkillTag.Locked, Trigger)`、fangquan=`WithOptionalTriggerMetadata`、ruoyu=`WithStructuredSkillMetadata(SkillTag.Awakening | SkillTag.Lord, Trigger)`；AddGeneral（classic:liu-shan，shu，BaseHp 3，AdditionalSkillIds=[classic:fangquan, classic:ruoyu]，紧跟姜维 AddGeneral 之后），CurrentGeneralIds 追加进入 identity:classic-5/8 共享武将池。规则版本 185→187。官方立绘（gid 54，105401，574×761，sha256 见目录）经 `sync_general_art.py --phase classic --key liu-shan` 下载入库（97 武将 870 PNG，--phase verify 通过），图鉴 myth-mountain 组追加 liu-shan（`GeneralGalleryCatalog.cs`），WPF 资源经 csproj 通配自动收录。

定向检查 `tests/CardGame.Core.Tests/LiuShanChecks.cs`（9 项，注册于 Program.cs 姜维之后；人机同场 fixture：人=seat0 主公刘禅、AI 为 8 体力无技能 bank 牌堆，身份模式主公 +1 上限口径下 4/4 仍为存活最小）：

1. 定义与触发schema：蜀 3 体力三技能、双身份池；享乐锁定/before-target-effects/三种杀；放权三触发器+状态+usageScope turn/limit 1；若愚 Lord|Awakening、compare currentHp≤livingPlayersMinHp、game/1；schema 拒收样例（nullify 非 Always/ChoiceIs 条件、chooseOption 非 actor 支付、category 过滤要求 chooser=cardOwner、条件移动带 resultBind、pendExtraTurn targetRef 非 selectedTarget、pendExtraTurn target=selectedTarget、compare 右值未注册枚举）。
2. 享乐付费支：AI 攻击者弃 1 张基本手牌（xiaole reason、Hand→DiscardPile）、杀照常伤害、无 SkillNullified、checkpoint 重放全等。
3. 享乐无效支：攻击者无基本牌 → decline → CardEffectSkippedEvent(SkillNullified)、刘禅不掉血、攻击者不付牌、重放全等。
4. 享乐只对杀触发：决斗指定刘禅不开启享乐窗口、无支付询问。
5. 放权跳过+选 seat2 获额外回合：声明后出牌阶段消失（PlayCard 决策出现即抛）、回合结束弃 1 手牌、ProgramExtraTurnPendedEvent(Seat=2)、TurnStarted(2) 跳过 seat1、重放全等。
6. 放权拒绝声明：拒绝后同回合出牌阶段保留、回合结束无 grant 提示、全程无放权额外回合。
7. 若愚觉醒：SkillAwakenedEvent(MaxHp+1、AcquiredSkillIds=[jijiang])、恰好一次、回复至上限不溢出、获激将且同出牌阶段激将为真实合法行动、重放全等。
8. 若愚非最小不觉醒：bank 2 体力独立 mode，3+1>2 保持蛰伏。
9. 非主公不拥有若愚：HumanRole=Loyalist 模式技能表恰为 [享乐, 放权]、无若愚行动；场景包给 bank1 一个主动技（挑衅）作 AI 主公的确定性选择诱饵，使人类忠臣座稳定可选刘禅。

执行器单测（`SkillProgramExecutorChecks`）：共享 host 显式分派测试补 pendExtraTurn 两分支——targetRef=selectedTarget 记录 `extra-turn:0:1`、无 targetRef 记录 `extra-turn:0:owner`，均恰好完成一次。

## 门禁

- Release 全解构建：0 警告 0 错误（--artifacts-path 独立目录 Card-ls-artifacts）。
- 定向：Liu Shan 9/9 全绿。
- 邻接过滤：Jiang Wei 6/6、Lu Su 5/5、Cai Wenji 5/5、Sun Ce 4/4 全绿。
- 全量 Core：**606/606 全绿**（基线 597 + 本批 9 项；基线即本 worktree 基线提交 3c2cd47d=姜维批交付态）。两处入池漂移修复（共享池加入刘禅改变候选枚举 RNG，种子扫描共享夹具命中既有脆面；纯 HEAD 均绿，非本批语义回归，处置沿姜维批先例）：
  1. **QilinBow（FindHumanTrigger）**：新桌下 seat0 选中 ol:shen-guan-yu，其武神系红牌转化声明使任意红 QilinBow 不可作装备打出，装备硬断言失败。修复：装备提交改为种子试验化——被拒即弃该种子续扫（同姜维批 Qixi 处置）；杀提交同样改软失败弃种。夹具诊断实证（拒绝即 source=ol:shen-guan-yu）。
  2. **BorrowedSword（FindHumanOwnerResponse）**：新桌下强制杀目标=classic:da-qiao，其流离将杀转移第三者、先于闪避窗口吸收结算，"嵌套杀带闪避窗"断言失败。修复：闪避能力谓词补排除闪避前置型锁定触发——`classic:xiangle`（本批新内容）与既有 `classic:liuli`（同属"锁定卡牌窗口技先于闪避窗"性质，与无双夹具排除流离同源）。断言原文未弱化。
- 格式：本批全部变更代码文件 dotnet format whitespace --verify-no-changes 0 条（LiuShanChecks 6 行由格式器自动修复后复验通过；修复后重跑构建 0/0、Liu Shan 9/9、全量 606/606 确认）。
- 批内修复：享乐 presentation 补 optionLabels（校验器要求）；JSON 模板 Reject 用例全部改为不跨行界的唯一 token 替换（姜维批已证 C# 原样字符串逐字保留源码行尾，跨行 Replace 在 CRLF 检出下静默失配）。

## 批次边界与并行状态说明

- 规则版本 186 与经典包 1.156.0 为并行董卓批预留，本批跳号使用 187/1.157.0；合并时 `LivingPlayersMinHp` 两枚举按约定去重（TriggerValueKind=25、NumberExpression=14）。
- 全部工作在隔离 worktree（batch/liu-shan，基线 3c2cd47d）完成；共享主树（Desktop/Card）零写操作；未触碰 dong-zhuo worktree；未执行任何 git commit（待编排者复核）。
- 享乐参与者、放权声明形态、若愚承载均为审计偏差按现场源码重新核实的取舍（见实现要点 4/5），非语义弱化：享乐语义（锁定向攻击者索基本牌否则无效）、放权语义（跳过出牌阶段、回合结束弃一张手牌选另一角色获额外回合）、若愚语义（最小体力觉醒 +1 上限回 1 血获激将）均与官方口径一致。
- tools/sync_general_art.py 的 --phase finalize 在 HEAD 即存在 NEW_OL_DEFAULTS 未定义的 NameError（先于本批），本批以 --phase classic/--phase verify 完成立绘入库与校验，未修该既有缺陷（留待工具属主）。
- 放权额外回合与连破既有单槽位的共存：两者共用 `_pendingExtraTurnSeat`，本批裁定"后挂覆盖前挂"写入 PendProgramExtraTurn 注释；既有连破测试全绿（全量门禁覆盖）。
- 定向 fixture 的 Start 采用种子前扫（候选按种子抽样、 seat0 候选须含刘禅才选定），保证 9 项检查在任一机器确定性收敛；种子上限 400（放权 60、觉醒 20、蛰伏 40）内均命中。
