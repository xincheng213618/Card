# RUNTIME V67 —— 规则 190 / 经典包 1.161.0

本版本号为贾诩批次（完杀 / 乱武 / 帷幕）使用。Checkpoint schema 仍为 3；规则版本 189 → 190，经典包 1.160.0 → 1.161.0。技能 JSON schema 62 不变（本批全部为加值枚举与可选节点）。旧规则的检查点按现有版本边界拒绝恢复。

## 贾诩批次：完杀 / 乱武 / 帷幕

单武将交付：贾诩（群，男，3 体力，神话再临·林 2010，最低规则 190）。完杀为既有共享能力的纯配置消费；乱武与帷幕各新增一项公共能力。

### 新增公共能力

1. **操作 `requestSlashByNearest`（枚举值 409，参与者/私库操作族）**：描述符沿用 `ParticipantReserveDescriptor`（target 必须 owner、amount 1..64、targetKind 限 anyLiving/otherLiving），并追加该操作的专属约束——targetKind 必须 otherLiving、amount ≥ 1、`secondaryAmount` 必须等于 `amount`（惩罚值在挂起应答后按效果原值重放，首/次游标分叉会使挂起前后惩罚不一致）。`Interaction = Choice`，处理器经 `ParticipantReserveHandler` 反射注册。语义：从拥有者下家起沿行动顺序逐名处理其他角色，每人须对“距离最近的另一名角色”使用一张【杀】，否则失去 amount 点体力。
   - 最近集合 = 排除自身、按 `GetCombatDistance` 取最小距离的存活角色升序（含拥有者本人），并列时由该角色任选其一；距离口径与攻击范围无关，强制作出的【杀】不设攻击范围限制、不计入本回合【杀】次数。
   - 应答帧：选项 = 每张手牌【杀】×每个最近座位（组合卡+目标，私有提示），另给“不使用【杀】→失去体力”弃疗分支。无合法【杀】或无可选目标时直接失去体力，不发布提示。
   - 使用【杀】走 `ResolveSlashCore(countsTowardSlashLimit: false, programSkillCardUseFrameId: frame.Id)`，由既有 program-skill 子帧收束路径续接游标；失去体力走 `ProgramSkillHost.LoseHp`，其濒死子帧按 `DyingContinuation.ProgramSkill` 续接。挂起期间 `ReexecuteParticipantInstruction` 保持为真并计入 `AssertParticipantReserveDraft` 白名单；应答侧重验座位、帧号、牌仍在该座位手牌、目标仍属最近集合、双方存活与技能实例有效。AI 取首张合法【杀】以保持确定性。
   - 座位序特化：`ExecuteParticipantReserve` 对本操作使用“拥有者下家起”的环绕序，其余参与者操作仍按座位序。
2. **卡牌策略 `prohibitTargetBySuit`**：按花色的目标资格屏蔽。解析要求单一 `inputSuit`、禁 `outputSuit`/`value`/`requiredCardKinds`，`cardKinds` 非空且全属指向性锦囊族（duel/dismantlement/snatch/fireAttack/ironChain/borrowedSword/indulgence/supplyShortage/barbarianAssault/arrowBarrage/peachGarden/fiveGrains）。`IsCardTargetProhibited` 增可选 `Suit?` 参数并叠加该策略查询，屏蔽在三处生效：
   - 出牌合法动作漏斗：按动作实体牌物理花色解析 declaredSuit；**AOE 四类（南蛮/万箭/桃园/五谷）由 `.All` 改为 `.Any` 逐目标剔除语义**——受 shield 的目标单独退出结算，整张牌仅在无人可选时才作废；其余指向牌维持 `.All`。
   - 枚举点早期过滤：物理/转换决斗枚举、程序“全手牌当普通锦囊”选项、程序“两同花色手牌当万箭”目标集。多牌虚拟牌仅当全部物理牌同花色时携带该花色，混色视为无花色。
   - 结算期目标重算：`ResolveGroupCard`/`ResolvePeachGarden`/`ResolveFiveGrains` 在既有 `ExcludeGlobalTarget` 旁并上花色屏蔽，使结算帧目标集与出牌期声明一致。
   - “黑色”按物理花色判定（与仁王盾同一口径）。回归安全性：现有内容的 `prohibitTarget` 仅覆盖杀/决斗/顺手/乐不思蜀，`forbidTarget` 仅杀族，AOE 语义切换对既有内容零影响。

### 消费方

- 完杀：cardPolicies `exclusiveDyingPeachRescue`（cardKinds [peach]，最低规则 189）——复用 `CanUsePeachToRescue` 既有三态放行（当前回合拥有者 / 濒死者本人 / 拥有者已死亡）。
- 乱武：activation `force-nearest-slash`（minCards/maxCards 0、minTargets/maxTargets 0、targetKind otherLiving、usesPerGame 1）→ `requestSlashByNearest`（owner/otherLiving/amount 1）。
- 帷幕：cardPolicies 两条 `prohibitTargetBySuit`（黑桃、梅花各一条，12 类指向性锦囊）。

## 验证口径

- 定向检查 `JiaXuChecks`（4 项，注册入口 `Program.cs` “2010 Jia Xu …”）：定义与策略 schema（含 9 条解析拒收面）、乱武最近强杀族与回放一致、乱武无杀失体力与行动顺序及回放一致、帷幕黑锦囊差异（黑南蛮/黑决斗从不指向贾诩 + 红南蛮恒指向并受伤）。
- 共享检查：`composition kernel descriptor contracts` 已覆盖新操作的 descriptor/parse/handler 三方一致性。
- 同一组断言另以磁盘直挂夹具（不经经典注册表，只注册本批三技能与四名无技对手）独立复验通过，用于在并行批次阻断经典注册表/测试工程期间确认引擎行为。
- 如实记录：截至本版本落笔，并行 2011 批次的 `Fame2011FaZhengChecks.cs` 引用尚不存在的 `ProgramSkillStartedEvent.TriggerId`，测试工程整体编译失败，故本批未产生全量 Core 数字；已在该文件之外的同批改动上确认零编译错误与零新警告。批次记录见 [2026-09-30-jia-xu](../../benchmarks/2026-09-30-jia-xu.md)（含并行在制面对全量验证的阻塞归因）。
