# 陈登单武将批次

当前 OL 陈登（群势力、4 体力、男、称号"湖海豪气"，官网 495 号）接入：丰积（每轮开始时，你可以依次选择是否令对应项数值-1：1.摸牌阶段摸牌数；2.出牌阶段使用【杀】的次数上限。你每选择一项，便令一名其他角色本轮的对应项数值+2，选择完成后，你令本轮选择否的对应项数值+1）、旋回（准备阶段，你可以令所有受到"丰积"效果影响的其他角色与你交换对应的"丰积"效果，然后此技能失效直到一名角色死亡）。官方立绘 49500 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-chen-deng-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 丰积 | fengji-round-choice：新共享触发窗口 roundStarting（每实际回合在恒业恢复之后、他人回合开始拼点与准备阶段程序之前开启一次，额外出牌型回合跳过；复用 `ProgramLifecycleTriggerWindowFrame`，候选按距轮开始座位顺序收集，参与者 facts 逐座位冻结），可选触发 + 新条件 `fengjiRoundChoicePending`（当前实际回合即推进轮账本的回合、且本轮无该拥有者的 `ProgramFengjiOptionChosenEvent`）→ 新 op `fengjiRoundChoice`（7244）按文本顺序逐项问"是/否"：是则追问一名其他存活角色后提交该选项标量事实（Accepted+RecipientSeat），否则直接提交（+1 留给拥有者）。轮账本经依赖开关 `TracksFengjiRoundLedger`（`HasTriggerOperation(fengjiRoundChoice)`）接入 `EvaluateDrawCount`/`EvaluateSlashUseLimit` 的门控贡献：拥有者 -1 / 接受者 +2（按交换次数奇偶翻转归属）/ 未选项 +1，沿用既有 [0,∞) 界clamp。轮标识来自 `RoundStartedEvent`（`UsesRoundTracking` 增加该 op 键），效果只作用于提交时记录的轮号。 |
| 旋回 | xuanhui-swap：turnStartBeforeNormalFlow（既定准备阶段窗口）、可选触发 + 新条件 `fengjiSwapAvailable`（未被自身交换置失效，且本轮存在接受项且其接受者存活且非拥有者）→ 新 op `xuanhuiSwapEffects`（7245）提交一条 `ProgramXuanhuiEffectsSwappedEvent`；账本推导按交换次数奇偶把所有接受项的拥有者↔接受者归属翻转（未选项 +1 无对应方、不参与交换），本轮剩余时间生效。"失效直到一名角色死亡"由事件账本推导：最新的交换事件晚于最新的 `PlayerDiedEvent` 即视为失效，任意角色死亡即恢复；无序列化运行时状态。 |

## 共享能力扩展

- 新增 EffectOp 7244–7245（只占用分配的 7244–7251 段）；描述符按反射目录自动注册，ProgramOperationAiSemantic 尾部新增 `FengjiRoundChoice`、`XuanhuiSwapEffects`。未新增 PlayerMarkerKind、SkillProgramTriggerFactKind（分配的 1040/1041 未使用）、触发 usage scope 或 schema 节点。
- 新增触发窗口 `roundStarting`：枚举尾部成员、解析器 isLifecycleWindow/supportsTriggerCondition 白名单、`ProgramEntryCapabilities.SupportsWindow`、`CanRunProgramTrigger` 分支、`GameEngine.Runtime.cs` 完成绑定分支、`ProgramLifecycleContinuation.RoundProgramsTurnStart = 8040`（枚举尾部显式值，不移动既有隐式值，checkpoint 兼容）、`BeginTurn` 流在 `ContinueTurnAfterHengye` 入口处插入派发（无该窗口依赖的指纹在该入口立即返回原路径，行为不变）。
- 新增 Condition 尾部成员 1027–1028 及对应 nullable facts（JsonIgnore WhenWritingNull，旧存档反序列化为 null）。
- `UsesRoundTracking` 增加 `fengjiRoundChoice` 键（与既有键并联，仅在新技能在册时改变轮追踪开关）。
- 丰积/旋回的全部状态由已提交事件推导（无新增序列化运行时状态）；冷恢复按接受命令前缀确定性重建。

## 边界口径

- 丰积：整个"每轮开始时"机会是外层可选触发——整体放弃则本轮无任何调整（含 +1）；激活后按文本顺序逐项选择。提示只在轮开始的那一实际回合出现一次（额外出牌型回合既不开启窗口也不计为机会）；放弃后本轮不再补问。接受项的 +2 指定对象在提交时校验存活，交付立即完成；因此"中途死亡回收 +2"不存在。摸牌项的 -1/+2 在下个摸牌阶段按查询时点生效（DrawCount 在摸牌阶段开始冻结基数时读取账本），杀项在每次出杀时实时生效；两者都经 [0,∞) clamp（基础值 2→最低 1；杀基础 1→最低 0，即该轮不能出杀）。
- 旋回："交换对应的丰积效果"只作用于本轮接受项的 -1↔+2 归属对；未选项的 +1 只有拥有者一方受影响，没有交换对象、保持不变（诚实记录的解释边界，官方无更细文案）。交换不回收已经按旧归属结算过的摸牌/出杀——它改变的是此后整轮的查询归属。失效只由自身交换触发；任意一名角色死亡即恢复（含自己以外角色），恢复后同一轮内可再次交换（奇偶再次翻转）。接受者已死亡的接受项仍是有效交换对象（其 -1 归属对死者查询无效果），提示条件只要求至少一项有存活接受者。
- 轮账本事实按 (RoundNumber, OwnerSeat, Option) 幂等：同一轮同一项重复提交命令会被拒绝。两个证据事件均为纯标量记录，暗手牌 id 不进任何普通快照。
- AI 口径：丰积触发估值只计下限（未选项 +1），激活后逐项选择"否"；旋回估值计 +2 落到自身，满足条件即发动。选择提示的 AI 决策按确定性选择器（拒绝项优先）。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案）：0 error；20 条 warning 逐条核对全部位于本批未触碰的文件与行（SkillProgramExecutor、ActualHandGainPrograms、EndingHistoricalUses、LiangXingPrograms、OwnedDeathBenefitReturns、PublicPilePreparation、SameTypeActualUseAid 及 tests 下 11 条），非本批引入。
- 例行 `tools/Test-Changed.ps1`（无过滤，最终提交代码）：Core 266 通过 / 57 失败、WPF 18 通过 / 0 失败。57 项失败名单与本批前对基线快照（1fc7b1d6，经 `git archive` 提取到独立临时目录实测）逐名 diff 完全一致：0 项消失、0 项新增。失败均为基线在制状态（离魂/狂斧/源咒/边界系列/SP 系列等），无一涉及本批改动面。
- AI 自对弈冒烟：`--ai-batch`（64 固定种子 × 2 规模 × 2 策略，256 局完整对局）全部正常结束——roundStarting 窗口派发在每局每回合执行；陈登未在这些随机选将对局中出场，其专属 op 路径无自动化覆盖（按用户指令本批不含行为检查），如实记录。
- 基线说明：本批基线 = 严畯批实测的修复后基线（Core 266 通过 / 57 失败、WPF 18/0，57 项与协调者 46 项名单的差异属 aed24e70 批次自身的在制状态，见 docs/benchmarks/2026-10-07-ol-yan-jun.md 验证节）。本批结果与之逐项 diff，无新增失败。
- 内容包加载冒烟：`Ol Lü Kai definitions and metadata`（会初始化整个 Standard 内容包，含本批新 bundle 校验）单独过滤运行 PASS。
- 本批在独立 worktree（batch/ol-chen-deng）开发，与蒋干、周鲂、潘淑、黄承彦、杨仪、朱灵、刘辩等路并行。
