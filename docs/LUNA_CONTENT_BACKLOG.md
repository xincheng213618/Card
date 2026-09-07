# Luna Max 长期内容目标与动态 Backlog

本文件是 Luna 长期 goal 的施工入口。用户已将 Card 项目的后续迭代统一交给 Luna；Core、内容、WPF、测试和文档在同一个 goal 中按稳定阶段推进，不创建重复 goal。

## 1. 文件所有权

当前 goal 可修改整个 Card 工作区；以下目录仍作为内容批次的默认落点：

```text
src/CardGame.Content.Standard/**
src/CardGame.Modes.Identity/**
src/CardGame.Modes.Team2v2/**
src/CardGame.Modes.NationalWar/**
tests/CardGame.Content.Standard.Tests/**
tests/CardGame.Modes.Tests/**
tests/CardGame.Scenarios/**
docs/content/**
```

核心/UI 文件也可在本 goal 内修改，但必须保留职责边界：

```text
src/CardGame.Core/**（命令、Registry、结算等核心演进）
src/CardGame.Wpf/ViewModels/**（只做适配和观察能力）
docs/CORE_CONTROL_DESIGN.md（同步实际契约）
```

仍须保持的边界：Core/UI 解耦、AI 只读脱敏玩家视角、内容不反射修改引擎私有状态；如果某能力尚未提供，记录到 `docs/content/CORE_API_REQUESTS.md`，不要用全局状态或 UI 特判绕过。

当前 `CardGame.Core/Content.cs` 作为兼容投影保留；K3 已新增 `CardGame.Content.Standard` 正式内容包和不可变 Registry，后续内容优先进入命名空间定义，不继续扩大 Core 的封闭枚举。

## 2. Backlog 状态

状态约定：

- `BLOCKED(Kx)`：等待核心 API 等级；
- `READY`：可以在当前接口上实现；
- `ACTIVE`：Luna 当前正在做；
- `DONE`：实现、测试、文档和 Demo 验证全部完成。

## 3. 内容批次

### C0 内容盘点与数据准备 — DONE（2026-09-07）

- 整理标准牌、装备、锦囊、武将、技能的 manifest；
- 为每项分配命名空间 ID、名称、类别、简述、AI 标签；
- 建立不依赖运行时行为的牌堆配方草案；
- 标注每项依赖的核心时机、Query、Effect、Prompt 或 Mode 能力；
- 不提前把未支持的行为塞入 `GameEngine switch`。

完成物：

- `docs/content/CONTENT_MANIFEST.md`：标准牌、牌堆、武将、技能、锦囊、装备和 AI 标签清单；
- `docs/content/SCENARIO_MATRIX.md`：正向、拒绝/边界、视图和确定性场景矩阵；
- `docs/content/CORE_API_REQUESTS.md`：按 K1/K2/K3/K4/K5/K6/K7/K9 分组的核心扩展请求。

当前 C0 的历史清单和测试契约已完成；正式运行内容包已在 C0-K3 落地，K4 的模式开局切片已开放，复杂结算行为仍按 K5-K7 的开放等级推进。

### C0-K1 卡牌移动契约包 — DONE（2026-09-07）

核心已通过同一长期 goal 正式开放 K1。本批只消费牌区与移动契约；当时不创建正式 Registry、不接入运行时内容，作为历史阶段记录保留。

- 为当前基本牌、已实现技能和后续标准牌/锦囊/装备补充 `From → Processing → To` 生命周期；
- 对齐 K1 已有的 `CardMoveReason`，并为 K2/K5/K6 内容记录建议的 namespaced reason ID；
- 明确普通玩家视图、可信宿主移动账本和 `CardMoved` 诊断通知的边界；
- 写入杀命中、闪避、奸雄取得、桃、发牌/摸牌、手牌上限弃置、死亡清区（K1 手牌；装备/判定区目标分别在 K6/K7）、主公惩罚和重洗的强制场景规格；
- 每个场景都要求牌区守恒、非法来源/目标不得部分提交、固定 seed 可重放和视图不泄漏。

完成物：

- `docs/content/CARD_MOVEMENT_CONTRACT.md`：生命周期、reason、可见性、内容映射和 K1 验收规格；
- `docs/content/SCENARIO_MATRIX.md`：新增 `C0-K1 强制移动场景` 映射和执行顺序；
- `docs/content/CORE_API_REQUESTS.md`：记录 K1 正式通知、稳定语义和后续 K2/K3/K5 边界。

本批验证：契约中的 31 个 `standard:*` ID 均存在于 manifest，9 个 K1 强制场景和 11 个 K1 reason 均齐全；当前 Core/Core.Tests Release 编译通过、Console 30/30、隔离 WPF Release 编译 0 警告/0 错误、format gate 通过。

### C0-K2 命令与询问契约包 — DONE（2026-09-07）

- 为出牌、结束出牌和闪响应补充 `PromptId`、发布 Revision、完整 `PromptChoice` 以及旧 WPF 兼容映射；
- 覆盖过期 Revision、错误响应者、错误 Prompt、未发布 Choice、错误卡牌和错误目标的零状态变化场景；
- 验证真人与兼容 API 都经过同一提交边界，AI 仍只接收自己的脱敏快照。

完成物：`PendingDecision` K2 字段、`GameCommand`/`CommandResult` API 和 Core Console 命令边界测试。

本批验证：37/37 Console 检查通过；新命令拒绝测试覆盖 stale revision、精确目标、Prompt/Choice 和派发期重入。

### C0-K3 标准 Registry 包 — DONE（2026-09-07）

- 新增 `CardGame.Content.Standard`，注册 `standard:*` 基本牌、技能、武将和牌堆，以及 `identity:standard-8` 模式元数据；
- 验证 Registry 的包依赖拓扑、重复 ID、未知引用、版本不足、依赖环和独立只读投影；
- WPF 通过 Standard Registry 创建对局，Core 的旧静态内容目录只作为兼容路径。

本批验证：37/37 Console 检查通过，完整解决方案隔离 Release 构建 0 警告/0 错误，`dotnet format --verify-no-changes` 通过。

### C0-K4 模式开局与私有选将 — DONE（2026-09-07）

- `ContentModeDefinition` 现在携带牌堆 ID、候选数量和共享武将池；Standard Registry 注册 5/8 人身份模式；
- `GameOptions.UseInteractiveSetup = true` 开启座次/身份/选将/公开/洗牌/逐轮发牌的可暂停 Core 流程；
- `SelectGeneralCommand` 使用私有 `PendingDecision.ValidContentIds` 和 `PromptChoice.ContentIds`，共享池按选中结果移除，禁止重复武将；
- AI 选将只读取本座 `GameSnapshot` 与本座候选，评分保留在可信宿主 `AiGeneralThoughts`；WPF 已提供候选按钮；
- 选将结果在所有座位完成后才公开，初始牌按逐轮顺序发放；旧固定构造通过 `UseInteractiveSetup = false` 保留兼容。

本批验证：53/53 Console 检查通过；覆盖 8 人真人私有候选、5 人全 AI 选将/整局终止、共享池无重复、非法候选零状态变化、普通/火/雷杀类型化伤害、无中生有无目标摸牌、南蛮入侵/万箭齐发逐目标私有响应、桃园结义逐目标恢复、五谷丰登公开牌与私有选牌、过河拆桥隐藏手牌盲弃、顺手牵羊距离一隐藏手牌转移和同 seed + 同命令确定性；完整解决方案隔离 Release 构建 0 警告/0 错误，format gate 通过。

### C1 标准基本牌 — ACTIVE（K3/K5 切片已开放，扩展行为等待后续阶段）

- 杀、火杀、雷杀、闪、桃、酒、决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊的正式 Registry 定义已完成；兼容引擎行为仍由 Core 提供；
- 酒的出牌阶段一次性直接杀 +1 伤害和濒死者自救 1 点体力已完成；酒效公开进入 `PlayerSnapshot.HasAlcoholEffect`，直接杀声明时消费，回合结束未消费则失效；酒在濒死响应中只允许持有者自救，不能救援他人；
- 普通杀及属性杀的内容定义与 `DamageNature` 最小伤害类型已完成；属性抗性、属性转化和铁索连环仍待后续结算阶段；
- AI 使用/响应/保留价值；
- 场景：出杀、打闪、普通/火焰/雷电伤害、濒死自救、他人求桃。
- 场景：出杀、打闪、普通/火焰/雷电伤害、酒效一次性加伤、酒效回合结束失效、酒濒死自救、他人求桃。
- 本批增量验证：55/55 Console 检查通过；覆盖酒的无目标使用、处理区生命周期、公开酒效、直接杀伤害金额贯穿伤害帧/事件/技能上下文、回合结束失效、濒死者私有酒自救 Prompt、类型化濒死响应/恢复事件、非法选择零状态变化、AI 仅使用自己的快照，以及 70 张牌守恒；完整解决方案隔离 Release 构建 0 警告/0 错误，format gate 通过。

### C2 第一批规则覆盖型武将 — ACTIVE（K5 最小伤害后触发已开放，其余技能仍等待）

优先选择能验证核心扩展点的技能，不按武将数量堆叠：

- 摸牌 modifier：英姿；
- 次数 modifier：咆哮；
- 目标禁止：空城；
- 牌转换：武圣红牌按杀已完成；龙胆的杀/闪互转已复用响应型牌转化入口，并覆盖 AI、精确 Prompt 与 WPF 响应选择；
- 伤害后触发：奸雄、反馈、遗计、节命、援护；刚烈仍待后续的判定/伤害后效果入口；
- 其中反馈已完成存活伤害后的私有可选触发最小切片：Core 先冻结排序后的候选并压入可序列化 `DamageTriggerWindowFrame`，每个可选候选再进入 `DamageSkillFrame`；遗计已在同一帧栈上完成受伤后私有摸两张牌，并从合法牌/其他存活目标组合中将一张牌交出的真实跨手牌效果；节命已复用同一帧栈，按公开存活状态、手牌数量和体力上限发布私有目标 Choice，并将牌补至目标上限。候选收集已通过 `CanTriggerAfterDamage` 逐个检查当前存活拥有者，但现有内置技能默认仍只参与受伤者窗口；通用非受伤者触发语义和刚烈仍待后续入口。
- 判定修改：鬼才（等 K7）；
- 出牌主动技能：仁德、制衡、苦肉；
- 距离/范围：马术、奇才（等 K6）；
- 多人/主公询问：激将（较晚批次）。

每个技能至少包含发动/不发动、边界条件、死亡/失效和确定性场景。

本轮新增 `standard:yuanhu`：援护者在其他角色受到正伤害后，沿同一 `DamageTriggerWindowFrame` 获得私有 `Yuanhu` 弃牌 Choice；发动时将一张自己的手牌按 `skill.yuanhu.discard` 经过 `Processing` 移入弃牌堆，并由 `RecoveryFrame` 令固定受伤目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 与 `DamageSkillResolvedEvent.EffectTargetSeat` 只向可信宿主提供完整证据；这是当前唯一显式 opt-in 的非受伤者触发示例。

当前 Feedback/Yiji/Jieming 切片的边界是：Core 先按 `DamageTriggerOrdering` 稳定排序并压入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前可选候选压入 `DamageSkillFrame`。反馈只向反馈者发布私有 `Feedback` Choice，发动才将伤害牌移入技能拥有者手牌并发布 `DamageCardClaimedEvent`，不发动则进入弃牌堆；遗计只向受伤者发布私有 `Yiji` Choice，先将两张牌从 `DrawPile` 摸入其手牌，发动时按精确牌/其他存活目标组合将一张牌移给目标并发布 `DamageSkillCardGivenEvent`，不发动则两张牌留在其手牌；节命只向受伤者发布私有 `Jieming` Choice，按公开合法目标缺口从 `DrawPile` 摸牌至目标体力上限，并以 `DamageSkillCardsDrawnEvent.TargetSeat` 和 `skill.jieming.draw` 移动 reason 记录目标。三条路径都保留候选标识、支持群体父帧续接且不向普通快照泄漏私有取得牌 ID；候选收集虽已开放逐个存活拥有者的 `CanTriggerAfterDamage` 时机钩子，现有内置技能仍默认只参与受伤者窗口，通用非受伤者触发、多伤害嵌套和其他复杂伤害后技能仍待后续阶段。

武圣切片的边界是：红色且不是原生杀/火杀/雷杀的实体牌，可以在合法目标上生成有效牌型为 `Slash` 的动作；`LegalAction.PlayedCardKind`、`PlayCardCommand.PlayedCardKind` 和 WPF 独立按钮负责消除原生使用与转化使用的歧义。事件/伤害链记录有效 `Slash`，`CardMovementRecord` 仍记录原牌的实体 ID/物理 `CardKind`，不复制牌；AI 只从脱敏视角的红牌和合法动作中评分。

龙胆切片的边界是：物理闪可在出牌阶段生成有效 `Slash`；物理杀/火杀/雷杀可在需要 `Dodge` 的响应窗口生成有效 `Dodge`，物理牌仍只通过 `Hand → Processing → DiscardPile` 移动。响应 Prompt 为每个物理响应牌发布有效牌型参数，AI 只从自己的玩家视图和合法响应列表中选择；当前不扩展装备、无懈链或其他响应时机。

本批增量验证：69/69 Console 检查通过；覆盖反馈、遗计与节命的 AI/人类可选触发、私有 Prompt、遗计摸牌/交牌事件、节命目标补牌事件、`DamageTriggerWindowFrame` 候选游标与 `DamageSkillFrame` 暂停恢复、命令日志 JSON 编解码与确定性重放、自动候选取得并续接父结算、发动/跳过三条牌区路径、伤害触发候选稳定排序、武圣 AI/人类红牌按杀转化、龙胆 AI/人类闪杀互转、火攻目标私有展示/同花色弃牌/火焰伤害两条路径、`PlayedCardKind`/有效响应牌型事件、`Processing → Hand(target)` 移动 reason、类型化事件、普通/私有视图边界和牌区守恒；完整解决方案 Release 构建 0 警告/0 错误，format gate 通过。

### C3 标准身份内容 — READY（K4 开局与 K5 结算切片已开放；复杂结算仍等待后续阶段）

- 8 人 1/2/4/1 配置已由 Standard Registry 描述并由 Core 兼容模式运行；
- 5 人 1/1/2/1 配置已由同一 Core 状态机运行并有固定种子测试；
- 主公先选、普通候选和通用武将池已进入 K4 运行时；
- 主公加体力、击杀反贼摸三、误杀忠臣弃牌；
- 身份模式 AI 标签与整局批量模拟。

### C4 装备包 — BLOCKED(K6)

按机制覆盖顺序，而不是一次加入全部名称：

1. 基础武器（仅攻击范围）；
2. 基础护甲（响应/伤害 modifier）；
3. 加一马、减一马；
4. 替换装备与失去装备触发；
5. 带主动询问或复杂命中的武器；
6. 宝物。

每件装备测试装备、替换、失效、死亡弃置、距离或触发效果。

### C5 即时锦囊 — ACTIVE（决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊与火攻最小切片已开放，其余等待 K5/K7）

建议顺序：

1. 无中生有（已完成无目标摸牌切片）；
2. 过河拆桥（已完成目标选择与确定性隐藏手牌盲弃最小切片）、顺手牵羊（已完成距离一确定性隐藏手牌盲取最小切片）；
3. 决斗（已完成最小交替杀响应切片）；
4. 桃园结义（已完成多目标恢复切片）、五谷丰登（已完成公开翻牌与逐人私有选牌切片）；
5. 南蛮入侵、万箭齐发（均已完成最小群体逐目标响应切片）；
6. 铁索连环（火/雷杀的最小类型化伤害已完成）；
7. 无懈可击和多层响应链。

每张牌包含目标合法性、无目标/目标死亡、响应、被无懈、结算中断和 AI 价值测试。

### C6 延时锦囊与判定 — BLOCKED(K7)

- 乐不思蜀；
- 兵粮寸断；
- 闪电；
- 判定牌移动、改判、判定区重复禁止、角色死亡清理；
- AI 对延时锦囊目标与改判的价值判断。

### C7 2v2 — BLOCKED(M1)

- `team2v2:open_v1` 模式定义；
- 公开队伍 AI；
- 先手摸牌补偿；
- 无身份局击杀奖惩；
- 双方胜负场景和整局统计。

### C8 国战 Lite — BLOCKED(M2)

- 同势力双将候选池；
- 主副将、暗置/明置文案和 UI 元数据；
- 第一批只依赖亮将状态的国战武将技能；
- 势力胜负和暗信息测试；
- 明确显示 Lite 未实现项。

### C9 完整国战 — BLOCKED(M3)

- 野心家及势力人数规则；
- 珠联璧合、阵法、队列/围攻；
- 变更副将；
- 国战专属牌堆、装备、锦囊；
- 完整 AI 明置策略和胜负策略。

## 4. 每项内容的完成定义

一项内容只有同时满足以下条件才可标记 DONE：

1. 定义、行为和中文文案均在内容项目；
2. 没有修改 Core 私有状态或 WPF 事件处理；
3. 至少一个正向场景和一个拒绝/边界场景；
4. AI 有使用、响应或保留策略；确实不适用时明确说明；
5. 玩家视图不会泄漏相关暗牌、候选或询问；
6. 固定 seed 可复现；
7. Release build、全部测试和 format gate 通过；
8. 更新内容 manifest 和仍不支持的组合。

## 5. 核心升级通知模板

核心任务每完成一个模块，向 Luna 的同一长期 goal 追加：

```markdown
## Core API Level Kx 已开放

- 契约文件：
- 新增类型/接口：
- 稳定时机：
- Luna 可修改目录：
- 本批从 BLOCKED 变为 READY：
- 强制新增场景：
- 暂不支持与禁止绕过项：
```

Luna 收到后更新本文件状态，将新批次并入现有 goal 并继续自主执行。
