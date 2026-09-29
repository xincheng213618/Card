# 董卓（酒池/肉林/崩坏/暴虐）单武将交付批记录

日期：2026-09-29。武将：经典董卓（群，8体力，神话再临·林 2010；官方 gid 46）。技能：酒池、肉林（锁定）、崩坏（锁定）、暴虐（主公技）。前序：姜维批（规则 184→185 / 经典包 1.155.0）、孙策/蔡文姬/鲁肃批等；本批在隔离 worktree（batch/dong-zhuo，基线 3c2cd47d）完成。本批规则 185→186（Replay.CurrentRulesVersion）/ 经典包 1.155.0→1.156.0。

## 官方口径

- 酒池：你可以将一张黑桃手牌当【酒】使用。
- 肉林：锁定技，每当你使用【杀】指定一名女性目标角色后，其需依次使用两张【闪】才能抵消；锁定技，每当你成为女性角色使用【杀】的目标后，你需依次使用两张【闪】才能抵消。
- 崩坏：锁定技，结束阶段，若有当前的体力值比你小的角色，你选择一项：1.失去1点体力；2.减1点体力上限。
- 暴虐：主公技，每当其他群势力角色造成伤害后，其可以进行判定，若结果为黑桃，你回复1点体力。
- 来源与体力口径见 [dong-zhuo 来源记录](../content/sources/dong-zhuo-2026-09-29.json)（官方页不渲染体力，8 勾玉取 BWIKI 口径，为全武将库最高体力；BWIKI 崩坏"若你不是全场体力值最低的角色"、暴虐"当其他群势力角色造成伤害后"与官方页"若有当前的体力值比你小的角色""每当"语义一致，以官方页为准）。

## 公共能力与实现要点

1. **酒池为纯 viewAs**：`spade-hand-as-alcohol`（inputSuits=[spade]、outputKind=alcohol、forPlay 与 forResponse 双开）零增量复用既有转化管线，覆盖使用与响应（含濒死以黑桃手牌替代【酒】自救）两条路径；注册用 WithContinuousStateMetadata，无任何新引擎能力。
2. **肉林双向响应计数**：两条牌策略同挂——攻击侧 `female-target-needs-two-dodges`（既有 `minimumResponseCount`，条件 `eventTargetGenderIs=female`：董卓用杀指定女性目标后其需两张闪）+ 防守侧 `female-source-slash-needs-two-dodges`（**新策略类型 `minimumResponseCountAsTarget`=16**，条件 `eventSourceGenderIs=female`：女性角色对董卓用杀后董卓需两张闪）。新增条件类型 EventTargetGenderIs=30 / EventSourceGenderIs=31（SkillProgramCondition 增加 GeneralGender? Gender 载荷，目录校验"gender 字段当且仅当这两类条件出现"）；引擎 `GetProgramRequiredResponseCount` 调用点补传 responderSeat：攻击侧取董卓的 MinimumResponseCount（空集回退 1）、防守侧取响应者自身的 MinimumResponseCountAsTarget（空集回退 0），两性别事实分别按真实 sourceSeat/responderSeat 计算，**Math.Max 聚合（不乘积）**，与无双先例一致。
3. **崩坏为纯组合**：turnEnding 触发（subject owner、optional=false、锁定）+ 条件 compare(currentHp > livingPlayersMinHp)（"存在体力值比你小的角色" ⇔ 自身体力严格大于存活者最小体力）+ chooseOption(lose-hp/reduce-max-hp，presentation 补 optionLabels) + choiceIs 门控 loseHp(1) / changeMaximumHp(-1)；注册用 WithStructuredSkillMetadata(Locked, Trigger)。
4. **共享能力 `livingPlayersMinHp` 登记**（本批新公共事实，非董卓私有）：SkillProgramNumberExpression 追加 LivingPlayersMinHp=14、SkillProgramTriggerValueKind 追加 =25；接线五面——RuleQueries 新增 `GetLivingPlayersMinHp()`（存活者体力最小值）；ProgramLifecycle 两处数值表达式求值（minimum clamp 与效果 amount）+ AI 公共事实与伤害窗事实注入；ProgramOwnedCards 求值臂；ProgramCompositionAi 公共上下文字段（缺省回退 owner 体力）与三处估值臂；selectOwnedCards / recoverTo 操作描述符放行该表达式。
5. **暴虐主公技语义落地**（非持有者决策族，此前仅颂威/制霸记录未实现，本批首次落地）：
   - **势力触发事实**：新触发条件 DamageSourceIsOwner=25 / DamageSourceFactionIs=26（SkillProgramTriggerCondition 增加 Factions 载荷），伤害窗事实注入 DamageSourceIsOwner / DamageSourceFactionId（GetEffectiveFactionId(伤害来源)）；目录校验：伤害来源事实仅允许 afterDamageApplied / DamageAppliedBeforeDying 窗，damageSourceFactionIs 必须带非空 factions。
   - **事件源目标**：新目标类型 EventSource=25（GetProgramTargetSeats 判 target.Seat == windowContext.SourceSeat），暴虐以 selectTarget(targetKind=eventSource) 把伤害来源绑为 selectedTarget，供 startJudgment(target=selectedTarget) 使用——判定牌归属伤害来源，符合官方"其可以进行判定"。
   - **非持有者决策落点**：chooseOption(target=owner, chooserRef={kind:eventSource})——决策提示发给伤害来源而非董卓（定向检查断言 choice.OwnerSeat==0 && choice.ChooserSeat!=0，即"其他群势力角色必须选择是否判定"）；startJudgment 以 sourceRef={kind:eventSource} 记来源，judgmentReason=skill.classic.baonve、resultBind=baonve-judgment、public 可见，moveBoundCards 将判定牌以 choiceIs(judge) 门控移入弃牌堆。startJudgment 操作描述符放宽为允许 Always 或 choiceIs 门控（判定本身可由命名选择分支触发，资源清理在组合层校验）。
   - **黑桃回复支**：第二触发 baonve-spade-recover（judgmentFinalized 窗、judgmentReasons=[skill.classic.baonve]、suits=[spade]）→ recover(owner, 1)。
   - 触发本体：afterDamageApplied、subject any、damageOccurrence=perDamage、条件 all(not(damageSourceIsOwner), damageSourceFactionIs qun)；注册用 WithStructuredSkillMetadata(Lord, Trigger)。
6. **cardsMoved 触发窗判定互斥**（本批引擎防自反馈）：存在进行中判定（_pendingJudgment）时，效果含 startJudgment 的 cardsMoved 触发本轮跳过候选——判定牌在途时其进出判定区的牌Motion不该再拉起新判定（避免自我反馈同一判定）。
7. **版本面**：Replay.CurrentRulesVersion 185→186（注明本批语义：livingPlayersMinHp 公共事实、参战者性别牌策略条件、EventSource 目标类型、主公技伤害来源势力事实）；经典包 CurrentVersion 1.156.0，四个嵌入资源 const + ClassicDongZhuoCatalog 惰性目录 + DongZhuoProgram 工厂；AddGeneral（classic:dong-zhuo，"董卓"，dong_zhuo，qun，8 体力，主技能 classic:jiuchi，AdditionalSkillIds=[roulin/benghuai/baonve]）；CurrentGeneralIds 追加（董卓进入 identity 共享武将池）。官方立绘（gid 46，经典形象 104601，574×761，sha256 见目录）入 general-art-catalog（--phase verify：97 武将 870 PNG），图鉴 myth-forest 组新增 dong-zhuo，tools/sync_general_art.py CLASSIC_HEROES 补 dong-zhuo:46，WPF 资源经 csproj 通配自动收录。

## 内容与验证

内容：`classic-dong-zhuo.rules.json`（四技能 revision 1、最低规则 186、schemaVersion 62）+ `classic-dong-zhuo.presentation.json`（schemaVersion 3，四技能官方逐字文本；benghuai/baonve 补 optionLabels）。

定向检查 `tests/CardGame.Core.Tests/DongZhuoChecks.cs`（11 项，自然命令与真实决策应答，注册于 Program.cs 姜维之后、鲁肃之前）：

1. 定义与触发schema：群 8 体力、身份池；四技能程序形态（viewAs/双牌策略/崩坏组合/暴虐双触发）；schema 拒收样例（肉林条件缺 gender、缺 requiredCardKinds、暴虐误用 drawPhase 窗、damageSourceFactionIs 缺 factions）。
2. 酒池黑桃手牌当酒使用并回放。
3. 酒池濒死以黑桃手牌替代酒自救。
4. 肉林对女性目标需两张闪。
5. 肉林男性目标一张闪即抵消（负样）。
6. 肉林女性角色杀董卓时董卓需两张闪（防守向）。
7. 崩坏无更小体力者保持沉默（负样）。
8. 崩坏失去体力支并回放。
9. 崩坏减体力上限支。
10. 暴虐群来源伤害触发判定、黑桃回复 1（回放自暂停决策点全等）。
11. 暴虐忽略董卓自身与非群来源（负样）。

## 门禁

- 定向：Dong Zhuo 11/11 全绿。
- Release 全解构建：0警告0错误（--artifacts-path 独立目录 Card-dz-artifacts）。
- 邻接过滤（测试名为子串匹配，逐一单独跑）：Jiang Wei 6/6、Lu Su 5/5、Cai Wenji 5/5、Sun Ce 4/4 全绿。
- 全量 Core：**608/608 全绿**（0 跳过）。批内首次全量 605/608，3 失败 = 入池漂移（见下），无语义回归。
- 格式：本批变更文件 dotnet format whitespace --verify-no-changes 0 条。
- 立绘：tools/sync_general_art.py --phase verify 通过（**实际 97 武将 / 870 PNG 记录**，非预期口播的 871）。
- 三处入池漂移根因与修复（identity:classic 共享池加入董卓改变候选枚举 RNG，种子扫描夹具选中全新牌桌组合命中既有脆面；三者均为引擎既有路径问题，非本批语义回归。修法照种子试验化模式：夹具在种子扫描循环内驱动候选种子到关键步骤并验证场景不变式，不满足即弃种子续扫；**断言原文一律未弱化**。诊断期以 Console.Error 输出候选种子五家武将与首个不满足不变式定位干扰源）：
  1. **借刀杀人双夹具（BorrowedSwordScenario）**：干扰源为大乔（classic:liuli）。新桌下被"需对董卓出杀"选中的目标若持流离，目标侧 slashRedirecting 程序触发窗（ProgramCardTriggerWindowFrame）先行打开，闪避 ResponseWindowFrame 被推迟，夹具"应答后立即存在闪避窗"的不变式落空。修复：扫描谓词静态排除 classic:liuli 持有者作为被借刀目标（谓词按本桌实测干扰源定，未照抄相邻批的 xiangle+liuli 排除集——乐不思蜀在本桌不可达为干扰）。
  2. **AI 对局长跑（"long AI runs finish without leaving an active resolution"）**：无董卓在场，纯池漂移可达。根因是青龙偃月刀嵌套跟随杀与多目标杀续接的既有碰撞：BeginQinglongCrescentBladeFollowup 中 CompleteAttack(父攻) 先把多目标杀（如程普烈火程序目标数）推进到下一目标（产生 _pendingAttack + 闪避窗），随后的 ResolveSlashCore(跟随杀) 覆写 _pendingAttack；跟随杀先完成时其收尾把覆写后的续接清空，外层目标的闪避窗与 _pendingFangtianHalberd 成为孤儿，直到完成态不变式"A completed game cannot retain pending resolution state"抛出（seed 27 实录）。修复（GameEngine.cs）：新增 QinglongFollowupSuspension——跟随杀 ResolveSlashCore 前挂起外层续接（下一次攻击/决策/方天画戟续接 + 引擎状态）；CompleteAttackAfterCardResolution 顶部在"完成者非外层结算且栈上无其他 CardUseFrame"时恢复挂起态（跟随期间引擎已完局则丢弃方天续接），恢复后提前返回。诊断期前任曾放宽 hasActiveCardResolution 不变式作遮罩，本批**回退该弱化**、以挂起/恢复根治。
  3. **麒麟弓双夹具（QilinBowScenario）**：干扰源为转化锁定武将（ol:shen-guan-yu seed 36 / classic:shen-guan-yu seed 2403 等，武圣族 cardIdentity 把全部手牌声明为红杀），其装备麒麟弓的 PlayCardCommand 被规则拒绝，夹具预设"装备必成"落空。修复：装备提交被拒即弃该种子续扫（continue）；另补枚举与提交间隙牌桌漂移的弃种子（break）。断言与夹具目标不变。

## 批次边界与并行状态说明

- 暴虐"其可以进行判定"落地为伤害来源的显式二元选择（judge/no-judge，不判则无判定）；判定牌最终进弃牌堆；黑桃回复只归董卓。崩坏官方措辞"有体力值比你小的角色"以 strict-greater 于存活者最小体力实现，含董卓自身在内的最小值语义等价（董卓严格更大时最小值必属他人）。
- 前任半成品清点（均已处置，最终树无残留）：① GameEngine.cs 两处 [TRACE] 调试块（引用不存在的 ParentFrameId，编译不过）——删除；② GameEngine.ProgramTurnPhase.cs BeginProgramVirtualCardUse 残留 [TRACE] Console.WriteLine——本会话补删后重跑全部门禁；③ hasActiveCardResolution 完成态不变式被前任放宽作遮罩——回退为基线严格不变式；④ ProgramNestedDamage.cs 与 BorrowedSwordChecks.cs 的临时插桩——已全量还原为与基线零差异。最终 diff 无 TODO/FIXME/HACK、无 Console 输出、无跳过测试（全量 0 skipped）。
- 入池漂移三处修法全部为夹具侧种子试验化（借刀排除谓词 / 青龙挂起-恢复为唯一引擎侧修复 / 麒麟弓弃种子），参考批（liu-shan）的排除集仅作模式参考，谓词按本桌诊断实际干扰源（liuli / 转化锁定武圣族）另行确定。
- tools/sync_general_art.py --phase verify 实测 97 武将 / 870 PNG 记录；任务口径中的 871 以脚本实际输出为准。
