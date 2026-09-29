# 神吕蒙批次：涉猎不同花色替换摸牌与攻心观手处置

- 日期：2026-09-30
- 批次：classic:shen-lu-meng（神吕蒙，神，3体力，神话再临·风·神将，十周年武将库 gid 202）单武将
- 运行时：schema 62 不变，规则版本 188→190，经典包 1.158.0→1.160.0
- 状态：**已完成**——引擎扩展、注册、定向检查与全量回归全部落地（见"工作树意外删除与重放"一节）

## 官方文本与口径

> 【涉猎】摸牌阶段开始时，你可以放弃摸牌，从牌堆顶亮出五张牌，然后获得其中不同花色的牌各一张，将其余的牌置入弃牌堆。
> 【攻心】出牌阶段限1次，你可以观看1名其他角色的手牌，然后你可以展示其中1张红桃牌，选择1项：1.弃置此牌；2.将此牌置于牌堆顶。

- 涉猎："不同花色的牌各一张"实现为**恰好**取每种出现花色各一张（onePerSuit 强约束 min=max=亮出牌的相异花色数）；亮出不足五张（牌堆耗尽）由取消路径兜底，不虚造选项。
- 攻心：观手是私有的全手牌视图；仅红桃可被展示（suits 过滤）；无红桃或主动放弃时走 decline，提交空私有绑定，下游选择分支全部静默，不触发任何亮出事件。
- 神势力归属"god"势力 id，图鉴归入既有"神"分组（与神关羽、神司马懿同组）；开局按既有 SelectFaction 流程选归属势力，不改写印刷势力。

## 实现设计

| 技能 | 形态 | 效果链 | 依赖 |
| --- | --- | --- | --- |
| 涉猎 | drawPhaseStarting 替换触发器（priority 90，drawPhaseMode replacement，沿用再起/突袭先例） | revealTopCards(5, public) → selectCardSubset(onePerSuit) → moveBoundCards(taken→ownerHand) → moveBoundCards(revealed except taken→discardPile) | E1/E2 |
| 攻心 | 出牌阶段限 1 次激活（otherLivingWithHand 单目标，激活目标即选即定，不写显式 selectTarget） | revealTargetHandCard(suits=[heart], allowDecline) → chooseOption(disposition) → moveBoundCards(→discardPile, choiceIs discard) → moveBoundCards(→drawPileTop, choiceIs top) | E2/E3/E4 |

## 引擎扩展清单（已落地）

- **E1 花色互异子集约束**：`CardSubsetCandidate` 增 `Suit`；`CardSubsetConstraint` 增 `AtMostOnePerSuit`（默认 false，既有调用零改动）；枚举循环内同花色第二次命中即剪枝（violatedSuitConstraint 标志，不产生伪空选项）；`SkillProgramEffect` 增 `OnePerSuit`；selectCardSubset 解析允许 `onePerSuit`；执行器选项界与提示文案按 onePerSuit 切换；运行时提交校验镜像（选中牌数=来源相异花色数且选中牌彼此花色互异）。
- **E2 revealTargetHandCard 花色过滤与放弃**：解析允许 `suits`（非空校验）与 `allowDecline`（必须伴随 suits）；`ProgramRevealCardSelection` 增 `EligibleCardIds`/`AllowDecline`；提示只列可选花色牌并附一个 decline 选项；decline 提交空私有绑定、不发亮出事件；无可选牌且不允许放弃时取消结算；私有草稿断言镜像扩展。
- **E3 drawPileTop 目的地**：`SkillProgramCardDestination` 加值（8，值唯一性有检查锁定）；`CardZones.PlaceDrawPileCardsAtTop`（镜像既有 AtBottom：要求恰为最近追加段，列表尾=牌堆顶，反转使 cardIds[0] 成为下一张被摸的牌）；moveBoundCards 解析白名单与 choiceIs 分支白名单加值（攻心置顶分支）；执行器移动循环改 switch 分派。
- **E4 组合校验的同名选择分支语义**：MoveCardSet 的"重复移动"检查区分 ChoiceIs 分支——同 choiceBind 的不同 optionId 分支可分区消费同一批原子（记录于 `Root.ChoiceConsumed`/`ChoiceOptionConsumed`），其余重叠仍拒绝（"repeat option branch"与"branch then unconditional"夹具双向锁定）。

## 内容与测试

- `src/CardGame.Content.Standard/SkillPrograms/classic-shen-lu-meng.rules.json`（schema 62，minimumRulesVersion 190）
- `src/CardGame.Content.Standard/SkillPrograms/classic-shen-lu-meng.presentation.json`（含攻心 optionLabels）
- `tests/CardGame.Core.Tests/ShenLuMengChecks.cs`（六项：定义与 schema、涉猎替换+回放、涉猎放弃保持正常摸牌、攻心弃置+回放、攻心置顶+回放（含置顶牌被下一手摸起的驱动断言）、无红桃放弃保持手牌隐藏；全部走 种子扫描+暂停回放双跑+事件/状态逐项对比，失败种子 continue 跳过不断言削弱）
- `CardSubsetSelectorChecks` 增 one-per-suit 检查（花色组合掩码/点数和逐一锁定）；`ProgramCompositionDefinitionChecks` 增 one-per-suit 分区 Accept 与三条 choiceIs 分支夹具（1 Accept + 2 Reject）+ reveal 解析两条 Reject（空 suits、无 suits 的 allowDecline）；`SkillProgramExecutorChecks`/`LiDianChecks` 随签名与记录更新
- 注册：`StandardClassicGeneralPackage.cs` 资源常量 + 懒加载目录 + AddSkill（涉猎可选触发器元数据 / 攻心主动元数据）+ AddGeneral（神，3体力，classic-5/8 池）+ CurrentGeneralIds；`GeneralGalleryCatalog.cs`"神"分组加 `shen-lu-meng`，WPF 图鉴检查如实断言三名成员
- 立绘：`sync_general_art.py` CLASSIC_HEROES 加 `"shen-lu-meng": 202`；`official-shen-lu-meng.png`（120201 经典形象，574×761）+ catalog 条目（sha256 153cf4b6…）

## 工作树意外删除与重放（诚实口径）

- 提交前，工作树目录 `Temp/card-batch-slm` 被外部进程整目录删除，当时全部改动未提交；分支 `batch/shen-lu-meng` 完好停在基线 4967d45。
- 以 `git worktree add` 在同路径同分支重建工作树（4482 文件检出于基线），随后按本次会话完整记录**逐文件重放**全部编辑（python `newline=""` 读写、CRLF 探测保持、每处替换 count==1 断言）；立绘 PNG 重新下载并生成同哈希 catalog 条目。
- 重放后全部门禁在同一工作树重新执行并全部复现（数字见下节）；重放中发现并修正两处重放引入的偏差（SelectProgramCardSubset 空引用修复前的 `List.AsReadOnly` 误用、涉猎检查的 before 取样时点早于开局弃牌），均以重跑定向检查确认。
- `--phase finalize` 的既有 NameError 按约束未触碰未使用。

## 验证结果

- Release 构建（`--artifacts-path Temp/Card-slm-artifacts`）：**0 error / 2 warning**，警告均为 `GaoDaYiHaoChecks.cs` 既有 CS8602（基线同现，非本批引入）。
- 定向检查（测试 exe `--filter`）：God Shen Lu Meng **6/6**；Li Dian 5/5；card subset selector **4/4**（含新增 one-per-suit）；composition 15/15；resource graph 1/1；Zhang Song 8/8；skill schema 1/1。
- 全量 Core（Release，最终提交前源码）：**644/644 全绿**（基线 637 + 本批 7 项：神吕蒙 6 + 子集选择器 1）。
- `dotnet format whitespace --include <21 个触碰文件> --verify-no-changes`：**exit 0**（LiDianChecks 两处基线遗留续行缩进一并修正）。
- 立绘 `--phase verify`：shen-lu-meng 条目校验通过后停在**既有** zhou-yu/201203 哈希漂移（基线同败，移交立绘负责方）。
- WPF：CardGame.Wpf.Tests 构建 0 error；`GeneralGalleryChecks` 神势力系列断言更新为三名成员（含 Kingdom=="神" 不变项）。全量 WPF 套件仍被既有"鬼龙斩月刀缺牌面"检查在早期中止（赵云批已录，主树同态，未代改）。

## 移交与遗留

- 既有（非本批）：GaoDaYiHao CS8602 ×2；zhou-yu/201203 立绘哈希漂移；WPF 套件既有鬼龙斩月刀失败；`sync_general_art.py --phase finalize` NameError。
- 版本分层：Checkpoint SchemaVersion 3 不变；`SkillProgramCatalog.RulesSchemaVersion` 保持 62；规则版本 188→190（189 为并行贾诩批预留，187 为主侧张郃/神赵云所耗）；经典包 1.158.0→1.160.0（159 留给并行批）。
