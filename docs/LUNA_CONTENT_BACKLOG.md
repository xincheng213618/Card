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

当前 C0 的历史清单和测试契约已完成；正式运行内容包已在 C0-K3 落地，K4 的模式开局、K5 基础结算、K6 距离/装备基础切片和 K7 无懈/铁索连环/鬼才/乐不思蜀/兵粮寸断/闪电基础判定切片均已开放，复杂结算行为仍按 K5-K7 的开放等级推进。

### C0-K1 卡牌移动契约包 — DONE（2026-09-07）

核心已通过同一长期 goal 正式开放 K1。本批只消费牌区与移动契约；当时不创建正式 Registry、不接入运行时内容，作为历史阶段记录保留。

- 为当前基本牌、已实现技能和后续标准牌/锦囊/装备补充 `From → Processing → To` 生命周期；
- 对齐 K1 已有的 `CardMoveReason`，并为 K2/K5/K6 内容记录建议的 namespaced reason ID；
- 明确普通玩家视图、可信宿主移动账本和 `CardMoved` 诊断通知的边界；
- 写入杀命中、闪避、奸雄取得、桃、发牌/摸牌、手牌上限弃置、死亡清区（K1 手牌；装备区已在 K6 基础切片完成；判定区基础移动已在 K7）、主公惩罚和重洗的强制场景规格；
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
- 普通杀及属性杀的内容定义与 `DamageNature` 最小伤害类型已完成；铁索连环的精确一/二目标、公开标记和火/雷同额传导已完成；属性抗性、属性转化和更复杂的多伤害嵌套仍待后续结算阶段；
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
- 伤害后触发：奸雄、反馈、遗计、节命、援护、刚烈；刚烈已完成判定/伤害后效果入口的最小闭环；
- 其中反馈已完成存活伤害后的私有可选触发最小切片：Core 先冻结排序后的候选并压入可序列化 `DamageTriggerWindowFrame`，每个可选候选再进入 `DamageSkillFrame`；遗计已在同一帧栈上完成受伤后私有摸两张牌，并从合法牌/其他存活目标组合中将一张牌交出的真实跨手牌效果；节命已复用同一帧栈，按公开存活状态、手牌数量和体力上限发布私有目标 Choice，并将牌补至目标上限；刚烈已复用同一帧栈，在受伤者私有选择后公开判定，红色结果向伤害来源发布精确两牌弃置/承受 1 点伤害 Choice，并支持反制伤害濒死后的原帧续接。候选收集已通过 `CanTriggerAfterDamage` 逐个检查当前存活拥有者；通用非受伤者触发语义和更复杂的跨座位效果仍待后续入口。
- 判定修改：鬼才已完成 K7 基础判定替换切片，乐不思蜀、兵粮寸断和闪电已完成基础延时判定切片；复杂改判和其他延时判定仍待后续；
- 出牌主动技能：仁德、制衡、苦肉、青囊、回春；`IActiveSkill`/`UseSkillCommand` 已支持无牌/无目标苦肉、带私有手牌多选的制衡，以及仁德的私有手牌多选/其他存活目标选择/跨手牌移动/按数量回复；青囊的私有一张手牌/受伤存活目标选择/弃置与 1 点恢复；苦肉在 1 点体力发动后的共享濒死救援续接已完成，更复杂的多目标/多效果仍等待后续入口。
- 距离/范围：马术、奇才（等后续 K6；当前基础装备 modifier 已开放）；
- 多人/主公询问：激将（较晚批次）。

每个技能至少包含发动/不发动、边界条件、死亡/失效和确定性场景。

本轮新增 `standard:yuanhu`：援护者在其他角色受到正伤害后，沿同一 `DamageTriggerWindowFrame` 获得私有 `Yuanhu` 弃牌 Choice；发动时将一张自己的手牌按 `skill.yuanhu.discard` 经过 `Processing` 移入弃牌堆，并由 `RecoveryFrame` 令固定受伤目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 与 `DamageSkillResolvedEvent.EffectTargetSeat` 只向可信宿主提供完整证据；这是当前受约束的非受伤者触发示例。

本轮新增 `standard:ganglie`：刚烈者受到正伤害后沿同一 `DamageTriggerWindowFrame` 获得私有 `Ganglie` 发动/跳过 Choice；发动后公开判定，红色时向伤害来源发布私有 `GangliePunish` 精确两牌组合或承受 1 点伤害 Choice。弃牌、判定和结果事件沿统一移动契约提交；反制伤害若使来源濒死，救援完成后恢复刚烈技能帧和原伤害触发游标，普通快照不泄漏来源手牌 ID。

当前 Feedback/Yiji/Jieming 切片的边界是：Core 先按 `DamageTriggerOrdering` 稳定排序并压入带 `CandidateIndex` 的 `DamageTriggerWindowFrame`，再为当前可选候选压入 `DamageSkillFrame`。反馈只向反馈者发布私有 `Feedback` Choice，发动才将伤害牌移入技能拥有者手牌并发布 `DamageCardClaimedEvent`，不发动则进入弃牌堆；遗计只向受伤者发布私有 `Yiji` Choice，先将两张牌从 `DrawPile` 摸入其手牌，发动时按精确牌/其他存活目标组合将一张牌移给目标并发布 `DamageSkillCardGivenEvent`，不发动则两张牌留在其手牌；节命只向受伤者发布私有 `Jieming` Choice，按公开合法目标缺口从 `DrawPile` 摸牌至目标体力上限，并以 `DamageSkillCardsDrawnEvent.TargetSeat` 和 `skill.jieming.draw` 移动 reason 记录目标。三条路径都保留候选标识、支持群体父帧续接且不向普通快照泄漏私有取得牌 ID；候选收集虽已开放逐个存活拥有者的 `CanTriggerAfterDamage` 时机钩子，现有内置技能仍默认只参与受伤者窗口，通用非受伤者触发、多伤害嵌套和其他复杂伤害后技能仍待后续阶段。无懈可击已另行完成有限多层响应切片：可抵消锦囊在效果结算前按固定座次发布私有 `Nullification` Prompt，响应牌经过 `Processing` 后由 `NullificationWindowFrame` 继续或结束链条；铁索连环使用牌也复用该效果前响应边界，公开连环标记和火/雷传导由独立状态事件记录。

本轮新增主动技能扩展包 `standard-active-skills@1.0.0`：它依赖 `standard@1.11.0`，注册 `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、五个主动技能演示武将和 5/8 人演示模式。苦肉在出牌阶段且体力大于 0 时发动，沿 `ActiveSkillFrame` 失去 1 点体力并摸两张牌；若降至 0，先复用共享私有 `RescueDying` 窗口，获救后才摸牌，死亡则清理主动技能帧；制衡从当前拥有者私有手牌中选择至少一张，经过 `Processing` 弃置后摸等量牌；仁德从当前拥有者私有手牌中选择一至若干张并选择一名其他存活角色，经过 `Processing` 交给目标，一次交给至少两张时按目标伤势回复 1 点体力且同一回合只能发动一次；青囊从当前拥有者私有手牌中选择一张并选择一名受伤存活角色，经过 `Processing` 弃置后恢复 1 点体力且同一回合只能发动一次；回春从当前拥有者私有手牌中选择精确两张，并选择 2–3 名受伤存活角色逐目标恢复 1 点体力，同一回合只能发动一次。`ActiveSkillRequestedEvent`/`ActiveSkillResolvedEvent` 保存精确选择，`SkillHpLostEvent`、`SkillCardsDiscardedEvent`、`SkillCardsGivenEvent`、`SkillCardsDrawnEvent` 和对应移动 reason 组成可回放证据，暗牌 ID 不进入普通快照。更复杂的多目标/多效果结算仍保留为后续切片。

武圣切片的边界是：红色且不是原生杀/火杀/雷杀的实体牌，可以在合法目标上生成有效牌型为 `Slash` 的动作；`LegalAction.PlayedCardKind`、`PlayCardCommand.PlayedCardKind` 和 WPF 独立按钮负责消除原生使用与转化使用的歧义。事件/伤害链记录有效 `Slash`，`CardMovementRecord` 仍记录原牌的实体 ID/物理 `CardKind`，不复制牌；AI 只从脱敏视角的红牌和合法动作中评分。

龙胆切片的边界是：物理闪可在出牌阶段生成有效 `Slash`；物理杀/火杀/雷杀可在需要 `Dodge` 的响应窗口生成有效 `Dodge`，物理牌仍只通过 `Hand → Processing → DiscardPile` 移动。响应 Prompt 为每个物理响应牌发布有效牌型参数，AI 只从自己的玩家视图和合法响应列表中选择；K6 已另行接入装备基础生命周期与距离 modifier；无懈可击已拥有独立的有限响应窗口，其他复杂响应时机仍待后续。

本批增量验证：107/107 Console 检查通过；覆盖反馈、遗计、节命、援护、刚烈与鬼才的 AI/人类可选触发、私有 Prompt、遗计摸牌/交牌事件、节命目标补牌事件、刚烈公开判定/来源私有两牌反制与濒死续接、正式规则路径的嵌套反制伤害帧、鬼才判定替换、乐不思蜀/兵粮寸断/闪电目标判定区/红黑结果/命中转移/伤害续接/跳过阶段与延时牌收尾、同一目标多张延时牌累计独立效果、`DamageTriggerWindowFrame` 候选游标与 `DamageSkillFrame` 暂停恢复、命令日志 JSON 编解码与确定性重放、可信命令 Checkpoint 私有 Prompt 恢复、规则行为版本兼容和同版本内容漂移拒绝、自动候选取得并续接父结算、发动/跳过五条牌区路径、伤害触发候选稳定排序、武圣 AI/人类红牌按杀转化、龙胆 AI/人类闪杀互转、火攻目标私有展示/同花色弃牌/火焰伤害两条路径、`PlayedCardKind`/有效响应牌型事件、`Processing → Hand(target)` 移动 reason、类型化事件、普通/私有视图边界和牌区守恒；另覆盖主动技能命令/帧/事件、扩展 Registry、苦肉体力与摸牌、苦肉濒死救援续接、青囊弃牌恢复、移动 reason、普通视图脱敏和 WPF 按钮提交；完整解决方案 Release 构建 0 警告/0 错误，format gate 通过。

### C2-K5 回春多目标主动技能增量 — DONE（2026-09-08）

本轮在既有苦肉、制衡、仁德和青囊主动技能之后新增 `standard:huichun`/回春演示：出牌阶段每回合私有选择精确两张手牌，并选择 2–3 名受伤存活角色；两张牌按 `Hand → Processing → DiscardPile`、`skill.huichun.discard` 移动，随后按提交顺序逐目标建立 `RecoveryFrame`，各自发布 `RecoveryAppliedEvent`。目标数量不足、重复目标或越权手牌均在移动前原子拒绝；AI 只读取公开受伤状态与自己的手牌，普通视图不泄漏暗牌 ID。

本轮验证：Core `108/108`、WPF `21/21`；Core 覆盖精确牌/目标选择、移动 reason、逐目标恢复、类型化事件、普通视图脱敏和 Checkpoint/命令重放，WPF 离屏控件覆盖多目标高亮、双牌选择、按钮摘要和单命令提交。

### C2-K6 马术公开距离修正增量 — DONE（2026-09-08）

本轮在既有装备距离 modifier 之后新增 `standard:mashu`/马术演示：`IPassiveSkill` 增加 `ModifyOutgoingDistance` 查询钩子，Core 在计算存活座位环距离和装备修正后统一应用马术的出攻距离 -1，并将最终距离钳制为至少 1。顺手牵羊与杀的合法动作不复制距离分支，AI 继续只接收自己的过滤快照和合法动作，WPF 座位卡显示同一公开距离结果；基础 Standard Registry、90 张牌堆和旧命令/回放入口不变。

本轮验证：Core `109/109`、WPF `21/21`；覆盖扩展 Registry、被动技能映射、距离边界、距离一顺手牵羊合法性、普通视图不暴露隐藏种子/牌 ID，以及 WPF 技能文案和座位距离文本。

### C2-K6 奇才锦囊距离豁免增量 — DONE（2026-09-08）

本轮在马术公开距离修正之后新增 `standard:qicai`/奇才演示：`IPassiveSkill` 增加 `IgnoresTrickDistance` 查询钩子，Core 在构造距离型锦囊合法动作时统一消费该结果；当前顺手牵羊可选择距离大于 1 且存在目标牌的其他存活角色，仍由 Core 校验目标、区域和实体牌来源。AI 只读取自己的技能、公开目标状态和合法动作，WPF 通过同一目标选择投影，基础 Standard Registry、90 张牌堆和旧命令/回放入口不变。

本轮验证：Core `110/110`、WPF `21/21`；覆盖扩展 Registry、被动技能映射、锦囊距离边界、距离型顺手牵羊合法动作、普通视图脱敏，以及 WPF 奇才技能文案和远距离目标选择。

### C2-K5 伤害触发范围契约增量 — DONE（2026-09-08）

本轮把伤害后技能的座位关系抽为 Core 的 `DamageTriggerScope`：默认技能使用 `DamagedPlayer`，跨座位技能可声明 `OtherLivingPlayer` 或 `AnyLivingPlayer`；`IPassiveSkill.CanTriggerAfterDamage` 统一校验正伤害、目标关系和缺少目标的边界，存活过滤仍由 Core 候选收集器负责。`standard:yuanhu` 移除自身的跨座位关系特判，改为复用 `AfterDamageTriggerScope`，候选排序、`DamageTriggerWindowFrame` 游标、AI/WPF 私有 Choice、牌区移动和回放边界保持不变。

本轮验证：Core `110/110`、WPF `21/21`；覆盖默认受伤者范围、其他存活角色范围、任意存活角色范围、零伤害/缺少目标拒绝、援护真实跨座位恢复，以及既有普通视图脱敏和确定性回归。

### C2-K5 濒死窗口跨座位酒救援增量 — DONE（2026-09-08）

本轮将【酒】从濒死者自救扩展为当前规则版本的通用濒死救援：固定座次的每名存活 responder 都只从自己的私有手牌获得酒候选，AI 仍只读取本座过滤快照，真人 Prompt 区分自救/救援文案；酒通过既有 CardUseFrame/RecoveryFrame、Processing 移动、DyingResponseEvent.UsedAlcohol 和 RecoveryAppliedEvent 完成跨座位恢复。规则语义版本前进至 3，旧 v1/v2 Checkpoint 保留酒只自救的回放行为；标准包 `standard@1.11.0` 的描述/标签和内容指纹保持冻结。

本轮验证：Core `111/111`、WPF `21/21`；覆盖跨座位 Prompt、普通视图脱敏、伪造 Choice 原子拒绝、类型化事件、Processing 生命周期、AI 阵营评分和旧 Checkpoint 兼容。

### C2-K5 急救红牌濒死转化增量 — DONE（2026-09-08）

本轮新增独立 `standard-rescue-skills@1.0.0` 扩展包及 `standard:jijiu`/急救者演示武将：急救者在私有 `RescueDying` Prompt 中可将红色非桃实体牌当作桃使用。转化牌仍以同一实体经过 `Hand → Processing → DiscardPile`，有效牌型写入 `CardUseDeclaredEvent`/`CardUseFinishedEvent`，原始物理牌型保留在移动账本与 `DyingResponseEvent.UsedPeachPhysicalCardKind`；AI 与 WPF 只消费本座私有候选，普通视图不泄漏牌 ID。默认 Standard Registry 和旧 `standard-active-skills@1.0.0` Registry/内容指纹保持不变，Checkpoint 恢复会按新包签名选择完整 Registry。

本轮验证：Core `113/113`、WPF `22/22`；覆盖扩展包隔离、红牌筛选、AI/人类选择、私有 Prompt 脱敏、有效/物理牌型事件、Processing 生命周期和 Checkpoint/Replay 一致性；WPF 通过真实控件路径渲染“当作【桃】”并提交急救选择。

### C3 标准身份内容 — READY（K4 开局与 K5 结算切片已开放；复杂结算仍等待后续阶段）

- 8 人 1/2/4/1 配置已由 Standard Registry 描述并由 Core 兼容模式运行；
- 5 人 1/1/2/1 配置已由同一 Core 状态机运行并有固定种子测试；
- 主公先选、普通候选和通用武将池已进入 K4 运行时；
- 主公加体力、击杀反贼摸三、误杀忠臣弃牌；
- 身份模式 AI 标签与整局批量模拟。

### C4 装备包 — ACTIVE（K6 基础切片已完成，复杂效果仍待后续）

K6 已先交付可复用的装备基础闭环：

1. 五类槽位：武器、防具、进攻坐骑、防御坐骑、宝物；
2. 装备牌通过 `LegalActionKind.Equip` 完成 `Hand → Processing → Equipment`；
3. 同槽替换、`EquipmentChangedEvent`、公开装备快照和阵亡装备清理；
4. 诸葛连弩的攻击范围/杀次数 modifier、赤兔/绝影的战斗距离 modifier、玉玺的摸牌 modifier、仁王盾的黑色杀目标过滤；
5. WPF、AI、移动账本和 113 项 Console 自测均覆盖基础观察与牌数守恒；刚烈公开判定/来源私有反制与濒死续接、鬼才私有判定替换、乐不思蜀/兵粮寸断/闪电延时判定与累计效果、闪电命中/转移与雷电伤害续接、青釭剑无视防具、仁王盾阻挡黑色杀、八卦阵判定成功/失败、过河拆桥/顺手牵羊公开装备与判定区牌目标、无懈有限多层响应、铁索连环火/雷传导、马术公开距离修正、奇才锦囊距离豁免、当前规则版本跨座位酒救援、急救红牌濒死转化、主动技能扩展包的青囊弃牌恢复、回春双牌/多目标恢复、苦肉濒死救援续接与 Checkpoint 同版本内容漂移拒绝另有独立回归。

仍待后续 K7：失效/卸载触发和带主动询问或复杂命中的装备；青釭剑的直接杀无视防具、仁王盾的黑色杀目标过滤、八卦阵的红色判定防御以及公开装备/判定区牌目标选择已从后续队列移入当前基础闭环。

### C5 即时锦囊 — ACTIVE（决斗、无中生有、南蛮入侵、万箭齐发、桃园结义、五谷丰登、过河拆桥、顺手牵羊、火攻、无懈可击与铁索连环最小切片已开放，其余等待 K5/K7）

建议顺序：

1. 无中生有（已完成无目标摸牌切片）；
2. 过河拆桥（已完成目标选择、隐藏手牌盲弃与公开装备/判定区牌逐张选择）、顺手牵羊（已完成距离一隐藏手牌盲取与公开装备/判定区牌逐张选择）；
3. 决斗（已完成最小交替杀响应切片）；
4. 桃园结义（已完成多目标恢复切片）、五谷丰登（已完成公开翻牌与逐人私有选牌切片）；
5. 南蛮入侵、万箭齐发（均已完成最小群体逐目标响应切片）；
6. 铁索连环（精确一/二目标、公开连环标记和火/雷杀同额传导已完成）；
7. 无懈可击的有限多层响应已完成；复杂响应时机、多伤害嵌套和其他多层结算仍待后续。

每张牌包含目标合法性、无目标/目标死亡、响应、被无懈、结算中断和 AI 价值测试。

### C6 延时锦囊与判定 — ACTIVE（乐不思蜀/兵粮寸断/闪电基础切片已开放；复杂延时判定仍待后续）

- 乐不思蜀基础切片已完成；后续规则 v11 已校正为非红桃跳过出牌阶段，v1–v10 保留本计划记录的历史红黑语义；
- 兵粮寸断基础切片已完成；后续规则 v11 已校正为非梅花跳过摸牌阶段，v1–v10 保留本计划记录的历史红黑语义；
- 两种延时牌共享判定帧、无懈/鬼才窗口、死亡清理和收尾移动，并允许同一目标累计独立阶段效果；
- 闪电基础切片已完成：只能对自己使用；下个回合判定为黑桃 2 至 9 时造成 3 点雷电伤害，否则转移到下一名存活角色的判定区；命中后的伤害/濒死链会回到原回合收尾；
- 复杂延时锦囊的判定牌移动、改判、判定区重复禁止、角色死亡清理；三种基础延时牌已覆盖移动、无懈窗口、鬼才替换、目标合法性和死亡清理；复杂改判仍待后续；
- AI 对剩余延时锦囊目标与复杂改判的价值判断；乐不思蜀使用公开判定区和体力/敌对关系评分，兵粮寸断只使用公开手牌数量和敌对关系评分，闪电只使用自己的公开体力评估命中风险，不读取目标隐藏手牌或牌堆顺序。

### C7 2v2 — DONE（M1，2026-09-08）

- `standard-team-modes@1.0.0` 独立包和 `team:standard-2v2` 模式定义；
- `ContentModeKind.Team`、`TeamCounts`、公开 `TeamId`/`TeamAssignedEvent` 和兼容队伍角色投影；
- 公开阵营 AI 敌我判断、先手首轮少摸一张、无身份局击杀奖惩和队伍胜负；
- 普通快照继续隐藏 seed、他人手牌和宿主内部信息；
- 固定 seed 全 AI 终局、事件签名、牌数守恒、Checkpoint/Replay 以及 WPF 2v2 设置页离屏回归均已覆盖。

### C8 国战 Lite — DONE（M2，2026-09-12）

- `standard-national-war-lite@1.1.0` 与 `national:lite-4` 已注册四人魏蜀模式，内容注册要求显式的带势力标签武将池；
- 每名玩家依次选择两名同势力武将，主将/副将可分别暗置或明置，明置状态独立进入快照、事件、技能查询和 WPF 双肖像；
- 规则版本 7/8/9 已覆盖已明置技能、双将基础体力平均向下取整、国战武圣响应，以及【杀】响应窗口内明置后刷新当前私有候选；1.0.0 / 7 旧包仍可恢复固定 4 点体力；
- AI 只消费自己的势力和玩家视角，势力公开后再标记同伴/对手；势力胜负、隐私、边界、固定 seed 和 Checkpoint/Replay 均已测试；
- 四人 Lite 的未实现范围已在指南、模式设计和体验文档中明确保留，完整国战仍由 C9 负责。

### C9 完整国战 — ACTIVE(M3 首个切片)

本轮先开放 M3-1：国战 AI 明置策略已接入 `SimpleAiBrain`。AI 会从自己的私有手牌、体力、双将技能和当前公开行动机会评估主将/副将明置；武圣红牌转杀、龙胆杀转闪、咆哮多杀和空城空手等即时收益有专门权重，平分时仍使用可回放的确定性抖动。思路记录不读取牌堆、其他玩家手牌或引擎私有状态。

随后开放 M3-2 的可玩试验切片：`standard-national-war-ambitious@1.0.0` 增加 `national:ambitious-6`，以 `FactionCounts = 魏 3 / 蜀 2 / 野心家 1` 和 `SoloFactionIds = [ambitious]` 表达六人三方桌；Core 放宽国战模式的势力数量约束，WPF 接入模式选择、指南、野心家标签、原创肖像别名和半明置存档恢复。内容哈希会纳入非空独立势力元数据；AI、普通视图和回放仍沿既有隐私边界。统一命令收口还修复了“赢家已确定但未完成当前嵌套濒死/伤害帧”的终局悬挂。

M3-3 在战术 AI v2 上追加受约束的公开攻击证据：Core 只把杀、决斗、群体攻击和火攻中公开可见的势力字段及攻击者/目标座位传给对应 AI；己方攻击隐藏座位、隐藏座位攻击公开己方或公开敌方分别形成弱敌对/友方信号，置信度固定钳制在 [-6, 6]。已公开 `FactionId` 继续优先于推断，v1 不消费该信号；没有把引擎私有势力、手牌、牌堆或结算栈传入 AI。

M3-4 让同一有界证据进入濒死救援评分：隐藏目标不会被揭示，公开证据只会让 v2 更倾向救援或放弃救援；已公开同伴/敌对势力继续使用确定性强关系值，v1 仍保持旧行为。

M3-5 增加公开攻击后的中途回放验收：六人国战在仍有暗势力且已产生公开攻击的暂停点捕获可信 Checkpoint，序列化恢复后保持相同的玩家视图、类型化事件流、AI/明置思考和 Prompt；该校验不把 AI 推断写入普通快照。

补充的 AI policy v3 将身份局火攻接入同一公开攻击观察入口；v1/v2 仍按既有身份局火攻行为运行。该扩展不泄漏隐藏身份、手牌、牌堆或结算栈，并以现有确定性回归确认旧策略日志不漂移。

当前验证为 Core 135/135、WPF 45/45；M3-2 只是独立势力/三方人数的可玩实验，M3-3/M3-4 只增加受约束的公开证据消费，M3-5 只加强命令回放边界，policy v3 的身份局火攻扩展也不等同于完整野心家规则。完整国战仍未完成。

- 野心家及势力人数规则；
- 珠联璧合、阵法、队列/围攻；
- 变更副将；
- 国战专属牌堆、装备、锦囊；
- 完整 AI 胜负策略和剩余国战内容。

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
