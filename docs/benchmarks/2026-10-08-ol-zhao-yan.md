# 赵俨单武将批次

当前 OL 赵俨（魏势力、4 体力、男、称号"刚毅有度"，官网 511 号，璀璨星河-虎贲包、史诗品质、2022-09-02 上线）接入：同协（出牌阶段开始时，你可以令你与至多两名其他角色直到你的下回合开始称为"同协"角色，然后令其中手牌唯一最少的角色摸一张牌。当同协角色使用仅指定单一目标的【杀】结算后，其他同协角色可以依次对目标使用一张无距离限制的【杀】。当同协角色受到伤害时，本回合未失去过体力的其他同协角色可以防止此伤害并失去1点体力。）。官方立绘 51100 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-zhao-yan-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查）。

| 技能 | 实现口径 |
| --- | --- |
| 同协 | tongxie-arm：playPhaseStarting 可选触发 → 新 op `tongxieArm`（7292）弹出"你+至多两名其他角色"的组合私有提示（全部单人 + 全部双人组合，按座位序稳定排列）；接受的成员座位进公开事件 `ProgramTongxieArmedEvent`（成员集合公开，无私密信息），随后当场找出手牌唯一最少的同协角色摸一张牌，并列最少（含全员 0 张并列）则无人摸并记日志。tongxie-follow-up：discardPileReceived 强制触发 → 新 op `tongxieFollowUp`（7293）。"当同协角色使用仅指定单一目标的【杀】结算后"复用逊贤建立的既定用牌账目（`SettledActionCardDiscardEvent`，批次完成时在用牌帧存活期捕获），按候选的 movementIndex 钉住本链的杀，再经冻结的 `CardActionAcceptedEvent` 回溯使用者与唯一目标；其他同协角色按从使用者开始的座位顺序依次收到私有提示（每张手牌杀一个选项 + 放弃），接受的杀经 `ResolveSlashCore` 作为完整无距离限制使用结算（子结算挂起，`ReexecuteParticipantInstruction` 重入），链上证据进 `ProgramTongxieFollowUpResolvedEvent`。"不因此技能使用的"经事件 `UsedCardIds` 排除：链上被使用的杀永不重开新链。tongxie-guard-other / tongxie-guard-self：beforeDamageApplied 两条强制触发（owner=拥有者视角 / damageTarget=成员受害者视角）→ 新 op `tongxieGuard`（7294）：本回合未失去过体力的其他同协角色（相对受害者）按受害者之后的座位顺序依次收到私有提示，第一名接受者经 `PreventProgramCurrentDamage` 防止此伤害并失去 1 点体力，致命代价的濒死作为子结算在窗口内完成后回到护法指令收尾；证据进 `ProgramTongxieGuardedEvent`。 |

## 共享能力扩展

- 新增 EffectOp 7292–7294（tongxieArm / tongxieFollowUp / tongxieGuard，只占用分配的 7292–7299 段）；描述符按反射目录自动注册；AI 语义复用既有 GainCards / RequestSlashByTarget / PreventCurrentDamage，未新增 `ProgramOperationAiSemantic` 成员。未新增 PlayerMarkerKind、ConditionKind、触发窗口枚举或 schema 节点；AI 选择器走 `ResolvePendingAiProgramTrigger` 的既有分发点。
- `TracksSettledActionCards` 开关从逊贤单操作扩为 `HasTriggerOperation(XunxianGiftUsedCard) || HasTriggerOperation(TongxieFollowUp)`：既定用牌账目（`SettledActionCardDiscardEvent`）成为按操作注册发现的共享 completed-use 台账；开关关闭时 `CaptureSettledActionCards` 完全跳过，既有内容指纹不受影响。
- `MatchingDiscardPileIndexes` 增加 TongxieFollowUp 操作分支（`MatchingTongxieSettledSlashIndexes`）：与逊贤的 own-settled 匹配器并列的操作专用匹配器，绕过通用弃牌过滤器——use-finished 完成式原因被 `GetProgramDiscardSource` 通用过滤器显式排除，既定账目是唯一合法的 completed-use 触发通道。
- `AssertParticipantReserveDraft` 的 `ReexecuteParticipantInstruction` 白名单追加 TongxieFollowUp / TongxieGuard：跨命令边界的"逐成员提示 + 子结算"操作按参与者游标机制重入（与 RequestSlashByNearest 同族），指令游标在子结算期间保持已提交位置。
- `AssertCoreInvariants` 的 before-damage 窗口观察者链追加 `HasTongxieGuardDying`：致命护法代价的濒死子进程以 `DyingFrame.ParentFrameId` 精确钉回护法程序帧，窗口保持其伤害上下文；谓词不放宽任何既有观察者的条件。

## 边界口径

- 成员集合：arm 接受"你 + 至多两名其他角色"（组合提示只含单人与双人组合，不含纯放弃以外的混合形态）；成员身份公开（事件与日志）。持续到拥有者的下回合开始（arm 事件与拥有者 `TurnStartedEvent` 的最后先后判定到期）。多个赵俨各自维护自己的成员集合与账目，互不重叠。
- 摸牌：arm 结算时点比较所有同协角色手牌数，唯一最少者摸一张；并列最少（含全员 0 张）无人摸并记日志——"唯一最少"逐字执行。
- 追杀触发：同协角色使用、仅指定单一目标、结算完成后。经既定账目钉住的杀还要求当前仍在弃牌堆；链杀结算完成的弃牌批次同样进入窗口，但"不因此技能使用的"由 `UsedCardIds` 排除重开。versionBoundary：可回溯的使用要求单一目标且单一实体牌（`CardActionAcceptedEvent` 回溯），多目标杀与多牌转化杀不触发；响应类打出（如决斗/借刀的杀）不是"使用"亦不触发。暗手牌 id 不进任何事件。
- 追杀询问：响应方 = 除使用者外的其他同协角色（目标本身为成员时因不能对自己用杀自然排除），按从使用者开始按座位距离依次询问；每个成员看到自己当时手牌的全部杀（无距离限制）与放弃；列表在窗口开始冻结，中途失去杀的成员静默跳过；接受者连续追问直到全员表态。接受的杀是完整使用：可被闪抵消、计入伤害与链证据。
- 护法：任一同协角色受到伤害时进入窗口；候选 = 本回合未失去过体力的其他同协角色（"其他"相对受害者；受害者为自己时即 guard-self 触发），按受害者之后的座位顺序依次询问，第一名接受者结束询问。防止的是本次窗口的全部伤害量（`PreventedAmount` 记录），随后失去 1 点体力；该体力流失计入"本回合失去过体力"（`ProgramSkillHpLostEvent`），同一成员本回合不会再被询问。致命代价的濒死在窗口内完成后护法照常收尾。
- versionBoundary：赵俨死亡后其未决追杀/护法不再结算（程序触发统一要求拥有者存活，平台既有边界）；成员中途死亡按冻结名单静默跳过；多赵俨同局时各链独立、互不重开（账目与 `UsedCardIds` 按各自事件过滤）。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查。
- Release 构建（`dotnet build CardGame.sln -c Release` 全解决方案）：0 error；20 条 warning 全部为基线既有文件（tests/ 既有 CS8602/CS9113/CS8604/CS0169，及 SkillProgramExecutor.cs、GameEngine.ActualHandGainPrograms.cs、GameEngine.EndingHistoricalUses.cs、GameEngine.LiangXingPrograms.cs、GameEngine.OwnedDeathBenefitReturns.cs、GameEngine.PublicPilePreparation.cs、GameEngine.SameTypeActualUseAid.cs），本批新文件无任何 warning。
- 内容静态校验：`tools/Inspect-SkillProgram.ps1` 对 ol-zhao-yan.rules.json + presentation.json 解析通过（EXIT:0），四个触发器（tongxie-arm / tongxie-follow-up / tongxie-guard-other / tongxie-guard-self）均按描述符目录解析成功。
- 运行期冒烟（临时控制台工程引用构建产物，5 种子 × 14 回合的完整命令流）：5/5 局完整收局，无断言失败；arm + 成员宣布、手牌唯一最少摸牌（含并列无人摸）、追杀链（成员与赵俨本人使用、闪抵消后链正常收尾）、护法（他人受伤、自保、致命代价濒死）全部观察到位。
- 例行 `tools/Test-Changed.ps1`（无过滤，ArtifactsPath 独立目录 `artifacts-zhao-yan`）：构建 15.9s 通过、Core 214 项过滤联合执行 118.3s（266 通过 / 57 失败 / 0 跳过，共 323 项）、WPF 18 通过 / 0 失败（17.3s）。57 项失败名单与严畯批基线（`yan-jun-baseline-fails.txt` 实测名单）逐项 diff 完全一致（byte-identical），无新增失败；全部失败均为基线既有的在制特性检查（狂斧、离魂、强武、源咒、SP 群雄等），无任何赵俨相关失败。
- 本批在独立 worktree（batch/ol-zhao-yan）开发，与杨婉、唐姬等批次并行。
