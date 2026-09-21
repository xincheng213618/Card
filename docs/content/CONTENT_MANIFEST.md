# Standard 内容清单（C0 草案 / M3 试验）

更新时间：2026-09-21

这是内容流水线的设计清单和正式包的对照表。稳定内容 ID 使用 `package:name` 形式；`implemented-registry` 表示已经进入 `CardGame.Content.Standard`，`implemented-legacy` 表示仍由 Core 的 `CardKind`/`SkillKind` 兼容投影运行，`planned` 表示内容定义已规划但等待后续核心 API。K1 的牌区生命周期、K2 的 Prompt/Choice、reason、可见性和强制场景见 [`CARD_MOVEMENT_CONTRACT.md`](./CARD_MOVEMENT_CONTRACT.md)；`definitionId`/`instanceId` 的 Registry 关系已在 K3 冻结。所有文案、AI 标签和规则描述均为本项目自有文字，不包含卡面、插画、音频或其他素材。

## 0. 内容包与扩展入口

| 包 ID | 依赖 | 内容 | 状态 |
| --- | --- | --- | --- |
| `standard@1.11.0` | 无 | 基础牌、装备、技能、武将、演示牌堆和标准身份模式 | implemented-registry |
| `standard-active-skills@1.0.0` | `standard@1.11.0` | `standard:kujin`、`standard:zhiheng`、`standard:rende`、`standard:qingnang`、`standard:huichun`、`standard:mashu`、`standard:qicai`、七个技能演示武将、`identity:active-skills-8/5` | implemented-registry；可选扩展 |
| `standard-rescue-skills@1.0.0` | `standard-active-skills@1.0.0` | `standard:jijiu`、`standard:demo-jijiu`；扩展模式中的急救红牌濒死救援 | implemented-registry；可选扩展 |
| `standard-classic-generals@1.80.0` | `standard-rescue-skills@1.0.0` | 当前正式经典身份层；1.80.0／rules v102 注册谋吕蒙，以横野完成整局成长与技能重置消费者、以英博完成所有角色同名伤害牌轮账本及首次不可响应／结算后可交牌、重复牌火焰增伤分支；1.79.0 及更早内容定义、规则版本、存档签名与历史牌堆继续保留 | implemented-registry；可选扩展 |
| `standard-classic-generals@1.79.0` | `standard-rescue-skills@1.0.0` | 正式经典身份层；1.79.0 为鬼才、刚烈、急救、遗计、英姿、奸雄、节命建立独立 `classic:` ID 及 Trigger／State 元数据；1.78.0 配合 rules v100 完成当前仁德的同阶段重复给牌、累计第二张时自我回复一次与阶段账本；1.77.0 为仁德、制衡、青囊、苦肉建立带 `ActionForms.Active` 的独立 `classic:` ID；1.76.0 为激将、乱击、天义、双雄组合独立标签、状态／触发形态和主动入口轴；1.75.0 为反间、强袭、离间、结姻、驱虎增加独立主动入口；1.74.0 将 11 项持续牌转化技能标为 `State`；1.73.0 将 27 项纯可选技能标为 `Trigger`；1.72.0 为空城／马术／奇才建立独立 `classic:` ID；1.71.0 为另外 13 项锁定技补齐元数据；1.70.0 注册严颜／拒战；1.0.0–1.78.0 的内容定义、规则版本、存档签名与历史牌堆继续保留 | implemented-registry；可选扩展 |
| `standard-team-modes@1.0.0` | `standard@1.11.0` | `team:standard-2v2`；公开青/赤阵营和队伍胜负适配 | implemented-registry；可选扩展 |
| `standard-national-war-lite@1.1.0` | `standard@1.11.0` | `national:lite-4`；四人魏蜀双将国战 Lite | implemented-registry；可选扩展 |
| `standard-national-war-ambitious@1.0.0` | `standard-national-war-lite@1.0.0` | `national:ambitious-6`；魏 3、蜀 2、野心家 1 的六人独立势力试验 | implemented-registry；M3 可选扩展 |
| `standard-national-zhang-jiao@1.0.0` | `standard-national-war-lite@1.1.0`、`standard-active-skills@1.0.0`、`standard-rescue-skills@1.0.0` | `national:zhang-jiao`、`national:hua-tuo` 与 `national:zhang-jiao-4`；魏 1、蜀 2、群 1 的四人正式技能试验 | implemented-registry；rules v89 可选扩展 |

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
| `standard:indulgence` | 乐不思蜀 | Trick | implemented-registry | delayed-judgment, target-one, public-judgment, skip-play | K7 + rules v11：使用牌进入目标公开判定区；目标下回合摸牌前判定，非红桃跳过出牌阶段、红桃正常出牌；复用 `JudgmentFrame`、无懈和鬼才窗口；v1–v10 保留历史语义 |
| `standard:supply_shortage` | 兵粮寸断 | Trick | implemented-registry | delayed-judgment, target-one, public-judgment, skip-draw | K7 + rules v11：使用牌进入有手牌目标的公开判定区；目标下回合摸牌前判定，非梅花跳过摸牌阶段、梅花正常摸牌；复用 `JudgmentFrame`、无懈和鬼才窗口；v1–v10 保留历史语义 |
| `standard:lightning` | 闪电 | Trick | implemented-registry | delayed-judgment, self-target, public-judgment, thunder-damage, transfer | K7：只能对自己使用；下个回合黑桃 2 至 9 命中并造成 3 点雷电伤害，否则转移到下一名存活角色；复用 `JudgmentFrame`、鬼才窗口和延时牌收尾 |
| `standard:nullification` | 无懈可击 | Trick | implemented-registry | counter, response-chain | K7：锦囊效果前的固定座次私有响应；支持有限多层互相抵消 |
| `standard:iron_chain` | 铁索连环 | Trick | implemented-registry | target-two, state-mark, elemental-propagation | K5/K7：精确一/二目标、公开连环标记、火/雷伤害同额传导与有限无懈窗口 |
| `classic:borrowed-sword` | 借刀杀人 | Trick | implemented-registry + classic 1.23 | ordered-target-pair, weapon-owner, nested-slash, weapon-transfer | rules v42：精确选择持武器者及其攻击范围内另一角色；持武器者私有选择实体杀、武圣、激将或交出武器；真实子杀完整复用响应/伤害/濒死，失败分支精确转移武器 |
| `standard:alcohol` | 酒 | Basic | implemented-registry | slash-boost, one-shot, dying-self-rescue | K5：无目标 `CardUseFrame`、公开酒效、直接杀伤害金额与回合结束失效；规则 v12 起只允许濒死者用自己的酒自救 1 点体力，v3–v11 保留跨座位兼容回放 |

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

基础 Standard 身份牌堆沿用同一个 90 张 Recipe 形状；经典包从 1.23.0 起依次使用 92/93/94/96/97/98 张 `classic:standard-deck`，1.28.0 在青龙偃月刀之后加入一张寒冰剑。五类装备槽、十二种装备牌、换装、武器范围、防具时机、贯石斧、丈八蛇矛、雌雄双股剑、青龙偃月刀和寒冰剑均已接入；寒冰剑以不透明手牌位/精确装备 Choice 防止杀伤害并顺序弃置至多两张目标牌。后续仍可追加更多锦囊和装备效果，不把这些演示配方误称为完整商业卡表。标准包的 ID、标签和描述保持 v1.11.0 兼容冻结。

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

## 3.4 四人国战张角试验

| 模式 ID | 人数/势力 | 牌堆 | 候选数 | 武将池 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `national:zhang-jiao-4` | 4；魏 1、蜀 2、群 1，势力初始隐藏 | `standard:basic-demo` | 2 | 魏 2、蜀 4、群 2，共 8 名 | implemented-registry + N20 |

群势力两名候选固定为 1.5 阴阳鱼的 `national:zhang-jiao`（雷击／鬼道）与 `national:hua-tuo`（急救／青囊）。张角的 schema 8 程序最低 rules v89，只表达有效闪后的另一角色判定、黑桃 2 雷伤和黑色手／装备牌改判后取得原判定牌；不含身份黄天、身份梅花分支或界张角差异。规则来源、语义指纹、历史华佗来源和素材复用边界保存在 [`national-zhang-jiao-2026-09-20.json`](sources/national-zhang-jiao-2026-09-20.json)。该模式仍复用 Lite 状态机和演示牌堆，不宣称完整国战规则。

## 4. 标准武将与技能

### 当前 Standard 演示池（含可选扩展）

| 内容 ID | 武将 | 兼容 ID | 技能 ID | 状态 | AI 标签 |
| --- | --- | --- | --- | --- | --- |
| `standard:cao-cao` | 曹操 | `cao-cao` | `standard:jianxiong` | implemented-registry | damage-card, retain |
| `classic:cao-cao` | 曹操 | `cao-cao` | `standard:jianxiong` + `classic:hujia` | implemented-registry + classic 1.4 | damage-card, retain, lord, cross-seat-response |
| `standard:zhang-fei` | 张飞 | `zhang-fei` | `standard:paoxiao` | implemented-registry | attack, unlimited-slash |
| `standard:zhou-yu` | 周瑜 | `zhou-yu` | `standard:yingzi` | implemented-registry | draw, card-advantage |
| `classic:zhou-yu` | 周瑜 | `zhou-yu` | `standard:yingzi` + `classic:fanjian` | implemented-registry + classic 1.2 | draw, active-skill, private-suit-choice, random-gift, damage |
| `standard:zhuge-liang` | 诸葛亮 | `zhuge-liang` | `standard:kongcheng` | implemented-registry | hand-zero, target-lock |
| `classic:zhuge-liang` | 诸葛亮 | `zhuge-liang` | `classic:guanxing` + `standard:kongcheng` | implemented-registry + classic 1.3 | private-deck-order, judgment-control, draw-control, hand-zero, target-lock |
| `standard:liu-bei` | 刘备 | `liu-bei` | `standard:none` | implemented-registry | placeholder |
| `classic:liu-bei` | 刘备 | `liu-bei` | `classic:rende` + `classic:jijiang` | implemented-registry + classic 1.77 | active-skill, gift, recovery, lord, cross-seat-slash |
| `classic:sun-quan` | 孙权 | `sun-quan` | `classic:zhiheng` + `classic:jiuyuan` | implemented-registry + classic 1.77 | active-skill, discard-draw, lord, dying-recovery |
| `classic:huang-gai` | 黄盖 | `huang-gai` | `classic:kujin` | implemented-registry + classic 1.77 | active-skill, hp-cost, repeatable, dying-continuation, draw-two |
| `classic:gan-ning` | 甘宁 | `gan-ning` | `classic:qixi` | implemented-registry + classic 1.8 | black-card, conversion, hand-or-equipment, target-card, nullification |
| `classic:lu-meng` | 吕蒙 | `lu-meng` | `classic:keji` | implemented-registry + classic 1.9 | phase-skip, optional, slash-history, retain-hand |
| `classic:zhang-liao` | 张辽 | `zhang-liao` | `classic:tuxi` | implemented-registry + classic 1.10 | draw-replacement, one-or-two-targets, hidden-hand-gain |
| `classic:xu-chu` | 许褚 | `xu-chu` | `classic:luoyi` | implemented-registry + classic 1.11 | draw-reduction, turn-damage-bonus, duel-attribution |
| `classic:dian-wei` | 典韦 | `dian-wei` | `classic:qiangxi` | implemented-registry + classic 1.12 | active-skill, hp-or-weapon-cost, attack-range, cardless-damage, dying-continuation |
| `classic:xu-huang` | 徐晃 | `xu-huang` | `classic:duanliang` | implemented-registry + classic 1.13 | black-basic-or-equipment, supply-shortage-conversion, distance-two, persistent-effective-kind |
| `classic:zhen-ji` | 甄姬 | `zhen-ji` | `classic:luoshen` + `classic:qingguo` | implemented-registry + classic 1.14 | repeated-judgment, black-card-claim, dodge-conversion, private-choice |
| `classic:huang-yueying` | 黄月英 | `huang_yueying` | `classic:jizhi` + `standard:qicai` | implemented-registry + classic 1.15 | ordinary-trick-trigger, private-choice, draw-one, trick-distance |
| `classic:ma-chao` | 马超 | `ma_chao` | `classic:tieqi` + `standard:mashu` | implemented-registry + classic 1.16 | slash-target-trigger, private-choice, judgment, dodge-prohibition, outgoing-distance |
| `classic:huang-zhong` | 黄忠 | `huang_zhong` | `classic:liegong` | implemented-registry + classic 1.17 | play-phase-slash, public-hand-threshold, attack-range-threshold, private-choice, dodge-prohibition |
| `classic:wei-yan` | 魏延 | `wei_yan` | `classic:kuanggu` | implemented-registry + classic 1.18 | damage-source-trigger, public-distance-one, locked-recovery, damage-amount |
| `classic:lu-bu` | 吕布 | `lu_bu` | `classic:wushuang` | implemented-registry + classic 1.19 | locked-response-count, slash-double-dodge, duel-double-slash, sequential-prompts |
| `classic:zhang-fei` | 张飞 | `zhang_fei` | `classic:paoxiao` | implemented-registry + classic 1.20 | locked-slash-limit, same-phase-multiple-slash, shared-ai-policy |
| `classic:zhao-yun` | 赵云 | `zhao_yun` | `classic:longdan` | implemented-registry + classic 1.21 | dodge-to-slash, slash-to-dodge, physical-effective-kind-separation, private-response |
| `classic:guan-yu` | 关羽 | `guan_yu` | `classic:wusheng` | implemented-registry + classic 1.22 | hand-or-equipment red-card, active-or-response conversion, source-zone-preservation |
| `classic:yan-yan` | 严颜 | `yan_yan` | `classic:juzhan` | implemented-registry + classic 1.70 | conversion-state, slash-target-trigger, opaque-target-card, per-target-turn-prohibition |
| `mou:lu-meng` | 谋吕蒙 | `mou-lu-meng`（官网独立肖像） | `mou:hengye` + `mou:yingbo` | implemented-registry + classic 1.80 / rules v102 | game-growth, skill-reset, round-card-name-ledger, unrespondable-first-use, optional-card-gift, fire-damage-bonus |
| `standard:guan-yu` | 关羽 | `guan_yu` | `standard:wusheng` | implemented-registry + classic 1.21 compatibility | hand red-card, conversion, attack |
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

刘备和孙权仍使用 `standard:none` 作为明确的 Demo 占位，不代表正式技能；关羽已接入 `standard:wusheng` 的红色牌按杀使用最小切片，赵云已接入 `standard:longdan` 的出牌/响应杀闪互转最小切片，华佗已接入 `standard:feedback` 的存活伤害后私有可选触发最小切片，郭嘉已接入 `standard:yiji` 的受伤后私有摸牌和跨座位分配最小切片，荀彧已接入 `standard:jieming` 的受伤后公开目标筛选和补牌至上限最小切片，援护者已接入 `standard:yuanhu` 的明确跨座位弃牌恢复最小切片，刚烈者已接入 `standard:ganglie` 的受伤后公开判定、来源私有弃牌/受伤选择和濒死续接最小切片，鬼才者已接入 `standard:guicai` 的判定前私有换牌、公开结果和统一牌区移动最小切片；黄盖先通过可选主动技能扩展包验证 `standard:kujin`，当前经典包 1.7.0 已将同一技能接入正式 `classic:huang-gai`；经典包 1.8.0 进一步加入甘宁/奇袭，并按实体来源区与有效过河拆桥牌型分离复用目标和无懈链；经典包 1.9.0 再加入吕蒙/克己，以回合内杀标记和弃牌阶段私有 Choice 接入阶段跳过；经典包 1.10.0 再加入张辽/突袭，以摸牌阶段公开目标组合和随机暗手牌转移替代普通摸牌；经典包 1.11.0 再加入许褚/裸衣，以少摸一张的回合标记和原始用牌者/实际伤害来源分离接入杀与决斗加伤；经典包 1.12.0 再加入典韦/强袭，以体力或武器二选一成本、攻击范围目标和无实体牌来源伤害复用统一伤害/濒死链；制衡者通过主动技能包接入 `standard:zhiheng` 的私有多选弃牌/等量摸牌最小切片，仁德者通过同一扩展包接入 `standard:rende` 的私有多选交牌/其他存活目标/按数量回复最小切片；青囊者通过同一扩展包接入 `standard:qingnang` 的私有一张手牌/受伤角色选择/弃置与 1 点恢复最小切片；回春者通过同一扩展包接入 `standard:huichun` 的私有两张手牌/两至三名受伤目标选择/逐目标恢复最小切片；马术者通过同一扩展包接入 `standard:mashu` 的公开出攻距离 -1 modifier，统一影响杀与顺手牵羊的合法性；奇才者通过同一扩展包接入 `standard:qicai` 的锦囊无距离限制，当前距离型顺手牵羊复用同一 Core 合法性查询。新增技能必须有与语义匹配的 modifier、trigger、effect 或 prompt 入口，不能借用现有技能名制造假实现。

经典包 1.13.0 再加入徐晃/断粮，以黑色基本牌或装备牌的实体来源、有效兵粮寸断牌型、距离 2 修正和判定区持久有效身份复用延时锦囊链；1.14.0 再加入甄姬/洛神+倾国，以可重复私有 Choice、判定牌取得和黑色手牌转闪复用判定与响应链；1.15.0 再加入黄月英/集智+奇才，以普通锦囊声明后的私有 Choice、精确摸牌和父结算游标续接复用无懈与效果链；1.16.0 再加入马超/铁骑+马术，以杀指定目标后的私有 Choice、共享判定/鬼才替换和当前杀闪响应禁止复用攻击链；1.17.0 再加入黄忠/烈弓，以公开手牌数、当前体力、攻击范围条件和私有 Choice 复用同一杀闪响应禁止链；1.18.0 再加入魏延/旧版狂骨，以实际伤害来源、公开战斗距离、伤害量和统一恢复帧复用伤害后触发链；1.19.0 再加入吕布/无双，以连续响应计数复用杀、决斗、八卦阵、护驾、激将和转换牌链；1.20.0 用经典张飞/咆哮 ID 复用出杀次数查询；1.21.0 用经典赵云/龙胆 ID 复用杀闪双向转化链；1.22.0 用经典关羽/武圣 ID 并以 rules v40 补齐装备区红牌主动/响应转换；1.23.0 注册借刀杀人与版本化 92 张牌堆，并以 rules v42 接入有序目标、持武器者私有 Choice、真实子杀和武器转移；1.24.0 再注册贯石斧与 93 张牌堆，以 rules v43 接入最后一张闪后的精确两牌费用和原杀伤害续接；1.25.0 再注册丈八蛇矛与 94 张牌堆，以 rules v44 接入精确两手牌的主动杀和组合杀响应；1.26.0 再注册雌雄双股剑与 96 张牌堆，以 rules v45 接入版本化性别和异性杀后的两阶段资源选择。1.25.0 及更早包继续保留对应历史牌堆、武将池、默认性别投影和内容指纹。

经典包 1.27.0 再注册青龙偃月刀与 97 张牌堆，以 rules v46 接入最后一张闪后的私有精确同目标新杀；刘备可在该窗口发动激将，由蜀势力提供者支付实体杀或丈八双牌。新杀不计普通杀次数并重新经过完整攻击链。1.26.0 及更早包继续保留各自历史牌堆、武将池、性别投影和内容指纹。

经典包 1.28.0 再注册寒冰剑与 98 张牌堆，以 rules v47 接入杀伤害前的私有顺序弃牌窗口；手牌实体对来源仍按牌位脱敏，装备公开精确，发动后防止全部伤害并在仍有牌时强制第二次选择。1.27.0 及更早包继续保留各自历史牌堆、武将池和内容指纹。

规则行为版本 5 的结算约定：当前伤害若使目标降至 0 点体力，已注册的伤害后候选仍在 `DyingFrame` 创建前按稳定游标完成；该行为由 `GameCheckpoint.CurrentRulesVersion` 标识，普通快照只投影对应 responder 可见的选择。

### 技能状态清单

| 技能 ID | 名称 | 内容方向 | 状态 | 依赖 |
| --- | --- | --- | --- | --- |
| `standard:wusheng` / `classic:wusheng` | 武圣 | 红色牌转化为杀；rules v40 经典身份含自己的装备区 | implemented-registry + classic extension | K5：`PlayedCardKind` 与物理牌实例分离；Hand/Equipment 实际来源区；主动/响应/激将 |
| `standard:longdan` / `classic:longdan` | 龙胆 | 杀/闪互相转化 | implemented-registry | K5：有效/物理牌型分离与精确响应 Prompt；经典包 1.21 正式身份 |
| `standard:ganglie` | 刚烈 | 受伤后判定并令伤害来源选择弃两张手牌或承受 1 点伤害 | implemented-registry | K5/K7：公开判定、私有反制 Choice、牌区移动与濒死续接 |
| `standard:yiji` | 遗计 | 受伤后私有摸牌并分配一张给其他角色 | implemented-registry | K1/K2/K5：牌移动、精确牌/目标 Choice 与隐私 |
| `standard:jieming` | 节命 | 受伤后按公开手牌数补牌至目标体力上限 | implemented-registry | K1/K2/K5：公开目标筛选、私有 Choice、牌堆到目标手牌移动与隐私 |
| `standard:yuanhu` | 援护 | 其他角色受伤后弃置一张手牌并令其回复 1 点体力 | implemented-registry | K5：`DamageTriggerScope.OtherLivingPlayer`、私有弃牌 Choice、恢复子帧与脱敏事件 |
| `standard:guicai` | 鬼才 | 判定牌生效前用一张手牌替换 | implemented-registry | K7：`JudgmentFrame` 候选游标、私有替换 Choice、公开结果与 `skill.guicai.replace` 移动 |
| `classic:tiandu` | 天妒 | 规则 v22 经典身份中，自己的判定牌生效后可获得此牌 | implemented-registry + classic extension | K2/K7：结果后私有 Choice、`Judgment → Hand` 移动、`JudgmentCardClaimedEvent` 与父判定续接 |
| `classic:guanxing` | 观星 | 规则 v24 经典身份中，准备阶段可私有观看并排列至多五张牌堆顶牌 | implemented-registry + classic extension | K1/K2/K7：私有多步 Choice、牌堆顶冻结、同区顶/底重排、数量事件、延时判定与摸牌续接 |
| `classic:hujia` | 护驾 | 规则 v25 经典身份中，主公曹操需要闪时可按行动顺序请求其他魏势力角色代为响应 | implemented-registry + classic extension | K1/K2/K7：私有跨座位响应、实体牌提供者与有效响应者分离、提供者八卦判定、候选游标与父响应窗续接 |
| `classic:jijiang` | 激将 | 规则 v26 经典身份中，主公刘备可在出牌阶段或杀响应窗按行动顺序请求其他蜀势力角色提供杀 | implemented-registry + classic extension | K1/K2/K5：主动/响应双入口、私有跨座位杀 Choice、实体提供者与有效 source/responder 分离、失败重试与父结算续接 |
| `classic:jiuyuan` | 救援 | 规则 v27 经典身份中，其他吴势力角色对濒死主公孙权使用桃时回复量+1 | implemented-registry + classic extension | K1/K2/K7：复用私有濒死 Choice、实体桃牌区与恢复帧，专用事件记录提供者和 2 点回复；自救、非吴、酒与旧规则不加成 |
| `classic:qixi` | 奇袭 | 规则 v28 经典身份中，将一张黑色手牌或装备区牌当过河拆桥使用 | implemented-registry + classic extension | K2/K5/K6/K7：物理牌与有效 Dismantlement 分离、Hand/Equipment 来源、目标牌精确或不透明选择、无懈暂停与回放；v27 不发布动作 |
| `classic:keji` | 克己 | 规则 v29 经典身份中，若本回合 Play 阶段未使用或打出杀，可选择跳过弃牌阶段 | implemented-registry + classic extension | K2/K5/K7：有效杀历史标记、Discard 阶段私有 Choice、阶段结束事件、超上限手牌保留、暂停/完成回放；v28 不发布选择 |
| `classic:tuxi` | 突袭 | 规则 v30 经典身份中，摸牌阶段可改为获得至多两名其他角色各一张手牌 | implemented-registry + classic extension | K1/K2/K5/K7：有手牌公开目标组合、随机暗手牌 `Hand → Processing → Hand`、脱敏结果事件、暂停/完成回放；v29 不发布选择 |
| `classic:luoyi` | 裸衣 | 规则 v31 经典身份中，摸牌阶段可少摸一张，使本回合由自己使用的杀或决斗伤害 +1 | implemented-registry + classic extension | K2/K5/K7：私有发动/跳过、回合标记、原始用牌者/实际伤害来源分离、类型化伤害修正事件、暂停/完成回放；v30 不发布选择并保留旧决斗归因 |
| `classic:qiangxi` | 强袭 | 规则 v32 经典身份中，每个出牌阶段限一次，失去 1 点体力或弃置一张手牌/装备区武器牌，对攻击范围内一名其他角色造成 1 点伤害 | implemented-registry + classic extension | K1/K2/K5/K6/K7：可选武器成本、Hand/Equipment 统一弃置、攻击范围目标、无实体牌来源伤害、伤害后触发、自损濒死续接与旧规则兼容 |
| `classic:duanliang` | 断粮 | 规则 v33 经典身份中，将一张黑色基本牌或黑色装备牌当兵粮寸断使用，并可对距离 2 的角色使用兵粮寸断 | implemented-registry + classic extension | K1/K2/K6/K7：Hand/Equipment 实体来源、有效 SupplyShortage 与物理牌分离、判定区持久有效牌型、距离上限修正、同名去重、无懈/判定/回放与 v32 兼容 |
| `classic:luoshen` | 洛神 | 规则 v34 经典身份中，准备阶段可反复判定；获得生效后的黑色判定牌，红色结果结束 | implemented-registry + classic extension | K1/K2/K7：准备阶段私有发动/停止 Choice、公开判定、鬼才替换、`Judgment → Hand` 取得、重复游标、暂停/完成回放与 v33 兼容 |
| `classic:qingguo` | 倾国 | 规则 v34 经典身份中，将一张黑色手牌当闪使用或打出 | implemented-registry + classic extension | K1/K2/K5/K7：有效 Dodge 与物理牌分离、杀/万箭齐发响应、精确私有候选、移动账本、暂停/完成回放与 v33 兼容 |
| `classic:jizhi` | 集智 | 规则 v35 经典身份中，使用普通锦囊牌后可摸一张牌 | implemented-registry + classic extension | K1/K2/K5/K7：声明后私有发动/跳过 Choice、`DrawPile → Hand`、普通锦囊/无懈触发、延时锦囊排除、父 `CardUseFrame`/`NullificationWindowFrame` 游标续接及 v34 兼容 |
| `classic:tieqi` | 铁骑 | 规则 v36 经典身份中，使用杀指定目标后可判定；红色结果禁止该目标使用闪响应此杀 | implemented-registry + classic extension | K1/K2/K5/K7：目标声明后私有发动/跳过 Choice、共享 `JudgmentFrame`/鬼才替换、当前 `AttackResolution` 闪响应禁止、实体闪/倾国/八卦阵/护驾统一拦截及 v35 兼容 |
| `classic:liegong` | 烈弓 | 规则 v37 经典身份中，出牌阶段用杀指定满足手牌数条件的目标后可令其不能使用闪 | implemented-registry + classic extension | K1/K2/K5/K6/K7：公开手牌数与当前体力/攻击范围双条件、私有发动/跳过 Choice、当前 `AttackResolution` 闪响应禁止、实体闪/倾国/八卦阵/护驾统一拦截及 v36 兼容 |
| `classic:juzhan` | 拒战 | rules v99 中，阳面成为其他角色杀的目标后双方各摸一张并禁止其本回合再以牌指定自己；阴面使用杀指定目标后获得其中一名目标的一张牌并禁止自己本回合再以牌指定该目标，实际发动后切换 | implemented-registry + classic 1.70 | K1/K2/K5/K7：结构化转换面、目标最终确定窗口、暗手牌位／公开装备判定牌、`source-target` 回合作用域账本、合法动作过滤、Checkpoint/Replay 与 v98 边界 |
| `classic:kuanggu` | 狂骨 | 规则 v38 经典身份中，实际伤害来源魏延对距离 1 以内角色造成伤害后按伤害点数回复体力 | implemented-registry + classic extension | K1/K5/K6/K7：`DamageTriggerScope.DamageSource`、公开结算距离、锁定高优先级候选、统一恢复帧、伤害/恢复事件顺序、满体力无空恢复及 v37 兼容 |
| `classic:wushuang` | 无双 | 规则 v39 经典身份中，吕布使用杀时目标需依次使用两张闪；与吕布决斗的另一方每轮需依次打出两张杀 | implemented-registry + classic extension | K1/K2/K5/K7：`ModifyRequiredResponseCount`、独立 `ResponseWindowFrame`、实体/转换响应、八卦阵/护驾/激将续接、公开进度事件、完成回放及 v38 兼容 |
| `standard:kujin` | 苦肉 | 出牌阶段失去 1 点体力并摸两张牌；若降至 0，救援结算后再摸牌 | implemented-registry + extension | K5：`IActiveSkill`、`UseSkillCommand`、`ActiveSkillFrame`、类型化体力/摸牌事件；体力大于 0，濒死时保留主动技能帧并复用私有 `RescueDying` |
| `standard:rende` | 仁德 | 主动交牌并按数量回复 | implemented-registry + extension | K2/K5：私有选牌/其他存活目标白名单、Processing 跨手牌移动、按数量恢复、回合一次限制 |
| `standard:zhiheng` | 制衡 | 规则 v17 经典身份每阶段限一次，可混选自己的手牌与公开装备后弃置并摸等量牌；旧规则/演示模式仅手牌 | implemented-registry + extension | K2/K5/K6：主动多选、混合来源 `Processing` 牌区、等量摸牌和私有 Prompt |
| `standard:qingnang` | 青囊 | 出牌阶段每回合弃置一张手牌，令一名受伤角色回复 1 点体力 | implemented-registry + extension | K5：私有手牌/受伤存活目标选择、`Processing` 弃牌、`RecoveryAppliedEvent` 和回合一次限制 |
| `classic:kujin` / `classic:rende` / `classic:zhiheng` / `classic:qingnang` | 经典苦肉／仁德／制衡／青囊 | 经典包 1.77.0 建立独立主动技能身份；1.78.0 起使用正式仁德规则 | implemented-registry + classic extension | 四项复用对应 `SkillKind` 并显式标记 `ActionForms.Active`；rules v100 + 包 1.78.0 及以后版本的仁德可在同阶段重复向不同目标交牌，累计第二张时只让刘备回复一次；rules v99、包 1.77.0 与演示包继续旧行为 |
| `classic:guicai` / `classic:ganglie` / `classic:jijiu` / `classic:yiji` / `classic:yingzi` / `classic:jianxiong` / `classic:jieming` | 经典鬼才／刚烈／急救／遗计／英姿／奸雄／节命 | 经典包 1.79.0 建立独立共享技能身份 | implemented-registry + classic extension | 鬼才、刚烈、遗计、英姿、奸雄、节命为 `Trigger`，急救为 `State`；七项复用对应 `SkillKind`，1.78.0 与稳定标准／救援包继续原 `standard:` ID，rules v100 不变 |
| `mou:hengye` / `mou:yingbo` | 横野／英博 | 经典包 1.80.0 注册谋吕蒙双技能 | implemented-registry + classic extension | 横野为 `Locked + State`，`Game` 成长最多 3 并在击杀后通用重置；英博为 `State + Trigger`，读取 `Round` 同名伤害牌账本，首次不可响应并可交实体牌，重复使用改火伤且 +1；rules v101 与包 1.79.0 保留边界 |
| `standard:huichun` | 回春 | 出牌阶段每回合弃置两张手牌，令至少两名受伤角色各回复 1 点体力 | implemented-registry + extension | K5：私有两牌/多目标选择、逐目标 `RecoveryFrame`、`Processing` 弃牌、`RecoveryAppliedEvent` 和回合一次限制 |
| `standard:mashu` | 马术 | 计算与其他角色的距离 -1 | implemented-registry + extension | K6：`IPassiveSkill.ModifyOutgoingDistance`，由 Core 统一影响公开距离型合法性 |
| `standard:qicai` | 奇才 | 使用锦囊牌无距离限制 | implemented-registry + extension | K6：`IPassiveSkill.IgnoresTrickDistance`，由 Core 统一影响距离型锦囊合法性 |
| `standard:jijiu` | 急救 | 回合外在濒死窗口将红色非桃牌当作桃使用；rules v41 经典华佗含自己的装备区 | implemented-registry + rescue extension | K5：`IPassiveSkill.CanUseAsDyingRescue`，有效牌型为 Peach，物理牌型和 Hand/Equipment 实际来源保留在事件与移动账本 |

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
| `standard:crossbow` | 诸葛连弩 | Weapon | attack, unlimited-slash | implemented-registry | K6：武器槽；规则 v13 攻击范围 1、无限杀，v1–v12 保留旧 +1 范围 |
| `standard:qinggang_sword` | 青釭剑 | Weapon | attack, ignore-armor | implemented-registry | K6：武器槽、直接杀无视防具；`CardUseFrame.IgnoresArmor` 类型化记录 |
| `classic:stone-axe` | 贯石斧 | Weapon | attack, range-three, dodge-override, exact-two-card-cost | implemented-registry + classic 1.24 | rules v43：攻击范围 3；最后一张有效闪抵消杀后，从自己的手牌/装备区精确弃置两张牌（可含贯石斧自身），再沿同一 `AttackResolution` 造成伤害；rules v42 不发布 Choice |
| `classic:zhangba-serpent-spear` | 丈八蛇矛 | Weapon | attack, range-three, two-hand-cards-as-slash | implemented-registry + classic 1.25 | rules v44：攻击范围 3；自己的两张手牌以精确组合当作一张无固定花色的杀主动使用或响应；覆盖决斗、南蛮入侵、借刀杀人与激将，双实体牌共同进入处理区并结束；rules v43 不发布动作 |
| `classic:cixiong-double-swords` | 雌雄双股剑 | Weapon | attack, range-two, opposite-gender-resource-choice | implemented-registry + classic 1.26 | rules v45：攻击范围 2；对异性角色使用杀指定目标后，来源可发动，目标再从自己每张手牌的精确弃置 Choice 与令来源摸一张之间选择；rules v44 不发布窗口 |
| `classic:qinglong-crescent-blade` | 青龙偃月刀 | Weapon | attack, range-three, same-target-followup-slash | implemented-registry + classic 1.27 | rules v46：攻击范围 3；最后一张有效闪抵消杀后，来源可从当前精确杀候选或刘备的激将中选择同目标真实新杀，提供者可支付实体杀或丈八双牌，新杀不计普通次数并可再次触发；rules v45 不发布窗口 |
| `classic:ice-sword` | 寒冰剑 | Weapon | attack, range-two, damage-prevention, target-card-discard | implemented-registry + classic 1.28 | rules v47：攻击范围 2；杀伤害前可保留伤害，或防止全部伤害并依次弃置目标至多两张手牌/装备；手牌仅为不透明牌位，装备精确公开，有第二张时发动后必须继续；rules v46 不发布窗口 |
| `standard:bagua` | 八卦阵 | Armor | response, judgment | implemented-registry | K6：防具槽和公开生命周期；K7：`JudgmentFrame`、判定区移动和红色判定视为闪；规则 v14 覆盖直接杀与万箭齐发，v1–v13 保留直接杀响应 |
| `standard:renwang_shield` | 仁王盾 | Armor | defense, block-black-slash | implemented-registry | 规则 v14：黑色杀指定目标后的公开无效结算与 `ArmorEffectAppliedEvent`，青釭剑可绕过；v1–v13 保留合法目标过滤 |
| `standard:offensive_horse` | 赤兔 | OffensiveHorse | range, attack | implemented-registry | K6：进攻坐骑槽、战斗距离 -1 |
| `standard:defensive_horse` | 绝影 | DefensiveHorse | defense, distance | implemented-registry | K6：防御坐骑槽、战斗距离 +1 |
| `standard:jade_seal` | 玉玺 | Treasure | draw, modifier | implemented-registry | K6：宝物槽、摸牌 +1 |

装备内容必须进入 `Equipment` 区域，拥有替换、失效、死亡清理和持久 modifier 语义；当前 K6 基础切片已开放五类槽位、十二种装备牌（含贯石斧、丈八蛇矛、雌雄双股剑、青龙偃月刀、寒冰剑、仁王盾）、换装、战斗距离/攻击范围查询，以及上述武器与防具的类型化结算和阵亡清理；复杂失效/卸载效果仍待后续入口，不把装备伪装成普通手牌或一次性锦囊。

## 7. 内容级 AI 标签约定

AI 标签是内容包输入，不是隐藏身份推断：

- `attack` / `pressure` / `finish`：目标价值和击杀压力；
- `response` / `survival`：响应伤害和保留到后续回合；
- `recovery` / `dying-save`：回复体力或救援濒死；
- `draw` / `card-advantage` / `retain`：手牌收益和弃牌保留；
- `target-one` / `target-two` / `all-opponents`：目标数量约束；
- `distance` / `range` / `state-mark`：依赖通用查询或持久状态。

标签不能替代核心合法性校验，也不能让 AI 读取暗身份、暗牌、完整牌堆顺序或其他玩家的私有候选。
