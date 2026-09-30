# 神周瑜批次：琴音 / 业炎（神话再临·火，2011）

- 日期：2026-09-30
- 批次：classic:shen-zhou-yu（神周瑜，神，男，4 勾玉）单武将
- 运行时：schema 62，规则版本 190（本批不上抬），经典包 1.162.0
- 状态：内容、注册、立绘、定向检查已落地；**零引擎增量**
- 入库：整树快照 `c6051aa`（与贾诩批同一次绿灯提交）

## 官方文本与口径

来源：`docs/content/sources/shen-zhou-yu-2026-09-30.json`（官方武将库 gid 203 逐字提取；客户端目录 `heroes.json` 二手核对神/男/4 勾玉与默认皮肤 120301）。

- 琴音：弃牌阶段结束时，若你于此阶段内弃置过你的至少两张手牌，你可以选择一项：1. 令所有角色各回复 1 点体力；2. 令所有角色各失去 1 点体力。
- 业炎：限定技，出牌阶段，你可以选择一至三名角色，对这些角色造成至多共 3 点火焰伤害（你选择目标时任意分配每名目标角色受到的伤害点数，若你将对一名角色分配的火焰伤害点数大于等于 2，你须先弃置四张花色各不相同的手牌并失去 3 点体力）。

包归属口径不一致处如实记录：官方页「版本」字段渲染为「一将成名」，客户端 lamp 标注为「神话·神」，预检文档按神话再临·火（2011）归类；本批图鉴入 `god` 组，技能文本以官方页为准。

## 实现映射（全部现行原语，最低规则 187）

- 琴音：`discardPhaseEnded` 窗口 + `SkillProgramTurnOwnerScope.Own` + `AllowOwnDiscardPhaseEnded`，条件为 `turnOwnerDiscardPhaseHandDiscardCount >= 2` 的 Compare/GreaterThanOrEqual 形状 → `chooseOption`（resultBind `qinyin-melody`，heal/lose 两项各带 presentation 标签）→ `recoverAllLiving(1)` 与 `loseHpParticipants(anyLiving, 1)`，两支按 ChoiceIs 门控。
- 业炎：限定技（`SkillTag.Limited` + `SkillActionForm.Active`）四档激活 `small` / `two` / `three` / `two-and-one`；高档两档先 `discardSelected` 四张花色各异手牌（`selectDistinctSuitHandDiscards`）再 `loseHpUnclamped(3)`，随后 `damageParticipants(nature fire)`，并保留 `ContinueAfterOwnerDeath`。
- 与 OL 线 `ol:qinyin` / `ol:yeyan` 的结构 diff 只有技能前缀与 `resultBind` 名称两处，因此行为语义由既有 `OlClassicGodChecks` 覆盖；按 AGENTS.md「纯配置消费既有能力不重复加检查」口径，本批不重复机制面断言。

## 定向检查

`tests/CardGame.Core.Tests/ShenZhouYuChecks.cs`（4 项，入口 `Program.cs` 的 "God Shen Zhou Yu …"）：定义与 roster schema；琴音两支（含从选项提示起的检查点-重放逐事件与快照一致）；业炎小档至多三名目标各 1 点火焰且限定技此后不可再发动；业炎大档四异花色手牌 + 自身 3 点体力换 2 点火焰伤害，牌动与体力变化逐项核对并回放一致。夹具以 `identity:classic-shen-zhou-yu-*` 三种模式 + 四名无技 AI 对手（黑桃/红桃杀、梅花/方块闪交替牌堆），并用 `InitialHp = BaseHp - 1` 使琴音回复支存在可回复对象。

## 本批自纠记录

- `TargetsFor` 原样返回全部可选座位并只断言 `count >= MaxTargetCount`，在 5 人局把 4 个座位一起交给上限 3 的业炎小档，被引擎以「The program's target selection is invalid」拒收；改为按 `MaxTargetCount` 截断后通过。
- 琴音探测原先要求同一个提示既属 `ProgramTrigger` 又直接带 `option-id`。实测琴音是可选触发：引擎先发「是否发动」提示（选项带 `skill-id` 与 `program-action = activate`），答复后才发选项提示。改为按两步序列探测——这与贾诩批「非真人座位的私有提示在步间不可见」属同一类修正：断言面必须是引擎真实发布的提示序列。
- 势力口径纠正：注册与 roster 断言曾写 `"shen"`，而仓内神将（神关羽/神吕蒙/神赵云/神曹操与全部 OL 神将）一律使用 `"god"`，且 WPF 的势力徽章、着色与图鉴排序都以 `god` 为键（`GeneralChoiceViewModel.cs:48`、`MainViewModel.cs:2244`、`MainViewModel.GeneralGallery.cs:219`）。内容 id 不变，势力 id 与断言两处一并改为 `god`。

## 验证结果

- 定向：`--filter="God Shen Zhou Yu"` **4 passed, 0 failed**；同批 `--filter="2010 Jia Xu"` 4/4（贾诩记录同口径）。
- 全量 Core：隔离输出目录构建后 **729 passed, 0 failed, 0 skipped（729 total）**；`dotnet build` 0 error，`dotnet format whitespace --verify-no-changes` 对本批两个新文件无残留告警。
- 立绘：`sync_general_art --phase skins --key shen-zhou-yu` 取 gid 203 的 4 张皮肤，默认经典形象 120301（574×761，sha256 `e4671273f74fa312ff868bf5b3890ffa66f584e12a450c0b04e33076d12bcd9d`）落 `src/CardGame.Wpf/Assets/official-shen-zhou-yu.png`，其余入 `Assets/Skins/shen-zhou-yu/`，并在 `docs/content/general-art-catalog.json` 登记。

## 并行在制面归因

- 本批评测期间主工作区并行推进一将成名 2011/2013/2014、OL 神将与武器/弃牌挑战等共享能力。同一晚全量结果从 441 失败 → 13 → 10 → 1 → 0 收敛，可复核的外部阻塞面包括：`classic-fa-zheng`/`Fame2014Content` 内容对在制（`LoadPresentations` 抛错使经典注册表整体失败）、`Fame2014ControlChecks.cs` 引用不存在的 `EndPlayCommand`（CS0246）、并行新增操作 `OfferCompletedCardGift`（`SkillPrograms.cs:303`）缺 AI 处理器与 op 覆盖表行、并行未跟踪文件 `GameEngine.CompletedCardGifts.cs` / `GameEngine.AssistedFactionSlash.cs` / `Resolution.cs` 的拼点与牌使用重构。上述文件均非本批改动。
- 并行测试进程持有 `bin/` 输出锁时本批构建报 MSB3021/MSB3027，属资源竞争；改用 `-o` 隔离输出目录构建并直接运行测试 DLL 后不受影响，此法可作为本仓共用在制期的常规验证手段。
