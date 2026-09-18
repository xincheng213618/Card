# Card 长期开发总计划

更新时间：2026-09-12

最新响应补充：规则版本 9 为武圣接入红牌当杀响应决斗／南蛮入侵，真人与 AI 共用合法性和实体牌移动；国战玩家还可在【杀】响应窗口内明置武将并刷新自己的私有候选；国战 AI 明置改为按自己的手牌、体力和双将技能机会评分，并在战术 v2/v3 中消费受约束的公开攻击证据评估攻击与濒死救援；v3 另外把身份局火攻接入同一公开攻击观察入口；公开攻击后的中途 Checkpoint 已验证能恢复同一 AI 诊断和暂停 Prompt；旧版本继续原行为。随后追加六人 M3 独立势力试验及嵌套终局收口修复，当前全量验证为 Core 135/135、WPF 45/45；详见 [`WUSHENG_RESPONSES.md`](WUSHENG_RESPONSES.md) 和 [`NATIONAL_WAR_EXPERIENCE.md`](NATIONAL_WAR_EXPERIENCE.md)。下方保留各阶段验证记录。

本轮牌桌可读性补充：国战双将并排肖像、独立明暗与技能状态、公开势力关系标签已经落地；当前边界校验还要求国战模式显式声明带势力标签的专属武将池，六人 M3 也复用同一视图边界，最新 Core 135/135、WPF 45/45。详见 [`NATIONAL_SEAT_PRESENTATION.md`](NATIONAL_SEAT_PRESENTATION.md)。

四人国战 Lite 已进入实际开局、双将选择、明置、响应中明置、存档、指南与结算流程；M3 又加入按私有即时机会选择主将/副将的 AI 明置策略、六人独立势力试验、公开攻击证据推断和证据驱动的隐藏目标救援评分，并验证公开攻击后的中途 Checkpoint/Replay。规则版本 7 为已明置主副将启用各自技能；规则版本 8 和国战内容包 1.1.0 补齐 M2 的基础体力合成与选将预览。当前验证为 Core 135/135、WPF 45/45，实际旧版存档保留原始内容、规则和固定体力；完整国战 M3 未完成。详见 [`NATIONAL_WAR_EXPERIENCE.md`](NATIONAL_WAR_EXPERIENCE.md) 和 [`NATIONAL_DUAL_HEALTH.md`](NATIONAL_DUAL_HEALTH.md)。下方各已交付切片保留对应阶段的验证记录。

玩家出牌建议已接入指南，使用普通玩家视图和合法行动，不改动当前选牌或实际 AI 状态；五场完整对局逐次校验合法性与命令历史。该阶段验证为 Core 122/122、WPF 36/36，详见 [`PLAY_ADVICE.md`](PLAY_ADVICE.md)。

声音与动画现在独立保存为本机偏好，开局前可调整；已有偏好不被读档覆盖，首次旧档导入和损坏文件恢复已验证。当前 Release 构建零警告、零错误，Core 122/122、WPF 36/36，详见 [`PLAYER_PREFERENCES.md`](PLAYER_PREFERENCES.md)。

本地历史战绩已接通开局、牌桌与结算页，保留最近 50 局结果及全员统计，支持重启读取、终局去重和写入重试；浏览时暂停并隔离牌局输入。该阶段验证为 Core 122/122、WPF 36/36，详见 [`MATCH_HISTORY.md`](MATCH_HISTORY.md)。规则版本 6 的铁索自选与重铸继续保持版本 1–5 存档原行为，详见 [`IRON_CHAIN_RECAST.md`](IRON_CHAIN_RECAST.md)。

本地交付入口已补齐：根目录 `Play.cmd` 启动最近一次验证通过的游戏包；`tools/Build-GamePackage.ps1` 生成自带 .NET 8 运行时的 Windows x64 ZIP，并从解压目录执行界面和素材自检后更新启动指针。使用方式见 [`LOCAL_PLAY.md`](LOCAL_PLAY.md)。

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

当前核心 API 等级：**K4 开局切片 + K5 基础响应/恢复/公开选牌/隐藏手牌/伤害后技能效果与触发游标 + K6 距离/装备基础切片 + K7 八卦阵判定防御、无懈有限响应、铁索连环、鬼才判定替换与乐不思蜀/兵粮寸断/闪电基础延时切片 + K8 可信命令 Checkpoint 兼容切片 + 主动技能目标/濒死续接切片（2026-09-08）**。本轮在仁德的私有手牌/目标选择、跨手牌移动和按数量回复之后，新增苦肉降至 0 点体力时复用共享濒死救援窗口，并在 K6 距离查询上接入马术被动修正与奇才锦囊距离合法性查询；主动技能现已覆盖无牌/无目标苦肉、苦肉濒死续接、私有多选弃牌制衡、私有目标交牌仁德、青囊弃牌恢复和回春双牌/多目标恢复，下一施工阶段是更复杂的多目标选择、多效果结算与 K5 完整触发/多层结算。

规则行为版本 5 已加入当前核心状态：当伤害使目标降至 0 点体力时，只要存在可触发的伤害后技能，仍先完成冻结的 `DamageTriggerWindowFrame`/`DamageSkillFrame`，再进入 `DyingFrame`；v1–v4 回放继续保留未开放该窗口时的历史事件顺序。

| 阶段 | 核心交付 | 解锁给 Luna 的内容 | 完成定义 |
| --- | --- | --- | --- |
| K0 基线（DONE） | 当前 WPF Demo、杀闪桃、四技能、可解释 AI | 仅整理内容清单 | Release 通过，15 项以上自测 |
| K1 牌区与提交边界（DONE） | `Processing`、统一卡牌移动、移动账本、操作后通知 | 卡牌移动类技能的场景数据 | 每个稳定边界牌数守恒；观察者异常不破坏状态 |
| K2 命令与询问（DONE） | `Submit(GameCommand)`、Revision、PromptId、完整 Choice | 主动技能和多选目标定义 | 旧响应/伪造组合被类型化拒绝；旧 WPF API 继续工作 |
| K3 内容注册表（DONE） | 不可变 Registry、包清单、命名空间 ID、依赖校验 | 标准内容项目正式启用 | 重复 ID/缺失引用/依赖环构建失败；不同 Registry 内容隔离 |
| K4 开局流水线（切片 DONE） | Registry 模式、身份分配、私有单将候选、共享池去重、初始化、洗牌、逐轮发牌 | 8 人、5 人身份模式和首批武将池 | 同 seed+同命令得到同开局；选将信息按玩家隔离 |
| K5 类型化结算（基础切片 DONE） | 事件信封、`ResolutionFrame` 栈、杀/火杀/雷杀/闪/酒/决斗/南蛮入侵/万箭齐发响应、`DamageNature` 属性伤害、酒的一次性杀伤害修正、桃园结义多目标恢复、五谷丰登公开逐人选牌、过河拆桥隐藏手牌不透明牌位选择/公开装备与判定区牌精确弃置、顺手牵羊隐藏手牌不透明牌位转移/公开装备与判定区牌精确取得、无中生有即时效果、铁索连环精确目标/公开标记/属性伤害传导、伤害/恢复/基础濒死帧、反馈/遗计/节命/援护/刚烈伤害后技能效果与回春多目标恢复、急救红牌濒死牌转化 | 当前基本牌、首批即时锦囊和后续触发技能的稳定入口 | 现有杀链、属性杀闪响应与类型化伤害、酒效直接杀 +1 伤害与回合结束失效、规则 v12 濒死者酒自救 1 点体力与 v3–v11 跨座位兼容回放、急救者在私有濒死窗口将红色非桃实体牌当作桃、苦肉主动技能在 0 点体力的共享濒死救援续接、决斗交替响应、南蛮入侵/万箭齐发逐目标响应、桃园结义逐目标恢复、五谷丰登公开翻牌/私有选牌、规则版本 4 的过河拆桥目标不透明牌位选择或公开装备/判定区牌目标选择、规则版本 4 的顺手牵羊距离一目标不透明牌位选择取得或公开装备/判定区牌目标选择、铁索连环一/二目标选择与火/雷同额传导、反馈取得伤害牌、遗计私有摸牌/跨座位分配、节命按公开手牌数补牌、刚烈公开判定/来源私有反制、可抵消锦囊效果前的有限无懈响应均可暂停观察；无中生有无目标摸牌、基础濒死求桃、处理区与事件顺序固定；属性抗性、通用非受伤者触发、除回春外更复杂的主动技能多目标/多效果和复杂濒死响应仍未开放 |
| K6 距离与装备（基础切片 DONE） | 距离查询、攻击范围、五类装备槽、装备替换/死亡清理、公开装备快照、被动距离修正 | 诸葛连弩、青釭剑、八卦阵、赤兔、绝影、玉玺、马术、奇才 | 换装、战斗距离、规则 v13 牌面武器攻击范围及 v1–v12 旧范围回放、摸牌 modifier、青釭剑无视防具、马术距离 -1 和死亡弃装均有测试；八卦阵的判定入口由 K7 扩展 |
| K7 锦囊与判定（基础切片 DONE） | 判定帧、判定区移动、公开判定区目标牌精确选择、事件顺序、八卦阵防御、锦囊效果前有限无懈响应、铁索连环公开标记与属性伤害传导、鬼才判定替换、乐不思蜀/兵粮寸断/闪电延时判定、累计阶段效果 | 八卦阵、无懈可击、铁索连环、鬼才、乐不思蜀、兵粮寸断、闪电及相关判定内容 | 红色判定、有限无懈链、铁索传导、鬼才替换、两种阶段跳过与闪电命中/转移/雷电伤害、公开判定区目标牌移动可回放；复杂改判、私有目标候选和复杂多层响应仍待后续 |
| M1 2v2（DONE） | 通用 Team 模式策略、阵营公开视图 | 2v2 模式包与 AI 权重 | 四人完整对局稳定结束，无身份局特判；普通视图不泄漏手牌/seed |
| M2 国战 Lite（DONE） | 双将、暗将、明置、势力投影、双将体力 | 同势力双将池、亮将技能 | 暗将不泄漏；亮将前后技能/体力正确 |
| M3 完整国战 | 野心家、势力人数限制、阵法/围攻接口、模式牌堆 | 完整国战规则与内容 | 关键模式场景矩阵通过 |
| K8 录像与存档（兼容切片 DONE） | 可信命令 Checkpoint、命令流录像、SchemaVersion、内容包签名、规范化内容指纹、规则行为版本 | 内容兼容迁移与完整内部状态存档 | 命令驱动暂停点可保存恢复；同版本定义漂移被拒绝；缺少规则版本的旧 Checkpoint 保留历史事件语义；完整录像/内部状态存档仍待后续 |
| K9 AI 与体验 | 通用 AI 行动接口、模式策略、批量模拟、WPF 选择器 | 内容启发式与技能策略 | AI 不读隐藏状态；基准胜率和耗时有报告 |

## K9 首轮可玩性切片

M1 2v2 已落地：`standard-team-modes@1.0.0` 注册 `team:standard-2v2`，Core 复用既有选将、结算、AI、事件和回放边界，公开 `TeamId` 但保持手牌与 seed 脱敏；WPF 新局设置可直接进入 2v2，座位阵营标签、目标文案和胜负音效已接通。当前验证为 Core 122/122、WPF 36/36；四人固定 seed 全 AI 对局可终止并与 Checkpoint/Replay 保持一致。

K9 首轮已有可玩性切片：围桌 UI、玩家手动弃牌、本地继续存档、战斗动画与原创音效、保持旧回放兼容的 v2 阵营 AI、v12 酒仅自救与 v3–v11 跨座位兼容回放、规则版本 4 的目标手牌不透明牌位选择、规则版本 5 的致命伤害后触发窗口、急救红牌濒死转化、随牌局更新的指南和图鉴，以及杀/闪/桃/弃牌四步新手演练。该阶段验证为 Core 122/122、WPF 36/36；此前 256 场固定种子 AI 对照牌局均已结束。仍未宣称通用模式策略、完整规则、难度系统、持久化教学进度或桌面输入验收完成。实现与证据见 [`UI_PLAYABLE_ITERATION.md`](UI_PLAYABLE_ITERATION.md)、[`TACTICAL_AI.md`](TACTICAL_AI.md)、[`PLAYER_GUIDE.md`](PLAYER_GUIDE.md) 和 [`NEW_PLAYER_TUTORIAL.md`](NEW_PLAYER_TUTORIAL.md)。

## K1 已交付

- `CardZoneStore` 成为摸牌堆、手牌、处理区、弃牌堆、装备区、判定区和移出游戏区的唯一实体牌位置来源；
- `Move`、同源批量移动和通用 `MoveBatch` 都先整体验证，非法目标或错误来源不会产生部分移动；
- 杀、火杀、雷杀、闪、桃、决斗，初始发牌、摸牌、自动弃牌、阵亡弃牌、主公惩罚和重洗均通过统一移动入口；
- 等待闪时，造成伤害的杀真实停留在 `Processing`；奸雄从该区取得伤害牌，不再依赖虚构布尔状态；
- `CardMovementRecord`、类型化 `CardMoveReason` 和可信宿主牌区诊断已经可用；玩家快照只增加公开的处理区数量；
- 日志、AI 思考、卡牌移动和状态通知在公共操作提交后派发；订阅者异常被隔离并进入限长诊断；
- 玩家询问集合采用防御性投影；AI 结束出牌必定发布最终状态；步数保护只在活动响应收尾后的稳定边界判平；
- 31/31 Console 检查通过，隔离目录下完整 Release 构建为 0 warning / 0 error，格式校验通过。
- 额外审查覆盖 384 组逐步真人 API、1000 个长程全 AI seed 和 1000-seed 卡牌生命周期；未发现牌区、处理区或 pending 一致性失败。

K1 还不是通用事件/技能系统。`CardMoved` 当前是提交后的宿主观察通知，不能让内容代码在其中反向修改状态；内部 `CardsMoving/CardsMoved` 触发时机随 K5 类型化结算交付。

## K2/K3/K4 已交付；K6 基础与 K7 判定切片已交付

- `GameCommand`、`CommandResult` 和稳定 `CommandErrorCode` 已提供统一 `Submit` 入口；过期 Revision、错误操作者、Prompt、Choice、卡牌或目标均返回拒绝结果，不用异常表达玩家输入错误；
- `PendingDecision` 增加 PromptId、发布 Revision 和完整 `PromptChoice`；出牌的牌/目标组合以及闪/不响应选项由核心逐项发布，旧 `ValidCardIds`/`ValidTargetSeats` 仍作为 WPF 兼容投影；
- 旧 `Start`、`Advance`、`HumanPlay`、`HumanEndPlay`、`HumanRespond` 和单步 API 仍可用，并共享同一 Revision/提交后通知边界；
- `ContentRegistry` 按依赖拓扑冻结包清单，拒绝重复 ID、未知牌/技能引用、版本不足和依赖环；每个 Registry 的字典和嵌套列表均为独立只读投影；
- `standard-active-skills@1.0.0` 作为依赖 Standard 的可选扩展包，注册 `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、`standard:mashu`、`standard:qicai`、七个演示武将和 `identity:active-skills-8/5`；`UseSkillCommand`、`LegalActionKind.UseSkill`、`ActiveSkillFrame` 和主动技能类型化事件沿统一命令/回放边界接入，标准 `Create()` 内容指纹保持不变。
- 新增 `CardGame.Content.Standard`，注册标准基本牌、装备牌、十三个兼容技能（含 standard:none）、十四名武将、基础演示牌堆和八人身份模式元数据；WPF 通过该 Registry 创建对局，Core 未注册时仍保留兼容内置路径；当前标准牌堆包含二十七种牌型，并包含 1 张仁王盾，并包含各 2 张乐不思蜀、兵粮寸断和闪电。
- K4 开局切片已通过 Registry 的 `ModeDefinition` 选择 5/8 人身份分布、牌堆、候选数量和武将池；`UseInteractiveSetup = true` 时座次/身份/选将/共享池去重/洗牌/逐轮发牌均在可暂停状态机内执行，WPF 已使用该路径；旧构造式 Demo 仍作为兼容适配器保留；
- K4 的 `SelectGeneralCommand` 使用私有 `PendingDecision`、`ValidContentIds` 和精确 Choice；AI 只收到本座候选，选中武将直到全部完成后才公开；
- K4 补充覆盖 5 人全 AI 选将和整局终止、8 人真人选将信息隔离、共享池无重复以及同 seed + 同选择命令的快照/事件确定性；
- 可信宿主 `Events`/`EventCommitted` 已提供带 `EventId`、Revision、CorrelationId 的类型化观察投影；K5 切片已增加数据型结算帧和稳定观察事件，内容可触发时机/完整 EventBatch 仍待 K5 完整阶段；
- K5 基础结算切片已将杀/火杀/雷杀/闪/桃/酒/决斗/无中生有/南蛮入侵/万箭齐发/桃园结义/五谷丰登/过河拆桥/顺手牵羊/火攻/基础濒死/死亡链包装为可序列化 `ResolutionFrame` 栈；属性杀仍复用 `RespondDodge`，并将 `DamageNature` 与实际伤害金额写入 `DamageFrame`、`DamageRequestedEvent`、`DamageAppliedEvent`、`AfterDamageEvent`、`DamageSkillContext` 和奸雄上下文；酒通过独立 `AlcoholAppliedEvent`/`AlcoholExpiredEvent` 表示一次性直接杀加伤和回合结束失效，规则 v12 只有濒死者本人可通过自己的私有濒死 Prompt 使用酒自救 1 点体力并生成类型化响应/恢复事件；v3–v11 保留跨座位兼容回放，v1/v2 仍按原仅自救语义；同时新增 `RespondSlash`、`RespondDodge`、`SelectHarvestCard`、`FireAttackReveal`、`FireAttackDiscard`、单目标隐藏手牌不透明牌位选择与公开装备/判定区牌精确目标、私有展示/同花色弃牌、交替响应、`CardUseFrame.TargetIndex`、`GroupCardUsedEvent`、通用 `GroupResponseEvent`、`CardsRevealedEvent`、`HarvestCardSelectedEvent`、`FireAttackCardRevealedEvent`、`FireAttackResolvedEvent`、`TargetCardDiscardedEvent`、`TargetCardTakenEvent` 和多目标 `RecoveryFrame`；无中生有使用空目标列表并通过统一摸牌入口完成即时效果，南蛮入侵和万箭齐发按固定座次逐目标完成杀/闪响应或单次伤害，桃园结义按固定座次逐目标恢复，五谷丰登公开翻牌后按固定座次私有选牌，过河拆桥在规则版本 4 通过私有不透明牌位选择移除目标手牌，v1–v3 回放按确定性随机数盲弃，或按 `TargetCardId` 弃置公开装备/判定区牌，顺手牵羊在规则版本 4 按座位环距离一通过私有不透明牌位选择取得目标手牌，v1–v3 回放按确定性盲取，或按 `TargetCardId` 取得目标公开装备/判定区牌，火攻按目标私有展示、攻击者同花色弃牌后进入火焰伤害链；伤害后候选中新增援护这一明确的跨座位弃牌恢复效果和刚烈这一受伤者判定/来源反制效果；栈仍只属于可信宿主，不进入普通玩家快照；
- 122/122 Console 和 36/36 WPF 检查通过，隔离目录下完整 Release 构建为 0 warning / 0 error，格式校验通过；新增反馈伤害后可选触发、援护跨座位弃牌恢复、刚烈公开判定/来源私有两牌反制与濒死续接、武圣红牌按杀转化、龙胆闪/杀响应转化、马术公开距离修正、奇才公开锦囊距离豁免、v12 酒仅自救与 v3–v11 跨座位兼容回放、`PlayedCardKind` 有效/物理牌型分离、火攻两段私有 Prompt、`DamageTriggerWindowFrame` 候选游标及窗口生命周期事件、`DamageSkillFrame` 暂停恢复、遗计跨手牌分配、节命公开目标筛选/补牌至上限、命令日志 JSON 编解码与确定性重放、可信命令 Checkpoint 私有 Prompt 恢复和同版本内容漂移拒绝、群体父帧续接、稳定伤害触发候选排序、装备槽替换/距离/范围 modifier、公开装备/判定区牌目标精确选择、规则 4 目标手牌不透明牌位选择、规则 5 致命伤害后触发窗口、青釭剑无视防具、仁王盾阻挡黑色杀的类型化结算、八卦阵 `JudgmentFrame`/红色判定防御、无懈可击有限多层响应、铁索连环精确一/二目标与火/雷伤害传导、鬼才私有改判、乐不思蜀/兵粮寸断/闪电延时判定红黑分支、闪电命中/转移与雷电伤害续接、同目标累计效果、判定区收尾、阵亡清理和视图脱敏回归。
- 主动技能增量覆盖 `UseSkillCommand` 的苦肉空牌/空目标、苦肉在 0 点体力的共享濒死/救援续接、制衡私有多选和仁德私有手牌/目标选择精确校验、回春双牌/多目标选择与逐目标恢复、`ActiveSkillFrame` 生命周期、体力变化/弃牌/摸牌/交牌/恢复/完成事件、`skill.kujin.draw`/`skill.zhiheng.discard`/`skill.zhiheng.draw`/`skill.rende.give-card`/`skill.qingnang.discard`/`skill.huichun.discard` 移动 reason、扩展 Registry 与旧 Standard Registry 隔离、普通快照脱敏、Checkpoint 恢复后的确定性事件一致性，以及 WPF 默认扩展包按钮的离屏布局、目标高亮和命令提交；马术另覆盖 `GetCombatDistance`、顺手牵羊距离门槛、AI 合法动作输入和 WPF 距离文本观察；奇才覆盖锦囊距离豁免、顺手牵羊合法动作和 WPF 目标选择观察。
- 已接受的 `GameCommand` 可由可信宿主通过 `AcceptedCommands` 记录，使用 `CommandJson` 编解码并由 `GameReplay` 在相同 seed、内容 Registry 和 Revision/Prompt 校验下重放；命令日志不属于玩家视图。
- `standard:feedback` 已接入 `IPassiveSkill.OffersDamageCardChoice`：目标存活且伤害牌仍在 `Processing` 时，Core 先按固定键冻结全部当前候选，再压入可序列化 `DamageTriggerWindowFrame`；每个可选技能再压入 `DamageSkillFrame`，完成后递增 `CandidateIndex` 并继续窗口。`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent` 保留 `CandidateId`/`Priority`，只向反馈者发布 `DecisionKind.Feedback`；发动后按 `skill.feedback.claim-damage-card` 移入技能拥有者手牌并发布 `DamageCardClaimedEvent`，跳过则正常弃牌。候选收集已改为逐个存活拥有者调用 `CanTriggerAfterDamage`；`DamageTriggerScope`/`AfterDamageTriggerScope` 进一步把受伤者、其他存活角色和任意存活角色的座位关系变成通用契约，援护复用 `OtherLivingPlayer`；刚烈作为受伤者候选的显式效果，红色判定后另向伤害来源发布私有反制 Choice。复杂的跨座位效果仍待后续。
- `standard:yiji` 已接入同一伤害触发窗口：郭嘉受伤后私有摸两张牌，`DecisionKind.Yiji` 发布精确的牌/其他存活目标组合；AI 与人类均只能使用自己的快照/Prompt，发动后通过 `skill.yiji.give-card` 将一张牌从拥有者手牌移至目标手牌，并发布可信宿主的 `DamageSkillCardsDrawnEvent`/`DamageSkillCardGivenEvent`；普通视图不泄漏候选牌面。
- `standard:jieming` 已接入同一伤害触发窗口：荀彧受伤后发布私有 `DecisionKind.Jieming` 目标选择，合法目标只按存活、公开手牌数量和体力上限判断，发动后经 `skill.jieming.draw` 摸牌至目标体力上限，并在 `DamageSkillCardsDrawnEvent.TargetSeat`/移动账本中保留可信宿主证据；AI 和人类都不能从目标隐藏手牌牌面推断选择。
- `standard:yuanhu` 已接入同一伤害触发窗口：援护者通过 `DamageTriggerScope.OtherLivingPlayer` 在其他角色受到正伤害后获得私有 `DecisionKind.Yuanhu` 弃牌 Choice；发动时按 `skill.yuanhu.discard` 将拥有者一张手牌移入弃牌堆，再用 `RecoveryFrame` 令固定伤害目标回复 1 点体力。`DamageSkillCardDiscardedEvent`、`RecoveryAppliedEvent` 和 `DamageSkillResolvedEvent.EffectTargetSeat` 保留可信宿主证据，普通玩家视图不泄漏弃牌 ID；这是当前受约束的非受伤者触发效果，后续技能可复用范围契约。
- `standard:ganglie` 已接入同一伤害触发窗口：受伤者获得私有 `DecisionKind.Ganglie` 发动/跳过 Choice；发动后公开判定，红色时向伤害来源发布私有 `DecisionKind.GangliePunish`，可选择精确两张手牌弃置或承受 1 点伤害。当前规则版本把反制伤害接入嵌套 `DamageFrame` 与完整伤害事件链；旧 Checkpoint 缺少规则版本时继续使用历史事件形状。判定事件、结果事件和牌区移动保持可信宿主记录，濒死救援完成后回到原伤害触发游标，普通视图不泄漏来源手牌 ID。
- `standard:guicai` 已接入 K7 判定前窗口：判定牌进入公开 `Judgment(target)` 后，Core 按 `JudgmentTriggerOrdering` 冻结存活鬼才候选，向当前拥有者发布私有 `DecisionKind.Guicai` 替换/跳过 Choice；替换牌按 `Hand(owner) → Processing → Judgment(target)` 进入同一帧，旧牌先按 `judgment.finish` 结束，最后由 `JudgmentResolvedEvent` 公开最终红/黑结果。AI 只读取自己的过滤快照和发布的有效手牌 ID，普通视图不泄漏其他拥有者的手牌。
- 伤害触发同时覆盖 AI 快照决策、人类 `AnswerPromptCommand`、`DamageSkillRequestedEvent`/`DamageSkillResolvedEvent`、反馈取得事件、遗计分配事件、节命补牌事件和援护弃牌/恢复事件；普通视图看不到其他 responder 的 Prompt 与隐藏牌 ID。
- 武圣同时覆盖 AI 选择、人类 `PlayCardCommand.PlayedCardKind` 和 WPF 独立转化按钮；有效牌型事件记录 `Slash`，物理移动账本保留原始实体牌，原生牌型与转化牌型不会因兼容调用产生歧义。
- 龙胆同时覆盖 AI/人类响应牌筛选、人类精确 `PromptChoice` 和 WPF 响应选择列表；闪转杀复用 `PlayedCardKind`，杀转闪通过 `CardKind? EffectiveCardKind` 记录有效响应牌型，物理移动账本仍保留同一实体牌。

## K6 基础切片已交付

- `EquipmentSlot` 固定为武器、防具、进攻坐骑、防御坐骑、宝物五类；装备牌通过 `LegalActionKind.Equip` 走普通出牌提交，实体牌保持 `Hand → Processing → Equipment`。
- 同槽装备替换先将旧实体牌移入弃牌堆，再让新牌进入装备区；`EquipmentChangedEvent`、`equipment.use`、`equipment.enter`、`equipment.replace` 和死亡清理 reason 形成可审计生命周期。
- `GetCombatDistance` 计算存活座位环距离并叠加赤兔/绝影/马术修正，`GetAttackRange` 消费武器范围 modifier；诸葛连弩取消杀次数限制，玉玺增加摸牌，装备在普通快照、AI 快照和 WPF 座位卡上均公开；奇才的距离型锦囊豁免通过 `GetLegalActions` 统一发布。
- 八卦阵已在 K7 最小切片中接入直接杀响应：目标可选择公开的判定选项，判定牌经过 `Judgment(seat)` 后公开结果并弃置，红色判定视为闪；鬼才已在同一 `JudgmentFrame` 上接入判定生效前的私有换牌和候选游标；青釭剑已在 K6/K7 扩展中接入武器槽、直接杀无视防具的基础 modifier 和类型化结算记录，过河拆桥/顺手牵羊的公开装备与判定区牌目标选择也已接入；乐不思蜀/兵粮寸断/闪电基础延时判定已开放，其中闪电自用时按黑桃 2 至 9 命中 3 点雷电伤害，否则转移到下一名存活角色；复杂装备主动效果、私有判定区目标选择和更复杂改判仍保留为后续切片。

## 接下来三个核心阶段

1. **K4 扩展验证**：把候选池扩大为可配置内容批次，补齐 5/8 人固定 seed 矩阵和真正的模式策略对象；兼容路径逐步迁移到交互开局。
2. **K5 完整结算**：在现有决斗/南蛮入侵/基础濒死帧栈上继续扩展跨事件触发收集/排序、通用非受伤者触发条件、增加多伤害/重复救援和完整状态 Checkpoint；当前已开放伤害候选窗口与游标、反馈取得、遗计跨座位分配、节命公开目标补牌、援护这一明确的跨座位弃牌恢复示例，以及苦肉主动技能濒死续接，保留兼容 API。
3. **K7 扩展**：继续补齐私有判定区候选、复杂无懈/改判和其他判定内容，再按顺序开放新锦囊、响应链和内容批次；无懈可击的固定座次有限多层响应、铁索连环的精确一/二目标与火/雷传导、鬼才的基础判定替换、乐不思蜀/兵粮寸断/闪电的基础延时判定与累计阶段效果、过河拆桥/顺手牵羊的公开装备与判定区牌目标选择、青釭剑基础无视防具切片已完成。

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
