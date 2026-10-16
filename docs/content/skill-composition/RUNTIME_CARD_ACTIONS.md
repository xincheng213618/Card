# 牌身份、转换与实际用牌

牌动作使用同一原生合法性、费用、响应、效果和完成链。相关配置入口见[运行时](RUNTIME.md)。

## 可选转换与持续身份

`viewAs` 声明输入区域、实体数量、牌型/花色过滤、输出牌型以及使用/响应资格。选定后冻结 SkillId、转换绑定、技能实例、实际材料和有效牌型；移动保留物理身份。是否按“使用”或“打出”进入原生链，由具体请求决定，不能只看最终牌名。

`cardIdentities` 表达持续有效牌名，匹配时不保留原牌名的普通入口。`zones`、`inputKinds`、`inputSuits` 和 `outputKind` 显式配置；牌种与花色筛选同时存在时取交集。动作接受后即使实体离开手牌进入 `Processing`，该动作仍保留声明时冻结的身份。

[经典武神](../../../src/Characters/guan-yu/classic/Skills/classic-shen-guan-yu.rules.json)以红桃手牌作为普通杀，并用 `sourceCardIdentityId` 绑定对应距离修正。距离无限不会自动改变杀次数、目标数、可响应性、防具或伤害属性。牌名规则与数值策略分别声明。

转换后的触发以精确 `conversionChain`/绑定来源判断，不能仅凭有效牌型猜测发动了哪个技能。参考 [SP 赵云龙胆/冲阵](../../../src/Characters/zhao-yun/sp/Skills/sp-zhao-yun.rules.json)。

## 原生动作与零实体牌

普通实体、多实体转换、被请求用牌、供牌以及明确声明的零实体转换都必须保留真实使用者、提供者、原请求者和父流程。`CardActionAcceptedEvent` 及拥有帧的动作收据提供因果身份；一次移动或裸 `CardUsed` 通知不证明发生了一次原生使用。

零实体能力须由对应可选策略和真实生产入口明确授权，材料集合为空，不制造牌 ID 或 `Processing` 移动。它仍遵守普通次数、距离、目标、防具、响应、无懈和完成返回。无懈的特殊反制父链必须使用其专门返回合同。

## 目标与角色

目标选择支持单人、多人及有序组合。`livingPairDistinct` 为两名互异的存活角色，顺序保留；`selectedFirst`/`selectedSecond` 在能力允许时分别引用来源与去向。借刀杀人使用持武器者与其攻击目标的完整有序对，不能拆成彼此独立的目标。

`selectTargets` 配合 `minimumTargets`、`maximumTargets` 及允许的 `numberExpression` 冻结选择范围；执行时仍处理死亡和失效。选中的集合不是任意后续动作都可消费的目标集合，每种操作有自己的校验。

用牌目标修改必须绑定当前动作和原拥有帧。`addCurrentCardUseTarget` 可在受支持的唯一杀目标窗口由该目标拥有者驱动；`replaceCurrentCardUseActor` 仍要求当前使用者身份。不能据此任意篡改牌的来源或增加不合法目标。

`selfOnly` 目标限制可显式绑定 `SubjectSeat`；未指定时作用于授予者。它与 `TargetSeat` 的指定目标策略分开，避免把“使他只能指定自己”错误授予给技能拥有者。

## 跨拥有者贡献

`contributions[]` 表达“拥有者持有技能，符合资格的其他出牌角色交一张自己的实体手牌给拥有者”。

| 字段 | 合同 |
| --- | --- |
| `id` | 与同一技能的主动绑定共用身份命名空间 |
| `providerFactions` | 非空且不重复的实际提供者势力 |
| `ownerRole` | 拥有者资格，例如 `lord`；本人不能向自己贡献 |
| `cardKinds`、`cardSuits` | 对提供者实体手牌取并集匹配；两数组不能同时为空 |
| `usesPerPlayPhase` | 按提供者、拥有者、SkillId 和绑定记录实际出牌阶段额度 |

命令携带独立技能拥有者座位。提交时复验人物、势力、技能实例、实体来源与次数，再以真实移动进入接收者手牌。黄天的不同规则版本分别配置牌过滤，见[张角规则](../generals/ZHANG_JIAO_JUDGMENT_MIGRATION.md)。贡献能力不替代护驾、激将等响应供牌链。

## 观察与收益

`cardUseTargetsFinalized`、`cardResponseAccepted` 等窗口消费规范化动作与对抗主体。冲阵的“对方”来自原生请求上下文，供牌者、伤害来源和回合角色都不能直接替代它。

牌材料申领仅取得该动作实际使用且仍位于允许来源的实体，并记录原动作收据。被申领后原牌继续结算，完成时不会再次弃置已经离开的材料。当前目标的不可抵消/无效化也各有范围；禁止闪抵消不等于无视全部防具。
