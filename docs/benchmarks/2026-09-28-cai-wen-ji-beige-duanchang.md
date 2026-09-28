# 蔡文姬批：悲歌四花色判定分支与断肠

状态：蔡文姬（神话再临-山，2011年7月，群3体力）单武将交付完成。schema62 / 最低规则182 / 经典包1.152.0 / Checkpoint3。本批为单对话交付，基于并行批次提交 82d39a92 之后的工作区；断肠（classic:duanchang）系该并行批次已提交内容，本批原样保留并随武将首次正式注册（此前仅有技能定义与 ResponseAndSkillSuppressionChecks 行为测试，无武将注册）。

## 技能与官方口径

- 悲歌：每当一名角色受到【杀】造成的伤害后，若其存活，你可以弃置一张牌，令其判定，若结果为：红桃，其回复1点体力；方块，其摸两张牌；梅花，来源弃置两张牌；黑桃，来源翻面。
- 断肠：锁定技，当你死亡时，杀死你的角色失去其当前的所有武将技能。（既有内容，原样复用）

官方现行文本取自官网武将库 gid 58（x.sanguosha.com/hero/58），与二手口径一致。体力值：官方页不渲染，3勾玉取自二手资料通行口径（山包蔡文姬 3 血），如实标注。来源记录见 [cai-wen-ji-2026-09-28](../content/sources/cai-wen-ji-2026-09-28.json)。

## 公共能力与实现要点

- 新增触发条件 `damageCardIsSlash`（值 22）：伤害窗 AttackResolution 的 `EffectiveCardKind` 属杀系（slash/fireSlash/thunderSlash，含虚拟与转化杀）为真；事实字段 `DamageCardIsSlash` 在伤害窗事实捕获处统一写入；校验器仅收 afterDamageApplied。费用守卫沿用圆壶定式（eventTargetHp>0 存活 + currentHandCount>0 有牌可弃）。
- 新增判定来源 `startJudgment.sourceRef`（事件参与者引用，限 eventSource/eventTarget）：判定 SourceSeat 缺省仍为技能持有者，显式 sourceRef 时改为所引参与者；`IsValidProgramJudgmentContinuation` 按刚执行的 startJudgment 指令的 sourceRef 解析期望来源，重复判定继承原判定来源；StartProgramJudgment 参数化传递。悲歌以 `sourceRef {eventSource}` 使判定来源=伤害来源。
- 伤害窗 eventTarget 目标解析：`GetProgramTargetSeats` 的 EventTarget 分支此前只认用牌上下文，afterDamageApplied/damageAppliedBeforeDying 上下文按 TargetSeat（受害者）解析；同步修复 `CanRunAfterDamageProgramTrigger` 漏传窗口上下文、`EstimateCompositionForAi` 无上下文估算（新增可选 windowContext，触发激活估算传入挂起候选上下文）——后者在悲歌入池后于 AI 选将估算路径必然触发，属本批必须修复的既有缺陷。
- 新增操作 `chooseOwnCardDiscard`（ chooserRef 限 eventSource/eventTarget，zones 限 hand/equipment）：由所引参与者自行选择其区域牌逐张弃置（每次一张、无 decline、无目标变化提示），完整决策流镜像 chooseOtherOwnedCardDiscard（构造/选择构建/人机应答解析/AI 分派/宿主）；AI 弃牌脑按公开保留价值弃最低。梅花分支"来源弃置两张牌"以两次顺序 chooseOwnCardDiscard 承载，来源两区皆空时分支无效果。
- turnOver 新增 `targetRef`（owner 占位 + eventSource/eventTarget）：黑桃分支翻伤害来源的武将牌；解析校验占位与参与种类，资源契约登记参与者。
- 校验器放宽：eventSource 参与者引用此前仅限伤害窗，现兼收 judgmentFinalized（判定窗上下文本就携带 SourceSeat）；`EnumerateParticipantReferences` 纳入 sourceRef。
- 悲歌五触发器：afterDamageApplied 提示链（费用 selectOwnedCards→moveBoundCards 弃置→selectTarget(eventTarget)→startJudgment(selectedTarget, reason skill.classic.beige, sourceRef eventSource)→判定牌 moveBoundCards 入弃牌堆）+ 四 judgmentFinalized 花色分支（heart→recover(eventTarget,1)；diamond→draw(eventTarget,2)；club→chooseOwnCardDiscard(eventSource)×2；spade→turnOver(targetRef eventSource)）。分支以判定原因限定作用域，不用 judgmentSource（该匹配按 owner 座位比对，与"来源"语义冲突）。

## 内容与验证

内容：`classic-cai-wen-ji.rules.json` 追加 classic:beige（revision 1，最低规则182），classic:duanchang 原样保留；presentation 补悲歌官方逐字文本。`StandardClassicGeneralPackage` 注册武将（faction qun、portraitKey cai_wen_ji、BaseHp 3、技能 [beige, duanchang]），版本 1.151.0 → 1.152.0，`CurrentGeneralIds` 追加 classic:cai-wen-ji。规则版本 181 → 182（Replay.CurrentRulesVersion）。官方立绘（gid 58，经典形象105801，574×761）入 general-art-catalog（92 武将 853 PNG，--phase verify 通过），WPF 资产 `official-cai-wen-ji.png`，图鉴 myth-mountain 组新增蔡文姬。

定向检查 `tests/CardGame.Core.Tests/CaiWenJiChecks.cs`（4项，自然命令与真实决策应答；另有并行批次既有断肠行为测试随武将注册继续通过）：

1. 定义与触发schema：群3体力、身份池、悲歌提示链属性（窗口/subject/perDamage/可选/杀系条件）、判定 sourceRef、四花色分支partition与各分支效果、断肠 ownerDied 保留；schema拒收样例（damageCardIsSlash 误用于用牌窗、sourceRef 出事件参与者、chooseOwnCardDiscard 用判定区、turnOver targetRef 非 owner 占位）。
2. 每判定恰一分支并回放：费用恰弃一张手牌、判定牌入弃牌堆、四分支效果互斥计数（回血/摸二/来源弃二/翻面恰一）、Checkpoint 还原后事件与状态全等。
3. 梅花分支：伤害来源自弃恰两张自己的手牌/装备区牌。
4. 黑桃分支：伤害来源武将牌翻面。

验证结果：定向（含断肠）5/5通过。Release 全解构建0警告0错误。Core 全量 578/578 全绿。中途全量失败归因（HEAD 基线 worktree 对照，基线全过）：

1. 4例"Event-target selection requires card-action context"与描述符契约覆盖缺失为本批引入：AI 激活估算路径无窗口上下文解析 eventTarget（悲歌入池后 AI 选到蔡文姬即触发）、伤害窗 eventTarget 无解析分支、新 op 未登记解析 fixture——均已修复（见公共能力）。
2. 驱虎荀彧1例为牌池位移的既有脆弱断言：新增武将进 CurrentGeneralIds 后 QuhuScenario.Find 选中不同种子，新种子结算流含无关伤害/救援事件，"体力恰-1"代理断言失真；并行批次移出本武将复验通过证实。修复为意图保持的稳健断言（保留"对手→受害者1点伤害"+技能结算事件，移除精确体力等式），定性与前批"苦肉濒死/烈弓转通过"同类。

格式检查：沿用范围化口径，编辑过的既有文件逐文件报错条数与 HEAD（82d39a92 干净 worktree）完全一致（SkillPrograms 95=95、ProgramLifecycle 258=258、SkillProgramExecutor 10=10、Host 6=6、ProgramOperationDefinitions 3=3、CompositionAi 3=3、RepeatedJudgment 6=6、Program.cs 8=8、ProgramCompositionDefinitionChecks 14=14、QuhuChecks 11=11、SkillProgramExecutorChecks 3=3；行级差异均为工作区行尾状态差异，提交时 autocrlf 归一），新文件 CaiWenJiChecks.cs 与 GameEngine.ProgramOwnCardDiscards.cs 复验 0 条。收尾阶段随顺带修复复验 ShenSimaYiChecks 0=0、SkillProgramExecutorChecks 复适配后 0=0。

终验（用户以 cf28aab2 收尾提交本批核心后的复核）：主工作区全量当时被并行在制 classic:zaoxian 解析错阻断（325/326 失败全部归一该错误），故以隔离 worktree 在 cf28aab2 提交态复核——该提交本身含两处并行批次撕裂面：SkillPrograms.cs 枚举成员错置（CardsUsedOrRespondedThisTurn 误入条件枚举致 CS0117，按并行写入者工作区最新形态补齐值枚举成员即愈）与本批无关的既有 ShenSimaYiChecks fixture 缺陷（见下）。补齐后 cf28aab2 提交态：Release 0警0错、Core 全量 578/578 全绿（含本批定向 5 项）。WPF 全量仍被既有鬼龙斩月刀缺牌面阻断（曹丕批已记录），按过滤器验证：general portraits 过滤器下缺立绘名单不含 classic:cai-wen-ji（余 4 项 classic:xu-sheng/boundary:xu-sheng/classic:zhang-xiu/ol:shen-guan-yu 为并行批注册先行、目录项与立绘均缺的既有缺口，两棵树一致），complete matches 过滤器通过（含蔡文姬入池的 5 局完整对局）。

顺带修复（非本批引入，归因 82d39a92）：ShenSimaYiChecks“definition and kill-window schema”的 marker 拒收样例 Replace 模式含换行敏感字面量，82d39a92 改写 raw string 后模式永不匹配、拒收断言空转（在 82d39a92 基线 worktree 复现同败，与本批无关）；改为无空白敏感的等价替换模式，意图保持。

## 批次边界与并行状态说明

- 判定来源语义：悲歌判定为受害者判定（target=受害者），来源仅作为 SourceSeat 参与判定窗上下文；官方文本未赋予来源质疑权，无其他交互面。
- 梅花分支"来源弃置两张牌"实现为来源自选、逐张结算（两次独立询问）；来源唯一合法区域为手牌与装备区。
- 文档收尾时段工作区出现并行批次在制写入（classic:tuntian 半成品、CardsUsedOrRespondedThisTurn/ClaimJudgmentCard 签名扩展），与本批文件交叠处均已等待其写入完成并按其最新接口适配测试桩（收尾阶段并行写入者将 ClaimJudgmentCard 收敛回单参签名，测试桩随之复适配）；本批最终验证以隔离 worktree 的 cf28aab2 提交态为准（见终验），主工作区在制 classic:zaoxian 解析错与本批无关。
