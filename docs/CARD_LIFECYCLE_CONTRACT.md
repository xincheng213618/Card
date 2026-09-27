# 公共时机与规则集对照

本文件记录 2026-09-27 核查结果，以及后续扩展必须遵守的契约。表格中的“缺口”不是已实现接口。技能通过 `SkillProgram` 的窗口、条件和公共操作组合；不要因为某张武将牌需要一个时机，就在引擎里增加该人物的分支。

依据用户指定的[官方规则集 3.0 网页版](https://gltjk.com/sanguosha/rules/)，重点是[使用流程](https://gltjk.com/sanguosha/rules/flow/use.html)、[移动流程](https://gltjk.com/sanguosha/rules/flow/move.html)、[伤害流程](https://gltjk.com/sanguosha/rules/flow/damage.html)和[失去体力流程](https://gltjk.com/sanguosha/rules/flow/loselife.html)。网站是历史文本的网页整理；新版技能另保留其官方来源和版本，不用旧武将条目覆盖新技能。

## 当前可用的组合入口

| 需求 | 已有能力 | 覆盖与缺口 |
| --- | --- | --- |
| 使用牌时，例如两版强识 | `cardUseCommitted`，`CardActionContext` | 已接入普通杀、桃、酒、装备、锦囊的共用使用链。效果与触发分离，实体牌成本、转换来源和整张牌 ID 不丢失。 |
| 使用结算后 | `cardUseCompleted` | 本批补齐普通基本牌、装备和锦囊；多目标杀只触发一次。重铸和装备主动效果不进入。 |
| 目标数、距离、次数，例如天义 | `cardTargetCount`、规则查询与回合修正 | 选择目标前决定合法集合；与触发后修改目标是两种机制。 |
| 成为目标时，例如流离 | `slashTargetRedirecting`、`redirectCurrentAttack` | 目前专门适配杀；尚非所有牌型统一的四段目标流程。 |
| 指定目标后，例如铁骑、烈弓 | `cardUseTargetsFinalized`、`slashBeforeResponse`、`prohibitCurrentResponse` | 已有公共操作；部分能力仍在逐目标响应前执行，与规则集的全体目标阶段屏障尚未完全对齐。 |
| 成为目标后，例如贞烈 | `cardUseBeforeTargetEffects`、`nullifyCurrentCardEffect` | 杀/锦囊可使当前目标无效；这个名字不能作为“使用牌时”的替代。 |
| 伤害前防止/转移，例如天香 | `beforeDamageApplied`、`preventCurrentDamage`、`redirectCurrentDamage` | 已有公共帧；寒冰剑、麒麟弓等装备仍有独立适配，来源侧修正与受伤者侧修正尚未完全拆成统一窗口。 |
| 增伤，例如界徐盛破军 | `damageModifiers`、临时伤害修正 | 按来源/目标及牌类型判断，保留来源、实际数额和连环/转移边界。与目标阶段的扣牌效果分别组合。 |
| 受伤后摸牌 | `damageAppliedBeforeDying` / `afterDamageApplied`，按次/按点策略 | 能区分濒死前事实记录和救援后的收益；不是所有“体力降低”都算伤害。 |
| 失去装备，例如孙尚香枭姬、凌统旋风 | `cardsMoved`、`sourceZones: [equipment]`、按张/按批 | 枭姬已有数据定义。能力存在不等于所有旋风版本都已接入。普通牌流程的移动触发仍可能延迟到父结算空闲处，程序内移动已有局部续接。 |
| 获得牌后再触发 | `cardsGained`、`destinationZones: [hand]`、原因过滤、按张/按批 | 以获得者收集候选，冻结移动前后的目的区数量。程序内摸牌/交牌在下一条指令前续接；普通牌和旧流程的移动仍在其安全边界调度。 |
| 失去体力后摸牌 | `afterHpLost`、`hpChangeOccurrence: perEvent / perPoint` | 记录实际失去量及变化前后体力，濒死结算完成后续接；伤害和直接设置体力不进入。 |
| 回复体力后 | `afterHpRecovered`、按次/按点、来源角色 | 所有 `RecoveryFrame` 统一记录实际回复量；满血回复不触发。程序指令、桃/酒完成、桃园结义每个目标之间均有续接；其他旧入口在安全边界调度。 |
| 封手牌，例如义绝 | 牌使用/打出禁止、手牌颜色限制、定向目标限制 | **部分**：已有潜袭式颜色过滤；完整义绝式区域、动作和期限组合尚需统一封牌策略。 |
| 封非锁定技能，例如界铁骑 | 技能实例与绑定索引 | **缺口**：禁止闪不等于技能失效。尚缺按技能标签、实例、期限过滤的统一抑制层。 |

## 后续窗口的设计约束

用牌流程应显式分为：支付与身份冻结 → 使用时 → 指定目标时 → 成为目标时 → 指定目标后 → 成为目标后 → 逐目标生效/响应 → 整张牌完成。阶段内按行动顺序收集候选。多目标结算前必须让所有目标完成同一时机，再进入下一时机；不要让第一个目标受到伤害后才询问第二个目标的“成为目标后”。

目标出现次数须与座位区分。整张牌触发用 ActionId 去重，逐目标触发用目标出现索引识别；流离可能产生同座位的多次结算，不能只用 `Distinct()` 当完整规则。当前引擎对此仍有限制，应先补出现索引和阶段屏障，再接需要该语义的技能。

伤害上下文须携带来源、接收者、来源牌/技能、属性、原始与当前数额、连环和转移来源。来源侧修改、目标侧修改、实际扣血、濒死、来源造成伤害后、目标受到伤害后、完成分别推进。防止伤害之后不得增加“已造成伤害”计数；转移不得重新套用已经结算过的来源增伤。

移动接口以原子批次记录每张实体牌的原区/目的区、操作原因、操作者、失去者和获得者。失装备按原区判断，得牌按目的区判断；摸牌也是移动，但得牌后摸牌必须通过原因/次数限制控制递归。替换装备要保留同一操作下的两条移动，不可把“用了装备”和“失去了装备”混为同一事件。

封牌与封技应进入统一规则查询。封牌保留被限制的牌区、牌型/花色、Use/Response/Discard 动作及期限；封技保留技能实例、标签（例如排除锁定技）、来源和清理时机。UI、AI、合法动作、转换、自动触发和暂停后复查都须读取同一策略。

每个真正上线的窗口必须同时具备：上下文能力声明、加载器拒绝无上下文的操作、冻结候选和事实、触发条件复查、可序列化游标、明确父流程续接、私密选择和公开事件边界、当前规则版本回放测试。仅增加枚举值或方法名不能算接口完成。

当前可发现的实际入口/操作由 `ProgramEntryCapabilities` 和操作 descriptor 提供，可用 `tools/Inspect-SkillProgram.ps1` 查询。新增技能优先复用这些节点；表中的缺口先做公共能力及跨技能场景测试，再写人物配置。

## rules 176：得牌、失去体力、回复后的配置契约

三个新入口使用现有 schema 62，新增内容的 `minimumRulesVersion` 应为 176。规则号上升是因为程序指令之间新增了可暂停的子窗口，且恢复帧会记录实际体力变化；旧规则回放不混用。

`cardsMoved` 仍是**离开指定区域**，例如手牌移入自己的装备区也属于失去手牌；它不等同于规则中失去所有权。`cardsGained` 是**移入手牌区**，目的区只接受 `["hand"]`，装备上身不算获得牌。两者共享如下字段：

| 字段 | 契约 |
| --- | --- |
| `movementOccurrence` | 必填；`perBatch` 按底层原子移动批次，`perCard` 按符合过滤条件的实体牌。现有连续摸牌可能包含多个批次，不把一次摸牌指令自动视为一个批次。 |
| `movementReasons` | 可选白名单，精确匹配移动账本的 namespaced 原因，例如 `rule.draw`；空列表不限制。 |
| `excludedMovementReasons` | 可选黑名单；不能与白名单重叠。 |
| `ignoreOwnSkillMovements` | 默认 `false`；设为 `true` 时排除同一拥有者、技能和技能实例所产生的移动。不会排除另一角色持有的同名技能。 |
| `movedCardCount` | 该拥有者和区域内、经过原因过滤的移动张数。 |
| `sourceZoneCountBefore/After` | 仅供 `cardsMoved` 条件使用，记录整个区域的变化前后数量。 |
| `destinationZoneCountBefore/After` | 仅供 `cardsGained` 条件使用，记录整个区域的变化前后数量。 |

例如“收到牌后摸一张，自己的这个技能摸牌不再次触发”：

```json
{
  "id": "after-gain",
  "window": "cardsGained",
  "subject": "owner",
  "destinationZones": ["hand"],
  "movementOccurrence": "perBatch",
  "ignoreOwnSkillMovements": true,
  "optional": true,
  "effects": [{ "op": "draw", "target": "owner", "amount": 1 }]
}
```

`afterHpLost` / `afterHpRecovered` 目前订阅 `subject: owner`。`hpChangeOccurrence` 默认 `perEvent`，也可选 `perPoint`；条件可读取 `hpChangeAmount`、`hpBeforeChange`、`hpAfterChange`。这些值属于原事件，救援后的当前体力用 `currentHp`，二者不能混淆。失去体力没有伤害来源；回复保留回复来源角色。规则依据见[失去体力流程](https://gltjk.com/sanguosha/rules/flow/loselife.html)和[回复体力流程](https://gltjk.com/sanguosha/rules/flow/recoverlife.html)。

候选、条件事实和出现索引在窗口开始时冻结，暂停后仍复查存活、技能实例和使用次数。触发效果造成的新事件作为子窗口完成，再恢复原事件。得牌后摸牌、失去体力后继续失去体力等定义，必须按具体规则配置原因、条件或次数限制；引擎不擅自吞掉规则允许的嵌套事件。

尚未实现：移动前拦截、失去体力前防止、任意体力变化订阅、体力上限变化订阅、翻面/横置订阅，以及上表标记的完整目标阶段屏障和封牌/封技策略。新增枚举不能替代实际流程适配，这些能力须分别补齐续接和回放验证后开放。
