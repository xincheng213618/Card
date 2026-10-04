# Batch 27 · 当前普通 OL 界王异 621

只在本 owned stage 写入。未构建、未运行 C# loader、未测试或 benchmark。`assemble-old.py` 仅读取明确列出的 11 个主区文件，输出阶段内 before 字节、logical preview 和每文件单 Update 的物理顺序 patch。最终基线已按父代理确认的 636afb35（包含 620/640）刷新，保留 5400–5403、5402 ride、5500/5501 及其 owned/Dying 接线。五处枚举共 399 个既有成员数值逐项静态校验不变，新成员仅尾增。

官方完整正文及身份见 `docs/content/sources/fenglin-twenty-seventh-621-source-2026-10-04.json`，SHA `d1e82784d133cb26ad50ec8dbcf1e9a0acb4913bb1b06b54fc1f2c84acc932a6`。页面和 API 均当前 HTTP 200，两个技能无缺失 font 引用。API 没有 gender；`StandardClassicGeneralPackage.cs` 同人物王异明确 Female 为补充来源。当前两技能文本保持原文，包括贞烈两项前 NBSP。经典 `classic:zhenlie` / `classic:miji` 未改。

## 新能力与真实返回

| 能力 | 指令/窗口合同 | 实际状态归属 |
|---|---|---|
| 5600 PayHpThenNullifyOwnActualUseTarget | Owner 的 OtherActualUseTargeted；唯一实际 Use actor 为其他人；Slash 或普通锦囊 | CardUse → ActualUseTargetWindow → 当前 candidate Program；支付 1 HP 的标量 receipt 留在 Program。HP/Dying/救援/死亡孩子结束后才应用同 Use 本人目标无效。 |
| 5601 ScheduleEarnedActualEndingBenefit | 已付并应用 5600；ChoiceIs benefit/ending；准确引用另一个技能的 mandatory earned-ending 5602 | 标量公开承诺事实绑定原 actual turn、Paid source 与 Benefit source 的 instance/hash/binding。TurnEnding 真实 item 持有承诺，无平行 pending/map。每个承诺独立一次，自然秘计独立可选。 |
| 5602 DrawLostHpThenOfferOwnedCardsUpTo | Own optional 或 EarnedActualEnding mandatory；单指令、TurnEnding、Owner | 当前 Program 自持冻结 X、实际 draw ledger、paid ID 集合、last payment、paid/delivered 两计数。真实 HE→Processing→对方 Hand；每次 AwaitMovement 后原指令回返，不重复摸牌或移牌。 |

OtherActualUseTargeted=5600；EarnedActualEnding scope=5601；ActualUseTargetWindow frame kind=5600；新参与者资源 flag ActualUseSource=8192。枚举分属于各自命名空间，旧值不变。新 descriptor 自动加入已有 OperationCatalog，不新增人物 Host 分支或 runner。

Slash 入口位于实际目标调整完成后、原 BeforeTargetEffects 前。普通锦囊入口复用实际 JizhiResolution/原 use；返回直接调用原 typed target-effect continuation，避免再次开新窗口。旧神速 Action=null 的真实零实体杀从其成熟 UseVirtualCard typed producer 进入，捕获原 TargetsConfirmed 选择，不补写旧 Action。普通 Slash/Dodge Response 不进入；借刀第二“被杀”对象不是借刀直接指定目标。胜负已定的新 typed return 只清理原 Use 仍在 Processing 的实体（排除已领取实体）并回原父，不开启新无懈或卡窗。

## OLD 窄接线

| 文件 | 增量 |
|---|---|
| SkillPrograms.cs | 三 op、新窗口/Ending scope；新窗口 lifecycle/condition gates；PaidTargetEndingComposition 严格结构校验，保留现有各批校验。 |
| Resolution.cs | 新 frame 的 enum/JSON discriminator；Program 两个 nullable receipt，WhenWritingNull。 |
| ProgramLifecycle.cs | Context 两个 nullable scalar 身份/承诺；Ending item optional EarnedBenefit。旧 serialized fields 不变。 |
| ProgramEntryCapabilities.cs | 仅新窗口的 ActualUseSource capability；该窗口的 handler 支持。 |
| ProgramOperationDefinitions.cs | EventSource 的 RequireAnyContext 增量认新 capability，旧窗口资源不增。 |
| GameEngine.Runtime.cs | 新 frame push/advance；无 prompt 的新窗口推进；精确新 paid/gift Dying proof OR；Program Pop 前完成不可撤销 paid ineffectiveness；新 window typed completion。 |
| GameEngine.cs | 真实 finalized Slash 入口；exact owning subtree invariant；两个新 receipt assertion。 |
| GameEngine.CardActions.cs | 真实普通锦囊 target 前新窗口入口。 |
| GameEngine.ProgramTurnPhase.cs | 默认旧无 Action 虚拟 Slash 的 opt-in 新窗口；无匹配保持原路径。 |
| GameEngine.ProgramLifecycle.cs | 资格、选择、AI、Ending append/context/consume/skip；新窗口 source Kind 只读公开 AI 估值。 |
| ContentRegistry.cs | 5601 引用必须指向 exact mandatory earned-ending 单 5602，不允许错误技能/binding。 |

不编辑 Replay/schema/package versions，不编辑 committed view/event projector。新 public events 只包含标量及全标量 immutable records，无可变集合；因此无新事件集合分支。新 owning frame 的 Candidates/Contexts/Facts、gift PaidCardIds 在构造、init/with 与 JSON 参数反序列化时复制为只读集合。Facts 所有 9 个现有 collection 字段逐项复制，拥有证明仅比所需标量，不依赖含集合 record Equals。PlayerSnapshot 继续既有统一冻结。

局部 PaidTargetObserverEdge 复用成熟 movement/HP/Dying/Death/Peach/多材料/醇醪返回；Dying→Program 用 PolicyCounterspellDyingProgramRidesOn 严格候选/hash/token；九诗翻面用 PolicyCounterspellDyingFaceEdge。只有当前 Dying 完整入边证明成功后才采纳成熟 whole-tail rescue/alcohol proof。白银狮子首子帧额外锁定 exact LastPayment 原 Equipment(owner)→Processing 的 SilverLion ledger，再认同 root AwaitedProgramMovement 的 self Recovery / SilverLion RecoveryReplacement。赠牌两次移动后读取当前拥有帧再合并 receipt，保留原生追加的 PendingRecoveryAttempts；5602 局部 await 先开该 root 的 queued recovery/replacement，再 HP 和 movement，返回均为 AwaitedProgramMovement，避免旧通用 post-instruction 的 Program continuation 丢失实际支付归属。旧 generic observer edge/await helper 原样保留。

## 父代理登记草稿

Content package 调用 `BoundaryWangYiContent.Register(builder)` 一次，增加 `boundary:wang-yi` 一个完整模块；将两个 JSON 通过现有 EmbeddedResource wildcard 接入，现有测试 runner 登记以下四项一次：

| 建议 generic 检查名 | 方法 |
|---|---|
| Paid actual target HP and original use return | BoundaryWangYiChecks.PaidOtherUseNullificationOpaqueObtainAndActualSlash |
| Paid actual target Dying source loss and decline | BoundaryWangYiChecks.PaidDyingRescueSourceLossAndDeclineResumeOriginalUse |
| Earned actual Ending and lost HP gift budget | BoundaryWangYiChecks.IndependentActualEndingPromisesFrozenXAndOptionalHeGifts |
| Lost HP owned gift Dying native and strict contract | BoundaryWangYiChecks.LostHpGiftMovementDyingNativeAndStrictContracts |

Routine 仅选第一项建议 prefix `Paid actual target HP and original use return`。没有 WPF 改动、纯人物 definition snapshot、新 runner 或恢复历史测试。四方法固定 Seed=31、小实体 deck，在关键 HP、Dying、实体桃、暗手选择和 gift 暂停点真正恢复 JSON checkpoint 后改用返回的新 engine 继续命令。第一项另含正式已登记神速 Action=null 的实际目标成本/回返草稿。拒绝非法选择保存四个私密视角和完整实体 ledger 不变。native 使用正式角色选将权重与 Start/Advance，MaxTurns=6 允许 owner 第二个真实 Ending；赠牌 AI 通过本人视角 SimpleAi.ScoreProgramTarget 及本人 HE 保留值、公开目标手牌数评分，不调用读取他人实际隐藏 Role 的 AreProgramDistributionAllies。这是工程先验，均未执行。

工程默认与未证 FAQ 单列在 `engineering-defaults.md`。当前全部实际 loader、编译、behavior、cold restore 和 native 结果均未验收；死亡、跳过 Ending、extra turn、Jiushi/白银狮子交叉仅完成静态合同，不能称已过测试。

## 主线静态整合记录

2026-10-04：基于 636afb35 接入 9 个 NEW 文件、11 处 OLD 文件窄接线，冻结 manifest SHA-256 为 3f2f79fa8c40a508cc264c061decffe28a19e6e8a86dcd25d2fbb0b890cc9e01。OLD 与冻结预览按统一换行文本逐项一致，NEW 与原始源记录逐项散列一致。登记完整 boundary:wang-yi、四项行为检查和一项 routine 名称过滤。未执行构建、生产 loader、测试、基准或离线资源验收；用户睡醒后统一验证。未因普通人物增量提升全局规则、schema 或内容包版本。
