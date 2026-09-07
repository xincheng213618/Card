# Card 长期开发总计划

更新时间：2026-09-07

## 目标

把当前可运行的八人身份局 Demo 逐步发展为一个可扩展的本地卡牌游戏框架。框架需要支持标准身份局、五人身份局、2v2 和国战，并允许内容线程持续增加卡牌、装备、武将和技能，而不用反复修改核心状态机。

项目不是以一次性复刻全部规则为目标。每个里程碑都必须形成一个可构建、可测试、可运行的小版本。

## 两条开发流水线

### 核心流水线

由当前 Luna 长期 goal 统一维护，Core 与内容按稳定阶段一起推进：

- 命令提交、版本校验和拒绝结果；
- 结算栈、暂停、恢复和响应链；
- 牌区、卡牌移动和状态不变量；
- 类型化事件、触发顺序和规则查询；
- 内容注册表与包契约；
- 玩家视图、可信诊断视图、存档和录像；
- 模式、选将、发牌和 AI 所依赖的通用接口；
- WPF 对通用接口的适配。

核心不负责批量编写具体卡牌和技能。

### 内容流水线

与核心流水线共享同一长期 goal，按已实现的 Registry/结算能力逐批推进：

- 标准牌、锦囊、装备和牌堆配方；
- 武将、技能与内容级 AI 策略；
- 8/5 人身份、2v2、国战的模式定义和模式内容；
- 每一项内容对应的场景测试、文案和 UI 元数据。

内容代码不得访问 `GameEngine` 私有状态，不得把规则特判写进 WPF，也不得修改核心契约来迁就单张牌。

## 目标工程结构

```text
src/
  CardGame.Core/                 状态机、命令、结算、事件、牌区、投影
  CardGame.Content.Standard/     标准基本牌、锦囊、装备、标准武将
  CardGame.Modes.Identity/       8 人与 5 人身份模式
  CardGame.Modes.Team2v2/        项目定义的公开阵营 2v2
  CardGame.Modes.NationalWar/    国战 Lite 与完整国战扩展
  CardGame.Wpf/                  本地桌面宿主

tests/
  CardGame.Core.Tests/           核心不变量和状态机测试
  CardGame.Content.Standard.Tests/
  CardGame.Modes.Tests/
  CardGame.Scenarios/            跨包固定场景和整局回归
```

在拆项目之前，现有 API 保持兼容；迁移采用适配器，不进行一次性大重写。

## 里程碑

当前核心 API 等级：**K4 开局切片 + K5 基础响应/恢复/公开选牌/隐藏手牌/伤害后技能效果与触发游标切片（2026-09-07）**。下一施工阶段是 K5 完整触发/多层结算。

| 阶段 | 核心交付 | 解锁给 Luna 的内容 | 完成定义 |
| --- | --- | --- | --- |
| K0 基线（DONE） | 当前 WPF Demo、杀闪桃、四技能、可解释 AI | 仅整理内容清单 | Release 通过，15 项以上自测 |
| K1 牌区与提交边界（DONE） | `Processing`、统一卡牌移动、移动账本、操作后通知 | 卡牌移动类技能的场景数据 | 每个稳定边界牌数守恒；观察者异常不破坏状态 |
| K2 命令与询问（DONE） | `Submit(GameCommand)`、Revision、PromptId、完整 Choice | 主动技能和多选目标定义 | 旧响应/伪造组合被类型化拒绝；旧 WPF API 继续工作 |
| K3 内容注册表（DONE） | 不可变 Registry、包清单、命名空间 ID、依赖校验 | 标准内容项目正式启用 | 重复 ID/缺失引用/依赖环构建失败；不同 Registry 内容隔离 |
| K4 开局流水线（切片 DONE） | Registry 模式、身份分配、私有单将候选、共享池去重、初始化、洗牌、逐轮发牌 | 8 人、5 人身份模式和首批武将池 | 同 seed+同命令得到同开局；选将信息按玩家隔离 |
| K5 类型化结算（基础切片 DONE） | 事件信封、`ResolutionFrame` 栈、杀/火杀/雷杀/闪/酒/决斗/南蛮入侵/万箭齐发响应、`DamageNature` 属性伤害、酒的一次性杀伤害修正、桃园结义多目标恢复、五谷丰登公开逐人选牌、过河拆桥隐藏手牌盲弃、顺手牵羊隐藏手牌转移、无中生有即时效果、伤害/恢复/基础濒死帧、反馈/遗计/节命/援护伤害后技能效果 | 当前基本牌、首批即时锦囊和后续触发技能的稳定入口 | 现有杀链、属性杀闪响应与类型化伤害、酒效直接杀 +1 伤害与回合结束失效、濒死者酒自救 1 点体力、决斗交替响应、南蛮入侵/万箭齐发逐目标响应、桃园结义逐目标恢复、五谷丰登公开翻牌/私有选牌、过河拆桥目标盲弃和顺手牵羊距离一盲取、反馈取得伤害牌、遗计私有摸牌/跨座位分配、节命按公开手牌数补牌均可暂停观察；无中生有无目标摸牌、基础濒死求桃、处理区与事件顺序固定；用酒救援他人、属性抗性、通用非受伤者触发和复杂濒死响应仍未开放 |
| K6 距离与装备 | 距离查询、攻击范围、五类装备槽、技能挂载/卸载 | 武器、护甲、加减一马、宝物 | 换装、距离、失效和死亡弃装均有测试 |
| K7 锦囊与判定 | 多目标效果、无懈链、延时锦囊、判定区、改判 | 标准锦囊与相关技能 | 多层响应可恢复；判定牌移动和事件顺序守恒 |
| M1 2v2 | 通用 Team 模式策略、阵营公开视图 | 2v2 模式包与 AI 权重 | 四人完整对局稳定结束，无身份局特判 |
| M2 国战 Lite | 双将、暗将、明置、势力投影、双将体力 | 同势力双将池、亮将技能 | 暗将不泄漏；亮将前后技能/体力正确 |
| M3 完整国战 | 野心家、势力人数限制、阵法/围攻接口、模式牌堆 | 完整国战规则与内容 | 关键模式场景矩阵通过 |
| K8 录像与存档 | Checkpoint、命令流录像、SchemaVersion、内容清单 | 内容兼容迁移数据 | 任意暂停点保存恢复；录像逐事件一致 |
| K9 AI 与体验 | 通用 AI 行动接口、模式策略、批量模拟、WPF 选择器 | 内容启发式与技能策略 | AI 不读隐藏状态；基准胜率和耗时有报告 |

## K1 已交付

- `CardZoneStore` 成为摸牌堆、手牌、处理区、弃牌堆、装备区、判定区和移出游戏区的唯一实体牌位置来源；
- `Move`、同源批量移动和通用 `MoveBatch` 都先整体验证，非法目标或错误来源不会产生部分移动；
- 杀、火杀、雷杀、闪、桃、决斗，初始发牌、摸牌、自动弃牌、阵亡弃牌、主公惩罚和重洗均通过统一移动入口；
- 等待闪时，造成伤害的杀真实停留在 `Processing`；奸雄从该区取得伤害牌，不再依赖虚构布尔状态；
- `CardMovementRecord`、类型化 `CardMoveReason` 和可信宿主牌区诊断已经可用；玩家快照只增加公开的处理区数量；
- 日志、AI 思考、卡牌移动和状态通知在公共操作提交后派发；订阅者异常被隔离并进入限长诊断；
- 玩家询问集合采用防御性投影；AI 结束出牌必定发布最终状态；步数保护只在活动响应收尾后的稳定边界判平；
- 30/30 Console 检查通过，隔离目录下完整 Release 构建为 0 warning / 0 error，格式校验通过。
- 额外审查覆盖 384 组逐步真人 API、1000 个长程全 AI seed 和 1000-seed 卡牌生命周期；未发现牌区、处理区或 pending 一致性失败。

K1 还不是通用事件/技能系统。`CardMoved` 当前是提交后的宿主观察通知，不能让内容代码在其中反向修改状态；内部 `CardsMoving/CardsMoved` 触发时机随 K5 类型化结算交付。

## K2/K3/K4 已交付

- `GameCommand`、`CommandResult` 和稳定 `CommandErrorCode` 已提供统一 `Submit` 入口；过期 Revision、错误操作者、Prompt、Choice、卡牌或目标均返回拒绝结果，不用异常表达玩家输入错误；
- `PendingDecision` 增加 PromptId、发布 Revision 和完整 `PromptChoice`；出牌的牌/目标组合以及闪/不响应选项由核心逐项发布，旧 `ValidCardIds`/`ValidTargetSeats` 仍作为 WPF 兼容投影；
- 旧 `Start`、`Advance`、`HumanPlay`、`HumanEndPlay`、`HumanRespond` 和单步 API 仍可用，并共享同一 Revision/提交后通知边界；
- `ContentRegistry` 按依赖拓扑冻结包清单，拒绝重复 ID、未知牌/技能引用、版本不足和依赖环；每个 Registry 的字典和嵌套列表均为独立只读投影；
- 新增 `CardGame.Content.Standard`，注册标准基本牌、十个兼容技能、十二名武将、基础演示牌堆和八人身份模式元数据；WPF 通过该 Registry 创建对局，Core 未注册时仍保留兼容内置路径；当前标准牌堆包含十五种牌型。
- K4 开局切片已通过 Registry 的 `ModeDefinition` 选择 5/8 人身份分布、牌堆、候选数量和武将池；`UseInteractiveSetup = true` 时座次/身份/选将/共享池去重/洗牌/逐轮发牌均在可暂停状态机内执行，WPF 已使用该路径；旧构造式 Demo 仍作为兼容适配器保留；
- K4 的 `SelectGeneralCommand` 使用私有 `PendingDecision`、`ValidContentIds` 和精确 Choice；AI 只收到本座候选，选中武将直到全部完成后才公开；
- K4 补充覆盖 5 人全 AI 选将和整局终止、8 人真人选将信息隔离、共享池无重复以及同 seed + 同选择命令的快照/事件确定性；
- 可信宿主 `Events`/`EventCommitted` 已提供带 `EventId`、Revision、CorrelationId 的类型化观察投影；K5 切片已增加数据型结算帧和稳定观察事件，内容可触发时机/完整 EventBatch 仍待 K5 完整阶段；
- K5 基础结算切片已将杀/火杀/雷杀/闪/桃/酒/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/火攻/基础濒死/死亡链包装为可序列化 `ResolutionFrame` 栈；属性杀仍复用 `RespondDodge`，并将 `DamageNature` 与实际伤害金额写入 `DamageFrame`、`DamageRequestedEvent`、`DamageAppliedEvent`、`AfterDamageEvent`、`DamageSkillContext` 和奸雄上下文；酒通过独立 `AlcoholAppliedEvent`/`AlcoholExpiredEvent` 表示一次性直接杀加伤和回合结束失效，濒死者通过私有濒死 Prompt 使用酒自救 1 点体力并生成类型化响应/恢复事件；同时新增 `RespondSlash`、`RespondDodge`、`SelectHarvestCard`、`FireAttackReveal`、`FireAttackDiscard`、单目标隐藏手牌盲弃/盲取、私有展示/同花色弃牌、交替响应、`CardUseFrame.TargetIndex`、`GroupCardUsedEvent`、通用 `GroupResponseEvent`、`CardsRevealedEvent`、`HarvestCardSelectedEvent`、`FireAttackCardRevealedEvent`、`FireAttackResolvedEvent`、`TargetCardDiscardedEvent`、`TargetCardTakenEvent` 和多目标 `RecoveryFrame`；无中生有使用空目标列表并通过统一摸牌入口完成即时效果，南蛮入侵和万箭齐发按固定座次逐目标完成杀/闪响应或单次伤害，桃园结义按固定座次逐目标恢复，五谷丰登公开翻牌后按固定座次私有选牌，过河拆桥按确定性随机数盲弃目标手牌，顺手牵羊按座位环距离一确定性盲取目标手牌，火攻按目标私有展示、攻击者同花色弃牌后进入火焰伤害链；伤害后候选中新增援护这一明确的跨座位弃牌恢复效果；栈仍只属于可信宿主，不进入普通玩家快照；
- 70/70 Console 检查通过，隔离目录下完整 Release 构建为 0 warning / 0 error，格式校验通过；新增反馈伤害后可选触发、援护跨座位弃牌恢复、武圣红牌按杀转化、龙胆闪/杀响应转化、`PlayedCardKind` 有效/物理牌型分离、火攻两段私有 Prompt、`DamageTriggerWindowFrame` 候选游标及窗口生命周期事件、`DamageSkillFrame` 暂停恢复、遗计跨手牌分配、节命公开目标筛选/补牌至上限、命令日志 JSON 编解码与确定性重放、群体父帧续接、稳定伤害触发候选排序和视图脱敏回归。
- 已接受的 `GameCommand` 可由可信宿主通过 `AcceptedCommands` 记录，使用 `CommandJson` 编解码并由 `GameReplay` 在相同 seed、内容 Registry 和 Revision/Prompt 校验下重放；命令日志不属于玩家视图。
- `standard:feedback` 已接入 `IPassiveSkill.OffersDamageCardChoice`：目标存活且伤害牌仍在 `Processing` 时，Core 先按固定键冻结全部当前候选，再压入可序列化 `DamageTriggerWindowFrame`；每个可选技能再压入 `DamageSkillFrame`，完成后递增 `CandidateIndex` 并继续窗口。`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留 `CandidateId`/`Priority`，只向反馈者发布 `DecisionKind.Feedback`；发动后按 `skill.feedback.claim-damage-card` 移入技能拥有者手牌并发布 `DamageCardClaimedEvent`，跳过则正常弃牌。候选收集已改为逐个存活拥有者调用 `CanTriggerAfterDamage`，但现有内置技能的触发条件默认仍只匹配受伤者。
- `standard:yiji` 已接入同一伤害触发窗口：郭嘉受伤后私有摸两张牌，`DecisionKind.Yiji` 发布精确的牌/其他存活目标组合；AI 与人类均只能使用自己的快照/Prompt，发动后通过 `skill.yiji.give-card` 将一张牌从拥有者手牌移至目标手牌，并发布可信宿主的 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent`；普通视图不泄漏候选牌面。
- `standard:jieming` 已接入同一伤害触发窗口：荀彧受伤后发布私有 `DecisionKind.Jieming` 目标选择，合法目标只按存活、公开手牌数量和体力上限判断，发动后经 `skill.jieming.draw` 摸牌至目标体力上限，并在 `DamageSkillCardsDrawnEvent.TargetSeat`/移动账本中保留可信宿主证据；AI 和人类都不能从目标隐藏手牌牌面推断选择。
- `standard:yuanhu` 已接入同一伤害触发窗口：援护者在其他角色受到正伤害后获得私有 `DecisionKind.Yuanhu` 弃牌 Choice；发动时按 `skill.yuanhu.discard` 将拥有者一张手牌移入弃牌堆，再用 `RecoveryFrame` 令固定伤害目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 和 `DamageSkillResolvedEvent.EffectTargetSeat` 保留可信宿主证据，普通玩家视图不泄漏弃牌 ID；这是当前唯一显式 opt-in 的非受伤者触发示例。
- 伤害触发同时覆盖 AI 快照决策、人类 `AnswerPromptCommand`、`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent`、反馈取得事件、遗计分配事件、节命补牌事件和援护弃牌/恢复事件；普通视图看不到其他 responder 的 Prompt 与隐藏牌 ID。
- 武圣同时覆盖 AI 选择、人类 `PlayCardCommand.PlayedCardKind` 和 WPF 独立转化按钮；有效牌型事件记录 `Slash`，物理移动账本保留原始实体牌，原生牌型与转化牌型不会因兼容调用产生歧义。
- 龙胆同时覆盖 AI/人类响应牌筛选、人类精确 `PromptChoice` 和 WPF 响应选择列表；闪转杀复用 `PlayedCardKind`，杀转闪通过 `CardKind? EffectiveCardKind` 记录有效响应牌型，物理移动账本仍保留同一实体牌。

## 接下来三个核心阶段

1. **K4 扩展验证**：把候选池扩大为可配置内容批次，补齐 5/8 人固定 seed 矩阵和真正的模式策略对象；兼容路径逐步迁移到交互开局。
2. **K5 完整结算**：在现有决斗/南蛮入侵/基础濒死帧栈上继续扩展跨事件触发收集/排序、通用非受伤者触发条件、增加多伤害/重复救援和可恢复 Checkpoint；当前已开放伤害候选窗口与游标、反馈取得、遗计跨座位分配、节命公开目标补牌和援护这一明确的跨座位弃牌恢复示例，保留兼容 API。
3. **K6/K7 扩展**：按先装备距离、再锦囊判定的顺序开放新牌区、响应链和内容批次。

## 核心 API 等级与内容解锁

每个核心阶段完成后，核心任务向 Luna 的同一个长期 goal 发送一次增量说明：

```text
Core API Level: Kx
新增扩展点:
允许写入的项目/目录:
本批建议内容:
强制测试:
暂不支持:
```

Luna 把本批内容加入同一个动态 backlog，不新建重复 goal。核心任务不逐轮监控 Luna，只负责在契约升级时发布新的扩展能力。

## 全局完成门槛

每个阶段都必须满足：

1. `dotnet build .\CardGame.sln -c Release` 为 0 warning / 0 error；
2. 所有 Console/单元/场景测试通过；
3. `dotnet format .\CardGame.sln --verify-no-changes` 通过；
4. 固定种子可复现，非法命令不改变状态；
5. 玩家视图没有暗身份、他人手牌、牌堆顺序、随机种子或他人私有询问；
6. 每张牌恰好处于一个区域，任何稳定结算边界都通过不变量；
7. 当前 WPF Demo 仍能启动并至少走通一条真人行动/响应链；
8. 文档注明新增能力、仍有意保留的简化和下一批解锁内容。

当前本地若已有 `CardGame.Wpf` 进程占用常规 Release 输出，验证使用 `dotnet build .\CardGame.sln -c Release --artifacts-path <isolated-temp>`，不结束或干扰用户进程；结果仍必须记录为全解构建证据。

## 明确不做

- 不把 WPF 控件当作领域对象；
- 不让内容包通过反射修改引擎私有字段；
- 不用字符串字典替代核心事件和状态；
- 不在核心注册表构建完成前无限扩充 `CardKind`/`SkillKind` 枚举；
- 不把脱敏 `GameSnapshot` 当作完整存档；
- 不为了本地八人游戏引入分布式系统、ECS 或异步线程状态机；
- 不复制来源不明或商业受限的卡面、插画和音频。
