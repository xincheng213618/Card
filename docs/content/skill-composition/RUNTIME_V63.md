# RUNTIME V63 —— 赵云家族批次：界赵云 / 神赵云 / 高达一号（规则 182/184 / 经典包 1.156.0）

本批为三武将串行交付：界赵云（龙胆/涯角，最低规则 182）、神赵云（绝境/龙魂/斩将，最低规则 184）、高达一号（同人四技能，最低规则 184）。新增公共能力五项，其余全部复用既有语汇。

## 新增公共能力

1. **数值表达式 `handLimitMinusHandCount`（值 14）**：draw 的 numberExpression 白名单新成员，语义 `max(0, 手牌上限 - 当前手牌数)`。绝境补牌触发器（cardsMoved perBatch）以此在任意时点补至固定上限。执行侧 `DrawProgramCards` 增对应分支。
2. **目标种类 `otherLivingWithQinggangSword`（值 25）**：存活、非拥有者、装备区含【青釭剑】。激活目标枚举与 `GetProgramTargetSeats` 两处同步。
3. **`selectAndMoveOwnedCard` 的 `cardKinds` 白名单**：公示支付牌按种类过滤；执行期若公示牌种类不再匹配，支付选择以"公布的支付牌种类已失效"拒绝。斩将取剑以 `cardKinds: [qinggangSword]` 复用该通路，零新增移动语义。
4. **fireSlash viewAs 单花色输入（仅出牌）**：在既有"一张实体杀、仅出牌"之外放行"零 inputKinds + 恰一 inputSuits、forPlay 且非 forResponse"。实体杀路径的冻结契约文案 `fireSlash viewAs currently requires one physical slash for play only` 原样保留（CardUseCompletedChecks 冻结断言），花色路径另有专属文案。
5. **replacement 摸牌的零额 `amount: 0`**：`drawPhaseMode: replacement` 触发器内的 draw 允许 `amount 0`（绝境"不摸牌"）。allowZero 沿 `ParseTrigger → ParseCompositionEffect → ProgramOperationCatalog.Parse → ProgramOperationNodeReader.AllowZeroDraw` 传递；其余场合维持冻结契约 `between 1 and 20`。`Amount` 校验器以 `allowZero` 参数输出真实上下界。

## 消费方

- 界赵云：龙胆四条 viewAs（闪→杀双向杀系输入、酒↔桃）；涯角双触发器（cardUseCommitted / cardResponseAccepted，`cardActionFromOwnerHand` + 非自己回合）→ 亮顶牌 → 类别相同 `giveBoundCard` 给任意存活角色 / 类别不同 `selectTarget(otherLivingWhoseAttackRangeIncludesOwner)` + `selectAndMoveOwnedCard` 令其自弃一张（新条件种类 `cardActionFromOwnerHand`（24）与 `boundCardCategoryMatchesCardAction`（29）沿用本会话早期已并入的值段）。
- 神赵云：绝境=handLimit Set 4 修饰符 + 摸牌阶段 replacement 零摸 + cardsMoved perBatch `handLimitMinusHandCount` 补牌；龙魂=四条单花色 viewAs，sourceZones 手牌+装备区；斩将=turnStartBeforeNormalFlow 可选触发，selectTarget(25) + selectAndMoveOwnedCard(cardKinds)。
- 高达一号：光束步枪=activation（usesPerTurn 1，`otherLivingSlashable`）discardSelected + damage；I力场=cardPolicies preventTrickDamage（清单同 classic:wuyan）；机动装甲=attackRange/slashLimit `add` 修饰符（两者此前无内容先例，引擎数值规则通路本就完整）；核心战机=selfDyingResponse + usageScope game/usageLimit 1，discardOwnedZoneCards → recoverTo(2, clampToMaxHp) → draw 2。

## 顺带修复与批次纪律

- 向 `CurrentGeneralIds` 增武将即移动种子扫描型 fixture 的选将池：本批把 gao-da-yi-hao 先入池后复验，发现 Luoyi/借刀×2/青龙/麒麟弓 五个种子 fixture 复败；移出至 `FanGeneralIds` 后全部恢复。同人/实验武将默认不入正式池。
- 两处既有冻结契约因本批改动被摆动（fireSlash 契约文案、draw amount 契约文案），已按冻结文案修复并复验——契约测试的报错文案属于冻结面，改文案即破坏契约。

## 验证口径

定向检查 `BoundaryZhaoYunChecks`（4项）、`ShenZhaoYunChecks`（4项）、`GaoDaYiHaoChecks`（5项）全部通过；测试内自建 fixture 模式与牌堆（物理牌列表），濒死救援按"濒死询问中的 program-trigger 选项"驱动（NiepanChecks 同构），出牌结算后一律驱动到 seat-0 PlayCard/DiscardCards 再断言。快照全量 Core 591/599，8 失败均外部归因（详见批次记录 [2026-09-29-zhao-yun-family-gaoda](../../benchmarks/2026-09-29-zhao-yun-family-gaoda.md)）。
