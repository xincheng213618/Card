# 技能程序 schema 55：移动续接、锦囊目标与伤害期限

本批为 rules165、`standard-classic-generals@1.140.0`，Checkpoint schema仍为3。新字段在schema54及更早版本拒绝，旧枚举数值和省略字段默认不变。朱治2015、界甘宁2014、界许褚2014首发的首次注册门槛固定为1.140.0，不绑定会变化的CurrentVersion。

## 装备移动后的公开范围结果

`selectAndMoveOwnedCard` 新增三个独立选项：

- `allowSameOwnerHandReturn`：仅允许公开装备/判定区回到同一持有人的手牌，要求cardOwnerRef与targetRef一致。手牌原地移动仍拒绝。
- `coverageResultBind`：只为无条件、不能skipIfNoCards的装备移动产生具名结果，包含主体、移动前和续接后的攻击范围覆盖人数。它不是攻击范围数字，也不按合法杀目标计数。`attackRangeCoverageDecreased`条件通过sourceBind读取，可组合进all/any/not；验证器检查生产顺序与名称冲突。
- `awaitMovementTriggers`：保留已支付父指令，将本次移动及后代CardsMoved批次归属到等待帧，局部结算后续接。省略时沿用原空栈延迟行为。等待帧、已支付指令、活跃窗口祖先与批次scope必须一致；不能通过全局清队列或放宽栈不变量实现。

覆盖计算共用当前距离/攻击范围规则查询，排除主体和死亡角色。移动子响应先完成，再记录后值；主体/技能持有人死亡或原技能实例失效时取消续接。朱治消费该结果条件摸牌；测试中的另一技能将装备弃置后根据同一结果回复，证明能力不绑定安国整图。失装同步装备钩子继续使用现有移动管线。

## 冻结指定目标与目标效果子集

`CardActionContext.DesignatedTargetSeats`表示用牌时冻结的真实指定目标，区别于复杂牌的结算参数。借刀保留旧TargetSeats中的持刀者和杀目标，但指定目标仅含持刀者。原始人数事实不随随后死亡、他人无效化改变；选择时再过滤当前存活且未无效的目标。

- 触发器`cardCategories`可过滤锦囊类别，与cardKinds同时声明时取交集；不限非延时锦囊。
- `cardUseDesignatedTargetCount`可作为触发事实及动态目标上限表达式。
- `selectTargets`的`currentCardUseTargets`来源要求当前用牌上下文及before-target窗口。
- `nullifySelectedCardEffects`要求已选择非空目标集，核对原用牌帧/ActionId/牌种/使用者，再将所选目标并入IneffectiveTargetSeats。保留原TargetSeats及未选目标的结算次序；不取消整张牌、不等价于无懈。
- `cardEffectIntervention` AI只看公开牌种、关系和体力，在是否发动与子集选择间共用同一收益函数。

奋威的游戏一次限额由内容图定义，节点本身不要求限定技或固定两步配方。普通锦囊在集智/无懈之前接入现有cardUseBeforeTargetEffects，不扩大旧cardUseTargetsFinalized订阅范围。新用牌入口必须显式提供真实指定目标集合。

## 牌集合类型过滤

`filterBoundCards`可声明`categories`、`equipmentSlots`、`cardKinds`，含义为三类谓词的并集；如另有suits，则与该并集取交集。至少要有一个非空谓词，拒绝空数组与重复值。带新类型谓词时不接受未经证明的effectiveSuitForRef混合。运行时核对冻结位置，保持顺序、可见性和空集合语义。

资源证明与运行时/AI共用类型匹配规则。只有实际使用类型谓词的图才建立CardKind×Suit原子；旧纯花色图保留四原子及4096分区上限，避免新能力反向拒绝旧合法图。补集消费仍需同根、不可重复移动、临时揭示牌全部清理。AI估计使用有界公开先验，不读取牌堆。

## 跨回合伤害修正

`grantTurnCardDamageModifier`增加正交字段：

| 字段 | 旧默认 | 新值 |
| --- | --- | --- |
| expires | currentTurnEnd | nextOwnerTurnStart |
| sourceScope | ownerUsed | damageSource |

nextOwnerTurnStart在下次该角色回合开始、TurnStarted事件之前过期；死亡只提前清此期限的修正，保留旧默认事件顺序。damageSource以实际伤害来源匹配，因此别人发起决斗而持有效果者赢得应答时也适用；连环传播不重复加成。授予的幂等键同时校验期限和来源范围。

许褚内容先选择放弃摸牌，再亮三张并获得基本牌/武器/决斗，余牌弃置，零命中仍授予修正。真实反向决斗、下一自己回合开始到期及Replay已验证；翻面跳过/额外回合仅覆盖源码与公共存储边界，未宣称独立命令验收。

整批时间、失败修复及验证范围见[第四批实测](../../benchmarks/2026-09-26-movement-and-target-generals.md)。
