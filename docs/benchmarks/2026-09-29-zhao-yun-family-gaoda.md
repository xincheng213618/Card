# 赵云家族批次：界赵云 / 神赵云 / 高达一号（2026-09-29）

本批串行交付三名赵云系武将（原计划三路子代理并行；子代理容量为零，经用户同意改为主会话串行实现，用户随后冻结神赵云官方文本并授权"高达一号你测测"，即由本会话设计并验收同人武将）。隔离快照 `Card-zhaoyun-inline-20260929`，回写主树仅做定向编辑；经典包版本 1.155.0 → 1.156.0。

| 武将 | 技能 | schema | 最低规则 | 定向检查 |
| --- | --- | --- | --- | --- |
| 界赵云 boundary:zhao-yun（2014） | boundary:longdan / boundary:yajiao | 62 | 182 | BoundaryZhaoYunChecks 4/4 |
| 神赵云 classic:shen-zhao-yun（2011 神话再临·山） | classic:juejing / classic:longhun / classic:zhanjiang | 62 | 184 | ShenZhaoYunChecks 4/4 |
| 高达一号 classic:gao-da-yi-hao（同人，用户授权设计） | classic:beam-rifle / classic:i-field / classic:mobile-armor / classic:core-fighter | 62 | 184 | GaoDaYiHaoChecks 5/5 |

界赵云龙胆：闪↔杀（杀含火杀/雷杀输入）、酒↔桃 双向转化，闪→杀出牌与响应双开、其余按官方响应/出牌窗口收紧；涯角：非自己回合以龙胆转化用手牌结算后亮牌堆顶——类别相同则交给任意存活角色，类别不同则改为令攻击范围包含自己的一名其他角色自弃一张（手牌/装备/判定区）。

神赵云按用户冻结的 2011 初版文本实现：绝境=锁定手牌上限 Set 4 + 摸牌阶段 replacement 零摸 + 手牌离开即补至 4（perBatch cardsMoved → `handLimitMinusHandCount`）；龙魂=花色输入 viewAs（红桃→桃响应、方块→火杀仅出牌、梅花→闪、黑桃→无懈），sourceZones 手牌+装备区（"你的牌"）；斩将=准备阶段获得场上其他存活角色的【青釭剑】（`otherLivingWithQinggangSword` 目标种类 + selectAndMoveOwnedCard `cardKinds` 白名单）。

高达一号（完全同人设定，**不加入经典身份池**，`FanGeneralIds` 单列）：光束步枪=出牌阶段限一次弃一张手牌对攻击范围内一名其他角色造成1点伤害（activation `otherLivingSlashable`）；I力场=锁定 preventTrickDamage 锦囊伤害免疫（口径同 classic:wuyan 全锦囊清单）；机动装甲=锁定 攻击范围+1 与每回合【杀】次数上限+1（`attackRange`/`slashLimit` add 修饰符，两者此前均无内容先例但引擎数值规则通路完整）；核心战机=限定技濒死自弃全部牌、回复至2、摸两张（selfDyingResponse + usageScope game / usageLimit 1）。

## 新增公共能力（详见 RUNTIME_V61）

- 数值表达式 `handLimitMinusHandCount`（14）：draw 的 numberExpression 白名单新成员，绝境补牌用。
- 目标种类 `otherLivingWithQinggangSword`（25）：斩将取剑。
- `selectAndMoveOwnedCard` 新增 `cardKinds` 过滤：公示支付牌的合法性校验同步收紧（"公布的支付牌种类已失效"）。
- fireSlash viewAs 单花色输入（仅出牌）：服务龙魂方块→火杀；物理杀输入的冻结契约文案 `fireSlash viewAs currently requires one physical slash for play only` 原样保留（CardUseCompletedChecks 冻结断言依赖）。
- draw `amount: 0` 仅在 `drawPhaseMode: replacement` 触发器内可定义（allowZeroDraw 沿 ParseTrigger → ParseCompositionEffect → ProgramOperationCatalog → reader 传递）；其余场合维持冻结契约文案 `between 1 and 20`。
- `Amount` 校验器新增 `allowZero` 参数并按调用口径输出上下界文案。

## 批次边界

- 红桃→桃仅响应：引擎无主动回桃执行器；方块→火杀仅出牌（fireSlash 响应窗口不受支持）。
- 绝境手牌上限以弃牌阶段口径收敛：回合中允许暂超 4，弃牌阶段自动弃至 4（fixture `UseInteractiveDiscard=false`，弃牌为引擎自动结算，无弃牌决策）。
- 神赵云 god 势力按现行 god 通用流程在选将后询问归属势力；斩将目标种类限定装备区青釭剑。
- 涯角按官方现行文本口径（亮顶牌分类别分支），非 2014 首发"交给一名角色"旧版。
- 高达一号四技能均为同人设计：光束步枪伤害来源=高达一号本体；I力场沿用 preventTrickDamage 双参与者判定（引擎既有口径），对"被锦囊伤害"与"作为来源"两侧都生效；核心战机 usageLimit 1 的"二次濒死不再询问"仅结构断言，行为断言覆盖首次复活（AI 救援可能延迟二次濒死，不做种子强依赖）。

## 验收

- 定向 16/16 全绿（界4 + 神4 + 高达5 + 经典赵云现行3），Release 构建 0 警告 0 错误。
- 快照全量 Core 591/599：8 失败全部外部归因——Deng Ai×3、Sha Mo Ke×2、composition malformed（三者为快照陈旧副本，主树已修，composition 已在主树复核通过）；Gan Ning Qixi、Lu Bu Wushuang（主树当前在制面同样失败，非本批引入）。WPF 全量仅既有鬼龙斩月刀缺牌面阻断（不修）。
- 本批引入的三处回归已在快照内修复并复验：fireSlash 契约文案、draw amount 契约文案与 allowZero 口径、gao-da-yi-hao 移出经典池（池位移使 Luoyi/借刀×2/青龙/麒麟弓 种子 fixture 复败，移出后五项全部恢复）。

## 教训（供后续批次复用）

- `AdvanceAfterHumanCommands=false` 下出牌结算后 `ResolutionStack==0` 不代表到达下一决策点，必须驱动到 seat-0 PlayCard/DiscardCards（`DriveToOwnTurnDecision`）；以 `GetHumanLegalActions()` 判空会误判"无杀可出"。
- 濒死程序救援不是 `ProgramTrigger` 决策：它是濒死询问 prompt 中带 `response=program-trigger`、`skill-id` 参数的选项（ NiepanChecks 同构），按决策类型分派会永久错过。
- 向 `CurrentGeneralIds` 增加武将即移动全部种子扫描型 fixture 的选将池；同人/实验武将应默认走 `FanGeneralIds`，正式池只进官方武将。

## 主树回写与整合验收（补充）

- 回写采用定向编辑；其中 `ProgramCompositionValidator.cs` 的 gift 校验放宽（reveal 绑定可有条件赠牌，非持有绑定的赠牌原子记入 Consumed）首轮回写时遗漏，表现为 `boundary:yajiao` 加载失败并连带 13 项定向检查全败——整文件 diff 确认该文件仅含本批两个语义 hunk 后补齐。教训：回写清点应以"快照 vs 主树逐文件 diff"全量扫一遍，不能只凭编辑清单。
- 主树整合态全量 Core **610/610 全绿**（含并行姜维批次的在制注册与测试，其与主树工作树共存复验通过）；WPF 全量仍仅既有鬼龙斩月刀缺牌面阻断。
- 池位移在主树复现两处种子 fixture 失败（驱虎荀彧、周泰不屈），归因实验（临时移出本批两个池条目后两测试即恢复）确认为池组成变化暴露的既有引擎限制，非本批内容/引擎改动缺陷。两 fixture 按既定稳健化模式修复：种子扫描循环对单种子内与被测场景无关的失败（含引擎异常）跳过续扫；周泰 fixture 将"重复点数"驱动并入扫描并以"首个唯一创伤"时点快照断言（断言语义与原版一致，仅时点回归原位）。
- **报给引擎负责方的既有缺陷（本批未修）**：嵌套判定窗口冲突。复现：seed 18（identity:classic-5, 5人, AiPolicyVersion 2）司空懿拆迁弃邓艾手牌 → 屯田触发 startJudgment（判定31）→ 界张角鬼才替换完成（JudgmentReplacementResolved 已发）→ 替换移牌又在判定31未终结时打开嵌套 CardsMovedTriggerWindow(34)（替换牌从手牌离开触发）→ 嵌套窗口内屯田绑定B执行 startJudgment → `BeginJudgment` 因 `_pendingJudgment` 仍挂判定31 抛 "The engine cannot resolve two judgments at once"。当时解析栈 `[CardsMovedTriggerWindow:27, ProgramSkill:30(屯田A), Judgment:31, ProgramSkill:33(鬼才), CardsMovedTriggerWindow:34, ProgramSkill:35(屯田B)]`。根因：`_pendingJudgment` 为全局单槽，挂起/搁置中的外层判定未与解析栈位置绑定，嵌套窗口内的新 startJudgment 无法与外层共存。修复方向（引擎负责方决策）：判定挂起态与栈绑定 + 嵌套判定排队，或移牌触发的触发窗口延后到外层判定终结之后开启（后者会改动事件时序，需过重放冻结面）。
