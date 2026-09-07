# Standard 内容清单（C0 草案）

更新时间：2026-09-07

这是内容流水线的设计清单和正式包的对照表。稳定内容 ID 使用 `package:name` 形式；`implemented-registry` 表示已经进入 `CardGame.Content.Standard`，`implemented-legacy` 表示仍由 Core 的 `CardKind`/`SkillKind` 兼容投影运行，`planned` 表示内容定义已规划但等待后续核心 API。K1 的牌区生命周期、K2 的 Prompt/Choice、reason、可见性和强制场景见 [`CARD_MOVEMENT_CONTRACT.md`](./CARD_MOVEMENT_CONTRACT.md)；`definitionId`/`instanceId` 的 Registry 关系已在 K3 冻结。所有文案、AI 标签和规则描述均为本项目自有文字，不包含卡面、插画、音频或其他素材。

## 1. 基本牌

| 内容 ID | 名称 | 类别 | 状态 | AI 标签 | 核心依赖 |
| --- | --- | --- | --- | --- | --- |
| `standard:slash` | 杀 | Basic | implemented-registry | attack, pressure, finish, target-one | K2 精确目标 Choice；Core 兼容结算 |
| `standard:fire_slash` | 火杀 | Basic | implemented-registry | attack, nature-fire, pressure | K5 共用杀/闪响应；`DamageNature.Fire` 类型化伤害 |
| `standard:thunder_slash` | 雷杀 | Basic | implemented-registry | attack, nature-thunder, pressure | K5 共用杀/闪响应；`DamageNature.Thunder` 类型化伤害 |
| `standard:dodge` | 闪 | Basic | implemented-registry | response, survival, retain | K2 `AnswerPromptCommand`；Core 兼容结算 |
| `standard:peach` | 桃 | Basic | implemented-registry | recovery, self, dying-save | K2 精确 Choice；K5 基础出牌自救与濒死救援 |
| `standard:duel` | 决斗 | Trick | implemented-registry | attack, repeated-response | K5 `RespondSlash` 交替响应与伤害结算 |
| `standard:draw_two` | 无中生有 | Trick | implemented-registry | draw, card-advantage | K5 无目标 `CardUseFrame` 与摸牌效果 |
| `standard:barbarian_assault` | 南蛮入侵 | Trick | implemented-registry | attack, all-opponents, repeated-response | K5 `TargetIndex` 逐目标 `RespondSlash` 与单次伤害续接 |
| `standard:arrow_barrage` | 万箭齐发 | Trick | implemented-registry | attack, all-opponents, repeated-response | K5 `TargetIndex` 逐目标 `RespondDodge` 与单次伤害续接 |
| `standard:peach_garden` | 桃园结义 | Trick | implemented-registry | recovery, all-alive, repeated-effect | K5 `TargetIndex` 逐目标 `RecoveryFrame` 与恢复事件 |
| `standard:five_grains` | 五谷丰登 | Trick | implemented-registry | public-reveal, draft, all-alive, private-choice | K5 `TargetIndex` 逐目标私有选牌；公共展示牌与 `HarvestCardSelectedEvent` |
| `standard:dismantlement` | 过河拆桥 | Trick | implemented-registry | target-one, hidden-hand-discard | K5 单目标 `CardUseFrame`；确定性盲弃目标手牌，公共事件不含牌面 |
| `standard:snatch` | 顺手牵羊 | Trick | implemented-registry | target-one, hidden-hand-take, distance-one | K5 单目标 `CardUseFrame`；座位环距离一，确定性盲取目标手牌，公共事件不含牌面 |
| `standard:fire_attack` | 火攻 | Trick | implemented-registry | target-one, private-reveal, same-suit-discard, nature-fire | K5 单目标 `CardUseFrame`；目标私有展示、攻击者同花色弃牌与 `DamageNature.Fire` |
| `standard:alcohol` | 酒 | Basic | implemented-registry | slash-boost, one-shot, dying-self-rescue | K5：无目标 `CardUseFrame`、公开酒效、直接杀伤害金额与回合结束失效；濒死者自救 1 点体力，不可用酒救援他人 |

当前可运行的牌面描述和 AI 数值仍由兼容目录 `CardGame.Core/Content.cs` 提供；Standard Registry 先冻结稳定 ID、描述和 AI 标签，后续行为迁移不改变这些 ID，也不让 UI 或录像依赖枚举顺序。

## 2. 牌堆配方

### `standard:basic-demo`

这是当前可运行 Demo 的兼容配方，不声称是任何商业游戏的官方卡表：

| 参数 | 值 |
| --- | --- |
| 总牌数 | 72 |
| `standard:slash` | 18 |
| `standard:fire_slash` | 2 |
| `standard:thunder_slash` | 2 |
| `standard:alcohol` | 2 |
| `standard:dodge` | 18 |
| `standard:peach` | 10 |
| `standard:duel` | 4 |
| `standard:draw_two` | 2 |
| `standard:barbarian_assault` | 2 |
| `standard:arrow_barrage` | 2 |
| `standard:peach_garden` | 2 |
| `standard:five_grains` | 2 |
| `standard:dismantlement` | 2 |
| `standard:snatch` | 2 |
| `standard:fire_attack` | 2 |
| 初始手牌 | 每人 4 张，逐轮发牌 |
| 摸牌阶段 | 基础 2 张，英姿由技能 modifier 增加 |
| 首行动者 | 主公 |
| 兼容别名 | `identity_8_basic_demo` |
| 状态 | implemented-registry |

当前标准身份牌堆沿用同一个 Recipe 形状；酒的出牌阶段一次性直接杀加伤和濒死者自救已实现，火攻的私有展示/同花色弃牌伤害已实现，酒不能救援其他角色；后续仍可追加更多锦囊和装备，不把当前 72 张演示牌堆误称为完整商业卡表。

## 3. 身份模式开局配置

| 模式 ID | 人数/身份 | 牌堆 | 候选数 | 武将池 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `identity:standard-8` | 8；1/2/4/1 | `standard:basic-demo` | 3 | 当前 12 名 Standard 武将 | implemented-registry + K4 setup |
| `identity:standard-5` | 5；1/1/2/1 | `standard:basic-demo` | 3 | 当前 12 名 Standard 武将 | implemented-registry + K4 setup |

`UseInteractiveSetup = true` 时，模式配置驱动私有单将候选、共享池移除、结果公开、确定性洗牌和逐轮发牌；候选不会进入其他玩家快照。更大武将池、同时选将和国战双将仍待后续阶段。

## 4. 标准武将与技能

### 当前十二人演示池

| 内容 ID | 武将 | 兼容 ID | 技能 ID | 状态 | AI 标签 |
| --- | --- | --- | --- | --- | --- |
| `standard:cao-cao` | 曹操 | `cao-cao` | `standard:jianxiong` | implemented-registry | damage-card, retain |
| `standard:zhang-fei` | 张飞 | `zhang-fei` | `standard:paoxiao` | implemented-registry | attack, unlimited-slash |
| `standard:zhou-yu` | 周瑜 | `zhou-yu` | `standard:yingzi` | implemented-registry | draw, card-advantage |
| `standard:zhuge-liang` | 诸葛亮 | `zhuge-liang` | `standard:kongcheng` | implemented-registry | hand-zero, target-lock |
| `standard:liu-bei` | 刘备 | `liu-bei` | `standard:none` | implemented-registry | placeholder |
| `standard:guan-yu` | 关羽 | `guan-yu` | `standard:wusheng` | implemented-registry | red-card, conversion, attack |
| `standard:zhao-yun` | 赵云 | `zhao-yun` | `standard:longdan` | implemented-registry | slash-dodge, conversion, response |
| `standard:sun-quan` | 孙权 | `sun-quan` | `standard:none` | implemented-registry | placeholder |
| `standard:hua-tuo` | 华佗 | `hua-tuo` | `standard:feedback` | implemented-registry | damage-card, after-damage, private-choice, retain |
| `standard:guo-jia` | 郭嘉 | `guo-jia` | `standard:yiji` | implemented-registry | draw-two, after-damage, private-choice, cross-seat-gift |
| `standard:xun-yu` | 荀彧 | `xun-yu` | `standard:jieming` | implemented-registry | draw-to-max-hand, after-damage, private-choice, public-target |
| `standard:demo-yuanhu` | 援护者 | `demo-yuanhu` | `standard:yuanhu` | implemented-registry | after-damage, cross-seat, private-choice, discard, recovery |

刘备和孙权仍使用 `standard:none` 作为明确的 Demo 占位，不代表正式技能；关羽已接入 `standard:wusheng` 的红色牌按杀使用最小切片，赵云已接入 `standard:longdan` 的出牌/响应杀闪互转最小切片，华佗已接入 `standard:feedback` 的存活伤害后私有可选触发最小切片，郭嘉已接入 `standard:yiji` 的受伤后私有摸牌和跨座位分配最小切片，荀彧已接入 `standard:jieming` 的受伤后公开目标筛选和补牌至上限最小切片，援护者已接入 `standard:yuanhu` 的明确跨座位弃牌恢复最小切片。新增技能必须有与语义匹配的 modifier、trigger、effect 或 prompt 入口，不能借用现有技能名制造假实现。

### 技能状态清单

| 技能 ID | 名称 | 内容方向 | 状态 | 依赖 |
| --- | --- | --- | --- | --- |
| `standard:wusheng` | 武圣 | 红色牌转化为杀 | implemented-registry | K5：`PlayedCardKind` 与物理牌实例分离 |
| `standard:longdan` | 龙胆 | 杀/闪互相转化 | implemented-registry | K5：有效/物理牌型分离与精确响应 Prompt |
| `standard:ganglie` | 刚烈 | 受伤后判定并选择伤害/弃牌 | planned | K5/K7：判定与 prompt |
| `standard:yiji` | 遗计 | 受伤后私有摸牌并分配一张给其他角色 | implemented-registry | K1/K2/K5：牌移动、精确牌/目标 Choice 与隐私 |
| `standard:jieming` | 节命 | 受伤后按公开手牌数补牌至目标体力上限 | implemented-registry | K1/K2/K5：公开目标筛选、私有 Choice、牌堆到目标手牌移动与隐私 |
| `standard:yuanhu` | 援护 | 其他角色受伤后弃置一张手牌并令其回复 1 点体力 | implemented-registry | K5：显式跨座位候选、私有弃牌 Choice、恢复子帧与脱敏事件 |
| `standard:guicai` | 鬼才 | 判定修改 | planned | K7：判定窗口 |
| `standard:rende` | 仁德 | 主动交牌并按数量回复 | planned | K2/K5：主动选牌与 effect |
| `standard:zhiheng` | 制衡 | 弃牌后摸等量牌 | planned | K2/K5：主动弃牌与牌移动 |

## 5. 锦囊清单

| 内容 ID | 名称 | AI 标签 | 状态 | 需要验证的核心能力 |
| --- | --- | --- | --- | --- |
| `standard:draw_two` | 无中生有 | draw, card-advantage | implemented-registry | K5：无目标 `CardUseFrame` 与摸牌效果 |
| `standard:dismantlement` | 过河拆桥 | target-one, hidden-hand-discard | implemented-registry | K5：目标选择、确定性盲弃手牌与隐藏牌面事件；装备/判定区及私有候选仍待后续 |
| `standard:snatch` | 顺手牵羊 | target-one, hidden-hand-take, distance-one | implemented-registry | K5：距离一合法性、确定性盲取手牌与 `TargetCardTakenEvent`；装备区仍待后续 |
| `standard:duel` | 决斗 | attack, repeated-response | implemented-registry | K5：交替杀响应链已开放；群体/多伤害仍待后续 |
| `standard:nullification` | 无懈可击 | counter, response-chain | planned | K5/K7：链式响应 |
| `standard:peach_garden` | 桃园结义 | recovery, all-alive, repeated-effect | implemented-registry | K5：`TargetIndex` 逐目标恢复、`RecoveryFrame` 与 `RecoveryAppliedEvent` |
| `standard:five_grains` | 五谷丰登 | public-reveal, draft, all-alive, private-choice | implemented-registry | K5：公开展示牌、当前 picker 私有 `SelectHarvestCard`、逐人选牌事件 |
| `standard:barbarian_assault` | 南蛮入侵 | attack, all-opponents, repeated-response | implemented-registry | K5：`TargetIndex` 逐目标杀响应、单次伤害与濒死续接 |
| `standard:arrow_barrage` | 万箭齐发 | attack, all-opponents, repeated-response | implemented-registry | K5：`TargetIndex` 逐目标闪响应、单次伤害与濒死续接 |
| `standard:iron_chain` | 铁索连环 | target-two, state-mark | planned | K5/K7：状态标记与属性伤害 |

## 6. 装备清单

| 内容 ID | 名称 | 类型 | AI 标签 | 状态 | 需要验证的核心能力 |
| --- | --- | --- | --- | --- | --- |
| `standard:crossbow` | 诸葛连弩 | Weapon | attack, unlimited-slash | planned | K6：武器槽、攻击次数 modifier |
| `standard:qinggang_sword` | 青釭剑 | Weapon | attack, ignore-armor | planned | K6：武器命中效果 |
| `standard:bagua` | 八卦阵 | Armor | response, judgment | planned | K6/K7：防具响应与判定 |
| `standard:offensive_horse` | -1 马 | Horse | range, attack | planned | K6：距离修正 |
| `standard:defensive_horse` | +1 马 | Horse | defense, distance | planned | K6：距离修正 |

装备内容必须进入 `Equipment` 区域，拥有替换、失效、死亡清理和持久 modifier 语义；在这些入口开放前不把装备伪装成普通手牌或一次性锦囊。

## 7. 内容级 AI 标签约定

AI 标签是内容包输入，不是隐藏身份推断：

- `attack` / `pressure` / `finish`：目标价值和击杀压力；
- `response` / `survival`：响应伤害和保留到后续回合；
- `recovery` / `dying-save`：回复体力或救援濒死；
- `draw` / `card-advantage` / `retain`：手牌收益和弃牌保留；
- `target-one` / `target-two` / `all-opponents`：目标数量约束；
- `distance` / `range` / `state-mark`：依赖通用查询或持久状态。

标签不能替代核心合法性校验，也不能让 AI 读取暗身份、暗牌、完整牌堆顺序或其他玩家的私有候选。
