# RUNTIME V60 —— 孙策"激昂 / 魂姿"（规则 181 / 经典包 1.151.0）

规则版本 180 → 181：新增触发条件种类 `cardActionCardIsRed`（枚举值 21）与对应触发事实字段。本批其余能力全部复用既有语汇；魂姿授予的英姿/英魂为现行已注册内容技能的直接复用（见下）。本批另修复一处被注册表池变化暴露的既有引擎不变量缺陷与一处种子脆弱的共享 fixture（见"顺带修复"）。

## 新增公共能力

新增触发条件 `cardActionCardIsRed`（事实 `CardActionCardIsRed`，值 21）：

- 语义：当前用牌动作的实体牌颜色为红（实体牌花色 ∈ {Heart, Diamond}；多张实体牌时要求全部为红，与 AI 公共上下文 `CardUseIsRed` 与伤害事实 `DamageCardIsRed` 的既有口径一致；无实体牌的虚拟用牌视为不成立）。
- 事实捕获：`CaptureProgramTriggerFacts(owner, action)` 统一填充，位置在 `CardActionCategory` 旁；按 `CardActionCost.CardId` 经 `GetLocation`/`CardsAt` 解析实体牌花色。
- 解析器约束：仅允许声明在用牌类窗口（`isCardActionWindow`：cardUseCommitted / cardUseBeforeTargetEffects / cardUseTargetsFinalized / cardResponseAccepted / cardUseCompleted / slashTargetRedirecting / slashBeforeResponse / slashFullyDodged），其余窗口拒收，报错文案 `cardActionCardIsRed requires a card-action trigger`。
- 激昂使用 `cardUseBeforeTargetEffects`（指定目标后、结算目标效果前）承载"指定目标后"的官方时点；该窗口的 cardKinds 过滤支持杀系与锦囊牌类（决斗属锦囊牌），而 `cardUseTargetsFinalized` 只支持杀系——这是决斗分支选择前者的直接原因。

## 顺带修复：伤害游标不变量对骑跨嵌套帧的豁免

新增武将进入 `CurrentGeneralIds` 后，共享注册表测试（ClassicGeneralChecks 等）的种子选将池随之变化，暴露了两处既有问题（HEAD 基线可复现归因）：

1. **`AssertCoreInvariants` 两处伤害续接游标断言不认骑跨嵌套帧**。合法状态 `[CardUseFrame, DamageFrame, DamageTriggerWindowFrame, ProgramSkillFrame(afterDamageApplied), CardsMovedTriggerWindowFrame]`——御策（classic:yuce）的 afterDamageApplied 效果移牌时，神司马懿忍戒/陆逊连营的 cardsMoved 触发器窗口按设计骑跨在带 `PendingMovementContinuation` 的技能帧上（移动窗口本就声明"在下一个安全规则边界处理"），但不变量只认栈顶为其直接子帧，误报游标丢失。修复：新增 `DamageCursorEffectiveTop()`（`DamageFrameRidesOn` 校验 `Batch.AwaitingProgramFrameId/ParentFrameId` 与 `HpChangedTriggerWindowFrame.ResumeFrameId` 的真实链接后向下走查），两处断言（"retain its ordered cursor as the stack top"与"retain its window or skill frame at the stack top"）都改用有效栈顶；链接不成立的真正错序仍会抛错。
2. **`ConfiguredKujinDyingContinuation` 的 fixture 对起手牌含桃的隐性种子依赖**。致死苦肉后救援询问只有在响应者有桃/酒/程序转化时才同步暂停；池变化移动了 fixture 命中的种子，新手牌 14 张无桃导致 `pending=none`。修复：`SelectGeneral` 增加可选 `fixtureFilter`（发牌后评估），该测试要求起手有桃，其余调用不受影响。

## 消费方：孙策"激昂 / 魂姿"

- 激昂（可选，四触发器同构）：`cardUseBeforeTargetEffects` + `ownerRelation: actor/target` × `cardKinds: [duel] / [slash, fireSlash]`；两个杀分支携带 `cardActionCardIsRed` 条件；效果均为 `draw(owner, 1)`。
- 魂姿（觉醒，非可选，usageScope game / usageLimit 1）：`turnStartBeforeNormalFlow` + 条件 `compare(currentHp, equal, 1)`，效果 `changeMaximumHp(-1)` → `grantSkills([classic:yingzi, classic:yinghun])`——与神司马懿授权连破同构。
- 授予复用：`classic:yingzi`（draw-phase-skills，摸牌阶段额外摸一张，revision 2）与 `classic:yinghun`（owned-card-exchange-skills，准备阶段开始时已受伤可选摸X弃1 / 摸1弃X，revision 1）为现行注册内容技能（周瑜/孙坚在用），语义与孙策官方授予版一致，本批零改动复用。

## 验证口径

定向检查 `tests/CardGame.Core.Tests/SunCeChecks.cs`（4项，自然命令与真实决策应答）：

1. 定义与触发schema：注册表存在 classic:sun-ce（吴4体力、双技能、身份池）、激昂四触发器窗口/归属/卡种/红色条件断言、魂姿觉醒属性断言；schema拒收样例（cardActionCardIsRed 声明在 afterDamageApplied）。
2. 使用决斗摸牌并回放：出决斗 → 激昂提示 → Checkpoint 还原后事件与状态全等 → 激活后恰一张按技能原因入手。
3. 被指定目标摸牌且黑杀不摸：主公结束出牌后等 AI 决斗/红杀指定主公 → 激活后恰一张入手；使用黑杀后断言无激昂绑定事件。
4. 魂姿觉醒并授予：体力1回合开始自动觉醒（主公上限5→4）→ SkillAwakenedEvent 记录 maximumHp 4 且授予 [yingzi, yinghun] → 回放全等 → 下一回合英魂提示可见 → 英姿提示激活后恰一张按原因入手。

验证结果：定向4/4通过（详见批次记录）。

## 边界与未覆盖

- 主公技"制霸"（其他吴势力角色出牌阶段限一次与其拼点，其没赢可获得拼点牌）未实现：需要拼点公共能力与"其他角色决策+主公技"族语义，与颂威同因（颂威边界见 RUNTIME_V59）。
- 体力值口径：官方详情页不渲染体力，4勾玉取自两处独立二手资料（一致），批次记录如实标注。
- 英魂选择顺序沿用孙坚版（先选角色、后执行分支），与孙策官方文本"选择一项：令一名其他角色……"的表述顺序不同但结算等价。
