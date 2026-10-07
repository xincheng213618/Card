# 王双单武将批次

当前 OL 王双（魏势力、8 体力、男、称号"遏北的悍锋"，官网 490 号，璀璨星河-天柱包、史诗品质、2022-02-12 上线）接入：追猎（锁定技，你使用【杀】无距离限制。当你使用【杀】指定攻击范围外的角色为目标后，此【杀】无次数限制且你判定，若为武器牌或坐骑牌，此【杀】伤害值等于其体力值，否则你失去1点体力。）。官方立绘 49000 已离线入库并登记图鉴/映射。来源见[来源档案](../content/sources/ol-wang-shuang-2026-10-08.json)。按用户指令，本批不含任何新增行为检查（未新建测试文件、未改动 tests/、未注册检查），唯一门槛为 Release 全解决方案构建。

| 技能 | 实现口径 |
| --- | --- |
| 追猎 | 静态口径①：技能 `modifiers` 声明 `query=slashDistanceLimit`、`operation=unlimited` 的**无身份**静态修饰符，经新增 `IgnoresSlashUseDistance` 消费点在杀的使用距离判定处生效（持久，不随回合过期；攻击范围本身不变，"攻击范围外"的目标仍然存在）。触发口径②：`zhui-lie-outside-range` 在 `cardUseTargetsFinalized`、`ownerRelation=actor`、杀族三类牌上锁定强制触发，触发条件 `compare(ownerEventTargetDistance > currentAttackRange)`（逐字"指定攻击范围外的角色为目标后"），并复用 final-target-Slash 的逐目标绑定（多目标杀按最终目标逐个结算）。效果序列：`refundCardUseDebit`（"此【杀】不计入次数"——出牌阶段内本人使用被记账的杀额度即时返还；回合外/非出牌阶段使用本无记账则空操作）→ `startJudgment`（resultBind `zhui-lie-judgment`，公开）→ 新 op `zhuiLieEscalateTargetDamage`（7308）：判定牌命中"武器牌或坐骑牌"白名单（按 `EquipmentCatalog` 槽位核对：武器 16 类 + 坐骑 7 类；防具与宝物不属）时发"抬升回执"（`ProgramTargetSlashReceipt.EscalateToTargetHp`，与固定加值/防止取消回执同族，去重与生产者身份校验一致），伤害结算时经 `FinalTargetSlashEscalationBonus` 把本次【杀】伤害抬升至目标当前体力值（低则抬至、高则不变，即"增至"口径），证据进标量事件 `ProgramTargetSlashDamageEscalatedEvent` → `loseHp` 1（判定牌非武器/坐骑分支，`not(boundCardsMatchKinds)` 条件）→ `moveBoundCards` 判定牌进弃牌堆。 |

## 共享能力扩展

- 新增 EffectOp 7308（`ZhuiLieEscalateTargetDamage`，只占用分配的 7308–7315 段）；描述符 `ZhuiLieEscalateTargetDamageDescriptor : FinalTargetSlashDescriptor` 按反射目录自动注册，复用 final-target 回执签发通道（`IssueFinalTargetSlashReceipt`），`FinalTargetComparison` 固定 `Always`；AI 语义继承 final-target-Slash 族（`ProhibitCurrentResponse`/`FinalTargetSlashValue`，抬升回执 DamageBonus=0 时估值为中性——触发锁定强制无选择点）。未新增 `ProgramOperationAiSemantic`、PlayerMarkerKind、ConditionKind、触发窗口枚举或 schema 节点。
- `ProgramTargetSlashReceipt` 增加可选 `EscalateToTargetHp` 标记（默认 false；既有回执与已存档事件不受影响）；`HasFinalTargetSlashEffects`、`ReceiptProducerMatches`、`AssertFinalTargetSlashReceipts` 的回执族校验同步覆盖抬升回执；`FinalizeAttackDamageAmount` 在既有伤害加分合计后计算抬升（`max(0, 目标当前体力值 − 既有伤害)`），白银狮子/被动伤害上限等既有封顶在抬升后照常收敛。
- `SkillPrograms.cs` 校验放宽两处（均为纯放行，既有内容全部仍合法、行为不变）：① `CurrentAttackRange` 触发比较值放行 `cardUseTargetsFinalized` 窗口（该窗口事实本就冻结 `GetAttackRange`，此前仅校验名单未含）；② `slashDistanceLimit unlimited` 静态修饰符不再强制要求 `sourceCardIdentityId`（身份绑定修饰符照旧走 `IgnoresProgramSlashDistance` 转换通道）。
- 静态无身份 `slashDistanceLimit unlimited` 的消费点：新增 `IgnoresSlashUseDistance`（回合级 turn modifier ∨ 静态无身份修饰符），替换 6 处杀使用距离判定中原本只读回合级修饰符的位置——`CanUseSlashTarget`、`CanUseVirtualSlashTarget`、`CanUseProvidedSlashTarget`（GameEngine.cs）、`CurrentSlashFireOffers`、`NearestLegalSlashRequests`、`SameTypeActualUseAid`。无身份修饰符在既有内容中不存在，该扩展对既有内容是恒等变换。

## 边界口径

- 无距离限制是"使用距离"口径，不改攻击范围：攻击范围照常参与口径②的范围外判定与"伤害增至体力值"，也与麒麟弓等按射程结算的效果一致。
- 触发与逐目标：多目标【杀】（方天画戟等）按最终目标逐个触发，每个范围外目标独立"不计次数 + 判定 + 抬升/失去体力"；范围内目标不触发。锁定技强制触发，无 AI 选择点。
- 不计入次数：仅返还本杀被记账的出牌阶段杀额度（`refundCardUseDebit` 按冻结用牌上下文的记账身份返还一次）；回合外、非出牌阶段、或本就免记账的使用自然不返还。
- 判定与抬升：判定在目标指定后当场进行（可被改判，取最终判定牌）；"武器牌或坐骑牌"按 23 类白名单逐类判定。抬升读伤害结算时目标当前体力值（目标指定后至伤害结算之间目标体力无变化窗口）；"增至"不降低既有伤害：伤害已不低于目标体力值时抬升量为 0；抬升与酒/裸衣等既有加算共存，白银狮子等封顶仍在最终结算收敛。
- 失去体力分支：判定牌非武器/坐骑时失去 1 点体力（程序 hp 管线 `loseHp`，可触发遗计等失去体力响应、计入濒死）。
- versionBoundary：判定牌被替换时按最终判定牌分支；王双死亡后其未决触发不再结算（程序触发统一要求拥有者存活，平台既有边界）；多王双同局各自技能实例独立记账互不干扰；暗手牌 id 不进任何事件或快照。

## 验证

- 按用户指令本批不含新增行为检查：未新建测试文件、未改动 tests/ 下任何文件、未注册检查；除下述构建外未运行任何检查/测试/模拟对局（含 `tools/Inspect-SkillProgram.ps1`——其内部构建 tests 工程，按指令未使用）。
- Release 全解决方案构建（`dotnet build CardGame.sln -c Release`）：0 error。warning 共 20 条，全部为基线既有文件：Core 9 条（SkillProgramExecutor.cs、GameEngine.ActualHandGainPrograms.cs、GameEngine.EndingHistoricalUses.cs、GameEngine.LiangXingPrograms.cs、GameEngine.OwnedDeathBenefitReturns.cs ×3、GameEngine.PublicPilePreparation.cs、GameEngine.SameTypeActualUseAid.cs，与赵俨批基线名单一致；SameTypeActualUseAid 的警告在其 178 行，非本批改动的 89 行），tests/ 既有 11 条（CS8602/CS9113/CS8604/CS0169，均为既有测试文件）。本批新建/改动文件无任何 warning。
- 静态自证（构建产物层面）：`CardGame.Content.Standard.dll` 内嵌资源含 `ol-wang-shuang.rules.json` / `ol-wang-shuang.presentation.json`，`ol:zhui-lie` 与 `zhuiLieEscalateTargetDamage` 均已编入。JSON 形状逐节点对照既有内容（trigger 属性集、compare 条件对照清河公主-增苟、boundCardsMatchKinds 对照朱桓/刚烈、startJudgment/moveBoundCards 对照刚烈、modifiers 对照武神）静态核对。
- 内容语义与既有机制的等价性核对：抬升回执复用赵俨/黄祖批已验收的 final-target 回执链路（签发去重、生产者身份、断言、事件），静态杀距离修饰符复用武神已验收的 `slashDistanceLimit unlimited` 形态（去身份绑定）。
- 本批在独立 worktree（batch/ol-wang-shuang）开发，与刘宏、田豫等批次并行。
