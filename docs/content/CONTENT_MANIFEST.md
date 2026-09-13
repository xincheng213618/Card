# Standard 内容清单（C0 草案 / M3 试验）

更新时间：2026-09-12

这是内容流水线的设计清单和正式包的对照表。稳定内容 ID 使用 `package:name` 形式；`implemented-registry` 表示已经进入 `CardGame.Content.Standard`，`implemented-legacy` 表示仍由 Core 的 `CardKind`/`SkillKind` 兼容投影运行，`planned` 表示内容定义已规划但等待后续核心 API。K1 的牌区生命周期、K2 的 Prompt/Choice、reason、可见性和强制场景见 [`CARD_MOVEMENT_CONTRACT.md`](./CARD_MOVEMENT_CONTRACT.md)；`definitionId`/`instanceId` 的 Registry 关系已在 K3 冻结。所有文案、AI 标签和规则描述均为本项目自有文字，不包含卡面、插画、音频或其他素材。

## 0. 内容包与扩展入口

| 包 ID | 依赖 | 内容 | 状态 |
| --- | --- | --- | --- |
| `standard@1.11.0` | 无 | 基础牌、装备、技能、武将、演示牌堆和标准身份模式 | implemented-registry |
| `standard-active-skills@1.0.0` | `standard@1.11.0` | `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、`standard:mashu`、`standard:qicai`、七个技能演示武将、`identity:active-skills-8/5` | implemented-registry；可选扩展 |
| `standard-rescue-skills@1.0.0` | `standard-active-skills@1.0.0` | `standard:jijiu`、`standard:demo-jijiu`；扩展模式中的急救红牌濒死救援 | implemented-registry；可选扩展 |
| `standard-team-modes@1.0.0` | `standard@1.11.0` | `team:standard-2v2`；公开青/赤阵营和队伍胜负适配 | implemented-registry；可选扩展 |
| `standard-national-war-lite@1.1.0` | `standard@1.11.0` | `national:lite-4`；四人魏蜀双将国战 Lite | implemented-registry；可选扩展 |
| `standard-national-war-ambitious@1.0.0` | `standard-national-war-lite@1.0.0` | `national:ambitious-6`；魏 3、蜀 2、野心家 1 的六人独立势力试验 | implemented-registry；M3 可选扩展 |

扩展包不修改基础 Standard 包的注册结果或内容指纹；WPF 默认窗口显式选择包含急救层的扩展 Registry，普通测试/旧存档仍可使用基础 Registry 或不含急救的主动技能 Registry。

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
| `standard:dismantlement` | 过河拆桥 | Trick | implemented-registry | target-one, hidden-hand-discard, public-equipment-target, public-judgment-target | K5/K7；规则版本 4 单目标 `CardUseFrame`；手牌不透明牌位选择弃置，公开装备/判定区牌由 `TargetCardId` 精确选择，事件按来源区域脱敏 |
| `standard:snatch` | 顺手牵羊 | Trick | implemented-registry | target-one, hidden-hand-take, public-equipment-target, public-judgment-target, distance-one | K5/K7；规则版本 4 单目标 `CardUseFrame`；座位环距离一，手牌不透明牌位选择取得，公开装备/判定区牌由 `TargetCardId` 精确选择 |
| `standard:fire_attack` | 火攻 | Trick | implemented-registry | target-one, private-reveal, same-suit-discard, nature-fire | K5 单目标 `CardUseFrame`；目标私有展示、攻击者同花色弃牌与 `DamageNature.Fire` |
| `standard:indulgence` | 乐不思蜀 | Trick | implemented-registry | delayed-judgment, target-one, public-judgment, skip-play | K7：使用牌进入目标公开判定区；目标下回合摸牌前判定，红色跳过出牌阶段，黑色正常出牌；复用 `JudgmentFrame`、无懈和鬼才窗口 |
| `standard:supply_shortage` | 兵粮寸断 | Trick | implemented-registry | delayed-judgment, target-one, public-judgment, skip-draw | K7：使用牌进入有手牌目标的公开判定区；目标下回合摸牌前判定，黑色跳过摸牌阶段，红色正常摸牌；复用 `JudgmentFrame`、无懈和鬼才窗口 |
| `standard:lightning` | 闪电 | Trick | implemented-registry | delayed-judgment, self-target, public-judgment, thunder-damage, transfer | K7：只能对自己使用；下个回合黑桃 2 至 9 命中并造成 3 点雷电伤害，否则转移到下一名存活角色；复用 `JudgmentFrame`、鬼才窗口和延时牌收尾 |
| `standard:nullification` | 无懈可击 | Trick | implemented-registry | counter, response-chain | K7：锦囊效果前的固定座次私有响应；支持有限多层互相抵消 |
| `standard:iron_chain` | 铁索连环 | Trick | implemented-registry | target-two, state-mark, elemental-propagation | K5/K7：精确一/二目标、公开连环标记、火/雷伤害同额传导与有限无懈窗口 |
| `standard:alcohol` | 酒 | Basic | implemented-registry | slash-boost, one-shot, dying-rescue | K5：无目标 `CardUseFrame`、公开酒效、直接杀伤害金额与回合结束失效；当前规则版本的濒死窗口允许当前 responder 使用自己的酒救援濒死角色回复 1 点体力 |

当前可运行的牌面描述和 AI 数值仍由兼容目录 `CardGame.Core/Content.cs` 提供；Standard Registry 先冻结稳定 ID、描述和 AI 标签，后续行为迁移不改变这些 ID，也不让 UI 或录像依赖枚举顺序。

## 2. 牌堆配方

### `standard:basic-demo`

这是当前可运行 Demo 的兼容配方，不声称是任何商业游戏的官方卡表：

| 参数 | 值 |
| --- | --- |
| 总牌数 | 90 |
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
| `standard:indulgence` | 2 |
| `standard:supply_shortage` | 2 |
| `standard:lightning` | 2 |
| `standard:nullification` | 2 |
| `standard:iron_chain` | 2 |
| `standard:crossbow` | 2 |
| `standard:bagua` | 1 |
| `standard:qinggang_sword` | 1 |
| `standard:offensive_horse` | 1 |
| `standard:defensive_horse` | 1 |
| `standard:jade_seal` | 1 |
| `standard:renwang_shield` | 1 |
| 初始手牌 | 每人 4 张，逐轮发牌 |
| 摸牌阶段 | 基础 2 张，英姿由技能 modifier 增加 |
| 首行动者 | 主公 |
| 兼容别名 | `identity_8_basic_demo` |
| 状态 | implemented-registry |

当前标准身份牌堆沿用同一个 Recipe 形状；酒的出牌阶段一次性直接杀加伤和当前规则版本的濒死窗口救援已实现，火攻的私有展示/同花色弃牌伤害已实现，乐不思蜀、兵粮寸断和闪电的公开判定区、下回合判定及红黑/命中转移效果已实现，五类装备槽、七种装备牌（含仁王盾）、换装、基础战斗距离/攻击范围 modifier、青釭剑直接杀无视防具、仁王盾阻挡黑色杀和阵亡清理已实现，无懈可击已接入效果前的有限多层响应窗口，铁索连环已接入精确一/二目标、公开状态标记和火/雷伤害传导；后续仍可追加更多锦囊和装备效果，不把当前 90 张演示牌堆误称为完整商业卡表。标准包的 ID、标签和描述保持 v1.11.0 兼容冻结，跨座位酒救援由规则行为版本 3 的 Core 语义提供。

## 3. 身份模式开局配置

| 模式 ID | 人数/身份 | 牌堆 | 候选数 | 武将池 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `identity:standard-8` | 8；1/2/4/1 | `standard:basic-demo` | 3 | 当前 14 名 Standard 武将 | implemented-registry + K4 setup |
| `identity:standard-5` | 5；1/1/2/1 | `standard:basic-demo` | 3 | 当前 14 名 Standard 武将 | implemented-registry + K4 setup |
| `identity:active-skills-8` | 8；1/2/4/1 | `standard:basic-demo` | 21（急救层为 22） | 当前 21 名 Standard 武将；`CreateWithRescueSkills()` 追加急救者 | implemented-registry + opt-in skill extension |
| `identity:active-skills-5` | 5；1/1/2/1 | `standard:basic-demo` | 21（急救层为 22） | 当前 21 名 Standard 武将；`CreateWithRescueSkills()` 追加急救者 | implemented-registry + opt-in skill extension |

`UseInteractiveSetup = true` 时，模式配置驱动私有单将候选、共享池移除、结果公开、确定性洗牌和逐轮发牌；候选不会进入其他玩家快照。更大武将池、同时选将和国战双将仍待后续阶段。

## 3.1 公开阵营模式

| 模式 ID | 人数/阵营 | 牌堆 | 候选数 | 武将池 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `team:standard-2v2` | 4；青队 2、赤队 2，开局公开 | `standard:basic-demo` | 3 | 当前 Standard 武将池 | implemented-registry + M1 |

该模式使用 `ContentModeKind.Team` 和 `TeamCounts`，兼容角色值仅用于旧 UI/音效投影；真正的敌我关系、胜负和公开信息使用 `TeamId`。普通玩家视图公开阵营与已公开角色状态，仍不显示其他玩家手牌、seed 或可信宿主事件细节。基础 `standard@1.11.0` Registry 不包含该包，以保持旧内容哈希和旧身份局 Checkpoint 兼容。

## 3.2 国战 Lite 模式

| 模式 ID | 人数/势力 | 牌堆 | 候选数 | 武将池 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `national:lite-4` | 4；魏 2、蜀 2，势力初始隐藏 | `standard:basic-demo` | 3 | 8 名带 `FactionId` 的国战专属武将 | implemented-registry + M2 Lite |

`standard-national-war-lite@1.1.0` 依赖 `standard@1.11.0`，但使用独立的 `national:*` 武将 ID，避免改写基础 Standard Registry 的内容指纹。国战模式必须显式提供带势力标签的 `GeneralPoolIds`；每名玩家依次选择两名同势力武将，双将可分别明置，最后存活的势力获胜。普通玩家视图保留自己的双将和私有势力，但隐藏其他角色尚未明置的武将、技能和势力；规则版本 1.0.0 / 7 的旧体力存档与 1.1.0 / 8 的双将平均体力存档分别按原内容包恢复。该模式仍是 Lite，不包含野心家、阵法、明置奖励和完整国战牌堆。

## 3.3 六人国战 M3 独立势力试验

| 模式 ID | 人数/势力 | 牌堆 | 候选数 | 武将池 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `national:ambitious-6` | 6；魏 3、蜀 2、野心家 1，势力初始隐藏 | `standard:basic-demo` | 3 | 12 名带 `FactionId` 的国战专属武将 | implemented-registry + M3 |

该模式由 `standard-national-war-ambitious@1.0.0` 追加 4 名武将：`national:wei-xiahou-dun`、`national:wei-sima-yi`、`national:ambitious-lu-bu`、`national:ambitious-diao-chan`；其余 8 名国战 Lite 武将通过依赖包复用。`FactionCounts` 固化座位分配，`SoloFactionIds = [ambitious]` 固化唯一一席的独立势力，并参与内容哈希。它只验证三方人数、双将隐私/明置、AI 视角和最后存活势力胜负，不宣称完整野心家、珠联璧合、阵法、围攻、变更副将或国战专属牌堆。

## 4. 标准武将与技能

### 当前 Standard 演示池（含可选扩展）

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
| `standard:demo-ganglie` | 刚烈者 | `demo-ganglie` | `standard:ganglie` | implemented-registry | after-damage, public-judgment, private-punishment, discard-or-damage |
| `standard:demo-guicai` | 鬼才者 | `demo-guicai` | `standard:guicai` | implemented-registry | judgment-replacement, private-choice, public-result, retain |
| `standard:demo-kujin` | 黄盖 | `demo-kujin` | `standard:kujin` | implemented-registry + extension | active-skill, hp-cost, draw-two, private-result |
| `standard:demo-zhiheng` | 制衡者 | `demo-zhiheng` | `standard:zhiheng` | implemented-registry + extension | active-skill, private-card-selection, discard-and-draw |
| `standard:demo-rende` | 仁德者 | `demo-rende` | `standard:rende` | implemented-registry + extension | active-skill, private-card-selection, private-target-selection, cross-seat-gift, recovery |
| `standard:demo-qingnang` | 青囊者 | `demo-qingnang` | `standard:qingnang` | implemented-registry + extension | active-skill, private-card-selection, private-target-selection, discard, recovery |
| `standard:demo-huichun` | 回春者 | `demo-huichun` | `standard:huichun` | implemented-registry + extension | active-skill, private-card-selection, private-target-selection, multi-target, discard, recovery |
| `standard:demo-mashu` | 马术者 | `demo-mashu` | `standard:mashu` | implemented-registry + extension | passive-skill, outgoing-distance, public-range, attack, snatch |
| `standard:demo-qicai` | 奇才者 | `demo-qicai` | `standard:qicai` | implemented-registry + extension | passive-skill, trick-distance, public-legality, snatch |
| `standard:demo-jijiu` | 急救者 | `demo-jijiu` | `standard:jijiu` | implemented-registry + rescue extension | dying-rescue, red-card, conversion, private-choice |

刘备和孙权仍使用 `standard:none` 作为明确的 Demo 占位，不代表正式技能；关羽已接入 `standard:wusheng` 的红色牌按杀使用最小切片，赵云已接入 `standard:longdan` 的出牌/响应杀闪互转最小切片，华佗已接入 `standard:feedback` 的存活伤害后私有可选触发最小切片，郭嘉已接入 `standard:yiji` 的受伤后私有摸牌和跨座位分配最小切片，荀彧已接入 `standard:jieming` 的受伤后公开目标筛选和补牌至上限最小切片，援护者已接入 `standard:yuanhu` 的明确跨座位弃牌恢复最小切片，刚烈者已接入 `standard:ganglie` 的受伤后公开判定、来源私有弃牌/受伤选择和濒死续接最小切片，鬼才者已接入 `standard:guicai` 的判定前私有换牌、公开结果和统一牌区移动最小切片；黄盖通过可选主动技能扩展包接入 `standard:kujin`，制衡者通过同一扩展包接入 `standard:zhiheng` 的私有多选弃牌/等量摸牌最小切片，仁德者通过同一扩展包接入 `standard:rende` 的私有多选交牌/其他存活目标/按数量回复最小切片；青囊者通过同一扩展包接入 `standard:qingnang` 的私有一张手牌/受伤角色选择/弃置与 1 点恢复最小切片；回春者通过同一扩展包接入 `standard:huichun` 的私有两张手牌/两至三名受伤角色选择/逐目标弃置与恢复最小切片；马术者通过同一扩展包接入 `standard:mashu` 的公开出攻距离 -1 modifier，统一影响杀与顺手牵羊的合法性；奇才者通过同一扩展包接入 `standard:qicai` 的锦囊无距离限制，当前距离型顺手牵羊复用同一 Core 合法性查询。新增技能必须有与语义匹配的 modifier、trigger、effect 或 prompt 入口，不能借用现有技能名制造假实现。

规则行为版本 5 的结算约定：当前伤害若使目标降至 0 点体力，已注册的伤害后候选仍在 `DyingFrame` 创建前按稳定游标完成；该行为由 `GameCheckpoint.CurrentRulesVersion` 标识，普通快照只投影对应 responder 可见的选择。

### 技能状态清单

| 技能 ID | 名称 | 内容方向 | 状态 | 依赖 |
| --- | --- | --- | --- | --- |
| `standard:wusheng` | 武圣 | 红色牌转化为杀 | implemented-registry | K5：`PlayedCardKind` 与物理牌实例分离 |
| `standard:longdan` | 龙胆 | 杀/闪互相转化 | implemented-registry | K5：有效/物理牌型分离与精确响应 Prompt |
| `standard:ganglie` | 刚烈 | 受伤后判定并令伤害来源选择弃两张手牌或承受 1 点伤害 | implemented-registry | K5/K7：公开判定、私有反制 Choice、牌区移动与濒死续接 |
| `standard:yiji` | 遗计 | 受伤后私有摸牌并分配一张给其他角色 | implemented-registry | K1/K2/K5：牌移动、精确牌/目标 Choice 与隐私 |
| `standard:jieming` | 节命 | 受伤后按公开手牌数补牌至目标体力上限 | implemented-registry | K1/K2/K5：公开目标筛选、私有 Choice、牌堆到目标手牌移动与隐私 |
| `standard:yuanhu` | 援护 | 其他角色受伤后弃置一张手牌并令其回复 1 点体力 | implemented-registry | K5：`DamageTriggerScope.OtherLivingPlayer`、私有弃牌 Choice、恢复子帧与脱敏事件 |
| `standard:guicai` | 鬼才 | 判定牌生效前用一张手牌替换 | implemented-registry | K7：`JudgmentFrame` 候选游标、私有替换 Choice、公开结果与 `skill.guicai.replace` 移动 |
| `standard:kujin` | 苦肉 | 出牌阶段失去 1 点体力并摸两张牌；若降至 0，救援结算后再摸牌 | implemented-registry + extension | K5：`IActiveSkill`、`UseSkillCommand`、`ActiveSkillFrame`、类型化体力/摸牌事件；体力大于 0，濒死时保留主动技能帧并复用私有 `RescueDying` |
| `standard:rende` | 仁德 | 主动交牌并按数量回复 | implemented-registry + extension | K2/K5：私有选牌/其他存活目标白名单、Processing 跨手牌移动、按数量恢复、回合一次限制 |
| `standard:zhiheng` | 制衡 | 出牌阶段私有选择至少一张手牌弃置后摸等量牌 | implemented-registry + extension | K2/K5：主动多选、`Processing` 牌区、等量摸牌和私有 Prompt |
| `standard:qingnang` | 青囊 | 出牌阶段每回合弃置一张手牌，令一名受伤角色回复 1 点体力 | implemented-registry + extension | K5：私有手牌/受伤存活目标选择、`Processing` 弃牌、`RecoveryAppliedEvent` 和回合一次限制 |
| `standard:huichun` | 回春 | 出牌阶段每回合弃置两张手牌，令至少两名受伤角色各回复 1 点体力 | implemented-registry + extension | K5：私有两牌/多目标选择、逐目标 `RecoveryFrame`、`Processing` 弃牌、`RecoveryAppliedEvent` 和回合一次限制 |
| `standard:mashu` | 马术 | 计算与其他角色的距离 -1 | implemented-registry + extension | K6：`IPassiveSkill.ModifyOutgoingDistance`，由 Core 统一影响公开距离型合法性 |
| `standard:qicai` | 奇才 | 使用锦囊牌无距离限制 | implemented-registry + extension | K6：`IPassiveSkill.IgnoresTrickDistance`，由 Core 统一影响距离型锦囊合法性 |
| `standard:jijiu` | 急救 | 濒死窗口可将红色非桃牌当作桃使用 | implemented-registry + rescue extension | K5：`IPassiveSkill.CanUseAsDyingRescue`，有效牌型为 Peach，物理牌型保留在事件和移动账本 |

## 5. 锦囊清单

| 内容 ID | 名称 | AI 标签 | 状态 | 需要验证的核心能力 |
| --- | --- | --- | --- | --- |
| `standard:draw_two` | 无中生有 | draw, card-advantage | implemented-registry | K5：无目标 `CardUseFrame` 与摸牌效果 |
| `standard:dismantlement` | 过河拆桥 | target-one, hidden-hand-discard, public-equipment-target, public-judgment-target | implemented-registry | K5/K7：手牌不透明牌位选择弃置，公开装备或判定区牌由 `TargetCardId` 精确选择并经 `Processing` 移动；私有牌位候选由 `TargetCardSelectionFrame` 提供 |
| `standard:snatch` | 顺手牵羊 | target-one, hidden-hand-take, public-equipment-target, public-judgment-target, distance-one | implemented-registry | K5/K7：战斗距离一合法性、手牌不透明牌位选择取得，或精确取得公开装备/判定区牌；私有牌位候选由 `TargetCardSelectionFrame` 提供 |
| `standard:duel` | 决斗 | attack, repeated-response | implemented-registry | K5：交替杀响应链已开放；群体/多伤害仍待后续 |
| `standard:nullification` | 无懈可击 | counter, response-chain | implemented-registry | K7：`NullificationWindowFrame`、固定座次有限多层响应、私有牌面选择与类型化事件 |
| `standard:peach_garden` | 桃园结义 | recovery, all-alive, repeated-effect | implemented-registry | K5：`TargetIndex` 逐目标恢复、`RecoveryFrame` 与 `RecoveryAppliedEvent` |
| `standard:five_grains` | 五谷丰登 | public-reveal, draft, all-alive, private-choice | implemented-registry | K5：公开展示牌、当前 picker 私有 `SelectHarvestCard`、逐人选牌事件 |
| `standard:barbarian_assault` | 南蛮入侵 | attack, all-opponents, repeated-response | implemented-registry | K5：`TargetIndex` 逐目标杀响应、单次伤害与濒死续接 |
| `standard:arrow_barrage` | 万箭齐发 | attack, all-opponents, repeated-response | implemented-registry | K5：`TargetIndex` 逐目标闪响应、单次伤害与濒死续接 |
| `standard:iron_chain` | 铁索连环 | target-two, state-mark, elemental-propagation | implemented-registry | K5/K7：精确一/二目标、公开状态标记、火/雷同额传导与有限无懈窗口 |
| `standard:lightning` | 闪电 | delayed-judgment, self-target, public-judgment, thunder-damage, transfer | implemented-registry | K7：自用判定区；黑桃 2 至 9 命中 3 点雷电伤害，其他判定牌转移到下一名存活角色 |

## 6. 装备清单

| 内容 ID | 名称 | 类型 | AI 标签 | 状态 | 需要验证的核心能力 |
| --- | --- | --- | --- | --- | --- |
| `standard:crossbow` | 诸葛连弩 | Weapon | attack, unlimited-slash | implemented-registry | K6：武器槽、攻击范围 +1、攻击次数 modifier |
| `standard:qinggang_sword` | 青釭剑 | Weapon | attack, ignore-armor | implemented-registry | K6：武器槽、直接杀无视防具；`CardUseFrame.IgnoresArmor` 类型化记录 |
| `standard:bagua` | 八卦阵 | Armor | response, judgment | implemented-registry | K6：防具槽和公开生命周期；K7：`JudgmentFrame`、判定区移动和红色判定视为闪 |
| `standard:renwang_shield` | 仁王盾 | Armor | defense, block-black-slash | implemented-registry | K6/K7：防具槽；黑色杀不进入对装备者的合法目标列表，青釭剑可绕过该阻挡 |
| `standard:offensive_horse` | 赤兔 | OffensiveHorse | range, attack | implemented-registry | K6：进攻坐骑槽、战斗距离 -1 |
| `standard:defensive_horse` | 绝影 | DefensiveHorse | defense, distance | implemented-registry | K6：防御坐骑槽、战斗距离 +1 |
| `standard:jade_seal` | 玉玺 | Treasure | draw, modifier | implemented-registry | K6：宝物槽、摸牌 +1 |

装备内容必须进入 `Equipment` 区域，拥有替换、失效、死亡清理和持久 modifier 语义；当前 K6 基础切片已开放五类槽位、七种装备牌（含仁王盾）、换装、战斗距离/攻击范围查询、玉玺摸牌和青釭剑无视防具和仁王盾阻挡黑色杀 modifier，以及阵亡清理；K7 已为八卦阵接入直接杀响应中的公开判定、判定区移动和红色判定视为闪，闪电接入自用延时判定、命中伤害和失败转移；复杂判定区目标选择及复杂失效/卸载效果仍待后续入口，不把装备伪装成普通手牌或一次性锦囊。

## 7. 内容级 AI 标签约定

AI 标签是内容包输入，不是隐藏身份推断：

- `attack` / `pressure` / `finish`：目标价值和击杀压力；
- `response` / `survival`：响应伤害和保留到后续回合；
- `recovery` / `dying-save`：回复体力或救援濒死；
- `draw` / `card-advantage` / `retain`：手牌收益和弃牌保留；
- `target-one` / `target-two` / `all-opponents`：目标数量约束；
- `distance` / `range` / `state-mark`：依赖通用查询或持久状态。

标签不能替代核心合法性校验，也不能让 AI 读取暗身份、暗牌、完整牌堆顺序或其他玩家的私有候选。
